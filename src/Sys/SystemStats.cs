using System;
using System.Runtime.InteropServices;

namespace Orbit;

// CPU and memory load for the watch's widgets: GetSystemTimes deltas (the total Task Manager shows) and
// GlobalMemoryStatusEx. Sampled at most once a second, and only while a widget on the watch shows them.
public sealed class SystemStats
{
    [StructLayout(LayoutKind.Sequential)]
    struct FILETIME { public uint Lo, Hi; public ulong V => ((ulong)Hi << 32) | Lo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    ulong _idle, _kernel, _user;
    double _next;
    public float Cpu = -1;                       // 0..1, -1 = not measured yet
    public const int History = 60;               // the last minute of CPU load (for the opened widget)
    public readonly float[] CpuHistory = new float[History];
    public int CpuCount, CpuHead;
    public float RamUsedGB = -1, RamTotalGB = -1;
    public bool Hold;                            // the tools' pictures: keep the made-up numbers (this PC's load would vary them)

    public void Sample(double now)
    {
        if (Hold || now < _next) return;
        _next = now + 1;
        if (GetSystemTimes(out var i, out var k, out var u))
        {
            // kernel time includes idle time
            ulong di = i.V - _idle, busy = (k.V - _kernel) + (u.V - _user);
            if (_kernel != 0 && busy > 0)
            {
                Cpu = Math.Clamp(1f - (float)di / busy, 0, 1);
                CpuHistory[CpuHead] = Cpu;
                CpuHead = (CpuHead + 1) % History;
                CpuCount = Math.Min(History, CpuCount + 1);
            }
            _idle = i.V; _kernel = k.V; _user = u.V;
        }
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            RamTotalGB = m.ullTotalPhys / 1073741824f;
            RamUsedGB = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824f;
        }
    }
}

