using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Orbit;

// Cyber theater's floor grid (Erik 10-18: "the grid under the city looks strange - it should not go down to infinity, and it should not be
// 360 degrees around either, but there should be dimming; and it looks very chunky, as if it were low poly all around").
// Why it looked low poly: SteamVR draws a curved overlay as a mesh of a set number of flat columns across its width, whatever its arc.
// The grid was on the night city's wall - a whole circle - so each column spanned many degrees, and a line well below eye level (the
// floor's rings) became a polygon with a corner at every column: its elevation dips across each flat piece. The rays, vertical lines
// every 5° on that cylinder, made the columns read as a cage. Detail at eye level (the skyline, the horizon) and smooth gradients (the
// dim) don't show it.
// Now the floor is only in front, only a short way down, and on overlays of its own:
//  - an arc of FloorArc (156°) centred on the screen's direction in the city's level frame, fading out from 50° to 77° either side -
//    nothing beside or behind you (the default screen covers about 32° either side: the floor shows round it and fades away);
//  - from the horizon down to 14° below it, fading out from 8° - no lines running down under you;
//  - the rings crowd towards the horizon as before (circles round you on the floor every 1.2 m: rows of the cylinder); the rays are the
//    floor's lines running out towards the screen, so they fan in to a point on the horizon behind it and end in the fade. Where they
//    crowd (near the horizon) both melt into a soft haze that gives way to the neon horizon;
//  - three curved overlays of 52° each, side by side on one cylinder between the wall and the skyline band (FloorIn). The same columns
//    over a seventh of the circle are a few degrees wide each: a line 14° down is off by a few thousandths of a degree - under a headset
//    pixel whatever SteamVR's count is (16 columns: 0.005°). Each has its own texture (no texture bounds, so nothing rests on how SteamVR
//    sizes a part of one), worked out texel by texel with soft lines (a gaussian FloorLine texels across: about two texels at half
//    strength, 0.108° a texel) - drawn once, while theater is off (TheaterWarm.cs, a quarter of one a frame), 3 x 262 KB, paid for by
//    the wall's empty rows (TheaterCyber.cs: the two together are 5 KB under the old wall alone);
//  - lights: their alpha the wall's x 1.3, like the skyline band's - they flicker on with the wall's tube and fade with it;
//  - sort order SortFloor: over the wall, under the skyline band, the horizon, the cars, the glow and the screens.
// What a frame does: with the wall shown, the three overlays' alpha follows the wall's - a call only when it moves, none at rest; their
// place only when the city's frame or radius does (theater start, Recenter, the screen moved). The checks and the headset views (before /
// after, SteamVR's columns modelled): ORBIT_SNAP_ONLY=cyfloor (CyberFloorTest.cs).
public sealed partial class Theater
{
    public const int FloorPieces = 3, FloorTexW = 480, FloorTexH = 140;
    public const float FloorArc = 156, FloorPieceArc = FloorArc / FloorPieces;  // degrees round, centred on the screen
    public const float FloorTop = 0.2f;                                         // degrees above the horizon: the textures' top edge
    public const float FloorFadeFrom = 8, FloorEnd = 14;                       // degrees below the horizon: fading from, gone by
    public const float FloorSideFrom = 50, FloorSideTo = 77;                    // degrees either side of the screen: fading from, gone by
    const float FloorIn = BandIn / 2;                                           // between the wall and the skyline band
    const float FloorEye = 1.4f, FloorTile = 1.2f;                              // meters: your eyes over the floor (as before), the grid's step
    const float FloorLine = 0.85f;                                              // a line's softness: its gaussian's sigma, in texels
    const float RingR = 1, RingG = 0.165f, RingB = 0.427f;                      // the rings' pink (#FF2A6D, the horizon's)
    const float RayR = 0.169f, RayG = 0.831f, RayB = 0.894f;                    // the rays' cyan (#2BD4E4)

    readonly Surface[] _floorTex = new Surface[FloorPieces];
    readonly VrOverlay[] _floorOv = new VrOverlay[FloorPieces];
    bool _floorUp, _floorSet, _floorFailed;     // shown / given its textures / SteamVR would not make them (none until VR connects again)
    float _floorA = -1, _floorR = -1;           // the alpha and the radius sent last
    Matrix4x4 _floorLevel;                      // the city's frame they were placed in
    uint[] _floorBuf;
    int _floorRows;                             // the textures' rows drawn so far (FloorPieces x FloorTexH: all of them)
    const int FloorWarmRows = 35;               // rows a frame while theater is off (TheaterWarm.cs): a quarter of a texture, ~1.5 ms

