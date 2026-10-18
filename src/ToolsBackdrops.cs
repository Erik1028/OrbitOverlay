using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;

namespace Orbit;

// --snapshot, theater mode's backdrops (Panels/TheaterBackdrop*.cs) in every style, Cyber's night city beside them for comparison
// (ORBIT_SNAP_ONLY=backdrops: this alone, in seconds). Into <dir>/backdrops:
//  - backdrop_<style>.png: the whole texture as SteamVR gets it (all the way round, 81° up and down; premultiplied, saved with its
//    alpha) and backdrop_<style>_black.png the same over black;
//  - backdrop_<style>_view.png: what you see round the screen - a made-up lit room, the black wall over it at the default Dim, the
//    backdrop at the same alpha over that (Cyber: its city and its skyline band), a grey screen 2.8 m wide 2.5 m away in the middle;
//    backdrop_plain_view.png the plain dim as it was, for before / after;
//  - backdrops_preview.png: every view side by side.
// And the checks:
//  - the style's backdrop: each its own (Cyber: none - its city);
//  - drawn on its own thread and uploaded: the texture read back is the picture (and how long it took);
//  - the horizon round the screen at least as dark as the plain dim was, and darker there than up high (the vignette);
//  - nothing past the fade: every texel past 79.5° up or down clear (the cylinder's edge is at 81°);
//  - its alpha smooth: no step between two neighbouring texels (the seam behind you too) over 2 levels - dithered, no bands;
//  - premultiplied: no colour over its alpha;
//  - one texture at a time: the city's let go when a backdrop is made, the backdrop's when Cyber's city is on - and in VR (a fake
//    SteamVR) with the style changed while theater is off, the wall's overlay never keeps a texture that is gone (TheaterWarm.cs);
//  - the style back to the one before while the new picture is half way into the texture: the old one drawn again, never half and half;
//  - the motion: Terminal's slow warm-up only with Motion Full; the picture comes up after the dim (Full).
static partial class Tools
{
    static int BackdropsOnly(ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        BackdropsRun(cfg, painter, gpu, dir);
        return 0;
    }

    const float ScreenAz = 32.1f, ScreenEl = 17.5f;         // a 2.8 m wide 16:9 screen 2.5 m away, curved round you
    const float ViewAz = 100, ViewEl = 55;                    // the view: this far either side and up / down

