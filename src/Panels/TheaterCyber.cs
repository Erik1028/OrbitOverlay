using System;
using System.Runtime.CompilerServices;
using System.Drawing;
using System.Numerics;

namespace Orbit;

// Theater mode in the Cyber style (Erik 10-17: the grid wall "looked low effort"): the dark wall all the way round you is a night
// city - a violet sky with a few stars over a glowing neon horizon, a synthwave floor grid running out to it (on the cylinder the
// floor's rays are its vertical lines and its rings rows that crowd towards the horizon), and on a second, sharper band of its own a
// skyline round the horizon: building silhouettes, lit windows, the odd neon sign. Both textures are drawn ONCE (a few ms, the first
// time) and kept; coming in, the wall powers up like a tube (a flicker of its alpha) and the city's lights come on after it (Full) - a
// quick off / on carries on from where it got to, nothing jumps (CityLit).
// The screen's own lock-on and scan, and the switch's unfold over the old one, are CyberScreenFx's (the panels').
//
// The city has a frame of its own (Erik 10-17: it moved and tilted with the screen, "the houses must be level"): level, round your
// head where it was when theater started, turned to face the screen - set again by Recenter only. Its radius is the screen's distance
// from there plus Behind, so it only changes when the screen is moved (it followed the head before: the whole city was re-laid every
// frame). The skyline band is a little nearer than the wall: at one distance SteamVR swapped the two now and then - the buildings
// flickered.
// While it shows with Motion Full the city lives a little - windows, signs, antenna lights, a few stars, flying cars (TheaterCityLife.cs):
// DrawSkyline records every lamp it draws for that, and the static city is exactly the one drawn here.
public sealed partial class Theater
{
    Surface _city, _sky;                 // the wall (WallAspect tall, fading like the black one) and the skyline band
    VrOverlay _band;
    bool _cityOn;
    Matrix4x4 _level = Matrix4x4.Identity;  // the city's frame: level, at your head, facing the screen (Start / Recenter)
    float _ringR = -1;                       // its radius set last (-1: set it again)
    float _tubeLit = 1, _tubeAt, _cityLit = 1;  // the power-up now (CityLit): the wall's tube, how far its blink has played, the city's lights
    bool _wallUp;                            // the wall showed the frame before (else theater starts from nothing)
    const float BandIn = 0.15f;              // the band this much nearer than the wall
    const int CityW = 1024, BandW = 4096, BandH = 512;   // the band: an eighth of its width tall - 21° above and below the horizon

    // the wall's texture rows: f = 0 the horizon .. 1 the top / bottom edge; the elevation there (WallAlpha's)
    static float RowAngle(float f) => MathF.Atan(f * WallAspect * MathF.PI);
    // the band's rows: the same angle, its half-height being an eighth of 2π
    static float BandRow(float deg) => MathF.Tan(deg * MathF.PI / 180) / (MathF.PI / 8);

    void CreateCyber()
    {
        _band = new VrOverlay("erik.orbit.theater.city", "OrbitOverlay theater city");
        _band.SetSortOrder(2);                                          // over the wall, under every screen and panel
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
        if (_city == null) { _city = new Surface(_g, CityW, (int)(CityW * WallAspect)); DrawCity(); }
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
        // coming in (Full): the wall flickers on like a tube, the city's lights after it; going: both fade with the screen (CityLit)
        _dim.SetAlpha(a * _tubeLit);
        if (_band == null) return true;
        float ba = a * _cityLit;
        if (ba <= 0.004f) { _band.Hide(); return true; }
        _bandA = Math.Min(1, ba * 1.3f);              // (city life's cars fade with it)
        _band.SetAlpha(_bandA);                       // the lights a little brighter than the dimming itself
        _band.Show();
        return true;
    }

    // The power-up frame by frame (Update, before the wall and the horizon; up: the wall shows this frame). It has a memory, like a
    // backdrop's lights (BackdropLit): a quick off no longer pops the skyline (and the horizon ring with it) to full - its lights were
    // worked out from On and _t alone, 1 the moment theater stopped coming in - and a quick on again no longer blinks the tube a second time.
    void CityLit(bool up)
    {
        bool fresh = !_wallUp;
        _wallUp = up;
        if (!up) return;
        bool full = CyberFx.Full;
        _cityLit = BackdropLit(_cityLit, fresh, Backdrop.None, _t, On, full);
        _tubeLit = TubeLit(_tubeLit, ref _tubeAt, fresh, _t, On, full);
    }

