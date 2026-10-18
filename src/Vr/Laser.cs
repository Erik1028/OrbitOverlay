using System;
using System.Collections.Generic;
using System.Numerics;
using Valve.VR;

namespace Orbit;

public enum PtrButton { Primary, Secondary }

// Something our own laser can point at, click and grab.
public interface IPointerTarget
{
    VrOverlay Overlay { get; }
    bool Interactive { get; }
    bool AcceptsFrom(Hand h);
    Vector2 TextureSize { get; }
    void PointerMove(Hand h, Vector2 px);
    void PointerLeave(Hand h);
    void PointerButton(Hand h, PtrButton b, bool down, Vector2 px);
    void PointerScroll(Hand h, float notches, Vector2 px);

    bool Grabbable { get; }
    Matrix4x4 WorldTransform { get; }
    void GrabBegin(bool detach);              // detach = B/Y held: take a monitor out of its row
    void GrabMove(Matrix4x4 world);
    void GrabStickX(float v, float dt);       // monitors: bend, others: resize
    void GrabTwoHandBegin();
    void GrabTwoHandScale(float ratio);       // hand distance / distance when the 2nd hand grabbed
    bool GrabSnapPreview { get; }             // releasing now would snap to a neighbour
    void GrabEnd();

    // > 0: this hand drives the Windows cursor here, draw it at this many metres per screen pixel
    float LaserCursorScale(Hand h) => 0;

    // a monitor / window with the mouse on: the laser may jump onto it where your eyes are (Laser.EyeWarp)
    bool WarpTarget => false;

    // A drag held on 'from' reaches this panel: true = it carries on here (the next monitor of the same Windows desktop),
    // false = 'from' keeps the beam on its edge (pointer capture)
    bool TakesDragFrom(IPointerTarget from, Hand h) => false;
    void AdoptDrag(IPointerTarget from, Hand h) { }
}

// One hand's pointer: ray test against our overlays, hover/click/scroll forwarding,
// grip = grab and move (stick up/down = distance, left/right = bend or size),
// grip with the other hand on the same panel = two-handed resize.
public sealed class Laser : IDisposable
{
    const int BeamW = 256, BeamH = 16;
    public float Thickness = 0.0035f;     // metres, from Look > Laser
    public bool AlwaysOn;                 // show a short beam even when not pointing at a panel

    readonly Hand _hand;
    readonly VrOverlay _beam, _dot, _cursor;
    readonly Surface _beamTex, _dotTex;
    public SystemCursor Cursor;           // shared, set by the app
    int _cursorVersion = -1;
    const float DotSize = 0.016f;
    readonly Painter _p;
    public Laser Other;
    IPointerTarget _hover, _press, _pressSecondary, _grab;
    Matrix4x4 _grabRel;
    Laser _partner;          // on the primary hand: the hand helping with a two-handed grab
    Laser _primary;          // on the helping hand: whose grab it joined
    float _twoStartDist;
    bool _snapShown;
    Vector2 _lastPx;
    int _themeVersion = -1;

    // smoothing + click stabilisation (pulling the trigger tilts the controller a little)
    readonly RayFilter _filter = new();
    public int Smoothing = 2;
    double _clock;
    IPointerTarget _freezeTarget;
    Vector2 _freezePx;
    Vector3 _freezeDir, _freezePoint;
    double _freezeUntil;
    const float FreezeAngle = 0.9f * MathF.PI / 180;      // move further than this to break out early

    // ---- the eye warp (Erik 10-18, eye tracking: "the cursor jumps to where you look, the laser only fine-tunes") ----
    // A press on a monitor while your eyes are on another spot of one - more than WarpAngle from the laser's point, as your eye sees
    // them - does not click: the beam turns to where you look and the next press clicks. The turn is kept in the controller's own frame
    // (the hand's moves stay one to one, the beam a straight line from it); it goes once the beam has been off every panel for a moment,
    // on a panel it does not jump to (the watch, the keyboard), on a grab, or with the setting off.
    public static bool EyeWarp;                      // General.EyeWarp with eye tracking on (OrbitApp, every frame)
    public const float WarpAngle = 7 * MathF.PI / 180;
    Vector3 _warpLocal;                              // the beam's direction in the controller's frame while warped
    bool _warped;
    double _offSince = -1;
    public bool Warped => _warped;