    static void BackdropsRun(ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        string bd = Path.Combine(dir, "backdrops");
        Directory.CreateDirectory(bd);
        float dim = new TheaterConfig().Dim;
        var styles = new (string id, string name, Theater.Backdrop want)[]
        {
            ("modern", "Modern: a dark cinema hall", Theater.Backdrop.Hall),
            ("classic", "Windows 95: Starfield", Theater.Backdrop.Starfield),
            ("xp", "Windows XP: a Luna night over the hills", Theater.Backdrop.Hills),
            ("aero", "Windows 7: glassy light ribbons", Theater.Backdrop.Aurora),
            ("mac", "Classic Mac: a calm dark desktop", Theater.Backdrop.Platinum),
            ("terminal", "Terminal: green phosphor", Theater.Backdrop.Phosphor),
            (CyberHud.Id, "Cyber: the night city (as it is)", Theater.Backdrop.None),
        };
        var views = new List<(string label, string file)>();
        int W = Theater.BackW, H = Theater.BackH;
        try
        {
            // before: the plain dim as it was (the black wall at the default Dim, nothing in front of it)
            string plain = Path.Combine(bd, "backdrop_plain_view.png");
            SaveOpaque(BackdropView(null, null, dim, out _), plain);
            views.Add(("before: the plain dim (the wall alone)", plain));
            using var th = new Theater(gpu, painter);
            foreach (var s in styles)
            {
                Theme.Apply(new AppearanceConfig { Style = s.id });
                var k = Theater.BackdropFor();
                Log.Info($"backdrops {s.id}: the style's backdrop {k} {k == s.want} (want True)");
                uint[] px;
                Func<float, float, (float r, float g, float b, float a)> band = null;
                if (k == Theater.Backdrop.None)
                {
                    // Cyber: its city on the wall and its skyline band 0.15 m nearer (its alpha a little over the dim's), as it is
                    px = th.GridForTest(0).ReadPixels(gpu);
                    var sky = th.SkylineForTest();
                    var spx = sky.ReadPixels(gpu);
                    int sw = sky.W, sh = sky.H;
                    float ba = Math.Min(1, dim * 1.3f);
                    band = (az, el) =>
                    {
                        float row = MathF.Tan(el * MathF.PI / 180) / (MathF.PI / 8);       // TheaterCyber.BandRow
                        int by = (int)MathF.Floor(sh / 2f - row * sh / 2f);
                        if (by < 0 || by >= sh) return (0, 0, 0, 0);
                        // the band has 4 texels to the wall's one: their average
                        int bx = (int)((az / 360 + 0.5f) * sw);
                        float r = 0, g = 0, b = 0, a = 0;
                        for (int i = -2; i < 2; i++)
                        {
                            uint p = spx[by * sw + ((bx + i) % sw + sw) % sw];
                            a += p >> 24; r += (p >> 16) & 0xFF; g += (p >> 8) & 0xFF; b += p & 0xFF;
                        }
                        float k = ba / (4 * 255f);
                        return (r * k, g * k, b * k, a * k);
                    };
                    int horizonA = 255;
                    for (int x = 0; x < W; x++) horizonA = Math.Min(horizonA, (int)(px[H / 2 * W + x] >> 24));
                    Log.Info($"backdrops cyber: the city's horizon as dark as the dim {horizonA >= 254} (want True)");
                }
                else
                {
                    var surf = th.BackdropForTest(k);
                    px = surf?.ReadPixels(gpu);
                    var direct = Theater.PaintBackdrop(k);
                    bool same = px != null && px.AsSpan().SequenceEqual(direct);
                    Log.Info($"backdrops {s.id}: drawn on its own thread in {th.BackdropMs} ms and uploaded - the texture read back is the picture {same} (want True)");
                    px ??= direct;
                    BackdropChecks(s.id, px, dim);
                }
                SaveBackdrop(px, W, H, Path.Combine(bd, $"backdrop_{s.id}.png"), keepAlpha: true);
                SaveBackdrop(px, W, H, Path.Combine(bd, $"backdrop_{s.id}_black.png"), keepAlpha: false);
                string view = Path.Combine(bd, $"backdrop_{s.id}_view.png");
                var v = BackdropView(px, band, dim, out var share);
                SaveOpaque(v, view);
                views.Add((s.name, view));
                Log.Info($"backdrops {s.id}: the room shows through {share.beside:P0} beside the screen, {share.behind:P0} behind you, {share.up:P0} " +
                         $"65° up (the plain dim: {1 - dim:P0}) - round the screen at least as dark as the plain dim {share.beside <= 1 - dim + 0.005f} " +
                         $"(want True), darker there than up high {share.beside < share.up} (want True)");
            }

            // one texture at a time: the city (made just now, in Cyber) goes when a backdrop is made; the backdrop goes in Cyber
            bool city0 = th.CityHeldForTest;
            Theme.Apply(new AppearanceConfig { Style = "modern" });
            th.BackdropForTest(Theater.Backdrop.Hall);
            bool cityGone = !th.CityHeldForTest, backHeld = th.BackdropHeldForTest;
            th.CyberForTest();
            Log.Info($"backdrops: the city held in Cyber {city0} (want True), let go when a backdrop is made {cityGone} (want True), the backdrop held " +
                     $"{backHeld} (want True) and let go in Cyber {!th.BackdropHeldForTest} (want True)");

            // a picture half way into the texture (a quarter a frame) when the style goes back to the one before: that one drawn and uploaded
            // again - never the new one's top over the old one's rest kept as its backdrop
            th.BackdropForTest(Theater.Backdrop.Hall);
            var up = System.Diagnostics.Stopwatch.StartNew();
            while (!th.BackdropUploadingForTest && up.ElapsedMilliseconds < 20000) { th.BackdropFrameForTest(Theater.Backdrop.Hills); System.Threading.Thread.Sleep(2); }
            bool halfway = th.BackdropUploadingForTest;
            var again = th.BackdropForTest(Theater.Backdrop.Hall)?.ReadPixels(gpu);
            bool whole = again != null && again.AsSpan().SequenceEqual(Theater.PaintBackdrop(Theater.Backdrop.Hall));
            th.CyberForTest();
            Log.Info($"backdrops: the XP picture a quarter into the texture {halfway} (want True), the style back to Modern then - the hall drawn and uploaded " +
                     $"again, the texture read back exactly it {whole} (want True)");

            // in VR (a fake SteamVR), the style changed with theater off: TheaterWarm.cs makes the new style's textures on idle frames and
            // lets the old style's go - the wall's overlay must not keep a texture that is gone. After a Cyber theater, a Modern one starts
            // on the black wall; after a Modern one, a Cyber one on the city.
            bool fake0 = VrOverlay.Fake;
            VrOverlay.Fake = true;
            try
            {
                var vr = new VrSession();
                vr.Device[0] = Matrix4x4.CreateTranslation(0, 1.6f, 0);
                vr.DeviceValid[0] = true;
                var scr = new TheaterTest.FakeScreen { Name = "AOC 24G4X", HomeWorld = Matrix4x4.CreateTranslation(0, 1.4f, -1.1f) };
                var live = new List<ITheaterScreen> { scr };
                var tcfg = new TheaterConfig();
                using var tv = new Theater(gpu, painter);
                tv.CreateOverlay();
                void Frames(int n) { for (int i = 0; i < n; i++) tv.Update(vr, 1 / 90f, tcfg.Dim, live); }
                Theme.Apply(new AppearanceConfig { Style = CyberHud.Id });
                Frames(10);                                                 // theater off: the city made ready
                bool cityReady0 = tv.CityHeldForTest, wallBlack0 = tv.WallIsBlackForTest;
                tv.Start(scr, scr, vr, tcfg); Frames(90);
                bool cityOn = tv.WallIsCityForTest;
                tv.Stop(vr, tcfg); Frames(90);
                Theme.Apply(new AppearanceConfig { Style = "modern" });
                Frames(2);                                                  // theater off: the city let go, the hall being drawn
                bool cityGone2 = !tv.CityHeldForTest;
                tv.Start(scr, scr, vr, tcfg); Frames(1);
                bool blackBack = tv.WallIsBlackForTest;
                tv.Stop(vr, tcfg); Frames(90);
                Theme.Apply(new AppearanceConfig { Style = CyberHud.Id });
                Frames(10);
                bool hallGone = !tv.BackdropHeldForTest, cityReady = tv.CityHeldForTest;
                tv.Start(scr, scr, vr, tcfg); Frames(10);                   // (the wall shows a few frames in: the dim eases up with the screen)
                bool cityBack = tv.WallIsCityForTest;
                tv.Stop(null, tcfg);
                tv.DestroyOverlay();
                Log.Info($"backdrops in VR, the style changed with theater off: in Cyber the city made ready before theater {cityReady0} (want True) on a black wall " +
                         $"{wallBlack0} (want True), the city on the wall while it played {cityOn} (want True); to Modern - the city let go {cityGone2} (want True), " +
                         $"the next theater on the black wall, not the texture that is gone {blackBack} (want True); back to Cyber - the hall let go {hallGone} " +
                         $"(want True), the city ready {cityReady} (want True) and on the wall as theater starts {cityBack} (want True)");
            }
            finally { VrOverlay.Fake = fake0; VrOverlay.Calls.Reset(); }

            // the motion: Terminal's phosphor warms up slowly only with Motion Full; the picture comes up after the dim (Full only)
            float tFull = Theater.BackdropLights(Theater.Backdrop.Phosphor, 0.08f, true, true);
            float tReduced = Theater.BackdropLights(Theater.Backdrop.Phosphor, 0.08f, true, false);
            float hall = Theater.BackdropLights(Theater.Backdrop.Hall, 0.15f, true, true), going = Theater.BackdropLights(Theater.Backdrop.Hall, 0.15f, false, true);
            bool steady = true;                                     // coming in it only ever gets brighter: no blink in any style
            foreach (Theater.Backdrop k in Enum.GetValues(typeof(Theater.Backdrop)))
                for (float t = 0, last = 0; t <= 1; t += 1 / 90f)
                {
                    float l = Theater.BackdropLights(k, t, true, true);
                    if (l < last - 1e-6f) steady = false;
                    last = l;
                }
            Log.Info($"backdrops motion: Terminal's tube still dark at 0.08 s with Full {tFull == 0} (want True), there at once with Reduced {tReduced == 1} " +
                     $"(want True); the hall's picture after the dim (Full, 0.15 s) {hall < 0.05f} (want True), going out with the dim {going == 1} (want True); " +
                     $"never a blink coming in {steady} (want True)");
            // a quick off / on, frame by frame (BackdropLit): started from nothing, turned off half way up (the lights must not flash up), on
            // again on the way out (they must not drop back to dark), then all the way in
            bool noJump = true, startsDark = true, allIn = true;
            foreach (var k in new[] { Theater.Backdrop.Hall, Theater.Backdrop.Phosphor })
            {
                float lit = Theater.BackdropLit(1, true, k, 1 / 90f, true, true);
                startsDark &= lit == 0;
                void Frames(float from, float to, bool on)
                {
                    for (float t = from; on ? t < to : t > to; t += on ? 1 / 90f : -1 / 90f)
                    {
                        float l = Theater.BackdropLit(lit, false, k, t, on, true);
                        if (on ? l < lit - 1e-6f : l > lit + 1e-6f) noJump = false;
                        lit = l;
                    }
                }
                Frames(1 / 90f, 0.5f, true);
                Frames(0.5f, 0.3f, false);
                Frames(0.3f, 1, true);
                lit = Theater.BackdropLit(lit, false, k, 1, true, true);
                allIn &= lit == 1;
            }
            Log.Info($"backdrops motion: a quick off / on - the lights start dark {startsDark} (want True), never jump {noJump} (want True), all up once in {allIn} (want True)");

            BackdropSheet(views, Path.Combine(dir, "backdrops_preview.png"));
            Log.Info("backdrops: written to " + bd);
        }
        catch (Exception e) { Log.Error("backdrops: " + e); }
        finally { Theme.Apply(cfg.Data.Appearance); }
    }

