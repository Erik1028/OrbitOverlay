using System;
using System.Numerics;

namespace Orbit;

// Theater mode in Cyber: the night city's neon horizon breathes with the sound, and a spark runs round it as theater comes in (Erik
// 10-18: "work more on the Cyber style, mainly theater mode"). Gfx/HorizonPulse.cs has the arithmetic and the textures; this puts them
// on three overlays of their own, all in the city's level frame (TheaterCyber.cs) and following its radius:
//  - the ring: a thin neon line all the way round (curvature 1) 0.06 m nearer than the skyline band, so it lies along the skyline's foot
//    on the wall's own horizon line. Sort order 2 like the band and nearer than it (in front of it whichever one SteamVR goes by - the
//    band and the wall at one distance once swapped now and then), under the Ambilight (3) and every screen;
//  - two sparks: small flat flares on the horizon, sort 3, a little nearer still (still behind the Ambilight's farthest), shown only
//    while they run.
// Coming in (Motion Full), the moment the city's lights come on, a spark sets off each way from the screen and runs to behind you,
// lighting the ring behind it: the ring is an arc from the screen out to the sparks - its width and bend grow with them (the same
// radius) and its texture bounds show as much of the texture as the arc is of the circle, so the line keeps its height and nothing
// stretches (SteamVR takes the height from the bounds: Cyber's unfold relies on it). They meet behind you and fade, the ring settles
// into its breathing. Going out it fades with the wall; a quick off / on carries on from where it was (no second sweep).
// What a frame does: at rest the ring's SetAlpha, only when its brightness moves (in silence: nothing), and one meter reading; while
// the sparks run, the ring's width / bend / bounds and the two sparks' place and alpha - small calls. The textures are made once (the
// first Cyber theater in VR) and never drawn again. Motion reduced / off: the ring is just there at a calm still brightness - no sweep,
// no breathing, no meter read. Every other style, theater off: nothing (one look at a flag).
public sealed partial class Theater
{
    // what the PC plays now, 0..1 peak (-1: unknown) - AudioMixer.OutputPeak (OrbitApp); the tools give a made-up one. Asked once a
    // frame, only while Cyber's ring shows with Motion Full.
    public Func<float> SoundLevel;

    const float RingIn = 0.06f, SparkIn = 0.12f;     // nearer than the skyline band (which is BandIn nearer than the wall)
    const float MinAngle = 0.5f * MathF.PI / 180;    // the arc shorter than this (behind the screen anyway): not shown

    readonly HorizonPulse _pulse = new();
    VrOverlay _hzRing, _hzSparkL, _hzSparkR;
    Surface _hzRingTex, _hzSparkTex;
    bool _hzShown, _hzArmed, _hzFailed;
    float _hzSweep = -1;                     // seconds since the sparks set off (-1: not running)
    float _hzHalf = 0.5f;                    // the screen's half-angle seen from the city's middle when they set off (their fade in)

    // what the horizon is this frame (the checks; the overlays get the same)
    public struct HorizonFrame
    {
        public bool Ring, Sparks;            // shown
        public float Alpha, Width, Curvature, U0, U1, Radius;     // the ring's alpha, its arc, its texture bounds, its radius
        public float Angle, SparkAlpha, SparkRadius;              // how far round it is lit (π: all of it), the sparks
    }
    public HorizonFrame HorizonNow { get; private set; }
    public HorizonPulse PulseForTest => _pulse;
    public bool HorizonSweeping => _hzArmed || _hzSweep >= 0;
    public float HorizonSweepTime => _hzSweep;      // seconds since the sparks set off (-1: not running)

