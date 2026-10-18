using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Valve.VR;
using Vortice.DirectWrite;

namespace Orbit;

// The bar under a screen.
// - Under the middle monitor (the one Recenter centres on) it shows up when you point at the lower part of it (or at the
//   bar itself): a Theater button and theater mode's settings - how dark the world gets, the big screen's size, curved
//   or flat. It stays under that monitor: pointing at the others does not move it about.
// - In theater mode it sits under the big screen while you point at it (and for a moment when it starts): a button for
//   every monitor and window to switch to (left to right as on your desk, then the windows), the same settings, live,
//   and Exit.
// - Both: a keyboard button (lit while the keyboard is shown).
// It fades away when you are not using it, so nothing sits under the picture. Turned off: Settings > Monitors.
public sealed partial class TheaterBar : IPointerTarget, IDisposable
{
    // modern: pills on a rounded bar; 95: push buttons, trackbars and a check box on a strip, half-size canvas drawn 2x
    const int H = 84, Pad = 10, ChipW = 230, Gap = 8, GoW = 196, SliderW = 210, CurveW = 168;
    const int H95 = 34, Btn95W = 150, Go95W = 86, Track95W = 84;
    // the keyboard and exit buttons: as wide as their words in another language (the 1995 sizes in English)
    float Exit95W => Lang.English ? 46 : MathF.Max(46, MathF.Ceiling(_ui.LabelWidth95("Exit")) + 16);
    float Kb95W => Lang.English ? 86 : MathF.Max(86, MathF.Ceiling(_ui.LabelWidth95("&Keyboard")) + 40);

    readonly Gpu _g;
    readonly Painter _p;
    readonly Ui _ui;
    readonly ConfigStore _cfg;
    readonly PointerState _ptr = new();
    Surface _surf, _canvas95, _retired;     // _retired: the last size's texture, freed once the overlay has the new one
    int _tw, _th, _w95, _layoutKey = -1, _look = -1;
    public VrOverlay Ov { get; private set; }
    float _vis, _lastSize, _wheelAcc, _drawnDim = -1;
    bool _dirty = true, _wasOn, _theater, _drawnCurved, _drawnKb;
    double _showUntil, _holdUntil, _wake = double.PositiveInfinity;    // _wake: a style's animation wants its next frame then (AnimWake.cs)
    Hand? _owner;
    Matrix4x4 _world = Matrix4x4.Identity;
    readonly List<ITheaterScreen> _choices = new();
    ITheaterScreen _target;                 // the screen the bar is under
    TheaterView _view;                      // theater mode: the big screen

    public Action<ITheaterScreen> OnSwitch;     // theater mode: play this one instead
    public Action<ITheaterScreen> OnTheater;    // under a screen: start theater mode with it
    public Action OnExit;
    public Action OnSettings;                   // a setting changed (size and curve apply to the big screen at once)
    public Action OnKeyboard;                   // the keyboard button: show / hide the keyboard
    public Func<bool> KeyboardShown;

    TheaterConfig C => _cfg.Data.Theater;

    public TheaterBar(Gpu g, Painter p, ConfigStore cfg) { _g = g; _p = p; _cfg = cfg; _ui = new Ui(p); }

    public void CreateOverlay()
    {
        Ov = new VrOverlay("erik.orbit.theater.bar", "OrbitOverlay screen bar");
        Ov.SetSortOrder(110);           // over the screens, under the keyboard and the panels
        Ov.SetFlag(VROverlayFlags.IsPremultiplied, true);
        _layoutKey = -1;
        _vis = 0;
    }

    public void DestroyOverlay() { Cancel(); Ov?.Dispose(); Ov = null; _vis = 0; _target = null; }

    // drop whatever the pointer was doing without it counting as a release: a slider held while the bar changes role
    // or goes away must not take the stale position as its new value
    void Cancel()
    {
        _ptr.Leave();
        _ptr.PressedLatch = _ptr.ReleasedLatch = false;
        _ptr.Wheel = 0;
        _wheelAcc = 0;
        _owner = null;
    }

