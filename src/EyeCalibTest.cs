using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace Orbit;

// The eye calibration's checks (Sys/EyeCalib.cs, Erik 10-24) - CPU only, no VR (--eyechecks; --eyechecks calib: these alone). A made-up
// tracker that reports the look with a known error (a bias, a vertical gain: worse near the top and the bottom, its noise, a stray reading
// now and then) is calibrated through the whole path - EyeGaze.Feed with made-up FaceStates, EyeCalibRun's points by themselves or by the
// trigger, the head moving a little meanwhile - and the fit must give that error back within 0.1 deg; with the calibration on, looks
// anywhere within 15 deg come out where they truly were (within 0.1 deg). A look that never rests on the dots, or rests 10 deg beside
// them, keeps nothing; a reading costs 0 B; the config writes nothing until calibrated and reads one back as it was.
static class EyeCalibTools
{
    const float Deg = MathF.PI / 180;

    // the tracker of a check: the true look in the head -> what it reports (yaw' = GY yaw + BY, pitch' = GP pitch + BP, degrees; noise
    // Sigma an axis; Stray of the readings thrown 5 deg off)
    sealed class Tracker
    {
        public float BY, BP, GY = 1, GP = 1, Sigma, Stray;
        public Random Rng = new(1);

        public Vector3 Read(Vector3 truth, bool noisy = true)
        {
            float y = EyeCalib.Yaw(truth) / Deg, p = EyeCalib.Pitch(truth) / Deg;
            y = GY * y + BY; p = GP * p + BP;
            if (noisy && Sigma > 0) { y += Gauss(Rng) * Sigma; p += Gauss(Rng) * Sigma; }
            if (noisy && Stray > 0 && Rng.NextDouble() < Stray) { double a = Rng.NextDouble() * 2 * Math.PI; y += 5 * (float)Math.Cos(a); p += 5 * (float)Math.Sin(a); }
            return EyeCalib.Dir(y * Deg, p * Deg);
        }
    }

