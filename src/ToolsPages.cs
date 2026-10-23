using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Orbit;

// --snapshot's part for the watch pages (1.12): the VRChat, Performance and Everyday pages in both styles, every new widget at its
// sizes with made-up data, the dots and a swipe half-way, the editor's page strip; and the rules as "want" lines - the migration,
// a new install, swipes, a dot, the 4 x 3 grid's rules, the microphone (a made-up one) and a button group.
// ORBIT_SNAP_ONLY=pages: only this part.
static partial class Tools
{
    // a cover for the made-up song: a dusk gradient with a low sun (premultiplied, MediaInfo.ArtSize square)
    static uint[] FakeCover()
    {
        int n = MediaInfo.ArtSize;
        var px = new uint[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float t = (x + y) / (2f * (n - 1));
                uint c = t < 0.5f ? Theme.Mix(0xFF2B1B6B, 0xFFD9467D, t * 2) : Theme.Mix(0xFFD9467D, 0xFFF59E42, (t - 0.5f) * 2);
                float dx = x - n * 0.62f, dy = y - n * 0.66f;
                if (dx * dx + dy * dy < n * n * 0.04f) c = Theme.Mix(c, 0xFFFFE6B0, 0.85f);
                if (y > n * 0.78f) c = Theme.Mix(c, 0xFF120A2A, 0.7f);
                px[y * n + x] = c;
            }
        return px;
    }

    // a face like a busy own one (17 widgets on the 6 x 4 grid): the pages' first page in the pictures
    static List<WatchWidget> BusyFace()
    {
        var l = new List<WatchWidget>();
        void Put(string t, int x, int y, int w = 1, int h = 1) => l.Add(new WatchWidget { Type = t, X = x, Y = y, W = w, H = h });
        Put("time", 0, 0, 3, 2); Put("session", 3, 0); Put("volume", 4, 0, 2, 1); Put("battery", 3, 1); Put("dock", 4, 1, 2, 1);
        Put("media", 0, 2, 2, 1); Put("ram", 2, 2); Put("cpu", 3, 2); Put("room", 4, 2); Put("vram", 5, 2);
        string[] six = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
        for (int i = 0; i < six.Length; i++) Put(six[i], i, 3);
        return l;
    }

    // the pages' made-up data: a room of 12 (3 friends) in a world with a long name, three notifications, a song with its cover and
    // where in it, the last 30 s of frames with a spike over budget (and a dropped frame at its top)
    static int Cells(List<WatchWidget> l) => l?.Sum(w => w.W * w.H) ?? 0;

    internal static void PagesData(PanelData data, bool notes = true)
    {
        var room = new RoomWatcher.Snapshot { Version = 7, LogFound = true, InRoom = true, World = "The Black Cat \u2013 Midnight Rooftop Lounge (Extended)", VrcxFound = true, Friends = 120 };
        string[] names = { "Nova", "Mira", "Pixel Fox", "Tobi", "Moss", "Juniper", "Static", "Kite", "Orbit Fan 2000", "Lumen", "Quill", "Sable" };
        for (int i = 0; i < names.Length; i++)
            room.Here.Add(new RoomWatcher.Person { Id = $"usr_{i}", Name = names[i], Joined = DateTime.Now.AddMinutes(-3 * i - 1), Friend = names[i] is "Mira" or "Tobi" or "Kite" });
        data.Room = room;
        data.Notes = new NotificationHistory();
        if (notes)
        {
            data.Notes.Add("Discord", "New message", "Alex: 2 new messages", DateTime.Now.AddMinutes(-3).AddSeconds(-5));
            data.Notes.Add("Room", Lang.F("{0} is here", "Mira"), "The Black Cat", DateTime.Now.AddMinutes(-2).AddSeconds(-10));
            data.Notes.Add("Room", Lang.F("{0} joined", "Tobi"), "The Black Cat", DateTime.Now.AddSeconds(-20));
        }
        data.FakeMedia = (true, "Midnight City", "M83", true);
        data.FakeTimeline = (102, 243);
        data.FakeArt = FakeCover();
        data.VramTotalGB = 16;
        var rng = new Random(5);
        double t0 = 1000;
        for (int i = 0; i < VrStats.LongBuckets; i++)
        {
            float bump = i >= 236 && i <= 246 ? 9.5f * (1 - MathF.Abs(i - 241) / 5.5f) : 0;
            for (int f = 0; f < 9; f++)
            {
                float ms = 7.6f + 0.9f * MathF.Sin(i / 11f) + (float)rng.NextDouble() * 0.8f + bump;
                byte flag = (byte)(i == 241 && f == 4 ? 2 : ms > 11.1f ? 1 : 0);
                float interval = rng.NextDouble() < 0.02 || bump > 2 ? 13.5f : 11.1f;
                data.Vr.AddLong(t0 + i * 0.1 + f * 0.011, ms, flag, interval);
            }
        }
    }