    // the picture's own checks (premultiplied BGRA, the wall's grid)
    static void BackdropChecks(string id, uint[] px, float dim)
    {
        int W = Theater.BackW, H = Theater.BackH;
        int past = 0, over = 0, step = 0, stepX = 0, stepY = 0;
        float nextTo = 1;
        for (int y = 0; y < H; y++)
        {
            float el = Theater.RowEl(y);
            for (int x = 0; x < W; x++)
            {
                uint p = px[y * W + x];
                int a = (int)(p >> 24);
                if (MathF.Abs(el) > 79.5f && p != 0) past++;
                if (((p >> 16) & 0xFF) > a || ((p >> 8) & 0xFF) > a || (p & 0xFF) > a) over++;
                int dx = Math.Abs(a - (int)(px[y * W + (x + 1) % W] >> 24));                 // (the seam behind you too)
                if (dx > step) { step = dx; stepX = x; stepY = y; }
                if (y + 1 < H)
                {
                    int dy = Math.Abs(a - (int)(px[(y + 1) * W + x] >> 24));
                    if (dy > step) { step = dy; stepX = x; stepY = y; }
                }
                float az = Theater.ColAz(x);
                if (MathF.Abs(el) < 5 && MathF.Abs(az) > ScreenAz && MathF.Abs(az) < 45) nextTo = MathF.Min(nextTo, a / 255f);
            }
        }
        Log.Info($"backdrops {id}: nothing past the fade - {past} texel(s) past 79.5° (want 0); no colour over its alpha - {over} (want 0); its alpha smooth: " +
                 $"the biggest step between two texels {step} level(s) at {stepX},{stepY} {step <= 2} (want True); its own dark next to the screen " +
                 $"{nextTo:0.00} {nextTo >= 0.7f} (want True)");
    }

