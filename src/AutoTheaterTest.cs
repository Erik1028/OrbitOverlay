using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace Orbit;

// ORBIT_SNAP_ONLY=autotheater --snapshot <dir>: auto theater (Sys/AutoTheater.cs) without VR and without a real window - the decision
// fed made-up windows, monitors, players and a clock that steps 4 times a second as the app looks, as "want" lines; the look at a
// window (WindowLook) on the shell's own window for what it allocates (nothing is printed of it); the setting clicked in Modern and 95,
// its texts measured, and the Layouts page in every style: autotheater/*.png.
static class AutoTheaterTools
{
    // a made-up desk: a 2560 x 1440 monitor in the middle, a 1920 x 1080 one to its right (lower), one on the left without a panel
    static readonly List<AutoTheater.Screen> Desk = new()
    {
        new() { X = 0, Y = 0, W = 2560, H = 1440, Panel = true },
        new() { X = 2560, Y = 180, W = 1920, H = 1080, Panel = true },
        new() { X = -1920, Y = 0, W = 1920, H = 1080, Panel = false },
    };

    // full screen on monitor m (no caption); a normal window; maximised on the middle monitor (a 48 px taskbar: the invisible borders
    // 8 px past three edges)
    static AutoTheater.Win Full(nint h, string exe, int m)
    {
        var s = Desk[m];
        return new() { Hwnd = h, L = s.X, T = s.Y, R = s.X + s.W, B = s.Y + s.H, Shown = true, Process = exe };
    }
    static AutoTheater.Win Normal(nint h, string exe) => new() { Hwnd = h, L = 200, T = 150, R = 1500, B = 900, Framed = true, Shown = true, Process = exe };
    static AutoTheater.Win Maxed(nint h, string exe) => new() { Hwnd = h, L = -8, T = -8, R = 2568, B = 1400, Framed = true, Shown = true, Process = exe };

    // The app's side, made up: the windows on the desk now (by handle), the one in front, theater on / off as the app would turn it
    sealed class Rig
    {
        public readonly AutoTheater A = new();
        public readonly Dictionary<nint, AutoTheater.Win> Wins = new();
        public nint Front;
        public bool Playing = true, Enabled = true, Ready = true, TheaterOn, OnOurs = true, FailStart;
        public double T;
        public int Starts, Stops, StartMon = -1, Latches;
        public double StartAt = -1, StopAt = -1;

        public void Put(AutoTheater.Win w) => Wins[w.Hwnd] = w;
        AutoTheater.Win Get(nint h) => h != 0 && Wins.TryGetValue(h, out var w) ? w : default;

        public void Look()
        {
            var front = Get(Front);
            var watched = Get(A.Watch);
            bool playing = A.WantsPlaying(TheaterOn, Ready, front, Desk) && Playing;
            var act = A.Step(T, Enabled, Ready, TheaterOn, TheaterOn && OnOurs, front, watched, playing, Desk);
            if (A.LatchedNow) Latches++;
            if (act == AutoTheater.Act.Start) { Starts++; StartMon = A.Monitor; StartAt = T; TheaterOn = !FailStart; OnOurs = true; }
            if (act == AutoTheater.Act.Stop) { Stops++; StopAt = T; TheaterOn = false; }
        }

        // looks every 0.25 s for this long: Run(1.5) looks at +0, +0.25 ... +1.25 (the clock is then 1.5 s on)
        public void Run(double seconds)
        {
            int n = (int)Math.Round(seconds / AutoTheater.Every);
            for (int i = 0; i < n; i++) { Look(); T += AutoTheater.Every; }
        }
        public void Clear() { Starts = Stops = Latches = 0; StartAt = StopAt = -1; StartMon = -1; }
    }