    // the tube: coming in (Full) it plays Flicker once, only as t goes past where it has been (on again after a quick off: it carries on from
    // there); once theater is in it is on; going out it stays as it is and fades with the dim. fresh: from nothing.
    public static float TubeLit(float lit, ref float reached, bool fresh, float t, bool on, bool full)
    {
        if (fresh) { lit = 0; reached = 0; }
        if (!on) return lit;
        if (!full || t >= 1) { reached = 1; return 1; }
        if (t <= reached) return lit;
        reached = t;
        return Flicker(t);
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

    // ---- the wall: sky, horizon, floor grid ----
    void DrawCity()
    {
        int w = _city.W, h = _city.H, mid = h / 2;
        _p.Begin(_city.Bitmap, 0x00000000);
        const int Band = 4;
        for (int y = 0; y < h; y += Band)
        {
            float f = MathF.Abs(y + Band / 2f - mid) / mid, fade = WallAlpha(f);
            if (fade <= 0.003f) continue;
            float deg = RowAngle(f) * 180 / MathF.PI;
            bool sky = y < mid;
            // the sky: deep violet at the horizon going black up high; the floor: near black with a violet haze at the horizon
            uint c = sky ? Theme.Mix(0xFF2A0B36, 0xFF040309, Smooth01(deg / 38))
                         : Theme.Mix(0xFF1A0822, 0xFF05060B, Smooth01(deg / 14));
            _p.Fill(new RectangleF(0, y, w, Band), Pm(c, fade));
        }
        // a few faint stars (always the same ones)
        var rnd = new Random(1028);
        for (int i = 0; i < 260; i++)
        {
            float x = rnd.Next(w), y = rnd.Next((int)(mid * 0.08f), (int)(mid * 0.92f));
            float f = (mid - y) / mid, fade = WallAlpha(f);
            if (fade <= 0.05f || RowAngle(f) * 180 / MathF.PI < 12) continue;
            _p.Fill(new RectangleF(x, y, 1, 1), Pm(rnd.Next(4) == 0 ? 0xFF9FE9FF : 0xFFE8E0F0, fade * (0.25f + 0.45f * (float)rnd.NextDouble())));
        }
        // the floor's rings: distances out from you (1.4 m below your eyes), the row of each - they crowd towards the horizon
        float[] dist = { 0.9f, 1.2f, 1.6f, 2.1f, 2.8f, 3.7f, 5f, 7f, 10f, 15f, 24f, 40f };
        foreach (float d in dist)
        {
            float tan = 1.4f / d, f = tan / (WallAspect * MathF.PI), y = mid + f * mid, fade = WallAlpha(f);
            if (fade <= 0.003f) continue;
            float near = Smooth01((MathF.Atan(tan) * 180 / MathF.PI - 1.5f) / 25);     // dimmer towards the horizon
            _p.Fill(new RectangleF(0, MathF.Round(y), w, 1), Pm(Theme.Mix(0xFF5A1A52, 0xFFFF2A6D, 0.35f * near), fade * (0.25f + 0.6f * near)));
        }
        // the floor's rays: on the cylinder, a ray along the floor is a vertical line (every 5°), fading as it nears the horizon
        int rays = 72;
        for (int y = mid + 2; y < h; y += Band)
        {
            float f = (y + Band / 2f - mid) / mid, fade = WallAlpha(f);
            if (fade <= 0.003f) continue;
            float near = Smooth01((RowAngle(f) * 180 / MathF.PI - 1) / 22);
            uint c = Pm(Theme.Mix(0xFF143C46, 0xFF2BD4E4, 0.25f * near), fade * (0.2f + 0.55f * near));
            for (int i = 0; i < rays; i++) _p.Fill(new RectangleF(MathF.Round(i * (float)w / rays), y, 1, Band), c);
        }
        // the horizon: a neon line with its glow
        for (int k = 6; k >= 1; k--)
            _p.Fill(new RectangleF(0, mid - k, w, 2 * k), Pm(0xFFFF2A6D, 0.05f + 0.02f * (7 - k)));
        _p.Fill(new RectangleF(0, mid - 1, w, 2), Pm(0xFFFF5C93, 0.9f));
        _p.End();
    }

    // ---- the band: the skyline all the way round, standing on the horizon (the band's middle row) ----
    // bare: into that texture without its lamps and haze (city life's base: the band under them, TheaterCityLife.cs)
    // (AggressiveOptimization here, on LampsEnd and LampsRead: run once a session, they ran as the JIT's tier-0 code - the band in 12 ms,
    // its lamps listed in 4.1 where the same code warm takes 2.9 and 0.7, the band under the lamps in 10 where warm takes 0.8. Compiled
    // fully the first time they are quick the once they run. Not on the others: DrawCity is D2D's calls, and SparkPixels got 3x slower -
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
        _city ??= new Surface(_g, CityW, (int)(CityW * WallAspect));
        DrawCity();
        return _city;
    }

    public Surface SkylineForTest()
    {
        _sky ??= new Surface(_g, BandW, BandH);
        DrawSkyline();
        return _sky;
    }

    // the power-up now (the tube, the city's lights) and the band's alpha as shown (0: hidden)
    public (float tube, float city) PowerUpForTest => (_tubeLit, _cityLit);
    public float BandAlphaForTest => _band != null && _band.Visible ? _bandA : 0;
    public bool WallUpForTest => _wallUp;

    void DisposeCyber()
    {
        DisposeCityLife();
        _band?.Dispose(); _band = null;
        _city?.Dispose(); _city = null;
        _sky?.Dispose(); _sky = null;
        _cityOn = false;
    }
}
