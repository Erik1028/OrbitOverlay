using System;
using System.Numerics;
using Valve.VR;
using Windows.Graphics.DirectX.Direct3D11;

namespace Orbit;

// One application window mirrored into VR. Works like a monitor panel (pointing moves the real
// cursor, trigger = click, B/Y = right click, stick = wheel, grip = move), and before a click the
// window is brought to the front so the click can't land in whatever covers it on the real desktop.
// The app's window scan binds it to a live window (WindowList.Find) and re-binds after a restart.
public sealed class WindowPanel : ITheaterScreen, IDisposable
{
    readonly Gpu _g;
    readonly ConfigStore _cfg;
    readonly MouseOwner _mouse;
    public readonly WindowConfig Cfg;
    public IntPtr Hwnd { get; private set; }
    MonitorCapture _cap;
    public VrOverlay Ov { get; private set; }
    VrSession _vr;
    IDirect3DDevice _dev;
    double _nextFrame, _lastNow;
    float _wheelAcc, _vis, _opacity = 1;
    CyberScreenFx _cyFx;
    bool _hasTexture;
    readonly bool[] _hovering = new bool[2];
    public bool DimUnlooked;                 // (eye tracking: OrbitApp sets it; DesktopPanel.LookDim)
    float _lookA = 1;
    double _lookedAt = -100;
    Vector2 _lastPx;
    public string Error;

    public WindowPanel(Gpu g, ConfigStore cfg, MouseOwner mouse, WindowConfig wc)
    {
        _g = g; _cfg = cfg; _mouse = mouse; Cfg = wc;
    }

    // the window is gone (closed) or its capture ended: the scan looks for it again
    public bool Lost => Hwnd != IntPtr.Zero && (!WindowList.IsWindow(Hwnd) || _cap is { Closed: true });

    public string Status
    {
        get
        {
            if (Hwnd == IntPtr.Zero) return "waiting for the window to open";
            if (Error != null) return "can't capture it: " + Error;
            if (WindowList.IsIconic(Hwnd)) return _hasTexture ? "minimised - click it in VR to restore" : "minimised - restore it to see it";
            if (_cap == null) return "starts when SteamVR runs";
            return _hasTexture ? $"live · {_cap.Target.W}×{_cap.Target.H}" : "waiting for the first frame";
        }
    }

    public void CreateOverlay(VrSession vr, IDirect3DDevice dev)
    {
        _vr = vr;
        _dev = dev;
        Ov = new VrOverlay($"erik.orbit.window.{Cfg.Id}", "OrbitOverlay Window");
        Ov.SetFlag(VROverlayFlags.IgnoreTextureAlpha, true);
        Ov.SetSortOrder(BaseSort);
        if (Hwnd != IntPtr.Zero) StartCapture();
    }

    public void DestroyOverlay()
    {
        ReleaseButtons();
        StopCapture();
        _vis = 0;
        _cyFx?.Dispose(); _cyFx = null;
        Ov?.Dispose(); Ov = null;
        _vr = null;
    }

    // Cyber's HUD frame unused a minute: its texture let go (CyberScreenFx.ReleaseIdle; OrbitApp.GpuSweep)
    public void ReleaseIdleFx(double now) => _cyFx?.ReleaseIdle(now);

    public void Attach(WindowInfo w)
    {
        if (w.Hwnd == Hwnd && (_cap != null || Error != null)) return;    // same window: no retry loop on errors
        ReleaseButtons();
        StopCapture();
        Hwnd = w.Hwnd;
        Error = null;
        if (!string.IsNullOrEmpty(w.Title) && w.Title != Cfg.Title) { Cfg.Title = w.Title; _cfg.MarkDirty(); }
        if (Ov != null) StartCapture();
    }

    public void Detach()
    {
        ReleaseButtons();
        StopCapture();
        Hwnd = IntPtr.Zero;
        Error = null;
    }

    // keeps the stored title current (it is the tie-break when finding the window after a restart)
    public void RefreshTitle()
    {
        if (Hwnd == IntPtr.Zero) return;
        string t = WindowList.TitleOf(Hwnd);
        if (!string.IsNullOrEmpty(t) && t != Cfg.Title) { Cfg.Title = t; _cfg.MarkSoftDirty(); }
    }

    void StartCapture()
    {
        if (_dev == null) return;
        try { _cap = MonitorCapture.ForWindow(_g, _dev, Hwnd); }
        catch (Exception e) { Error = e.Message; _cap = null; Log.Warn($"window capture '{Cfg.Title}': {e.Message}"); }
        _hasTexture = false;
        _nextFrame = 0;
    }

    void StopCapture()
    {
        _cap?.Dispose();
        _cap = null;
        _hasTexture = false;
    }

    const uint BaseSort = 105;      // in front of monitors where they overlap

