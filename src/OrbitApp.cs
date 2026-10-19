using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Valve.VR;
using Windows.Graphics.DirectX.Direct3D11;

namespace Orbit;

public sealed partial class OrbitApp : IOrbitHost, IDisposable
{
    public const string AppKey = "erik.orbit";
    public const string ExitEventName = @"Local\OrbitOverlayExit", ShowEventName = @"Local\OrbitOverlayShow";
    // created with a permissive security descriptor so a normal-rights --exit / second start can reach an elevated Orbit
    readonly EventWaitHandle _exitEvent = NamedObjects.CreateEvent(ExitEventName);
    readonly EventWaitHandle _showEvent = NamedObjects.CreateEvent(ShowEventName);
    const int DashW = 1500, DashH = 960;

    readonly ConfigStore _cfg = new();
    readonly bool _startHidden;
    Gpu _gpu;
    Painter _painter;
    readonly PanelData _data = new();
    readonly GpuStats _gpuStats = new();
    readonly EyeGaze _eyes = new();                  // Virtual Desktop's eye tracking (Sys/EyeGaze.cs)
    readonly MixtapeLink _mixtape = new();           // Mixtape's Remote API: the Mixtape widget (Sys/MixtapeLink.cs)
    readonly MediaInfo _media = new();
    readonly AudioMixer _mixer = new();              // the output device's apps; the Windows volume in VR (Sys/AudioMixer.cs)
    readonly NotificationHistory _notes = new();     // the watch's notifications widget
    readonly WeatherService _weather = new();        // the weather widget's report (Sys/Weather.cs)
    readonly LumosLink _lumos = new();               // the room light through Lumos (Sys/RoomLights.cs)
    readonly PrismaLink _prisma = new();             // the PC's RGB through Prisma
    readonly HexpadLink _hexpad = new();
    readonly RoomWatcher _room = new();
    RoomWatcher.Snapshot _roomSnap = new();
    RoomPanel _roomPanel;
    StatusLine _status;
    double _nextStatus;
    HexpadFaces _dockFaces;
    DockPanel _dockPanel;
    HexpadLink.Snapshot _dockSnap = new();
    readonly NotificationListener _notifyIn = new();
    NotificationPanel _notifyPanel;
    WristPanel _wrist;
    KeyboardPanel _kb;
    SettingsPanel _settingsPanel;
    readonly List<DesktopPanel> _desktops = new();
    readonly List<WindowPanel> _windowPanels = new();
    TextFocusWatcher _focus;
    bool _kbAuto;            // the keyboard is up because a text field got focus (hides itself again)
    double _kbHideAt = -1;
    int _focusSeq;
    float _dimNow = 1;
    double _nextWindowScan, _nextDeadRebuild;
    List<MonitorInfo> _monitors = new();
    string _monitorKey = "";
    readonly MouseOwner _mouse = new();
    readonly DesktopLayout _layout;
    readonly OscSender _osc = new();
    readonly double[] _gripDownAt = { -10, -10 };
    double _lastChord = -10;
    bool _restoreDesktops = true, _restoreKeyboard;
    Theater _theater;
    GazeFocus _gazeFocus;            // Desktops > Focus the monitor you look at (Panels/GazeFocus.cs)
    ITheaterScreen _lastScreen;      // the monitor / window the laser was on last: theater mode plays it
    bool _theaterKb;                 // theater mode put the keyboard away (it comes back after)
    readonly List<ITheaterScreen> _screens = new(), _theaterChoices = new();
    TheaterBar _theaterBar;
    WhatsNewPanel _whatsNewPanel;
    readonly VrSession _vr = new();
    readonly FramePacer _pacer = new();
    bool _awake = true;
    Laser[] _lasers;
    SystemCursor _sysCursor;
    IDirect3DDevice _winrtDev;
    SettingsWindow _win;
    Tray _tray;

    // SteamVR dashboard tab
    VrOverlay _dash, _dashThumb;
    Surface _dashSurf, _thumbSurf;
    SettingsView _dashView;
    Ui _dashUi;
    readonly PointerState _dashPtr = new();
    bool _dashDirty = true;
    double _dashNext;

    bool _quit;
    double _nextConnect, _nextMonitorCheck, _nextMonitorFull, _nextRuntimeCheck, _last;
    int _screensSig;

    bool ScreensMoved()
    {
        var h = new HashCode();
        foreach (var s in Screen.AllScreens) h.Add(s.Bounds);
        int sig = h.ToHashCode();
        if (sig == _screensSig) return false;
        _screensSig = sig;
        return true;
    }
    string _vrStatus = "Waiting for SteamVR";

    static double Now => Clock.Now;
    Config C => _cfg.Data;

    public OrbitApp(bool startHidden)
    {
        _startHidden = startHidden;
        _cfg.Background = true;              // the config written on a worker (Config.cs)
        InputQueue.On = true;                // what goes to Windows, on its own thread (Sys/InputQueue.cs)
        int adapter = -1;
        // If SteamVR already runs, connect first so the GPU device is created on its adapter.
        if (VrSession.RuntimeRunning() && _vr.TryConnect(ActionManifest, out _, AppKey))
            adapter = _vr.AdapterIndex;
        _gpu = new Gpu(adapter);
        _painter = new Painter(_gpu);
        Lang.Set(C.General.Language);          // General > Language (null: English, nothing looked up)
        CyberScreenFx.Painter = _painter;   // the Cyber screens' HUD frames draw with it
        Theme.Apply(C.Appearance);
        CyberFx.Live = true;                // the Cyber style's effects may start by themselves here (not in the --snapshot tools)
        // 1.12, the watch pages: a new install starts on the VRChat and Performance pages; an older config gets them once, after its
        // own face (a copy of config.json first). Only the config here - no window exists yet (1.10.0 crashed touching one).
        WatchPages.Migrate(_cfg);
        WatchPages.MergeMixtape(_cfg);      // 10-19: the Mixtape widget went into Now playing (a copy of config.json first)
        _layout = new DesktopLayout(_desktops, _cfg);
        _gazeFocus = new GazeFocus(_desktops);
        _data.Gpu = _gpuStats;
        _data.Media = _media;
        _data.VramTotalGB = _gpu.DedicatedVram / (1024f * 1024 * 1024);

        _wrist = new WristPanel(_gpu, _painter, _cfg) { OnAction = WristAction };
        _wrist.UseRealAudio(_mixer);                         // the watch's volume and mixer on the real output device (the tools' are made up)
        _wrist.Mixtape = _mixtape;                           // the Mixtape widget's link (Sys/MixtapeLink.cs)
        _wrist.OnOpenPhoto = PhotoGallery.Open;              // the latest photo's Open in VR Gallery, on a tap (Sys/PhotoWatcher.cs)
        _kb = new KeyboardPanel(_gpu, _painter, _cfg);
        _settingsPanel = new SettingsPanel(_gpu, _painter, _cfg, this, PreparePreviews);
        _dockFaces = new HexpadFaces();
        _dockPanel = new DockPanel(_gpu, _painter, _cfg, _hexpad, _dockFaces);
        _wrist.DockIn = new DockInput(_hexpad, _cfg);       // the watch's opened dock: the same link, its own presses
        _roomPanel = new RoomPanel(_gpu, _painter, _cfg);
        _whatsNewPanel = new WhatsNewPanel(_gpu, _painter) { OnOk = DismissWhatsNew };
        _theater = new Theater(_gpu, _painter);
        _theater.SoundLevel = _mixer.OutputPeak;             // Cyber's horizon breathes with what plays (Panels/TheaterHorizon.cs)
        _theaterBar = new TheaterBar(_gpu, _painter, _cfg)
        {
            OnSwitch = s => _theater.Switch(s),
            OnExit = ToggleTheater,
            OnTheater = s => { _lastScreen = s; if (!_theater.On) ToggleTheater(); },
            OnSettings = () => { _theater.Apply(C.Theater); _cfg.MarkDirty(); _win.Dirty = true; },
            OnKeyboard = ToggleKeyboard,
            KeyboardShown = () => C.Keyboard.Visible,
            // Cyber's media console in theater mode (Panels/TheaterBarMedia.cs): the Media widget's reader and its very commands
            ReadMedia = () => TheaterBar.Read(_media),
            OnMedia = WristAction,
            OnSeekTo = s => _media.SeekTo(s),
        };
        _status = new StatusLine(_osc);
        _data.Dock = _dockSnap;
        _data.DockFaces = _dockFaces;
        _hexpad.Start();
        _notifyPanel = new NotificationPanel(_gpu, _painter, _cfg) { History = _notes };
        _data.Notes = _notes;
        // VRChat chatbox: /chatbox/input <text> <send now> <notification sound>, /chatbox/typing <bool>
        _kb.OnChatSend = text =>
        {
            _osc.SendMessage(C.General.OscPort, "/chatbox/input", text, true, true);
            _status.Pause(Clock.Now, 20);                    // your own line stays up for a while
            Log.Info($"chatbox: {text.Length} chars");
        };
        _kb.OnChatTyping = on => { _status.Typing = on; _osc.SendMessage(C.General.OscPort, "/chatbox/typing", on); };
        SyncMonitors(force: true);
        SyncWindowPanels();
        NoteVersion();
        try
        {
            _focus = new TextFocusWatcher();
            _focus.Start();
            _mouse.Clicked = () => { if (C.Keyboard.AutoShow) _focus.InspectFor(1.5); };
        }
        catch (Exception e) { Log.Warn("focus watch: " + e.Message); _focus = null; }

        _tray = new Tray(ShowSettings, Recenter, ToggleDesktops, ToggleKeyboard, () => _quit = true);
        var view = new SettingsView(this);
        _win = new SettingsWindow(_gpu, _painter, view, () => PreparePreviews(view), _tray.AppIcon) { ModalTick = ModalTick };
        if (!_startHidden) _win.Show();

        if (_vr.Connected) OnVrConnected();
    }

    static string ActionManifest => Path.Combine(AppContext.BaseDirectory, "assets", "actions.json");
    static string VrManifest => Path.Combine(AppContext.BaseDirectory, "orbit.vrmanifest");

