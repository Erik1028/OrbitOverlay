using System;
using System.Collections.Generic;
using System.Numerics;

namespace Orbit;

// A monitor or window panel that can play the theater screen.
public interface ITheaterScreen : IPointerTarget
{
    TheaterView Theater { get; set; }     // non-null: this panel is the theater screen, drawn where it says
    bool TheaterHidden { get; set; }      // another panel is: step aside
    string TheaterLabel { get; }          // its name on the switcher under the screen
    bool Playable { get; }                // has an overlay and a live capture
    bool HomeInWorld { get; }             // its own place is in the room (not on a hand / the head)
    string TheaterState { get; }          // for the log: is it showing, has it a picture
    float TheaterFade { get; }            // how far it has faded in, 0..1
    void TheaterVanish();                 // gone at once: it fades in again from nothing wherever it shows next
    // its picture now (the captured texture; null before its first frame) and that frame's number: the Ambilight samples it
    Surface TheaterPicture(out long frame) { frame = 0; return null; }
    // its own (saved) place, size, bend and opacity, where the screen grows from and goes back to
    bool Home(out Matrix4x4 world, out float width, out float curvature, out float opacity);
}

// Where the theater screen is. Place / Size / Bend are the theater's own (grabbing moves and sizes them, in memory
// only); World / Width / Curvature / Opacity are what the panel draws this frame, on the way in or out.
public sealed class TheaterView
{
    public Matrix4x4 Place;
    public float Size, Bend;
    public Vector3 Head;                  // the headset now: the bend is centred on it
    public Matrix4x4 World;
    public float Width, Curvature, Opacity = 1;
    public bool Behind;                   // the screen being switched away from: drawn under the new one
    public bool Flat;                     // the "Curved" setting is off
    float _scaleFrom;

    public void Move(in Matrix4x4 world) { Place = world; Refit(); }

    // stick left / right while holding it: smaller / bigger
    public void Resize(float v, float dt) { Size = Math.Clamp(Size * (1 + v * dt * 0.8f), Theater.MinWidth, Theater.MaxWidth); Refit(); }

    public void ScaleBegin() => _scaleFrom = Size;
    public void Scale(float ratio) { Size = Math.Clamp(_scaleFrom * ratio, Theater.MinWidth, Theater.MaxWidth); Refit(); }

    // curved around you at whatever distance it is: c = w / (2 pi r); r is the straight distance, as the screen faces
    // your head (also when it is up above you, lying back)
    public void Refit() => Bend = Flat ? 0 : DesktopLayout.CurvatureFor(Size, Math.Max(0.5f, Vector3.Distance(Place.Translation, Head)));

    public void Mirror(TheaterView v)
    {
        Place = v.Place; Size = v.Size; Bend = v.Bend; Head = v.Head;
        World = v.World; Width = v.Width; Curvature = v.Curvature; Opacity = v.Opacity;
    }
}

// Theater mode: one monitor or window becomes a big curved screen in front of your monitors, the other panels and the
// keyboard step aside and the world dims behind it. The saved layout is not touched: the screen's place is an override
// for as long as it lasts, and the next tap puts everything back. The size and distance you leave it at are kept.
//
// In and out: a monitor that is about where the big screen goes (the middle one) grows out of its own place and goes
// back into it; one off to the side fades in / out where the big screen is, with a slight zoom (gliding across from
// the side looked odd). Switching: the new one fades in over the old one, which stays until it is covered - no dip.
public sealed partial class Theater : IDisposable
{
    public const float MinWidth = 0.8f, MaxWidth = 8f;
    const float Glide = 0.45f, Appear = 0.3f;    // seconds: growing out of its place / fading in where it plays
    const float GlideAngle = 25;                 // degrees between its own place and the big screen to still grow out of it

    readonly Gpu _g;
    readonly Painter _p;
    Surface _black;
    VrOverlay _dim;
    TheaterView _view, _behind;
    ITheaterScreen _screen, _leaving;
    float _t, _leaveT;                    // _t: 0 = the screen at its home / faded out, 1 = the theater screen
    bool _glide, _checked;
    public bool On { get; private set; }

    public Theater(Gpu g, Painter p) { _g = g; _p = p; }

    // The world dims behind it: a black wall just behind the screen, bent all the way round you (a closed cylinder), dark up to
    // 55° above and below the middle and fading out by 80° - no edge to see when you look up or turn round (Erik 10-16: half a
    // circle with a hard top edge "showed very clearly"). It is under the screen both by sort order and by distance, so it can't
    // end up in front of it whichever one SteamVR goes by (1.8.0 used a sheet on the headset, 0.5 m away: in front of everything).
    // (Erik 10-18: since then it stands in the night city's level frame in every style, with the style's backdrop in front of it -
    // TheaterBackdrop.cs. NB: SteamVR takes an overlay's height from its texture, so this 4 x 256 one is 64 times as tall as it is round:
    // in fact it darkens all but straight up and down, and WallAlpha's fade only happens there.)
    public const float WallAspect = 2;     // its height for its width (2πr round): the top and bottom 81° from you
    const int WallW = 4, WallH = 256;

