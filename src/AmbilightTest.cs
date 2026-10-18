using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Threading;

namespace Orbit;

// --snapshot's part for theater mode's Ambilight (Gfx/Ambilight.cs, Panels/TheaterAmbilight.cs), ORBIT_SNAP_ONLY=ambilight: on made-up
// pictures only (a red left edge and a blue right one, a cut to green, plain white, four coloured sides) - never a real screen, no VR:
//  - the sampling: the edge colours read back from the GPU a frame later (not the same frame), fine stripes averaged (no shimmer);
//  - the easing: a hard cut eases over ~0.4 s, no jump and no overshoot, and stops (nothing written at rest); the brightness cap;
//  - the glow: fading to nothing at its rim, no seams or blocks (round the corners too), where it goes behind the screen;
//  - in theater mode (a made-up headset and screen): it comes and goes with the screen, 10 readings a second at most and none for a
//    still picture, the setting fades it, a switch does not dip it; what a frame costs and allocates;
//  - pictures: the glow, the wall + glow + screen composed (also with a grey screen), the settings' Theater section in every style.
static partial class Tools
{
    sealed class AmbScreen : ITheaterScreen
    {
        public Surface Pic;
        public long Frame = 1;
        public float Fade;
        public Matrix4x4 HomeWorld;
        public Vector2 Size = new(1920, 1080);
        public TheaterView Theater { get; set; }
        public bool TheaterHidden { get; set; }
        public string TheaterLabel => "Made-up screen";
        public bool Playable => true;
        public bool HomeInWorld => true;
        public string TheaterState => "made up";
        public float TheaterFade => Fade;
        public void TheaterVanish() => Fade = 0;
        public Surface TheaterPicture(out long frame) { frame = Frame; return Pic; }
        public bool Home(out Matrix4x4 world, out float width, out float curvature, out float opacity)
        {
            world = HomeWorld; width = 0.8f; curvature = 0.05f; opacity = 1;
            return true;
        }
        public VrOverlay Overlay => null;
        public bool Interactive => false;
        public bool AcceptsFrom(Hand h) => true;
        public Vector2 TextureSize => Size;
        public void PointerMove(Hand h, Vector2 px) { }
        public void PointerLeave(Hand h) { }
        public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px) { }
        public void PointerScroll(Hand h, float notches, Vector2 px) { }
        public bool Grabbable => false;
        public Matrix4x4 WorldTransform => Theater?.World ?? HomeWorld;
        public void GrabBegin(bool detach) { }
        public void GrabMove(Matrix4x4 world) { }
        public void GrabStickX(float v, float dt) { }
        public void GrabTwoHandBegin() { }
        public void GrabTwoHandScale(float ratio) { }
        public bool GrabSnapPreview => false;
        public void GrabEnd() { }
    }

    // a made-up picture: opaque BGRA pixels, and the texture holding them
    static (Surface s, uint[] px) AmbPicture(Gpu gpu, int w, int h, Func<int, int, uint> at)
    {
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) px[y * w + x] = at(x, y) | 0xFF000000;
        var s = new Surface(gpu, w, h);
        gpu.Ctx.UpdateSubresource(px, s.Tex, 0, (uint)w * 4, 0, null);
        return (s, px);
    }

    static uint Rgb(int r, int g, int b) => (uint)(r << 16 | g << 8 | b);

    static int AmbilightOnly(ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        Motion.Apply(cfg.Data.Appearance);
        int fails = 0;
        void Want(string what, bool ok) { Log.Info($"ambilight: {what} {ok} (want True)"); if (!ok) fails++; }

        // ---- the made-up pictures ----
        const int PW = 1920, PH = 1080;
        // A: a dark scene, the left 22 % red, the right 22 % blue, fine 1-px black / white stripes at the bottom middle
        uint SceneA(int x, int y)
        {
            float fx = x / (float)PW, fy = y / (float)PH;
            if (fx < 0.22f) return Rgb(224, 40 + (int)(10 * fy), 40);
            if (fx >= 0.78f) return Rgb(40, 80, 232);
            if (fy >= 0.88f && fx >= 0.35f && fx < 0.65f) return (x & 1) == 0 ? 0u : Rgb(255, 255, 255);
            return Rgb((int)(40 - 20 * fy), (int)(52 - 28 * fy), (int)(70 - 40 * fy));
        }
        var (picA, pxA) = AmbPicture(gpu, PW, PH, SceneA);
        var (picB, pxB) = AmbPicture(gpu, PW, PH, (x, y) => Rgb(32, 192, 64));
        var (picW, pxW) = AmbPicture(gpu, PW, PH, (x, y) => Rgb(255, 255, 255));
        // Q: four coloured sides (top yellow, bottom magenta, left green, right blue - the sides over the top / bottom at the corners)
        var (picQ, pxQ) = AmbPicture(gpu, PW, PH, (x, y) =>
        {
            float fx = x / (float)PW, fy = y / (float)PH;
            if (fx < 0.2f) return Rgb(40, 200, 80);
            if (fx >= 0.8f) return Rgb(40, 80, 232);
            if (fy < 0.2f) return Rgb(240, 200, 40);
            if (fy >= 0.8f) return Rgb(220, 40, 200);
            return Rgb(60, 60, 64);
        });
        var (pic4K, _) = AmbPicture(gpu, 3840, 2160, (x, y) => SceneA(x / 2, y / 2));

        using var amb = new Ambilight(gpu, painter);
        amb.Shape(PW / (float)PH);
        // a reading: sampled, then looked at once a frame (the GPU done with each frame first) until it is back
        int Read(Surface pic)
        {
            amb.Sample(pic);
            for (int f = 1; f <= 20; f++)
            {
                GpuIdle(gpu);
                if (amb.Poll()) return f;
            }
            return -1;
        }

        // ---- sampling ----
        amb.Snap(true);
        amb.Sample(picA);
        bool same = amb.Poll();                         // the frame it was sent in: not looked at yet
        int frames = 1;
        while (amb.Reads == 0 && frames < 20) { GpuIdle(gpu); amb.Poll(); frames++; }
        Want($"sampling: not read back in the frame it was sent ({(same ? "read" : "not read")}), read {frames} frame(s) later, {amb.Waits} wait(s)",
            !same && amb.Reads == 1 && frames >= 2 && frames <= 3);
        bool red = true, blue = true;
        for (int j = 3; j < Ambilight.GridH - 3; j++)
        {
            var l = amb.Target(Ambilight.Left(j));
            var r = amb.Target(Ambilight.Right(j));
            red &= l.X > 0.5f && l.X > 3 * l.Y && l.X > 3 * l.Z;
            blue &= r.Z > 0.5f && r.Z > 2.5f * r.X && r.Z > 2.5f * r.Y;
        }
        var l8 = amb.Target(Ambilight.Left(8));
        var r8 = amb.Target(Ambilight.Right(8));
        Want($"sampling: the left edge red ({l8.X:0.00} {l8.Y:0.00} {l8.Z:0.00}), the right edge blue ({r8.X:0.00} {r8.Y:0.00} {r8.Z:0.00})", red && blue);
        int sMin = 255, sMax = 0;
        for (int x = 13; x <= 18; x++)
        {
            uint c = amb.Cell(x, Ambilight.GridH - 1);
            foreach (int v in new[] { (int)(c >> 16 & 255), (int)(c >> 8 & 255), (int)(c & 255) }) { sMin = Math.Min(sMin, v); sMax = Math.Max(sMax, v); }
        }
        Want($"sampling: 1-px black / white stripes come out grey {sMin}..{sMax} (an average of every pixel, no shimmer)", sMin >= 100 && sMax <= 156);

        // ---- the glow of A, then a hard cut to green: eased, no jump, no overshoot, no brighter than either ----
        amb.Step(1 / 90f);
        amb.Compose();
        var glowA = (uint[])amb.Pixels.Clone();
        int composed0 = amb.Composed;
        amb.Snap(false);
        var start = amb.Eased(Ambilight.Left(8));
        Read(picB);
        var goal = amb.Target(Ambilight.Left(8));
        var prog = new List<float>();
        bool mono = true, noFlash = true;
        float lumMax = MathF.Max(Lum(start), Lum(goal)) + 0.01f, last = 0;
        uint[] glowCut = null;
        // the glow's writes along the way (the cadence: every frame while the colours move fast, every third frame in the tail of the
        // easing, once more at rest): the eased green and the green the glow was last written with, frame by frame
        var easedG = new List<float>();
        var writtenG = new List<float>();
        int writes = 0, movingFrames = 0;
        float written = start.Y, lag = 0, lastE = start.Y;
        for (int f = 1; f <= 90; f++)
        {
            if (amb.Step(1 / 90f)) { amb.Compose(); writes++; written = amb.Eased(Ambilight.Left(8)).Y; }
            var e = amb.Eased(Ambilight.Left(8));
            float p = (e.Y - start.Y) / (goal.Y - start.Y);
            prog.Add(p);
            mono &= p >= last - 1e-4f && p <= 1 + 1e-4f;
            noFlash &= Lum(e) <= lumMax;
            last = p;
            if (f == 14) glowCut = (uint[])amb.Pixels.Clone();
            if (e.Y != lastE) movingFrames++;
            lastE = e.Y;
            easedG.Add(e.Y);
            writtenG.Add(written);
            lag = MathF.Max(lag, MathF.Abs(e.Y - written) * 255);
        }
        Want($"easing: a cut to green after 1 frame {prog[0]:0.00} (no jump), 0.15 s {prog[13]:0.00}, 0.42 s {prog[37]:0.00}, 1 s {prog[89]:0.00}",
            prog[0] < 0.12f && prog[13] > 0.5f && prog[13] < 0.85f && prog[37] > 0.93f && prog[89] == 1);
        Want($"easing: steady (never back, never past the new colour) {mono}, no brighter than either colour on the way", mono && noFlash);
        Want($"cadence: the hard cut's glow written {writes} times while its colours moved on {movingFrames} frames (every frame would be {movingFrames}: " +
             $"want under 0.7 of it) - every frame while a colour moves 2 levels or more a frame, then every third; the glow at most {lag:0.0} levels " +
             $"behind the eased colour (want 4 or less), and on it at rest ({(writtenG[^1] == easedG[^1] ? "yes" : "no")})",
            writes < 0.7f * movingFrames && writes >= 20 && lag <= 4.01f && writtenG[^1] == easedG[^1]);
        CadenceGraph(Path.Combine(dir, "ambilight_cadence.png"), easedG, writtenG, start.Y, goal.Y);
        int composedRest = amb.Composed;
        bool still = false;
        for (int f = 0; f < 30; f++) still |= amb.Step(1 / 90f);
        Want($"easing: at rest nothing moves and nothing is written ({amb.Composed - composedRest} writes)", !still && amb.Composed == composedRest);
        var glowB = (uint[])amb.Pixels.Clone();

        // ---- the cap, and the rim ----
        amb.Snap(true);
        Read(picW);
        amb.Step(0);
        amb.Compose();
        var glowW = (uint[])amb.Pixels.Clone();
        int capW = MaxChannel(glowW), capA = MaxChannel(glowA);
        int wantW = (int)MathF.Round(Ambilight.Peak * Ambilight.LumCap * 255) + 1, wantA = (int)MathF.Round(Ambilight.Peak * Ambilight.MaxCap * 255) + 1;
        Want($"cap: a white picture's glow at most {capW} (cap {wantW}), the red / blue one's {capA} (cap {wantA})", capW <= wantW && capA <= wantA && capW >= wantW - 12);
        int tw = amb.TW, th = amb.TH, rim = 0;
        var hole = amb.Hole;
        for (int x = 0; x < tw; x++) rim = Math.Max(rim, Math.Max((int)(glowW[x] >> 24), (int)(glowW[(th - 1) * tw + x] >> 24)));
        for (int y = 0; y < th; y++) rim = Math.Max(rim, Math.Max((int)(glowW[y * tw] >> 24), (int)(glowW[y * tw + tw - 1] >> 24)));
        int edge = (int)(glowW[((int)hole.Top - 1) * tw + tw / 2] >> 24);
        Want($"glow: {tw} x {th}, strong at the screen's edge ({edge}), nothing left at its rim ({rim})", rim <= 1 && edge >= 0.75f * capW);

        // ---- smooth everywhere: four coloured sides, the corners where they meet ----
        amb.Snap(true);
        Read(picQ);
        amb.Step(0);
        amb.Compose();
        var glowQ = (uint[])amb.Pixels.Clone();
        int step = 0, stepAt = 0;
        bool Out(int x, int y) => x + 0.5f < hole.Left || x + 0.5f > hole.Right || y + 0.5f < hole.Top || y + 0.5f > hole.Bottom;
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                if (!Out(x, y)) continue;
                foreach (var (nx, ny) in new[] { (x + 1, y), (x, y + 1) })
                {
                    if (nx >= tw || ny >= th || !Out(nx, ny)) continue;
                    int d = MaxDiff(glowQ[y * tw + x], glowQ[ny * tw + nx]);
                    if (d > step) { step = d; stepAt = y * tw + x; }
                }
            }
        Want($"glow: no seams or blocks - the biggest step between neighbouring texels {step} (at {stepAt % tw},{stepAt / tw})", step <= 40);
        // the top-right corner turns from the top's yellow to the right's blue (no hard diagonal): along the arc halfway out
        float mt = hole.Left, cx = hole.Right, cy = hole.Top;
        int arcStep = 0;
        uint prev = 0;
        for (int i = 0; i <= 24; i++)
        {
            float ang = i / 24f * MathF.PI / 2;
            int x = (int)(cx + 0.5f * mt * MathF.Sin(ang)), y = (int)(cy - 0.5f * mt * MathF.Cos(ang));
            uint c = glowQ[Math.Clamp(y, 0, th - 1) * tw + Math.Clamp(x, 0, tw - 1)];
            if (i > 0) arcStep = Math.Max(arcStep, MaxDiff(c, prev));
            prev = c;
        }
        Want($"glow: round the top-right corner the colour turns over smoothly (biggest step {arcStep})", arcStep <= 24);

        // ---- where it goes: behind the screen, round the same axis, the same angle past it all round ----
        {
            var head = new Vector3(0, 1.5f, 0);
            var v = new TheaterView { World = Matrix4x4.CreateTranslation(0, 1.5f, -2.5f), Head = head, Width = 2.8f, Curvature = 2.8f / (2 * MathF.PI * 2.5f) };
            Theater.GlowPlace(v, new Vector2(PW, PH), out var gw, out float gwid, out float gc, out float back);
            float rs = v.Width / (2 * MathF.PI * v.Curvature), rg = gwid / (2 * MathF.PI * gc);
            var axisS = v.World.Translation + MathUtil.Back(v.World) * rs;
            var axisG = gw.Translation + MathUtil.Back(gw) * rg;
            float h = v.Width * PH / PW, m = Ambilight.MarginK * MathF.Sqrt(v.Width * h);
            float angS = v.Width / rs, angG = gwid / rg;
            Want($"place: {back:0.00} m behind the screen (before the wall and the city), bent round its axis (off by {Vector3.Distance(axisS, axisG) * 1000:0.0} mm), " +
                 $"{angG / angS:0.00}x its angle, {m / rs * 180 / MathF.PI:0.0}° past it each side",
                back >= 0.08f && back <= 0.3f && Vector3.Distance(axisS, axisG) < 0.001f && angG / angS > 1.25f && angG / angS < 1.45f &&
                MathF.Abs((angG - angS) / 2 - m / rs) < 1e-3f);
            // the headset's little moves (breathing, leaning a bit) do not move it while the screen rests: no SteamVR calls a frame
            var vj = new TheaterView { World = v.World, Head = head + new Vector3(0.012f, -0.008f, 0.01f), Width = v.Width, Curvature = v.Curvature };
            Theater.GlowPlace(vj, new Vector2(PW, PH), out var gwj, out float gwidj, out _, out _);
            bool stillCurved = gwj == gw && gwidj == gwid;
            vj.Curvature = v.Curvature = 0;
            Theater.GlowPlace(v, new Vector2(PW, PH), out gw, out gwid, out _, out _);
            Theater.GlowPlace(vj, new Vector2(PW, PH), out gwj, out gwidj, out _, out _);
            Want($"place: the headset moving a little does not move the glow (curved {stillCurved}, flat {gwj == gw && gwidj == gwid})",
                stillCurved && gwj == gw && gwidj == gwid);
            Theater.GlowPlace(v, new Vector2(PW, PH), out gw, out gwid, out gc, out back);
            float d = 2.5f;
            Want($"place: flat - flat too, {gwid:0.00} m wide at {d + back:0.00} m (the same angle past the screen)",
                gc == 0 && MathF.Abs(gwid - (v.Width + 2 * m) * (d + back) / d) < 1e-3f && MathF.Abs(gw.Translation.Z - (-d - back)) < 1e-4f);
        }

        // ---- in theater mode: a made-up headset and screen ----
        var vr = new VrSession();
        var hd = new Vector3(0, 1.5f, 0);
        vr.Device[0] = Matrix4x4.CreateTranslation(hd);
        vr.DeviceValid[0] = true;
        var scr = new AmbScreen { Pic = picA, HomeWorld = Matrix4x4.CreateTranslation(0, 1.4f, -1.1f) };
        var scr2 = new AmbScreen { Pic = null, HomeWorld = Matrix4x4.CreateTranslation(0.8f, 1.4f, -1f) };
        var live = new List<ITheaterScreen> { scr, scr2 };
        var tcfg = new TheaterConfig();
        using var theater = new Theater(gpu, painter);
        const float Dt = 1 / 90f;
        void Frames(float seconds, bool newFrames)
        {
            for (float t = 0; t < seconds - 1e-4f; t += Dt)
            {
                foreach (var s in new[] { scr, scr2 })
                {
                    if (s.Theater != null && s.Pic != null && !s.TheaterHidden) s.Fade = Math.Min(1, s.Fade + Dt / 0.2f);     // the panel fading in
                    if (newFrames) s.Frame++;
                }
                theater.Update(vr, Dt, tcfg.Dim, live);
                GpuIdle(gpu);
            }
        }
        theater.Start(scr, scr, vr, tcfg);
        Frames(0.05f, true);
        float a05 = theater.AmbilightAlpha;
        Frames(0.6f, true);
        var ta = theater.AmbilightForTest;
        Want($"theater: the glow comes in with the screen ({a05:0.00} after 0.05 s, {theater.AmbilightAlpha:0.00} at 0.65 s) with its colours " +
             $"(left {Fmt(ta?.Eased(Ambilight.Left(8)))})", a05 < 0.2f && theater.AmbilightAlpha > 0.97f && ta != null && ta.HasReading &&
             ta.Eased(Ambilight.Left(8)).X > 3 * ta.Eased(Ambilight.Left(8)).Y);
        int s0 = ta.Sampled;
        Frames(1, true);
        int perSecond = ta.Sampled - s0;
        Frames(0.3f, false);                     // (the last new frame's reading eases in)
        s0 = ta.Sampled;
        int c0 = ta.Composed;
        Frames(1, false);
        Want($"theater: {perSecond} readings in a second of new frames (at most 10 + 1), {ta.Sampled - s0} of a still picture, " +
             $"{ta.Composed - c0} glow writes at rest", perSecond >= 9 && perSecond <= 11 && ta.Sampled == s0 && ta.Composed == c0);
        // the setting: off fades it away and stops the sampling; on brings it back
        theater.AmbilightOn = false;
        Frames(0.35f, true);
        float offA = theater.AmbilightAlpha;
        s0 = ta.Sampled;
        Frames(0.5f, true);
        Want($"theater: Ambilight off - gone in 0.35 s ({offA:0.000}), nothing sampled once it is gone ({ta.Sampled - s0})", offA == 0 && ta.Sampled == s0);
        theater.AmbilightOn = true;
        Frames(0.4f, true);
        Want($"theater: on again ({theater.AmbilightAlpha:0.00})", theater.AmbilightAlpha > 0.97f);
        // a switch: the new screen fades in over the old one (which stays solid) - the glow must not dip, then turns green
        theater.Switch(scr2);
        float dip = 1;
        for (int f = 0; f < 40; f++)
        {
            if (f == 3) scr2.Pic = picB;           // its first frame comes a moment later
            Frames(Dt, true);
            dip = MathF.Min(dip, theater.AmbilightAlpha);
        }
        Frames(0.6f, true);
        var g8 = ta.Eased(Ambilight.Left(8));
        Want($"theater: switching, the glow does not dip (lowest {dip:0.00}) and turns to the new picture (left {Fmt(g8)})", dip > 0.95f && g8.Y > 2 * g8.X);
        // perf: a frame of theater mode without the glow (Theater's own: the baseline), with it at rest, and with its colours easing
        // (a new reading every 0.1 s, alternating red / green)
        theater.AmbilightOn = false;
        Frames(0.4f, false);
        var bare = FrameCost(gpu, () => theater.Update(vr, Dt, tcfg.Dim, live), 200);
        theater.AmbilightOn = true;
        Frames(1, false);
        var rest = FrameCost(gpu, () => theater.Update(vr, Dt, tcfg.Dim, live), 200);
        int k = 0;
        var moving = FrameCost(gpu, () =>
        {
            if (++k % 9 == 0) scr2.Pic = scr2.Pic == picA ? picB : picA;
            scr2.Frame++;
            theater.Update(vr, Dt, tcfg.Dim, live);
        }, 200);
        var compose = FrameCost(gpu, () => ta.Compose(), 200);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 400; i++) ta.Compose();
        double composeCpu = sw.Elapsed.TotalMilliseconds / 400;
        gpu.Ctx.Flush();
        var shapeSw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 20; i++) ta.Shape(i % 2 == 0 ? 1.6f : 1.78f);      // a new shape each time
        double shapeMs = shapeSw.Elapsed.TotalMilliseconds / 20;
        ta.Retire();
        bool rebuilt = false;
        for (int i = 0; i <= 10; i++) rebuilt |= ta.Shape(1.78f * (1 + 0.0015f * i));                  // a window resized a little at a time
        Want($"shape: a window resized by 1.5 % does not rebuild the glow ({(rebuilt ? "rebuilt" : "kept")})", !rebuilt);
        ta.Shape(PW / (float)PH);
        var sample = FrameCost(gpu, () => ta.Sample(pic4K), 30);
        double gpuMs = GpuTime(gpu, () => ta.Sample(pic4K)), gpuMs1080 = GpuTime(gpu, () => ta.Sample(picA));
        Log.Info($"perf: ambilight a theater frame, no glow (Theater's own) {bare.ms:0.000} ms {bare.bytes} B");
        Log.Info($"perf: ambilight a theater frame, glow at rest {rest.ms:0.000} ms {rest.bytes} B");
        Log.Info($"perf: ambilight a theater frame, glow easing (10 readings/s) {moving.ms:0.000} ms {moving.bytes} B");
        Log.Info($"perf: ambilight writing the glow ({ta.TW} x {ta.TH}) {compose.ms:0.000} ms {compose.bytes} B (with its flush; " +
                 $"the work alone {composeCpu:0.000} ms)");
        Log.Info($"perf: ambilight a new shape (another screen / a window resized) {shapeMs:0.000} ms");
        Log.Info($"perf: ambilight sampling a 4K picture (10x a second at most): CPU {sample.ms:0.000} ms {sample.bytes} B, GPU {gpuMs:0.000} ms " +
                 $"(1080p: {gpuMs1080:0.000} ms)");
        Want($"perf: the glow allocates nothing a frame (at rest {rest.bytes - bare.bytes} B, easing {moving.bytes - bare.bytes} B more than without it, " +
             $"writing {compose.bytes} B)", rest.bytes == bare.bytes && moving.bytes == bare.bytes && compose.bytes == 0);
        theater.Stop(vr, tcfg);
        Frames(0.7f, true);
        Want($"theater: stopped, the glow goes with the screen ({theater.AmbilightAlpha:0.00})", theater.AmbilightAlpha == 0);
        // Look > Motion off: theater comes back at full strength at once - the glow must not light up in the last picture's colours
        // first and ease over (a white picture now: grey light from its very first frame)
        {
            var motion = Motion.Level;
            Motion.Level = MotionLevel.Off;
            scr.Pic = picW;
            scr.Fade = 1;
            theater.Start(scr, scr, vr, tcfg);
            int firstAt = -1;
            bool stale = false;
            for (int f = 0; f < 10; f++)
            {
                scr.Frame++;
                theater.Update(vr, Dt, tcfg.Dim, live);
                GpuIdle(gpu);
                if (theater.AmbilightAlpha <= 0 || !ta.HasReading) continue;
                if (firstAt < 0) firstAt = f;
                var e = ta.Eased(Ambilight.Left(8));
                stale |= MathF.Max(MathF.Abs(e.X - e.Y), MathF.Abs(e.Y - e.Z)) > 0.03f;
            }
            Motion.Level = motion;
            Want($"theater: Motion off, on again with another picture - the glow in its colours from its first frame (frame {firstAt}, " +
                 $"{(stale ? "the last picture's colours first" : "no old colours")})", firstAt >= 0 && firstAt <= 3 && !stale);
            theater.Stop(null, null);
        }

        // ---- pictures ----
        var shots = new List<(string label, string path)>();
        string Shot(string name, string label, uint[] glow, uint[] screen, bool room = true)
        {
            string path = Path.Combine(dir, $"ambilight_{name}.png");
            using var bmp = Composed(glow, tw, th, hole, screen, PW, PH, room);
            bmp.Save(path, ImageFormat.Png);
            shots.Add((label, path));
            return path;
        }
        Shot("glow", "the glow alone (red / blue picture), over black", glowA, null, false);
        Shot("view_red_blue", "wall + glow + screen: red left edge, blue right", glowA, pxA);
        Shot("view_grey", "wall + glow + a grey screen", glowA, Array.Empty<uint>());
        Shot("view_cut", "0.15 s after a hard cut to green", glowCut, pxB);
        Shot("view_green", "settled on green", glowB, pxB);
        Shot("view_white", "a white picture: the cap", glowW, pxW);
        Shot("view_corners", "four coloured sides: the corners", glowQ, pxQ);
        Sheet(shots, Path.Combine(dir, "ambilight_sheet.png"));

        // the settings' Theater section, every style
        var host = new FakeHost { Cfg = cfg, GpuName = "Made-up GPU", MonitorList = new List<MonitorInfo>(), Data = new PanelData() };
        var view = new SettingsView(host) { Tab = SettingsView.TabLayouts };
        var ui = new Ui(painter);
        var ptr = new PointerState();
        using var surf = new Surface(gpu, 1500, 960);
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style == "modern" ? null : style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            ui.ResetAnimations();
            for (int i = 0; i < 2; i++)
            {
                if (Theme.Classic) view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
                else { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
            }
            surf.SavePng(gpu, Path.Combine(dir, $"ambilight_settings{tag}.png"));
        }
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);

        foreach (var s in new[] { picA, picB, picW, picQ, pic4K }) s.Dispose();
        System.Globalization.CultureInfo.CurrentCulture = culture;
        Log.Info($"ambilight: {(fails == 0 ? "all checks True" : fails + " check(s) False")} - pictures in {dir}");
        return 0;
    }

    static void GpuIdle(Gpu gpu)
    {
        using var q = gpu.Device.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.Event));
        gpu.Ctx.End(q);
        gpu.Ctx.Flush();
        while (gpu.Ctx.GetData(q, IntPtr.Zero, 0, Vortice.Direct3D11.AsyncGetDataFlags.None).Code != 0) Thread.SpinWait(50);
    }

    // what a piece of GPU work takes on the GPU (timestamps round it; the best of 10)
    static unsafe double GpuTime(Gpu gpu, Action work)
    {
        using var dis = gpu.Device.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.TimestampDisjoint));
        using var t0 = gpu.Device.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.Timestamp));
        using var t1 = gpu.Device.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.Timestamp));
        double best = double.MaxValue;
        for (int i = 0; i < 10; i++)
        {
            GpuIdle(gpu);
            gpu.Ctx.Begin(dis);
            gpu.Ctx.End(t0);
            work();
            gpu.Ctx.End(t1);
            gpu.Ctx.End(dis);
            gpu.Ctx.Flush();
            Vortice.Direct3D11.QueryDataTimestampDisjoint d;
            while (gpu.Ctx.GetData(dis, (IntPtr)(&d), (uint)sizeof(Vortice.Direct3D11.QueryDataTimestampDisjoint), Vortice.Direct3D11.AsyncGetDataFlags.None).Code != 0) Thread.SpinWait(50);
            ulong a, b;
            while (gpu.Ctx.GetData(t0, (IntPtr)(&a), 8, Vortice.Direct3D11.AsyncGetDataFlags.None).Code != 0) Thread.SpinWait(50);
            while (gpu.Ctx.GetData(t1, (IntPtr)(&b), 8, Vortice.Direct3D11.AsyncGetDataFlags.None).Code != 0) Thread.SpinWait(50);
            if (!d.Disjoint && d.Frequency > 0) best = Math.Min(best, (b - a) * 1000.0 / d.Frequency);
        }
        return best;
    }

    static float Lum(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
    static string Fmt(Vector3? c) => c is { } v ? $"{v.X:0.00} {v.Y:0.00} {v.Z:0.00}" : "none";

    static int MaxChannel(uint[] px)
    {
        int m = 0;
        foreach (uint c in px) m = Math.Max(m, Math.Max((int)(c >> 16 & 255), Math.Max((int)(c >> 8 & 255), (int)(c & 255))));
        return m;
    }

    static int MaxDiff(uint a, uint b)
    {
        int d = 0;
        for (int s = 0; s < 32; s += 8) d = Math.Max(d, Math.Abs((int)(a >> s & 255) - (int)(b >> s & 255)));
        return d;
    }

    // The glow as you would see it, straight on: a made-up room, the dark wall over it (the default 60 %), the glow (premultiplied, its
    // texture blown up smoothly like the compositor does) and the screen in its hole (screen: the picture; empty: grey; null: none)
    static Bitmap Composed(uint[] glow, int tw, int th, RectangleF hole, uint[] screen, int pw, int ph, bool room)
    {
        const int OW = 1100;
        int oh = (int)MathF.Round(OW * th / (float)tw);
        float sx = tw / (float)OW, sy = th / (float)oh;
        var bmp = new Bitmap(OW, oh, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, OW, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new int[OW];
        var rh = new RectangleF(hole.X / sx, hole.Y / sy, hole.Width / sx, hole.Height / sy);
        for (int y = 0; y < oh; y++)
        {
            for (int x = 0; x < OW; x++)
            {
                Vector3 c;
                if (room)
                {
                    // the room: a warm wall, a lit window up left, a floor - made up
                    float fy = y / (float)oh, fx = x / (float)OW;
                    c = fy > 0.78f ? new Vector3(0.36f, 0.30f, 0.25f) : Vector3.Lerp(new Vector3(0.62f, 0.58f, 0.52f), new Vector3(0.48f, 0.44f, 0.40f), fy);
                    if (fx > 0.05f && fx < 0.2f && fy > 0.1f && fy < 0.45f) c = new Vector3(0.85f, 0.88f, 0.92f);
                    c *= 1 - 0.6f;                              // the wall, 60 % dark
                }
                else c = Vector3.Zero;
                // the glow, bilinear (clamped at its edge), premultiplied over
                float gx = Math.Clamp((x + 0.5f) * sx - 0.5f, 0, tw - 1), gy = Math.Clamp((y + 0.5f) * sy - 0.5f, 0, th - 1);
                int x0 = (int)gx, y0 = (int)gy, x1 = Math.Min(x0 + 1, tw - 1), y1 = Math.Min(y0 + 1, th - 1);
                float fx0 = gx - x0, fy0 = gy - y0;
                Vector4 G(uint p) => new Vector4(p >> 16 & 255, p >> 8 & 255, p & 255, p >> 24) / 255f;
                var g = Vector4.Lerp(Vector4.Lerp(G(glow[y0 * tw + x0]), G(glow[y0 * tw + x1]), fx0), Vector4.Lerp(G(glow[y1 * tw + x0]), G(glow[y1 * tw + x1]), fx0), fy0);
                c = new Vector3(g.X, g.Y, g.Z) + c * (1 - g.W);
                if (screen != null && rh.Contains(x + 0.5f, y + 0.5f))
                {
                    if (screen.Length == 0) c = new Vector3(0.5f);
                    else
                    {
                        int px = Math.Clamp((int)((x + 0.5f - rh.X) / rh.Width * pw), 0, pw - 1), py = Math.Clamp((int)((y + 0.5f - rh.Y) / rh.Height * ph), 0, ph - 1);
                        uint p = screen[py * pw + px];
                        c = new Vector3(p >> 16 & 255, p >> 8 & 255, p & 255) / 255f;
                    }
                }
                c = Vector3.Clamp(c, Vector3.Zero, Vector3.One) * 255;
                row[x] = unchecked((int)0xFF000000) | (int)(c.X + 0.5f) << 16 | (int)(c.Y + 0.5f) << 8 | (int)(c.Z + 0.5f);
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, OW);
        }
        bmp.UnlockBits(bd);
        return bmp;
    }

    // the glow's write cadence after a hard cut: the eased edge colour (green, 0..1) frame by frame as a line, and the green the glow was
    // last written with as a stair over it - the two run together while the colour moves fast, the stair's steps are a third of a
    // frame's... three frames' moves in the tail, and it ends on the line
    static void CadenceGraph(string path, List<float> eased, List<float> written, float from, float to)
    {
        const int PxF = 10, L = 46, Top = 34, GH = 240, Bottom = 40;
        int n = eased.Count, w = L + n * PxF + 20, h = Top + GH + Bottom;
        float lo = MathF.Min(from, to), hi = MathF.Max(from, to), span = MathF.Max(1e-3f, hi - lo);
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(255, 20, 18, 28));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 11, GraphicsUnit.Pixel);
        float Y(float v) => Top + GH - (v - lo) / span * GH;
        float X(int i) => L + i * PxF;
        using (var grid = new Pen(Color.FromArgb(255, 44, 40, 58)))
            for (int f = 0; f <= n; f += 9) { g.DrawLine(grid, X(f), Top, X(f), Top + GH); g.DrawString($"{f / 90f:0.0}s", font, Brushes.Gray, X(f) - 10, Top + GH + 4); }
        g.DrawString($"{to:0.00}", font, Brushes.Gray, 4, Y(to) - 7);
        g.DrawString($"{from:0.00}", font, Brushes.Gray, 4, Y(from) - 7);
        var pts = new PointF[n];
        for (int i = 0; i < n; i++) pts[i] = new PointF(X(i), Y(eased[i]));
        using (var pen = new Pen(Color.FromArgb(255, 90, 200, 120), 2)) g.DrawLines(pen, pts);
        using (var pen = new Pen(Color.FromArgb(255, 255, 200, 80), 1.5f))
            for (int i = 1; i < n; i++)
            {
                g.DrawLine(pen, X(i - 1), Y(written[i - 1]), X(i), Y(written[i - 1]));
                if (written[i] != written[i - 1]) g.DrawLine(pen, X(i), Y(written[i - 1]), X(i), Y(written[i]));
            }
        int writes = 0;
        for (int i = 1; i < n; i++) if (written[i] != written[i - 1]) writes++;
        g.DrawString($"the Ambilight after a hard cut (red to green): the eased edge colour's green every frame (line) and the green the glow was written with (stair) - " +
                     $"{writes + 1} writes in {n} frames", font, Brushes.Gainsboro, 6, 8);
        g.DrawString("every frame while it moves 2 levels or more a frame, every third frame in the tail, once more at rest; the stair is never 4 levels off the line",
                     font, Brushes.Gray, 6, h - 18);
        bmp.Save(path, ImageFormat.Png);
    }

    static void Sheet(List<(string label, string path)> shots, string path)
    {
        const int W = 540, Gap = 12, Label = 24;
        int h0;
        using (var b0 = new Bitmap(shots[0].path)) h0 = (int)MathF.Round(W * b0.Height / (float)b0.Width);
        int cols = 2, rows = (shots.Count + 1) / 2;
        using var sheet = new Bitmap(cols * W + (cols + 1) * Gap, rows * (h0 + Label + Gap) + Gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 32, 34, 40));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int i = 0; i < shots.Count; i++)
            {
                int x = Gap + i % cols * (W + Gap), y = Gap + i / cols * (h0 + Label + Gap);
                g.DrawString(shots[i].label, font, Brushes.White, x, y + 4);
                using var b = new Bitmap(shots[i].path);
                g.DrawImage(b, x, y + Label, W, h0);
            }
        }
        sheet.Save(path, ImageFormat.Png);
        Log.Info("ambilight: the pictures side by side: " + path);
    }
}
