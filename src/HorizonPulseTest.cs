using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;

namespace Orbit;

// --snapshot's part for Cyber's neon horizon in theater mode (Gfx/HorizonPulse.cs, Panels/TheaterHorizon.cs), ORBIT_SNAP_ONLY=pulse.
// Made-up sound only (never the real output device), no VR:
//  - the breathing on made-up level signals - silence, speech-like syllables, drum hits over silence, music with drums, a sudden loud
//    start, full-scale noise, a jittery meter on a steady tone: the floor and the cap, the attack (~80 ms) and the release (~0.5 s), no
//    frame moving it by more than 0.03 at 90 Hz, no swing faster than the beat (nothing over ~3 Hz), the same curve at 90 and 144 Hz;
//  - the sweep: the sparks' angle over time (monotone, the screen to behind you in 1.8 s), how long you see them run from the screen's
//    edge to the edge of the view, fading in past the screen and out behind you, the ring settling after;
//  - in theater mode (a made-up headset and screen, Cyber): the sparks set off as the city's lights come on; the arc keeps the ring's
//    height and radius frame by frame; at rest only the ring's alpha moves (in silence: no call at all); loud and quiet; going out it
//    fades with the wall and the meter is no longer read; a real off / on sweeps again, a quick one does not; Motion reduced - no sweep, a
//    still brightness, the meter never read; another style, theater off - nothing, the meter never read; what a frame costs / allocates;
//  - pictures (<dir>/pulse): the ring's and a spark's textures, the sweep as a strip of moments all the way round and close up at the
//    screen's edge, the breathing as a graph and as the ring itself over time, the horizon at the floor and at the cap.
static partial class Tools
{
    const float PulseDt = 1 / 90f;

    static float PDb(float db) => MathF.Pow(10, db / 20);
    static float PJit(double t, int seed) => CyberFx.Hash((int)Math.Floor(t * 1000), seed);        // 0..1, the same for the same ms

    // drum hits twice a second: a sharp attack at 'db', gone in ~0.3 s
    static float PKick(double t, float db)
    {
        if (t < 0) return 0;
        double p = t % 0.5;
        return p < 0.3 ? PDb(db) * (float)Math.Exp(-p / 0.06) : 0;
    }

    // speech: syllables 4.5 a second, every sixth a pause, each its own level (-34..-22 dB), voiced 60 % of it; room tone between
    static float PSpeech(double t)
    {
        int n = (int)Math.Floor(t * 4.5);
        double ph = t * 4.5 - n;
        if (n % 6 == 5 || ph > 0.6) return PDb(-62);
        return PDb(-34 + 12 * CyberFx.Hash(n, 11)) * (0.75f + 0.25f * PJit(t, 13));
    }

    enum PSig { Silence, Speech, Drums, Music, Loud, Noise, Jitter }

    static float PSignal(PSig k, double t) => k switch
    {
        PSig.Speech => PSpeech(t),
        PSig.Drums => PKick(t, -4),
        PSig.Music => MathF.Max(PDb(-16 + 4 * (PJit(t, 3) - 0.5f)), MathF.Max(PKick(t, -3), PKick(t - 0.25, -8))),
        PSig.Loud => PDb(-6 + 2 * (PJit(t, 17) - 0.5f)),
        PSig.Noise => 0.85f + 0.15f * PJit(t, 5),
        PSig.Jitter => PDb(-20 + 6 * (PJit(t, 7) - 0.5f)),
        _ => 0,
    };

    // what the brightness did over a stretch: its frames, the biggest step between two, its swing, how often it turns back (a turn
    // counts once it has gone 0.015 the other way: a sawtooth's teeth, not the meter's crumbs), its mean
    sealed class PRun
    {
        public readonly List<float> B = new(), S = new(), X = new();
        public float MaxStep, Min = float.MaxValue, Max = float.MinValue, Mean;
        public float Turns;                  // a second
        public void Stats(int from)
        {
            const float H = 0.015f;
            int dir = 0, turns = 0;
            float hi = B[from], lo = B[from], sum = 0;
            for (int i = from; i < B.Count; i++)
            {
                float b = B[i];
                if (i > from) MaxStep = MathF.Max(MaxStep, MathF.Abs(b - B[i - 1]));
                Min = MathF.Min(Min, b); Max = MathF.Max(Max, b); sum += b;
                if (dir >= 0)
                {
                    hi = MathF.Max(hi, b);
                    if (b < hi - H) { if (dir > 0) turns++; dir = -1; lo = b; }
                }
                if (dir <= 0)
                {
                    lo = MathF.Min(lo, b);
                    if (b > lo + H) { if (dir < 0) turns++; dir = 1; hi = b; }
                }
            }
            Mean = sum / (B.Count - from);
            Turns = turns / ((B.Count - from) * PulseDt);
        }
    }

    static PRun PFeed(HorizonPulse p, PSig k, double from, double seconds, float dt = PulseDt, PRun run = null)
    {
        run ??= new PRun();
        int n = (int)Math.Round(seconds / dt);
        for (int i = 0; i < n; i++)
        {
            double t = from + i * dt;
            float pk = PSignal(k, t);
            run.B.Add(p.Step(pk, dt));
            run.S.Add(p.Smooth);
            run.X.Add(HorizonPulse.Map(pk));
        }
        return run;
    }