    public void Update(VrSession vr, double now, bool globallyVisible, int fps, bool smart)
    {
        if (Ov == null) return;
        float dt = (float)Math.Clamp(now - _lastNow, 0, 0.1);
        _lastNow = now;
        var th = Theater;
        bool want = (globallyVisible || th != null) && !TheaterHidden && Cfg.Enabled && (Cfg.Place.M != null || th != null) && _cap != null && !_cap.Closed;
        if (want)
        {
            // the theater screen draws over the other panels; while switching, the new one over the old one
            Ov.SetSortOrder(th == null ? BaseSort : th.Behind ? 106u : 107u);
            if (th != null) Ov.SetAbsolute(th.World);
            else if (!Placement.Apply(Cfg.Place, vr, Ov)) want = false;
        }
        _vis = Motion.Step(_vis, want && _hasTexture, dt, 0.2f);         // Look > Motion off: at once
        if (!want && Ov.Visible) ReleaseButtons();
        if (_vis <= 0) Ov.Hide();
        else
        {
            // fading out it keeps the size and bend it had: a screen switched away in theater mode fades out where
            // and as big as it played, not shrunk to its own size
            if (want)
            {
                Ov.SetWidth(th?.Width ?? Cfg.Width);
                Ov.SetCurvature(th?.Curvature ?? Cfg.Curvature);
                _opacity = th?.Opacity ?? Cfg.Opacity;
            }
            // the dim of what you are not looking at (eye tracking: DesktopPanel.LookDim)
            bool looked = !DimUnlooked || th != null || _hovering[0] || _hovering[1]
                || DesktopPanel.LookedAt(vr, WorldTransform, th?.Width ?? Cfg.Width, th?.Curvature ?? Cfg.Curvature, TextureSize);
            _lookA = DesktopPanel.LookDim(_lookA, ref _lookedAt, looked, now, dt);
            Ov.SetAlpha(_opacity * (1 - MathF.Pow(1 - _vis, 3)) * _lookA);
            Ov.Show();
        }
        if (Theme.Cyber || _cyFx != null) (_cyFx ??= new CyberScreenFx(_g, Ov.Key)).Update(Ov, _vis, want, th != null, TextureSize, now);   // Cyber's unfold and HUD frame
        if (_pending is { } pb && now >= _pendingAt) { _pending = null; Press(pb, true); }
        _cap?.SetCursorCapture(!(want && _mouse.Panel == this && _mouse.Hand != null && Cfg.Mouse));
        // aside in theater mode it is still captured, slowly: switching to it shows what is on it now
        bool warm = !want && TheaterHidden && _cap != null && !_cap.Closed;
        if (want && _staleTex && _cap != null && !_cap.Closed) Hand();       // shown again: the picture taken meanwhile, at once
        if (!want && !warm) return;
        bool hovered = _hovering[0] || _hovering[1];
        float rate = !want ? 3 : !smart ? fps
            : th != null ? DesktopPanel.SmartFps(vr, th.World, th.Width, hovered, fps)
            : DesktopPanel.SmartFps(vr, Cfg.Place, Cfg.Width, hovered, fps);
        if (!DesktopPanel.CaptureDue(ref _nextFrame, now, rate)) return;
        if (_cap.Poll())
        {
            // hidden it is only kept fresh: SteamVR gets it once it shows
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

    // ---- IPointerTarget ----
    public VrOverlay Overlay => Ov;
    public bool Interactive => Ov != null && Ov.Visible && _vis > 0.8f && Hwnd != IntPtr.Zero;
    public bool AcceptsFrom(Hand h) => true;
    public Vector2 TextureSize => new(_cap?.Target?.W ?? 1, _cap?.Target?.H ?? 1);

    bool OwnsMouse(Hand h)
    {
        double now = Clock.Now;
        if (_mouse.Held && _mouse.Hand != h) return false;     // the other hand is in the middle of a drag
        if (_mouse.Hand == null || _mouse.Hand == h || _mouse.Panel == null || now - _mouse.LastMove > 0.4)
        {
            _mouse.Hand = h; _mouse.Panel = this; _mouse.LastMove = now;
            return true;
        }
        return false;
    }

    // texture pixel -> screen pixel: the capture is the window's visible frame, wherever it is now
    bool ScreenPoint(Vector2 px, out int x, out int y)
    {
        x = y = 0;
        if (!WindowList.Bounds(Hwnd, out var r)) return false;
        var size = TextureSize;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        x = r.Left + (int)Math.Clamp(px.X * w / size.X, 0, w - 1);
        y = r.Top + (int)Math.Clamp(px.Y * h / size.Y, 0, h - 1);
        return true;
    }

    public void PointerMove(Hand h, Vector2 px)
    {
        _hovering[(int)h] = true;
        if (!Cfg.Mouse || Hwnd == IntPtr.Zero || !OwnsMouse(h)) return;
        if (Vector2.DistanceSquared(px, _lastPx) < 0.25f) return;
        _lastPx = px;
        if (ScreenPoint(px, out int x, out int y)) Native.SetCursorPos(x, y);
    }

    public void PointerLeave(Hand h)
    {
        _hovering[(int)h] = false;
        if (_mouse.Hand == h && _mouse.Panel == this && !_mouse.Held) { _mouse.Hand = null; _mouse.Panel = null; }
    }

    bool _leftDown, _rightDown;
    PtrButton? _pending;      // press held back until the window has come to the front
    double _pendingAt;

    public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px)
    {
        if (!Cfg.Mouse && down) return;      // ups still go out, or a button would stay held
        if (down && _mouse.Held && _mouse.Hand != h) return;      // the other hand is in the middle of a drag
        if (Hwnd == IntPtr.Zero) { ReleaseButtons(); return; }
        if (down)
        {
            _mouse.Hand = h; _mouse.Panel = this; _mouse.LastMove = Clock.Now;
            _mouse.NoteClick();
            if (WindowList.IsIconic(Hwnd)) { WindowList.BringToFront(Hwnd); return; }   // this click only restores it
            if (ScreenPoint(px, out int x, out int y)) Native.SetCursorPos(x, y);
            if (!WindowList.IsForeground(Hwnd))
            {
                WindowList.BringToFront(Hwnd);
                _pending = b;
                _pendingAt = Clock.Now + 0.06;
                return;
            }
        }
        else if (_pending == b)
        {
            // released before the held-back press went out: still a click
            _pending = null;
            Press(b, true);
        }
        Press(b, down);
    }

    void Press(PtrButton b, bool down)
    {
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
        if (_mouse.Panel == this) _mouse.Held = _leftDown || _rightDown || _pending != null;
    }

    void ReleaseButtons()
    {
        bool had = _leftDown || _rightDown || _pending != null;
        _pending = null;
        if (_leftDown) { _leftDown = false; Native.SendMouse(Native.MOUSEEVENTF_LEFTUP); }
        if (_rightDown) { _rightDown = false; Native.SendMouse(Native.MOUSEEVENTF_RIGHTUP); }
        if (had && _mouse.Panel == this) _mouse.Held = false;
    }

    public void PointerScroll(Hand h, float notches, Vector2 px)
    {
        if (!Cfg.Mouse || Hwnd == IntPtr.Zero || !OwnsMouse(h)) return;
        _wheelAcc += notches * 120;
        int delta = (int)_wheelAcc;
        if (delta == 0) return;
        _wheelAcc -= delta;
        // the wheel goes to the window under the cursor: make sure that is this one
        if (!WindowList.IsForeground(Hwnd)) WindowList.BringToFront(Hwnd);
        Native.SendMouse(Native.MOUSEEVENTF_WHEEL, delta);
    }

    bool DrivesCursor(Hand h) => Cfg.Mouse && _mouse.Hand == h && _mouse.Panel == this;
    public float LaserCursorScale(Hand h) => DrivesCursor(h) ? (Theater?.Width ?? Cfg.Width) / Math.Max(1, TextureSize.X) : 0;
    public bool WarpTarget => Cfg.Mouse && Hwnd != IntPtr.Zero;

    // ---- grab: move, stick = bend, two hands = size ----
    public bool Grabbable => true;

    public Matrix4x4 WorldTransform =>
        Theater is { } th ? th.World
        : _vr != null && Placement.TryWorld(Cfg.Place, _vr, out var w) ? w : MathUtil.FromArray(Cfg.Place.M);

    // ---- theater mode (Panels/Theater.cs): the big screen is an override, nothing here is saved ----
    public TheaterView Theater { get; set; }
    public bool TheaterHidden { get; set; }
    public string TheaterLabel => string.IsNullOrWhiteSpace(Cfg.Title) ? "Window" : Cfg.Title;
    public bool Playable => Ov != null && _cap != null && !_cap.Closed && Cfg.Enabled && Hwnd != IntPtr.Zero;
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
        return _vr != null && Cfg.Place.M != null && Placement.TryWorld(Cfg.Place, _vr, out world);
    }

