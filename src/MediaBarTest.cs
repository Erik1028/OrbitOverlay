using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// ORBIT_SNAP_ONLY=mediabar --snapshot <dir>: the theater bar's media console (Panels/TheaterBarMedia.cs) on made-up sessions only - never
// the real media session, its commands recorded (never sent), no VR. Into <dir>/mediabar:
//  - mediabar_<state>.png: the Cyber bar in theater mode - no session, playing (with where it is), paused, a player that can't jump
//    (previous / next), a very long title (cut), a film (hours), no timeline, a jump on its way, the session gone (its last 3 s);
//    mediabar_modern*.png, mediabar_95*.png: those bars with a session - as they were;
//  - mediabar_marquee.png: the long title scrolling (Motion Full) at several moments, one under the other; mediabar_open.png: the bar
//    opening from its middle with the media part on it.
// And the checks ("... (want ...)"):
//  - the width: the part only with a session (exactly its width more), the rest of the bar where it was, inside the texture, no two
//    controls crossing, the readout's words not crossing each other or its edges (English and Magyar);
//  - the commands: play / pause, -10 s / +10 s (to where it shows, 10 s on), previous / next without jumps - and the stick: on the
//    readout or a plate, 10 s a notch sent once it rests (a track at a time without jumps), the sliders' stick steps as before;
//  - the session going: the part stays 3 s (a track change's empty moment), not under the laser, then goes;
//  - the readout drawn alone is the same pixels as the whole bar drawn; how often it is drawn (a scrolling title, a playing one, a paused
//    one) and what a frame of each costs and allocates; Modern and the 95 bar the same with or without a session.
static partial class Tools
{
    static int MediaBarOnly(ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        string md = Path.Combine(dir, "mediabar");
        Directory.CreateDirectory(md);
        int fails = 0;
        void Want(string line, bool ok) { if (!ok) fails++; Log.Info("mediabar: " + line); }
        bool live0 = CyberFx.Live;
        var motion0 = Motion.Level;
        try
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = CyberHud.Id, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            Motion.Apply(cfg.Data.Appearance);
            CyberFx.Live = false;                       // the settled pictures (the marquee: its own part below)

            using var bar = new TheaterBar(gpu, painter, cfg);
            var a = new TheaterTest.FakeScreen { Name = "DELL U2723QE" };
            var b = new TheaterTest.FakeScreen { Name = "LG ULTRAGEAR" };
            var choices = new List<ITheaterScreen> { a, b };
            var view = new TheaterView { Size = 2.8f };
            var fake = new TheaterBar.BarMedia();
            var cmds = new List<string>();
            var seeks = new List<double>();
            int kbs = 0;
            bool advance = false;                       // the made-up player's time runs on (as MediaInfo.Timeline does) - the rates below
            double fakeAt = 0, now = 1000;
            bar.ReadMedia = () =>
            {
                var m = fake;
                if (advance && m.Playing && m.Timeline) m.Pos = Math.Min(m.Dur, m.Pos + (now - fakeAt));
                return m;
            };
            bar.OnMedia = c => cmds.Add(c);
            bar.OnSeekTo = s => seeks.Add(s);
            bar.OnKeyboard = () => kbs++;
            var ui = bar.UiForTest;
            bar.VisForTest = 1;                         // faded in (the opening: its own pictures below)
            var hits = new Dictionary<string, RectangleF>();
            ui.HitSpy = (id, r) => hits[id] = r;
            var texts = new List<(string s, RectangleF r, float size, FontWeight w, string fam)>();
            Surface Frame(double dt = 0.02)
            {
                now += dt;
                bar.MediaTickForTest(now);
                hits.Clear();
                return bar.DrawFor(choices, b, view);
            }
            var playing = new TheaterBar.BarMedia { Has = true, Title = "Midnight City", Artist = "M83", Playing = true, Timeline = true, Pos = 134, Dur = 243, CanSeek = true };

            // ---- no session: the bar as it was ----
            fake = default;
            Frame(5);                                   // (a fresh start: the bar was away)
            var s0 = Frame();
            int w0 = bar.WidthForTest;
            var dim0 = hits["thb.dim"];
            s0.SavePng(gpu, Path.Combine(md, "mediabar_none.png"));
            Want($"no session: no media part {!bar.MediaShownForTest} (want True), width {w0} px (want 1244: as before)", !bar.MediaShownForTest && w0 == 1244);

            // ---- the states ----
            const int PartW = 252 + 6 + 50 + 6 + 64 + 6 + 50 + 8;
            void State(string tag, string what, TheaterBar.BarMedia m, bool fresh = true)
            {
                fake = m;
                if (fresh) Frame(5);
                texts.Clear();
                painter.TextSpy = (s, r, c, size, w, fam) => texts.Add((s, r, size, w, fam));
                var s = Frame();
                painter.TextSpy = null;
                s.SavePng(gpu, Path.Combine(md, $"mediabar_{tag}.png"));
                LayoutCheck(tag, what);
            }
            void LayoutCheck(string tag, string what)
            {
                int tw = bar.WidthForTest;
                var rd = bar.ReadoutForTest;
                var list = hits.Where(h => h.Value.Width > 0).ToList();
                var cross = new List<string>();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var x = RectangleF.Intersect(list[i].Value, list[j].Value);
                        if (x.Width >= 1.5f && x.Height >= 1.5f) cross.Add($"{list[i].Key} x {list[j].Key}");
                    }
                bool inside = list.All(h => h.Value.Left >= 0 && h.Value.Right <= tw && h.Value.Top >= 0 && h.Value.Bottom <= 84);
                bool parts = hits.ContainsKey("thb.mread") && hits.ContainsKey("thb.mb") && hits.ContainsKey("thb.mp") && hits.ContainsKey("thb.mf") &&
                             hits.ContainsKey("thb.kb") && hits.ContainsKey("thb.x") && hits.ContainsKey("thb0") && hits.ContainsKey("thb1");
                // the rest of the bar where it was: the settings moved over by exactly the part's width
                float moved = hits.TryGetValue("thb.dim", out var d) ? d.X - dim0.X : -1;
                // the readout's words: inside it, none crossing another on its row
                var boxes = new List<(string s, float x0, float x1, float y0, float y1)>();
                foreach (var t in texts)
                {
                    if (t.fam == Theme.IconFont || t.r.Right <= rd.X || t.r.X >= rd.Right || t.r.Y < rd.Y - 2 || t.r.Bottom > rd.Bottom + 8) continue;
                    float mw = painter.MeasureWidth(t.s, t.size, t.w, t.fam);
                    bool trailing = t.s.Contains(" / ");
                    float x0 = trailing ? t.r.Right - mw : t.r.X;
                    boxes.Add((t.s, x0, x0 + mw, t.r.Y, t.r.Bottom));
                }
                var bad = new List<string>();
                foreach (var bx in boxes)
                    if (bx.x0 < rd.X + 10 || bx.x1 > rd.Right - 10) bad.Add($"'{bx.s}' leaves the readout ({bx.x0 - rd.X:0}..{bx.x1 - rd.X:0} of {rd.Width:0})");
                for (int i = 0; i < boxes.Count; i++)
                    for (int j = i + 1; j < boxes.Count; j++)
                    {
                        var (p, q) = (boxes[i], boxes[j]);
                        float oy = MathF.Min(p.y1, q.y1) - MathF.Max(p.y0, q.y0), ox = MathF.Min(p.x1, q.x1) - MathF.Max(p.x0, q.x0);
                        if (oy > 4 && ox > 0.5f) bad.Add($"'{p.s}' x '{q.s}' ({ox:0.#} px)");
                    }
                Want($"{tag} ({what}): width {tw} px (want {w0 + PartW}: the part's {PartW} more), the settings {moved:0} px over (want {PartW}), " +
                     $"all its controls {parts} (want True), inside the texture {inside} (want True), controls crossing {cross.Count} (want 0)" +
                     (cross.Count > 0 ? ": " + string.Join(", ", cross) : ""),
                    tw == w0 + PartW && Math.Abs(moved - PartW) < 0.01f && parts && inside && cross.Count == 0);
                Want($"{tag}: the readout's words {boxes.Count}, crossing or leaving it {bad.Count} (want 0){(bad.Count > 0 ? ": " + string.Join("; ", bad) : "")}",
                    boxes.Count >= 2 && bad.Count == 0);
            }

