using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Valve.VR;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Slide = Orbit.DockInput.Slide;

namespace Orbit;

// The faces of the dock's six keys as bitmaps, shared by the dock panel and the watch widget (one Painter,
// one device). Rebuilt only for the keys that changed. Bitmaps are the device's own 68 px faces (the 95 window
// and the watch draw them pixel for pixel); Hi the same faces drawn at 2x by an api 2 Hexpad (null when none).
public sealed class HexpadFaces : IDisposable
{
    public readonly ID2D1Bitmap[] Bitmaps = new ID2D1Bitmap[HexpadLink.Keys];
    public readonly ID2D1Bitmap[] Hi = new ID2D1Bitmap[HexpadLink.Keys];
    readonly HexpadLink.Face[] _dev = new HexpadLink.Face[HexpadLink.Keys], _hi = new HexpadLink.Face[HexpadLink.Keys];
    public bool Any { get; private set; }

    // the sharpest picture of key i
    public ID2D1Bitmap Best(int i) => Hi[i] ?? Bitmaps[i];

    public void Update(Painter p, HexpadLink.Snapshot s)
    {
        Any = false;
        for (int i = 0; i < HexpadLink.Keys; i++)
        {
            Rebuild(p, s.Faces[i], _dev, Bitmaps, i);
            Rebuild(p, s.HiFaces[i], _hi, Hi, i);
            Any |= Bitmaps[i] != null || Hi[i] != null;
        }
    }

    static void Rebuild(Painter p, HexpadLink.Face f, HexpadLink.Face[] made, ID2D1Bitmap[] bmps, int i)
    {
        if (ReferenceEquals(f, made[i])) return;
        made[i] = f;
        bmps[i]?.Dispose();
        bmps[i] = null;
        if (f != null && f.W > 0 && f.H > 0 && f.Px.Length == f.W * f.H) bmps[i] = p.CreateBitmap(f.Px, f.W, f.H);
    }

    public void Dispose()
    {
        for (int i = 0; i < Bitmaps.Length; i++)
        {
            Bitmaps[i]?.Dispose(); Bitmaps[i] = null; _dev[i] = null;
            Hi[i]?.Dispose(); Hi[i] = null; _hi[i] = null;
        }
    }
}

// The stream dock in VR: Hexpad's AKP03 laid out like the real one - six keys (what they show right now) and
// a round button under each column - with its three knobs as sliders on the right (a knob is awkward to turn
// with a laser): the big knob first, then the two small ones. A slider Hexpad can read and set ("abs": volume,
// key brightness, monitors) shows the value and sets it as you drag; any other turn is a "jog" slider that springs
// back to the middle and turns the knob a detent per bit of drag. The stick over a slider moves it too. The button
// beside each slider pushes the knob. The laser presses keys and buttons (press and release, so Hexpad's hold and
// double tap work); what could end the VR session (lock, sleep, empty the bin) asks first. Grab it once onto the
// real dock on your desk; it stays there.
public sealed partial class DockPanel : IPointerTarget, IDisposable
{
    public const int TW = 800, TH = 480, W95 = TW / 2, H95 = TH / 2;

    readonly Painter _p;
    readonly Ui _ui;
    readonly ConfigStore _cfg;
    readonly HexpadFaces _faces;
    readonly PointerState _ptr = new();
    public readonly Surface Surf;
    Surface _canvas95;
    public VrOverlay Ov { get; private set; }
    VrSession _vr;
    float _vis;
    bool _dirty = true;
    double _nextDraw;
    int _seen = -1;
    float _twoStartWidth;

    // input: DockInput's (the watch's opened dock has one of its own); its parts under the names the drawing below always used
    Hand? _owner;
    readonly DockInput _in;
    HexpadLink.Snapshot _s => _in.Snap;
    string _hover => _in.Hover;
    string _pressed => _in.Pressed;
    string _confirm => _in.Confirm;
    string _drag => _in.Drag;
    float _jogOff => _in.JogOff;
    int _jogSent => _in.JogSent;
    Dictionary<string, double> _holdUntil => _in.HoldUntil;
    Dictionary<string, RectangleF> _hit => _in.Hit;
    Dictionary<string, RectangleF> _cards => _in.Cards;
    Dictionary<string, RectangleF> _track => _in.Track;

    DockConfig C => _cfg.Data.Dock;
    public bool Shown => C.Enabled && C.Visible;

    public DockPanel(Gpu g, Painter p, ConfigStore cfg, HexpadLink link, HexpadFaces faces)
    {
        _p = p; _cfg = cfg; _faces = faces;
        _in = new DockInput(link, cfg);
        _ui = new Ui(p);
        Surf = new Surface(g, TW, TH);
    }

    public void CreateOverlay(VrSession vr)
    {
        _vr = vr;
        Ov = new VrOverlay("erik.orbit.dock", "OrbitOverlay Hexpad");
        Ov.SetSortOrder(135);
        Ov.SetFlag(VROverlayFlags.IsPremultiplied, true);
        Ov.SetMouseScale(TW, TH);
        _vis = 0;
        _dirty = true;
    }

