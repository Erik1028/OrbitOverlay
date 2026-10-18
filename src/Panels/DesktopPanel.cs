using System;
using System.Numerics;
using Valve.VR;
using Windows.Graphics.DirectX.Direct3D11;

namespace Orbit;

// Shared mouse ownership: only one hand drives the real cursor at a time.
public sealed class MouseOwner
{
    public Hand? Hand;
    public IPointerTarget Panel;      // monitor or window panel
    public bool Held;                 // the owning hand holds a mouse button: nobody else takes the mouse until it lets go
    public double LastMove;
    public double LastClick = -10;    // a click from VR went into Windows (keyboard pop-up uses it)
    public Action Clicked;

    public void NoteClick() { LastClick = Clock.Now; Clicked?.Invoke(); }
}

// One monitor mirrored into VR. Pointing moves the real Windows cursor there,
// trigger = left click, B/Y = right click, stick = wheel, grip = grab and move.
public sealed class DesktopPanel : ITheaterScreen, IDisposable
{
    readonly Gpu _g;
    readonly ConfigStore _cfg;
    readonly MouseOwner _mouse;
    public readonly MonitorInfo Mon;
    public readonly DesktopConfig Cfg;
    MonitorCapture _cap;
    public VrOverlay Ov { get; private set; }
    VrSession _vr;
    double _nextFrame;
    float _wheelAcc;
    readonly bool[] _hovering = new bool[2];
    Vector2 _lastPx;
    public string Error;

    public DesktopLayout Layout;

    public DesktopPanel(Gpu g, ConfigStore cfg, MouseOwner mouse, MonitorInfo mon, DesktopConfig dc)
    {
        _g = g; _cfg = cfg; _mouse = mouse; Mon = mon; Cfg = dc;
    }

    public void CreateOverlay(VrSession vr, IDirect3DDevice winrtDevice, int n)
    {
        _vr = vr;
        Ov = new VrOverlay($"erik.orbit.desktop.{n}", $"OrbitOverlay Desktop {Mon.Name}");
        Ov.SetFlag(VROverlayFlags.IgnoreTextureAlpha, true);
        Ov.SetSortOrder(BaseSort);
        try { _cap = new MonitorCapture(_g, winrtDevice, Mon); }
        catch (Exception e) { Error = e.Message; Log.Warn($"capture {Mon}: {e}"); }
    }

    public void DestroyOverlay()
    {
        ReleaseButtons();
        _cap?.Dispose(); _cap = null;
        _hasTexture = false;
        _vis = 0;
        _cyFx?.Dispose(); _cyFx = null;
        Ov?.Dispose(); Ov = null;
        _vr = null;
    }

    const uint BaseSort = 100;

