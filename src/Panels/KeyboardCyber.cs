using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// The keyboard in the Cyber style: a HUD input console, not the modern keyboard with cut corners (Erik, 1.13.2: "only the key shapes
// changed"). The same keys in the same places, drawn the watch's way:
//  - the frame: near black, olive, corners cut (the cuts yellow); a header strip - the yellow INPUT // KEYBOARD tag, the layout and
//    the mode as monospace readouts, a lamp for each modifier (lit cyan while it is on), a hazard chip at the end;
//  - the top bar: HUD buttons (a cyan-edged plate, the icon cyan, the name in condensed capitals; Chatbox lit cyan while on, Send
//    the yellow one);
//  - a key: a dark plate, its bottom-right corner cut, a thin dark outline, a small olive tick in its top-left corner; the letter in
//    Bahnschrift's condensed bold, the shifted symbol and the AltGr one as small monospace legends (grey / cyan); the keys that do
//    something rather than type (Esc, Tab, Caps, Shift, Ctrl, Win, Alt, AltGr, Del, Enter, Backspace, the arrows) in cyan
//    monospace capitals, the F keys grey; the space bar a row of segments;
//  - a modifier once: a cyan edge and brackets; locked (and Caps on): a cyan block with a LOCK code.
// Motion (CyberFx; Look > Motion): a key under the laser - a cyan edge, brackets closing in; pressed - a cyan block; let go - the
// cyan fades (0.25 s) while its brackets fly out and the legend splits red / cyan for a moment (the glitch; Full); a modifier
// latching - its brackets snap in from 10 px out (once) or the cyan wipes in from the left (locked), 0.22 s (Reduced too); the
// keyboard opening - the corner brackets snap out from the middle, a scan line sweeps down and every row it passes flashes cyan
// (Full; Reduced: the brackets); closing - it folds into a line (Full). Nothing moves at rest: no frame of its own then.
public sealed partial class KeyboardPanel
{
    // the Cyber rows: a header strip on top (46 px), the top bar and the key rows a little lower (676 px tall in every style)
    const float CyHead = 46, CyBar = 74, CyU = 89.6f;
    bool _builtCyber;

    void RebuildLayout()
    {
        ReleaseAll();
        _keys.Clear(); _barNormal.Clear(); _barChat.Clear();
        Array.Clear(_hover); Array.Clear(_down); Array.Clear(_held95);
        _flash.Clear();
        _cyLatch.Clear();
        _cyBaseOk = false;
        BuildLayout();
        _hkl = IntPtr.Zero;            // the key caps again (Draw)
        _dirty = true;
    }

    // ---- the latch animation: when a key's modifier state last changed ----
    readonly Dictionary<Key, (int state, double at)> _cyLatch = new();
    const double CyLatchTime = 0.22;

    float CyLatch(Key k, int state, double now, out int prev)
    {
        prev = state;
        if (!_cyLatch.TryGetValue(k, out var l)) { _cyLatch[k] = (state, -10); return 1; }
        if (l.state != state) { prev = l.state; _cyLatch[k] = l = (state, now); }
        double t = now - l.at;
        if (!CyberFx.Any || t >= CyLatchTime) return 1;
        AnimWake.At(now);
        return CyberFx.EaseOut((float)(t / CyLatchTime));
    }

    static readonly string CyGlobe = ((char)0xE774).ToString();
    static readonly string[] CyMods = { "shift", "ctrl", "alt", "altgr", "win" };
    static readonly string[] CyModNames = { "SHIFT", "CTRL", "ALT", "ALTGR", "WIN" };

    // a key that does something rather than type: its legend in cyan
    static bool CyCommand(Key k) => k.Kind is Kind.Modifier or Kind.Caps || k.Icon != null ||
                                    k.Fixed is "Esc" or "Tab" or "Del";