// The default output device's master volume and mute (Core Audio), for the watch's volume widget.
// "Default" = the console role, the device the Windows volume icon controls. In VR, Virtual Desktop makes its own
// "Virtual Desktop Audio" device the default: the device is checked every second and followed at once (1.9.1 asked
// for the multimedia role every 5 s and kept turning the HyperX headset's volume instead).
public sealed class SystemVolume : IDisposable
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    const int eRender = 0, eConsole = 0, CLSCTX_ALL = 23;
    static Guid _ctx = CoreAudioDevice.Context;     // Orbit's own change: the mixer's callback does not take it for someone else's
    IMMDeviceEnumerator _en;
    IMMDevice _dev;
    IAudioEndpointVolume _vol;
    string _devId, _devName = "";
    double _next, _nextDevice;
    float _asked = -1;                // the level we last set, checked against what the device reads back
    bool _warned;                     // one warning per device
    public float Level = -1;          // 0..1, -1 = no output device
    public bool Muted;
    public string DeviceName => _devName;

    // A device that ignores its own volume - Virtual Desktop's speakers: Windows' own slider did nothing in VR (Erik, 10-15). 1.12.1 set
    // every app's own volume from here; since 1.14 the app's mixer (AudioMixer, on its main loop) makes the apps follow that endpoint
    // volume whoever moves it, so the watch shows the endpoint volume like Windows does and sets it through the mixer - the endpoint
    // and the apps at once, and the mixer does not apply it again.
    public AudioMixer Engine;
    public bool PerApp => Engine != null && Engine.PerApp;
    public bool Fake;                 // the tools: a made-up level and mute, no device opened

    public void Sample(double now)
    {
        if (Fake)
        {
            if (Level < 0) Level = 0.62f;
            return;
        }
        if (Background) { Want(now); return; }
        lock (_gate) SampleNow(now);
    }

    // The app's (Background): the Core Audio calls run on a worker of their own while the watch wants them (Sample = "I want it"), the
    // watch only reads the fields - a call to the audio service takes milliseconds now and then, and the watch's first frame waited 7 ms
    // for one on the main loop (Erik 10-18). The tools': at once, on their thread. A tap's change takes the same lock as the reads.
    public bool Background;
    readonly object _gate = new();
    readonly System.Threading.AutoResetEvent _wake = new(false);
    System.Threading.Thread _worker;
    volatile bool _stop;
    double _wantedUntil;

    void Want(double now)
    {
        bool was = now < System.Threading.Volatile.Read(ref _wantedUntil);
        System.Threading.Volatile.Write(ref _wantedUntil, now + 1.5);
        if (_worker == null)
        {
            _worker = new System.Threading.Thread(Work) { IsBackground = true, Name = "OrbitOverlay volume", Priority = System.Threading.ThreadPriority.BelowNormal };
            _worker.Start();
        }
        else if (!was) _wake.Set();
    }

    void Work()
    {
        while (!_stop)
        {
            double now = Clock.Now;
            bool wanted = now < System.Threading.Volatile.Read(ref _wantedUntil);
            if (wanted) lock (_gate) SampleNow(now);
            _wake.WaitOne(wanted ? 260 : System.Threading.Timeout.Infinite);
        }
    }

    void StopWorker()
    {
        _stop = true;
        _wake.Set();
        _worker?.Join(500);
    }

    void SampleNow(double now)
    {
        if (now < _next) return;
        _next = now + 0.25;
        try
        {
            if (_vol == null || now >= _nextDevice)
            {
                _nextDevice = now + 1;
                if (DefaultChanged()) Acquire();
            }
            if (_vol == null || _vol.GetMasterVolumeLevelScalar(out float l) != 0) { Level = -1; return; }
            if (_asked >= 0)
            {
                if (MathF.Abs(l - _asked) > 0.02f) Warn($"did not take the new level (asked {_asked * 100:0}%, reads {l * 100:0}%)");
                _asked = -1;
            }
            Level = l;
            Muted = _vol.GetMute(out bool m) == 0 && m;
        }
        catch (Exception e)
        {
            Log.Warn("volume: " + e.Message);
            Release();
            // the device list too: a broken one kept failing for good (the microphone in 1.12.3) - a fresh one in 5 s
            if (_en != null) { try { Marshal.ReleaseComObject(_en); } catch { } _en = null; }
            _devId = null;
            Level = -1;
            _next = _nextDevice = now + 5;
        }
    }

    // up / down in steps (0.05 = 5 %); unmutes on the way up
    public void Step(float delta)
    {
        if ((!Fake && _vol == null) || Level < 0) return;
        Set(Level + delta);
        if (delta > 0 && Muted) ToggleMute();
    }

    public void Set(float level)
    {
        lock (_gate) SetNow(level);
        _wake.Set();                    // (read back soon)
    }

    void SetNow(float level)
    {
        level = Math.Clamp(level, 0, 1);
        if (Fake) { Level = level; return; }
        if (PerApp)
        {
            Engine.SetMaster(level, Muted);
            Level = level;
            _next = 0;
            return;
        }
        if (_vol == null) return;
        try
        {
            int hr = _vol.SetMasterVolumeLevelScalar(level, ref _ctx);
            if (hr != 0) { Warn($"refused the new level (0x{hr:X8})"); return; }
            Level = level;
            _asked = level;
            _next = 0;
        }
        catch (Exception e) { Log.Warn("volume: " + e.Message); }
    }

    public void ToggleMute()
    {
        lock (_gate) ToggleMuteNow();
        _wake.Set();
    }

    void ToggleMuteNow()
    {
        if (Fake) { Muted = !Muted; return; }
        if (PerApp)
        {
            Engine.SetMaster(Level, !Muted);
            Muted = !Muted;
            _next = 0;
            return;
        }
        if (_vol == null) return;
        try
        {
            int hr = _vol.SetMute(!Muted, ref _ctx);
            if (hr != 0) { Warn($"refused mute (0x{hr:X8})"); return; }
            Muted = !Muted;
            _next = 0;
        }
        catch (Exception e) { Log.Warn("volume: " + e.Message); }
    }

    void Warn(string what)
    {
        if (_warned) return;
        _warned = true;
        Log.Warn($"volume: '{_devName}' {what}");
    }

    // is the default device another one than the one held (or none held yet)?
    bool DefaultChanged()
    {
        _en ??= (IMMDeviceEnumerator)CoreAudio.NewEnumerator();      // untyped: the one object every class gets (CoreAudio)
        if (_en.GetDefaultAudioEndpoint(eRender, eConsole, out var d) != 0 || d == null) { Release(); _devId = null; return false; }
        string id = d.GetId(out string s) == 0 ? s : null;
        Marshal.ReleaseComObject(d);
        return _vol == null || id != _devId;
    }

    void Acquire()
    {
        Release();
        _en ??= (IMMDeviceEnumerator)CoreAudio.NewEnumerator();      // untyped: the one object every class gets (CoreAudio)
        if (_en.GetDefaultAudioEndpoint(eRender, eConsole, out _dev) != 0 || _dev == null) return;
        _devId = _dev.GetId(out string id) == 0 ? id : null;
        var iid = typeof(IAudioEndpointVolume).GUID;
        if (_dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object o) == 0) _vol = (IAudioEndpointVolume)o;
        _devName = FriendlyName(_devId);
        _warned = false;
        _asked = -1;
        Log.Info($"volume: now controlling '{_devName}'{(_vol == null ? " (no volume control)" : "")}");
    }

    // "Speakers (Virtual Desktop Audio)": the device's name as the Sound settings show it (registry, read only)
    static string FriendlyName(string id)
    {
        try
        {
            if (string.IsNullOrEmpty(id)) return "?";
            string g = id[id.LastIndexOf('{')..];
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{g}\Properties");
            string dev = k?.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string;
            string iface = k?.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string;
            return dev == null ? id : string.IsNullOrEmpty(iface) ? dev : $"{dev} ({iface})";
        }
        catch { return id ?? "?"; }
    }

    void Release()
    {
        if (_vol != null) { Marshal.ReleaseComObject(_vol); _vol = null; }
        if (_dev != null) { Marshal.ReleaseComObject(_dev); _dev = null; }
    }

    public void Dispose()
    {
        StopWorker();
        lock (_gate)
        {
            Release();
            if (_en != null) { Marshal.ReleaseComObject(_en); _en = null; }
        }
    }
}