            State("playing", "Midnight City by M83, 2:14 of 4:03, can jump", playing);
            State("paused", "paused", playing with { Playing = false });
            State("noseek", "a player that can't jump: previous / next", playing with { CanSeek = false });
            State("long", "a very long title: cut", playing with
            {
                Title = "A Very Long Song Title That Goes On And On (Extended Club Remix)", Artist = "Somebody Featuring Somebody Else",
            });
            Want($"long: it would scroll (Motion Full) {bar.MarqueeForTest} (want True)", bar.MarqueeForTest);
            State("film", "a film: hours", new TheaterBar.BarMedia { Has = true, Title = "Blade Runner 2049", Playing = true, Timeline = true, Pos = 3734, Dur = 9840, CanSeek = true });
            State("notime", "no timeline (a live stream)", playing with { Timeline = false, Pos = 0, Dur = 0, Title = "Lofi hip hop radio - beats to relax/study to", Artist = "Lofi Girl" });
            State("short", "a short title without an artist", playing with { Title = "Intro", Artist = null });
            State("emoji", "letters of other scripts and an emoji", playing with { Title = "Ünnepi Hangverseny — Ё ♪ 🎬 Live", Artist = "Zeneakadémia" });
            // (how wide it gets on a desk like Erik's: three monitors and a window - a picture only)
            fake = playing;
            var wideChoices = new List<ITheaterScreen> { new TheaterTest.FakeScreen { Name = "AOC 24G4X" }, b, new TheaterTest.FakeScreen { Name = "Xiaomi Monitor" },
                new TheaterTest.FakeScreen { Name = "YouTube - Mozilla Firefox" } };
            Frame(5);
            bar.DrawFor(wideChoices, b, view).SavePng(gpu, Path.Combine(md, "mediabar_wide.png"));
            Log.Info($"mediabar: mediabar_wide.png - three monitors and a window: {bar.WidthForTest} px wide, {bar.WidthForTest / 84f:0.0} times as wide as tall " +
                     $"({bar.WidthForTest / 84f * TheaterBar.HeightAt(2.5f):0.00} m at 2.5 m; the screen 2.8 m)");
            Frame(5);