    void DrawCyber()
    {
        if (_builtCyber != Theme.Cyber) RebuildLayout();
        if (_hkl == IntPtr.Zero)
        {
            if (BuiltIn) { _hkl = (IntPtr)(-1); ApplyBuiltInLabels(); }
            else RefreshLabels(Native.ForegroundLayout());
        }
        double now = Clock.Now;
        var all = new RectangleF(0, 0, TW, TH);
        double shownAt = Ov != null ? Ov.ShownAt : OpenAtForTest;
        float vis = Ov != null ? _vis : 1;
        bool closing = Ov != null && !(C.Visible && C.Place.M != null);
        bool shift = _mods["shift"] > 0, altgr = _mods["altgr"] > 0;
        _cyCaps = Caps;
        FadeGlow();
        for (int s = 0; s < _hover.Length; s++)
            if (_cyHoverKey[s] != _hover[s]) { _cyHoverKey[s] = _hover[s]; _cyHoverAt[s] = now; }
        // the picture at rest, kept: made again only when it has changed; the opening / closing and the --snapshot's text checks draw
        // all of it, as before
        bool live = CyberReveal.Running(shownAt, vis, closing) || DrawsAll;
        if (!live)
        {
            int sig = CyBaseSig(shift, altgr);
            if (_cyBase == null || !_cyBaseOk || sig != _cyBaseSig) CyDrawBase(shift, altgr, now, sig);
        }
        _p.Begin(Surf.Bitmap, 0x00000000);
        var rev = CyberReveal.Begin(_p, all, shownAt, vis, closing);
        if (live)
        {
            CyberHud.ConsoleFrame(_p, all, Theme.OverlayBg, 22);
            CyHeader();
        }
        else _p.Image(_cyBase.Bitmap, all);
        if (_chat) CyChatField(now);
        // the opening's scan line: the rows it has just passed flash (Full)
        float scanY = rev.Kind == 1 && CyberFx.Full ? CyberReveal.ScanY(rev) : -1;
        int mode = live ? KeyLive : KeyOver;
        foreach (var k in _keys) CyKey(k, shift, altgr, now, scanY, mode);
        foreach (var k in _chat ? _barChat : _barNormal) CyKey(k, shift, altgr, now, scanY, mode);
        CyberReveal.End(_p, rev);
        _p.End();
    }

    // ---- the picture at rest, kept ----
    // The frame, the header and every key as it looks with nothing on it, in a texture of its own: made again when that picture
    // changes (a modifier, Caps, the layout, Chatbox, the look, the language). A frame is that texture, the Chatbox line and only the
    // keys doing something over it - under a laser, held, fading out, latching: ~0.2 ms instead of ~2.5 (Erik, 10-16: "better
    // performance overall"; the settings' Keyboard tab draws the keyboard in every frame of its own). The modern keyboard keeps its
    // picture in the same texture, under the same signature (KeyboardPanel.Draw, 10-18).
    Surface _cyBase;
    int _cyBaseSig;
    bool _cyBaseOk, _cyCaps;
    readonly List<Key> _flashKeys = new();          // the glowing keys, copied before their glow fades (every style's frame)
    readonly Key[] _cyHoverKey = new Key[3];
    readonly double[] _cyHoverAt = new double[3];

    // the style changed a while ago (OrbitApp.GpuSweep, Erik 10-24: Orbit's VRAM): what the style in use never draws let go, made again by
    // the next draw that wants it - in a retro style the kept picture (the modern and the Cyber keyboard's, 10-18), else the half-size canvas
    public void ReleaseStyle()
    {
        if (Theme.Classic) { _cyBase?.Dispose(); _cyBase = null; _cyBaseOk = false; }
        else { _canvas95?.Dispose(); _canvas95 = null; }
    }

    int CyBaseSig(bool shift, bool altgr)
    {
        var h = new HashCode();
        h.Add(Theme.Version);
        h.Add(Lang.Code);
        h.Add(shift);
        h.Add(altgr);
        h.Add(_cyCaps);
        h.Add(_chat);
        foreach (var v in _mods.Values) h.Add(v);
        foreach (var k in _keys) { h.Add(k.Normal); h.Add(k.Shifted); h.Add(k.AltGr); h.Add(k.Fixed); }
        return h.ToHashCode();
    }