    // what something costs on the CPU alone: the best of 5 rounds of 20000 in a row, in microseconds
    static double TightUs(Action work)
    {
        double best = double.MaxValue;
        for (int r = 0; r < 5; r++)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 20000; i++) work();
            best = Math.Min(best, System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000 / 20000);
        }
        return best;
    }

    static int PulseOnly(ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        string pd = Path.Combine(dir, "pulse");
        Directory.CreateDirectory(pd);
        int fails = 0;
        void Want(string what, bool ok) { Log.Info($"pulse: {what} {ok} (want True)"); if (!ok) fails++; }
        bool live = CyberFx.Live;
        var motion = Motion.Level;
        try
        {
            PulseEnvelope(Want, pd);
            PulseSweep(Want);
            PulseTheater(Want, gpu, painter, pd);
        }
        catch (Exception e) { Log.Error("pulse: " + e); fails++; }
        finally
        {
            CyberFx.Live = live;
            Motion.Level = motion;
            Theme.Apply(cfg.Data.Appearance);
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        Log.Info($"pulse: {(fails == 0 ? "all checks True" : fails + " check(s) False")} - pictures in {pd}");
        return 0;
    }

    // ---- the breathing ----
    static void PulseEnvelope(Action<string, bool> Want, string pd)
    {
        const float F = HorizonPulse.Floor, C = HorizonPulse.Cap;
        // silence: exactly the floor, and never under it
        var p = new HorizonPulse();
        var sil = PFeed(p, PSig.Silence, 0, 3);
        sil.Stats(0);
        Want($"silence: the floor {sil.Max:0.000} (want {F:0.00}), never under it", sil.Min == F && sil.Max == F);
        // a sudden loud start (after silence): the envelope 63 % up in ~80 ms, the brightness (its 0.03 a frame at most) soon after
        float target = HorizonPulse.Map(PDb(-6)), bt = F + (C - F) * target;
        var up = PFeed(p, PSig.Loud, 0, 2);
        int s63 = up.S.FindIndex(v => v >= 0.63f * target), b63 = up.B.FindIndex(v => v >= F + 0.63f * (bt - F)),
            b90 = up.B.FindIndex(v => v >= F + 0.9f * (bt - F));
        float ms(int i) => (i + 1) * PulseDt * 1000;
        up.Stats(0);
        Want($"a sudden loud start: the envelope 63 % up in {ms(s63):0} ms (want 60-110), the brightness 63 % in {ms(b63):0} ms and 90 % in " +
             $"{ms(b90):0} ms (want under 250), settled under the cap ({up.Max:0.000} <= {C:0.00}), its biggest step {up.MaxStep:0.000} (want <= 0.03)",
             ms(s63) >= 60 && ms(s63) <= 110 && ms(b90) <= 250 && up.Max <= C + 1e-6f && up.Max >= bt - 0.01f && up.MaxStep <= 0.03f + 1e-6f);
        // its release: 63 % down in ~0.5 s, back on the floor exactly
        float top = up.B[^1];
        var down = PFeed(p, PSig.Silence, 0, 6);
        int r63 = down.B.FindIndex(v => v <= top - 0.63f * (top - F));
        down.Stats(0);
        Want($"silence after it: 63 % down in {ms(r63):0} ms (want 400-650), back on the floor exactly ({down.B[^1]:0.0000}), its biggest step " +
             $"{down.MaxStep:0.000} (want <= 0.03)", ms(r63) >= 400 && ms(r63) <= 650 && down.B[^1] == F && down.MaxStep <= 0.03f + 1e-6f);
        // full-scale noise (an explosion): at the cap, steady, never past it
        p.Reset();
        var noise = PFeed(p, PSig.Noise, 0, 3);
        noise.Stats(90);
        Want($"full-scale noise: never past the cap (highest {noise.Max:0.000}), steady once up (it moves {noise.Max - noise.Min:0.0000} after 1 s)",
             noise.Max <= C + 1e-6f && noise.Max - noise.Min < 0.01f);
        // drum hits over silence (the worst case): a swell with each beat - no faster than it, not much of a swing, small steps
        p.Reset();
        var drums = PFeed(p, PSig.Drums, 0, 8);
        drums.Stats(180);
        Want($"drum hits over silence (2 a second): it swings {drums.Max - drums.Min:0.000} (want <= 0.3), turns back {drums.Turns:0.0} times a " +
             $"second (want <= 4.5: the beat's up and down, nothing faster), its biggest step {drums.MaxStep:0.000} (want <= 0.03)",
             drums.Max - drums.Min <= 0.3f && drums.Turns <= 4.5f && drums.MaxStep <= 0.03f + 1e-6f);
        // music with drums: up high, a gentle swell
        p.Reset();
        var music = PFeed(p, PSig.Music, 0, 8);
        music.Stats(180);
        Want($"music with drums: around {music.Mean:0.00} (want 0.65-0.88), swings {music.Max - music.Min:0.000} (want <= 0.15), turns back " +
             $"{music.Turns:0.0} times a second (want <= 4.5), its biggest step {music.MaxStep:0.000} (want <= 0.03)",
             music.Mean >= 0.65f && music.Mean <= 0.88f && music.Max - music.Min <= 0.15f && music.Turns <= 4.5f && music.MaxStep <= 0.03f + 1e-6f);
        // speech: low, calm - the syllables (4.5 a second) do not make it flutter
        p.Reset();
        var speech = PFeed(p, PSig.Speech, 0, 8);
        speech.Stats(180);
        Want($"speech: around {speech.Mean:0.00} (want 0.38-0.62: lower than music), swings {speech.Max - speech.Min:0.000}, turns back " +
             $"{speech.Turns:0.0} times a second (want <= 3: no flutter with the syllables), its biggest step {speech.MaxStep:0.000} (want <= 0.03)",
             speech.Mean >= 0.38f && speech.Mean <= 0.62f && speech.Mean < music.Mean - 0.1f && speech.Turns <= 3 && speech.MaxStep <= 0.03f + 1e-6f);
        // a steady tone through a jittery meter (every reading ±3 dB): the jitter does not show
        p.Reset();
        var jit = PFeed(p, PSig.Jitter, 0, 6);
        jit.Stats(180);
        Want($"a jittery meter on a steady tone (±3 dB a reading): it moves {jit.MaxStep:0.0000} a frame at most (want < 0.006), {jit.Max - jit.Min:0.000} " +
             $"in all (want < 0.05), turns back {jit.Turns:0.0} times a second (want <= 1)", jit.MaxStep < 0.006f && jit.Max - jit.Min < 0.05f && jit.Turns <= 1);
        // the same at 144 Hz (a loud start, then silence): the curve does not depend on the frame rate
        float worst = 0;
        {
            var a = new HorizonPulse();
            var b = new HorizonPulse();
            var ra = PFeed(a, PSig.Loud, 0, 1.2f, 1 / 90f);
            PFeed(a, PSig.Silence, 0, 1.5f, 1 / 90f, ra);
            var rb = PFeed(b, PSig.Loud, 0, 1.2f, 1 / 144f);
            PFeed(b, PSig.Silence, 0, 1.5f, 1 / 144f, rb);
            for (double t = 0.02; t < 2.6; t += 0.01)
            {
                float va = ra.B[Math.Min(ra.B.Count - 1, (int)(t * 90))], vb = rb.B[Math.Min(rb.B.Count - 1, (int)(t * 144))];
                worst = MathF.Max(worst, MathF.Abs(va - vb));
            }
        }
        Want($"the same sound at 90 and 144 Hz: the brightness at most {worst:0.000} apart (want <= 0.04)", worst <= 0.04f);
        // the meter off / missing (-1): silence
        var miss = new HorizonPulse();
        for (int i = 0; i < 90; i++) miss.Step(-1, PulseDt);
        Want($"no meter (-1): the floor ({miss.Bright:0.00})", miss.Bright == F);
        // its cost: one step (a log, three exps)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var q = new HorizonPulse();
        float sink = 0;
        for (int i = 0; i < 200000; i++) sink += q.Step(PSignal(PSig.Music, i * PulseDt), PulseDt);
        Log.Info($"perf: pulse one envelope step (with a made-up reading) {sw.Elapsed.TotalMilliseconds * 1000 / 200000:0.000} us ({sink:0})");

        // ---- the graph: a made-up evening (silence, speech, silence, music starting at once, an explosion, silence, drums alone) ----
        var segs = new (PSig k, double s, string label)[]
        {
            (PSig.Silence, 2, "silence"), (PSig.Speech, 4, "speech"), (PSig.Silence, 1, "silence"), (PSig.Music, 4, "music starts at once"),
            (PSig.Noise, 1, "explosion"), (PSig.Silence, 3, "silence (release)"), (PSig.Drums, 3, "drum hits alone"),
        };
        var g = new HorizonPulse();
        var all = new PRun();
        var marks = new List<(double t, string label)>();
        double at = 0;
        foreach (var s in segs)
        {
            marks.Add((at, s.label));
            PFeed(g, s.k, 0, s.s, PulseDt, all);
            at += s.s;
        }
        PulseGraph(Path.Combine(pd, "pulse_envelope.png"), all, marks, at);
    }

    // ---- the sweep ----
    static void PulseSweep(Action<string, bool> Want)
    {
        float last = 0, fastest = 0, fastAt = 0;
        bool mono = true;
        double edge = -1, view = -1;
        for (int i = 1; i <= 2000; i++)
        {
            float t = i / 1000f, a = HorizonPulse.SweepAngle(t) * 180 / MathF.PI;
            if (a < last - 1e-4f) mono = false;
            float v = (a - last) * 1000;
            if (v > fastest) { fastest = v; fastAt = t; }
            if (edge < 0 && a >= 32.1f) edge = t;          // a 2.8 m screen 2.5 m away ends 32° out
            if (view < 0 && a >= 55) view = t;             // the headset sees about 55° either side
            last = a;
        }
        var sb = new System.Text.StringBuilder();
        foreach (float t in new[] { 0f, 0.2f, 0.35f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.8f })
            sb.Append($" {t:0.00}s {HorizonPulse.SweepAngle(t) * 180 / MathF.PI:0}°");
        Log.Info("pulse: the sparks' way round -" + sb);
        Want($"the sweep: from the screen ({HorizonPulse.SweepAngle(0):0}) to behind you ({HorizonPulse.SweepAngle(HorizonPulse.SweepS) * 180 / MathF.PI:0}°) " +
             $"in {HorizonPulse.SweepS} s, never back ({mono}); past the screen's edge at {edge:0.00} s, out of view at {view:0.00} s - you see them run " +
             $"{view - edge:0.00} s (want >= 0.25), fastest {fastest:0}°/s at {fastAt:0.00} s (round your sides)",
             HorizonPulse.SweepAngle(0) == 0 && HorizonPulse.SweepAngle(HorizonPulse.SweepS) == MathF.PI && mono && view - edge >= 0.25f && fastAt > 0.8f);
        float half = 32.1f * MathF.PI / 180;
        float behind = HorizonPulse.SparkAlpha(0.2f, HorizonPulse.SweepAngle(0.2f), half);
        float atEdge = HorizonPulse.SparkAlpha((float)edge + 0.05f, HorizonPulse.SweepAngle((float)edge + 0.05f), half);
        float mid = HorizonPulse.SparkAlpha(1, HorizonPulse.SweepAngle(1), half), gone = HorizonPulse.SparkAlpha(HorizonPulse.SweepS + 0.4f, MathF.PI, half);
        Want($"the sparks: faint behind the screen ({behind:0.00} at 0.2 s), bright just past its edge ({atEdge:0.00}), full on their way ({mid:0.00}), " +
             $"gone after meeting behind you ({gone:0.00})", behind < 0.35f && atEdge > 0.85f && mid == 1 && gone == 0);
        bool settles = true;
        for (float t = HorizonPulse.SweepS; t < HorizonPulse.SweepS + HorizonPulse.SettleS; t += PulseDt)
            if (HorizonPulse.Charge(t + PulseDt) > HorizonPulse.Charge(t) + 1e-6f) settles = false;
        Want($"the ring lit while they run ({HorizonPulse.Charge(1)}), settling into its breathing over {HorizonPulse.SettleS} s without going back " +
             $"up ({settles}), done ({HorizonPulse.Charge(HorizonPulse.SweepS + HorizonPulse.SettleS)})",
             HorizonPulse.Charge(1) == 1 && settles && HorizonPulse.Charge(HorizonPulse.SweepS + HorizonPulse.SettleS) == 0);
    }

    // ---- in theater mode ----
    static void PulseTheater(Action<string, bool> Want, Gpu gpu, Painter painter, string pd)
    {
        var vr = new VrSession();
        vr.Device[0] = Matrix4x4.CreateTranslation(0, 1.6f, 0);
        vr.DeviceValid[0] = true;
        var scr = new AmbScreen { Pic = null, HomeWorld = Matrix4x4.CreateTranslation(0, 1.5f, -1.2f) };
        var live = new List<ITheaterScreen> { scr };
        var tcfg = new TheaterConfig();
        using var th = new Theater(gpu, painter);
        int asks = 0;
        float level = 0;
        th.SoundLevel = () => { asks++; return level; };
        PSig sig = PSig.Silence;
        double clock = 0;
        var frames = new List<(double t, float sweep, Theater.HorizonFrame f)>();
        bool record = false;
        void Frames(float seconds)
        {
            int n = (int)Math.Round(seconds / PulseDt);
            for (int i = 0; i < n; i++)
            {
                level = PSignal(sig, clock);
                th.Update(vr, PulseDt, tcfg.Dim, live);
                clock += PulseDt;
                if (record) frames.Add((clock, th.HorizonSweepTime, th.HorizonNow));
            }
        }
        // the calls a frame makes on SteamVR (VrOverlay sends a value only when it changed): the ring's, the sparks'
        static int Calls(in Theater.HorizonFrame a, in Theater.HorizonFrame b)
        {
            int n = 0;
            if (a.Ring != b.Ring) n++;
            if (b.Ring)
            {
                if (a.Alpha != b.Alpha) n++;
                if (a.Width != b.Width) n++;
                if (a.Curvature != b.Curvature) n++;
                if (a.U0 != b.U0 || a.U1 != b.U1) n++;
                if (a.Radius != b.Radius) n++;
            }
            if (a.Sparks != b.Sparks) n += 2;
            if (b.Sparks)
            {
                if (a.Angle != b.Angle) n += 2;
                if (a.SparkAlpha != b.SparkAlpha) n += 2;
                if (a.SparkRadius != b.SparkRadius) n += 2;
            }
            return n;
        }
        float city = MathF.Min(1, Math.Clamp(tcfg.Dim, 0, 0.95f) * 1.3f);
        static float Q(float v) => MathF.Round(v * 1024) / 1024;

        // theater off, in Cyber: nothing, the meter never read
        Theme.Apply(new AppearanceConfig { Style = CyberHud.Id });
        CyberFx.Live = true;
        Motion.Level = MotionLevel.Full;
        Frames(0.5f);
        Want($"theater off: no horizon ({th.HorizonNow.Ring}), the meter read {asks} time(s) (want 0)", !th.HorizonNow.Ring && asks == 0);

        // in, Motion Full, music playing: the sparks set off as the city's lights come on (0.2 s into the screen's 0.45 s glide)
        sig = PSig.Music;
        th.Start(scr, scr, vr, tcfg);
        double start = clock;
        record = true;
        Frames(HorizonPulse.SweepS + HorizonPulse.SettleS + 0.8f);
        record = false;
        int off = frames.FindIndex(x => x.sweep >= 0);
        bool dark = true, geo = true, tied = true;
        float hErr = 0, rErr = 0, sparkMax = 0, sparkFirst = -1;
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i].f;
            if (i < off && f.Ring) dark = false;
            if (!f.Ring) continue;
            // the arc: its height (from the bounds) and its radius (from the width and the bend) the full ring's
            float h = f.Width * HorizonPulse.RingH / (HorizonPulse.RingW * (f.U1 - f.U0)), full = 2 * MathF.PI * f.Radius * HorizonPulse.RingH / HorizonPulse.RingW;
            hErr = MathF.Max(hErr, MathF.Abs(h / full - 1));
            if (f.Curvature > 0) rErr = MathF.Max(rErr, MathF.Abs(f.Width / (2 * MathF.PI * f.Curvature) / f.Radius - 1));
            if (MathF.Abs((f.U1 - f.U0) - f.Angle / MathF.PI) > 1e-5f) tied = false;
            if (f.Sparks && sparkFirst < 0) sparkFirst = frames[i].sweep;
            sparkMax = MathF.Max(sparkMax, f.SparkAlpha);
        }
        geo = hErr < 1e-4f && rErr < 1e-4f;
        double setOff = off >= 0 ? frames[off].t - start : -1;
        var end = th.HorizonNow;
        Want($"in: the sparks set off {setOff:0.000} s after Start (want 0.19-0.24: the lights come on), the ring dark before ({dark})", setOff >= 0.19 && setOff <= 0.24 && dark);
        Want($"the arc: its height off the full ring's by {hErr:0.00000} at most, its radius by {rErr:0.00000} (want < 0.0001), lit as far as the sparks ({tied})", geo && tied);
        Want($"the sparks: shown from {sparkFirst:0.00} s on their way (want > 0.15: behind the screen first), at most {sparkMax:0.00} (the city's lights " +
             $"{city:0.00}); after: hidden ({!end.Sparks}), the whole ring (angle {end.Angle * 180 / MathF.PI:0}°, bend {end.Curvature}, bounds {end.U0}-{end.U1}), " +
             $"no sweep left ({!th.HorizonSweeping})", sparkFirst > 0.15f && sparkMax > city - 0.02f && sparkMax <= city + 1e-6f && !end.Sparks &&
             end.Angle == MathF.PI && end.Curvature == 1 && end.U0 == 0 && end.U1 == 1 && !th.HorizonSweeping);
        // the strip's moments (the frames nearest to them)
        var moments = new[] { 0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f, 1.1f, 1.35f, 1.6f, 1.8f, 2.2f, 2.8f };
        var shots = new List<(string label, Theater.HorizonFrame f)>();
        foreach (float m in moments)
        {
            int best = -1;
            for (int i = 0; i < frames.Count; i++)
                if (frames[i].sweep >= 0 && (best < 0 || MathF.Abs(frames[i].sweep - m) < MathF.Abs(frames[best].sweep - m))) best = i;
            if (best >= 0) shots.Add(($"{frames[best].sweep:0.00} s", frames[best].f));
        }

        // at rest with music: only the ring's alpha moves - one call a frame at most
        var prev = th.HorizonNow;
        int most = 0, sum = 0, n = 0;
        for (int i = 0; i < 270; i++)
        {
            Frames(PulseDt);
            int c = Calls(prev, th.HorizonNow);
            most = Math.Max(most, c); sum += c; n++;
            prev = th.HorizonNow;
        }
        Want($"at rest, music: {most} call(s) a frame at most (want <= 1: the ring's SetAlpha), {sum / (float)n:0.00} on average", most <= 1);
        // loud: at the cap (by the city's lights)
        sig = PSig.Noise;
        Frames(2);
        float loud = th.HorizonNow.Alpha;
        Want($"loud: the ring's alpha {loud:0.000} (want {Q(city * HorizonPulse.Cap):0.000}: the cap)", MathF.Abs(loud - Q(city * HorizonPulse.Cap)) < 0.002f);
        // silence: down on the floor, then not a single call
        sig = PSig.Silence;
        Frames(5);
        prev = th.HorizonNow;
        sum = 0;
        for (int i = 0; i < 90; i++) { Frames(PulseDt); sum += Calls(prev, th.HorizonNow); prev = th.HorizonNow; }
        var quiet = th.HorizonNow;
        Want($"silence: the ring's alpha {quiet.Alpha:0.000} (want {Q(city * HorizonPulse.Floor):0.000}: the floor, still there), {sum} call(s) in a second " +
             $"(want 0)", quiet.Ring && quiet.Alpha == Q(city * HorizonPulse.Floor) && sum == 0);

        // perf: a theater frame in Cyber with the horizon breathing (the meter read, the envelope, the frame worked out) against one in
        // another style (no horizon); Motion reduced (the ring still); theater off
        sig = PSig.Music;
        Frames(1);
        var breathing = FrameCost(gpu, () => { level = PSignal(sig, clock); clock += PulseDt; th.Update(vr, PulseDt, tcfg.Dim, live); }, 300);
        double breathUs = TightUs(() => { level = PSignal(sig, clock); clock += PulseDt; th.Update(vr, PulseDt, tcfg.Dim, live); });
        double signalUs = TightUs(() => { level = PSignal(sig, clock); clock += PulseDt; });

        // out: it fades with the wall, then nothing - not even a reading
        sig = PSig.Silence;
        Frames(5);
        th.Stop(vr, tcfg);
        float lastA = th.HorizonNow.Alpha;
        bool fades = true;
        double goneAt = -1, stopAt = clock;
        for (int i = 0; i < 90; i++)
        {
            Frames(PulseDt);
            var f = th.HorizonNow;
            if (f.Ring && f.Alpha > lastA + 1e-6f) fades = false;
            if (f.Ring) lastA = f.Alpha;
            if (!f.Ring && goneAt < 0) goneAt = clock - stopAt;
        }
        int asks0 = asks;
        Frames(1);
        Want($"out: the ring fades with the wall ({fades}), gone {goneAt:0.00} s after Stop (want <= 0.5), the meter read {asks - asks0} time(s) after (want 0)",
             fades && goneAt > 0 && goneAt <= 0.5 && asks == asks0);
        var offCost = FrameCost(gpu, () => th.Update(vr, PulseDt, tcfg.Dim, live), 300);

        // a real off / on: a sweep again; a quick one (the ring still showing): none
        th.Start(scr, scr, vr, tcfg);
        bool again = false;
        for (int i = 0; i < 40; i++) { Frames(PulseDt); again |= th.HorizonSweepTime >= 0; }
        Frames(HorizonPulse.SweepS + HorizonPulse.SettleS + 0.2f);
        th.Stop(vr, tcfg);
        Frames(0.1f);
        th.Start(scr, scr, vr, tcfg);
        bool second = false;
        float dip = 1;
        for (int i = 0; i < 180; i++)
        {
            Frames(PulseDt);
            second |= th.HorizonSweeping;
            dip = MathF.Min(dip, th.HorizonNow.Ring ? th.HorizonNow.Alpha : 0);
        }
        Want($"off and on again: the sparks run again ({again}); a quick off / on: no second sweep ({!second}), the ring dips to {dip:0.000} (want > 0: not gone)",
             again && !second && dip > 0);

        // Motion reduced: no sweep, a still brightness whatever plays, the meter never read
        th.Stop(vr, tcfg);
        Frames(1);
        Motion.Level = MotionLevel.Reduced;
        asks0 = asks;
        sig = PSig.Noise;
        th.Start(scr, scr, vr, tcfg);
        bool swept = false, wasRing = false;
        for (int i = 0; i < 135; i++) { Frames(PulseDt); swept |= th.HorizonSweeping || (th.HorizonNow.Ring && th.HorizonNow.Angle < MathF.PI); wasRing |= th.HorizonNow.Ring; }
        float rLoud = th.HorizonNow.Alpha;
        var reducedCost = FrameCost(gpu, () => th.Update(vr, PulseDt, tcfg.Dim, live), 300);
        sig = PSig.Silence;
        Frames(1);
        Want($"Motion reduced: the ring there ({wasRing}) at once and whole (no sweep: {!swept}), still - {rLoud:0.000} loud, {th.HorizonNow.Alpha:0.000} " +
             $"quiet (want {Q(city * HorizonPulse.Calm):0.000} both), the meter read {asks - asks0} time(s) (want 0)",
             wasRing && !swept && rLoud == Q(city * HorizonPulse.Calm) && th.HorizonNow.Alpha == rLoud && asks == asks0);
        Motion.Level = MotionLevel.Full;

        // another style: none of it
        th.Stop(vr, tcfg);
        Frames(1);
        Theme.Apply(new AppearanceConfig { Style = "modern" });
        asks0 = asks;
        th.Start(scr, scr, vr, tcfg);
        bool any = false;
        for (int i = 0; i < 180; i++) { Frames(PulseDt); any |= th.HorizonNow.Ring || th.HorizonNow.Sparks; }
        var otherCost = FrameCost(gpu, () => th.Update(vr, PulseDt, tcfg.Dim, live), 300);
        double modernUs = TightUs(() => th.Update(vr, PulseDt, tcfg.Dim, live));
        Want($"Modern: no horizon ({!any}), the meter read {asks - asks0} time(s) (want 0)", !any && asks == asks0);
        th.Stop(null, null);
        Theme.Apply(new AppearanceConfig { Style = CyberHud.Id });

        double horizonUs = breathUs - signalUs - modernUs;
        Log.Info($"perf: pulse theater's Update alone (20000 in a row, no GPU): Cyber breathing {breathUs - signalUs:0.00} us, Modern {modernUs:0.00} us - the " +
                 $"horizon's share {horizonUs:0.00} us a frame (the meter's COM call not in it: made-up sound)");
        Log.Info($"perf: pulse a theater frame in Cyber, the horizon breathing {breathing.ms * 1000:0.0} us {breathing.bytes} B");
        Log.Info($"perf: pulse a theater frame in Cyber, Motion reduced (the ring still) {reducedCost.ms * 1000:0.0} us {reducedCost.bytes} B");
        Log.Info($"perf: pulse a theater frame in Modern (no horizon) {otherCost.ms * 1000:0.0} us {otherCost.bytes} B");
        Log.Info($"perf: pulse a frame with theater off {offCost.ms * 1000:0.0} us {offCost.bytes} B");
        Want($"perf: the horizon allocates nothing a frame (breathing {breathing.bytes} B, reduced {reducedCost.bytes} B - as many as Modern's " +
             $"{otherCost.bytes} B, theater's own), its share of a frame {horizonUs:0.00} us (want < 10)", breathing.bytes == otherCost.bytes &&
             reducedCost.bytes == otherCost.bytes && horizonUs < 10);

        // ---- the textures ----
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ringPx = HorizonPulse.RingPixels();
        double ringMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var sparkPx = HorizonPulse.SparkPixels();
        double sparkMs = sw.Elapsed.TotalMilliseconds;
        var ringTex = th.HorizonRingForTest.ReadPixels(gpu);
        var sparkTex = th.HorizonSparkForTest.ReadPixels(gpu);
        Log.Info($"perf: pulse the ring's texture ({HorizonPulse.RingW} x {HorizonPulse.RingH}) worked out in {ringMs:0.00} ms, a spark's ({HorizonPulse.SparkW} x " +
                 $"{HorizonPulse.SparkH}) in {sparkMs:0.00} ms - once, the first Cyber theater");
        PulseTexChecks(Want, ringPx, sparkPx, ringTex, sparkTex);
        SaveBackdrop(ringPx, HorizonPulse.RingW, HorizonPulse.RingH, Path.Combine(pd, "pulse_ring.png"), keepAlpha: true);
        SaveBackdrop(sparkPx, HorizonPulse.SparkW, HorizonPulse.SparkH, Path.Combine(pd, "pulse_spark.png"), keepAlpha: true);
        PulseZoom(ringPx, HorizonPulse.RingW, HorizonPulse.RingH, 0, 640, 10, Path.Combine(pd, "pulse_ring_zoom.png"));
        PulseZoom(sparkPx, HorizonPulse.SparkW, HorizonPulse.SparkH, 0, HorizonPulse.SparkW, 3, Path.Combine(pd, "pulse_spark_zoom.png"));

        // ---- the views ----
        var tex = new PulseTex
        {
            City = th.GridForTest(0).ReadPixels(gpu), Band = th.SkylineForTest().ReadPixels(gpu), Ring = ringPx, Spark = sparkPx, Dim = tcfg.Dim,
        };
        // the sweep all the way round (behind you at both ends), and close up where the sparks leave the screen
        PulseStrip(tex, shots, -180, 180, -5, 8, 0.15f, Path.Combine(pd, "pulse_sweep_strip.png"));
        PulseStrip(tex, shots.GetRange(1, 5), 20, 80, -3, 5, 0.03f, Path.Combine(pd, "pulse_sweep_close.png"));
        // the horizon round the screen at the floor and at the cap
        var floorF = new Theater.HorizonFrame { Ring = true, Angle = MathF.PI, Alpha = Q(city * HorizonPulse.Floor) };
        var capF = new Theater.HorizonFrame { Ring = true, Angle = MathF.PI, Alpha = Q(city * HorizonPulse.Cap) };
        var noneF = new Theater.HorizonFrame();
        PulseStrip(tex, new List<(string, Theater.HorizonFrame)> { ("before (no ring)", noneF), ("silence (floor)", floorF), ("loudest (cap)", capF) },
            -100, 100, -18, 24, 0.1f, Path.Combine(pd, "pulse_floor_cap.png"));
        PulseStrip(tex, new List<(string, Theater.HorizonFrame)> { ("before (no ring)", noneF), ("silence (floor)", floorF), ("loudest (cap)", capF) },
            25, 75, -3, 5, 0.03f, Path.Combine(pd, "pulse_floor_cap_close.png"));
    }

    static void PulseTexChecks(Action<string, bool> Want, uint[] ring, uint[] spark, uint[] ringTex, uint[] sparkTex)
    {
        static int Over(uint[] px) { int n = 0; foreach (uint p in px) { uint a = p >> 24; if (((p >> 16) & 0xFF) > a || ((p >> 8) & 0xFF) > a || (p & 0xFF) > a) n++; } return n; }
        int W = HorizonPulse.RingW, H = HorizonPulse.RingH;
        // the ring: the same column everywhere, nothing in the top / bottom rows, a soft core (wider than a texel at half its alpha)
        bool same = true;
        for (int y = 0; y < H; y++) for (int x = 1; x < W; x += 97) if (ring[y * W + x] != ring[y * W]) same = false;
        uint edgeA = Math.Max(ring[0] >> 24, ring[(H - 1) * W] >> 24);
        int peak = 0, halfRows = 0, step = 0;
        for (int y = 0; y < H; y++) peak = Math.Max(peak, (int)(ring[y * W] >> 24));
        for (int y = 0; y < H; y++)
        {
            int a = (int)(ring[y * W] >> 24);
            if (a >= peak / 2) halfRows++;
            if (y > 0) step = Math.Max(step, Math.Abs(a - (int)(ring[(y - 1) * W] >> 24)));
        }
        Want($"the ring's texture: one column all round ({same}), nothing at its top and bottom (alpha {edgeA}), its core {peak} at most, {halfRows} rows " +
             $"({halfRows * 2 * 2.81f / H:0.00}°) over half of it (want >= 3: no 1 px line), the biggest step between rows {step} (want <= 60), no colour over its " +
             $"alpha ({Over(ring)}), uploaded as worked out ({ringTex.AsSpan().SequenceEqual(ring)})",
             same && edgeA == 0 && halfRows >= 3 && step <= 60 && Over(ring) == 0 && ringTex.AsSpan().SequenceEqual(ring));
        int sw = HorizonPulse.SparkW, sh = HorizonPulse.SparkH, border = 0;
        for (int x = 0; x < sw; x++) border = Math.Max(border, (int)Math.Max(spark[x] >> 24, spark[(sh - 1) * sw + x] >> 24));
        for (int y = 0; y < sh; y++) border = Math.Max(border, (int)Math.Max(spark[y * sw] >> 24, spark[y * sw + sw - 1] >> 24));
        uint mid = spark[sh / 2 * sw + sw / 2];
        Want($"a spark's texture: nothing at its edges (alpha {border} at most), white in the middle (#{mid & 0xFFFFFF:X6} at {mid >> 24}), no colour over its " +
             $"alpha ({Over(spark)}), uploaded as worked out ({sparkTex.AsSpan().SequenceEqual(spark)})",
             border <= 1 && (mid >> 24) >= 250 && (mid & 0xFF) >= 240 && Over(spark) == 0 && sparkTex.AsSpan().SequenceEqual(spark));
    }

    // a part of a texture over black, each texel a 'k' x 'k' block (the profile seen up close)
    static unsafe void PulseZoom(uint[] px, int w, int h, int x0, int x1, int k, string path)
    {
        int ow = (x1 - x0) * (k > 4 ? 1 : k), oh = h * k;        // the ring: only taller (it is the same along); a spark: both ways
        using var bmp = new Bitmap(ow, oh, PixelFormat.Format32bppRgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, bmp.PixelFormat);
        for (int y = 0; y < oh; y++)
        {
            uint* dst = (uint*)((byte*)bd.Scan0 + y * bd.Stride);
            for (int x = 0; x < ow; x++) dst[x] = px[y / k * w + x0 + (k > 4 ? x : x / k)] | 0xFF000000;
        }
        bmp.UnlockBits(bd);
        bmp.Save(path, ImageFormat.Png);
    }

    sealed class PulseTex
    {
        public uint[] City, Band, Ring, Spark;
        public float Dim;
    }

    // a premultiplied texel between texel centres; outside the texture (past its rows; its columns unless it wraps): nothing
    static (float r, float g, float b, float a) PSample(uint[] px, int w, int h, float fx, float fy, bool wrap)
    {
        if (fy < -0.5f || fy > h - 0.5f || (!wrap && (fx < -0.5f || fx > w - 0.5f))) return default;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float tx = fx - x0, ty = fy - y0, r = 0, g = 0, b = 0, a = 0;
        for (int k = 0; k < 4; k++)
        {
            int x = x0 + (k & 1), y = Math.Clamp(y0 + (k >> 1), 0, h - 1);
            x = wrap ? (x % w + w) % w : Math.Clamp(x, 0, w - 1);
            float wt = ((k & 1) == 1 ? tx : 1 - tx) * ((k >> 1) == 1 ? ty : 1 - ty);
            uint p = px[y * w + x];
            a += wt * (p >> 24); r += wt * ((p >> 16) & 0xFF); g += wt * ((p >> 8) & 0xFF); b += wt * (p & 0xFF);
        }
        return (r / 255, g / 255, b / 255, a / 255);
    }

    // What you see at az / el (degrees, from the city's middle - every layer is round it, so the angles are exact): the made-up room, the
    // city's wall at the dim, its skyline band, the horizon's ring as the frame has it (its arc and alpha), the sparks, the grey screen
    static (float r, float g, float b) PulseAt(PulseTex t, in Theater.HorizonFrame f, float az, float el)
    {
        var (r, g, b) = FakeRoom(az, el);
        static void Over(ref float r, ref float g, ref float b, (float r, float g, float b, float a) q, float k)
        {
            r = r * (1 - q.a * k) + q.r * k; g = g * (1 - q.a * k) + q.g * k; b = b * (1 - q.a * k) + q.b * k;
        }
        float tanEl = MathF.Tan(el * MathF.PI / 180);
        int WW = Theater.BackW, WH = Theater.BackH;
        Over(ref r, ref g, ref b, PSample(t.City, WW, WH, (az / 360 + 0.5f) * WW - 0.5f, WH / 2f - tanEl / (Theater.WallAspect * MathF.PI) * (WH / 2f) - 0.5f, true), t.Dim);
        const int BW = 4096, BH = 512;
        Over(ref r, ref g, ref b, PSample(t.Band, BW, BH, (az / 360 + 0.5f) * BW - 0.5f, BH / 2f - tanEl / (MathF.PI / 8) * (BH / 2f) - 0.5f, true),
            MathF.Min(1, t.Dim * 1.3f));
        float deg = f.Angle * 180 / MathF.PI;
        if (f.Ring && MathF.Abs(az) <= deg)
        {
            int RW = HorizonPulse.RingW, RH = HorizonPulse.RingH;
            float fy = (0.5f - tanEl * RW / (2 * MathF.PI * RH)) * RH - 0.5f;
            Over(ref r, ref g, ref b, PSample(t.Ring, RW, RH, 1, fy, false), f.Alpha);
        }
        if (f.Sparks)
        {
            float tan8 = MathF.Tan(HorizonPulse.SparkDeg / 2 * MathF.PI / 180);
            foreach (float at in new[] { -deg, deg })
            {
                float d = az - at;
                d = d > 180 ? d - 360 : d < -180 ? d + 360 : d;
                if (MathF.Abs(d) > 30) continue;
                float dr = d * MathF.PI / 180, u = 0.5f + MathF.Tan(dr) / (2 * tan8);
                float v = 0.5f - tanEl / MathF.Cos(dr) * HorizonPulse.SparkW / (2 * tan8 * HorizonPulse.SparkH);
                Over(ref r, ref g, ref b, PSample(t.Spark, HorizonPulse.SparkW, HorizonPulse.SparkH, u * HorizonPulse.SparkW - 0.5f, v * HorizonPulse.SparkH - 0.5f, false), f.SparkAlpha);
            }
        }
        if (MathF.Abs(az) < ScreenAz && MathF.Abs(el) < ScreenEl) (r, g, b) = (0.42f, 0.43f, 0.45f);          // the screen
        return (r, g, b);
    }

    // moments one under another, each with its label: what you see from az0 to az1, el0 to el1, 'step' degrees a pixel
    static unsafe void PulseStrip(PulseTex t, List<(string label, Theater.HorizonFrame f)> rows, float az0, float az1, float el0, float el1, float step, string path)
    {
        const int Label = 20, Gap = 6;
        int w = (int)((az1 - az0) / step), h = (int)((el1 - el0) / step);
        int maxW = Math.Min(w, 3000);
        float sx = w / (float)maxW;                                // wider than 3000 px: every column a little more than one step
        using var bmp = new Bitmap(maxW, rows.Count * (h + Label + Gap) + Gap, PixelFormat.Format32bppRgb);
        using (var gr = Graphics.FromImage(bmp)) gr.Clear(Color.FromArgb(255, 24, 22, 30));
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        for (int k = 0; k < rows.Count; k++)
        {
            int oy = Gap + k * (h + Label + Gap) + Label;
            var f = rows[k].f;
            for (int j = 0; j < h; j++)
            {
                uint* dst = (uint*)((byte*)bd.Scan0 + (oy + j) * bd.Stride);
                float el = el1 - (j + 0.5f) * step;
                for (int i = 0; i < maxW; i++)
                {
                    var (r, g, b) = PulseAt(t, f, az0 + (i + 0.5f) * step * sx, el);
                    dst[i] = 0xFF000000 | Byte(r) << 16 | Byte(g) << 8 | Byte(b);
                }
            }
        }
        bmp.UnlockBits(bd);
        using (var gr = Graphics.FromImage(bmp))
        {
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int k = 0; k < rows.Count; k++)
                gr.DrawString($"{rows[k].label}   (az {az0:0}..{az1:0}°, el {el0:0}..{el1:0}°)", font, Brushes.White, 6, Gap + k * (h + Label + Gap) + 2);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    // the made-up evening as a graph: the level as read (grey), the envelope (cyan), the brightness (pink) between the floor and the cap;
    // under it the ring itself over time (each column its profile at that moment's brightness, at the default dim)
    static void PulseGraph(string path, PRun run, List<(double t, string label)> marks, double seconds)
    {
        const int PxS = 100, L = 46, Top = 30, GH = 260, Strip = 96, Gap = 26;
        int w = L + (int)(seconds * PxS) + 20, h = Top + GH + Gap + Strip + 30;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(255, 20, 18, 28));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 11, GraphicsUnit.Pixel);
        float Y(float v) => Top + GH - v * GH;
        float X(int i) => L + i * PulseDt * PxS;
        using (var grid = new Pen(Color.FromArgb(255, 44, 40, 58)))
            for (int s = 0; s <= (int)seconds; s++) { g.DrawLine(grid, L + s * PxS, Top, L + s * PxS, Top + GH); g.DrawString($"{s}s", font, Brushes.Gray, L + s * PxS - 6, Top + GH + 2); }
        using (var dash = new Pen(Color.FromArgb(255, 120, 110, 140)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
        {
            g.DrawLine(dash, L, Y(HorizonPulse.Floor), w - 20, Y(HorizonPulse.Floor));
            g.DrawLine(dash, L, Y(HorizonPulse.Cap), w - 20, Y(HorizonPulse.Cap));
            g.DrawString("floor", font, Brushes.Gray, 4, Y(HorizonPulse.Floor) - 8);
            g.DrawString("cap", font, Brushes.Gray, 4, Y(HorizonPulse.Cap) - 8);
        }
        foreach (var m in marks)
        {
            g.DrawLine(Pens.DimGray, L + (float)m.t * PxS, Top - 4, L + (float)m.t * PxS, Top + GH);
            g.DrawString(m.label, font, Brushes.Gainsboro, L + (float)m.t * PxS + 3, Top - 18);
        }
        void Line(List<float> v, Color c, float width)
        {
            var pts = new PointF[v.Count];
            for (int i = 0; i < v.Count; i++) pts[i] = new PointF(X(i), Y(v[i]));
            using var pen = new Pen(c, width);
            g.DrawLines(pen, pts);
        }
        // the level and the envelope in the brightness's units (floor + that much of the way to the cap): the pink follows the cyan
        var xs = new List<float>(run.X.Count);
        var ss = new List<float>(run.S.Count);
        foreach (float v in run.X) xs.Add(HorizonPulse.Floor + (HorizonPulse.Cap - HorizonPulse.Floor) * v);
        foreach (float v in run.S) ss.Add(HorizonPulse.Floor + (HorizonPulse.Cap - HorizonPulse.Floor) * v);
        Line(xs, Color.FromArgb(255, 90, 90, 104), 1);
        Line(ss, Color.FromArgb(255, 43, 212, 228), 1.2f);
        Line(run.B, Color.FromArgb(255, 255, 92, 147), 2.2f);
        g.DrawString("grey: the level as read   cyan: its envelope   pink: the ring's brightness (0.03 a frame at most) - all as brightness", font, Brushes.Gainsboro, L, h - 18);
        // the ring over time: the profile's 64 rows (1.5 px each) at that moment's alpha, over the wall's dark violet at the horizon
        var ring = HorizonPulse.RingPixels();
        int oy = Top + GH + Gap;
        float city = MathF.Min(1, new TheaterConfig().Dim * 1.3f);
        for (int x = L; x < L + (int)(seconds * PxS); x++)
        {
            int i = Math.Min(run.B.Count - 1, (int)((x - L) / (PulseDt * PxS)));
            float al = city * run.B[i];
            for (int y = 0; y < Strip; y++)
            {
                uint p = ring[(int)(y * HorizonPulse.RingH / (float)Strip) * HorizonPulse.RingW];
                float r = 0x2A / 255f * 0.6f, gg = 0x0B / 255f * 0.6f, b = 0x36 / 255f * 0.6f;
                float a = al * (p >> 24) / 255f;
                r = r * (1 - a) + al * ((p >> 16) & 0xFF) / 255f; gg = gg * (1 - a) + al * ((p >> 8) & 0xFF) / 255f; b = b * (1 - a) + al * (p & 0xFF) / 255f;
                bmp.SetPixel(x, oy + y, Color.FromArgb(255, (int)Byte(r), (int)Byte(gg), (int)Byte(b)));
            }
        }
        g.DrawString("the ring itself over time (its profile, 5.6° tall, magnified)", font, Brushes.Gainsboro, L, oy - 16);
        bmp.Save(path, ImageFormat.Png);
    }
}
