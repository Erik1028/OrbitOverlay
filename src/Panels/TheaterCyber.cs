using System;
using System.Runtime.CompilerServices;
using System.Drawing;
using System.Numerics;

namespace Orbit;

// Theater mode in the Cyber style (Erik 10-17: the grid wall "looked low effort"): the dark wall all the way round you is a night
// city - a violet sky with a few stars over a glowing neon horizon, a synthwave floor grid running out to it (on the cylinder the
// floor's rays are its vertical lines and its rings rows that crowd towards the horizon), and on a second, sharper band of its own a
// skyline round the horizon: building silhouettes, lit windows, the odd neon sign. Both textures are drawn ONCE (a few ms, the first
// time) and kept; coming in, the wall powers up like a tube (a flicker of its alpha) and the city's lights come on after it (Full).
// The screen's own lock-on and scan, and the switch's unfold over the old one, are CyberScreenFx's (the panels').
//
// The city has a frame of its own (Erik 10-17: it moved and tilted with the screen, "the houses must be level"): level, round your
// head where it was when theater started, turned to face the screen - set again by Recenter only. Its radius is the screen's distance
// from there plus Behind, so it only changes when the screen is moved (it followed the head before: the whole city was re-laid every
// frame). The skyline band is a little nearer than the wall: at one distance SteamVR swapped the two now and then - the buildings
// flickered.
// While it shows with Motion Full the city lives a little - windows, signs, antenna lights, a few stars, flying cars (TheaterCityLife.cs):
// DrawSkyline records every lamp it draws for that, and the static city is exactly the one drawn here.
// (Erik 10-18: the grid "should not go down to infinity, nor be 360° round, and it looks low poly": the floor grid is no longer on the wall
// but in front only, a short way down, on overlays of its own - TheaterFloor.cs. The wall keeps the sky, the stars and the horizon's glow.)
public sealed partial class Theater
{
    Surface _city, _sky;                 // the wall (WallAspect tall, fading like the black one) and the skyline band
    VrOverlay _band;
    bool _cityOn;
    Matrix4x4 _level = Matrix4x4.Identity;  // the city's frame: level, at your head, facing the screen (Start / Recenter)
    float _ringR = -1;                       // its radius set last (-1: set it again)
    const float BandIn = 0.15f;              // the band this much nearer than the wall
    const int CityW = 1024, BandW = 4096, BandH = 512;   // the band: an eighth of its width tall - 21° above and below the horizon

    // the wall's texture rows: f = 0 the horizon .. 1 the top / bottom edge; the elevation there (WallAlpha's)
    static float RowAngle(float f) => MathF.Atan(f * WallAspect * MathF.PI);
    // the band's rows: the same angle, its half-height being an eighth of 2π
    static float BandRow(float deg) => MathF.Tan(deg * MathF.PI / 180) / (MathF.PI / 8);

    void CreateCyber()
    {
        _band = new VrOverlay("erik.orbit.theater.city", "OrbitOverlay theater city");
        _band.SetSortOrder(SortBand);                                   // over the wall and the floor, under the horizon, cars, glow, screens
        _band.SetFlag(Valve.VR.VROverlayFlags.IsPremultiplied, true);
        _band.SetCurvature(1f);
    }

    // the city's frame (Start / Recenter): level, at your head, turned to face the screen
    void Level(VrSession vr)
    {
        _level = LevelFrame(vr.Hmd, _view.Place.Translation);
        _ringR = -1;
    }