    // from Update, every frame (after the city's wall and band have their place: CyberWall)
    void UpdateHorizon(float dt, float dim)
    {
        if (_screen == null || _view == null) { HorizonOff(); return; }
        float a = Math.Clamp(dim, 0, 0.95f) * Ease(_t);            // the wall's alpha (Update's)
        if (a <= 0.004f || !Theme.Cyber || _p == null) { HorizonOff(); return; }
        bool full = CyberFx.Full, coming = On && full && _t < 1;
        float lights = coming ? Smooth01((_t - 0.45f) / 0.45f) : 1;  // the city's lights coming on (CyberWall's)
        float city = Math.Min(1, a * 1.3f) * lights;                   // the band's alpha: the ring is one of the city's lights
        if (!_hzShown)
        {
            // in from nothing: Full - the sweep, once the lights come on; else the ring is just there
            _hzShown = true;
            _hzArmed = coming;
            _hzSweep = -1;
            _pulse.Reset();
        }
        if (!full) { _hzArmed = false; _hzSweep = -1; }                // (Motion turned down meanwhile: done at once)
        // only on the way in: stopped again before the lights came on, it stays dark as it goes (the sparks must not set off then -
        // going out 'lights' is 1 at once)
        if (_hzArmed && lights > 0 && On)
        {
            _hzArmed = false;
            _hzSweep = 0;
            _hzHalf = Math.Clamp(_view.Size / 2 / MathF.Max(0.5f, Vector3.Distance(_view.Place.Translation, _level.Translation)), 0.1f, 1.2f);
        }
        else if (_hzSweep >= 0 && (_hzSweep += dt) >= HorizonPulse.SweepS + HorizonPulse.SettleS) _hzSweep = -1;
        float bright = full ? _pulse.Step(SoundLevel?.Invoke() ?? -1, dt) : HorizonPulse.Calm;
        float ang = _hzArmed ? 0 : _hzSweep >= 0 ? HorizonPulse.SweepAngle(_hzSweep) : MathF.PI;
        if (_hzSweep >= 0) bright += (HorizonPulse.Cap - bright) * HorizonPulse.Charge(_hzSweep);
        float spark = _hzSweep >= 0 ? city * HorizonPulse.SparkAlpha(_hzSweep, ang, _hzHalf) : 0;
        // the band's radius (CyberWall set it this frame; the tools have no wall: the same sum)
        var c = _level.Translation;
        float r = _ringR > 0 ? _ringR : MathF.Max(Vector3.Distance(_view.Place.Translation, c), Vector3.Distance(_view.World.Translation, c)) + 0.6f;
        float rr = r - BandIn - RingIn, hu = ang / (2 * MathF.PI);
        var f = new HorizonFrame
        {
            Ring = ang >= MinAngle,
            Alpha = MathF.Round(Math.Clamp(city * bright, 0, 1) * 1024) / 1024,     // in 1/1024 steps: no call for a change nobody sees
            Width = 2 * ang * rr, Curvature = ang / MathF.PI, U0 = 0.5f - hu, U1 = 0.5f + hu, Radius = rr,
            Angle = ang, SparkAlpha = spark, Sparks = spark > 0.004f, SparkRadius = r - BandIn - SparkIn,
        };
        if (f.Alpha <= 0) f.Ring = false;
        HorizonNow = f;
        if (_dim == null || !EnsureHorizon()) return;                // no VR (the tools): worked out all the same
        if (!f.Ring) _hzRing.Hide();
        else
        {
            _hzRing.SetAbsolute(Ring(rr));
            _hzRing.SetWidth(f.Width);
            _hzRing.SetCurvature(f.Curvature);
            _hzRing.SetTextureBounds(f.U0, 0, f.U1, 1);
            _hzRing.SetAlpha(f.Alpha);
            _hzRing.Show();
        }
        if (!f.Sparks) { _hzSparkL.Hide(); _hzSparkR.Hide(); }
        else
        {
            float w = 2 * f.SparkRadius * MathF.Tan(HorizonPulse.SparkDeg / 2 * MathF.PI / 180);
            _hzSparkL.SetAbsolute(SparkAt(-ang, f.SparkRadius));
            _hzSparkR.SetAbsolute(SparkAt(ang, f.SparkRadius));
            _hzSparkL.SetWidth(w);
            _hzSparkR.SetWidth(w);
            _hzSparkL.SetAlpha(spark);
            _hzSparkR.SetAlpha(spark);
            _hzSparkL.Show();
            _hzSparkR.Show();
        }
    }

