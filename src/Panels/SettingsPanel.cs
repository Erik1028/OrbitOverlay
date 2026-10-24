using System;
using System.Numerics;
using Valve.VR;

namespace Orbit;

// The settings as a floating Orbit panel in VR (instead of the SteamVR dashboard, which
// collides with the monitors). Opened from the watch, used with our laser, grabbed like any panel.
public sealed class SettingsPanel : IPointerTarget, IDisposable
{
    public const int TW = 1500, TH = 960;
    const float Scale = TW / SettingsView.W;

    readonly Painter _p;
    readonly Ui _ui;
    readonly ConfigStore _cfg;
    readonly SettingsView _view;
    readonly Action<SettingsView> _beforeDraw;
    readonly PointerState _ptr = new();
    public readonly Surface Surf;
    public VrOverlay Ov { get; private set; }
    VrSession _vr;
    bool _open, _dirty = true;
    float _vis;                 // 0..1 open animation
    double _nextCheck;          // when the page is next asked whether it shows anything new (SettingsView.LiveSig)
    double _wake = double.PositiveInfinity;    // a style's animation wants its next frame then (AnimWake.cs)
    int _liveSig;
    float _twoStartWidth;

    SettingsPanelConfig C => _cfg.Data.SettingsPanel;
    public bool IsOpen => _open;

    public SettingsPanel(Gpu g, Painter p, ConfigStore cfg, IOrbitHost host, Action<SettingsView> beforeDraw)
    {
        _p = p; _cfg = cfg; _beforeDraw = beforeDraw;
        _ui = new Ui(p);
        _view = new SettingsView(host) { OnClose = () => _open = false };
        Surf = new Surface(g, TW, TH);
    }

    public void CreateOverlay(VrSession vr)
    {
        _vr = vr;
        Ov = new VrOverlay("erik.orbit.settingspanel", "OrbitOverlay Settings");
        Ov.SetSortOrder(140);
        Ov.SetFlag(VROverlayFlags.IsPremultiplied, true);     // transparent rounded corners
        Ov.SetMouseScale(TW, TH);
        _vis = 0;
        _dirty = true;
    }

    public void DestroyOverlay() { _ptr.Leave(); Ov?.Dispose(); Ov = null; _vr = null; _open = false; _vis = 0; }

    public void Toggle()
    {
        if (_open) { _open = false; return; }
        Open();
    }

    // Opens in front of the user unless it is already somewhere close and in view.
    public void Open()
    {
        _open = true;
        _dirty = true;
        if (_vr == null || !_vr.HmdValid) return;
        bool keep = false;
        if (C.Place.M != null && Placement.TryWorld(C.Place, _vr, out var w))
        {
            var toPanel = w.Translation - _vr.Hmd.Translation;
            keep = toPanel.Length() < 1.6f &&
                   Vector3.Dot(Vector3.Normalize(toPanel), Vector3.Normalize(MathUtil.Forward(_vr.Hmd))) > 0.6f;
        }
        if (!keep)
        {
            // 0.7 m ahead, a little below the eyes, tilted back slightly
            var local = Matrix4x4.CreateRotationX(-12 * MathF.PI / 180) * Matrix4x4.CreateTranslation(0, -0.12f, -0.7f);
            C.Place.Attach = "world";
            C.Place.M = MathUtil.ToArray(local * MathUtil.GazeFrame(_vr.Hmd));
            _cfg.MarkDirty();
        }
    }

    public void Update(VrSession vr, double now, float dt)
    {
        if (Ov == null) return;
        float target = _open ? 1 : 0;
        if (_vis <= 0 && target > 0) _view.ShownAt = now;          // coming into view (a style's opening animation)
        _vis = Motion.Step(_vis, _open, dt, 0.18f);                      // Look > Motion off: at once
        if (_vis > 0) _lastSeen = now;
        if (_vis <= 0 || C.Place.M == null || !Placement.Apply(C.Place, vr, Ov))
        {
            if (_vis <= 0) _ptr.Leave();
            Ov.Hide();
            return;
        }
        float e = 1 - MathF.Pow(1 - _vis, 3);                 // ease-out cubic
        Ov.SetAlpha(e);
        Ov.SetWidth(C.Width * (0.92f + 0.08f * e));
        bool fading = _vis < 1 && Theme.RetroSkin.AnimatesFade;        // a style's opening / closing shows on every frame of the fade
        bool redraw = _dirty || _ui.Animating || fading || now >= _wake;
        // twice a second the page is asked whether anything it shows has changed by itself (SettingsView.LiveSig): drawn again and
        // handed to SteamVR only then - it was drawn and handed over on every beat whatever it showed (5.8 MB a time)
        if (!redraw && now >= _nextCheck)
        {
            _nextCheck = now + 0.5;
            redraw = _view.LiveSig() != _liveSig;
        }
        if (redraw)
        {
            // what the page shows, asked before it is drawn: a value that moves while it draws (the updater's thread finishing its
            // check) then differs on the next beat and is drawn again - asked after, the page kept the old one with the new one's sig
            long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int sig = _view.LiveSig();
            _beforeDraw?.Invoke(_view);
            Spikes.Piece("settings: previews", s0);
            AnimWake.Take();             // the previews' asks (the watch, the keyboard) are their own panels', not this one's
            _view.Vis = _vis;
            _view.Closing = !_open;
            DrawFrame(_ptr);
            s0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Ov.SetTexture(Surf.Tex);
            Spikes.Piece("settings: texture to SteamVR", s0);
            _liveSig = sig;
            // a press / release frame always gets one more frame: what the click changed (picker closed,
            // new theme, other card selected) was drawn before the click was handled
            _dirty = _ui.Interacting || _ui.Changed || _ui.Pressed || _ui.Released;
            _wake = AnimWake.Take();                                    // a style's animation asks for its next frame (AnimWake.cs)
            _nextCheck = now + 0.5;
        }
        Ov.Show();
    }

