using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace Orbit;

// Theater mode's backdrop in every style but Cyber (Erik 10-18: "make the background dimming nicer", and a background for every style
// like Cyber's night city). Two layers round you, both in the city's level frame (TheaterCyber.cs: level, at your head where it was
// when theater started, facing the screen - they never tilt or move with the screen, and are set again only when the screen moves):
//  - the dim (sort 1): the black wall as it was. Its 4 x 256 texture makes a cylinder 64 times as tall as it is round, so it darkens
//    everything but a pin-prick straight up and down; the Dim setting is its alpha, as before;
//  - the picture (sort 2, 0.15 m nearer - at one distance SteamVR swapped two layers now and then): the style's own backdrop. Its
//    alpha deepens the dark round the screen (a vignette: the world shows least round the film, more behind you and up high) and its
//    colours are light on that dark. It is drawn once, on a thread of its own (30-120 ms: nothing waits - the black wall is there
//    meanwhile, the picture fades in when it comes) into one buffer kept for every draw, put into the texture a quarter a frame, and
//    kept until the style changes. (Theater off, it is drawn ahead: TheaterWarm.cs.)
// The backdrops (TheaterBackdropRetro.cs for the retro ones):
//  - Modern: a dark cinema hall - the screen's light in a faint pool on the floor below it, rows of seat backs in silhouette against
//    it, a few warm step lights down the aisles;
//  - Windows 95: the Starfield screensaver's look - crisp grey and white star pixels in the 16-colour palette's greys, small and
//    dim near the screen (far away), bigger and whiter further out (flying past you);
//  - Windows XP: a Luna-blue night over soft rolling hills, a crescent moon off to the side;
//  - Windows 7: dark glassy light ribbons, aurora-like, with the bright rim of glass along them;
//  - Classic Mac: a calm dark desktop pattern, a 1-bit dithered Platinum-grey haze on the horizon, pinstripes below, pixel stars and
//    a pixel crescent moon;
//  - Terminal: green phosphor - vector mountains drawn in glowing lines on the black glass, scanlines through the glow.
// All of them procedural and our own (no wallpaper, screensaver or logo copied). The rules:
//  - the alpha is smooth (no edge in it anywhere: pictures are colour only) and dithered - an 8-bit gradient over a wall this size
//    showed bands; the colours are dithered too;
//  - premultiplied, never brighter than its alpha;
//  - nothing past 79° up or down (the cylinder's edge is at 81°: no edge to see);
//  - only the style in use has a texture: the city's go when a backdrop is made, the backdrop's when the city is.
public sealed partial class Theater
{
    public enum Backdrop { None, Hall, Starfield, Hills, Aurora, Platinum, Phosphor }     // None: Cyber's city (TheaterCyber.cs)

    // the backdrop of the style in use
    public static Backdrop BackdropFor() => Theme.Cyber ? Backdrop.None : Theme.Retro switch
    {
        RetroStyle.Win95 => Backdrop.Starfield,
        RetroStyle.XP => Backdrop.Hills,
        RetroStyle.Aero => Backdrop.Aurora,
        RetroStyle.Mac => Backdrop.Platinum,
        RetroStyle.Terminal => Backdrop.Phosphor,
        _ => Backdrop.Hall,
    };

    public const int BackW = 1024, BackH = (int)(BackW * WallAspect);     // like the city: 0.35° a texel at the horizon, all the way round
    const float FadeFrom = 50, FadeTo = 79;      // degrees up / down: all of the picture to here, gone by there (the edge is at 81°)

    VrOverlay _back;
    Surface _backTex;
    Backdrop _backKind, _backSet;     // the picture in _backTex / the one the overlay was given
    float _backIn;                    // the picture fading in once it is there (the first time it may come after theater has)
    float _backLit;                   // how far its lights are up (BackdropLit: they never jump)
    Thread _job;                      // drawing one (one at a time)
    Backdrop _jobKind, _backFailed;
    uint[] _jobBuf, _jobPx;           // the pixels: one buffer kept for every draw (8 MB: a new one each time was a gen2 GC, every thread stopped)
    long _jobMs;
    uint[] _upload;                   // the picture drawn, on its way into the texture a slice a frame (8 MB in one go was a 3 ms frame)
    int _uploadRow;
    Backdrop _uploadKind;
    const int UploadSlices = 4;
    public long BackdropMs => _jobMs;          // how long the last one took to draw (the log; --snapshot)