    float _twoStartWidth;
    TheaterView _grabView;      // the grab started on the theater screen; theater starting / ending mid-grab drops the rest of it

    bool GrabStale => _grabView != Theater;

    public void GrabBegin(bool detach) => _grabView = Theater;

    public void GrabMove(Matrix4x4 world)
    {
        if (_vr == null || GrabStale) return;
        if (Theater is { } th) { th.Move(world); return; }
        Placement.SetWorld(Cfg.Place, _vr, world);
        _cfg.MarkDirty();
    }

    public void GrabStickX(float v, float dt)
    {
        if (GrabStale) return;
        if (Theater is { } th) { th.Resize(v, dt); return; }
        Cfg.Curvature = DesktopLayout.Flat(Math.Clamp(Cfg.Curvature + v * dt * 0.25f, 0f, 0.5f));
        _cfg.MarkDirty();
    }

    public void GrabTwoHandBegin()
    {
        if (Theater is { } th) { th.ScaleBegin(); return; }
        _twoStartWidth = Cfg.Width;
    }

    public void GrabTwoHandScale(float ratio)
    {
        if (GrabStale) return;
        if (Theater is { } th) { th.Scale(ratio); return; }
        Cfg.Width = Math.Clamp(_twoStartWidth * ratio, 0.15f, 3f);
        _cfg.MarkDirty();
    }

    public bool GrabSnapPreview => false;

    public void GrabEnd()
    {
        bool layout = _grabView == null && Theater == null;
        _grabView = null;
        if (layout) _cfg.MarkDirty();
    }

    public void Dispose() => DestroyOverlay();
}
