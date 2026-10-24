using System;

namespace Orbit;

// GPU memory of what is not in use let go (Erik 10-24: Orbit took 341 MB of VRAM - every texture was made the first time it was needed
// and kept for good). Once a second, at the end of a frame - outside every draw (a style picked in the settings is applied in the middle
// of their draw, which goes on drawing into its canvas) - and from here alone:
//  - the other styles' textures, 10 s after the style changed (flipping through the styles makes nothing twice): the retro canvases
//    without a retro style, the keyboard's kept picture in one, Cyber's kept settings console and its theater horizon without Cyber;
//  - the settings' kept textures (the VR panel's, the window's) once it has been closed / hidden a minute;
//  - Cyber's HUD frame over a monitor / window a minute after it last showed (CyberScreenFx.ReleaseIdle);
//  - the SteamVR dashboard tab's picture a minute after the dashboard closed (its thumbnail stays: SteamVR shows it whenever the
//    dashboard opens);
//  - theater's textures (17 MB in Cyber, 8 in the other styles) once Orbit has not been used for 5 minutes - no laser, no watch, no
//    theater, no full-screen video waiting for auto theater; warmed again the moment it is (TheaterWarm.cs).
// Nothing an overlay may show goes: an overlay that was given the texture is hidden and SteamVR told to let go of it first
// (VrOverlay.ClearTexture). Each release logs what it freed ("gpu: released N MB (what)": the drop of Surface's count); the perf log's
// mem: line ends with every surface alive and Orbit's own VRAM (what Task Manager shows). The next use makes it again (the "??=" and the
// null checks that made it the first time). What stays: every panel's own texture and the monitors' captures - most of it (a monitor's
// picture and the 3 buffers Windows captures it into).
public sealed partial class OrbitApp
{
    const double StyleKeepS = 10, SettingsKeepS = 60, DashKeepS = 60, TheaterKeepS = 300;
    double _gpuSweepAt, _styleGoneAt = -1;
    double _gpuIdleFrom;                     // SteamVR connected (OnVrConnected): Orbit's unused time counts from there or from its last use
    double _dashClosedAt;                    // when SteamVR's dashboard closed (made: CreateDashboard) - its tab's picture goes DashKeepS after
    int _gpuStyle = -1;                      // 0 Modern, 1 a retro one, 2 Cyber (the textures differ between these only)
    bool _autoWants;                         // auto theater: a full-screen video is waiting to start it (OrbitApp.AutoTheater.cs)
    Vortice.DXGI.IDXGIAdapter3 _vramAdapter;
    bool _vramFailed;

    static void GpuReleased(long bytes, string what)
    {
        if (bytes > 0) Log.Info($"gpu: released {bytes / 1048576.0:0.0} MB ({what})");
    }

    // the end of every frame (Tick): a frame pays two tests, the rest is once a second; strings only when something goes
    void GpuSweep(double now)
    {
        // Orbit in use again (a laser, the watch, theater; a full-screen video waiting for auto theater - every way into theater): its
        // textures warmed again from the next frame (TheaterWarm.cs), before a theater can start
        if (_theater.Cold && _vr.Connected && (now < _gpuBusyUntil || _autoWants))
        {
            _theater.Cold = false;
            Log.Info("gpu: theater's textures made again (Orbit in use)");
        }
        if (now < _gpuSweepAt) return;
        _gpuSweepAt = now + 1;
        int style = Theme.Classic ? 1 : Theme.Cyber ? 2 : 0;
        if (style != _gpuStyle)
        {
            if (_gpuStyle >= 0) _styleGoneAt = now + StyleKeepS;
            _gpuStyle = style;
        }
        if (_styleGoneAt >= 0 && now >= _styleGoneAt)
        {
            // (each one asks the style in use what it never draws: nothing of the style picked again meanwhile goes)
            _styleGoneAt = -1;
            long b = Surface.LiveBytes;
            _wrist.ReleaseStyle(); _kb.ReleaseStyle(); _settingsPanel.ReleaseStyle(); _dockPanel.ReleaseStyle(); _roomPanel.ReleaseStyle();
            _notifyPanel.ReleaseStyle(); _whatsNewPanel.ReleaseStyle(); _theaterBar.ReleaseStyle(); _theater.ReleaseStyle();
            _win.View.ReleaseStyle(); _dashView?.ReleaseStyle();
            GpuReleased(b - Surface.LiveBytes, "the other styles' textures");
        }
        long s = Surface.LiveBytes;
        _settingsPanel.ReleaseIdle(now);
        if ((!_win.Visible || _win.WindowState == System.Windows.Forms.FormWindowState.Minimized) && now - _win.LastShown >= SettingsKeepS)
            _win.View.Dispose95();
        GpuReleased(s - Surface.LiveBytes, "the settings' kept textures, away a minute");
        if (!_vr.Connected) return;
        long f = Surface.LiveBytes;
        foreach (var d in _desktops) d.ReleaseIdleFx(now);
        foreach (var w in _windowPanels) w.ReleaseIdleFx(now);
        GpuReleased(f - Surface.LiveBytes, "the Cyber screens' HUD frames, unused a minute");
        if (_dashSurf != null && !_dashWasOpen && now - _dashClosedAt >= DashKeepS) ReleaseDashboard();
        if (!_theater.Cold && _theater.Resting && !_autoWants && now - Math.Max(_gpuBusyUntil, _gpuIdleFrom) >= TheaterKeepS)
        {
            long t = Surface.LiveBytes;
            _theater.ReleaseTextures();
            GpuReleased(t - Surface.LiveBytes, "theater's textures, Orbit not used for 5 minutes");
        }
    }

    // SteamVR's dashboard closed a minute: its Orbit tab's picture let go - SteamVR told first (the tab cannot show while the dashboard is
    // closed) - and the tab's page's kept textures with it. The dashboard opening on the tab draws it into a new one in its first frame
    // (UpdateDashboard: the close made it dirty; until then the tab may be empty for a compositor frame). The thumbnail stays: handed over
    // once, SteamVR shows it in the dashboard's strip whenever that opens.
    void ReleaseDashboard()
    {
        long b = Surface.LiveBytes;
        _dash?.ClearTexture();
        _dashSurf.Dispose();
        _dashSurf = null;
        _dashView?.Dispose95();
        _dashDirty = true;
        GpuReleased(b - Surface.LiveBytes, "the SteamVR dashboard tab, closed a minute");
    }

    // Orbit's own dedicated VRAM now (DXGI's count for this process: what Task Manager shows) - the perf log's mem: line; "" if it can't be had.
    // The releases' real measure: the driver may keep a freed texture's memory a while, so it can lag the "released" lines.
    string OwnVram()
    {
        if (_vramFailed) return "";
        try
        {
            if (_vramAdapter == null)
            {
                _dxgi ??= _gpu.Device.QueryInterface<Vortice.DXGI.IDXGIDevice>();
                _dxgi.GetAdapter(out var a);
                _vramAdapter = a.QueryInterface<Vortice.DXGI.IDXGIAdapter3>();
                a.Dispose();
            }
            return $", vram {_vramAdapter.QueryVideoMemoryInfo(0, Vortice.DXGI.MemorySegmentGroup.Local).CurrentUsage / 1048576} MB";
        }
        catch (Exception e) { Log.Warn("vram: " + e.Message); _vramFailed = true; return ""; }
    }
}
