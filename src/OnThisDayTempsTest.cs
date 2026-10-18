using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Orbit;

// --snapshot's part for On this day (Sys/PhotoMemories.cs, WristMemory.cs) and the Temperatures (Sys/Temps.cs, WristTemps.cs), on
// made-up photos (PhotoFake's scenes, invented worlds and people; a folder of them written into the snapshot's directory) and made-up
// readings only - VRChat's real folder and the real sensors are never touched by it (--tempstest reads the sensors, nothing else).
static class OnThisDayTempsTools
{
    // made-up photos of this day: 1 year ago (two), 3 years ago (the nearest day's)
    public static PhotoWatcher.Snapshot Memories()
    {
        var s = PhotoFake.Snap(4);
        for (int i = 0; i < s.Recent.Count; i++)
        {
            var p = s.Recent[i];
            p.Years = i < 2 ? 1 : 3;
            p.OtherDay = i >= 2;
            p.Taken = p.Taken.AddYears(-p.Years).AddDays(p.OtherDay ? -2 : 0);
            p.Fresh = false;
        }
        return s;
    }

    // made-up readings: a CPU at 61, a GPU at 66 (hotspot 79, fan 1240 rpm), 10 minutes of a game warming them up
    public static TempReport Temps() => TempReport.Fake(61, 66, 79, 1240, TempReport.History,
        i => (48 + 13 * MathF.Min(1, i / 120f) + 2 * MathF.Sin(i / 7f), 45 + 21 * MathF.Min(1, i / 150f) + 1.5f * MathF.Sin(i / 5f), 55 + 24 * MathF.Min(1, i / 150f)));

    // nothing read: the CPU without administrator, the GPU not readable
    public static TempReport None() => TempReport.Fake(float.NaN, float.NaN, float.NaN, float.NaN, 0, i => default);

    static bool Want(string line, bool ok) => Tools.Want(line, ok);

