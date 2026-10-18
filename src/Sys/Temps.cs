using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Orbit;

// The PC's temperatures (the watch's Temperatures widget, WristTemps.cs; the GPU / CPU hot alert, Sys/Alerts.cs), read on a thread of
// their own every 2 s - only while a temperatures widget is on a page (and the watch can be seen) or the alert is on. Never a made-up
// number: what cannot be read is NaN, drawn as "–", and the report says why.
// - the GPU: AMD's ADL (atiadlxx.dll, the driver's own; no administrator needed) - the edge temperature, the hotspot (junction) and the
//   fan's rpm from the card's PM log (Overdrive 8: the RX 5000 series and later, Erik's RX 9060 XT). Not an AMD card (or no ADL):
//   Windows' own numbers (D3DKMT's adapter performance data, WDDM 2.4: what Task Manager shows - the temperature and the fan, no hotspot).
// - the CPU: an AMD Ryzen (family 17h / 19h / 1Ah) - its Tctl / Tdie register (SMN 0x59800, as Linux's k10temp reads it) through
//   PawnIO, the signed driver LibreHardwareMonitor uses (Hexpad reads its CPU temperature the same way), with PawnIO's own AMDFamily17
//   module (third_party/pawnio), under "Global\Access_PCI" - the mutex every hardware monitor takes around the PCI bus. PawnIO opens
//   for administrators only: Orbit normally runs elevated (its 'Orbit (admin)' task). Not installed, not elevated, another CPU: "–".
//   Nothing is installed: PawnIO is used only when it is already there.
public sealed class TempSensors : IDisposable
{
    public const double Every = 2;                       // s between two samples