            // ---- the commands, recorded ----
            fake = playing;
            Frame(5);
            Frame();
            var rd0 = hits["thb.mread"];
            Vector2 Mid(RectangleF r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
            void Click(string id)
            {
                var p = Mid(hits[id]);
                bar.PointerMove(Hand.Right, p);
                Frame();
                bar.PointerButton(Hand.Right, PtrButton.Primary, true, p);
                Frame();
                bar.PointerButton(Hand.Right, PtrButton.Primary, false, p);
                Frame();
            }
            void Away()
            {
                bar.PointerLeave(Hand.Right);
                ui.ResetAnimations();                   // the next pictures settled (no hover glow fading out)
                Frame();
            }
            cmds.Clear(); seeks.Clear();
            double t0 = now;
            Click("thb.mb");
            Frame();
            double exp = 134 + (now - t0);
            Want($"-10 s: one jump {seeks.Count} (want 1), to {(seeks.Count > 0 ? seeks[0] : -1):0.0} s (want {exp - 10:0.0}: 10 s back from where it plays)",
                seeks.Count == 1 && Math.Abs(seeks[0] - (exp - 10)) < 0.6);
            seeks.Clear();
            Click("thb.mf");
            Frame();
            // where it shows now: the jump just sent (124 and running) - 10 s on from there
            Want($"+10 s: one jump {seeks.Count} (want 1), to {(seeks.Count > 0 ? seeks[0] : -1):0.0} s (want about {exp:0.0}: 10 s on from the jump before, not from the old reading)",
                seeks.Count == 1 && Math.Abs(seeks[0] - exp) < 0.8);
            Click("thb.mp");
            Want($"play / pause: '{string.Join(",", cmds)}' (want 'media.play': the Media widget's own command), no jump {seeks.Count == 1} (want True)",
                cmds.Count == 1 && cmds[0] == "media.play" && seeks.Count == 1);
            Away();

            // the stick on the readout: 3 notches up = 30 s on, sent once it rests
            Frame(5);
            seeks.Clear(); cmds.Clear();
            var rp = Mid(hits["thb.mread"]);
            bar.PointerMove(Hand.Right, rp);
            Frame();
            bar.PointerScroll(Hand.Right, 3, rp);
            Frame();
            Frame(0.1);
            int early = seeks.Count;
            var sPend = bar.DrawFor(choices, b, view);
            sPend.SavePng(gpu, Path.Combine(md, "mediabar_jump.png"));
            Frame(0.4);
            Want($"stick on the readout, 3 notches up: nothing sent while it moves {early} (want 0), then one jump {seeks.Count} (want 1) to " +
                 $"{(seeks.Count > 0 ? seeks[0] : -1):0.0} s (want 164.0: 30 s on), no track command {cmds.Count} (want 0)",
                early == 0 && seeks.Count == 1 && Math.Abs(seeks[0] - 164) < 0.6 && cmds.Count == 0);
            // the stick on the +10 plate, 2 notches down
            seeks.Clear();
            Frame(5);
            var fp = Mid(hits["thb.mf"]);
            bar.PointerMove(Hand.Right, fp);
            Frame();
            bar.PointerScroll(Hand.Right, -2, fp);
            Frame();
            Frame(0.5);
            Want($"stick on the +10 plate, 2 notches down: one jump {seeks.Count} (want 1) to {(seeks.Count > 0 ? seeks[0] : -1):0.0} s (want 114.0: 20 s back)",
                seeks.Count == 1 && Math.Abs(seeks[0] - 114) < 0.6);
            Away();

            // a player that can't jump: previous / next, the stick a track at a time
            fake = playing with { CanSeek = false };
            Frame(5);
            Frame();
            cmds.Clear(); seeks.Clear();
            Click("thb.mb");
            Click("thb.mf");
            Want($"no jumps: the plates '{string.Join(",", cmds)}' (want 'media.prev,media.next'), no jump {seeks.Count} (want 0)",
                string.Join(",", cmds) == "media.prev,media.next" && seeks.Count == 0);
            cmds.Clear();
            rp = Mid(hits["thb.mread"]);
            bar.PointerMove(Hand.Right, rp);
            Frame();
            bar.PointerScroll(Hand.Right, 1, rp); Frame();
            bar.PointerScroll(Hand.Right, 1, rp); Frame(0.1);
            bar.PointerScroll(Hand.Right, -1, rp); Frame(0.7);
            bar.PointerScroll(Hand.Right, -1, rp); Frame();
            Want($"no jumps, the stick: '{string.Join(",", cmds)}' (want 'media.next,media.prev': a track at a time, the second notch in 0.6 s not another)",
                string.Join(",", cmds) == "media.next,media.prev");
            Away();

            // what was there before: a slider's stick step, the keyboard plate
            fake = playing;
            Frame(5);
            Frame();
            float dimV = cfg.Data.Theater.Dim;
            var dp = Mid(hits["thb.dim"]);
            bar.PointerMove(Hand.Right, dp);
            Frame();
            bar.PointerScroll(Hand.Right, 1, dp);
            Frame();
            float dimStep = cfg.Data.Theater.Dim - dimV;
            seeks.Clear();
            Frame(0.5);
            Click("thb.kb");
            Want($"before: the Dim slider's stick step {dimStep:0.000} (want {0.9f / 50:0.000}), no jump from it {seeks.Count} (want 0), the keyboard plate {kbs} (want 1)",
                Math.Abs(dimStep - 0.9f / 50) < 0.001f && seeks.Count == 0 && kbs == 1);
            Away();

            // ---- the session going ----
            fake = playing;
            Frame(5);
            Frame();
            fake = default;
            Frame(0.3);
            bool stays = bar.MediaShownForTest;
            bar.DrawFor(choices, b, view).SavePng(gpu, Path.Combine(md, "mediabar_going.png"));
            for (int i = 0; i < 11; i++) Frame(0.3);
            bool gone = !bar.MediaShownForTest;
            int wGone = bar.WidthForTest;
            Want($"the session goes: still there after 0.3 s {stays} (want True: a track change's empty moment), gone after 3 s {gone} (want True), width {wGone} (want {w0})",
                stays && gone && wGone == w0);
            fake = playing;
            Frame(5);
            Frame();
            var hp = Mid(hits["thb.dim"]);
            bar.PointerMove(Hand.Right, hp);
            Frame();
            fake = default;
            for (int i = 0; i < 12; i++) Frame(0.3);
            bool held = bar.MediaShownForTest;
            Away();
            Frame();
            Want($"the session goes while the laser is on the bar: kept {held} (want True: the buttons must not slide away), gone once it leaves {!bar.MediaShownForTest} (want True)",
                held && !bar.MediaShownForTest);

            // ---- the readout drawn alone: the same pixels as the whole bar ----
            // the whole bar drawn playing; then drawn paused (another code, colours) and only the readout drawn playing over that
            fake = playing;
            Frame(5);
            var full = Frame().ReadPixels(gpu);
            fake = playing with { Playing = false };
            Frame(5);
            var surfP = Frame();
            fake = playing;
            now += 0.3;
            bar.MediaTickForTest(now);
            bool part = bar.MediaPartForTest();
            var mixed = surfP.ReadPixels(gpu);
            int diff = 0, maxd = 0, sw = surfP.W;
            var rdc = Rectangle.Round(bar.ReadoutForTest);
            for (int i = 0; i < full.Length; i++)
            {
                if (!rdc.Contains(i % sw, i / sw)) continue;     // (the play plate's icon is the paused one there: only the readout was drawn)
                uint p = full[i], q = mixed[i];
                if (p == q) continue;
                int d = 0;
                for (int k = 0; k < 32; k += 8) d = Math.Max(d, Math.Abs((int)((p >> k) & 0xFF) - (int)((q >> k) & 0xFF)));
                if (d > 1) diff++;
                maxd = Math.Max(maxd, d);
            }
            Want($"the readout drawn alone into the bar: drawn {part} (want True), pixels off by more than 1 level {diff} (want 0), largest {maxd}", part && diff == 0);

            // ---- how often the readout is drawn alone, at 90 Hz for 20 s ----
            double Rate(TheaterBar.BarMedia m, bool liveFx, MotionLevel lvl)
            {
                CyberFx.Live = liveFx;
                Motion.Level = lvl;
                fake = m;
                advance = true;
                fakeAt = now + 5;
                Frame(5);
                Frame();
                int n = 0;
                for (int i = 0; i < 90 * 20; i++)
                {
                    now += 1 / 90.0;
                    bar.MediaTickForTest(now);
                    if (bar.MediaStepForTest()) n++;           // as Update does: the readout (or its title line) when it moved on
                }
                CyberFx.Live = false;
                Motion.Level = MotionLevel.Full;
                advance = false;
                return n / 20.0;
            }
            var longOne = playing with { Title = "A Very Long Song Title That Goes On And On (Extended Club Remix)", Artist = "Somebody Featuring Somebody Else" };
            double rLong = Rate(longOne, true, MotionLevel.Full), rLongRed = Rate(longOne, true, MotionLevel.Reduced);
            double rPlay = Rate(playing, true, MotionLevel.Full), rPause = Rate(playing with { Playing = false }, true, MotionLevel.Full);
            Want($"the readout alone, a second: a long title scrolling {rLong:0.0} (want 10-30: 26 px/s with a pause at its start), Motion Reduced {rLongRed:0.0} " +
                 $"(want 1-3: cut, not scrolling - the time and the line), a short one playing {rPlay:0.0} (want 1-3: the second, the line's pixel), paused {rPause:0.0} (want 0)",
                rLong >= 10 && rLong <= 30 && rLongRed >= 1 && rLongRed <= 3 && rPlay >= 1 && rPlay <= 3 && rPause == 0);

            // ---- the marquee at some moments (Motion Full), one under the other ----
            {
                CyberFx.Live = true;
                fake = longOne;
                advance = true;
                fakeAt = now + 5;
                Frame(5);
                var surf = Frame();
                var rd = bar.ReadoutForTest;
                double start = now;
                var frames = new List<uint[]>();
                var marks = new List<string>();
                int lines = 0, reads = 0;
                uint[] stepped = null;
                foreach (double t in new[] { 0.0, 1.5, 2.5, 4.0, 6.0, 9.0, 12.0, 14.5 })
                {
                    // frame by frame as the app does (a gap would be the bar coming back): the readout's or the title line's own update
                    while (now < start + t - 1e-6)
                    {
                        now = Math.Min(start + t, now + 1 / 90.0);
                        bar.MediaTickForTest(now);
                        bool rdP = bar.ReadoutPendingForTest;
                        if (bar.MediaStepForTest()) { if (rdP) reads++; else lines++; }
                    }
                    var px = surf.ReadPixels(gpu);
                    frames.Add(Crop(px, surf.W, Rectangle.Round(RectangleF.Inflate(rd, 6, 6))));
                    marks.Add($"{t:0.0}s");
                    if (t == 14.5) stepped = px;
                }
                SaveStack(frames, (int)rd.Width + 12, (int)rd.Height + 12, Path.Combine(md, "mediabar_marquee.png"));
                Log.Info($"mediabar: mediabar_marquee.png - the long title at {string.Join(", ", marks)}");
                // the scrolling as the title line's own steps (14.5 s of them) is the same picture as the whole bar drawn at that moment
                bool moved = !frames[0].AsSpan().SequenceEqual(frames[4]);
                var whole = bar.DrawFor(choices, b, view).ReadPixels(gpu);
                var rdc2 = Rectangle.Round(rd);
                int off2 = 0;
                for (int i = 0; i < whole.Length; i++)
                    if (rdc2.Contains(i % surf.W, i / surf.W) && whole[i] != stepped[i]) off2++;
                Want($"marquee: it moves {moved} (want True), {lines} title-line steps and {reads} readout updates in 14.5 s (want lines > 250, readouts < 40), " +
                     $"the stepped picture against the whole bar drawn then: {off2} pixel(s) differ (want 0)", moved && lines > 250 && reads < 40 && off2 == 0);
                // the bar opening from its middle with the part on it
                var open = new List<uint[]>();
                foreach (float v in new[] { 0.05f, 0.2f, 0.4f, 0.6f, 0.8f, 1f })
                {
                    bar.VisForTest = v;
                    var s = bar.DrawFor(choices, b, view);
                    open.Add(Crop(s.ReadPixels(gpu), s.W, new Rectangle(0, 0, s.W, s.H)));
                }
                bar.VisForTest = 1;
                SaveStack(open, bar.WidthForTest, 84, Path.Combine(md, "mediabar_open.png"));
                CyberFx.Live = false;
                advance = false;
            }

            // ---- what it costs (without --lang's own bookkeeping of untranslated text: the app has none) ----
            var miss0 = Lang.Miss;
            Lang.Miss = null;
            fake = playing;
            Frame(5);
            Frame();
            var cFull = FrameCost(gpu, () => bar.DrawFor(choices, b, view));
            var cPart = FrameCost(gpu, () => bar.MediaPartForTest());
            fake = default;
            Frame(5);
            Frame();
            var cBase = FrameCost(gpu, () => bar.DrawFor(choices, b, view));
            fake = longOne;
            Frame(5);
            CyberFx.Live = true;
            var cTick = FrameCost(gpu, () => { now += 1 / 90.0; bar.MediaTickForTest(now); }, 200);
            // the tick alone, without FrameCost's GPU flush round each call
            var swT = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20000; i++) { now += 1 / 90.0; bar.MediaTickForTest(now); }
            double tickUs = swT.Elapsed.TotalMilliseconds * 1000 / 20000;
            // what any drawing at all costs: an empty picture of the readout's size begun and ended (Direct2D's own, and the flush)
            double floorMs;
            using (var empty = new Surface(gpu, 252, 64)) floorMs = FrameCost(gpu, () => { painter.Begin(empty.Bitmap, 0); painter.End(); }).ms;
            var cMq = FrameCost(gpu, () => { now += 1 / 26.0; bar.MediaTickForTest(now); bar.MediaStepForTest(); });
            var cLine = FrameCost(gpu, () => bar.MediaLineForTest());
            CyberFx.Live = false;
            Log.Info($"mediabar: cost a frame - the whole Cyber bar with the part {cFull.ms:0.000} ms {cFull.bytes} B, without it {cBase.ms:0.000} ms {cBase.bytes} B, " +
                     $"the readout alone {cPart.ms:0.000} ms {cPart.bytes} B, the title line alone {cLine.ms:0.000} ms {cLine.bytes} B, a scrolling step (tick + update) {cMq.ms:0.000} ms {cMq.bytes} B, the per-frame tick {tickUs:0.00} us " +
                     $"({cTick.ms * 1000:0.0} us with a GPU flush) {cTick.bytes} B; an empty Begin / End of that size {floorMs:0.000} ms");
            Want($"allocations: the per-frame tick {cTick.bytes} B (want 0), the readout alone {cPart.bytes} B (want 0), a scrolling step {cMq.bytes} B " +
                 $"(want < 64: only the time's text, once a second)", cTick.bytes == 0 && cPart.bytes == 0 && cMq.bytes < 64);
            Lang.Miss = miss0;