    static float Gauss(Random r) => (float)(Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble()));

    // the eyes as Virtual Desktop's FaceState has them: both looking along dir, a little below and in front of the head's middle
    static readonly Vector3 EyeL = new(-0.032f, -0.01f, -0.02f), EyeR = new(0.032f, -0.01f, -0.02f);

    static unsafe EyeGaze.FaceState Face(Vector3 dir)
    {
        var o = EyeSpot.FromTo(-Vector3.UnitZ, Vector3.Normalize(dir));
        var s = new EyeGaze.FaceState { LeftEyeIsValid = 1, RightEyeIsValid = 1, LeftEyeConfidence = 1, RightEyeConfidence = 1 };
        s.LeftEyePose = new EyeGaze.Pose { Orientation = o, Position = EyeL };
        s.RightEyePose = new EyeGaze.Pose { Orientation = o, Position = EyeR };
        return s;
    }

    static float Angle(Vector3 a, Vector3 b) => EyeSpot.Angle(Vector3.Normalize(a), Vector3.Normalize(b)) / Deg;

    // a whole calibration: 90 frames a second, 72 readings a second of the eyes on the dot (where it is now), the head as headAt has it.
    // trigger: pressed 0.35 s after the dot came to rest (else it moves on by itself); look: degrees the eyes rest beside the dot
    static (EyeCalibRun run, double took) Calibrate(Tracker tr, Func<double, Matrix4x4> headAt, bool trigger, float lookBeside = 0, double maxT = 60)
    {
        var g = new EyeGaze();
        var run = new EyeCalibRun();
        double t = 0, next = 0, restAt = -1;
        run.Start(headAt(0), 0);
        var face = Face(-Vector3.UnitZ);
        var mid = (EyeL + EyeR) / 2;
        while (run.Running && t < maxT)
        {
            t += 1 / 90.0;
            var head = headAt(t);
            run.Tick(t);
            if (run.State == EyeCalibRun.Phase.Rest) { if (restAt < 0) restAt = t; } else restAt = -1;
            if (trigger && restAt >= 0 && t - restAt >= 0.35) run.Mark(t);
            if (next <= t)
            {
                next += 1 / 72.0;
                var dot = run.Dot(t, out _);
                var at = Vector3.Transform(new Vector3(dot.X, dot.Y, 0), run.Board);
                var truth = Vector3.Normalize(Vector3.TransformNormal(at - Vector3.Transform(mid, head), MathUtil.Invert(head)));
                if (lookBeside != 0) truth = EyeCalib.Dir(EyeCalib.Yaw(truth) + lookBeside * Deg, EyeCalib.Pitch(truth));
                face = Face(tr.Read(truth));
            }
            g.Feed(face, t);
            if (g.Fresh) run.Feed(g.TrackerDir, g.HeadOrigin, head, t);
        }
        return (run, t);
    }

    // looks spread over +-span deg, the tracker's error on them (no noise): how far off EyeGaze puts them with c on - the worst, degrees
    static float WorstAfter(Tracker tr, EyeCalibConfig c, float span, int seed, out float worstBefore, out bool trackerKept)
    {
        var g = new EyeGaze { Calib = c };
        var r = new Random(seed);
        float worst = 0;
        worstBefore = 0;
        trackerKept = true;
        for (int i = 0; i < 40; i++)
        {
            var truth = EyeCalib.Dir(((float)r.NextDouble() * 2 - 1) * span * Deg, ((float)r.NextDouble() * 2 - 1) * span * Deg);
            var read = tr.Read(truth, false);
            g.Feed(Face(read), 1 + i * 0.05);
            worst = MathF.Max(worst, Angle(g.RawDir, truth));
            worstBefore = MathF.Max(worstBefore, Angle(read, truth));
            trackerKept &= Angle(g.TrackerDir, read) < 0.001f;
        }
        return worst;
    }

    static string Show(EyeCalibConfig c) => c == null ? "none" :
        $"yaw {c.Yaw:+0.000;-0.000} pitch {c.Pitch:+0.000;-0.000}" + (c.Linear ? $" linear {c.Xx:0.000} {c.Xy:+0.000;-0.000} / {c.Yx:+0.000;-0.000} {c.Yy:0.000}" : " (bias only)");

    public static int Checks(Action<string, bool> Want)
    {
        int problems = 0;
        void Check(string line, bool ok) { if (!ok) problems++; Want(line, ok); }
        Matrix4x4 Still(double t) => Matrix4x4.CreateTranslation(0.1f, 1.6f, -0.2f);
        // the head drifting while it calibrates: 3 deg of yaw over 10 s, a little pitch, 3 deg of roll, 2 cm sideways
        Matrix4x4 Drift(double t) => Matrix4x4.CreateFromYawPitchRoll((float)(0.3 * t) * Deg, (2 + (float)(0.1 * t)) * Deg, 3 * Deg)
            * Matrix4x4.CreateTranslation(0.1f + (float)(0.002 * t), 1.6f, -0.2f);

        // 1. a constant error (Erik's: the look ~1 deg beside, here 1.1 deg left and 0.8 deg up of the truth), his noise (0.17 deg an axis)
        // and 3 % stray readings (thrown 5 deg), every point by itself, the head drifting - then the same with the sim's worst noise (0.5 deg)
        {
            var tr = new Tracker { BY = -1.1f, BP = 0.8f, Sigma = 0.17f, Stray = 0.03f, Rng = new Random(21) };
            var (run, took) = Calibrate(tr, Drift, trigger: false);
            var c = run.Result;
            bool auto = run.State == EyeCalibRun.Phase.Done && Array.TrueForAll(run.Taken, q => q.Auto);
            float ey = c == null ? 99 : MathF.Abs(c.Yaw - 1.1f), ep = c == null ? 99 : MathF.Abs(c.Pitch + 0.8f);
            float worst = WorstAfter(tr, c, 15, 31, out float before, out bool kept), worstNone = WorstAfter(tr, null, 15, 31, out _, out _);
            var tr5 = new Tracker { BY = -1.1f, BP = 0.8f, Sigma = 0.5f, Rng = new Random(22) };
            var (run5, took5) = Calibrate(tr5, Drift, trigger: false);
            var c5 = run5.Result;
            float ey5 = c5 == null ? 99 : MathF.Abs(c5.Yaw - 1.1f), ep5 = c5 == null ? 99 : MathF.Abs(c5.Pitch + 0.8f);
            Check($"eye calibration, a constant error (1.1 deg left, 0.8 up; noise 0.17 deg, 3 % stray readings, the head drifting 3 deg): " +
                  $"{run.State} in {took:0.0} s, every point by itself {auto} (want Done, under 15 s, True); the fit {Show(c)} - {ey:0.000} / {ep:0.000} " +
                  $"deg off the error (want under 0.1, bias only {c != null && !c.Linear}); looks within 15 deg {before:0.00} deg off without it, " +
                  $"{worst:0.000} with it at worst (want under 0.1; without the calibration {worstNone:0.00}), the tracker's own look kept {kept}; " +
                  $"noise 0.5 deg: {run5.State} in {took5:0.0} s, {Show(c5)} - {ey5:0.000} / {ep5:0.000} off (want Done, under 0.1)",
                auto && took < 15 && ey < 0.1f && ep < 0.1f && c is { Linear: false } && before > 1.3f && worst < 0.1f && kept
                && MathF.Abs(worstNone - before) < 0.001f && run5.State == EyeCalibRun.Phase.Done && ey5 < 0.1f && ep5 < 0.1f);
        }

        // 2. worse near the top and the bottom (the tracker's pitch 0.9 x, then 0.5 deg down; 0.6 deg right), by the trigger: the linear
        // term must come and undo it - looks within 12 deg (the points' span) within 0.1 deg
        {
            var tr = new Tracker { BY = 0.6f, BP = -0.5f, GP = 0.9f, Sigma = 0.17f, Rng = new Random(23) };
            var (run, took) = Calibrate(tr, Still, trigger: true);
            var c = run.Result;
            bool byTrigger = run.State == EyeCalibRun.Phase.Done && Array.TrueForAll(run.Taken, q => !q.Auto);
            float eyy = c == null ? 99 : MathF.Abs(c.Yy - 1 / 0.9f), exx = c == null ? 99 : MathF.Abs(c.Xx - 1),
                cross = c == null ? 99 : MathF.Max(MathF.Abs(c.Xy), MathF.Abs(c.Yx)), oy = c == null ? 99 : MathF.Abs(c.Yaw + 0.6f),
                op = c == null ? 99 : MathF.Abs(c.Pitch - 0.5f / 0.9f);
            float worst = WorstAfter(tr, c, 12, 32, out float before, out _);
            Check($"eye calibration, worse near the top and the bottom (pitch 0.9 x - 0.5 deg, yaw + 0.6), by the trigger: {run.State} in {took:0.0} s, " +
                  $"every point by the trigger {byTrigger} (want Done, True); the fit {Show(c)} - the pitch's gain {eyy:0.000} off 1.111, the yaw's {exx:0.000} " +
                  $"off 1, crosstalk {cross:0.000} (want linear, each under 0.03), the bias {oy:0.000} / {op:0.000} off (want under 0.1); before " +
                  $"{c?.Before ?? 0:0.00} deg, after {c?.After ?? 0:0.00}; looks within 12 deg {before:0.00} deg off without it, {worst:0.000} with it at worst " +
                  $"(want under 0.1)",
                byTrigger && c is { Linear: true } && eyy < 0.03f && exx < 0.03f && cross < 0.03f && oy < 0.1f && op < 0.1f && worst < 0.1f && before > 1.5f);
        }

        // 3. what keeps nothing: eyes that never rest near the dots (15 deg beside them) - given up after PointTimeout; the trigger pressed
        // with the eyes 10 deg beside every dot - too far to be a look at them
        {
            var tr = new Tracker { Sigma = 0.17f, Rng = new Random(24) };
            var (never, tookNever) = Calibrate(tr, Still, trigger: false, lookBeside: 15, maxT: 40);
            var (far, _) = Calibrate(tr, Still, trigger: true, lookBeside: 10);
            Check($"eye calibration, nothing kept: eyes 15 deg beside the dots - {never.State} after {tookNever:0.0} s, why '{never.Why}', a result " +
                  $"{never.Result != null} (want Failed after ~20 s, the no-look reason, False); the trigger pressed 10 deg beside each dot - " +
                  $"{far.State}, why '{far.Why}' (want Failed, the far reason)",
                never.State == EyeCalibRun.Phase.Failed && never.Why == EyeCalib.WhyNoLook && never.Result == null && tookNever > 20 && tookNever < 22
                && far.State == EyeCalibRun.Phase.Failed && far.Why == EyeCalib.WhyFar && far.Result == null);
        }

        // 4. a reading costs nothing: 400 frames of a point resting (EyeGaze.Feed with the calibration on, the run's Feed and Tick)
        {
            var g = new EyeGaze { Calib = new EyeCalibConfig { Yaw = 1, Pitch = -1, Yy = 1.05f } };
            var run = new EyeCalibRun();
            var head = Still(0);
            run.Start(head, 0);
            var faces = new EyeGaze.FaceState[8];
            var rng = new Random(25);
            for (int i = 0; i < faces.Length; i++) faces[i] = Face(EyeCalib.Dir(20 * Deg + Gauss(rng) * 0.003f, Gauss(rng) * 0.003f));   // (20 deg beside: it stays)
            double t = 0;
            for (int i = 0; i < 60; i++) { t += 1 / 90.0; run.Tick(t); g.Feed(faces[i % faces.Length], t); if (g.Fresh) run.Feed(g.TrackerDir, g.HeadOrigin, head, t); }
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 400; i++) { t += 1 / 90.0; run.Tick(t); g.Feed(faces[i % faces.Length], t); if (g.Fresh) run.Feed(g.TrackerDir, g.HeadOrigin, head, t); run.Dot(t, out _); }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
            Check($"eye calibration, the cost: 400 frames of readings through the calibration and a resting point - {bytes} B allocated, still {run.State} " +
                  $"(want 0, Rest)", bytes == 0 && run.State == EyeCalibRun.Phase.Rest);
        }

        // 5. the config: none writes nothing, a calibration reads back as it was; Reset (null) gives the tracker's look back
        {
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string none = System.Text.Json.JsonSerializer.Serialize(new GeneralConfig(), opts);
            var c = new EyeCalibConfig { Yaw = 1.25f, Pitch = -0.75f, Yy = 1.1f, Before = 1.4f, After = 0.08f, Points = 5 };
            string js = System.Text.Json.JsonSerializer.Serialize(new GeneralConfig { EyeCalib = c }, opts);
            var back = System.Text.Json.JsonSerializer.Deserialize<GeneralConfig>(js, opts)?.EyeCalib;
            bool same = back != null && back.Yaw == c.Yaw && back.Pitch == c.Pitch && back.Xx == 1 && back.Yy == c.Yy && back.Before == c.Before
                && back.After == c.After && back.Points == 5 && back.Linear;
            var g = new EyeGaze { Calib = c };
            var read = EyeCalib.Dir(4 * Deg, -3 * Deg);
            g.Feed(Face(read), 1);
            float on = Angle(g.RawDir, read);
            g.Calib = null;
            g.Feed(Face(EyeCalib.Dir(4.5f * Deg, -3 * Deg)), 1.05);
            float off = Angle(g.RawDir, g.TrackerDir);
            bool noneWritten = !none.Contains("EyeCalib"), linearKept = !js.Contains("\"Linear\"");
            Check($"eye calibration, the config: none writes no EyeCalib {noneWritten} (want True), one reads back as it was {same} " +
                  $"(want True; Linear itself not written {linearKept}); on, a look moved {on:0.00} deg (want ~1.6), reset - the tracker's " +
                  $"own {off:0.000} deg off it (want 0)",
                noneWritten && same && linearKept && on > 1 && off < 0.001f);
        }
        return problems;
    }
}

