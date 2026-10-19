using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Orbit;

// ORBIT_SNAP_ONLY=anim_cyber --snapshot <dir>: the Cyber style's animations (WristCyberFx.cs, UiCyber.cs, KeyboardCyber.cs) as
// frames, for looking at them - each effect started on the concept's pages and drawn in real time (~120 frames a second), the
// frames nearest to the listed times kept and cropped to what moves: qa/anim_cyber_<name>_<ms>.png, listed in
// qa/anim_cyber_index.txt (a line per effect: name|what|files). The boot and a minute glitch also whole, 30 frames a second:
// qa/anim_cyber_seq_<nnn>.png (a moving picture's frames). Only here are the effects live (CyberFx.Live): every other --snapshot
// picture is the settled face.
static class CyberAnimShot
{
    public static int Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, KeyboardPanel kb, SettingsView view, Ui ui,
        Surface surf, PointerState ptr, Painter painter)
    {
        string qa = Path.Combine(dir, "qa");
        Directory.CreateDirectory(qa);
        foreach (var f in Directory.GetFiles(qa, "anim_cyber_*")) File.Delete(f);
        // ORBIT_ANIM_MOTION=reduced / off: the same frames at that Look > Motion (what still moves)
        cfg.Data.Appearance = new AppearanceConfig { Style = CyberHud.Id, Clock24h = true, Motion = Environment.GetEnvironmentVariable("ORBIT_ANIM_MOTION") };
        Theme.Apply(cfg.Data.Appearance);
        System.Globalization.CultureInfo.CurrentCulture = Tools.SnapCulture;
        CyberFx.Live = true;
        wrist.CyberLive = true;
        bool still = Motion.Still;
        Motion.Still = false;                       // --snapshot holds the shared loops at rest (Motion.Still): here they move
        wrist.OnAction = id => { if (id == "night") data.NightMode = !data.NightMode; };
        Tools.PagesData(data);
        data.FakeArt = null;                        // the concept's equalizer in the cover box
        data.Vr.Batteries.Clear();
        data.Vr.Batteries.Add(("HMD", 0.71f, false));
        data.Vr.Batteries.Add(("L", 0.82f, false));
        data.Vr.Batteries.Add(("R", 0.18f, true));
        data.Vr.Batteries.Add(("Trk×6", 0.44f, false));
        var wc = cfg.Data.Wrist;
        wc.Face = "custom";
        wc.Background = "panel";
        wc.Grid = null;
        wc.Name = "Concept";
        wc.Widgets = Concept();
        wc.Pages = new List<WatchPage> { WatchPages.Make("vrchat", "6x4"), WatchPages.Make("performance", "6x4") };
        wc.Page = 0;
        var index = new StringBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        RectangleF Card(string type, int page = 0)
        {
            var list = page == 0 ? wc.Widgets : wc.Pages[page - 1].Widgets;
            var w = list.Find(x => x.Type == type);
            return WatchWidgets.Rect(w, false, WatchPages.Grid(wc, page));
        }
        void Fresh(int page)
        {
            wc.Page = page;
            wrist.Settle();
            wrist.Draw(data);
            wrist.Draw(data);
        }
        List<(double t, uint[] px)> Watch(double[] at, Action start, Action<int> each = null) =>
            Capture(() => wrist.Draw(data), () => wrist.Surf.ReadPixels(gpu), gpu, at, start, each);
        void Save(string name, string what, List<(double t, uint[] px)> frames, int w, int h, RectangleF crop, string[] labels = null)
        {
            var files = new List<string>();
            for (int i = 0; i < frames.Count; i++)
            {
                string label = labels != null ? labels[i] : $"{frames[i].t * 1000:0}";
                string file = $"anim_cyber_{name}_{label}.png";
                Write(Path.Combine(qa, file), frames[i].px, w, h, crop);
                files.Add(file);
            }
            index.AppendLine($"{name}|{what}|{string.Join(",", files)}");
        }
        var all = new RectangleF(0, 0, WristPanel.TW, WristPanel.TH);
        RectangleF Around(RectangleF r, float m = 12) => RectangleF.Intersect(RectangleF.Inflate(r, m, m), all);

        // the boot, the face's own frames, and whole at 30 a second
        Fresh(0);
        Save("boot", "the boot (the watch comes into view)", Watch(new[] { 0.03, 0.1, 0.17, 0.24, 0.32, 0.4, 0.5, 0.62, 0.78, 1.1 }, wrist.Intro), WristPanel.TW, WristPanel.TH, all);
        // held: the watch fading in before its boot - nothing of the face yet
        Fresh(0);
        Save("held", "held for its boot (the watch still fading in)", Watch(new[] { 0.0, 0.2 }, wrist.IntroHoldForTest), WristPanel.TW, WristPanel.TH, all);
        // the glance: back in view within 20 s - the face at once, brackets and a line card by card, the time glitches once
        Fresh(0);
        Save("glance", "a glance (back in view within 20 s)", Watch(new[] { 0.0, 0.04, 0.08, 0.12, 0.17, 0.23, 0.3, 0.4, 0.5 }, wrist.CyberGlanceForTest), WristPanel.TW, WristPanel.TH, all);
        Fresh(0);
        var seq = Watch(Steps(0, 1.4, 1 / 30.0), wrist.Intro);
        // the minute glitch
        Fresh(0);
        Save("glitch", "the minute glitch (the yellow time card)", Watch(new[] { 0.0, 0.03, 0.06, 0.1, 0.14, 0.18, 0.21, 0.3 }, () => wrist.CyberTest("glitch")),
            WristPanel.TW, WristPanel.TH, Around(Card("time")));
        Fresh(0);
        var seq2 = Watch(Steps(0, 0.6, 1 / 30.0), () => wrist.CyberTest("glitch"));
        for (int i = 0; i < seq.Count; i++) Write(Path.Combine(qa, $"anim_cyber_seq_{i:000}.png"), seq[i].px, WristPanel.TW, WristPanel.TH, all);
        for (int i = 0; i < seq2.Count; i++) Write(Path.Combine(qa, $"anim_cyber_seq_{seq.Count + i:000}.png"), seq2[i].px, WristPanel.TW, WristPanel.TH, all);
        index.AppendLine($"seq|the boot, then a minute glitch, 30 frames a second|{seq.Count}+{seq2.Count}");
        // the page swipe
        Fresh(0);
        Save("swipe", "the page swipe (a scan line on the seam)", Watch(new[] { 0.02, 0.06, 0.1, 0.14, 0.19, 0.25, 0.33, 0.5 }, () => wrist.GoToPage(1, animate: true)),
            WristPanel.TW, WristPanel.TH, all);
        // an opened view unfolding, and closing
        Fresh(0);
        Save("unfold", "an opened view unfolds (brackets leading)", Watch(new[] { 0.0, 0.03, 0.06, 0.1, 0.14, 0.19, 0.26, 0.4 }, () => wrist.CyberTest("open:media")),
            WristPanel.TW, WristPanel.TH, all);
        Save("fold", "and closes", Watch(new[] { 0.0, 0.04, 0.08, 0.12, 0.17, 0.24 }, () => wrist.CyberTest("close")), WristPanel.TW, WristPanel.TH, all);
        wrist.CloseForTest();
        // a tap on a button: the flash, the corners, the fill wiping on
        Fresh(0);
        var night = Card("night");
        var at = new Vector2(night.X + night.Width / 2, night.Y + night.Height / 2);
        Save("tap", "a tap: flash, corners snap, the fill wipes on", Watch(new[] { 0.0, 0.03, 0.06, 0.09, 0.13, 0.18, 0.24, 0.32, 0.45 }, () =>
        {
            wrist.PointerMove(Hand.Left, at);
            wrist.PointerButton(Hand.Left, PtrButton.Primary, true, at);
            wrist.Draw(data);
            wrist.PointerButton(Hand.Left, PtrButton.Primary, false, at);
        }, n => { if (n == 2) wrist.PointerLeave(Hand.Left); }), WristPanel.TW, WristPanel.TH, Around(RectangleF.Union(night, Card("layout")), 14));
        Save("untap", "tapped again: the fill wipes off", Watch(new[] { 0.0, 0.04, 0.08, 0.12, 0.18, 0.3 }, () =>
        {
            wrist.PointerMove(Hand.Left, at);
            wrist.PointerButton(Hand.Left, PtrButton.Primary, true, at);
            wrist.Draw(data);
            wrist.PointerButton(Hand.Left, PtrButton.Primary, false, at);
        }, n => { if (n == 2) wrist.PointerLeave(Hand.Left); }), WristPanel.TW, WristPanel.TH, Around(night, 14));
        // a scan sweep over the frame graph
        Fresh(0);
        Save("sweep", "a scan sweep (every 7-12 s, a card)", Watch(new[] { 0.03, 0.12, 0.22, 0.32, 0.42, 0.52, 0.62, 0.69 }, () => wrist.CyberTest("scan")),
            WristPanel.TW, WristPanel.TH, Around(Card("graph"), 4));
        // a warning pulsing (R 18 %)
        Fresh(0);
        Save("pulse", "a warning pulses (R 18 %, 1.8 s)", Watch(new[] { 0.0, 0.3, 0.6, 0.9, 1.2, 1.5 }, null), WristPanel.TW, WristPanel.TH, Around(Card("battery"), 4));
        // a value that changes flickers
        Fresh(0);
        data.Vr.AppFps = 86;
        wrist.Draw(data);
        Save("flicker", "a value changes: a short flicker", Watch(new[] { 0.0, 0.04, 0.08, 0.15 }, () => { data.Vr.AppFps = 88; data.Vr.ReprojPct = 6; }),
            WristPanel.TW, WristPanel.TH, Around(RectangleF.Union(Card("fps"), Card("reproj")), 4));
        data.Vr.AppFps = 86; data.Vr.ReprojPct = 4;
        // a note that changes types in again
        Fresh(0);
        Save("note", "a note changes: it types in", Watch(new[] { 0.0, 0.04, 0.08, 0.12, 0.16, 0.25 }, () => data.FakeMedia = (true, "Midnight City", "M83", false)),
            WristPanel.TW, WristPanel.TH, Around(Card("media"), 4));
        data.FakeMedia = (true, "Midnight City", "M83", true);
        // the hazard stripes crawling (a step a second)
        Fresh(0);
        var steps = new List<(double, uint[])>();
        for (int s = 0; s < 5; s++)
        {
            wrist.TestNow = new DateTime(2026, 10, 16, 23, 47, 30 + s);
            wrist.Draw(data);
            steps.Add((s, wrist.Surf.ReadPixels(gpu)));
        }
        wrist.TestNow = null;
        Save("stripes", "the hazard stripes crawl (a step a second)", steps, WristPanel.TW, WristPanel.TH, Around(Card("time"), 4), new[] { "s0", "s1", "s2", "s3", "s4" });
        // the VRChat page: the microphone's dot, a new notification
        Fresh(1);
        Save("mic", "the live microphone's dot breathes (2.4 s)", Watch(new[] { 0.0, 0.4, 0.8, 1.2, 1.6, 2.0 }, null), WristPanel.TW, WristPanel.TH, Around(Card("mic", 1), 4));
        Fresh(1);
        Save("inbox", "a new notification: the tag blinks, the row types in", Watch(new[] { 0.0, 0.1, 0.2, 0.3, 0.42, 0.6, 0.8, 1.0, 1.3 },
            () => data.Notes.Add("VRChat", "Friend request", "Friend request from Vega", DateTime.Now)), WristPanel.TW, WristPanel.TH, Around(Card("notifications", 1), 4));
        // the settings: a tab's tags slide in, a button's hover
        view.Tab = SettingsView.TabWrist;
        void FrameS() { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
        ui.ResetAnimations();
        ptr.Leave();
        FrameS(); FrameS();
        var sv = Capture(FrameS, () => surf.ReadPixels(gpu), gpu, new[] { 0.0, 0.03, 0.06, 0.1, 0.14, 0.2, 0.3 }, () => view.Tab = SettingsView.TabLook);
        Save("settings_tags", "settings: a tab's tags slide in and type", sv, surf.W, surf.H, new RectangleF(320, 105, 1180, 640));
        var hv = Capture(FrameS, () => surf.ReadPixels(gpu), gpu, new[] { 0.0, 0.03, 0.06, 0.1, 0.18 }, () => ptr.Move(957, 48));
        Save("settings_hover", "settings: a button's hover", hv, surf.W, surf.H, new RectangleF(600, 6, 900, 84));
        ptr.Leave();
        // the keyboard: a key under the laser, a key let go
        kb.Draw();
        var kr = kb.KeyRectForTest(20);
        var kc = new Vector2(kr.X + kr.Width / 2, kr.Y + kr.Height / 2);
        var kf = Capture(() => kb.Draw(), () => kb.Surf.ReadPixels(gpu), gpu, new[] { 0.0, 0.03, 0.07, 0.11, 0.16, 0.22 }, () => kb.CyberFlashForTest(20));
        var kbArea = RectangleF.Inflate(kr, kr.Width * 1.6f, kr.Height * 0.9f);
        Save("key", "keyboard: a key let go", kf, kb.Surf.W, kb.Surf.H, kbArea);
        kb.PointerMove(Hand.Left, kc);
        kb.Draw();
        Save("key_hover", "keyboard: a key under the laser", new List<(double, uint[])> { (0, kb.Surf.ReadPixels(gpu)) }, kb.Surf.W, kb.Surf.H, kbArea, new[] { "hover" });
        kb.PointerLeave(Hand.Left);
        File.WriteAllText(Path.Combine(qa, "anim_cyber_index.txt"), index.ToString());
        CyberFx.Live = false;
        wrist.CyberLive = false;
        Motion.Still = still;
        Log.Info($"anim cyber: frames written to {qa} in {sw.Elapsed.TotalSeconds:0.0} s");
        return 0;
    }

    // --animstrip <dir> --only-style cyber: the preset faces in Cyber (WristCyberFaces.cs) - each booting as the watch comes into view,
    // and the minute's glitch - drawn in real time, the frames at the listed times in a sheet four across at half size:
    // <dir>/cyberface_<face>_boot.png, cyberface_<face>_glitch.png (frame times in <dir>/cyberface_index.txt)
    public static int Faces(string dir)
    {
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(dir);
        var cfg = new ConfigStore(Path.Combine(dir, "config.json"));
        cfg.Data.Appearance = new AppearanceConfig { Style = CyberHud.Id, Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        System.Globalization.CultureInfo.CurrentCulture = Tools.SnapCulture;
        using var gpu = new Gpu(-1);
        using var painter = new Painter(gpu);
        var data = new PanelData { VrConnected = true, VramTotalGB = 16, FakeGpuLoad = 63, FakeVram = 9.4f, VrSince = DateTime.Now.AddMinutes(-83) };
        data.Vr.AppFps = 86; data.Vr.ReprojPct = 4; data.Vr.GpuMs = 9.3f; data.Vr.DisplayHz = 90;
        data.Vr.Batteries.Add(("HMD", 0.71f, false));
        data.Vr.Batteries.Add(("L", 0.82f, false));
        data.Vr.Batteries.Add(("R", 0.18f, true));
        data.Vr.Batteries.Add(("Trk×6", 0.44f, false));
        data.FakeMedia = (true, "Midnight City", "M83", true);
        using var wrist = new WristPanel(gpu, painter, cfg);
        wrist.Mic.Fake = true;
        CyberFx.Live = true;
        wrist.CyberLive = true;
        var index = new StringBuilder();
        double[] boot = { 0.03, 0.1, 0.17, 0.24, 0.32, 0.42, 0.55, 0.7, 0.85, 1.0, 1.15, 1.4 }, glitch = { 0.0, 0.04, 0.08, 0.12, 0.17, 0.3 };
        void Sheet(string name, string what, List<(double t, uint[] px)> frames)
        {
            const int cols = 4, gap = 6, fw = WristPanel.TW / 2, fh = WristPanel.TH / 2;
            int rows = (frames.Count + cols - 1) / cols;
            using var sheet = new Bitmap(cols * fw + (cols - 1) * gap, rows * fh + (rows - 1) * gap, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(255, 70, 70, 70));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                for (int i = 0; i < frames.Count; i++)
                {
                    string tmp = Path.Combine(dir, "_frame.png");
                    Write(tmp, frames[i].px, WristPanel.TW, WristPanel.TH, new RectangleF(0, 0, WristPanel.TW, WristPanel.TH));
                    using (var f = new Bitmap(tmp)) g.DrawImage(f, i % cols * (fw + gap), i / cols * (fh + gap), fw, fh);
                    File.Delete(tmp);
                }
            }
            sheet.Save(Path.Combine(dir, $"cyberface_{name}.png"), ImageFormat.Png);
            var ms = new List<string>();
            foreach (var (t, _) in frames) ms.Add($"{t * 1000:0}");
            index.AppendLine($"{name}|{what}|{string.Join(",", ms)} ms");
        }
        foreach (var face in new[] { "full", "analog", "minimal", "time" })
        {
            cfg.Data.Wrist.Face = face;
            wrist.Settle(); wrist.Draw(data); wrist.Draw(data);
            Sheet($"{face}_boot", $"the {face} face boots (the watch comes into view)", Capture(() => wrist.Draw(data), () => wrist.Surf.ReadPixels(gpu), gpu, boot, wrist.Intro));
            wrist.Settle(); wrist.Draw(data); wrist.Draw(data);
            Sheet($"{face}_glitch", $"the {face} face's minute glitch", Capture(() => wrist.Draw(data), () => wrist.Surf.ReadPixels(gpu), gpu, glitch, () => wrist.CyberTest("glitch")));
        }
        File.WriteAllText(Path.Combine(dir, "cyberface_index.txt"), index.ToString());
        CyberFx.Live = false;
        wrist.CyberLive = false;
        Log.Info("animstrip: the Cyber preset faces written to " + dir);
        return 0;
    }

    static List<WatchWidget> Concept()
    {
        var l = new List<WatchWidget>();
        void Put(string t, int x, int y, int w = 1, int h = 1) => l.Add(new WatchWidget { Type = t, X = x, Y = y, W = w, H = h });
        Put("analog", 0, 0, 2, 2); Put("time", 2, 0, 2, 1); Put("date", 2, 1, 2, 1); Put("battery", 4, 0, 2, 2);
        Put("media", 0, 2, 3, 1); Put("graph", 3, 2, 3, 1);
        Put("fps", 0, 3); Put("reproj", 1, 3); Put("gpu", 2, 3); Put("vram", 3, 3); Put("night", 4, 3); Put("layout", 5, 3);
        return l;
    }

    static double[] Steps(double from, double to, double step)
    {
        var l = new List<double>();
        for (double t = from; t <= to + 1e-9; t += step) l.Add(t);
        return l.ToArray();
    }

    // draws in real time (~120 a second) after start(), keeping the frame nearest after each time in 'at'; each(n): before frame n
    static List<(double t, uint[] px)> Capture(Action draw, Func<uint[]> read, Gpu gpu, double[] at, Action start, Action<int> each = null)
    {
        var got = new List<(double, uint[])>();
        start?.Invoke();
        double t0 = Clock.Now, next = t0;
        int i = 0, n = 0;
        while (i < at.Length && Clock.Now - t0 < at[^1] + 2)
        {
            each?.Invoke(n++);
            double t = Clock.Now - t0;
            draw();
            gpu.Ctx.Flush();
            if (t >= at[i] - 0.004) { got.Add((t, read())); i++; }
            next += 1 / 120.0;
            while (Clock.Now < next) Thread.SpinWait(40);
        }
        return got;
    }

    // a frame (premultiplied BGRA: over black), cropped
    static unsafe void Write(string path, uint[] px, int w, int h, RectangleF crop)
    {
        var c = Rectangle.Intersect(Rectangle.Round(crop), new Rectangle(0, 0, w, h));
        using var bmp = new Bitmap(c.Width, c.Height, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, c.Width, c.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < c.Height; y++)
        {
            uint* dst = (uint*)((byte*)bd.Scan0 + y * bd.Stride);
            for (int x = 0; x < c.Width; x++) dst[x] = px[(c.Y + y) * w + c.X + x] | 0xFF000000;
        }
        bmp.UnlockBits(bd);
        bmp.Save(path, ImageFormat.Png);
    }
}