    // a made-up lit room (warm walls with the corners shaded, a window, a door, a ceiling light, a wooden floor): just something for the
    // dim and the backdrop to show over
    static (float r, float g, float b) FakeRoom(float az, float el)
    {
        if (el > 40 && Math.Abs(az + 30) < 6 && el < 47) return (1f, 0.95f, 0.84f);                 // the light
        if (el < -24)
        {
            float plank = (int)MathF.Floor((az + 400) / 7) % 2 == 0 ? 1 : 0.9f;
            return (0.34f * plank, 0.24f * plank, 0.16f * plank);
        }
        if (az > 48 && az < 86 && el > -6 && el < 28)
            return MathF.Abs(az - 67) < 0.8f || MathF.Abs(el - 11) < 0.8f ? (0.85f, 0.85f, 0.82f) : (0.72f, 0.84f, 0.96f);   // the window
        if (az > -82 && az < -62 && el > -24 && el < 14) return (0.38f, 0.29f, 0.22f);           // the door
        float shade = 0.86f + 0.14f * MathF.Cos(az * 4 * MathF.PI / 180);                         // a corner every 90°
        return (0.66f * shade, 0.61f * shade, 0.54f * shade);
    }

    // what you see round the screen, at even angles (0.35° a pixel, the texture's own step at the horizon; sampled the way the compositor
    // does, bilinear - the texture itself crowds its rows towards the top): the room, the black wall at the dim over it, then the
    // backdrop (Cyber: its city) at the dim, then Cyber's band, then the grey screen. share: how much of the room shows beside the
    // screen, behind you, 65° up
    static (uint[] px, int w) BackdropView(uint[] back, Func<float, float, (float r, float g, float b, float a)> band, float dim,
        out (float beside, float behind, float up) share)
    {
        int W = Theater.BackW, H = Theater.BackH;
        float step = 360f / W;
        int vw = (int)(2 * ViewAz / step), vh = (int)(2 * ViewEl / step);
        var o = new uint[vw * vh];
        bool city = band != null;
        float Share(int x, int y)
        {
            float s = city ? 1 : 1 - dim;                          // the black wall (Cyber: the city is the wall)
            if (back != null) s *= 1 - dim * (back[y * W + x] >> 24) / 255f;
            if (band != null) s *= 1 - band(Theater.ColAz(x), Theater.RowEl(y)).a;
            return s;
        }
        for (int j = 0; j < vh; j++)
        {
            float el = ViewEl - (j + 0.5f) * step;
            float fy = H / 2f - MathF.Tan(el * MathF.PI / 180) / (Theater.WallAspect * MathF.PI) * (H / 2f) - 0.5f;
            for (int i = 0; i < vw; i++)
            {
                float az = -ViewAz + (i + 0.5f) * step;
                var (r, g, b) = FakeRoom(az, el);
                if (!city) { r *= 1 - dim; g *= 1 - dim; b *= 1 - dim; }
                if (back != null)
                {
                    var q = Bilinear(back, (az / 360 + 0.5f) * W - 0.5f, fy);
                    float a = dim * q.a;
                    r = r * (1 - a) + dim * q.r; g = g * (1 - a) + dim * q.g; b = b * (1 - a) + dim * q.b;
                }
                if (band != null)
                {
                    var q = band(az, el);
                    r = r * (1 - q.a) + q.r; g = g * (1 - q.a) + q.g; b = b * (1 - q.a) + q.b;
                }
                if (MathF.Abs(az) < ScreenAz && MathF.Abs(el) < ScreenEl) (r, g, b) = (0.42f, 0.43f, 0.45f);          // the screen
                o[j * vw + i] = 0xFF000000 | Byte(r) << 16 | Byte(g) << 8 | Byte(b);
            }
        }
        // beside the screen (level, just off its edges), right behind you, 65° up over the screen
        float beside = 0;
        int n = 0;
        for (int x = 0; x < W; x++)
        {
            float az = MathF.Abs(Theater.ColAz(x));
            if (az > ScreenAz && az < 45) { beside += Share(x, H / 2); n++; }
        }
        share = (beside / Math.Max(1, n), Share(0, H / 2), Share(W / 2, Theater.ElRow(65)));
        return (o, vw);
    }

