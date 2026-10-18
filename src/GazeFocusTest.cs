using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace Orbit;

// ORBIT_SNAP_ONLY=focus --snapshot <dir>: gaze focus (Panels/GazeFocus.cs) without VR - a made-up row of three monitors (and one of two)
// around a made-up head whose yaw sweeps and dithers at 90 fps, a made-up laser, as "want" lines: the dwell, the edge band, dithering,
// looking at nothing, the geometry at rest, continuity through a switch mid-way, the saved layout untouched, a grab against the same grab
// without the feature, theater, Motion reduced / off, the laser's pixel on a small monitor, the cost. Pictures: a top-down strip of the
// row over time (focus/strip.png), the setting clicked in Modern and 95 and the Desktops page in every style (focus/desktops_*.png).
static class GazeFocusTools
{
    const float Fps = 90, Dt = 1 / Fps;
    static readonly Vector3 HeadPos = new(0, 1.6f, 0);

    // three 2560 x 1440 monitors side by side on the desk, the middle one primary (two: the middle and the right; four: a lone one beside
    // the row of three)
    public static List<MonitorInfo> FakeMonitors(int count)
    {
        var l = new List<MonitorInfo>();
        if (count > 2) l.Add(new MonitorInfo { Id = "fake-left", Name = "Left", Device = @"\\.\DISPLAY2", X = -2560, Y = 0, W = 2560, H = 1440 });
        l.Add(new MonitorInfo { Id = "fake-middle", Name = "Middle", Device = @"\\.\DISPLAY1", X = 0, Y = 0, W = 2560, H = 1440, Primary = true });
        l.Add(new MonitorInfo { Id = "fake-right", Name = "Right", Device = @"\\.\DISPLAY3", X = 2560, Y = 0, W = 2560, H = 1440 });
        if (count > 3) l.Add(new MonitorInfo { Id = "fake-lone", Name = "Lone", Device = @"\\.\DISPLAY4", X = 5120, Y = 0, W = 2560, H = 1440 });
        return l;
    }

    // yaw in degrees, + = to the right (System.Numerics turns -Z towards -X for a positive angle about Y); pitch + = up
    static Matrix4x4 Head(float yawDeg, float pitchDeg = 0) =>
        Matrix4x4.CreateRotationX(pitchDeg * MathF.PI / 180) * Matrix4x4.CreateRotationY(-yawDeg * MathF.PI / 180) * Matrix4x4.CreateTranslation(HeadPos);

    // The app's side, made up: the monitors joined into one curved row 1.2 m round the head (JoinAll, as a first run does), the focus,
    // a clock stepping at 90 fps, and what is drawn tracked frame to frame (the largest move / growth of any monitor between two frames)
    sealed class Rig
    {
        public readonly List<DesktopPanel> Panels = new();
        public readonly DesktopLayout Layout;
        public readonly VrSession Vr = new();
        public readonly GazeFocus Focus;
        public readonly FocusConfig Cfg = new() { Enabled = true };
        public double T;
        public bool Theater, Hold;
        public DesktopPanel Laser;
        public float Yaw, Pitch;                      // where the head looks; the row line is 12 cm under the eyes (JoinAll), so a small monitor
                                                      // of a narrow one wants a look a little down, as in VR you look AT it
        public float MaxMove, MaxGrow, MaxMoveGrab;
        public bool Relative;                         // track the drawn centre's offset from the saved one (a grab moves the saved row itself)
        public long Ticks; public int Frames;         // the focus update alone
        readonly Vector3[] _pos; readonly float[] _w; bool _tracked;

        public Rig(Gpu gpu, string dir, int count, bool on = true)
        {
            var store = new ConfigStore(Path.Combine(dir, "focus_config.json"));     // its own, never saved
            Cfg.Enabled = on;
            var mouse = new MouseOwner();
            foreach (var m in FakeMonitors(count))
            {
                var dc = new DesktopConfig { Id = m.Id, Name = m.Name };
                store.Data.Desktops.Add(dc);
                var p = new DesktopPanel(gpu, store, mouse, m, dc);
                p.AttachSession(Vr);
                Panels.Add(p);
            }
            Layout = new DesktopLayout(Panels, store);
            foreach (var p in Panels) p.Layout = Layout;
            Vr.Device[0] = Head(0);
            Vr.DeviceValid[0] = true;
            Layout.JoinAll(MathUtil.YawFrame(Vr.Hmd), 1.2f, -0.12f);
            if (count > 3) Layout.Detach(this["Lone"]);      // the fourth one: on its own, right of the row
            Focus = new GazeFocus(Panels);
            _pos = new Vector3[Panels.Count];
            _w = new float[Panels.Count];
        }

        public DesktopPanel this[string name] => Panels.Find(p => p.Mon.Name == name);
        public DesktopPanel Focused => Focus.FocusedOf(Panels[0]);
        public string FocusName => Focused?.Mon.Name ?? "-";

        public void Step()
        {
            Vr.Device[0] = Head(Yaw, Pitch);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Focus.Update(Vr, T, Dt, Cfg, Theater, Hold, Laser, null);
            Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            Frames++;
            T += Dt;
            for (int i = 0; i < Panels.Count; i++)
            {
                var (w, width, _) = Drawn(Panels[i]);
                var pos = Relative ? w.Translation - Saved(Panels[i]).world.Translation : w.Translation;
                if (_tracked)
                {
                    MaxMove = MathF.Max(MaxMove, Vector3.Distance(pos, _pos[i]));
                    MaxGrow = MathF.Max(MaxGrow, MathF.Abs(width - _w[i]));
                }
                _pos[i] = pos; _w[i] = width;
            }
            _tracked = true;
        }

        public void Run(double seconds) { int n = (int)Math.Round(seconds * Fps); for (int i = 0; i < n; i++) Step(); }
        public void ResetTrack(bool relative = false) { MaxMove = MaxGrow = 0; Relative = relative; _tracked = false; }