    public void Update(VrSession vr, double now, bool globallyVisible, int fps)
    {
        if (Ov == null) return;
        float dt = (float)Math.Clamp(now - _lastNow, 0, 0.1);
        _lastNow = now;
        var th = Theater;
        var fv = Focus;
        bool want = (globallyVisible || th != null) && !TheaterHidden && Cfg.Enabled && (Cfg.Place.M != null || th != null) && _cap != null && !_cap.Closed;
        if (want)
        {
            // the theater screen draws over the other panels; while switching, the new one over the old one
            Ov.SetSortOrder(th == null ? BaseSort : th.Behind ? 106u : 107u);
            if (th != null) Ov.SetAbsolute(th.World);
            else if (fv != null) Ov.SetAbsolute(fv.World);                   // its row's gaze focus draws it (Panels/GazeFocus.cs)
            else if (!Placement.Apply(Cfg.Place, vr, Ov)) want = false;     // attached to a device that is gone
        }
        // fade in once there is a picture, fade out when hidden
        _vis = Motion.Step(_vis, want && _hasTexture, dt, 0.2f);         // Look > Motion off: at once
        if (!want && Ov.Visible) ReleaseButtons();
        if (_vis <= 0) Ov.Hide();
        else
        {
            // fading out it keeps the size and bend it had: a screen switched away in theater mode fades out where
            // and as big as it played, not shrunk to its own size
            if (want)
            {
                Ov.SetWidth(th?.Width ?? fv?.Width ?? Cfg.Width);
                Ov.SetCurvature(th?.Curvature ?? fv?.Curvature ?? Cfg.Curvature);
                _opacity = th?.Opacity ?? Cfg.Opacity;
            }
            // the dim (eye tracking, VRChat > Eye tracking): looked at or pointed at - as it is; else a little darker
            bool looked = !DimUnlooked || th != null || _hovering[0] || _hovering[1]
                || LookedAt(vr, WorldTransform, th?.Width ?? fv?.Width ?? Cfg.Width, th?.Curvature ?? fv?.Curvature ?? Cfg.Curvature, TextureSize);
            _lookA = LookDim(_lookA, looked, dt);
            Ov.SetAlpha(_opacity * (1 - MathF.Pow(1 - _vis, 3)) * _lookA);
            Ov.Show();
        }
        if (Theme.Cyber || _cyFx != null) (_cyFx ??= new CyberScreenFx(_g, Ov.Key)).Update(Ov, _vis, want, th != null, TextureSize, now);   // Cyber's unfold and HUD frame
        _cap?.SetCursorCapture(!(want && _mouse.Panel == this && _mouse.Hand != null && Cfg.Mouse));
        // aside in theater mode it is still captured, slowly: switching to it shows what is on it now, not the
        // picture from when it was hidden (that jumped to the new one a moment later)
        bool warm = !want && TheaterHidden && _cap != null && !_cap.Closed;
        if (want && _staleTex && _cap != null && !_cap.Closed) Hand();       // shown again: the picture taken meanwhile, at once
        if (!(want || warm) || !CaptureDue(ref _nextFrame, now, want ? EffectiveFps(vr, fps) : 3)) return;
        if (_cap.Poll())
        {
            // hidden it is only kept fresh: SteamVR gets it once it shows (it copied every one of those 3 a second for nothing)
            if (want) Hand(); else _staleTex = true;
        }
    }

    bool _staleTex;

    void Hand()
    {
        Ov.SetMouseScale(_cap.Target.W, _cap.Target.H);
        Ov.SetTexture(_cap.Target.Tex);
        _hasTexture = true;
        _staleTex = false;
    }

    public bool SmartCapture = true;

    // The dim of what you are not looking at (Erik 10-18: eye tracking): OrbitApp sets DimUnlooked while it is on and the eyes are
    // tracked; a monitor your eyes are not on (nor a laser) eases to DimTo in ~0.15 s and back as soon as you look at it.
    public bool DimUnlooked;
    public const float DimTo = 0.6f;
    float _lookA = 1;

    public static bool LookedAt(VrSession vr, in Matrix4x4 world, float width, float curvature, System.Numerics.Vector2 tex) =>
        vr.EyesValid && LocalRaycast.Intersect(world, width, curvature, tex.X, tex.Y, vr.GazeOrigin, vr.Gaze, out _, out _, out _, out _);

    public static float LookDim(float a, bool looked, float dt)
    {
        float want = looked ? 1 : DimTo;
        if (!Motion.Any) return want;
        a += (want - a) * (1 - MathF.Exp(-dt * 14));
        return MathF.Abs(want - a) < 0.004f ? want : a;
    }

    // the capture ended (monitor dropped out for a moment, or a capture error): the app rebuilds it
    public bool CaptureDead => _cap != null && _cap.Closed;

    float EffectiveFps(VrSession vr, int fps) =>
        !SmartCapture ? fps
        : Theater is { } th ? SmartFps(vr, th.World, th.Width, _hovering[0] || _hovering[1], fps)
        : Focus is { } fv ? SmartFps(vr, fv.World, fv.Width, _hovering[0] || _hovering[1], fps)
        : SmartFps(vr, Cfg.Place, Cfg.Width, _hovering[0] || _hovering[1], fps);

    // Paces captures to an exact average rate on a loop that runs at the compositor's rate:
    // "next = now + 1/fps" skipped every other loop when the two were close (60 fps on an
    // 80-90 Hz loop came out at ~40). The next slot is kept on the grid instead (2 ms early is fine).
    public static bool CaptureDue(ref double next, double now, float fps)
    {
        double period = 1.0 / Math.Clamp(fps, 1, 240);
        if (next > now + period) next = now + period;      // the rate just went up (you turned to it)
        if (now + 0.002 < next) return false;
        next = Math.Max(next + period, now - period * 0.5);
        return true;
    }