    // ---- a window pulled out of a monitor (OrbitApp.PopOut.cs, Erik 10-18) ----
    // While the trigger drags a window on a monitor and the beam leaves it into the air (PopAngle past its edge), the app makes the window a
    // panel of its own (null: it does not); this hand holds that panel until the trigger lets go
    public static Func<IPointerTarget, Hand, Vector3, Vector3, float, IPointerTarget> PopOut;
    public const float PopAngle = 9 * MathF.PI / 180;
    bool _grabByTrigger;

    public bool Active => _hover != null || _grab != null || _primary != null;
    public IPointerTarget Hovered => _hover;
    public Vector2 HoverPx => _lastPx;          // where on Hovered the beam is (texture pixels, top-left origin)

    public Laser(Gpu gpu, Painter p, Hand hand)
    {
        _hand = hand;
        _p = p;
        _beamTex = new Surface(gpu, BeamW, BeamH);
        _beam = new VrOverlay($"erik.orbit.laser.{hand.ToString().ToLowerInvariant()}", $"OrbitOverlay laser {hand}");
        _beam.SetFlag(VROverlayFlags.IsPremultiplied, true);
        _beam.SetSortOrder(200);
        _dotTex = new Surface(gpu, 64, 64);
        _dot = new VrOverlay($"erik.orbit.laserdot.{hand.ToString().ToLowerInvariant()}", $"OrbitOverlay laser dot {hand}");
        _dot.SetFlag(VROverlayFlags.IsPremultiplied, true);
        _dot.SetSortOrder(201);
        _dot.SetWidth(DotSize);
        _cursor = new VrOverlay($"erik.orbit.cursor.{hand.ToString().ToLowerInvariant()}", $"OrbitOverlay cursor {hand}");
        _cursor.SetFlag(VROverlayFlags.IsPremultiplied, true);
        _cursor.SetSortOrder(202);
        Redraw();
    }

    // soft beam in the laser colour, fading in from the controller
    void Redraw()
    {
        _themeVersion = Theme.Version;
        _p.Begin(_beamTex.Bitmap, 0x00000000);
        for (int x = 0; x < BeamW; x += 4)
        {
            float t = x / (float)BeamW;
            float a = Math.Min(1, t * 6) * 0.9f;
            _p.Fill(new System.Drawing.RectangleF(x, 3, 4, BeamH - 6), Theme.WithAlpha(Theme.Laser, a * 0.45f));
            _p.Fill(new System.Drawing.RectangleF(x, 6, 4, BeamH - 12), Theme.WithAlpha(Theme.Mix(Theme.Laser, 0xFFFFFFFF, 0.75f), a));
        }
        _p.End();
        _beam.SetTexture(_beamTex.Tex);
        // hit dot: bright core, soft halo
        _p.Begin(_dotTex.Bitmap, 0x00000000);
        _p.Circle(32, 32, 30, Theme.WithAlpha(Theme.Laser, 0.25f));
        _p.Circle(32, 32, 20, Theme.WithAlpha(Theme.Laser, 0.6f));
        _p.Circle(32, 32, 12, Theme.Mix(Theme.Laser, 0xFFFFFFFF, 0.7f));
        _p.End();
        _dot.SetTexture(_dotTex.Tex);
    }