    // after the wall (Update, every frame)
    void UpdateFloor()
    {
        float a = _dim != null && _cityOn && _dim.Visible ? Math.Min(1, _dim.Alpha * 1.3f) : 0;       // (a light: a little over the dim, as the band)
        if (a <= 0.004f) { FloorHide(); return; }
        if (_floorFailed) return;
        while (FloorTextureStep(FloorTexH)) { }       // (TheaterWarm.cs draws them ahead: here only when it has not got to them yet)
        if (_floorOv[0] == null && !FloorCreate()) return;
        if (!_floorSet)
        {
            for (int k = 0; k < FloorPieces; k++) _floorOv[k].SetTexture(_floorTex[k].Tex);     // once: they never change
            _floorSet = true;
        }
        if (_ringR != _floorR || _level != _floorLevel)
        {
            _floorR = _ringR;
            _floorLevel = _level;
            float r = _ringR - FloorIn;
            for (int k = 0; k < FloorPieces; k++)
            {
                _floorOv[k].SetAbsolute(FloorAt(k, r));
                _floorOv[k].SetWidth(r * FloorPieceArc * MathF.PI / 180);
                _floorOv[k].SetCurvature(FloorPieceArc / 360);       // its arc of the circle: bent round your head at radius r
            }
        }
        if (a != _floorA)
        {
            _floorA = a;
            for (int k = 0; k < FloorPieces; k++) _floorOv[k].SetAlpha(a);
        }
        if (!_floorUp)
        {
            for (int k = 0; k < FloorPieces; k++) _floorOv[k].Show();
            _floorUp = true;
        }
    }

    // piece k (0: the left one as you face the screen) at radius r: its middle FloorPieceArc round from the screen's direction, its
    // texture's middle row as far down as its rows ask (its top edge FloorTop over the horizon) - in the city's level frame
    Matrix4x4 FloorAt(int k, float r)
    {
        float phi = (-FloorArc / 2 + (k + 0.5f) * FloorPieceArc) * MathF.PI / 180;
        return Matrix4x4.CreateTranslation(0, FloorMiddle * r, -r) * Matrix4x4.CreateRotationY(-phi) * _level;
    }

    // a texel's step (as a part of the radius: the same across and down), the textures' middle row's height (the same)
    static float FloorStep => FloorPieceArc * MathF.PI / 180 / FloorTexW;
    static float FloorMiddle => MathF.Tan(FloorTop * MathF.PI / 180) - FloorStep * FloorTexH / 2;

    void FloorHide()
    {
        if (!_floorUp) return;
        foreach (var o in _floorOv) o?.Hide();
        _floorUp = false;
        _floorA = -1;
    }

    // made in the middle of a frame, like the horizon's: SteamVR refusing them (too many overlays) must not throw out of every frame
    bool FloorCreate()
    {
        try
        {
            for (int k = 0; k < FloorPieces; k++)
            {
                var o = new VrOverlay($"erik.orbit.theater.floor{k}", $"OrbitOverlay theater floor {k}");
                _floorOv[k] = o;
                o.SetSortOrder(SortFloor);
                o.SetFlag(Valve.VR.VROverlayFlags.IsPremultiplied, true);
            }
            _floorSet = _floorUp = false;
            _floorA = _floorR = -1;
            return true;
        }
        catch (Exception e)
        {
            Log.Warn("theater floor overlays: " + e.Message);
            DestroyFloor();
            _floorFailed = true;
            return false;
        }
    }