    public void DestroyOverlay()
    {
        _in.ReleasePress();
        _ptr.Leave();
        Ov?.Dispose();
        Ov = null;
        _vr = null;
        _vis = 0;
    }

    public void Toggle()
    {
        if (C.Visible) { C.Visible = false; _in.Confirm = null; _cfg.MarkDirty(); return; }
        Open();
    }

    public void Open()
    {
        C.Visible = true;
        _cfg.MarkDirty();
        _dirty = true;
        if (C.Place.M == null) BringHere();             // else where it was put last (over the real dock)
    }

    // a desk console in front of you: 0.45 m ahead, 0.3 m below the eyes, facing up at you
    public void BringHere()
    {
        if (_vr == null || !_vr.HmdValid) return;
        var local = Matrix4x4.CreateRotationX(-35 * MathF.PI / 180) * Matrix4x4.CreateTranslation(0, -0.30f, -0.45f);
        C.Place.Attach = "world";
        C.Place.M = MathUtil.ToArray(local * MathUtil.PlacementFrame(_vr.Hmd, _vr.HeadsetWorn, "dock"));
        _cfg.MarkDirty();
    }

    public void Update(VrSession vr, double now, float dt, HexpadLink.Snapshot s)
    {
        if (Ov == null) return;
        float target = Shown ? 1 : 0;
        _vis = Motion.Step(_vis, Shown, dt, 0.18f);                      // Look > Motion off: at once
        if (_vis <= 0 || C.Place.M == null || !Placement.Apply(C.Place, vr, Ov))
        {
            if (_vis <= 0) { _in.ReleasePress(); _ptr.Leave(); _in.Hover = null; }
            Ov.Hide();
            return;
        }
        float e = 1 - MathF.Pow(1 - _vis, 3);
        Ov.SetAlpha(e);
        Ov.SetWidth(Math.Clamp(C.Width, 0.15f, 0.9f) * (0.92f + 0.08f * e));
        if (s.Version != _seen) { _seen = s.Version; _in.Snap = s; _dirty = true; }
        _in.Timers(now);
        if (_in.TakeDirty()) _dirty = true;
        bool flashing = now - _s.EchoAt < 0.4;
        // a style's opening / closing shows on every frame of the fade
        if (_dirty || flashing || now >= _nextDraw || _ui.Animating || (_vis < 1 && Theme.RetroSkin.AnimatesFade))
        {
            Draw(now);
            Ov.SetTexture(Surf.Tex);
            _dirty = _ui.Interacting || _ui.Changed;
            _nextDraw = Math.Min(now + 1, AnimWake.Take());              // sooner when a style's animation asks (AnimWake.cs)
        }
        Ov.Show();
    }

    // ---------------- input: one hand at a time, handed to DockInput ----------------

    bool Live => _in.Live;
    static string S(string id) => DockInput.S(id);
    Slide TypeOf(string id) => _in.TypeOf(id);
    bool Off(string id) => _in.Off(id);
    float? AbsValue(string id, double now) => _in.AbsValue(id, now);
    float Detent(string id) => _in.Detent(id);

    public void PointerMove(Hand h, Vector2 px)
    {
        _owner ??= h;
        if (_owner != h) return;
        _ptr.Move(px.X, px.Y);
        _in.Move(px);
        if (_in.TakeDirty()) _dirty = true;
        if (_ui.NeedsRedrawForMove(_ptr, Theme.Classic ? 2 : 1)) _dirty = true;
    }

    public void PointerLeave(Hand h)
    {
        if (_owner != h) return;
        _owner = null;
        _in.Leave();
        _in.TakeDirty();
        _ptr.Leave();
        _dirty = true;
    }