    void CreateBackdrop()
    {
        _back = new VrOverlay("erik.orbit.theater.backdrop", "OrbitOverlay theater backdrop");
        _back.SetSortOrder(SortBackdrop);                               // over the dim, under the glow and every screen and panel
        _back.SetFlag(Valve.VR.VROverlayFlags.IsPremultiplied, true);
        _back.SetCurvature(1f);
        _backSet = Backdrop.None;
    }

    // every style but Cyber, with the wall shown this frame (a = its alpha): the black wall and the style's picture in front of it
    void BackdropWall(float a, float behind, float dt)
    {
        var want = BackdropFor();
        BackdropTextures(want);
        // behind the screen wherever it is now and where it is going (growing out of its place it may be the farther one)
        var c = _level.Translation;
        float r = MathF.Max(Vector3.Distance(_view.Place.Translation, c), Vector3.Distance(_view.World.Translation, c)) + behind;
        if (MathF.Abs(r - _ringR) > 0.001f)
        {
            _ringR = r;
            _dim.SetAbsolute(Ring(r));
            _dim.SetWidth(2 * MathF.PI * r);        // all the way round you at that radius (curvature 1)
            _back?.SetAbsolute(Ring(r - BandIn));
            _back?.SetWidth(2 * MathF.PI * (r - BandIn));
        }
        _dim.SetAlpha(a);
        _backLit = BackdropLit(_backLit, !_dim.Visible, want, _t, On, Motion.Full);      // (the dim was off: theater starts from nothing)
        if (_back == null) return;
        if (_backTex == null || _backKind != want) { _back.Hide(); _backIn = 0; return; }     // still being drawn: the black wall alone
        if (_backSet != want) { _back.SetTexture(_backTex.Tex); _backSet = want; }       // once: it never changes
        _backIn = Motion.Step(_backIn, true, dt, 0.5f);
        float pa = a * _backIn * _backLit;
        if (pa <= 0.004f) { _back.Hide(); return; }
        _back.SetAlpha(pa);
        _back.Show();
    }

    // Cyber: its city instead - no picture, and the picture's texture let go
    void BackdropOff()
    {
        _back?.Hide();
        if (_backTex != null) FreeBackdrop();
    }

    // the textures for this style: the city's let go (only Cyber has them), the picture drawn if it is not there yet
    void BackdropTextures(Backdrop want)
    {
        FloorFree();                        // (Cyber's floor grid too: TheaterFloor.cs)
        if (_city != null || _sky != null)
        {
            _band?.Hide();
            // the wall still has the city on it (a Cyber theater played): the black wall back on it before the city's texture goes.
            // CyberWall did this, but only with theater on; with the style changed while it is off (TheaterWarm.cs lets the city go
            // here, on an idle frame) the next theater would have shown SteamVR a texture that is gone.
            if (_cityOn && _dim != null) { _dim.SetTexture(_black.Tex); _ringR = -1; }
            _city?.Dispose(); _city = null;
            _sky?.Dispose(); _sky = null;
            _cityOn = false;
        }
        if (want == Backdrop.None || want == _backFailed || (_backTex != null && _backKind == want)) return;
        if (_upload != null)
        {
            // drawn, going into the texture a slice a frame
            if (_uploadKind == want) { UploadSlice(); return; }
            _upload = null;                                                 // (the style changed meanwhile: dropped, the new one drawn below)
        }
        if (_job != null)
        {
            if (_job.IsAlive) return;
            _job = null;
            var px = Interlocked.Exchange(ref _jobPx, null);
            if (px == null) { _backFailed = _jobKind; return; }          // it threw (the log says why): the black wall, no retrying
            if (_jobKind == want)
            {
                Log.Info($"theater: the {want} backdrop drawn in {_jobMs} ms");
                _upload = px;
                _uploadRow = 0;
                _uploadKind = want;
                UploadSlice();
                return;
            }
        }
        // the style changed while one was drawn: that one is dropped, this one drawn now
        BackdropJob(want);
    }

    // the next quarter of the picture into the texture; the last one makes it the picture in use (the overlay is given it then)
    void UploadSlice()
    {
        if (_backTex == null) { _backTex = new Surface(_g, BackW, BackH); return; }    // one texture, whichever style's picture is in it (its own frame: 8 MB)
        int y0 = _uploadRow, y1 = Math.Min(BackH, y0 + BackH / UploadSlices);
        _g.Ctx.UpdateSubresource<uint>(_upload.AsSpan(y0 * BackW, (y1 - y0) * BackW), _backTex.Tex, 0, BackW * 4, 0,
            new Vortice.Mathematics.Box(0, y0, 0, BackW, y1, 1));
        _uploadRow = y1;
        if (y1 < BackH) return;
        _upload = null;
        _backKind = _uploadKind;
        _backSet = Backdrop.None;
    }

