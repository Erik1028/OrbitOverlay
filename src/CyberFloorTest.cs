using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Orbit;

// ORBIT_SNAP_ONLY=cyfloor --snapshot <dir>: Cyber theater's floor grid (TheaterFloor.cs; Erik 10-18: "it should not go down to infinity,
// nor be 360° round, but the dimming should stay; and it looks very chunky, as if it were low poly all round"). In <dir>/cyfloor:
//  - the textures: the wall (cyfloor_wall.png with its alpha, _black over black), the floor's three side by side (cyfloor_floor.png,
//    _black, _zoom: 3x, a stretch round the screen's edge);
//  - the headset views (cyfloor_view_n16.png, _n32, _n64, _exact): straight at the screen, 30° down, 60° to the left, behind you - before
//    (main's textures: ORBIT_CYFLOOR_BEFORE = a directory with main's cyberui_wall_000.png and cyberui_skyline.png, from its
//    ORBIT_SNAP_ONLY=cyberui) beside after. Drawn the way SteamVR draws overlays: each curved one a mesh of N flat columns across its
//    width (N = 16, 32, 64; 'exact': the true cylinder), its texture mapped along them (its bounds; its height from its texture),
//    bilinear, every overlay composited in sort order with its alpha over a made-up room, seen from the head 100° wide;
//    cyfloor_detail.png: 58° to the left, 24° wide, N = 16 against the true cylinder - where the columns' corners show;
//    cyfloor_detail_skyline.png: the skyline band (left a whole circle) where two of 16 columns meet, against the true cylinder;
//  - the checks: nothing of the floor past FloorEnd below the horizon, nothing above it, nothing outside its arc, its three pieces tiling
//    the arc on one cylinder; the wall's alpha WallAlpha at every elevation all the way round (stars excepted), smooth; the sort orders
//    distinct and back to front; the city's frame level (the floor's pieces with it) with the head and the screen tilted; at rest no
//    SteamVR call a frame; the columns: below the horizon, N = 16 against the true cylinder - before off by many levels where the rings
//    are, after by next to nothing.
static partial class Tools
{
    static int CyberFloorOnly(ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        var look0 = cfg.Data.Appearance;
        var level0 = Motion.Level;
        bool live0 = CyberFx.Live, fake0 = VrOverlay.Fake;
        int fails = 0;
        void Want(string what, bool ok) { Log.Info($"cyfloor: {what} {ok} (want True)"); if (!ok) fails++; }
        string od = Path.Combine(dir, "cyfloor");
        Directory.CreateDirectory(od);
        try
        {
            CyberFx.Live = true;
            cfg.Data.Appearance = new AppearanceConfig { Style = CyberHud.Id, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            FloorTextures(painter, gpu, od, Want, out var wall, out var floor);
            VrOverlay.Fake = true;
            FloorSorts(painter, gpu, Want);
            FloorLevel(painter, gpu, Want);
            FloorViews(painter, gpu, od, Want, wall, floor);
        }
        catch (Exception e) { Log.Error("cyfloor: " + e); fails++; }
        finally
        {
            VrOverlay.Fake = fake0;
            VrOverlay.Calls.Reset();
            CyberFx.Live = live0;
            Motion.Level = level0;
            cfg.Data.Appearance = look0;
            Theme.Apply(look0);
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        Log.Info($"cyfloor: {(fails == 0 ? "all checks True" : fails + " check(s) False")} - pictures in {od}");
        return 0;
    }

    // ---- the textures and their checks ----
    static void FloorTextures(Painter painter, Gpu gpu, string od, Action<string, bool> Want, out uint[] wall, out uint[] floor)
    {
        using var th = new Theater(gpu, painter);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ws = th.GridForTest(0);
        GpuIdle(gpu);
        double wallMs = sw.Elapsed.TotalMilliseconds;
        wall = ws.ReadPixels(gpu);
        sw.Restart();
        var pieces = th.FloorForTest();
        GpuIdle(gpu);
        double floorMs = sw.Elapsed.TotalMilliseconds;
        int W = Theater.FloorTexW * Theater.FloorPieces, H = Theater.FloorTexH, CW = Theater.CityWidth, CH = Theater.CityH;
        floor = new uint[W * H];
        bool uploaded = true;
        var direct = new uint[Theater.FloorTexW * H];
        for (int k = 0; k < Theater.FloorPieces; k++)
        {
            var px = pieces[k].ReadPixels(gpu);
            Theater.FloorPixels(k, direct);
            uploaded &= px.AsSpan().SequenceEqual(direct);
            for (int y = 0; y < H; y++) Array.Copy(px, y * Theater.FloorTexW, floor, y * W + k * Theater.FloorTexW, Theater.FloorTexW);
        }
        long wallB = (long)CW * CH * 4, floorB = (long)W * H * 4, oldB = (long)CW * (int)(CW * Theater.WallAspect) * 4;
        Log.Info($"cyfloor: the wall {CW} x {CH} drawn in {wallMs:0.0} ms (first time, with the JIT), the floor's three {Theater.FloorTexW} x {H} in {floorMs:0.0} ms; " +
                 $"texture memory: the wall {wallB / 1024} KB + the floor {floorB / 1024} KB = {(wallB + floorB) / 1024} KB, before the wall alone {oldB / 1024} KB");
        Want($"memory: the wall and the floor together {(wallB + floorB) / 1024} KB, not more than the old wall's {oldB / 1024} KB", wallB + floorB <= oldB);
        Want($"the floor's textures uploaded as worked out {uploaded}", uploaded);

        // the floor: nothing under FloorEnd, nothing over the horizon, nothing outside its arc; no colour over its alpha
        float step = Theater.FloorPieceArc / Theater.FloorTexW, top = MathF.Tan(Theater.FloorTop * MathF.PI / 180), stepR = step * MathF.PI / 180;
        int below = 0, above = 0, outside = 0, over = 0, maxA = 0;
        float deepest = 0, widest = 0;
        for (int y = 0; y < H; y++)
        {
            float t = top - (y + 0.5f) * stepR, deg = -MathF.Atan(t) * 180 / MathF.PI;          // degrees under the horizon (- over it)
            float lo = -MathF.Atan(t - stepR / 2) * 180 / MathF.PI;                              // the texel's lower edge
            for (int x = 0; x < W; x++)
            {
                uint p = floor[y * W + x];
                if (p == 0) continue;
                int a = (int)(p >> 24);
                float az = -Theater.FloorArc / 2 + (x + 0.5f) * step;
                maxA = Math.Max(maxA, a);
                deepest = MathF.Max(deepest, lo);
                widest = MathF.Max(widest, MathF.Abs(az) + step / 2);
                if (lo >= Theater.FloorEnd) below++;
                if (deg <= 0) above++;
                if (MathF.Abs(az) + step / 2 >= Theater.FloorSideTo) outside++;
                if (((p >> 16) & 0xFF) > a || ((p >> 8) & 0xFF) > a || (p & 0xFF) > a) over++;
            }
        }
        Want($"floor: nothing at or past {Theater.FloorEnd}° under the horizon ({below} texels; its deepest texel reaches {deepest:0.00}°), nothing over the horizon " +
             $"({above}), nothing past {Theater.FloorSideTo}° either side ({outside}; its widest reaches {widest:0.00}° of the arc's {Theater.FloorArc / 2}°), no colour over " +
             $"its alpha ({over}); its strongest texel {maxA}", below == 0 && above == 0 && outside == 0 && over == 0 && deepest < Theater.FloorEnd && maxA > 120);

        // the wall: every row's alpha WallAlpha (dithered: within a level of it), all the way round - only a star makes a texel darker
        int bad = 0, starTex = 0, overW = 0;
        double worstMean = 0;
        float worstAt = 0;
        for (int y = 0; y < CH; y++)
        {
            float f = MathF.Abs(CH / 2f - y - 0.5f) / CW, v = 255 * Theater.WallAlpha(f);
            double sum = 0;
            int n = 0;
            for (int x = 0; x < CW; x++)
            {
                uint p = wall[y * CW + x];
                int a = (int)(p >> 24);
                if (((p >> 16) & 0xFF) > a || ((p >> 8) & 0xFF) > a || (p & 0xFF) > a) overW++;
                if (a >= v + 1) { starTex++; continue; }         // a star (only ever darker - more alpha - than the dim)
                if (a <= v - 1) bad++;
                sum += a; n++;
            }
            double mean = n > 0 ? sum / n - v : 0;
            if (Math.Abs(mean) > Math.Abs(worstMean)) { worstMean = mean; worstAt = Theater.CityRowEl(y); }
        }
        Want($"wall: its alpha WallAlpha at every elevation, all the way round - {bad} texel(s) a level or more under it (want 0), each row's average off by " +
             $"{worstMean:+0.000;-0.000} at most (at {worstAt:0.0}°; want within 0.15: dithered, no bands), {starTex} texels a star makes darker; no colour " +
             $"over its alpha ({overW})", bad == 0 && Math.Abs(worstMean) <= 0.15 && overW == 0 && starTex > 0);
        // its top and bottom rows empty (the edge at 80°: nothing to see), the horizon as dark as the dim
        int edge = 0, horizonMin = 255;
        for (int x = 0; x < CW; x++)
        {
            edge = Math.Max(edge, (int)Math.Max(wall[x] >> 24, wall[(CH - 1) * CW + x] >> 24));
            horizonMin = Math.Min(horizonMin, (int)(wall[(CH / 2) * CW + x] >> 24));
        }
        Want($"wall: its top and bottom rows (80°) empty (alpha {edge} at most), its horizon as dark as the dim all round ({horizonMin})", edge == 0 && horizonMin >= 254);

        // the pictures
        SaveBackdrop(wall, CW, CH, Path.Combine(od, "cyfloor_wall.png"), keepAlpha: true);
        SaveBackdrop(wall, CW, CH, Path.Combine(od, "cyfloor_wall_black.png"), keepAlpha: false);
        SaveBackdrop(floor, W, H, Path.Combine(od, "cyfloor_floor.png"), keepAlpha: true);
        SaveBackdrop(floor, W, H, Path.Combine(od, "cyfloor_floor_black.png"), keepAlpha: false);
        const int Z = 3, zx = 980, zw = 420;                 // round the screen's right edge, 3 texels a texel
        var zoom = new uint[zw * Z * H * Z];
        for (int y = 0; y < H * Z; y++) for (int x = 0; x < zw * Z; x++) zoom[y * zw * Z + x] = floor[y / Z * W + zx + x / Z];
        SaveBackdrop(zoom, zw * Z, H * Z, Path.Combine(od, "cyfloor_floor_zoom.png"), keepAlpha: false);
    }

    // ---- the sort orders: every layer of a Cyber theater with everything on (Motion Full, a picture for the glow) ----
    static void FloorSorts(Painter painter, Gpu gpu, Action<string, bool> Want)
    {
        Motion.Level = MotionLevel.Full;
        var vr = FloorVr(Matrix4x4.CreateTranslation(0, 1.6f, 0));
        var (pic, _) = AmbPicture(gpu, 480, 270, (x, y) => x < 120 ? Rgb(200, 60, 40) : x >= 360 ? Rgb(40, 90, 220) : Rgb(60, 60, 80));
        var scr = new AmbScreen { Pic = pic, Fade = 1, HomeWorld = Matrix4x4.CreateTranslation(0, 1.4f, -1.1f) };
        var live = new List<ITheaterScreen> { scr };
        var tcfg = new TheaterConfig();
        using (var th = new Theater(gpu, painter) { LifeSeed = 1018, AmbilightOn = true })
        {
            th.SoundLevel = () => 0.5f;
            th.CreateOverlay();
            for (int i = 0; i < 30; i++) th.Update(vr, 1 / 90f, tcfg.Dim, live);
            th.Start(scr, scr, vr, tcfg);
            for (int i = 0; i < 90; i++) { th.Update(vr, 1 / 90f, tcfg.Dim, live); scr.Frame++; if (i % 10 == 0) GpuIdle(gpu); }
            var layers = th.LayersForTest();
            string Group(string n) => n.StartsWith("floor") ? "floor" : n.StartsWith("spark") ? "sparks" : n.StartsWith("car") ? "cars" : n;
            string[] order = { "wall", "backdrop", "floor", "band", "ring", "sparks", "cars", "glow" };
            var sorts = new Dictionary<string, HashSet<uint>>();
            foreach (var (name, ov, _) in layers)
            {
                string g = Group(name);
                if (!sorts.TryGetValue(g, out var set)) sorts[g] = set = new HashSet<uint>();
                set.Add(ov.SortOrderForTest);
            }
            bool all = order.All(sorts.ContainsKey), one = sorts.Values.All(s => s.Count == 1), rising = true;
            uint last = 0;
            var table = new System.Text.StringBuilder();
            foreach (string g in order)
            {
                if (!sorts.TryGetValue(g, out var set)) { table.Append($" {g} -;"); continue; }
                uint s = set.Min();
                table.Append($" {g} {string.Join("/", set)};");
                if (s <= last) rising = false;
                last = s;
            }
            Want($"sort orders, back to front:{table} every layer there ({all}), one each ({one}), each over the one before ({rising}), all under the screens' 106 " +
                 $"({last < 106})", all && one && rising && last < 106);
            th.Stop(null, null);
        }
        pic.Dispose();
    }

    // ---- the city's frame level, with the floor in it, whatever way the head and the screen are tilted; no call a frame at rest ----
    static void FloorLevel(Painter painter, Gpu gpu, Action<string, bool> Want)
    {
        Motion.Level = MotionLevel.Reduced;                 // (no cars, no breathing: nothing is meant to move)
        var vr = FloorVr(Matrix4x4.CreateRotationX(0.5f) * Matrix4x4.CreateRotationZ(0.3f) * Matrix4x4.CreateTranslation(0.2f, 1.6f, 0.1f));
        var scr = new AmbScreen { HomeWorld = Matrix4x4.CreateRotationX(0.4f) * Matrix4x4.CreateRotationZ(-0.2f) * Matrix4x4.CreateTranslation(0.9f, 2.4f, -2f) };
        var live = new List<ITheaterScreen> { scr };
        var tcfg = new TheaterConfig();
        using var th = new Theater(gpu, painter);
        th.CreateOverlay();
        for (int i = 0; i < 30; i++) th.Update(vr, 1 / 90f, tcfg.Dim, live);
        th.Start(scr, scr, vr, tcfg);
        for (int i = 0; i < 180; i++) th.Update(vr, 1 / 90f, tcfg.Dim, live);
        var lv = th.LevelForTest;
        float r = th.CityRadiusForTest - 0.075f;
        float tilt = 0, place = 0, gap = 0, curve = 0;
        var ov = th.FloorOverlaysForTest;
        float edgeL = 0, edgeR = 0, lastRight = float.NaN;
        for (int k = 0; k < Theater.FloorPieces; k++)
        {
            var m = ov[k].TransformForTest;
            tilt = MathF.Max(tilt, Vector3.Distance(new Vector3(m.M21, m.M22, m.M23), Vector3.UnitY));
            place = MathF.Max(place, Vector3.Distance(m.Translation, th.FloorPlaceForTest(k, r).Translation));
            // its arc: where its middle is round the city's frame, how wide; on a cylinder round the head at r
            Matrix4x4.Invert(lv, out var inv);
            var p = Vector3.Transform(m.Translation, inv);
            float mid = MathF.Atan2(p.X, -p.Z) * 180 / MathF.PI, arc = ov[k].Curvature * 360;
            curve = MathF.Max(curve, MathF.Abs(ov[k].Width / (2 * MathF.PI * ov[k].Curvature) - r));
            float left = mid - arc / 2, right = mid + arc / 2;
            if (k == 0) edgeL = left;
            else gap = MathF.Max(gap, MathF.Abs(left - lastRight));
            lastRight = right;
            edgeR = right;
        }
        var wm = th.LayersForTest().First(l => l.name == "wall").ov.TransformForTest;
        var bm = th.LayersForTest().First(l => l.name == "band").ov.TransformForTest;
        bool level = MathF.Abs(lv.M22 - 1) < 1e-4f && MathF.Abs(wm.M22 - 1) < 1e-4f && MathF.Abs(bm.M22 - 1) < 1e-4f;
        Want($"level: head and screen tilted, the city's frame, the wall and the band level ({level}), the floor's pieces level (off by {tilt:0.000000}), " +
             $"where they belong (off by {place:0.000000} m), on one cylinder round the head at the floor's radius (off by {curve:0.0000} m), side by side with no " +
             $"gap ({gap:0.0000}°), from {edgeL:0.000}° to {edgeR:0.000}° (want ±{Theater.FloorArc / 2})", level && tilt < 1e-4f && place < 1e-4f && curve < 1e-3f &&
             gap < 1e-3f && MathF.Abs(edgeL + Theater.FloorArc / 2) < 1e-3f && MathF.Abs(edgeR - Theater.FloorArc / 2) < 1e-3f);
        // at rest: not one SteamVR call a frame; the floor shown with the wall at its alpha
        int sent0 = VrOverlay.Calls.SentTotal;
        for (int i = 0; i < 180; i++) th.Update(vr, 1 / 90f, tcfg.Dim, live);
        int rest = VrOverlay.Calls.SentTotal - sent0;
        var wallOv = th.LayersForTest().First(l => l.name == "wall").ov;
        bool shown = ov.All(o => o.Visible && o.Alpha == Math.Min(1, wallOv.Alpha * 1.3f));
        Want($"at rest (Motion reduced, 2 s): {rest} SteamVR call(s) (want 0); the floor shown at its alpha {ov[0].Alpha:0.000}, the wall's {wallOv.Alpha:0.000} x 1.3 ({shown})", rest == 0 && shown);
        // going: it goes with the wall
        th.Stop(vr, tcfg);
        for (int i = 0; i < 90; i++) th.Update(vr, 1 / 90f, tcfg.Dim, live);
        Want($"theater off: the floor hidden with the wall ({!ov.Any(o => o.Visible)}, the wall {(wallOv.Visible ? "shown" : "hidden")})", !ov.Any(o => o.Visible) && !wallOv.Visible);
        // another style: the floor's textures let go (only the style in use has textures), back in Cyber they come again
        Theme.Apply(new AppearanceConfig { Style = "modern" });
        for (int i = 0; i < 3; i++) th.Update(vr, 1 / 90f, tcfg.Dim, live);
        bool gone = !th.FloorHeldForTest;
        Theme.Apply(new AppearanceConfig { Style = CyberHud.Id });
        for (int i = 0; i < 30; i++) th.Update(vr, 1 / 90f, tcfg.Dim, live);
        Want($"the style changed with theater off: the floor's textures let go ({gone}), made ready again back in Cyber ({th.FloorHeldForTest})", gone && th.FloorHeldForTest);
        th.Start(scr, scr, vr, tcfg);
        for (int i = 0; i < 90; i++) th.Update(vr, 1 / 90f, tcfg.Dim, live);
        bool again = ov.All(o => o.Visible) && th.FloorOverlaysForTest.All(o => th.LayersForTest().First(l => l.ov == o).tex != null);
        Want($"and shown with their new textures in the next theater ({again})", again);
        th.Stop(null, null);
        th.DestroyOverlay();
    }

    // the floor's three as one picture, sampled at az / el (degrees, round the city's frame): premultiplied, at the wall's alpha 'dim'
    static Func<float, float, (float r, float g, float b, float a)> FloorSampler(Theater th, Gpu gpu, float dim)
    {
        int tw = Theater.FloorTexW, W = tw * Theater.FloorPieces, H = Theater.FloorTexH;
        var px = new uint[W * H];
        var pieces = th.FloorForTest();
        for (int k = 0; k < pieces.Length; k++)
        {
            var p = pieces[k].ReadPixels(gpu);
            for (int y = 0; y < H; y++) Array.Copy(p, y * tw, px, y * W + k * tw, tw);
        }
        float step = Theater.FloorPieceArc / tw, stepR = step * MathF.PI / 180, top = MathF.Tan(Theater.FloorTop * MathF.PI / 180);
        return (az, el) =>
        {
            var q = PSample(px, W, H, (az + Theater.FloorArc / 2) / step - 0.5f, (top - MathF.Tan(el * MathF.PI / 180)) / stepR - 0.5f, false);
            return (q.r * dim, q.g * dim, q.b * dim, q.a * dim);
        };
    }

    static VrSession FloorVr(Matrix4x4 hmd)
    {
        var vr = new VrSession();
        vr.Device[0] = hmd;
        vr.DeviceValid[0] = true;
        return vr;
    }

    // ================= the headset views =================

    // an overlay as SteamVR was told it: its texture (premultiplied BGRA), the part of it shown, its place, width, bend and alpha
    sealed class RLayer
    {
        public string Name;
        public uint Sort;
        public uint[] Px;
        public int TW, TH;
        public float U0, V0, U1 = 1, V1 = 1;
        public Matrix4x4 World, Inv;
        public float Width, Curv, Alpha = 1;
        public bool Screen;
        public float R, Arc, Half, H;          // its cylinder's radius, its arc (radians), half of it; its height

        public RLayer Prepare()
        {
            Matrix4x4.Invert(World, out Inv);
            H = Width * (TH * (V1 - V0)) / (TW * (U1 - U0));           // SteamVR: as tall as the part of its texture it shows
            if (Curv > 0) { R = Width / (2 * MathF.PI * Curv); Arc = 2 * MathF.PI * Curv; Half = Arc / 2; }
            return this;
        }

        public RLayer With(uint[] px, int w, int h) { var l = (RLayer)MemberwiseClone(); l.Px = px; l.TW = w; l.TH = h; return l.Prepare(); }
    }

    // Where a ray (o + t d, in the overlay's own frame) meets the overlay as SteamVR draws it: curved, n flat columns across its width
    // (0: the true cylinder) - each column the flat strip between two points of the arc, the texture's u along it, v down it. SteamVR's
    // curved overlay: its middle at its origin, the arc bending towards +z round an axis R in front of it (R = width / 2π curvature).
    static bool RHit(RLayer L, Vector3 o, Vector3 d, int n, out float u, out float v)
    {
        u = v = 0;
        if (L.Curv <= 0)
        {
            if (MathF.Abs(d.Z) < 1e-9f) return false;
            float tf = -o.Z / d.Z;
            if (tf <= 0) return false;
            var p = o + tf * d;
            u = p.X / L.Width + 0.5f; v = 0.5f - p.Y / L.H;
            return u >= 0 && u <= 1 && v >= 0 && v <= 1;
        }
        float R = L.R, oz = o.Z - R;
        float a = d.X * d.X + d.Z * d.Z, b = 2 * (o.X * d.X + oz * d.Z), c = o.X * o.X + oz * oz - R * R, disc = b * b - 4 * a * c;
        if (a < 1e-12f || disc < 0) return false;
        float sq = MathF.Sqrt(disc);
        for (int pass = 0; pass < 2; pass++)
        {
            float t = (-b + (pass == 0 ? -sq : sq)) / (2 * a);
            if (t <= 1e-5f) continue;
            var p = o + t * d;
            float th = MathF.Atan2(p.X, R - p.Z);
            if (MathF.Abs(th) > L.Half) continue;
            if (n <= 0)
            {
                u = (th + L.Half) / L.Arc; v = 0.5f - p.Y / L.H;
                if (v >= 0 && v <= 1) return true;
                continue;
            }
            int k = Math.Clamp((int)MathF.Floor((th + L.Half) / L.Arc * n), 0, n - 1);
            for (int tries = 0; tries < 3; tries++)
            {
                float t0 = -L.Half + k * L.Arc / n, t1 = t0 + L.Arc / n;
                float ax = R * MathF.Sin(t0), az = R - R * MathF.Cos(t0), ex = R * MathF.Sin(t1) - ax, ez = R - R * MathF.Cos(t1) - az;
                float det = -d.X * ez + ex * d.Z;
                if (MathF.Abs(det) < 1e-12f) break;
                float tt = (-(ax - o.X) * ez + ex * (az - o.Z)) / det, s = (d.X * (az - o.Z) - d.Z * (ax - o.X)) / det;
                if (s < 0) { if (k == 0) break; k--; continue; }
                if (s > 1) { if (k == n - 1) break; k++; continue; }
                if (tt <= 1e-5f) break;
                u = (k + s) / n; v = 0.5f - (o.Y + tt * d.Y) / L.H;
                if (v >= 0 && v <= 1) return true;
                break;
            }
        }
        return false;
    }

    // a premultiplied texel between texel centres, clamped at the texture's edges (0..1)
    static Vector4 RSample(RLayer L, float u, float v)
    {
        float fx = (L.U0 + u * (L.U1 - L.U0)) * L.TW - 0.5f, fy = (L.V0 + v * (L.V1 - L.V0)) * L.TH - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float tx = fx - x0, ty = fy - y0;
        var s = Vector4.Zero;
        for (int q = 0; q < 4; q++)
        {
            int x = Math.Clamp(x0 + (q & 1), 0, L.TW - 1), y = Math.Clamp(y0 + (q >> 1), 0, L.TH - 1);
            float w = ((q & 1) == 1 ? tx : 1 - tx) * ((q >> 1) == 1 ? ty : 1 - ty);
            uint p = L.Px[y * L.TW + x];
            s += w * new Vector4((p >> 16) & 0xFF, (p >> 8) & 0xFF, p & 0xFF, p >> 24);
        }
        return s / 255;
    }

    // what the headset shows from 'eye' looking 'yaw' left / 'pitch' up (degrees) in the city's level frame, 'fov' wide, n columns a curved
    // overlay; mask: where the screen is (left out of the comparisons)
    static Vector3[] RView(List<RLayer> layers, Matrix4x4 level, float yaw, float pitch, float fov, int size, int n, bool[] mask)
    {
        var img = new Vector3[size * size];
        var eye = level.Translation;
        var cam = Matrix4x4.CreateRotationX(pitch * MathF.PI / 180) * Matrix4x4.CreateRotationY(yaw * MathF.PI / 180);
        var rot = level; rot.Translation = Vector3.Zero;
        float tf = MathF.Tan(fov / 2 * MathF.PI / 180);
        var sorted = layers.OrderBy(l => l.Sort).ThenByDescending(l => Vector3.Distance(l.World.Translation, eye)).ToArray();
        Parallel.For(0, size, j =>
        {
            for (int i = 0; i < size; i++)
            {
                var dc = Vector3.Normalize(new Vector3(tf * (2 * (i + 0.5f) / size - 1), -tf * (2 * (j + 0.5f) / size - 1), -1));
                var dl = Vector3.TransformNormal(dc, cam);                 // in the level frame
                var dw = Vector3.TransformNormal(dl, rot);
                float az = MathF.Atan2(dl.X, -dl.Z) * 180 / MathF.PI, el = MathF.Asin(Math.Clamp(dl.Y, -1, 1)) * 180 / MathF.PI;
                var (r, g, b) = FakeRoom(az, el);
                var c = new Vector3(r, g, b);
                foreach (var L in sorted)
                {
                    var o = Vector3.Transform(eye, L.Inv);
                    var d = Vector3.TransformNormal(dw, L.Inv);
                    if (!RHit(L, o, d, n, out float u, out float v)) continue;
                    var s = RSample(L, u, v) * L.Alpha;
                    c = new Vector3(s.X, s.Y, s.Z) + c * (1 - s.W);
                    if (L.Screen && mask != null) mask[j * size + i] = true;
                }
                img[j * size + i] = c;
            }
        });
        return img;
    }

    // the elevation (level frame) of a view's pixel
    static float RViewEl(float pitch, float fov, int size, int i, int j)
    {
        float tf = MathF.Tan(fov / 2 * MathF.PI / 180);
        var dc = Vector3.Normalize(new Vector3(tf * (2 * (i + 0.5f) / size - 1), -tf * (2 * (j + 0.5f) / size - 1), -1));
        var dl = Vector3.TransformNormal(dc, Matrix4x4.CreateRotationX(pitch * MathF.PI / 180));
        return MathF.Asin(Math.Clamp(dl.Y, -1, 1)) * 180 / MathF.PI;
    }

    static unsafe Bitmap RBitmap(Vector3[] img, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppRgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, bmp.PixelFormat);
        for (int y = 0; y < size; y++)
        {
            uint* dst = (uint*)((byte*)bd.Scan0 + y * bd.Stride);
            for (int x = 0; x < size; x++) { var c = img[y * size + x]; dst[x] = 0xFF000000 | Byte(c.X) << 16 | Byte(c.Y) << 8 | Byte(c.Z); }
        }
        bmp.UnlockBits(bd);
        return bmp;
    }

    // pictures in a grid with a label over each, a title over all
    static void RSheet(string title, List<(string label, Bitmap bmp)> cells, int cols, string path)
    {
        const int Gap = 10, Label = 24, Title = 34;
        int cw = cells[0].bmp.Width, ch = cells[0].bmp.Height, rows = (cells.Count + cols - 1) / cols;
        using var sheet = new Bitmap(cols * (cw + Gap) + Gap, Title + rows * (ch + Label + Gap) + Gap, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(24, 22, 30));
            using var tfont = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
            using var font = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(title, tfont, Brushes.White, Gap, 7);
            for (int k = 0; k < cells.Count; k++)
            {
                int x = Gap + k % cols * (cw + Gap), y = Title + k / cols * (ch + Label + Gap);
                g.DrawString(cells[k].label, font, Brushes.White, x, y + 3);
                g.DrawImage(cells[k].bmp, x, y + Label, cw, ch);
            }
        }
        sheet.Save(path, ImageFormat.Png);
    }