    // choices: the screens theater mode can switch to, in bar order; pointed: the monitor / window a laser is on (null:
    // none) and lower: on the lower part of it; onBar: a laser is on the bar
    public void Update(VrSession vr, double now, float dt, Theater theater, List<ITheaterScreen> choices,
        ITheaterScreen pointed, bool lower, bool onBar)
    {
        if (Ov == null) return;
        _view = theater.View;
        bool th = theater.On && _view != null && theater.Screen != null;
        if (th != _theater)
        {
            // theater mode started or ended: the bar starts afresh in its other role
            _theater = th;
            _vis = 0;
            _target = null;
            Cancel();
            _dirty = true;
        }
        bool show;
        if (th)
        {
            if (!_wasOn) _showUntil = now + 4;        // just started: show where the switcher is
            _target = theater.Screen;
            if (pointed == _target || onBar || _owner != null) _showUntil = Math.Max(_showUntil, now + 2.5);
            show = now < _showUntil;
        }
        else
        {
            // a panel that lost its overlay (monitors rebuilt, window closed, layout switched) or went onto a hand / the
            // head (the bar would trail behind it): the bar goes with it
            if (_target != null && (_target.Overlay == null || !_target.HomeInWorld)) { _target = null; _vis = 0; }
            if (C.Bar && pointed != null && lower && pointed != _target && _owner == null && pointed.Playable && pointed.HomeInWorld)
            {
                // the lower part of another screen: the bar moves there and fades in afresh
                _vis = 0;
                _target = pointed;
                _dirty = true;
            }
            if (_target != null && ((pointed == _target && lower) || onBar || _owner != null)) _showUntil = Math.Max(_showUntil, now + 2);
            show = C.Bar && _target != null && now < _showUntil && _target.Overlay is { Visible: true } && _target.Playable;
        }
        _wasOn = th;
        _vis = Motion.Step(_vis, show, dt, 0.18f);                       // Look > Motion off: at once
        if (_vis <= 0 || _target == null)
        {
            Cancel();
            if (!th) _target = null;
            Ov.Hide();
            return;
        }

        if (th) MediaTick(now);          // theater mode: what plays, for the Cyber bar's media part (TheaterBarMedia.cs)
        // held (a slider dragged) or just stepped with the stick: the bar keeps its place and layout
        bool held = (_ptr.Down || now < _holdUntil) && _owner != null;
        if (th && !held && (!Same(choices) || _current != theater.Screen))
        {
            _choices.Clear();
            _choices.AddRange(choices);
            _current = theater.Screen;
            _dirty = true;
        }
        int key = (th ? 1000 + _choices.Count * 8 : 0) + (int)Theme.Retro + 100000 * Lang.Version + MediaKey;     // another style or language: another width
        if (key != _layoutKey && !held) Relayout(key);
        // changed elsewhere (grabbing the big screen, the settings window): the sliders follow
        if (SizeNow != _lastSize || C.Dim != _drawnDim || C.Curved != _drawnCurved || KbOn != _drawnKb) _dirty = true;
        if (_look != Theme.Version) { _look = Theme.Version; _dirty = true; }      // a new look (colours, font): drawn again

        // under the middle of the screen, the same height on your view whatever the distance, bent like the screen,
        // 1 cm nearer than it (a growing big screen must not cover it); it rises into place a little as it fades in.
        // Held, it stays where it is: a new size moves the screen's bottom edge, and the bar must not slide out from
        // under the laser. Fading out under a panel it stays where it was too (the panel may be on its way out).
        float e = 1 - MathF.Pow(1 - _vis, 3);
        if (!held && (th || show))
        {
            Matrix4x4 sw;
            float swidth, scurv;
            if (th) { sw = _view.World; swidth = _view.Width; scurv = _view.Curvature; }
            else { sw = _target.WorldTransform; swidth = _target.Overlay.Width; scurv = _target.Overlay.Curvature; }
            var ts = _target.TextureSize;
            float screenH = swidth * ts.Y / Math.Max(1, ts.X);
            float dist = vr.HmdValid ? Vector3.Distance(vr.Hmd.Translation, sw.Translation) : 2.5f;
            float hM = HeightAt(dist), wM = hM * _tw / _th;
            _world = Matrix4x4.CreateTranslation(0, -(screenH / 2 + hM * 0.35f + hM / 2) - (1 - e) * hM * 0.4f, 0.01f) * sw;
            Ov.SetWidth(wM);
            Ov.SetCurvature(scurv > 0 && swidth > 0 ? Math.Clamp(wM * scurv / swidth, 0, 1) : 0);
        }
        Ov.SetAbsolute(_world);
        Ov.SetAlpha(e);
        // a style's opening / closing (the Mac's Control Strip sliding) shows on every frame of the fade
        if (_dirty || _ui.Animating || now >= _wake || (_vis < 1 && Theme.RetroSkin.AnimatesFade))
        {
            Draw();
            Ov.SetTexture(_surf.Tex);
            _retired?.Dispose();
            _retired = null;
            _dirty = _ui.Interacting || _ui.Changed;
            _wake = AnimWake.Take();
        }
        else if (MediaPartUpdate()) Ov.SetTexture(_surf.Tex);      // only the media readout moved on (TheaterBarMedia.cs)
        Ov.Show();
    }