    public void Update(VrSession vr, IReadOnlyList<IPointerTarget> targets, bool enabled, float dt, bool haptics)
    {
        if (_themeVersion != Theme.Version) Redraw();
        ref var hs = ref vr.Hands[(int)_hand];
        _clock += dt;
        if (!enabled || !hs.PoseValid)
        {
            Reset();
            _filter.Reset();
            return;
        }
        _rawO = hs.Aim.Translation;
        if (_warped && !EyeWarp) ClearWarp();
        _rawD = _warped ? Vector3.Normalize(Vector3.TransformNormal(_warpLocal, hs.Aim)) : Vector3.Normalize(MathUtil.Forward(hs.Aim));
        var (o, d) = _filter.Filter(_rawO, _rawD, dt, Smoothing);

        // Releases are handled before anything else: the grab / two-hand branches below return
        // early, and a lost release would leave a mouse button or a key held down in Windows.
        if (hs.ClickReleased && _press != null)
        {
            var pr = _press;
            _press = null;
            if (_freezeTarget == pr) { _freezeUntil = _clock + 0.12; _freezePx = _lastPx; _freezePoint = _lastPoint; _freezeDir = d; }
            pr.PointerButton(_hand, PtrButton.Primary, false, _lastPx);
        }
        if (hs.SecondaryReleased && _pressSecondary != null)
        {
            var pr = _pressSecondary;
            _pressSecondary = null;
            pr.PointerButton(_hand, PtrButton.Secondary, false, _lastPx);
        }

        // helping with the other hand's grab: only the beam, the primary does the work
        if (_primary != null)
        {
            if (!hs.Grab || _primary._grab == null || _primary._partner != this)
            {
                if (_primary._partner == this) _primary._partner = null;
                _primary = null;
            }
            else
            {
                ShowBeam(vr, o, d, Math.Max(0.05f, Vector3.Distance(o, _primary._grab.WorldTransform.Translation)));
                _cursor.Hide();
                return;
            }
        }

        if (_grab != null)
        {
            bool held = _grabByTrigger ? hs.Click : hs.Grab;            // (a window just pulled out: the trigger holds it, it is still fading in)
            if (!held || (!_grab.Interactive && !_grabByTrigger))
            {
                var g = _grab;
                _grab = null;
                _grabByTrigger = false;
                _partner = null;
                EndGrab(g);
                if (haptics && _snapShown) vr.Haptic(_hand, 0.6f, 0.03f);
                _snapShown = false;
            }
            else
            {
                if (_partner != null)
                {
                    float dist = Vector3.Distance(hs.Aim.Translation, vr.Hands[(int)_partner._hand].Aim.Translation);
                    if (_twoStartDist > 0.02f) _grab.GrabTwoHandScale(dist / _twoStartDist);
                }
                if (Math.Abs(hs.Stick.Y) > 0.2f)
                {
                    // push/pull along the hand's pointing axis (rel is expressed in hand space)
                    float z = _grabRel.M43 - hs.Stick.Y * 1.6f * dt;
                    _grabRel.M43 = Math.Clamp(z, -6f, 0.05f);
                }
                if (Math.Abs(hs.Stick.X) > 0.3f)
                    _grab.GrabStickX(hs.Stick.X, dt);
                var world = _grabRel * hs.Aim;
                _grab.GrabMove(world);
                bool snap = _grab.GrabSnapPreview;
                if (snap && !_snapShown && haptics) vr.Haptic(_hand, 0.25f);
                _snapShown = snap;
                if (snap) _grab.Overlay?.SetColor(0.78f, 1f, 0.84f);
                else _grab.Overlay?.SetColor(0.84f, 0.92f, 1f);
                ShowBeam(vr, o, d, Math.Max(0.05f, Vector3.Distance(o, world.Translation)));
                _cursor.Hide();
                return;
            }
        }

        IPointerTarget best = null;
        float bestDist = float.MaxValue;
        Vector2 bestUv = default;
        Vector3 bestPoint = default, bestNormal = default;
        for (int ti = 0; ti < targets.Count; ti++)
        {
            var t = targets[ti];
            if (t == null || !t.Interactive || !t.AcceptsFrom(_hand)) continue;
            if (Hit(vr, t.Overlay, o, d, out float dist, out Vector2 uv, out var pt, out var nrm) && dist < bestDist && dist < 8f)
            {
                best = t; bestDist = dist; bestUv = uv; bestPoint = pt; bestNormal = nrm;
            }
        }

        // Pointer capture, like Windows: while the trigger is held on a panel, that panel keeps the beam past its edge, the
        // point held on the edge. A window dragged to the top of a monitor reaches the very top (snap to full screen), and
        // the beam leaving the panel no longer lets the other hand take the mouse in the middle of the drag. Only another
        // monitor takes the drag over (a window dragged across screens): the button stays down in Windows, the cursor follows.
        if (_press != null && best != _press && best != null && best.TakesDragFrom(_press, _hand))
        {
            best.AdoptDrag(_press, _hand);
            if (_pressSecondary == _press) _pressSecondary = best;
            _press = best;
            _freezeUntil = 0;
        }
        else if (_press != null && best != _press && _press.Interactive && _press.Overlay != null
            && _press.Overlay.ProjectLocal(vr, o, d, out float cDist, out var cUv, out var cPoint, out var cNormal))
        {
            // a window dragged off the monitor into the air: out it comes as a panel of its own, in this hand until the trigger lets go
            if (best == null && PopOut != null && hs.Click
                && MathF.Acos(Math.Clamp(Vector3.Dot(d, Vector3.Normalize(cPoint - o)), -1f, 1f)) > PopAngle
                && PopOut(_press, _hand, o, d, cDist) is { } popped)
            {
                _press = null;
                _freezeUntil = 0;
                TriggerGrab(vr, popped, hs.Aim, o, d, haptics);
                return;
            }
            best = _press; bestDist = cDist; bestUv = cUv; bestPoint = cPoint; bestNormal = cNormal;
        }

        Spikes.Mark(Spikes.Rays);
        if (best != _hover)
        {
            _hover?.PointerLeave(_hand);
            _hover = best;
            if (best != null && haptics) vr.Haptic(_hand, 0.12f);
        }
        // the eye warp lets go: off every panel for a moment, or on one it does not jump to
        _offSince = _hover != null ? -1 : _offSince < 0 ? _clock : _offSince;
        if (_warped && (_hover != null ? !_hover.WarpTarget : _clock - _offSince > 0.6)) ClearWarp();

        if (_hover != null)
        {
            var px = bestUv * _hover.TextureSize;
            if (_freezeTarget == _hover && _clock < _freezeUntil)
            {
                if (MathF.Acos(Math.Clamp(Vector3.Dot(d, _freezeDir), -1f, 1f)) < FreezeAngle) { px = _freezePx; bestPoint = _freezePoint; }
                else _freezeUntil = 0;
            }
            _lastPx = px;
            _lastPoint = bestPoint;
            _hover.PointerMove(_hand, px);
            if (hs.ClickPressed && TryWarp(vr, targets, bestPoint, hs.Aim))
            {
                if (haptics) vr.Haptic(_hand, 0.3f, 0.012f);         // the beam turned to where you look: a light tick, no click
            }
            else if (hs.ClickPressed)
            {
                // hold the cursor still for a moment so the click lands where it was aimed
                _freezeTarget = _hover; _freezePx = px; _freezePoint = bestPoint; _freezeDir = d; _freezeUntil = _clock + 0.3;
                _press = _hover;
                _hover.PointerButton(_hand, PtrButton.Primary, true, px);
                if (haptics) vr.Haptic(_hand, 0.35f);
            }
            if (hs.SecondaryPressed)
            {
                _pressSecondary = _hover;
                _hover.PointerButton(_hand, PtrButton.Secondary, true, px);
                if (haptics) vr.Haptic(_hand, 0.35f);
            }
            if (hs.GrabPressed && !_hover.Grabbable && haptics)
                vr.Haptic(_hand, 0.2f, 0.008f);          // locked: a short tick, it stays where it is
            if (hs.GrabPressed && _hover.Grabbable)
            {
                if (Other != null && Other._grab == _hover && Other._partner == null)
                {
                    // second hand on the same panel: two-handed resize
                    _primary = Other;
                    Other._partner = this;
                    Other._twoStartDist = Vector3.Distance(hs.Aim.Translation, vr.Hands[(int)Other._hand].Aim.Translation);
                    _hover.GrabTwoHandBegin();
                    _hover.PointerLeave(_hand);
                    _hover = null;
                    if (haptics) vr.Haptic(_hand, 0.5f, 0.02f);
                    return;
                }
                if (Other == null || Other._grab != _hover)
                {
                    ClearWarp();
                    _grab = _hover;
                    _hover.PointerLeave(_hand);        // a grabbed monitor gives the mouse back
                    _hover = null;
                    _grab.GrabBegin(detach: hs.Secondary);
                    _grabRel = _grab.WorldTransform * MathUtil.Invert(hs.Aim);
                    if (haptics) vr.Haptic(_hand, 0.5f, 0.02f);
                    ShowBeam(vr, o, d, bestDist);
                    _cursor.Hide();
                    return;
                }
            }
            else if (Math.Abs(hs.Stick.Y) > 0.25f)
            {
                // speed grows with deflection, ~12 notches/s at full tilt
                float s = Math.Sign(hs.Stick.Y) * (Math.Abs(hs.Stick.Y) - 0.25f) / 0.75f;
                _hover.PointerScroll(_hand, s * 12f * dt, px);
            }
            Spikes.Mark(Spikes.Pointer);
            ShowBeam(vr, o, d, bestDist);
            float mpp = _hover?.LaserCursorScale(_hand) ?? 0;
            if (mpp > 0 && Cursor != null)
            {
                ShowCursor(bestPoint, bestNormal, d, _hover.WorldTransform, mpp);
                _dot.Hide();      // the cursor marks the spot
            }
            else _cursor.Hide();
        }
        else if (AlwaysOn)
        {
            ShowBeam(vr, o, d, 1.0f);
            _dot.Hide();
            _cursor.Hide();
        }
        else
            HideBeam();
    }