    public void Run()
    {
        Native.timeBeginPeriod(1);
        NoPowerThrottling();
        ApplyGpuPriority();
        // the loop needs ~0.2 ms a frame but should get it on time even when VRChat keeps every core busy
        try { Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; } catch (Exception e) { Log.Warn("priority: " + e.Message); }
        _last = Now;
        try
        {
            while (!_quit)
            {
                Application.DoEvents();
                if (_exitEvent.WaitOne(0)) _quit = true;
                if (_showEvent.WaitOne(0)) ShowSettings();
                if (_quit) break;
                SafeTick();
                if (_vr.Connected)
                {
                    double w0 = Clock.Now;
                    if (C.General.HeadsetPacing)
                    {
                        _pacer.Wait(_awake ? _vr.DisplayHz : 20);       // headset off: a slow tick is plenty (see FramePacer)
                        if (_lateN < _lateMs.Length) _lateMs[_lateN++] = (float)(_pacer.LastLate * 1000);
                    }
                    else
                    {
                        // once per game frame (72/90/120 Hz when the game keeps up); a short sleep when none comes
                        var we = OpenVR.Overlay.WaitFrameSync(30);
                        if (we != EVROverlayError.None)
                        {
                            if (!_waitWarned) { _waitWarned = true; Log.Warn("WaitFrameSync: " + we); }
                            Thread.Sleep(5);
                        }
                    }
                    _perfWaitSum += (Clock.Now - w0) * 1000;
                }
                else
                    Native.MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, 20, Native.QS_ALLINPUT, Native.MWMO_INPUTAVAILABLE);
            }
        }
        finally { Native.timeEndPeriod(1); }
    }

    // ---- timing, logged every 10 s while in VR (to find what makes the loop slow) ----
    static readonly string[] PerfNames = { "vr", "lasers", "wrist", "kbd", "settings", "desktops", "dash+osc" };
    readonly double[] _perfSum = new double[7], _perfMax = new double[7];
    int _perfFrames;
    double _perfStart = -1, _perfWaitSum;

    void Perf(int i, ref double t)
    {
        double n = Clock.Now, ms = (n - t) * 1000;
        _perfSum[i] += ms;
        if (ms > _perfMax[i]) _perfMax[i] = ms;
        t = n;
        long b = GC.GetAllocatedBytesForCurrentThread();
        _allocSum[i] += b - _allocMark;
        _allocMark = b;
    }

    // garbage made per section (main thread), and what the GC did, logged next to the timings
    readonly long[] _allocSum = new long[7];
    long _allocMark, _allocThreadMark, _allocAllMark;
    readonly int[] _gcMark = new int[3];
    TimeSpan _gcPauseMark;

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage,
            QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }
    [DllImport("psapi.dll")] static extern bool GetProcessMemoryInfo(IntPtr process, out PROCESS_MEMORY_COUNTERS_EX c, int size);
    [DllImport("kernel32.dll")] static extern bool GetProcessHandleCount(IntPtr process, out uint count);

    // private bytes, kernel handles, GDI / USER objects: a native leak shows up here first. Asked of this process alone - Process's
    // PrivateMemorySize64 / HandleCount read every process of the PC (12-15 ms on the main loop every 10 s: the spike line, 10-18)
    static string ProcessCounters()
    {
        try
        {
            IntPtr me = GetCurrentProcess();
            bool mem = GetProcessMemoryInfo(me, out var mc, Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX>());
            string ws = mem ? $"{(ulong)mc.WorkingSetSize / 1048576}" : "?", priv = mem ? $"{(ulong)mc.PrivateUsage / 1048576}" : "?";
            string handles = GetProcessHandleCount(me, out uint hc) ? $"{hc}" : "?";
            return $"working set {ws} MB, private {priv} MB, handles {handles}, gdi {GetGuiResources(me, 0)}, user {GetGuiResources(me, 1)}";
        }
        catch { return "counters n/a"; }
    }

    // ---- scheduling: CPU power throttling off, GPU priority up, GPU queue probe ----

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

    // Windows 11 runs window-less background processes (Orbit lives in the tray) with "efficiency"
    // scheduling and may ignore their timer resolution. Opt out of both.
    static void NoPowerThrottling()
    {
        const int ProcessPowerThrottling = 4;
        const uint EXECUTION_SPEED = 0x1, IGNORE_TIMER_RESOLUTION = 0x4;
        var st = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION, StateMask = 0 };
        bool ok = SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref st, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        Log.Info(ok ? "power throttling: off (full speed, timer resolution kept)" : $"power throttling: could not opt out ({Marshal.GetLastWin32Error()})");
    }

    // SetGPUThreadPriority needs the "increase scheduling priority" right (admin); without it the call fails harmlessly.
    // High (7) only while Orbit is in use - a laser on a panel or holding one, the watch in view, theater playing, and 1.5 s after
    // (Erik 10-18: "it must not lower VRChat's frame rate, above all while it is off"): at 7 every capture copy and panel draw jumped
    // ahead of the game's frame and broke into it; while nothing is used, Orbit's few jobs wait their turn (0) like any app's.
    Vortice.DXGI.IDXGIDevice _dxgi;
    int _gpuPrio = -1;
    double _gpuBusyUntil;
    bool _gpuPrioFailed;

    void ApplyGpuPriority(int want = 0)
    {
        if (!C.General.HighGpuPriority) want = 0;
        if (want == _gpuPrio || _gpuPrioFailed) return;
        try
        {
            _dxgi ??= _gpu.Device.QueryInterface<Vortice.DXGI.IDXGIDevice>();
            var hr = _dxgi.SetGPUThreadPriority(want);
            if (_gpuPrio < 0 || !hr.Success) Log.Info($"GPU priority {want}: {(hr.Success ? "ok (7 while Orbit is in use, else 0)" : hr.ToString())}");
            if (!hr.Success) _gpuPrioFailed = true;
            _gpuPrio = want;
        }
        catch (Exception e) { Log.Warn("GPU priority: " + e.Message); _gpuPrioFailed = true; }
    }

    // each frame: in use now? (the decision's own cost: a few flag tests; the call itself only on a change)
    void UpdateGpuPriority(double now)
    {
        bool busy = _lasers[0].Active || _lasers[1].Active || _theater.On || (_wrist?.Alpha ?? 0) > 0.02f;
        if (busy) _gpuBusyUntil = now + 1.5;
        ApplyGpuPriority(now < _gpuBusyUntil ? 7 : 0);
    }

    // An event query after each frame's work: if it is still pending one loop later, Orbit's GPU work
    // (panel drawing, capture copies, texture hand-over) had to wait behind the game.
    Vortice.Direct3D11.ID3D11Query _gpuProbe;
    bool _gpuProbePending;
    int _gpuLate, _gpuProbes;

    void GpuProbeCheck()
    {
        if (_gpuProbe == null || !_gpuProbePending) return;
        _gpuProbes++;
        if (_gpu.Ctx.GetData(_gpuProbe, IntPtr.Zero, 0, Vortice.Direct3D11.AsyncGetDataFlags.DoNotFlush).Code != 0) _gpuLate++;
        _gpuProbePending = false;
    }

    long _probeTex = -1;

    void GpuProbeIssue()
    {
        // only a frame that gave SteamVR a picture had GPU work to hand over (a draw, a capture copy, an upload) - an idle frame
        // sent the GPU an empty submission 90 times a second, at the high priority
        long tex = VrOverlay.Calls.Sent[VrOverlay.Calls.Texture];
        if (tex == _probeTex) return;
        _probeTex = tex;
        try
        {
            _gpuProbe ??= _gpu.Device.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.Event));
            _gpu.Ctx.End(_gpuProbe);
            _gpu.Ctx.Flush();          // hand this frame's work to the GPU now, not at some later implicit flush
            _gpuProbePending = true;
        }
        catch (Exception e) { Log.Warn("gpu probe: " + e.Message); _gpuProbe = null; }
    }

    long _lastGcLogged;

    void MemMarks()
    {
        _allocThreadMark = GC.GetAllocatedBytesForCurrentThread();
        _allocAllMark = GC.GetTotalAllocatedBytes();
        for (int g = 0; g < 3; g++) _gcMark[g] = GC.CollectionCount(g);
        _gcPauseMark = GC.GetTotalPauseDuration();
    }

    void PerfFrame(double now)
    {
        _perfFrames++;
        if (_perfStart < 0) { _perfStart = now; MemMarks(); return; }
        if (now - _perfStart < 10) return;
        double secs = now - _perfStart;
        var sa = new System.Text.StringBuilder();
        for (int i = 0; i < PerfNames.Length; i++) sa.Append($" {PerfNames[i]} {_allocSum[i] / _perfFrames}");
        Log.Info($"mem: main thread {(GC.GetAllocatedBytesForCurrentThread() - _allocThreadMark) / secs / 1024:0.0} KB/s, " +
                 $"all threads {(GC.GetTotalAllocatedBytes() - _allocAllMark) / secs / 1024:0.0} KB/s, " +
                 $"GC {GC.CollectionCount(0) - _gcMark[0]}/{GC.CollectionCount(1) - _gcMark[1]}/{GC.CollectionCount(2) - _gcMark[2]} " +
                 $"({(GC.GetTotalPauseDuration() - _gcPauseMark).TotalMilliseconds:0.0} ms pause), heap {GC.GetGCMemoryInfo().HeapSizeBytes / 1048576.0:0.0} MB, " +
                 $"{ProcessCounters()} | bytes/loop:{sa}");      // (Environment.WorkingSet: no - it may read every process too)
        var gi = GC.GetGCMemoryInfo(GCKind.Any);
        if (gi.Index > 0 && gi.Index != _lastGcLogged)
            Log.Info($"gc: last #{gi.Index} gen{gi.Generation}{(gi.Concurrent ? " background" : "")}{(gi.Compacted ? " compacting" : "")}, " +
                     $"pause {gi.PauseDurations[0].TotalMilliseconds:0.0} ms, promoted {gi.PromotedBytes / 1024} KB, " +
                     $"load {gi.MemoryLoadBytes * 100 / Math.Max(1, gi.TotalAvailableMemoryBytes)} %, finalization pending {gi.FinalizationPendingCount}");
        _lastGcLogged = gi.Index;
        Array.Clear(_allocSum);
        MemMarks();
        var sb = new System.Text.StringBuilder();
        double total = 0;
        for (int i = 0; i < PerfNames.Length; i++)
        {
            total += _perfSum[i];
            sb.Append($" {PerfNames[i]} {_perfSum[i] / _perfFrames:0.00}/{_perfMax[i]:0.0}");
        }
        string rays = RayStats.TakeLine();
        if (rays != null) Log.Info(rays);
        string gpuq = _gpuProbes > 0 ? $", gpu late {100.0 * _gpuLate / _gpuProbes:0.0}%" : "";
        _gpuLate = _gpuProbes = 0;
        Log.Info($"perf: {_perfFrames / (now - _perfStart):0} loops/s{CompositorRates(now - _perfStart)}, work {total / _perfFrames:0.00} ms, wait {_perfWaitSum / _perfFrames:0.0} ms{gpuq} | avg/max ms:{sb}");
        if (Spikes.Take() is { } spike) Log.Info(spike);        // where the window's slowest frame went (Sys/Spikes.cs)
        if (_vr.Connected) Log.Info(ShowLine(now - _perfStart));
        Array.Clear(_perfSum); Array.Clear(_perfMax);
        _perfFrames = 0; _perfWaitSum = 0; _perfStart = now;
    }

    bool _waitWarned;

    // what the game and the compositor managed meanwhile, next to the loop's own rate (the loop runs at the headset's
    // rate on its own clock; the compositor can only show as many frames as it presents)
    Compositor_CumulativeStats _cstats;
    uint _cSubmits, _cPresents;

    string CompositorRates(double secs)
    {
        try
        {
            var c = OpenVR.Compositor;
            if (c == null || secs <= 0) return "";
            c.GetCumulativeStats(ref _cstats, (uint)Marshal.SizeOf<Compositor_CumulativeStats>());
            uint s = _cstats.m_nNumFrameSubmits, p = _cstats.m_nNumFramePresents;
            string r = _cPresents == 0 || p < _cPresents ? "" : $" (game {(s - _cSubmits) / secs:0}, compositor {(p - _cPresents) / secs:0} fps)";
            uint re = _cstats.m_nNumReprojectedFrames, dr = _cstats.m_nNumDroppedFrames;
            (_reproDelta, _dropDelta) = _cPresents == 0 || re < _cRepro || dr < _cDropped ? (0, 0) : (re - _cRepro, dr - _cDropped);
            _cRepro = re; _cDropped = dr;
            _cSubmits = s; _cPresents = p;
            return r;
        }
        catch { return ""; }
    }

    // What the overlays got meanwhile, to tell the game's share from Orbit's (Erik 10-19: "azt veszem eszre hogyha meg van nyitva a
    // steamvr overlay is, a mi overlayunk sokkal jobb fps-el renderelodik"): the share of the loops the SteamVR dashboard was open, the
    // compositor's reprojected / dropped frames, how late the loop woke after its slot (p99, max), the monitors' new pictures and the
    // textures handed to SteamVR, the share at GPU priority 7. Read beside the perf line (dashboard open vs closed).
    uint _cRepro, _cDropped, _reproDelta, _dropDelta;
    int _dashLoops, _prioLoops, _lateN, _texMark;
    readonly float[] _lateMs = new float[4096];
    long _capMark;

    string ShowLine(double secs)
    {
        Array.Sort(_lateMs, 0, _lateN);
        float p99 = _lateN == 0 ? 0 : _lateMs[Math.Min(_lateN - 1, (int)(_lateN * 0.99))], max = _lateN == 0 ? 0 : _lateMs[_lateN - 1];
        long caps = 0;
        int shown = 0;
        foreach (var d in _desktops) { caps += d.CaptureFrames; if (d.Shown) shown++; }
        int tex = VrOverlay.Calls.Sent[VrOverlay.Calls.Texture];
        long newCaps = caps >= _capMark ? caps - _capMark : caps;
        int newTex = tex >= _texMark ? tex - _texMark : tex;
        int loops = Math.Max(1, _perfFrames);
        string s = $"show: dashboard open {100.0 * _dashLoops / loops:0}%, compositor reprojected {_reproDelta / secs:0}/s dropped {_dropDelta / secs:0}/s, " +
                   $"loop woke late p99 {p99:0.0} max {max:0.0} ms, monitors shown {shown} with {newCaps / secs:0} new pictures/s, " +
                   $"textures to SteamVR {newTex / secs:0}/s, GPU priority high {100.0 * _prioLoops / loops:0}%";
        _capMark = caps; _texMark = tex; _lateN = 0; _dashLoops = _prioLoops = 0;
        return s;
    }

    int _tickErrors;
    double _nextErrorLog, _nextOsc;

    // A frame that throws is logged and skipped instead of ending the app in the middle of VR.
    // A lost GPU device (driver reset / update) can't recover in place, so Orbit restarts itself.
    // frames from inside Windows' modal loop while the settings window is moved (SettingsWindow.ModalTick); never one inside another
    bool _inTick;
    void ModalTick() { if (!_inTick) SafeTick(); }

    void SafeTick()
    {
        _inTick = true;
        try
        {
            Tick();
            _tickErrors = 0;
        }
        catch (Exception e)
        {
            _tickErrors++;
            if (Now >= _nextErrorLog)
            {
                _nextErrorLog = Now + 10;
                Log.Error($"frame failed ({_tickErrors}x in a row): {e}");
            }
            bool removed = false;
            try { removed = _gpu.Device.DeviceRemovedReason.Failure; } catch { removed = true; }
            if (removed || _tickErrors > 900)
            {
                Log.Error(removed ? "GPU device lost - restarting Orbit" : "frames keep failing - restarting Orbit");
                RestartSelf();
            }
        }
        finally { _inTick = false; }
    }

    void RestartSelf()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath, (_startHidden ? "--steamvr " : "") + "--restart --from-task")
            { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
        }
        catch (Exception e) { Log.Error("restart: " + e.Message); }
        _quit = true;
    }

    void Tick()
    {
        double now = Now;
        float dt = (float)Math.Clamp(now - _last, 0, 0.1);
        _last = now;

        if (!_vr.Connected && now >= _nextConnect)
        {
            _nextConnect = now + 3;
            TryConnectVr();
        }
        if (now >= _nextMonitorCheck)
        {
            _nextMonitorCheck = now + 5;
            // the full query (QueryDisplayConfig and a device-info call a display: some drivers take ms, on the VR loop's thread) only
            // when the screens' places changed - Windows keeps those for free - or once a minute (a monitor swapped in the same place)
            if (ScreensMoved() || now >= _nextMonitorFull)
            {
                _nextMonitorFull = now + 60;
                SyncMonitors(force: false);
            }
            // a monitor that dropped out for a moment (link retrain, power cycle) keeps the same layout,
            // so the check above sees nothing - but its capture has ended: rebuild (at most every 30 s)
            if (now >= _nextDeadRebuild && _desktops.Any(d => d.CaptureDead))
            {
                _nextDeadRebuild = now + 30;
                Log.Info("a monitor capture ended - rebuilding the monitor panels");
                SyncMonitors(force: true);
            }
        }

        if (_vr.Connected)
        {
            var ev = new VREvent_t();
            while (_vr.Connected && _vr.PollEvent(ref ev)) HandleEvent(ev);
            if (_vr.Connected && now >= _nextRuntimeCheck)
            {
                _nextRuntimeCheck = now + 2;
                if (!VrSession.RuntimeRunning())
                {
                    DisconnectVr(false);
                    if (_startHidden) _quit = true;   // started by SteamVR and SteamVR is gone (crash / killed)
                }
            }
        }

        if (_vr.Connected)
        {
            GpuProbeCheck();
            double t = Clock.Now;
            Spikes.Begin();
            _allocMark = GC.GetAllocatedBytesForCurrentThread();
            _vr.Idle = VrOverlay.ShownCount == 0 && !_vr.Suppress[0] && !_vr.Suppress[1];     // nothing of Orbit's in view (VrSession.Idle)
            _vr.Update();
            // the look: the eyes while they are tracked, else the head (VrSession.Gaze)
            _eyes.Update(now, C.General.EyeTracking && _vr.HmdValid);
            _vr.EyesValid = _eyes.Valid && _vr.HmdValid;
            _vr.Gaze = _vr.EyesValid ? _eyes.WorldDir(_vr.Hmd) : MathUtil.Forward(_vr.Hmd);
            _vr.GazeOrigin = _vr.EyesValid ? _eyes.WorldOrigin(_vr.Hmd) : _vr.Hmd.Translation;
            bool watched = (_wrist?.Alpha ?? 0) > 0.02f;
            _data.Vr.Watched = watched;                 // the batteries every second while the watch shows, else every 5 s
            _gpuStats.Active = watched;                 // the GPU counters (a few ms a sample) only while the watch can show them
            _data.Vr.Update(_vr, now);
            FirstUseIfPending();
            if (_warmAt > 0 && now >= _warmAt) WarmAgain();
            if (_wrist.WantsFrames) _data.Vr.SampleFrame(_vr);
            PlaceMissing();
            bool dash = _vr.Ov.IsDashboardVisible();
            if (dash) _dashLoops++;
            if (_gpuPrio == 7) _prioLoops++;
            if (C.General.QuickToggle) CheckQuickToggle(now);
            Perf(0, ref t);
            Spikes.Mark(Spikes.Vr);
            // the Windows cursor's shape: only while a laser points at a panel (nothing else shows it; Vr/SystemCursor.cs)
            if (_lasers[0].Active || _lasers[1].Active) _sysCursor?.Update(now);
            Spikes.Mark(Spikes.Cursor);
            var targets = Targets();
            string lh = C.General.LaserHand;
            _lasers[0].Smoothing = _lasers[1].Smoothing = C.General.LaserSmoothing;
            Laser.RaycastMode = C.General.Raycast is "steamvr" or "local" ? C.General.Raycast : "shadow";
            string eyeCursor = C.General.EyeTracking ? C.General.EyeCursor : "off";       // the eye cursor (Vr/Laser.cs)
            Laser.EyeWarp = eyeCursor == "click";
            Laser.EyeFollow = eyeCursor == "follow";
            Laser.PopOut = _popOut ??= PopOutWindow;                         // a window pulled off a monitor (OrbitApp.PopOut.cs)
            RestoreDueWindow(now);
            foreach (var la in _lasers)
            {
                la.Thickness = Math.Clamp(C.Appearance.LaserThickness, 1f, 10f) / 1000f;
                la.AlwaysOn = C.Appearance.LaserAlways;
            }
            bool awake = _vr.HeadsetWorn || !C.General.PauseWhenOff;
            _awake = awake;
            _lasers[0].Update(_vr, targets, awake && !dash && lh != "right", dt, C.General.Haptics);
            Spikes.Mark(Spikes.LaserRest);
            _lasers[1].Update(_vr, targets, awake && !dash && lh != "left", dt, C.General.Haptics);
            Spikes.Mark(Spikes.LaserRest);
            UpdateExtras(now, awake, dash);     // B / Y twice (before the block moves on), alerts, per-game rules (OrbitApp.Extras.cs)
            BlockGameInput(awake && !dash, now);
            foreach (var la in _lasers) if (la.Hovered is ITheaterScreen sc) _lastScreen = sc;
            Perf(1, ref t);
            Spikes.Mark(Spikes.Extras);
            _wrist.Update(_vr, now, dt, _data);
            Perf(2, ref t);
            Spikes.Mark(Spikes.Watch);
            _kb.Update(_vr, now);
            Perf(3, ref t);
            Spikes.Mark(Spikes.Keyboard);
            _settingsPanel.Update(_vr, now, dt);
            UpdateDock(now, dt);
            UpdateRoom(now, dt);
            // after an update: what changed, in front of you once the headset is on (a moment after connecting)
            _whatsNewPanel.Update(_vr, dt, WhatsNewList, WhatsNewSub,
                awake && _vr.HeadsetWorn && _data.VrSince is { } since && (DateTime.Now - since).TotalSeconds > 3);
            Perf(4, ref t);
            Spikes.Mark(Spikes.Settings);
            _screens.Clear();
            foreach (var d in _desktops) _screens.Add(d);           // (AddRange of another element type boxed an enumerator each time)
            foreach (var w in _windowPanels) _screens.Add(w);
            bool theaterWas = _theater.On;
            _theater.AmbilightOn = C.Theater.Ambilight;
            _theater.Update(_vr, dt, C.Theater.Dim, _screens);
            if (theaterWas && !_theater.On) StopTheater(glide: false);      // its screen went away
            if (_theater.View is { } tv && tv.Size != C.Theater.Width) { C.Theater.Width = tv.Size; _cfg.MarkSoftDirty(); }
            UpdateAutoTheater(now, awake);      // a video in full screen starts / ends theater by itself (OrbitApp.AutoTheater.cs)
            UpdateTheaterBar(now, dt);
            // headset off: no capturing (the overlays keep their last picture)
            if (awake)
            {
                // gaze focus (Panels/GazeFocus.cs): which monitor of a row is drawn full size this frame - held while a screen plays or is
                // on its way in / out, and while a hand holds a mouse button (a drag across screens)
                bool busy = _theater.On;
                if (!busy) foreach (var d in _desktops) if (d.Theater != null) { busy = true; break; }
                long gf = System.Diagnostics.Stopwatch.GetTimestamp();
                _gazeFocus.Update(_vr, now, dt, C.Focus, busy, _mouse.Held, _lasers[0].Hovered, _lasers[1].Hovered);
                Spikes.Piece("gaze focus", gf);
                bool dimLook = C.General.DimUnlooked && _vr.EyesValid;
                foreach (var d in _desktops) { d.SmartCapture = C.General.SmartCapture; d.DimUnlooked = dimLook; d.Update(_vr, now, C.General.DesktopsVisible, C.General.CaptureFps); }
                foreach (var w in _windowPanels) { w.DimUnlooked = dimLook; w.Update(_vr, now, C.General.DesktopsVisible, C.General.CaptureFps, C.General.SmartCapture); }
                if (now >= _nextWindowScan) { _nextWindowScan = now + 2; ScanWindows(); }
            }
            Perf(5, ref t);
            Spikes.Mark(Spikes.Desktops);
            long rp = System.Diagnostics.Stopwatch.GetTimestamp();
            UpdateDashboard(now, dash);
            Spikes.Piece("rest: dashboard tab", rp); rp = System.Diagnostics.Stopwatch.GetTimestamp();
            _notifyPanel.Update(_notifyIn, now, dt);
            Spikes.Piece("rest: notification cards", rp); rp = System.Diagnostics.Stopwatch.GetTimestamp();
            AutoKeyboard(now);
            Spikes.Piece("rest: keyboard pop-up", rp); rp = System.Diagnostics.Stopwatch.GetTimestamp();
            // night mode fades in / out over a moment
            float dimWant = C.Appearance.NightMode ? Math.Clamp(C.Appearance.NightBrightness, 0.1f, 1f) : 1f;
            _dimNow = Motion.Any ? _dimNow + (dimWant - _dimNow) * Math.Min(1f, dt * 8) : dimWant;      // Motion off: at once
            if (MathF.Abs(dimWant - _dimNow) < 0.004f) _dimNow = dimWant;
            VrOverlay.SetDim(_dimNow);
            if (C.General.Osc && now >= _nextOsc)       // 10 Hz is plenty for avatar parameters
            {
                _nextOsc = now + 0.1;
                SendOsc(now);
            }
            Spikes.Piece("rest: osc", rp); rp = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now >= _nextStatus)
            {
                _nextStatus = now + 1;
                var m = _media.Snapshot();
                _status.Update(now, C.Chatbox.StatusLine, C.General.OscPort,
                    StatusLine.Compose(C.Chatbox, (m.has, m.title, m.artist, m.playing), DateTime.Now, _data.VrSince, C.Appearance.Clock24h));
            }
            Spikes.Piece("rest: status line", rp); rp = System.Diagnostics.Stopwatch.GetTimestamp();
            UpdateGpuPriority(now);
            Spikes.Piece("rest: gpu priority", rp);
            Perf(6, ref t);
            Spikes.Mark(Spikes.Rest);
            GpuProbeIssue();
            PerfFrame(now);
            Spikes.Mark(Spikes.PerfLog);
        }

        long ap = System.Diagnostics.Stopwatch.GetTimestamp();       // (each watcher's time: the spike line's slowest piece, Sys/Spikes.cs)
        _notifyIn.SetEnabled(C.General.Notifications && _vr.Connected);
        _media.Active = _vr.Connected;
        WatchWidgets.DockAvailable = C.Dock.Enabled && _hexpad.Present;     // the editor offers the dock widget (also on the desktop)
        _room.Active = _vr.Connected && C.Room.Enabled;
        _data.Room = C.Room.Enabled ? _roomSnap : null;
        Spikes.Piece("app: notifications, media, room", ap); ap = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdatePhotos(now);                  // VRChat's photos for the latest photo widget (OrbitApp.Photos.cs)
        Spikes.Piece("app: photos", ap); ap = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateSocial(now);                  // VRCX's friends online, the session and its recap (OrbitApp.Social.cs)
        Spikes.Piece("app: friends + recap (VRCX)", ap); ap = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateMemories(now);                // this day's photos of earlier years, On this day (OrbitApp.Memories.cs)
        Spikes.Piece("app: on this day", ap); ap = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateTemps(now);                   // the temperatures widget / the hot alert's sensors (OrbitApp.Temps.cs)
        Spikes.Piece("app: temperatures", ap); ap = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateAvatar(now);                  // the VRChat avatar's toggles (OrbitApp.Avatar.cs)
        Spikes.Piece("app: avatar", ap); ap = System.Diagnostics.Stopwatch.GetTimestamp();
        // the weather: the place from the settings (else Hexpad's), fetched while a weather widget is on one of the pages
        _weather.SetLocation(C.Weather?.Location);
        _weather.Active = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "weather");
        _data.Weather = _weather.Report;
        Spikes.Piece("app: weather", ap); ap = System.Diagnostics.Stopwatch.GetTimestamp();
        // the light and the RGB: their apps asked while their widget is on one of the pages (the bulb read again when the watch shows)
        TapoAuto();
        _lumos.SetKey(C.Lights?.Key);
        _lumos.SetBulb(C.Lights?.Bulb);
        _lumos.Active = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "light");
        _lumos.Watched = _wrist?.Alpha > 0.02f;
        _prisma.Active = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "rgb");
        // Mixtape: linked while a page has Now playing (Mixtape's song and player in it) and SteamVR runs (or the settings preview shows
        // it), faster while the watch shows it
        _mixtape.Active = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "media") && (_vr.Connected || now - _photoPreview < 3);
        _mixtape.Watched = _wrist?.Alpha > 0.02f;
        _mixtape.Pump();
        Spikes.Piece("app: light + rgb", ap);
        _data.Lamp = _lumos;
        _data.Rgb = _prisma;
        _data.StatusLine = C.Chatbox.StatusLine;
        _data.Theater = _theater.On;
        _data.VrConnected = _vr.Connected;
        if (_vr.Connected) _data.VrSince ??= DateTime.Now; else _data.VrSince = null;
        _data.DesktopsVisible = C.General.DesktopsVisible;
        _data.NightMode = C.Appearance.NightMode;
        _data.LayoutName = C.ActiveLayout;
        _data.KeyboardVisible = C.Keyboard.Visible;
        Spikes.Mark(Spikes.App);
        // the output device's apps follow the Windows volume while SteamVR runs (a device that ignores its own); the mixer's list
        AudioMixer.Mode = C.General.VolumeToApps is "on" or "off" ? C.General.VolumeToApps : "auto";
        _mixer.Pump(now, _vr.Connected);   // (its Core Audio work on a worker of its own: Sys/AudioMixer.cs)
        Spikes.Mark(Spikes.Mixer);
        long wt = System.Diagnostics.Stopwatch.GetTimestamp();
        _win.Tick(now);
        Spikes.Piece("the settings window", wt); wt = System.Diagnostics.Stopwatch.GetTimestamp();
        _cfg.SaveIfDue();
        Spikes.Piece("config save", wt);
        Spikes.Mark(Spikes.Window);
        Spikes.End();                       // (the frame's parts kept if it was the window's slowest: Sys/Spikes.cs)
    }

    // A hand whose laser points at / holds an Orbit panel keeps its buttons from the game (no stray VRChat clicks).
    // It is let go only once that hand releases trigger, grip and B / Y: a button still held when the beam leaves
    // the panel must not reach the game as a fresh press.
    // It also lingers 1.5 s after the beam leaves a monitor or window: the game gets the input back once, when you are
    // done, not every time the beam crosses the gap between two screens (VRChat re-centres its cursor when it gets it
    // back). Not after the watch, keyboard and the small panels (0.3 s), and not at all once the panel it was on is gone
    // (turned off, faded away): Erik waited seconds for VRChat after switching the overlay off.
    readonly double[] _blockUntil = new double[2];
    readonly IPointerTarget[] _blockOn = new IPointerTarget[2];

    void BlockGameInput(bool on, double now)
    {
        for (int h = 0; h < 2; h++)
        {
            ref var hs = ref _vr.Hands[h];
            bool busy = on && C.General.BlockGameInput && _lasers[h].Active;
            if (busy)
            {
                _vr.Suppress[h] = true;
                var t = _lasers[h].Hovered;
                if (t != null) _blockOn[h] = t;
                _blockUntil[h] = now + (_blockOn[h] is DesktopPanel or WindowPanel && !_lasers[h].EyeCursorOn ? 1.5 : 0.3);   // (the eye cursor: soon)
            }
            else if (_blockOn[h] is { } last && !last.Interactive) { _blockUntil[h] = Math.Min(_blockUntil[h], now); _blockOn[h] = null; }
            else if (_vr.Suppress[h] && (!on || !C.General.BlockGameInput || now >= _blockUntil[h]) && !(hs.Click || hs.Grab || hs.Secondary))
                _vr.Suppress[h] = false;
        }
    }

    List<IPointerTarget> _targets;

    // rebuilt only when the monitor panels change (SyncMonitors clears it)
    List<IPointerTarget> Targets()
    {
        if (_targets != null) return _targets;
        _targets = new List<IPointerTarget>(_desktops.Count + _windowPanels.Count + 4) { _settingsPanel, _dockPanel, _roomPanel, _whatsNewPanel, _wrist, _kb, _theaterBar };
        _targets.AddRange(_windowPanels);
        _targets.AddRange(_desktops);
        return _targets;
    }

    // ---------------- SteamVR connection ----------------

    void TryConnectVr()
    {
        if (!VrSession.RuntimeRunning()) { _vrStatus = "Waiting for SteamVR"; return; }
        if (!_vr.TryConnect(ActionManifest, out string err, AppKey))
        {
            _vrStatus = "SteamVR: " + err;
            Log.Warn("VR init failed: " + err);
            return;
        }
        OnVrConnected();
    }

    void OnVrConnected()
    {
        // the overlay textures must live on the adapter SteamVR renders with
        try
        {
            using var f = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
            if (_vr.AdapterIndex >= 0 && f.EnumAdapters1((uint)_vr.AdapterIndex, out var a).Success)
            {
                if (a.Description1.Luid != _gpu.AdapterLuid)
                    Log.Warn($"SteamVR uses adapter '{a.Description1.Description}', Orbit renders on '{_gpu.AdapterName}' - restart Orbit after SteamVR");
                a.Dispose();
            }
        }
        catch (Exception e) { Log.Warn("adapter check: " + e.Message); }

        _lasers = new[] { new Laser(_gpu, _painter, Hand.Left), new Laser(_gpu, _painter, Hand.Right) };
        _lasers[0].Other = _lasers[1];
        _lasers[1].Other = _lasers[0];
        _sysCursor ??= new SystemCursor(_gpu);
        _lasers[0].Cursor = _lasers[1].Cursor = _sysCursor;
        _wrist.CreateOverlay(_vr);
        _kb.CreateOverlay(_vr);
        // first draws load fonts (~25 ms each): do it now, not when you first look at the watch / keyboard
        try { _wrist.Draw(_data); _kb.Draw(); _settingsPanel.Warm(); } catch (Exception e) { Log.Warn("warm-up draw: " + e.Message); }
        _warmAt = Clock.Now + 8;            // and again once the data is in (WarmAgain)
        _settingsPanel.CreateOverlay(_vr);
        _dockPanel.CreateOverlay(_vr);
        _roomPanel.CreateOverlay(_vr);
        _whatsNewPanel.CreateOverlay(_vr);
        _theater.CreateOverlay();
        _theaterBar.CreateOverlay();
        _notifyPanel.CreateOverlay();
        try { _winrtDev ??= MonitorCapture.CreateWinRtDevice(_gpu); }
        catch (Exception e) { Log.Error("WinRT device: " + e); }
        for (int i = 0; i < _desktops.Count; i++)
        {
            int di = i;
            if (_desktops[di].Cfg.Enabled && _winrtDev != null) SafeCreate(() => _desktops[di].CreateOverlay(_vr, _winrtDev, di), _desktops[di].Cfg.Name);
        }
        foreach (var w in _windowPanels)
            if (w.Cfg.Enabled && _winrtDev != null) SafeCreate(() => w.CreateOverlay(_vr, _winrtDev), w.Cfg.Title);
        _nextWindowScan = 0;
        CreateDashboard();
        ApplyAutoStart();
        _vrStatus = $"SteamVR · {_vr.DisplayHz:0} Hz";
        _win.Dirty = true;
    }

    void CreateDashboard()
    {
        try
        {
            (_dash, _dashThumb) = VrOverlay.CreateDashboard(AppKey + ".settings", "OrbitOverlay");
            _dash.SetWidth(2.5f);
            _vr.Ov.SetOverlayInputMethod(_dash.Handle, VROverlayInputMethod.Mouse);
            var scale = new HmdVector2_t { v0 = DashW, v1 = DashH };
            _vr.Ov.SetOverlayMouseScale(_dash.Handle, ref scale);
            _dashSurf ??= new Surface(_gpu, DashW, DashH);
            _dashView ??= new SettingsView(this);
            _dashUi ??= new Ui(_painter);
            if (_thumbSurf == null)
            {
                _thumbSurf = new Surface(_gpu, 256, 256, rgba: true);
                _painter.Begin(_thumbSurf.Bitmap, 0x00000000);
                _painter.Logo(new System.Drawing.RectangleF(8, 8, 240, 240));
                _painter.End();
            }
            _dashThumb.SetFlag(VROverlayFlags.IsPremultiplied, true);
            _dashThumb.SetTexture(_thumbSurf.Tex);
            _dashDirty = true;
        }
        catch (Exception e) { Log.Error("dashboard: " + e.Message); }
    }

    // dashOpen: SteamVR's dashboard is open (asked once a frame anyway) - closed, its Orbit tab cannot show: no events to read, nothing
    // to draw (two calls to SteamVR a frame spared); a leave it missed is caught up when it opens again
    bool _dashWasOpen;

    void UpdateDashboard(double now, bool dashOpen)
    {
        if (_dash == null) return;
        if (!dashOpen)
        {
            if (_dashWasOpen) { _dashWasOpen = false; _dashPtr.Leave(); _dashDirty = true; }
            return;
        }
        _dashWasOpen = true;
        var ev = new VREvent_t();
        uint size = (uint)Marshal.SizeOf<VREvent_t>();
        while (_vr.Ov.PollNextOverlayEvent(_dash.Handle, ref ev, size))
        {
            switch ((EVREventType)ev.eventType)
            {
                case EVREventType.VREvent_MouseMove:
                    _dashPtr.Move(ev.data.mouse.x, VrOverlay.UvFlipV ? DashH - ev.data.mouse.y : ev.data.mouse.y);
                    _dashDirty = true;
                    break;
                case EVREventType.VREvent_MouseButtonDown:
                    if (ev.data.mouse.button == (uint)EVRMouseButton.Left) { _dashPtr.Press(); _dashDirty = true; }
                    break;
                case EVREventType.VREvent_MouseButtonUp:
                    if (ev.data.mouse.button == (uint)EVRMouseButton.Left) { _dashPtr.Release(); _dashDirty = true; }
                    break;
                case EVREventType.VREvent_ScrollDiscrete:
                case EVREventType.VREvent_ScrollSmooth:
                    _dashPtr.Wheel += ev.data.scroll.ydelta;
                    _dashDirty = true;
                    break;
                case EVREventType.VREvent_FocusLeave:
                    _dashPtr.Leave();
                    _dashDirty = true;
                    break;
            }
        }
        if (!_vr.Ov.IsOverlayVisible(_dash.Handle)) return;
        if (!_dashDirty && now < _dashNext) return;
        _dashDirty = false;
        _dashNext = now + 0.5;
        PreparePreviews(_dashView);
        AnimWake.Take();             // the previews' asks are their own panels', not the dashboard's
        DrawDashboard();
        _dashNext = Math.Min(_dashNext, AnimWake.Take());       // a style's animation wants its next frame (AnimWake.cs)
        if (_dashUi.Interacting || _dashUi.Changed || _dashUi.Animating) _dashDirty = true;
        _dash.SetTexture(_dashSurf.Tex);
    }

    void DrawDashboard()
    {
        if (Theme.Classic)
            _dashView.Render95(_dashUi, _dashPtr, _dashSurf.Bitmap, DashW / SettingsView.W95W, 0, 0, Theme.W95Teal);
        else
        {
            float s = DashW / SettingsView.W;
            _dashUi.Begin(_dashPtr, s);
            _painter.Begin(_dashSurf.Bitmap, Theme.Bg, s);
            _dashView.Draw(_dashUi);
            _painter.End();
            _dashUi.End();
        }
    }

    void HandleEvent(in VREvent_t ev)
    {
        if ((EVREventType)ev.eventType == EVREventType.VREvent_Quit)
        {
            Log.Info("SteamVR is quitting");
            DisconnectVr(true);
            if (_startHidden) _quit = true;   // started by SteamVR -> leave with it
        }
    }

    void DisconnectVr(bool acknowledgeQuit)
    {
        if (_lasers != null) foreach (var l in _lasers) { l.Reset(); l.Dispose(); }
        _lasers = null;
        _mouse.Hand = null;
        _mouse.Panel = null;
        _wrist.DestroyOverlay();
        _kb.DestroyOverlay();
        _settingsPanel.DestroyOverlay();
        _dockPanel.DestroyOverlay();
        _roomPanel.DestroyOverlay();
        _whatsNewPanel.DestroyOverlay();
        ExtrasReset();
        AutoTheaterReset();
        if (_theater.On) StopTheater(glide: false);
        _theater.DestroyOverlay();
        _theaterBar.DestroyOverlay();
        _status.Clear(C.General.OscPort, Clock.Now);
        _notifyPanel.DestroyOverlay();
        foreach (var d in _desktops) d.DestroyOverlay();
        foreach (var w in _windowPanels) w.DestroyOverlay();
        _dash?.Dispose(); _dashThumb?.Dispose();
        _dash = _dashThumb = null;
        if (C.General.Osc) SendOsc(Now, allOff: true);
        _vr.Disconnect(acknowledgeQuit);
        _data.Vr = new VrStats();
        _vrStatus = "Waiting for SteamVR";
        _nextConnect = Now + 5;
        _win.Dirty = true;
    }

    // ---------------- monitors ----------------

    void SyncMonitors(bool force)
    {
        List<MonitorInfo> mons;
        try { mons = Monitors.Enumerate(); }
        catch (Exception e) { Log.Warn("monitor enum: " + e.Message); return; }
        string key = string.Join("|", mons.Select(m => $"{m.Id}@{m.X},{m.Y},{m.W},{m.H}"));
        if (!force && key == _monitorKey) return;
        _monitorKey = key;
        Log.Info("monitors: " + string.Join("; ", mons));

        if (_lasers != null) foreach (var l in _lasers) l.Reset();
        _mouse.Hand = null;
        _mouse.Panel = null;
        foreach (var d in _desktops) d.Dispose();
        _desktops.Clear();
        _targets = null;
        _monitors = mons;
        foreach (var m in mons)
        {
            var dc = C.Desktops.FirstOrDefault(d => d.Id == m.Id);
            if (dc == null)
            {
                dc = new DesktopConfig { Id = m.Id, Name = m.Name, Enabled = !m.Virtual };
                C.Desktops.Add(dc);
                _cfg.MarkDirty();
            }
            dc.Name = m.Name;
            _desktops.Add(new DesktopPanel(_gpu, _cfg, _mouse, m, dc) { Layout = _layout });
        }
        if (_vr.Connected && _winrtDev != null)
            for (int i = 0; i < _desktops.Count; i++)
            {
                int di = i;
                if (_desktops[di].Cfg.Enabled) SafeCreate(() => _desktops[di].CreateOverlay(_vr, _winrtDev, di), _desktops[di].Cfg.Name);
            }
        if (_win != null) _win.Dirty = true;
    }

    // ---------------- placement ----------------

    void PlaceMissing()
    {
        if (!_vr.HmdValid) return;
        // runs every frame: look first without building lists (they were garbage 80 times a second)
        bool anyMissing = false;
        foreach (var d in _desktops) if (d.Cfg.Enabled && d.Cfg.Place.M == null) { anyMissing = true; break; }
        if (anyMissing) PlaceMissingDesktops();
        if (C.Keyboard.Visible && C.Keyboard.Place.M == null) PlaceKeyboard();
        foreach (var w in _windowPanels)
            if (w.Cfg.Enabled && w.Cfg.Place.M == null) PlaceWindow(w);
    }

    void PlaceMissingDesktops()
    {
        var enabled = _desktops.Where(d => d.Cfg.Enabled).ToList();
        var missing = enabled.Where(d => d.Cfg.Place.M == null).ToList();
        if (missing.Count > 0 && missing.Count == enabled.Count)
            _layout.JoinAll(MathUtil.GazeFrame(_vr.Hmd), RowRadius, -0.12f);     // first run: one curved screen
        else if (missing.Count > 0)
            PlaceDesktops(missing);
    }

    // a new window goes in front of you, a little closer than the monitors; several step aside
    // forceWorld: a new window / Reset position. Otherwise a hand or head attachment the user chose is
    // kept; a hand that isn't tracked yet just leaves it for the next frame.
    void PlaceWindow(WindowPanel w, bool forceWorld = false)
    {
        if (!_vr.HmdValid) return;
        int k = Math.Max(0, _windowPanels.IndexOf(w)) % 4;
        var local = Matrix4x4.CreateTranslation(0.1f * k, -0.05f - 0.04f * k, -0.8f + 0.03f * k);
        var world = local * MathUtil.GazeFrame(_vr.Hmd);
        if (forceWorld || w.Cfg.Place.Attach is null) w.Cfg.Place.Attach = "world";
        Placement.SetWorld(w.Cfg.Place, _vr, world);
        if (w.Cfg.Place.M == null)
        {
            if (w.Cfg.Place.Attach is "left" or "right") return;
            w.Cfg.Place.Attach = "world";
            Placement.SetWorld(w.Cfg.Place, _vr, world);
        }
        _cfg.MarkDirty();
    }

    const float RowRadius = 1.2f;

    // The monitors on an arc around the head, laid out like the real desk (left / middle / right).
    void PlaceDesktops(IEnumerable<DesktopPanel> which)
    {
        if (!_vr.HmdValid) return;
        var yaw = MathUtil.GazeFrame(_vr.Hmd);
        var enabled = _desktops.Where(d => d.Cfg.Enabled).ToList();
        if (enabled.Count == 0) return;
        var anchor = enabled.FirstOrDefault(d => d.Mon.Primary) ?? enabled[enabled.Count / 2];
        float cx0 = anchor.Mon.X + anchor.Mon.W / 2f, cy0 = anchor.Mon.Y + anchor.Mon.H / 2f;
        const float R = 1.2f;
        foreach (var d in which)
        {
            float s = d.Cfg.Width / d.Mon.W;
            float xm = (d.Mon.X + d.Mon.W / 2f - cx0) * s * 1.03f;
            float ym = -(d.Mon.Y + d.Mon.H / 2f - cy0) * s;
            float ang = xm / R;
            var local = Matrix4x4.CreateRotationY(-ang) *
                        Matrix4x4.CreateTranslation(R * MathF.Sin(ang), ym - 0.12f, -R * MathF.Cos(ang));
            Placement.SetWorld(d.Cfg.Place, _vr, local * yaw);
            if (d.Cfg.Place.M == null) { d.Cfg.Place.Attach = "world"; Placement.SetWorld(d.Cfg.Place, _vr, local * yaw); }
        }
        _cfg.MarkDirty();
    }

    // Recenter: the keyboard in the middle under the monitors - on the line from your eyes to just under the lowest monitor's bottom
    // edge (measured below the middle one) and the bar that shows under it, at arm's length (a laser types easily there), facing
    // you. In theater under the big screen and its bar if that is lower (Erik 10-17: it hung into the bar).
    const float KbReach = 0.9f;

    void PlaceKeyboardUnder(DesktopPanel anchor, List<DesktopPanel> placed)
    {
        var a = MathUtil.FromArray(anchor.Cfg.Place.M);
        var up = MathUtil.Up(a);
        if (up.Length() < 0.1f) { PlaceKeyboard(); return; }
        up = Vector3.Normalize(up);
        var mid = a.Translation;
        var head = _vr.Hmd.Translation;
        float low = 0;
        foreach (var d in placed)
        {
            var m = MathUtil.FromArray(d.Cfg.Place.M);
            float h = d.Cfg.Width * d.Mon.H / Math.Max(1f, d.Mon.W);
            low = Math.Min(low, Vector3.Dot(m.Translation - mid, up) - h / 2 - 1.35f * TheaterBar.HeightAt(Vector3.Distance(head, m.Translation)));
        }
        var dir = mid + up * (low - 0.03f) - head;
        float len = dir.Length();
        if (len < 0.3f) { PlaceKeyboard(); return; }
        dir /= len;
        if (_theater.LowestPoint(head, out var tp))
        {
            var td = tp - head;
            if (td.Length() > 0.3f && Vector3.Dot(Vector3.Normalize(td), up) < Vector3.Dot(dir, up))
            {
                len = td.Length();
                dir = td / len;
                // not steeper than 44° down: a huge big screen must not send it into your lap
                const float MaxDown = 0.7f;
                var flat = dir - Vector3.Dot(dir, up) * up;
                if (Vector3.Dot(dir, up) < -MaxDown && flat.Length() > 0.1f)
                    dir = Vector3.Normalize(flat) * MathF.Sqrt(1 - MaxDown * MaxDown) - up * MaxDown;
            }
        }
        var z = -dir;                                           // facing you
        var y = up - Vector3.Dot(up, z) * z;
        if (y.Length() < 0.1f) { PlaceKeyboard(); return; }
        y = Vector3.Normalize(y);
        var x = Vector3.Cross(y, z);
        var top = head + dir * MathF.Min(len, KbReach);         // the middle of its top edge
        float kh = C.Keyboard.Width * KeyboardPanel.TH / KeyboardPanel.TW;
        if (C.Keyboard.Place.Attach is "head") C.Keyboard.Place.Attach = "world";
        Placement.SetWorld(C.Keyboard.Place, _vr, MathUtil.Basis(x, y, z, top - y * (kh / 2)));
        _cfg.MarkDirty();
    }

    void PlaceKeyboard()
    {
        if (!_vr.HmdValid) return;
        var yaw = MathUtil.GazeFrame(_vr.Hmd);
        var local = Matrix4x4.CreateRotationX(-50 * MathF.PI / 180) * Matrix4x4.CreateTranslation(0, -0.5f, -0.45f);
        if (C.Keyboard.Place.Attach is "head") C.Keyboard.Place.Attach = "world";
        Placement.SetWorld(C.Keyboard.Place, _vr, local * yaw);
        if (C.Keyboard.Place.M == null) { C.Keyboard.Place.Attach = "world"; Placement.SetWorld(C.Keyboard.Place, _vr, local * yaw); }
        _cfg.MarkDirty();
    }

    // ---------------- IOrbitHost ----------------

    public ConfigStore Cfg => _cfg;
    public bool VrConnected => _vr.Connected;
    public string VrStatus => _vrStatus;
    public string InputStatus => _vr.Connected ? _vr.InputStatus : "–";
    public string GpuName => _gpu.AdapterName;
    public IReadOnlyList<MonitorInfo> MonitorList => _monitors;
    public PanelData Data => _data;
    public WristPanel Wrist => _wrist;
    public KeyboardPanel Keyboard => _kb;

    // Brings the whole arrangement (rows, custom spots) in front of the user, keeping its shape:
    // the primary/middle monitor goes straight ahead, everything else moves rigidly with it.
    public void Recenter()
    {
        if (!_vr.Connected || !_vr.HmdValid) return;
        var placed = _desktops.Where(d => d.Cfg.Enabled && Placement.IsWorld(d.Cfg.Place) && d.Cfg.Place.M != null).ToList();
        DesktopPanel kbUnder = null;
        if (placed.Count > 0)
        {
            var anchor = placed.FirstOrDefault(d => d.Mon.Primary) ?? placed.OrderBy(d => d.Mon.X).ElementAt(placed.Count / 2);
            if (anchor.Cfg.Group != null)
            {
                // middle of the row, not one end of it
                var row = _layout.Members(anchor.Cfg.Group);
                if (row.Count > 0) anchor = row[row.Count / 2];
            }
            float r = DesktopLayout.Radius(anchor.Cfg);
            float dist = float.IsInfinity(r) || r > 4 ? RowRadius : r;
            var desired = Matrix4x4.CreateTranslation(0, -0.12f, -dist) * MathUtil.GazeFrame(_vr.Hmd);
            var delta = MathUtil.Invert(MathUtil.FromArray(anchor.Cfg.Place.M)) * desired;
            foreach (var d in placed)
                d.Cfg.Place.M = MathUtil.ToArray(MathUtil.FromArray(d.Cfg.Place.M) * delta);
            kbUnder = anchor;
            foreach (var w in _windowPanels)
                if (Placement.IsWorld(w.Cfg.Place) && w.Cfg.Place.M != null)
                    w.Cfg.Place.M = MathUtil.ToArray(MathUtil.FromArray(w.Cfg.Place.M) * delta);
        }
        else
            foreach (var w in _windowPanels)
                if (Placement.IsWorld(w.Cfg.Place)) PlaceWindow(w);
        var missing = _desktops.Where(d => d.Cfg.Enabled && d.Cfg.Place.M == null).ToList();
        if (missing.Count > 0) PlaceDesktops(missing);
        _theater.Recenter(_vr, TheaterAnchor());            // (before the keyboard: it goes under the big screen too)
        // the keyboard in the middle under the monitors - hidden as well: it comes back where Recenter put things (Erik 10-16); on a
        // hand it stays there
        if (Placement.IsWorld(C.Keyboard.Place) || C.Keyboard.Place.Attach is "head")
        {
            if (kbUnder != null) PlaceKeyboardUnder(kbUnder, placed);
            else PlaceKeyboard();
        }
        _cfg.MarkDirty();
    }

    // Theater mode: the monitor / window you used last (else the main monitor) as one big curved screen in front of
    // you; the other panels and the keyboard step aside and the world dims. The next tap puts everything back.
    public void ToggleTheater()
    {
        if (_theater.On) { StopTheater(glide: true); return; }
        if (!_vr.Connected || !_vr.HmdValid) return;
        var screen = _lastScreen is { Playable: true } last ? last
            : (ITheaterScreen)_desktops.FirstOrDefault(d => d.Mon.Primary && d.Playable) ?? _desktops.FirstOrDefault(d => d.Playable)
              ?? (ITheaterScreen)_windowPanels.FirstOrDefault(w => w.Playable);
        if (screen == null)
        {
            _notifyIn.Incoming.Enqueue(new Notification { Source = "OrbitOverlay", Title = Lang.T("Theater"), Content = Lang.T("There is no monitor or window to show."), Timeout = 4 });
            return;
        }
        StartTheater(screen);
    }

    // theater mode on this screen: the keyboard put away for it (it comes back after); the watch / the bar, and auto theater
    void StartTheater(ITheaterScreen screen)
    {
        _theaterKb = C.Keyboard.Visible;
        if (_theaterKb) { C.Keyboard.Visible = false; _kbAuto = false; _kbHideAt = -1; }
        _theater.Start(screen, TheaterAnchor(), _vr, C.Theater);
        _cfg.MarkDirty();
        _win.Dirty = true;
    }

    // the bar under a screen: under the big screen in theater mode (the switcher), else under the monitor / window
    // whose lower part a laser points at (Theater button + settings)
    ITheaterScreen _barAnchor;
    double _barAnchorAt;

    void UpdateTheaterBar(double now, float dt)
    {
        _theaterChoices.Clear();
        if (_theater.On)
        {
            // monitors left to right as they stand on the desk, then the windows (no garbage: it runs every frame)
            foreach (var d in _desktops)
            {
                if (!d.Playable) continue;
                int at = _theaterChoices.Count;
                while (at > 0 && ((DesktopPanel)_theaterChoices[at - 1]).Mon.X > d.Mon.X) at--;
                _theaterChoices.Insert(at, d);
            }
            foreach (var w in _windowPanels) if (w.Playable) _theaterChoices.Add(w);
        }
        ITheaterScreen pointed = null;
        bool lower = false, onBar = false;
        // outside theater mode the bar belongs to the middle monitor only (Erik: it jumping under whichever monitor the
        // cursor crossed was distracting); the pick is re-read once a second, it allocates
        if (!_theater.On && now >= _barAnchorAt) { _barAnchor = TheaterAnchor(); _barAnchorAt = now + 1; }
        foreach (var la in _lasers)
        {
            // the eye cursor's laser: the hand's own ray decides (the eyes looking low on a monitor never open the bar - Erik 10-19)
            var hov = la.EyeCursorOn ? la.HandScreen : la.Hovered;
            var hpx = la.EyeCursorOn ? la.HandPx : la.HoverPx;
            if (hov == null) continue;
            if (hov == _theaterBar) { onBar = true; continue; }
            if (hov is not ITheaterScreen s) continue;
            if (!_theater.On && s != _barAnchor) continue;
            var ts = s.TextureSize;
            bool low = ts.Y > 0 && hpx.Y / ts.Y > 0.72f;
            if (pointed == null || low) { pointed = s; lower = low; }
        }
        _theaterBar.Update(_vr, now, dt, _theater, _theaterChoices, pointed, lower, onBar);
    }

    readonly List<DesktopPanel> _anchorPlaced = new();

    // the big screen goes in front of the middle of your monitors, whichever one plays (same pick as Recenter) - no LINQ: the bar asks
    // once a second
    ITheaterScreen TheaterAnchor()
    {
        var placed = _anchorPlaced;
        placed.Clear();
        DesktopPanel primary = null;
        foreach (var d in _desktops)
        {
            if (!(d.Cfg.Enabled && Placement.IsWorld(d.Cfg.Place) && d.Cfg.Place.M != null)) continue;
            if (primary == null && d.Mon.Primary) primary = d;
            // left to right, as OrderBy put them (stable: an equal X stays after the one before it)
            int at = placed.Count;
            while (at > 0 && placed[at - 1].Mon.X > d.Mon.X) at--;
            placed.Insert(at, d);
        }
        if (placed.Count == 0) return null;
        var anchor = primary ?? placed[placed.Count / 2];
        if (anchor.Cfg.Group != null)
        {
            var row = _layout.Members(anchor.Cfg.Group);
            if (row.Count > 0) anchor = row[row.Count / 2];
        }
        return anchor;
    }

    void StopTheater(bool glide)
    {
        _theater.Stop(glide ? _vr : null, C.Theater);
        if (_theaterKb && !C.Keyboard.Visible && _vr.Connected) ToggleKeyboard();
        _theaterKb = false;
        _cfg.MarkDirty();
        _win.Dirty = true;
    }

    public void ResetDesktop(DesktopConfig d)
    {
        var p = _desktops.FirstOrDefault(x => x.Cfg == d);
        if (p == null || !_vr.Connected) return;
        _layout.Detach(p);
        d.Place.Attach = "world";
        PlaceDesktops(new[] { p });
    }

    public void ResetWrist() { C.Wrist.Offset = null; _cfg.MarkDirty(); }

    public Updater Updates { get; } = new();

    // ---------------- keyboard pops up for text fields ----------------

    // A click from VR that puts the focus into a text field shows the keyboard; when the focus
    // moves on to something that is not text, the keyboard goes away again (only if it came by itself).
    void AutoKeyboard(double now)
    {
        if (_kbAuto && !C.Keyboard.Visible) _kbAuto = false;      // hidden some other way
        if (_focus == null || !C.Keyboard.AutoShow) { _kbAuto = false; return; }
        if (_kbAuto) _focus.InspectFor(1.0);                       // keep watching while it is up
        var (seq, state, at) = _focus.Snapshot();
        if (seq != _focusSeq)
        {
            _focusSeq = seq;
            if (state == 1)
            {
                _kbHideAt = -1;
                bool fromVr = at - _mouse.LastClick is > -0.05 and < 1.5;
                if (!C.Keyboard.Visible && fromVr)
                {
                    ToggleKeyboard();
                    _kbAuto = true;
                    Log.Info("text field got focus from a VR click: keyboard up");
                }
            }
            else if (state == 0 && _kbAuto)
                _kbHideAt = now + 0.8;
        }
        if (_kbHideAt > 0 && now >= _kbHideAt)
        {
            _kbHideAt = -1;
            // not while typing into the VRChat chatbox or right after a key
            if (_kbAuto && C.Keyboard.Visible && !_kb.ChatMode && now - _kb.LastKeyAt > 0.8)
            {
                ToggleKeyboard();
                Log.Info("focus left the text field: keyboard down");
            }
        }
    }

    // ---------------- layout profiles ----------------

    LayoutProfile ActiveLayout => C.Layouts.FirstOrDefault(l => l.Name == C.ActiveLayout) ?? C.Layouts[0];

    static PlacementConfig CopyPlace(PlacementConfig p) =>
        p == null ? new PlacementConfig() : new PlacementConfig { Attach = p.Attach, M = p.M?.ToArray() };

    void CaptureLayout(LayoutProfile p)
    {
        var list = new List<PanelSnapshot>();
        foreach (var d in C.Desktops)
            list.Add(new PanelSnapshot
            {
                Kind = "desktop", Id = d.Id, Enabled = d.Enabled, Width = d.Width, Curvature = d.Curvature, Opacity = d.Opacity,
                Place = CopyPlace(d.Place), Group = d.Group, GroupIndex = d.GroupIndex,
            });
        foreach (var w in C.Windows)
            list.Add(new PanelSnapshot
            {
                Kind = "window", Id = w.Id, Enabled = w.Enabled, Width = w.Width, Curvature = w.Curvature, Opacity = w.Opacity,
                Place = CopyPlace(w.Place),
            });
        list.Add(new PanelSnapshot { Kind = "keyboard", Id = "keyboard", Width = C.Keyboard.Width, Place = CopyPlace(C.Keyboard.Place) });
        p.Panels = list;
        p.NightMode = C.Appearance.NightMode;
        p.Saved = DateTime.Now;
    }

    // Two passes: first every saved value goes back (rows were saved consistent, so no reflow is
    // needed - reflowing half-restored rows tore them apart), then overlays are created / removed.
    void ApplyLayout(LayoutProfile p)
    {
        var deskIds = new HashSet<string>();
        var winIds = new HashSet<string>();
        var groups = new HashSet<string>();
        var deskChanged = new List<DesktopConfig>();
        var winChanged = new List<WindowConfig>();
        foreach (var s in p.Panels)
        {
            switch (s.Kind)
            {
                case "desktop":
                {
                    var d = C.Desktops.FirstOrDefault(x => x.Id == s.Id);
                    if (d == null) break;
                    deskIds.Add(d.Id);
                    if (s.Group != null) groups.Add(s.Group);
                    d.Width = s.Width; d.Curvature = s.Curvature; d.Opacity = s.Opacity;
                    d.Place = CopyPlace(s.Place); d.Group = s.Group; d.GroupIndex = s.GroupIndex;
                    if (d.Enabled != s.Enabled) { d.Enabled = s.Enabled; deskChanged.Add(d); }
                    break;
                }
                case "window":
                {
                    var w = C.Windows.FirstOrDefault(x => x.Id == s.Id);
                    if (w == null) break;
                    winIds.Add(w.Id);
                    w.Width = s.Width; w.Curvature = s.Curvature; w.Opacity = s.Opacity;
                    w.Place = CopyPlace(s.Place);
                    if (w.Enabled != s.Enabled) { w.Enabled = s.Enabled; winChanged.Add(w); }
                    break;
                }
                case "keyboard":
                    C.Keyboard.Width = s.Width;
                    C.Keyboard.Place = CopyPlace(s.Place);
                    break;
            }
        }
        // added while another profile was in use: this profile never saw them - out of that other
        // profile's rows, and placed in front of you again (world ones; hand / head ones stay attached)
        foreach (var d in C.Desktops)
        {
            if (deskIds.Contains(d.Id)) continue;
            if (d.Group != null && groups.Contains(d.Group)) d.Group = null;
            if (Placement.IsWorld(d.Place)) d.Place.M = null;
        }
        foreach (var w in C.Windows)
            if (!winIds.Contains(w.Id) && Placement.IsWorld(w.Place)) w.Place.M = null;

        foreach (var d in deskChanged) SetDesktopOverlay(d);
        foreach (var w in winChanged) SetWindowEnabled(w, w.Enabled);
        C.Appearance.NightMode = p.NightMode;
    }

    // The current arrangement is kept in the profile you leave; the new one is put back as it was.
    // A profile used for the first time starts from the current arrangement brought to where you look.
    public void SwitchLayout(string name)
    {
        var target = C.Layouts.FirstOrDefault(l => l.Name == name);
        var cur = ActiveLayout;
        if (target == null || target == cur) return;
        if (_lasers != null) foreach (var l in _lasers) l.StopGrab();
        CaptureLayout(cur);
        C.ActiveLayout = target.Name;
        _firstUsePending = null;
        if (target.Saved == null)
        {
            if (_vr.Connected && _vr.HmdValid)
            {
                Recenter();
                CaptureLayout(target);
            }
            else
                _firstUsePending = target.Name;     // brought in front as soon as SteamVR has the headset
        }
        else
            ApplyLayout(target);
        Log.Info($"layout: {cur.Name} -> {target.Name}");
        _cfg.MarkDirty();
        _win.Dirty = true;
    }

    string _firstUsePending;

    void FirstUseIfPending()
    {
        if (_firstUsePending == null || !_vr.HmdValid) return;
        if (_firstUsePending == C.ActiveLayout)
        {
            Recenter();
            CaptureLayout(ActiveLayout);
            Log.Info($"layout {C.ActiveLayout}: first use, brought in front");
        }
        _firstUsePending = null;
    }

    // the watch's layout button: next profile that is on the watch
    void CycleLayout()
    {
        var ring = C.Layouts.Where(l => l.OnWrist || l.Name == C.ActiveLayout).ToList();
        if (ring.Count < 2) return;
        int i = ring.FindIndex(l => l.Name == C.ActiveLayout);
        SwitchLayout(ring[(i + 1) % ring.Count].Name);
    }

    public void ForgetLayout(string name)
    {
        var p = C.Layouts.FirstOrDefault(l => l.Name == name);
        if (p == null || p.Name == C.ActiveLayout) return;
        p.Panels = new();
        p.Saved = null;
        p.NightMode = false;
        _cfg.MarkDirty();
    }

    // ---------------- mirrored windows ----------------

    // config list -> panels (after adding / removing one)
    void SyncWindowPanels()
    {
        var gone = _windowPanels.Where(p => !C.Windows.Contains(p.Cfg)).ToList();
        foreach (var p in gone)
        {
            if (_lasers != null) foreach (var l in _lasers) l.Forget(p);
            if (_mouse.Panel == p) { _mouse.Hand = null; _mouse.Panel = null; }
            p.Dispose();
            _windowPanels.Remove(p);
        }
        foreach (var wc in C.Windows)
        {
            if (_windowPanels.Any(p => p.Cfg == wc)) continue;
            var p = new WindowPanel(_gpu, _cfg, _mouse, wc);
            _windowPanels.Add(p);
            if (_vr.Connected && _winrtDev != null && wc.Enabled) SafeCreate(() => p.CreateOverlay(_vr, _winrtDev), wc.Title);
        }
        _targets = null;
        _nextWindowScan = 0;
    }

    // closed windows let go of their panel; panels without a window look for theirs
    void ScanWindows()
    {
        if (_windowPanels.Count == 0) return;
        var taken = new HashSet<IntPtr>();
        bool searching = false;
        foreach (var p in _windowPanels)
        {
            if (p.Lost) { Log.Info($"window gone: '{p.Cfg.Title}'"); p.Detach(); }
            if (p.Hwnd != IntPtr.Zero) { taken.Add(p.Hwnd); p.RefreshTitle(); }
            else searching = true;
        }
        if (!searching) return;
        List<WindowInfo> open;
        try { open = WindowList.Enumerate(); }
        catch (Exception e) { Log.Warn("window list: " + e.Message); return; }
        foreach (var p in _windowPanels)
        {
            if (p.Hwnd != IntPtr.Zero) continue;
            var w = WindowList.Find(p.Cfg, open, taken);
            if (w == null) continue;
            taken.Add(w.Hwnd);
            p.Attach(w);
            Log.Info($"window panel {p.Cfg.Id} -> {w}");
        }
    }

    List<WindowInfo> _openCache;
    double _openCacheAt = -10;

    public IReadOnlyList<WindowInfo> OpenWindows()
    {
        if (_openCache == null || Now - _openCacheAt > 1)
        {
            try { _openCache = WindowList.Enumerate(); }
            catch (Exception e) { Log.Warn("window list: " + e.Message); _openCache ??= new(); }
            _openCacheAt = Now;
        }
        return _openCache;
    }

    public bool IsMirrored(IntPtr hwnd) => _windowPanels.Any(p => p.Hwnd == hwnd);

    public WindowConfig AddWindow(WindowInfo w)
    {
        var existing = _windowPanels.FirstOrDefault(p => p.Hwnd == w.Hwnd);
        if (existing != null) return existing.Cfg;
        var wc = new WindowConfig { Title = w.Title, Process = w.Process, Class = w.Class };
        // about the size the window has on the monitor (a 1920 px monitor is 0.9 m)
        if (WindowList.Bounds(w.Hwnd, out var r)) wc.Width = Math.Clamp((r.Right - r.Left) * 0.9f / 1920, 0.25f, 1.4f);
        C.Windows.Add(wc);
        SyncWindowPanels();
        var panel = _windowPanels.First(p => p.Cfg == wc);
        panel.Attach(w);
        if (_vr.Connected) PlaceWindow(panel, forceWorld: true);
        C.General.DesktopsVisible = true;      // you want to see what you just added
        Log.Info($"mirroring window {w}");
        _cfg.MarkDirty();
        _win.Dirty = true;
        return wc;
    }

    public void RemoveWindow(WindowConfig wc)
    {
        C.Windows.Remove(wc);
        SyncWindowPanels();
        _cfg.MarkDirty();
        _win.Dirty = true;
    }

    public string WindowStatus(WindowConfig wc)
    {
        var p = _windowPanels.FirstOrDefault(x => x.Cfg == wc);
        if (p == null) return "";
        if (!wc.Enabled) return "hidden";
        return p.Status;
    }

    public void ResetWindow(WindowConfig wc)
    {
        var p = _windowPanels.FirstOrDefault(x => x.Cfg == wc);
        if (p == null || !_vr.Connected) return;
        PlaceWindow(p, forceWorld: true);
    }

    public void SetWindowAttach(WindowConfig wc, string attach)
    {
        if (_vr.Connected) Placement.Reattach(wc.Place, _vr, attach);
        else { wc.Place.Attach = attach; wc.Place.M = null; }
        _cfg.MarkDirty();
    }

    public void SetWindowEnabled(WindowConfig wc, bool on)
    {
        wc.Enabled = on;
        var p = _windowPanels.FirstOrDefault(x => x.Cfg == wc);
        if (p != null && _vr.Connected && _winrtDev != null)
        {
            if (on && p.Ov == null) SafeCreate(() => p.CreateOverlay(_vr, _winrtDev), wc.Title);
            if (!on) p.DestroyOverlay();
        }
        _cfg.MarkDirty();
    }

    public void ResetKeyboard() { if (_vr.Connected) PlaceKeyboard(); }

    public void SetDesktopAttach(DesktopConfig d, string attach)
    {
        var panel = _desktops.FirstOrDefault(x => x.Cfg == d);
        if (panel != null && attach != "world") _layout.Detach(panel);   // rows only live in the world
        if (_vr.Connected) Placement.Reattach(d.Place, _vr, attach);
        else { d.Place.Attach = attach; d.Place.M = null; }
        _cfg.MarkDirty();
    }

    public void SetKeyboardAttach(string attach)
    {
        if (_vr.Connected) Placement.Reattach(C.Keyboard.Place, _vr, attach);
        else { C.Keyboard.Place.Attach = attach; C.Keyboard.Place.M = null; }
        _cfg.MarkDirty();
    }

    public void SetDesktopEnabled(DesktopConfig d, bool on)
    {
        d.Enabled = on;
        if (d.Group != null)
        {
            var row = _layout.Members(d.Group);       // enabled members, in row order
            if (row.Count > 0) _layout.Reflow(row[0]);
        }
        SetDesktopOverlay(d);
        _cfg.MarkDirty();
    }

    void SetDesktopOverlay(DesktopConfig d)
    {
        int i = _desktops.FindIndex(x => x.Cfg == d);
        if (i < 0 || !_vr.Connected || _winrtDev == null) return;
        if (d.Enabled && _desktops[i].Ov == null) SafeCreate(() => _desktops[i].CreateOverlay(_vr, _winrtDev, i), d.Name);
        if (!d.Enabled) _desktops[i].DestroyOverlay();
    }

    // one panel that can't get its overlay (a duplicate key, SteamVR refusing) must not stop the rest
    static void SafeCreate(Action create, string what)
    {
        try { create(); }
        catch (Exception e) { Log.Warn($"overlay for {what}: {e.Message}"); }
    }

    // The setting is the wanted state; it is pushed to SteamVR now if it runs, else on the next connect.
    public void SetAutoStart(bool on)
    {
        C.General.AutoStart = on;
        _cfg.MarkDirty();
        if (_vr.Connected) ApplyAutoStart();
    }

    // what the auto-start did, in English (the log's words); AutoStartStatus says it in the language picked
    string _autoStart = "applied when SteamVR runs";
    public string AutoStartStatus => _autoStart.StartsWith("SteamVR refused: ", StringComparison.Ordinal)
        ? Lang.F("SteamVR refused: {0}", _autoStart[17..]) : Lang.T(_autoStart);

    // Registers orbit.vrmanifest (SteamVR then starts "Orbit.exe --steamvr", which hops to the admin task)
    // and switches its auto-launch on/off. Re-registers if the exe moved since the last registration.
    void ApplyAutoStart()
    {
        try
        {
            var apps = OpenVR.Applications;
            bool installed = apps.IsApplicationInstalled(AppKey);
            string path = "";
            if (installed)
            {
                var sb = new System.Text.StringBuilder(1024);
                var pe = EVRApplicationError.None;
                apps.GetApplicationPropertyString(AppKey, EVRApplicationProperty.BinaryPath_String, sb, (uint)sb.Capacity, ref pe);
                path = sb.ToString();
            }
            string exe = Environment.ProcessPath ?? "";
            bool samePath = installed && string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
            EVRApplicationError e = EVRApplicationError.None;
            if (C.General.AutoStart)
            {
                if (!samePath)
                {
                    // registered from another folder before: drop that manifest, then add this one
                    if (installed && path.Length > 0)
                        apps.RemoveApplicationManifest(Path.Combine(Path.GetDirectoryName(path) ?? "", "orbit.vrmanifest"));
                    e = apps.AddApplicationManifest(VrManifest, false);
                }
                if (e == EVRApplicationError.None && !apps.GetApplicationAutoLaunch(AppKey))
                    e = apps.SetApplicationAutoLaunch(AppKey, true);
                _autoStart = e == EVRApplicationError.None ? "on: SteamVR starts OrbitOverlay and closes it on exit" : "SteamVR refused: " + e;
            }
            else if (installed)
            {
                apps.SetApplicationAutoLaunch(AppKey, false);
                e = apps.RemoveApplicationManifest(VrManifest);
                _autoStart = "off";
            }
            else _autoStart = "off";
            Log.Info($"auto-start wanted={C.General.AutoStart} installed={installed} path='{path}' -> {e}, autolaunch={apps.GetApplicationAutoLaunch(AppKey)}");
        }
        catch (Exception ex) { Log.Warn("auto-start: " + ex.Message); }
        _win.Dirty = true;
    }

    public void ToggleKeyboard()
    {
        _kbAuto = false;
        _kbHideAt = -1;
        C.Keyboard.Visible = !C.Keyboard.Visible;
        // shown again far away (walked off, recentered)? bring it back in front
        if (C.Keyboard.Visible && _vr.Connected && _vr.HmdValid && C.Keyboard.Place.M != null
            && Placement.TryWorld(C.Keyboard.Place, _vr, out var w)
            && Vector3.Distance(w.Translation, _vr.Hmd.Translation) > 1.5f)
            PlaceKeyboard();
        _cfg.MarkDirty();
        _win.Dirty = true;
    }

    public void ToggleDesktops()
    {
        if (_theater.On) StopTheater(glide: false);
        C.General.DesktopsVisible = !C.General.DesktopsVisible;
        _cfg.MarkDirty();
        _win.Dirty = true;
    }

    public int GroupSize(DesktopConfig d) => _layout.GroupSize(d);

    public bool IsElevated { get; } = Elevation.IsElevated;

    public string NotificationStatus => _notifyIn.Status;

    // the first start of a new version: a card in VR and a line under the version in the settings
    void NoteVersion()
    {
        string now = Updater.CurrentText, last = Program.UpdatedFrom ?? C.General.LastVersion;
        // a version that stopped before it saved (1.10.0) left the config on an older one: the older of the two, so
        // "What's new" also lists what that version brought
        if (Version.TryParse(C.General.LastVersion, out var saved) && Version.TryParse(last, out var given) && saved < given)
            last = C.General.LastVersion;
        // 1.10, the Bento redesign: an install that ran before gets the new colours once (a new one starts with them); the
        // old theme stays one click away on the Look tab, and the log keeps the old colours
        if (C.Appearance.LookVersion < 1)
        {
            var a = C.Appearance;
            if (C.General.LastVersion != null && (a.Accent != 0xFFFF5D93 || a.PanelColor != 0xFF0A0B10))
            {
                Log.Info($"look: the Bento colours (were accent {a.Accent:X8}, panel {a.PanelColor:X8}, laser {a.LaserColor:X8})");
                a.Accent = 0xFFFF5D93;
                a.PanelColor = 0xFF0A0B10;
                Theme.Apply(a);     // NOT AppearanceChanged(): this runs in the constructor, before the window exists (1.10.0 crashed here)
            }
            a.LookVersion = 1;
            _cfg.MarkDirty();
        }
        if (last == now) return;
        if (last != null)
        {
            Log.Info($"updated: {last} -> {now}");
            // the config as the old version left it, kept once as config.backup-<it>.json - nothing of this version is saved over it yet
            // (the constructor runs before the first SaveIfDue); named for the version that wrote it (LastVersion)
            if (_cfg.KeepBackup(C.General.LastVersion ?? last) is { } kept) Log.Info("update: the config before it kept as " + System.IO.Path.GetFileName(kept));
            Updates.NoteUpdated(last);
            if (WhatsNew.Since(last).Count > 0)
            {
                // "What's new" until OK; two updates before you close it: it shows both (keeps the older version)
                string pending = C.General.WhatsNewFrom;
                if (pending == null || (Version.TryParse(pending, out var a) && Version.TryParse(last, out var b) && b < a))
                    C.General.WhatsNewFrom = last;
            }
            else
                _notifyIn.Incoming.Enqueue(new Notification
                {
                    Source = "OrbitOverlay", Title = Lang.T("OrbitOverlay updated"),
                    Content = Lang.F("Now running {0} (was {1}).", now, last), Timeout = 8,
                });
        }
        C.General.LastVersion = now;
        _cfg.MarkDirty();
    }

    // "What's new" after an update: the same list in the VR panel and over the settings window
    List<WhatsNew.Entry> _wnList = new();
    string _wnFor;

    public IReadOnlyList<WhatsNew.Entry> WhatsNewList
    {
        get
        {
            string from = C.General.WhatsNewFrom;
            if (from != _wnFor) { _wnFor = from; _wnList = from == null ? new() : WhatsNew.Since(from); }
            return _wnList;
        }
    }

    public string WhatsNewSub => C.General.WhatsNewFrom == null ? "" : Lang.F("Updated to {0} from {1}", Updater.CurrentText, C.General.WhatsNewFrom);

    public void DismissWhatsNew()
    {
        if (C.General.WhatsNewFrom == null) return;
        C.General.WhatsNewFrom = null;
        Log.Info("what's new: closed");
        _cfg.MarkDirty();
        _win.Dirty = true;
    }

    public void TestNotification() => _notifyIn.Incoming.Enqueue(new Notification
    {
        Source = "OrbitOverlay", Title = Lang.T("Test notification"),
        Content = Lang.T("Notifications from VRCX and other tools appear here."), Timeout = 4,
    });

    public void SetRunAsAdmin(bool on)
    {
        C.General.RunAsAdmin = on;
        if (!on && IsElevated) Elevation.RemoveTask();
        _cfg.MarkDirty();
        _cfg.SaveIfDue(force: true);
    }

    // Starts an elevated Orbit (task, or one UAC prompt) and quits this one; the new one waits for us.
    public void RestartElevated()
    {
        _cfg.SaveIfDue(force: true);
        string pass = "--show --restart";
        if (Elevation.RunTask(pass) || Elevation.RelaunchWithPrompt("--install-task " + pass))
            _quit = true;
    }

    public void JoinAllDesktops()
    {
        if (!_vr.Connected || !_vr.HmdValid) return;
        _layout.JoinAll(MathUtil.GazeFrame(_vr.Hmd), RowRadius, -0.12f);
    }

    public void SeparateDesktops() => _layout.SeparateAll();

    public void FitCurve(DesktopConfig d)
    {
        var p = _desktops.FirstOrDefault(x => x.Cfg == d);
        if (p != null && _vr.Connected && _vr.HmdValid) _layout.FitCurve(p, _vr.Hmd.Translation);
    }

    // size / curvature changed in the settings: keep its row seamless around it
    public void DesktopEdited(DesktopConfig d)
    {
        var p = _desktops.FirstOrDefault(x => x.Cfg == d);
        if (p != null && d.Group != null) _layout.Reflow(p);
    }

    public void AppearanceChanged()
    {
        Theme.Apply(C.Appearance);
        _dashDirty = true;
        _win.Dirty = true;
        _cfg.MarkDirty();
    }

    // Hides monitors + keyboard together; the next call brings back what was showing (OVR Toolkit style).
    public void QuickToggle()
    {
        if (_theater.On) StopTheater(glide: false);
        bool anyShown = C.General.DesktopsVisible || C.Keyboard.Visible;
        if (_settingsPanel.IsOpen) _settingsPanel.Toggle();      // the gesture clears the view
        if (anyShown)
        {
            _restoreDesktops = C.General.DesktopsVisible;
            _restoreKeyboard = C.Keyboard.Visible;
            C.General.DesktopsVisible = false;
            C.Keyboard.Visible = false;
        }
        else
        {
            C.General.DesktopsVisible = _restoreDesktops || !_restoreKeyboard;
            if (_restoreKeyboard) ToggleKeyboard();
        }
        if (_vr.Connected && C.General.Haptics)
        {
            _vr.Haptic(Hand.Left, 0.6f, 0.04f);
            _vr.Haptic(Hand.Right, 0.6f, 0.04f);
        }
        _cfg.MarkDirty();
        _win.Dirty = true;
    }

    // Both grips pressed together (within 0.3 s), twice within 0.6 s.
    void CheckQuickToggle(double now)
    {
        ref var l = ref _vr.Hands[0];
        ref var r = ref _vr.Hands[1];
        if (l.GrabPressed) _gripDownAt[0] = now;
        if (r.GrabPressed) _gripDownAt[1] = now;
        bool chord = l.Grab && r.Grab && (l.GrabPressed || r.GrabPressed) && Math.Abs(_gripDownAt[0] - _gripDownAt[1]) < 0.3;
        if (!chord) return;
        if (now - _lastChord < 0.6)
        {
            _lastChord = -10;
            foreach (var la in _lasers) la.Reset();
            QuickToggle();
        }
        else
            _lastChord = now;
    }

    // XSOverlay-compatible avatar parameters for DXOverlay and similar avatar add-ons.
    void SendOsc(double now, bool allOff = false)
    {
        bool kb = !allOff && _kb.Ov?.Visible == true;
        // monitors and mirrored windows only: Orbit's own settings panel is not "an overlay open" for
        // avatar add-ons (DXOverlay started its effect when you only opened the settings)
        int desks = 0;
        if (!allOff)
        {
            foreach (var d in _desktops) if (d.Ov?.Visible == true) desks++;
            foreach (var w in _windowPanels) if (w.Ov?.Visible == true) desks++;
        }
        bool wrist = !allOff && _wrist.Ov?.Visible == true && _wrist.Alpha > 0.5f;
        var v = _data.Vr;
        // 10 times a second: each value goes out when it changed, all of them every 5 s (OscSender) - no list, nothing boxed
        _osc.Begin(now, C.General.OscPort, force: allOff);
        _osc.Param("isOverlayOpen", desks > 0 || kb);
        _osc.Param("isKeyboardOpen", kb);
        _osc.Param("isWristVisible", wrist);
        _osc.Param("openOverlayCount", desks + (kb ? 1 : 0));
        if (!allOff)
        {
            if (v.LeftBattery >= 0) _osc.Param("leftControllerBattery", v.LeftBattery);
            if (v.RightBattery >= 0) _osc.Param("rightControllerBattery", v.RightBattery);
            // the average as Enumerable.Average took it: a double sum, then a float
            double hsum = 0;
            int hn = 0;
            if (v.LeftBattery >= 0) { hsum += v.LeftBattery; hn++; }
            if (v.RightBattery >= 0) { hsum += v.RightBattery; hn++; }
            if (hn > 0) _osc.Param("averageControllerBattery", (float)(hsum / hn));
            if (v.TrackerAverage >= 0) _osc.Param("averageTrackerBattery", v.TrackerAverage);
        }
    }

    public void OpenConfigFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Dir}\"") { UseShellExecute = true }); }
        catch (Exception e) { Log.Warn("open folder: " + e.Message); }
    }

    void ShowSettings() => _win.ShowAndFocus();

    // The bulb straight by itself (Erik 10-19: "a tapo izzot is supportalhatna nativan az orbitoverlay, jelenleg lumos nelkul nem megy"):
    // a light widget on a page, no key yet and "Use Lumos" never picked - the Tapo account Hexpad or Lumos keeps (TapoAccount.Import,
    // read only; only the bulb's key kept, DPAPI-protected), once a run, on a worker; the bulb's address from Lumos's list when none is
    // set. No account in either: through Lumos as before ("Go direct" in the watch editor asks for one).
    System.Threading.Tasks.Task<(string kept, string from, string bulb)> _tapoAuto;
    bool _tapoAutoDone;

    void TapoAuto()
    {
        if (_tapoAutoDone || C.Lights?.Key != null || C.Lights?.ViaLumos == true || !(C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "light"))) return;
        _tapoAuto ??= System.Threading.Tasks.Task.Run(() =>
        {
            try { return (TapoAccount.Import(out string from), from, TapoAccount.BulbFromLumos()); }
            catch (Exception e) { Log.Warn("lights: the Tapo account could not be read (" + e.GetType().Name + ")"); return ((string)null, (string)null, (string)null); }
        });
        if (!_tapoAuto.IsCompleted) return;
        _tapoAutoDone = true;
        var (kept, from, bulb) = _tapoAuto.Result;
        if (kept == null) { Log.Info("lights: no Tapo account in Hexpad or Lumos - through Lumos (Go direct in the watch editor asks for one)"); return; }
        var l = C.Lights ??= new LightsConfig();
        l.Key = kept;
        bool addr = l.Bulb == null && bulb != null;
        if (addr) l.Bulb = bulb;
        _cfg.MarkDirty();
        Log.Info($"lights: straight to the bulb - the Tapo account from {from}{(addr ? ", its address from Lumos" : "")}");
    }

    void WristAction(string id)
    {
        switch (id)
        {
            case "keyboard": ToggleKeyboard(); break;
            case "desktops": ToggleDesktops(); break;
            case "recenter": Recenter(); break;
            case "layout": CycleLayout(); break;
            case "night":
                C.Appearance.NightMode = !C.Appearance.NightMode;
                _cfg.MarkDirty();
                _win.Dirty = true;
                break;
            case "media.prev": _media.Command("prev"); break;
            case "media.play": _media.Command("play"); break;
            case "timer.done":
                _notifyIn.Incoming.Enqueue(new Notification { Source = "OrbitOverlay", Title = Lang.T("Timer"), Content = Lang.T("Time is up."), Timeout = 6 });
                break;
            case "media.next": _media.Command("next"); break;
            case "mines.flag":
            case "mines.boom":
                // Minesweeper: a held square got (or lost) its flag - a tick in the hand with the laser; a mine went off - a jolt
                if (_vr.Connected && C.General.Haptics)
                    _vr.Haptic(C.Wrist.Hand == "right" ? Hand.Left : Hand.Right, id == "mines.boom" ? 0.9f : 0.5f, id == "mines.boom" ? 0.12f : 0.02f);
                break;
            case "settings":
                _settingsPanel.Toggle();        // floating panel; the dashboard tab still exists too
                break;
            case "dock":
                _dockPanel.Toggle();            // Hexpad's stream dock as a panel
                break;
            case "room":
                _roomPanel.Toggle();
                break;
            case "theater": ToggleTheater(); break;
            case "status":
                C.Chatbox.StatusLine = !C.Chatbox.StatusLine;
                _cfg.MarkDirty();
                _win.Dirty = true;
                break;
        }
    }

    // Hexpad's stream dock: the link runs while something shows it (its panel, or a watch widget while VR runs);
    // the faces become bitmaps once per change, for the panel and the widget alike
    void UpdateDock(double now, float dt)
    {
        var d = C.Dock;
        bool widget = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "dock");
        _hexpad.Active = _vr.Connected && d.Enabled && (d.Visible || widget);
        int v = _hexpad.Version;
        if (v != _dockSnap.Version)
        {
            _dockSnap = _hexpad.Get();
            _dockFaces.Update(_painter, _dockSnap);
            _data.Dock = _dockSnap;
        }
        _dockPanel.Update(_vr, now, dt, _dockSnap);
    }

    // who is in the VRChat instance: the panel, and a card when a friend / a starred person comes or goes (arriving
    // in a world: one card for everyone you know who is already there, not one per person)
    void UpdateRoom(double now, float dt)
    {
        if (_room.Version != _roomSnap.Version)
        {
            _roomSnap = _room.Get();
            if (C.Room.Enabled) _data.Room = _roomSnap;
        }
        while (_room.Events.TryDequeue(out var ev))
            if (C.Room.Enabled) RoomEventCard(ev);
        if (_buzzAgainAt > 0 && now >= _buzzAgainAt)
        {
            _buzzAgainAt = -1;
            if (_vr.Connected) _vr.Haptic(WatchHand, 0.7f, 0.05f);
        }
        _roomPanel.Update(_vr, now, dt, _roomSnap);
    }

    // (a method of its own: the lambda below holds the config, and that holder was made on every call of UpdateRoom - every frame)
    void RoomEventCard(RoomWatcher.RoomEvent ev)
    {
        var r = C.Room;
        bool Known(RoomWatcher.Person p) => (r.NotifyFriends && p.Friend) || (r.NotifyStarred && r.Starred.Contains(p.Key));
        string world = ev.World.Length > 60 ? ev.World[..59] + "\u2026" : ev.World;
        if (ev.Kind == RoomWatcher.Kind.Arrived)
        {
            var who = Array.FindAll(ev.People, Known);
            if (who.Length == 0) return;
            string names = string.Join(", ", Array.ConvertAll(who.Length > 4 ? who[..4] : who, p => p.Name)) + (who.Length > 4 ? $" +{who.Length - 4}" : "");
            RoomCard(who.Length == 1 ? Lang.F("{0} is here", who[0].Name) : Lang.F("{0} people you know are here", who.Length), names + (world.Length > 0 ? " · " + world : ""));
            if (r.NotifyFriends && Array.Exists(who, x => x.Friend)) FriendBuzz(twice: false);
            return;
        }
        var p = ev.People[0];
        if (!(r.NotifyEveryone || Known(p))) return;
        // a friend joining (Erik 10-18): the card with a heart, a little louder and longer, the watch's hand buzzes twice
        bool friend = ev.Kind == RoomWatcher.Kind.Joined && p.Friend && r.NotifyFriends;
        if (friend)
        {
            RoomCard(Lang.F("{0} joined", p.Name), Lang.T("Friend") + (world.Length > 0 ? " · " + world : ""), "\uEB52", 6, 0.7f);
            FriendBuzz(twice: true);
            return;
        }
        RoomCard(ev.Kind == RoomWatcher.Kind.Joined ? Lang.F("{0} joined", p.Name) : string.Format(Lang.C("room", "{0} left"), p.Name), world);
    }

    void RoomCard(string title, string content, string glyph = "\uE716", float timeout = 4, float volume = 0.5f) =>
        _notifyIn.Incoming.Enqueue(new Notification { Source = "Room", Title = title, Content = content, Timeout = timeout, Volume = volume, Glyph = glyph });

    // a friend in the room: the watch's hand buzzes (twice for one who just joined: the second pulse a moment later, UpdateRoom)
    double _buzzAgainAt = -1;

    void FriendBuzz(bool twice)
    {
        if (!_vr.Connected || !C.General.Haptics || !C.Room.BuzzFriends) return;
        _vr.Haptic(WatchHand, 0.7f, 0.05f);
        if (twice) _buzzAgainAt = Clock.Now + 0.16;
    }

    public string StatusLinePreview
    {
        get
        {
            var m = _media.Snapshot();
            return StatusLine.Compose(C.Chatbox, (m.has, m.title, m.artist, m.playing), DateTime.Now, _data.VrSince, C.Appearance.Clock24h);
        }
    }

    // the settings' eye tracking line: is Virtual Desktop's there, are the eyes tracked now
    public string EyeStatus =>
        !C.General.EyeTracking ? Lang.T("off: the head's direction")
        : !_vr.Connected ? Lang.T("Virtual Desktop's, while SteamVR runs")
        : _vr.EyesValid ? Lang.T("your eyes are tracked")
        : _eyes.Present ? Lang.T("Virtual Desktop found, the eyes not tracked now")
        : Lang.T("not found: Virtual Desktop, 'Forward tracking data' on");

    public string RoomStatus
    {
        get
        {
            var s = _roomSnap;
            if (!_vr.Connected) return Lang.T("follows the log while SteamVR runs");
            if (!s.LogFound) return Lang.T("no VRChat log found");
            string where = s.InRoom ? Lang.F("{0} in {1}", s.Here.Count, s.World.Length > 28 ? s.World[..27] + "\u2026" : s.World) : Lang.T("not in a world");
            return where + (s.VrcxFound ? Lang.F(" · VRCX: {0} friends", s.Friends) : Lang.T(" · VRCX not found"));
        }
    }

    public void ToggleRoom() => _roomPanel.Toggle();

    public void TheaterEdited()
    {
        _theater.Apply(C.Theater);
        _cfg.MarkDirty();
    }

    // 8 s after connecting, the data in (the weather, the photos, the light): the watch's face drawn once more with it, its intro too,
    // and a notification card - each one's first frame made its code ready in front of you (Erik 10-18, the spike line: the first look
    // at the watch 20 ms, the first card 7.8 ms). Only while the watch is away; nothing is shown or sent.
    double _warmAt = -1;

    void WarmAgain()
    {
        _warmAt = -1;
        if (_wrist.Alpha > 0.02f) return;
        try
        {
            _wrist.Intro();
            _wrist.Draw(_data);
            _wrist.Settle();
            _wrist.Draw(_data);
            _wrist.Settle();
            _notifyPanel.Warm();
            // the SteamVR dashboard's tab: its first draw was 18.8 ms when the dashboard opened (Erik 10-19)
            if (_dash != null && _dashSurf != null && !_vr.Ov.IsDashboardVisible()) { DrawDashboard(); AnimWake.Take(); _dashDirty = true; }
        }
        catch (Exception e) { Log.Warn("warm-up draw: " + e.Message); }
    }

    // previews in the settings screen are separate draws; they must not nest inside its BeginDraw
    void PreparePreviews(SettingsView v)
    {
        if (v.ShowsWristPreview) { _photoPreview = Clock.Now; _wrist.Draw(_data); }
        if (v.ShowsKeyboardPreview) _kb.Draw();
    }

    public void Dispose()
    {
        try { if (_vr.Connected) DisconnectVr(false); } catch (Exception e) { Log.Warn("shutdown: " + e.Message); }
        InputQueue.Flush();                  // a button / key let go reaches Windows before OrbitOverlay ends
        _cfg.SaveIfDue(force: true);
        foreach (var d in _desktops) d.Dispose();
        foreach (var w in _windowPanels) w.Dispose();
        _wrist?.Dispose(); _kb?.Dispose(); _settingsPanel?.Dispose(); _dockPanel?.Dispose(); _dockFaces?.Dispose(); _roomPanel?.Dispose(); _theater?.Dispose(); _theaterBar?.Dispose(); _whatsNewPanel?.Dispose();
        SocialDispose();                    // a session still open ends here: its recap written (OrbitApp.Social.cs)
        _room.Dispose();
        _photos.Dispose();
        _memories.Dispose();
        _temps.Dispose();
        _weather.Dispose();
        _lumos.Dispose();
        _dxgi?.Dispose();
        _eyes.Dispose();
        _mixtape.Dispose();
        _prisma.Dispose();
        AvatarDispose();
        _pacer.Dispose();
        _hexpad.Dispose();
        _dashSurf?.Dispose(); _thumbSurf?.Dispose();
        _gpuStats.Dispose();
        _notifyIn.Dispose();
        _focus?.Dispose();
        _notifyPanel?.Dispose();
        _media.Dispose();
        _mixer.Dispose();
        _sysCursor?.Dispose();
        _osc.Dispose();
        _win?.Dispose();
        _tray?.Dispose();
        _painter?.Dispose();
        _gpu?.Dispose();
        Log.Info("bye");
    }
}
