using System;
using System.Diagnostics;
using System.Threading;

namespace Orbit;

// ORBIT_SNAP_ONLY=bootdraw --snapshot <dir>: the watch's first draws as a start makes them (Erik 10-24, the boot: line - "the watch's
// first draw" 106 ms in one frame after the connect, with the fonts warmed). A fresh process, as OrbitApp's start is: the warm-up's
// fonts on their own thread (Sys/BootWarm.cs, without its capture part), then the watch's first draw where the constructor makes it now
// (OrbitApp.WarmWatch: nothing from SteamVR yet - no batteries, no time in VR), then the connect step's (BootTick, with a connected
// start's data) and one more. Each is a frame of Spikes' boot line, so the log gets a "boot:" line made as the live one is: #0 the
// constructor's draw (no frame in the app), #1 the connect step's, #2 a later one. Its own "bootdraw:" line: the connect step's draw at
// most a third of the first one's - what the connect step paid before -, on WARP, where D2D's rasterizing is the CPU's too. Cyber,
// Erik's style; ORBIT_BOOTDRAW_STYLE = modern | 95 | xp | aero | mac | term for another (a run each: a first draw is once a process).
static class BootDrawTools
{
    public static int Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu)
    {
        string want = Environment.GetEnvironmentVariable("ORBIT_BOOTDRAW_STYLE") ?? CyberHud.Id;
        string style = want is "modern" ? "modern" : want == CyberHud.Id ? CyberHud.Id : Array.Find(Skins.All, k => k.Tag == want || k.Id == want)?.Id;
        if (style == null) { Log.Error($"bootdraw: no style '{want}' (cyber, modern, 95, xp, aero, mac, term)"); return 1; }
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
        double Frame(string what)
        {
            Idle();
            Spikes.Begin();
            long t0 = Stopwatch.GetTimestamp();
            wrist.Draw(data);
            Spikes.Piece(what, t0);
            Spikes.Mark(Spikes.Boot);
            Spikes.End();
            return Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }

        // the constructor's draw: SteamVR not there yet
        var bats = data.Vr.Batteries.ToArray();
        var since = data.VrSince;
        data.Vr.Batteries.Clear();
        data.VrSince = null;
        data.VrConnected = false;
        Spikes.BootBegin();
        double first = Frame("warm-up: the watch's first draw (WarmWatch)");
        data.Vr.Batteries.AddRange(bats);
        data.VrSince = since;
        data.VrConnected = true;
        double step = Frame("boot: the watch's second draw");
        double later = Frame("a later draw");
        for (int i = 3; i < Spikes.BootFrames; i++) { Spikes.Begin(); Spikes.End(); }     // the boot line's other frames: empty (it is logged after the last)
        BootWarm.Stop();

        bool ok = step <= first / 3;
        Log.Info($"bootdraw ({style}): the watch's first draw {first:0.0} ms - made before the connect now (OrbitApp.WarmWatch), the connect " +
                 $"step paid it before; the connect step's draw {step:0.0} ms, a later one {later:0.0} ms; the fonts warmed first {fonts} " +
                 $"({fontMs:0} ms) | the connect step's draw at most a third of the first {ok} (want True)");
        return ok ? 0 : 1;
    }
}