    // Smart rate: the monitor you point at or look at runs at the headset's refresh rate (it is
    // limited by the monitor's own rate and by the loop), 15 fps at the edge of view, 4 behind you.
    public static float SmartFps(VrSession vr, PlacementConfig place, float width, bool hovered, int fps)
    {
        if (hovered || !vr.HmdValid) return Math.Max(fps, vr.DisplayHz);
        if (!Placement.TryWorld(place, vr, out var w)) return fps;
        return SmartFps(vr, w, width, false, fps);
    }

    public static float SmartFps(VrSession vr, in Matrix4x4 w, float width, bool hovered, int fps)
    {
        float focus = Math.Max(fps, vr.DisplayHz);
        if (hovered || !vr.HmdValid) return focus;
        var to = w.Translation - vr.Hmd.Translation;
        if (to.LengthSquared() < 1e-6f) return fps;
        float cos = Vector3.Dot(Vector3.Normalize(to), vr.Gaze);           // (your eyes' look when tracked, else the head's: VrSession.Gaze)
        // the panel's half-width widens the "looked at" cone for big / close monitors
        float half = MathF.Atan2(width / 2, to.Length());
        float ang = MathF.Acos(Math.Clamp(cos, -1f, 1f)) - half;
        if (ang < 30 * MathF.PI / 180) return focus;
        if (ang < 70 * MathF.PI / 180) return Math.Min(fps, 15);
        return Math.Min(fps, 4);
    }

    bool _hasTexture;
    float _vis, _opacity = 1;
    CyberScreenFx _cyFx;
    double _lastNow;

    // ---- IPointerTarget ----
    public VrOverlay Overlay => Ov;
    public bool Interactive => Ov != null && Ov.Visible && _vis > 0.8f;
    public bool AcceptsFrom(Hand h) => true;
    public Vector2 TextureSize => new(_cap?.Target?.W ?? Mon.W, _cap?.Target?.H ?? Mon.H);

    bool OwnsMouse(Hand h)
    {
        double now = Clock.Now;
        // a hand in the middle of a drag keeps the mouse, even standing still or past the edge (two beams on one screen)
        if (_mouse.Held && _mouse.Hand != h) return false;
        // take over when nobody owns it, the owner stopped pointing at a desktop, or went quiet for a moment
        if (_mouse.Hand == null || _mouse.Hand == h || _mouse.Panel == null || now - _mouse.LastMove > 0.4)
        {
            _mouse.Hand = h; _mouse.Panel = this; _mouse.LastMove = now;
            return true;
        }
        return false;
    }

    (int x, int y) ScreenPoint(Vector2 px)
    {
        var size = TextureSize;
        int x = Mon.X + (int)Math.Clamp(px.X * Mon.W / size.X, 0, Mon.W - 1);
        int y = Mon.Y + (int)Math.Clamp(px.Y * Mon.H / size.Y, 0, Mon.H - 1);
        return (x, y);
    }

    public void PointerMove(Hand h, Vector2 px)
    {
        _hovering[(int)h] = true;
        if (!Cfg.Mouse || !OwnsMouse(h)) return;
        if (Vector2.DistanceSquared(px, _lastPx) < 0.25f) return;
        _lastPx = px;
        var (x, y) = ScreenPoint(px);
        Native.SetCursorPos(x, y);
    }

    public void PointerLeave(Hand h)
    {
        _hovering[(int)h] = false;
        if (_mouse.Hand == h && _mouse.Panel == this && !_mouse.Held) { _mouse.Hand = null; _mouse.Panel = null; }
    }

    bool _leftDown, _rightDown;

