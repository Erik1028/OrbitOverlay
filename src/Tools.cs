using System;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Valve.VR;

namespace Orbit;

// Developer tools: --snapshot renders every panel to PNG without VR (every retro style too: *_95, *_xp, *_aero, *_mac, *_term; and
// Cyber, *_cyber; --snapshot <dir> --only-style xp - or ORBIT_SNAP_STYLE=xp / cyber - renders one style's screens alone in seconds);
// --selftest checks the
// SteamVR side.
static partial class Tools
{
    sealed class FakeHost : IOrbitHost
    {
        public ConfigStore Cfg { get; init; }
        public bool VrConnected => true;
        public string VrStatus => "SteamVR · 90 Hz";
        public string InputStatus => "ok";
        public string GpuName { get; init; }
        public IReadOnlyList<MonitorInfo> MonitorList { get; init; }
        public PanelData Data { get; init; }
        public WristPanel Wrist { get; init; }
        public KeyboardPanel Keyboard { get; init; }
        public void Recenter() { }
        public void ResetDesktop(DesktopConfig d) { }
        public void ResetWrist() { }
        public void ResetKeyboard() { }
        public void SetDesktopAttach(DesktopConfig d, string a) => d.Place.Attach = a;
        public void SetKeyboardAttach(string a) { }
        public void SetDesktopEnabled(DesktopConfig d, bool on) => d.Enabled = on;
        public void SetAutoStart(bool on) { }
        public string AutoStartStatus => "on: SteamVR starts OrbitOverlay and closes it on exit";
        public void ToggleKeyboard() { }
        public void ToggleDesktops() { }
        public void OpenConfigFolder() { }
        public int GroupSize(DesktopConfig d) => d.Group == null ? 1 : Cfg.Data.Desktops.Count(x => x.Group == d.Group);
        public void JoinAllDesktops() { }
        public void SeparateDesktops() { }
        public void FitCurve(DesktopConfig d) { }
        public void DesktopEdited(DesktopConfig d) { }
        public void AppearanceChanged() => Theme.Apply(Cfg.Data.Appearance);
        public bool IsElevated => true;
        public VirtualMonitors Virtual { get; set; }
        public void SetRunAsAdmin(bool on) { }
        public void RestartElevated() { }
        public string NotificationStatus => Lang.F("listening on UDP {0}", 42069);
        public string StatusLinePreview => "\U0001F3B5 Midnight City \u2013 M83";
        public string RoomStatus => Lang.F("{0} in {1}", 12, "Midnight Rooftop") + Lang.F(" · VRCX: {0} friends", 120);
        public string EyeStatus => Lang.T("your eyes are tracked");
        public void ToggleRoom() { }
        public void TheaterEdited() { }
        public IReadOnlyList<WhatsNew.Entry> WhatsNewList { get; set; } = Array.Empty<WhatsNew.Entry>();
        public string WhatsNewSub { get; set; } = "";
        public void DismissWhatsNew() => WhatsNewList = Array.Empty<WhatsNew.Entry>();
        public void TestNotification() { }
        List<WindowInfo> _open;
        public IReadOnlyList<WindowInfo> OpenWindows() => _open ??= WindowList.Enumerate();
        public bool IsMirrored(IntPtr h) => Cfg.Data.Windows.Count > 0 && _open?.Count > 1 && _open[1].Hwnd == h;
        public WindowConfig AddWindow(WindowInfo w)
        {
            var wc = new WindowConfig { Title = w.Title, Process = w.Process, Class = w.Class };
            Cfg.Data.Windows.Add(wc);
            return wc;
        }
        public void RemoveWindow(WindowConfig w) => Cfg.Data.Windows.Remove(w);
        public string WindowStatus(WindowConfig w) => w.Enabled ? "live · 1600×900" : "hidden";
        public void ResetWindow(WindowConfig w) { }
        public void SetWindowAttach(WindowConfig w, string a) => w.Place.Attach = a;
        public void SetWindowEnabled(WindowConfig w, bool on) => w.Enabled = on;
        public void SwitchLayout(string name) => Cfg.Data.ActiveLayout = name;
        public Updater Updates { get; } = new();
        public void ForgetLayout(string name) { }
        // 1.14 (ToolsExtras.cs): a made-up running app; the cards asked for
        public (string key, string name) RunningApp { get; set; }
        public readonly List<(string title, string text)> Notes = new();
        public void Notify(string title, string text) => Notes.Add((title, text));
    }

