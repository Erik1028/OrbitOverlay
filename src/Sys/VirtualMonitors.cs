using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Orbit;

// Virtual monitors (Erik 10-21: "lehetne virtuális monitorokat is létrehozni" - and for anyone who downloads Orbit, out of the box):
// screens Windows extends the desktop onto that exist only in VR. The display driver is the open-source Virtual Display Driver
// (github.com/VirtualDrivers/Virtual-Display-Driver, MIT, signed by SignPath Foundation): "Add" downloads the driver-only zip of a
// pinned release (its SHA-256 checked), creates the Root\MttVDD device and installs the driver on it. Windows asks about the publisher
// once - Orbit does NOT trust the certificate for the user (the project's own silent script does).
// How many: <monitors><count> in the driver's vdd_settings.xml (C:\VirtualDisplayDriver, or the folder its VDDPATH names), read when
// the device starts - a count is the file and a device restart; none is the device disabled (a count of 0 the driver reads as 1).
// Every step on a thread of its own: pnputil takes a second or two and the screens blink while Windows adds them. The settings only
// read State / Count / Doing / Note.
public sealed class VirtualMonitors
{
    public const string Release = "25.7.23";
    public const int Max = 4;
    const string HwId = @"Root\MttVDD";
    const string Url = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/" + Release + "/";
    // the x64 driver ships in the zip named x86 (its DLL is AMD64: checked 10-21); the digests GitHub lists for the release assets
    static readonly (Architecture arch, string file, string sha)[] Zips =
    {
        (Architecture.X64, "VirtualDisplayDriver-x86.Driver.Only.zip", "e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a"),
        (Architecture.Arm64, "VirtualDisplayDriver-ARM64.Driver.Only.zip", "dcd487161469c726bab60f174a7e7f70ee02afd437cad0d5c0e6755953de5a25"),
    };

    public enum Kind { Unknown, NoDriver, Off, On, Broken }

    public volatile Kind State = Kind.Unknown;
    public volatile int Count;                     // on: the monitors the driver was started with
    public volatile bool Busy;
    public volatile string Doing;                  // what the job does now ("Installing the driver...")
    public volatile string Note;                   // the last job's trouble (null: none)
    public bool Admin = Elevation.IsElevated;
    public bool Fake;                              // --snapshot: a state set by hand, nothing looked at or run
    string _instance;                              // ROOT\DISPLAY\000n
    long _lookAt, _retryAt;                        // Environment.TickCount64

    public bool Ready => !Busy && (State == Kind.On || State == Kind.Off || State == Kind.Broken);

    // ---- what the settings and the app call ----

    // the device's state again - in the background, at most every 10 s (the settings page asks as it draws)
    public void LookSoon()
    {
        if (Fake || Busy || Environment.TickCount64 < _lookAt) return;
        _lookAt = Environment.TickCount64 + 10000;
        Start(null, () => { });
    }

    // the app's wish (OrbitApp.Virtual.cs): this many on (0: the device off); nothing when it is so already, or a job runs, or the
    // last try failed less than a minute ago (asked: a click in the settings - at once)
    public void Want(int n, bool asked = false)
    {
        if (Fake || !Admin || !Ready || (!asked && Environment.TickCount64 < _retryAt)) return;
        n = Math.Clamp(n, 0, Max);
        if (n == 0 ? State == Kind.Off : State == Kind.On && Count == n) return;
        Start(n == 0 ? "Turning them off..." : State == Kind.Off ? "Turning them on..." : "Changing the count...", () => SetCount(n));
    }

    public void Install(int n)
    {
        if (Fake || !Admin || Busy || State != Kind.NoDriver) return;
        Start("Downloading the driver...", () => DoInstall(Math.Clamp(n, 1, Max)));
    }

    public void Remove()
    {
        if (Fake || !Admin || !Ready) return;
        Start("Removing the driver...", DoRemove);
    }