    public static void DataChecks(string dir)
    {
        // ---- On this day's picking, on a folder of made-up photos (a fixed day: 2026-10-17) ----
        string root = Path.Combine(dir, "memory_folder");
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            var png = PhotoFake.Png(2, 64, 36);
            void Put(DateTime t, bool inRoot = false)
            {
                string d = inRoot ? root : Path.Combine(root, t.ToString("yyyy-MM"));
                Directory.CreateDirectory(d);
                File.WriteAllBytes(Path.Combine(d, $"VRChat_{t:yyyy-MM-dd_HH-mm-ss.fff}_1920x1080.png"), png);
            }
            var today = new DateTime(2026, 10, 17);
            Put(new DateTime(2025, 10, 17, 20, 15, 0));                 // a year ago, that day
            Put(new DateTime(2025, 10, 18, 1, 30, 0));                  // ... past midnight: still that day
            Put(new DateTime(2025, 10, 16, 21, 0, 0));                  // the day before: not wanted, that day has its own
            Put(new DateTime(2024, 10, 15, 19, 0, 0));                  // two years ago: none that day, the nearest 2 days before
            Put(new DateTime(2024, 10, 19, 19, 0, 0));                  // ... and 2 days after (the earlier wins)
            Put(new DateTime(2023, 10, 11, 19, 0, 0));                  // three years ago: 6 days off - none
            Put(new DateTime(2022, 10, 17, 18, 0, 0), true);            // four years ago, in the folder itself (before the month folders)
            Put(new DateTime(2026, 10, 17, 12, 0, 0));                  // today's: not a memory
            var f = PhotoMemories.Find(root, today);
            string got = string.Join(" ", f.Select(p => $"{p.Years}y{p.Taken:MMdd-HHmm}{(p.OtherDay ? "~" : "")}"));
            const string want = "1y1018-0130 1y1017-2015 2y1015-1900~ 4y1017-1800";
            Want($"memory pick: exact day, past midnight, nearest earlier day, a year with none, the old folder: {got} (want {want})", got == want);
            var none = PhotoMemories.Find(root, new DateTime(2026, 4, 2));
            Want($"memory pick: a day without photos in any year gives {none.Count} (want 0)", none.Count == 0);
            var later = PhotoMemories.Find(root, new DateTime(2026, 10, 20));
            Want($"memory pick: 3 days later finds a year ago's nearest day {(later.Count > 0 ? $"{later[0].Years}y{later[0].Taken:MMdd}{(later[0].OtherDay ? "~" : "")}" : "none")} " +
                 "(want 1y1018~)", later.Count > 0 && later[0].Years == 1 && later[0].Taken.Month == 10 && later[0].Taken.Day == 18 && later[0].OtherDay);
            // the finder on its own thread: the first photo's thumbnail made, the others when wanted
            using var m = new PhotoMemories { FolderOverride = root, NowOverride = new DateTime(2026, 10, 17, 14, 0, 0) };
            m.Active = true;
            PhotoWatcher.Snapshot s = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 5 && !((s = m.Get()).Ready && s.Recent.Count > 0 && s.Recent[0].Thumb != null)) System.Threading.Thread.Sleep(50);
            bool first = s.Recent.Count == 4 && s.Recent[0].Thumb != null && s.Recent[1].Thumb == null;
            m.Want(s.Recent[2].Path);
            while (sw.Elapsed.TotalSeconds < 8 && (s = m.Get()).Recent[2].Thumb == null) System.Threading.Thread.Sleep(50);
            bool wanted = s.Recent[2].Thumb != null;
            m.Active = false;
            Want($"memory finder: 4 photos, the first's thumbnail at once {first}, another made when wanted {wanted} -", first && wanted);
        }
        catch (Exception e) { Log.Error("memory checks: " + e); Want("memory checks: ran to the end", false); }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }

        // ---- the temperatures: the sources' numbers read from made-up buffers, "–" when not read ----
        var words = new int[1 + 2 * 256];
        void S(int i, int v) { words[1 + 2 * i] = 1; words[2 + 2 * i] = v; }
        S(8, 39); S(27, 42); S(14, 0);
        var (edge, hot, fan) = AdlGpu.Parse(words);
        Array.Clear(words);
        S(28, 51);
        var (gfx, hot2, fan2) = AdlGpu.Parse(words);
        Want($"temps ADL: edge {edge} hotspot {hot} fan {fan} (want 39 42 0); no edge sensor: the GFX {gfx} (want 51), no hotspot / fan NaN {float.IsNaN(hot2) && float.IsNaN(fan2)} -",
             edge == 39 && hot == 42 && fan == 0 && gfx == 51 && float.IsNaN(hot2) && float.IsNaN(fan2));
        var kb = new byte[64];
        BitConverter.TryWriteBytes(kb.AsSpan(56), 390u);
        var (kt, kf) = KmtGpu.Parse(kb);
        var kb0 = new byte[64];
        var (kt0, _) = KmtGpu.Parse(kb0);
        Want($"temps D3DKMT: 390 tenths is {kt} °C (want 39), fan 0 is unknown {float.IsNaN(kf)}, no temperature NaN {float.IsNaN(kt0)} -",
             kt == 39 && float.IsNaN(kf) && float.IsNaN(kt0));
        uint raw = (uint)(45.625f / 0.125f) << 21, rawSel = ((uint)((61 + 49) / 0.125f) << 21) | 0x80000;
        string tidy = TempSensors.Tidy("AMD Ryzen 7 5700X3D 8-Core Processor           ");
        Want($"temps Tctl: {PawnCpu.Decode(raw)} (want 45.625), with RANGE_SEL {PawnCpu.Decode(rawSel)} (want 61); 2700X offset {PawnCpu.Offset("AMD Ryzen 7 2700X")} " +
             $"(want -10), 5700X3D {PawnCpu.Offset("AMD Ryzen 7 5700X3D")} (want 0); name '{tidy}' (want 'AMD Ryzen 7 5700X3D')",
             PawnCpu.Decode(raw) == 45.625f && PawnCpu.Decode(rawSel) == 61 && PawnCpu.Offset("AMD Ryzen 7 2700X") == -10 &&
             PawnCpu.Offset("AMD Ryzen 7 5700X3D") == 0 && tidy == "AMD Ryzen 7 5700X3D");

        // ---- the hot alert's timing ----
        var a = new Alerts();
        int Run(double from, double to, float gpu, float cpu, bool valid = true)
        {
            int fired = 0;
            for (double t = from; t <= to + 1e-9; t += 0.5) if (a.Hot(t, valid, gpu, cpu, 85) != 0) fired++;
            return fired;
        }
        int early = Run(0, 29, 86, 50);
        int at30 = Run(29.5, 31, 86, 50);
        int again = Run(31.5, 120, 88, 50);
        int hover = Run(120.5, 125, 82, 50) + Run(125.5, 160, 86, 50);
        int cooled = Run(160.5, 162, 79, 50) + Run(162.5, 200, 50, 91);
        int broken = Run(200.5, 260, 50, 50) + Run(260.5, 280, 87, 50) + Run(280.5, 281, 87, 50, false) + Run(281.5, 305, 87, 50);
        Want($"temps alert: 29 s at 86 °C {early} card(s) (want 0); at 30 s {at30} (want 1); still hot {again} (want 0); 82 °C then hot again {hover} (want 0); " +
             $"cooled to 79 °C, then the CPU at 91 for 30 s {cooled} (want 1); the headset off in the middle restarts the 30 s {broken} (want 0)",
             early == 0 && at30 == 1 && again == 0 && hover == 0 && cooled == 1 && broken == 0);

        // ---- the settings in the config: written only once set, read back ----
        var opts = new JsonSerializerOptions { WriteIndented = true };
        var def = new AlertsConfig();
        var set = new AlertsConfig { Hot = true, HotC = 90 };
        var back = JsonSerializer.Deserialize<AlertsConfig>(JsonSerializer.Serialize(set, opts), opts);
        string defJson = JsonSerializer.Serialize(def, opts);
        Want($"temps alert settings: the defaults write no hot {!defJson.Contains("Hot")} and are the defaults {def.IsDefault}; on at 90 °C " +
             $"reads back {back.Hot && back.HotLimit == 90}, is not the defaults {!set.IsDefault} -",
             !defJson.Contains("Hot") && def.IsDefault && back.Hot && back.HotLimit == 90 && !set.IsDefault);
    }

    // per style: both widgets on a face and opened; the checks that they show what they should
    public static readonly List<(string tag, string face, string mem, string temps)> Preview = new();

    static List<WatchWidget> Face() => new()
    {
        new() { Type = "memory", X = 0, Y = 0, W = 2, H = 2 }, new() { Type = "memory", X = 2, Y = 0, W = 2, H = 1 }, new() { Type = "memory", X = 4, Y = 0, W = 1, H = 1 },
        new() { Type = "temps", X = 2, Y = 1, W = 2, H = 2 }, new() { Type = "temps", X = 4, Y = 1, W = 2, H = 1 }, new() { Type = "temps", X = 5, Y = 0, W = 1, H = 1 },
        new() { Type = "time", X = 0, Y = 2, W = 2, H = 1 }, new() { Type = "battery", X = 4, Y = 2, W = 2, H = 1 },
        new() { Type = "keyboard", X = 0, Y = 3 }, new() { Type = "desktops", X = 1, Y = 3 }, new() { Type = "recenter", X = 2, Y = 3 },
        new() { Type = "layout", X = 3, Y = 3 }, new() { Type = "night", X = 4, Y = 3 }, new() { Type = "settings", X = 5, Y = 3 },
    };

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var (m0, t0) = (data.Memories, data.Temps);
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            wcfg.Widgets = Face();
            data.Memories = Memories();
            data.Temps = Temps();
            wrist.Settle();
            wrist.Draw(data); wrist.Draw(data);
            string face = Path.Combine(dir, $"wrist_onthisday_temps{tag}.png");
            wrist.Surf.SavePng(gpu, face);
            bool memShown = wrist.MemoryWidgetShown == data.Memories.Recent[0].Path;
            string temps = wrist.TempsShown(data);
            // On this day opened: the arrow steps to the one before (the other year's)
            wrist.OpenForTest("memory");
            wrist.Draw(data); wrist.Draw(data);
            string mem = Path.Combine(dir, $"wrist_open_memory{tag}.png");
            wrist.Surf.SavePng(gpu, mem);
            bool memOpen = wrist.MemoryOpenShown == data.Memories.Recent[0].Path;
            var prev = wrist.ControlPoint(data, Theme.Classic ? "wph95p" : "wphprev");
            if (prev is { } pp)
            {
                wrist.PointerMove(Hand.Left, pp); wrist.Draw(data);
                wrist.PointerButton(Hand.Left, PtrButton.Primary, true, pp); wrist.Draw(data);
                wrist.PointerButton(Hand.Left, PtrButton.Primary, false, pp); wrist.Draw(data);
                wrist.PointerLeave(Hand.Left); wrist.Draw(data);
            }
            bool stepped = wrist.MemoryOpenShown == data.Memories.Recent[1].Path;
            wrist.CloseForTest();
            wrist.OpenForTest("temps");
            wrist.Draw(data); wrist.Draw(data);
            string tp = Path.Combine(dir, $"wrist_open_temps{tag}.png");
            wrist.Surf.SavePng(gpu, tp);
            wrist.CloseForTest();
            // nothing read: "–"
            data.Temps = None();
            data.Memories = PhotoFake.None();
            wrist.Settle();
            wrist.Draw(data); wrist.Draw(data);
            string dash = wrist.TempsShown(data);
            bool memEmpty = wrist.MemoryEmptyShown || Theme.Classic;          // (95's one cell shows an empty picture box)
            wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_onthisday_temps_none{tag}.png"));
            wrist.OpenForTest("temps");
            wrist.Draw(data); wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_open_temps_none{tag}.png"));
            wrist.CloseForTest();
            // what a frame allocates at rest with only these widgets on the face (want 0)
            data.Memories = Memories();
            data.Temps = Temps();
            wcfg.Widgets = Face().FindAll(x => x.Type is "memory" or "temps");
            using var appOnly = Tools.AppAllocs();
            wrist.Settle();
            // (--lang's count of the texts drawn without a translation makes garbage of its own on every text: off while it is measured)
            var miss = Lang.Miss;
            Lang.Miss = null;
            for (int i = 0; i < 6; i++) wrist.Draw(data);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) wrist.Draw(data);
            long perFrame = (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.OnThisDayAllocTypes("onthisday/temps" + tag, () => wrist.Draw(data));
            Lang.Miss = miss;
            Want($"onthisday/temps view{tag}: On this day shows its newest {memShown}, opened on it {memOpen}, the arrow steps to the one before {stepped}, " +
                 $"none: its empty state {memEmpty}; temperatures GPU/CPU '{temps}' (want '66/61'), nothing read '{dash}' (want '–/–') -",
                 memShown && memOpen && stepped && memEmpty && temps == "66/61" && dash == "–/–");
            Want($"onthisday/temps view{tag}: a frame at rest with only these widgets on the face {perFrame} B (want 0)", perFrame == 0);
            Preview.RemoveAll(p => p.tag == tag);
            Preview.Add((tag, face, mem, tp));
        }
        catch (Exception e) { Log.Error("onthisday/temps view: " + e); Want($"onthisday/temps view{tag}: ran to the end", false); }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (data.Memories, data.Temps) = (m0, t0);
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    // ORBIT_SNAP_ONLY=onthisday: the data checks, both widgets in every style, the preview sheet - in seconds
    public static int Only(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir)
    {
        int failed0 = Tools.WantsFailed;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = Tools.SnapCulture;
        DataChecks(dir);
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            Run(wrist, data, cfg, gpu, dir, tag);
        }
        PreviewSheet(dir);
        System.Globalization.CultureInfo.CurrentCulture = culture;
        return Tools.WantsExit(failed0);
    }

    // every style's face, On this day opened and the temperatures opened side by side, one row a style
    public static void PreviewSheet(string dir)
    {
        if (Preview.Count == 0) return;
        const int W = 640, H = 440, Gap = 12, Label = 26;
        using var sheet = new Bitmap(3 * W + 4 * Gap, Preview.Count * (H + Label + Gap) + Gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 32, 34, 40));
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int i = 0; i < Preview.Count; i++)
            {
                var (t, face, mem, tp) = Preview[i];
                float y = Gap + i * (H + Label + Gap);
                g.DrawString($"{(t.Length == 0 ? "modern" : t[1..])}: the face, On this day opened, the temperatures opened (made-up photos and readings)", font,
                    Brushes.White, Gap, y + 4);
                int k = 0;
                foreach (var f in new[] { face, mem, tp })
                {
                    using (var b = new Bitmap(f)) g.DrawImage(b, Gap + k * (W + Gap), y + Label, W, H);
                    k++;
                }
            }
        }
        string path = Path.Combine(dir, "onthisday_temps_preview.png");
        sheet.Save(path, ImageFormat.Png);
        Log.Info("onthisday/temps: every style side by side: " + path);
    }
}

static partial class Tools
{
    internal static void OnThisDayAllocTypes(string what, Action draw) => AllocTypes(what, draw, null);
}
