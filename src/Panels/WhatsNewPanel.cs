using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Valve.VR;

namespace Orbit;

// "What's new" in VR: after an update it appears once in front of you with what changed since your old version
// (Ui/WhatsNewView.cs draws it), until OK. Grab it to move it, the stick scrolls. Its place is not saved: it is a
// one-off.
public sealed class WhatsNewPanel : IPointerTarget, IDisposable
{
    public const int TW = 760, TH = 540, W95 = TW / 2, H95 = TH / 2;

    readonly Painter _p;
    readonly Ui _ui;
    readonly PointerState _ptr = new();
    public readonly Surface Surf;
    Surface _canvas95;
    public VrOverlay Ov { get; private set; }
    VrSession _vr;
    float _vis, _scroll, _width = 0.75f, _twoStartWidth;
    bool _dirty = true, _placed, _cyClosing;
    double _wake = double.PositiveInfinity;         // a style's animation wants its next frame then (AnimWake.cs)
    int _look = -1;
    Hand? _owner;
    Matrix4x4 _world = Matrix4x4.Identity;
    IReadOnlyList<WhatsNew.Entry> _list = Array.Empty<WhatsNew.Entry>();
    string _sub = "";

    public Action OnOk;

    public WhatsNewPanel(Gpu g, Painter p)
    {
        _p = p;
        _ui = new Ui(p);
        Surf = new Surface(g, TW, TH);
    }

    public void CreateOverlay(VrSession vr)
    {
        _vr = vr;
        Ov = new VrOverlay("erik.orbit.whatsnew", "OrbitOverlay What's new");
        Ov.SetSortOrder(138);
        Ov.SetFlag(VROverlayFlags.IsPremultiplied, true);
        Ov.SetMouseScale(TW, TH);
        _vis = 0;
        _placed = false;
        _dirty = true;
    }

    public void DestroyOverlay() { _ptr.Leave(); Ov?.Dispose(); Ov = null; _vr = null; _vis = 0; }

    // 0.8 m ahead, a little below the eyes, tilted back slightly. It appears by itself (not from the watch), so where
    // you look at that moment is where you are facing.
    void BringHere()
    {
        if (_vr == null || !_vr.HmdValid) return;
        _world = Matrix4x4.CreateRotationX(-8 * MathF.PI / 180) * Matrix4x4.CreateTranslation(0, -0.06f, -0.8f) * MathUtil.GazeFrame(_vr.Hmd);
        _placed = true;
    }

    // list: what to show (empty = nothing pending); show: VR is up and the headset is on
    public void Update(VrSession vr, float dt, IReadOnlyList<WhatsNew.Entry> list, string sub, bool show)
    {
        if (Ov == null) return;
        bool want = show && list != null && list.Count > 0;
        if (want && (!ReferenceEquals(list, _list) || sub != _sub)) { _list = list; _sub = sub; _scroll = 0; _dirty = true; }
        if (_look != Theme.Version) { _look = Theme.Version; _dirty = true; }      // a new look (colours, font): drawn again
        if (want && !_placed) BringHere();
        _vis = Motion.Step(_vis, want, dt, 0.22f);                       // Look > Motion off: at once
        if (_vis <= 0 || !_placed)
        {
            if (_vis <= 0) { _ptr.Leave(); if (!want) _placed = false; }      // next time: in front of you again
            Ov.Hide();
            return;
        }
        float e = 1 - MathF.Pow(1 - _vis, 3);
        Ov.SetAbsolute(_world);
        Ov.SetAlpha(e);
        Ov.SetWidth(_width * (0.92f + 0.08f * e));
        _cyClosing = !want;
        if (_dirty || _ui.Animating || Clock.Now >= _wake || (_vis < 1 && Theme.RetroSkin.AnimatesFade))
        {
            Draw();
            _wake = AnimWake.Take();
            Ov.SetTexture(Surf.Tex);
            _dirty = _ui.Interacting || _ui.Changed;
        }
        Ov.Show();
    }

    public void Draw()
    {
        bool ok;
        if (Theme.Classic)
        {
            _canvas95 ??= new Surface(_p.Gpu, W95, H95);
            _ui.Begin(_ptr, 2);
            _ui.Canvas95 = new SizeF(W95, H95);
            _p.BeginPixel(_canvas95.Bitmap, 0x00000000);
            ok = WhatsNewView.Draw95(_ui, new RectangleF(0, 0, W95, H95), _list, _sub);
            _p.End();
            _p.Begin(Surf.Bitmap, 0x00000000);
            _p.ImageNearest(_canvas95.Bitmap, new RectangleF(0, 0, TW, TH));
            _p.End();
            _ui.End();
        }
        else
        {
            _ui.Begin(_ptr, 1);
            _p.Begin(Surf.Bitmap, 0x00000000);
            var rev = CyberReveal.Begin(_p, new RectangleF(0, 0, TW, TH), Ov != null ? Ov.ShownAt : -10, Ov != null ? _vis : 1, _cyClosing);   // Cyber
            ok = WhatsNewView.DrawModern(_ui, new RectangleF(0, 0, TW, TH), _list, _sub, ref _scroll, Theme.OverlayBg);
            CyberReveal.End(_p, rev);
            _p.End();
            _ui.End();
        }
        if (ok) OnOk?.Invoke();
    }

    // used by --snapshot
    public void DrawFor(IReadOnlyList<WhatsNew.Entry> list, string sub) { _list = list; _sub = sub; Draw(); }

    // ---------------- input: one hand at a time, the stick scrolls ----------------

    public VrOverlay Overlay => Ov;
    public bool Interactive => Ov != null && Ov.Visible && _vis > 0.8f;
    public bool AcceptsFrom(Hand h) => true;
    public Vector2 TextureSize => new(TW, TH);

    public void PointerMove(Hand h, Vector2 px)
    {
        _owner ??= h;
        if (_owner != h) return;
        _ptr.Move(px.X, px.Y);
        if (_ptr.Down || _ui.NeedsRedrawForMove(_ptr, Theme.Classic ? 2 : 1)) _dirty = true;
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
    public Matrix4x4 WorldTransform => _world;
    public void GrabBegin(bool detach) { }
    public void GrabMove(Matrix4x4 world) => _world = world;
    public void GrabStickX(float v, float dt) => _width = Math.Clamp(_width * (1 + v * 0.9f * dt), 0.3f, 1.6f);
    public void GrabTwoHandBegin() => _twoStartWidth = _width;
    public void GrabTwoHandScale(float r) => _width = Math.Clamp(_twoStartWidth * r, 0.3f, 1.6f);
    public bool GrabSnapPreview => false;
    public void GrabEnd() { }

    // no retro style on a while (OrbitApp.GpuSweep, Erik 10-24: Orbit's VRAM): its half-size canvas let go, made again by the next retro draw
    public void ReleaseStyle() { if (!Theme.Classic) { _canvas95?.Dispose(); _canvas95 = null; } }

    public void Dispose()
    {
        DestroyOverlay();
        Surf.Dispose();
        _canvas95?.Dispose();
    }
}
