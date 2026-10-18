using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;
using Valve.VR;
using Vortice.DirectWrite;

namespace Orbit;

// Full keyboard overlay. Keys are sent as scan codes, so Windows applies the active layout;
// the key caps are read from that same layout (ToUnicodeEx), e.g. Hungarian QWERTZ.
// Shift/Ctrl/Alt/AltGr/Win: one click = applies to the next key, second click = locked, third = off.
// Top bar: Chatbox mode switch + shortcut buttons (select all / copy / paste / cut / undo / redo).
// In Chatbox mode the keys type into a local line instead of Windows; Enter sends it to the
// VRChat chatbox over OSC.
public sealed partial class KeyboardPanel : IPointerTarget, IDisposable
{
    public const int TW = 1480, TH = 676;
    const float U = 96, Gap = 8, Pad = 20, Bar = 84;
    const int ChatMax = 144;            // VRChat chatbox limit

    enum Kind { Normal, Modifier, Caps, Action }

    sealed class Key
    {
        public RectangleF R;
        public ushort Scan;
        public bool Ext;
        public Kind Kind;
        public string Mod;           // shift / ctrl / alt / altgr / win
        public string Fixed, Icon;   // fixed caption or icon instead of a layout character
        public string Normal, Shifted, AltGr;
        public string Action;        // top bar buttons
    }

    readonly List<Key> _keys = new();
    readonly List<Key> _barNormal = new(), _barChat = new();
    bool _chat;
    readonly StringBuilder _chatText = new();
    RectangleF _chatField;
    int _blink = -1;
    public Action<string> OnChatSend;
    public Action<bool> OnChatTyping;
    public bool ChatMode => _chat;
    public void ReloadLayout() { _hkl = IntPtr.Zero; _dirty = true; }
    public void PreviewChat(string text) { _chat = true; _chatText.Clear().Append(text); _blink = 0; _dirty = true; }
    readonly Painter _p;
    readonly ConfigStore _cfg;
    public readonly Surface Surf;
    public VrOverlay Ov { get; private set; }
    VrSession _vr;
    IntPtr _hkl;
    double _nextLayoutCheck;
    bool _dirty = true;
    int _themeVersion = -1;
    float _vis;                                              // show/hide animation 0..1
    double _wake = double.PositiveInfinity;                  // a style's animation wants its next frame then (AnimWake.cs)
    readonly System.Collections.Generic.Dictionary<Key, float> _flash = new();   // press glow per key
    float _lastDt = 1 / 90f;

    readonly Dictionary<string, int> _mods = new() { ["shift"] = 0, ["ctrl"] = 0, ["alt"] = 0, ["altgr"] = 0, ["win"] = 0 };   // 0 off, 1 once, 2 locked
    readonly Key[] _hover = new Key[3];      // per hand (+ index 2 = desktop preview)
    readonly Key[] _down = new Key[3];
    readonly double[] _downAt = new double[3], _repeatAt = new double[3];
    readonly bool[] _downChat = new bool[3];      // pressed while in Chatbox mode (nothing to release in Windows)
    readonly bool[] _downUnicode = new bool[3];   // typed as a character (built-in layout): nothing to release
    readonly ushort[] _downVk = new ushort[3];    // built-in layout + Ctrl/Alt/Win: the key went out by virtual key

    // ---- language ----
    // "windows": key caps and typing follow the foreground window's layout (scan codes).
    // "en-US": a built-in US English layout, characters typed as Unicode - works without adding
    // English to Windows. Shortcuts (Ctrl/Alt/Win + key) go out by the English letter's virtual key.
    bool BuiltIn => C.Language == "en-US";

    static readonly System.Collections.Generic.Dictionary<ushort, (string n, string s)> UsKeys = new()
    {
        [0x29] = ("`", "~"), [0x02] = ("1", "!"), [0x03] = ("2", "@"), [0x04] = ("3", "#"), [0x05] = ("4", "$"),
        [0x06] = ("5", "%"), [0x07] = ("6", "^"), [0x08] = ("7", "&"), [0x09] = ("8", "*"), [0x0A] = ("9", "("),
        [0x0B] = ("0", ")"), [0x0C] = ("-", "_"), [0x0D] = ("=", "+"),
        [0x10] = ("q", "Q"), [0x11] = ("w", "W"), [0x12] = ("e", "E"), [0x13] = ("r", "R"), [0x14] = ("t", "T"),
        [0x15] = ("y", "Y"), [0x16] = ("u", "U"), [0x17] = ("i", "I"), [0x18] = ("o", "O"), [0x19] = ("p", "P"),
        [0x1A] = ("[", "{"), [0x1B] = ("]", "}"), [0x2B] = ("\\", "|"),
        [0x1E] = ("a", "A"), [0x1F] = ("s", "S"), [0x20] = ("d", "D"), [0x21] = ("f", "F"), [0x22] = ("g", "G"),
        [0x23] = ("h", "H"), [0x24] = ("j", "J"), [0x25] = ("k", "K"), [0x26] = ("l", "L"), [0x27] = (";", ":"),
        [0x28] = ("'", "\""),
        [0x56] = ("\\", "|"), [0x2C] = ("z", "Z"), [0x2D] = ("x", "X"), [0x2E] = ("c", "C"), [0x2F] = ("v", "V"),
        [0x30] = ("b", "B"), [0x31] = ("n", "N"), [0x32] = ("m", "M"), [0x33] = (",", "<"), [0x34] = (".", ">"),
        [0x35] = ("/", "?"),
    };