    // onlyStyle (--only-style 95 | xp | aero | mac | term, or ORBIT_SNAP_STYLE): that retro style's screens alone, in seconds - the
    // modern pictures, the other styles, the fonts and the pages left out (for working on a style; its layout check runs too)
    static int SnapshotRun(string dir, string onlyStyle = null)
    {
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(dir);
        onlyStyle ??= Environment.GetEnvironmentVariable("ORBIT_SNAP_STYLE");
        RetroSkin only = null;
        bool onlyCyber = onlyStyle == CyberHud.Id;          // Cyber: a modern style, its own run (CyberShots)
        if (!string.IsNullOrEmpty(onlyStyle) && !onlyCyber)
        {
            only = Array.Find(Skins.All, k => k.Tag == onlyStyle || k.Id == onlyStyle);
            if (only == null) { Log.Error($"snapshot: no style '{onlyStyle}' (95, xp, aero, mac, term, cyber)"); return 1; }
        }
        bool everything = only == null && !onlyCyber;
        // separate config: a snapshot never touches the real one
        var cfg = new ConfigStore(Path.Combine(dir, "config.json"));
        using var gpu = new Gpu(-1);
        using var painter = new Painter(gpu);

        var data = new PanelData { VrConnected = true, VramTotalGB = gpu.DedicatedVram / (1024f * 1024 * 1024), FakeGpuLoad = 63, FakeVram = 9.4f };
        data.VrSince = DateTime.Now.AddMinutes(-83);
        data.Vr.AppFps = 86; data.Vr.ReprojPct = 4; data.Vr.GpuMs = 9.3f; data.Vr.DisplayHz = 90;
        data.Vr.Batteries.Add(("HMD", 0.71f, false));
        data.Vr.Batteries.Add(("L", 0.82f, false));
        data.Vr.Batteries.Add(("R", 0.18f, true));
        data.Vr.Batteries.Add(("Trk×6", 0.44f, false));
        data.FakeMedia = (true, "Midnight City", "M83", true);
        data.Photos = PhotoFake.Snap();                 // made-up photos (PhotoViewTest.cs): a tool never reads a real one
        data.Weather = WeatherViewTools.Report();       // a made-up forecast for a made-up town: never the network (WeatherViewTest.cs)
        data.Friends = SocialViewTools.Friends();       // made-up friends online and a made-up evening's recap: never VRCX's (SocialViewTest.cs)
        data.Recap = SocialViewTools.Recap();
        LightsViewTools.Fake(data);                     // a made-up light and RGB: never Lumos, Prisma or a device (LightsViewTest.cs)
        AvatarViewTools.Fake(data);                     // a made-up avatar's toggles: never VRChat, its OSC folder or its port (AvatarViewTest.cs)
        data.Memories = OnThisDayTempsTools.Memories(); // made-up photos of this day in earlier years, made-up temperatures (OnThisDayTempsTest.cs)
        data.Temps = OnThisDayTempsTools.Temps();
        var frng = new Random(3);
        for (int i = 0; i < VrStats.FrameHistory; i++)
        {
            data.Vr.FrameMs[i] = 7.5f + (float)frng.NextDouble() * 2.5f + (i % 97 == 0 ? 6 : 0);
            data.Vr.FrameFlag[i] = (byte)(i % 97 == 0 ? 1 : i == 300 ? 2 : 0);
        }
        data.Vr.FrameHead = 0; data.Vr.FrameCount = VrStats.FrameHistory;

        using var wrist = new WristPanel(gpu, painter, cfg);
        wrist.Mic.Fake = true;                          // never the real microphone: a tap flips a made-up one
        wrist.Mic.Present = true;
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "pages") return PagesRun(wrist, data, cfg, painter, gpu, dir, null, null, null);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "mixer") return MixerOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "notes") return NotesOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "photo") return PhotoOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "weather") return WeatherOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "folder") return FolderOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "social") return SocialOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "lights") return LightsOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "onthisday") return OnThisDayTempsTools.Only(wrist, data, cfg, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "mixtape") return MixtapeOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") is { } kindOnly && kindOnly.StartsWith("kind:")) return KindOnly(wrist, data, cfg, painter, gpu, dir, kindOnly[5..]);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "avatar") return AvatarOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "backdrops") return BackdropsOnly(cfg, painter, gpu, dir);    // theater's backdrops (ToolsBackdrops.cs)
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "citylife") return CityLifeOnly(cfg, painter, gpu, dir);     // Cyber's night city alive (CityLifeTest.cs)
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "ambilight") return AmbilightOnly(cfg, painter, gpu, dir);     // theater's glow (AmbilightTest.cs)
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "pulse") return PulseOnly(cfg, painter, gpu, dir);           // Cyber's horizon (HorizonPulseTest.cs)
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "autotheater") return AutoTheaterOnly(wrist, data, cfg, painter, gpu, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "mediabar") return MediaBarOnly(cfg, painter, gpu, dir);     // theater bar's media console (MediaBarTest.cs)
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "focus") return FocusOnly(wrist, data, cfg, painter, gpu, dir);     // gaze focus (GazeFocusTest.cs)
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "theaterperf") return TheaterPerfOnly(cfg, painter, gpu, dir);  // a theater frame's cost, 20 s of it (ToolsTheaterPerf.cs)
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "cyfloor") return CyberFloorOnly(cfg, painter, gpu, dir);        // Cyber theater's floor grid, the headset views (CyberFloorTest.cs)
        if (everything)
        {
            var sw0 = System.Diagnostics.Stopwatch.StartNew();
            wrist.Draw(data);
            gpu.Ctx.Flush();
            Log.Info($"first wrist draw {sw0.Elapsed.TotalMilliseconds:0.0} ms");
            sw0.Restart(); wrist.Draw(data); gpu.Ctx.Flush();
            Log.Info($"second wrist draw {sw0.Elapsed.TotalMilliseconds:0.0} ms");
            wrist.Surf.SavePng(gpu, Path.Combine(dir, "wrist.png"));
            foreach (var face in new[] { "analog", "minimal", "time" })
            {
                cfg.Data.Wrist.Face = face;
                cfg.Data.Wrist.PerfGraph = face == "analog";
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_{face}.png"));
            }
            cfg.Data.Wrist.Face = "full";
            cfg.Data.Wrist.PerfGraph = true;
            wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, "wrist_graph.png"));
            cfg.Data.Wrist.PerfGraph = false;
        }

        using var kb = new KeyboardPanel(gpu, painter, cfg);
        if (everything)
        {
            kb.Draw();
            kb.Surf.SavePng(gpu, Path.Combine(dir, "keyboard.png"));
            cfg.Data.Keyboard.Language = "en-US";
            kb.ReloadLayout();
            kb.Draw();
            kb.Surf.SavePng(gpu, Path.Combine(dir, "keyboard_en.png"));
            cfg.Data.Keyboard.Language = "windows";
            kb.ReloadLayout();
            kb.PreviewChat("Szia! Mindjárt jövök, csak befejezem ezt :)");
            kb.Draw();
            kb.Surf.SavePng(gpu, Path.Combine(dir, "keyboard_chat.png"));
            kb.PreviewChat("");
            typeof(KeyboardPanel).GetField("_chat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(kb, false);
        }
        var parsed = NotificationListener.Parse(System.Text.Encoding.UTF8.GetBytes(
            "{\"messageType\":1,\"index\":0,\"timeout\":5.0,\"height\":110.0,\"opacity\":1.0,\"volume\":0.7,\"audioPath\":\"\",\"title\":\"<b>Friend online</b>\",\"content\":\"Sam is in The Black Cat\",\"useBase64Icon\":false,\"icon\":\"default\",\"sourceApp\":\"VRCX\"}"));
        Log.Info($"notification parse: title='{parsed?.Title}' content='{parsed?.Content}' source={parsed?.Source} timeout={parsed?.Timeout}");

        var mons = Monitors.Enumerate();
        foreach (var m in mons)
        {
            Log.Info("monitor " + m);
            if (cfg.Data.Desktops.All(d => d.Id != m.Id))
                cfg.Data.Desktops.Add(new DesktopConfig { Id = m.Id, Name = m.Name, Enabled = !m.Virtual });
        }

        // capture each monitor once
        var dev = MonitorCapture.CreateWinRtDevice(gpu);
        int n = 0;
        foreach (var m in everything ? mons : new List<MonitorInfo>())
        {
            try
            {
                using var cap = new MonitorCapture(gpu, dev, m);
                bool got = false;
                for (int i = 0; i < 100 && !got; i++) { got = cap.Poll(); if (!got) Thread.Sleep(20); }
                Log.Info($"capture {m.Name}: {(got ? "frame ok" : "NO FRAME")} {cap.Target.W}x{cap.Target.H}");
                if (got) cap.Target.SavePng(gpu, Path.Combine(dir, $"desktop{++n}.png"), premultiplied: false);
            }
            catch (Exception e) { Log.Error($"capture {m}: {e}"); }
        }

        // (the Desktops page with two virtual monitors on: a made-up state, nothing installed - ORBIT_SNAP_ONLY=virtual has all the states)
        cfg.Data.General.VirtualMonitors = 2;
        var host = new FakeHost { Cfg = cfg, GpuName = gpu.AdapterName, MonitorList = mons, Data = data, Wrist = wrist, Keyboard = kb,
            Virtual = new VirtualMonitors { Fake = true, Admin = true, State = VirtualMonitors.Kind.On, Count = 2 } };
        var view = new SettingsView(host);
        var ui = new Ui(painter);
        using var surf = new Surface(gpu, 1500, 960);
        var ptr = new PointerState();
        // "What's new" after an update from 1.8.2: over the settings (General tab) and as the VR panel
        void WhatsNewShots(string st, bool modern = false)
        {
            host.WhatsNewList = WhatsNew.Since("1.8.2");
            host.WhatsNewSub = Lang.F("Updated to {0} from {1}", Updater.CurrentText, "1.8.2");
            Log.Info($"what's new since 1.8.2: {host.WhatsNewList.Count} versions, {WhatsNew.All.Count} in the file");
            int keep = view.Tab;
            view.Tab = SettingsView.TabGeneral;
            ui.ResetAnimations();
            if (st == "" || modern)
            {
                ui.Begin(ptr, 1.5f);
                painter.Begin(surf.Bitmap, Theme.Bg, 1.5f);
                view.Draw(ui);
                painter.End();
                ui.End();
            }
            else view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
            surf.SavePng(gpu, Path.Combine(dir, $"settings_whatsnew{st}.png"));
            using var wnp = new WhatsNewPanel(gpu, painter);
            wnp.DrawFor(host.WhatsNewList, host.WhatsNewSub);
            wnp.Surf.SavePng(gpu, Path.Combine(dir, $"whatsnew_panel{st}.png"));
            host.WhatsNewList = Array.Empty<WhatsNew.Entry>();
            view.Tab = keep;
        }
        // a widget's look in the config: written only once set, and read back as it was
        {
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string pj = System.Text.Json.JsonSerializer.Serialize(new WatchWidget { Type = "cpu", X = 1, Y = 2 }, opts);
            var styled = new WatchWidget { Type = "cpu", Style = "twotone", Color = 0xFF14B8A6, Color2 = 0xFFA855F7 };
            var back = System.Text.Json.JsonSerializer.Deserialize<WatchWidget>(System.Text.Json.JsonSerializer.Serialize(styled, opts), opts);
            Log.Info($"config: an unstyled widget writes no look {!pj.Contains("Style") && !pj.Contains("Color")} (want True), a styled one reads back " +
                     $"{back.Style == "twotone" && back.Color == 0xFF14B8A6 && back.Color2 == 0xFFA855F7} (want True)");
        }
        VariantWants();
        // the modern-only sizes (time 1x1 and 2x2, date 1x1): Clean() keeps them whatever the style (one face for both); the modern
        // editor's Smaller / corner drag / add reach them, the 95 one's do not
        {
            List<WatchWidget> Extras() => new()
            {
                new() { Type = "time", X = 0, Y = 0, W = 1, H = 1 }, new() { Type = "time", X = 1, Y = 0, W = 2, H = 2 },
                new() { Type = "date", X = 3, Y = 0, W = 1, H = 1 }, new() { Type = "time", X = 4, Y = 0, W = 2, H = 1 },
                new() { Type = "analog", X = 3, Y = 1, W = 1, H = 1 },
            };
            static string Sizes(List<WatchWidget> l) => string.Join(" ", l.Select(w => $"{w.Type}{w.W}x{w.H}"));
            string want = Sizes(Extras());
            Log.Info($"sizes: Clean() keeps a modern-made face (time 1x1, 2x2, date 1x1, analog 1x1) {Sizes(WatchWidgets.Clean(Extras())) == want} (want True)");
            var time = WatchWidgets.Find("time");
            var one = new List<WatchWidget> { new() { Type = "time", X = 0, Y = 0, W = 2, H = 1 }, new() { Type = "date", X = 0, Y = 2, W = 2, H = 1 } };
            string S((int W, int H)? s) => s is { } v ? $"{v.W}x{v.H}" : "none";
            Log.Info($"sizes: Smaller from time 2x1 modern {S(WatchWidgets.Step(one, one[0], -1, false))} (want 1x1), 95 {S(WatchWidgets.Step(one, one[0], -1, true))} " +
                     $"(want none); date 2x1 modern {S(WatchWidgets.Step(one, one[1], -1, false))} (want 1x1), 95 {S(WatchWidgets.Step(one, one[1], -1, true))} (want none)");
            Log.Info($"sizes: corner drag to 1x1 modern {S(WatchWidgets.SnapSize(time, 1, 1, false))} (want 1x1), 95 {S(WatchWidgets.SnapSize(time, 1, 1, true))} " +
                     $"(want 2x1); to 2x2 modern {S(WatchWidgets.SnapSize(time, 2, 2, false))} (want 2x2), 95 {S(WatchWidgets.SnapSize(time, 2, 2, true))} (want 2x1)");
            // a 1x1 hole (every other cell taken): the modern palette fits a time there, the 95 one finds no room
            var full = new List<WatchWidget>();
            for (int y = 0; y < WatchWidgets.Rows; y++)
                for (int x = 0; x < WatchWidgets.Cols; x++)
                    if (x != 2 || y != 1) full.Add(new WatchWidget { Type = "night", X = x, Y = y });
            var addM = WatchWidgets.Add(new List<WatchWidget>(full), time, 2, 1, false);
            var add95 = WatchWidgets.Add(new List<WatchWidget>(full), time, 2, 1, true);
            Log.Info($"sizes: a time added into a 1x1 hole modern {(addM == null ? "none" : $"{addM.W}x{addM.H}")} (want 1x1), 95 {(add95 == null ? "none" : $"{add95.W}x{add95.H}")} (want none)");
            var analog = WatchWidgets.Find("analog");
            var anM = WatchWidgets.Add(new List<WatchWidget>(full), analog, 2, 1, false);
            var an95 = WatchWidgets.Add(new List<WatchWidget>(full), analog, 2, 1, true);
            Log.Info($"sizes: an analog clock added into a 1x1 hole modern {(anM == null ? "none" : $"{anM.W}x{anM.H}")} (want 1x1), 95 {(an95 == null ? "none" : $"{an95.W}x{an95.H}")} (want none); " +
                     $"into an empty face {(WatchWidgets.Add(new List<WatchWidget>(), analog) is { } e ? $"{e.W}x{e.H}" : "none")} (want 2x2)");
        }
        // a used "Bed" profile with night mode for the Layouts tab
        cfg.Data.Layouts[1].Saved = new DateTime(2026, 10, 13, 23, 40, 0);
        cfg.Data.Layouts[1].NightMode = true;
        cfg.Data.Layouts[1].Panels = cfg.Data.Desktops.Select(d => new PanelSnapshot { Kind = "desktop", Id = d.Id, Enabled = true }).ToList();
        data.NightMode = true;
        data.LayoutName = "Desk";
        wrist.Draw(data);
        wrist.Surf.SavePng(gpu, Path.Combine(dir, "wrist_buttons.png"));
        data.NightMode = false;
        var wcfg = cfg.Data.Wrist;
        string face0 = wcfg.Face;
        var panelView = new SettingsView(host) { OnClose = () => { }, Tab = SettingsView.TabGeneral };
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "tabs") return TabsOnly(view, ui, surf, ptr, painter, gpu, wrist, kb, data, cfg, dir);   // ToolsTabs.cs
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "undo") return UndoOnly(view, ui, surf, ptr, painter, gpu, cfg, wrist, data, dir);   // UndoTest.cs
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "virtual") return VirtualOnly(host, view, ui, surf, ptr, painter, gpu, cfg, dir);   // VirtualMonitorsTest.cs
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "anim_cyber") return CyberAnimShot.Run(wrist, data, cfg, gpu, dir, kb, view, ui, surf, ptr, painter);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "cyberui") return CyberUiOnly(view, panelView, ui, surf, ptr, painter, gpu, cfg, wrist, data, kb, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "cyberperf") return CyberPerfOnly(view, ui, surf, ptr, painter, gpu, cfg, kb);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "perf") return PerfOnly(view, panelView, ui, surf, ptr, painter, gpu, cfg, kb, data, dir, mons, dev);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "extras") return ExtrasRun(wrist, kb, view, panelView, ui, surf, ptr, painter, gpu, cfg, data, host, dir);
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "overlaps") { OverlapRun(wrist, kb, view, ui, surf, ptr, painter, cfg, data, gpu, dir); return 0; }
        // the font pass alone (Look > Font: every installed font's texts against their boxes - 5 1/2 min with 10 fonts), whatever ORBIT_SNAP_FONTS says
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_ONLY") == "fonts") return FontsRun(wrist, kb, view, ui, surf, ptr, painter, gpu, cfg, data, host, dir, Gallery, true) > 0 ? 1 : 0;
        if (only != null)
        {
            RetroShots(only);
            Log.Info($"snapshot ({only.Name} only) written to " + dir);
            return 0;
        }
        if (onlyCyber)
        {
            CyberShots();
            Log.Info($"snapshot ({CyberHud.Name} only) written to " + dir);
            return 0;
        }
        for (int t = 0; t < SettingsView.TabCount; t++)
        {
            view.Tab = t;
            ui.ResetAnimations();          // every picture fully settled (no half-way nav pill / buttons)
            if (view.ShowsWristPreview) wrist.Draw(data);
            if (view.ShowsKeyboardPreview) kb.Draw();
            ui.Begin(ptr, 1.5f);
            painter.Begin(surf.Bitmap, Theme.Bg, 1.5f);
            view.Draw(ui);
            painter.End();
            ui.End();
            surf.SavePng(gpu, Path.Combine(dir, $"settings{t}.png"));
        }
        WhatsNewShots("");
        // the floating in-VR settings panel (has a close button), General tab
        // Windows tab with two mirrored windows (one hidden), then the picker on top of them
        var open = host.OpenWindows();
        Log.Info($"open windows: {open.Count}" + string.Concat(open.Take(12).Select(w => $"\n   {w}")));
        if (open.Count > 1)
        {
            host.AddWindow(open[1]);
            var hidden = host.AddWindow(open[0]);
            hidden.Enabled = false;
            view.Tab = SettingsView.TabWindows;
            for (int pass = 0; pass < 2; pass++)
            {
                view.Picking = pass == 1;
                ui.Begin(ptr, 1.5f);
                painter.Begin(surf.Bitmap, Theme.Bg, 1.5f);
                view.Draw(ui);
                painter.End();
                ui.End();
                surf.SavePng(gpu, Path.Combine(dir, pass == 0 ? "settings_windows.png" : "settings_windows_pick.png"));
            }
            view.Picking = false;
        }
        // one real window capture: the frame must be the window's visible bounds (mouse mapping relies on it)
        foreach (var w in open)
        {
            if (WindowList.IsIconic(w.Hwnd) || !WindowList.Bounds(w.Hwnd, out var b)) continue;
            try
            {
                using var cap = MonitorCapture.ForWindow(gpu, dev, w.Hwnd);
                bool got = false;
                for (int i = 0; i < 100 && !got; i++) { got = cap.Poll(); if (!got) Thread.Sleep(20); }
                if (!got) for (int i = 0; i < 50 && !got; i++) { got = cap.Poll(); if (!got) Thread.Sleep(20); }   // a resize first
                Log.Info($"window capture {w}: {(got ? "frame ok" : "NO FRAME")} {cap.Target.W}x{cap.Target.H}, frame bounds {b.Right - b.Left}x{b.Bottom - b.Top}");
                if (got) cap.Target.SavePng(gpu, Path.Combine(dir, "window1.png"), premultiplied: false);
            }
            catch (Exception e) { Log.Error($"window capture {w}: {e.Message}"); }
            break;
        }
        for (int frame = 0; frame < 2; frame++)      // 2nd frame: animations settled
        {
            ui.Begin(ptr, 1.5f);
            painter.Begin(surf.Bitmap, 0x00000000, 1.5f);
            SettingsPanel.DrawBody(painter, ui, panelView);       // as the VR panel draws it: rounded corners, the outline
            painter.End();
            ui.End();
        }
        surf.SavePng(gpu, Path.Combine(dir, "settings_panel.png"));
        // cost of one in-VR settings frame, as the panel (its rounded corners and outline) and the page alone
        foreach (bool rounded in new[] { true, false, true, false })
        {
            var swb = System.Diagnostics.Stopwatch.StartNew();
            for (int frame = 0; frame < 20; frame++)
            {
                ui.Begin(ptr, 1.5f);
                painter.Begin(surf.Bitmap, 0x00000000, 1.5f);
                if (rounded) SettingsPanel.DrawBody(painter, ui, panelView);
                else panelView.Draw(ui);
                painter.End();
                ui.End();
            }
            gpu.Ctx.Flush();
            Log.Info($"settings frame ({(rounded ? "rounded" : "plain")}): {swb.Elapsed.TotalMilliseconds / 20:0.00} ms");
        }
        // the laser sweeping over the General tab: which moves need a new picture
        foreach (float sy in new[] { 150f, 330f, 480f })
        {
            var sweep = new PointerState();
            int moves = 0, redraws = 0;
            for (float x = 10; x < SettingsView.W - 10; x += 1.5f)
            {
                sweep.Move(x * 1.5f, sy * 1.5f);
                moves++;
                if (ui.NeedsRedrawForMove(sweep, 1.5f)) redraws++;
            }
            Log.Info($"pointer sweep at y={sy}: {redraws} redraws for {moves} moves");
        }

        // the same panels in another look (pink accent, midnight panels, 70 % opacity)
        cfg.Data.Appearance = new AppearanceConfig { Accent = 0xFFF472B6, PanelColor = 0xFF0F172A, PanelOpacity = 0.7f, Clock24h = false };
        Theme.Apply(cfg.Data.Appearance);
        wrist.Draw(data);
        wrist.Surf.SavePng(gpu, Path.Combine(dir, "wrist_alt.png"));
        kb.Draw();
        kb.Surf.SavePng(gpu, Path.Combine(dir, "keyboard_alt.png"));
        view.Tab = SettingsView.TabLook;
        wrist.Draw(data);
        ui.Begin(ptr, 1.5f);
        painter.Begin(surf.Bitmap, Theme.Bg, 1.5f);
        view.Draw(ui);
        painter.End();
        ui.End();
        surf.SavePng(gpu, Path.Combine(dir, "settings_alt.png"));
        // Look > Style: its list opened over the page (every style with its little window), Windows XP picked - it applies at once -,
        // then Modern again from the XP sheet's Style box
        {
            void FrameL() { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
            void ClickL(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); FrameL(); ptr.Press(); FrameL(); ptr.Release(); FrameL(); }
            void FrameS() => view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
            void ClickS(float x, float y) { ptr.Move(x * 2, y * 2); FrameS(); ptr.Press(); FrameS(); ptr.Release(); FrameS(); FrameS(); }
            ui.ResetAnimations();
            float listTop = 512 - 6 - StyleMenu.Ids.Length * 40 - 12;     // the list over the Style field (SettingsView.StyleList)
            ClickL(410, 531);                                     // the Style field
            ptr.Move(410 * 1.5f, (listTop + 6 + 2 * 40 + 20) * 1.5f);  // over Windows XP in the list
            FrameL(); FrameL();
            surf.SavePng(gpu, Path.Combine(dir, "settings_look_styles.png"));
            ClickL(410, listTop + 6 + 2 * 40 + 20);
            bool xp = cfg.Data.Appearance.Style == "xp" && Theme.Retro == RetroStyle.XP && Theme.Classic;
            ptr.Leave();
            ClickS(180, 86);                                      // the XP sheet's Style box: its list opens
            ClickS(180, 98 + Theme.RetroSkin.DropRowH / 2);       // Modern (the first)
            Log.Info($"look: Windows XP picked from the Style list applies at once {xp} (want True); Modern again from the sheet's Style box " +
                     $"{cfg.Data.Appearance.Style == "modern" && !Theme.Classic} (want True)");
            ptr.Leave();
            ui.ResetAnimations();
        }
        // Look > Motion (Gfx/Motion.cs), its segments in the Look tab: a pick applies at once and is written only when not Full; Off
        // puts a spring at its target in one frame (an eased value too); Reduced skips the watch's cascade, Full plays it
        {
            void FrameL() { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
            void ClickL(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); FrameL(); ptr.Press(); FrameL(); ptr.Release(); FrameL(); }
            static float Seg(int i) => 612 + 356 / 3f * (i + 0.5f);
            static bool Written(AppearanceConfig x) => System.Text.Json.JsonSerializer.Serialize(x).Contains("\"Motion\"");
            var am = cfg.Data.Appearance;
            view.Tab = SettingsView.TabLook;
            ui.ResetAnimations();
            ClickL(Seg(1), 607);
            bool reduced = am.Motion == "reduced" && Motion.Level == MotionLevel.Reduced && Written(am);
            wrist.Settle();
            wrist.Intro();
            bool cascadeReduced = wrist.IntroRunning;
            ClickL(Seg(2), 607);
            bool off = am.Motion == "off" && Motion.Level == MotionLevel.Off && Written(am);
            var mu = new Ui(painter);
            mu.Spring("s", 0); mu.Anim("a", 0);
            bool snapS = mu.Spring("s", 100) == 100, snapA = mu.Anim("a", 1) == 1;
            ClickL(Seg(0), 607);
            bool full = am.Motion == null && Motion.Full && !Written(am);
            var fu = new Ui(painter);
            fu.Spring("s", 0);
            bool moves = fu.Spring("s", 100) < 100;
            wrist.Settle();
            wrist.Intro();
            bool cascadeFull = wrist.IntroRunning;
            wrist.Settle();
            ptr.Leave();
            ui.ResetAnimations();
            Log.Info($"motion: Reduced picked in the Look tab applies at once and is written {reduced} (want True), Off {off} (want True), " +
                     $"Full again writes nothing {full} (want True)");
            Log.Info($"motion: Off - a spring is at its target in one frame {snapS} (want True), an eased value too {snapA} (want True); " +
                     $"Full - it moves there {moves} (want True)");
            Log.Info($"motion: Reduced skips the watch's cascade {!cascadeReduced} (want True), Full plays it {cascadeFull} (want True)");
        }
        KeySounds.Export(dir);
        using (var np = new NotificationPanel(gpu, painter, cfg))
        {
            np.AddForPreview(new Notification { Source = "VRCX", Title = "Friend online", Content = "Sam has joined The Black Cat (Friends+)" });
            np.AddForPreview(new Notification { Source = "Discord", Title = "New message", Content = "Alex: are you in VR? The new watch looks great, the cursor is way smoother now." });
            np.Draw();
            np.Surface.SavePng(gpu, Path.Combine(dir, "notifications.png"));
        }
        using (var cur = new SystemCursor(gpu))
        {
            cur.Update(1);
            cur.Surf.SavePng(gpu, Path.Combine(dir, "cursor.png"));
            Log.Info($"cursor hotspot {cur.Hotspot}");
        }
        // ---- the custom face (widgets on the grid): the watch, the Wrist tab, the editor and a scripted edit ----
        // In the default look (the Bento theme, #FF5D93 on #0A0B10), so the galleries check the ON tiles and discs in the colours
        // a new watch has; the 12-hour clock of the look above is kept (AM / PM is the tighter fit).
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = false };
        Theme.Apply(cfg.Data.Appearance);
        List<WatchWidget> Gallery(int n)
        {
            var l = new List<WatchWidget>();
            void Put(string t, int x, int y, int w, int h) => l.Add(new WatchWidget { Type = t, X = x, Y = y, W = w, H = h });
            if (n == 0)
            {
                Put("analog", 0, 0, 2, 2); Put("time", 2, 0, 2, 1); Put("date", 2, 1, 2, 1); Put("battery", 4, 0, 2, 2);
                Put("media", 0, 2, 3, 1); Put("graph", 3, 2, 3, 1);
                Put("fps", 0, 3, 1, 1); Put("reproj", 1, 3, 1, 1); Put("gpu", 2, 3, 1, 1); Put("vram", 3, 3, 1, 1);
                Put("night", 4, 3, 1, 1); Put("layout", 5, 3, 1, 1);
            }
            else if (n == 1)
            {
                Put("media", 0, 0, 4, 2); Put("battery", 4, 0, 1, 1); Put("fps", 5, 0, 1, 1); Put("keyboard", 4, 1, 2, 1);
                Put("perf", 0, 2, 6, 1); Put("time", 0, 3, 3, 1); Put("desktops", 3, 3, 2, 1); Put("settings", 5, 3, 1, 1);
            }
            else
            {
                // the newer widgets
                Put("calendar", 0, 0, 2, 2); Put("timer", 2, 0, 2, 2); Put("battery", 4, 0, 2, 2);
                Put("stopwatch", 0, 2, 2, 1); Put("volume", 2, 2, 3, 1); Put("session", 5, 2, 1, 1);
                Put("cpu", 0, 3, 2, 1); Put("ram", 2, 3, 1, 1); Put("calendar", 3, 3, 1, 1); Put("volume", 4, 3, 2, 1);
            }
            return l;
        }
        void SaveCustom(string tag)
        {
            for (int n = 0; n < 3; n++)
                foreach (var bg in new[] { "panel", "cards" })
                {
                    wcfg.Widgets = Gallery(n);
                    wcfg.Background = bg;
                    wrist.Settle();
                    wrist.Draw(data);
                    wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_custom{n}_{bg}{tag}.png"));
                }
            wcfg.Background = "panel";
            wcfg.Face = face0;
            wcfg.Widgets = null;
        }
        wcfg.Face = "custom";
        var culture0 = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;   // docs pictures in English
        wrist.Stats.Sample(Clock.Now);                 // a CPU reading needs two samples
        System.Threading.Thread.Sleep(400);
        wrist.Stats.Sample(Clock.Now + 2);
        SaveCustom("");
        System.Globalization.CultureInfo.CurrentCulture = culture0;
        wcfg.Face = "custom";
        foreach (int g in new[] { 0, 2 })
        {
            wcfg.Widgets = Gallery(g);
            wrist.Settle();
            wrist.Draw(data);
            var swc = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 30; i++) { wrist.Draw(data); gpu.Ctx.Flush(); }
            Log.Info($"custom face {g}: {swc.Elapsed.TotalMilliseconds / 30:0.00} ms per draw (settled)");
            wrist.Intro();
            swc.Restart();
            int frames = 0;
            while (wrist.Animating && swc.Elapsed.TotalSeconds < 3) { wrist.Draw(data); gpu.Ctx.Flush(); frames++; Thread.Sleep(22); }
            Log.Info($"custom face {g}: the cascade settles in {swc.Elapsed.TotalSeconds:0.00} s ({frames} frames at ~45 fps)");
        }
        wcfg.Widgets = Gallery(0);
        wrist.Draw(data);
        wrist.Intro();
        Thread.Sleep(170);
        wrist.Draw(data);
        wrist.Surf.SavePng(gpu, Path.Combine(dir, "wrist_custom_intro.png"));
        Thread.Sleep(1300);
        wrist.Draw(data);
        wcfg.Face = face0;
        wcfg.Widgets = null;
        void LayoutCheck(string style)
        {
            string f0 = wcfg.Face;
            LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, style);
            wcfg.Face = f0;
            wcfg.Widgets = null;
        }
        // every opened view once (both styles: see the 95 section), and a tap that opens one / a tap beside it that closes it
        void SaveOpened(string tag)
        {
            var kinds = new[] { ("media", 0), ("battery", 0), ("time", 0), ("calendar", 2), ("timer", 2), ("stopwatch", 2), ("fps", 0), ("cpu", 2), ("volume", 2) };
            foreach (var (type, g) in kinds)
            {
                wcfg.Face = "custom";
                wcfg.Widgets = Gallery(g);
                wrist.Settle();
                if (!wrist.OpenForTest(type)) { Log.Warn("snapshot: no widget " + type); continue; }
                wrist.Draw(data);
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_open_{type}{tag}.png"));
                wrist.CloseForTest();
            }
            RoomViewTools.Run(wrist, data, cfg, gpu, dir, tag);      // the room opened: its cases, a tap opens it (not the Room panel)
            DockViewTools.Run(wrist, data, cfg, painter, gpu, dir, tag);      // the dock opened, on made-up data (not the Hexpad panel)
            if (tag == "") MixerViewTools.MasterChecks();                     // the Windows volume in VR, on a made-up device
            MixerViewTools.Run(wrist, data, cfg, gpu, dir, tag);              // the volume mixer, on made-up apps
            NotesViewTools.Run(wrist, data, cfg, gpu, dir, tag);              // the notifications opened, on made-up ones
            if (tag == "") PhotoViewTools.WatcherChecks(wrist, data, cfg, gpu, dir);     // the photo watcher on a folder of made-up photos
            PhotoViewTools.Run(wrist, data, cfg, gpu, dir, tag);              // the latest photo opened, on made-up photos
            if (tag == "") WeatherViewTools.DataChecks(wrist, data, cfg, dir);     // the weather's data: parser, cache, stale, no place, Hexpad's
            WeatherViewTools.Run(wrist, data, cfg, gpu, dir, tag);            // the weather widget and opened, on a made-up forecast
            FolderViewTools.Run(wrist, data, cfg, gpu, dir, tag);             // the folder: opened, its widgets live, one's view and back
            if (tag == "") SocialViewTools.DataChecks(wrist, data, cfg, dir);     // a made-up VRCX database read, the order, the recap's numbers and when
            SocialViewTools.Run(wrist, data, cfg, gpu, dir, tag);             // the friends and the recap, widgets and opened, on made-up data
            if (tag == "") LightsViewTools.DataChecks(wrist, data, dir);     // the Lumos / Prisma links on a made-up Lumos and a pipe of the tool's own
            LightsViewTools.Run(wrist, data, cfg, gpu, dir, tag);             // the light and RGB widgets and opened, on made-up states
            if (tag == "") OnThisDayTempsTools.DataChecks(dir);               // on this day's picking, the temperatures' parsing, the hot alert
            OnThisDayTempsTools.Run(wrist, data, cfg, gpu, dir, tag);         // both widgets and opened, on made-up photos and readings
            // gallery 0's media widget (cells 0-2, row 2): a tap on its card, away from its button
            wcfg.Widgets = Gallery(0);
            wrist.Settle();
            var at = new System.Numerics.Vector2(Theme.Classic ? 150 : 150, Theme.Classic ? 270 : 270);
            void TapWrist(System.Numerics.Vector2 p)
            {
                wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
                wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
                wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
            }
            TapWrist(at);
            bool opened = wrist.IsOpen;
            TapWrist(new System.Numerics.Vector2(3, 3));       // beside the card (modern) / the 95 desktop
            Log.Info($"opened widget{tag}: a tap opened it {opened} (want True), a tap beside it closed it {!wrist.IsOpen} (want {(!Theme.Classic).ToString()})");
            wrist.CloseForTest();
            wcfg.Face = face0;
            wcfg.Widgets = null;
        }
        // a tap on a time-family widget of any size (the modern-only ones too) opens its view: the clock, or the calendar
        void TapFamily(string tag)
        {
            var miss = new List<string>();
            int n = 0;
            foreach (var kind in new[] { "time", "date", "calendar", "session", "analog", "notifications" })
                foreach (var (w, h) in WatchWidgets.Find(kind).Union)
                {
                    n++;
                    wcfg.Face = "custom";
                    wcfg.Widgets = new List<WatchWidget> { new() { Type = kind, X = 0, Y = 0, W = w, H = h } };
                    wrist.Settle();
                    wrist.Draw(data);
                    var c = WatchWidgets.CellRect(0, 0, w, h, Theme.Classic);
                    float s = Theme.Classic ? 2 : 1;          // the 95 canvas is shown 2x
                    var p = new System.Numerics.Vector2((c.X + c.Width / 2) * s, (c.Y + c.Height / 2) * s);
                    wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
                    wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
                    wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
                    if (!wrist.IsOpen) miss.Add($"{kind} {w}x{h}");
                    wrist.CloseForTest();
                    wrist.PointerLeave(Hand.Left);
                }
            Log.Info($"opened widget{tag}: a tap on every time, date, calendar, time-in-VR, analog clock and notifications size opened it {n - miss.Count} of {n} (want {n} of {n})" +
                     (miss.Count > 0 ? ", not: " + string.Join(", ", miss) : ""));
            wcfg.Face = face0;
            wcfg.Widgets = null;
        }
        SaveOpened("");
        LayoutCheck("modern");
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        StylesRun(wrist, data, painter, cfg, gpu, dir);
        System.Globalization.CultureInfo.CurrentCulture = culture0;
        TapFamily("");
        // the 1x1 analog clock (Erik's picture: the macOS widget clock) close up, drawn at 2.5x (its card at 25,25 - 262,270) and as
        // the watch has it: qa/analog_1x1_<moment / look>[_1x].png
        foreach (var (tag, at, look, col) in new (string, TimeSpan, string, uint?)[]
        {
            ("0644", new(6, 44, 47), null, null), ("1009", new(10, 9, 30), null, null), ("0645", new(6, 45, 12), null, null), ("0220", new(2, 20, 50), null, null),
            ("blue", new(10, 9, 30), null, 0xFF3B82F6), ("solid_teal", new(10, 9, 30), "solid", 0xFF14B8A6), ("glass", new(10, 9, 30), "glass", null),
            ("header_pink", new(10, 9, 30), "header", 0xFFFF5D93), ("solid_white", new(10, 9, 30), "solid", 0xFFE5E7EB),
        })
        {
            wcfg.Face = "custom";
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "analog", X = 0, Y = 0, W = 1, H = 1, Style = look, Color = col } };
            wrist.TestNow = new DateTime(2026, 10, 15) + at;
            foreach (float sc in new[] { 2.5f, 1f })
            {
                wrist.TestScale = sc;
                wrist.Settle();
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, "qa", $"analog_1x1_{tag}{(sc == 1 ? "_1x" : "")}.png"));
            }
        }
        wrist.TestScale = 1;
        wrist.TestNow = null;
        MinesTools.Run(wrist, data, cfg, gpu, dir, "");       // Minesweeper: its rules, the laser's ways, its pictures
        wcfg.Face = face0;
        wcfg.Widgets = null;
        string Dump() => string.Join(" ", wcfg.Widgets.Select(w => $"{w.Type}@{w.X},{w.Y}:{w.W}x{w.H}"));
        // Wrist tab with the custom face picked (seeded from "full"), then the editor
        view.Tab = SettingsView.TabWrist;
        void FrameM()
        {
            wrist.Draw(data);
            ui.Begin(ptr, 1.5f);
            painter.Begin(surf.Bitmap, Theme.Bg, 1.5f);
            view.Draw(ui);
            painter.End();
            ui.End();
        }
        void ClickM(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); FrameM(); ptr.Press(); FrameM(); ptr.Release(); FrameM(); }
        ui.ResetAnimations();
        wcfg.Face = "full";
        ClickM(612 + 10 + (376 - 20) * 4.5f / 5, 92 + 372f * WristPanel.TH / WristPanel.TW + 38 + 18);     // the "Custom" segment
        Log.Info($"custom face: picked '{wcfg.Face}', seeded {wcfg.Widgets?.Count} widgets: {Dump()}");
        surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_custom.png"));
        view.EditingWatch = true;
        ptr.Leave();
        FrameM();
        // remove the fps widget, move gpu into its place, stretch gpu to 2 x 1 by its corner
        static System.Numerics.Vector2 CellM(int cx, int cy)
        {
            var pv = SettingsView.EditorPrev;
            var c = WatchWidgets.CellRect(cx, cy, 1, 1, false);
            return new(pv.X + (c.X + c.Width / 2) * pv.Width / WristPanel.TW, pv.Y + (c.Y + c.Height / 2) * pv.Height / WristPanel.TH);
        }
        var c42 = CellM(4, 2);
        ClickM(c42.X, c42.Y);
        ClickM(724 + 2 * (244 / 3f + 8) + 40, 138 + 17);     // Remove (the third button under the name)
        var gpuW = wcfg.Widgets.FirstOrDefault(w => w.Type == "gpu");
        var c52 = CellM(5, 2);
        ptr.Move(c52.X * 1.5f, c52.Y * 1.5f); FrameM(); ptr.Press(); FrameM();
        ptr.Move(c42.X * 1.5f, c42.Y * 1.5f); FrameM();
        surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_editor_drag.png"));
        ptr.Release(); FrameM();
        Log.Info($"editor: fps removed {wcfg.Widgets.All(w => w.Type != "fps")}, gpu moved to {gpuW?.X},{gpuW?.Y} (want 4,2)");
        var hc = WatchWidgets.CellRect(4, 2, 1, 1, false);
        var hnd = new System.Numerics.Vector2(SettingsView.EditorPrev.X + hc.Right * SettingsView.EditorPrev.Width / WristPanel.TW - 2,
            SettingsView.EditorPrev.Y + hc.Bottom * SettingsView.EditorPrev.Height / WristPanel.TH - 2);
        ptr.Move(hnd.X * 1.5f, hnd.Y * 1.5f); FrameM(); ptr.Press(); FrameM();
        ptr.Move(c52.X * 1.5f, c52.Y * 1.5f); FrameM(); ptr.Release(); FrameM();
        Log.Info($"editor: gpu size {gpuW?.W}x{gpuW?.H} (want 2x1); layout now {Dump()}");
        // no room: every cell is taken, so adding must say so and change nothing
        int before = wcfg.Widgets.Count;
        ClickM(222 + 58, SettingsView.PaletteY + 14);     // the palette's first widget (any: the grid is full)
        Log.Info($"editor: add with a full grid kept {wcfg.Widgets.Count == before} ({wcfg.Widgets.Count} widgets)");
        ptr.Leave();
        FrameM();
        surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_editor.png"));
        // the selected widget's look (gpu): Solid, Teal; then Two-tone with Purple second, on every widget
        static System.Numerics.Vector2 Chip(int i) => new(724 + i % 4 * 66.5f + 29.75f, 206 + i / 4 * 52 + 23);
        static System.Numerics.Vector2 Swatch(float top, int i) => new(724 + 17 + i % 7 * 37.6f, top + 17 + i / 7 * 38);
        int Pal(string n) => 1 + Array.FindIndex(Bento.Palette, q => q.name == n);
        ClickM(Chip(2).X, Chip(2).Y);
        var tealSw = Swatch(338, Pal("Teal"));
        ClickM(tealSw.X, tealSw.Y);
        Log.Info($"editor: gpu look {gpuW?.Style} {gpuW?.Color:X8} (want solid FF14B8A6)");
        ClickM(Chip(3).X, Chip(3).Y);
        var purpleSw = Swatch(442, Pal("Purple"));
        ClickM(purpleSw.X, purpleSw.Y);
        ClickM(724 + 130, 522 + 17);              // Use on all widgets
        bool allTwo = wcfg.Widgets.All(x => x.Style == "twotone" && x.Color == 0xFF14B8A6 && x.Color2 == 0xFFA855F7);
        Log.Info($"editor: every widget two-tone teal / purple {allTwo} (want True), {wcfg.Widgets.Count} widgets");
        ptr.Leave();
        wrist.Settle();
        FrameM();
        ui.ResetAnimations();                     // settled: a swatch's ring fades in (half-way, the picked one looks unpicked)
        FrameM();
        surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_editor_style.png"));
        ClickM(Chip(0).X, Chip(0).Y);             // back to the default card: only gpu
        Log.Info($"editor: gpu back to the default card {gpuW?.Style == null} (want True), its colour kept {gpuW?.Color:X8} (want FF14B8A6)");
        // a clock's designs: gallery 0's time (2,0 2x1) - Flip, then Standard again (stored as none); a two-tone clock (its look still
        // ends above Done); its analog clock (0,0 2x2) - Station
        wcfg.Widgets = Gallery(0);
        wrist.Settle();
        ptr.Leave(); FrameM();
        void ClickCell(int cx, int cy, int cw, int ch)
        {
            var c = WatchWidgets.CellRect(cx, cy, cw, ch, false);
            ClickM(222 + (c.X + c.Width / 2) * 0.75f, 84 + (c.Y + c.Height / 2) * 0.75f);
        }
        void ClickChip(WatchWidget w, string id)
        {
            var offered = WatchWidgets.VariantsOf(w.Type).Where(v => v.Fits(w.W, w.H)).ToList();
            var c = SettingsView.ClockChip(offered.FindIndex(v => v.Id == id), offered.Count);
            ClickM(c.X + c.Width / 2, c.Y + c.Height / 2);
        }
        void Settled() { ptr.Leave(); wrist.Settle(); FrameM(); ui.ResetAnimations(); FrameM(); }
        var timeW = wcfg.Widgets.First(x => x.Type == "time");
        var analogW = wcfg.Widgets.First(x => x.Type == "analog");
        ClickCell(2, 0, 2, 1);
        ClickChip(timeW, "flip");
        Settled();
        surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_editor_clock.png"));
        Log.Info($"editor: the time's design {timeW.Variant} (want flip)");
        ClickChip(timeW, "standard");
        Log.Info($"editor: Standard again is stored as none {timeW.Variant == null} (want True)");
        timeW.Style = "twotone";
        timeW.Variant = "segment";
        Settled();
        surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_editor_clock_twotone.png"));
        Log.Info($"editor: a two-tone clock's look ends at {view.LookBottom:0} (want under 574)");
        ClickCell(0, 0, 2, 2);
        ClickChip(analogW, "station");
        Settled();
        surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_editor_analog.png"));
        Log.Info($"editor: the analog clock's design {analogW.Variant} (want station)");
        view.EditingWatch = false;
        wcfg.Face = face0;
        wcfg.Widgets = null;
        view.Tab = 0;

        // the retro styles: every screen once more in each, Windows 95 first (its pictures keep their *_95 names); then Cyber
        foreach (var sk in Skins.All) RetroShots(sk);
        CyberShots();
        BackdropsRun(cfg, painter, gpu, dir);              // theater's backdrop in every style, backdrops_preview.png (ToolsBackdrops.cs)
        PhotoViewTools.PreviewSheet(dir);       // the latest photo in every style, side by side (photo_preview.png)
        WeatherViewTools.Sheets(dir, painter, gpu);        // weather_preview.png: every style's weather side by side
        FolderViewTools.Sheet(dir);                        // folder_preview.png: every style's folder closed and open
        FolderViewTools.EditorChecks(view, ui, surf, ptr, painter, gpu, cfg, wrist, data, dir);     // into a folder and out of it in the editor
        SocialViewTools.Sheets(dir);                       // social_preview.png: every style's friends and recap side by side
        OnThisDayTempsTools.PreviewSheet(dir);             // onthisday_temps_preview.png: both widgets in every style
        WeatherViewTools.SettingsChecks(view, ui, surf, ptr, painter, gpu, cfg, wrist, data, dir);     // its place and units in the settings
        ExtrasRun(wrist, kb, view, panelView, ui, surf, ptr, painter, gpu, cfg, data, host, dir);     // 1.14: alerts, B / Y twice, per game, share (ToolsExtras.cs)
        OverlapRun(wrist, kb, view, ui, surf, ptr, painter, cfg, data, gpu, dir);       // controls crossing each other, every tab in every style (ToolsOverlap.cs)
        FontsRun(wrist, kb, view, ui, surf, ptr, painter, gpu, cfg, data, host, dir, Gallery);
        PagesRun(wrist, data, cfg, painter, gpu, dir, view, ui, surf);
        Log.Info("snapshot written to " + dir);
        return 0;

        void RetroShots(RetroSkin sk)
        {
            // a retro style: every screen once more, its pictures named *_<tag> (*_95, *_xp, *_aero, *_mac, *_term)
            string tag = "_" + sk.Tag;
            float rowH = sk.DropRowH;
            cfg.Data.Appearance = new AppearanceConfig { Style = sk.Id, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
            for (int t = 0; t < SettingsView.TabCount; t++)
            {
                view.Tab = t;
                ui.ResetAnimations();
                if (view.ShowsWristPreview) wrist.Draw(data);
                if (view.ShowsKeyboardPreview) kb.Draw();
                view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
                surf.SavePng(gpu, Path.Combine(dir, $"settings{t}{tag}.png"));
            }
            WhatsNewShots(tag);
            // a drop-down list opened by a click (General: Smoothing), then the panel version
            view.Tab = SettingsView.TabGeneral;
            var combo = new System.Numerics.Vector2(170 * 2, 114 * 2);
            ptr.Move(combo.X, combo.Y);
            view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
            ptr.Press();
            view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
            ptr.Release();
            view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
            ptr.Move(combo.X, combo.Y + rowH * 2 * 3);
            view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
            surf.SavePng(gpu, Path.Combine(dir, $"settings_combo{tag}.png"));
            void Frame95() => view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
            void Click95(float x, float y) { ptr.Move(x * 2, y * 2); Frame95(); ptr.Press(); Frame95(); ptr.Release(); Frame95(); Frame95(); }
            // pick "High" (item 3) in the open list
            int smooth0 = cfg.Data.General.LaserSmoothing;
            Click95(170, 126 + 3 * rowH + 6);
            Log.Info($"{sk.Tag} drop-down: picked smoothing {cfg.Data.General.LaserSmoothing} (want 3, was {smooth0})");
            // a click outside an open list only closes it: the check box under the pointer must not change
            bool hap0 = cfg.Data.General.Haptics;
            Click95(170, 114);                  // open again
            Click95(34, 140);                   // "Haptic ticks", outside the list
            bool hap1 = cfg.Data.General.Haptics;
            Click95(34, 140);                   // now it is a real click
            Log.Info($"{sk.Tag} drop-down: outside click kept haptics {hap0}->{hap1} (want same), next click {cfg.Data.General.Haptics} (want {!hap0})");
            cfg.Data.General.LaserSmoothing = smooth0;
            cfg.Data.General.Haptics = hap0;
            {
                // Look > Motion on each style's own Look page: Off applies at once and is written, Full again writes nothing.
                // 95 / XP: the sheet's option buttons; 7: a drop-down; the Mac: a pop-up menu; the Terminal: its [VALUE] clicked round
                view.Tab = SettingsView.TabLook;
                ui.ResetAnimations();
                bool IsOff() => cfg.Data.Appearance.Motion == "off" && Motion.Level == MotionLevel.Off;
                bool IsFull() => cfg.Data.Appearance.Motion == null && Motion.Full;
                bool off, full;
                string how;
                if (sk.Tag is "aero" or "mac")
                {
                    var f = sk.Tag == "mac" ? SettingsView.MotionMac : SettingsView.MotionAero;
                    float lh = 3 * rowH + 2, ly = f.Bottom + lh <= SettingsView.W95H ? f.Bottom : f.Y - lh;    // where Combo95 opens it
                    void Pick(int i) { Click95(f.X + f.Width / 2, f.Y + f.Height / 2); Click95(f.X + f.Width / 2, ly + 1 + i * rowH + rowH / 2); }
                    Pick(2);
                    off = IsOff();
                    Pick(0);
                    full = IsFull();
                    how = sk.Tag == "mac" ? "the pop-up menu" : "the drop-down";
                }
                else if (sk.Tag == "term")
                {
                    var v = SettingsView.MotionTerm;
                    Click95(v.X + v.Width / 2, v.Y + v.Height / 2);
                    bool reduced = cfg.Data.Appearance.Motion == "reduced" && Motion.Level == MotionLevel.Reduced;
                    Click95(v.X + v.Width / 2, v.Y + v.Height / 2);
                    off = reduced && IsOff();
                    Click95(v.X + v.Width / 2, v.Y + v.Height / 2);
                    full = IsFull();
                    how = "the [VALUE] (FULL, REDUCED, OFF)";
                }
                else
                {
                    Click95(34, 348 + 2 * 20 + 5);
                    off = IsOff();
                    Click95(34, 348 + 5);
                    full = IsFull();
                    how = "the option buttons";
                }
                Log.Info($"{sk.Tag} motion: Off picked from {how} applies at once {off} (want True), Full again writes nothing {full} (want True)");
                if (cfg.Data.Appearance.Motion != null) { cfg.Data.Appearance.Motion = null; Theme.Apply(cfg.Data.Appearance); }   // (a failed pick)
                view.Tab = SettingsView.TabGeneral;
            }
            ptr.Leave();
            ui.ResetAnimations();
            panelView.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, 0x00000000);
            surf.SavePng(gpu, Path.Combine(dir, $"settings_panel{tag}.png"));
            foreach (var face in new[] { "full", "analog", "minimal", "time" })
            {
                cfg.Data.Wrist.Face = face;
                cfg.Data.Wrist.PerfGraph = face == "analog";
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_{face}{tag}.png"));
            }
            cfg.Data.Wrist.Face = "full";
            cfg.Data.Wrist.PerfGraph = false;
            kb.Draw();
            kb.Surf.SavePng(gpu, Path.Combine(dir, $"keyboard{tag}.png"));
            using (var np = new NotificationPanel(gpu, painter, cfg))
            {
                np.AddForPreview(new Notification { Source = "VRCX", Title = "Friend online", Content = "Sam has joined The Black Cat (Friends+)" });
                np.Draw();
                np.Surface.SavePng(gpu, Path.Combine(dir, $"notifications{tag}.png"));
            }
            SaveOpened(tag);
            LayoutCheck(sk.Tag);
            // the custom face in the 95 style: the watch, the Wrist page and its editor with a selection
            wcfg.Face = "custom";
            SaveCustom(tag);
            wcfg.Face = "custom";
            wcfg.Widgets = Gallery(0);
            view.Tab = SettingsView.TabWrist;
            ui.ResetAnimations();
            wrist.Draw(data);
            view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
            surf.SavePng(gpu, Path.Combine(dir, $"settings_wrist_custom{tag}.png"));
            view.EditingWatch = true;
            void Frame95W() { wrist.Draw(data); view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal); }
            // click the battery window (cells 4-5, rows 0-1): 95 cell 4 starts at 4 + 4 * 52.67
            ptr.Move((31 + 4 + 4 * 52.67f + 40) * 2, (82 + 4 + 30) * 2);
            Frame95W(); ptr.Press(); Frame95W(); ptr.Release(); Frame95W();
            ptr.Leave(); Frame95W();
            surf.SavePng(gpu, Path.Combine(dir, $"settings_wrist_editor{tag}.png"));
            // a face made in the modern style: its 1x1 time selected in the 95 editor (the size box has no 95 size to show: empty),
            // then 2 x 1 picked from the box
            wcfg.Widgets = Gallery(0);
            wcfg.Widgets.RemoveAll(x => x.Type is "fps" or "reproj");
            var t11 = new WatchWidget { Type = "time", X = 0, Y = 3, W = 1, H = 1 };
            wcfg.Widgets.Add(t11);
            ptr.Move((31 + 4 + 24) * 2, (82 + 4 + 3 * 54 + 25) * 2);
            Frame95W(); ptr.Press(); Frame95W(); ptr.Release(); Frame95W();
            ptr.Leave(); Frame95W();
            surf.SavePng(gpu, Path.Combine(dir, $"settings_wrist_editor{tag}_extra.png"));
            string was = $"{t11.W}x{t11.H}";
            void Click95W(float x, float y) { ptr.Move(x * 2, y * 2); Frame95W(); ptr.Press(); Frame95W(); ptr.Release(); Frame95W(); Frame95W(); }
            Click95W(518, 332);                       // the size box: its list opens
            Click95W(518, 344 + MathF.Floor(rowH / 2));   // "2 x 1" (the first)
            ptr.Leave(); Frame95W();
            Log.Info($"{sk.Tag} editor: a 1x1 time (modern-only) selected, still {was} (want 1x1); picked 2 x 1 in the size box: {t11.W}x{t11.H} (want 2x1)");
            view.EditingWatch = false;
            TheaterShots(tag);
            PanelShots(tag);
            TapFamily(tag);
            MinesTools.Run(wrist, data, cfg, gpu, dir, tag);
            PageShots(tag);
            wcfg.Face = face0;
            wcfg.Widgets = null;
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }

        // Cyber (a modern style with its own token layer and watch designs): every modern screen once more, its pictures named
        // *_cyber; the modern layout checks on its faces ('layout cyber'), the styles checks too ('layout styles cyber' - the widgets'
        // own looks do not apply in Cyber, so one pass of them)
        void CyberShots()
        {
            const string tag = "_cyber";
            cfg.Data.Appearance = new AppearanceConfig { Style = CyberHud.Id, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
            void FrameC() { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
            for (int t = 0; t < SettingsView.TabCount; t++)
            {
                view.Tab = t;
                ui.ResetAnimations();
                if (view.ShowsWristPreview) wrist.Draw(data);
                if (view.ShowsKeyboardPreview) kb.Draw();
                FrameC();
                surf.SavePng(gpu, Path.Combine(dir, $"settings{t}{tag}.png"));
            }
            {
                // Look > Motion in Cyber (the Look tab's segments): Off - the shared springs and eased values are at their targets in
                // one frame and the HUD does not boot; Full again - a spring moves, the HUD boots (its effects live for this check)
                view.Tab = SettingsView.TabLook;
                ui.ResetAnimations();
                void ClickC(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); FrameC(); ptr.Press(); FrameC(); ptr.Release(); FrameC(); }
                static float SegC(int i) => 612 + 356 / 3f * (i + 0.5f);
                string faceC = wcfg.Face;
                ClickC(SegC(2), 607);
                bool off = cfg.Data.Appearance.Motion == "off" && Motion.Level == MotionLevel.Off;
                var mu = new Ui(painter);
                mu.Spring("s", 0); mu.Anim("a", 0);
                bool snap = mu.Spring("s", 100) == 100 && mu.Anim("a", 1) == 1;
                CyberFx.Live = true;
                wcfg.Face = "custom";
                wrist.Settle(); wrist.Intro(); wrist.Draw(data);
                bool bootOff = wrist.CyberMoving;
                ClickC(SegC(0), 607);
                bool full = cfg.Data.Appearance.Motion == null && Motion.Full;
                var fu = new Ui(painter);
                fu.Spring("s", 0);
                bool moves = fu.Spring("s", 100) < 100;
                wrist.Settle(); wrist.Intro(); wrist.Draw(data);
                bool bootFull = wrist.CyberMoving;
                wrist.Settle();
                CyberFx.Live = false;
                wcfg.Face = faceC;
                ptr.Leave();
                ui.ResetAnimations();
                Log.Info($"cyber motion: Off picked in the Look tab {off} (want True) - the shared springs and eased values at their targets in one frame " +
                         $"{snap} (want True), the HUD does not boot {!bootOff} (want True)");
                Log.Info($"cyber motion: Full again {full} (want True) - a spring moves {moves} (want True), the HUD boots {bootFull} (want True)");
            }
            WhatsNewShots(tag, true);
            ui.ResetAnimations();
            for (int frame = 0; frame < 2; frame++)
            {
                ui.Begin(ptr, 1.5f);
                painter.Begin(surf.Bitmap, 0x00000000, 1.5f);
                var all = new System.Drawing.RectangleF(0, 0, SettingsView.W, SettingsView.H);
                painter.PushRoundedClip(all, 22);
                panelView.Draw(ui);
                painter.PopRoundedClip();
                painter.Stroke(System.Drawing.RectangleF.Inflate(all, -1, -1), CyberHud.Frame, 2, 21);
                painter.End();
                ui.End();
            }
            surf.SavePng(gpu, Path.Combine(dir, $"settings_panel{tag}.png"));
            foreach (var face in new[] { "full", "analog", "minimal", "time" })
            {
                cfg.Data.Wrist.Face = face;
                cfg.Data.Wrist.PerfGraph = face == "analog";
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_{face}{tag}.png"));
            }
            CyberPresets(tag);
            cfg.Data.Wrist.Face = "full";
            cfg.Data.Wrist.PerfGraph = false;
            kb.Draw();
            kb.Surf.SavePng(gpu, Path.Combine(dir, $"keyboard{tag}.png"));
            kb.PreviewChat("Szia! Mindjárt jövök, csak befejezem ezt :)");
            kb.Draw();
            kb.Surf.SavePng(gpu, Path.Combine(dir, $"keyboard_chat{tag}.png"));
            kb.PreviewChat("");
            typeof(KeyboardPanel).GetField("_chat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(kb, false);
            using (var np = new NotificationPanel(gpu, painter, cfg))
            {
                np.AddForPreview(new Notification { Source = "VRCX", Title = "Friend online", Content = "Sam has joined The Black Cat (Friends+)" });
                np.AddForPreview(new Notification { Source = "Discord", Title = "New message", Content = "Alex: are you in VR? The new watch looks great." });
                np.Draw();
                np.Surface.SavePng(gpu, Path.Combine(dir, $"notifications{tag}.png"));
            }
            wcfg.Face = "custom";
            wrist.Stats.Sample(Clock.Now);
            SaveCustom(tag);
            SaveOpened(tag);
            CyberDetailTools.Run(wrist, data, cfg, gpu, dir);       // its own opened views (the clock, the notifications' scan)
            if (Environment.GetEnvironmentVariable("ORBIT_SNAP_CHECKS") != "0")
            {
                LayoutCheck("cyber");
                StylesRun(wrist, data, painter, cfg, gpu, dir, " cyber", defaultOnly: true);
            }
            TapFamily(tag);
            // the Wrist tab and its editor with the custom face
            wcfg.Face = "custom";
            wcfg.Widgets = Gallery(0);
            view.Tab = SettingsView.TabWrist;
            ui.ResetAnimations();
            wrist.Settle();
            wrist.Draw(data);
            FrameC();
            surf.SavePng(gpu, Path.Combine(dir, $"settings_wrist_custom{tag}.png"));
            view.EditingWatch = true;
            wrist.Draw(data);
            FrameC();
            ui.ResetAnimations();
            wrist.Draw(data);
            FrameC();
            surf.SavePng(gpu, Path.Combine(dir, $"settings_wrist_editor{tag}.png"));
            view.EditingWatch = false;
            view.Tab = 0;
            TheaterShots(tag);
            PanelShots(tag);
            CyberUiRun(view, panelView, ui, surf, ptr, painter, gpu, cfg, host, wrist, data, kb, dir, Gallery);     // its own UI (ToolsCyberUi.cs)
            MinesTools.Run(wrist, data, cfg, gpu, dir, tag);
            PageShots(tag);
            wcfg.Face = face0;
            wcfg.Widgets = null;
            System.Globalization.CultureInfo.CurrentCulture = culture;
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = false };
            Theme.Apply(cfg.Data.Appearance);
        }

        // The presets' own Cyber designs (WristCyberFaces.cs) in their other forms: the numbers / the graph, without batteries, media or
        // readings, SteamVR off with nothing playing (wrist_<face>_<form>_cyber.png). Then the old faces' buttons - a click in the middle
        // of each of the six does what it did - and the motion: a preset boots with Motion on Full, not on Off; at rest it asks for no
        // frame but its next second.
        void CyberPresets(string tag)
        {
            var w = cfg.Data.Wrist;
            var odd = new List<string>();               // a text cut short or under 14 px (not a value's smaller "%", nor an icon)
            int shots = 0;
            void Shot(string face, string name)
            {
                w.Face = face;
                wrist.Settle();
                wrist.Draw(data);
                painter.TextSpy = (s, _, _, size, _, _) => { if (s.Contains('…') || (size < 14 && s != "%" && !(s.Length == 1 && char.GetUnicodeCategory(s[0]) == UnicodeCategory.PrivateUse))) odd.Add($"{name}: '{s}' {size:0.#} px"); };
                wrist.Draw(data);
                painter.TextSpy = null;
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_{name}{tag}.png"));
                shots++;
            }
            w.PerfGraph = true; Shot("full", "full_graph");
            w.PerfGraph = false; Shot("analog", "analog_numbers");
            w.ShowBatteries = false; Shot("full", "full_nobat"); Shot("analog", "analog_nobat");
            w.ShowMedia = false; Shot("full", "full_perfonly");
            w.ShowBatteries = true; w.ShowPerformance = false; Shot("full", "full_batonly");
            w.ShowBatteries = false; Shot("analog", "analog_bare");
            w.ShowMedia = true; Shot("full", "full_mediaonly");
            w.ShowPerformance = w.ShowBatteries = true;
            w.PerfGraph = true; w.ShowBatteries = w.ShowMedia = false; Shot("full", "full_graphonly");
            w.PerfGraph = false; w.ShowBatteries = w.ShowMedia = true;
            // a 12-hour clock (AM / PM), Hungarian dates
            cfg.Data.Appearance.Clock24h = false;
            Shot("full", "full_12h"); Shot("analog", "analog_12h"); Shot("time", "time_12h");
            cfg.Data.Appearance.Clock24h = true;
            var cul0 = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("hu-HU");
            Shot("analog", "analog_hu"); Shot("minimal", "minimal_hu");
            CultureInfo.CurrentCulture = cul0;
            var vr0 = (data.Vr.AppFps, data.Vr.ReprojPct, data.Vr.GpuMs, data.VrConnected, data.FakeMedia);
            var bats0 = new List<(string, float, bool)>(data.Vr.Batteries);
            (data.Vr.AppFps, data.Vr.ReprojPct, data.Vr.GpuMs, data.VrConnected, data.FakeMedia) = (-1, -1, -1, false, (false, null, null, false));
            data.Vr.Batteries.Clear();
            Shot("full", "full_off"); Shot("analog", "analog_off"); Shot("minimal", "minimal_off");
            (data.Vr.AppFps, data.Vr.ReprojPct, data.Vr.GpuMs, data.VrConnected, data.FakeMedia) = vr0;
            data.Vr.Batteries.AddRange(bats0);
            Log.Info($"cyber presets: {shots} pictures, texts cut short or under 14 px: {(odd.Count == 0 ? "none" : string.Join(", ", odd))} (want none)");
            // the buttons: the middle of each old one (Buttons: 6 across from x 28, 89 wide and 10 apart, y 354-416)
            var hits = new List<string>();
            var act0 = wrist.OnAction;
            wrist.OnAction = id => hits.Add(id);
            var got = new List<string>();
            foreach (var face in new[] { "full", "analog", "minimal" })
            {
                w.Face = face;
                wrist.Settle();
                wrist.Draw(data);
                hits.Clear();
                for (int i = 0; i < 6; i++)
                {
                    var at = new Vector2(28 + i * 99 + 44.5f, 385);
                    wrist.PointerMove(Hand.Left, at); wrist.Draw(data);
                    wrist.PointerButton(Hand.Left, PtrButton.Primary, true, at); wrist.Draw(data);
                    wrist.PointerButton(Hand.Left, PtrButton.Primary, false, at); wrist.Draw(data);
                }
                wrist.PointerLeave(Hand.Left);
                got.Add($"{face}: {string.Join(" ", hits)}");
            }
            wrist.OnAction = act0;
            Log.Info($"cyber presets: a click in the middle of each old button - {string.Join("; ", got)} (want keyboard desktops recenter layout night settings each)");
            // the motion; at rest as in the headset (its effects live, no warning on the face: a warning pulses for 8 s)
            var level0 = Motion.Level;
            CyberFx.Live = true;
            var boots = new List<string>();
            bool rest = true;
            data.Vr.Batteries.Clear();
            foreach (var face in new[] { "full", "analog", "minimal", "time" })
            {
                w.Face = face;
                Motion.Level = MotionLevel.Off;
                wrist.Settle(); wrist.Intro(); wrist.Draw(data);
                bool off = wrist.CyberMoving;
                Motion.Level = MotionLevel.Full;
                wrist.Settle(); wrist.Intro(); wrist.Draw(data);
                bool full = wrist.CyberMoving;
                wrist.Settle();
                wrist.CyberLive = true;
                AnimWake.Take();
                for (int i = 0; i < 3; i++) wrist.Draw(data);
                double ask = AnimWake.Take() - Clock.Now, own = wrist.CyberNextFrame - Clock.Now;
                rest &= !wrist.Animating && ask > 0 && ask < 1.05 && own > 5;
                wrist.CyberLive = false;
                boots.Add($"{face} {full}/{off}");
            }
            data.Vr.Batteries.AddRange(bats0);
            Motion.Level = level0;
            CyberFx.Live = false;
            wrist.Settle();
            Log.Info($"cyber presets: boot on Full / on Off - {string.Join(", ", boots)} (want True/False each); at rest only the next second asked, " +
                     $"Cyber's own next frame 5 s off or more {rest} (want True)");
            w.Face = "full";
        }

        // the custom face with three pages (the last on the 4 x 3 grid): the taskbar along the bottom, each page once
        void PageShots(string tag)
        {
            var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Name, wcfg.Pages, wcfg.Page, wcfg.Background);
            var pages = new[] { WatchPages.Make("vrchat", "6x4"), WatchPages.Make("performance", "6x4"), WatchPages.Make("everyday", "4x3") };
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Widgets = pages[0].Widgets;
            wcfg.Grid = null;
            wcfg.Name = pages[0].Name;
            wcfg.Pages = pages.Skip(1).ToList();
            for (int i = 0; i < pages.Length; i++)
            {
                wcfg.Page = i;
                wrist.Settle();
                wrist.Draw(data);
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_pages{i + 1}{tag}.png"));
            }
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Name, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }

        // the floating Room and Hexpad panels, on made-up data (no VRChat log; never the real Hexpad: the panel has no link)
        void PanelShots(string tag)
        {
            using (var room = new RoomPanel(gpu, painter, cfg))
            {
                room.DrawFor(RoomViewTools.Room(12, 2), DateTime.Now);
                room.Surf.SavePng(gpu, Path.Combine(dir, $"room_panel{tag}.png"));
            }
            using var faces = new HexpadFaces();
            var fake = DockViewTools.Fake();
            faces.Update(painter, fake);
            using var dock = new DockPanel(gpu, painter, cfg, null, faces);
            dock.DrawFor(fake, Clock.Now);
            dock.Surf.SavePng(gpu, Path.Combine(dir, $"dock_panel{tag}.png"));
        }

        // the theater bar under a screen, with its settings, and in theater mode (the switcher); made-up screens, no VR
        void TheaterShots(string tag)
        {
            using var bar = new TheaterBar(gpu, painter, cfg);
            var a = new TheaterTest.FakeScreen { Name = "DELL U2723QE" };
            var b = new TheaterTest.FakeScreen { Name = "LG ULTRAGEAR" };
            var choices = new List<ITheaterScreen> { a, b, new TheaterTest.FakeScreen { Name = "YouTube - Lofi hip hop radio - beats to relax/study to - Mozilla Firefox" } };
            bar.DrawFor(choices, b, new TheaterView { Size = 2.8f }).SavePng(gpu, Path.Combine(dir, $"theater_bar{tag}.png"));
            bar.DrawFor(new List<ITheaterScreen>(), null).SavePng(gpu, Path.Combine(dir, $"theater_bar_screen{tag}.png"));
        }
    }

    // ---- Look > Font: the modern style in every font offered (the 95 one keeps its own pixel font). First the list itself: opened by
    // a click, a pick applies at once and is saved, a click beside it only closes it, the default again leaves no trace in the config.
    // Then per font, in fonts/<font>: the settings pages, the watch faces and opened views, the keyboard, the notifications and What's
    // new, the ink of every text against its box (sticking out a pixel or more where the default font does not is a problem); its
    // digits one width (tabular: a ticking clock must not jiggle); and the layout checks again, modern and styles (their qa pictures
    // kept only when one fails). About 5 1/2 minutes with 10 fonts; ORBIT_SNAP_FONTS=0 leaves all of it out of the full run, ORBIT_SNAP_ONLY=fonts runs
    // it alone. The problems found (0: none).
    static int FontsRun(WristPanel wrist, KeyboardPanel kb, SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter,
        Gpu gpu, ConfigStore cfg, PanelData data, FakeHost host, string dir, Func<int, List<WatchWidget>> gallery, bool asked = false)
    {
        if (!asked && Environment.GetEnvironmentVariable("ORBIT_SNAP_FONTS") == "0") return 0;
        var wcfg = cfg.Data.Wrist;
        string face0 = wcfg.Face;
        var keep = cfg.Data.Appearance;
        var culture0 = CultureInfo.CurrentCulture;
        var en = SnapCulture;
        void Look(string font)
        {
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = false, Font = font };
            Theme.Apply(cfg.Data.Appearance);
        }
        void Frame()
        {
            ui.Begin(ptr, 1.5f);
            painter.Begin(surf.Bitmap, Theme.Bg, 1.5f);
            view.Draw(ui);
            painter.End();
            ui.End();
        }
        void Click(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); Frame(); ptr.Press(); Frame(); ptr.Release(); Frame(); }

        // the list (Look tab, its field under Style): rows of 40 above the field, the default first
        CultureInfo.CurrentCulture = en;
        Look(null);
        view.Tab = SettingsView.TabLook;
        ui.ResetAnimations();
        int n = Theme.FontsInstalled.Length + 1;
        float top = 562 - n * 40 - 12;
        float Row(int i) => top + 6 + i * 40 + 20;
        Click(332, 607);
        ptr.Move(332 * 1.5f, Row(2) * 1.5f);
        Frame(); Frame();
        surf.SavePng(gpu, Path.Combine(dir, "settings_look_fonts.png"));
        string first = n > 1 ? Theme.FontsInstalled[0] : null;
        Click(332, Row(1));
        bool picked = cfg.Data.Appearance.Font == first && Theme.UserFont == first && Theme.Font == first;
        bool saved = cfg.Serialized().Contains($"\"Font\": \"{first}\"");
        ptr.Leave(); Frame();
        surf.SavePng(gpu, Path.Combine(dir, "settings_look_font_picked.png"));
        Log.Info($"fonts: the list offers {n} ({Theme.DefaultFontName}, {string.Join(", ", Theme.FontsInstalled)}); a pick applies {picked} " +
                 $"(want True), is saved {saved} (want True)");
        uint accent0 = cfg.Data.Appearance.Accent;
        Click(332, 607);                     // open
        Click(612 + 124 + 58, 129);          // the "Ocean" theme card, beside the open list
        bool kept = cfg.Data.Appearance.Accent == accent0;
        Click(332, 607);                     // open
        Click(332, Row(0));                  // the default
        Log.Info($"fonts: a click beside the open list changed nothing {kept} (want True); the default again {cfg.Data.Appearance.Font == null && Theme.UserFont == null} " +
                 $"(want True), nothing about it in the config {!cfg.Serialized().Contains("\"Font\"")} (want True)");
        ptr.Leave();

        var fonts = new List<string> { null };
        fonts.AddRange(Theme.FontsInstalled);
        var baseline = new Dictionary<string, HashSet<string>>();
        var spied = new List<(string s, System.Drawing.RectangleF r, float size, Vortice.DirectWrite.FontWeight w, string fam, bool wrap,
            Vortice.DirectWrite.TextAlignment h, Vortice.DirectWrite.ParagraphAlignment v, float tracking)>();
        int total = 0;
        var notes = new List<string>();
        foreach (var font in fonts)
        {
            string name = font ?? "default";
            string fdir = Path.Combine(dir, "fonts", name.Replace(' ', '_'));
            Directory.CreateDirectory(fdir);
            CultureInfo.CurrentCulture = en;
            Look(font);
            var misfit = new Dictionary<string, Dictionary<string, string>>();       // page -> text key -> what is wrong
            void Spy(string page, Action draw)
            {
                spied.Clear();
                painter.FitSpy = (s, r, size, w, fam, wrap, h, v, tr) => spied.Add((s, r, size, w, fam, wrap, h, v, tr));
                draw();
                painter.FitSpy = null;
                if (!misfit.TryGetValue(page, out var bad)) misfit[page] = bad = new Dictionary<string, string>();
                foreach (var t in spied)
                {
                    // ink outside its box: cut off (or over its neighbour); the key leaves out the live digits (clock, CPU) and the size
                    if (t.fam == Theme.IconFont || string.IsNullOrWhiteSpace(t.s)) continue;
                    float o = painter.Overhang(t.s, t.r, t.size, t.w, t.fam, t.wrap, t.h, t.v, t.tracking);
                    if (o >= 1)          // under a pixel: only the anti-aliased fringe
                        bad[$"{new string(t.s.Select(ch => char.IsDigit(ch) ? '0' : ch).ToArray())}|{t.r.Width:0}x{t.r.Height:0}"] =
                            $"'{t.s}' {t.size:0.#} px in {t.fam} sticks out {o:0.#} px of its {t.r.Width:0.#} x {t.r.Height:0.#} box";
                }
            }
            // the settings pages, What's new over them and as its own panel
            for (int t = 0; t < SettingsView.TabCount; t++)
            {
                view.Tab = t;
                ui.ResetAnimations();
                if (view.ShowsWristPreview) wrist.Draw(data);
                if (view.ShowsKeyboardPreview) kb.Draw();
                Spy($"settings{t}", Frame);
                surf.SavePng(gpu, Path.Combine(fdir, $"settings{t}.png"));
            }
            host.WhatsNewList = WhatsNew.Since("1.8.2");
            host.WhatsNewSub = Lang.F("Updated to {0} from {1}", Updater.CurrentText, "1.8.2");
            view.Tab = SettingsView.TabGeneral;
            ui.ResetAnimations();
            Spy("settings_whatsnew", Frame);
            surf.SavePng(gpu, Path.Combine(fdir, "settings_whatsnew.png"));
            using (var wnp = new WhatsNewPanel(gpu, painter))
            {
                Spy("whatsnew_panel", () => wnp.DrawFor(host.WhatsNewList, host.WhatsNewSub));
                wnp.Surf.SavePng(gpu, Path.Combine(fdir, "whatsnew_panel.png"));
            }
            host.WhatsNewList = Array.Empty<WhatsNew.Entry>();
            view.Tab = 0;
            // the watch: the faces, the custom face's galleries, every opened view
            foreach (var face in new[] { "full", "analog", "minimal", "time" })
            {
                wcfg.Face = face;
                wcfg.PerfGraph = face == "analog";
                wrist.Draw(data);
                Spy($"wrist_{face}", () => wrist.Draw(data));
                wrist.Surf.SavePng(gpu, Path.Combine(fdir, $"wrist_{face}.png"));
            }
            wcfg.PerfGraph = false;
            wcfg.Face = "custom";
            for (int g = 0; g < 3; g++)
            {
                wcfg.Widgets = gallery(g);
                wrist.Settle();
                wrist.Draw(data);
                Spy($"wrist_custom{g}", () => wrist.Draw(data));
                wrist.Surf.SavePng(gpu, Path.Combine(fdir, $"wrist_custom{g}.png"));
            }
            // every kind's opened view (as the layout check opens them: one widget of its usual size; the 1.15 ones on the made-up data
            // the snapshot gives them - photos, weather, friends, recap, light, RGB, memories, temperatures, avatar), the notifications
            // with a long made-up list
            var notes0 = data.Notes;
            data.Notes = NotesViewTools.Fake(NotesViewTools.Long);
            foreach (var kind in WatchWidgets.Kinds.Where(k => !k.Button))
            {
                var (w, h) = kind.Sizes[kind.Default];
                wcfg.Widgets = new List<WatchWidget> { new() { Type = kind.Id, X = 0, Y = 0, W = w, H = h } };
                wrist.Settle();
                if (!wrist.OpenForTest(kind.Id)) continue;          // (the microphone and the button group have no opened view)
                wrist.Draw(data);
                Spy($"wrist_open_{kind.Id}", () => wrist.Draw(data));
                wrist.Surf.SavePng(gpu, Path.Combine(fdir, $"wrist_open_{kind.Id}.png"));
                wrist.CloseForTest();
            }
            data.Notes = notes0;
            wcfg.Face = face0;
            wcfg.Widgets = null;
            // the keyboard (and its chatbox line), the notifications
            Spy("keyboard", kb.Draw);
            kb.Surf.SavePng(gpu, Path.Combine(fdir, "keyboard.png"));
            kb.PreviewChat("Szia! Mindjárt jövök, csak befejezem ezt :)");
            Spy("keyboard_chat", kb.Draw);
            kb.Surf.SavePng(gpu, Path.Combine(fdir, "keyboard_chat.png"));
            kb.PreviewChat("");
            typeof(KeyboardPanel).GetField("_chat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(kb, false);
            kb.Draw();
            using (var np = new NotificationPanel(gpu, painter, cfg))
            {
                np.AddForPreview(new Notification { Source = "VRCX", Title = "Friend online", Content = "Sam has joined The Black Cat (Friends+)" });
                np.AddForPreview(new Notification { Source = "Discord", Title = "New message", Content = "Alex: are you in VR? The new watch looks great, the cursor is way smoother now." });
                Spy("notifications", np.Draw);
                np.Surface.SavePng(gpu, Path.Combine(fdir, "notifications.png"));
            }
            // the digits: one width each, and times of the same shape as wide as each other
            bool tabular = true;
            foreach (var (fam, size, w) in new[] { (Theme.Display, 44f, Vortice.DirectWrite.FontWeight.Bold), (Theme.Display, 44f, Vortice.DirectWrite.FontWeight.SemiBold),
                                                   (Theme.Font, 16f, Vortice.DirectWrite.FontWeight.Normal), (Theme.Font, 16f, Vortice.DirectWrite.FontWeight.SemiBold) })
            {
                var dw = Enumerable.Range(0, 10).Select(d => painter.MeasureWidth(((char)('0' + d)).ToString(), size, w, fam)).ToList();
                var tw = new[] { "00:00", "11:11", "12:58", "23:47" }.Select(x => painter.MeasureWidth(x, size, w, fam)).ToList();
                if (dw.Max() - dw.Min() > 0.01f || tw.Max() - tw.Min() > 0.01f) tabular = false;
            }
            int problems = 0, fits = 0;
            if (font == null)
            {
                foreach (var (page, m) in misfit) baseline[page] = m.Keys.ToHashSet();
                File.WriteAllLines(Path.Combine(fdir, "misfits.txt"), misfit.SelectMany(kv => kv.Value.Values.Select(v => $"{kv.Key}: {v}")));
                Log.Info($"fonts default: {misfit.Sum(kv => kv.Value.Count)} texts already sticking out of their box (the baseline; fonts/default/misfits.txt), " +
                         $"digits one width {tabular} (the default as it always was)");
                continue;
            }
            foreach (var (page, m) in misfit)
                foreach (var (key, what) in m)
                    if (!baseline.TryGetValue(page, out var b) || !b.Contains(key)) { fits++; Log.Warn($"fonts {name}: {page}: {what}"); }
            CultureInfo.CurrentCulture = culture0;
            int lp = LayoutCheckRun(wrist, data, cfg, painter, gpu, fdir, "modern " + name);
            CultureInfo.CurrentCulture = en;
            int sp = StylesRun(wrist, data, painter, cfg, gpu, fdir, " " + name);
            if (lp + sp == 0 && Directory.Exists(Path.Combine(fdir, "qa"))) Directory.Delete(Path.Combine(fdir, "qa"), true);
            wcfg.Face = face0;
            wcfg.Widgets = null;
            problems = fits + lp + sp + (tabular ? 0 : 1);
            total += problems;
            Log.Info($"fonts {name} (drawn at {painter.FontScale(font) * 100:0}%): texts sticking out of their box {fits} (want 0), digits one width {tabular} (want True), layout {lp}, styles {sp}: " +
                     $"{problems} problem(s)");
            notes.Add($"{name} {problems}");
        }
        Log.Info($"layout fonts{LangTag}: {total} problem(s) in {fonts.Count - 1} fonts ({string.Join(", ", notes)})");
        cfg.Data.Appearance = keep;
        Theme.Apply(keep);
        CultureInfo.CurrentCulture = culture0;
        wcfg.Face = face0;
        wcfg.Widgets = null;
        view.Tab = 0;
        return total;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    // A clock's design in the config: written only once set and read back; Clean() keeps it (a newer Orbit's too); a size it does
    // not fit draws that size's default; the editor stores a size's default as none
    static void VariantWants()
    {
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        string none = System.Text.Json.JsonSerializer.Serialize(new WatchWidget { Type = "time", X = 0, Y = 0, W = 3, H = 2 }, opts);
        var back = System.Text.Json.JsonSerializer.Deserialize<WatchWidget>(
            System.Text.Json.JsonSerializer.Serialize(new WatchWidget { Type = "time", W = 2, H = 1, Variant = "flip" }, opts), opts);
        Log.Info($"config: a clock without a design writes no Variant {!none.Contains("Variant")} (want True), one with a design reads back {back.Variant == "flip"} (want True)");
        var face = new List<WatchWidget>
        {
            new() { Type = "time", X = 0, Y = 0, W = 3, H = 2, Variant = "words" }, new() { Type = "analog", X = 3, Y = 0, W = 2, H = 2, Variant = "station" },
            new() { Type = "analog", X = 5, Y = 0, W = 1, H = 1, Variant = "roman" }, new() { Type = "time", X = 0, Y = 2, W = 2, H = 1, Variant = "hologram" },
        };
        string before = string.Join(" ", face.Select(w => w.Variant));
        string after = string.Join(" ", WatchWidgets.Clean(face).Select(w => w.Variant));
        Log.Info($"variants: Clean() keeps every design, an unknown one too {before == after} (want True): {after}");
        static string Of(string kind, int w, int h, string v) => WatchWidgets.VariantOf(new WatchWidget { Type = kind, W = w, H = h, Variant = v });
        Log.Info($"variants: at a size it does not fit a design draws the default: words 2x1 {Of("time", 2, 1, "words")} (want standard), roman 1x1 " +
                 $"{Of("analog", 1, 1, "roman")} (want squircle), classic 1x1 {Of("analog", 1, 1, "classic")} (want squircle), unknown 3x2 {Of("time", 3, 2, "hologram")} (want standard)");
        Log.Info($"variants: where it fits it draws: words 3x2 {Of("time", 3, 2, "words")} (want words), squircle 3x3 {Of("analog", 3, 3, "squircle")} (want squircle), " +
                 $"none 2x2 {Of("analog", 2, 2, null)} (want classic), none 1x1 {Of("analog", 1, 1, null)} (want squircle)");
        var a2 = new WatchWidget { Type = "analog", W = 2, H = 2 };
        var a1 = new WatchWidget { Type = "analog", W = 1, H = 1 };
        var t3 = new WatchWidget { Type = "time", W = 3, H = 2 };
        Log.Info($"variants: a size's default picked is stored as none: classic 2x2 {WatchWidgets.StoreVariant(a2, "classic") == null}, squircle 1x1 " +
                 $"{WatchWidgets.StoreVariant(a1, "squircle") == null}, standard 3x2 {WatchWidgets.StoreVariant(t3, "standard") == null}, squircle 2x2 " +
                 $"{WatchWidgets.StoreVariant(a2, "squircle")} (want True, True, True, squircle)");
    }

    // --clockshot DIR: only the clocks' designs (a quick --snapshot for them, in its own config): the config rules, every design at
    // every size with the layout and style checks, the sheets qa/variants_time.png / variants_analog.png
    public static int ClockShot(string dir)
    {
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(Path.Combine(dir, "qa"));
        var cfg = new ConfigStore(Path.Combine(dir, "config.json"));
        using var gpu = new Gpu(-1);
        using var painter = new Painter(gpu);
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = false };
        Theme.Apply(cfg.Data.Appearance);
        var data = new PanelData { VrConnected = true, VramTotalGB = 16, FakeGpuLoad = 63, FakeVram = 9.4f, VrSince = DateTime.Now.AddMinutes(-83) };
        data.Vr.AppFps = 86; data.Vr.ReprojPct = 4; data.Vr.GpuMs = 9.3f; data.Vr.DisplayHz = 90;
        using var wrist = new WristPanel(gpu, painter, cfg);
        VariantWants();
        var culture0 = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = SnapCulture;
        cfg.Data.Wrist.Face = "custom";
        LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, "modern", true);
        StylesRun(wrist, data, painter, cfg, gpu, dir, variantsOnly: true);
        // and in every font of Look > Font (in fonts/<font>; ORBIT_SNAP_FONTS=0 leaves them out)
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_FONTS") != "0")
        {
            int total = 0;
            var notes = new List<string>();
            foreach (var font in Theme.FontsInstalled)
            {
                cfg.Data.Appearance = new AppearanceConfig { Clock24h = false, Font = font };
                Theme.Apply(cfg.Data.Appearance);
                string fdir = Path.Combine(dir, "fonts", font.Replace(' ', '_'));
                int n = LayoutCheckRun(wrist, data, cfg, painter, gpu, fdir, "modern " + font, true) +
                        StylesRun(wrist, data, painter, cfg, gpu, fdir, " " + font, true);
                total += n;
                notes.Add($"{font} {n}");
            }
            Log.Info($"clockshot fonts: {total} problem(s) in {Theme.FontsInstalled.Length} fonts ({string.Join(", ", notes)})");
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = false };
            Theme.Apply(cfg.Data.Appearance);
        }
        CultureInfo.CurrentCulture = culture0;
        Log.Info("clockshot written to " + dir);
        return 0;
    }

    // --faceshot DIR: your own watch face, without VR - from a COPY of the config (the real one is only read), drawn in the
    // modern style with your look, then once more with the Bento theme's colours (accent #FF5D93, panel #0A0B10): before /
    // after pictures of a redesign. The numbers are the snapshot's made-up ones (plus a made-up room and Hexpad), the clock,
    // CPU, RAM and the system volume are live, dates in this PC's language. The pages are drawn in Windows 95 and Cyber too
    // (face_page*_95, face_page*_cyber), and the preset faces in Cyber (face_<face>_cyber, face_<face>_cyber_off).
    public static int FaceShot(string dir)
    {
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(dir);
        string real = Path.Combine(Log.Dir, "config.json"), copy = Path.Combine(dir, "config.json");
        if (File.Exists(real)) File.Copy(real, copy, true);
        else Log.Warn("faceshot: no config.json in " + Log.Dir + " - the default face");
        var cfg = new ConfigStore(copy);
        // read and written again unchanged, it must be the same file (no look on any widget: nothing new in it)
        if (File.Exists(copy))
            Log.Info($"faceshot: config read and written again is byte-identical {cfg.Serialized() == File.ReadAllText(copy)} (want True), " +
                     $"widgets with a look of their own: {cfg.Data.Wrist.Widgets?.Count(w => w.Style != null || w.Color != null || w.Color2 != null) ?? 0}");
        using var gpu = new Gpu(-1);
        using var painter = new Painter(gpu);
        var data = new PanelData { VrConnected = true, VramTotalGB = gpu.DedicatedVram / (1024f * 1024 * 1024), FakeGpuLoad = 63, FakeVram = 9.4f };
        data.VrSince = DateTime.Now.AddMinutes(-83);
        data.Vr.AppFps = 86; data.Vr.ReprojPct = 4; data.Vr.GpuMs = 9.3f; data.Vr.DisplayHz = 90;
        data.Vr.Batteries.Add(("HMD", 0.71f, false));
        data.Vr.Batteries.Add(("L", 0.82f, false));
        data.Vr.Batteries.Add(("R", 0.18f, true));
        data.Vr.Batteries.Add(("Trk×6", 0.44f, false));
        data.FakeMedia = (true, "Midnight City", "M83", true);
        data.Photos = PhotoFake.Snap();                 // made-up photos (PhotoViewTest.cs): a tool never reads a real one
        var frng = new Random(3);
        for (int i = 0; i < VrStats.FrameHistory; i++)
        {
            data.Vr.FrameMs[i] = 7.5f + (float)frng.NextDouble() * 2.5f + (i % 97 == 0 ? 6 : 0);
            data.Vr.FrameFlag[i] = (byte)(i % 97 == 0 ? 1 : i == 300 ? 2 : 0);
        }
        data.Vr.FrameHead = 0; data.Vr.FrameCount = VrStats.FrameHistory;
        data.NightMode = cfg.Data.Appearance.NightMode;
        data.DesktopsVisible = cfg.Data.General.DesktopsVisible;
        data.LayoutName = cfg.Data.ActiveLayout ?? "Desk";
        // a made-up VRChat room and a made-up Hexpad (a colour per key)
        var room = new RoomWatcher.Snapshot { Version = 1, LogFound = true, InRoom = true, World = "Midnight Rooftop", VrcxFound = true, Friends = 120 };
        string[] names = { "Nova", "Pixel Fox", "Moss", "Kite", "Juniper", "Static", "Orbit Fan 2000", "Lumen", "Quill", "Sable", "Wren", "Echo" };
        for (int i = 0; i < names.Length; i++)
            room.Here.Add(new RoomWatcher.Person { Id = $"usr_{i}", Name = names[i], Joined = DateTime.Now.AddMinutes(-3 * i - 1), Friend = i is 1 or 4 or 9 });
        data.Room = room;
        var dock = new HexpadLink.Snapshot { State = HexpadLink.LinkState.Ok, Api = 2, Profile = "Default", PageName = "Home", Page = 0, Pages = 3, Version = 1 };
        uint[] cols = { 0xFF2D6CDF, 0xFFE0473C, 0xFF3CB371, 0xFFE8A33D, 0xFF8E5BD8, 0xFF2BB5B8 };
        for (int i = 0; i < HexpadLink.Keys; i++)
        {
            var px = new uint[68 * 68];
            for (int y = 0; y < 68; y++)
                for (int x = 0; x < 68; x++)
                {
                    float d = MathF.Sqrt((x - 34) * (x - 34) + (y - 34) * (y - 34));
                    px[y * 68 + x] = d < 18 ? 0xFFFFFFFF : cols[i];
                }
            dock.Faces[i] = new HexpadLink.Face(px, 68, 68);
            dock.FaceVersion[i] = 1000 + i;
        }
        using var faces = new HexpadFaces();
        faces.Update(painter, dock);
        data.Dock = dock;
        data.DockFaces = faces;

        using var wrist = new WristPanel(gpu, painter, cfg);
        wrist.Mic.Fake = true;                          // never the real microphone
        wrist.Mic.Present = true;
        var a = cfg.Data.Appearance;
        var wc = cfg.Data.Wrist;
        Log.Info($"faceshot: style {a.Style}, accent {a.Accent:X8}, panel {a.PanelColor:X8} at {a.PanelOpacity:0.00}, face {wc.Face}, background {wc.Background}, " +
                 $"widgets {string.Join(" ", wc.Widgets?.Select(w => $"{w.Type}@{w.X},{w.Y}:{w.W}x{w.H}") ?? Array.Empty<string>())}");
        wrist.Stats.Sample(Clock.Now);                 // a CPU reading needs two samples
        Thread.Sleep(400);
        wrist.Stats.Sample(Clock.Now + 2);
        a.Style = "modern";
        uint accent0 = a.Accent, panel0 = a.PanelColor;
        foreach (var (tag, accent, panel) in new[] { ("real", accent0, panel0), ("bento", 0xFFFF5D93u, 0xFF0A0B10u) })
        {
            a.Accent = accent;
            a.PanelColor = panel;
            Theme.Apply(a);
            wrist.Settle();
            wrist.Draw(data);
            wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, $"face_{tag}.png"));
        }
        // the same face in each of the widgets' own looks (on the copy only): automatic colours, then a colour per widget
        if (wc.Face == "custom" && wc.Widgets is { Count: > 0 } ws)
        {
            var keep = ws.Select(w => (w.Style, w.Color, w.Color2)).ToList();
            uint[] picks = { 0xFF1E3A8A, 0xFF14B8A6, 0xFFF59E0B, 0xFFFF5D93, 0xFFA855F7, 0xFF3B82F6 };
            foreach (var look in Bento.Styles)
                foreach (bool colours in new[] { false, true })
                {
                    if (look == null && !colours) continue;
                    for (int i = 0; i < ws.Count; i++) { ws[i].Style = look; ws[i].Color = colours ? picks[i % picks.Length] : null; ws[i].Color2 = null; }
                    wrist.Settle();
                    wrist.Draw(data);
                    wrist.Draw(data);
                    wrist.Surf.SavePng(gpu, Path.Combine(dir, $"face_look_{look ?? "default"}{(colours ? "_colours" : "")}.png"));
                }
            for (int i = 0; i < ws.Count; i++) (ws[i].Style, ws[i].Color, ws[i].Color2) = keep[i];
        }
        // the same face in every font of Look > Font (your colours): face_font_<font>.png
        string font0 = a.Font;
        a.Accent = accent0;
        a.PanelColor = panel0;
        foreach (var font in new string[] { null }.Concat(Theme.FontsInstalled))
        {
            a.Font = font;
            Theme.Apply(a);
            wrist.Settle();
            wrist.Draw(data);
            wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, $"face_font_{(font ?? "default").Replace(' ', '_')}.png"));
        }
        a.Font = font0;
        Theme.Apply(a);
        // What the first start of 1.12 makes of this config (the copy: its own copy of it lands next to it, the real one is untouched):
        // the face as page 1, unchanged, then the VRChat and Performance pages - drawn in this look, with the dots, and in each retro style
        {
            var jo = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string w0 = System.Text.Json.JsonSerializer.Serialize(wc.Widgets, jo);
            int pages0 = WatchPages.Count(wc);
            bool migrated = WatchPages.Migrate(cfg);
            string w1 = System.Text.Json.JsonSerializer.Serialize(wc.Widgets, jo);
            string pnames = string.Join(", ", Enumerable.Range(0, WatchPages.Count(wc)).Select(i => $"{WatchPages.Name(wc, i)} ({WatchPages.GridId(wc, i)}, {WatchPages.Widgets(wc, i)?.Count ?? 0} widgets)"));
            Log.Info($"faceshot: the pages' first start on the copy: migrated {migrated} (want True), pages {pages0} -> {WatchPages.Count(wc)} (want 1 -> 3): {pnames}; " +
                     $"page 1 byte-identical {w0 == w1} (want True), face {wc.Face}");
            PagesData(data);
            a.Accent = accent0;
            a.PanelColor = panel0;
            // in Modern, every retro style and Cyber: face_page<n>.png, then face_page<n>_95 / _xp / _aero / _mac / _term / _cyber.png
            var looks = new List<(string style, string suffix)> { ("modern", "") };
            foreach (var skin in Skins.All) looks.Add((skin.Id, "_" + skin.Tag));
            looks.Add((CyberHud.Id, "_cyber"));
            foreach (var (style, suffix) in looks)
            {
                a.Style = style;
                Theme.Apply(a);
                for (int i = 0; i < WatchPages.Count(wc); i++)
                {
                    wc.Page = i;
                    wrist.Settle();
                    for (int f = 0; f < 8; f++) { wrist.Draw(data); Thread.Sleep(50); }      // a new cover fades in
                    wrist.Surf.SavePng(gpu, Path.Combine(dir, $"face_page{i + 1}{suffix}.png"));
                }
            }
            a.Style = "modern";
            Theme.Apply(a);
            wc.Page = 0;
        }
        // the preset faces in Cyber (WristCyberFaces.cs) with your options and clock: face_<face>_cyber.png; then SteamVR off, nothing
        // playing: face_<face>_cyber_off.png
        {
            string face0 = wc.Face;
            a.Style = CyberHud.Id;
            Theme.Apply(a);
            foreach (bool off in new[] { false, true })
            {
                if (off)
                {
                    (data.Vr.AppFps, data.Vr.ReprojPct, data.Vr.GpuMs, data.VrConnected, data.FakeMedia) = (-1, -1, -1, false, (false, null, null, false));
                    data.Vr.Batteries.Clear();
                }
                foreach (var face in new[] { "full", "analog", "minimal", "time" })
                {
                    wc.Face = face;
                    wrist.Settle();
                    wrist.Draw(data);
                    wrist.Draw(data);
                    wrist.Surf.SavePng(gpu, Path.Combine(dir, $"face_{face}_cyber{(off ? "_off" : "")}.png"));
                }
            }
            wc.Face = face0;
            a.Style = "modern";
            Theme.Apply(a);
        }
        Log.Info("faceshot written to " + dir);
        return 0;
    }

    // Every widget kind at every size, alone on a watch with no background ("cards"), and every opened view
    // without its clip: anything drawn outside the card sticks out (a non-transparent pixel there), and in the
    // modern style anything within a few pixels of the card's edge is too close to it. Logged, one line each.
    // ORBIT_SNAP_ONLY=kind:<id> --snapshot <dir>: one kind's layout check in every style (its pictures in qa/) - a new kind's first look
    static int KindOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir, string kind)
    {
        int layout = 0;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        data.Vr.CpuMs = 18.6f;                                     // (the "cputime" widget: a game its CPU holds back)
        foreach (var (style, tag) in new[] { ("modern", "modern"), (CyberHud.Id, "cyber"), ("classic", "95"), ("xp", "xp"), ("aero", "aero"), ("mac", "mac"), ("terminal", "term") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag, onlyKind: kind);
        }
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        System.Globalization.CultureInfo.CurrentCulture = culture;
        Log.Info($"snapshot (kind {kind}) written to {dir}");
        return layout > 0 ? 1 : 0;
    }

    static int LayoutCheckRun(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir, string style, bool variantsOnly = false,
        string onlyKind = null)
    {
        var wcfg = cfg.Data.Wrist;
        bool classic = Theme.Classic;
        int problems = 0;
        float k = classic ? 2 : 1;                       // 95: the canvas is shown 2x
        uint[] Read()
        {
            string tmp = Path.Combine(dir, "_layout.png");
            wrist.Surf.SavePng(gpu, tmp);
            uint[] px;
            using (var bmp = new System.Drawing.Bitmap(tmp))
            {
                px = new uint[bmp.Width * bmp.Height];
                var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, (int[])(object)px, 0, px.Length);
                bmp.UnlockBits(bd);
            }
            File.Delete(tmp);
            return px;
        }
        // pixels outside 'r' (grown by 'grow') that are not transparent; and pixels in the band [inset, inset2) inside r
        // that differ from the card's own colour (the colour at its centre-left edge band, sampled)
        (int outside, string where) Outside(uint[] px, System.Drawing.RectangleF r, float grow, System.Drawing.RectangleF? also = null)
        {
            int n = 0; string where = null;
            for (int y = 0; y < WristPanel.TH; y++)
                for (int x = 0; x < WristPanel.TW; x++)
                {
                    if (x >= r.Left - grow && x < r.Right + grow && y >= r.Top - grow && y < r.Bottom + grow) continue;
                    if (also is { } a && x >= a.Left - grow && x < a.Right + grow && y >= a.Top - grow && y < a.Bottom + grow) continue;
                    if ((px[y * WristPanel.TW + x] >> 24) > 24) { n++; where ??= $"({x},{y})"; }
                }
            return (n, where);
        }
        // bg: the same card drawn alone (its fill, band, border) - each pixel is compared with its own background; else the card
        // colour is the most common colour in the band (the opened views)
        (int close, string where) TooClose(uint[] px, System.Drawing.RectangleF r, float from, float to, uint[] bg = null)
        {
            var counts = new Dictionary<uint, int>();
            var band = new List<(int, int)>();
            for (int y = (int)r.Top; y < (int)r.Bottom; y++)
                for (int x = (int)r.Left; x < (int)r.Right; x++)
                {
                    float dx = MathF.Min(x - r.Left, r.Right - 1 - x), dy = MathF.Min(y - r.Top, r.Bottom - 1 - y), d = MathF.Min(dx, dy);
                    // skip the rounded corners (the card's own curve lives there)
                    if (dx < 26 && dy < 26) continue;
                    if (d < from || d >= to) continue;
                    uint c = px[y * WristPanel.TW + x];
                    band.Add((x, y));
                    counts[c] = counts.TryGetValue(c, out int v) ? v + 1 : 1;
                }
            if (band.Count == 0) return (0, null);
            uint card = 0; int best = -1;
            foreach (var kv in counts) if (kv.Value > best) { best = kv.Value; card = kv.Key; }
            int n = 0; string where = null;
            foreach (var (x, y) in band)
            {
                uint c = px[y * WristPanel.TW + x];
                if (bg != null) card = bg[y * WristPanel.TW + x];
                int dr = (int)((c >> 16) & 255) - (int)((card >> 16) & 255), dg = (int)((c >> 8) & 255) - (int)((card >> 8) & 255), db = (int)(c & 255) - (int)(card & 255);
                if (Math.Abs(dr) + Math.Abs(dg) + Math.Abs(db) > 24) { n++; where ??= $"({x},{y})"; }
            }
            return (n, where);
        }
        wcfg.Face = "custom";
        wcfg.Background = "cards";
        if (variantsOnly)
        {
            problems += VariantChecks();
            wcfg.Background = "panel";
            Log.Info($"layout variants{LangTag}: {problems} problem(s)");
            return problems;
        }
        // every size the style offers; in 95 then the modern-only ones too (a face made in the modern style, drawn with the
        // nearest 95 picture)
        var sized = WatchWidgets.Kinds.SelectMany(kd => kd.SizesFor(classic).Select(s => (kd, s.W, s.H))).ToList();
        if (classic) sized.AddRange(WatchWidgets.Kinds.Where(kd => kd.Extra != null).SelectMany(kd => kd.Extra.Select(s => (kd, s.W, s.H))));
        foreach (var (kind, w, h) in sized)
        {
            if (onlyKind != null && kind.Id != onlyKind) continue;
            wcfg.Widgets = new List<WatchWidget> { new() { Type = kind.Id, X = 0, Y = 0, W = w, H = h } };
            wrist.Settle();
            wrist.Draw(data);
            wrist.Draw(data);
            Directory.CreateDirectory(Path.Combine(dir, "qa"));
            wrist.Surf.SavePng(gpu, Path.Combine(dir, "qa", $"{style}_{kind.Id}_{w}x{h}.png"));
            var px = Read();
            uint[] cardOnly = null;
            if (!classic && !kind.Button)
            {
                wrist.Backgrounds = true;
                wrist.Draw(data);
                wrist.Backgrounds = false;
                cardOnly = Read();
            }
            var r = WatchWidgets.CellRect(0, 0, w, h, classic);
            r = new System.Drawing.RectangleF(r.X * k, r.Y * k, r.Width * k, r.Height * k);
            var (o, ow) = Outside(px, r, classic ? 0 : 1.5f);
            var (cl, cw) = classic || kind.Button ? (0, null) : TooClose(px, r, 3, 8, cardOnly);
            if (o > 0 || cl > 0)
            {
                problems++;
                Log.Warn($"layout {style}: {kind.Id} {w}x{h}: {(o > 0 ? $"{o} px outside the card, first at {ow}" : "")}{(o > 0 && cl > 0 ? "; " : "")}{(cl > 0 ? $"{cl} px within 8 px of its edge, first at {cw}" : "")}");
            }
        }
        // the 4 x 3 page's sizes of every kind; the 95 face's on a face with pages (its taskbar along the bottom is not the widget's)
        foreach (var kind in WatchWidgets.Kinds)
            foreach (var (w, h) in WatchWidgets.Sizes43(kind))
            {
                if (onlyKind != null && kind.Id != onlyKind) continue;
                wcfg.Grid = "4x3";
                wcfg.Widgets = new List<WatchWidget> { new() { Type = kind.Id, X = 0, Y = 0, W = w, H = h } };
                wcfg.Pages = classic ? new List<WatchPage> { new() { Grid = "4x3" } } : null;
                wcfg.Page = 0;
                var grid = WatchPages.Grid(wcfg, 0);
                wrist.Settle();
                wrist.Draw(data);
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, "qa", $"{style}43_{kind.Id}_{w}x{h}.png"));
                var px = Read();
                uint[] cardOnly = null;
                if (!classic && !kind.Button)
                {
                    wrist.Backgrounds = true;
                    wrist.Draw(data);
                    wrist.Backgrounds = false;
                    cardOnly = Read();
                }
                var r = WatchWidgets.CellRect(0, 0, w, h, classic, grid);
                r = new System.Drawing.RectangleF(r.X * k, r.Y * k, r.Width * k, r.Height * k);
                System.Drawing.RectangleF? bar = classic ? new System.Drawing.RectangleF(0, (WristPanel.TH95 - WatchWidgets.Taskbar95 - 2) * k, WristPanel.TW, WristPanel.TH) : null;
                var (o, ow) = Outside(px, r, classic ? 0 : 1.5f, bar);
                var (cl, cw) = classic || kind.Button ? (0, null) : TooClose(px, r, 3, 8, cardOnly);
                if (o > 0 || cl > 0)
                {
                    problems++;
                    Log.Warn($"layout {style}: {kind.Id} 4x3 {w}x{h}: {(o > 0 ? $"{o} px outside the card, first at {ow}" : "")}{(o > 0 && cl > 0 ? "; " : "")}{(cl > 0 ? $"{cl} px within 8 px of its edge, first at {cw}" : "")}");
                }
            }
        wcfg.Grid = null;
        wcfg.Pages = null;
        wcfg.Page = 0;
        // the opened views: a 2x2 (or the smallest) widget of each, opened, without clip / dimming; the notifications with a long
        // made-up list, then with none (its empty state)
        wrist.LayoutTest = true;
        var notes0 = data.Notes;
        var opened = WatchWidgets.Kinds.Where(kd => !kd.Button && (onlyKind == null || kd.Id == onlyKind)).Select(kd => (kind: kd, name: kd.Id)).ToList();
        if (opened.Any(o => o.name == "notifications")) opened.Add((WatchWidgets.Find("notifications"), "notifications_none"));
        foreach (var (kind, name) in opened)
        {
            if (kind.Id == "notifications") data.Notes = name == kind.Id ? NotesViewTools.Fake(NotesViewTools.Long) : new NotificationHistory();
            var (w, h) = kind.Sizes[kind.Default];
            wcfg.Widgets = new List<WatchWidget> { new() { Type = kind.Id, X = 0, Y = 0, W = w, H = h } };
            wrist.Settle();
            if (!wrist.OpenForTest(kind.Id)) continue;
            wrist.Draw(data);
            wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, "qa", $"{style}_open_{name}.png"));
            var px = Read();
            var t = WatchWidgets.CellRect(0, 0, WatchWidgets.Cols, WatchWidgets.Rows, classic);       // the opened window covers the grid
            if (classic) t = new System.Drawing.RectangleF(t.X * 2, t.Y * 2, t.Width * 2, t.Height * 2);
            bool sheet = kind.Id == "folder";       // a folder opens over the whole watch, its widgets 10 px in as on a page (WristFolder.cs)
            if (sheet) t = new System.Drawing.RectangleF(0, 0, WristPanel.TW, WristPanel.TH);
            var own = WatchWidgets.CellRect(0, 0, w, h, classic);
            own = new System.Drawing.RectangleF(own.X * k, own.Y * k, own.Width * k, own.Height * k);
            var (o, ow) = Outside(px, t, classic ? 0 : 1.5f, own);
            var (cl, cw) = classic ? (0, null) : TooClose(px, t, 3, sheet ? 8 : 14);
            if (o > 0 || cl > 0)
            {
                problems++;
                Log.Warn($"layout {style}: opened {name}: {(o > 0 ? $"{o} px outside the card, first at {ow}" : "")}{(o > 0 && cl > 0 ? "; " : "")}{(cl > 0 ? $"{cl} px within 14 px of its edge, first at {cw}" : "")}");
            }
            wrist.CloseForTest();
        }
        data.Notes = notes0;
        wrist.LayoutTest = false;
        if (onlyKind != null)
        {
            wcfg.Background = "panel";
            Log.Info($"layout {style} ({onlyKind} only){LangTag}: {problems} problem(s)");
            return problems;
        }
        var bats0 = new List<(string, float, bool)>(data.Vr.Batteries);
        var fake0 = data.FakeMedia;
        void Shot(string type, int w, int h, string name, bool open)
        {
            wcfg.Widgets = new List<WatchWidget> { new() { Type = type, X = 0, Y = 0, W = w, H = h } };
            wrist.Settle();
            if (open) wrist.OpenForTest(type);
            wrist.Draw(data);
            wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, "qa", $"{style}_case_{name}.png"));
            wrist.CloseForTest();
        }
        (string, float, bool)[] all =
        {
            ("HMD", 0.71f, false), ("L", 0.82f, false), ("R", 0.18f, true), ("Trk×6", 0.44f, false), ("C", 0.09f, false), ("Trk×2", 1f, false),
        };
        foreach (int nb in new[] { 1, 2, 3, 5, 6 })
        {
            data.Vr.Batteries.Clear();
            for (int i = 0; i < nb; i++) data.Vr.Batteries.Add(all[i]);
            Shot("battery", 2, 2, $"bat{nb}_2x2", false);
            Shot("battery", 3, 2, $"bat{nb}_3x2", false);
            Shot("battery", 2, 2, $"bat{nb}_open", true);
        }
        data.Vr.Batteries.Clear();
        data.Vr.Batteries.AddRange(bats0);
        data.FakeMedia = (true, "A Very Long Song Title That Goes On And On (Extended Club Remix)", "Somebody Featuring Somebody Else", true);
        foreach (var (w, h) in new[] { (2, 1), (3, 1), (4, 1), (6, 1), (4, 2), (6, 2) }) Shot("media", w, h, $"longtitle_{w}x{h}", false);
        Shot("media", 4, 2, "longtitle_open", true);
        data.FakeMedia = (false, null, null, false);
        foreach (var (w, h) in new[] { (2, 1), (4, 1), (4, 2) }) Shot("media", w, h, $"nothing_{w}x{h}", false);
        Shot("media", 4, 2, "nothing_open", true);
        data.FakeMedia = fake0;
        // the time family in both languages and clocks, SteamVR on and off: qa/modern_<kind>_<WxH>_<case>.png, the same rules, and
        // no text cut short ("…") nor under 14 px; then qa/family_time.png, all of it side by side
        if (!classic)
        {
            var texts = new List<(string s, float size)>();
            var sheet = new List<(string label, string file, int w, int h)>();
            foreach (var fc in FamilyCases())
            {
                FamilySet(wrist, data, cfg, fc);
                wcfg.Widgets = new List<WatchWidget> { new() { Type = fc.Kind, X = 0, Y = 0, W = fc.W, H = fc.H } };
                wrist.Settle();
                wrist.Draw(data);
                texts.Clear();
                painter.TextSpy = (s, _, _, size, _, _) => texts.Add((s, size));
                wrist.Draw(data);
                painter.TextSpy = null;
                string file = Path.Combine(dir, "qa", $"{(classic ? "modern" : style)}_{fc.Kind}_{fc.W}x{fc.H}_{fc.Tag}.png");
                wrist.Surf.SavePng(gpu, file);
                if (fc.Tag is "en_12h" or "hu_24h" || (fc.Kind != "time" && fc.Tag is "en" or "hu" or "en_on_12h" or "hu_on_24h"))
                    sheet.Add(($"{fc.Kind} {fc.W}x{fc.H} {fc.Tag}", file, fc.W, fc.H));
                var px = Read();
                wrist.Backgrounds = true;
                wrist.Draw(data);
                wrist.Backgrounds = false;
                var cardOnly = Read();
                var r = WatchWidgets.CellRect(0, 0, fc.W, fc.H, false);
                var (o, ow) = Outside(px, r, 1.5f);
                var (cl, cw) = TooClose(px, r, 3, 8, cardOnly);
                var cut = texts.Where(x => x.s.Contains('…')).Select(x => x.s).ToList();
                var small = texts.Where(x => x.size < 14).Select(x => $"'{x.s}' {x.size:0.#} px").ToList();
                if (o > 0 || cl > 0 || cut.Count > 0 || small.Count > 0)
                {
                    problems++;
                    Log.Warn($"layout {style}: {fc.Kind} {fc.W}x{fc.H} {fc.Tag}: " + string.Join("; ", new[]
                    {
                        o > 0 ? $"{o} px outside the card, first at {ow}" : null, cl > 0 ? $"{cl} px within 8 px of its edge, first at {cw}" : null,
                        cut.Count > 0 ? "cut: " + string.Join(", ", cut) : null, small.Count > 0 ? "under 14 px: " + string.Join(", ", small) : null,
                    }.Where(x => x != null)));
                }
            }
            FamilySet(wrist, data, cfg, null);
            FamilySheet(Path.Combine(dir, "qa", $"family_time{Suffix(style)}.png"), sheet);
            problems += VariantChecks();
        }

        // The clocks' designs (WatchWidget.Variant): every design at every size it takes, the time in both languages and both clocks
        // (SteamVR on), with the family's rules (nothing outside its card or near its edge, no text cut or under 14 px); each also in
        // three looks. qa/variants/<kind>_<design>_<WxH>_<case>.png, side by side in qa/variants_time.png / variants_analog.png.
        // Then a flip card's fold: frames only while it folds.
        int VariantChecks()
        {
            int bad = 0;
            var texts = new List<(string s, float size)>();
            string vq = Path.Combine(dir, "qa", "variants" + Suffix(style));
            Directory.CreateDirectory(vq);
            var sheets = new Dictionary<string, List<(string label, string file, int w, int h)>> { ["time"] = new(), ["analog"] = new() };
            void One(string kind, string variant, int w, int h, Family fc, string look, uint? colour, bool sheet)
            {
                FamilySet(wrist, data, cfg, fc);
                wcfg.Widgets = new List<WatchWidget> { new() { Type = kind, X = 0, Y = 0, W = w, H = h, Variant = variant, Style = look, Color = colour } };
                wrist.Settle();
                wrist.Draw(data);
                texts.Clear();
                painter.TextSpy = (s, _, _, size, _, _) => texts.Add((s, size));
                wrist.Draw(data);
                painter.TextSpy = null;
                string tag = look == null ? fc.Tag : "look_" + look;
                string file = Path.Combine(vq, $"{kind}_{variant}_{w}x{h}_{tag}.png");
                wrist.Surf.SavePng(gpu, file);
                if (sheet) sheets[kind].Add(($"{variant} {w}x{h} {tag}", file, w, h));
                var px = Read();
                wrist.Backgrounds = true;
                wrist.Draw(data);
                wrist.Backgrounds = false;
                var cardOnly = Read();
                var r = WatchWidgets.CellRect(0, 0, w, h, false);
                var (o, ow) = Outside(px, r, 1.5f);
                var (cl, cw) = TooClose(px, r, 3, 8, cardOnly);
                var cut = texts.Where(x => x.s.Contains('…')).Select(x => x.s).ToList();
                var small = texts.Where(x => x.size < 14).Select(x => $"'{x.s}' {x.size:0.#} px").ToList();
                if (o > 0 || cl > 0 || cut.Count > 0 || small.Count > 0)
                {
                    bad++;
                    Log.Warn($"layout {style}: {kind} {variant} {w}x{h} {tag}: " + string.Join("; ", new[]
                    {
                        o > 0 ? $"{o} px outside the card, first at {ow}" : null, cl > 0 ? $"{cl} px within 8 px of its edge, first at {cw}" : null,
                        cut.Count > 0 ? "cut: " + string.Join(", ", cut) : null, small.Count > 0 ? "under 14 px: " + string.Join(", ", small) : null,
                    }.Where(x => x != null)));
                }
            }
            foreach (var lang in new[] { "en-US", "hu-HU" })
            {
                string l = lang[..2];
                var day = lang == "en-US" ? new DateTime(2026, 9, 30) : new DateTime(2026, 9, 24);
                DateTime noon = day.Add(new TimeSpan(12, 58, 47)), late = day.Add(new TimeSpan(23, 58, 47));
                foreach (var v in WatchWidgets.TimeVariants)
                    foreach (var (w, h) in WatchWidgets.Find("time").Union.Where(x => v.Fits(x.W, x.H)))
                    {
                        One("time", v.Id, w, h, new Family("time", w, h, $"{l}_12h", lang, false, 83, noon), null, null, l == "en");
                        One("time", v.Id, w, h, new Family("time", w, h, $"{l}_24h", lang, true, 83, late), null, null, l == "hu");
                    }
            }
            var enNoon = new Family("time", 3, 2, "en_12h", "en-US", false, 83, new DateTime(2026, 9, 30, 12, 58, 47));
            var looks = new (string, uint?)[] { ("solid", 0xFF14B8A6), ("glass", null), ("header", 0xFFFF5D93) };
            foreach (var v in WatchWidgets.TimeVariants)
                foreach (var (look, c) in looks) One("time", v.Id, 3, 2, enNoon, look, c, true);
            var ten = new Family("analog", 2, 2, "en", "en-US", true, 83, new DateTime(2026, 10, 15, 10, 9, 36));
            foreach (var v in WatchWidgets.AnalogVariants)
            {
                foreach (var (w, h) in WatchWidgets.Find("analog").Union.Where(x => v.Fits(x.W, x.H)).OrderBy(x => x.W))
                    One("analog", v.Id, w, h, ten, null, null, true);
                foreach (var (look, c) in looks) One("analog", v.Id, 2, 2, ten, look, c, true);
            }
            Log.Info("variants: the word clock's words " + string.Join(", ", new[] { (2, 2), (4, 1), (3, 2), (4, 2), (6, 2), (6, 3), (6, 4) }
                .Select(x => $"{x.Item1}x{x.Item2} {wrist.WordSize(x.Item1, x.Item2):0.#} px")));
            // a flip card folds only when its digits change, and asks for frames only while it folds
            FamilySet(wrist, data, cfg, enNoon with { At = new DateTime(2026, 9, 30, 12, 58, 59) });
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "time", X = 0, Y = 0, W = 3, H = 2, Variant = "flip" } };
            wrist.Settle();
            wrist.Draw(data);
            wrist.Draw(data);
            bool still = !wrist.Animating;
            wrist.TestNow = new DateTime(2026, 9, 30, 12, 59, 0);
            wrist.Draw(data);
            bool folds = wrist.Animating;
            Thread.Sleep(110);
            wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, "qa", $"variants_flip_fold{Suffix(style)}.png"));
            Thread.Sleep(320);
            wrist.Draw(data);
            bool done = !wrist.Animating;
            Log.Info($"variants: a flip card asks for no frames at rest {still} (want True), folds when the minute changes {folds} (want True), " +
                     $"stops within 0.45 s {done} (want True)");
            FamilySet(wrist, data, cfg, null);
            int Order(string kind, string label) => Array.FindIndex(WatchWidgets.VariantsOf(kind), v => label.StartsWith(v.Id + " "));
            foreach (var (kind, items) in sheets)
                FamilySheet(Path.Combine(dir, "qa", $"variants_{kind}{Suffix(style)}.png"),
                    items.OrderBy(x => Order(kind, x.label)).ThenBy(x => x.label.Split(' ')[^1]).ToList());
            return bad;
        }
        wcfg.Background = "panel";
        Log.Info($"layout {style}{LangTag}: {problems} problem(s)");
        return problems;
    }

    // The time family's test cases: every size of time, date, calendar and time in VR, in English and Hungarian, at a moment with
    // the longest names (en: Wednesday, September 30; hu: csütörtök, szeptember 24) and the widest digits (12:58 PM / 23:58);
    // the time in 12 and 24 hours and with SteamVR off, the calendar's month grid also in a month of six weeks (August 2026), the
    // time in VR on (1:23, since 11:35 AM / 22:35), in its first hour, long (12:05) and off.
    record struct Family(string Kind, int W, int H, string Tag, string Lang, bool H24, int VrMinutes, DateTime At);

    static IEnumerable<Family> FamilyCases()
    {
        foreach (var lang in new[] { "en-US", "hu-HU" })
        {
            string l = lang[..2];
            var day = lang == "en-US" ? new DateTime(2026, 9, 30) : new DateTime(2026, 9, 24);
            DateTime noon = day.Add(new TimeSpan(12, 58, 47)), late = day.Add(new TimeSpan(23, 58, 47));
            foreach (var kind in new[] { "time", "date", "calendar", "session" })
                foreach (var (w, h) in WatchWidgets.Find(kind).Union)
                    switch (kind)
                    {
                        case "time":
                            yield return new(kind, w, h, $"{l}_12h", lang, false, 83, noon);
                            yield return new(kind, w, h, $"{l}_24h", lang, true, 83, late);
                            yield return new(kind, w, h, $"{l}_off", lang, true, -1, late);
                            break;
                        case "session":
                            yield return new(kind, w, h, $"{l}_on_12h", lang, false, 83, noon);
                            yield return new(kind, w, h, $"{l}_on_24h", lang, true, 83, late);
                            yield return new(kind, w, h, $"{l}_min", lang, true, 42, late);
                            yield return new(kind, w, h, $"{l}_long", lang, true, 725, late);
                            yield return new(kind, w, h, $"{l}_off", lang, true, -1, late);
                            break;
                        default:
                            yield return new(kind, w, h, l, lang, true, 83, late);
                            if (kind == "calendar" && h >= 2) yield return new(kind, w, h, $"{l}_6wk", lang, true, 83, new DateTime(2026, 8, 31, 23, 58, 47));
                            break;
                    }
        }
    }

    // a style's pictures beside the modern ones: "" for the modern style (and the 95 ones), "_cyber" for Cyber
    static string Suffix(string style) => style == "cyber" ? "_cyber" : "";

    // a family case's moment, language, clock and SteamVR start on the watch (null: back to the clock, en-US, as it was)
    static CultureInfo _familyCulture;
    static bool _family24;
    static DateTime? _familySince;
    static void FamilySet(WristPanel wrist, PanelData data, ConfigStore cfg, Family? fc)
    {
        if (fc is not { } f)
        {
            if (_familyCulture == null) return;
            CultureInfo.CurrentCulture = _familyCulture;
            cfg.Data.Appearance.Clock24h = _family24;
            data.VrSince = _familySince;
            wrist.TestNow = null;
            _familyCulture = null;
            return;
        }
        if (_familyCulture == null) (_familyCulture, _family24, _familySince) = (CultureInfo.CurrentCulture, cfg.Data.Appearance.Clock24h, data.VrSince);
        if (CultureInfo.CurrentCulture.Name != f.Lang) CultureInfo.CurrentCulture = new CultureInfo(f.Lang);
        cfg.Data.Appearance.Clock24h = f.H24;
        wrist.TestNow = f.At;
        data.VrSince = f.VrMinutes < 0 ? null : f.At.AddMinutes(-f.VrMinutes);
    }

    // qa/family_time.png: the family's pictures cut to their cards, side by side in rows, each with its name
    static void FamilySheet(string path, List<(string label, string file, int w, int h)> items)
    {
        const int Width = 1700, Gap = 12, Label = 18;
        var place = new List<(int x, int y, System.Drawing.Rectangle crop)>();
        int x = Gap, y = Gap, rowH = 0;
        string last = null;
        foreach (var it in items)
        {
            var c = WatchWidgets.CellRect(0, 0, it.w, it.h, false);
            var crop = System.Drawing.Rectangle.FromLTRB((int)c.Left - 4, (int)c.Top - 4, (int)c.Right + 4, (int)c.Bottom + 4);
            string group = it.label.Split(' ')[0] + it.label.Split(' ')[^1][..2];
            if (x + crop.Width > Width || (last != null && group != last && x > Gap)) { x = Gap; y += rowH + Label + Gap; rowH = 0; }
            last = group;
            place.Add((x, y, crop));
            x += crop.Width + Gap;
            rowH = Math.Max(rowH, crop.Height);
        }
        using var bmp = new System.Drawing.Bitmap(Width, y + rowH + Label + Gap);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(255, 38, 40, 46));
        using var font = new System.Drawing.Font("Segoe UI", 9f);
        for (int i = 0; i < items.Count; i++)
        {
            var (px, py, crop) = place[i];
            g.DrawString(items[i].label, font, System.Drawing.Brushes.Gainsboro, px, py);
            using var src = new System.Drawing.Bitmap(items[i].file);
            g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 10, 11, 16)), px, py + Label, crop.Width, crop.Height);
            g.DrawImage(src, new System.Drawing.Rectangle(px, py + Label, crop.Width, crop.Height), crop, System.Drawing.GraphicsUnit.Pixel);
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // The widgets' own looks (WatchWidget.Style / Color / Color2): a gallery, and the layout check's rules on it.
    // qa/styles_<look>.png: a face in that look, its rows in a dark, a mid and a light colour; _<colour>: all of it in one colour;
    // _night: night mode (the fills dimmer), _night_vr: the same as seen in VR (the overlay dims everything to 45 % at night);
    // styles_mixed: every look at once; styles_glass_cards: glass with no page behind. Then every kind at every size alone in
    // each look (a picture only when something is wrong). Checked: nothing within 3-8 px of a card's edge - against a render of
    // the cards alone, so a gradient or a header's band is the card, not content - nor on a header band's lower edge, and every
    // text's contrast against what is really under it (a render without text; with no page, over a dark room and a white wall):
    // 4.5:1, 3:1 for big, bold or icon text; "nothing there" (Faint) is left out. styles_buttons_*: the tiles in every look.
    static int StylesRun(WristPanel wrist, PanelData data, Painter painter, ConfigStore cfg, Gpu gpu, string dir, string tag = "",
        bool variantsOnly = false, bool defaultOnly = false)
    {
        var wcfg = cfg.Data.Wrist;
        string qa = Path.Combine(dir, "qa");
        string pre = tag.Trim().Length > 0 ? tag.Trim() + "_" : "";       // another style's run: qa/<style>_styles_*.png
        Directory.CreateDirectory(qa);
        int TW = WristPanel.TW, TH = WristPanel.TH, problems = 0, faces = 0, checkedTexts = 0;
        var texts = new List<(string s, System.Drawing.RectangleF r, uint c, float size, Vortice.DirectWrite.FontWeight w, string fam, bool faint)>();
        var lowest = new Dictionary<string, (float cr, string what)>();
        uint[] Black = { 0xFF000000 }, Worlds = { 0xFF2A2C33, 0xFFFFFFFF };
        static int Dist(uint a, uint b) =>
            Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255)) + Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)) +
            Math.Abs((int)(a & 255) - (int)(b & 255));
        // a pixel (premultiplied: over black) seen over a world - floating cards (no page) are checked over a dark room and a
        // white wall
        static uint Over(uint p, uint world)
        {
            uint k = 255 - (p >> 24);
            uint Ch(int s) => Math.Min(255, ((p >> s) & 255) + (((world >> s) & 255) * k + 127) / 255);
            return 0xFF000000 | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
        }

        // the face on the watch now: drawn, then without text, then the cards alone; save = its picture (null: only when wrong)
        int Check(string look, string name, string save)
        {
            faces++;
            wrist.Settle();
            wrist.Draw(data);
            texts.Clear();
            painter.TextSpy = (s, r, c, size, w, fam) => texts.Add((s, r, c, size, w, fam, c == Bento.Faint));
            wrist.Draw(data);
            painter.TextSpy = null;
            var px = wrist.Surf.ReadPixels(gpu);
            painter.NoText = true;
            wrist.Draw(data);
            painter.NoText = false;
            var bare = wrist.Surf.ReadPixels(gpu);
            wrist.Backgrounds = true;
            wrist.Draw(data);
            wrist.Backgrounds = false;
            var cardsOnly = wrist.Surf.ReadPixels(gpu);
            int n = 0;
            bool floating = wcfg.Background == "cards";
            var worlds = floating ? Worlds : Black;
            var rects = new List<System.Drawing.RectangleF>();
            foreach (var w in wcfg.Widgets)
            {
                var k = WatchWidgets.Find(w.Type);
                if (k == null) continue;
                var r = WatchWidgets.Rect(w, false, WatchPages.Grid(wcfg, 0));
                rects.Add(r);
                if (k.Button) continue;          // a button is its card: drawn in its look, nothing near its edge
                int close = 0; string where = null;
                for (int y = (int)r.Top; y < (int)r.Bottom; y++)
                    for (int x = (int)r.Left; x < (int)r.Right; x++)
                    {
                        float dx = MathF.Min(x - r.Left, r.Right - 1 - x), dy = MathF.Min(y - r.Top, r.Bottom - 1 - y), d = MathF.Min(dx, dy);
                        if ((dx < 26 && dy < 26) || d < 3 || d >= 8) continue;
                        if (Dist(px[y * TW + x], cardsOnly[y * TW + x]) > 24) { close++; where ??= $"({x},{y})"; }
                    }
                if (close > 0) { n++; Log.Warn($"styles {name}: {w.Type} {w.W}x{w.H}: {close} px within 8 px of its edge, first at {where}"); }
                // a header's band: nothing but text within 3 px of its lower edge (a button half on it fuses with it)
                float band = wrist.BandOf(w);
                if (band <= 0) continue;
                int touch = 0; string at = null;
                for (int y = (int)(r.Top + band) - 3; y <= (int)(r.Top + band) + 3; y++)
                    for (int x = (int)r.Left + 4; x < (int)r.Right - 4; x++)
                        if (Dist(bare[y * TW + x], cardsOnly[y * TW + x]) > 24) { touch++; at ??= $"({x},{y})"; }
                if (touch > 0) { n++; Log.Warn($"styles {name}: {w.Type} {w.W}x{w.H}: {touch} px on its band's edge ({band:0} px), first at {at}"); }
            }
            if (floating)
            {
                // no page: nothing outside the cards
                int outside = 0; string where = null;
                for (int y = 0; y < TH; y++)
                    for (int x = 0; x < TW; x++)
                    {
                        bool inCard = false;
                        foreach (var r in rects)
                            if (x >= r.Left - 1.5f && x < r.Right + 1.5f && y >= r.Top - 1.5f && y < r.Bottom + 1.5f) { inCard = true; break; }
                        if (!inCard && (px[y * TW + x] >> 24) > 24) { outside++; where ??= $"({x},{y})"; }
                    }
                if (outside > 0) { n++; Log.Warn($"styles {name}: {outside} px outside the cards, first at {where}"); }
            }
            foreach (var t in texts)
            {
                if (t.faint || (t.c >> 24) < 0xF0) continue;
                int x0 = Math.Max(0, (int)t.r.Left), x1 = Math.Min(TW, (int)MathF.Ceiling(t.r.Right));
                int y0 = Math.Max(0, (int)t.r.Top), y1 = Math.Min(TH, (int)MathF.Ceiling(t.r.Bottom));
                var ink = new List<uint>();
                var all = new List<uint>();
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        uint a = px[y * TW + x], b = bare[y * TW + x];
                        all.Add(b);
                        if (Dist(a, b) > 48) ink.Add(b);
                    }
                var under = ink.Count >= 4 ? ink : all;
                if (under.Count == 0) continue;
                // what is under its letters: the middle of those backgrounds (by contrast) - with no page, over the world that
                // reads worst (a card that is not opaque shows it)
                float cr = float.MaxValue;
                uint bg = 0;
                foreach (uint world in worlds)
                {
                    var crs = new List<(float cr, uint bg)>(under.Count);
                    foreach (var b in under)
                    {
                        uint seen = Over(b, world);
                        crs.Add((Bento.Contrast(t.c | 0xFF000000, seen), seen));
                    }
                    crs.Sort((p, q) => p.cr.CompareTo(q.cr));
                    if (crs[crs.Count / 2].cr < cr) (cr, bg) = crs[crs.Count / 2];
                }
                bool big = t.size >= 24 || (t.w >= Vortice.DirectWrite.FontWeight.SemiBold && t.size >= 15) || t.fam == Theme.IconFont;
                float want = big ? 3f : 4.5f;
                checkedTexts++;
                string what = $"'{t.s}' {t.c & 0xFFFFFF:X6} on {bg & 0xFFFFFF:X6} in {name}";
                if (!lowest.TryGetValue(look, out var lo) || cr < lo.cr) lowest[look] = (cr, what);
                if (cr < want) { n++; Log.Warn($"styles {name}: {what}: {cr:0.0}:1, want {want:0.0}"); }
            }
            if (save == null && (n > 0 || Environment.GetEnvironmentVariable("ORBIT_QA_ALL") == "1")) save = $"style_{name.Replace(' ', '_')}.png";
            if (save != null)
            {
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(qa, pre + save));
            }
            problems += n;
            return n;
        }

        // a picture as seen in VR at night (the overlay's colour multiplies everything)
        static void Dim(string path, float k)
        {
            using var src = new System.Drawing.Bitmap(path);
            using var bmp = new System.Drawing.Bitmap(src);
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(c.A, (int)(c.R * k), (int)(c.G * k), (int)(c.B * k)));
                }
            bmp.Save(path.Replace(".png", "_vr.png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        List<WatchWidget> Face(string style, uint? c0, uint? c1, uint? c2)
        {
            var l = new List<WatchWidget>();
            void Put(string t, int x, int y, int w, int h, uint? c) =>
                l.Add(new WatchWidget { Type = t, X = x, Y = y, W = w, H = h, Style = style, Color = c });
            Put("time", 0, 0, 3, 2, c0); Put("battery", 3, 0, 2, 2, c0); Put("cpu", 5, 0, 1, 1, c0); Put("ram", 5, 1, 1, 1, c0);
            Put("media", 0, 2, 2, 1, c1); Put("graph", 2, 2, 3, 1, c1); Put("vram", 5, 2, 1, 1, c1);
            Put("calendar", 0, 3, 1, 1, c2); Put("volume", 1, 3, 1, 1, c2); Put("session", 2, 3, 1, 1, c2);
            Put("keyboard", 3, 3, 1, 1, c2); Put("desktops", 4, 3, 1, 1, c2); Put("night", 5, 3, 1, 1, c2);
            return l;
        }
        uint Col(string n) => Array.Find(Bento.Palette, p => p.name == n).c;
        uint navy = Col("Navy"), teal = Col("Teal"), amber = Col("Amber");

        // levels that need attention (a dot on a coloured card): VRAM nearly full, a frame time over budget, a low battery
        var vram0 = data.FakeVram;
        var gpu0 = data.Vr.GpuMs;
        data.FakeVram = data.VramTotalGB > 0 ? data.VramTotalGB * 0.95f : 15.2f;
        data.Vr.GpuMs = 12.5f;
        data.DesktopsVisible = true;
        data.KeyboardVisible = false;
        wcfg.Face = "custom";
        wcfg.Background = "panel";
        string[] looks = defaultOnly ? new[] { "default" } : new[] { "default", "tint", "solid", "twotone", "header", "outline", "glass" };
        // the clocks' designs in every look, each at every size it takes (a size's default is checked with the kinds below), with no
        // page; the colours taking turns (none every eighth), the time in English at 12 h and in Hungarian at 24 h by turns
        void VariantStyles()
        {
            wcfg.Background = "cards";
            uint[] vt = { navy, teal, amber, Col("Pink"), Col("White"), Col("Purple"), Col("Green") };
            int n = 0;
            foreach (var look in looks)
                foreach (var (kind, vs) in new[] { ("time", WatchWidgets.TimeVariants), ("analog", WatchWidgets.AnalogVariants) })
                    foreach (var v in vs)
                        foreach (var (w, h) in WatchWidgets.Find(kind).Union)
                        {
                            if (!v.Fits(w, h) || v.Id == WatchWidgets.DefaultVariant(kind, w, h)) continue;
                            n++;
                            bool en = n % 2 == 0;
                            var at = kind == "analog" ? new DateTime(2026, 10, 15, 10, 9, 36) : en ? new DateTime(2026, 9, 30, 12, 58, 47) : new DateTime(2026, 9, 24, 23, 58, 47);
                            string tag = en ? "en_12h" : "hu_24h";
                            FamilySet(wrist, data, cfg, new Family(kind, w, h, tag, en || kind == "analog" ? "en-US" : "hu-HU", !en, 83, at));
                            wcfg.Widgets = new List<WatchWidget>
                            {
                                new() { Type = kind, X = 0, Y = 0, W = w, H = h, Variant = v.Id, Style = look == "default" ? null : look, Color = n % 8 == 0 ? null : vt[n % vt.Length] },
                            };
                            Check(look, $"{look} {kind} {v.Id} {w}x{h} {tag}", null);
                        }
            FamilySet(wrist, data, cfg, null);
        }
        if (variantsOnly)
        {
            VariantStyles();
            wcfg.Background = "panel";
            data.FakeVram = vram0;
            data.Vr.GpuMs = gpu0;
            foreach (var look in lowest.Keys) Log.Info($"styles {look}: lowest text contrast {lowest[look].cr:0.0}:1 ({lowest[look].what})");
            Log.Info($"layout styles (variants){LangTag}: {problems} problem(s) in {faces} faces, {checkedTexts} texts checked");
            return problems;
        }
        foreach (var look in looks)
        {
            string style = look == "default" ? null : look;
            wcfg.Widgets = Face(style, navy, teal, amber);
            Check(look, look, $"styles_{look}.png");
            if (defaultOnly)
            {
                // the faces of the pages too (VRChat, Performance; Everyday on the 4 x 3 grid)
                foreach (var (preset, grid) in new[] { ("vrchat", "6x4"), ("performance", "6x4"), ("everyday", "4x3") })
                {
                    wcfg.Grid = grid == "4x3" ? grid : null;
                    wcfg.Widgets = WatchWidgets.PagePreset(preset, GridSpec.Of(grid));
                    Check(look, $"{look} page {preset}", $"styles_page_{preset}.png");
                }
                wcfg.Grid = null;
                continue;
            }
            foreach (var (cn, c) in new[] { ("navy", navy), ("pink", Col("Pink")), ("teal", teal), ("amber", amber), ("white", Col("White")) })
            {
                wcfg.Widgets = Face(style, c, c, c);
                Check(look, $"{look} {cn}", $"styles_{look}_{cn}.png");
            }
            if (style != null)
            {
                // automatic colours: the accent, purple / blue for the CPU and memory
                wcfg.Widgets = Face(style, null, null, null);
                Check(look, $"{look} auto", $"styles_{look}_auto.png");
            }
        }
        if (!defaultOnly)
        {
        // two-tone with a picked second colour
        wcfg.Widgets = Face("twotone", navy, teal, amber);
        foreach (var w in wcfg.Widgets) w.Color2 = w.Color == navy ? Col("Purple") : w.Color == teal ? Col("Blue") : Col("Red");
        Check("twotone", "twotone picked", "styles_twotone_picked.png");
        // night: the coloured fills dimmer (the night tile is on)
        data.NightMode = true;
        foreach (var look in new[] { "solid", "twotone", "tint", "header" })
        {
            wcfg.Widgets = Face(look, navy, teal, amber);
            Check(look, look + " night", $"styles_{look}_night.png");
            Dim(Path.Combine(qa, pre + $"styles_{look}_night.png"), 0.45f);
        }
        data.NightMode = false;
        // every look at once, and glass with no page behind
        wcfg.Widgets = Face(null, null, null, null);
        string[] mix = { "solid", "twotone", "glass", "tint", "header", "outline", "solid", "glass", "twotone", "tint", "solid", "outline", "header" };
        uint[] mixc = { navy, Col("Pink"), 0, teal, amber, Col("Purple"), Col("Green"), Col("Blue"), Col("Orange"), Col("Red"), Col("White"), Col("Cyan"), Col("Indigo") };
        for (int i = 0; i < wcfg.Widgets.Count; i++)
        {
            wcfg.Widgets[i].Style = mix[i % mix.Length];
            wcfg.Widgets[i].Color = mixc[i % mixc.Length] == 0 ? null : mixc[i % mixc.Length];
        }
        Check("mixed", "mixed", "styles_mixed.png");
        // the button tiles, a look per row: off (keyboard, night), on (monitors), and the ones with no on / off
        foreach (var (file, rows) in new[]
        {
            ("styles_buttons_1.png", new[] { ("default", navy), ("tint", teal), ("solid", amber), ("twotone", Col("Pink")) }),
            ("styles_buttons_2.png", new[] { ("header", navy), ("outline", teal), ("glass", amber), ("solid", Col("White")) }),
        })
        {
            wcfg.Widgets = new List<WatchWidget>();
            string[] tiles = { "keyboard", "desktops", "night", "recenter", "settings", "layout" };
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < tiles.Length; x++)
                    wcfg.Widgets.Add(new WatchWidget { Type = tiles[x], X = x, Y = y, Style = rows[y].Item1 == "default" ? null : rows[y].Item1, Color = rows[y].Item2 });
            Check("buttons", file[..^4], file);
        }
        // an opened widget with a colour of its own: that colour as the accent there too
        foreach (var (type, c) in new[] { ("volume", Col("Purple")), ("cpu", teal), ("timer", amber) })
        {
            wcfg.Widgets = new List<WatchWidget> { new() { Type = type, X = 0, Y = 0, W = 2, H = type == "timer" ? 2 : 1, Style = "solid", Color = c } };
            wrist.Settle();
            if (!wrist.OpenForTest(type)) continue;
            wrist.Draw(data);
            wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(qa, pre + $"styles_open_{type}.png"));
            wrist.CloseForTest();
        }
        wcfg.Background = "cards";
        wcfg.Widgets = Face("glass", null, teal, amber);
        Check("glass", "glass cards", "styles_glass_cards.png");
        wcfg.Widgets = Face("solid", navy, teal, amber);
        Check("solid", "solid cards", "styles_solid_cards.png");
        }
        else
        {
            // no page behind the cards (Erik's own face has none): the default face and the night tile
            wcfg.Background = "cards";
            wcfg.Widgets = Face(null, null, null, null);
            Check("default", "default cards", "styles_default_cards.png");
            data.NightMode = true;
            Check("default", "default cards night", "styles_default_cards_night.png");
            data.NightMode = false;
            wcfg.Background = "panel";
        }
        // every kind at every size alone, in each look (no page: whatever sticks out shows), the colours taking turns
        uint[] turns = { navy, teal, amber, Col("Pink"), Col("White"), Col("Purple") };
        static bool Apart(string id) => id is "weather" or "friends" or "recap";
        int turn = 0, wxTurn = 0, lxTurn = 0;      // (the weather, the light and the RGB take their turns apart: the kinds before them keep the colours they were checked in)
        foreach (var look in looks)
            foreach (var kind in WatchWidgets.Kinds)
                foreach (var (w, h) in kind.SizesFor(false))
                {
                    if (kind.Id == "analog" && w == 1) continue;     // below, in every colour (the others keep the colours they had)
                    wcfg.Widgets = new List<WatchWidget>
                        { new() { Type = kind.Id, X = 0, Y = 0, W = w, H = h, Style = look == "default" ? null : look, Color = kind.Id is "light" or "rgb" ? turns[lxTurn++ % turns.Length] : Apart(kind.Id) ? turns[wxTurn++ % turns.Length] : turns[turn++ % turns.Length] } };
                    Check(look, $"{look} {kind.Id} {w}x{h}", null);
                }
        // the 1x1 analog clock (its face is its card, ticks to its edge): each look in each colour and without one
        foreach (var look in looks)
            foreach (uint? c in turns.Select(x => (uint?)x).Append(null))
            {
                wcfg.Widgets = new List<WatchWidget> { new() { Type = "analog", X = 0, Y = 0, W = 1, H = 1, Style = look == "default" ? null : look, Color = c } };
                Check(look, $"{look} analog 1x1 {(c is { } v ? (v & 0xFFFFFF).ToString("X6") : "auto")}", null);
            }
        // the same on a 4 x 3 page
        wcfg.Grid = "4x3";
        foreach (var look in looks)
            foreach (var kind in WatchWidgets.Kinds)
                foreach (var (w, h) in WatchWidgets.Sizes43(kind))
                {
                    wcfg.Widgets = new List<WatchWidget>
                        { new() { Type = kind.Id, X = 0, Y = 0, W = w, H = h, Style = look == "default" ? null : look, Color = kind.Id is "light" or "rgb" ? turns[lxTurn++ % turns.Length] : Apart(kind.Id) ? turns[wxTurn++ % turns.Length] : turns[turn++ % turns.Length] } };
                    Check(look, $"{look} {kind.Id} 4x3 {w}x{h}", null);
                }
        wcfg.Grid = null;
        // the time family's cases (both languages and clocks, SteamVR on and off) in four looks
        foreach (var look in defaultOnly ? looks : new[] { "default", "solid", "header", "glass" })
            foreach (var fc in FamilyCases())
            {
                FamilySet(wrist, data, cfg, fc);
                wcfg.Widgets = new List<WatchWidget>
                    { new() { Type = fc.Kind, X = 0, Y = 0, W = fc.W, H = fc.H, Style = look == "default" ? null : look, Color = turns[turn++ % turns.Length] } };
                Check(look, $"{look} {fc.Kind} {fc.W}x{fc.H} {fc.Tag}", null);
            }
        FamilySet(wrist, data, cfg, null);
        VariantStyles();
        wcfg.Background = "panel";
        data.FakeVram = vram0;
        data.Vr.GpuMs = gpu0;
        foreach (var look in lowest.Keys) Log.Info($"styles{tag} {look}: lowest text contrast {lowest[look].cr:0.0}:1 ({lowest[look].what})");
        Log.Info($"layout styles{tag}{LangTag}: {problems} problem(s) in {faces} faces, {checkedTexts} texts checked");
        return problems;
    }

    // --wintest DIR: the desktop settings window in both styles (with its own config in DIR), on screen
    // for a moment each: a screenshot, its size at this DPI, and what the 95 caption answers to
    // WM_NCHITTEST (it must drag the window, but not over its ? and X boxes).
    // Hexpad's stream dock without a headset. Reads the live link (read-only: it only says hello - nothing is pressed,
    // turned, set or woken) and draws the panel in both styles and the watch widget in its sizes; then the same with
    // made-up faces (68 px device ones and 136 px sharp ones) in every state: not running, unplugged, idle, folder,
    // a confirmation, hover / pressed, sliders dragged (abs and jog), a disabled one, an api 1 Hexpad, long names.
    public static int DockTest(string dir)
    {
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(dir);
        var cfg = new ConfigStore(Path.Combine(dir, "config.json"));
        using var gpu = new Gpu(-1);
        using var painter = new Painter(gpu);
        using var link = new HexpadLink();
        link.Start();
        link.Active = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var live = link.Get();
        while (sw.Elapsed.TotalSeconds < 8)
        {
            live = link.Get();
            bool ok = live.State == HexpadLink.LinkState.Ok && live.Controls.Count > 0 && Array.TrueForAll(live.Faces, f => f != null);
            // an api 2 Hexpad sends the sharp faces a tick or two after the device ones
            if (ok && (live.Api < 2 || Array.Exists(live.HiFaces, f => f != null) || sw.Elapsed.TotalSeconds > 3)) break;
            if (live.State is HexpadLink.LinkState.NotRunning or HexpadLink.LinkState.OldHexpad && sw.Elapsed.TotalSeconds > 4) break;
            Thread.Sleep(100);
        }
        link.Active = false;                     // read once; never sends anything but hello
        int nf = Array.FindAll(live.Faces, f => f != null).Length, nh = Array.FindAll(live.HiFaces, f => f != null).Length;
        var f0 = Array.Find(live.Faces, f => f != null);
        var h0 = Array.Find(live.HiFaces, f => f != null);
        Log.Info($"docktest: {live.State} in {sw.Elapsed.TotalSeconds:0.0} s, present {link.Present}, api {live.Api} [{string.Join(",", live.Caps)}]; " +
                 $"page '{live.PageName}' {live.Page + 1}/{live.Pages}, folder '{live.FolderName}', idle {live.Ambient}, dark {live.Dark}, " +
                 $"faces {nf} ({f0?.W}x{f0?.H}), sharp {nh} ({h0?.W}x{h0?.H}), controls {live.Controls.Count}, values {live.Values.Count}");
        foreach (var (k, c) in live.Controls)
            Log.Info($"docktest: {k,-5} '{c.Label}'{(c.Push.Length > 0 ? $" push '{c.Push}'" : "")}{(c.Left.Length > 0 ? $" left '{c.Left}' right '{c.Right}'" : "")}" +
                     $"{(c.Danger ? " DANGER" : "")}{(k.StartsWith("enc") ? $" slider '{c.Slider}' kind '{c.Kind}' max {c.Max}" : "")}");
        foreach (var (k, v) in live.Values)
            Log.Info($"docktest: {k,-5} value {(v.V is float x ? x.ToString("0.00") : "null")}{(v.Muted ? " muted" : "")}{(v.Off ? " off" : "")}");

        using var faces = new HexpadFaces();
        using var panel = new DockPanel(gpu, painter, cfg, link, faces);
        using var wrist = new WristPanel(gpu, painter, cfg);
        var data = new PanelData { VrConnected = true };
        void Shots(string tag, HexpadLink.Snapshot s, (string name, string hover, string pressed, string confirm, string drag, float at)[] extra = null)
        {
            faces.Update(painter, s);
            foreach (var style in new[] { "modern", "classic" })
            {
                cfg.Data.Appearance.Style = style;
                Theme.Apply(cfg.Data.Appearance);
                string st = style == "classic" ? "_95" : "";
                panel.DrawFor(s, Clock.Now);
                panel.Surf.SavePng(gpu, Path.Combine(dir, $"dock_{tag}{st}.png"));
                foreach (var (name, hover, pressed, confirm, drag, at) in extra ?? Array.Empty<(string, string, string, string, string, float)>())
                {
                    panel.DrawFor(s, Clock.Now, hover, pressed, confirm, drag, at);
                    panel.Surf.SavePng(gpu, Path.Combine(dir, $"dock_{tag}_{name}{st}.png"));
                }
                // the watch widget in its three sizes, beside a few others
                data.Dock = s;
                data.DockFaces = faces;
                cfg.Data.Wrist.Face = "custom";
                cfg.Data.Wrist.Widgets = new List<WatchWidget>
                {
                    new() { Type = "dock", X = 0, Y = 0, W = 1, H = 1 }, new() { Type = "dock", X = 1, Y = 0, W = 2, H = 1 },
                    new() { Type = "dock", X = 3, Y = 0, W = 2, H = 2 }, new() { Type = "time", X = 0, Y = 1, W = 3, H = 1 },
                    new() { Type = "battery", X = 0, Y = 2, W = 2, H = 2 }, new() { Type = "media", X = 2, Y = 2, W = 4, H = 1 },
                    new() { Type = "settings", X = 5, Y = 0, W = 1, H = 1 },
                };
                wrist.Settle();
                wrist.Draw(data);
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_dock_{tag}{st}.png"));
            }
            cfg.Data.Appearance.Style = "modern";
            Theme.Apply(cfg.Data.Appearance);
        }
        if (nf > 0) Shots("live", live, new (string, string, string, string, string, float)[]
        {
            ("hover", "key2", null, null, null, 0), ("slider", "enc1:s", null, null, null, 0),
        });

        // made-up faces: a colour per key with its number, so orientation and order show; the sharp (136 px) ones
        // also have a ring, so a shot shows which of the two is drawn (modern: sharp, 95: the device's)
        var fake = new HexpadLink.Snapshot
        {
            State = HexpadLink.LinkState.Ok, Api = 2, Caps = new[] { "hi", "values", "set", "jog" },
            Profile = "Default", PageName = "Home", Page = 0, Pages = 10, Version = 1,
        };
        uint[] cols = { 0xFF2D6CDF, 0xFFE0473C, 0xFF3CB371, 0xFFE8A33D, 0xFF8E5BD8, 0xFF2BB5B8 };
        for (int i = 0; i < HexpadLink.Keys; i++)
        {
            var px = new uint[68 * 68];
            for (int y = 0; y < 68; y++)
                for (int x = 0; x < 68; x++)
                {
                    bool edge = x < 3 || y < 3 || x > 64 || y > 64, mark = x > 8 && x < 8 + 6 * (i + 1) && y > 8 && y < 16;
                    px[y * 68 + x] = edge ? 0xFFFFFFFF : mark ? 0xFF000000 : cols[i];
                }
            fake.Faces[i] = new HexpadLink.Face(px, 68, 68);
            var hp = new uint[136 * 136];
            for (int y = 0; y < 136; y++)
                for (int x = 0; x < 136; x++)
                {
                    bool edge = x < 6 || y < 6 || x > 129 || y > 129, mark = x > 16 && x < 16 + 12 * (i + 1) && y > 16 && y < 32;
                    float d = MathF.Sqrt((x - 68) * (x - 68) + (y - 80) * (y - 80));
                    hp[y * 136 + x] = edge ? 0xFFFFFFFF : mark ? 0xFF000000 : MathF.Abs(d - 34) < 1.5f ? 0xFFFFFFFF : cols[i];
                }
            fake.HiFaces[i] = new HexpadLink.Face(hp, 136, 136);
            fake.FaceVersion[i] = 1000 + i;
        }
        fake.Controls = new Dictionary<string, HexpadLink.Control>
        {
            ["key1"] = new() { Label = "Clock" }, ["key2"] = new() { Label = "Folder" }, ["key3"] = new() { Label = "System: sleep", Danger = true },
            ["key4"] = new() { Label = "Media: play" }, ["key5"] = new() { Label = "Folder" }, ["key6"] = new() { Label = "Quick: Empty Recycle Bin", Danger = true },
            ["btn7"] = new() { Label = "Page: prev" }, ["btn8"] = new() { Label = "Page: goto" }, ["btn9"] = new() { Label = "Page: next" },
            ["enc0"] = new() { Label = "Bright", Left = "Brightness: down", Right = "Brightness: up", Push = "Brightness: set", Slider = "abs", Kind = "brightness" },
            ["enc1"] = new() { Label = "Volume", Left = "Volume: down", Right = "Volume: up", Push = "Volume: mute", Slider = "abs", Kind = "volume" },
            ["enc2"] = new() { Label = "Light", Left = "Smartlight: brightness_down", Right = "Smartlight: brightness_up", Push = "Smartlight: toggle",
                               Slider = "jog", Kind = "smartlight" },
        };
        fake.Values = new Dictionary<string, HexpadLink.Value> { ["enc0"] = new() { V = 0 }, ["enc1"] = new() { V = 0.42f } };
        Shots("fake", fake, new (string, string, string, string, string, float)[]
        {
            ("hover", "key4", null, null, null, 0), ("pressed", "key4", "key4", null, null, 0), ("push", "enc1", "enc1", null, null, 0),
            ("slider", "enc1:s", null, null, null, 0), ("drag_abs", null, null, null, "enc1", 0.7f), ("drag_abs0", null, null, null, "enc0", 1f),
            ("drag_jog", null, null, null, "enc2", 62), ("drag_jog_far", null, null, null, "enc2", -400),
            ("confirm", null, null, "key3", null, 0),
        });
        HexpadLink.Snapshot With(Action<HexpadLink.Snapshot> f) { var c = fake.Copy(); c.Version++; f(c); return c; }
        void Ctl(HexpadLink.Snapshot c, string id, HexpadLink.Control v) => c.Controls = new(c.Controls) { [id] = v };
        // no turn binding (push only), monitors powered off, a volume Hexpad does not know yet
        Shots("disabled", With(c =>
        {
            Ctl(c, "enc0", new() { Label = "Mic", Push = "Mic: toggle" });
            Ctl(c, "enc2", new() { Label = "Screen", Left = "Monitors: down", Right = "Monitors: up", Push = "Monitors: set", Slider = "abs", Kind = "monitor" });
            c.Values = new() { ["enc1"] = new() { V = null }, ["enc2"] = new() { V = 0.5f, Off = true } };
        }));
        Shots("muted", With(c => c.Values = new() { ["enc0"] = new() { V = 0.66f }, ["enc1"] = new() { V = 0.42f, Muted = true } }));
        // an api 1 Hexpad: no values, no sharp faces, every turn a jog (what the parser makes of its controls)
        Shots("api1", With(c =>
        {
            c.Api = 1; c.Caps = Array.Empty<string>(); c.Values = new(); c.HiFaces = new HexpadLink.Face[HexpadLink.Keys];
            Ctl(c, "enc0", new() { Label = "Page", Left = "Page: prev", Right = "Page: next", Push = "Page: goto", Slider = "jog", Kind = "page" });
            Ctl(c, "enc1", new() { Label = "Volume", Left = "Volume: down", Right = "Volume: up", Push = "Volume: mute", Slider = "jog", Kind = "volume" });
            Ctl(c, "enc2", new() { Label = "Screen", Left = "Monitors: down", Right = "Monitors: up", Push = "Monitors: set", Slider = "jog", Kind = "monitors" });
        }), new (string, string, string, string, string, float)[] { ("drag_page", null, null, null, "enc0", 130), ("drag_vol", null, null, null, "enc1", -50) });
        // long names and pushes: nothing may run out of its box
        Shots("long", With(c =>
        {
            Ctl(c, "enc0", new() { Label = "Discord output volume", Left = "Discord: outvol_down", Right = "Discord: outvol_up", Push = "Discord: outvol_down",
                                   Slider = "abs", Kind = "discord", Max = 200 });
            Ctl(c, "enc1", new() { Label = "Desktop Audio (OBS)", Left = "OBS: vol_down", Right = "OBS: vol_up", Push = "OBS: vol_mute", Slider = "jog", Kind = "obs",
                                   Danger = true });
            Ctl(c, "enc2", new() { Label = "Prisma scene brightness", Left = "Rgbscene: bright_down", Right = "Rgbscene: bright_up",
                                   Push = "Rgbscene: next_scene_please", Slider = "jog", Kind = "rgbscene" });
            c.Values = new() { ["enc0"] = new() { V = 0.5f } };
        }), new (string, string, string, string, string, float)[] { ("drag", null, null, null, "enc0", 1f), ("confirm", null, null, "enc1", null, 0) });
        Shots("idle", With(c => c.Ambient = true));
        Shots("folder", With(c => { c.InFolder = true; c.FolderName = "Games"; c.FolderPage = 0; c.FolderPages = 2; }));
        Shots("notrunning", With(c => c.State = HexpadLink.LinkState.NotRunning));
        Shots("unplugged", With(c => c.State = HexpadLink.LinkState.Unplugged));
        Shots("old", With(c => c.State = HexpadLink.LinkState.OldHexpad));
        Log.Info("docktest: done");
        return 0;
    }

    // Who is in the room, without VRChat or a headset. 1) A made-up log written line by line into a temp folder (no VRCX):
    // arriving in a world (an 80-person burst = ONE "arrived" event), a join, a leave, OnPlayerLeftRoom, leaving the world
    // (no events) - checked, and the result logged. 2) The real newest VRChat log and VRCX, read-only, drawn as the panel
    // and the watch widget in both styles (names are Erik's own data: the PNGs stay local).
    public static int RoomTest(string dir)
    {
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(dir);
        int fails = 0;
        void Ck(string name, bool ok) { Log.Info($"roomtest: {(ok ? "ok  " : "FAIL")} {name}"); if (!ok) fails++; }

        // ---- 1. the parser on a made-up log ----
        var logDir = Path.Combine(dir, "log");
        if (Directory.Exists(logDir)) Directory.Delete(logDir, true);
        Directory.CreateDirectory(logDir);
        var file = Path.Combine(logDir, "output_log_2026-10-14_20-00-00.txt");
        string T() => DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss");
        void W(params string[] lines)
        {
            using var f = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var b = Encoding.UTF8.GetBytes(string.Concat(Array.ConvertAll(lines, l => $"{T()} Debug      -  {l}\r\n")));
            f.Write(b, 0, b.Length);
        }
        W("[Behaviour] Entering Room: Old World", "[Behaviour] OnPlayerJoined Before Person (usr_00000000-0000-0000-0000-000000000001)");
        using (var w = new RoomWatcher { LogDir = logDir, VrcxDb = Path.Combine(dir, "no-vrcx.sqlite3") })
        {
            w.Active = true;
            bool Wait(Func<RoomWatcher.Snapshot, bool> cond, double s)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < s) { if (cond(w.Get())) return true; Thread.Sleep(100); }
                return false;
            }
            Ck("history: the last room from the log, no events", Wait(s => s.InRoom && s.World == "Old World" && s.Here.Count == 1, 8) && w.Events.IsEmpty);
            W("[Behaviour] OnLeftRoom", "[Behaviour] OnPlayerLeft Before Person (usr_00000000-0000-0000-0000-000000000001)");
            Ck("leaving a world: nobody 'left', no events", Wait(s => !s.InRoom && s.Here.Count == 0, 4) && w.Events.IsEmpty);
            W("[Behaviour] Entering Room: Midnight Rooftop", "[Behaviour] Joining wrld_x:1", "[Behaviour] Successfully joined room");
            var burst = new List<string>();
            for (int i = 0; i < 80; i++) burst.Add($"[Behaviour] OnPlayerJoined Guest {i} (Tag) (usr_10000000-0000-0000-0000-{i:000000000000})");
            W(burst.ToArray());
            Ck("arriving: the burst is one 'arrived' event after it settles", Wait(s => s.InRoom && !s.Arriving && s.Here.Count == 80, 12));
            int arrived = 0, other = 0;
            while (w.Events.TryDequeue(out var ev)) { if (ev.Kind == RoomWatcher.Kind.Arrived && ev.People.Length == 80) arrived++; else other++; }
            Ck("exactly one arrived event with 80 people", arrived == 1 && other == 0);
            Ck("a name with brackets keeps them, the id is the last (usr_", w.Get().Here.Exists(p => p.Name == "Guest 7 (Tag)" && p.Id.EndsWith("000000000007")));
            W("[Behaviour] OnPlayerJoined Late Friend (usr_20000000-0000-0000-0000-000000000001)");
            Ck("a join after arriving is an event", Wait(_ => !w.Events.IsEmpty, 4) && w.Events.TryDequeue(out var j) && j.Kind == RoomWatcher.Kind.Joined && j.People[0].Name == "Late Friend");
            W("[Behaviour] OnPlayerLeftRoom", "[Behaviour] OnPlayerLeft Late Friend (usr_20000000-0000-0000-0000-000000000001)");
            Ck("a leave is an event (OnPlayerLeftRoom is not)", Wait(_ => !w.Events.IsEmpty, 4) && w.Events.TryDequeue(out var l) && l.Kind == RoomWatcher.Kind.Left
                && w.Events.IsEmpty && w.Get().Gone.Count == 1);
            Ck("the counts", w.Get().Here.Count == 80);
            // a new log (VRChat restarted): followed from its start
            var file2 = Path.Combine(logDir, "output_log_2026-10-14_21-00-00.txt");
            File.WriteAllText(file2, $"{T()} Debug      -  [Behaviour] Entering Room: Second\n");
            File.SetLastWriteTimeUtc(file2, DateTime.UtcNow.AddSeconds(5));
            Ck("a newer log takes over", Wait(s => s.World == "Second" && s.Here.Count == 0, 12));
        }

        // ---- 2. the real log + VRCX, drawn ----
        var cfg = new ConfigStore(Path.Combine(dir, "config.json"));
        using var gpu = new Gpu(-1);
        using var painter = new Painter(gpu);
        RoomWatcher.Snapshot live;
        using (var w = new RoomWatcher())
        {
            w.Active = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            live = w.Get();
            while (sw.Elapsed.TotalSeconds < 6 && live.Version < 2) { Thread.Sleep(200); live = w.Get(); }
            Thread.Sleep(500);
            live = w.Get();
        }
        Log.Info($"roomtest: real log found {live.LogFound}, in room {live.InRoom}, {live.Here.Count} here ({live.Here.FindAll(p => p.Friend).Count} friends), " +
                 $"{live.Gone.Count} gone, VRCX {live.VrcxFound} ({live.Friends} friends), me {(live.MeId.Length > 0 ? "known" : "unknown")}");
        // a made-up room for the pictures that may be shown
        var fake = new RoomWatcher.Snapshot { Version = 1, LogFound = true, InRoom = true, World = "Midnight Rooftop", VrcxFound = true, Friends = 120 };
        string[] names = { "Nova", "Pixel Fox", "Moss", "Kite", "Juniper", "Static", "Orbit Fan 2000", "Lumen", "Quill", "Sable", "Wren", "Echo" };
        for (int i = 0; i < names.Length; i++)
            fake.Here.Add(new RoomWatcher.Person { Id = $"usr_{i}", Name = names[i], Joined = DateTime.Now.AddMinutes(-3 * i - 1), Friend = i is 1 or 4 or 9 });
        fake.Gone.Add(new RoomWatcher.Person { Id = "usr_g", Name = "Drifter", Joined = DateTime.Now.AddMinutes(-40), Left = DateTime.Now.AddMinutes(-2) });
        cfg.Data.Room.Starred.Add("usr_6");
        using var panel = new RoomPanel(gpu, painter, cfg);
        using var wrist = new WristPanel(gpu, painter, cfg);
        var data = new PanelData { VrConnected = true };
        foreach (var (tag, s) in new[] { ("live", live), ("fake", fake), ("empty", new RoomWatcher.Snapshot { Version = 1, LogFound = true }) })
            foreach (var style in new[] { "modern", "classic" })
            {
                cfg.Data.Appearance.Style = style;
                Theme.Apply(cfg.Data.Appearance);
                string st = style == "classic" ? "_95" : "";
                panel.DrawFor(s, DateTime.Now);
                panel.Surf.SavePng(gpu, Path.Combine(dir, $"room_{tag}{st}.png"));
                data.Room = s;
                cfg.Data.Wrist.Face = "custom";
                cfg.Data.Wrist.Widgets = new List<WatchWidget>
                {
                    new() { Type = "room", X = 0, Y = 0, W = 1, H = 1 }, new() { Type = "room", X = 1, Y = 0, W = 2, H = 1 },
                    new() { Type = "status", X = 3, Y = 0, W = 2, H = 1 }, new() { Type = "status", X = 5, Y = 0, W = 1, H = 1 },
                    new() { Type = "time", X = 0, Y = 1, W = 4, H = 2 },
                };
                data.StatusLine = tag == "fake";
                wrist.Settle();
                wrist.Draw(data);
                wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_room_{tag}{st}.png"));
            }
        // the status line text
        var cb = new ChatboxConfig { StatusLine = true, ShowMedia = true, ShowTime = true, ShowVrTime = true };
        string line = StatusLine.Compose(cb, (true, "Midnight City", "M83", true), new DateTime(2026, 10, 14, 21, 34, 0), DateTime.Now.AddMinutes(-83), true);
        Ck("status line: " + line, line.Contains("Midnight City") && line.Contains("21:34") && line.Contains("in VR"));
        string longLine = StatusLine.Compose(cb, (true, new string('x', 300), "y", true), DateTime.Now, null, true);
        Ck("status line is cut to 144", longLine.Length <= StatusLine.Max);
        Ck("nothing playing, nothing else: empty", StatusLine.Compose(new ChatboxConfig { StatusLine = true }, (false, null, null, false), DateTime.Now, null, true) == "");
        Log.Info($"roomtest: {(fails == 0 ? "ALL OK" : fails + " FAILED")}");
        return fails == 0 ? 0 : 1;
    }

    public static int WinTest(string dir)
    {
        Directory.CreateDirectory(dir);
        var cfg = new ConfigStore(Path.Combine(dir, "config.json"));
        using var gpu = new Gpu(-1);
        using var painter = new Painter(gpu);
        var data = new PanelData { VrConnected = true };
        using var wrist = new WristPanel(gpu, painter, cfg);
        using var kb = new KeyboardPanel(gpu, painter, cfg);
        var mons = Monitors.Enumerate();
        foreach (var m in mons)
            if (cfg.Data.Desktops.All(d => d.Id != m.Id))
                cfg.Data.Desktops.Add(new DesktopConfig { Id = m.Id, Name = m.Name, Enabled = !m.Virtual });
        var host = new FakeHost { Cfg = cfg, GpuName = gpu.AdapterName, MonitorList = mons, Data = data, Wrist = wrist, Keyboard = kb };
        var view = new SettingsView(host);
        using var win = new SettingsWindow(gpu, painter, view, () =>
        {
            if (view.ShowsWristPreview) wrist.Draw(data);
            if (view.ShowsKeyboardPreview) kb.Draw();
        }, System.Drawing.SystemIcons.Application);
        void Pump(double seconds)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds) { System.Windows.Forms.Application.DoEvents(); win.Tick(Clock.Now); Thread.Sleep(15); }
        }
        int Hit(float cx, float cy)
        {
            var p = win.PointToScreen(new System.Drawing.Point((int)cx, (int)cy));
            return (int)SendMessage(win.Handle, 0x84, IntPtr.Zero, (IntPtr)((p.Y << 16) | (p.X & 0xFFFF)));
        }
        int step = 0;
        foreach (var style in new[] { "classic", "modern", "classic" })
        {
            cfg.Data.Appearance.Style = style;
            Theme.Apply(cfg.Data.Appearance);
            win.ShowAndFocus();
            win.TopMost = true;
            Pump(1.5);
            float k = win.ClientSize.Width / (float)SettingsView.W95W;
            Log.Info($"{style}: border {win.FormBorderStyle}, client {win.ClientSize.Width}x{win.ClientSize.Height}, dpi {win.DeviceDpi}, " +
                     $"hit caption={Hit(100 * k, 10 * k)} help={Hit(720 * k, 12 * k)} close={Hit(737 * k, 12 * k)} page={Hit(100 * k, 100 * k)}");
            var r = win.RectangleToScreen(win.ClientRectangle);
            using var bmp = new System.Drawing.Bitmap(r.Width, r.Height);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, System.Drawing.Point.Empty, r.Size);
            bmp.Save(Path.Combine(dir, $"window{++step}_{style}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        win.AllowClose = true;
        win.Close();
        return 0;
    }

    // Registers a temporary NON-elevated copy of the task, runs it with arguments ("--osctest 9018",
    // which a listener can catch), then deletes it. Proves the COM task path without a UAC prompt.
    public static int TaskTest()
    {
        const string name = "Orbit (test, temporary)";
        if (!Elevation.InstallTask(highest: false, name: name)) return 2;
        try
        {
            bool ready = Elevation.TaskReady(name);
            bool ran = Elevation.RunTask("--osctest 9018", name);
            Log.Info($"task test: ready={ready} ran={ran}");
            Thread.Sleep(3000);
            return ready && ran ? 0 : 3;
        }
        finally { Elevation.RemoveTask(name); }
    }

    // Simulated hand tremor + tracking noise at 90 Hz through each smoothing level: jitter left on a
    // 1920 px monitor 1.07 m wide at 1.2 m while holding still, and how far the cursor trails behind
    // slow / medium / fast sweeps (ms behind the hand, measured once the sweep is steady).
    // An off-screen window with a text box, a read-only text box and a button: classifies each
    // directly (class name / accessibility role) and through the focus hook.
    public static int FocusTest()
    {
        const int OBJID_CLIENT = -4;
        var form = new System.Windows.Forms.Form
        {
            StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-6000, -6000),
            Size = new System.Drawing.Size(400, 200), ShowInTaskbar = false, Text = "Orbit focus test",
        };
        var text = new System.Windows.Forms.TextBox { Left = 10, Top = 10, Width = 200 };
        var ro = new System.Windows.Forms.TextBox { Left = 10, Top = 40, Width = 200, ReadOnly = true, Text = "read only" };
        var button = new System.Windows.Forms.Button { Left = 10, Top = 70, Text = "OK" };
        var multi = new System.Windows.Forms.RichTextBox { Left = 220, Top = 10, Width = 150, Height = 80 };
        form.Controls.AddRange(new System.Windows.Forms.Control[] { text, ro, button, multi });
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        foreach (var (name, c) in new (string, System.Windows.Forms.Control)[] { ("text box", text), ("read-only box", ro), ("button", button), ("rich text", multi) })
        {
            int quick = TextFocusWatcher.Classify(c.Handle, OBJID_CLIENT, 0, false, out _);
            int deep = TextFocusWatcher.Classify(c.Handle, OBJID_CLIENT, 0, true, out bool vague);
            Log.Info($"classify {name,-14} quick={quick,2} deep={deep,2}{(vague ? " (vague)" : "")}");
        }
        using var w = new TextFocusWatcher(skipOwnProcess: false) { Trace = s => Log.Info("  " + s) };
        w.Start();
        w.InspectFor(30);
        Thread.Sleep(200);
        form.Activate();
        foreach (var (name, c) in new (string, System.Windows.Forms.Control)[] { ("text box", text), ("button", button), ("read-only box", ro), ("rich text", multi) })
        {
            c.Focus();
            for (int i = 0; i < 20; i++) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(15); }
            var (seq, state, _) = w.Snapshot();
            Log.Info($"focus {name,-14} -> event #{seq} state {state}");
        }
        form.Close();
        return 0;
    }

    // Runs one background component alone and logs the process's private memory every 10 s,
    // then forces a full GC: growth that a GC gives back = garbage / RCWs, growth that stays = a leak.
    // (10-24: the components that run all through a VR session - Erik's private bytes grew 1-3 MB a minute, the managed heap did not)
    public static int LeakTest(string what, int seconds)
    {
        static long Priv() { using var p = System.Diagnostics.Process.GetCurrentProcess(); return p.PrivateMemorySize64; }
        Action tick = null;                  // what the main loop does for it, 90 times a second
        Gpu gpu = null;
        IDisposable comp = what switch
        {
            "gpu" => new GpuStats { Active = true },
            "media" => new MediaInfo { Active = true },
            "room" => new RoomWatcher { Active = true },
            "photos" => new PhotoWatcher { Active = true },
            _ => null,
        };
        if (what == "mixer")
        {
            var mx = new AudioMixer();
            comp = mx;
            tick = () => mx.Pump(Clock.Now, true);
        }
        else if (what == "mixtape")
        {
            // Mixtape's link as the app runs it with Now playing on a page (its state once a second; read only)
            var mt = new MixtapeLink { Active = true };
            comp = mt;
            tick = () => mt.Pump();
        }
        else if (what == "lumos")
        {
            // the bulb straight, as the light widget reads it (its state once a minute, every 5 s while watched; read only)
            var ll = new LumosLink();
            ll.SetKey(TapoAccount.Import(out _));
            string bulbIp = "";             // (the config only read, as text: no ConfigStore on the real one - it may write)
            try
            {
                using var cj = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Log.Dir, "config.json")));
                if (cj.RootElement.TryGetProperty("Lights", out var lj) && lj.TryGetProperty("Bulb", out var bj)) bulbIp = bj.GetString() ?? "";
            }
            catch { }
            ll.SetBulb(bulbIp);
            ll.Active = true;
            ll.Watched = true;
            comp = ll;
        }
        else if (what is "audioenum" or "audiofollow")
        {
            // the mixer's two once-a-second Core Audio calls alone, 20 times a second: the session list (GetSessionEnumerator) or the
            // default device (GetDefaultAudioEndpoint + GetId) - which one keeps native memory (10-24)
            tick = CoreAudioDevice.LeakProbe(what == "audioenum");
        }
        else if (what == "eyes")
        {
            var ey = new EyeGaze();
            comp = ey;
            tick = () => ey.Update(Clock.Now, true);
        }
        else if (what == "capture")
        {
            // the first monitor captured as the panels do (a frame polled 90 times a second, its copy kept)
            gpu = new Gpu(0);
            var dev = MonitorCapture.CreateWinRtDevice(gpu);
            var cap = new MonitorCapture(gpu, dev, Monitors.Enumerate()[0]);
            comp = cap;
            tick = () => cap.Poll();
        }
        long start = Priv();
        Log.Info($"leaktest {what}: start private {start / 1024} KB");
        long alloc0 = GC.GetTotalAllocatedBytes();
        for (int t = 10; t <= seconds; t += 10)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until) { tick?.Invoke(); Thread.Sleep(tick == null ? 200 : 11); }
            long alloc = GC.GetTotalAllocatedBytes();
            Log.Info($"leaktest {what}: {t,3} s  private {Priv() / 1024} KB  (+{(Priv() - start) / 1024} KB)  GCs {GC.CollectionCount(0)}  allocated {(alloc - alloc0) / 10240} KB/s");
            alloc0 = alloc;
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Thread.Sleep(500);
        Log.Info($"leaktest {what}: after full GC  private {Priv() / 1024} KB  (+{(Priv() - start) / 1024} KB)");
        if (comp is MediaInfo mi)
        {
            var snap = mi.Snapshot();
            Log.Info($"leaktest media: {mi.Reads} reads, has={snap.has} playing={snap.playing} title='{snap.title}' art={(snap.art != null ? "yes" : "no")}");
        }
        comp?.Dispose();
        gpu?.Dispose();
        return 0;
    }

    public static int FilterTest()
    {
        const float dt = 1f / 90, pxPerRad = 1920 / 1.07f * 1.2f;
        for (int level = 0; level <= 3; level++)
        {
            var rng = new Random(1);
            var f = new RayFilter();
            double sum = 0, sum2 = 0; int n = 0;
            for (int i = 0; i < 900; i++)
            {
                float t = i * dt;
                // 0.12 deg noise + 0.08 deg 8 Hz tremor around straight ahead
                float jx = (float)(Gauss(rng) * 0.0021 + 0.0014 * Math.Sin(2 * Math.PI * 8 * t));
                float jy = (float)(Gauss(rng) * 0.0021 + 0.0014 * Math.Cos(2 * Math.PI * 7 * t));
                var (_, d) = f.Filter(Vector3.Zero, Vector3.Normalize(new Vector3(jx, jy, -1)), dt, level);
                if (i < 180) continue;
                double ax = Math.Atan2(d.X, -d.Z);
                sum += ax; sum2 += ax * ax; n++;
            }
            double sd = Math.Sqrt(sum2 / n - (sum / n) * (sum / n));
            var lags = new System.Collections.Generic.List<string>();
            foreach (float speed in new[] { 0.1f, 0.5f, 2.0f })
            {
                var f2 = new RayFilter();
                double lag = 0;
                int frames = Math.Min(270, (int)(2.5f / speed / dt));    // stay below 2.5 rad of sweep
                for (int i = 0; i < frames; i++)
                {
                    float ang = i * dt * speed;
                    var (_, d) = f2.Filter(Vector3.Zero, new Vector3(MathF.Sin(ang), 0, -MathF.Cos(ang)), dt, level);
                    lag = (ang - Math.Atan2(d.X, -d.Z)) / speed;
                }
                lags.Add($"{speed:0.0} rad/s {lag * 1000:0} ms");
            }
            Log.Info($"smoothing {level}: jitter {sd * 180 / Math.PI:0.000} deg = {sd * pxPerRad:0.0} px | lag {string.Join(", ", lags)}");
        }
        return 0;
    }

    static double Gauss(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

    // Sends one of each OSC message type to 127.0.0.1:<port> (for checking the wire format).
    public static int OscTest(int port)
    {
        using var osc = new OscSender();
        osc.Send(0, port, new (string, object)[] { ("isOverlayOpen", true), ("isKeyboardOpen", false), ("openOverlayCount", 3), ("leftControllerBattery", 0.75f) });
        return 0;
    }

    // Pretends to be a game: submits a dark gradient to both eyes so the dashboard closes and
    // overlays can be seen (and timed) the way they look over VRChat.
    public static int FakeScene(bool openDashboardTab)
    {
        var e = EVRInitError.None;
        OpenVR.Init(ref e, EVRApplicationType.VRApplication_Scene);
        if (e != EVRInitError.None) { Log.Error("scene init: " + e); return 3; }
        try
        {
            int adapter = -1;
            OpenVR.System.GetDXGIOutputInfo(ref adapter);
            using var gpu = new Gpu(adapter);
            using var painter = new Painter(gpu);
            uint w = 0, h = 0;
            OpenVR.System.GetRecommendedRenderTargetSize(ref w, ref h);
            using var eye = new Surface(gpu, (int)w, (int)h);
            painter.Begin(eye.Bitmap, 0xFF20304A);
            for (int y = 0; y < h; y += 8)
                painter.Fill(new System.Drawing.RectangleF(0, y, w, 8), Theme.Mix(0xFF2B4A6E, 0xFF101820, y / (float)h));
            painter.End();
            var tex = new Texture_t { handle = eye.Tex.NativePointer, eType = ETextureType.DirectX, eColorSpace = EColorSpace.Gamma };
            var bounds = new VRTextureBounds_t { uMin = 0, vMin = 0, uMax = 1, vMax = 1 };
            var render = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            var game = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool dashDone = false;
            while (sw.Elapsed.TotalSeconds < 12)
            {
                OpenVR.Compositor.WaitGetPoses(render, game);
                OpenVR.Compositor.Submit(EVREye.Eye_Left, ref tex, ref bounds, EVRSubmitFlags.Submit_Default);
                OpenVR.Compositor.Submit(EVREye.Eye_Right, ref tex, ref bounds, EVRSubmitFlags.Submit_Default);
                if (openDashboardTab && !dashDone && sw.Elapsed.TotalSeconds > 4)
                {
                    dashDone = true;
                    OpenVR.Overlay.ShowDashboard(OrbitApp.AppKey + ".settings");
                    Log.Info("ShowDashboard called");
                }
            }
            Log.Info("scene done");
            return 0;
        }
        finally { OpenVR.Shutdown(); }
    }

    // Needs SteamVR running (a headset or the null driver). Shows a test overlay and checks
    // which way ComputeOverlayIntersection reports V, plus frame sync and timing calls.
    public static int SelfTest()
    {
        if (!VrSession.RuntimeRunning()) { Log.Error("SteamVR is not running"); return 2; }
        using var vr = new VrSession();
        if (!vr.TryConnect(Path.Combine(AppContext.BaseDirectory, "assets", "actions.json"), out var err)) { Log.Error("init: " + err); return 3; }
        using var gpu = new Gpu(vr.AdapterIndex);
        using var painter = new Painter(gpu);
        using var surf = new Surface(gpu, 512, 256);
        painter.Begin(surf.Bitmap, Theme.Panel2);
        painter.Fill(new System.Drawing.RectangleF(0, 0, 512, 64), Theme.Accent);       // top band
        painter.Text("Orbit self-test (top = cyan)", new System.Drawing.RectangleF(0, 64, 512, 192), Theme.Text, 30,
            Vortice.DirectWrite.FontWeight.SemiBold, Vortice.DirectWrite.TextAlignment.Center);
        painter.End();

        using var ov = new VrOverlay("erik.orbit.selftest", "Orbit self-test");
        ov.SetTexture(surf.Tex);
        ov.SetMouseScale(512, 256);
        ov.SetWidth(1.0f);               // -> 0.5 m tall
        var at = Matrix4x4.CreateTranslation(0, 1.5f, -2f);
        ov.SetAbsolute(at);
        ov.Show();
        Thread.Sleep(300);

        // ray 0.15 m above the centre and 0.25 m right: top-left uv should be (0.75, 0.2)
        var p = new VROverlayIntersectionParams_t
        {
            vSource = new HmdVector3_t { v0 = 0.25f, v1 = 1.65f, v2 = 0 },
            vDirection = new HmdVector3_t { v0 = 0, v1 = 0, v2 = -1 },
            eOrigin = ETrackingUniverseOrigin.TrackingUniverseStanding,
        };
        var r = new VROverlayIntersectionResults_t();
        bool hit = OpenVR.Overlay.ComputeOverlayIntersection(ov.Handle, ref p, ref r);
        Log.Info($"intersection hit={hit} dist={r.fDistance:0.000} uv=({r.vUVs.v0:0.000},{r.vUVs.v1:0.000}) point=({r.vPoint.v0:0.00},{r.vPoint.v1:0.00},{r.vPoint.v2:0.00})");
        if (hit)
            Log.Info(Math.Abs(r.vUVs.v1 - 0.8f) < 0.05f ? "UV CONVENTION: bottom-left origin (flip needed) -> UvFlipV=true is right"
                : Math.Abs(r.vUVs.v1 - 0.2f) < 0.05f ? "UV CONVENTION: top-left origin -> UvFlipV must be false" : "UV CONVENTION: unexpected");

        // curved overlay: does it bend around an axis r in front of its origin (+Z)?
        using (var cv = new VrOverlay("erik.orbit.selftest.curve", "Orbit self-test curve"))
        {
            const float w = 1.0f, c = 0.2f;
            float rad = w / (2 * MathF.PI * c), alpha = w / (2 * rad);
            cv.SetTexture(surf.Tex);
            cv.SetMouseScale(512, 256);
            cv.SetWidth(w);
            cv.SetCurvature(c);
            cv.SetAbsolute(Matrix4x4.CreateTranslation(0, 1.5f, -rad));
            cv.Show();
            Thread.Sleep(300);
            foreach (float k in new[] { 0f, 0.5f, 0.9f, -0.9f })
            {
                float phi = k * alpha;
                var cp = new VROverlayIntersectionParams_t
                {
                    vSource = new HmdVector3_t { v0 = 0, v1 = 1.5f, v2 = 0 },
                    vDirection = new HmdVector3_t { v0 = MathF.Sin(phi), v1 = 0, v2 = -MathF.Cos(phi) },
                    eOrigin = ETrackingUniverseOrigin.TrackingUniverseStanding,
                };
                var cr = new VROverlayIntersectionResults_t();
                bool ch = OpenVR.Overlay.ComputeOverlayIntersection(cv.Handle, ref cp, ref cr);
                Log.Info($"curve k={k,5:0.0}: hit={ch} dist={cr.fDistance:0.000} (axis model expects {rad:0.000}) u={cr.vUVs.v0:0.000} (expects {0.5f + k / 2:0.000})");
            }
        }

        var sync = OpenVR.Overlay.WaitFrameSync(100);
        Log.Info($"WaitFrameSync: {sync}");
        vr.Update();
        Log.Info($"HMD valid={vr.HmdValid} pos={vr.Hmd.Translation} hz={vr.DisplayHz} input={vr.InputStatus} left={vr.LeftIndex} right={vr.RightIndex}");
        var stats = new VrStats();
        stats.Update(vr, 1); Thread.Sleep(1100); stats.Update(vr, 2.2);
        Log.Info($"stats fps={stats.AppFps} reproj={stats.ReprojPct} gpu={stats.GpuMs}ms batteries={string.Join(",", stats.Batteries)}");

        int holdMs = 4000;
        Log.Info($"holding the test overlay for {holdMs} ms");
        Thread.Sleep(holdMs);
        return 0;
    }
}