    // 0 = the middle row .. 1 = the top / bottom edge: how dark (the angle up from you does not depend on the radius)
    public static float WallAlpha(float f)
    {
        float deg = MathF.Atan(f * WallAspect * MathF.PI) * 180 / MathF.PI;
        float t = Math.Clamp((80 - deg) / (80 - 55), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public void CreateOverlay()
    {
        if (_black == null)
        {
            // straight into the texture (no painter: a retro style's colour mapping must not touch it), premultiplied black
            _black = new Surface(_g, WallW, WallH);
            var px = new uint[WallW * WallH];
            for (int y = 0; y < WallH; y++)
            {
                uint a = (uint)MathF.Round(255 * WallAlpha(MathF.Abs(y + 0.5f - WallH / 2f) / (WallH / 2f)));
                for (int x = 0; x < WallW; x++) px[y * WallW + x] = a << 24;
            }
            _g.Ctx.UpdateSubresource(px, _black.Tex, 0, WallW * 4, 0, null);
        }
        _dim = new VrOverlay("erik.orbit.theater.dim", "OrbitOverlay theater dim");
        _dim.SetSortOrder(1);
        _dim.SetFlag(Valve.VR.VROverlayFlags.IsPremultiplied, true);
        _dim.SetTexture(_black.Tex);
        _cityOn = false;                    // (Cyber: the night city, TheaterCyber.cs)
        _ringR = -1;
        _dim.SetCurvature(1f);
        CreateCyber();
        CreateBackdrop();                   // (every other style: its own backdrop in front of the wall, TheaterBackdrop.cs)
    }

    public void DestroyOverlay()
    {
        Stop(null, null);
        _dim?.Dispose();
        _dim = null;
        _band?.Dispose();
        _band = null;
        DestroyCityLife();                  // (the flying cars' overlays, TheaterCityLife.cs)
        DestroyBackdrop();
        DestroyAmbilight();
        DestroyHorizon();
    }

    public ITheaterScreen Screen => On ? _screen : null;
    public TheaterView View => On ? _view : null;

    // anchor: the monitor in the middle of your row (the big screen goes in front of it); null = the screen itself
    public void Start(ITheaterScreen screen, ITheaterScreen anchor, VrSession vr, TheaterConfig cfg)
    {
        if (On || screen == null || !vr.HmdValid) return;
        EndLeaving();
        float size = Math.Clamp(cfg.Width, MinWidth, MaxWidth), dist = Math.Clamp(cfg.Distance, 0.6f, 10f);
        _view = new TheaterView { Size = size, Head = vr.Hmd.Translation, Flat = !cfg.Curved };
        _view.Move(Where(anchor ?? screen, vr, dist));
        Level(vr);
        bool back = _screen == screen && screen.Theater != null;      // still on its way out: turn around from there
        if (_screen != null && !back) Release(_screen);
        _screen = screen;
        if (!back) { _t = 0; _glide = GrowsFromHome(screen); }
        Blend();
        _screen.Theater = _view;
        On = true;
        _checked = false;
        Log.Info($"theater: on - {screen.TheaterLabel} ({screen.TheaterState}), {(_glide ? "grows out of its place" : "fades in")}");
    }

    // its own place is about where the big screen goes (seen from your head): it can grow out of it
    bool GrowsFromHome(ITheaterScreen s)
    {
        if (!Motion.Full) return false;        // Look > Motion reduced: it fades in where it plays (a screen flying across is a lot)
        if (!s.Home(out var home, out _, out _, out _)) return false;
        var a = home.Translation - _view.Head;
        var b = _view.Place.Translation - _view.Head;
        if (a.Length() < 0.2f || b.Length() < 0.2f) return false;
        float cos = Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b));
        return cos > MathF.Cos(GlideAngle * MathF.PI / 180);
    }

