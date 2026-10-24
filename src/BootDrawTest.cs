using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Orbit;

// ORBIT_SNAP_ONLY=bootdraw --snapshot <dir>: the first draws as a start makes them (Erik 10-24, the boot: line - "the watch's first
// draw" 106 ms in one frame after the connect, with the fonts warmed; then "keyboard 39,3" and the first settings tab's ~46 ms). A fresh
// process, as OrbitApp's start is: the warm-up's fonts on their own thread (Sys/BootWarm.cs, without its capture part), then the
// constructor's first draws where it makes them now - the watch's (OrbitApp.WarmWatch: nothing from SteamVR yet - no batteries, no time
// in VR), the keyboard's and the settings' first tabs within the app's budget (OrbitApp.WarmPanels, SettingsPanel.WarmTabs) -, then the
// connect steps' (BootTick, with a connected start's data): the watch, the keyboard, each settings tab; and one more of the watch. Each
// is a frame of Spikes' boot line, so the log gets a "boot:" line made as the live one is: the constructor's draws first (no frame in
// the app), then the connect steps', a frame each, the later one last. Its own "bootdraw:" lines: the connect step's draw of the watch
// at most a third of its first, the keyboard's and the first tab's at most half of theirs (a retro keyboard keeps no picture at rest:
// its whole canvas drawn every time) and the warmed tabs' together less than their first draws (on WARP, where D2D's rasterizing - the
// same for a first draw and the next - is the CPU's too); nothing written to the config, the panel still closed, the keyboard's picture
// a fresh keyboard's. Cyber, Erik's style; ORBIT_BOOTDRAW_STYLE = modern | 95 | xp | aero | mac | term for another (a run each: a first
// draw is once a process). ORBIT_BOOTDRAW_WARM=0: the keyboard and the tabs as before 10-24's WarmPanels, their first draws the connect
// steps' (the "before" numbers); ORBIT_BOOTDRAW_BUDGET=<ms>: another budget than the app's (OrbitApp.WarmPanelsMs; 2000 warms every tab).
static class BootDrawTools
{
    public static int Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, Painter painter)
    {
        string want = Environment.GetEnvironmentVariable("ORBIT_BOOTDRAW_STYLE") ?? CyberHud.Id;
        string style = want is "modern" ? "modern" : want == CyberHud.Id ? CyberHud.Id : Array.Find(Skins.All, k => k.Tag == want || k.Id == want)?.Id;
        if (style == null) { Log.Error($"bootdraw: no style '{want}' (cyber, modern, 95, xp, aero, mac, term)"); return 1; }
        bool warm = Environment.GetEnvironmentVariable("ORBIT_BOOTDRAW_WARM") != "0";
        double budget = double.TryParse(Environment.GetEnvironmentVariable("ORBIT_BOOTDRAW_BUDGET"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double bms) ? bms : OrbitApp.WarmPanelsMs;
        System.Globalization.CultureInfo.CurrentCulture = Tools.SnapCulture;
        cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        var wc = cfg.Data.Wrist;
        wc.Face = "custom";
        wc.Background = "panel";
        wc.Grid = null;
        wc.Widgets = WatchWidgets.Seed(wc, "full");     // a new install's first page: the time, batteries, now playing, fps, gpu, buttons
        wc.Pages = null;
        wc.Page = 0;

        // the keyboard and the settings panel as OrbitApp makes them (the style applied first), the settings' previews drawn the way its
        // PreparePreviews draws them; made-up monitors, the host's a --snapshot one (Tools.FakeHost: nothing it does reaches anything)
        using var kb = new KeyboardPanel(gpu, painter, cfg);
        var host = Tools.BootHost(cfg, gpu, data, wrist, kb);
        using var settings = new SettingsPanel(gpu, painter, cfg, host, v =>
        {
            if (v.ShowsWristPreview) wrist.Draw(data);
            if (v.ShowsKeyboardPreview) kb.Draw();
        });

        // the warm-up as OrbitApp's start runs it: its thread from the top, the fonts once the style is known
        long w0 = Stopwatch.GetTimestamp();
        BootWarm.Start(capture: false);
        BootWarm.GpuReady(gpu);
        bool fonts = BootWarm.WaitFonts(20000);
        double fontMs = Stopwatch.GetElapsedTime(w0).TotalMilliseconds;

        using var query = gpu.Device.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.Event));
        void Idle()
        {
            // the last draw's GPU work done: the next one is timed alone
            gpu.Ctx.End(query);
            gpu.Ctx.Flush();
            while (gpu.Ctx.GetData(query, IntPtr.Zero, 0, Vortice.Direct3D11.AsyncGetDataFlags.None).Code != 0) Thread.SpinWait(50);
        }
        int frames = 0;
        double Frame(string what, Action draw)
        {
            Idle();
            Spikes.Begin();
            long t0 = Stopwatch.GetTimestamp();
            draw();
            Spikes.Piece(what, t0);
            Spikes.Mark(Spikes.Boot);
            Spikes.End();
            frames++;
            return Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }

        // the constructor's draws: SteamVR not there yet
        var bats = data.Vr.Batteries.ToArray();
        var since = data.VrSince;
        data.Vr.Batteries.Clear();
        data.VrSince = null;
        data.VrConnected = false;
        Spikes.BootBegin();
        double first = Frame("warm-up: the watch's first draw (WarmWatch)", () => wrist.Draw(data));
        int changes = cfg.Changes;
        bool dirty = cfg.Dirty;
        int tabs = SettingsView.TabCount, warmed = 0;
        var tabFirst = new double[tabs];
        double kbFirst = 0, tabsFirst = 0;
        if (warm)
        {
            kbFirst = Frame("warm-up: the keyboard's first draw (WarmPanels)", kb.Draw);
            tabsFirst = Frame("warm-up: the settings' first tabs (WarmPanels)", () => warmed = settings.WarmTabs(budget - kbFirst, tabFirst));
        }
        data.Vr.Batteries.AddRange(bats);
        data.VrSince = since;
        data.VrConnected = true;

        // the connect steps (BootTick), a frame each
        double step = Frame("boot: the watch's second draw", () => wrist.Draw(data));
        double kbStep = Frame(warm ? "boot: the keyboard's second draw" : "boot: the keyboard's first draw", kb.Draw);
        var tabStep = new double[tabs];
        for (int t = 0; t < tabs; t++)
        {
            int tab = t;
            tabStep[t] = Frame(t < warmed ? "boot: a settings tab drawn again" : "boot: a settings tab's first draw", () => settings.WarmTab(tab));
        }
        double later = Frame("a later draw", () => wrist.Draw(data));
        for (int i = frames; i < Spikes.BootFrames; i++) { Spikes.Begin(); Spikes.End(); }     // the boot line's other frames: empty (it is logged after the last)
        BootWarm.Stop();

        // the state afterwards: nothing written to the config, the panel still closed, the keyboard's picture at rest a fresh keyboard's
        bool cfgSame = cfg.Changes == changes && cfg.Dirty == dirty;
        kb.Draw();
        var kbPx = kb.Surf.ReadPixels(gpu);
        int kbDiff;
        using (var fresh = new KeyboardPanel(gpu, painter, cfg))
        {
            fresh.Draw();
            var fpx = fresh.Surf.ReadPixels(gpu);
            kbDiff = 0;
            for (int i = 0; i < kbPx.Length; i++) if (kbPx[i] != fpx[i]) kbDiff++;
        }
        bool stateOk = cfgSame && !settings.IsOpen && kbDiff == 0;

        bool ok = step <= first / 3;
        Log.Info($"bootdraw ({style}): the watch's first draw {first:0.0} ms - made before the connect now (OrbitApp.WarmWatch), the connect " +
                 $"step paid it before; the connect step's draw {step:0.0} ms, a later one {later:0.0} ms; the fonts warmed first {fonts} " +
                 $"({fontMs:0} ms) | the connect step's draw at most a third of the first {ok} (want True)");
        string Ms(double[] a, int from, int to)
        {
            var parts = new List<string>();
            for (int i = from; i < to; i++) parts.Add(a[i].ToString("0.0"));
            return parts.Count == 0 ? "-" : string.Join(" ", parts);
        }
        double Sum(double[] a, int from, int to) { double s = 0; for (int i = from; i < to; i++) s += a[i]; return s; }
        if (!warm)
        {
            // before WarmPanels: the connect steps pay the keyboard's and every tab's first draw
            Log.Info($"bootdraw ({style}, as before - no WarmPanels): the connect steps' first draws: the keyboard {kbStep:0.0} ms, the tabs " +
                     $"{Ms(tabStep, 0, tabs)} ms ({Sum(tabStep, 0, tabs):0.0} together, the first {tabStep[0]:0.0}) | nothing written to the config " +
                     $"{cfgSame}, the panel closed {!settings.IsOpen}, the keyboard's picture a fresh one's {kbDiff == 0} ({kbDiff} px differ) (want True)");
            return ok && stateOk ? 0 : 1;
        }
        bool kbOk = kbStep <= kbFirst / 2;
        bool tabOk = warmed >= 1 && tabStep[0] <= tabFirst[0] / 2 && Sum(tabStep, 0, warmed) < Sum(tabFirst, 0, warmed);
        Log.Info($"bootdraw ({style}): before the connect (WarmPanels, budget {budget:0} ms): the keyboard's first draw {kbFirst:0.0} ms, the " +
                 $"settings' first {warmed} of {tabs} tabs {tabsFirst:0.0} ms ({Ms(tabFirst, 0, warmed)}); the connect steps now: the keyboard " +
                 $"{kbStep:0.0} ms, the warmed tabs {Ms(tabStep, 0, warmed)} ms ({Sum(tabStep, 0, warmed):0.0} together), the others' first draws " +
                 $"{Ms(tabStep, warmed, tabs)} ms; {kbFirst + tabsFirst:0} ms added to the start | the keyboard's connect draw at most half of its " +
                 $"first {kbOk}, the first tab's at most half of its first and the warmed ones' less than theirs {tabOk} (want True)");
        Log.Info($"bootdraw ({style}): afterwards nothing written to the config {cfgSame}, the panel closed {!settings.IsOpen}, the keyboard's " +
                 $"picture a fresh keyboard's {kbDiff == 0} ({kbDiff} px differ) (want True)");
        return ok && kbOk && tabOk && stateOk ? 0 : 1;
    }
}

static partial class Tools
{
    // the bootdraw check's host (BootDrawTest.cs): Tools' made-up one, with made-up monitors as the Desktops tab shows them
    public static IOrbitHost BootHost(ConfigStore cfg, Gpu gpu, PanelData data, WristPanel wrist, KeyboardPanel kb) =>
        new FakeHost { Cfg = cfg, GpuName = gpu.AdapterName, MonitorList = GazeFocusTools.FakeMonitors(3), Data = data, Wrist = wrist, Keyboard = kb };
}