    void CyDrawBase(bool shift, bool altgr, double now, int sig)
    {
        _cyBase ??= new Surface(_p.Gpu, TW, TH);
        _p.Begin(_cyBase.Bitmap, 0x00000000);
        CyberHud.ConsoleFrame(_p, new RectangleF(0, 0, TW, TH), Theme.OverlayBg, 22);
        CyHeader();
        foreach (var k in _keys) CyKey(k, shift, altgr, now, -1, KeyBase);
        foreach (var k in _chat ? _barChat : _barNormal) CyKey(k, shift, altgr, now, -1, KeyBase);
        _p.End();
        _cyBaseSig = sig;
        _cyBaseOk = true;
    }

    // ---- the header strip: the tag, the readouts, the modifier lamps, a hazard chip ----
    void CyHeader()
    {
        float y = Pad + 2, h = CyHead - 12, x = Pad + 6;
        float tw = CyberHud.Tag(_p, x, y + (h - 28) / 2, "INPUT // KEYBOARD", size: 19, h: 28);
        x += tw + 18;
        float by = y + h / 2 + 7;
        string lang = "--";
        foreach (var k in _keys) if (k.Action == "lang") { lang = k.Fixed ?? "--"; break; }
        x = CyReadout(x, by, Lang.C("kbd", "LAYOUT"), lang, CyberHud.White);     // the keys' layout (not the watch's: "kbd|LAYOUT")
        x = CyReadout(x, by, "MODE", _chat ? "CHATBOX" : "TYPE", _chat ? CyberHud.Cyan : CyberHud.White);
        // the lamps: right-aligned, before the hazard chip
        float hx = TW - Pad - 6 - 96;
        var hz = new RectangleF(hx, y + h / 2 - 6, 96, 12);
        _p.Fill(hz, CyberHud.Yellow);
        CyberHud.Hazard(_p, hz, CyberHud.Ink, 12);
        float lx = hx - 14;
        bool caps = Caps;
        for (int i = CyMods.Length; i >= 0; i--)
        {
            string name = i == CyMods.Length ? "CAPS" : CyModNames[i];
            int st = i == CyMods.Length ? (caps ? 2 : 0) : _mods[CyMods[i]];
            float w = CyberHud.MonoWidth(_p, name, 16, FontWeight.Bold) + 20;
            var r = new RectangleF(lx - w, y + (h - 28) / 2, w, 28);
            if (st == 2) _p.FillCut(r, CyberHud.Cyan, 0, 0, 6, 0);
            else if (st == 1) _p.FillCut(r, Theme.WithAlpha(CyberHud.Cyan, 0.16f), 0, 0, 6, 0);
            _p.StrokeCut(RectangleF.Inflate(r, -0.6f, -0.6f), st > 0 ? CyberHud.Cyan : CyberHud.Line, 1.25f, 0, 0, 5.7f, 0);
            _p.Text(name, r, st == 2 ? CyberHud.Ink : st == 1 ? CyberHud.Cyan : CyberHud.Faint, 16, FontWeight.Bold, TextAlignment.Center,
                ParagraphAlignment.Center, CyberHud.Mono);
            lx -= w + 6;
        }
        _p.Fill(new RectangleF(Pad, Pad + CyHead - 4, TW - 2 * Pad, 1), CyberHud.Line);
        for (int i = 0; i < 3; i++) _p.Fill(new RectangleF(Pad + 2 + i * 6, Pad + CyHead - 7, 4, 4), i == 0 ? CyberHud.Yellow : CyberHud.Edge);
    }

    float CyReadout(float x, float by, string key, string value, uint c)
    {
        CyberHud.MonoAt(_p, key, x, by, 140, CyberHud.Grey, 18, FontWeight.SemiBold);
        x += CyberHud.MonoWidth(_p, key, 18, FontWeight.SemiBold) + 10;
        CyberHud.MonoAt(_p, value, x, by, 220, c, 18, FontWeight.Bold);
        return x + CyberHud.MonoWidth(_p, value, 18, FontWeight.Bold) + 30;
    }

