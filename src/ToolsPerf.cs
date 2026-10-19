using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace Orbit;

// ORBIT_SNAP_ONLY=perf --snapshot <dir>: what a frame of every panel costs and allocates, in Modern, Cyber and Windows 95 - each the
// best of 3 rounds (the PC's load swings ±0.5 ms), CPU = drawing + hand-over with the GPU idle before each frame (FrameCost):
//  - the watch with your own face (from a COPY of the config, like --perfbench): at rest (what a redraw costs - the clock's second,
//    the 0.5 s rule) and animating (the cascade / Cyber's boot as the watch comes into view, paced at 90 Hz);
//  - the keyboard at rest and typing (a key let go: the frames of its glow);
//  - the settings as the desktop window and as the VR panel (General tab: at rest, a switch under the pointer);
//  - the theater bar, the notifications, the room and the dock panels;
//  - the main loop's capture polls (every monitor, read only: what --snapshot captures anyway).
// ORBIT_PERF_TYPES=1: what each case allocates, by type (the runtime's allocation samples, one per ~100 KB).
static partial class Tools
{
    static int PerfOnly(SettingsView view, SettingsView panelView, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu,
        ConfigStore cfg, KeyboardPanel kb, PanelData data, string dir, IReadOnlyList<MonitorInfo> mons, Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice dev)
    {
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        int failed0 = WantsFailed;
        bool still = Motion.Still;
        Motion.Still = false;                          // the loops run as on the headset
        bool types = Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1";
        var rows = new List<(string what, string style, double ms, long bytes, double layouts)>();
        void Row(string what, string style, (double ms, long bytes) c, double layouts = -1)
        {
            rows.Add((what, style, c.ms, c.bytes, layouts));
            Log.Info($"perf: {style,-6} {what,-34} {c.ms,7:0.000} ms {c.bytes,7} B a frame" + (layouts >= 0 ? $", {layouts:0.0} text layouts made" : "")
                     + (_pacedWorst > 0 ? $", worst frame {_pacedWorst:0.000} ms (frame {_pacedWorstAt})" : ""));
            _pacedWorst = 0;
        }
        // a case's cost, and the text layouts it makes a frame (a layout made is ~50 us: none at rest is the goal)
        (double ms, long bytes) Cost(string what, string style, Action draw, Action start = null, int n = 60, bool paced = false)
        {
            int made0 = painter.LayoutsMade, frames0 = _perfFrames;
            var c = paced ? PacedCost(gpu, start, draw, n) : FrameCost(gpu, () => { start?.Invoke(); draw(); _perfFrames++; }, n);
            if (paced) _perfFrames += 3 * n;
            double lay = _perfFrames > frames0 ? (painter.LayoutsMade - made0) / (double)(_perfFrames - frames0) : 0;
            Row(what, style, c, lay);
            if (types) AllocTypes(what + " " + style, draw, start);
            return c;
        }

        PanelCornersCheck(panelView, ui, surf, ptr, painter, gpu, cfg);
        PixelTextCheck(view, ui, surf, ptr, painter, gpu, cfg, kb, data);
        KeyboardBaseCheck(kb, gpu, cfg);

        // the watch: your own face, on a copy of your config
        string real = Path.Combine(Log.Dir, "config.json"), copy = Path.Combine(dir, "perf_config.json");
        if (File.Exists(real)) File.Copy(real, copy, true);
        var mine = new ConfigStore(copy);
        WatchPages.Migrate(mine);
        var look = mine.Data.Appearance;           // your colours and motion; the style set per round
        string style0 = look.Style;
        using var wrist = new WristPanel(gpu, painter, mine);
        wrist.Mic.Fake = true;
        wrist.Mic.Present = true;
        wrist.CyberLive = true;
        var wdata = new PanelData { VrConnected = true, VramTotalGB = 16, FakeGpuLoad = 63, FakeVram = 9.4f, VrSince = DateTime.Now.AddMinutes(-83) };
        wdata.Vr.AppFps = 86; wdata.Vr.ReprojPct = 4; wdata.Vr.GpuMs = 9.3f; wdata.Vr.DisplayHz = 90;
        wdata.Vr.Batteries.Add(("HMD", 0.71f, false));
        wdata.Vr.Batteries.Add(("L", 0.82f, false));
        wdata.Vr.Batteries.Add(("R", 0.18f, true));
        wdata.FakeMedia = (true, "Midnight City", "M83", true);
        for (int i = 0; i < VrStats.FrameHistory; i++) wdata.Vr.FrameMs[i] = 7.5f + (i % 7) * 0.3f;
        wdata.Vr.FrameHead = 0; wdata.Vr.FrameCount = VrStats.FrameHistory;
        wdata.Room = RoomViewTools.Room(12, 2);
        wdata.Notes = NotesViewTools.Fake();
        wdata.Photos = PhotoFake.Snap();                // made-up photos (PhotoViewTest.cs)
        wdata.FakeArt = FakeCover();                    // as in VR: a cover, Hexpad's keys
        using var wfaces = new HexpadFaces();
        wdata.Dock = DockViewTools.Fake();
        wfaces.Update(painter, wdata.Dock);
        wdata.DockFaces = wfaces;
        Log.Info($"perf: the watch is your face '{mine.Data.Wrist.Face}', {WatchPages.Count(mine.Data.Wrist)} page(s), page 1: " +
                 string.Join(" ", (mine.Data.Wrist.Widgets ?? new List<WatchWidget>()).Select(w => $"{w.Type}{w.W}x{w.H}")));

        using var np = new NotificationPanel(gpu, painter, cfg);
        np.AddForPreview(new Notification { Source = "VRCX", Title = "Friend online", Content = "Sam has joined The Black Cat (Friends+)" });
        np.AddForPreview(new Notification { Source = "Discord", Title = "New message", Content = "Alex: are you in VR? The new watch looks great." });
        using var room = new RoomPanel(gpu, painter, cfg);
        var roomSnap = RoomViewTools.Room(12, 2);
        using var faces = new HexpadFaces();
        var fake = DockViewTools.Fake();
        faces.Update(painter, fake);
        using var dock = new DockPanel(gpu, painter, cfg, null, faces);
        using var bar = new TheaterBar(gpu, painter, cfg);
        var screenB = new TheaterTest.FakeScreen { Name = "LG ULTRAGEAR" };
        var choices = new List<ITheaterScreen> { new TheaterTest.FakeScreen { Name = "DELL U2723QE" }, screenB,
            new TheaterTest.FakeScreen { Name = "YouTube - Lofi hip hop radio - beats to relax/study to - Mozilla Firefox" } };
        var tview = new TheaterView { Size = 2.8f };

        foreach (string style in new[] { null, CyberHud.Id, "classic" })
        {
            string name = style == null ? "Modern" : style == CyberHud.Id ? "Cyber" : "95";
            look.Style = style;
            Theme.Apply(look);
            CyberFx.Live = true;

            // ---- the watch ----
            mine.Data.Wrist.Page = 0;
            wrist.Settle();
            wrist.Draw(wdata);
            Cost("watch at rest", name, () => wrist.Draw(wdata));
            Cost("watch animating (comes into view)", name, () => wrist.Draw(wdata), () => { wrist.Settle(); wrist.Draw(wdata); wrist.Intro(); }, 54, paced: true);
            wrist.Settle();

            // ---- the keyboard ----
            kb.Draw();
            Cost("keyboard at rest", name, kb.Draw);
            Cost("keyboard typing (a key let go)", name, kb.Draw, () => { kb.HoldForTest(30, true); kb.Draw(); kb.HoldForTest(30, false); }, 12, paced: true);

            // ---- the settings: the desktop window and the VR panel, General ----
            void Window()
            {
                if (Theme.Classic) { view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal); return; }      // the sheet, as the window shows it
                ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End();
            }
            void Panel()
            {
                if (Theme.Classic) { panelView.Render95(ui, ptr, surf.Bitmap, SettingsPanel.TW / SettingsView.W95W, 0, 0, 0x00000000); return; }
                ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, 0x00000000, 1.5f); SettingsPanel.DrawBody(painter, ui, panelView); painter.End(); ui.End();
            }
            view.Tab = panelView.Tab = SettingsView.TabGeneral;
            foreach (var (what, draw) in new (string, Action)[] { ("settings window", Window), ("settings VR panel", Panel) })
            {
                ptr.Leave();
                ui.ResetAnimations();
                draw(); draw();
                Cost(what + " at rest", name, draw);
                ptr.Move((222 + 200) * 1.5f, (196 + 22) * 1.5f);         // over the Haptic ticks switch
                draw(); draw();
                Cost(what + ", a switch hovered", name, draw);
                ptr.Leave();
            }