    // Orbit closing: off now, on this thread (a job still running gets a moment first)
    public void OffNow(int waitMs)
    {
        if (Fake || !Admin) return;
        for (int i = 0; i < waitMs / 50 && Busy; i++) Thread.Sleep(50);
        if (Busy || State != Kind.On || _instance == null) return;
        try
        {
            Pnp("/disable-device", _instance);
            Log.Info("virtual monitors: off (Orbit closing)");
        }
        catch (Exception e) { Log.Warn("virtual monitors: off at exit: " + e.Message); }
    }

    // --virtualstatus: what Orbit sees - read only, nothing installed or changed
    public static int Status()
    {
        var d = FindDevice();
        Log.Info(d == null ? $"virtual monitors: no {HwId} device"
            : $"virtual monitors: {d.Value.instance}, status 0x{d.Value.status:X}, problem {d.Value.problem}" +
              $"{(d.Value.problem == Api.CM_PROB_DISABLED ? " (disabled)" : (d.Value.status & Api.DN_STARTED) != 0 ? " (started)" : "")}, driver {DriverInf(d.Value.instance) ?? "?"}");
        Log.Info($"virtual monitors: settings {SettingsFile}: {(File.Exists(SettingsFile) ? "count " + (ReadCount()?.ToString() ?? "?") : "none")}; admin {Elevation.IsElevated}; {RuntimeInformation.OSArchitecture}");
        foreach (var m in Monitors.Enumerate()) Log.Info("  monitor " + m + "  " + m.Id);
        return 0;
    }