    // ---- the Chatbox line: a dark field with a cyan edge and brackets, a block cursor ----
    void CyChatField(double now)
    {
        var f = _chatField;
        _p.FillCut(f, CyberHud.Page, 0, 0, 10, 0);
        _p.StrokeCut(RectangleF.Inflate(f, -0.75f, -0.75f), CyberHud.Cyan, 1.5f, 0, 0, 9.6f, 0);
        CyberHud.Brackets(_p, f, CyberHud.Cyan, 10, 4, 2);
        string text = _chatText.ToString();
        var tr = new RectangleF(f.X + 18, f.Y, f.Width - 130, f.Height);
        if (text.Length == 0)
            _p.Text("Type a message for the VRChat chatbox, Enter sends", tr, CyberHud.Faint, 18, FontWeight.Normal, TextAlignment.Leading,
                ParagraphAlignment.Center, CyberHud.Mono);
        else
        {
            string shown = text;
            while (shown.Length > 1 && _p.MeasureWidth(shown, 26) > tr.Width - 20) shown = shown[1..];
            if (shown.Length < text.Length) shown = "…" + shown;
            _p.Text(shown, tr, CyberHud.White, 26);
            if (_blink == 0)
            {
                float cx = tr.X + _p.MeasureWidth(shown, 26) + 4;
                _p.Fill(new RectangleF(cx, f.Y + 15, 12, f.Height - 30), CyberHud.Cyan);
            }
        }
        if (_cyCountLen != text.Length || _cyCount == null) { _cyCountLen = text.Length; _cyCount = $"{text.Length}/{ChatMax}"; }
        _p.Text(_cyCount, new RectangleF(f.Right - 120, f.Y, 104, f.Height), CyberHud.Grey, 15, FontWeight.SemiBold,
            TextAlignment.Trailing, ParagraphAlignment.Center, CyberHud.Mono);
    }

    string _cyCount;
    int _cyCountLen = -1;

    static readonly Vector2[] _cyTick = new Vector2[4];

    // how a key is drawn: as it is now; as it looks with nothing on it (the kept picture); over the kept picture - only when it is
    // doing something (nothing drawn otherwise)
    const int KeyLive = 0, KeyBase = 1, KeyOver = 2;