            // ---- the small panels ----
            Cost("theater bar (switcher)", name, () => bar.DrawFor(choices, screenB, tview));
            Cost("notifications (2 cards)", name, np.Draw);
            Cost("room panel", name, () => room.DrawFor(roomSnap, DateTime.Now));
            Cost("dock panel", name, () => dock.DrawFor(fake, Clock.Now));
            CyberFx.Live = false;
        }
        look.Style = style0;
        Theme.Apply(cfg.Data.Appearance);

        // ---- the capture polls: each monitor at 90 Hz for 2 s (a poll with a new frame copies it; without one it only asks) ----
        foreach (var m in mons)
        {
            try
            {
                using var cap = new MonitorCapture(gpu, dev, m);
                for (int i = 0; i < 100 && !cap.Poll(); i++) Thread.Sleep(20);
                double got = 0, none = 0, worst = 0;
                int nGot = 0, nNone = 0;
                long alloc = 0;
                double t0 = Clock.Now, next = t0;
                while (Clock.Now - t0 < 2)
                {
                    next += 1 / 90.0;
                    while (Clock.Now < next) Thread.Sleep(1);
                    long m0 = GC.GetAllocatedBytesForCurrentThread();
                    long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    bool f = cap.Poll();
                    gpu.Ctx.Flush();
                    double ms = System.Diagnostics.Stopwatch.GetElapsedTime(s0).TotalMilliseconds;
                    alloc += GC.GetAllocatedBytesForCurrentThread() - m0;
                    if (f) { got += ms; nGot++; } else { none += ms; nNone++; }
                    worst = Math.Max(worst, ms);
                }
                Log.Info($"perf: capture {m.Name,-14} {nGot} new frames {(nGot > 0 ? got / nGot : 0):0.000} ms a poll, {nNone} without {(nNone > 0 ? none / nNone : 0):0.000} ms, " +
                         $"{alloc / Math.Max(1, nGot + nNone)} B a poll, worst {worst:0.000} ms");
            }
            catch (Exception e) { Log.Error($"perf: capture {m}: {e.Message}"); }
        }

        PacerCost();
        CursorCost(gpu);
        VolumeCost();
        ConfigWriterCheck(dir);

        File.WriteAllLines(Path.Combine(dir, "perf.tsv"), rows.Select(r => $"{r.style}\t{r.what}\t{r.ms:0.000}\t{r.bytes}\t{r.layouts:0.0}"));
        Motion.Still = still;
        return WantsExit(failed0);
    }

    static int _perfFrames;
    static double _pacedWorst;          // a paced case's slowest frame (the best round's), and which one it was
    static int _pacedWorstAt;

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    // the config written on a worker (Config.cs, the app's way) - on a copy in the snapshot folder: what a save costs the main loop, the
    // newest settings on disk after a save in the background and a forced one right behind it (never an older text over a newer)
    static void ConfigWriterCheck(string dir)
    {
        string path = Path.Combine(dir, "writer_config.json"), real = Path.Combine(Log.Dir, "config.json");
        if (File.Exists(real)) File.Copy(real, path, true);
        var store = new ConfigStore(path) { Background = true };
        float Read() { try { return new ConfigStore(path).Data.Theater.Width; } catch { return -1; } }
        store.Data.Theater.Width = 1.11f;
        store.MarkDirty();
        Thread.Sleep(1100);                                       // (a quiet second: a save is due)
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        store.SaveIfDue();
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        store.Data.Theater.Width = 2.22f;                         // a change right behind it, saved by force (closing, Apply)
        store.MarkDirty();
        store.SaveIfDue(force: true);
        Thread.Sleep(300);                                        // (the worker done)
        float after = Read();
        store.Data.Theater.Width = 3.33f;                         // and one more through the worker alone
        store.MarkDirty();
        Thread.Sleep(1100);
        store.SaveIfDue();
        Thread.Sleep(300);
        float last = Read();
        Log.Info($"perf: config save on the main loop {ms:0.000} ms (written on its worker)");
        Want($"config: a save in the background then a forced one: on disk {after:0.00} (want 2.22), then the worker's {last:0.00} (want 3.33), " +
             $"the main loop's part {ms:0.000} ms (want under 3)", MathF.Abs(after - 2.22f) < 0.001f && MathF.Abs(last - 3.33f) < 0.001f && ms < 3);
    }

    // the watch's volume (Sys/SystemStats.cs): read in the frame (the tools' way) and asked of its worker (the app's) - read only
    static void VolumeCost()
    {
        double direct;
        using (var v = new SystemVolume())
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            v.Sample(Clock.Now);
            direct = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }
        using var bg = new SystemVolume { Background = true };
        long a0 = System.Diagnostics.Stopwatch.GetTimestamp();
        bg.Sample(Clock.Now);
        double ask = System.Diagnostics.Stopwatch.GetElapsedTime(a0).TotalMilliseconds;
        double w0 = Clock.Now;
        while (bg.Level < 0 && Clock.Now - w0 < 3) Thread.Sleep(5);
        double read = (Clock.Now - w0) * 1000;
        Log.Info($"perf: volume read in the frame {direct:0.000} ms; asked of its worker {ask:0.000} ms, there {read:0} ms later (level {bg.Level:0.00})");
        Want($"volume: asking its worker {ask:0.000} ms (want under 0.5), it read the device {bg.Level >= 0} (want True)", ask < 0.5 && bg.Level >= 0);
    }

    // the laser's Windows cursor (Vr/SystemCursor.cs): a shape's first time (drawn) and again (kept) - the system's own cursors
    static void CursorCost(Gpu gpu)
    {
        int[] ids = { 32512, 32513, 32649, 32644, 32645, 32646, 32642, 32643, 32515 };     // arrow, I-beam, hand, the sizes, cross
        using var cur = new SystemCursor(gpu);
        double first = 0, again = 0, worst = 0;
        long bytes = 0;
        for (int round = 0; round < 2; round++)
            foreach (int id in ids)
            {
                var h = LoadCursor(IntPtr.Zero, (IntPtr)id);
                long m0 = GC.GetAllocatedBytesForCurrentThread();
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                cur.Use(h);
                gpu.Ctx.Flush();
                double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                if (round == 0) { first += ms; worst = Math.Max(worst, ms); }
                else { again += ms; bytes += GC.GetAllocatedBytesForCurrentThread() - m0; }
            }
        Log.Info($"perf: cursor shape, first time {first / ids.Length:0.000} ms (worst {worst:0.000}), again {again / ids.Length:0.000} ms " +
                 $"{bytes / ids.Length} B, {cur.Drawn} drawn for {2 * ids.Length} changes");
        Want($"cursor: {cur.Drawn} shapes drawn for {2 * ids.Length} changes (want {ids.Length}: a shape seen before is not drawn again)", cur.Drawn == ids.Length);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

    // the main loop's pacer (Sys/FramePacer.cs) at 90 Hz for 2 s with ~0.3 ms of work a tick: what a wait costs the CPU (this thread's
    // CPU cycles while it waits, in us by a 50 ms spin measured the same way), how even the ticks come, what it allocates
    static void PacerCost()
    {
        Native.timeBeginPeriod(1);
        try
        {
            using var p = new FramePacer();
            QueryThreadCycleTime(GetCurrentThread(), out ulong c0);
            double s0 = Clock.Now;
            while (Clock.Now - s0 < 0.05) { }
            QueryThreadCycleTime(GetCurrentThread(), out ulong c1);
            double cyclesPerUs = (c1 - c0) / ((Clock.Now - s0) * 1e6);
            p.Wait(90);
            var gaps = new List<double>();
            ulong cpu = 0;
            long alloc = 0;
            double t0 = Clock.Now, last = Clock.Now;
            while (Clock.Now - t0 < 2)
            {
                double w = Clock.Now;
                while (Clock.Now - w < 0.0003) { }
                QueryThreadCycleTime(GetCurrentThread(), out ulong w0);
                long m0 = GC.GetAllocatedBytesForCurrentThread();
                p.Wait(90);
                alloc += GC.GetAllocatedBytesForCurrentThread() - m0;
                QueryThreadCycleTime(GetCurrentThread(), out ulong w1);
                cpu += w1 - w0;
                double n = Clock.Now;
                gaps.Add((n - last) * 1000);
                last = n;
            }
            gaps.Sort();
            Log.Info($"perf: pacer {gaps.Count} ticks, cpu {cpu / cyclesPerUs / gaps.Count:0.0} us a wait, interval median {gaps[gaps.Count / 2]:0.00} ms " +
                     $"p99 {gaps[(int)(gaps.Count * 0.99)]:0.00} (target 11.11), {alloc / gaps.Count} B a wait");
        }
        finally { Native.timeEndPeriod(1); }
    }

    // the retro styles' pixel text from kept layouts (Painter.PixelLayouts) against DrawText laying it out every time: the sheet's every
    // tab, the keyboard and the watch in every retro style - the pixels that differ (want 0)
    static void PixelTextCheck(SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg,
        KeyboardPanel kb, PanelData data)
    {
        string style0 = cfg.Data.Appearance.Style;
        int keep = view.Tab, diff = 0, shots = 0;
        ptr.Leave();
        using var wrist = new WristPanel(gpu, painter, cfg);
        wrist.Mic.Fake = true;
        wrist.Mic.Present = true;
        wrist.TestNow = new DateTime(2026, 9, 30, 23, 47, 58);         // the same moment in both pictures
        uint[] Shot(Action draw, Surface s, bool kept)
        {
            painter.PixelLayouts = kept;
            draw(); draw();
            painter.PixelLayouts = true;
            return s.ReadPixels(gpu);
        }
        void Pair(Action draw, Surface s)
        {
            var a = Shot(draw, s, false);
            var b = Shot(draw, s, true);
            shots++;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) diff++;
        }
        foreach (var sk in Skins.All)
        {
            cfg.Data.Appearance.Style = sk.Id;
            Theme.Apply(cfg.Data.Appearance);
            for (int t = 0; t < SettingsView.TabCount; t++)
            {
                view.Tab = t;
                ui.ResetAnimations();
                Pair(() => view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal), surf);
            }
            Pair(kb.Draw, kb.Surf);
            Pair(() => { wrist.Settle(); wrist.Draw(data); }, wrist.Surf);
        }
        view.Tab = keep;
        ui.ResetAnimations();
        cfg.Data.Appearance.Style = style0;
        Theme.Apply(cfg.Data.Appearance);
        Want($"perf: the retro styles' pixel text from kept layouts against DrawText, {shots} pictures: {diff} pixels differ (want 0)", diff == 0 && shots > 0);
    }

    // the modern keyboard's kept picture (KeyboardPanel.Draw: the frame and the keys at rest kept, only the keys doing something drawn
    // over it) against the whole picture drawn, in the states a frame can be in - at rest, a key under the laser, held, let go (its
    // glow half-way), a one-shot Shift under the laser, Chatbox on with a line typed: the pixels that differ (want 0)
    static void KeyboardBaseCheck(KeyboardPanel kb, Gpu gpu, ConfigStore cfg)
    {
        string style0 = cfg.Data.Appearance.Style;
        cfg.Data.Appearance.Style = null;
        Theme.Apply(cfg.Data.Appearance);
        kb.Draw();
        int diff = 0, shots = 0;
        uint[] Shot(bool all)
        {
            kb.DrawAllForTest = all;
            kb.Draw();
            kb.DrawAllForTest = false;
            return kb.Surf.ReadPixels(gpu);
        }
        void Pair()
        {
            var a = Shot(true);
            var b = Shot(false);
            shots++;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) diff++;
        }
        const int key = 30;                                           // a letter key (the typing case's)
        var kr = kb.KeyRectForTest(key);
        var mid = new System.Numerics.Vector2(kr.X + kr.Width / 2, kr.Y + kr.Height / 2);
        Pair();                                                       // at rest
        kb.SetHover(2, mid); Pair();                                  // under the laser
        kb.HoldForTest(key, true); Pair();                            // held
        kb.HoldForTest(key, false); kb.GlowForTest(key, 0.6f); Pair();   // let go: the glow
        kb.GlowForTest(key, 0); kb.SetHover(2, null);
        int shift = kb.KeyIndexForTest("shift");
        var sr = kb.KeyRectForTest(shift);
        kb.ModForTest("shift", 1); kb.SetHover(2, new System.Numerics.Vector2(sr.X + sr.Width / 2, sr.Y + sr.Height / 2)); Pair();    // a one-shot Shift under the laser
        kb.ModForTest("shift", 0); kb.SetHover(2, null);
        kb.PreviewChat("Szia! Mindjárt jövök"); Pair();              // Chatbox on, a line typed
        kb.PreviewChat("");
        typeof(KeyboardPanel).GetField("_chat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(kb, false);
        cfg.Data.Appearance.Style = style0;
        Theme.Apply(cfg.Data.Appearance);
        Want($"perf: the modern keyboard's kept picture against the whole picture drawn, {shots} states: {diff} pixels differ (want 0)", diff == 0 && shots > 0);
    }

    // the VR settings panel's corners cut after drawing (Painter.CutCorners) against the clip layer they replaced, every tab: the
    // pixels that differ and by how much (a channel, 0-255). CutCorners keeps the layer's coverage of a corner as an 8-bit alpha and
    // multiplies by it later, where the layer multiplies by its own coverage at once: two roundings for one, a pixel on the curve can be
    // a level off (the "one 8-bit step" of its comment). So a level is allowed - only in the four corner squares; a pixel anywhere
    // else differing at all, or one more than a level off, is a real difference.
    static void PanelCornersCheck(SettingsView panelView, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg)
    {
        string style0 = cfg.Data.Appearance.Style;
        cfg.Data.Appearance.Style = null;
        Theme.Apply(cfg.Data.Appearance);
        int keep = panelView.Tab, diff = 0, max = 0, over = 0, outside = 0;
        // the corner squares as CutCorners takes them (its own sums: the radius 22 at the panel's 1.5 scale, 2 px more)
        int cs = (int)MathF.Ceiling(22 * 1.5f) + 2, cx1 = (int)MathF.Ceiling(SettingsView.W * 1.5f) - cs, cy1 = (int)MathF.Ceiling(SettingsView.H * 1.5f) - cs;
        bool InCorner(int x, int y) => (x < cs || (x >= cx1 && x < cx1 + cs)) && (y < cs || (y >= cy1 && y < cy1 + cs));
        ptr.Leave();
        var all = new RectangleF(0, 0, SettingsView.W, SettingsView.H);
        uint[] Shot(bool layer)
        {
            for (int f = 0; f < 2; f++)
            {
                ui.Begin(ptr, 1.5f);
                painter.Begin(surf.Bitmap, 0x00000000, 1.5f);
                if (layer)
                {
                    painter.PushRoundedClip(all, 22);
                    panelView.Draw(ui);
                    painter.PopRoundedClip();
                    painter.Stroke(RectangleF.Inflate(all, -1, -1), Theme.PanelLine, 2, 21);
                }
                else SettingsPanel.DrawBody(painter, ui, panelView);
                painter.End();
                ui.End();
            }
            return surf.ReadPixels(gpu);
        }
        for (int t = 0; t < SettingsView.TabCount; t++)
        {
            panelView.Tab = t;
            ui.ResetAnimations();
            var a = Shot(true);
            ui.ResetAnimations();
            var b = Shot(false);
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == b[i]) continue;
                diff++;
                int most = 0;
                for (int sh = 0; sh < 32; sh += 8) most = Math.Max(most, Math.Abs((int)((a[i] >> sh) & 0xFF) - (int)((b[i] >> sh) & 0xFF)));
                max = Math.Max(max, most);
                if (most > 1) over++;
                if (!InCorner(i % surf.W, i / surf.W)) outside++;
            }
        }
        panelView.Tab = keep;
        ui.ResetAnimations();
        cfg.Data.Appearance.Style = style0;
        Theme.Apply(cfg.Data.Appearance);
        Want($"perf: the VR settings panel's corners cut against the clip layer, {SettingsView.TabCount} tabs: {diff} pixels differ by up to {max} of 255 " +
             $"(want 1 or less: the 8-bit corner picture's rounding) - {over} more than a level off (want 0), {outside} outside the corners (want 0)", over == 0 && outside == 0);
    }

    // like FrameCost, but each round starts something (start) and then draws n frames on a 90 Hz grid - an animation measured over
    // the same stretch of it in every round
    static (double ms, long bytes) PacedCost(Gpu gpu, Action start, Action draw, int n)
    {
        using var query = gpu.Device.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.Event));
        double best = double.MaxValue, bestWorst = 0;
        int bestWorstAt = 0;
        long bytes = long.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            start?.Invoke();
            double sum = 0, next = Clock.Now, worst = 0;
            int worstAt = 0;
            long alloc = 0;
            for (int i = 0; i < n; i++)
            {
                next += 1 / 90.0;
                while (Clock.Now < next - 0.002) Thread.Sleep(1);
                while (Clock.Now < next) Thread.SpinWait(20);
                gpu.Ctx.End(query);
                gpu.Ctx.Flush();
                while (gpu.Ctx.GetData(query, IntPtr.Zero, 0, Vortice.Direct3D11.AsyncGetDataFlags.None).Code != 0) Thread.SpinWait(50);
                long m0 = GC.GetAllocatedBytesForCurrentThread();
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                draw();
                gpu.Ctx.Flush();
                double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                sum += ms;
                if (ms > worst) { worst = ms; worstAt = i; }
                alloc += GC.GetAllocatedBytesForCurrentThread() - m0;
            }
            if (sum / n < best) { best = sum / n; bestWorst = worst; bestWorstAt = worstAt; }
            bytes = Math.Min(bytes, alloc / n);
        }
        _pacedWorst = bestWorst;
        _pacedWorstAt = bestWorstAt;
        return (best, bytes);
    }

    // ORBIT_PERF_TYPES=1: the runtime's allocation samples (GCAllocationTick, one per ~100 KB allocated) over 2000 frames, by type
    sealed class AllocListener : EventListener
    {
        public readonly Dictionary<string, int> Types = new();
        public bool On;
        protected override void OnEventSourceCreated(EventSource s)
        {
            if (s.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(s, EventLevel.Verbose, (EventKeywords)0x1);
        }
        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (!On || e.EventName == null || !e.EventName.StartsWith("GCAllocationTick")) return;
            int i = e.PayloadNames?.IndexOf("TypeName") ?? -1;
            string t = i >= 0 ? e.Payload[i] as string ?? "?" : "?";
            lock (Types) Types[t] = Types.GetValueOrDefault(t) + 1;
        }
    }

    // (a listener of its own each time: one left listening would slow the next case's measurement down)
    static void AllocTypes(string what, Action draw, Action start)
    {
        using var appOnly = AppAllocs();
        using var l = new AllocListener();
        Thread.Sleep(300);
        l.On = true;
        for (int i = 0; i < 2000; i++) { if (start != null && i % 12 == 0) start(); draw(); }
        Thread.Sleep(1500);         // the samples arrive on the listener's own thread
        l.On = false;
        lock (l.Types)
            if (l.Types.Count > 0)
                Log.Info($"perf types {what}: " + string.Join(", ", l.Types.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{Closure(kv.Key)} x{kv.Value}")));
    }

    // a lambda's closure by the variables it holds (its class name alone says nothing): <>c__DisplayClass11_0{id}
    static string Closure(string type)
    {
        if (!type.StartsWith("<>c__DisplayClass", StringComparison.Ordinal)) return type;
        var all = typeof(Tools).Assembly.GetTypes().Where(t => t.Name == type).ToList();
        const System.Reflection.BindingFlags all_ = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        return type + "{" + string.Join(" | ", all.Select(t => t.DeclaringType?.Name + ": " + string.Join(",", t.GetFields(all_).Select(f => f.Name)) +
            " in " + string.Join(",", t.GetMethods(all_).Where(m => m.DeclaringType == t).Select(m => m.Name)))) + "}";
    }
}