    // (its own method: the lambda's closure over 'want' is made only here - in BackdropTextures the compiler made it on entry, every
    // frame the backdrop showed, 32 B a frame for a thread started once)
    void BackdropJob(Backdrop want)
    {
        _jobKind = want;
        _job = new Thread(() =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var buf = _jobBuf ??= new uint[BackW * BackH];     // (the first time, here: 8 MB made and cleared off the main thread)
                PaintBackdrop(want, buf);
                _jobMs = sw.ElapsedMilliseconds;
                Volatile.Write(ref _jobPx, buf);
            }
            catch (Exception e) { Log.Error("theater backdrop: " + e); }
        }) { IsBackground = true, Name = "theater backdrop", Priority = ThreadPriority.BelowNormal };
        _job.Start();
    }

    void FreeBackdrop()
    {
        _backTex?.Dispose();
        _backTex = null;
        _backKind = _backSet = Backdrop.None;
        _upload = null;                   // (a picture half way into the texture goes with it: drawn again when that style is back)
    }

    // how much of the picture shows as theater comes in (t: 0..1). Full motion: the picture comes up a little after the dimming, like the
    // lights in a hall; Terminal's phosphor warms up slower, creeping up from dark (no blinking: Erik hates flicker). Otherwise (and
    // going out) it follows the dimming.
    public static float BackdropLights(Backdrop k, float t, bool coming, bool full)
    {
        if (!coming || !full) return 1;
        if (k == Backdrop.Phosphor)
        {
            float w = Smooth01((t - 0.15f) / 0.85f);
            return w * w;
        }
        return Smooth01((t - 0.2f) / 0.6f);
    }

    // the lights frame by frame: coming in they follow BackdropLights but only ever go up, once theater is in they are all up, going out
    // they stay as they are and fade with the dim. fresh: theater starts from nothing (they start dark). So a quick off / on never makes
    // them jump: turned off half way up they don't flash up to full, turned on again on the way out they don't drop back to dark (Terminal's
    // tube went from full to 15% and up again - a blink; Erik hates flicker)
    public static float BackdropLit(float lit, bool fresh, Backdrop k, float t, bool on, bool full)
    {
        if (fresh) lit = 0;
        if (on && t < 1) return Math.Max(lit, BackdropLights(k, t, true, full));
        return on ? 1 : lit;
    }

    void DestroyBackdrop()
    {
        _back?.Dispose();
        _back = null;
        _backSet = Backdrop.None;
    }

    void DisposeBackdrop()
    {
        DestroyBackdrop();
        FreeBackdrop();
    }

    // ---- --snapshot ----

    // the style's picture through the same thread and upload as in VR (it waits for it here); null if it could not be drawn
    public Surface BackdropForTest(Backdrop k)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)
        {
            BackdropTextures(k);
            if (_backTex != null && _backKind == k) return _backTex;
            if (_backFailed == k) return null;
            Thread.Sleep(2);
        }
        return null;
    }

    public bool CityHeldForTest => _city != null || _sky != null;
    public bool BackdropHeldForTest => _backTex != null;
    // what the wall's overlay was given last (a fake SteamVR): the black wall / the city
    public bool WallIsBlackForTest => _dim != null && _dim.TextureForTest == _black.Tex.NativePointer;
    public bool WallIsCityForTest => _dim != null && _city != null && _dim.TextureForTest == _city.Tex.NativePointer;
    public void CyberForTest() => BackdropOff();        // what a frame in Cyber does to the picture's texture

    // ---- drawing: any thread (no painter, no Theme) - plain numbers into premultiplied BGRA ----

    // a picture row by row: its opacity and colour per texel (the opacity smooth - no edges; the colour straight, 0..1); then the
    // fade up / down and the dither into 8 bits; small things (stars, lamps) after that, into the pixels
    abstract class Art
    {
        public abstract void Row(int y, float el, float[] a, float[] r, float[] g, float[] b);
        public virtual void Sprites(uint[] px) { }
    }

    public static uint[] PaintBackdrop(Backdrop k)
    {
        var px = new uint[BackW * BackH];
        PaintBackdrop(k, px);
        return px;
    }

    // into px (BackW x BackH; cleared first: the rows past the fade are left clear)
    public static void PaintBackdrop(Backdrop k, uint[] px)
    {
        Art art = k switch
        {
            Backdrop.Starfield => new StarfieldArt(),
            Backdrop.Hills => new HillsArt(),
            Backdrop.Aurora => new AuroraArt(),
            Backdrop.Platinum => new PlatinumArt(),
            Backdrop.Phosphor => new PhosphorArt(),
            _ => new HallArt(),
        };
        Array.Clear(px);
        float[] a = new float[BackW], r = new float[BackW], g = new float[BackW], b = new float[BackW];
        for (int y = 0; y < BackH; y++)
        {
            float el = RowEl(y), fade = BackFade(el);
            if (fade <= 0) continue;                    // up / down past the fade: clear
            Array.Clear(r); Array.Clear(g); Array.Clear(b);
            art.Row(y, el, a, r, g, b);
            int o = y * BackW;
            for (int x = 0; x < BackW; x++)
            {
                // the same dither for the alpha and the colour (so the colour stays under it); interleaved gradient noise: even, no
                // clumps, no pattern to see
                float n = Ign(x, y), al = a[x] * fade;
                uint ua = Q(al, n);
                uint ur = Math.Min(ua, Q(Math.Clamp(r[x], 0, 1) * al, n));
                uint ug = Math.Min(ua, Q(Math.Clamp(g[x], 0, 1) * al, n));
                uint ub = Math.Min(ua, Q(Math.Clamp(b[x], 0, 1) * al, n));
                px[o + x] = ua << 24 | ur << 16 | ug << 8 | ub;
            }
        }
        art.Sprites(px);
    }

    static uint Q(float v, float n) => (uint)Math.Clamp((int)(v * 255 + n), 0, 255);
    static float Ign(int x, int y) { float f = 0.06711056f * x + 0.00583715f * y; f = 52.9829189f * (f - MathF.Floor(f)); return f - MathF.Floor(f); }

    // a row's elevation (degrees, + up): the rows are even in height on the cylinder, so they crowd towards the top and bottom
    public static float RowEl(int y) => MathF.Atan((BackH / 2f - (y + 0.5f)) / (BackH / 2f) * WallAspect * MathF.PI) * 180 / MathF.PI;
    public static int ElRow(float el) => (int)MathF.Floor(BackH / 2f - MathF.Tan(el * MathF.PI / 180) / (WallAspect * MathF.PI) * (BackH / 2f));
    // a column's azimuth (degrees: 0 = the screen, + to its right, ±180 behind you) and back
    public static float ColAz(int x) => ((x + 0.5f) / BackW - 0.5f) * 360;
    static int AzCol(float az) => ((int)MathF.Floor((az / 360 + 0.5f) * BackW) % BackW + BackW) % BackW;
    // a texel's height in degrees at an elevation (its width is 360 / BackW everywhere)
    static float TexH(float el) { float c = MathF.Cos(el * MathF.PI / 180); return 360f / BackW * c * c; }

    // all of it up to FadeFrom, gone by FadeTo - a long, soft ease (smootherstep)
    public static float BackFade(float el)
    {
        float t = Math.Clamp((MathF.Abs(el) - FadeFrom) / (FadeTo - FadeFrom), 0, 1);
        return 1 - t * t * t * (t * (6 * t - 15) + 10);
    }

    static float Gauss(float d, float s) => MathF.Exp(-(d / s) * (d / s));
    static float Cover(float d, float th) => Math.Clamp(d / th + 0.5f, 0, 1);      // how much of a texel is inside an edge d degrees away
    static float[] Columns(Func<float, float> f)
    {
        var c = new float[BackW];
        for (int x = 0; x < BackW; x++) c[x] = f(ColAz(x));
        return c;
    }

    // a small thing (a star, a lamp) of straight colour (r, g, b) on the picture at (az, el): w texels wide and as tall as it is wide
    // seen from you (up high a texel looks narrower by cos(el) and its row thinner by cos²(el): w / cos(el) rows); k how much of it
    static void Spot(uint[] px, float az, float el, int w, float r, float g, float b, float k = 1)
    {
        int x0 = AzCol(az), y0 = ElRow(el), h = Math.Max(1, (int)MathF.Round(w / MathF.Cos(el * MathF.PI / 180)));
        for (int y = y0; y < y0 + h; y++)
        {
            if (y < 0 || y >= BackH) continue;
            for (int i = 0; i < w; i++) Tint(px, y * BackW + (x0 + i) % BackW, r, g, b, k);
        }
    }

    // a texel towards straight colour (r, g, b) by k, at its own alpha
    static void Tint(uint[] px, int i, float r, float g, float b, float k)
    {
        uint p = px[i], ua = p >> 24;
        if (ua == 0) return;
        float al = ua;
        uint Ch(int sh, float c)
        {
            float v = (p >> sh) & 0xFF;
            return (uint)Math.Clamp((int)MathF.Round(v + (c * al - v) * k), 0, (int)ua);
        }
        px[i] = ua << 24 | Ch(16, r) << 16 | Ch(8, g) << 8 | Ch(0, b);
    }

    // ---- Modern: a dark cinema hall ----
    // Darkest round the screen; the screen's light falls in a faint cool pool on the floor below it, rows of seat backs stand black
    // against it (only against it: elsewhere they are the dark itself, never shapes over your room), small warm step lights mark the
    // two aisles, which open out towards you the way they would.
    sealed class HallArt : Art
    {
        // the rows in front of you, the farthest first: its seat tops (deg), a seat's width (deg), its back's height (deg), its shift
        // (in seats: every other row sits between the seats of the one before), where the aisle crosses it (deg either side): the seat
        // there is left out - a whole one
        static readonly (float top, float w, float h, float shift, float aisle)[] Rows =
            { (-20, 6.4f, 3.2f, 0, 29), (-26.5f, 7.8f, 4.2f, 0.5f, 35), (-35, 10, 5.6f, 0.15f, 44), (-47, 13.6f, 7.6f, 0.65f, 57) };
        readonly float[] _vigAz = Columns(az => Gauss(az, 100)), _pool = Columns(az => Gauss(az, 48));

        public override void Row(int y, float el, float[] a, float[] r, float[] g, float[] b)
        {
            float vigEl = Gauss(el, 52), th = TexH(el), tw = 360f / BackW;
            // the pool of light: from just under the screen down past the seats, brightest about 25° down
            float pool = 0.30f * Gauss(el + 26, 13) * Math.Clamp((-el - 9) / 8, 0, 1);
            for (int x = 0; x < BackW; x++)
            {
                float az = ColAz(x);
                a[x] = 0.34f + 0.48f * _vigAz[x] * vigEl;        // the vignette: darkest round the screen
                float l = pool * _pool[x];
                if (l < 0.002f) continue;
                l *= 1 - Seats(az, el, th, tw);
                // straight colour: the light's colour over the opacity it is on (it ends up as l in the premultiplied texel)
                float k = l / a[x];
                r[x] = 0.62f * k; g[x] = 0.70f * k; b[x] = 0.90f * k;
            }
        }

        // the seat of a row at an azimuth (its number), and the middle of the one each aisle takes
        static float Seat(float az, (float top, float w, float h, float shift, float aisle) row) => MathF.Floor(az / row.w + row.shift);
        static float AisleAz((float top, float w, float h, float shift, float aisle) row, float side) =>
            (Seat(side * row.aisle, row) + 0.5f - row.shift) * row.w;

        // how much of the texel at (az, el) the seat backs cover
        static float Seats(float az, float el, float th, float tw)
        {
            float cover = 0;
            foreach (var row in Rows)
            {
                if (el > row.top + 1 || el < row.top - row.h - 1) continue;
                float u = az / row.w + row.shift, n = MathF.Floor(u), f = u - n;
                if (n == Seat(row.aisle, row) || n == Seat(-row.aisle, row)) continue;
                float q = MathF.Abs(f - 0.5f) / 0.43f;                       // 0 the middle of a seat .. 1 its side
                float side = Cover((1 - q) * 0.43f * row.w, tw);            // the gaps between seats
                if (side <= 0) continue;
                float qq = Math.Min(1, q);
                float top = row.top - row.h * 0.42f * (1 - MathF.Sqrt(1 - MathF.Pow(qq, 6)));     // the rounded corners of its back
                float c = side * Cover(top - el, th) * Cover(el - (row.top - row.h), th);
                if (c > cover) cover = c;
            }
            return cover;
        }

        // the step lights: a small warm lamp at the end of every row, down both aisles
        public override void Sprites(uint[] px)
        {
            foreach (var row in Rows)
                foreach (float side in new[] { -1f, 1f })
                {
                    float el = row.top - row.h * 0.85f, az = AisleAz(row, side) - 0.4f;
                    Spot(px, az - 0.4f, el + 0.35f, 4, 1f, 0.6f, 0.28f, 0.10f);     // its glow on the step
                    Spot(px, az, el, 2, 1f, 0.66f, 0.32f, 0.85f);
                }
        }
    }
}