    ITheaterScreen _current;

    bool Same(List<ITheaterScreen> l)
    {
        if (l.Count != _choices.Count) return false;
        for (int i = 0; i < l.Count; i++) if (l[i] != _choices[i]) return false;
        return true;
    }

    // the width of everything right of the screen buttons / the Theater button
    float SettingsWidth95() => 8 + DimW95 + Track95W + 8 + SizeW95 + Track95W + 8 + 19 + _ui.LabelWidth95("Curved") + 10;

    // the trackbars' labels: as wide as their words (the 1995 sizes at least - the English bar keeps its places)
    float DimW95 => Lang.English ? 26 : MathF.Max(26, MathF.Ceiling(_ui.LabelWidth95("&Dim:")) + 4);
    float SizeW95 => Lang.English ? 30 : MathF.Max(30, MathF.Ceiling(_ui.LabelWidth95("&Size:")) + 4);

    void Relayout(int key)
    {
        _layoutKey = key;
        int n = _choices.Count;
        if (Theme.Classic)
        {
            _w95 = (int)MathF.Ceiling(4 + (_theater ? n * (Btn95W + 3) : Go95W + 3) + SettingsWidth95() + Kb95W + 3 + (_theater ? Exit95W + 3 : 0) + 4);
            SkinTheaterWidth(ref _w95);     // a style laying the bar out its own way (ScreenHooks.cs)
            _tw = _w95 * 2; _th = H95 * 2;
        }
        else
        {
            _tw = Pad + (_theater ? n * (ChipW + Gap) : GoW + Gap) + MediaW + SliderW + Gap + SliderW + Gap + CurveW + Gap + (H - 2 * Pad) +
                  (_theater ? Gap + (H - 2 * Pad) : 0) + Pad;      // MediaW: Cyber's media part in theater mode (TheaterBarMedia.cs), else 0
            _th = H;
        }
        _retired?.Dispose();
        _retired = Ov != null ? _surf : null;           // the overlay still shows it until the next SetTexture
        if (Ov == null) _surf?.Dispose();
        _surf = new Surface(_g, _tw, _th);
        _canvas95?.Dispose();
        _canvas95 = null;
        Ov?.SetMouseScale(_tw, _th);
        _dirty = true;
    }

    // ---------------- drawing ----------------

