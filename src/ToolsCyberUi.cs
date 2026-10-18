using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Orbit;

// --snapshot, Cyber: its own UI (the settings as a HUD console, the keyboard, the panels - Ui/UiHud.cs, Ui/SettingsCyber.cs,
// Panels/*Cyber.cs) checked like the modern one:
//  - the scripted clicks again, in Cyber: the nav tabs, a switch, a choice, a slider, the Look tab's style list (its own place),
//    Default look (what Cyber's Look sets, Modern's colours kept), the watch editor's select / remove / move / resize / add;
//  - the keyboard's Cyber rows: inside the texture, under the header strip, none over another;
//  - 'layout cyber ui': every text of those screens inside its box (its ink, as the fonts check measures it); qa/cyberui_misfits.txt;
//  - its motion as frames (qa/cyberui_<effect>_<ms>.png, CyberFx live only here): a tab picked, a button hovered / held / let go, a
//    switch flipped, a choice picked, the VR panel opening and closing, the keyboard opening, a key pressed and let go, a modifier
//    latching, a notification scanning in, the screen bar opening, a monitor's unfold frame and the theater lock-on, the theater
//    wall's grid. ORBIT_SNAP_CHECKS=0 leaves the checks out (the pictures stay);
//  - what a settings and a keyboard frame cost against Modern (FrameCost; ORBIT_SNAP_ONLY=cyberperf: that alone, more cases).
static partial class Tools
{
    static void CyberUiRun(SettingsView view, SettingsView panelView, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu,
        ConfigStore cfg, FakeHost host, WristPanel wrist, PanelData data, KeyboardPanel kb, string dir, Func<int, List<WatchWidget>> gallery)
    {
        string qa = Path.Combine(dir, "qa");
        Directory.CreateDirectory(qa);
        var wcfg = cfg.Data.Wrist;
        string face0 = wcfg.Face;
        void Frame()
        {
            if (view.ShowsWristPreview) wrist.Draw(data);
            if (view.ShowsKeyboardPreview) kb.Draw();
            ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End();
        }
        void Click(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); Frame(); ptr.Press(); Frame(); ptr.Release(); Frame(); }
        bool checks = Environment.GetEnvironmentVariable("ORBIT_SNAP_CHECKS") != "0";
        int keep = view.Tab;
        ui.ResetAnimations();
        if (checks)
        {
            // ---- the nav ----
            int okTabs = 0;
            for (int t = SettingsView.TabCount - 1; t >= 0; t--) { Click(106, 84 + t * 50 + 21); if (view.Tab == t) okTabs++; }
            Log.Info($"cyber ui: a click on each nav tab opened it {okTabs} of {SettingsView.TabCount} (want {SettingsView.TabCount} of {SettingsView.TabCount})");
            // ---- General: a switch, a choice; Keyboard: a slider ----
            var g = cfg.Data.General;
            view.Tab = SettingsView.TabGeneral;
            Frame();
            bool hap0 = g.Haptics;
            Click(222 + 200, 196 + 22);
            bool hapFlip = g.Haptics == !hap0;
            Click(222 + 200, 196 + 22);
            string hand0 = g.LaserHand;
            float cw = (376 - 20) / 3f;
            Click(232 + cw * 1.5f, 108 + 19);
            bool left = g.LaserHand == "left";
            Click(232 + cw * 0.5f, 108 + 19);
            Log.Info($"cyber ui: General - the Haptic ticks switch flips {hapFlip} (want True) and back {g.Haptics == hap0} (want True); Laser on: Left picked " +
                     $"{left} (want True), Both hands again {g.LaserHand == "both"} (want True)");
            g.LaserHand = hand0;
            view.Tab = SettingsView.TabKeyboard;
            Frame();
            float kw0 = cfg.Data.Keyboard.Width;
            Click(232 + 356 * 0.5f, 144 + 58 - 15);
            float kw = cfg.Data.Keyboard.Width;
            Log.Info($"cyber ui: Keyboard - a click half-way along the Size bar sets the width to {kw:0.00} (want 0.75)");
            cfg.Data.Keyboard.Width = kw0;
            // ---- Look: the style list from its Cyber field; Default look ----
            var a = cfg.Data.Appearance;
            view.Tab = SettingsView.TabLook;
            ui.ResetAnimations();
            Frame();
            var field = SettingsView.CyStyleFieldForTest;
            var list = SettingsView.CyStyleListForTest;
            Click(field.X + field.Width / 2, field.Y + field.Height / 2);
            ptr.Move((list.X + 100) * 1.5f, (list.Y + 6 + 2 * 40 + 20) * 1.5f);
            Frame(); Frame();
            surf.SavePng(gpu, Path.Combine(dir, "settings_look_styles_cyber.png"));
            Click(list.X + 100, list.Y + 6 + 20);            // Modern, the first
            bool modern = a.Style == "modern" && !Theme.Cyber;
            a.Style = CyberHud.Id;
            Theme.Apply(a);
            ptr.Leave();
            ui.ResetAnimations();
            Frame();
            uint acc0 = a.Accent;
            a.Accent = 0xFF4ADE80;
            a.PanelOpacity = 0.6f;
            a.LaserColor = 0xFFF472B6;
            int lv = a.LookVersion;
            Theme.Apply(a);
            Frame();
            var prev = SettingsView.CyLookPreview;
            Click(232 + 85, prev.Bottom + 70 + 19);
            var d = new AppearanceConfig();
            bool reset = a.PanelOpacity == d.PanelOpacity && a.LaserColor == 0 && a.Accent == 0xFF4ADE80 && a.LookVersion == lv && a.Style == CyberHud.Id;
            Log.Info($"cyber ui: Look - Modern picked from the Cyber style list applies at once {modern} (want True); Default look puts back the opacity " +
                     $"and the laser colour and keeps Modern's accent {reset} (want True)");
            a.Accent = acc0;
            Theme.Apply(a);
            // ---- the watch editor in Cyber: the modern pass's clicks ----
            view.Tab = SettingsView.TabWrist;
            void FrameW() { wrist.Draw(data); Frame(); }
            void ClickW(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); FrameW(); ptr.Press(); FrameW(); ptr.Release(); FrameW(); }
            ui.ResetAnimations();
            wcfg.Face = "full";
            wcfg.Widgets = null;
            ClickW(612 + 10 + (376 - 20) * 4.5f / 5, 92 + 372f * WristPanel.TH / WristPanel.TW + 38 + 18);
            bool custom = wcfg.Face == "custom" && wcfg.Widgets?.Count > 0;
            view.EditingWatch = true;
            ptr.Leave();
            FrameW();
            static Vector2 CellM(int cx, int cy)
            {
                var pv = SettingsView.EditorPrev;
                var c = WatchWidgets.CellRect(cx, cy, 1, 1, false);
                return new(pv.X + (c.X + c.Width / 2) * pv.Width / WristPanel.TW, pv.Y + (c.Y + c.Height / 2) * pv.Height / WristPanel.TH);
            }
            var c42 = CellM(4, 2);
            ClickW(c42.X, c42.Y);
            ptr.Leave(); wrist.Settle(); FrameW(); ui.ResetAnimations(); FrameW();      // the watch and the page at rest for the picture
            surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_editor_sel_cyber.png"));
            ClickW(724 + 2 * (244 / 3f + 8) + 40, 138 + 17);
            var gpuW = wcfg.Widgets.FirstOrDefault(w => w.Type == "gpu");
            var c52 = CellM(5, 2);
            ptr.Move(c52.X * 1.5f, c52.Y * 1.5f); FrameW(); ptr.Press(); FrameW();
            ptr.Move(c42.X * 1.5f, c42.Y * 1.5f); FrameW();
            surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_editor_drag_cyber.png"));
            ptr.Release(); FrameW();
            bool moved = wcfg.Widgets.All(w => w.Type != "fps") && gpuW?.X == 4 && gpuW?.Y == 2;
            var hc = WatchWidgets.CellRect(4, 2, 1, 1, false);
            var hnd = new Vector2(SettingsView.EditorPrev.X + hc.Right * SettingsView.EditorPrev.Width / WristPanel.TW - 2,
                SettingsView.EditorPrev.Y + hc.Bottom * SettingsView.EditorPrev.Height / WristPanel.TH - 2);
            ptr.Move(hnd.X * 1.5f, hnd.Y * 1.5f); FrameW(); ptr.Press(); FrameW();
            ptr.Move(c52.X * 1.5f, c52.Y * 1.5f); FrameW(); ptr.Release(); FrameW();
            int before = wcfg.Widgets.Count;
            ClickW(222 + 58, SettingsView.PaletteY + 14);
            Log.Info($"cyber ui: editor - Custom picked {custom} (want True), fps removed and gpu moved to 4,2 {moved} (want True), gpu {gpuW?.W}x{gpuW?.H} " +
                     $"(want 2x1), add with a full grid kept {wcfg.Widgets.Count == before} (want True)");
            ptr.Leave();
            FrameW();
            view.EditingWatch = false;
            wcfg.Face = face0;
            wcfg.Widgets = null;
            // ---- 1.14's pages in Cyber: Layouts > Per game, the editor's Share - a click opens each ----
            view.Tab = SettingsView.TabLayouts;
            ui.ResetAnimations();
            Frame();
            Click(612 + 180, 394 + 19);
            bool rules = OpenFor(view);
            ptr.Leave(); ui.ResetAnimations(); Frame();
            surf.SavePng(gpu, Path.Combine(dir, "settings_layouts_rules_cyber.png"));
            SetOpen(view, "_rulesOpen", false);
            view.Tab = SettingsView.TabWrist;
            wcfg.Face = "custom";
            wcfg.Widgets = null;
            view.EditingWatch = true;
            ui.ResetAnimations();
            FrameW();
            ClickW(SettingsView.ShareMidX(WatchPages.Count(wcfg)), 76 + 16);
            bool share = ShareOpen(view);
            ptr.Leave(); wrist.Settle(); FrameW(); ui.ResetAnimations(); FrameW();
            surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_share_cyber.png"));
            SetOpen(view, "_wshare", false);
            view.EditingWatch = false;
            wcfg.Face = face0;
            wcfg.Widgets = null;
            Log.Info($"cyber ui: Per game opens from its button {rules} (want True), the editor's Share from its button {share} (want True)");
            // ---- the keyboard's Cyber rows ----
            kb.Draw();
            int n = 0, under = 0, outside = 0, overlap = 0;
            var rects = new List<RectangleF>();
            for (int i = 0; ; i++)
            {
                var r = kb.KeyRectForTest(i);
                if (r.IsEmpty) break;
                n++;
                if (r.Y < 20 + 46 + 66) under++;          // the header strip (and the top bar above the rows)
                if (r.X < 0 || r.Y < 0 || r.Right > KeyboardPanel.TW - 18 || r.Bottom > KeyboardPanel.TH - 18) outside++;
                foreach (var o in rects) if (o.IntersectsWith(r)) overlap++;
                rects.Add(r);
            }
            Log.Info($"cyber ui: keyboard - {n} keys: under the header or the top bar {under} (want 0), outside the frame {outside} (want 0), " +
                     $"over another {overlap} (want 0)");
            // ---- what a frame costs: the settings (General), the keyboard ----
            view.Tab = SettingsView.TabGeneral;
            ptr.Leave();
            ui.ResetAnimations();
            var (sms, _) = FrameCost(gpu, Frame);
            var (kms, _) = FrameCost(gpu, kb.Draw);
            // the same in Modern, for comparison (the machine's load varies from run to run)
            var look = cfg.Data.Appearance;
            look.Style = null;
            Theme.Apply(look);
            var (smm, _) = FrameCost(gpu, Frame);
            var (kmm, _) = FrameCost(gpu, kb.Draw);
            look.Style = CyberHud.Id;
            Theme.Apply(look);
            kb.Draw();
            Log.Info($"cyber ui: a settings frame (General) {sms:0.00} ms (Modern {smm:0.00}), a keyboard frame {kms:0.00} ms (Modern {kmm:0.00})");
            // ---- every text inside its box ----
            CyberFit(view, ui, surf, ptr, painter, gpu, cfg, host, wrist, data, kb, qa, Frame);
        }
        CyberMotion(view, panelView, ui, surf, ptr, painter, gpu, cfg, wrist, data, kb, qa, Frame);
        view.Tab = keep;
        view.EditingWatch = false;
        wcfg.Face = face0;
        wcfg.Widgets = null;
        ptr.Leave();
        ui.ResetAnimations();
    }

    // ORBIT_SNAP_ONLY=cyberui --snapshot <dir>: only the Cyber UI's motion frames (qa/cyberui_*.png), in seconds
    static int CyberUiOnly(SettingsView view, SettingsView panelView, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg,
        WristPanel wrist, PanelData data, KeyboardPanel kb, string dir)
    {
        cfg.Data.Appearance = new AppearanceConfig { Style = CyberHud.Id, Clock24h = true, Motion = Environment.GetEnvironmentVariable("ORBIT_ANIM_MOTION") };
        Theme.Apply(cfg.Data.Appearance);
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        string qa = Path.Combine(dir, "qa");
        Directory.CreateDirectory(qa);
        void Frame()
        {
            if (view.ShowsWristPreview) wrist.Draw(data);
            if (view.ShowsKeyboardPreview) kb.Draw();
            ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End();
        }
        CyberMotion(view, panelView, ui, surf, ptr, painter, gpu, cfg, wrist, data, kb, qa, Frame);
        return 0;
    }

    // What a frame costs on the CPU (drawing + hand-over, the GPU idle before each one): the best of 3 rounds of n frames - the PC's
    // load swings from run to run - and what a frame allocates
    static (double ms, long bytes) FrameCost(Gpu gpu, Action draw, int n = 60)
    {
        using var query = gpu.Device.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.Event));
        void Idle()
        {
            gpu.Ctx.End(query);
            gpu.Ctx.Flush();
            while (gpu.Ctx.GetData(query, IntPtr.Zero, 0, Vortice.Direct3D11.AsyncGetDataFlags.None).Code != 0) Thread.SpinWait(50);
        }
        draw(); draw();
        double best = double.MaxValue;
        long bytes = long.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            double sum = 0;
            long alloc = 0;
            for (int i = 0; i < n; i++)
            {
                Idle();
                long m0 = GC.GetAllocatedBytesForCurrentThread();
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                draw();
                gpu.Ctx.Flush();
                sum += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                alloc += GC.GetAllocatedBytesForCurrentThread() - m0;
            }
            best = Math.Min(best, sum / n);
            bytes = Math.Min(bytes, alloc / n);
        }
        return (best, bytes);
    }

    // ORBIT_SNAP_ONLY=cyberperf --snapshot <dir>: what the Cyber settings and keyboard cost a frame against Modern's, at rest and moving
    // (a button under the pointer, a key let go, a tab picked), each the best of 3 rounds - in seconds
    static int CyberPerfOnly(SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg, KeyboardPanel kb)
    {
        void Frame()
        {
            ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End();
        }
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        var look = cfg.Data.Appearance;
        var lines = new List<string>();
        foreach (string style in new[] { CyberHud.Id, null })
        {
            look.Style = style;
            look.Motion = Environment.GetEnvironmentVariable("ORBIT_BENCH_MOTION");
            Theme.Apply(look);
            CyberFx.Live = true;
            string name = style == null ? "Modern" : CyberHud.Name;
            view.Tab = SettingsView.TabGeneral;
            ptr.Leave();
            ui.ResetAnimations();
            var rest = FrameCost(gpu, Frame);
            ptr.Move((222 + 200) * 1.5f, (196 + 22) * 1.5f);         // over the Haptic ticks switch
            var hover = FrameCost(gpu, Frame);
            ptr.Leave();
            // a tab picked: the frames of its transition (each round from the click)
            double tab = double.MaxValue;
            for (int r = 0; r < 3; r++)
            {
                view.Tab = SettingsView.TabDesktops;
                ui.ResetAnimations();
                Frame();
                ptr.Move(106 * 1.5f, (84 + SettingsView.TabGeneral * 50 + 21) * 1.5f);
                Frame(); ptr.Press(); Frame(); ptr.Release();
                tab = Math.Min(tab, FrameCost(gpu, Frame, 12).ms);
                ptr.Leave();
            }
            var krest = FrameCost(gpu, kb.Draw);
            double kpress = double.MaxValue;
            for (int r = 0; r < 3; r++)
            {
                kb.HoldForTest(30, true); kb.Draw();
                kb.HoldForTest(30, false);
                kpress = Math.Min(kpress, FrameCost(gpu, () => { kb.CyberFlashForTest(-1); kb.Draw(); }, 12).ms);
            }
            CyberFx.Live = false;
            lines.Add($"cyber perf: {name,-6} settings (General) at rest {rest.ms:0.000} ms ({rest.bytes} B), a switch under the pointer {hover.ms:0.000} ms " +
                      $"({hover.bytes} B), a tab picked {tab:0.000} ms; keyboard at rest {krest.ms:0.000} ms ({krest.bytes} B), a key let go {kpress:0.000} ms");
        }
        look.Style = CyberHud.Id;
        look.Motion = null;
        Theme.Apply(look);
        foreach (var l in lines) Log.Info(l);
        return 0;
    }

    // 'layout cyber ui': the ink of every text of the Cyber settings, What's new, the keyboard and the panels inside its box
    static void CyberFit(SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg, FakeHost host,
        WristPanel wrist, PanelData data, KeyboardPanel kb, string qa, Action frame)
    {
        var spied = new List<(string s, RectangleF r, float size, Vortice.DirectWrite.FontWeight w, string fam, bool wrap,
            Vortice.DirectWrite.TextAlignment h, Vortice.DirectWrite.ParagraphAlignment v, float tr)>();
        var bad = new List<string>();
        int texts = 0;
        void Spy(string page, Action draw)
        {
            spied.Clear();
            painter.FitSpy = (s, r, size, w, fam, wrap, h, v, tr) => spied.Add((s, r, size, w, fam, wrap, h, v, tr));
            draw();
            painter.FitSpy = null;
            foreach (var t in spied)
            {
                if (t.fam == Theme.IconFont || string.IsNullOrWhiteSpace(t.s)) continue;
                texts++;
                float o = painter.Overhang(t.s, t.r, t.size, t.w, t.fam, t.wrap, t.h, t.v, t.tr);
                if (o >= 1) bad.Add($"{page}: '{t.s}' {t.size:0.#} px in {t.fam} sticks out {o:0.#} px of its {t.r.Width:0.#} x {t.r.Height:0.#} box");
            }
        }
        ptr.Leave();
        for (int t = 0; t < SettingsView.TabCount; t++)
        {
            view.Tab = t;
            ui.ResetAnimations();
            Spy($"settings{t}", frame);
        }
        var wcfg = cfg.Data.Wrist;
        string f0 = wcfg.Face;
        wcfg.Face = "custom";
        wcfg.Widgets = null;
        view.Tab = SettingsView.TabWrist;
        view.EditingWatch = true;
        ui.ResetAnimations();
        Spy("editor", frame);
        view.EditingWatch = false;
        wcfg.Face = f0;
        wcfg.Widgets = null;
        host.WhatsNewList = WhatsNew.Since("1.8.2");
        host.WhatsNewSub = $"Updated to {Updater.CurrentText} from 1.8.2";
        view.Tab = SettingsView.TabGeneral;
        ui.ResetAnimations();
        Spy("settings_whatsnew", frame);
        using (var wnp = new WhatsNewPanel(gpu, painter)) Spy("whatsnew_panel", () => wnp.DrawFor(host.WhatsNewList, host.WhatsNewSub));
        host.WhatsNewList = Array.Empty<WhatsNew.Entry>();
        Spy("keyboard", kb.Draw);
        kb.PreviewChat("Szia! Mindjárt jövök, csak befejezem ezt :)");
        Spy("keyboard_chat", kb.Draw);
        kb.PreviewChat("");
        typeof(KeyboardPanel).GetField("_chat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(kb, false);
        using (var np = new NotificationPanel(gpu, painter, cfg))
        {
            np.AddForPreview(new Notification { Source = "VRCX", Title = "Friend online", Content = "Sam has joined The Black Cat (Friends+)" });
            np.AddForPreview(new Notification { Source = "Discord", Title = "New message", Content = "Alex: are you in VR? The new watch looks great." });
            Spy("notifications", np.Draw);
        }
        using (var room = new RoomPanel(gpu, painter, cfg)) Spy("room", () => room.DrawFor(RoomViewTools.Room(12, 2), DateTime.Now));
        using (var faces = new HexpadFaces())
        {
            var fake = DockViewTools.Fake();
            faces.Update(painter, fake);
            using var dock = new DockPanel(gpu, painter, cfg, null, faces);
            Spy("dock", () => dock.DrawFor(fake, Clock.Now));
        }
        using (var bar = new TheaterBar(gpu, painter, cfg))
        {
            var a = new TheaterTest.FakeScreen { Name = "DELL U2723QE" };
            var b = new TheaterTest.FakeScreen { Name = "LG ULTRAGEAR" };
            var choices = new List<ITheaterScreen> { a, b, new TheaterTest.FakeScreen { Name = "YouTube - Lofi hip hop radio - beats to relax/study to - Mozilla Firefox" } };
            Spy("theater_bar", () => bar.DrawFor(choices, b, new TheaterView { Size = 2.8f }));
            Spy("theater_bar_screen", () => bar.DrawFor(new List<ITheaterScreen>(), null));
        }
        File.WriteAllLines(Path.Combine(qa, "cyberui_misfits.txt"), bad);
        foreach (var l in bad.Take(12)) Log.Warn($"layout cyber ui{LangTag}: " + l);
        Log.Info($"layout cyber ui{LangTag}: {bad.Count} problem(s) in {texts} texts");
    }

    // ---- the motion, as frames (CyberFx live for this alone) ----
    static void CyberMotion(SettingsView view, SettingsView panelView, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu,
        ConfigStore cfg, WristPanel wrist, PanelData data, KeyboardPanel kb, string qa, Action frame)
    {
        foreach (var f in Directory.GetFiles(qa, "cyberui_*.png")) File.Delete(f);
        var index = new System.Text.StringBuilder();
        CyberFx.Live = true;
        bool still = Motion.Still;
        Motion.Still = false;
        try
        {
            // real time, ~120 draws a second after start(); the frame nearest each moment kept
            void Shoot(string name, string what, double[] at, Action start, Action draw, Func<Surface> s, RectangleF crop, Action<double> each = null)
            {
                var files = new List<string>();
                start?.Invoke();
                double t0 = Clock.Now, next = t0;
                int i = 0;
                while (i < at.Length && Clock.Now - t0 < at[^1] + 2)
                {
                    double t = Clock.Now - t0;
                    each?.Invoke(t);
                    draw();
                    gpu.Ctx.Flush();
                    if (t >= at[i] - 0.004)
                    {
                        string file = $"cyberui_{name}_{at[i] * 1000:000}.png";
                        SaveCrop(s(), gpu, Path.Combine(qa, file), crop);
                        files.Add(file);
                        i++;
                    }
                    next += 1 / 120.0;
                    while (Clock.Now < next) Thread.SpinWait(40);
                }
                index.AppendLine($"{name}|{what}|{string.Join(",", files)}");
            }
            var whole = new RectangleF(0, 0, surf.W, surf.H);
            // a tab picked: the scan reveals the page
            view.Tab = SettingsView.TabDesktops;
            ui.ResetAnimations();
            ptr.Leave();
            frame(); frame();
            Shoot("tab", "settings: a tab picked - a scan line reveals the page, its tags type in",
                new[] { 0.0, 0.03, 0.06, 0.09, 0.12, 0.16, 0.2, 0.25, 0.32, 0.45 },
                () =>
                {
                    ptr.Move(106 * 1.5f, (84 + SettingsView.TabGeneral * 50 + 21) * 1.5f);
                    frame();
                    ptr.Press(); frame(); ptr.Release();
                }, frame, () => surf, whole);
            ptr.Leave();
            // the HUD controls (General): a button the pointer comes onto, holds and lets go; a switch flipped on; a choice picked
            var g0 = cfg.Data.General;
            view.Tab = SettingsView.TabGeneral;
            ui.ResetAnimations();
            frame(); frame();
            Shoot("button", "a button: the pointer comes onto it (a light crosses the plate), held (its label splits red / cyan), let go (its corners kick out)",
                new[] { 0.0, 0.04, 0.08, 0.12, 0.16, 0.2, 0.28, 0.32, 0.36, 0.42, 0.52 }, () => ptr.Move((572 + 66) * 1.5f, (14 + 18) * 1.5f), frame, () => surf,
                new RectangleF(540 * 1.5f, 0, 300 * 1.5f, 66 * 1.5f), t => { if (t >= 0.26 && t < 0.3 && !ptr.Down) ptr.Press(); else if (t >= 0.3 && ptr.Down) ptr.Release(); });
            ptr.Leave();
            bool hap0 = g0.Haptics;
            g0.Haptics = false;
            ui.ResetAnimations();
            frame(); frame();
            Shoot("switch", "a switch flipped on: the fill wipes over with a bright front, the housing's corners kick out, the word glitches",
                new[] { 0.0, 0.03, 0.06, 0.1, 0.14, 0.2, 0.3 }, () => { ptr.Move(422 * 1.5f, 218 * 1.5f); frame(); ptr.Press(); frame(); ptr.Release(); }, frame,
                () => surf, new RectangleF(470 * 1.5f, 186 * 1.5f, 150 * 1.5f, 60 * 1.5f));
            g0.Haptics = hap0;
            string hand0 = g0.LaserHand;
            Shoot("choice", "a choice picked: the underline stretches over and settles, the new pick's name glitches",
                new[] { 0.0, 0.03, 0.06, 0.1, 0.14, 0.2, 0.3 }, () => { ptr.Move(410 * 1.5f, 127 * 1.5f); frame(); ptr.Press(); frame(); ptr.Release(); }, frame,
                () => surf, new RectangleF(225 * 1.5f, 100 * 1.5f, 370 * 1.5f, 52 * 1.5f));
            g0.LaserHand = hand0;
            ptr.Leave();
            // the VR panel: opening, then closing
            void FrameP() { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, 0x00000000, 1.5f); panelView.Draw(ui); painter.End(); ui.End(); }
            panelView.Tab = SettingsView.TabLook;
            panelView.Vis = 1; panelView.Closing = false; panelView.ShownAt = -10;
            ui.ResetAnimations();
            wrist.Draw(data);
            FrameP();
            Shoot("panel_open", "the VR settings panel opening: brackets snap out, a scan line reveals it",
                new[] { 0.0, 0.04, 0.08, 0.12, 0.16, 0.2, 0.25, 0.3, 0.36, 0.5 }, () => panelView.ShownAt = Clock.Now, FrameP, () => surf, whole);
            double c0 = 0;
            Shoot("panel_close", "and closing: it folds into a line",
                new[] { 0.0, 0.04, 0.08, 0.12, 0.15, 0.18 }, () => { panelView.Closing = true; c0 = Clock.Now; }, FrameP, () => surf, whole,
                t => panelView.Vis = Math.Max(0, 1 - (float)t / 0.18f));
            panelView.Vis = 1; panelView.Closing = false; panelView.ShownAt = -10;
            // the keyboard: opening, a key pressed and let go, a modifier latching
            var kall = new RectangleF(0, 0, KeyboardPanel.TW, KeyboardPanel.TH);
            kb.Draw();
            Shoot("kb_open", "the keyboard opening: brackets, a scan line, the rows it passes flash",
                new[] { 0.0, 0.05, 0.1, 0.14, 0.18, 0.22, 0.27, 0.32, 0.4, 0.5 }, () => kb.OpenAtForTest = Clock.Now, kb.Draw, () => kb.Surf, kall);
            kb.OpenAtForTest = -10;
            int key = 30;                      // a letter in the middle of the board
            var kr = kb.KeyRectForTest(key);
            var around = RectangleF.Inflate(kr, kr.Width * 2.2f, kr.Height * 1.1f);
            kb.Draw();
            Shoot("kb_press", "a key pressed (cyan block), let go: the glow fades, its brackets fly out, the legend splits red / cyan",
                new[] { 0.0, 0.06, 0.1, 0.13, 0.16, 0.2, 0.25, 0.3, 0.38 }, () => kb.HoldForTest(key, true), kb.Draw, () => kb.Surf, around,
                t => { if (t >= 0.09) kb.HoldForTest(key, false); });
            int shift = kb.KeyIndexForTest("shift");
            var sr = kb.KeyRectForTest(shift);
            kb.Draw();
            Shoot("kb_latch", "Shift once (brackets snap in), then locked (cyan wipes in), then off",
                new[] { 0.0, 0.05, 0.1, 0.2, 0.3, 0.35, 0.4, 0.5, 0.62 }, () => kb.ModForTest("shift", 1), kb.Draw, () => kb.Surf,
                new RectangleF(0, sr.Y - 300, KeyboardPanel.TW, sr.Height + 320), t => { if (t >= 0.28 && t < 0.58) kb.ModForTest("shift", 2); else if (t >= 0.58) kb.ModForTest("shift", 0); });
            kb.ModForTest("shift", 0);
            // a notification scanning in
            using (var np = new NotificationPanel(gpu, painter, cfg))
            {
                var n = new Notification { Source = "VRCX", Title = "Friend online", Content = "Sam has joined The Black Cat (Friends+)" };
                np.AddForPreview(n);
                Shoot("notify", "a notification scans in", new[] { 0.0, 0.04, 0.08, 0.12, 0.16, 0.2, 0.24 }, null, np.Draw, () => np.Surface,
                    new RectangleF(0, 0, NotificationPanel.TW, 220), t => n.Vis = Math.Min(1, (float)t / 0.22f));
            }
            // the screen bar opening from its middle
            using (var bar = new TheaterBar(gpu, painter, cfg))
            {
                var a = new TheaterTest.FakeScreen { Name = "DELL U2723QE" };
                var b = new TheaterTest.FakeScreen { Name = "LG ULTRAGEAR" };
                var choices = new List<ITheaterScreen> { a, b };
                Surface bs = bar.DrawFor(choices, b, new TheaterView { Size = 2.8f });
                Shoot("bar", "the screen bar opens from its middle", new[] { 0.0, 0.03, 0.06, 0.09, 0.12, 0.15, 0.18 }, null,
                    () => bs = bar.DrawFor(choices, b, new TheaterView { Size = 2.8f }), () => bs, new RectangleF(0, 0, bs.W, bs.H),
                    t => bar.VisForTest = Math.Min(1, (float)t / 0.18f));
                bar.VisForTest = 1;
            }
            // a monitor's HUD frame: its unfold, the theater lock-on (drawn on their own; the moments, not real time)
            using (var fx = new CyberScreenFx(gpu, "test"))
            {
                var tex = new Vector2(1920, 1080);
                var moments = new (string tag, float k, bool hiding, double ta, double tl, double tr)[]
                {
                    ("u000", 0.02f, false, 0.0, -1, -1), ("u050", 0.3f, false, 0.05, -1, -1), ("u100", 0.62f, false, 0.1, -1, -1),
                    ("u160", 1, false, 0.16, -1, -1), ("u260", 1, false, 0.26, -1, -1),
                    ("h050", 0.5f, true, -1, -1, -1), ("h120", 0.08f, true, -1, -1, -1),
                    ("t000", 1, false, -1, 0.0, -1), ("t100", 1, false, -1, 0.1, -1), ("t180", 1, false, -1, 0.18, -1), ("t250", 1, false, -1, 0.25, -1),
                    ("t320", 1, false, -1, 0.32, -1), ("t450", 1, false, -1, 0.45, -1),
                    ("r100", 1, false, -1, -1, 0.1),
                };
                var files = new List<string>();
                foreach (var m in moments)
                {
                    var s = fx.DrawForTest(painter, tex, m.k, m.hiding, m.ta, m.tl, m.tr);
                    string file = $"cyberui_screen_{m.tag}.png";
                    SaveCrop(s, gpu, Path.Combine(qa, file), new RectangleF(0, 0, s.W, s.H), keepAlpha: true);
                    files.Add($"{file}:{m.k}");
                }
                index.AppendLine($"screen|a monitor's HUD frame: the unfold (k), going away, the theater lock-on|{string.Join(",", files)}");
            }
            using (var th = new Theater(gpu, painter))
            {
                var wall = th.GridForTest(0);
                SaveCrop(wall, gpu, Path.Combine(qa, "cyberui_wall_000.png"), new RectangleF(0, 0, wall.W, wall.H), keepAlpha: true);
                var sky = th.SkylineForTest();
                SaveCrop(sky, gpu, Path.Combine(qa, "cyberui_skyline.png"), new RectangleF(0, 0, sky.W, sky.H), keepAlpha: true);
                var fl = th.FloorForTest();                   // the floor grid in front, its three pieces (TheaterFloor.cs)
                for (int k = 0; k < fl.Length; k++) SaveCrop(fl[k], gpu, Path.Combine(qa, $"cyberui_floor_{k}.png"), new RectangleF(0, 0, fl[k].W, fl[k].H), keepAlpha: true);
                index.AppendLine("wall|the theater wall: the night city (sky, stars, neon horizon's glow), its floor grid in front (three pieces) and its skyline band|" +
                                 "cyberui_wall_000.png,cyberui_floor_0.png,cyberui_floor_1.png,cyberui_floor_2.png,cyberui_skyline.png");
            }
            {
                // the city's frame: level whatever way the head and the screen are tilted, facing the screen, at the head
                var hmd = Matrix4x4.CreateRotationX(0.5f) * Matrix4x4.CreateRotationZ(0.3f) * Matrix4x4.CreateTranslation(0.2f, 1.6f, 0.1f);
                var scr = new Vector3(0.9f, 2.4f, -2f);
                var lv = Theater.LevelFrame(hmd, scr);
                var fwd = -new Vector3(lv.M31, lv.M32, lv.M33);
                var flat = Vector3.Normalize(new Vector3(scr.X - 0.2f, 0, scr.Z - 0.1f));
                bool level = MathF.Abs(lv.M22 - 1) < 1e-4f, facing = Vector3.Dot(fwd, flat) > 0.9999f, at = Vector3.Distance(lv.Translation, hmd.Translation) < 1e-4f;
                Log.Info($"cyber ui: theater city frame level {level} (want True), facing the screen {facing} (want True), at the head {at} (want True)");
            }
            File.WriteAllText(Path.Combine(qa, "cyberui_index.txt"), index.ToString());
            Log.Info($"cyber ui: motion frames written to {qa}");
        }
        finally
        {
            CyberFx.Live = false;
            Motion.Still = still;
        }
    }

    static unsafe void SaveCrop(Surface s, Gpu gpu, string path, RectangleF crop, bool keepAlpha = false)
    {
        var px = s.ReadPixels(gpu);
        var c = Rectangle.Intersect(Rectangle.Round(crop), new Rectangle(0, 0, s.W, s.H));
        using var bmp = new Bitmap(c.Width, c.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, c.Width, c.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        for (int y = 0; y < c.Height; y++)
        {
            uint* dst = (uint*)((byte*)bd.Scan0 + y * bd.Stride);
            for (int x = 0; x < c.Width; x++)
            {
                uint v = px[(c.Y + y) * s.W + c.X + x];
                dst[x] = keepAlpha ? v : v | 0xFF000000;
            }
        }
        bmp.UnlockBits(bd);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