    // Where the big screen goes: out along the line from your head through the anchor (the middle of your monitors),
    // tilted like it, at the theater distance. NOT where you look: theater mode is started from the watch, so at that
    // moment you look at your wrist (lying back with the arm up, that put the screen on the ceiling). A screen on a
    // hand or the head has no such place: then level in front of you.
    public static Matrix4x4 Where(ITheaterScreen anchor, VrSession vr, float dist)
    {
        var head = vr.Hmd.Translation;
        if (anchor.HomeInWorld && anchor.Home(out var home, out _, out _, out _))
        {
            var dir = home.Translation - head;
            if (dir.Length() > 0.2f)
            {
                var z = -Vector3.Normalize(dir);
                var up = MathUtil.Up(home);
                up -= Vector3.Dot(up, z) * z;
                if (up.Length() < 0.3f) { up = Vector3.UnitY - Vector3.Dot(Vector3.UnitY, z) * z; }
                if (up.Length() > 0.1f)
                {
                    up = Vector3.Normalize(up);
                    var x = Vector3.Normalize(Vector3.Cross(up, z));
                    up = Vector3.Cross(z, x);
                    return MathUtil.Basis(x, up, z, head - z * dist);
                }
            }
        }
        return Matrix4x4.CreateTranslation(0, 0, -dist) * MathUtil.YawFrame(vr.Hmd);
    }

    // the lowest point of the big screen with its bar under it, where it is going, a little lower still (Recenter puts the
    // keyboard under it)
    public bool LowestPoint(Vector3 head, out Vector3 p)
    {
        p = default;
        if (!On || _view == null || _screen == null) return false;
        var ts = _screen.TextureSize;
        float h = _view.Size * ts.Y / Math.Max(1, ts.X), dist = Vector3.Distance(head, _view.Place.Translation);
        p = _view.Place.Translation - Vector3.Normalize(MathUtil.Up(_view.Place)) * (h / 2 + 1.35f * TheaterBar.HeightAt(dist) + 0.03f * dist);
        return true;
    }

    // the settings on the bar under the screen: size and curve apply at once
    public void Apply(TheaterConfig cfg)
    {
        if (!On || _view == null) return;
        _view.Size = Math.Clamp(cfg.Width, MinWidth, MaxWidth);
        _view.Flat = !cfg.Curved;
        _view.Refit();
    }

    // Recenter while it plays: the layout has just been brought in front of you, the screen follows it
    public void Recenter(VrSession vr, ITheaterScreen anchor)
    {
        if (!On || !vr.HmdValid || _screen == null) return;
        float dist = Math.Clamp(Vector3.Distance(_view.Place.Translation, _view.Head), 0.6f, 10f);
        _view.Head = vr.Hmd.Translation;
        _view.Move(Where(anchor ?? _screen, vr, dist));
        Level(vr);
    }

    // the switcher under the screen: another monitor / window takes the same place and size. It fades in over the one
    // playing, which stays as it is underneath until it is covered (fading both at once let the world show through).
    public void Switch(ITheaterScreen to)
    {
        if (!On || to == null || to == _screen || !to.Playable) return;
        EndLeaving();
        _leaving = _screen;
        _behind = new TheaterView { Behind = true };
        _behind.Mirror(_view);
        _leaving.Theater = _behind;
        _leaveT = 0;
        to.TheaterHidden = false;
        to.TheaterVanish();
        _screen = to;
        _screen.Theater = _view;
        Log.Info("theater: switched to " + to.TheaterLabel);
    }

    void EndLeaving()
    {
        if (_leaving == null) return;
        _leaving.Theater = null;
        _leaving.TheaterVanish();         // covered by the new one: it just goes
        _leaving = null;
        _behind = null;
    }

    // the screen is done playing: back to its own panel. One that faded out where it played shows up at home by
    // fading in there (not popping in at full strength).
    void Release(ITheaterScreen s)
    {
        s.Theater = null;
        if (!_glide) s.TheaterVanish();
    }

    // goes back (vr != null), or at once; keeps the size and distance for next time
    public void Stop(VrSession vr, TheaterConfig cfg)
    {
        if (On && cfg != null && _view != null)
        {
            cfg.Width = _view.Size;
            cfg.Distance = Math.Clamp(Vector3.Distance(_view.Place.Translation, _view.Head), 0.6f, 10f);
            Log.Info($"theater: off ({cfg.Width:0.00} m wide at {cfg.Distance:0.00} m)");
        }
        On = false;
        EndLeaving();
        if (_screen == null) return;
        if (vr == null) { _glide = false; Release(_screen); _screen = null; _t = 0; return; }
        _glide = GrowsFromHome(_screen);  // the one playing now may be another than the one it started with
    }