    float InputScale => Theme.Classic ? TW / SettingsView.W95W : Scale;

    void DrawFrame(PointerState ptr)
    {
        long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Theme.Classic)
        {
            // the 95 property sheet at 1995 size, every pixel 2 x 2 (its window frame is part of it)
            _view.Render95(_ui, ptr, Surf.Bitmap, TW / SettingsView.W95W, 0, 0, 0x00000000);
            Spikes.Piece("settings: page drawn (95)", s0);
            return;
        }
        _ui.Begin(ptr, Scale);
        _p.Begin(Surf.Bitmap, 0x00000000, Scale);
        DrawBody(_p, _ui, _view);
        Spikes.Piece("settings: page drawn", s0);
        s0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _p.End();
        Spikes.Piece("settings: D2D flush", s0);         // (10-24: 15-18 ms in VR while the page draws in 1-2 ms - the GPU's wait?)
        _ui.End();
    }

    // the modern panel's picture, between Begin (cleared see-through) and End - also --snapshot's settings_panel.png and the perf tool's
    public static void DrawBody(Painter p, Ui ui, SettingsView view)
    {
        var all = new System.Drawing.RectangleF(0, 0, SettingsView.W, SettingsView.H);
        if (Theme.Cyber) view.Draw(ui);          // Cyber: the view draws its own cut frame (SettingsCyber.cs)
        else
        {
            view.Draw(ui);
            p.CutCorners(all, 22);           // its rounded corners (no clip layer: Painter.CutCorners)
            p.Stroke(System.Drawing.RectangleF.Inflate(all, -1, -1), Theme.PanelLine, 2, 21);
        }
    }

    // Draws every tab once off-screen (fonts, layers, previews), so the first time the panel opens
    // in VR there is no hitch (it was 25-30 ms).
    public void Warm()
    {
        int keep = _view.Tab;
        var idle = new PointerState();
        for (int t = 0; t < SettingsView.TabCount; t++)
        {
            _view.Tab = t;
            _beforeDraw?.Invoke(_view);
            DrawFrame(idle);
        }
        _view.Tab = keep;
        _ui.ResetAnimations();
        _dirty = true;
        _lastSeen = Clock.Now;            // (what it made is kept a minute, as if it had been open: ReleaseIdle)
    }

    // ---- IPointerTarget ----
    public VrOverlay Overlay => Ov;
    public bool Interactive => Ov != null && Ov.Visible && _open && _vis > 0.8f;
    public bool AcceptsFrom(Hand h) => true;
    public Vector2 TextureSize => new(TW, TH);
    // One pointer state, so one hand drives it at a time: the hand that pointed first keeps it
    // until it leaves; the other hand takes over by clicking (unless the owner is mid-click/drag).
    Hand? _owner;

    public void PointerMove(Hand h, Vector2 px)
    {
        _owner ??= h;
        if (_owner != h) return;
        _ptr.Move(px.X, px.Y);
        if (_ui.NeedsRedrawForMove(_ptr, InputScale)) _dirty = true;
    }

    public void PointerLeave(Hand h)
    {
        if (_owner != h) return;
        _owner = null;
        _ptr.Leave();
        _dirty = true;
    }

    public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px)
    {
        if (b != PtrButton.Primary) return;
        if (down)
        {
            if (_owner != h && _ptr.Down) return;
            _owner = h;
            _ptr.Move(px.X, px.Y);
            _ptr.Press();
        }
        else
        {
            if (_owner != h) return;
            _ptr.Move(px.X, px.Y);
            _ptr.Release();
        }
        _dirty = true;
    }

    public void PointerScroll(Hand h, float notches, Vector2 px)
    {
        if (_owner != h) return;
        _ptr.Wheel += notches;
        _dirty = true;
    }

    public bool Grabbable => true;
    public Matrix4x4 WorldTransform =>
        _vr != null && Placement.TryWorld(C.Place, _vr, out var w) ? w : MathUtil.FromArray(C.Place.M);
    public void GrabBegin(bool detach) { }
    public void GrabMove(Matrix4x4 world) { if (_vr != null) { Placement.SetWorld(C.Place, _vr, world); _cfg.MarkDirty(); } }
    public void GrabStickX(float v, float dt) { C.Width = Math.Clamp(C.Width * (1 + v * 0.9f * dt), 0.4f, 2f); _cfg.MarkDirty(); }
    public void GrabTwoHandBegin() => _twoStartWidth = C.Width;
    public void GrabTwoHandScale(float r) { C.Width = Math.Clamp(_twoStartWidth * r, 0.4f, 2f); _cfg.MarkDirty(); }
    public bool GrabSnapPreview => false;
    public void GrabEnd() { }

    // its page's textures of another style (SettingsView.ReleaseStyle; OrbitApp.GpuSweep)
    public void ReleaseStyle() => _view.ReleaseStyle();

    // closed (faded out) a minute: its page's kept textures let go - the 1995 canvas, Cyber's console (5.5 MB; drawn again on every new
    // minute in VR anyway) - made again by its next draw (SettingsView.Dispose95; OrbitApp.GpuSweep, Erik 10-24)
    double _lastSeen;
    public void ReleaseIdle(double now) { if (!_open && _vis <= 0 && now - _lastSeen >= 60) _view.Dispose95(); }

    public void Dispose() { DestroyOverlay(); Surf.Dispose(); _view.Dispose95(); }
}