    public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px)
    {
        if (!Cfg.Mouse && down) return;      // ups still go out, or a button would stay held
        if (down && _mouse.Held && _mouse.Hand != h) return;      // the other hand is in the middle of a drag
        if (down)
        {
            _mouse.Hand = h; _mouse.Panel = this; _mouse.LastMove = Clock.Now;
            _mouse.NoteClick();
            var (x, y) = ScreenPoint(px);
            Native.SetCursorPos(x, y);
        }
        if (b == PtrButton.Primary && down != _leftDown)
        {
            _leftDown = down;
            Native.SendMouse(down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP);
        }
        else if (b == PtrButton.Secondary && down != _rightDown)
        {
            _rightDown = down;
            Native.SendMouse(down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP);
        }
        if (_mouse.Panel == this) _mouse.Held = _leftDown || _rightDown;
    }

    // a window dragged from one monitor onto the next: this panel carries the held button on (no up / down goes to
    // Windows, so the drag continues) and lets it go when the trigger is released
    public bool TakesDragFrom(IPointerTarget from, Hand h) =>
        from is DesktopPanel o && o != this && Cfg.Mouse && o.Cfg.Mouse && (o._leftDown || o._rightDown) && _mouse.Hand == h && _mouse.Panel == o;

    public void AdoptDrag(IPointerTarget from, Hand h)
    {
        var o = (DesktopPanel)from;
        _leftDown = o._leftDown; _rightDown = o._rightDown;
        o._leftDown = o._rightDown = false;
        o._hovering[(int)h] = false;
        _mouse.Hand = h; _mouse.Panel = this; _mouse.LastMove = Clock.Now;
        _mouse.Held = _leftDown || _rightDown;
        _lastPx = new Vector2(-100, -100);        // the next move always places the cursor here
    }

    void ReleaseButtons()
    {
        bool had = _leftDown || _rightDown;
        if (_leftDown) { _leftDown = false; Native.SendMouse(Native.MOUSEEVENTF_LEFTUP); }
        if (_rightDown) { _rightDown = false; Native.SendMouse(Native.MOUSEEVENTF_RIGHTUP); }
        if (had && _mouse.Panel == this) _mouse.Held = false;
    }

    public void PointerScroll(Hand h, float notches, Vector2 px)
    {
        if (!Cfg.Mouse || !OwnsMouse(h)) return;
        _wheelAcc += notches * 120;
        int delta = (int)_wheelAcc;
        if (delta == 0) return;
        _wheelAcc -= delta;
        Native.SendMouse(Native.MOUSEEVENTF_WHEEL, delta);
    }

    public bool Grabbable => true;

    bool DrivesCursor(Hand h) => Cfg.Mouse && _mouse.Hand == h && _mouse.Panel == this;
    public float LaserCursorScale(Hand h) => DrivesCursor(h) ? (Theater?.Width ?? Focus?.Width ?? Cfg.Width) / Math.Max(1, Mon.W) : 0;

    public Matrix4x4 WorldTransform =>
        Theater is { } th ? th.World
        // a grab works on the saved layout: the hand's offset is taken from the saved place (the laser asks right after GrabBegin);
        // GazeFocus eases the drawn row back to the saved sizes meanwhile
        : Focus is { } fv && !Grabbing ? fv.World
        : _vr != null && Placement.TryWorld(Cfg.Place, _vr, out var w) ? w : MathUtil.FromArray(Cfg.Place.M);

    // ---- gaze focus (Panels/GazeFocus.cs): the row's draw override while it is on; like Theater, nothing here is saved ----
    public FocusView Focus { get; set; }
    public bool Grabbing { get; private set; }        // between GrabBegin and GrabEnd of a grab that edits the saved layout (not the theater screen's)

    // ---- theater mode (Panels/Theater.cs): the big screen is an override, nothing here is saved ----
    public TheaterView Theater { get; set; }
    public bool TheaterHidden { get; set; }
    public string TheaterLabel => string.IsNullOrWhiteSpace(Mon.Name) ? Mon.Device.TrimStart('\\', '.') : Mon.Name;
    public bool Playable => Ov != null && _cap != null && !_cap.Closed && Cfg.Enabled;
    public bool HomeInWorld => Placement.IsWorld(Cfg.Place);
    public float TheaterFade => _vis;
    public void TheaterVanish() => _vis = 0;
    public Surface TheaterPicture(out long frame) { frame = _cap?.Frames ?? 0; return _hasTexture && _cap is { Closed: false } ? _cap.Target : null; }
    public string TheaterState =>
        $"overlay {(Ov == null ? "none" : Ov.Visible ? "shown" : "hidden")}, fade {_vis:0.00}, picture {(_hasTexture ? "yes" : "no")}, " +
        $"capture {(_cap == null ? "none" : _cap.Closed ? "closed" : "live")}";

    public bool Home(out Matrix4x4 world, out float width, out float curvature, out float opacity)
    {
        width = Cfg.Width; curvature = Cfg.Curvature; opacity = Cfg.Opacity;
        world = default;
        if (_vr == null || Cfg.Place.M == null) return false;
        // its row's gaze focus draws it smaller / elsewhere: the big screen grows out of that and goes back into it (no jump)
        if (Focus is { } fv) { world = fv.World; width = fv.Width; curvature = fv.Curvature; return true; }
        return Placement.TryWorld(Cfg.Place, _vr, out world);
    }

    // the saved place alone (not the focus's drawn one, which follows a Recenter only later in the frame): where the big screen lines up
    public bool SavedHome(out Matrix4x4 world)
    {
        world = default;
        return _vr != null && Cfg.Place.M != null && Placement.TryWorld(Cfg.Place, _vr, out world);
    }

    DesktopLayout.Snap? _snap;
    float[] _twoStartWidths;
    System.Collections.Generic.List<DesktopPanel> _twoRow;
    TheaterView _grabView;      // the grab started on the theater screen; theater starting / ending mid-grab drops the rest of it

    bool GrabStale => _grabView != Theater;

    public void GrabBegin(bool detach)
    {
        _snap = null;
        _grabView = Theater;
        Grabbing = Theater == null;        // the big screen's grab moves the TheaterView, not the layout: the rows stay as they are
        if (Theater != null) return;
        if (detach && Cfg.Group != null) Layout?.Detach(this);
    }

    public void GrabMove(Matrix4x4 world)
    {
        if (_vr == null || GrabStale) return;
        if (Theater is { } th) { th.Move(world); return; }
        Placement.SetWorld(Cfg.Place, _vr, world);
        if (Cfg.Group != null) Layout?.Reflow(this);       // the whole row follows
        _snap = Layout?.FindSnap(this, world);
        _cfg.MarkDirty();
    }

    // stick left/right while holding: bend (the whole row shares the new radius)
    public void GrabStickX(float v, float dt)
    {
        if (GrabStale) return;
        if (Theater is { } th) { th.Resize(v, dt); return; }       // it bends around you by itself: the stick sizes it
        Cfg.Curvature = DesktopLayout.Flat(Math.Clamp(Cfg.Curvature + v * dt * 0.25f, 0f, 1f));
        if (Cfg.Group != null) Layout?.Reflow(this);
        _cfg.MarkDirty();
    }

    public void GrabTwoHandBegin()
    {
        if (Theater is { } th) { th.ScaleBegin(); return; }
        _twoRow = Cfg.Group != null && Layout != null ? Layout.Members(Cfg.Group) : new() { this };
        if (!_twoRow.Contains(this)) _twoRow.Add(this);
        _twoStartWidths = _twoRow.ConvertAll(p => p.Cfg.Width).ToArray();
    }

    // uniform scale: widths grow, curvature stays, so the row keeps its shape
    public void GrabTwoHandScale(float ratio)
    {
        if (GrabStale) return;
        if (Theater is { } th) { th.Scale(ratio); return; }
        if (_twoRow == null) return;
        for (int i = 0; i < _twoRow.Count; i++)
            _twoRow[i].Cfg.Width = Math.Clamp(_twoStartWidths[i] * ratio, 0.25f, 4f);
        if (Cfg.Group != null) Layout?.Reflow(this);
        _cfg.MarkDirty();
    }

    public bool GrabSnapPreview => _snap != null;

    public void GrabEnd()
    {
        bool layout = _grabView == null && Theater == null;
        _grabView = null;
        Grabbing = false;
        if (layout && _snap is { } s && Layout != null) Layout.Join(this, s);
        _snap = null;
        _twoRow = null;
        if (layout) _cfg.MarkDirty();
    }

    // used by --snapshot
    public MonitorCapture StartCaptureOnly(IDirect3DDevice dev) => _cap = new MonitorCapture(_g, dev, Mon);
    public void AttachSession(VrSession vr) => _vr = vr;      // (GazeFocusTest: a grab without an overlay)

    public void Dispose() => DestroyOverlay();
}