    readonly AutoResetEvent _wake = new(false);
    Thread _thread;
    volatile bool _stop, _active;
    TempReport _pub;

    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            _active = value;
            if (value && _thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "temperatures", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
            _wake.Set();
        }
    }

    // the newest reading (null: none yet, or switched off)
    public TempReport Report => Volatile.Read(ref _pub);

    void Run()
    {
        while (!_stop)
        {
            if (!_active)
            {
                // off: the history starts over next time (a gap would draw as if it had been read)
                _count = 0;
                Volatile.Write(ref _pub, null);
                _wake.WaitOne();
                continue;
            }
            double t0 = Clock.Now;
            try { Sample(); }
            catch (Exception e) { Log.Info("temps: " + e.Message); }
            _wake.WaitOne(TimeSpan.FromSeconds(Math.Clamp(Every - (Clock.Now - t0), 0.05, Every)));
        }
        Close();
    }

    // ---- the sources ----

    AdlGpu _adl;
    KmtGpu _kmt;
    PawnCpu _cpu;
    bool _opened;
    double _cpuRetry;
    string _cpuName, _gpuName, _cpuWhy, _gpuWhy, _gpuFrom;

    void Open()
    {
        _opened = true;
        _cpuName = CpuName();
        _adl = AdlGpu.Open();
        if (_adl != null) { _gpuName = _adl.Name; _gpuFrom = "AMD ADL"; }
        else
        {
            _kmt = KmtGpu.Open();
            _gpuFrom = _kmt != null ? "Windows" : null;
            _gpuWhy = _kmt != null ? null : "Not readable";
        }
        OpenCpu();
        Log.Info($"temps: GPU {(_adl != null ? $"{_gpuName} through ADL (edge, hotspot, fan)" : _kmt != null ? "through Windows' D3DKMT (temperature, fan)" : "not readable")}; " +
                 $"CPU {_cpuName ?? "?"} {(_cpu != null ? "through PawnIO" : "not read: " + _cpuWhy)}");
    }

    void OpenCpu()
    {
        _cpu = PawnCpu.Open(_cpuName, out _cpuWhy);
        _cpuRetry = Clock.Now + 60;
    }

    void Close()
    {
        _adl?.Dispose(); _adl = null;
        _kmt?.Dispose(); _kmt = null;
        _cpu?.Dispose(); _cpu = null;
        _opened = false;
    }

    // ---- the history: shared rings, a report reads the samples it was published with (never one written after it) ----

    const int Ring = TempReport.History + 30;
    readonly float[] _rCpu = new float[Ring], _rGpu = new float[Ring], _rHot = new float[Ring];
    int _head, _count, _seq;

    // one sample, published (the tool's probe calls it directly)
    public TempReport Sample()
    {
        if (!_opened) Open();
        float gpu = float.NaN, hot = float.NaN, fan = float.NaN, cpu = float.NaN;
        if (_adl != null) (gpu, hot, fan) = _adl.Read();
        else if (_kmt != null) (gpu, fan) = _kmt.Read();
        if (_cpu == null && _cpuWhy is "Needs PawnIO" && Clock.Now >= _cpuRetry) OpenCpu();      // installed since: tried again every minute
        if (_cpu != null) cpu = _cpu.Read();
        _rCpu[_head] = cpu;
        _rGpu[_head] = gpu;
        _rHot[_head] = hot;
        _head = (_head + 1) % Ring;
        _count = Math.Min(_count + 1, TempReport.History);
        var r = new TempReport
        {
            Cpu = cpu, Gpu = gpu, Hot = hot, Fan = fan, At = Clock.Now, Seq = ++_seq,
            CpuName = _cpuName, GpuName = _gpuName, CpuFrom = _cpu != null ? "PawnIO" : null, GpuFrom = _gpuFrom,
            CpuWhy = _cpu != null ? null : _cpuWhy, GpuWhy = _gpuWhy,
            RCpu = _rCpu, RGpu = _rGpu, RHot = _rHot, Head = _head, Count = _count, Size = Ring,
        };
        Volatile.Write(ref _pub, r);
        return r;
    }

    // the CPU's name as Windows has it ("AMD Ryzen 7 5700X3D 8-Core Processor": the part before "-Core" kept)
    static string CpuName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return Tidy(k?.GetValue("ProcessorNameString") as string);
        }
        catch { return null; }
    }

    public static string Tidy(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        int core = name.IndexOf("-Core", StringComparison.OrdinalIgnoreCase);
        if (core > 0)
        {
            int sp = name.LastIndexOf(' ', core);
            if (sp > 0) name = name[..sp];
        }
        return name.Replace("(R)", "").Replace("(TM)", "").Replace("  ", " ").Trim();
    }

    // --tempstest: the real sources read twice, 2 s apart (read only: nothing is written, nothing installed); the numbers in the log
    public static int Probe()
    {
        using var t = new TempSensors();
        t.Sample();
        Thread.Sleep(2000);
        var r = t.Sample();
        static string N(float v, string unit) => float.IsNaN(v) ? "–" : $"{v:0.0} {unit}";
        Log.Info($"tempstest: elevated {Elevation.IsElevated}");
        Log.Info($"tempstest: CPU {N(r.Cpu, "°C")} ({r.CpuName ?? "?"}, {r.CpuFrom ?? "not read: " + r.CpuWhy})");
        Log.Info($"tempstest: GPU {N(r.Gpu, "°C")}, hotspot {N(r.Hot, "°C")}, fan {N(r.Fan, "rpm")} ({r.GpuName ?? "?"}, {r.GpuFrom ?? "not read: " + r.GpuWhy})");
        using (var k = KmtGpu.Open())
        {
            var (kt, kf) = k?.Read() ?? (float.NaN, float.NaN);
            Log.Info($"tempstest: Windows' own (D3DKMT, as Task Manager) GPU {N(kt, "°C")}, fan {N(kf, "rpm")}");
        }
        return float.IsNaN(r.Gpu) && float.IsNaN(r.Cpu) ? 1 : 0;
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        if (_thread == null) Close();
        else _thread.Join(2000);
        // (_wake is left to the GC, as AvatarLink's and LumosLink's are: a step still running past the Join - a sensor being read - would
        // come back to a disposed handle in its WaitOne and throw on a thread with no handler: the process gone mid-exit)
    }
}

// One sample of the temperatures, never changed once published. NaN: not read (drawn "–").
public sealed class TempReport
{
    public const int History = 300;                      // 10 minutes, a sample every 2 s