        // the saved layout as numbers (M, width, bend, row) to see that nothing of it changed
        public string SavedText(bool withRow = true)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in Panels)
            {
                sb.Append(withRow ? p.Cfg.Group : p.Cfg.Group == null ? "-" : "row").Append('/').Append(p.Cfg.GroupIndex).Append(' ').Append(p.Cfg.Width.ToString("R")).Append(' ').Append(p.Cfg.Curvature.ToString("R"));
                foreach (var f in p.Cfg.Place.M) sb.Append(' ').Append(f.ToString("R"));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    static (Matrix4x4 world, float width, float curvature) Drawn(DesktopPanel p) =>
        p.Focus is { } f ? (f.World, f.Width, f.Curvature) : Saved(p);
    static (Matrix4x4 world, float width, float curvature) Saved(DesktopPanel p) =>
        (MathUtil.FromArray(p.Cfg.Place.M), p.Cfg.Width, p.Cfg.Curvature);

    static float Radius(float width, float curvature) => curvature < 1e-4f ? float.PositiveInfinity : width / (2 * MathF.PI * curvature);

    // how far b (right of a) sits past "edge to edge" with a along the arc as drawn, and off a's line
    static (float gap, float vertical) Seam(DesktopPanel a, DesktopPanel b)
    {
        var (wa, wia, ca) = Drawn(a);
        var (wb, wib, _) = Drawn(b);
        var ideal = DesktopLayout.Neighbor(wa, wia, Radius(wia, ca), wib, 1);
        var d = wb.Translation - ideal.Translation;
        return (Vector3.Dot(d, MathUtil.Right(ideal)), Vector3.Dot(d, MathUtil.Up(ideal)));
    }

    // the row at rest in the focus layout: the focused one at its saved width - the primary (the row's home) at its saved place, another
    // one where it grew - the others at 'others' of theirs, seamless, level. A monitor out of the row is not looked at.
    static bool RowOk(Rig r, float others, out string why)
    {
        var f = r.Focused;
        why = "";
        if (f == null) { why = "no focus"; return false; }
        float off = Vector3.Distance(Drawn(f).world.Translation, Saved(f).world.Translation);
        bool ok = (!f.Mon.Primary || off < 0.02f) && MathF.Abs(Drawn(f).width - f.Cfg.Width) < 1e-4f;
        why += $"focus {f.Mon.Name} {off * 100:0.0} cm off its saved centre{(f.Mon.Primary ? "" : " (a side one grows where it is)")}";
        DesktopPanel prev = null;
        int i = 0;
        foreach (var p in r.Panels)
        {
            if (p.Cfg.Group == null) continue;
            if (p != f)
            {
                float want = p.Cfg.Width * others;
                if (MathF.Abs(Drawn(p).width - want) > 1e-3f) { ok = false; why += $", {p.Mon.Name} {Drawn(p).width:0.000} m wide (want {want:0.000})"; }
            }
            if (prev != null)
            {
                var (gap, vert) = Seam(prev, p);
                why += $", seam {++i} gap {gap * 100:0.0} cm up {vert * 100:0.0} cm";
                if (MathF.Abs(gap) > 0.01f || MathF.Abs(vert) > 0.01f) ok = false;
            }
            prev = p;
        }
        return ok;
    }

    // every row member's drawn centre, to compare two moments of the row
    static Vector3[] Centres(Rig r)
    {
        var l = new List<Vector3>();
        foreach (var p in r.Panels) if (p.Cfg.Group != null) l.Add(Drawn(p).world.Translation);
        return l.ToArray();
    }

    static float Farthest(Vector3[] a, Vector3[] b)
    {
        float d = 0;
        for (int i = 0; i < a.Length; i++) d = MathF.Max(d, Vector3.Distance(a[i], b[i]));
        return d;
    }

    // which monitor the head's ray meets as drawn, and where across it (0 = its left edge)
    static DesktopPanel LookedAt(Rig r, out float u)
    {
        DesktopPanel best = null; float bd = float.MaxValue; u = 0;
        var o = r.Vr.Hmd.Translation; var d = MathUtil.Forward(r.Vr.Hmd);
        foreach (var p in r.Panels)
        {
            var (w, width, c) = Drawn(p);
            var ts = p.TextureSize;
            if (LocalRaycast.Intersect(w, width, c, ts.X, ts.Y, o, d, out float dist, out var uv, out _, out _) && dist < bd) { bd = dist; best = p; u = uv.X; }
        }
        return best;
    }

    // a laser from a hand aimed at the desktop pixel px of p as drawn (or as saved): the pixel the hit comes back as
    static Vector2 PixelThrough(DesktopPanel p, Vector2 px, bool drawn)
    {
        var (world, w, c) = drawn ? Drawn(p) : Saved(p);
        var ts = p.TextureSize;
        float h = w * ts.Y / ts.X, u = px.X / ts.X, v = px.Y / ts.Y;
        Vector3 lp;
        if (c > 0)
        {
            float r = w / (2 * MathF.PI * c), th = (u - 0.5f) * 2 * MathF.PI * c;
            lp = new Vector3(r * MathF.Sin(th), (0.5f - v) * h, r - r * MathF.Cos(th));
        }
        else lp = new Vector3((u - 0.5f) * w, (0.5f - v) * h, 0);
        var wp = Vector3.Transform(lp, world);
        var hand = new Vector3(0.25f, 1.2f, 0.1f);
        if (!LocalRaycast.Intersect(world, w, c, ts.X, ts.Y, hand, Vector3.Normalize(wp - hand), out _, out var uv, out _, out _)) return new Vector2(-1, -1);
        return uv * ts;
    }

    public static int Checks(Gpu gpu, string dir)
    {
        int problems = 0;
        void Want(string line, bool ok) { if (!ok) problems++; Log.Info(line); }
        const float Others = GazeFocus.OthersDefault;
        var motion0 = Motion.Level;
        Motion.Level = MotionLevel.Full;
        try
        {
            // ---- the start: the primary (middle) monitor focused, the sides small beside it ----
            var r = new Rig(gpu, dir, 3);
            string saved0 = r.SavedText();
            r.Run(1);
            bool mid = r.FocusName == "Middle";
            bool rest = RowOk(r, Others, out string why);
            var looked = LookedAt(r, out _);
            r.Yaw = 30; r.Step();
            var lookedR = LookedAt(r, out float uR);
            r.Yaw = 0; r.Step();
            float rowW = 0, savedW = 0;
            foreach (var p in r.Panels) { rowW += Drawn(p).width; savedW += p.Cfg.Width; }
            Want($"focus start: the primary (middle) monitor is focused {mid} (want True); at rest {rest} (want True): {why}; looking ahead meets the " +
                 $"middle {looked == r["Middle"]} (want True), 30 deg right the small right one {lookedR == r["Right"]} (want True) at {uR:0.00} of its width; " +
                 $"the row {rowW:0.00} m of {savedW:0.00} m saved; one row made {r.Focus.Rebuilds} (want 1)", mid && rest && looked == r["Middle"] && lookedR == r["Right"] && r.Focus.Rebuilds == 1);

            // ---- the dwell: a glance does not switch, 0.4 s does; the new one grows where it is (its centre stays under the look) ----
            r.Yaw = 30; r.Run(0.06);
            bool glance = r.FocusName == "Middle";
            r.Yaw = 0; r.Run(0.5);
            r.ResetTrack();
            var cRight0 = Drawn(r["Right"]).world.Translation;                      // the small right one, where you look
            r.Yaw = 30; r.Run(0.4);
            bool switched = r.FocusName == "Right";
            float stayMid = Vector3.Distance(Drawn(r["Right"]).world.Translation, cRight0);
            r.Run(0.4);
            bool rightRest = RowOk(r, Others, out string whyR);
            float stay = Vector3.Distance(Drawn(r["Right"]).world.Translation, cRight0);
            var lookedNow = LookedAt(r, out float uNow);
            Want($"focus dwell: a 0.06 s glance at the right monitor keeps the middle {glance} (want True); 0.4 s focuses the right one {switched} (want True), " +
                 $"settled {rightRest} (want True): {whyR}; its centre moved {stayMid * 100:0.0} cm mid-way and {stay * 100:0.0} cm in all (want < 2: it grows " +
                 $"about its centre, what you look at stays under the look - the look is at {uNow:0.00} of it now, {(lookedNow == r["Right"] ? "still on it" : "NOT on it")}); " +
                 $"the largest step of a centre {r.MaxMove * 100:0.00} cm, of a width {r.MaxGrow * 100:0.00} cm per frame (want < 3)",
                glance && switched && rightRest && stay < 0.02f && stayMid < 0.02f && lookedNow == r["Right"] && r.MaxMove < 0.03f && r.MaxGrow < 0.03f);

            // ---- back to the middle, then dithering: around the edge band, and across it but back before the dwell (0.12 s) ----
            r.Yaw = 0; r.Run(0.8);
            bool back = r.FocusName == "Middle" && RowOk(r, Others, out _);
            int sw0 = r.Focus.Switches;
            for (int i = 0; i < 30; i++) { r.Yaw = i % 2 == 0 ? 19 : 24; r.Run(0.1); }
            bool band = r.Focus.Switches == sw0;
            for (int i = 0; i < 30; i++) { r.Yaw = i % 2 == 0 ? 19 : 32; r.Run(0.1); }
            bool across = r.Focus.Switches == sw0;
            r.Yaw = 0; r.Run(0.3);
            // looking at nothing (the room, 120 deg away; the keyboard: no monitor under the ray) keeps it; a laser on the keyboard too
            r.Yaw = 120; r.Run(2);
            bool nothing = r.FocusName == "Middle" && r.Focus.Switches == sw0;
            r.Yaw = -70; r.Run(1);                              // straight down-left-ish: still no monitor
            Want($"focus hysteresis: back on the middle {back} (want True); 3 s dithering 19 / 24 deg (the right one's edge band) never switches {band} " +
                 $"(want True); 3 s dithering 19 / 32 deg across the edge, 0.1 s each, never switches {across} (want True); 2 s looking at the room keeps it " +
                 $"{nothing} (want True), focus {r.FocusName}", back && band && across && nothing);

            // ---- a laser: a short dwell; a mouse button held: no switch (the look on the room meanwhile) ----
            r.Yaw = 0; r.Run(0.3);
            r.Yaw = 120;
            r.Laser = r["Right"]; r.Run(0.1);
            bool laserWait = r.FocusName == "Middle";
            r.Run(0.1);
            bool laserGo = r.FocusName == "Right";
            r.Laser = null; r.Run(0.6);
            r.Hold = true; r.Laser = r["Middle"]; r.Yaw = 0; r.Run(1);
            bool held = r.FocusName == "Right";
            r.Hold = false; r.Run(0.3);
            bool afterHold = r.FocusName == "Middle";
            r.Laser = null; r.Yaw = 0; r.Run(0.6);
            Want($"focus laser: on the right monitor 0.1 s - not yet {laserWait} (want True), 0.2 s - focused {laserGo} (want True); a mouse button held " +
                 $"while the laser and the look are on the middle: no switch for 1 s {held} (want True), let go: switched {afterHold} (want True)",
                laserWait && laserGo && held && afterHold);

            // ---- a switch mid-way (the laser 0.05 s after a look started a switch): continuous ----
            r.ResetTrack();
            r.Yaw = 30; r.Run(0.14);                            // the switch to the right just started (the dwell is 0.12 s)
            bool started = r.FocusName == "Right";
            r.Laser = r["Left"]; r.Run(0.2);                    // mid-way: the laser takes it to the left
            bool midway = r.FocusName == "Left";
            r.Laser = null; r.Yaw = -40; r.Run(0.6);
            bool leftRest = RowOk(r, Others, out string whyL);
            Want($"focus mid-way: a switch to the right started {started} (want True), the laser took it to the left 0.2 s in {midway} (want True), settled left " +
                 $"{leftRest} (want True): {whyL}; the largest step of a centre {r.MaxMove * 100:0.00} cm, of a width {r.MaxGrow * 100:0.00} cm per frame (want < 3)",
                started && midway && leftRest && r.MaxMove < 0.03f && r.MaxGrow < 0.03f);

            // ---- theater holds it; off: it goes on ----
            r.Yaw = 0; r.Run(0.8);
            string before = r.FocusName;
            var viewsBefore = new List<Matrix4x4>(); foreach (var p in r.Panels) viewsBefore.Add(Drawn(p).world);
            r["Middle"].Theater = new TheaterView { World = Matrix4x4.CreateTranslation(0, 1.5f, -2.5f), Width = 2.8f, Curvature = 0.1f };
            r.Theater = true; r.Yaw = 30; r.Run(1.5);
            bool heldTh = r.FocusName == before;
            bool still = true; for (int i = 0; i < r.Panels.Count; i++) still &= Drawn(r.Panels[i]).world == viewsBefore[i];
            r["Middle"].Theater = null; r.Theater = false;
            r.ResetTrack();
            r.Run(0.5);
            bool resumed = r.FocusName == "Right";
            r.Run(0.4);
            Want($"focus theater: 1.5 s of theater with the look on the right monitor - the focus stays on the {before} {heldTh} (want True) and nothing moves {still} (want True); " +
                 $"theater off: the right one focused after the dwell {resumed} (want True), the largest step {r.MaxMove * 100:0.00} cm (want < 3)",
                heldTh && still && resumed && r.MaxMove < 0.03f);

            // ---- the saved layout: untouched by all of it ----
            bool untouched = r.SavedText() == saved0;
            Want($"focus saved layout: unchanged after everything above {untouched} (want True)", untouched);

            // ---- Motion reduced: a short linear move; off: at once ----
            Motion.Level = MotionLevel.Reduced;
            r.Yaw = 0; r.Run(0.14);                             // the switch to the middle happens now
            bool swR = r.FocusName == "Middle";
            int frames = 0;
            while (frames < 60 && !RowOk(r, Others, out _)) { r.Step(); frames++; }
            r.ResetTrack(); r.Yaw = 30; r.Run(0.3);
            while (r.FocusName != "Right") r.Step();            // the frame of the switch takes the first step
            float step1 = r.MaxGrow; r.ResetTrack(); r.Step(); r.Step(); float step2 = r.MaxGrow;      // (a reset takes a frame as its base)
            r.Run(0.3);
            Motion.Level = MotionLevel.Off;
            r.Yaw = 0; r.Run(0.14);
            bool offRow = RowOk(r, Others, out string whyO);
            bool offNow = r.FocusName == "Middle" && offRow;
            Motion.Level = MotionLevel.Full;
            Want($"focus motion: reduced - switched {swR} (want True) and at the target {frames} frames later (want <= 20), the widths' steps even " +
                 $"({step1 * 100:0.00} then {step2 * 100:0.00} cm per frame, want equal); off - the row at its target the frame the focus switched {offNow} (want True): {whyO}",
                swR && frames <= 20 && MathF.Abs(step1 - step2) < 1e-4f && offNow);

            // ---- a grab in focus mode against the same grab without the feature: the saved layout comes out the same ----
            {
                var a = new Rig(gpu, dir, 3);
                var b = new Rig(gpu, dir, 3, on: false);
                a.Yaw = 30; a.Run(1);                           // the right one focused, the row drawn narrow
                b.Run(0.1);
                bool rightA = a.FocusName == "Right";
                string ma = GrabRun(a, "Middle"), mb = GrabRun(b, "Middle");
                bool same = ma == mb && rightA;
                a.ResetTrack(); a.Yaw = 30; a.Run(0.8);
                bool resume = RowOk(a, Others, out string whyG) && a.FocusName == "Right";
                Want($"focus grab: the middle monitor grabbed and moved 45 frames while the right one is focused - every saved place, width and bend equals the " +
                     $"same grab with the feature off {same} (want True); the largest step of a drawn centre past the hand's own move while it was held " +
                     $"{a.MaxMoveGrab * 100:0.00} cm (want < 3); the focus " +
                     $"back after {resume} (want True): {whyG}", same && resume && a.MaxMoveGrab < 0.03f);
            }

            // ---- the laser's pixel: aimed at a pixel of the small right monitor, the hit maps to that pixel - as on the full-size one ----
            {
                var px = new Vector2(0.3f * 2560, 0.6f * 1440);
                var small = PixelThrough(r["Right"], px, drawn: true);
                var full = PixelThrough(r["Right"], px, drawn: false);
                bool mapped = Vector2.Distance(small, px) < 0.5f && Vector2.Distance(full, px) < 0.5f;
                Want($"focus laser pixel: aimed at ({px.X:0}, {px.Y:0}) of the right monitor drawn {Drawn(r["Right"]).width:0.00} m wide the hit says " +
                     $"({small.X:0.0}, {small.Y:0.0}), at its saved {r["Right"].Cfg.Width:0.00} m ({full.X:0.0}, {full.Y:0.0}) {mapped} (want True; ScreenPoint scales by the texture, not the width)", mapped);
            }

            // ---- a row of two; a lone monitor ----
            {
                var two = new Rig(gpu, dir, 2);
                two.Run(0.6);
                bool row2 = RowOk(two, Others, out string why2);
                bool start2 = two.FocusName == "Middle" && row2;
                two.Yaw = 12; two.Run(0.8);                     // the row's middle is between the two: the small right one spans 0 .. 24 deg
                bool row2b = RowOk(two, Others, out string why2b);
                bool right2 = two.FocusName == "Right" && row2b;
                var lone = new Rig(gpu, dir, 2);
                lone["Right"].Cfg.Enabled = false;              // one monitor left in the group: no row
                lone.Run(0.3);
                bool none = !lone.Focus.Active && lone["Middle"].Focus == null;
                Want($"focus two: the middle focused {start2} (want True): {why2}; the right after a look {right2} (want True): {why2b}; a lone monitor is not a row " +
                     $"{none} (want True)", start2 && right2 && none);
            }

            // ---- no drift: monitors of three widths (the only way a row could wander), the look going round for a while - every time the
            // middle is focused the row is back where it was, exactly; a side one grows where it is ----
            {
                var d = new Rig(gpu, dir, 3) { Pitch = -8 };    // the 0.7 m one's small self is 22 cm tall: the look a little down, onto it
                d["Left"].Cfg.Width = 1.1f; d["Right"].Cfg.Width = 0.7f;
                d.Layout.Reflow(d["Middle"]);
                d.Run(1);
                var home0 = Centres(d);
                d.Yaw = 30; d.Step();
                var meets = LookedAt(d, out float uMeet);
                d.Yaw = 0; d.Step();
                float wander = 0, sideStay = 0;
                Vector3 rightAt = default, leftAt = default;
                bool sides = true;
                for (int cycle = 0; cycle < 4; cycle++)
                {
                    d.Yaw = 30; d.Run(0.8);
                    sides &= d.FocusName == "Right";
                    var cR = Drawn(d["Right"]).world.Translation;
                    if (cycle > 0) sideStay = MathF.Max(sideStay, Vector3.Distance(cR, rightAt));
                    rightAt = cR;
                    d.Yaw = -30; d.Run(0.8);
                    sides &= d.FocusName == "Left";
                    var cL = Drawn(d["Left"]).world.Translation;
                    if (cycle > 0) sideStay = MathF.Max(sideStay, Vector3.Distance(cL, leftAt));
                    leftAt = cL;
                    d.Yaw = 30; d.Run(0.8);
                    d.Yaw = 0; d.Run(0.8);
                    sides &= d.FocusName == "Middle";
                    wander = MathF.Max(wander, Farthest(Centres(d), home0));
                }
                bool homeOk = RowOk(d, Others, out string whyD) && d.FocusName == "Middle";
                Want($"focus drift: widths 1.1 / 0.9 / 0.7 m, the look 30 deg right and 8 down meets the {meets?.Mon.Name ?? "-"} at {uMeet:0.00} of it; " +
                     $"the look round right - left - right - middle 4 times, every focus taken {sides} (want True); " +
                     $"back on the middle the row is where it started, {wander * 100:0.0} cm off at most (want < 1), {homeOk} (want True): {whyD}; a side one " +
                     $"settles within {sideStay * 100:0.0} cm of where it was the time before (the row is anchored to the middle: it cannot wander off)",
                    sides && homeOk && wander < 0.01f && sideStay < 0.1f);
            }

            // ---- leaving a row: Separate in the settings, and B / Y pulling the small right one out - each eases to its saved place, no jump ----
            {
                var s = new Rig(gpu, dir, 3);
                s.Run(1);
                s.ResetTrack();
                s.Layout.SeparateAll();
                s.Run(0.5);
                bool apart = !s.Focus.Active;
                foreach (var p in s.Panels) apart &= p.Focus == null && Drawn(p).world == Saved(p).world;
                float sepMove = s.MaxMove, sepGrow = s.MaxGrow;
                var g = new Rig(gpu, dir, 3);
                g.Yaw = 0; g.Run(1);
                var right = g["Right"];
                var drawn0 = Drawn(right).world.Translation;
                g.ResetTrack(relative: true);
                var aim0 = Matrix4x4.CreateRotationX(-0.3f) * Matrix4x4.CreateTranslation(0.3f, 1.2f, -0.1f);
                right.GrabBegin(detach: true);
                var rel = right.WorldTransform * MathUtil.Invert(aim0);
                for (int k = 1; k <= 45; k++)
                {
                    // 36 cm to the right: past the 20 cm the snap reaches, or letting go would put it straight back into the row
                    var aim = Matrix4x4.CreateRotationX(-0.3f) * Matrix4x4.CreateTranslation(0.3f + 0.008f * k, 1.2f, -0.1f);
                    right.GrabMove(rel * aim);
                    g.Step();
                }
                float pullMove = g.MaxMove;
                bool alone = right.Cfg.Group == null && right.Focus == null && Drawn(right).world == Saved(right).world;
                right.GrabEnd();
                g.Run(0.6);
                bool restRow = RowOk(g, Others, out string whyP) && g.Focus.FocusedOf(g["Middle"]) == g["Middle"] && g.Focus.FocusedOf(right) == null;
                Want($"focus leaving: Separate - every monitor eased to its saved place and was let go {apart} (want True), the largest step {sepMove * 100:0.00} cm, " +
                     $"{sepGrow * 100:0.00} cm of width (want < 3); B / Y pulling the small right one out while held 45 frames - the largest step past the hand's " +
                     $"own move {pullMove * 100:0.00} cm (want < 3), on its own at its saved size by then {alone} (want True); let go: the two left focus the middle " +
                     $"{restRow} (want True): {whyP}", apart && sepMove < 0.03f && sepGrow < 0.03f && pullMove < 0.03f && alone && restRow);
            }

            // ---- a lone monitor grabbed beside a focused row: the row at its saved sizes while it is held (its edges where the snap goes), back after ----
            {
                var q = new Rig(gpu, dir, 4);
                q.Run(1);
                var lone = q["Lone"];
                bool rowQ0 = RowOk(q, Others, out string whyQ0);
                bool loneFree = lone.Focus == null && rowQ0;
                q.ResetTrack();
                lone.GrabBegin(detach: false);
                q.Run(0.5);
                bool plain = lone.Focus == null;
                foreach (var p in q.Panels) if (p.Cfg.Group != null) plain &= Vector3.Distance(Drawn(p).world.Translation, Saved(p).world.Translation) < 0.001f && MathF.Abs(Drawn(p).width - p.Cfg.Width) < 1e-4f;
                float holdMove = q.MaxMove;
                lone.GrabEnd();
                q.Run(0.6);
                bool backQ = RowOk(q, Others, out string whyQ) && q.FocusName == "Middle";
                Want($"focus lone grab: a fourth monitor beside the row is not drawn by it {loneFree} (want True): {whyQ0}; held: the row at its saved places and " +
                     $"sizes {plain} (want True), the largest step {holdMove * 100:0.00} cm (want < 3); let go: the focus layout again {backQ} (want True): {whyQ}",
                    loneFree && plain && holdMove < 0.03f && backQ);
            }

            // ---- the setting off: the row eases back and lets go; on again: eases in ----
            r.Yaw = 0; r.Run(0.6);
            r.ResetTrack();
            r.Cfg.Enabled = false; r.Run(0.5);
            bool letGo = !r.Focus.Active;
            bool atSaved = true;
            foreach (var p in r.Panels) atSaved &= p.Focus == null && Drawn(p).world == Saved(p).world;
            r.Cfg.Enabled = true; r.Run(0.5);
            bool onAgain = RowOk(r, Others, out string whyOn) && r.FocusName == "Middle";
            Want($"focus setting: off - eased back ({r.MaxMove * 100:0.00} cm per frame at most, want < 3) and let go {letGo && atSaved} (want True); on again - the " +
                 $"focus layout on the primary {onAgain} (want True): {whyOn}", letGo && atSaved && onAgain && r.MaxMove < 0.03f);

            // ---- the slider: the others follow it at once; Recenter (the saved row moved rigidly): the override follows ----
            r.Cfg.Others = 0.7f; r.Run(0.1);
            bool slid = RowOk(r, 0.7f, out string whyS);
            r.Cfg.Others = Others;
            var shift = Matrix4x4.CreateRotationY(0.2f) * Matrix4x4.CreateTranslation(0.1f, 0.05f, -0.2f);
            int rb0 = r.Focus.Rebuilds;
            foreach (var p in r.Panels) p.Cfg.Place.M = MathUtil.ToArray(MathUtil.FromArray(p.Cfg.Place.M) * shift);
            // theater's big screen placed at once, as OrbitApp.Recenter does it (before the focus's update of the frame): on the line through
            // the middle monitor's saved place as moved, not through where the focus drew it the frame before
            var headNow = r.Vr.Hmd.Translation;
            var bigAt = Theater.Where(r["Middle"], r.Vr, 2.5f).Translation;
            float bigOff = Vector3.Distance(bigAt, headNow + Vector3.Normalize(Saved(r["Middle"]).world.Translation - headNow) * 2.5f);
            bool lined = bigOff < 0.01f;
            r.Run(0.05);
            bool followed = RowOk(r, Others, out string whyM) && r.Focus.Rebuilds == rb0;
            Want($"focus follows: Others 70 % {slid} (want True): {whyS}; the saved row moved (Recenter) - the drawn row with it, no row rebuilt {followed} " +
                 $"(want True): {whyM}; theater's big screen on the line through the moved row {bigOff * 100:0.00} cm off it {lined} (want True)", slid && followed && lined);

            // ---- cost: the update alone, at rest and switching; nothing allocated; the views still at rest ----
            {
                r.Yaw = 0; r.Run(1);
                var views = new List<(Matrix4x4 w, float wd, float c)>(); foreach (var p in r.Panels) views.Add(Drawn(p));
                r.Ticks = 0; r.Frames = 0;
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                int changes = 0;
                for (int i = 0; i < 2000; i++)
                {
                    r.Step();
                    for (int k = 0; k < r.Panels.Count; k++) if (Drawn(r.Panels[k]) != views[k]) changes++;
                }
                long restAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                double restUs = r.Ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / r.Frames;
                r.Ticks = 0; r.Frames = 0;
                a0 = GC.GetAllocatedBytesForCurrentThread();
                DesktopPanel right = r["Right"], middle = r["Middle"];
                for (int i = 0; i < 200; i++) { r.Laser = (i / 20) % 2 == 0 ? right : middle; r.Step(); }      // warm
                r.Ticks = 0; r.Frames = 0;
                a0 = GC.GetAllocatedBytesForCurrentThread();
                int sw1 = r.Focus.Switches;
                for (int i = 0; i < 2000; i++) { r.Laser = (i / 20) % 2 == 0 ? right : middle; r.Step(); }     // a switch every 0.22 s: always moving
                long moveAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                double moveUs = r.Ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / r.Frames;
                r.Laser = null;
                Want($"focus cost: 2000 frames at rest {restUs:0.00} us per frame, {restAlloc} bytes allocated (want 0), views changed {changes} times (want 0: the " +
                     $"overlays get nothing); 2000 frames switching ({r.Focus.Switches - sw1} switches, always moving) {moveUs:0.00} us per frame, {moveAlloc} bytes " +
                     $"(want 0)", restAlloc == 0 && changes == 0 && moveAlloc == 0 && r.Focus.Switches - sw1 > 50);
            }

            // ---- the setting: off writes nothing (a config saves as before), on is written ----
            {
                var store = new ConfigStore(Path.Combine(dir, "focus_setting_config.json"));
                bool clean = !store.Serialized().Contains("\"Focus\"");
                store.Data.Focus = new FocusConfig { Enabled = true };
                bool written = store.Serialized().Contains("\"Focus\"") && store.Serialized().Contains("\"Others\"");
                Want($"focus config: off writes nothing {clean} (want True), on is written {written} (want True)", clean && written);
            }
        }
        finally { Motion.Level = motion0; }
        Log.Info($"focus: {(problems == 0 ? "every check as wanted" : $"{problems} checks NOT as wanted")}");
        return problems;
    }

    // the laser's grab, as Laser.cs does it: the hand's offset from the panel's WorldTransform right after GrabBegin, then the hand moves
    // 45 frames (a push to the right, a little up and a turn); the saved layout after
    static string GrabRun(Rig r, string which)
    {
        var p = r[which];
        var aim0 = Matrix4x4.CreateRotationX(-0.3f) * Matrix4x4.CreateTranslation(0.3f, 1.2f, -0.1f);
        p.GrabBegin(detach: false);
        var rel = p.WorldTransform * MathUtil.Invert(aim0);
        r.ResetTrack(relative: true);                  // the saved row moves with the hand: what the focus adds on top of that
        for (int k = 1; k <= 45; k++)
        {
            var aim = Matrix4x4.CreateRotationX(-0.3f) * Matrix4x4.CreateRotationY(0.004f * k) * Matrix4x4.CreateTranslation(0.3f + 0.004f * k, 1.2f + 0.002f * k, -0.1f);
            p.GrabMove(rel * aim);
            r.Step();
        }
        r.MaxMoveGrab = r.MaxMove;
        p.GrabEnd();
        return r.SavedText(withRow: false);
    }

    // ---- the pictures ----

    // a top-down strip of the row over time: the saved row grey, the drawn row in the accent, the head and its ray
    public static void Strip(Gpu gpu, Painter p, string dir)
    {
        var r = new Rig(gpu, dir, 3);
        r.Run(1);
        var frames = new List<(List<(Matrix4x4 w, float wd, float c)> drawn, Matrix4x4 head, string focus, string note, double t)>();
        void Snap(string note)
        {
            var l = new List<(Matrix4x4, float, float)>();
            foreach (var m in r.Panels) l.Add(Drawn(m));
            frames.Add((l, r.Vr.Hmd, r.FocusName, note, r.T));
        }
        double t0 = r.T;
        Snap("at rest, looking ahead");
        r.Yaw = 30; r.Run(0.14); Snap("looked right 0.12 s: switching");
        r.Run(0.12); Snap("0.12 s into the change");
        r.Run(0.12); Snap("0.24 s");
        r.Run(0.4); Snap("settled on the right one (grew where it was)");
        r.Yaw = -18; r.Run(0.45); Snap("looked left 0.45 s: mid-way");
        r.Run(0.6); Snap("settled on the left one");
        // with the left one grown where it was, the row sits centred ahead: the small middle one's centre is ~10 deg to the right (a look
        // exactly ahead lands on their shared edge - the edge band keeps it, as it should)
        r.Yaw = 10; r.Run(0.8); Snap("looked at the middle one: back where it started");
        const int ColW = 300, H = 440;
        const float Scale = 118;
        using var surf = new Surface(gpu, ColW * frames.Count, H);
        var miss = Lang.Miss; Lang.Miss = null;              // a diagram's labels, not the UI's words
        p.Begin(surf.Bitmap, Theme.Bg);
        var saved = new List<(Matrix4x4, float, float)>();
        foreach (var m in r.Panels) saved.Add(Saved(m));
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            float cx = i * ColW + ColW / 2f, cy = H - 60;
            Vector2 At(Vector3 w) => new(cx + (w.X - HeadPos.X) * Scale, cy + (w.Z - HeadPos.Z) * Scale);
            if (i > 0) p.Fill(new RectangleF(i * ColW, 0, 1, H), Theme.Border);
            foreach (var (w, wd, c) in saved) Arc(p, w, wd, c, At, Theme.Faint, 2);
            foreach (var (w, wd, c) in f.drawn) Arc(p, w, wd, c, At, Theme.Accent, 5);
            // the head and where it looks: the ray to the first monitor it meets as drawn (or 1.6 m out)
            var o = f.head.Translation; var d = MathUtil.Forward(f.head);
            float len = 1.6f;
            foreach (var (w, wd, c) in f.drawn)
                if (LocalRaycast.Intersect(w, wd, c, 2560, 1440, o, d, out float dist, out _, out _, out _)) len = MathF.Min(len, dist);
            var a = At(o); var b = At(o + d * len);
            p.Line(a.X, a.Y, b.X, b.Y, Theme.Warn, 2);
            p.Circle(a.X, a.Y, 7, Theme.Text);
            p.Circle(b.X, b.Y, 4, Theme.Warn);
            p.Text($"t = {f.t - t0:0.00} s", new RectangleF(i * ColW + 12, 10, ColW - 24, 22), Theme.Text, 15, Vortice.DirectWrite.FontWeight.SemiBold);
            p.Text(f.note, new RectangleF(i * ColW + 12, 34, ColW - 24, 20), Theme.Dim, 12);
            p.Text("focus: " + f.focus, new RectangleF(i * ColW + 12, 56, ColW - 24, 20), Theme.Accent, 13);
        }
        p.Text("saved layout", new RectangleF(12, H - 24, 120, 16), Theme.Faint, 11);
        p.Text("drawn", new RectangleF(110, H - 24, 80, 16), Theme.Accent, 11);
        p.Text("head and its look", new RectangleF(160, H - 24, 140, 16), Theme.Warn, 11);
        p.End();
        Lang.Miss = miss;
        surf.SavePng(gpu, Path.Combine(dir, "strip.png"));
    }