    public static int Checks(string dir)
    {
        int problems = 0;
        void Want(string line, bool ok) { if (!ok) problems++; Log.Info(line); }
        const nint Chrome = 11, Vlc = 12, Game = 13, Notes = 14, Mpv = 15, Tv = 16;

        // ---- starts after the hold, on that monitor; stops 1 s after leaving full screen ----
        {
            var r = new Rig();
            r.Put(Normal(Chrome, "chrome"));
            r.Front = Chrome;
            r.Run(3);
            bool notYet = r.Starts == 0;
            double t0 = r.T;
            r.Put(Full(Chrome, "chrome", 0));
            r.Run(1.5);
            bool noEarly = r.Starts == 0;
            r.Run(0.25);
            bool at = r.Starts == 1 && r.StartMon == 0 && Math.Abs(r.StartAt - t0 - 1.5) < 1e-9;
            r.Run(20);
            bool stays = r.Starts == 1 && r.Stops == 0 && r.TheaterOn;
            double t1 = r.T;
            r.Put(Maxed(Chrome, "chrome"));                     // Esc: back to the maximised window
            r.Run(1.0);
            bool noEarlyStop = r.Stops == 0;
            r.Run(0.25);
            bool stopped = r.Stops == 1 && Math.Abs(r.StopAt - t1 - 1.0) < 1e-9 && !r.TheaterOn;
            r.Run(10);
            bool once = r.Starts == 1 && r.Stops == 1 && r.Latches == 0;
            Want($"autotheater hold: a normal window starts nothing {notYet} (want True); full screen and playing - nothing in the first 1.5 s {noEarly} " +
                 $"(want True), at 1.5 s on the middle monitor {at} (want True), on while it stays {stays} (want True); out of full screen - nothing for " +
                 $"1 s {noEarlyStop} (want True), at 1 s theater off {stopped} (want True), and that is all {once} (want True)",
                notYet && noEarly && at && stays && noEarlyStop && stopped && once);
        }

        // ---- not for a game, not for a page in F11, not while paused (a paused video goes as soon as it plays) ----
        {
            var r = new Rig();
            r.Put(Full(Game, "cyberpunk2077", 0));
            r.Front = Game;
            r.Run(10);
            r.Put(Full(Game, "vrchat", 0));
            r.Run(10);
            bool game = r.Starts == 0;
            r.Put(Full(Chrome, "chrome", 0));
            r.Front = Chrome;
            r.Playing = false;
            r.Run(10);
            bool page = r.Starts == 0;
            r.Put(Full(Vlc, "vlc", 1));
            r.Front = Vlc;
            r.Run(5);
            bool paused = r.Starts == 0;
            double t0 = r.T;
            r.Playing = true;
            r.Run(0.25);
            bool plays = r.Starts == 1 && r.StartMon == 1 && Math.Abs(r.StartAt - t0) < 1e-9;
            Want($"autotheater what plays: a game in full screen (music playing) never {game} (want True); a browser in full screen with nothing playing " +
                 $"never {page} (want True); a paused video in full screen waits {paused} (want True) and goes the moment it plays, on its monitor {plays} " +
                 "(want True)", game && page && paused && plays);
        }

        // ---- ended by hand while still full screen: held back until that full screen ends (a blip does not end it) ----
        {
            var r = new Rig();
            r.Put(Full(Vlc, "vlc", 1));
            r.Front = Vlc;
            r.Run(2);
            bool on = r.Starts == 1;
            r.TheaterOn = false;                                // the bar's Exit
            r.Run(10);
            bool held = r.Starts == 1 && r.Latches == 1;
            r.Put(Normal(Vlc, "vlc"));                          // half a second out of full screen and back
            r.Run(0.5);
            r.Put(Full(Vlc, "vlc", 1));
            r.Run(5);
            bool blip = r.Starts == 1;
            r.Put(Normal(Vlc, "vlc"));                          // really out of it
            r.Run(2);
            double t0 = r.T;
            r.Put(Full(Vlc, "vlc", 1));
            r.Run(1.5);
            bool waits = r.Starts == 1;
            r.Run(0.25);
            bool again = r.Starts == 2 && Math.Abs(r.StartAt - t0 - 1.5) < 1e-9;
            // another full-screen video while one is held back: that one may start
            var r2 = new Rig();
            r2.Put(Full(Vlc, "vlc", 1));
            r2.Front = Vlc;
            r2.Run(2);
            r2.TheaterOn = false;
            r2.Run(1);
            r2.Put(Full(Mpv, "mpv", 0));
            r2.Front = Mpv;
            r2.Run(2);
            bool other = r2.Starts == 2 && r2.StartMon == 0;
            Want($"autotheater by hand: on {on} (want True); Exit while the video stays full screen - not again {held} (want True), not after a half-second " +
                 $"blip out of full screen {blip} (want True); out of it for good, then full screen again - the hold {waits} (want True), then on {again} " +
                 $"(want True); another video meanwhile may start {other} (want True)", on && held && blip && waits && again && other);
        }

        // ---- no flapping: a quick alt-tab away (or a player minimising for a moment) keeps it; a long one too while the video stays full screen ----
        {
            var r = new Rig();
            r.Put(Full(Chrome, "chrome", 0));
            r.Put(Normal(Notes, "notepad"));
            r.Front = Chrome;
            r.Run(2);
            bool on = r.Starts == 1;
            r.Front = Notes;
            r.Run(0.5);
            r.Front = Chrome;
            r.Run(3);
            var min = Full(Chrome, "chrome", 0);
            min.Shown = false;                                  // an exclusive-mode player minimises while alt-tabbed
            r.Put(min);
            r.Front = Notes;
            r.Run(0.75);
            r.Put(Full(Chrome, "chrome", 0));
            r.Front = Chrome;
            r.Run(3);
            bool quick = r.Stops == 0 && r.TheaterOn;
            r.Front = Notes;                                    // a window clicked on another monitor: the video stays full screen on its own
            r.Run(30);
            bool longer = r.Stops == 0 && r.TheaterOn && r.Starts == 1;
            // while it waits: full screen 1 s, alt-tab away for 0.5 s, back - the hold starts over, one start
            var h = new Rig();
            h.Put(Full(Chrome, "chrome", 0));
            h.Put(Normal(Notes, "notepad"));
            h.Front = Chrome;
            h.Run(1);
            h.Front = Notes;
            h.Run(0.5);
            double t0 = h.T;
            h.Front = Chrome;
            h.Run(1.5);
            bool restarted = h.Starts == 0;
            h.Run(0.25);
            bool one = h.Starts == 1 && Math.Abs(h.StartAt - t0 - 1.5) < 1e-9;
            // passing through a full-screen video with alt-tab (half a second in front): nothing
            var p = new Rig();
            p.Put(Full(Vlc, "vlc", 1));
            p.Put(Normal(Notes, "notepad"));
            for (int i = 0; i < 5; i++) { p.Front = Vlc; p.Run(0.5); p.Front = Notes; p.Run(1); }
            bool through = p.Starts == 0;
            Want($"autotheater flapping: on {on} (want True); alt-tab away for 0.5 s, a player minimised for 0.75 s - still on {quick} (want True); " +
                 $"another window in front for 30 s while the video stays full screen - still on {longer} (want True); an alt-tab during the hold starts it " +
                 $"over {restarted} (want True) and it starts once, 1.5 s after {one} (want True); passing through a full-screen video starts nothing {through} " +
                 "(want True)", on && quick && longer && restarted && one && through);
        }

        // ---- what is full screen ----
        {
            var hide = Full(Mpv, "mpv", 0);
            hide.Shown = false;
            var span = new AutoTheater.Win { Hwnd = Mpv, L = 0, T = 0, R = 4480, B = 1440, Shown = true, Process = "mpv" };
            var autoHide = new AutoTheater.Win { Hwnd = Chrome, L = -8, T = -8, R = 2568, B = 1448, Framed = true, Shown = true, Process = "chrome" };
            var over = new AutoTheater.Win { Hwnd = Mpv, L = -6, T = -6, R = 2566, B = 1446, Shown = true, Process = "mpv" };
            var tv = Full(Tv, "video.ui", 1);
            tv.Framed = true;                                   // Movies & TV: its frame keeps a caption style, exactly on the monitor
            bool maxed = AutoTheater.FullscreenOn(Maxed(Chrome, "chrome"), Desk) < 0 && AutoTheater.FullscreenOn(autoHide, Desk) < 0;
            bool spans = AutoTheater.FullscreenOn(span, Desk) < 0;
            bool hidden = AutoTheater.FullscreenOn(hide, Desk) < 0;
            bool noPanel = AutoTheater.FullscreenOn(Full(Mpv, "mpv", 2), Desk) == 2 && AutoTheater.Candidate(Full(Mpv, "mpv", 2), Desk) < 0;
            bool overOk = AutoTheater.FullscreenOn(over, Desk) == 0 && AutoTheater.Candidate(tv, Desk) == 1;
            var r = new Rig();
            r.Put(Full(Mpv, "mpv", 2));
            r.Front = Mpv;
            r.Run(5);
            bool none = r.Starts == 0;
            Want($"autotheater full screen: a maximised window (also with the taskbar hidden) is not {maxed} (want True), one over two monitors is not " +
                 $"{spans} (want True), a minimised one is not {hidden} (want True); on a monitor without a panel - seen but no theater {noPanel && none} " +
                 $"(want True); a player a few px over the edges, Movies & TV's framed full screen are {overOk} (want True)",
                maxed && spans && hidden && noPanel && none && overOk);
        }

        // ---- yours: a theater started by hand, or switched to another screen, is never stopped by it; the setting off / the headset off ----
        {
            var r = new Rig();
            r.Put(Full(Chrome, "chrome", 0));
            r.Front = Chrome;
            r.TheaterOn = true;                                 // started from the watch before the hold was over
            r.OnOurs = false;
            r.Run(5);
            r.Put(Maxed(Chrome, "chrome"));
            r.Run(3);
            bool manual = r.Starts == 0 && r.Stops == 0 && r.TheaterOn;
            r.Put(Full(Chrome, "chrome", 0));
            r.Run(1);
            r.TheaterOn = false;                                // Exit by hand over the full-screen video
            r.Run(10);
            bool heldManual = r.Starts == 0 && r.Latches == 1;

            var s = new Rig();
            s.Put(Full(Chrome, "chrome", 0));
            s.Front = Chrome;
            s.Run(2);
            s.OnOurs = false;                                   // the bar's switcher: another monitor plays now
            s.Run(1);
            s.Put(Maxed(Chrome, "chrome"));
            s.Run(5);
            bool switched = s.Starts == 1 && s.Stops == 0 && s.TheaterOn;

            var o = new Rig();
            o.Put(Full(Chrome, "chrome", 0));
            o.Front = Chrome;
            o.Run(2);
            o.Enabled = false;                                  // the setting turned off while its theater plays
            o.Put(Maxed(Chrome, "chrome"));
            o.Run(5);
            bool off = o.Starts == 1 && o.Stops == 0 && o.TheaterOn;

            var hs = new Rig { Ready = false };                 // the headset off: nothing starts
            hs.Put(Full(Chrome, "chrome", 0));
            hs.Front = Chrome;
            hs.Run(5);
            bool notWorn = hs.Starts == 0;
            double t0 = hs.T;
            hs.Ready = true;
            hs.Run(1.75);
            bool worn = hs.Starts == 1 && Math.Abs(hs.StartAt - t0 - 1.5) < 1e-9;
            hs.Ready = false;                                   // ...but its theater still ends with the video
            hs.Put(Maxed(Chrome, "chrome"));
            hs.Run(1.5);
            bool endsOff = hs.Stops == 1;

            var f = new Rig { FailStart = true };               // theater could not start (no headset pose): once, not 4 times a second
            f.Put(Full(Chrome, "chrome", 0));
            f.Front = Chrome;
            f.Run(10);
            bool failed = f.Starts == 1;
            Want($"autotheater yours: a theater started by hand is never stopped by it {manual} (want True), its Exit over the video holds it back " +
                 $"{heldManual} (want True); switched to another screen - not stopped when the video leaves full screen {switched} (want True); the setting " +
                 $"turned off - not stopped either {off} (want True); headset off - nothing starts {notWorn} (want True), on again - after the hold {worn} " +
                 $"(want True), a stop still happens {endsOff} (want True); a start that did not come on is not tried again {failed} (want True)",
                manual && heldManual && switched && off && notWorn && worn && endsOff && failed);
        }

        // ---- whose media session plays ----
        {
            bool yes = AutoTheater.SessionIs("chrome", "Chrome") && AutoTheater.SessionIs("chrome", "Chrome._crx_agimnkijcaahngcdmfeangaknmldooml") &&
                       AutoTheater.SessionIs("msedge", "MSEdge") && AutoTheater.SessionIs("firefox", "308046B0AF4A39CB") &&
                       AutoTheater.SessionIs("vlc", "vlc.exe") && AutoTheater.SessionIs("mpc-hc64", "mpc-hc64.exe") &&
                       AutoTheater.SessionIs("video.ui", "Microsoft.ZuneVideo_8wekyb3d8bbwe!Microsoft.ZuneVideo") &&
                       AutoTheater.SessionIs("microsoft.media.player", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic");
            bool no = !AutoTheater.SessionIs("chrome", "Spotify.exe") && !AutoTheater.SessionIs("firefox", "Spotify.exe") &&
                      !AutoTheater.SessionIs("msedge", "308046B0AF4A39CB") && !AutoTheater.SessionIs("cyberpunk2077", "Chrome") &&
                      !AutoTheater.SessionIs("chrome", null) && !AutoTheater.SessionIs(null, "Chrome");
            bool list = AutoTheater.Allowed("VLC") && AutoTheater.Allowed("potplayermini64") && !AutoTheater.Allowed("vrchat") &&
                        !AutoTheater.Allowed("explorer") && !AutoTheater.Allowed(null);
            Want($"autotheater sessions: a player's own session counts (Chrome, a Chrome app, Edge, Firefox's hash, VLC, MPC-HC, Movies & TV, Media " +
                 $"Player) {yes} (want True); another app's does not (Spotify behind Chrome or Firefox, Firefox's hash for Edge, a game) {no} (want True); " +
                 $"the allow-list: players and browsers, not games or Explorer {list} (want True)", yes && no && list);
        }

        // ---- a look allocates nothing: the decision 4000 times; WindowLook on the shell's window (Explorer's desktop) 400 times ----
        {
            var r = new Rig();
            AutoTheater.Win cf = Full(Chrome, "chrome", 0), cm = Maxed(Chrome, "chrome");
            r.Put(cf);
            r.Put(Full(Vlc, "vlc", 1));
            r.Put(Normal(Notes, "notepad"));
            r.Front = Chrome;
            r.Run(3);
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                r.Put((i / 25) % 2 == 0 ? cf : cm);             // in and out of full screen every 25 s
                r.Front = (i / 60) % 3 == 2 ? Notes : (i / 60) % 3 == 1 ? Vlc : Chrome;
                if (i % 200 == 100) r.TheaterOn = false;        // now and then an Exit by hand
                r.Run(AutoTheater.Every * 4);
            }
            long steps = GC.GetAllocatedBytesForCurrentThread() - b0;
            bool moved = r.Starts > 2;
            var shell = WindowLook.GetShellWindow();
            long looks = -1;
            if (shell != IntPtr.Zero)
            {
                var w = default(AutoTheater.Win);
                WindowLook.Look(shell, ref w);
                long l0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 400; i++) WindowLook.Look(shell, ref w);
                looks = GC.GetAllocatedBytesForCurrentThread() - l0;
            }
            Want($"autotheater cost: 4000 looks of the decision (starting and stopping {r.Starts} times) allocate {steps} bytes (want 0); 400 looks at a " +
                 $"window allocate {(looks < 0 ? "- (no shell window here)" : looks.ToString())} bytes (want 0)", steps == 0 && moved && looks <= 0);
        }