    // the next rows of the floor's textures (up to 'rows', never past a piece's end), drawn and uploaded; false: all there. TheaterWarm
    // FloorWarmRows a frame, UpdateFloor what is left at once.
    bool FloorTextureStep(int rows)
    {
        if (FloorReady) return false;
        int k = _floorRows / FloorTexH, j0 = _floorRows % FloorTexH, j1 = Math.Min(FloorTexH, j0 + rows);
        _floorTex[k] ??= new Surface(_g, FloorTexW, FloorTexH);
        var buf = _floorBuf ??= new uint[FloorTexW * FloorTexH];
        FloorPixels(k, buf, j0, j1);
        _g.Ctx.UpdateSubresource<uint>(buf.AsSpan(j0 * FloorTexW, (j1 - j0) * FloorTexW), _floorTex[k].Tex, 0, FloorTexW * 4, 0,
            new Vortice.Mathematics.Box(0, j0, 0, FloorTexW, j1, 1));
        _floorRows = k * FloorTexH + j1;
        if (FloorReady) _floorBuf = null;
        _floorSet = false;
        return true;
    }

    bool FloorReady => _floorRows >= FloorPieces * FloorTexH;

    // ---- the picture: piece k's texels (rows j0 to j1), premultiplied BGRA ----
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void FloorPixels(int k, uint[] px, int j0 = 0, int j1 = FloorTexH)
    {
        float step = FloorStep, top = MathF.Tan(FloorTop * MathF.PI / 180), stepDeg = FloorPieceArc / FloorTexW;
        var dk = _dither ??= MakeDither();
        // the columns: how far round (its sine, the squares), how far into the side fade
        Span<float> sn = stackalloc float[FloorTexW], cc = stackalloc float[FloorTexW], ss = stackalloc float[FloorTexW];
        Span<float> side = stackalloc float[FloorTexW];
        for (int i = 0; i < FloorTexW; i++)
        {
            float azd = -FloorArc / 2 + (k * FloorTexW + i + 0.5f) * stepDeg, az = azd * MathF.PI / 180;
            side[i] = Smooth01((FloorSideTo - MathF.Abs(azd)) / (FloorSideTo - FloorSideFrom));
            float s = MathF.Sin(az), c = MathF.Cos(az);
            sn[i] = s; cc[i] = c * c; ss[i] = s * s;
        }
        for (int j = j0; j < j1; j++)
        {
            float y = top - (j + 0.5f) * step;                   // the row's height over the horizon as a part of the radius: tan(elevation)
            float deg = -MathF.Atan(y) * 180 / MathF.PI;          // degrees below the horizon
            int o = j * FloorTexW;
            if (y > -1e-5f || deg >= FloorEnd) { Array.Clear(px, o, FloorTexW); continue; }
            float down = Smooth01((FloorEnd - deg) / (FloorEnd - FloorFadeFrom)) * Smooth01(deg / 0.8f);
            float near = Smooth01(deg / 9);                       // brighter nearer to you
            // the rings: a circle on the floor every FloorTile out from you; this row's distance out (in steps) and how many steps a row is
            float rho = FloorEye / -y;
            float ring = Lines(rho / FloorTile, FloorEye * step / (FloorTile * y * y), 1) * (0.6f + 0.4f * near);
            // the rays: the floor's lines running out towards the screen every FloorTile across - they meet on the horizon behind it. A
            // texel's place across them (rho sin / FloorTile) and how many of them a texel spans (its change across and down)
            float rs = rho / FloorTile, gy0 = FloorEye * step / (FloorTile * -y), iyy = 1 / (y * y), rayK = 0.55f + 0.4f * near;
            int dt = (j & 63) * 64;
            for (int i = 0; i < FloorTexW; i++)
            {
                float a0 = down * side[i];
                if (a0 <= 0) { px[o + i] = 0; continue; }
                float ray = Lines(rs * sn[i], gy0 * MathF.Sqrt(cc[i] + ss[i] * iyy), 1.6f) * rayK;
                float ar = ring * a0, ay = ray * a0, ka = 1 - ay;
                // the ray's cyan over the ring's pink
                px[o + i] = Pack(RingR * ar * ka + RayR * ay, RingG * ar * ka + RayG * ay, RingB * ar * ka + RayB * ay, 1 - (1 - ar) * ka,
                    (dk[dt + ((k * FloorTexW + i) & 63)] + 0.5f) / 64);
            }
        }
    }