    void Start(string doing, Action job)
    {
        Busy = true;
        Doing = doing;
        // a thread of its own, STA: the driver install shows Windows' publisher prompt
        var t = new Thread(() =>
        {
            try
            {
                if (doing != null) Note = null;
                job();
            }
            catch (Exception e)
            {
                Note = e.Message;
                _retryAt = Environment.TickCount64 + 60000;       // the app's wish tries again in a minute (a click at once)
                Log.Warn("virtual monitors: " + e.Message);
            }
            try { Look(); } catch (Exception e) { Log.Warn("virtual monitors: look: " + e.Message); }
            Doing = null;
            Busy = false;
        }) { IsBackground = true, Name = "OrbitOverlay virtual monitors" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    // ---- the device ----

    void Look()
    {
        var d = FindDevice();
        _instance = d?.instance;
        if (d == null) { State = Kind.NoDriver; Count = 0; return; }
        var (_, status, problem) = d.Value;
        if (problem == Api.CM_PROB_DISABLED) { State = Kind.Off; Count = 0; }
        else if ((status & Api.DN_STARTED) != 0 || problem == 0) { State = Kind.On; Count = ReadCount() ?? 1; }    // (no problem: starting)
        else
        {
            State = Kind.Broken;
            Count = 0;
            Note ??= $"The driver reports a problem (code {problem})";
        }
    }

    void SetCount(int n)
    {
        Look();
        if (_instance == null) throw new Exception("The driver is not installed");
        bool reboot;
        if (n == 0) reboot = State != Kind.Off && Pnp("/disable-device", _instance);
        else
        {
            WriteCount(n);
            reboot = Pnp(State == Kind.Off ? "/enable-device" : "/restart-device", _instance);     // the driver reads the file as it starts
        }
        Look();
        // not so now (Windows wants a restart, or has not done it): the app's wish waits - no pnputil and no blinking every 5 s
        if (reboot || (n == 0 ? State != Kind.Off : State != Kind.On))
        {
            _retryAt = Environment.TickCount64 + (reboot ? 600000 : 60000);
            Note = reboot ? "Windows wants a restart for this" : "Windows has not done it yet";
            Log.Warn($"virtual monitors: wanted {n}, now {State} {Count}{(reboot ? " - Windows wants a restart" : "")}");
        }
        else Log.Info(n == 0 ? "virtual monitors: off" : $"virtual monitors: {n} on");
    }

    // (the publisher prompt without an owner window: Orbit's own runs on the main loop's thread, this one would wait on it)
    void DoInstall(int n)
    {
        var owner = IntPtr.Zero;
        string inf = Download();
        Doing = "Installing the driver...";
        WriteCount(n);
        if (FindDevice() != null) { Log.Info("virtual monitors: the device is there already"); return; }
        var cls = Api.DisplayClass;
        IntPtr set = Api.SetupDiCreateDeviceInfoList(ref cls, owner);
        if (set == Api.Invalid) throw new Exception("Windows: no device list (" + Marshal.GetLastWin32Error() + ")");
        try
        {
            var dev = new Api.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<Api.SP_DEVINFO_DATA>() };
            if (!Api.SetupDiCreateDeviceInfoW(set, "Display", ref cls, null, owner, Api.DICD_GENERATE_ID, ref dev))
                throw new Exception("Windows: no new device (" + Marshal.GetLastWin32Error() + ")");
            var ids = Encoding.Unicode.GetBytes(HwId + "\0\0");
            if (!Api.SetupDiSetDeviceRegistryPropertyW(set, ref dev, Api.SPDRP_HARDWAREID, ids, (uint)ids.Length))
                throw new Exception("Windows: the device's id not set (" + Marshal.GetLastWin32Error() + ")");
            if (!Api.SetupDiCallClassInstaller(Api.DIF_REGISTERDEVICE, set, ref dev))
                throw new Exception("Windows: the device not made (" + Marshal.GetLastWin32Error() + ")");
            Log.Info("virtual monitors: the Root\\MttVDD device made; the driver on it (Windows asks about the publisher)");
            Doing = "Windows asks about the driver...";
            if (!Api.UpdateDriverForPlugAndPlayDevicesW(owner, HwId, inf, Api.INSTALLFLAG_FORCE, out bool reboot))
            {
                int err = Marshal.GetLastWin32Error();
                Api.SetupDiCallClassInstaller(Api.DIF_REMOVE, set, ref dev);          // no driver: no empty device left behind
                throw new Exception(err == 1223 || err == unchecked((int)0x800704C7) ? "The driver was not installed (cancelled)"
                    : unchecked((uint)err) == 0xE0000243 || unchecked((uint)err) == 0xE0000242 ? "The driver was not installed: its publisher was not allowed"
                    : $"The driver was not installed (0x{err:X})");
            }
            Log.Info($"virtual monitors: driver {Release} installed{(reboot ? " - Windows wants a restart" : "")}");
            if (reboot) Note = "Windows wants a restart for the driver";
        }
        finally { Api.SetupDiDestroyDeviceInfoList(set); }
    }

    void DoRemove()
    {
        Look();
        if (_instance == null) return;
        string oem = DriverInf(_instance);
        Pnp("/remove-device", _instance);
        if (oem != null && oem.StartsWith("oem", StringComparison.OrdinalIgnoreCase)) Pnp("/delete-driver", oem, "/uninstall");
        Log.Info($"virtual monitors: driver removed ({oem})");
    }

    // the pinned zip into %LOCALAPPDATA%\Orbit\vdd\<release>, checked, unpacked: the INF's path
    static string Download()
    {
        var arch = RuntimeInformation.OSArchitecture;
        var z = Zips.FirstOrDefault(x => x.arch == arch);
        if (z.file == null) throw new Exception($"No driver for {arch}");
        string dir = Path.Combine(Log.Dir, "vdd", Release);
        string inf = Path.Combine(dir, "VirtualDisplayDriver", "MttVDD.inf");
        string zip = Path.Combine(dir, z.file);
        Directory.CreateDirectory(dir);
        byte[] data = File.Exists(zip) ? File.ReadAllBytes(zip) : null;
        if (data == null || Sha(data) != z.sha)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitOverlay");
            data = http.GetByteArrayAsync(Url + z.file).GetAwaiter().GetResult();
            if (Sha(data) != z.sha) throw new Exception("The downloaded driver is not the expected file");
            File.WriteAllBytes(zip, data);
        }
        // unpacked afresh each time from the checked bytes (only Orbit's own folder)
        string unz = Path.Combine(dir, "VirtualDisplayDriver");
        if (Directory.Exists(unz)) Directory.Delete(unz, true);
        using (var ms = new MemoryStream(data)) ZipFile.ExtractToDirectory(ms, dir);
        if (!File.Exists(inf)) throw new Exception("The driver zip has no MttVDD.inf");
        return inf;
    }

    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    // ---- the driver's settings file ----