// The default recording device's mute and input level (Core Audio), for the watch's microphone widget. "Default" = the console
// role, the device Windows' Sound settings call the default input (what VRChat's "Default" microphone follows). The device and its
// mute are checked about once a second, the level four times; a failure is logged once. Fake: --snapshot's own microphone - it
// never opens a device, a tap only flips its state.
public sealed class SystemMic : IDisposable
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    const int eCapture = 1, eConsole = 0, CLSCTX_ALL = 23;
    static Guid _ctx = Guid.Empty;
    IMMDeviceEnumerator _en;
    IMMDevice _dev;
    IAudioEndpointVolume _vol;
    IAudioMeterInformation _meter;
    string _devId;
    double _next, _nextState;
    bool _warned;
    public bool Fake;                 // a made-up microphone (the tools): no device, a tap flips Muted
    public bool Present;              // there is a recording device
    public bool Muted;
    public float Peak = -1;           // its input level now, 0..1 (-1: unknown)
    public int Version;               // moves when the device, its mute or (a step of) its level changes

    public void Sample(double now)
    {
        if (Fake) return;
        if (Background) { Want(now); return; }
        lock (_gate) SampleNow(now);
    }

    // The app's (Background): the Core Audio calls run on a worker of their own while the watch wants them (Sample = "I want it"), the
    // watch only reads the fields - a call to the audio service takes milliseconds now and then, and the watch's first frame waited 7 ms
    // for one on the main loop (Erik 10-18). The tools': at once, on their thread. A tap's change takes the same lock as the reads.
    public bool Background;
    readonly object _gate = new();
    readonly System.Threading.AutoResetEvent _wake = new(false);
    System.Threading.Thread _worker;
    volatile bool _stop;
    double _wantedUntil;

    void Want(double now)
    {
        bool was = now < System.Threading.Volatile.Read(ref _wantedUntil);
        System.Threading.Volatile.Write(ref _wantedUntil, now + 1.5);
        if (_worker == null)
        {
            _worker = new System.Threading.Thread(Work) { IsBackground = true, Name = "OrbitOverlay microphone", Priority = System.Threading.ThreadPriority.BelowNormal };
            _worker.Start();
        }
        else if (!was) _wake.Set();
    }

    void Work()
    {
        while (!_stop)
        {
            double now = Clock.Now;
            bool wanted = now < System.Threading.Volatile.Read(ref _wantedUntil);
            if (wanted) lock (_gate) SampleNow(now);
            _wake.WaitOne(wanted ? 260 : System.Threading.Timeout.Infinite);
        }
    }

    void StopWorker()
    {
        _stop = true;
        _wake.Set();
        _worker?.Join(500);
    }

    void SampleNow(double now)
    {
        if (now < _next) return;
        _next = now + 0.25;
        try
        {
            if (_vol == null || now >= _nextState)
            {
                _nextState = now + 1;
                if (DefaultChanged()) Acquire();
                bool m = _vol != null && _vol.GetMute(out bool mm) == 0 && mm;
                bool p = _vol != null;
                if (m != Muted || p != Present) { Muted = m; Present = p; Version++; }
            }
            float pk = -1;
            if (_meter != null && _meter.GetPeakValue(out float v) == 0) pk = Math.Clamp(v, 0, 1);
            if ((int)(pk * 20) != (int)(Peak * 20)) Version++;
            Peak = pk;
        }
        catch (Exception e)
        {
            Warn($"{e.Message} ({e.StackTrace?.Split('\n')[0].Trim()})");      // where: the cast that failed in 1.12.0
            Release();
            // 1.12.3 failed in DefaultChanged and never recovered: the device list goes too, a fresh one in 5 s
            if (_en != null) { try { Marshal.ReleaseComObject(_en); } catch { } _en = null; }
            _devId = null;
            Present = false;
            Peak = -1;
            _next = _nextState = now + 5;
        }
    }

    public void ToggleMute()
    {
        lock (_gate) ToggleMuteNow();
        _wake.Set();
    }

    void ToggleMuteNow()
    {
        if (Fake) { Muted = !Muted; Present = true; Version++; return; }
        if (_vol == null) return;
        try
        {
            int hr = _vol.SetMute(!Muted, ref _ctx);
            if (hr != 0) { Warn($"refused mute (0x{hr:X8})"); return; }
            Muted = !Muted;
            Version++;
            Log.Info($"microphone: {(Muted ? "muted" : "live")}");
            _nextState = 0;
        }
        catch (Exception e) { Warn(e.Message); }
    }

    void Warn(string what)
    {
        if (_warned) return;
        _warned = true;
        Log.Warn("microphone: " + what);
    }

    bool DefaultChanged()
    {
        _en ??= (IMMDeviceEnumerator)CoreAudio.NewEnumerator();      // untyped: the one object every class gets (CoreAudio)
        if (_en.GetDefaultAudioEndpoint(eCapture, eConsole, out var d) != 0 || d == null) { Release(); _devId = null; return false; }
        string id = d.GetId(out string s) == 0 ? s : null;
        Marshal.ReleaseComObject(d);
        return _vol == null || id != _devId;
    }

    void Acquire()
    {
        Release();
        _en ??= (IMMDeviceEnumerator)CoreAudio.NewEnumerator();      // untyped: the one object every class gets (CoreAudio)
        if (_en.GetDefaultAudioEndpoint(eCapture, eConsole, out _dev) != 0 || _dev == null) return;
        _devId = _dev.GetId(out string id) == 0 ? id : null;
        // "as", not a cast: some virtual devices (Virtual Desktop's) hand back an object that refuses one of these - the widget
        // then shows what it can (1.12.0 threw "Specified cast is not valid" and never showed the microphone)
        var iid = typeof(IAudioEndpointVolume).GUID;
        if (_dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object o) == 0) _vol = o as IAudioEndpointVolume;
        var mid = typeof(IAudioMeterInformation).GUID;
        if (_dev.Activate(ref mid, CLSCTX_ALL, IntPtr.Zero, out object mo) == 0) _meter = mo as IAudioMeterInformation;
        _warned = false;
        Version++;
        Log.Info($"microphone: following the default recording device{(_vol == null ? " (no mute control)" : "")}{(_meter == null ? " (no level)" : "")}");
    }

    void Release()
    {
        if (_meter != null) { Marshal.ReleaseComObject(_meter); _meter = null; }
        if (_vol != null) { Marshal.ReleaseComObject(_vol); _vol = null; }
        if (_dev != null) { Marshal.ReleaseComObject(_dev); _dev = null; }
    }

    public void Dispose()
    {
        StopWorker();
        lock (_gate)
        {
            Release();
            if (_en != null) { Marshal.ReleaseComObject(_en); _en = null; }
        }
    }
}