    public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px)
    {
        if (b != PtrButton.Primary) return;
        if (down)
        {
            if (_owner != h && (_ptr.Down || _in.Busy)) return;
            _owner = h;
            _ptr.Move(px.X, px.Y);
            _ptr.Press();
            _in.Down(px);
        }
        else
        {
            if (_owner != h) return;
            _ptr.Move(px.X, px.Y);
            _ptr.Release();
            _in.Up();
        }
        _in.TakeDirty();
        _dirty = true;
    }

    // the stick over a knob's row moves its slider
    public void PointerScroll(Hand h, float notches, Vector2 px)
    {
        if (_owner != h) return;
        _in.Scroll(px, notches);
        if (_in.TakeDirty()) _dirty = true;
    }

    public VrOverlay Overlay => Ov;
    public bool Interactive => Ov != null && Ov.Visible && Shown && _vis > 0.8f;
    public bool AcceptsFrom(Hand h) => true;
    public Vector2 TextureSize => new(TW, TH);
    public bool Grabbable => true;
    public Matrix4x4 WorldTransform =>
        _vr != null && Placement.TryWorld(C.Place, _vr, out var w) ? w : MathUtil.FromArray(C.Place.M);
    public void GrabBegin(bool detach) { _in.ReleasePress(); }
    public void GrabMove(Matrix4x4 world) { if (_vr != null) { Placement.SetWorld(C.Place, _vr, world); _cfg.MarkDirty(); } }
    public void GrabStickX(float v, float dt) { C.Width = Math.Clamp(C.Width * (1 + v * 0.9f * dt), 0.15f, 0.9f); _cfg.MarkDirty(); }
    public void GrabTwoHandBegin() => _twoStartWidth = C.Width;
    public void GrabTwoHandScale(float r) { C.Width = Math.Clamp(_twoStartWidth * r, 0.15f, 0.9f); _cfg.MarkDirty(); }
    public bool GrabSnapPreview => false;
    public void GrabEnd() { }

    // ---------------- drawing ----------------

    // without VR (--docktest): this snapshot, a pointer over a control, a pending confirmation, a slider being
    // dragged (drag = the knob; dragAt = the value for an abs slider, the pull in texture px for a jog one).
    // Only draws: nothing is sent.
    public void DrawFor(HexpadLink.Snapshot s, double now, string hover = null, string pressed = null, string confirm = null,
        string drag = null, float dragAt = 0)
    {
        _in.Pose(s, hover, pressed, confirm, drag, dragAt);
        _ui.ResetAnimations();
        Draw(now);
        _in.AfterPose();
    }

    public void Draw(double now)
    {
        _in.BeginLayout();
        if (Theme.Classic) Draw95(now);
        else DrawModern(now);
    }

    string Where() => _in.Where();
    string StateLine() => _in.StateLine();
    HexpadLink.Control Ctl(string id) => _in.Ctl(id);
    bool Flash(string id, double now) => _in.Flash(id, now);
    static string KnobName(string id, HexpadLink.Control c) => DockInput.KnobName(id, c);
    static string PushLabel(HexpadLink.Control c) => DockInput.PushLabel(c);
    string ValueText(string id, Slide t, double now) => _in.ValueText(id, t, now);

    // ---- modern: a dark slab like the device ----
    const float Pad = 20, Tile = 136, Gap = 12, KeysY = 66;
    static float KeyX(int col) => Pad + col * (Tile + Gap);
    static float KeyY(int row) => KeysY + row * (Tile + Gap);
    // the knobs' column: the big one first, then the small left and right ones
    const float ColX = 472, RowH = 116, RowGap = 12;
    static readonly string[] Knobs = { "enc1", "enc0", "enc2" };
    // the keys' and the round buttons' ids as Hexpad names them, made once (a new string per key per frame before)
    static readonly string[] KeyIds = { "key1", "key2", "key3", "key4", "key5", "key6" }, BtnIds = { "btn7", "btn8", "btn9" };

    void DrawModern(double now)
    {
        _ui.Begin(_ptr, 1);
        _p.Begin(Surf.Bitmap, 0x00000000);
        var all = new RectangleF(0, 0, TW, TH);
        var cyRev = CyberReveal.Begin(_p, all, Ov != null ? Ov.ShownAt : -10, Ov != null ? _vis : 1, !Shown);     // Cyber: its reveal
        if (Theme.Cyber) CyberHud.ConsoleFrame(_p, all, Theme.OverlayBg);
        else
        {
            _p.Fill(all, Theme.OverlayBg, 30);
            _p.Stroke(new RectangleF(1, 1, TW - 2, TH - 2), 0x40FFFFFF, 2, 29);
        }
        uint sub = Theme.Mix(Theme.Text, Theme.OverlayBg, 0.42f);

        // header: name, where on the dock, the idle chip; bring here / close on the right
        if (Theme.Cyber) CyberHeader();            // a HEXPAD tag, where and the idle state as readouts (DockCyber.cs)
        else
        {
        _p.Text("Hexpad", new RectangleF(Pad + 4, 14, 140, 38), Theme.Text, 22, FontWeight.SemiBold);
        float hx = Pad + 4 + _p.MeasureWidth("Hexpad", 22, FontWeight.SemiBold) + 14;
        if (Live || _s.State == HexpadLink.LinkState.ScreenOff)
        {
            string where = Where();
            float pw = Math.Min(_p.MeasureWidth(where, 15, FontWeight.SemiBold) + 26, 360);
            _p.Fill(new RectangleF(hx, 19, pw, 30), Theme.Panel3, 15);
            _p.Text(where, new RectangleF(hx + 13, 19, pw - 22, 30), sub, 15, FontWeight.SemiBold);
            hx += pw + 10;
            if (_s.Ambient)
            {
                const string idle = "idle · point to wake";
                float iw = _p.MeasureWidth(idle, 14) + 24;
                _p.Fill(new RectangleF(hx, 19, iw, 30), Theme.WithAlpha(Theme.Accent, 0.18f), 15);
                _p.Text(idle, new RectangleF(hx + 12, 19, iw - 18, 30), Theme.Accent, 14);
            }
        }
        }
        if (HeaderButton("dk.x", new RectangleF(TW - Pad - 40, 14, 40, 40), "")) { Toggle(); }
        if (HeaderButton("dk.here", new RectangleF(TW - Pad - 90, 14, 40, 40), Theme.IcoRecenter)) BringHere();

        if (!Live && _s.State != HexpadLink.LinkState.ScreenOff || _s.State == HexpadLink.LinkState.ScreenOff && !_faces.Any)
        {
            _p.Text(StateLine(), new RectangleF(0, 150, TW, 60), Theme.Text, 24, FontWeight.SemiBold, TextAlignment.Center);
            string hint = _s.State switch
            {
                HexpadLink.LinkState.NotRunning => "start it, and the dock shows up here",
                HexpadLink.LinkState.ScreenOff => "hold its middle round button to turn it on",
                _ => "",
            };
            _p.Text(hint, new RectangleF(0, 206, TW, 30), sub, 16, FontWeight.Normal, TextAlignment.Center);
            if (cyRev.Kind != 0) CyberReveal.End(_p, cyRev);
            _p.End();
            _ui.End();
            return;
        }

        // the six keys: the sharp faces when Hexpad sends them, else the device's
        for (int i = 0; i < HexpadLink.Keys; i++)
        {
            string id = KeyIds[i];
            var r = new RectangleF(KeyX(i % 3), KeyY(i / 3), Tile, Tile);
            _hit[id] = r;
            bool down = _pressed == id, hov = _hover == id, flash = Flash(id, now);
            var rr = down ? RectangleF.Inflate(r, -5, -5) : r;
            _p.Fill(rr, 0xFF000000, 18);
            var bmp = _faces.Best(i);
            if (bmp != null && !_s.Dark)
            {
                _p.PushRoundedClip(rr, 18);
                _p.Image(bmp, rr);
                _p.PopRoundedClip();
            }
            if (Ctl(id) is { Danger: true }) _p.Circle(rr.Right - 14, rr.Y + 14, 5, Theme.Bad);
            if (hov || flash) _p.Stroke(RectangleF.Inflate(rr, 3, 3), flash ? Theme.Accent : Theme.WithAlpha(Theme.Accent, 0.85f), 3, 21);
            if (Theme.Cyber) CyberKeyMarks(rr, hov, flash, i);       // brackets, its number (DockCyber.cs)
        }

        // a round button under each column
        for (int col = 0; col < 3; col++)
        {
            string id = BtnIds[col];
            float cx = KeyX(col) + Tile / 2, cy = KeyY(2) + 18;
            var r = new RectangleF(cx - 24, cy - 24, 48, 48);
            _hit[id] = RectangleF.Inflate(r, 10, 6);
            bool down = _pressed == id, hov = _hover == id, flash = Flash(id, now);
            if (Theme.Cyber) CyberRoundButton(r, down, hov, flash);       // a cut plate (DockCyber.cs)
            else
            {
                _p.Circle(cx, cy, down ? 21 : 24, down || flash ? Theme.Accent : Theme.Panel3);
                if (hov) _p.Stroke(RectangleF.Inflate(r, 3, 3), Theme.Accent, 2.5f, 27);
            }
            string label = Ctl(id)?.Label ?? "";
            _p.Text(label, new RectangleF(cx - Tile / 2, cy + 28, Tile, 22), sub,
                FitSize(label, Tile - 4, 14), FontWeight.SemiBold, TextAlignment.Center);
        }

        // the knobs as sliders
        for (int k = 0; k < Knobs.Length; k++)
            SliderRow(Knobs[k], new RectangleF(ColX, KeysY + k * (RowH + RowGap), TW - Pad - ColX, RowH), now, sub);

        if (_confirm != null) ConfirmModern(sub);
        if (cyRev.Kind != 0) CyberReveal.End(_p, cyRev);
        _p.End();
        _ui.End();
    }

    float FitSize(string s, float w, float max)
    {
        if (string.IsNullOrEmpty(s)) return max;
        float w100 = _p.MeasureWidth(s, 100, FontWeight.SemiBold);
        return w100 <= 0 ? max : MathF.Max(9, MathF.Min(max, MathF.Floor(100 * w / w100 * 2) / 2));
    }

    // what does not fit even at the smallest size it may have ends in "…"
    string Ellipsize(string s, float w, float size)
    {
        s = Lang.T(s);                     // cut in the language picked
        if (string.IsNullOrEmpty(s) || _p.MeasureWidth(s, size, FontWeight.SemiBold) <= w) return s;
        int n = s.Length;
        while (n > 1 && _p.MeasureWidth(s[..n].TrimEnd() + "…", size, FontWeight.SemiBold) > w) n--;
        return s[..n].TrimEnd() + "…";
    }

    bool HeaderButton(string id, RectangleF r, string icon)
    {
        if (Theme.Cyber) return _ui.HudButton(id, r, null, icon, false, true, 13);        // a HUD plate
        bool click = _ui.Clickable(id, r, out bool hover, out bool held);
        _p.Fill(r, held ? Theme.Accent : hover ? Theme.Mix(Theme.Panel3, 0xFFFFFFFF, 0.12f) : Theme.Panel3, r.Height / 2);
        _p.Icon(icon, r, held ? Theme.OnAccent : Theme.Text, 16);
        return click;
    }

    // one knob: a card with its name and value, the push as a pill on the right, and a wide track for the laser
    void SliderRow(string id, RectangleF card, double now, uint sub)
    {
        var c = Ctl(id);
        var t = TypeOf(id);
        bool off = t == Slide.Abs && Off(id), on = t != Slide.None && !off;
        bool dragging = _drag == id, flash = Flash(id, now);
        bool hovS = on && (dragging || _hover == S(id)), hovP = _hover == id;
        _cards[id] = card;
        if (Theme.Cyber) CyberHud.CardShape(_p, card, CyberHud.Card, CyberHud.Edge);
        else _p.Fill(card, Theme.Panel2, 18);
        if (flash) _p.Stroke(RectangleF.Inflate(card, -1, -1), Theme.Accent, 2, 17);

        // the push: a pill at the right of the name
        string push = PushLabel(c);
        float right = card.Right - 16;
        if (push.Length > 0)
        {
            float bw = Math.Clamp(_p.MeasureWidth(push, 15, FontWeight.SemiBold) + 36, 84, 132);
            var b = new RectangleF(card.Right - 12 - bw, card.Y + 12, bw, 38);
            _hit[id] = RectangleF.Inflate(b, 6, 4);
            bool down = _pressed == id;
            var bb = down ? RectangleF.Inflate(b, -2, -2) : b;
            _p.Fill(bb, down ? Theme.Accent : hovP ? Theme.Mix(Theme.Panel3, 0xFFFFFFFF, 0.12f) : Theme.Panel3, bb.Height / 2);
            if (hovP && !down) _p.Stroke(RectangleF.Inflate(b, 2, 2), Theme.Accent, 2, b.Height / 2 + 2);
            // what could end the session (it asks first) has the keys' red dot, beside the label
            float dr = c.Danger ? 10 : 0, pw = b.Width - 24 - dr, ps = MathF.Max(12, FitSize(push, pw - 2, 15));
            _p.Text(Ellipsize(push, pw, ps), new RectangleF(b.X + 12, b.Y, pw, b.Height), down ? Theme.OnAccent : Theme.Text,
                ps, FontWeight.SemiBold, TextAlignment.Center);
            if (c.Danger) _p.Circle(b.Right - 15, b.Y + b.Height / 2, 4, Theme.Bad);
            right = b.X - 12;
        }

        // name, then the value
        string name = KnobName(id, c), value = ValueText(id, t, now);
        if (t == Slide.None) value = push.Length > 0 ? "push only" : "";
        float x0 = card.X + 18;
        float vw = value.Length > 0 ? _p.MeasureWidth(value, 16, FontWeight.SemiBold) + 12 : 0;
        float nmax = MathF.Max(40, right - x0 - vw);
        float ns = MathF.Max(15, FitSize(name, nmax, 20));
        name = Ellipsize(name, nmax, ns);
        float nw = MathF.Min(_p.MeasureWidth(name, ns, FontWeight.SemiBold), nmax);
        _p.Text(name, new RectangleF(x0, card.Y + 12, nmax + 2, 38), Theme.Text, ns, FontWeight.SemiBold);
        if (value.Length > 0)
        {
            uint vc = t == Slide.None || off ? Theme.Faint : dragging && t == Slide.Jog ? Theme.Accent : value == "Muted" ? Theme.Warn : sub;
            _p.Text(value, new RectangleF(x0 + nw + 12, card.Y + 12, MathF.Max(0, right - x0 - nw - 10), 38), vc, 16, FontWeight.SemiBold);
        }

        // the track: 14 px, a 28 px thumb; the whole lower part of the card takes the laser
        float cy = card.Y + 84;
        var tr = new RectangleF(card.X + 30, cy - 7, card.Width - 60, 14);
        _track[id] = tr;
        if (on) _hit[S(id)] = new RectangleF(card.X, card.Y + 56, card.Width, card.Height - 56 + 5);
        float hl = _ui.Anim(Ui.Id("dkh", id), hovS ? 1 : 0, 18);
        if (!on)
        {
            if (Theme.Cyber) CyberHud.Segments(_p, new RectangleF(tr.X, tr.Y + 2, tr.Width, 10), 0, 0, Theme.WithAlpha(CyberHud.Track, 0.55f), 0, 4);
            else _p.Fill(tr, Theme.WithAlpha(Theme.Panel3, 0.55f), 7);
            return;
        }
        if (Theme.Cyber && t == Slide.Abs) { CyberAbsTrack(id, tr, cy, hl, dragging, now); return; }       // segments and a notch (DockCyber.cs)
        _p.Fill(tr, Theme.Panel3, 7);
        if (t == Slide.Abs)
        {
            if (AbsValue(id, now) is not float v)
            {
                _p.Ring(tr.X + tr.Width / 2, cy, 11, Theme.Faint, 2);     // Hexpad does not know it yet
                return;
            }
            float kx = tr.X + tr.Width * v;
            bool muted = _s.Values.TryGetValue(id, out var x) && x.Muted && !dragging && !_holdUntil.ContainsKey(id);
            if (kx - tr.X > 1) _p.Fill(new RectangleF(tr.X, tr.Y, MathF.Max(14, kx - tr.X + 7), tr.Height), muted ? Theme.Faint : Theme.Accent, 7);
            Thumb(kx, cy, hl, dragging);
            return;
        }
        // jog: detent marks either side of the middle; the thumb follows the pull and springs back
        float mid = tr.X + tr.Width / 2, d = Detent(id), half = tr.Width / 2;
        for (float k = d; k < half - 6; k += d)
        {
            _p.Circle(mid - k, cy, 2, Theme.WithAlpha(Theme.Text, 0.3f));
            _p.Circle(mid + k, cy, 2, Theme.WithAlpha(Theme.Text, 0.3f));
        }
        _p.Icon(Theme.IcoLeft, new RectangleF(tr.X + 1, tr.Y, 14, tr.Height), sub, 9);
        _p.Icon(Theme.IcoRight, new RectangleF(tr.Right - 15, tr.Y, 14, tr.Height), sub, 9);
        float off2 = _ui.Anim(Ui.Id("dkj", id), dragging ? Math.Clamp(_jogOff, -half, half) : 0, dragging ? 1000 : 14);
        if (MathF.Abs(off2) > 1)
        {
            float a = MathF.Min(mid, mid + off2), w = MathF.Abs(off2);
            _p.Fill(new RectangleF(a - 7, tr.Y, w + 14, tr.Height), Theme.Accent, 7);
        }
        Thumb(mid + off2, cy, hl, dragging);
    }

    void Thumb(float x, float y, float hl, bool held)
    {
        if (Theme.Cyber) { CyberThumb(x, y, hl, held); return; }
        if (hl > 0.01f) _p.Circle(x, y, 14 + 9 * hl, Theme.WithAlpha(Theme.Accent, 0.22f * hl));
        _p.Circle(x, y, held ? 15 : 14, Theme.Text);
    }

    void ConfirmModern(uint sub)
    {
        string label = ConfirmLabel();
        _p.Fill(new RectangleF(0, 0, TW, TH), Theme.WithAlpha(0xFF000000, 0.55f), 30);
        var card = new RectangleF(TW / 2 - 230, TH / 2 - 100, 460, 200);
        if (Theme.Cyber) CyberConfirmCard(card);           // a HUD warning card (DockCyber.cs)
        else
        {
            _p.Fill(card, Theme.Panel, 22);
            _p.Stroke(RectangleF.Inflate(card, -0.6f, -0.6f), Theme.Border, 1.2f, 22);
        }
        _p.Text(Lang.F("Run “{0}”?", label), new RectangleF(card.X + 24, card.Y + 20, card.Width - 48, 40), Theme.Text,
            FitSize(Lang.F("Run “{0}”?", label), card.Width - 48, 22), FontWeight.SemiBold);
        _p.Text("It could end your VR session, so it asks first.", new RectangleF(card.X + 24, card.Y + 62, card.Width - 48, 26), sub, 15);
        if (_ui.Button("dk.no", new RectangleF(card.X + 24, card.Bottom - 68, 196, 46), "Cancel")) _in.ConfirmNo();
        if (_ui.Button("dk.yes", new RectangleF(card.Right - 220, card.Bottom - 68, 196, 46), "Do it", null, true)) _in.ConfirmYes();
    }

    string ConfirmLabel() => _in.ConfirmLabel();

    // ---- Windows 95: a "Hexpad" window, the key faces 1:1 in picture boxes, a trackbar group per knob ----
    const uint Blk95 = 0xFF000000, Wht95 = 0xFFFFFFFF, Navy95 = 0xFF000080;
    string _title95, _titleProfile;

    void Draw95(double now)
    {
        if (SkinDock(now)) return;          // a style's own panel (ScreenHooks.cs)
        _canvas95 ??= new Surface(_p.Gpu, W95, H95);
        _ui.Begin(_ptr, 2);
        _ui.Canvas95 = new SizeF(W95, H95);
        _p.BeginPixel(_canvas95.Bitmap, 0x00000000);
        var win = new RectangleF(0, 0, W95, H95);
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        if (!ReferenceEquals(_s.Profile, _titleProfile) || _title95 == null)        // the caption, made again only for another profile
        {
            _titleProfile = _s.Profile;
            _title95 = string.IsNullOrEmpty(_s.Profile) ? "Hexpad" : $"Hexpad - {_s.Profile}";
        }
        _ui.Caption95(new RectangleF(3, 3, W95 - 6, 18), _title95);
        if (_ui.CaptionClose95("dk95.x", W95 - 5 - 16, 5, true, 6)) Toggle();

        bool show = Live || _s.State == HexpadLink.LinkState.ScreenOff && _faces.Any;
        if (!show)
        {
            // nothing to show: the classic message in one white box where the keys would be
            var msg = new RectangleF(8, 26, 228, 150);
            _p.Fill(msg, Wht95);
            _p.Bevel(msg, Painter.Edge.Sunken);
            W95Icons.Draw(_p, W95Icons.Id.Info32, msg.X + 14, msg.Y + 16);
            _ui.Text95(Lang.T(StateLine()) + ".", new RectangleF(msg.X + 54, msg.Y + 18, msg.Width - 64, msg.Height - 30), Blk95, false,
                TextAlignment.Leading, true, true);
        }
        // the six keys: 68 x 68 picture boxes, the dock's own pixels
        for (int i = 0; i < HexpadLink.Keys && show; i++)
        {
            string id = KeyIds[i];
            var box = new RectangleF(8 + i % 3 * 78, 26 + i / 3 * 78, 72, 72);
            _hit[id] = Tex(box);
            bool down = _pressed == id;
            _p.Fill(box, show ? Blk95 : Wht95);
            var bmp = _faces.Bitmaps[i];
            if (show && bmp != null && !_s.Dark)
                _p.ImageNearest(bmp, new RectangleF(box.X + 2 + (down ? 1 : 0), box.Y + 2 + (down ? 1 : 0), 68, 68));
            _p.Bevel(box, down ? Painter.Edge.Pressed : Painter.Edge.Sunken);
            if (show && (_hover == id || Flash(id, now))) Dotted(RectangleF.Inflate(box, -4, -4), Flash(id, now) ? 0xFFFFFF00 : Wht95);
        }
        // the round buttons under the columns
        for (int col = 0; col < 3; col++)
        {
            string id = BtnIds[col];
            var r = new RectangleF(8 + col * 78, 182, 72, 22);
            _hit[id] = Tex(r);
            var inner = _p.ButtonFace95(r, false, _pressed == id || Flash(id, now), false);
            string label = Ctl(id)?.Label ?? "";
            _ui.Text95(Fit95(label, inner.Width - 4), inner, Blk95, false, TextAlignment.Center, show);
        }
        // the knobs: a trackbar group each, the big one first
        for (int k = 0; k < Knobs.Length; k++)
            SliderGroup95(Knobs[k], new RectangleF(243, 23 + k * 63, 152, 61), show, now);
        // status bar
        string pages = _s.InFolder ? (_s.FolderPages > 1 ? Lang.F("Folder {0} of {1}", _s.FolderPage + 1, _s.FolderPages) : "Folder")
            : Lang.F("Page {0} of {1}", _s.Page + 1, _s.Pages);
        string name = _s.InFolder ? _s.FolderName : _s.PageName;
        string state = Live ? (_s.Ambient ? "Idle" : "Connected") : StateLine();
        if (show)
        {
            _ui.Pane95(new RectangleF(4, 214, 96, 20), pages);
            _ui.Pane95(new RectangleF(102, 214, 140, 20), Fit95(name, 130));
            _ui.Pane95(new RectangleF(244, 214, W95 - 248, 20), Fit95(state, W95 - 262), Live ? 0xFF008000 : 0xFF808080);
        }
        else _ui.Pane95(new RectangleF(4, 214, W95 - 8, 20), Fit95(StateLine(), W95 - 20));
        if (_confirm != null) Confirm95();
        _p.End();
        _p.Begin(Surf.Bitmap, 0x00000000);
        _p.ImageNearest(_canvas95.Bitmap, new RectangleF(0, 0, TW, TH));
        _p.End();
        _ui.End();
    }

    static RectangleF Tex(RectangleF r) => new(r.X * 2, r.Y * 2, r.Width * 2, r.Height * 2);

    string Fit95(string s, float w)
    {
        s = Lang.T(s);                     // cut in the language picked
        if (string.IsNullOrEmpty(s) || _ui.Width95(s) <= w) return s;
        int n = s.Length;
        while (n > 1 && _ui.Width95(s[..n] + "...") > w) n--;
        return s[..n].TrimEnd() + "...";
    }

    void Dotted(RectangleF r, uint c)
    {
        for (float x = r.X; x < r.Right; x += 2) { _p.Fill(new RectangleF(x, r.Y, 1, 1), c); _p.Fill(new RectangleF(x, r.Bottom - 1, 1, 1), c); }
        for (float y = r.Y; y < r.Bottom; y += 2) { _p.Fill(new RectangleF(r.X, y, 1, 1), c); _p.Fill(new RectangleF(r.Right - 1, y, 1, 1), c); }
    }

    // a group box titled with what the knob does: a trackbar (no ticks, so its thumb is the plain raised block),
    // under it the value and a push button. Focus dots round the trackbar while the laser is on it.
    void SliderGroup95(string id, RectangleF g, bool show, double now)
    {
        var c = Ctl(id);
        var t = show ? TypeOf(id) : Slide.None;
        bool off = t == Slide.Abs && Off(id), on = t != Slide.None && !off;
        bool dragging = _drag == id;
        _cards[id] = Tex(g);
        _ui.Group95(g, Fit95(KnobName(id, c), g.Width - 20));

        // the trackbar: a 4 px channel, an 11 x 18 thumb whose centre runs from end to end of it
        var bar = new RectangleF(g.X + 7, g.Y + 15, g.Width - 14, 18);
        var ch = new RectangleF(bar.X + 5, bar.Y + 7, bar.Width - 10, 4);
        _track[id] = Tex(new RectangleF(ch.X, ch.Y, ch.Width - 1, ch.Height));
        if (on) _hit[S(id)] = Tex(new RectangleF(g.X + 2, g.Y + 8, g.Width - 4, 28));
        _p.Fill(ch, Wht95);
        _p.Bevel(ch, Painter.Edge.Sunken);
        float mid = ch.X + MathF.Floor((ch.Width - 1) / 2), tx = mid;
        bool known = true;
        if (t == Slide.Abs && !off)
        {
            if (AbsValue(id, now) is float v) tx = ch.X + MathF.Round((ch.Width - 1) * v);
            else known = false;
        }
        else if (t == Slide.Jog && dragging)
            tx = mid + Math.Clamp(MathF.Round(_jogOff / 2), -(ch.Width - 1) / 2, (ch.Width - 1) / 2);
        var thumb = new RectangleF(tx - 5, bar.Y, 11, bar.Height);
        _p.Fill(thumb, Theme.W95Face);
        _p.Bevel(thumb, Painter.Edge.Raised);
        if (!on || !known) _p.FillChecker(RectangleF.Inflate(thumb, -2, -2), Wht95, Theme.W95Face);   // greyed: nothing to show / turn
        if (on && (_hover == S(id) || dragging) || Flash(id, now)) Dotted(new RectangleF(g.X + 4, g.Y + 13, g.Width - 8, 22), Blk95);

        // the value, and the push button
        float by = g.Y + 37;
        string push = PushLabel(c);
        bool canPush = show && push.Length > 0;
        if (!canPush) push = "Push";
        float bw = Math.Clamp(_ui.Width95(push) + 18, 52, 75);
        var b = new RectangleF(g.Right - 8 - bw, by, bw, 20);
        if (canPush) _hit[id] = Tex(RectangleF.Inflate(b, 1, 1));
        bool down = canPush && _pressed == id;
        var inner = _p.ButtonFace95(b, false, down, false);
        float o = down ? 1 : 0;
        _ui.Text95(Fit95(push, inner.Width - 6), new RectangleF(inner.X + o, inner.Y + o, inner.Width, inner.Height), Blk95, false,
            TextAlignment.Center, canPush);
        string value = show ? ValueText(id, t, now).Replace('−', '-') : "";     // 1995 typed a hyphen
        var vr = new RectangleF(g.X + 9, by, b.X - g.X - 13, 20);
        _ui.Text95(Fit95(value, vr.Width), vr, Blk95, false, TextAlignment.Leading, on);
    }

    void Confirm95()
    {
        string label = ConfirmLabel();
        var box = new RectangleF(W95 / 2 - 130, H95 / 2 - 56, 260, 112);
        _p.Fill(box, Theme.W95Face);
        _p.Bevel(box, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(box.X + 3, box.Y + 3, box.Width - 6, 18), "Hexpad");
        W95Icons.Draw(_p, W95Icons.Id.Info32, box.X + 12, box.Y + 32);
        _ui.Text95(Fit95(Lang.F("Run \"{0}\"?", label), box.Width - 64), new RectangleF(box.X + 52, box.Y + 30, box.Width - 60, 16), Blk95, true);
        _ui.Text95("It could end your VR session.", new RectangleF(box.X + 52, box.Y + 48, box.Width - 60, 16));
        if (_ui.Button95("dk95.yes", new RectangleF(box.X + box.Width / 2 - 80, box.Bottom - 32, 75, 23), "&Yes", true, true))
            _in.ConfirmYes();
        if (_ui.Button95("dk95.no", new RectangleF(box.X + box.Width / 2 + 5, box.Bottom - 32, 75, 23), "&No")) _in.ConfirmNo();
    }

    // no retro style on a while (OrbitApp.GpuSweep, Erik 10-24: Orbit's VRAM): its half-size canvas let go, made again by the next retro draw
    public void ReleaseStyle() { if (!Theme.Classic) { _canvas95?.Dispose(); _canvas95 = null; } }

    public void Dispose()
    {
        DestroyOverlay();
        Surf.Dispose();
        _canvas95?.Dispose();
    }
}
