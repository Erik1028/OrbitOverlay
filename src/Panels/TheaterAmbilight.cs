using System;
using System.Numerics;

namespace Orbit;

// Theater mode's Ambilight (Erik 10-18): the big screen's edge colours as a soft glow on the dark wall round it (Gfx/Ambilight.cs samples
// the picture and draws the glow). Its own overlay, sort order 3: over the wall (1) and Cyber's city band (2), under every screen (106+).
// It sits just behind the screen - the same middle, bent round the same axis, a little farther - and reaches past it on every side by
// the same angle seen from you, so it hugs the screen wherever it is; it comes and goes with the screen (its fade, theater's own in /
// out), and Theater > Ambilight off fades it away. While it shows, the picture is sampled at most 10 times a second and only when the
// screen has a new frame; the glow's texture is written only while its colours are still easing (at rest: nothing but the alpha).
public sealed partial class Theater
{
    public bool AmbilightOn = true;          // TheaterConfig.Ambilight (OrbitApp sets it each frame)
    const double AmbSampleEvery = 0.1;       // seconds: at most 10 readings a second

    Ambilight _amb;
    VrOverlay _ambOv;
    float _ambOn = 1;                        // the setting's own fade
    bool _ambDirty = true;                   // the glow was written since the overlay was given it
    double _ambTime, _ambNext;               // the glow's own clock (the frames' dt): its 10 readings a second
    ITheaterScreen _ambSrc;                  // what was sampled last, and its frame
    long _ambFrame = -1;

    public float AmbilightAlpha { get; private set; }       // the glow's alpha this frame (the checks)
    public Ambilight AmbilightForTest => _amb;

    void UpdateAmbilight(float dt)
    {
        _ambOn = Motion.Step(_ambOn, AmbilightOn, dt, 0.3f);
        // the picture: the screen playing; while switching, the one under it until the new one has a frame
        var src = _screen;
        Surface pic = null;
        long frame = 0;
        if (src != null) pic = src.TheaterPicture(out frame);
        if (pic == null && _leaving != null) { src = _leaving; pic = src.TheaterPicture(out frame); }
        float a = 0;
        if (_screen != null && _view != null && _ambOn > 0)
        {
            // with the screen: its own fade in (the old one stays solid under a switch: no dip), theater's in / out
            float fade = _leaving != null ? 1 : Math.Clamp(_screen.TheaterFade, 0, 1), vis = 1 - (1 - fade) * (1 - fade) * (1 - fade);
            a = _ambOn * Ease(_t) * Math.Clamp(_view.Opacity, 0, 1) * vis;
        }
        AmbilightAlpha = a;
        if (a <= 0.004f || pic == null)
        {
            _ambOv?.Hide();
            _ambSrc = null;                  // shown again: sampled at once
            return;
        }
        _amb ??= new Ambilight(_g, _p);
        double now = _ambTime += dt;
        if (now >= _ambNext && (src != _ambSrc || frame != _ambFrame))
        {
            _amb.Sample(pic);
            _ambNext = now + AmbSampleEvery;
            _ambSrc = src;
            _ambFrame = frame;
        }
        _amb.Poll();
        _amb.Snap(a < 0.05f);                // barely showing yet: the colours are taken as they come, not eased in from the last ones
        var ts = src.TextureSize;
        if (_amb.Shape(ts.X / Math.Max(1, ts.Y))) _ambDirty = true;
        if (_amb.Step(dt)) { _amb.Compose(); _ambDirty = true; }
        if (!_amb.HasReading || _amb.Composed == 0) { _ambOv?.Hide(); return; }
        if (_ambOv == null && _dim != null)
        {
            _ambOv = new VrOverlay("erik.orbit.theater.glow", "OrbitOverlay theater glow");
            _ambOv.SetSortOrder(3);
            _ambOv.SetFlag(Valve.VR.VROverlayFlags.IsPremultiplied, true);
            _ambDirty = true;
        }
        if (_ambOv == null) return;          // no VR (--snapshot): the glow is worked out all the same
        if (_ambDirty)
        {
            _ambOv.SetTexture(_amb.Glow.Tex);
            _amb.Retire();
            _ambDirty = false;
        }
        GlowPlace(_view, ts, out var world, out float width, out float curv, out _);
        _ambOv.SetAbsolute(world);
        _ambOv.SetWidth(width);
        _ambOv.SetCurvature(curv);
        _ambOv.SetAlpha(Math.Min(1, a));
        _ambOv.Show();
    }

    // Where the glow goes for a screen drawn as v (its picture ts big): 'back' behind it along its own facing, bent round the same axis
    // (curved: its radius is the screen's + back; flat: as the screen's distance from you), and as much bigger as it is farther, so its
    // margin past the screen (Ambilight.MarginK x sqrt(w h), the glow texture's) is the same angle seen from you all round.
    public static void GlowPlace(TheaterView v, Vector2 ts, out Matrix4x4 world, out float width, out float curvature, out float back)
    {
        float w = Math.Max(0.05f, v.Width), h = w * ts.Y / Math.Max(1, ts.X);
        float dist = Math.Max(0.3f, Vector3.Distance(v.World.Translation, v.Head));
        back = Math.Clamp(0.06f * dist, 0.08f, 0.3f);      // well before the wall (0.6 m) and Cyber's skyline (0.45 m)
        bool curved = v.Curvature >= DesktopLayout.MinCurvature;
        float r = curved ? w / (2 * MathF.PI * v.Curvature) : dist;
        float m = Ambilight.MarginK * MathF.Sqrt(w * h), k = (r + back) / r;
        width = (w + 2 * m) * k;
        curvature = curved ? Math.Min(1, (w + 2 * m) / (2 * MathF.PI * r)) : 0;
        world = Matrix4x4.CreateTranslation(0, 0, -back) * v.World;
    }

    void DestroyAmbilight()
    {
        _ambOv?.Dispose();
        _ambOv = null;
        _ambDirty = true;
        _ambSrc = null;
    }

    void DisposeAmbilight()
    {
        DestroyAmbilight();
        _amb?.Dispose();
        _amb = null;
    }
}