    // a monitor from above: its arc (or line) sampled along its width
    static void Arc(Painter p, in Matrix4x4 w, float width, float c, Func<Vector3, Vector2> at, uint color, float thick)
    {
        const int N = 16;
        Vector2 prev = default;
        for (int k = 0; k <= N; k++)
        {
            float s = (k / (float)N - 0.5f) * width;
            Vector3 lp;
            if (c > 0) { float r = width / (2 * MathF.PI * c), th = s / r; lp = new Vector3(r * MathF.Sin(th), 0, r - r * MathF.Cos(th)); }
            else lp = new Vector3(s, 0, 0);
            var q = at(Vector3.Transform(lp, w));
            if (k > 0) p.Line(prev.X, prev.Y, q.X, q.Y, color, thick);
            prev = q;
        }
    }

    // the setting in the Desktops page: clicked in Modern and in the 95 sheet, its texts against their boxes (Modern, Cyber), the page in
    // every style with it on
    public static int Pictures(SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg, string dir)
    {
        int problems = 0;
        void Want(string line, bool ok) { if (!ok) problems++; Log.Info(line); }
        var look0 = cfg.Data.Appearance;
        var focus0 = cfg.Data.Focus;
        void Look(string style) { cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true }; Theme.Apply(cfg.Data.Appearance); }
        void FrameM() { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
        void ClickM(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); FrameM(); ptr.Press(); FrameM(); ptr.Release(); FrameM(); }
        void Frame95() => view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
        void Click95(float x, float y) { ptr.Move(x * 2, y * 2); Frame95(); ptr.Press(); Frame95(); ptr.Release(); Frame95(); Frame95(); }
        void Settle(Action frame) { ptr.Leave(); frame(); ui.ResetAnimations(); frame(); }
        void Save(string name) => surf.SavePng(gpu, Path.Combine(dir, name + ".png"));
        try
        {
            view.Tab = SettingsView.TabDesktops;
            // Modern: the switch under Arrangement, on and off again
            Look("modern");
            cfg.Data.Focus = null;
            ui.ResetAnimations();
            FrameM();
            ClickM(608 + 188, 542 + 23);
            bool onM = cfg.Data.Focus?.Enabled == true;
            Settle(FrameM);
            Save("desktops_modern");
            ClickM(608 + 188, 542 + 23);
            bool offM = cfg.Data.Focus?.Enabled == false;
            // 95: the check box under the row hint
            Look("classic");
            cfg.Data.Focus = null;
            ui.ResetAnimations();
            Frame95();
            Click95(388 + 8, 366 + 6);
            bool on95 = cfg.Data.Focus?.Enabled == true;
            Click95(388 + 8, 366 + 6);
            bool off95 = cfg.Data.Focus?.Enabled == false;
            Want($"focus settings: Modern's switch turns it on {onM} (want True) and off {offM} (want True); the 95 check box on {on95} (want True) and off {off95} (want True)",
                onM && offM && on95 && off95);

            // the texts of the lower part (capture, arrangement, focus) against their boxes, Modern and Cyber
            cfg.Data.Focus = new FocusConfig { Enabled = true };
            foreach (var (style, tag) in new[] { ("modern", "modern"), (CyberHud.Id, "cyber") })
            {
                Look(style);
                var spied = new List<(string s, RectangleF r, float size, Vortice.DirectWrite.FontWeight w, string fam, bool wrap,
                    Vortice.DirectWrite.TextAlignment h, Vortice.DirectWrite.ParagraphAlignment v, float tr)>();
                painter.FitSpy = (s, r, size, w, fam, wrap, h, v, tr) => spied.Add((s, r, size, w, fam, wrap, h, v, tr));
                ptr.Leave();
                ui.ResetAnimations();
                FrameM();
                painter.FitSpy = null;
                if (tag == "cyber") Save("desktops_cyber");
                int bad = 0;
                RectangleF right = new(600, 474, 392, 162), hint = new(222, 594, 380, 42);     // the Arrangement column, the row hint moved left
                foreach (var x in spied)
                {
                    if (x.fam == Theme.IconFont || string.IsNullOrWhiteSpace(x.s) || !(right.IntersectsWith(x.r) || hint.IntersectsWith(x.r))) continue;
                    float o = painter.Overhang(x.s, x.r, x.size, x.w, x.fam, x.wrap, x.h, x.v, x.tr);
                    if (o >= 1) { bad++; Log.Warn($"focus fit {style}: '{x.s}' sticks out {o:0.#} px of its {x.r.Width:0} x {x.r.Height:0} box"); }
                }
                Want($"focus fit {style}{(Lang.English ? "" : " " + Lang.Code)}: the focus switch's, the slider's, the Arrangement's and the row hint's texts sticking out of their box {bad} (want 0)", bad == 0);
            }
            // every retro style: the Desktops sheet with it ticked
            foreach (var sk in Skins.All)
            {
                Look(sk.Id);
                Settle(Frame95);
                Save("desktops_" + sk.Tag);
            }
        }
        finally
        {
            cfg.Data.Focus = focus0;
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
    // ORBIT_SNAP_ONLY=focus: gaze focus's checks, its strip, the setting clicked and the Desktops page in every style - in seconds, no monitor captured
    static int FocusOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = SnapCulture;
        string fdir = Path.Combine(dir, "focus");
        Directory.CreateDirectory(fdir);
        int problems = GazeFocusTools.Checks(gpu, fdir);
        GazeFocusTools.Strip(gpu, painter, fdir);
        // the Desktops page with the made-up row (the settings show "joined row of 3")
        var mons = GazeFocusTools.FakeMonitors(3);
        for (int i = 0; i < mons.Count; i++)
            if (cfg.Data.Desktops.FindIndex(d => d.Id == mons[i].Id) < 0)
                cfg.Data.Desktops.Add(new DesktopConfig { Id = mons[i].Id, Name = mons[i].Name, Group = "fakerow", GroupIndex = i, Curvature = 0.12f });
        var host = new FakeHost { Cfg = cfg, GpuName = gpu.AdapterName, MonitorList = mons, Data = data, Wrist = wrist };
        using (var surf = new Surface(gpu, 1500, 960))
            problems += GazeFocusTools.Pictures(new SettingsView(host), new Ui(painter), surf, new PointerState(), painter, gpu, cfg, fdir);
        CultureInfo.CurrentCulture = culture;
        Log.Info($"snapshot (gaze focus only) written to {fdir}: {(problems == 0 ? "every check as wanted" : $"{problems} NOT as wanted")}");
        return problems == 0 ? 0 : 1;
    }
}