    // the names on the buttons; two monitors of the same model get a number (left to right)
    string Label(int i)
    {
        string name = _choices[i].TheaterLabel;
        int same = 0, nth = 0;
        for (int j = 0; j < _choices.Count; j++)
            if (_choices[j].TheaterLabel == name) { same++; if (j <= i) nth++; }
        if (same <= 1) return name;
        if (!_numbered.TryGetValue((name, nth), out var l)) _numbered[(name, nth)] = l = $"{name} {nth}";
        return l;
    }

    // the labels made to fit, kept: cutting a long window title made two strings (and a measured width) per letter, every frame
    readonly Dictionary<(string, int), string> _numbered = new();
    readonly Dictionary<(string s, float w, float size, FontWeight wt, bool px), string> _fitted = new();
    int _fittedFor = -1;

    bool Fitted((string, float, float, FontWeight, bool) key, out string s)
    {
        int ver = Theme.Version * 1000 + Lang.Version;          // another look, font or language: measured again
        if (ver != _fittedFor || _fitted.Count > 64) { _fitted.Clear(); _numbered.Clear(); _fittedFor = ver; }
        return _fitted.TryGetValue(key, out s);
    }

    void Draw()
    {
        if (Theme.Classic) Draw95();
        else if (Theme.Cyber) DrawCyber();          // a HUD strip (TheaterBarCyber.cs)
        else DrawModern();
    }

    // the settings: in theater mode the size is the big screen's own (grabbing changes it too)
    float SizeNow => _theater && _view != null ? _view.Size : C.Width;