    // a PNG with its alpha (straight, as SaveBackdrop / SaveCrop write them) as premultiplied BGRA
    static unsafe uint[] LoadPremultiplied(string path, out int w, out int h)
    {
        using var bmp = new Bitmap(path);
        w = bmp.Width; h = bmp.Height;
        var px = new uint[w * h];
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < h; y++)
        {
            uint* src = (uint*)((byte*)bd.Scan0 + y * bd.Stride);
            for (int x = 0; x < w; x++) px[y * w + x] = src[x];
        }
        bmp.UnlockBits(bd);
        return px;
    }

    static void FloorViews(Painter painter, Gpu gpu, string od, Action<string, bool> Want, uint[] wallPx, uint[] floorAll)
    {
        // a Cyber theater at rest in VR (a fake SteamVR): what every overlay was told - the after scene
        Motion.Level = MotionLevel.Reduced;                 // (the ring at its calm brightness, no cars: a still picture)
        var vr = FloorVr(Matrix4x4.CreateTranslation(0, 1.6f, 0));
        var (pic, picPx) = AmbPicture(gpu, 480, 270, (x, y) => x < 90 ? Rgb(190, 70, 50) : x >= 390 ? Rgb(50, 90, 210) :
            Rgb(70 + 60 * x / 480, 70 + 40 * y / 270, 90));
        var scr = new AmbScreen { Pic = pic, Fade = 1, HomeWorld = Matrix4x4.CreateTranslation(0, 1.4f, -1.1f), Size = new Vector2(480, 270) };
        var live = new List<ITheaterScreen> { scr };
        var tcfg = new TheaterConfig();
        var after = new List<RLayer>();
        Matrix4x4 level;
        using (var th = new Theater(gpu, painter) { AmbilightOn = true })
        {
            th.CreateOverlay();
            for (int i = 0; i < 30; i++) th.Update(vr, 1 / 90f, tcfg.Dim, live);
            th.Start(scr, scr, vr, tcfg);
            for (int i = 0; i < 300; i++) { th.Update(vr, 1 / 90f, tcfg.Dim, live); scr.Frame++; if (i % 10 == 0) GpuIdle(gpu); }
            level = th.LevelForTest;
            foreach (var (name, ov, tex) in th.LayersForTest())
            {
                if (!ov.Visible || tex == null) continue;
                var b = ov.BoundsForTest;
                after.Add(new RLayer
                {
                    Name = name, Sort = ov.SortOrderForTest, Px = tex.ReadPixels(gpu), TW = tex.W, TH = tex.H, U0 = b.u0, V0 = b.v0, U1 = b.u1, V1 = b.v1,
                    World = ov.TransformForTest, Width = ov.Width, Curv = ov.Curvature, Alpha = ov.Alpha,
                }.Prepare());
            }
            var v = th.View;
            after.Add(new RLayer { Name = "screen", Sort = 107, Px = picPx, TW = 480, TH = 270, World = v.World, Width = v.Width, Curv = v.Curvature, Alpha = v.Opacity, Screen = true }.Prepare());
            th.Stop(null, null);
            th.DestroyOverlay();
        }
        pic.Dispose();
        Log.Info("cyfloor: the after scene - " + string.Join(", ", after.Select(l => $"{l.Name} (sort {l.Sort}, {l.TW}x{l.TH}, {l.Width:0.00} m, bend {l.Curv:0.000}, alpha {l.Alpha:0.00})")));

        // before: main's - the same places (the wall, the band, the ring as main put them), main's wall and band textures, no floor overlays
        List<RLayer> before = null;
        string bdir = Environment.GetEnvironmentVariable("ORBIT_CYFLOOR_BEFORE");
        if (!string.IsNullOrEmpty(bdir) && File.Exists(Path.Combine(bdir, "cyberui_wall_000.png")) && File.Exists(Path.Combine(bdir, "cyberui_skyline.png")))
        {
            var oldWall = LoadPremultiplied(Path.Combine(bdir, "cyberui_wall_000.png"), out int ow, out int oh);
            var oldBand = LoadPremultiplied(Path.Combine(bdir, "cyberui_skyline.png"), out int bw, out int bh);
            before = new List<RLayer>();
            foreach (var l in after)
            {
                if (l.Name.StartsWith("floor")) continue;
                before.Add(l.Name == "wall" ? l.With(oldWall, ow, oh) : l.Name == "band" ? l.With(oldBand, bw, bh) : l);
            }
            // (main's sort orders: the wall 1, the band and the ring 2, the glow 3, the screens 106+ - the same order as drawn here)
        }
        else Log.Warn("cyfloor: no before textures (ORBIT_CYFLOOR_BEFORE: a directory with main's cyberui_wall_000.png and cyberui_skyline.png) - after only");

        var views = new (string name, float yaw, float pitch)[] { ("straight at the screen", 0, 0), ("30° down", 0, -30), ("60° to the left", 60, 0), ("behind you", 180, 0) };
        const int Size = 900;
        const float Fov = 100;
        var counts = new[] { 16, 32, 64, 0 };
        var imgs = new Dictionary<(string scene, int n, int view), Vector3[]>();
        var masks = new Dictionary<(string scene, int view), bool[]>();
        foreach (var (scene, layers) in new[] { ("before", before), ("after", after) })
        {
            if (layers == null) continue;
            for (int vi = 0; vi < views.Length; vi++)
                foreach (int n in counts)
                {
                    bool[] mask = n == 0 ? new bool[Size * Size] : null;
                    imgs[(scene, n, vi)] = RView(layers, level, views[vi].yaw, views[vi].pitch, Fov, Size, n, mask);
                    if (mask != null) masks[(scene, vi)] = mask;
                }
        }
        foreach (int n in counts)
        {
            var cells = new List<(string, Bitmap)>();
            for (int vi = 0; vi < views.Length; vi++)
            {
                if (before != null) cells.Add(($"BEFORE (main) - {views[vi].name}", RBitmap(imgs[("before", n, vi)], Size)));
                cells.Add(($"AFTER - {views[vi].name}", RBitmap(imgs[("after", n, vi)], Size)));
            }
            string what = n > 0 ? $"every curved overlay drawn as {n} flat columns (SteamVR's mesh)" : "every curved overlay a true cylinder";
            RSheet($"Cyber theater, the headset's view ({Fov:0}° wide, from the head, dim {tcfg.Dim}) - {what}", cells, before != null ? 2 : 1,
                Path.Combine(od, $"cyfloor_view_{(n > 0 ? "n" + n : "exact")}.png"));
            foreach (var c in cells) c.Item2.Dispose();
        }

        // the columns, measured: N columns against the true cylinder, the screen left out - below the horizon (3° to 16° down: the floor,
        // the wall's lower half), and for the record what stays on whole-circle overlays: the skyline (1° to 12° up), the stars (20° to 55°)
        (float max, int over, int all) Off(string scene, int n, float lo, float hi)
        {
            float max = 0;
            int over = 0, all = 0;
            for (int vi = 0; vi < views.Length; vi++)
            {
                var a = imgs[(scene, n, vi)];
                var e = imgs[(scene, 0, vi)];
                var mask = masks[(scene, vi)];
                for (int j = 0; j < Size; j++)
                    for (int i = 0; i < Size; i++)
                    {
                        float el = RViewEl(views[vi].pitch, Fov, Size, i, j);
                        if (el > hi || el < lo || mask[j * Size + i]) continue;
                        var d = Vector3.Abs(a[j * Size + i] - e[j * Size + i]) * 255;
                        float m = MathF.Max(d.X, MathF.Max(d.Y, d.Z));
                        all++;
                        max = MathF.Max(max, m);
                        if (m > 8) over++;
                    }
            }
            return (max, over, all);
        }
        foreach (var (scene, layers) in new[] { ("before", before), ("after", after) })
        {
            if (layers == null) continue;
            foreach (var (what, lo, hi) in new[] { ("below the horizon (3-16° down)", -16f, -3f), ("the skyline band (1-12° up)", 1f, 12f), ("the stars (20-55° up)", 20f, 55f) })
            {
                var line = new System.Text.StringBuilder();
                foreach (int n in new[] { 16, 32, 64 })
                {
                    var (max, over, all) = Off(scene, n, lo, hi);
                    line.Append($" N {n}: off by {max:0} levels at most, {100.0 * over / Math.Max(1, all):0.000} % of the pixels by more than 8;");
                    if (n != 16 || lo > 0) continue;
                    if (scene == "before") Want($"the columns, before (main), 3-16° under the horizon, against the true cylinder - the wall's rings show their corners: off by " +
                        $"{max:0} levels (want 30 or more), {100.0 * over / Math.Max(1, all):0.00} % of the pixels by more than 8", max >= 30);
                    else Want($"the columns, after, 3-16° under the horizon, against the true cylinder at N = 16 - nothing faceted: off by {max:0} levels at most (want 4 " +
                        $"or less), {over} pixel(s) by more than 8 (want 0)", max <= 4 && over == 0);
                }
                Log.Info($"cyfloor: {scene}, {what}, every curved overlay N columns against the true cylinder:{line}");
            }
        }

        // close up: 60° to the left, 24° wide, a little down - N = 16 against the true cylinder
        var detail = new List<(string, Bitmap)>();
        foreach (var (scene, layers) in new[] { ("BEFORE (main)", before), ("AFTER", after) })
        {
            if (layers == null) continue;
            detail.Add(($"{scene} - 16 columns (SteamVR)", RBitmap(RView(layers, level, 58, -7, 24, Size, 16, null), Size)));
            detail.Add(($"{scene} - the true cylinder", RBitmap(RView(layers, level, 58, -7, 24, Size, 0, null), Size)));
        }
        RSheet("Close up: 58° to the left, 7° down, 24° wide (0.027° a pixel) - SteamVR's 16 columns against the true cylinder", detail, 2, Path.Combine(od, "cyfloor_detail.png"));
        foreach (var c in detail) c.Item2.Dispose();

        // the skyline (still a whole circle: its detail is at eye level) where two of its columns meet at N = 16 (45° to the right), its
        // tallest towers 7-11° up: the same in both scenes - how much the columns bend it
        var sky = new List<(string, Bitmap)>
        {
            ("the skyline band - 16 columns (SteamVR)", RBitmap(RView(after, level, -45, 6, 16, Size, 16, null), Size)),
            ("the skyline band - the true cylinder", RBitmap(RView(after, level, -45, 6, 16, Size, 0, null), Size)),
        };
        RSheet("Close up: the skyline 45° to the right (two of SteamVR's 16 columns meet there), 6° up, 16° wide (0.018° a pixel)", sky, 2,
            Path.Combine(od, "cyfloor_detail_skyline.png"));
        foreach (var c in sky) c.Item2.Dispose();
    }
}