    // ---- one key ----
    void CyKey(Key k, bool shift, bool altgr, double now, float scanY, int mode)
    {
        bool rest = mode == KeyBase;
        int hs = rest ? -1 : Array.IndexOf(_hover, k), ds = rest ? -1 : Array.IndexOf(_down, k);
        bool hover = hs >= 0, down = ds >= 0;
        float glow = 0;
        if (!rest) _flash.TryGetValue(k, out glow);
        int modState = k.Kind == Kind.Modifier ? _mods[k.Mod] : k.Kind == Kind.Caps && _cyCaps ? 2 : k.Action == "chat" && _chat ? 2 : 0;
        float latch = !rest && (k.Kind is Kind.Modifier or Kind.Caps || k.Action == "chat") ? CyLatch(k, modState, now, out _) : 1;
        // the laser coming onto it: its brackets close in from outside (0.12 s); held: the cap sinks in (0.06 s) white-hot, a ring of
        // brackets kicking out (0.18 s, Full)
        float hin = hover && CyberFx.Any ? CyberFx.EaseOut(CyberFx.Ramp(now - _cyHoverAt[hs], 0, 0.12)) : 1;
        double dt = down ? now - _downAt[ds] : 10;
        float sink = down && CyberFx.Any ? CyberFx.EaseOut(CyberFx.Ramp(dt, 0, 0.06)) : 1;
        float kick = down && CyberFx.Full ? CyberFx.Ramp(dt, 0, 0.18) : 1;
        if (hin < 1 || sink < 1 || kick < 1) AnimWake.At(now);
        if (mode == KeyOver && !hover && !down && glow <= 0 && latch >= 1) return;
        bool action = k.Kind == Kind.Action && k.Action != "lang";
        bool primary = k.Action == "send";
        var kr = k.R;
        float cut = action ? 12 : 10;
        bool lit = down || modState == 2;
        // the plate (held: a dark socket the cap sits in)
        uint bg = down ? CyberHud.Page : primary ? CyberHud.Yellow : hover ? CyberHud.Card3 : CyberHud.Card2;
        if (!down && glow > 0) bg = Theme.Mix(bg, CyberHud.Cyan, glow * 0.6f);
        _p.FillCut(kr, bg, 0, 0, cut, 0);
        if (down)
        {
            _p.StrokeCut(RectangleF.Inflate(kr, -0.75f, -0.75f), CyberHud.Cyan, 1.5f, 0, 0, cut - 0.45f, 0);
            kr = RectangleF.Inflate(kr, -3 * sink, -3 * sink);
        }
        if (lit)
        {
            // held / locked: the cyan block (locking: it wipes in from the left; held: white-hot for its first moments)
            float wk = down ? 1 : latch;
            uint cc = down && kick < 1 ? Theme.Mix(CyberHud.White, CyberHud.Cyan, CyberFx.EaseOut(kick * 2)) : CyberHud.Cyan;
            if (wk < 1) _p.PushClip(new RectangleF(kr.X, kr.Y, kr.Width * wk, kr.Height));
            _p.FillCut(kr, cc, 0, 0, cut, 0);
            if (wk < 1) _p.PopClip();
        }
        // the row the opening's scan line just passed: a cyan flash
        if (scanY >= 0 && kr.Y < scanY)
        {
            float a = 1 - CyberFx.Clamp01((scanY - kr.Y) / 110f);
            if (a > 0.01f) _p.FillCut(kr, Theme.WithAlpha(CyberHud.Cyan, 0.4f * a), 0, 0, cut, 0);
        }
        // the edge
        if (!lit)
        {
            uint edge = hover ? CyberHud.Cyan : modState == 1 ? CyberHud.Cyan : action ? CyberHud.CyanDim : primary ? CyberHud.Yellow : CyberHud.Line;
            float ew = hover || modState == 1 ? 1.75f : 1.25f;
            _p.StrokeCut(RectangleF.Inflate(kr, -ew / 2, -ew / 2), edge, ew, 0, 0, cut - ew * 0.3f, 0);
        }
        // the tick in its top-left corner
        if (!lit && !primary)
        {
            uint tc = action ? CyberHud.CyanDim : CyberHud.Frame;
            _p.Fill(new RectangleF(kr.X + 5, kr.Y + 5, 7, 1.5f), Theme.WithAlpha(tc, 0.8f));
            _p.Fill(new RectangleF(kr.X + 5, kr.Y + 5, 1.5f, 7), Theme.WithAlpha(tc, 0.8f));
        }
        // a modifier on once: its brackets (snapping in when it latches); hover: brackets closing in
        if (modState == 1) CyberHud.Brackets(_p, kr, CyberHud.Cyan, 10, 5 - 10 * (1 - latch), 2);
        else if (hover && !down) CyberHud.Brackets(_p, kr, Theme.WithAlpha(CyberHud.Cyan, 0.4f + 0.6f * hin), 9, 5 - 9 * (1 - hin), 2);
        if (modState == 2 && !down && k.Kind != Kind.Action)
            CyberHud.MonoAt(_p, "LOCK", kr.Right - 50, kr.Y + 20, 46, Theme.WithAlpha(CyberHud.Ink, 0.7f), 13, FontWeight.Bold);
        // held: the ring kicking out round the socket
        if (kick < 1)
            CyberFx.Corners(_p, k.R, Theme.WithAlpha(CyberHud.White, 1 - kick), 12, 2, 2 + 12 * CyberFx.EaseOut(kick));
        // let go: the brackets fly out as the glow fades, a scan line runs down the key (Full)
        if (glow > 0.01f && !down && Motion.Any)
        {
            CyberFx.Corners(_p, kr, Theme.WithAlpha(CyberHud.Cyan, glow), 10, 2, 1 + 8 * (1 - glow));
            if (CyberFx.Full)
            {
                float y = kr.Y + 4 + (kr.Height - 8) * (1 - glow), x1 = kr.Right - 4 - MathF.Max(0, y - (kr.Bottom - cut));
                _p.Fill(new RectangleF(kr.X + 4, y - 7, x1 - kr.X - 4, 6), Theme.WithAlpha(CyberHud.Cyan, 0.16f * glow));
                _p.Fill(new RectangleF(kr.X + 4, y - 1, x1 - kr.X - 4, 2), Theme.WithAlpha(CyberHud.White, 0.85f * glow));
            }
        }
        // the glitch: for the first moments after it is let go the legend splits red / cyan (Full)
        float split = glow > 0.55f && !down && CyberFx.Full ? (glow - 0.55f) / 0.45f * 3f : 0;
        CyLegend(k, kr, lit, primary, shift, altgr, split);
    }