            // ---- the big screen "selected" while the bar is up (CyberScreenFx) ----
            using (var fx = new CyberScreenFx(gpu, "test"))
            {
                var tex = new Vector2(1920, 1080);
                // the lock-on's last frame with the bar up, against the selected picture at its strength: the same (no dip at the hand-over)
                var lockEnd = fx.DrawForTest(painter, tex, 1, false, -1, 0.599, -1, 1).ReadPixels(gpu);
                var lockNoBar = fx.DrawForTest(painter, tex, 1, false, -1, 0.599, -1, 0).ReadPixels(gpu);
                var selS = fx.SelectForTest(painter, tex, out float selA);
                var selPx = selS.ReadPixels(gpu);
                int off = 0, smaxd = 0, lit = 0;
                uint maxA0 = 0;
                for (int i = 0; i < selPx.Length; i++)
                {
                    uint q = selPx[i], p = lockEnd[i];
                    if ((q >> 24) > 0) lit++;
                    maxA0 = Math.Max(maxA0, lockNoBar[i] >> 24);
                    for (int k = 0; k < 32; k += 8)
                    {
                        int want = (int)MathF.Round(((q >> k) & 0xFF) * selA), d = Math.Abs((int)((p >> k) & 0xFF) - want);
                        smaxd = Math.Max(smaxd, d);
                        if (d > 2) { off++; break; }
                    }
                }
                // (the faint strength is in the picture: with the bar up the overlay's alpha stays 1 through the hand-over - selA 1)
                Want($"selected: brackets drawn {lit} px (want > 0), the lock-on's last frame with the bar up = the selected picture at overlay alpha {selA:0.00} (want 1.00): {off} px off by more than 2 " +
                     $"(want 0, largest {smaxd}); without the bar it fades to nothing as before (alpha {maxA0}, want <= 3)", lit > 0 && off == 0 && maxA0 <= 3 && selA == 1);
                SaveCrop(selS, gpu, Path.Combine(md, "mediabar_selected.png"), new RectangleF(0, 0, selS.W, selS.H), keepAlpha: true);
                // what you see: a made-up picture on the screen, the brackets over it at their strength, the night round it
                var sview = new uint[selPx.Length];
                for (int i = 0; i < sview.Length; i++)
                {
                    int x = i % selS.W, y = i / selS.W;
                    bool scr = x >= 28 && x < selS.W - 28 && y >= 28 && y < selS.H - 28;
                    uint bgc = scr ? (uint)(0xFF000000 | (uint)(40 + 60 * x / selS.W) << 16 | (uint)(70 + 50 * y / selS.H) << 8 | 110) : 0xFF15121C;
                    uint q = selPx[i];
                    float qa = (q >> 24) / 255f * selA;
                    uint Ch(int k) => (uint)Math.Min(255, MathF.Round(((q >> k) & 0xFF) * selA + ((bgc >> k) & 0xFF) * (1 - qa)));
                    sview[i] = 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
                }
                SaveStack(new List<uint[]> { sview }, selS.W, selS.H, Path.Combine(md, "mediabar_selected_view.png"));
            }