    // US virtual key for a scan code (letters and digits; 0 = use the scan code)
    static ushort UsVk(ushort scan) =>
        UsKeys.TryGetValue(scan, out var c) && c.n.Length == 1 && char.IsLetterOrDigit(c.n[0]) ? char.ToUpperInvariant(c.n[0]) : (ushort)0;

    void ApplyBuiltInLabels()
    {
        foreach (var k in _keys)
        {
            if (k.Fixed != null || k.Icon != null) continue;
            if (UsKeys.TryGetValue(k.Scan, out var c)) { k.Normal = c.n; k.Shifted = c.s; }
            else { k.Normal = k.Shifted = null; }
            k.AltGr = null;
        }
        SetLanguageLabel("EN");
        _dirty = true;
    }

    void SetLanguageLabel(string code)
    {
        foreach (var k in _keys) if (k.Action == "lang") k.Fixed = code;
    }

    // The keys follow the keyboard LAYOUT (high word of the HKL), not the input language (low word):
    // "English (US) language + Hungarian keyboard" is 0x040E0409 and must read HU. Layout variants
    // (0xFxxx device ids) fall back to the language.
    static int LayoutLang(IntPtr hkl)
    {
        int hi = (int)(((long)hkl >> 16) & 0xFFFF), lo = (int)((long)hkl & 0xFFFF);
        if (hi == 0 || (hi & 0xF000) == 0xE000) return lo;              // none / legacy IME
        if ((hi & 0xF000) == 0xF000) return VariantLang(hi & 0x0FFF) ?? lo;   // e.g. "US-International", "Hungarian 101-key"
        return hi;
    }

    static readonly System.Collections.Generic.Dictionary<int, int?> _variants = new();

    // a variant layout's HKL holds its "Layout Id"; the KLID with that id names the layout's language
    static int? VariantLang(int layoutId)
    {
        if (_variants.TryGetValue(layoutId, out var cached)) return cached;
        int? found = null;
        try
        {
            using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts");
            if (root != null)
                foreach (var klid in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(klid);
                    if (k?.GetValue("Layout Id") is string id && int.TryParse(id, System.Globalization.NumberStyles.HexNumber, null, out int v) && v == layoutId
                        && int.TryParse(klid, System.Globalization.NumberStyles.HexNumber, null, out int kl))
                    {
                        found = kl & 0xFFFF;
                        break;
                    }
                }
        }
        catch { }
        _variants[layoutId] = found;
        return found;
    }

