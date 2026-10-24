using System;

namespace Orbit;

// GPU memory of what is not in use let go (Erik 10-24: Orbit took 341 MB of VRAM - every texture was made the first time it was needed
// and kept for good). Once a second (the style in use and the signs of theater: every frame), at the end of a frame - outside every draw
// (a style picked in the settings is applied in the middle of their draw, which goes on drawing into its canvas) - and from here alone:
//  - the other styles' textures, 10 s after the style changed (flipping through the styles makes nothing twice): the retro canvases
//    without a retro style, the keyboard's kept picture in one, Cyber's kept settings console and its theater horizon without Cyber;
//  - the settings' kept textures (the VR panel's, the window's) once it has been closed / hidden a minute;
//  - Cyber's HUD frame over a monitor / window a minute after it last showed (CyberScreenFx.ReleaseIdle);
//  - the SteamVR dashboard tab's picture a minute after the dashboard closed (its thumbnail stays: SteamVR shows it whenever the
//    dashboard opens);
//  - theater's textures (17 MB in Cyber, 8 in the other styles) after 20 minutes in VR without a sign of theater - not on, no laser on
//    the bar or the watch (their theater buttons), no full-screen video waiting for auto theater; warmed again at the next sign
//    (TheaterWarm.cs).
// Nothing an overlay may show goes: an overlay that was given the texture is hidden and SteamVR told to let go of it first
// (VrOverlay.ClearTexture). Each release logs what it freed ("gpu: released N MB (what)": the drop of Surface's count); the perf log's
// mem: line ends with every surface alive and Orbit's own VRAM (what Task Manager shows). The next use makes it again (the "??=" and the
// null checks that made it the first time). What stays: every panel's own texture and the monitors' captures - most of it (a monitor's
// picture and the 3 buffers Windows captures it into).
public sealed partial class OrbitApp
{
    const double StyleKeepS = 10, SettingsKeepS = 60, DashKeepS = 60, TheaterKeepS = 1200;
    double _gpuSweepAt, _styleGoneAt = -1;
    double _gpuIdleFrom;                     // SteamVR connected (OnVrConnected): theater's quiet time counts from there or from its last sign
    double _theaterNearAt;                   // the last frame with a sign of theater (GpuSweep: on, a laser on the bar / the watch, a video waiting)
    double _dashClosedAt;                    // when SteamVR's dashboard closed (made: CreateDashboard) - its tab's picture goes DashKeepS after
    int _gpuStyle = -1;                      // 0 Modern, 1 a retro one, 2 Cyber (the textures differ between these only)
    bool _autoWants;                         // auto theater: a full-screen video is waiting to start it (OrbitApp.AutoTheater.cs)
    Vortice.DXGI.IDXGIAdapter3 _vramAdapter;
    bool _vramFailed;

    static void GpuReleased(long bytes, string what)
    {
        if (bytes > 0) Log.Info($"gpu: released {bytes / 1048576.0:0.0} MB ({what})");
    }

    // the end of every frame (Tick): a frame pays a few tests, the rest is once a second; strings only when something goes
    void GpuSweep(double now)
    {
        // the style in use, every frame: a style that drew even one frame made its textures (10-24 review: asked once a second, Cyber
        // picked and Win95 again within it - a misclick, flipping through the list - was no change, and what Cyber made was kept)
        int style = Theme.Classic ? 1 : Theme.Cyber ? 2 : 0;
        if (style != _gpuStyle)
        {
            if (_gpuStyle >= 0) _styleGoneAt = now + StyleKeepS;
            _gpuStyle = style;
        }
        // a sign theater may start - on (or on its way out), a laser on the bar (its Theater button) or on the watch (its theater button),
        // a full-screen video waiting for auto theater: every way into theater. Its textures warmed again from the next frame
        // (TheaterWarm.cs), before a tap can start it. Not a glance at the watch for the time, not a laser on a monitor (10-24 review:
        // those warmed 17 MB and their draws again on the frames Orbit was being used, and let them go 5 minutes on - every evening)
        bool near = !_theater.Resting || _autoWants;
        if (_lasers != null) foreach (var la in _lasers) { var h = la.Hovered; if (h == _theaterBar || h == _wrist) near = true; }
        if (near)
        {
            _theaterNearAt = now;
            if (_theater.Cold && _vr.Connected)
            {
                _theater.Cold = false;
                Log.Info("gpu: theater's textures made again (a laser on the bar / the watch, or a video waiting)");
            }
        }
        if (now < _gpuSweepAt) return;
        _gpuSweepAt = now + 1;
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
        // (theater at rest, no video waiting: either one is a sign above, this very frame)
        if (!_theater.Cold && now - Math.Max(_theaterNearAt, _gpuIdleFrom) >= TheaterKeepS)
        {
            long t = Surface.LiveBytes;
            _theater.ReleaseTextures();
            GpuReleased(t - Surface.LiveBytes, "theater's textures, no sign of theater for 20 minutes");
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

    // Orbit's own dedicated VRAM now in MB (DXGI's count for this process: what Task Manager shows) - the perf log's mem: line; -1 if it
    // can't be had. The releases' real measure: the driver may keep a freed texture's memory a while, so it can lag the "released" lines.
    long OwnVramMb()
    {
        if (_vramFailed) return -1;
        try
        {
            if (_vramAdapter == null)
            {
                _dxgi ??= _gpu.Device.QueryInterface<Vortice.DXGI.IDXGIDevice>();
                _dxgi.GetAdapter(out var a);
                _vramAdapter = a.QueryInterface<Vortice.DXGI.IDXGIAdapter3>();
                a.Dispose();
            }
            return (long)(_vramAdapter.QueryVideoMemoryInfo(0, Vortice.DXGI.MemorySegmentGroup.Local).CurrentUsage / 1048576);
        }
        catch (Exception e) { Log.Warn("vram: " + e.Message); _vramFailed = true; return -1; }
    }
}