        // ---- the setting: off writes nothing (a config saves as before), on is written ----
        {
            var store = new ConfigStore(Path.Combine(dir, "autotheater_config.json"));
            bool clean = !store.Serialized().Contains("\"Auto\"");
            store.Data.Theater.Auto = true;
            bool written = store.Serialized().Contains("\"Auto\"");
            Want($"autotheater config: off writes nothing {clean} (want True), on is written {written} (want True)", clean && written);
        }
        Log.Info($"autotheater: {(problems == 0 ? "every check as wanted" : $"{problems} checks NOT as wanted")}");
        return problems;
    }

    // the setting in the Layouts page: clicked in Modern and in the 95 sheet, its texts against their boxes (Modern, Cyber), and the page
    // in every style with it on
    public static int Pictures(SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg, string dir)
    {
        int problems = 0;
        void Want(string line, bool ok) { if (!ok) problems++; Log.Info(line); }
        string adir = Path.Combine(dir, "autotheater");
        Directory.CreateDirectory(adir);
        var look0 = cfg.Data.Appearance;
        var t = cfg.Data.Theater;
        bool auto0 = t.Auto;
        void Look(string style) { cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true }; Theme.Apply(cfg.Data.Appearance); }
        void FrameM() { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
        void ClickM(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); FrameM(); ptr.Press(); FrameM(); ptr.Release(); FrameM(); }
        void Frame95() => view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
        void Click95(float x, float y) { ptr.Move(x * 2, y * 2); Frame95(); ptr.Press(); Frame95(); ptr.Release(); Frame95(); Frame95(); }
        void Settle(Action frame) { ptr.Leave(); frame(); ui.ResetAnimations(); frame(); }
        void Save(string name) => surf.SavePng(gpu, Path.Combine(adir, name + ".png"));
        try
        {
            view.Tab = SettingsView.TabLayouts;
            // Modern: the switch (third on the left under Theater mode), on and off again
            Look("modern");
            t.Auto = false;
            ui.ResetAnimations();
            ClickM(222 + 181, 570 + 24);
            bool onM = t.Auto;
            Settle(FrameM);
            Save("layouts_modern");
            ClickM(222 + 181, 570 + 24);
            bool offM = !t.Auto;
            // 95: the check box under the sliders
            Look("classic");
            t.Auto = false;
            ui.ResetAnimations();
            Click95(28 + 8, 408 + 6);
            bool on95 = t.Auto;
            Click95(28 + 8, 408 + 6);
            bool off95 = !t.Auto;
            Want($"autotheater settings: Modern's switch turns it on {onM} (want True) and off {offM} (want True); the 95 check box on {on95} (want True) " +
                 $"and off {off95} (want True)", onM && offM && on95 && off95);

            // the texts of the theater part against their boxes, Modern and Cyber (Painter.FitSpy, as the extras' check does)
            t.Auto = true;
            foreach (var (style, tag) in new[] { ("modern", ""), (CyberHud.Id, "_cyber") })
            {
                Look(style);
                var spied = new List<(string s, RectangleF r, float size, Vortice.DirectWrite.FontWeight w, string fam, bool wrap,
                    Vortice.DirectWrite.TextAlignment h, Vortice.DirectWrite.ParagraphAlignment v, float tr)>();
                painter.FitSpy = (s, r, size, w, fam, wrap, h, v, tr) => spied.Add((s, r, size, w, fam, wrap, h, v, tr));
                ptr.Leave();
                ui.ResetAnimations();
                FrameM();
                painter.FitSpy = null;
                Save("layouts" + (tag.Length == 0 ? "_modern_fit" : tag));
                int bad = 0;
                var area = new RectangleF(222, 440, 780, 200);
                foreach (var x in spied)
                {
                    if (x.fam == Theme.IconFont || string.IsNullOrWhiteSpace(x.s) || !area.IntersectsWith(x.r)) continue;
                    float o = painter.Overhang(x.s, x.r, x.size, x.w, x.fam, x.wrap, x.h, x.v, x.tr);
                    if (o >= 1) { bad++; Log.Warn($"autotheater fit {style}: '{x.s}' sticks out {o:0.#} px of its {x.r.Width:0} x {x.r.Height:0} box"); }
                }
                Want($"autotheater fit {style}{(Lang.English ? "" : " " + Lang.Code)}: the theater part's texts sticking out of their box {bad} (want 0)", bad == 0);
            }
            // every retro style: the Layouts sheet with it ticked
            foreach (var sk in Skins.All)
            {
                Look(sk.Id);
                Settle(Frame95);
                Save("layouts_" + sk.Tag);
            }
        }
        finally
        {
            t.Auto = auto0;
            cfg.Data.Appearance = look0;
            Theme.Apply(look0);
            view.Tab = 0;
            ptr.Leave();
            ui.ResetAnimations();
        }
        return problems;
    }
}

static partial class Tools
{
    // ORBIT_SNAP_ONLY=autotheater: the decision's checks, the setting clicked and its pictures in every style - in seconds, no monitor captured
    static int AutoTheaterOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = SnapCulture;
        int problems = AutoTheaterTools.Checks(dir);
        var host = new FakeHost { Cfg = cfg, GpuName = gpu.AdapterName, MonitorList = new List<MonitorInfo>(), Data = data, Wrist = wrist };
        using (var surf = new Surface(gpu, 1500, 960))
            problems += AutoTheaterTools.Pictures(new SettingsView(host), new Ui(painter), surf, new PointerState(), painter, gpu, cfg, dir);
        CultureInfo.CurrentCulture = culture;
        Log.Info($"snapshot (auto theater only) written to {dir}: {(problems == 0 ? "every check as wanted" : $"{problems} NOT as wanted")}");
        return problems == 0 ? 0 : 1;
    }
}