    void CyLegend(Key k, RectangleF kr, bool lit, bool primary, bool shift, bool altgr, float split)
    {
        uint ink = lit || primary ? CyberHud.Ink : 0;
        if (k.Kind == Kind.Action)
        {
            if (k.Action == "lang")
            {
                // the globe and the layout's code
                float lw = CyberHud.MonoWidth(_p, k.Fixed ?? "", 21, FontWeight.Bold);
                float x0 = kr.X + (kr.Width - (26 + 6 + lw)) / 2;
                _p.Icon(CyGlobe, new RectangleF(x0, kr.Y, 26, kr.Height), ink != 0 ? ink : CyberHud.Cyan, 22);
                _p.Text(k.Fixed, new RectangleF(x0 + 32, kr.Y, lw + 6, kr.Height), ink != 0 ? ink : CyberHud.White, 21, FontWeight.Bold,
                    TextAlignment.Leading, ParagraphAlignment.Center, CyberHud.Mono);
                return;
            }
            string label = CyUpper(k.Fixed);
            float fs = 21;
            float tw = _p.MeasureWidth(label, fs, FontWeight.SemiBold);
            if (28 + 10 + tw > kr.Width - 20) { fs = 18; tw = _p.MeasureWidth(label, fs, FontWeight.SemiBold); }
            float x = kr.X + (kr.Width - (28 + 10 + tw)) / 2;
            _p.Icon(k.Icon, new RectangleF(x, kr.Y, 28, kr.Height), ink != 0 ? ink : CyberHud.Cyan, 23);
            _p.Text(label, new RectangleF(x + 38, kr.Y, tw + 6, kr.Height), ink != 0 ? ink : CyberHud.White, fs, FontWeight.SemiBold);
            return;
        }
        if (k.Icon != null)
        {
            CySplit(k.Icon, kr, ink != 0 ? ink : CyberHud.Cyan, 28, FontWeight.Normal, Theme.IconFont, split);
            return;
        }
        if (k.Fixed == " ")
        {
            // the space bar: a row of segments and its name
            var bar = new RectangleF(kr.X + kr.Width * 0.2f, kr.Y + kr.Height / 2 - 4, kr.Width * 0.6f, 8);
            CyberHud.Segments(_p, bar, lit ? 1 : 0, ink != 0 ? ink : CyberHud.Cyan, lit ? Theme.WithAlpha(CyberHud.Ink, 0.4f) : CyberHud.Track, 14, 4);
            _p.Text("SPACE", new RectangleF(kr.X, kr.Bottom - 28, kr.Width, 22), ink != 0 ? ink : CyberHud.Faint, 14, FontWeight.Bold, TextAlignment.Center,
                ParagraphAlignment.Center, CyberHud.Mono);
            return;
        }
        if (k.Fixed != null)
        {
            string s = CyUpper(k.Fixed);
            bool fkey = s.Length >= 2 && s[0] == 'F' && char.IsDigit(s[1]);
            uint c = ink != 0 ? ink : CyCommand(k) ? CyberHud.Cyan : fkey ? CyberHud.Grey : CyberHud.White;
            CySplit(s, new RectangleF(kr.X, kr.Y + 1, kr.Width, kr.Height), c, s.Length > 4 ? 19 : 22, FontWeight.Bold, CyberHud.Mono, split);
            return;
        }
        string main = altgr && k.AltGr != null ? k.AltGr : shift ? k.Shifted ?? k.Normal : k.Normal;
        if (main != null && main.Length == 1 && char.IsLetter(main[0]) && _cyCaps && !altgr)
            main = shift ? CyLower(main) : CyUpper(main);
        CySplit(main, new RectangleF(kr.X, kr.Y + 2, kr.Width, kr.Height - 2), ink != 0 ? ink : CyberHud.White, 40, FontWeight.SemiBold, Theme.Display, split);
        // the small legends: the shifted symbol top-left (grey), the AltGr one bottom-right (cyan)
        if (!shift && k.Shifted != null && k.Normal != null && !char.IsLetter(k.Normal[0]))
            _p.Text(k.Shifted, new RectangleF(kr.X + 14, kr.Y + 6, 34, 28), ink != 0 ? ink : CyberHud.Grey, 20, FontWeight.SemiBold, TextAlignment.Leading,
                ParagraphAlignment.Center, CyberHud.Mono);
        if (!altgr && k.AltGr != null)
            _p.Text(k.AltGr, new RectangleF(kr.Right - 40, kr.Bottom - 34, 30, 28), ink != 0 ? ink : Theme.WithAlpha(CyberHud.Cyan, 0.9f), 20,
                FontWeight.SemiBold, TextAlignment.Trailing, ParagraphAlignment.Center, CyberHud.Mono);
    }