    Vector3 _lastPoint;

    // the press on a monitor: your eyes on another spot of a monitor / window, far enough from the laser's point - the beam turns there
    bool TryWarp(VrSession vr, IReadOnlyList<IPointerTarget> targets, Vector3 laserPoint, in Matrix4x4 aim)
    {
        if (!EyeWarp || !vr.EyesValid || _hover == null || !_hover.WarpTarget) return false;
        float nearest = float.MaxValue;
        Vector3 at = default;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.WarpTarget || !t.Interactive || t.Overlay == null) continue;
            if (t.Overlay.RaycastLocal(vr, vr.GazeOrigin, vr.Gaze, out float dist, out _, out var pt, out _) && dist < nearest && dist < 8f)
            {
                nearest = dist;
                at = pt;
            }
        }
        if (nearest == float.MaxValue || !WarpLocal(laserPoint, vr.GazeOrigin, at, aim, out var local)) return false;
        _warpLocal = local;
        _warped = true;
        _filter.Reset();
        return true;
    }

    // far enough apart as the eye sees them (WarpAngle): the direction from the controller to the spot looked at, in the controller's
    // own frame (GazeFocusTools' warp checks)
    internal static bool WarpLocal(Vector3 laserPoint, Vector3 eye, Vector3 lookPoint, in Matrix4x4 aim, out Vector3 local)
    {
        local = default;
        Vector3 toLaser = laserPoint - eye, toLook = lookPoint - eye;
        if (toLaser.LengthSquared() < 1e-6f || toLook.LengthSquared() < 1e-6f) return false;
        float ang = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(toLaser), Vector3.Normalize(toLook)), -1f, 1f));
        if (ang < WarpAngle) return false;
        var l = Vector3.TransformNormal(lookPoint - aim.Translation, MathUtil.Invert(aim));
        if (l.LengthSquared() < 1e-6f) return false;
        local = Vector3.Normalize(l);
        return true;
    }

    void ClearWarp()
    {
        if (!_warped) return;
        _warped = false;
        _filter.Reset();
    }

    // the window just pulled out: this hand holds it (the trigger, not the grip) where the beam is
    void TriggerGrab(VrSession vr, IPointerTarget t, in Matrix4x4 aim, Vector3 o, Vector3 d, bool haptics)
    {
        if (_hover != null) { _hover.PointerLeave(_hand); _hover = null; }
        ClearWarp();
        _grab = t;
        _grabByTrigger = true;
        _partner = null;
        t.GrabBegin(detach: false);
        _grabRel = t.WorldTransform * MathUtil.Invert(aim);
        if (haptics) vr.Haptic(_hand, 0.6f, 0.03f);
        ShowBeam(vr, o, d, Math.Max(0.05f, Vector3.Distance(o, t.WorldTransform.Translation)));
        _cursor.Hide();
    }

    static void EndGrab(IPointerTarget g)
    {
        try
        {
            g.Overlay?.SetColor(1, 1, 1);
            g.GrabEnd();
        }
        catch (Exception e) { Log.Warn("grab end: " + e.Message); }
    }

    Vector3 _rawO, _rawD;

    // The beam and the dot are parented to the controller (device-relative overlays), so the
    // compositor moves them with the newest controller pose - no lag behind the hand. Only the
    // cursor on the panel is smoothed; the beam follows the raw ray. o/d are ignored on purpose.
    void ShowBeam(VrSession vr, Vector3 o, Vector3 d, float len)
    {
        o = _rawO; d = _rawD;
        Vector3 mid = o + d * (len / 2);
        Vector3 toEye = vr.HmdValid ? vr.Hmd.Translation - mid : Vector3.UnitY;
        Vector3 z = toEye - Vector3.Dot(toEye, d) * d;
        if (z.LengthSquared() < 1e-8f) z = Vector3.UnitY - Vector3.Dot(Vector3.UnitY, d) * d;
        z = Vector3.Normalize(z);
        Vector3 y = Vector3.Cross(z, d);
        var beamWorld = MathUtil.Basis(d, y, z, mid);

        // the dot sits at the end, facing the eyes
        Vector3 tip = o + d * len;
        Vector3 dz = vr.HmdValid ? Vector3.Normalize(vr.Hmd.Translation - tip) : -d;
        Vector3 dx = Vector3.Cross(Vector3.UnitY, dz);
        dx = dx.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(dx);
        var dotWorld = MathUtil.Basis(dx, Vector3.Cross(dz, dx), dz, tip - d * 0.002f);

        uint dev = vr.HandIndex(_hand);
        if (vr.TryDeviceWorld(dev, out var devWorld))
        {
            var inv = MathUtil.Invert(devWorld);
            _beam.SetRelative(dev, beamWorld * inv);
            _dot.SetRelative(dev, dotWorld * inv);
        }
        else
        {
            _beam.SetAbsolute(beamWorld);
            _dot.SetAbsolute(dotWorld);
        }
        _beam.SetWidth(len);
        _beam.SetTexelAspect(len * BeamH / (BeamW * Thickness));
        _beam.Show();
        _dot.Show();
    }

    void HideBeam() { _beam.Hide(); _dot.Hide(); _cursor.Hide(); }

    // The Windows cursor drawn on the panel at the (smoothed) laser point, lying on the surface.
    void ShowCursor(Vector3 point, Vector3 normal, Vector3 rayDir, Matrix4x4 panel, float metersPerPixel)
    {
        if (_cursorVersion != Cursor.Version)
        {
            _cursorVersion = Cursor.Version;
            _cursor.SetTexture(Cursor.Surf.Tex);
        }
        if (normal.LengthSquared() < 1e-6f) normal = Vector3.Normalize(MathUtil.Back(panel));
        normal = Vector3.Normalize(normal);
        if (Vector3.Dot(normal, rayDir) > 0) normal = -normal;              // towards the viewer
        Vector3 up = MathUtil.Up(panel);
        up = Vector3.Normalize(up - Vector3.Dot(up, normal) * normal);
        Vector3 right = Vector3.Cross(up, normal);
        float w = SystemCursor.Size * metersPerPixel;
        float s = w / SystemCursor.Size;
        var hot = Cursor.Hotspot;
        // texture centre relative to the hotspot: +x right, +y down (= -up)
        Vector3 centre = point + right * ((SystemCursor.Size / 2f - hot.X) * s) - up * ((SystemCursor.Size / 2f - hot.Y) * s)
                         + normal * 0.0015f;
        _cursor.SetAbsolute(MathUtil.Basis(right, up, normal, centre));
        _cursor.SetWidth(w);
        _cursor.Show();
    }

    // Lets go of everything (pose lost, laser disabled, dashboard opened).
    // "steamvr": ask SteamVR (IPC per panel per hand per frame) | "local": LocalRaycast only |
    // "shadow": use SteamVR's answer, compute ours too and keep statistics of the difference
    public static string RaycastMode = "shadow";

    static bool Hit(VrSession vr, VrOverlay ov, Vector3 o, Vector3 d, out float dist, out Vector2 uv, out Vector3 pt, out Vector3 nrm)
    {
        if (RaycastMode == "local")
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool h = ov.RaycastLocal(vr, o, d, out dist, out uv, out pt, out nrm);
            RayStats.Local(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            return h;
        }
        long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
        bool hs = ov.Raycast(o, d, out dist, out uv, out pt, out nrm);
        long s1 = System.Diagnostics.Stopwatch.GetTimestamp();
        RayStats.Steam(s1 - s0);
        if (RaycastMode == "shadow")
        {
            bool hl = ov.RaycastLocal(vr, o, d, out float dl, out Vector2 uvl, out Vector3 pl, out Vector3 nl);
            RayStats.Local(System.Diagnostics.Stopwatch.GetTimestamp() - s1);
            RayStats.Add(ov.IsRelative, hs, hl, dist, uv, pt, nrm, dl, uvl, pl, nl);
        }
        return hs;
    }

    public void StopGrab()
    {
        if (_grab != null) Reset();
    }

    // a panel is going away: let go of it if this hand points at, presses or holds it
    public void Forget(IPointerTarget t)
    {
        if (t != null && (_hover == t || _press == t || _pressSecondary == t || _grab == t)) Reset();
    }

    public void Reset()
    {
        _hover?.PointerLeave(_hand);
        _press?.PointerButton(_hand, PtrButton.Primary, false, _lastPx);
        _pressSecondary?.PointerButton(_hand, PtrButton.Secondary, false, _lastPx);
        if (_grab != null) { var g = _grab; _grab = null; EndGrab(g); }
        if (_primary != null && _primary._partner == this) _primary._partner = null;
        _hover = _press = _pressSecondary = _grab = null;
        _partner = _primary = null;
        _snapShown = false;
        _grabByTrigger = false;
        _warped = false;
        HideBeam();
    }

    public void Dispose()
    {
        _beam.Dispose();
        _beamTex.Dispose();
        _dot.Dispose();
        _dotTex.Dispose();
        _cursor.Dispose();
    }
}