    // a spark at 'phi' round from the screen (+: to the right), on the horizon at radius r, facing the city's middle
    Matrix4x4 SparkAt(float phi, float r) => Matrix4x4.CreateTranslation(0, 0, -r) * Matrix4x4.CreateRotationY(-phi) * _level;

    // not shown (theater off, another style, the wall faded out): gone, and from the floor with a sweep the next time
    void HorizonOff()
    {
        if (!_hzShown) return;
        _hzShown = _hzArmed = false;
        _hzSweep = -1;
        _pulse.Reset();
        HorizonNow = default;
        _hzRing?.Hide();
        _hzSparkL?.Hide();
        _hzSparkR?.Hide();
    }

    // the overlays, made the first time they are needed - in the middle of a frame, like the Ambilight's: SteamVR refusing them (too
    // many overlays) must not throw out of every frame. No horizon until VR connects again then, theater goes on.
    bool EnsureHorizon()
    {
        if (_hzRing != null) return true;
        if (_hzFailed) return false;
        try
        {
            EnsureHorizonTex();
            _hzRing = new VrOverlay("erik.orbit.theater.horizon", "OrbitOverlay theater horizon");
            _hzRing.SetSortOrder(2);                                    // the band's: nearer than it, under the Ambilight and the screens
            _hzRing.SetFlag(Valve.VR.VROverlayFlags.IsPremultiplied, true);
            _hzRing.SetTexture(_hzRingTex.Tex);                          // once: it never changes
            _hzSparkL = Spark("erik.orbit.theater.spark.l");
            _hzSparkR = Spark("erik.orbit.theater.spark.r");
            return true;
        }
        catch (Exception e)
        {
            Log.Warn("theater horizon overlays: " + e.Message);
            DestroyHorizon();
            _hzFailed = true;
            return false;
        }
    }

    VrOverlay Spark(string key)
    {
        var o = new VrOverlay(key, "OrbitOverlay theater spark");
        o.SetSortOrder(3);
        o.SetFlag(Valve.VR.VROverlayFlags.IsPremultiplied, true);
        o.SetTexture(_hzSparkTex.Tex);
        return o;
    }

    // the two textures, once (a few ms; --snapshot: their pictures)
    public void EnsureHorizonTex()
    {
        if (_hzRingTex == null)
        {
            _hzRingTex = new Surface(_g, HorizonPulse.RingW, HorizonPulse.RingH);
            _g.Ctx.UpdateSubresource(HorizonPulse.RingPixels(), _hzRingTex.Tex, 0, HorizonPulse.RingW * 4, 0, null);
        }
        if (_hzSparkTex == null)
        {
            _hzSparkTex = new Surface(_g, HorizonPulse.SparkW, HorizonPulse.SparkH);
            _g.Ctx.UpdateSubresource(HorizonPulse.SparkPixels(), _hzSparkTex.Tex, 0, HorizonPulse.SparkW * 4, 0, null);
        }
    }

    public Surface HorizonRingForTest { get { EnsureHorizonTex(); return _hzRingTex; } }
    public Surface HorizonSparkForTest { get { EnsureHorizonTex(); return _hzSparkTex; } }

    void DestroyHorizon()
    {
        _hzRing?.Dispose(); _hzRing = null;
        _hzSparkL?.Dispose(); _hzSparkL = null;
        _hzSparkR?.Dispose(); _hzSparkR = null;
        _hzShown = _hzArmed = _hzFailed = false;
        _hzSweep = -1;
    }

    void DisposeHorizon()
    {
        DestroyHorizon();
        _hzRingTex?.Dispose(); _hzRingTex = null;
        _hzSparkTex?.Dispose(); _hzSparkTex = null;
    }
}