    static int PagesRun(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir, SettingsView view, Ui ui, Surface surf)
    {
        var wcfg = cfg.Data.Wrist;
        var app0 = cfg.Data.Appearance;
        var culture0 = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = Tools.SnapCulture;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Name, wcfg.Pages, wcfg.Page, wcfg.Background);
        var media0 = (data.FakeMedia, data.FakeTimeline, data.FakeArt, data.Room, data.Notes, data.NightMode, data.DesktopsVisible);
        var vr0 = data.Vr;
        string qa = Path.Combine(dir, "qa");
        Directory.CreateDirectory(qa);
        PagesData(data);
        var vramTotal0 = data.VramTotalGB;
        // the CPU's last minute and the microphone's level, made up (the pictures do not depend on this PC)
        var st = wrist.Stats;
        for (int i = 0; i < SystemStats.History; i++) st.CpuHistory[i] = 0.1f + 0.05f * MathF.Sin(i / 2.3f) + 0.03f * MathF.Sin(i * 1.7f) + (i % 23 == 5 ? 0.12f : 0);
        st.CpuCount = SystemStats.History;
        st.CpuHead = 0;
        st.Cpu = 0.1f;
        st.RamUsedGB = 15.9f;
        st.RamTotalGB = 32f;
        bool hold0 = st.Hold;
        st.Hold = true;
        wrist.Mic.Peak = 0.2f;
        var actions = new List<string>();
        var onAction0 = wrist.OnAction;
        wrist.OnAction = a =>
        {
            actions.Add(a);
            if (a == "night") data.NightMode = !data.NightMode;
            if (a == "desktops") data.DesktopsVisible = !data.DesktopsVisible;
        };
        void Style(bool classic)
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = classic ? "classic" : "modern" };
            Theme.Apply(cfg.Data.Appearance);
        }
        void SetPages(params WatchPage[] pages)
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Widgets = pages[0].Widgets;
            wcfg.Grid = pages[0].Grid == "6x4" ? null : pages[0].Grid;
            wcfg.Name = pages[0].Name;
            wcfg.Pages = pages.Length > 1 ? pages.Skip(1).ToList() : null;
            wcfg.Page = 0;
        }
        static WatchPage P(string preset, string grid) => WatchPages.Make(preset, grid);
        void Shot(string file, string sub = null)
        {
            wrist.Settle();
            wrist.Draw(data);
            wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(sub ?? dir, file));
        }
        float S() => Theme.Classic ? 2 : 1;
        void Tap(Vector2 p)
        {
            wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
            wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
            wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        }
        Vector2 Centre(WatchWidget w)
        {
            var c = WatchWidgets.Rect(w, Theme.Classic, WatchPages.Grid(wcfg, wrist.PageNow));
            return new Vector2((c.X + c.Width / 2) * S(), (c.Y + c.Height / 2) * S());
        }
        // a drag with the trigger held, from a to b in steps (a frame each)
        void Drag(Vector2 a, Vector2 b, int steps = 8, string midShot = null)
        {
            wrist.PointerMove(Hand.Left, a); wrist.Draw(data);
            wrist.PointerButton(Hand.Left, PtrButton.Primary, true, a); wrist.Draw(data);
            for (int i = 1; i <= steps; i++)
            {
                wrist.PointerMove(Hand.Left, Vector2.Lerp(a, b, i / (float)steps));
                wrist.Draw(data);
                if (i == steps / 2 + 1 && midShot != null) wrist.Surf.SavePng(gpu, Path.Combine(dir, midShot));
            }
            wrist.PointerButton(Hand.Left, PtrButton.Primary, false, b); wrist.Draw(data);
            for (int i = 0; i < 40 && wrist.Animating; i++) { System.Threading.Thread.Sleep(15); wrist.Draw(data); }
            wrist.PointerLeave(Hand.Left);
            wrist.Draw(data);
        }

        // ---- 1. the pages in both styles ----
        foreach (bool classic in new[] { false, true })
        {
            Style(classic);
            string tag = classic ? "_95" : "";
            SetPages(P("vrchat", "6x4"), P("performance", "6x4"), P("everyday", "6x4"));
            string[] names = { "vrchat", "performance", "everyday" };
            for (int i = 0; i < 3; i++) { wcfg.Page = i; Shot($"pages_{names[i]}{tag}.png"); }
            SetPages(P("vrchat", "4x3"), P("performance", "4x3"), P("everyday", "4x3"));
            for (int i = 0; i < 3; i++) { wcfg.Page = i; Shot($"pages_{names[i]}_4x3{tag}.png"); }
            // a busy face as page 1 with the two new pages after it: the dots (95: the taskbar)
            SetPages(new WatchPage { Widgets = BusyFace(), Grid = "6x4" }, P("vrchat", "6x4"), P("performance", "6x4"));
            Shot($"pages_dots{tag}.png");
            data.FakeMedia = (false, null, null, false);
            data.FakeArt = null;
            data.Notes = new NotificationHistory();
            data.Room = new RoomWatcher.Snapshot { LogFound = true };
            SetPages(P("vrchat", "6x4"), P("performance", "6x4"));
            Shot($"pages_vrchat_empty{tag}.png");
            PagesData(data);
        }
        Style(false);

        // ---- 2. every new or changed widget at its sizes, made-up data, both styles ----
        var sheet = new List<(string label, string file, string grid, int w, int h)>();
        void One(string kind, string grid, int w, int h, string tag, Action setup = null, List<string> items = null)
        {
            var g = GridSpec.Of(grid);
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Pages = null;
            wcfg.Page = 0;
            wcfg.Grid = grid == "6x4" ? null : grid;
            wcfg.Widgets = new List<WatchWidget> { new() { Type = kind, X = 0, Y = 0, W = w, H = h, Items = items } };
            setup?.Invoke();
            string st = Theme.Classic ? "95" : "modern";
            string file = Path.Combine(qa, $"pages_{st}_{kind}_{grid}_{w}x{h}_{tag}.png");
            Shot(Path.GetFileName(file), qa);
            sheet.Add(($"{kind} {grid} {w}x{h} {tag}", file, grid, w, h));
            data.Vr = vr0;
            PagesData(data);
            wrist.Mic.Muted = false;
            data.NightMode = false;
            data.DesktopsVisible = true;
        }
        foreach (bool classic in new[] { false, true })
        {
            Style(classic);
            sheet.Clear();
            foreach (var (grid, sizes) in new[] { ("6x4", new[] { (1, 1), (2, 1) }), ("4x3", new[] { (1, 1), (2, 1) }) })
                foreach (var (w, h) in sizes)
                {
                    One("mic", grid, w, h, "live");
                    One("mic", grid, w, h, "muted", () => wrist.Mic.Muted = true);
                }
            foreach (var (grid, w, h) in new[] { ("6x4", 2, 1), ("6x4", 3, 1), ("6x4", 2, 2), ("4x3", 2, 1), ("4x3", 2, 2) })
            {
                One("notifications", grid, w, h, "3");
                One("notifications", grid, w, h, "0", () => data.Notes = new NotificationHistory());
            }
            var six = new List<string> { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
            foreach (var (grid, w, h) in new[] { ("6x4", 2, 1), ("6x4", 3, 1), ("6x4", 2, 2), ("6x4", 6, 1), ("4x3", 2, 1), ("4x3", 2, 2) })
            {
                One("buttons", grid, w, h, "off", null, new List<string> { "desktops", "theater", "night" });
                One("buttons", grid, w, h, "on", () => { data.NightMode = true; data.DesktopsVisible = true; }, new List<string> { "desktops", "theater", "night" });
                One("buttons", grid, w, h, "six", null, six);
            }
            One("room", "6x4", 3, 1, "12");
            One("room", "6x4", 3, 1, "out", () => data.Room = new RoomWatcher.Snapshot { LogFound = true });
            One("room", "4x3", 2, 1, "12");
            One("room", "4x3", 2, 1, "out", () => data.Room = new RoomWatcher.Snapshot { LogFound = true });
            One("room", "4x3", 1, 1, "12");
            foreach (var (w, h) in WatchWidgets.Find("media").Sizes)
            {
                One("media", "6x4", w, h, "art");
                One("media", "6x4", w, h, "noart", () => { data.FakeArt = null; data.FakeTimeline = null; data.FakeMedia = (true, "A Very Long Song Title That Goes On And On (Extended Club Remix)", "Somebody Featuring Somebody Else", false); });
                One("media", "6x4", w, h, "nothing", () => { data.FakeArt = null; data.FakeMedia = (false, null, null, false); });
            }
            foreach (var (w, h) in WatchWidgets.Find("graph").Sizes) One("graph", "6x4", w, h, "spike");
            One("cpu", "6x4", 1, 1, "a");
            One("battery", "6x4", 2, 1, "4");
            foreach (var (w, h) in new[] { (1, 1), (2, 1), (2, 2), (4, 1) })
            {
                One("media", "4x3", w, h, "art");
                One("media", "4x3", w, h, "noart", () => { data.FakeArt = null; data.FakeTimeline = null; data.FakeMedia = (true, "A Very Long Song Title That Goes On And On (Extended Club Remix)", "Somebody Featuring Somebody Else", false); });
                One("media", "4x3", w, h, "nothing", () => { data.FakeArt = null; data.FakeMedia = (false, null, null, false); });
            }
            foreach (var (w, h) in new[] { (2, 1), (2, 2), (4, 1) }) One("graph", "4x3", w, h, "spike");
            One("graph", "4x3", 2, 2, "none", () => data.Vr = new VrStats { AppFps = -1, DisplayHz = 90 });
            foreach (var (w, h) in new[] { (1, 1), (2, 1), (2, 2) }) One("battery", "4x3", w, h, "4");
            foreach (var kind in new[] { "cpu", "vram", "ram", "time" })
                foreach (var (w, h) in WatchWidgets.Sizes43(WatchWidgets.Find(kind))) One(kind, "4x3", w, h, "a");
            PagesSheet(Path.Combine(qa, classic ? "pages_widgets_95.png" : "pages_widgets.png"), sheet);
        }
        Style(false);

        // ---- 3. the rules ----
        var opts = new JsonSerializerOptions { WriteIndented = true };
        {
            // an older config with its own face: the two pages once, a copy first, page 1 as it was
            string md = Path.Combine(dir, "migrate");
            if (Directory.Exists(md)) Directory.Delete(md, true);
            Directory.CreateDirectory(md);
            string mp = Path.Combine(md, "config.json");
            var old = new Config();
            old.Wrist.Face = "custom";
            old.Wrist.Widgets = BusyFace();
            old.General.LastVersion = "1.11.0";
            File.WriteAllText(mp, JsonSerializer.Serialize(old, opts));
            var s1 = new ConfigStore(mp);
            string w0 = JsonSerializer.Serialize(s1.Data.Wrist.Widgets, opts);
            bool m1 = WatchPages.Migrate(s1);
            s1.SaveIfDue(force: true);
            var s2 = new ConfigStore(mp);
            bool m2 = WatchPages.Migrate(s2);
            string w1 = JsonSerializer.Serialize(s2.Data.Wrist.Widgets, opts);
            int baks = Directory.GetFiles(md, "config.json.bak-pages-*").Length;
            string pn = string.Join(", ", s2.Data.Wrist.Pages?.Select(p => $"{p.Name} {p.Grid}") ?? Array.Empty<string>());
            Log.Info($"pages: migration of a config with its own face: migrated {m1} (want True), pages after page 1: {s2.Data.Wrist.Pages?.Count ?? 0} = {pn} " +
                     $"(want 2 = VRChat 6x4, Performance 6x4), again at the next start {m2} (want False), page 1 byte-identical {w0 == w1} (want True), " +
                     $"copies of the config {baks} (want 1), face {s2.Data.Wrist.Face} (want custom), page 1's grid {s2.Data.Wrist.Grid ?? "6x4"} (want 6x4)");
            s2.Data.Wrist.Pages = null;
            s2.MarkDirty();
            s2.SaveIfDue(force: true);
            var s3 = new ConfigStore(mp);
            bool m3 = WatchPages.Migrate(s3);
            Log.Info($"pages: both pages deleted, the next start: migrated {m3} (want False), pages {s3.Data.Wrist.Pages?.Count ?? 0} (want 0)");
            // a config that never had a custom face: its face stays, the pages wait for it
            string mp2 = Path.Combine(md, "plain.json");
            File.WriteAllText(mp2, JsonSerializer.Serialize(new Config { General = new GeneralConfig { LastVersion = "1.11.0" } }, opts));
            var s4 = new ConfigStore(mp2);
            WatchPages.Migrate(s4);
            Log.Info($"pages: a config with the full face: face {s4.Data.Wrist.Face} (want full), page 1 {(s4.Data.Wrist.Widgets == null ? "not set up" : "set up")} " +
                     $"(want not set up), pages {s4.Data.Wrist.Pages?.Count ?? 0} (want 2)");
            // a new install: no file at all
            string np = Path.Combine(md, "new", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(np));
            var s5 = new ConfigStore(np);
            WatchPages.Migrate(s5);
            var w5 = s5.Data.Wrist;
            Log.Info($"pages: a new install: face {w5.Face} (want custom), page 1 '{WatchPages.Name(w5, 0)}' on {WatchPages.GridId(w5, 0)} (want 'VRChat' on 6x4), " +
                     $"then {string.Join(", ", w5.Pages?.Select(p => $"'{p.Name}' on {p.Grid}") ?? Array.Empty<string>())} (want 'Performance' on 6x4), " +
                     $"widgets {w5.Widgets?.Count} + {w5.Pages?[0].Widgets.Count} (want 7 + 10), every cell used {Cells(w5.Widgets)} + {Cells(w5.Pages?[0].Widgets)} (want 24 + 24)");
        }
        {
            // an update (OrbitApp.NoteVersion -> ConfigStore.KeepBackup): the config as the old version left it kept as
            // config.backup-<it>.json, byte for byte - the .bak alone is the new version's own after two saves; the same jump again keeps
            // the copy there as it is; a version that is no file name stays in the folder; a new install has nothing to copy
            string ud = Path.Combine(dir, "update");
            if (Directory.Exists(ud)) Directory.Delete(ud, true);
            Directory.CreateDirectory(ud);
            string up = Path.Combine(ud, "config.json");
            var old = new Config();
            old.General.LastVersion = "1.14.1";
            old.Wrist.Face = "custom";
            old.Wrist.Widgets = BusyFace();
            File.WriteAllText(up, JsonSerializer.Serialize(old, opts));
            byte[] before = File.ReadAllBytes(up);
            var u1 = new ConfigStore(up);
            string k1 = u1.KeepBackup("1.14.1");
            u1.Data.General.LastVersion = "1.15.0";
            u1.MarkDirty(); u1.SaveIfDue(force: true);
            u1.Data.Wrist.Widgets = null;
            u1.MarkDirty(); u1.SaveIfDue(force: true);
            bool copied = k1 != null && Path.GetFileName(k1) == "config.backup-1.14.1.json" && File.ReadAllBytes(k1).AsSpan().SequenceEqual(before);
            bool bakNew = !File.ReadAllBytes(up + ".bak").AsSpan().SequenceEqual(before);
            string k2 = new ConfigStore(up).KeepBackup("1.14.1");
            bool kept = k2 == null && File.ReadAllBytes(k1).AsSpan().SequenceEqual(before);
            string k3 = new ConfigStore(up).KeepBackup(@"..\..\1.13");
            bool inside = k3 != null && Path.GetFullPath(Path.GetDirectoryName(k3)) == Path.GetFullPath(ud);
            string k4 = new ConfigStore(Path.Combine(ud, "new", "config.json")).KeepBackup("1.14.1");
            int copies = Directory.GetFiles(ud, "config.backup-*").Length;
            Log.Info($"update: the old config kept as {Path.GetFileName(k1)}, byte for byte {copied} (want True) - the .bak no longer the old file after two " +
                     $"saves {bakNew} (want True); the same jump again: the copy kept as it was {kept} (want True); a version with path characters - " +
                     $"'{Path.GetFileName(k3)}' in the config's folder {inside} (want True); a new install: no copy {k4 == null} (want True); copies {copies} (want 2)");
        }
        {
            // the 4 x 3 grid's rules
            var g = GridSpec.G43;
            string Z((int W, int H)? s) => s is { } v ? $"{v.W}x{v.H}" : "none";
            var vr = WatchWidgets.PagePreset("vrchat", g);
            var cleaned = WatchWidgets.Clean(new List<WatchWidget>(vr), g);
            var busy = BusyFace();
            var busyClean = WatchWidgets.Clean(new List<WatchWidget>(busy), GridSpec.G64Paged);
            var bad = new List<WatchWidget>
            {
                new() { Type = "time", X = 0, Y = 0, W = 3, H = 2 }, new() { Type = "cpu", X = 4, Y = 0 }, new() { Type = "ram", X = 1, Y = 1 },
                new() { Type = "graph", X = 0, Y = 2, W = 3, H = 1 }, new() { Type = "media", X = 2, Y = 2, W = 3, H = 1 },
            };
            var bc = WatchWidgets.Clean(bad, g);
            Log.Info($"pages: 4x3 Clean keeps the VRChat page {cleaned.Count == vr.Count} (want True), a 6x4 page cleaned as 6x4 keeps all {busyClean.Count == busy.Count} " +
                     $"(want True); a hand-made 4x3 list: {string.Join(" ", bc.Select(w => $"{w.Type}{w.W}x{w.H}@{w.X},{w.Y}"))} (want time2x2@0,0 graph2x1@0,2)");
            var noMic = vr.Where(w => w.Type != "mic").ToList();
            var time = vr.First(w => w.Type == "time");
            Log.Info($"pages: 4x3 Free: a taken cell {WatchWidgets.Free(vr, null, 2, 1, 1, 1, g)} (want False), the mic's cell once it is gone " +
                     $"{WatchWidgets.Free(noMic, null, 2, 1, 1, 1, g)} (want True), a 6x4 column {WatchWidgets.Free(new List<WatchWidget>(), null, 4, 0, 1, 1, g)} (want False); " +
                     $"Step: the time L smaller {Z(WatchWidgets.Step(vr, time, -1, false, g))} (want 2x1), larger {Z(WatchWidgets.Step(vr, time, 1, false, g))} (want none); " +
                     $"SnapSize time to 3x3 {Z(WatchWidgets.SnapSize(WatchWidgets.Find("time"), 3, 3, false, g))} (want 2x2), graph to 4x1 " +
                     $"{Z(WatchWidgets.SnapSize(WatchWidgets.Find("graph"), 4, 1, false, g))} (want 4x1); Add a CPU to an empty page " +
                     $"{(WatchWidgets.Add(new List<WatchWidget>(), WatchWidgets.Find("cpu"), -1, -1, false, g) is { } a1 ? $"{a1.W}x{a1.H}@{a1.X},{a1.Y}" : "none")} (want 1x1@0,0), " +
                     $"to the full VRChat page {(WatchWidgets.Add(new List<WatchWidget>(vr), WatchWidgets.Find("cpu"), -1, -1, false, g) == null ? "none" : "placed")} (want none)");
        }
        // swipes, the dots, a tap that is not a swipe
        foreach (bool classic in new[] { false, true })
        {
            Style(classic);
            string tag = classic ? "_95" : "";
            SetPages(new WatchPage { Widgets = BusyFace(), Grid = "6x4" }, P("vrchat", "6x4"), P("performance", "6x4"));
            wrist.Settle();
            wrist.Draw(data);
            float y = 160 * (classic ? 1 : 1);
            Drag(new Vector2(520, y), new Vector2(200, y), 8, classic ? null : "pages_swipe_mid.png");
            int afterLeft = wrist.PageNow;
            Drag(new Vector2(200, y), new Vector2(560, y));
            int afterRight = wrist.PageNow;
            Drag(new Vector2(560, y), new Vector2(620, y));
            int edge = wrist.PageNow;
            // a short drag that starts on the night button: no page change, no tap
            var night = wcfg.Widgets.First(w => w.Type == "night");
            actions.Clear();
            var nc = Centre(night);
            Drag(nc, nc + new Vector2(-60, 0), 4);
            int shortPage = wrist.PageNow;
            string shortActs = actions.Count == 0 ? "none" : string.Join(",", actions);
            actions.Clear();
            Tap(nc);
            string tapActs = string.Join(",", actions);
            Log.Info($"pages{tag}: swipe left -> page {afterLeft + 1} (want 2), right -> page {afterRight + 1} (want 1), right again at the first -> page {edge + 1} (want 1); " +
                     $"a short drag on a button -> page {shortPage + 1} (want 1), actions {shortActs} (want none); a tap on it -> {tapActs} (want night)");
            data.NightMode = false;
            if (!classic)
            {
                Tap(WristPanel.DotPoint(3, 2));
                int dot = wrist.PageNow;
                for (int i = 0; i < 40 && wrist.Animating; i++) { System.Threading.Thread.Sleep(15); wrist.Draw(data); }
                Tap(new Vector2(320 - 27 - 18, 435));
                Log.Info($"pages: a tap on the third dot -> page {dot + 1} (want 3), on the strip's left half -> page {wrist.PageNow + 1} (want 2)");
            }
            else
            {
                Tap(WristPanel.TaskbarPoint(3, 2));
                int tb = wrist.PageNow;
                Shot("pages_taskbar_95.png");
                Log.Info($"pages_95: a click on the taskbar's third button -> page {tb + 1} (want 3)");
            }
            // the stick pushed sideways (Erik 10-23): a page a push, none past the last
            wcfg.Page = 0;
            wrist.Settle();
            wrist.Draw(data);
            var mid = new Vector2(320, 160);
            var steps = new List<int>();
            foreach (int sd in new[] { 1, 1, 1, -1 }) { wrist.PointerStep(Hand.Left, sd, mid); steps.Add(wrist.PageNow + 1); }
            Want($"pages{tag}: the stick right, right, right, left -> pages {string.Join(", ", steps)} (want 2, 3, 3, 2)", string.Join(",", steps) == "2,3,3,2");
            wcfg.Page = 0;
        }
        Style(false);
        // the microphone (a made-up one) and a button group
        foreach (bool classic in new[] { false, true })
        {
            Style(classic);
            string tag = classic ? "_95" : "";
            SetPages(P("vrchat", "6x4"), P("performance", "6x4"));
            wrist.Settle();
            wrist.Draw(data);
            wrist.Mic.Muted = false;
            var mic = wcfg.Widgets.First(w => w.Type == "mic");
            Tap(Centre(mic));
            bool m1 = wrist.Mic.Muted;
            Shot($"pages_mic_muted{tag}.png");
            Tap(Centre(mic));
            bool m2 = wrist.Mic.Muted;
            Log.Info($"pages{tag}: a tap on the microphone (made-up) muted it {m1} (want True), a second tap {m2} (want False), a real device opened {!wrist.Mic.Fake} (want False)");
            wcfg.Page = 1;
            wrist.Settle();
            wrist.Draw(data);
            var grp = wcfg.Pages[0].Widgets.First(w => w.Type == "buttons");
            var gr = WatchWidgets.Rect(grp, classic, WatchPages.Grid(wcfg, 1));
            // its third tile: night
            var p3 = new Vector2((gr.Right - gr.Width / 6) * S(), (gr.Y + gr.Height / 2) * S());
            data.NightMode = false;
            actions.Clear();
            Tap(p3);
            Shot($"pages_group_on{tag}.png");
            string acts = string.Join(",", actions);
            bool on = data.NightMode;
            Tap(p3);
            Log.Info($"pages{tag}: a tap on the button group's third tile -> {acts} (want night), night on {on} (want True), again off {!data.NightMode} (want True)");
            data.NightMode = false;
        }
        Style(false);

        // ---- 4. the editor's page strip (needs the settings view) ----
        if (view != null) PagesEditorShots(wrist, data, cfg, painter, gpu, dir, view, ui, surf);

        wrist.OnAction = onAction0;
        st.Hold = hold0;
        (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Name, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        (data.FakeMedia, data.FakeTimeline, data.FakeArt, data.Room, data.Notes, data.NightMode, data.DesktopsVisible) = media0;
        data.VramTotalGB = vramTotal0;
        data.Vr = vr0;
        cfg.Data.Appearance = app0;
        Theme.Apply(app0);
        CultureInfo.CurrentCulture = culture0;
        Log.Info("pages: done");
        return 0;
    }

    // qa/pages_widgets*.png: the widget pictures cut to their cards, side by side, each with its name
    static void PagesSheet(string path, List<(string label, string file, string grid, int w, int h)> items)
    {
        const int Width = 1900, Gap = 12, Label = 18;
        var place = new List<(int x, int y, System.Drawing.Rectangle crop)>();
        int x = Gap, y = Gap, rowH = 0;
        string last = null;
        bool classic = path.Contains("_95");
        foreach (var it in items)
        {
            var c = WatchWidgets.CellRect(0, 0, it.w, it.h, classic, GridSpec.Of(it.grid));
            float k = classic ? 2 : 1;
            var crop = System.Drawing.Rectangle.FromLTRB((int)(c.Left * k) - 4, (int)(c.Top * k) - 4, (int)(c.Right * k) + 4, (int)(c.Bottom * k) + 4);
            string group = it.label.Split(' ')[0];
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
            g.DrawImage(src, new System.Drawing.Rectangle(px, py + Label, crop.Width, crop.Height), crop, System.Drawing.GraphicsUnit.Pixel);
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // The editor with pages: the strip over the watch, the new page's choices, a page added and one removed (two clicks), a page's
    // grid switched and back, a button group's buttons; the 95 editor's page row
    static void PagesEditorShots(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir, SettingsView view, Ui ui, Surface surf)
    {
        var wcfg = cfg.Data.Wrist;
        var ptr = new PointerState();
        int tab0 = view.Tab;
        view.Tab = SettingsView.TabWrist;
        void SetPages3()
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Widgets = BusyFace();
            wcfg.Grid = null;
            wcfg.Name = null;
            wcfg.Pages = new List<WatchPage> { WatchPages.Make("vrchat", "6x4"), WatchPages.Make("performance", "6x4") };
            wcfg.Page = 1;
        }
        string Names() => string.Join(", ", Enumerable.Range(0, WatchPages.Count(wcfg)).Select(i => $"{WatchPages.Name(wcfg, i)} {WatchPages.GridId(wcfg, i)}"));
        // ---- modern ----
        cfg.Data.Appearance = new AppearanceConfig();
        Theme.Apply(cfg.Data.Appearance);
        SetPages3();
        view.EditingWatch = true;
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
        void Settled(string file)
        {
            ptr.Leave();
            wrist.Settle();
            ui.ResetAnimations();
            FrameM();
            FrameM();
            surf.SavePng(gpu, Path.Combine(dir, file));
        }
        var pv = SettingsView.EditorPrev;
        Vector2 Cell64(int cx, int cy)
        {
            var c = WatchWidgets.CellRect(cx, cy, 1, 1, false, GridSpec.G64Paged);
            return new(pv.X + (c.X + c.Width / 2) * pv.Width / WristPanel.TW, pv.Y + (c.Y + c.Height / 2) * pv.Height / WristPanel.TH);
        }
        void ClickV(Vector2 v) => ClickM(v.X, v.Y);
        wrist.Settle();
        ui.ResetAnimations();
        FrameM();
        Settled("settings_wrist_pages.png");
        // the editing tools on the VRChat page (page 2): the room smaller, its style; the microphone moved, removed, added again
        var vrl = wcfg.Pages[0].Widgets;
        var room = vrl.First(w => w.Type == "room");
        ClickV(Cell64(4, 0));
        ClickM(724 + 40, 138 + 17);               // Smaller
        string roomSize = $"{room.W}x{room.H}";
        ClickM(724 + 2 * 66.5f + 29.75f, 206 + 23);      // the style chips: Solid
        string roomStyle = room.Style;
        var mic = vrl.First(w => w.Type == "mic");
        var m0 = Cell64(3, 1);
        var m1 = Cell64(5, 0);
        ptr.Move(m0.X * 1.5f, m0.Y * 1.5f); FrameM(); ptr.Press(); FrameM();
        ptr.Move(m1.X * 1.5f, m1.Y * 1.5f); FrameM(); ptr.Release(); FrameM();
        string micAt = $"{mic.X},{mic.Y}";
        ClickV(Cell64(5, 0));
        ClickM(724 + 2 * (244 / 3f + 8) + 40, 138 + 17);  // Remove
        bool micGone = !vrl.Contains(mic);
        view.PaletteCat = WatchWidgets.CategoryOf("mic");                       // the palette shows one category
        int pi = Array.IndexOf(WatchWidgets.PaletteFor(view.PaletteCat, null), WatchWidgets.Find("mic"));
        ClickM(222 + pi % 6 * 81 + 38, SettingsView.PaletteY + pi / 6 * SettingsView.PalettePitch + 13);
        var mic2 = vrl.FirstOrDefault(w => w.Type == "mic");
        Log.Info($"editor pages: on the VRChat page the room Smaller -> {roomSize} (want 2x1), its style {roomStyle} (want solid); the microphone " +
                 $"moved to {micAt} (want 5,0), removed {micGone} (want True), added back from the palette at {(mic2 == null ? "none" : $"{mic2.X},{mic2.Y}")} (want 5,0)");
        Settled("settings_wrist_pages_edited.png");
        room.Style = null;
        room.W = 3;
        if (mic2 != null) { mic2.X = 3; mic2.Y = 1; }
        ClickM(1060, 600);                        // beside everything: nothing selected
        // the first tab: page 1 shown (and edited)
        ClickM(222 + 40, 92);
        int tabbed = wrist.PageNow;
        // add a page: the choices, Everyday on 4 x 3
        ClickM(SettingsView.AddPageMidX(WatchPages.Count(wcfg)), 92);
        Settled("settings_wrist_addpage.png");
        ClickM(724 + 134 + 63, 147);              // Everyday
        ClickM(724 + 195, 257);                   // 4 x 3
        ClickM(854, 312);                         // Add page
        Log.Info($"editor pages: the first tab showed page {tabbed + 1} (want 1); added Everyday on 4x3: {Names()} " +
                 $"(want Page 1 6x4, VRChat 6x4, Performance 6x4, Everyday 4x3), shown {wrist.PageNow + 1} (want 4)");
        Settled("settings_wrist_pages_added.png");
        // remove it: the first click only asks
        ClickM(854, 167);
        int afterOne = WatchPages.Count(wcfg);
        Settled("settings_wrist_removepage.png");
        ClickM(854, 167);
        Log.Info($"editor pages: Remove once -> {afterOne} pages (want 4), twice -> {WatchPages.Count(wcfg)} (want 3), shown {wrist.PageNow + 1} (want 3)");
        // the Performance page on 4 x 3 and straight back: as it was
        string before = JsonSerializer.Serialize(wcfg.Pages[1].Widgets);
        ClickM(724 + 195, 125);
        string mid = WatchPages.GridId(wcfg, 2);
        Settled("settings_wrist_grid43.png");
        ClickM(724 + 65, 125);
        Log.Info($"editor pages: Performance on 4x3 -> {mid} (want 4x3), back on 6x4 as it was {JsonSerializer.Serialize(wcfg.Pages[1].Widgets) == before} (want True)");
        // its button group selected: the buttons under the watch; Keyboard added
        ClickV(Cell64(0, 3));
        var grp = wcfg.Pages[1].Widgets.First(w => w.Type == "buttons");
        ClickM(222 + 58, SettingsView.PaletteY + 2 + 17);
        Log.Info($"editor pages: the button group's buttons {string.Join(" ", grp.Items)} (want desktops theater night keyboard)");
        Settled("settings_wrist_group.png");
        grp.Items.Remove("keyboard");
        view.EditingWatch = false;
        view.Tab = SettingsView.TabWrist;
        // ---- 95 ----
        cfg.Data.Appearance = new AppearanceConfig { Style = "classic" };
        Theme.Apply(cfg.Data.Appearance);
        SetPages3();
        view.EditingWatch = true;
        void Frame95() { wrist.Draw(data); view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal); }
        void Click95(float x, float y) { ptr.Move(x * 2, y * 2); Frame95(); ptr.Press(); Frame95(); ptr.Release(); Frame95(); Frame95(); }
        ui.ResetAnimations();
        Frame95();
        ptr.Leave();
        Frame95();
        surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_pages_95.png"));
        Click95(188 + 44, 326 + 11);              // New page (from: Full)
        Log.Info($"95 editor pages: New page -> {Names()} (want ..., Full 6x4), shown {wrist.PageNow + 1} (want 4)");
        ptr.Leave();
        Frame95();
        surf.SavePng(gpu, Path.Combine(dir, "settings_wrist_pages_added_95.png"));
        Click95(280 + 43, 326 + 11);
        Click95(280 + 43, 326 + 11);
        Log.Info($"95 editor pages: Remove page twice -> {WatchPages.Count(wcfg)} pages (want 3)");
        view.EditingWatch = false;
        view.Tab = tab0;
        cfg.Data.Appearance = new AppearanceConfig();
        Theme.Apply(cfg.Data.Appearance);
    }
}