    public float Cpu = float.NaN, Gpu = float.NaN, Hot = float.NaN, Fan = float.NaN;      // °C, °C, °C (the GPU's hotspot), rpm
    public double At;                                    // Clock.Now when it was read
    public int Seq;                                      // the sample's number: another one, other texts
    public string CpuName, GpuName;                      // "AMD Ryzen 7 5700X3D", "AMD Radeon RX 9060 XT" (null: not known)
    public string CpuFrom, GpuFrom;                      // where it was read ("PawnIO", "AMD ADL", "Windows"); null: not read
    public string CpuWhy, GpuWhy;                        // why not ("Needs administrator", "Needs PawnIO", "AMD Ryzen only", "Not readable")

    // the samples before it, oldest first: the rings (shared with the next reports) and where this one's end
    public float[] RCpu, RGpu, RHot;
    public int Head, Count, Size;

    int At0(int i) => (Head - Count + i + 2 * Size) % Size;
    public float CpuAgo(int i) => RCpu[At0(i)];
    public float GpuAgo(int i) => RGpu[At0(i)];
    public float HotAgo(int i) => RHot[At0(i)];

    // the highest of the CPU's (cpu) or the GPU's history (NaN: none read)
    public float Peak(bool cpu)
    {
        float m = float.NaN;
        for (int i = 0; i < Count; i++)
        {
            float v = cpu ? CpuAgo(i) : GpuAgo(i);
            if (!float.IsNaN(v) && !(v <= m)) m = v;
        }
        return m;
    }

    // a made-up report (the tools: never the real sensors) - its numbers now and 'count' samples before (at(i): i = 0 the oldest)
    public static TempReport Fake(float cpu, float gpu, float hot, float fan, int count, Func<int, (float cpu, float gpu, float hot)> at,
        string cpuWhy = null, string gpuWhy = null)
    {
        count = Math.Clamp(count, 0, History);
        var r = new TempReport
        {
            Cpu = cpu, Gpu = gpu, Hot = hot, Fan = fan, At = Clock.Now, Seq = 1, CpuName = "AMD Ryzen 9 9900X", GpuName = "Radeon RX Made-Up",
            CpuFrom = float.IsNaN(cpu) ? null : "PawnIO", GpuFrom = float.IsNaN(gpu) ? null : "AMD ADL", CpuWhy = float.IsNaN(cpu) ? cpuWhy ?? "Needs administrator" : null,
            GpuWhy = float.IsNaN(gpu) ? gpuWhy ?? "Not readable" : null,
            RCpu = new float[History], RGpu = new float[History], RHot = new float[History], Head = 0, Count = count, Size = History,
        };
        for (int i = 0; i < count; i++)
        {
            var (c, g, h) = at(i);
            int k = (History - count + i) % History;
            (r.RCpu[k], r.RGpu[k], r.RHot[k]) = (c, g, h);
        }
        return r;
    }
}

// AMD's ADL (atiadlxx.dll, installed with the driver): the PM log of an Overdrive 8 card - sensor 8 the edge (28 the GFX, where a card
// has no edge sensor), 27 the hotspot, 14 the fan's rpm (0 = stopped: a quiet card at rest). The log is copied into a buffer kept
// for it: a sample allocates nothing.
sealed class AdlGpu : IDisposable
{
    const int MaxSensors = 256, InfoSize = 1572, Edge = 8, Gfx = 28, Hotspot = 27, FanRpm = 14, Amd = 1002;
    const string Dll = "atiadlxx.dll";