            // ---- Modern and the 95 bar: the same with or without a session ----
            foreach (var (style, tag) in new[] { ("modern", "modern"), ("classic", "95") })
            {
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                fake = default;
                Frame(5);
                var pNo = Frame().ReadPixels(gpu);
                int wNo = bar.WidthForTest;
                fake = playing;
                Frame(5);
                var sWith = Frame();
                var pWith = sWith.ReadPixels(gpu);
                sWith.SavePng(gpu, Path.Combine(md, $"mediabar_{tag}.png"));
                bool same = pNo.Length == pWith.Length && pNo.AsSpan().SequenceEqual(pWith);
                Want($"{tag}: with a session width {bar.WidthForTest} (want {wNo}), the same picture {same} (want True: Cyber only), no media part {!bar.MediaShownForTest} (want True)",
                    bar.WidthForTest == wNo && same && !bar.MediaShownForTest);
            }
        }
        finally
        {
            CyberFx.Live = live0;
            Motion.Level = motion0;
            painter.TextSpy = null;
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        Log.Info($"mediabar: {(fails == 0 ? "ALL OK" : fails + " FAILED")} - pictures in {md}");
        return fails == 0 ? 0 : 1;
    }

    static uint[] Crop(uint[] px, int w, Rectangle c)
    {
        var o = new uint[c.Width * c.Height];
        int h = px.Length / w;
        for (int y = 0; y < c.Height; y++)
            for (int x = 0; x < c.Width; x++)
            {
                int sx = c.X + x, sy = c.Y + y;
                o[y * c.Width + x] = sx >= 0 && sy >= 0 && sx < w && sy < h ? px[sy * w + sx] : 0;
            }
        return o;
    }

    // frames one under the other on the night's dark (premultiplied over it), 6 px apart
    static unsafe void SaveStack(List<uint[]> frames, int w, int h, string path)
    {
        int gap = 6, H = frames.Count * (h + gap) - gap;
        using var bmp = new Bitmap(w, H, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, w, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        const uint bg = 0xFF15121C;
        for (int y = 0; y < H; y++)
        {
            uint* row = (uint*)((byte*)bd.Scan0 + y * bd.Stride);
            int f = y / (h + gap), fy = y % (h + gap);
            for (int x = 0; x < w; x++)
            {
                if (f >= frames.Count || fy >= h) { row[x] = 0xFF000000; continue; }
                uint p = frames[f][fy * w + x];
                uint a = p >> 24, inv = 255 - a;
                uint r = Math.Min(255, ((p >> 16) & 0xFF) + ((bg >> 16) & 0xFF) * inv / 255);
                uint g = Math.Min(255, ((p >> 8) & 0xFF) + ((bg >> 8) & 0xFF) * inv / 255);
                uint bl = Math.Min(255, (p & 0xFF) + (bg & 0xFF) * inv / 255);
                row[x] = 0xFF000000 | r << 16 | g << 8 | bl;
            }
        }
        bmp.UnlockBits(bd);
        bmp.Save(path, ImageFormat.Png);
    }
}