    // a premultiplied texel of the wall's grid between texel centres (x round the circle)
    static (float r, float g, float b, float a) Bilinear(uint[] px, float fx, float fy)
    {
        int W = Theater.BackW, H = Theater.BackH;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float tx = fx - x0, ty = fy - y0;
        float r = 0, g = 0, b = 0, a = 0;
        for (int k = 0; k < 4; k++)
        {
            int x = ((x0 + (k & 1)) % W + W) % W, y = Math.Clamp(y0 + (k >> 1), 0, H - 1);
            float w = ((k & 1) == 1 ? tx : 1 - tx) * ((k >> 1) == 1 ? ty : 1 - ty);
            uint p = px[y * W + x];
            a += w * (p >> 24); r += w * ((p >> 16) & 0xFF); g += w * ((p >> 8) & 0xFF); b += w * (p & 0xFF);
        }
        return (r / 255, g / 255, b / 255, a / 255);
    }

    static uint Byte(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255), 0, 255);

    static unsafe void SaveBackdrop(uint[] px, int w, int h, string path, bool keepAlpha)
    {
        using var bmp = new Bitmap(w, h, keepAlpha ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppRgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bmp.PixelFormat);
        for (int y = 0; y < h; y++)
        {
            uint* dst = (uint*)((byte*)bd.Scan0 + y * bd.Stride);
            for (int x = 0; x < w; x++) dst[x] = keepAlpha ? px[y * w + x] : px[y * w + x] | 0xFF000000;
        }
        bmp.UnlockBits(bd);
        bmp.Save(path, ImageFormat.Png);
    }

    static void SaveOpaque((uint[] px, int w) v, string path) => SaveBackdrop(v.px, v.w, v.px.Length / v.w, path, false);

    // every view, two to a row, each with its name over it
    static void BackdropSheet(List<(string label, string file)> views, string path)
    {
        if (views.Count == 0) return;
        const int Gap = 12, Label = 26;
        Size one;
        using (var b0 = new Bitmap(views[0].file)) one = b0.Size;
        int rows = (views.Count + 1) / 2;
        using var sheet = new Bitmap(2 * one.Width + 3 * Gap, rows * (one.Height + Label + Gap) + Gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 32, 34, 40));
            using var font = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int i = 0; i < views.Count; i++)
            {
                int cx = Gap + i % 2 * (one.Width + Gap), cy = Gap + i / 2 * (one.Height + Label + Gap);
                g.DrawString(views[i].label, font, Brushes.White, cx, cy + 4);
                using var b = new Bitmap(views[i].file);
                g.DrawImage(b, cx, cy + Label, one.Width, one.Height);
            }
        }
        sheet.Save(path, ImageFormat.Png);
        Log.Info("backdrops: every style side by side: " + path);
    }
}