    // before the panels update: where the screen is this frame, who steps aside, how dark the world is. (live is the List itself, not
    // IReadOnlyCollection: through the interface every foreach boxed the list's enumerator - 40 B a frame, theater on or off, the main
    // loop's largest steady garbage (Erik 10-18: "it must be optimized"); the List's own enumerator is a struct, nothing is allocated.)
    public void Update(VrSession vr, float dt, float dim, List<ITheaterScreen> live)
    {
        if (_leaving != null)
        {
            _leaveT += dt;
            if (!Contains(live, _leaving)) { _leaving = null; _behind = null; }
            else if (_screen == null || (_screen.TheaterFade >= 0.999f && _leaveT > 0.1f) || _leaveT > 1) EndLeaving();
        }
        if (_screen != null && (!Contains(live, _screen) || !_screen.Playable))
        {
            // its panel was rebuilt (monitors changed) or removed, or its window closed: theater is over
            _screen.Theater = null;
            _screen = null;
            EndLeaving();
            if (On) { On = false; Log.Info("theater: its screen went away"); }
        }
        // the others step aside while it plays (a window panel added meanwhile too), and come back near the end of
        // the way out, so the screen does not pass through them
        bool aside = On || (_screen != null && _t > 0.3f);
        foreach (var s in live) s.TheaterHidden = aside && s != _screen && s != _leaving;
        _t = Motion.Step(_t, On, dt, _glide ? Glide : Appear);       // Motion off: there (and gone) at once
        if (_screen != null)
        {
            if (vr.HmdValid) _view.Head = vr.Hmd.Translation;
            if (!On && _t <= 0) { Release(_screen); _screen = null; }
            else Blend();
        }
        long pt = PerfTrace.Now();              // (the perf tools: where a theater frame's time goes, section by section - PerfTrace.cs)
        if (_dim != null)
        {
            float a = Math.Clamp(dim, 0, 0.95f) * Ease(_t);
            if (a <= 0.004f || _screen == null) { _dim.Hide(); _band?.Hide(); _back?.Hide(); }
            else
            {
                const float Behind = 0.6f;
                // Cyber: the night city, level and still (TheaterCyber.cs); every other style: the wall and its backdrop, level and
                // still the same way (TheaterBackdrop.cs)
                if (!CyberWall(a, Behind)) BackdropWall(a, Behind, dt);
                else BackdropOff();
                _dim.Show();
            }
        }
        PerfTrace.Add(PerfTrace.Wall, pt); pt = PerfTrace.Now();
        UpdateCityLife(dt);                     // Cyber's night city alive: its lamps, its flying cars (TheaterCityLife.cs)
        PerfTrace.Add(PerfTrace.CityLife, pt); pt = PerfTrace.Now();
        UpdateAmbilight(dt);                    // the glow round the screen (TheaterAmbilight.cs)
        PerfTrace.Add(PerfTrace.Ambilight, pt); pt = PerfTrace.Now();
        UpdateHorizon(dt, dim);                 // Cyber: the neon horizon's breathing and its sweep (TheaterHorizon.cs)
        PerfTrace.Add(PerfTrace.Horizon, pt);
        // once it is in: how the screen is doing (if it does not show up, the log says why)
        if (On && _screen != null && _t >= 1 && !_checked)
        {
            _checked = true;
            Log.Info($"theater: playing {_screen.TheaterLabel} - {_screen.TheaterState}, " +
                     $"{Vector3.Distance(_view.World.Translation, _view.Head):0.00} m away, {_view.Width:0.00} m wide");
        }
    }

    static bool Contains(List<ITheaterScreen> live, ITheaterScreen s)
    {
        for (int i = 0; i < live.Count; i++) if (live[i] == s) return true;
        return false;
    }

    static float Ease(float t) => t * t * (3 - 2 * t);

    void Blend()
    {
        var v = _view;
        if (_glide && _screen.Home(out var home, out float hw, out float hc, out float ho))
        {
            // grows out of its own place: position, turn, size, bend and opacity all on one eased curve
            float e = Ease(_t);
            Matrix4x4.Decompose(home, out _, out var qa, out var ta);
            Matrix4x4.Decompose(v.Place, out _, out var qb, out var tb);
            v.World = Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(qa, qb, e)) * Matrix4x4.CreateTranslation(Vector3.Lerp(ta, tb, e));
            v.Width = hw + (v.Size - hw) * e;
            v.Curvature = DesktopLayout.Flat(hc + (v.Bend - hc) * e);
            v.Opacity = ho + (1 - ho) * e;
        }
        else
        {
            // fades in where it plays, settling from slightly smaller (the bend scales with it: same radius); Motion reduced: only fades
            float s = Motion.Full ? 0.9f + 0.1f * (1 - MathF.Pow(1 - _t, 3)) : 1;
            v.World = v.Place;
            v.Width = v.Size * s;
            v.Curvature = DesktopLayout.Flat(v.Bend * s);
            v.Opacity = Ease(_t);
        }
        _behind?.Mirror(v);
        if (_behind != null) _behind.Opacity = 1;     // the old one stays solid underneath until it is covered
    }

    public void Dispose()
    {
        _dim?.Dispose();
        _black?.Dispose();
        DisposeCyber();
        DisposeBackdrop();
        DisposeAmbilight();
        DisposeHorizon();
    }
}