    static string LayoutCode(IntPtr hkl)
    {
        try { return new System.Globalization.CultureInfo(LayoutLang(hkl)).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch { return "??"; }
    }

    // the language key: installed Windows layouts, then the built-in English (if English isn't installed)
    void CycleLanguage()
    {
        var layouts = Native.InstalledLayouts();
        var options = new List<IntPtr>(layouts);
        bool englishInstalled = options.Exists(h => (LayoutLang(h) & 0x3FF) == 0x09);
        if (!englishInstalled) options.Add(IntPtr.Zero);        // Zero = built-in English
        int cur = BuiltIn ? options.IndexOf(IntPtr.Zero) : options.IndexOf(_hkl);
        var next = options[(cur + 1 + options.Count) % options.Count];
        if (next == IntPtr.Zero)
        {
            C.Language = "en-US";
            ApplyBuiltInLabels();
        }
        else
        {
            C.Language = "windows";
            Native.PostMessage(Native.GetForegroundWindow(), Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, next);
            RefreshLabels(next);
        }
        _cfg.MarkDirty();
    }
    readonly List<(ushort scan, bool ext)>[] _sentMods = { new(), new(), new() };

    KeyboardConfig C => _cfg.Data.Keyboard;

    public KeyboardPanel(Gpu g, Painter p, ConfigStore cfg)
    {
        _p = p; _cfg = cfg;
        Surf = new Surface(g, TW, TH);
        BuildLayout();
        _sounds.Warm();       // open the audio device in the background, before the first key
    }

    void BuildLayout()
    {
        // Cyber: a header strip over the top bar, the rows a little lower (KeyboardCyber.cs); every other style as it was
        _builtCyber = Theme.Cyber;
        float top = Pad + (_builtCyber ? CyHead : 0), barH = _builtCyber ? CyBar : Bar, uv = _builtCyber ? CyU : U;
        // top bar
        float bh = barH - Gap * 2;
        RectangleF Cell(float x, float w) => new(x + Gap / 2, top + Gap / 2, w - Gap, bh);
        Key ActionKey(string id, string label, string icon, float x, float w) =>
            new Key { Kind = Kind.Action, Action = id, Fixed = label, Icon = icon, R = Cell(x, w) };
        var chatKey = ActionKey("chat", "Chatbox", "\uE8BD", Pad, 220);
        _barNormal.Add(chatKey);
        _barChat.Add(chatKey);
        (string id, string label, string icon)[] shortcuts =
        {
            ("selall", "Select all", "\uE8B3"), ("copy", "Copy", "\uE8C8"), ("paste", "Paste", "\uE77F"),
            ("cut", "Cut", "\uE8C6"), ("undo", "Undo", "\uE7A7"), ("redo", "Redo", "\uE7A6"),
        };
        float sw = (TW - 2 * Pad - 220) / shortcuts.Length;
        for (int i = 0; i < shortcuts.Length; i++)
            _barNormal.Add(ActionKey(shortcuts[i].id, shortcuts[i].label, shortcuts[i].icon, Pad + 220 + i * sw, sw));
        _barChat.Add(ActionKey("send", "Send", "\uE724", TW - Pad - 200, 200));
        _chatField = Cell(Pad + 220, TW - 2 * Pad - 220 - 200);

        float y = top + barH;
        void Row(float h, params (float w, Action<Key> init)[] keys)
        {
            float x = Pad;
            foreach (var (w, init) in keys)
            {
                var k = new Key { R = new RectangleF(x + Gap / 2, y + Gap / 2, w * U - Gap, h - Gap) };
                init(k);
                _keys.Add(k);
                x += w * U;
            }
            y += h;
        }
        (float, Action<Key>) Sc(ushort sc, float w = 1) => (w, k => k.Scan = sc);
        (float, Action<Key>) Fx(string label, ushort sc, float w, bool ext = false, string icon = null) =>
            (w, k => { k.Scan = sc; k.Ext = ext; k.Fixed = label; k.Icon = icon; });
        (float, Action<Key>) Md(string label, string mod, ushort sc, float w, bool ext = false) =>
            (w, k => { k.Scan = sc; k.Ext = ext; k.Fixed = label; k.Kind = Kind.Modifier; k.Mod = mod; });

        var f = new List<(float, Action<Key>)> { Fx("Esc", 0x01, 1.25f) };
        ushort[] fsc = { 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58 };
        for (int i = 0; i < 12; i++) f.Add(Fx("F" + (i + 1), fsc[i], 1));
        f.Add(Fx("Del", 0x53, 1.75f, true));
        Row(uv * 0.75f, f.ToArray());

        var r1 = new List<(float, Action<Key>)> { Sc(0x29) };
        for (ushort s = 0x02; s <= 0x0D; s++) r1.Add(Sc(s));
        r1.Add(Fx(null, 0x0E, 2, false, Theme.IcoBack));
        Row(uv, r1.ToArray());

        var r2 = new List<(float, Action<Key>)> { Fx("Tab", 0x0F, 1.5f) };
        for (ushort s = 0x10; s <= 0x1B; s++) r2.Add(Sc(s));
        r2.Add(Sc(0x2B, 1.5f));
        Row(uv, r2.ToArray());

        var r3 = new List<(float, Action<Key>)> { (1.75f, k => { k.Scan = 0x3A; k.Fixed = "Caps"; k.Kind = Kind.Caps; }) };
        for (ushort s = 0x1E; s <= 0x28; s++) r3.Add(Sc(s));
        r3.Add(Fx(null, 0x1C, 2.25f, false, Theme.IcoEnter));
        Row(uv, r3.ToArray());

        var r4 = new List<(float, Action<Key>)> { Md("Shift", "shift", 0x2A, 1.25f), Sc(0x56) };
        for (ushort s = 0x2C; s <= 0x35; s++) r4.Add(Sc(s));
        r4.Add(Md("Shift", "shift", 0x36, 2.75f));
        Row(uv, r4.ToArray());

        Row(uv,
            Md("Ctrl", "ctrl", 0x1D, 1.25f), Md("Win", "win", 0x5B, 1.25f, true), Md("Alt", "alt", 0x38, 1.25f),
            (3.75f, k => { k.Scan = 0x39; k.Fixed = " "; }),
            (1f, k => { k.Kind = Kind.Action; k.Action = "lang"; k.Icon = "\uE774"; k.Fixed = "HU"; }),
            Md("AltGr", "altgr", 0x38, 1.25f, true), Md("Ctrl", "ctrl", 0x1D, 1.25f, true),
            Fx(null, 0x4B, 1, true, Theme.IcoLeft), Fx(null, 0x48, 1, true, Theme.IcoUp),
            Fx(null, 0x50, 1, true, Theme.IcoDown), Fx(null, 0x4D, 1, true, Theme.IcoRight));
    }

    // Reads the key caps from the layout of the foreground window (so it follows a layout switch).
    void RefreshLabels(IntPtr hkl)
    {
        _hkl = hkl;
        var st = new byte[256];
        var sb = new StringBuilder(8);
        string Char(uint vk, uint sc)
        {
            sb.Clear();
            int n = Native.ToUnicodeEx(vk, sc, st, sb, sb.Capacity, 4, hkl);   // 4 = don't touch the kernel key state
            if (n == 0) return null;
            string s = sb.ToString(0, n < 0 ? 1 : n);
            return s.Length > 0 && s[0] >= 0x20 ? s : null;
        }
        foreach (var k in _keys)
        {
            if (k.Fixed != null || k.Icon != null) continue;
            uint vk = Native.MapVirtualKeyEx(k.Scan, 3, hkl);
            Array.Clear(st);
            k.Normal = Char(vk, k.Scan);
            st[0x10] = 0x80;
            k.Shifted = Char(vk, k.Scan);
            Array.Clear(st);
            st[0x11] = 0x80; st[0x12] = 0x80;
            k.AltGr = Char(vk, k.Scan);
            if (k.AltGr == k.Normal) k.AltGr = null;
        }
        SetLanguageLabel(LayoutCode(hkl));
        _dirty = true;
    }

    public void CreateOverlay(VrSession vr)
    {
        _vr = vr;
        Ov = new VrOverlay("erik.orbit.keyboard", "OrbitOverlay Keyboard");
        Ov.SetFlag(VROverlayFlags.IsPremultiplied, true);
        Ov.SetSortOrder(120);
        Ov.SetMouseScale(TW, TH);
        _dirty = true;
    }

    public void DestroyOverlay() { ReleaseAll(); Ov?.Dispose(); Ov = null; _vr = null; }

    public void Update(VrSession vr, double now)
    {
        if (now >= _nextLayoutCheck)
        {
            _nextLayoutCheck = now + 1;
            if (BuiltIn) { if (_hkl != (IntPtr)(-1)) { _hkl = (IntPtr)(-1); ApplyBuiltInLabels(); } }
            else
            {
                var hkl = Native.ForegroundLayout();
                if (hkl != _hkl && hkl != IntPtr.Zero) RefreshLabels(hkl);
            }
        }
        // key repeat while the trigger is held
        for (int h = 0; h < 3; h++)
        {
            var k = _down[h];
            if (k == null || k.Kind != Kind.Normal) continue;
            if (now - _downAt[h] > 0.5 && now >= _repeatAt[h])
            {
                if (_downChat[h]) ChatKey(k);
                else if (_downUnicode[h]) { string c = CharFor(k); if (!string.IsNullOrEmpty(c)) Native.SendUnicode(c); }
                else if (_downVk[h] != 0) Native.SendVk(_downVk[h], false);
                else Native.SendScan(k.Scan, k.Ext, false);
                _repeatAt[h] = now + 0.045;
            }
        }
        if (_themeVersion != Theme.Version) { _themeVersion = Theme.Version; _dirty = true; }
        if (_builtCyber != Theme.Cyber) RebuildLayout();          // Cyber has its own rows (KeyboardCyber.cs)
        if (_chat)
        {
            int b = (int)(now * 2) % 2;
            if (b != _blink) { _blink = b; _dirty = true; }
        }
        if (Ov == null) return;
        bool want = C.Visible && C.Place.M != null;
        float dt = (float)Math.Clamp(now - _lastNow, 0, 0.1);
        _lastNow = now;
        _lastDt = Math.Max(dt, 0.001f);
        _vis = Motion.Step(_vis, want, dt, 0.18f);                       // Look > Motion off: at once
        if (_vis <= 0 || C.Place.M == null || !Placement.Apply(C.Place, vr, Ov))
        {
            if (Ov.Visible) ReleaseAll();
            Ov.Hide();
            return;
        }
        float e = 1 - MathF.Pow(1 - _vis, 3);
        Ov.SetAlpha(e);
        Ov.SetWidth(C.Width * (0.9f + 0.1f * e));
        if (!Ov.Visible) { _dirty = true; _sounds.Reopen(); }   // follow the current default audio device
        Ov.Show();
        // a style's opening / closing shows on every frame of the fade
        if (_dirty || _flash.Count > 0 || now >= _wake || (_vis < 1 && Theme.RetroSkin.AnimatesFade))
        {
            Draw();
            Ov.SetTexture(Surf.Tex);
            _dirty = false;
            _wake = AnimWake.Take();
        }
    }

    bool Caps => (Native.GetKeyState(0x14) & 1) != 0;
    double _lastNow;

    public void Draw()
    {
        if (_builtCyber != Theme.Cyber) RebuildLayout();          // Cyber has its own rows (KeyboardCyber.cs)
        if (_hkl == IntPtr.Zero)
        {
            if (BuiltIn) { _hkl = (IntPtr)(-1); ApplyBuiltInLabels(); }
            else RefreshLabels(Native.ForegroundLayout());
        }
        if (Theme.Classic) { Draw95(); return; }
        if (Theme.Cyber) { DrawCyber(); return; }        // the HUD keyboard (KeyboardCyber.cs)
        bool shift = _mods["shift"] > 0, altgr = _mods["altgr"] > 0, caps = Caps;      // Caps asks Windows: once a frame, not per key
        FadeGlow();
        // The picture at rest, kept (the Cyber keyboard's way, in the same texture: KeyboardCyber.cs): the frame and every key with
        // nothing on it, made again only when that picture changes (a modifier, Caps, the layout, Chatbox, the look). A frame is that
        // picture, the Chatbox line and only the keys doing something - under a laser, held, glowing - each drawn over the frame put
        // back under it (Uncover), so every pixel comes out as the whole picture drawn would have it. ~0.1 ms instead of ~0.8, and a
        // key let go was ~23 frames of it (Erik 10-18: "it must be optimized, get the maximum out of it"). --snapshot's text checks
        // (and DrawAllForTest) draw all of it, as before.
        _cyCaps = caps;
        bool live = DrawsAll;
        if (!live)
        {
            int sig = CyBaseSig(shift, altgr);
            if (_cyBase == null || !_cyBaseOk || sig != _cyBaseSig) DrawBase(shift, altgr, caps, sig);
        }
        _p.Begin(Surf.Bitmap, 0x00000000);
        if (live) Frame();
        else _p.Image(_cyBase.Bitmap, new RectangleF(0, 0, TW, TH));
        if (_chat) ChatField();
        var bar = _chat ? _barChat : _barNormal;        // the keys, then the top bar (no list made for them every frame)
        for (int ki = 0; ki < _keys.Count + bar.Count; ki++)
        {
            var k = ki < _keys.Count ? _keys[ki] : bar[ki - _keys.Count];
            bool hover = Array.IndexOf(_hover, k) >= 0;
            bool down = Array.IndexOf(_down, k) >= 0;
            _flash.TryGetValue(k, out float glow);
            if (!live)
            {
                if (!hover && !down && glow <= 0) continue;          // as the kept picture has it
                Uncover(k.R);
            }
            DrawKey(k, shift, altgr, caps, hover, down, glow);
        }
        _p.End();
    }

    // --snapshot's text checks read every text as it is drawn: the whole picture then, nothing kept (Cyber's keyboard too)
    bool DrawsAll => _p.FitSpy != null || Painter.FitAll != null || _p.TextSpy != null || _p.NoText || DrawAllForTest;

    // the perf tool: the whole picture drawn, to hold against the kept one
    public bool DrawAllForTest;

    // the press glow fades out after the key is let go (every style's frame)
    void FadeGlow()
    {
        _flashKeys.Clear();
        _flashKeys.AddRange(_flash.Keys);
        foreach (var key in _flashKeys)
        {
            if (Array.IndexOf(_down, key) >= 0) continue;
            float f = Motion.Any ? _flash[key] - _lastDt / 0.25f : 0;       // Look > Motion off: gone at once
            if (f <= 0) _flash.Remove(key); else _flash[key] = f;
        }
    }

    // the modern frame: the panel's fill and its outline
    void Frame()
    {
        _p.Fill(new RectangleF(0, 0, TW, TH), Theme.OverlayBg, 26);
        _p.Stroke(new RectangleF(1, 1, TW - 2, TH - 2), 0x40FFFFFF, 2, 25);
    }

    // the modern picture at rest into the kept texture: the frame and every key with nothing on it
    void DrawBase(bool shift, bool altgr, bool caps, int sig)
    {
        _cyBase ??= new Surface(_p.Gpu, TW, TH);
        _p.Begin(_cyBase.Bitmap, 0x00000000);
        Frame();
        foreach (var k in _keys) DrawKey(k, shift, altgr, caps, false, false, 0);
        foreach (var k in _chat ? _barChat : _barNormal) DrawKey(k, shift, altgr, caps, false, false, 0);
        _p.End();
        _cyBaseSig = sig;
        _cyBaseOk = true;
    }

    // The kept picture under a key made see-through again and the frame put back there, on whole pixels a little past the key: its
    // edge's smoothing and a one-shot modifier's outline fall inside, never a neighbour (the keys sit 8 px apart). Nothing but the
    // frame's fill lies under a key (its outline runs round the edge, the keys start 24 px in), so the key then draws over exactly
    // what the whole picture has under it - a held key's sunken cap shows the frame round it, as before.
    void Uncover(RectangleF r)
    {
        var u = RectangleF.FromLTRB(MathF.Floor(r.X - 2), MathF.Floor(r.Y - 2), MathF.Ceiling(r.Right + 2), MathF.Ceiling(r.Bottom + 2));
        _p.ClearRect(u);
        _p.Fill(u, Theme.OverlayBg);
    }

    // the modern Chatbox line: a dark field with the accent's edge, the text's end kept in view, a blinking caret, the count
    void ChatField()
    {
        var f = _chatField;
        _p.Fill(f, Theme.Mix(Theme.Panel, 0xFF000000, 0.25f), 12);
        _p.Stroke(f, Theme.Accent, 2, 12);
        string text = _chatText.ToString();
        var tr = new RectangleF(f.X + 18, f.Y, f.Width - 130, f.Height);
        if (text.Length == 0)
            _p.Text("Type a message for the VRChat chatbox, Enter sends", tr, Theme.Faint, 22);
        else
        {
            // keep the end of a long line in view
            string shown = text;
            while (shown.Length > 1 && _p.MeasureWidth(shown, 26) > tr.Width - 20) shown = shown[1..];
            if (shown.Length < text.Length) shown = "…" + shown;
            _p.Text(shown, tr, Theme.Text, 26);
            if (_blink == 0)
            {
                float cx = tr.X + _p.MeasureWidth(shown, 26) + 3;
                _p.Fill(new RectangleF(cx, f.Y + 16, 3, f.Height - 32), Theme.Accent);
            }
        }
        _p.Text($"{text.Length}/{ChatMax}", new RectangleF(f.Right - 110, f.Y, 96, f.Height), Theme.Dim, 18,
            FontWeight.Normal, TextAlignment.Trailing);
    }

    // one modern key as it is now: hover / held / glowing / a modifier's state, then its legend
    void DrawKey(Key k, bool shift, bool altgr, bool caps, bool hover, bool down, float glow)
    {
        int modState = k.Kind == Kind.Modifier ? _mods[k.Mod] : k.Kind == Kind.Caps && caps ? 2
                     : k.Action == "chat" && _chat ? 2 : 0;
        uint bg = down || modState == 2 ? Theme.Accent : hover ? Theme.Panel3 : Theme.Panel2;
        if (!down && glow > 0) bg = Theme.Mix(bg, Theme.Accent, glow);
        var kr = down ? RectangleF.Inflate(k.R, -3, -3) : k.R;     // pressed keys sink a little
        _p.Fill(kr, bg, 12);
        if (modState == 1) _p.Stroke(kr, Theme.Accent, 3, 12);
        uint fg = down || modState == 2 ? Theme.OnAccent : Theme.Text;
        if (k.Kind == Kind.Action)
        {
            // icon + label, centred
            float tw = _p.MeasureWidth(k.Fixed, 22, FontWeight.SemiBold);
            float x0 = k.R.X + (k.R.Width - (28 + 10 + tw)) / 2;
            _p.Icon(k.Icon, new RectangleF(x0, k.R.Y, 28, k.R.Height), fg, 24);
            _p.Text(k.Fixed, new RectangleF(x0 + 38, k.R.Y, tw + 6, k.R.Height), fg, 22, FontWeight.SemiBold);
        }
        else if (k.Icon != null)
            _p.Icon(k.Icon, k.R, fg, 30);
        else if (k.Fixed != null)
            _p.Text(k.Fixed, k.R, fg, 24, FontWeight.SemiBold, TextAlignment.Center);
        else
        {
            string main = altgr && k.AltGr != null ? k.AltGr : shift ? k.Shifted ?? k.Normal : k.Normal;
            if (main != null && main.Length == 1 && char.IsLetter(main[0]) && caps && !altgr)
                main = shift ? main.ToLowerInvariant() : main.ToUpperInvariant();
            _p.Text(main, k.R, fg, 38, FontWeight.Normal, TextAlignment.Center);
            // small hints: shifted symbol (for non-letters) top-left, AltGr symbol bottom-right
            if (!shift && k.Shifted != null && k.Normal != null && !char.IsLetter(k.Normal[0]))
                _p.Text(k.Shifted, new RectangleF(k.R.X + 8, k.R.Y + 4, 30, 28), down ? fg : Theme.Dim, 18);
            if (!altgr && k.AltGr != null)
                _p.Text(k.AltGr, new RectangleF(k.R.Right - 34, k.R.Bottom - 32, 28, 28), down ? fg : Theme.WithAlpha(Theme.Accent, 0.9f), 18,
                    FontWeight.Normal, TextAlignment.Trailing);
        }
    }

    Key HitKey(Vector2 px)
    {
        foreach (var k in _keys)
            if (k.R.Contains(px.X, px.Y)) return k;
        foreach (var k in _chat ? _barChat : _barNormal)
            if (k.R.Contains(px.X, px.Y)) return k;
        return null;
    }

    // ---- chatbox line ----
    string CharFor(Key k)
    {
        bool shift = _mods["shift"] > 0, altgr = _mods["altgr"] > 0;
        string c = altgr && k.AltGr != null ? k.AltGr : shift ? k.Shifted ?? k.Normal : k.Normal;
        if (c != null && c.Length == 1 && char.IsLetter(c[0]) && Caps && !altgr)
            c = shift ? c.ToLowerInvariant() : c.ToUpperInvariant();
        return c;
    }

    void SetChat(string text)
    {
        bool was = _chatText.Length > 0;
        _chatText.Clear().Append(text);
        if (was != (_chatText.Length > 0)) OnChatTyping?.Invoke(_chatText.Length > 0);
        _dirty = true;
    }

    void SendChat()
    {
        if (_chatText.Length == 0) return;
        OnChatSend?.Invoke(_chatText.ToString());
        SetChat("");
    }

    // one key in Chatbox mode (also used for key repeat)
    void ChatKey(Key k)
    {
        switch (k.Scan)
        {
            case 0x0E:      // backspace
                if (_chatText.Length > 0)
                {
                    int cut = _chatText.Length >= 2 && char.IsLowSurrogate(_chatText[^1]) ? 2 : 1;
                    SetChat(_chatText.ToString(0, _chatText.Length - cut));
                }
                return;
            case 0x1C: SendChat(); return;                       // enter
            case 0x01: case 0x53: SetChat(""); return;           // esc / del
            case 0x39: if (_chatText.Length < ChatMax) SetChat(_chatText + " "); return;
        }
        if (k.Fixed != null || k.Icon != null) return;            // tab, F-keys, arrows: nothing to type
        string c = CharFor(k);
        if (!string.IsNullOrEmpty(c) && _chatText.Length + c.Length <= ChatMax) SetChat(_chatText + c);
    }

    void RunAction(string id)
    {
        ushort vk = id switch { "selall" => 0x41, "copy" => 0x43, "paste" => 0x56, "cut" => 0x58, "undo" => 0x5A, "redo" => 0x59, _ => 0 };
        if (vk != 0) { Native.SendCtrlCombo(vk); return; }
        if (id == "chat")
        {
            _chat = !_chat;
            if (!_chat && _chatText.Length > 0) OnChatTyping?.Invoke(false);
            else if (_chat && _chatText.Length > 0) OnChatTyping?.Invoke(true);
        }
        else if (id == "send") SendChat();
        else if (id == "lang") CycleLanguage();
        _dirty = true;
    }

    // slot 0/1 = laser hands, 2 = mouse (desktop preview)
    readonly KeySounds _sounds = new();

    // wide keys (space, enter, backspace, shifts...) get the deeper sound; release = softer up-click
    public void PlayClick(bool big, bool release = false)
    {
        if (C.Sounds) _sounds.Play(big, release, C.SoundStyle, C.SoundVolume);
    }

    public double LastKeyAt = -10;

    public void Press(int slot, Vector2 px, double now)
    {
        var k = HitKey(px);
        if (k == null) return;
        LastKeyAt = now;
        if (_down[slot] != null) Release(slot);      // never start a key while the previous one is still held
        _dirty = true;
        PlayClick(k.R.Width > U * 1.6f);
        _flash[k] = 1;
        _held95[slot] = k;
        if (k.Kind == Kind.Modifier)
        {
            _mods[k.Mod] = (_mods[k.Mod] + 1) % 3;
            return;
        }
        if (k.Kind == Kind.Action)
        {
            RunAction(k.Action);
            return;
        }
        if (_chat && k.Kind == Kind.Normal)
        {
            ChatKey(k);
            _down[slot] = k;            // for the pressed look and key repeat; nothing goes to Windows
            _downChat[slot] = true;
            _downAt[slot] = now;
            _repeatAt[slot] = 0;
            return;
        }
        bool builtInChar = BuiltIn && k.Kind == Kind.Normal && k.Fixed == null && k.Icon == null && UsKeys.ContainsKey(k.Scan);
        bool shortcutMods = _mods["ctrl"] > 0 || _mods["alt"] > 0 || _mods["win"] > 0;
        if (builtInChar && !shortcutMods)
        {
            string c = CharFor(k);
            if (!string.IsNullOrEmpty(c)) Native.SendUnicode(c);
            _down[slot] = k;
            _downUnicode[slot] = true;
            _downChat[slot] = false;
            _downVk[slot] = 0;
            _downAt[slot] = now;
            _repeatAt[slot] = 0;
            return;
        }
        if (k.Kind == Kind.Caps)
        {
            Native.SendScan(k.Scan, false, false);
            Native.SendScan(k.Scan, false, true);
            return;
        }
        var sent = _sentMods[slot];
        sent.Clear();
        foreach (var m in _keys)
        {
            if (m.Kind != Kind.Modifier || _mods[m.Mod] == 0) continue;
            if (sent.Exists(s => s.scan == m.Scan && s.ext == m.Ext)) continue;
            // one physical key per active modifier (the left one, AltGr = right Alt)
            if (sent.Exists(s => ModOf(s) == m.Mod)) continue;
            sent.Add((m.Scan, m.Ext));
            Native.SendScan(m.Scan, m.Ext, false);
        }
        ushort vk = builtInChar ? UsVk(k.Scan) : (ushort)0;
        if (vk != 0) Native.SendVk(vk, false);
        else Native.SendScan(k.Scan, k.Ext, false);
        _down[slot] = k;
        _downVk[slot] = vk;
        _downUnicode[slot] = false;
        _downChat[slot] = false;
        _downAt[slot] = now;
        _repeatAt[slot] = 0;
    }

    string ModOf((ushort scan, bool ext) s)
    {
        foreach (var m in _keys)
            if (m.Kind == Kind.Modifier && m.Scan == s.scan && m.Ext == s.ext) return m.Mod;
        return null;
    }

    public void Release(int slot)
    {
        if (_held95[slot] != null) { _held95[slot] = null; _dirty = true; }
        var k = _down[slot];
        if (k == null) return;
        if (_downChat[slot] || _downUnicode[slot])
        {
            _downUnicode[slot] = false;
            foreach (var key in new List<string>(_mods.Keys))
                if (_mods[key] == 1) _mods[key] = 0;
            _down[slot] = null;
            _dirty = true;
            return;
        }
        if (_downVk[slot] != 0) { Native.SendVk(_downVk[slot], true); _downVk[slot] = 0; }
        else Native.SendScan(k.Scan, k.Ext, true);
        PlayClick(k.R.Width > U * 1.6f, release: true);
        var sent = _sentMods[slot];
        for (int i = sent.Count - 1; i >= 0; i--) Native.SendScan(sent[i].scan, sent[i].ext, true);
        sent.Clear();
        foreach (var key in new List<string>(_mods.Keys))
            if (_mods[key] == 1) _mods[key] = 0;
        _down[slot] = null;
        _dirty = true;
    }

    void ReleaseAll() { for (int i = 0; i < 3; i++) Release(i); }

    public void SetHover(int slot, Vector2? px)
    {
        var k = px.HasValue ? HitKey(px.Value) : null;
        if (_hover[slot] != k) { _hover[slot] = k; _dirty = true; }
    }

    // ---- IPointerTarget ----
    public VrOverlay Overlay => Ov;
    public bool Interactive => Ov != null && Ov.Visible;
    public bool AcceptsFrom(Hand h) => true;
    public Vector2 TextureSize => new(TW, TH);
    public void PointerMove(Hand h, Vector2 px) => SetHover((int)h, px);
    public void PointerLeave(Hand h) => SetHover((int)h, null);
    public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px)
    {
        if (b != PtrButton.Primary) return;
        if (down) Press((int)h, px, Clock.Now);
        else Release((int)h);
    }
    public void PointerScroll(Hand h, float n, Vector2 px) { }
    public bool Grabbable => true;

    public Matrix4x4 WorldTransform =>
        _vr != null && Placement.TryWorld(C.Place, _vr, out var w) ? w : MathUtil.FromArray(C.Place.M);

    public void GrabMove(Matrix4x4 world)
    {
        if (_vr == null) return;
        Placement.SetWorld(C.Place, _vr, world);
        _cfg.MarkDirty();
    }

    float _twoStartWidth;
    public void GrabBegin(bool detach) { }
    public void GrabStickX(float v, float dt) { C.Width = Math.Clamp(C.Width * (1 + v * 0.9f * dt), 0.25f, 1.5f); _cfg.MarkDirty(); }
    public void GrabTwoHandBegin() => _twoStartWidth = C.Width;
    public void GrabTwoHandScale(float r) { C.Width = Math.Clamp(_twoStartWidth * r, 0.25f, 1.5f); _cfg.MarkDirty(); }
    public bool GrabSnapPreview => false;
    public void GrabEnd() { }

    public void Dispose() { DestroyOverlay(); Surf.Dispose(); _sounds.Dispose(); _canvas95?.Dispose(); _cyBase?.Dispose(); }
}