    void DrawModern()
    {
        _ui.Begin(_ptr, 1);
        _p.Begin(_surf.Bitmap, 0x00000000);
        _p.Fill(new RectangleF(0, 0, _tw, _th), Theme.OverlayBg, _th / 2f);
        _p.Stroke(new RectangleF(1, 1, _tw - 2, _th - 2), Theme.PanelLine, 2, _th / 2f - 1);
        float x = Pad, ch = _th - 2 * Pad;
        ITheaterScreen pick = null;
        bool go = false, exit = false;
        if (_theater)
        {
            for (int i = 0; i < _choices.Count; i++, x += ChipW + Gap)
            {
                var r = new RectangleF(x, Pad, ChipW, ch);
                var s = _choices[i];
                bool on = s == _current;
                string id = Ui.Id("thb", i);
                if (_ui.Clickable(id, r, out bool hover, out bool held) && !on) pick = s;
                // the lit one moves over with a short fade; hover lightens, a press dips
                float a = _ui.Anim(Ui.Id(id, "#a"), on ? 1 : 0, 14), hv = _ui.Anim(Ui.Id(id, "#h"), hover ? 1 : 0, 18), pr = _ui.Anim(Ui.Id(id, "#p"), held ? 1 : 0, 30);
                uint bg = Theme.Mix(Theme.Mix(Theme.Panel3, Theme.Accent, a), 0xFFFFFFFF, 0.12f * hv * (1 - a));
                bg = Theme.Mix(bg, 0xFF000000, 0.2f * pr);
                uint fg = Theme.Mix(Theme.Text, Theme.OnAccent, a);
                r = RectangleF.Inflate(r, -ChipW * 0.02f * pr, -ch * 0.04f * pr);
                _p.Fill(r, bg, r.Height / 2);
                _p.Icon(s is WindowPanel ? Theme.IcoWindow : Theme.IcoMonitor, new RectangleF(r.X + 14, r.Y, 30, r.Height), fg, 19);
                _p.Text(Ellipsize(Label(i), r.Width - 66, 17, FontWeight.SemiBold), new RectangleF(r.X + 50, r.Y, r.Width - 62, r.Height), fg, 17, FontWeight.SemiBold);
            }
        }
        else
        {
            // the Theater button: the main thing here, in the accent colour
            var r = new RectangleF(x, Pad, GoW, ch);
            go = _ui.Clickable("thb.go", r, out bool hover, out bool held);
            float hv = _ui.Anim("thb.go#h", hover ? 1 : 0, 18), pr = _ui.Anim("thb.go#p", held ? 1 : 0, 30);
            uint bg = Theme.Mix(Theme.Mix(Theme.Accent, 0xFFFFFFFF, 0.14f * hv), 0xFF000000, 0.2f * pr);
            r = RectangleF.Inflate(r, -GoW * 0.02f * pr, -ch * 0.04f * pr);
            _p.Fill(r, bg, r.Height / 2);
            _p.Icon("\uE8B2", new RectangleF(r.X + 16, r.Y, 30, r.Height), Theme.OnAccent, 19);
            _p.Text("Theater", new RectangleF(r.X + 54, r.Y, r.Width - 62, r.Height), Theme.OnAccent, 18, FontWeight.SemiBold);
            x += GoW + Gap;
        }
        // the settings, on a slightly lighter pill
        var g = new RectangleF(x, Pad, SliderW + Gap + SliderW + Gap + CurveW, ch);
        _p.Fill(g, Theme.WithAlpha(Theme.Panel3, 0.55f), ch / 2);
        bool changed = false;
        float dim = C.Dim;
        if (_ui.Slider("thb.dim", new RectangleF(g.X + 14, Pad + 2, SliderW - 14, ch - 4), "Dim", ref dim, 0, 0.9f, v => $"{v * 100:0} %"))
        { C.Dim = dim; changed = true; }
        float size = SizeNow;
        if (_ui.Slider("thb.size", new RectangleF(g.X + SliderW + Gap, Pad + 2, SliderW, ch - 4), "Size", ref size, Theater.MinWidth, Theater.MaxWidth, v => $"{v:0.0} m"))
        { C.Width = size; changed = true; }
        bool curved = C.Curved;
        if (_ui.Toggle("thb.curve", new RectangleF(g.X + 2 * (SliderW + Gap), Pad, CurveW - 10, ch), "Curved", ref curved))
        { C.Curved = curved; changed = true; }
        x = g.Right + Gap;
        // the keyboard: lit while it is shown
        bool kbOn = KbOn;
        var kr = new RectangleF(x, Pad, ch, ch);
        bool kb = _ui.Clickable("thb.kb", kr, out bool kh, out bool kheld);
        float ka = _ui.Anim("thb.kb#a", kbOn ? 1 : 0, 14), khv = _ui.Anim("thb.kb#h", kh ? 1 : 0, 18), kp = _ui.Anim("thb.kb#p", kheld ? 1 : 0, 30);
        uint kbg = Theme.Mix(Theme.Mix(Theme.Mix(Theme.Panel3, Theme.Accent, ka), 0xFFFFFFFF, 0.12f * khv * (1 - ka)), 0xFF000000, 0.2f * kp);
        _p.Fill(RectangleF.Inflate(kr, -ch * 0.04f * kp, -ch * 0.04f * kp), kbg, ch / 2);
        _p.Icon(Theme.IcoKeyboard, kr, Theme.Mix(Theme.Text, Theme.OnAccent, ka), 20);
        x += ch + Gap;
        if (_theater)
        {
            // Exit: everything goes back
            var xr = new RectangleF(x, Pad, ch, ch);
            exit = _ui.Clickable("thb.x", xr, out bool xh, out bool xheld);
            float xa = _ui.Anim("thb.x#h", xh ? 1 : 0, 18), xp = _ui.Anim("thb.x#p", xheld ? 1 : 0, 30);
            _p.Fill(RectangleF.Inflate(xr, -ch * 0.04f * xp, -ch * 0.04f * xp), Theme.Mix(Theme.Mix(Theme.Panel3, 0xFFFFFFFF, 0.12f * xa), Theme.Accent, xp), ch / 2);
            _p.Icon("\uE711", xr, Theme.Mix(Theme.Text, Theme.OnAccent, xp), 18);
        }
        _p.End();
        _ui.End();
        Act(pick, go, exit, changed, kb);
    }