    delegate IntPtr AllocFn(int size);
    static readonly AllocFn Alloc = Marshal.AllocHGlobal;      // ADL keeps it: never collected

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int ADL2_Main_Control_Create(AllocFn cb, int connected, out IntPtr ctx);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int ADL2_Main_Control_Destroy(IntPtr ctx);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr ctx, out int n);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr ctx, IntPtr info, int size);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int ADL2_Adapter_Active_Get(IntPtr ctx, int idx, out int active);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int ADL2_Overdrive_Caps(IntPtr ctx, int idx, out int supported, out int enabled, out int version);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int ADL2_New_QueryPMLogData_Get(IntPtr ctx, int idx, IntPtr output);

    IntPtr _ctx, _buf;
    int _idx;
    readonly int[] _words = new int[1 + 2 * MaxSensors];
    public string Name;

    // the first active AMD card with a PM log that has a temperature (null: none, or no ADL - not an AMD driver)
    public static AdlGpu Open()
    {
        IntPtr ctx = IntPtr.Zero, info = IntPtr.Zero;
        var g = new AdlGpu();
        try
        {
            if (ADL2_Main_Control_Create(Alloc, 1, out ctx) != 0 || ctx == IntPtr.Zero) return null;
            if (ADL2_Adapter_NumberOfAdapters_Get(ctx, out int n) != 0 || n <= 0) { ADL2_Main_Control_Destroy(ctx); return null; }
            info = Marshal.AllocHGlobal(InfoSize * n);
            if (ADL2_Adapter_AdapterInfo_Get(ctx, info, InfoSize * n) != 0) { ADL2_Main_Control_Destroy(ctx); return null; }
            g._ctx = ctx;
            g._buf = Marshal.AllocHGlobal(4 * g._words.Length);
            int lastBus = -1;
            for (int i = 0; i < n; i++)
            {
                IntPtr a = info + i * InfoSize;
                int idx = Marshal.ReadInt32(a, 4), bus = Marshal.ReadInt32(a, 8 + 256), vendor = Marshal.ReadInt32(a, 8 + 256 + 12);
                if (vendor != Amd || bus == lastBus) continue;              // (a card is listed once per output)
                lastBus = bus;
                if (ADL2_Adapter_Active_Get(ctx, idx, out int active) != 0 || active == 0) continue;
                if (ADL2_Overdrive_Caps(ctx, idx, out int sup, out _, out int ver) != 0 || sup == 0 || ver < 8) continue;
                g._idx = idx;
                if (float.IsNaN(g.Read().edge)) continue;
                g.Name = TempSensors.Tidy(Marshal.PtrToStringAnsi(a + 8 + 256 + 16));
                return g;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
        catch (Exception e) { Log.Info("temps: ADL " + e.Message); }
        finally { if (info != IntPtr.Zero) Marshal.FreeHGlobal(info); }
        g.Dispose();
        return null;
    }

    public (float edge, float hot, float fan) Read()
    {
        if (_ctx == IntPtr.Zero || ADL2_New_QueryPMLogData_Get(_ctx, _idx, _buf) != 0) return (float.NaN, float.NaN, float.NaN);
        Marshal.Copy(_buf, _words, 0, _words.Length);
        return Parse(_words);
    }

    // the log: its size, then each sensor's (supported, value)
    public static (float edge, float hot, float fan) Parse(int[] w)
    {
        float edge = Sensor(w, Edge);
        if (!(edge > 0 && edge < 150)) edge = Sensor(w, Gfx);
        if (!(edge > 0 && edge < 150)) edge = float.NaN;
        float hot = Sensor(w, Hotspot);
        if (!(hot > 0 && hot < 150)) hot = float.NaN;
        float fan = Sensor(w, FanRpm);
        if (!(fan >= 0 && fan < 20000)) fan = float.NaN;
        return (edge, hot, fan);
    }

    static float Sensor(int[] w, int i) => 2 + 2 * i < w.Length && w[1 + 2 * i] != 0 ? w[2 + 2 * i] : float.NaN;

    public void Dispose()
    {
        if (_ctx != IntPtr.Zero) { try { ADL2_Main_Control_Destroy(_ctx); } catch { } _ctx = IntPtr.Zero; }
        if (_buf != IntPtr.Zero) { Marshal.FreeHGlobal(_buf); _buf = IntPtr.Zero; }
    }
}

// Windows' own GPU numbers (D3DKMT_ADAPTER_PERFDATA, WDDM 2.4+; Task Manager's): the temperature in tenths of a degree, the fan's rpm.
// The adapter that reports a temperature is kept; a sample reads into a buffer kept for it.
sealed class KmtGpu : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct EnumAdapters2 { public uint Count; public IntPtr Adapters; }
    [StructLayout(LayoutKind.Sequential)] struct QueryInfo { public uint Adapter; public int Type; public IntPtr Data; public uint Size; }
    [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref EnumAdapters2 e);
    [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref QueryInfo q);
    [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref uint adapter);
    const int PerfData = 62, PerfSize = 64, EntrySize = 20;       // KMTQAITYPE_ADAPTERPERFDATA; D3DKMT_ADAPTERINFO

    uint _h;
    IntPtr _buf;
    readonly byte[] _bytes = new byte[PerfSize];

    public static KmtGpu Open()
    {
        var e = new EnumAdapters2();
        IntPtr list = IntPtr.Zero;
        KmtGpu found = null;
        try
        {
            if (D3DKMTEnumAdapters2(ref e) != 0 || e.Count == 0) return null;
            list = Marshal.AllocHGlobal((int)e.Count * EntrySize);
            e.Adapters = list;
            if (D3DKMTEnumAdapters2(ref e) != 0) return null;
            for (int i = 0; i < e.Count; i++)
            {
                uint h = (uint)Marshal.ReadInt32(list + i * EntrySize);
                if (found == null)
                {
                    var g = new KmtGpu { _h = h, _buf = Marshal.AllocHGlobal(PerfSize) };
                    if (!float.IsNaN(g.Read().t)) { found = g; continue; }
                    Marshal.FreeHGlobal(g._buf);
                }
                D3DKMTCloseAdapter(ref h);
            }
            return found;
        }
        catch (Exception x) { Log.Info("temps: D3DKMT " + x.Message); return found; }
        finally { if (list != IntPtr.Zero) Marshal.FreeHGlobal(list); }
    }

    public (float t, float fan) Read()
    {
        if (_buf == IntPtr.Zero) return (float.NaN, float.NaN);
        for (int i = 0; i < PerfSize; i += 8) Marshal.WriteInt64(_buf, i, 0);
        var q = new QueryInfo { Adapter = _h, Type = PerfData, Data = _buf, Size = PerfSize };
        if (D3DKMTQueryAdapterInfo(ref q) != 0) return (float.NaN, float.NaN);
        Marshal.Copy(_buf, _bytes, 0, PerfSize);
        return Parse(_bytes);
    }

    // PhysicalAdapterIndex, 5 frequencies / bandwidths, then FanRPM at 48, Power at 52, Temperature (deci-°C) at 56
    public static (float t, float fan) Parse(byte[] b)
    {
        uint temp = BitConverter.ToUInt32(b, 56), fan = BitConverter.ToUInt32(b, 48);
        return (temp > 0 && temp < 1500 ? temp / 10f : float.NaN, fan > 0 && fan < 20000 ? fan : float.NaN);      // (no fan is 0 here, as unknown)
    }

    public void Dispose()
    {
        if (_buf != IntPtr.Zero) { Marshal.FreeHGlobal(_buf); _buf = IntPtr.Zero; uint h = _h; D3DKMTCloseAdapter(ref h); }
    }
}