    public static Matrix4x4 LevelFrame(in Matrix4x4 hmd, Vector3 screen)
    {
        var f = screen - hmd.Translation;
        f.Y = 0;
        if (f.LengthSquared() < 0.01f) return MathUtil.YawFrame(hmd);    // right above / below you: the way you face
        var z = -Vector3.Normalize(f);
        return MathUtil.Basis(Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)), Vector3.UnitY, z, hmd.Translation);
    }

    // a cylinder round the city's frame at radius r: its middle straight ahead, all the way round (curvature 1)
    Matrix4x4 Ring(float r) => Matrix4x4.CreateTranslation(0, 0, -r) * _level;

    // with the wall shown this frame (a = its alpha): in the Cyber style the city - its textures, its place, the power-up - and
    // true; otherwise the black wall's texture back, the band away, and false (the wall goes behind the screen then)
    bool CyberWall(float a, float behind)
    {
        bool cy = Theme.Cyber && _p != null;
        if (!cy)
        {
            if (_cityOn) { _dim.SetTexture(_black.Tex); _cityOn = false; _ringR = -1; }     // (the backdrop sets the wall's place)
            _band?.Hide();
            return false;
        }
        if (!CityReady) CityRows(CityH);
        if (_sky == null) { _sky = new Surface(_g, BandW, BandH); DrawSkyline(); }
        if (!_cityOn)
        {
            _dim.SetTexture(_city.Tex);
            _band?.SetTexture(_sky.Tex);            // once: they never change
            _cityOn = true;
            _ringR = -1;
        }
        // behind the screen wherever it is now and where it is going (growing out of its place it may be the farther one)
        var c = _level.Translation;
        float r = MathF.Max(Vector3.Distance(_view.Place.Translation, c), Vector3.Distance(_view.World.Translation, c)) + behind;
        if (MathF.Abs(r - _ringR) > 0.001f)
        {
            _ringR = r;
            _dim.SetAbsolute(Ring(r));
            _dim.SetWidth(2 * MathF.PI * r);
            _band?.SetAbsolute(Ring(r - BandIn));
            _band?.SetWidth(2 * MathF.PI * (r - BandIn));
        }
        // coming in (Full): the wall flickers on like a tube, the city's lights after it; going: both fade with the screen
        bool coming = On && CyberFx.Full && _t < 1;
        float wall = coming ? Flicker(_t) : 1, lights = coming ? Smooth01((_t - 0.45f) / 0.45f) : 1;
        _dim.SetAlpha(a * wall);
        if (_band == null) return true;
        float ba = a * lights;
        if (ba <= 0.004f) { _band.Hide(); return true; }
        _bandA = Math.Min(1, ba * 1.3f);              // (city life's cars fade with it)
        _band.SetAlpha(_bandA);                       // the lights a little brighter than the dimming itself
        _band.Show();
        return true;
    }

    // a tube switching on: two short blinks, then up
    static float Flicker(float t)
    {
        if (t < 0.05f) return 0;
        if (t < 0.11f) return 0.9f;
        if (t < 0.17f) return 0.15f;
        if (t < 0.21f) return 1;
        if (t < 0.25f) return 0.35f;
        return 1;
    }

    static float Smooth01(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    // premultiplied colour at alpha a (the textures are drawn on a transparent clear)
    static uint Pm(uint c, float a) => Theme.WithAlpha(c, Math.Clamp(a, 0, 1));

    // ---- the wall: the sky, the dim, the horizon's glow, soft stars ----
    // (Erik 10-18: the floor grid is off the wall - TheaterFloor.cs.) Nothing left on it has the detail SteamVR's columns can show (a
    // whole circle in a fixed number of flat columns, each many degrees wide): the sky and the dim are smooth gradients, dithered (8 bits
    // over a wall this size showed bands); the horizon's glow lies at eye level, where the columns bend nothing; the stars are points -
    // soft round ones now, about two texels across (a 1-texel star was a blocky diamond in the headset), the same stars as before. Its
    // alpha is WallAlpha everywhere but under a star: the dim as dark as ever, all the way round. Worked out texel by texel straight into
    // the texture, a slice of rows at a time (no painter: nothing to map; a 256 KB slice, not a 7.6 MB picture).
    // It is 1850 rows tall, not 2048: the rows past 80° up and down were empty (WallAlpha is 0 there), and SteamVR makes an overlay as tall
    // as its texture - its top and bottom are at 80° instead of 81°, the same dim, and 0.8 MB that pays for the floor's three textures.
    public const int CityH = 1850;
    public static int CityWidth => CityW;
    const int CitySlice = 64;                // rows uploaded at a time
    const int CityWarmRows = 240;            // rows a frame while theater is off (TheaterWarm.cs): 8 frames of ~1 ms
    const float StarDeg = 0.2f;              // a star's softness (its gaussian's sigma, degrees): half its strength 0.47° across

    struct WallStar { public float X, Y, Sx, Sy, A, R, G, B; public int Y0, Y1; }
    WallStar[] _wallStars;
    uint[] _cityBuf;
    readonly uint[] _cityLut = new uint[64];
    int _cityRow;                            // the wall's rows drawn so far (CityH: all of it)
    static byte[] _dither;                   // 64 x 64: the same fine noise under every gradient, in 64 steps

    bool CityReady => _city != null && _cityRow >= CityH;

    // the wall's next 'count' rows (made first if it is not there): TheaterWarm a few hundred a frame, CyberWall what is left at once
    void CityRows(int count)
    {
        if (_city == null) { _city = new Surface(_g, CityW, CityH); _cityRow = 0; }
        int end = Math.Min(CityH, _cityRow + count);
        while (_cityRow < end)
        {
            int y1 = Math.Min(end, _cityRow + CitySlice);
            DrawCityRows(_cityRow, y1);
            _cityRow = y1;
        }
        if (_cityRow >= CityH) _cityBuf = null;
    }

    // the elevation of the wall's row y (degrees, + up)
    public static float CityRowEl(float y) => RowAngle((CityH / 2f - y - 0.5f) / CityW) * 180 / MathF.PI;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    void DrawCityRows(int y0, int y1)
    {
        _wallStars ??= WallStars();
        var dk = _dither ??= MakeDither();
        int w = CityW;
        var buf = _cityBuf ??= new uint[CityW * CitySlice];
        var lut = _cityLut;
        for (int y = y0; y < y1; y++)
        {
            float f = (CityH / 2f - y - 0.5f) / CityW, fa = MathF.Abs(f), fade = WallAlpha(fa);
            float deg = RowAngle(fa) * 180 / MathF.PI;
            // the sky: deep violet at the horizon going black up high; below it the faintest violet haze going near black (no floor)
            uint c = f > 0 ? Theme.Mix(0xFF2A0B36, 0xFF040309, Smooth01(deg / 38)) : Theme.Mix(0xFF1A0822, 0xFF05060B, Smooth01(deg / 14));
            float r = ((c >> 16) & 0xFF) / 255f * fade, g = ((c >> 8) & 0xFF) / 255f * fade, b = (c & 0xFF) / 255f * fade, a = fade;
            // the horizon: a neon line with its glow, soft (half its core 2 rows, 0.7°, across) - the horizon ring's sharper line lies on it
            float dy = y + 0.5f - CityH / 2f;
            Over(ref r, ref g, ref b, ref a, 1f, 0.165f, 0.427f, 0.5f * MathF.Exp(-0.5f * dy * dy / (2.6f * 2.6f)));
            Over(ref r, ref g, ref b, ref a, 1f, 0.361f, 0.576f, 0.85f * MathF.Exp(-0.5f * dy * dy / (0.9f * 0.9f)));
            // the row is one colour: its 64 dithered texels worked out once, the row laid from them
            for (int k = 0; k < 64; k++) lut[k] = Pack(r, g, b, a, (k + 0.5f) / 64);
            int o = (y - y0) * w, dt = (y & 63) * 64;
            for (int x = 0; x < w; x++) buf[o + x] = lut[dk[dt + (x & 63)]];
            foreach (ref readonly var s in _wallStars.AsSpan())
            {
                if (y < s.Y0 || y > s.Y1) continue;
                float sy = (y + 0.5f - s.Y) / s.Sy, ey = MathF.Exp(-0.5f * sy * sy);
                int xr = (int)MathF.Ceiling(3 * s.Sx);
                for (int x = (int)s.X - xr; x <= (int)s.X + xr; x++)
                {
                    float sx = (x + 0.5f - s.X) / s.Sx, sa = s.A * ey * MathF.Exp(-0.5f * sx * sx);
                    if (sa < 0.002f) continue;
                    int xx = (x % w + w) % w, i = o + xx;
                    uint q = buf[i];
                    float qr = ((q >> 16) & 0xFF) / 255f, qg = ((q >> 8) & 0xFF) / 255f, qb = (q & 0xFF) / 255f, qa = (q >> 24) / 255f;
                    Over(ref qr, ref qg, ref qb, ref qa, s.R, s.G, s.B, sa);
                    buf[i] = Pack(qr, qg, qb, qa, (dk[dt + (xx & 63)] + 0.5f) / 64);
                }
            }
        }
        _g.Ctx.UpdateSubresource<uint>(buf.AsSpan(0, (y1 - y0) * w), _city.Tex, 0, (uint)(w * 4), 0, new Vortice.Mathematics.Box(0, y0, 0, w, y1, 1));
    }

    // premultiplied (r, g, b, a) as a BGRA texel, dithered by n (0..1): never a colour over its alpha
    static uint Pack(float r, float g, float b, float a, float n)
    {
        uint pa = (uint)Math.Clamp((int)(a * 255 + n), 0, 255);
        uint pr = Math.Min(pa, (uint)Math.Clamp((int)(r * 255 + n), 0, 255));
        uint pg = Math.Min(pa, (uint)Math.Clamp((int)(g * 255 + n), 0, 255));
        uint pb = Math.Min(pa, (uint)Math.Clamp((int)(b * 255 + n), 0, 255));
        return pa << 24 | pr << 16 | pg << 8 | pb;
    }

    // straight colour (r, g, b) at alpha k over a premultiplied texel
    static void Over(ref float r, ref float g, ref float b, ref float a, float cr, float cg, float cb, float k)
    {
        r = cr * k + r * (1 - k); g = cg * k + g * (1 - k); b = cb * k + b * (1 - k); a = k + a * (1 - k);
    }

    // the stars as they always were (the same random ones: where the 1-texel ones were, about as bright), each now a soft round dot -
    // round as you see it: the wall's rows crowd towards the top, so a star is taller in texels the higher it is
    static WallStar[] WallStars()
    {
        const int Mid = 1024;                                    // (where they are is worked out on the old 2048-row wall, as they were)
        var list = new System.Collections.Generic.List<WallStar>();
        var rnd = new Random(1028);
        float texDeg = 360f / CityW;
        for (int i = 0; i < 260; i++)
        {
            float x = rnd.Next(CityW), y = rnd.Next((int)(Mid * 0.08f), (int)(Mid * 0.92f));
            float f = (Mid - y) / Mid, fade = WallAlpha(f);
            if (fade <= 0.05f || RowAngle(f) * 180 / MathF.PI < 12) continue;
            uint c = rnd.Next(4) == 0 ? 0xFF9FE9FFu : 0xFFE8E0F0u;
            float a = fade * (0.25f + 0.45f * (float)rnd.NextDouble());
            float cy = y + 0.5f - (Mid - CityH / 2f), cos = MathF.Cos(RowAngle((CityH / 2f - cy) / CityW));
            float sx = StarDeg / (texDeg * cos), sy = StarDeg / (texDeg * cos * cos);
            list.Add(new WallStar
            {
                X = x + 0.5f, Y = cy, Sx = sx, Sy = sy, A = 0.8f * a,
                R = ((c >> 16) & 0xFF) / 255f, G = ((c >> 8) & 0xFF) / 255f, B = (c & 0xFF) / 255f,
                Y0 = (int)MathF.Floor(cy - 3 * sy), Y1 = (int)MathF.Ceiling(cy + 3 * sy),
            });
        }
        return list.ToArray();
    }

    // each of its rows the 64 steps of 1/64 in a shuffled order: a gradient's row comes out at its exact level on average (white noise was
    // off by up to a fifth of a level), and no pattern to see
    static byte[] MakeDither()
    {
        var t = new byte[64 * 64];
        var rnd = new Random(1018);
        var row = new byte[64];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++) row[x] = (byte)x;
            rnd.Shuffle(row);
            Array.Copy(row, 0, t, y * 64, 64);
        }
        return t;
    }

    // ---- the band: the skyline all the way round, standing on the horizon (the band's middle row) ----
    // bare: into that texture without its lamps and haze (city life's base: the band under them, TheaterCityLife.cs)
    // (AggressiveOptimization here, on LampsEnd and LampsRead: run once a session, they ran as the JIT's tier-0 code - the band in 12 ms,
    // its lamps listed in 4.1 where the same code warm takes 2.9 and 0.7, the band under the lamps in 10 where warm takes 0.8. Compiled
    // fully the first time they are quick the once they run. Not on the others: DrawCity (then) was D2D's calls, and SparkPixels got 3x slower -
    // tier-1 folds its static readonly layer table into the code, a first full compile cannot.)
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    void DrawSkyline(Surface bare = null)
    {
        var s = bare ?? _sky;
        int w = s.W, h = s.H, mid = h / 2;
        float px = mid;                                  // rows per unit of BandRow
        _p.Begin(s.Bitmap, 0x00000000);
        LampsBegin(bare != null);                        // every lamp recorded as it is drawn
        var rnd = new Random(1017);
        uint[] lit = { 0xFFF5E14B, 0xFFF5E14B, 0xFF48E5F0, 0xFFFF4FA3, 0xFFFFE9C9 };
        // a striped retro sun low on the horizon, a third of the way round (not behind the screen: the band's middle is in front)
        float sunX = w * 0.83f, sunR = BandRow(11) * px;
        {
            float cx = sunX, r = sunR, cy = mid + r * 0.18f;
            _p.PushClip(new RectangleF(0, 0, w, mid));             // its glow stays above the horizon
            for (int k = 6; k >= 1; k--) _p.Circle(cx, cy, r + k * 12, Pm(0xFFFF2A6D, 0.04f));
            _p.PopClip();
            for (float y = cy - r; y < mid; y += 1)
            {
                float dy = y - cy, half = MathF.Sqrt(MathF.Max(0, r * r - dy * dy));
                float t = Math.Clamp((y - (cy - r)) / (mid - (cy - r)), 0, 1);
                // the cuts: thin near the top, wider towards the horizon
                float band = (mid - y) / (r * 0.9f);
                if (t > 0.45f && ((int)((mid - y) / MathF.Max(3, 9 * band))) % 2 == 1) continue;
                _p.Fill(new RectangleF(cx - half, y, 2 * half, 1), Theme.Mix(0xFFF5E14B, 0xFFFF2A6D, t));
            }
        }
        // a far row of taller, hazier towers behind the near one (depth)
        bool AtSun(int bx, int bw) => bx + bw > sunX - sunR * 1.05f && bx < sunX + sunR * 1.05f;     // the city leaves the sun in view
        for (int fx = 0; fx < w;)
        {
            int fw = rnd.Next(30, 110);
            if (fx + fw > w) fw = w - fx;
            if (AtSun(fx, fw)) { fx += fw; continue; }
            int fh = (int)MathF.Round(BandRow(3 + 9 * (float)Math.Pow(rnd.NextDouble(), 1.4)) * px);
            _p.Fill(new RectangleF(fx, mid - fh, fw, fh), 0xFF1A0F2C);
            for (int wy = mid - fh + 5; wy < mid - 6; wy += 10)
                for (int wx = fx + 4; wx < fx + fw - 5; wx += 8)
                    if (rnd.NextDouble() < 0.07) _p.Fill(new RectangleF(wx, wy, 3, 3), Pm(0xFFB48CFF, 0.3f));
            fx += fw;
        }
        int x = 0;
        while (x < w)
        {
            int bw = rnd.Next(14, 64);
            if (x + bw > w - 8) bw = w - x;                 // the last one ends at the seam
            // mostly low blocks, now and then a tower (a cluster of them here and there)
            float deg = rnd.Next(10) == 0 ? 7 + 4.5f * (float)rnd.NextDouble() : 1.2f + 4.5f * (float)Math.Pow(rnd.NextDouble(), 1.6);
            if (AtSun(x, bw)) deg = MathF.Min(deg, 2.2f);                                   // only low roofs in front of the sun
            int bh = (int)MathF.Round(BandRow(deg) * px);
            int top = mid - bh;
            uint body = Theme.Mix(0xFF0B0A14, 0xFF16112A, (float)rnd.NextDouble() * 0.5f);
            _p.Fill(new RectangleF(x, top, bw, bh), body);
            _p.Fill(new RectangleF(x, top, bw, 2), 0xFF241C38);                     // a lit rim on its roof
            LampHouse(x, top, bw, mid);
            if (bh > 40 && rnd.Next(3) == 0)                                          // an antenna with a red light
            {
                int ax = x + bw / 2 - 1, ah = rnd.Next(6, 18);
                _p.Fill(new RectangleF(ax, top - ah, 2, ah), 0xFF2A2140);
                LampAntenna(ax, top - ah);
            }
            // windows: a grid of 3 x 4 cells, a few lit, each with a faint halo (no hard 2 px dots: they twinkled); the dark ones are
            // recorded too, in a colour of their own (city life lights some of them)
            float lp = 0.12f + 0.22f * (float)rnd.NextDouble();
            for (int wy = top + 5; wy < mid - 6; wy += 9)
                for (int wx = x + 3; wx < x + bw - 5; wx += 6)
                    if (rnd.NextDouble() < lp)
                    {
                        uint c = lit[rnd.Next(lit.Length)];
                        float al = 0.45f + 0.4f * (float)rnd.NextDouble();
                        LampWindow(wx, wy, c, al, true);
                    }
                    else LampWindow(wx, wy, lit[(int)(CyberFx.Hash(wx, wy) * lit.Length)], 0.45f + 0.4f * CyberFx.Hash(wy, wx), false);
            // now and then a neon sign down its side, with a glow
            if (bh > 30 && rnd.Next(8) == 0)
            {
                uint n = rnd.Next(2) == 0 ? 0xFF2BD4E4 : 0xFFFF2A6D;
                int sx = x + (rnd.Next(2) == 0 ? 2 : bw - 5), sl = Math.Min(bh - 10, rnd.Next(16, 44)), sy = top + rnd.Next(4, Math.Max(5, bh - sl - 4));
                LampSign(sx, sy, sl, n);
            }
            x += bw + (rnd.Next(5) == 0 ? rnd.Next(2, 10) : 0);                      // a gap now and then
        }
        // the street lights' haze along the foot of the skyline (over everything)
        for (int k = 0; k < 5; k++) LampHaze(mid - 2 - k * 2, w, 0xFFFF2A6D, 0.10f - 0.018f * k);
        LampsEnd(w, h, sunX, sunR);
        _p.End();
    }

    // --snapshot: the wall's and the band's textures
    public Surface GridForTest(float flash)
    {
        if (_city != null) _cityRow = 0;
        CityRows(CityH);
        return _city;
    }

    public Surface SkylineForTest()
    {
        _sky ??= new Surface(_g, BandW, BandH);
        DrawSkyline();
        return _sky;
    }

    void DisposeCyber()
    {
        DisposeCityLife();
        _band?.Dispose(); _band = null;
        _city?.Dispose(); _city = null;
        _sky?.Dispose(); _sky = null;
        _cityOn = false;
    }
}