    static RectangleF Off(RectangleF r, float dx) => new(r.X + dx, r.Y, r.Width, r.Height);

    // a legend centred in r, with a red ghost to the left and a cyan one to the right while it glitches
    void CySplit(string s, RectangleF r, uint c, float size, FontWeight w, string family, float split)
    {
        if (split > 0.3f)
        {
            _p.Text(s, Off(r, -split), CyberHud.Red, size, w, TextAlignment.Center, ParagraphAlignment.Center, family);
            _p.Text(s, Off(r, split), CyberHud.Cyan, size, w, TextAlignment.Center, ParagraphAlignment.Center, family);
        }
        _p.Text(s, r, c, size, w, TextAlignment.Center, ParagraphAlignment.Center, family);
    }

    // a legend in capitals / small letters, each made once (not a new string per key per frame)
    readonly Dictionary<string, string> _cyUpper = new(), _cyLower = new();

    string CyUpper(string s)
    {
        if (s == null) return null;
        if (!_cyUpper.TryGetValue(s, out var u)) { if (_cyUpper.Count > 400) _cyUpper.Clear(); _cyUpper[s] = u = s.ToUpperInvariant(); }
        return u;
    }

    string CyLower(string s)
    {
        if (!_cyLower.TryGetValue(s, out var u)) { if (_cyLower.Count > 400) _cyLower.Clear(); _cyLower[s] = u = s.ToLowerInvariant(); }
        return u;
    }

    // --animshot: key i let go just now (its glow)
    public void CyberFlashForTest(int i)
    {
        if (i >= 0 && i < _keys.Count) _flash[_keys[i]] = 1;
        _lastDt = 1 / 120f;                         // Draw fades the glow by the last Update's step: the tool's
    }

    // --animshot: where key i is (texture px)
    public RectangleF KeyRectForTest(int i) => i >= 0 && i < _keys.Count ? _keys[i].R : RectangleF.Empty;

    // the perf tool: key i's glow held at a value (0: none) - Draw fades it by the last Update's step, which is none here
    public void GlowForTest(int i, float glow)
    {
        if (i < 0 || i >= _keys.Count) return;
        if (glow > 0) _flash[_keys[i]] = glow; else _flash.Remove(_keys[i]);
        _lastDt = 0;
    }

    // --animshot: key i held down / let go - only its look (nothing is sent to Windows: the tool must not type)
    public void HoldForTest(int i, bool down)
    {
        if (i < 0 || i >= _keys.Count) return;
        if (down) { _down[2] = _keys[i]; _downAt[2] = Clock.Now; _flash[_keys[i]] = 1; }
        else _down[2] = null;
        _lastDt = 1 / 120f;
        _dirty = true;
    }

    // --animshot: a modifier's state (0 off, 1 once, 2 locked), as a click on it sets it
    public void ModForTest(string mod, int state) { _mods[mod] = state; _dirty = true; }

    public int KeyIndexForTest(string fixedOrMod)
    {
        for (int i = 0; i < _keys.Count; i++) if (_keys[i].Mod == fixedOrMod || _keys[i].Fixed == fixedOrMod) return i;
        return -1;
    }

    public double OpenAtForTest = -10;
}