// An AMD Ryzen's temperature through PawnIO: the AMDFamily17 module loaded into a handle of PawnIO's device (once), then its
// ioctl_read_smn for THM_TCON_CUR_TMP - bits 31..21 the temperature in 1/8 °C, 49 °C less when RANGE_SEL (bit 19) or both TJ_SEL bits
// (17:16) are set; the first Ryzens (1600X / 1700X / 1800X, 2700X, Threadripper 1900 / 2900) report Tctl a fixed amount over the die's
// Tdie (k10temp's table) - Tdie is shown. The IOCTLs are PawnIO's (device type 41394: load a binary 0x821, run a function 0x841).
sealed class PawnCpu : IDisposable
{
    const uint LoadBinary = (41394u << 16) | (0x821 << 2), Execute = (41394u << 16) | (0x841 << 2), TconCurTmp = 0x00059800;
    const string Device = @"\\?\GLOBALROOT\Device\PawnIO", Module = "pawnio/AMDFamily17.bin", Function = "ioctl_read_smn";

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

    SafeFileHandle _h;
    Mutex _pci;
    float _offset, _last = float.NaN;
    double _lastAt;
    readonly byte[] _in = new byte[32 + 8], _out = new byte[8];

    public static PawnCpu Open(string name, out string why)
    {
        why = null;
        if (!IsZen()) { why = "AMD Ryzen only"; return null; }
        var h = CreateFileW(Device, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);       // read / write, shared, open existing
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            h.Dispose();
            why = err == 5 ? "Needs administrator" : err is 2 or 3 ? "Needs PawnIO" : "Not readable";
            return null;
        }
        byte[] bin;
        using (var s = typeof(PawnCpu).Assembly.GetManifestResourceStream(Module))
        {
            if (s == null) { h.Dispose(); why = "Not readable"; return null; }
            bin = new byte[s.Length];
            s.ReadExactly(bin);
        }
        if (!DeviceIoControl(h, LoadBinary, bin, bin.Length, null, 0, out _, IntPtr.Zero))
        {
            Log.Info($"temps: PawnIO did not load its AMD module (error {Marshal.GetLastWin32Error()})");
            h.Dispose();
            why = "Not readable";
            return null;
        }
        var c = new PawnCpu { _h = h, _offset = Offset(name) };
        System.Text.Encoding.ASCII.GetBytes(Function, 0, Function.Length, c._in, 0);
        BitConverter.TryWriteBytes(c._in.AsSpan(32), (ulong)TconCurTmp);
        // the PCI bus's mutex, as every hardware monitor takes it (one made by an elevated monitor is open to everyone)
        try { if (!Mutex.TryOpenExisting(@"Global\Access_PCI", out c._pci)) c._pci = new Mutex(false, @"Global\Access_PCI"); }
        catch (Exception e) { Log.Info("temps: no PCI mutex (" + e.Message + ") - read without it"); c._pci = null; }
        if (float.IsNaN(c.Read())) { c.Dispose(); why = "Not readable"; return null; }
        return c;
    }

    // one reading; the bus busy for 10 ms: the last one (up to 10 s old)
    public float Read()
    {
        bool locked = false;
        try
        {
            if (_pci != null)
            {
                try { locked = _pci.WaitOne(10); }
                catch (AbandonedMutexException) { locked = true; }
                if (!locked) return Clock.Now - _lastAt < 10 ? _last : float.NaN;
            }
            Array.Clear(_out);
            if (!DeviceIoControl(_h, Execute, _in, _in.Length, _out, _out.Length, out int got, IntPtr.Zero) || got < 4) return float.NaN;
            float t = Decode(BitConverter.ToUInt32(_out, 0)) + _offset;
            if (!(t > 0 && t < 125)) return float.NaN;
            (_last, _lastAt) = (t, Clock.Now);
            return t;
        }
        finally { if (locked) _pci.ReleaseMutex(); }
    }

    public static float Decode(uint raw)
    {
        bool minus49 = (raw & 0x80000) != 0 || (raw & 0x30000) == 0x30000;
        return (raw >> 21) * 0.125f - (minus49 ? 49 : 0);
    }

    // Tdie = Tctl + this (k10temp's offsets)
    public static float Offset(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        if (name.Contains("1600X") || name.Contains("1700X") || name.Contains("1800X")) return -20;
        if (name.Contains("Threadripper 19") || name.Contains("Threadripper 29")) return -27;
        if (name.Contains("2700X")) return -10;
        return 0;
    }

    // AuthenticAMD, family 17h (Zen, Zen 2), 19h (Zen 3, Zen 4), 1Ah (Zen 5)
    public static bool IsZen()
    {
        if (!X86Base.IsSupported) return false;
        var (_, b, c, d) = X86Base.CpuId(0, 0);
        if (b != 0x68747541 || d != 0x69746E65 || c != 0x444D4163) return false;       // "Auth" "enti" "cAMD"
        int eax = X86Base.CpuId(1, 0).Eax, fam = (eax >> 8) & 0xF;
        if (fam == 0xF) fam += (eax >> 20) & 0xFF;
        return fam is 0x17 or 0x19 or 0x1A;
    }

    public void Dispose()
    {
        _h?.Dispose();
        _h = null;
        _pci?.Dispose();
        _pci = null;
    }
}