    bool KbOn => KeyboardShown?.Invoke() == true;

    void Draw95()
    {
        if (SkinTheater()) return;          // a style's own bar (ScreenHooks.cs)
        _canvas95 ??= new Surface(_p.Gpu, _w95, H95);
        _ui.Begin(_ptr, 2);
        _ui.Canvas95 = new SizeF(_w95, H95);
        _p.BeginPixel(_canvas95.Bitmap, 0x00000000);
        var strip = new RectangleF(0, 0, _w95, H95);
        _p.Fill(strip, Theme.W95Face);
        _p.Bevel(strip, Painter.Edge.Raised);
        float x = 4, by = (H95 - 22) / 2;
        ITheaterScreen pick = null;
        bool go = false, exit = false;
        if (_theater)
        {
            for (int i = 0; i < _choices.Count; i++, x += Btn95W + 3)
            {
                var s = _choices[i];
                bool on = s == _current;
                if (_ui.Button95(Ui.Id("thb95", i), new RectangleF(x, by, Btn95W, 22), Fit95(Label(i), Btn95W - 30), true, false, on,
                        s is WindowPanel ? W95Icons.Id.Window : W95Icons.Id.Computer) && !on)
                    pick = s;
            }
        }
        else
        {
            go = _ui.Button95("thb95.go", new RectangleF(x, by, Go95W, 22), "&Theater", true, true, false, W95Icons.Id.Theater);
            x += Go95W + 3;
        }
        // an etched divider, then the settings: two trackbars and a check box
        _p.Fill(new RectangleF(x + 2, 4, 1, H95 - 8), 0xFF808080);
        _p.Fill(new RectangleF(x + 3, 4, 1, H95 - 8), 0xFFFFFFFF);
        x += 8;
        bool changed = false;
        _ui.Label95("&Dim:", new RectangleF(x, 10, DimW95, 15));
        x += DimW95;
        float dim = C.Dim;
        if (_ui.Track95("thb95.dim", new RectangleF(x, 4, Track95W, 28), ref dim, 0, 0.9f, true, 10)) { C.Dim = dim; changed = true; }
        x += Track95W + 8;
        _ui.Label95("&Size:", new RectangleF(x, 10, SizeW95, 15));
        x += SizeW95;
        float size = SizeNow;
        if (_ui.Track95("thb95.size", new RectangleF(x, 4, Track95W, 28), ref size, Theater.MinWidth, Theater.MaxWidth, true, 10)) { C.Width = size; changed = true; }
        x += Track95W + 8;
        bool curved = C.Curved;
        if (_ui.Check95("thb95.curve", x, 11, "&Curved", ref curved)) { C.Curved = curved; changed = true; }
        x += 19 + _ui.LabelWidth95("Curved") + 10;
        // the keyboard: a push button that stays pushed in while the keyboard is shown
        bool kb = _ui.Button95("thb95.kb", new RectangleF(x, by, Kb95W, 22), "&Keyboard", true, false, KbOn, W95Icons.Id.Keyboard);
        x += Kb95W + 3;
        if (_theater) exit = _ui.Button95("thb95.x", new RectangleF(x, by, Exit95W, 22), "Exit");
        _p.End();
        _p.Begin(_surf.Bitmap, 0x00000000);
        _p.ImageNearest(_canvas95.Bitmap, new RectangleF(0, 0, _tw, _th));
        _p.End();
        _ui.End();
        Act(pick, go, exit, changed, kb);
    }

    void Act(ITheaterScreen pick, bool go, bool exit, bool changed, bool kb)
    {
        _lastSize = SizeNow;
        _drawnDim = C.Dim;
        _drawnCurved = C.Curved;
        _drawnKb = KbOn;
        if (kb) { OnKeyboard?.Invoke(); _dirty = true; }
        if (changed) { OnSettings?.Invoke(); _dirty = true; }
        if (pick != null) { OnSwitch?.Invoke(pick); _dirty = true; }
        else if (exit) OnExit?.Invoke();
        else if (go && _target != null) OnTheater?.Invoke(_target);
    }