    static string SettingsDir()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\MikeTheTech\VirtualDisplayDriver");
            if (k?.GetValue("VDDPATH") is string p && p.Length > 0) return p;
        }
        catch { }
        return @"C:\VirtualDisplayDriver";
    }

    static string SettingsFile => Path.Combine(SettingsDir(), "vdd_settings.xml");

    static readonly Regex CountRx = new(@"(<monitors>\s*<count>)\s*\d+\s*(</count>)", RegexOptions.IgnoreCase);

    static int? ReadCount()
    {
        try
        {
            var m = Regex.Match(File.ReadAllText(SettingsFile), @"<monitors>\s*<count>\s*(\d+)\s*</count>", RegexOptions.IgnoreCase);
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }
        catch { return null; }
    }

    // the count into the file; none there yet: Orbit's own (1920x1080 at 60 Hz first - the first mode is the one a new monitor starts
    // at). Someone else's file: only the number changes, and before the first change it is copied aside
    static void WriteCount(int n)
    {
        string f = SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(f));
        if (!File.Exists(f))
        {
            File.WriteAllText(f, OwnSettings.Replace("{count}", n.ToString()), new UTF8Encoding(false));
            Log.Info("virtual monitors: settings written: " + f);
            return;
        }
        var bytes = File.ReadAllBytes(f);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        if (!CountRx.IsMatch(text)) throw new Exception("The driver's settings file has no monitor count: " + f);
        string next = CountRx.Replace(text, "${1}" + n + "${2}", 1);
        if (next == text) return;
        string aside = Path.Combine(Path.GetDirectoryName(f), "vdd_settings.before-orbit.xml");
        if (!text.Contains(OwnMark) && !File.Exists(aside)) File.Copy(f, aside);
        File.WriteAllText(f, next, new UTF8Encoding(bom));
    }

    const string OwnMark = "written by OrbitOverlay";
    const string OwnSettings = """
        <?xml version='1.0' encoding='utf-8'?>
        <!-- written by OrbitOverlay (Settings > Desktops > Virtual monitors sets the count). The first resolution is the one a new
             monitor starts at; Windows' display settings pick any of the others. -->
        <vdd_settings>
            <monitors>
                <count>{count}</count>
            </monitors>
            <gpu>
                <friendlyname>default</friendlyname>
            </gpu>
            <resolutions>
                <resolution><width>1920</width><height>1080</height><refresh_rate>60</refresh_rate></resolution>
                <resolution><width>2560</width><height>1440</height><refresh_rate>60</refresh_rate></resolution>
                <resolution><width>3840</width><height>2160</height><refresh_rate>60</refresh_rate></resolution>
                <resolution><width>1280</width><height>720</height><refresh_rate>60</refresh_rate></resolution>
                <resolution><width>1920</width><height>1080</height><refresh_rate>90</refresh_rate></resolution>
                <resolution><width>2560</width><height>1440</height><refresh_rate>90</refresh_rate></resolution>
            </resolutions>
            <options>
                <CustomEdid>false</CustomEdid>
                <PreventSpoof>false</PreventSpoof>
                <EdidCeaOverride>false</EdidCeaOverride>
                <HardwareCursor>true</HardwareCursor>
                <SDR10bit>false</SDR10bit>
                <HDRPlus>false</HDRPlus>
                <logging>false</logging>
                <debuglogging>false</debuglogging>
            </options>
        </vdd_settings>

        """;

    // ---- Windows ----

    // true: done, but Windows wants a restart for it
    static bool Pnp(params string[] args)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new Exception("pnputil did not start");
        string output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } throw new Exception("pnputil took too long"); }
        string line = string.Join(" / ", output.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith("Microsoft PnP")));
        Log.Info($"virtual monitors: pnputil {string.Join(' ', args)} -> {p.ExitCode}: {line}");
        if (p.ExitCode == 3010) return true;                // ERROR_SUCCESS_REBOOT_REQUIRED
        if (p.ExitCode != 0) throw new Exception($"pnputil {args[0]} failed ({p.ExitCode})");
        return false;
    }

    static (string instance, uint status, uint problem)? FindDevice()
    {
        var cls = Api.DisplayClass;
        IntPtr set = Api.SetupDiGetClassDevsW(ref cls, null, IntPtr.Zero, Api.DIGCF_PRESENT);
        if (set == Api.Invalid) return null;
        try
        {
            var d = new Api.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<Api.SP_DEVINFO_DATA>() };
            var buf = new byte[2048];
            for (uint i = 0; Api.SetupDiEnumDeviceInfo(set, i, ref d); i++)
            {
                if (!Api.SetupDiGetDeviceRegistryPropertyW(set, ref d, Api.SPDRP_HARDWAREID, out _, buf, (uint)buf.Length, out uint got)) continue;
                var ids = Encoding.Unicode.GetString(buf, 0, (int)got).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (!ids.Any(x => x.Equals(HwId, StringComparison.OrdinalIgnoreCase))) continue;
                var id = new StringBuilder(512);
                Api.SetupDiGetDeviceInstanceIdW(set, ref d, id, id.Capacity, out _);
                if (Api.CM_Get_DevNode_Status(out uint status, out uint problem, d.DevInst, 0) != 0) { status = 0; problem = 0; }
                return (id.ToString(), status, problem);
            }
        }
        finally { Api.SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    // the driver package's name in the store (oem12.inf), for removing it
    static string DriverInf(string instance)
    {
        var cls = Api.DisplayClass;
        IntPtr set = Api.SetupDiGetClassDevsW(ref cls, null, IntPtr.Zero, Api.DIGCF_PRESENT);
        if (set == Api.Invalid) return null;
        try
        {
            var d = new Api.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<Api.SP_DEVINFO_DATA>() };
            var id = new StringBuilder(512);
            var buf = new byte[1024];
            for (uint i = 0; Api.SetupDiEnumDeviceInfo(set, i, ref d); i++)
            {
                Api.SetupDiGetDeviceInstanceIdW(set, ref d, id, id.Capacity, out _);
                if (!string.Equals(id.ToString(), instance, StringComparison.OrdinalIgnoreCase)) continue;
                var key = Api.DEVPKEY_Device_DriverInfPath;
                if (!Api.SetupDiGetDevicePropertyW(set, ref d, ref key, out _, buf, (uint)buf.Length, out uint got, 0)) return null;
                return Encoding.Unicode.GetString(buf, 0, (int)got).TrimEnd('\0');
            }
        }
        finally { Api.SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    static class Api
    {
        public static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");
        public static readonly IntPtr Invalid = new(-1);
        public const uint DIGCF_PRESENT = 2, SPDRP_HARDWAREID = 1, DICD_GENERATE_ID = 1, DIF_REGISTERDEVICE = 0x19, DIF_REMOVE = 5,
            INSTALLFLAG_FORCE = 1, DN_STARTED = 0x8, CM_PROB_DISABLED = 22;
        public static DEVPROPKEY DEVPKEY_Device_DriverInfPath = new() { fmtid = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), pid = 5 };

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevsW(ref Guid cls, string enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA d, uint prop, out uint type, byte[] buf, uint size, out uint required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA d, StringBuilder id, int size, out int required);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA d, ref DEVPROPKEY key, out uint type, byte[] buf, uint size, out uint required, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid cls, IntPtr hwnd);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiCreateDeviceInfoW(IntPtr set, string name, ref Guid cls, string desc, IntPtr hwnd, uint flags, ref SP_DEVINFO_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA d, uint prop, byte[] buf, uint size);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiCallClassInstaller(uint dif, IntPtr set, ref SP_DEVINFO_DATA d);
        [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool UpdateDriverForPlugAndPlayDevicesW(IntPtr hwnd, string hwid, string inf, uint flags, out bool reboot);
        [DllImport("cfgmgr32.dll")]
        public static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
    }
}