static partial class Tools
{
    // ORBIT_SNAP_ONLY=eyecalib: the eye calibration's screens (Sys/EyeCalib.cs, Panels/EyeCalibPanel.cs) - the board at its second point and
    // at its end in every style (eyecalib/board_<style>.png, done_<style>.png, failed_modern.png), the VRChat tab with Calibrate... / Reset in
    // every style (vrchat_<style>.png), both buttons clicked in Modern and in the 95 sheet, the texts by them against their boxes (Modern,
    // Cyber). In seconds; no monitor captured, nothing of VR.
    static int EyeCalibOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = SnapCulture;
        string edir = Path.Combine(dir, "eyecalib");
        Directory.CreateDirectory(edir);
        int problems = 0;
        void Want(string line, bool ok) { if (!ok) problems++; Log.Info(line); }
        var host = new FakeHost { Cfg = cfg, GpuName = gpu.AdapterName, MonitorList = GazeFocusTools.FakeMonitors(2), Data = data, Wrist = wrist };
        var view = new SettingsView(host) { Tab = SettingsView.TabVrchat };
        var ui = new Ui(painter);
        var ptr = new PointerState();
        var look0 = cfg.Data.Appearance;
        var sample = new EyeCalibConfig { Yaw = 1.1f, Pitch = -0.8f, Before = 1.36f, After = 0.06f, Points = 5 };
        string[] styles = { "modern", CyberHud.Id, "classic", "xp", "aero", "mac", "terminal" };
        string Tag(string style) => style == "modern" ? "modern" : style == CyberHud.Id ? "cyber" : Skins.For(Skins.Parse(style)).Tag;
        void Look(string style) { cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true }; Theme.Apply(cfg.Data.Appearance); }
        using var surf = new Surface(gpu, 1500, 960);
        void FrameM() { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
        void ClickM(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); FrameM(); ptr.Press(); FrameM(); ptr.Release(); FrameM(); }
        void Frame95() => view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
        void Click95(float x, float y) { ptr.Move(x * 2, y * 2); Frame95(); ptr.Press(); Frame95(); ptr.Release(); Frame95(); Frame95(); }
        void Frame() { if (Theme.Classic) Frame95(); else FrameM(); }
        try
        {
            // the board: the second point (the middle taken, the dot up) and the end, every style; a failed one in Modern
            using (var panel = new EyeCalibPanel(gpu, painter))
            {
                // the five points as the sample's calibration took them: the tracker's look 1.1 deg left and 0.8 deg up of each dot, give or take
                var rnd = new Random(5);
                for (int i = 0; i < EyeCalibRun.Count; i++)
                {
                    var (yaw, pitch) = EyeCalibRun.Points[i];
                    float rx = 1.1f + (float)(rnd.NextDouble() - 0.5) * 0.12f, ry = -0.8f + (float)(rnd.NextDouble() - 0.5) * 0.12f;
                    panel.Run.Taken[i] = new EyeCalib.Point { Mx = yaw - rx, My = pitch - ry, Rx = rx, Ry = ry, N = 36, Spread = 0.22f, Auto = true };
                }
                foreach (var st in styles)
                {
                    Look(st);
                    panel.Picture(1, EyeCalibRun.Phase.Rest);
                    panel.Surf.SavePng(gpu, Path.Combine(edir, $"board_{Tag(st)}.png"));
                    panel.Picture(4, EyeCalibRun.Phase.Done, sample);
                    panel.Surf.SavePng(gpu, Path.Combine(edir, $"done_{Tag(st)}.png"));
                }
                Look("modern");
                panel.Picture(2, EyeCalibRun.Phase.Failed, null, EyeCalib.WhyDisagree);
                panel.Surf.SavePng(gpu, Path.Combine(edir, "failed_modern.png"));
            }

            // the VRChat tab: Calibrate... and Reset clicked - Modern (the right column's bottom row), the 95 sheet (under Eye cursor)
            Look("modern");
            cfg.Data.General.EyeCalib = sample;
            ptr.Leave(); ui.ResetAnimations(); FrameM();
            ClickM(602 + 10 + 75, 600 + 17);
            bool calM = host.Calibrations == 1;
            ClickM(602 + 166 + 46, 600 + 17);
            bool resetM = cfg.Data.General.EyeCalib == null;
            Look("classic");
            cfg.Data.General.EyeCalib = sample;
            ptr.Leave(); ui.ResetAnimations(); Frame95();
            Click95(388 + 40, 336 + 11);
            bool cal95 = host.Calibrations == 2;
            Click95(394 + 80 + 30, 336 + 11);
            bool reset95 = cfg.Data.General.EyeCalib == null;
            Want($"eye calibration settings: Modern's Calibrate... asks the app {calM} (want True), Reset clears it {resetM} (want True); the 95 sheet's " +
                 $"Calibrate... {cal95} (want True), Reset {reset95} (want True)", calM && resetM && cal95 && reset95);

            // the texts round them against their boxes (Modern, Cyber): the eye tracking section, calibrated and not
            foreach (var st in new[] { "modern", CyberHud.Id })
            {
                Look(st);
                int bad = 0;
                foreach (var cal in new[] { sample, null })
                {
                    cfg.Data.General.EyeCalib = cal;
                    var spied = new List<(string s, RectangleF r, float size, Vortice.DirectWrite.FontWeight w, string fam, bool wrap,
                        Vortice.DirectWrite.TextAlignment h, Vortice.DirectWrite.ParagraphAlignment v, float tr)>();
                    painter.FitSpy = (s, r, size, w, fam, wrap, h, v, tr) => spied.Add((s, r, size, w, fam, wrap, h, v, tr));
                    ptr.Leave(); ui.ResetAnimations(); FrameM();
                    painter.FitSpy = null;
                    var area = new RectangleF(600, 380, 392, 260);
                    foreach (var x in spied)
                    {
                        if (x.fam == Theme.IconFont || string.IsNullOrWhiteSpace(x.s) || !area.IntersectsWith(x.r)) continue;
                        float o = painter.Overhang(x.s, x.r, x.size, x.w, x.fam, x.wrap, x.h, x.v, x.tr);
                        if (o >= 1) { bad++; Log.Warn($"eye calibration fit {st}: '{x.s}' sticks out {o:0.#} px of its {x.r.Width:0} x {x.r.Height:0} box"); }
                    }
                }
                Want($"eye calibration fit {st}{(Lang.English ? "" : " " + Lang.Code)}: the eye tracking section's texts (calibrated and not) sticking out " +
                     $"of their box {bad} (want 0)", bad == 0);
            }

            // the tab in every style, calibrated
            cfg.Data.General.EyeCalib = sample;
            foreach (var st in styles)
            {
                Look(st);
                ptr.Leave(); Frame(); ui.ResetAnimations(); Frame();
                surf.SavePng(gpu, Path.Combine(edir, $"vrchat_{Tag(st)}.png"));
            }
        }
        finally
        {
            cfg.Data.General.EyeCalib = null;
            cfg.Data.Appearance = look0;
            Theme.Apply(look0);
            CultureInfo.CurrentCulture = culture;
        }
        Log.Info($"snapshot (eye calibration only) written to {edir}: {(problems == 0 ? "every check as wanted" : $"{problems} NOT as wanted")}");
        return problems == 0 ? 0 : 1;
    }
}