    string Ellipsize(string s, float maxW, float size, FontWeight wt)
    {
        if (s != null && Fitted((s, maxW, size, wt, false), out var done)) return done;
        string key = s;
        s = Lang.T(s);                     // cut in the language picked
        if (string.IsNullOrEmpty(s) || _p.MeasureWidth(s, size, wt) <= maxW) return key == null ? s : _fitted[(key, maxW, size, wt, false)] = s;
        int n = s.Length;
        while (n > 1 && _p.MeasureWidth(s[..n] + "…", size, wt) > maxW) n--;
        if (n > 1 && char.IsHighSurrogate(s[n - 1])) n--;
        return _fitted[(key, maxW, size, wt, false)] = s[..n].TrimEnd() + "…";
    }

    string Fit95(string s, float w)
    {
        if (s != null && Fitted((s, w, 0, FontWeight.Normal, true), out var done)) return done;
        string key = s;
        s = Lang.T(s);                     // cut in the language picked
        if (string.IsNullOrEmpty(s) || _ui.Width95(s) <= w) return key == null ? s : _fitted[(key, w, 0, FontWeight.Normal, true)] = s;
        int n = s.Length;
        while (n > 1 && _ui.Width95(s[..n] + "...") > w) n--;
        return _fitted[(key, w, 0, FontWeight.Normal, true)] = s[..n].TrimEnd() + "...";
    }

    // ---------------- input: one hand at a time; the stick moves the slider under the laser ----------------

    public VrOverlay Overlay => Ov;
    public bool Interactive => Ov != null && Ov.Visible && _vis > 0.8f;
    public bool AcceptsFrom(Hand h) => true;
    public Vector2 TextureSize => new(Math.Max(1, _tw), Math.Max(1, _th));

    // how tall the bar is under a screen this far from you (the same height on your view); its bottom is 1.35 of it under the screen
    public static float HeightAt(float dist) => Math.Clamp(0.05f * dist, 0.04f, 0.4f);

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
        _wheelAcc = 0;
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

    // whole notches only (the stick gives a little every frame): one step of a slider each
    public void PointerScroll(Hand h, float notches, Vector2 px)
    {
        if (_owner != h) return;
        _wheelAcc += notches;
        int n = (int)_wheelAcc;
        if (n == 0) return;
        _wheelAcc -= n;
        _ptr.Wheel += n;
        _holdUntil = Clock.Now + 0.4;
        _dirty = true;
    }

    // it rides under its screen: grab the screen to move both
    public bool Grabbable => false;
    public Matrix4x4 WorldTransform => _world;
    public void GrabBegin(bool detach) { }
    public void GrabMove(Matrix4x4 world) { }
    public void GrabStickX(float v, float dt) { }
    public void GrabTwoHandBegin() { }
    public void GrabTwoHandScale(float ratio) { }
    public bool GrabSnapPreview => false;
    public void GrabEnd() { }

    // --snapshot (Cyber's motion frames): how far it has faded in
    public float VisForTest { set => _vis = value; }

    // used by --theatertest: the bar without VR - theater mode (switcher) when current != null, else a screen's bar
    public Surface DrawFor(List<ITheaterScreen> choices, ITheaterScreen current, TheaterView view = null)
    {
        _theater = current != null;
        _view = view;
        _choices.Clear();
        _choices.AddRange(choices);
        _current = current;
        int key = (_theater ? 1000 + _choices.Count * 2 : 0) + (Theme.Classic ? 1 : 0) + MediaKey;
        if (key != _layoutKey) Relayout(key);
        Draw();
        return _surf;
    }

    public void Dispose()
    {
        DestroyOverlay();
        _surf?.Dispose();
        _retired?.Dispose();
        _canvas95?.Dispose();
        _read?.Dispose();
        _line?.Dispose();
    }
}
