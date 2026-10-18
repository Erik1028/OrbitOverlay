using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;

namespace Orbit;

// --snapshot's part for the Cyber night city's life (Panels/TheaterCityLife.cs), ORBIT_SNAP_ONLY=citylife: a made-up headset and screen,
// no VR (the cars' places worked out, never sent), its own clock - 90 frames a second, minutes of it in seconds:
//  - the lamps: recorded as the band is drawn; put together on the CPU at their levels as drawn they are the very texels Direct2D drew;
//  - live (Motion Full): the band under the lamps read back a frame later (nothing waits); 5 ticks a second at most, small patches only,
//    the band sent again only when a lamp changed; windows on / off every few seconds with the lit count steady; one sign at a time
//    stutters, partly, tens of seconds apart; the antennas' soft beat; the stars' slow small twinkle; nothing allocated a frame; costs;
//  - the cars: in the city's level frame (level, facing you), nearer than the band and behind the screen, never near the screen and its
//    glow, slow, at most three, fading smoothly;
//  - Motion Reduced / Off, theater gone: the band back to exactly the city as drawn, the cars away, nothing done a frame;
//  - pictures in <dir>/citylife: a stretch of the band over seconds (windows, a sign, antennas), a sign's stutter and an antenna's beat
//    tick by tick, the whole skyline with the cars over 17 s (the screen's place greyed), a car crossing, the cars' textures.
static partial class Tools
{
    static int CityLifeOnly(ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        var look0 = cfg.Data.Appearance;
        bool live0 = CyberFx.Live, still0 = Motion.Still;
        cfg.Data.Appearance = new AppearanceConfig { Style = CyberHud.Id, Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        CyberFx.Live = true;
        Motion.Still = false;
        int fails = 0;
        try { fails = CityLifeRun(painter, gpu, Path.Combine(dir, "citylife")); }
        catch (Exception e) { Log.Error("city life: " + e); fails++; }
        finally
        {
            CyberFx.Live = live0;
            Motion.Still = still0;
            cfg.Data.Appearance = look0;
            Theme.Apply(look0);
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        Log.Info($"city life: {(fails == 0 ? "all checks True" : fails + " check(s) False")} - pictures in {Path.Combine(dir, "citylife")}");
        return fails == 0 ? 0 : 1;
    }

    static int CityLifeRun(Painter painter, Gpu gpu, string od)
    {
        Directory.CreateDirectory(od);
        foreach (var f in Directory.GetFiles(od, "*.png")) File.Delete(f);
        int fails = 0;
        void Want(string what, bool ok) { Log.Info($"city life: {what} {ok} (want True)"); if (!ok) fails++; }
        const float Dt = 1 / 90f;
        var vr = new VrSession();
        vr.Device[0] = Matrix4x4.CreateTranslation(0, 1.5f, 0);
        vr.DeviceValid[0] = true;
        var scr = new AmbScreen { HomeWorld = Matrix4x4.CreateTranslation(0, 1.4f, -1.1f) };
        var tcfg = new TheaterConfig();
        using var th = new Theater(gpu, painter) { LifeSeed = 1018 };
        th.Start(scr, scr, vr, tcfg);
        var wall = Theater.CityAsBackdrop(th.GridForTest(0).ReadPixels(gpu));
        void Frames(float seconds, float bandA = 1)
        {
            int n = (int)MathF.Round(seconds * 90);
            for (int f = 0; f < n; f++) { th.CityFrameForTest(Dt, bandA); if (f % 90 == 89) GpuIdle(gpu); }
        }

        // ---- the lamps ----
        th.SkylineForTest();
        GpuIdle(gpu);
        // what a draw of the band costs (the median of 7, the GPU idle before each): as drawn, and without its lamps
        double Median(bool bare)
        {
            var t = new List<double>();
            for (int i = 0; i < 7; i++) { GpuIdle(gpu); t.Add(th.SkylineDrawMsForTest(bare)); }
            t.Sort();
            return t[3];
        }
        double drawMed = Median(false), bareMed = Median(true);
        var sw = Stopwatch.StartNew();
        var band = th.SkylineForTest();               // (the second time: warm, as in VR after the first theater)
        GpuIdle(gpu);
        double drawMs = sw.Elapsed.TotalMilliseconds;
        var asDrawn = band.ReadPixels(gpu);
        var kinds = th.LampKinds;
        Want($"lamps: {th.LampCount} recorded as the band is drawn ({drawMs:0.0} ms with the drawing) - {kinds.wins} windows ({kinds.lit} lit), {kinds.signs} signs, " +
             $"{kinds.ants} antenna lights, {kinds.stars} new stars; the biggest patch {th.LampPatchMax} texels; {th.LampsCovered} partly under a building drawn " +
             $"later, {th.LampsCoveredTwice} under two (want 0)",
            kinds.wins > 300 && kinds.lit > 50 && kinds.signs >= 4 && kinds.ants >= 8 && kinds.stars == 8 && th.LampPatchMax <= 9 * 56 && th.LampsCoveredTwice == 0);

        // ---- live: the band under the lamps drawn and copied in the first frame, read back in a later one ----
        th.CityFrameForTest(Dt, 1);
        bool readyAt1 = th.LifeReadyForTest;
        double build1 = th.LifeBuildMs;
        int frameAt = 1;
        double worst = 0;
        while (!th.LifeReadyForTest && frameAt < 400)
        {
            GpuIdle(gpu);
            var sf = Stopwatch.StartNew();
            th.CityFrameForTest(Dt, 1);
            worst = Math.Max(worst, sf.Elapsed.TotalMilliseconds);
            frameAt++;
            if (!th.LifeReadyForTest) System.Threading.Thread.Sleep(1);
        }
        // (not ready after frame 1 is no race: the copy is made in frame 1, mapped in a later one, read out on its thread - three steps of
        // LifeBase at the least. The frames' time is this PC's, a busy moment of it too: logged, not checked)
        Want($"base: the band without its lamps drawn and copied in frame 1 ({build1:0.00} ms), nothing waits for it ({(readyAt1 ? "read at once" : "not read yet")}), " +
             $"read out on a thread of its own, here in frame {frameAt} (want under 400); {2 * th.LampBaseBytes / 1024} KB kept)", !readyAt1 && th.LifeReadyForTest && frameAt < 400);
        Log.Info($"city life: (timing, not a check) the frames after the first while it was read out took {worst:0.00} ms at most (under 1 on an idle PC)");
        th.WriteAllLampsForTest(true);
        var (dc0, mc0) = TexelDiff(asDrawn, band.ReadPixels(gpu));
        th.WriteAllLampsForTest(false);
        var (d0, m0) = TexelDiff(asDrawn, band.ReadPixels(gpu));
        Want($"exact: every lamp's patch put together on the CPU at its level as drawn is Direct2D's picture but for the GPU's rounding - {dc0} of " +
             $"{th.LampBaseBytes / 4} texels a level off at most ({mc0}, want 1 or less); written as city life writes a lamp as drawn: {d0} differ (want 0)",
            mc0 <= 1 && d0 == 0);

        // ---- two minutes live ----
        var lvl = th.LevelForTest;
        Matrix4x4.Invert(lvl, out var inv);
        float edge = th.FrontDegForTest(false), front = th.FrontDegForTest(true);
        float screenD = Vector3.Distance(th.View.Place.Translation, lvl.Translation);
        const float Run = 120;
        int ticks0 = th.LifeTicks, sends0 = th.LifeSends, patches0 = th.LifePatches, buzz0 = th.LifeBuzzes, on0 = th.LifeWinOn, off0 = th.LifeWinOff;
        long texels0 = th.LifeTexels;
        int lit0 = kinds.lit, litMin = int.MaxValue, litMax = 0, signsDim = 0;
        float signMin = 1, antMin = 1, antMax = 0, starLo = 1, starHi = 0, starStep = 0;
        var buzzAt = new List<float>();
        var starLast = new float[kinds.stars];
        var stars = th.StarsForTest;
        int upBad = 0, faceBad = 0, rBad = 0, elBad = 0, frontBad = 0, azBad = 0, visMax = 0, trips = 0;
        float speedMax = 0, alphaStep = 0;
        var lastAz = new float[3];
        var lastA = new float[3];
        var seen = new double[3];
        int frames = (int)(Run * 90);
        for (int f = 0; f < frames; f++)
        {
            int tb = th.LifeTicks, bz = th.LifeBuzzes;
            th.CityFrameForTest(Dt, 1);
            if (f % 90 == 89) GpuIdle(gpu);
            float t = (f + 1) * Dt;
            if (th.LifeBuzzes != bz) buzzAt.Add(t);
            if (th.LifeTicks != tb)
            {
                int dim = 0;
                foreach (int s in th.SignsForTest) { float l = th.LampLevelForTest(s); if (l < 0.999f) { dim++; signMin = MathF.Min(signMin, l); } }
                signsDim = Math.Max(signsDim, dim);
                int lit = th.LitNowForTest;
                litMin = Math.Min(litMin, lit); litMax = Math.Max(litMax, lit);
                if (t > 2.5f)              // (stars and antennas fade into their motion over the first 2 s)
                {
                    foreach (int a in th.AntennasForTest) { float l = th.LampLevelForTest(a); antMin = MathF.Min(antMin, l); antMax = MathF.Max(antMax, l); }
                    for (int s = 0; s < stars.Length; s++)
                    {
                        float l = th.LampLevelForTest(stars[s]);
                        if (t > 2.8f) starStep = MathF.Max(starStep, MathF.Abs(l - starLast[s]));
                        starLo = MathF.Min(starLo, l); starHi = MathF.Max(starHi, l);
                        starLast[s] = l;
                    }
                }
            }
            int vis = 0;
            for (int i = 0; i < 3; i++)
            {
                float a = th.CarAlpha[i];
                float daz = th.CarAz[i] - lastAz[i];
                daz -= 360 * MathF.Round(daz / 360);
                if (f > 0 && a > 0 && lastA[i] > 0) speedMax = MathF.Max(speedMax, MathF.Abs(daz) / Dt);
                if (f > 0) alphaStep = MathF.Max(alphaStep, MathF.Abs(a - lastA[i]));
                if (a > 0 && lastA[i] <= 0) trips++;
                lastAz[i] = th.CarAz[i];
                lastA[i] = a;
                if (a <= 0.004f) continue;
                vis++;
                seen[i] += Dt;
                var m = th.CarWorld[i] * inv;                   // in the city's frame
                var p = m.Translation;
                float rh = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
                if (Vector3.Dot(Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23)), Vector3.UnitY) < 0.9999f) upBad++;
                if (Vector3.Dot(Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33)), -Vector3.Normalize(new Vector3(p.X, 0, p.Z))) < 0.9999f) faceBad++;
                if (MathF.Abs(rh - th.CarRadius[i]) > 1e-3f || rh > th.BandRadiusForTest - 0.04f || rh < screenD + 0.15f) rBad++;
                float el = MathF.Atan2(p.Y, rh) * 180 / MathF.PI, az = MathF.Atan2(p.X, -p.Z) * 180 / MathF.PI;
                if (el < 1 || el > 13) elBad++;
                if (MathF.Abs(az) < front + 0.5f) frontBad++;
                float dd = az - th.CarAz[i];
                if (MathF.Abs(dd - 360 * MathF.Round(dd / 360)) > 0.01f) azBad++;
            }
            visMax = Math.Max(visMax, vis);
        }
        int ticks = th.LifeTicks - ticks0, sends = th.LifeSends - sends0, patches = th.LifePatches - patches0;
        long texels = th.LifeTexels - texels0;
        Want($"rate: {ticks} ticks in {Run:0} s ({ticks / Run:0.00} a second, want 5 at most), the band sent to SteamVR {sends} times ({sends / Run:0.00} a second: " +
             $"only when a lamp changed)", ticks <= Run * 5 + 1 && ticks >= Run * 5 - 3 && sends <= ticks);
        Want($"small: a tick writes at most {th.LifeMaxPatches} patches, {th.LifeMaxTexels} texels ({100.0 * th.LifeMaxTexels / (Theater.BackW * 4 * 512):0.000} % of the band); " +
             $"on average {patches / Run:0.0} patches and {texels / Run:0} texels a second", th.LifeMaxTexels <= 4000 && th.LifeMaxPatches <= 60);
        int won = th.LifeWinOn - on0, woff = th.LifeWinOff - off0;
        Want($"windows: {won} lit up and {woff} went dark in {Run:0} s (one to three every 2.4-4.8 s), lit now and then {litMin}..{litMax} of the {lit0} drawn " +
             $"(steady: within 5)", won >= 40 && won <= 150 && won == woff && litMin >= lit0 - 5 && litMax <= lit0 + 5);
        float gap = float.MaxValue;
        for (int i = 1; i < buzzAt.Count; i++) gap = MathF.Min(gap, buzzAt[i] - buzzAt[i - 1]);
        Want($"signs: {buzzAt.Count} stutters in {Run:0} s (first at {(buzzAt.Count > 0 ? buzzAt[0] : -1):0.0} s, then {(buzzAt.Count > 1 ? gap : 0):0} s apart at least: want 20+), " +
             $"never two at once (at most {signsDim} dimmed together), never below {signMin:0.00} of its light (want 0.45 or more: a partial dip)",
            buzzAt.Count >= 3 && gap >= 20 && signsDim <= 1 && signMin >= 0.449f);
        Want($"antennas: a soft beat between {antMin:0.00} and {antMax:0.00} (never quite off)", antMin >= 0.24f && antMin <= 0.26f && antMax >= 0.999f);
        Want($"stars: a slow small twinkle between {starLo:0.00} and {starHi:0.00} of their light, at most {starStep:0.000} a tick (want 0.08 or less)",
            starLo >= 0.59f && starHi <= 1.001f && starStep <= 0.08f && starHi - starLo >= 0.2f);
        Want($"cars: {trips} trips in {Run:0} s, at most {visMax} in sight at once (want 1..3), seen {seen[0]:0} / {seen[1]:0} / {seen[2]:0} s; no faster than {speedMax:0.00}°/s " +
             $"(want 3.5 at most); their alpha at most {alphaStep:0.0000} a frame (want 0.01: fading in and out, no pops)",
            trips >= 6 && visMax >= 1 && visMax <= 3 && seen[0] > 10 && seen[1] > 10 && seen[2] > 10 && speedMax <= 3.5f && alphaStep <= 0.01f);
        Want($"cars: in the city's level frame - tilted {upBad} frame(s) (want 0), not facing you {faceBad} (want 0), off their place {azBad} (want 0); " +
             $"between the screen ({screenD:0.00} m) and the band ({th.BandRadiusForTest:0.00} m) - outside {rBad} (want 0); 1-13° up - outside {elBad} (want 0); " +
             $"never near the screen and its glow (±{front:0.0}°) - {frontBad} (want 0)",
            upBad == 0 && faceBad == 0 && azBad == 0 && rBad == 0 && elBad == 0 && frontBad == 0);

        // ---- costs ----
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 900; f++) th.CityFrameForTest(Dt, 1);
        long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        GpuIdle(gpu);
        th.LifeTickMaxMs = 0;                           // (the first ticks' JIT left out)
        var swf = Stopwatch.StartNew();
        for (int f = 0; f < 900; f++) th.CityFrameForTest(Dt, 1);
        double work = swf.Elapsed.TotalMilliseconds / 900;
        GpuIdle(gpu);
        double tickMs0 = th.LifeTickMs;
        int tickN0 = th.LifeTicks;
        var cost = FrameCost(gpu, () => th.CityFrameForTest(Dt, 1), 450);
        double tickAvg = (th.LifeTickMs - tickMs0) / Math.Max(1, th.LifeTicks - tickN0);
        Log.Info($"perf: city life a frame (lamps' ticks included, cars' places; SteamVR calls left out) {work:0.0000} ms of work on average; " +
                 $"with a GPU flush each frame {cost.ms:0.0000} ms {cost.bytes} B");
        Log.Info($"perf: city life a tick (levels, patches put together, UpdateSubresource) {tickAvg:0.0000} ms on average, {th.LifeTickMaxMs:0.000} ms the most; " +
                 $"{tickAvg * 5:0.000} ms a second");
        Log.Info($"perf: city life once: the band without its lamps {build1:0.00} ms (a draw of it: {bareMed:0.00} ms, the median of 7) + reading it back " +
                 $"{th.LifeBuildMs - build1:0.00} ms on the frames; the band drawn with its lamps recorded {drawMed:0.00} ms (the median of 7; once, as before), " +
                 $"of it putting the lamps' list together {th.LampsEndMs:0.00} ms");
        Log.Info($"perf: city life in VR a frame: SetAbsolute + SetAlpha for each car in sight (3 at most), SetTexture of the band {sends / Run:0.0} times a second");
        Want($"alloc: nothing allocated in 10 s of frames ({alloc} B) nor a frame ({cost.bytes} B)", alloc == 0 && cost.bytes == 0);

        // ---- pictures (live) ----
        CityPictures(th, gpu, wall, od, Frames, edge);

        // ---- Motion Reduced / Off, theater gone: the city as drawn, nothing a frame ----
        foreach (var lvl2 in new[] { MotionLevel.Reduced, MotionLevel.Off })
        {
            Motion.Level = MotionLevel.Full;
            Frames(6);
            bool wasDirty = th.LifeDirtyForTest;
            Motion.Level = lvl2;
            th.CityFrameForTest(Dt, 1);
            var (dc, mc) = TexelDiff(asDrawn, band.ReadPixels(gpu));
            bool carsAway = Array.TrueForAll(th.CarAlpha, x => x == 0);
            int t1 = th.LifeTicks, s1 = th.LifeSends;
            Frames(3);
            Want($"Motion {lvl2}: live a moment before (lamps changed {wasDirty}), now the band exactly the city as drawn - {dc} texel(s) differ (want 0), the cars away " +
                 $"{carsAway}, nothing done in 3 s ({th.LifeTicks - t1} ticks, {th.LifeSends - s1} sends)",
                wasDirty && dc == 0 && carsAway && th.LifeTicks == t1 && th.LifeSends == s1 && !th.LifeOnForTest && !th.LifeDirtyForTest);
        }
        Motion.Level = MotionLevel.Full;
        Frames(6);
        bool dirty2 = th.LifeDirtyForTest;
        th.CityFrameForTest(Dt, 0);                     // the band hidden: theater gone
        var (dg, _) = TexelDiff(asDrawn, band.ReadPixels(gpu));
        int t2 = th.LifeTicks;
        var off = FrameCost(gpu, () => th.CityFrameForTest(Dt, 0), 450);
        Want($"theater gone: the band exactly as drawn ({dg} texels differ, want 0; lamps changed before {dirty2}), the cars away " +
             $"{Array.TrueForAll(th.CarAlpha, x => x == 0)}, nothing done ({th.LifeTicks - t2} ticks; a frame {off.ms:0.0000} ms {off.bytes} B)",
            dirty2 && dg == 0 && Array.TrueForAll(th.CarAlpha, x => x == 0) && th.LifeTicks == t2 && off.bytes == 0);
        // the cars' textures as SteamVR gets them: the car's (and its mirror image for going left) and the drone's, uploaded as drawn
        {
            var (right, left, droneTex) = th.CarTexturesForTest();
            uint[] r = right.ReadPixels(gpu), l = left.ReadPixels(gpu), dr = droneTex.ReadPixels(gpu), car = th.CarPixelsForTest(false);
            bool asDrawn2 = r.AsSpan().SequenceEqual(car) && dr.AsSpan().SequenceEqual(th.CarPixelsForTest(true)), mirror = true;
            for (int y = 0; y < right.H; y++) for (int x = 0; x < right.W; x++) mirror &= l[y * right.W + x] == r[y * right.W + right.W - 1 - x];
            Want($"car textures: uploaded as drawn {asDrawn2} (want True), the one going left the mirror image of the one going right {mirror} (want True)", asDrawn2 && mirror);
        }
        // the cars' textures: soft (no texel jumps more than a bit from its neighbour) and nothing at their rims
        foreach (bool drone in new[] { false, true })
        {
            var (w, h, _) = Theater.CarTexForTest(drone);
            var px = th.CarPixelsForTest(drone);
            int rim = 0, step = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int a = (int)(px[y * w + x] >> 24);
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) rim = Math.Max(rim, a);
                    if (x + 1 < w) step = Math.Max(step, Math.Abs(a - (int)(px[y * w + x + 1] >> 24)));
                    if (y + 1 < h) step = Math.Max(step, Math.Abs(a - (int)(px[(y + 1) * w + x] >> 24)));
                }
            Want($"{(drone ? "drone" : "car")} texture {w}x{h}: nothing at its rim ({rim}, want 3 or less), soft (the biggest alpha step between texels {step}, want 110 or less)",
                rim <= 3 && step <= 110);
        }
        return fails;
    }

    static (int n, int max) TexelDiff(uint[] a, uint[] b)
    {
        int n = 0, max = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            n++;
            max = Math.Max(max, MaxDiff(a[i], b[i]));
        }
        return (n, max);
    }

    // ---- the pictures ----

    const int CityRowsUp = 124, CityRowsDown = 6;       // the band's rows shown: this far above / below the horizon

    static void CityPictures(Theater th, Gpu gpu, uint[] wall, string od, Action<float, float> frames, float edge)
    {
        var band = th.BandForTest;
        int bw = band.W, mid = band.H / 2, y0 = mid - CityRowsUp, rows = CityRowsUp + CityRowsDown;
        uint[] Rows() { var all = band.ReadPixels(gpu); var r = new uint[bw * rows]; Array.Copy(all, y0 * bw, r, 0, r.Length); return r; }
        // the lamps whose texels differ between two pictures of the band (outlined in the next one)
        List<Rectangle> Changed(uint[] a, uint[] b)
        {
            var list = new List<Rectangle>();
            for (int i = 0; i < th.LampCount; i++)
            {
                var r = th.LampRectForTest(i);
                bool diff = false;
                for (int y = Math.Max(0, r.Y - y0); y < Math.Min(rows, r.Bottom - y0) && !diff; y++)
                    for (int x = r.X; x < r.Right && !diff; x++) diff = a[y * bw + x] != b[y * bw + x];
                if (diff) list.Add(r);
            }
            return list;
        }

        // a stretch of the band over 7 s: the most lively 384 texels of the side that is seen, the lamps that changed outlined
        var caps = new List<uint[]>();
        for (int i = 0; i < 15; i++) { caps.Add(Rows()); frames(0.5f, 1); }
        int bestX = 0;
        long best = -1;
        for (int x0 = 0; x0 + 384 <= bw; x0 += 32)
        {
            float az = MathF.Abs(((x0 + 192f) / bw - 0.5f) * 360);
            if (az < edge || az > edge + 75) continue;
            long act = 0;
            for (int c = 1; c < caps.Count; c++)
                for (int y = 0; y < rows; y++)
                    for (int x = x0; x < x0 + 384; x++) act += MaxDiff(caps[c][y * bw + x], caps[c - 1][y * bw + x]) > 8 ? 1 : 0;
            if (act > best) { best = act; bestX = x0; }
        }
        var strip = new List<(string, Bitmap)>();
        for (int c = 0; c < caps.Count; c += 2)
            strip.Add(($"t = {c * 0.5f:0.0} s{(c > 0 ? " (changed since the last outlined)" : "")}",
                CityView(caps[c], bw, rows, y0, wall, bestX, 0, 384, rows, 2, null, 0, c > 0 ? Changed(caps[c - 2], caps[c]) : null)));
        CityStack(strip, Path.Combine(od, "citylife_band_strip.png"),
            $"a stretch of the skyline beside the screen ({((bestX + 192f) / bw - 0.5f) * 360:0}° off it) over 7 s, 2x: windows fading on / off, the antenna lights' beat", cols: 2);

        // a sign stuttering, tick by tick
        int sign = -1;
        float signAz = 999;
        foreach (int s in th.SignsForTest)
        {
            var r = th.LampRectForTest(s);
            float az = MathF.Abs(((r.X + r.Width / 2f) / bw - 0.5f) * 360);
            if (az >= edge && az < signAz) { sign = s; signAz = az; }
        }
        if (sign >= 0)
        {
            var r = th.LampRectForTest(sign);
            int cx = Math.Clamp(r.X - 40, 0, bw - 90), cy = Math.Clamp(r.Y - 12 - y0, 0, rows - 1), ch = Math.Min(rows - cy, r.Height + 24);
            th.BuzzForTest(sign);
            var bz = new List<(string, Bitmap)>();
            for (int k = 0; k < 7; k++)
            {
                bz.Add(($"{k * 0.2f:0.0} s - {th.LampLevelForTest(sign):0.00}", CityView(Rows(), bw, rows, y0, wall, cx, cy, 90, ch, 4, null)));
                frames(0.2f, 1);
            }
            CityStack(bz, Path.Combine(od, "citylife_sign_stutter.png"), "a sign's stutter, a tick (0.2 s) a frame (4x): two partial dips, then lit again", cols: bz.Count);
        }

        // an antenna's soft beat, tick by tick
        var ants = th.AntennasForTest;
        if (ants.Length > 0)
        {
            int ant = ants[0];
            foreach (int a in ants) { float az = ((th.LampRectForTest(a).X + 3f) / bw - 0.5f) * 360; if (az > edge) { ant = a; break; } }
            var r = th.LampRectForTest(ant);
            int cx = Math.Clamp(r.X - 17, 0, bw - 40), cy = Math.Clamp(r.Y - 8 - y0, 0, rows - 30);
            var ab = new List<(string, Bitmap)>();
            for (int k = 0; k < 11; k++)
            {
                ab.Add(($"{k * 0.2f:0.0} s {th.LampLevelForTest(ant):0.00}", CityView(Rows(), bw, rows, y0, wall, cx, cy, 40, 30, 6, null)));
                frames(0.2f, 1);
            }
            CityStack(ab, Path.Combine(od, "citylife_antenna_beat.png"), "an antenna light's beat, 0.2 s a frame (6x): on about a second, soft down and up", cols: ab.Count);
        }

        // a star low over the skyline twinkling, 0.6 s a frame (8x)
        var stars = th.StarsForTest;
        if (stars.Length > 0)
        {
            int st = stars[0];
            foreach (int s in stars) if (MathF.Abs(((th.LampRectForTest(s).X + 2f) / bw - 0.5f) * 360) > edge) { st = s; break; }
            var r = th.LampRectForTest(st);
            var sb = new List<(string, Bitmap)>();
            for (int k = 0; k < 8; k++)
            {
                sb.Add(($"{k * 0.6f:0.0} s {th.LampLevelForTest(st):0.00}", CityView(band.ReadPixels(gpu), bw, band.H, 0, wall, Math.Clamp(r.X - 8, 0, bw - 20), Math.Max(0, r.Y - 6), 20, 16, 8, null)));
                frames(0.6f, 1);
            }
            CityStack(sb, Path.Combine(od, "citylife_star_twinkle.png"), $"one of the new stars ({((r.X + 2f) / bw - 0.5f) * 360:0}° round, {MathF.Atan((256 - r.Y - 2) / 256f * MathF.PI / 8) * 180 / MathF.PI:0.0}° up), 0.6 s a frame (8x): a very slight twinkle", cols: sb.Count);
        }

        // both sides of the skyline (30-150° off the screen, full size) with the cars over 17.5 s, from a moment two are out; each car
        // and the drone marked with faint corners
        for (int f = 0; f < 90 * 90; f++)
        {
            int n = 0;
            foreach (float a in th.CarAlpha) if (a > 0.3f) n++;
            if (n >= 2) break;
            frames(1 / 90f, 1);
        }
        int side = (int)(120 / 360f * bw), leftX = (int)((-150 / 360f + 0.5f) * bw), rightX = (int)((30 / 360f + 0.5f) * bw);
        var pano = new List<(string, Bitmap)>();
        for (int k = 0; k < 8; k++)
        {
            var px = Rows();
            string cars = "";
            for (int i = 0; i < 3; i++) if (th.CarAlpha[i] > 0.004f) cars += $" {(i == 2 ? "drone" : "car " + (i + 1))} {th.CarAz[i]:0}°/{th.CarEl[i]:0.0}° a {th.CarAlpha[i]:0.00};";
            pano.Add(($"t = {k * 2.5f:0.0} s, left (150-30° off){(cars.Length > 0 ? " -" + cars : "")}", CityView(px, bw, rows, y0, wall, leftX, 0, side, rows, 1, th, edge)));
            pano.Add(("right (30-150° off)", CityView(px, bw, rows, y0, wall, rightX, 0, side, rows, 1, th, edge)));
            frames(2.5f, 1);
        }
        CityStack(pano, Path.Combine(od, "citylife_cars_skyline.png"), "the cars and the drone along both sides of the skyline (full size, marked), 2.5 s apart", cols: 2);

        // a car crossing: from the moment it sets off, a frame a second, the stretch it flies along (3x)
        int car = -1;
        var lastA = (float[])th.CarAlpha.Clone();
        for (int f = 0; f < 90 * 90 && car < 0; f++)
        {
            frames(1 / 90f, 1);
            for (int i = 0; i < 2 && car < 0; i++) if (lastA[i] <= 0 && th.CarAlpha[i] > 0) car = i;
            Array.Copy(th.CarAlpha, lastA, 3);
        }
        if (car >= 0)
        {
            float az0 = th.CarAz[car], dir = th.CarFlip[car] ? -1 : 1;
            float fromAz = dir > 0 ? az0 - 3 : az0 - 31;
            int cw = (int)(34 / 360f * bw), cx = (int)((fromAz / 360 + 0.5f) * bw);
            cx = Math.Clamp(cx, 0, bw - cw);
            int el = (int)(mid - MathF.Tan(th.CarEl[car] * MathF.PI / 180) / (MathF.PI / 8) * mid) - y0;
            int cy = Math.Clamp(el - 34, 0, rows - 64);
            var cs = new List<(string, Bitmap)>();
            for (int k = 0; k < 10; k++)
            {
                cs.Add(($"t = {k:0} s - {th.CarAz[car]:0.0}°, {th.CarEl[car]:0.0}° up, alpha {th.CarAlpha[car]:0.00}", CityView(Rows(), bw, rows, y0, wall, cx, cy, cw, 64, 3, th, edge)));
                frames(1, 1);
            }
            CityStack(cs, Path.Combine(od, "citylife_car_crossing.png"), $"a car setting off ({(dir > 0 ? "rightwards" : "leftwards")}), a frame a second (3x): out of the haze, gliding along");
        }

        // the cars' textures, blown up (8x) over the night
        var tex = new List<(string, Bitmap)>();
        foreach (bool drone in new[] { false, true })
        {
            var (w, h, deg) = Theater.CarTexForTest(drone);
            var px = th.CarPixelsForTest(drone);
            var bmp = new Bitmap(w * 8, h * 8, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h * 8; y++)
                for (int x = 0; x < w * 8; x++)
                {
                    uint p = px[y / 8 * w + x / 8];
                    float a = (p >> 24) / 255f;
                    Vector3 bg = new(0.06f, 0.03f, 0.1f), c = new Vector3((p >> 16) & 255, (p >> 8) & 255, p & 255) / 255f + bg * (1 - a);
                    bmp.SetPixel(x, y, Color.FromArgb(255, (int)(Math.Min(1, c.X) * 255), (int)(Math.Min(1, c.Y) * 255), (int)(Math.Min(1, c.Z) * 255)));
                }
            tex.Add(($"{(drone ? "drone" : "car")}: {w} x {h} texels, {deg}° wide near", bmp));
        }
        CityStack(tex, Path.Combine(od, "citylife_car_textures.png"), "the cars' textures (8x)", cols: 2);
    }

    // a stretch of the band as you'd see it at the default dim: the wall (CyberWall: its alpha the dim) and the band over it a little brighter,
    // the cars over that (th: drawn and marked, the screen's place greyed); s: the picture's size for a texel; marks: band rectangles outlined
    static Bitmap CityView(uint[] rows, int bw, int nrows, int y0, uint[] wall, int x0, int cy0, int w, int h, float s, Theater th, float edge = 0,
        List<Rectangle> marks = null)
    {
        const float Dim = 0.6f, BandA = 0.78f;
        int mid = 256, ww = Theater.BackW, wh = Theater.BackH;          // (the band's horizon: its middle row)
        var img = new Vector3[w * h];
        for (int y = 0; y < h; y++)
        {
            int by = cy0 + y, ty = y0 + by;
            float el = MathF.Atan((mid - (ty + 0.5f)) / mid * MathF.PI / 8);
            int wy = Math.Clamp((int)(wh / 2f - MathF.Tan(el) / (Theater.WallAspect * MathF.PI) * (wh / 2f)), 0, wh - 1);
            for (int x = 0; x < w; x++)
            {
                int bx = x0 + x;
                uint q = wall[wy * ww + Math.Clamp(bx * ww / bw, 0, ww - 1)];
                var c = Rgb3(q) * Dim;
                uint p = rows[by * bw + bx];
                c = Rgb3(p) * BandA + c * (1 - BandA * (p >> 24) / 255f);
                if (th != null)
                {
                    float az = ((bx + 0.5f) / bw - 0.5f) * 360;
                    if (MathF.Abs(az) < edge && el * 180 / MathF.PI < 17.5f) c = new Vector3(0.36f, 0.37f, 0.4f);
                }
                img[y * w + x] = c;
            }
        }
        var boxes = new List<(RectangleF r, Vector3 c)>();
        if (marks != null)
            foreach (var m in marks) boxes.Add((new RectangleF(m.X - x0 - 1, m.Y - y0 - cy0 - 1, m.Width + 2, m.Height + 2), new Vector3(0.3f, 0.95f, 1f)));
        if (th != null)
            for (int i = 0; i < 3; i++)
            {
                float a = th.CarAlpha[i] * BandA;
                if (a <= 0.004f) continue;
                bool drone = i == 2;
                var (tw, tht, _) = Theater.CarTexForTest(drone);
                var px = th.CarPixelsForTest(drone);
                float cxB = (th.CarAz[i] / 360 + 0.5f) * bw - x0, cyB = mid - MathF.Tan(th.CarEl[i] * MathF.PI / 180) / (MathF.PI / 8) * mid - y0 - cy0;
                float wB = th.CarWidth[i] / th.CarRadius[i] * 180 / MathF.PI / 360 * bw, hB = wB * tht / tw;
                for (int y = (int)MathF.Floor(cyB - hB / 2); y <= (int)MathF.Ceiling(cyB + hB / 2); y++)
                    for (int x = (int)MathF.Floor(cxB - wB / 2); x <= (int)MathF.Ceiling(cxB + wB / 2); x++)
                    {
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        float u = (x + 0.5f - (cxB - wB / 2)) / wB, v = (y + 0.5f - (cyB - hB / 2)) / hB;
                        if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                        if (th.CarFlip[i]) u = 1 - u;
                        var (cr, ca) = Sample(px, tw, tht, u * tw - 0.5f, v * tht - 0.5f);
                        img[y * w + x] = cr * a + img[y * w + x] * (1 - ca * a);
                    }
                boxes.Add((new RectangleF(cxB - wB / 2 - 6, cyB - hB / 2 - 5, wB + 12, hB + 10), new Vector3(1f, 0.85f, 0.3f)));
            }
        int ow = Math.Max(1, (int)(w * s)), oh = Math.Max(1, (int)(h * s));
        var bmp = new Bitmap(ow, oh, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var line = new int[ow];
        for (int y = 0; y < oh; y++)
        {
            for (int x = 0; x < ow; x++)
            {
                Vector3 c;
                if (s >= 1) c = img[(int)(y / s) * w + (int)(x / s)];
                else
                {
                    // smaller: the average of the texels it covers
                    int n = (int)MathF.Round(1 / s);
                    c = Vector3.Zero;
                    for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) c += img[Math.Min(h - 1, y * n + j) * w + Math.Min(w - 1, x * n + i)];
                    c /= n * n;
                }
                // the outlines (a marked lamp: all round; a car: its corners), a picture pixel wide
                foreach (var (r, mc) in boxes)
                {
                    float l = r.Left * s, t = r.Top * s, rr = r.Right * s - 1, b = r.Bottom * s - 1;
                    bool onX = (x == (int)l || x == (int)rr) && y >= t && y <= b, onY = (y == (int)t || y == (int)b) && x >= l && x <= rr;
                    if (!onX && !onY) continue;
                    if (mc.X > 0.9f)
                    {
                        float arm = MathF.Min(6, (rr - l) / 3);
                        if (onY && x > l + arm && x < rr - arm) continue;
                        if (onX && y > t + arm && y < b - arm) continue;
                    }
                    c = c * 0.3f + mc * 0.7f;
                }
                c = Vector3.Clamp(c, Vector3.Zero, Vector3.One) * 255;
                line[x] = unchecked((int)0xFF000000) | (int)(c.X + 0.5f) << 16 | (int)(c.Y + 0.5f) << 8 | (int)(c.Z + 0.5f);
            }
            System.Runtime.InteropServices.Marshal.Copy(line, 0, bd.Scan0 + y * bd.Stride, ow);
        }
        bmp.UnlockBits(bd);
        return bmp;
    }

    static Vector3 Rgb3(uint p) => new Vector3((p >> 16) & 255, (p >> 8) & 255, p & 255) / 255f;

    // a premultiplied texel between texel centres (clamped at the edge): its colour and alpha
    static (Vector3 c, float a) Sample(uint[] px, int w, int h, float fx, float fy)
    {
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float tx = fx - x0, ty = fy - y0;
        Vector3 c = Vector3.Zero;
        float a = 0;
        for (int k = 0; k < 4; k++)
        {
            int x = Math.Clamp(x0 + (k & 1), 0, w - 1), y = Math.Clamp(y0 + (k >> 1), 0, h - 1);
            float wt = ((k & 1) == 1 ? tx : 1 - tx) * ((k >> 1) == 1 ? ty : 1 - ty);
            uint p = px[y * w + x];
            c += Rgb3(p) * wt;
            a += (p >> 24) / 255f * wt;
        }
        return (c, a);
    }

    // pictures in a grid of cols (1: one under another), each with its label, a title over all
    static void CityStack(List<(string label, Bitmap bmp)> shots, string path, string title, int cols = 1)
    {
        const int Gap = 10, Label = 22, Title = 30;
        int n = shots.Count, nrows = (n + cols - 1) / cols;
        var colW = new int[cols];
        var rowH = new int[nrows];
        for (int i = 0; i < n; i++)
        {
            colW[i % cols] = Math.Max(colW[i % cols], shots[i].bmp.Width);
            rowH[i / cols] = Math.Max(rowH[i / cols], shots[i].bmp.Height + Label);
        }
        int w = Gap, h = Title + Gap;
        foreach (int c in colW) w += c + Gap;
        foreach (int r in rowH) h += r + Gap;
        using var sheet = new Bitmap(Math.Max(w, 640), h);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 24, 22, 30));
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using var big = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(title, big, Brushes.White, Gap, 8);
            int y = Title + Gap;
            for (int r = 0; r < nrows; r++)
            {
                int x = Gap;
                for (int c = 0; c < cols && r * cols + c < n; c++)
                {
                    var (label, b) = shots[r * cols + c];
                    g.DrawString(label, font, Brushes.Gainsboro, x, y + 3);
                    g.DrawImageUnscaled(b, x, y + Label);
                    b.Dispose();
                    x += colW[c] + Gap;
                }
                y += rowH[r] + Gap;
            }
        }
        sheet.Save(path, ImageFormat.Png);
    }
}