    // Soft lines at every whole p, gp of p a texel apart: a gaussian FloorLine texels across round each, summed. Lines crowding closer
    // than about two texels melt into their average - a haze, kept dimmer than a line (by 'haze': the more they crowd, the dimmer), so the
    // crowd towards the horizon glows instead of turning into a bright band.
    static float Lines(float p, float gp, float haze)
    {
        float s = Math.Max(gp * FloorLine, 1e-6f), sum;
        if (s > 0.5f) sum = 2.5066283f * s;                      // the gaussians' sum is their average there (within 1.5 %)
        else
        {
            // the nearest line and the next two either side, each only while it reaches this texel (6 sigma): most texels need no exp
            float d = MathF.Abs(p - MathF.Round(p)), inv = 1 / s;
            sum = 0;
            for (int m = 0; m < 5; m++)
            {
                float q = (m == 0 ? d : m == 1 ? 1 - d : m == 2 ? 1 + d : m == 3 ? 2 - d : 2 + d) * inv;
                if (q < 6) sum += MathF.Exp(-0.5f * q * q);
            }
            if (sum <= 0) return 0;
        }
        return (1 - MathF.Exp(-1.6f * sum)) / MathF.Sqrt(1 + haze * 2.56f * s * s);
    }

    // another style: the floor's textures let go (only Cyber has them; TheaterBackdrop.cs)
    void FloorFree()
    {
        if (_floorTex[0] == null && !_floorUp) return;
        FloorHide();
        for (int k = 0; k < FloorPieces; k++) { _floorTex[k]?.Dispose(); _floorTex[k] = null; }
        _floorSet = false;
        _floorRows = 0;
        _floorBuf = null;
    }

    // SteamVR going away (Theater.DestroyOverlay)
    void DestroyFloor()
    {
        for (int k = 0; k < FloorPieces; k++) { _floorOv[k]?.Dispose(); _floorOv[k] = null; }
        _floorUp = _floorSet = _floorFailed = false;
        _floorA = _floorR = -1;
    }

    void DisposeFloor()
    {
        DestroyFloor();
        for (int k = 0; k < FloorPieces; k++) { _floorTex[k]?.Dispose(); _floorTex[k] = null; }
        _floorRows = 0;
        _floorBuf = null;
    }

    // ---- the checks (CyberFloorTest.cs) ----
    public Surface[] FloorForTest() { while (FloorTextureStep(FloorTexH)) { } return _floorTex; }
    public void FloorDrawForTest() { FloorFree(); FloorForTest(); }      // (theaterperf: what drawing all three costs)
    public VrOverlay[] FloorOverlaysForTest => _floorOv;
    public bool FloorHeldForTest => _floorTex[0] != null && FloorReady;
    public Matrix4x4 FloorPlaceForTest(int k, float r) => FloorAt(k, r);
    public float CityRadiusForTest => _ringR;

    // the wall's texels as the old 2048-row wall had them (its empty rows past 80° put back, top and bottom): for the tools that find a row
    // by the 2048-row arithmetic (BackW x BackH, as every other style's backdrop)
    public static uint[] CityAsBackdrop(uint[] px)
    {
        var o = new uint[BackW * BackH];
        Array.Copy(px, 0, o, (BackH - CityH) / 2 * BackW, Math.Min(px.Length, CityH * BackW));
        return o;
    }

    // every theater overlay there is, with the texture it was given last (null: one of no theater surface, a screen's)
    public List<(string name, VrOverlay ov, Surface tex)> LayersForTest()
    {
        var texs = new List<Surface> { _black, _city, _sky, _lifeSky, _hzRingTex, _hzSparkTex, _carTex, _carTexL, _droneTex, _backTex, _amb?.Glow };
        texs.AddRange(_floorTex);
        Surface Of(VrOverlay o)
        {
            foreach (var t in texs) if (t != null && t.Tex.NativePointer == o.TextureForTest) return t;
            return null;
        }
        var l = new List<(string, VrOverlay, Surface)>();
        void Add(string n, VrOverlay o) { if (o != null) l.Add((n, o, Of(o))); }
        Add("wall", _dim);
        Add("backdrop", _back);
        for (int k = 0; k < FloorPieces; k++) Add($"floor {k}", _floorOv[k]);
        Add("band", _band);
        Add("ring", _hzRing);
        Add("spark L", _hzSparkL);
        Add("spark R", _hzSparkR);
        for (int i = 0; i < CarCount; i++) Add($"car {i}", _carOv[i]);
        Add("glow", _ambOv);
        return l;
    }
}
