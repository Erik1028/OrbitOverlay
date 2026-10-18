using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using Vortice.DirectWrite;

namespace Orbit;

// A green phosphor terminal: #33FF66-ish on a black CRT, a 5 x 7 dot-matrix font of its own (TerminalFont.cs), ASCII bars
// [#####-----], F-key buttons, a text-mode setup utility, scanlines in the big letters and the lit bars - with hit areas as big
// as the 95 ones (the layouts are the shared 95 ones; the whole screens it lays out its own way are in Terminal/).
//
// Colours: the shared 95 code draws black text on grey and white; a phosphor screen is the other way round, so every brush
// colour that is not already one of the tube's (Ph: "signed" in its lowest bits) is turned into one by how DARK it is - black
// text becomes full green, grey text dim green, a navy selection a lit bar with black text in it, white and the 95 grey become
// the black of the glass. Warm colours (the 95 warnings: maroon, amber, yellow, red) become the brightest, whitest green.
public sealed partial class TerminalSkin : Win95Skin
{
    public override RetroStyle Style => RetroStyle.Terminal;
    public override string Id => "terminal";
    public override string Tag => "term";
    public override string Name => "Terminal";

    public static TerminalSkin It => (TerminalSkin)Skins.Terminal;

    // ---- the phosphor ----

    // a colour of the tube: its red channel's lowest bits 01, its blue's 10 (MapColor leaves these alone)
    public static uint Sign(uint c) => (c & ~0x00030003u) | 0x00010002u;
    public static bool Signed(uint c) => (c & 0x00030003u) == 0x00010002u;
    // a colour that is not phosphor (the monitor's case): kept as it is
    public static uint Raw(uint c) => Sign(c);

    // the glow at intensity v: 0 = the black glass, 1 = the phosphor's green, up to 1.3 = over-driven (bold, warnings)
    public static uint Ph(float v, uint alpha = 0xFF)
    {
        float r, g, b;
        if (v <= 1)
        {
            v = Math.Max(0, v);
            r = 0x02 + (0x33 - 0x02) * v; g = 0x0A + (0xFF - 0x0A) * v; b = 0x05 + (0x66 - 0x05) * v;
        }
        else
        {
            float t = Math.Min(1, (v - 1) / 0.3f);
            r = 0x33 + (0xC8 - 0x33) * t; g = 0xFF; b = 0x66 + (0xD8 - 0x66) * t;
        }
        return Sign((alpha << 24) | ((uint)MathF.Round(r) << 16) | ((uint)MathF.Round(g) << 8) | (uint)MathF.Round(b));
    }

    public static readonly uint BG = Ph(0), INK = Ph(1), HOT = Ph(1.3f), DIM = Ph(0.5f), MID = Ph(0.72f), FRAME = Ph(0.55f),
        FAINT = Ph(0.22f), LIT2 = Ph(0.8f), SEL = Ph(0.28f);
    // the monitor's case around the tube
    public static readonly uint Case = Raw(0xFF232527), CaseHi = Raw(0xFF34373A), CaseLo = Raw(0xFF121314);

    public override uint Face => BG;
    public override uint Light => BG;
    public override uint Hilight => BG;
    public override uint Shadow => DIM;
    public override uint DarkShadow => INK;
    public override uint Window => BG;
    public override uint WindowText => INK;
    public override uint ButtonText => INK;
    public override uint GrayText => DIM;
    public override uint DimText => MID;
    public override uint Highlight => INK;
    public override uint HighlightText => BG;
    public override uint ActiveCaption => INK;
    public override uint CaptionText => BG;
    public override uint InactiveCaption => DIM;
    public override uint InactiveCaptionText => BG;
    public override uint Desktop => BG;
    public override uint Laser => 0xFF33FF66;

    public static bool Warm(uint c)
    {
        int r = (int)((c >> 16) & 255), g = (int)((c >> 8) & 255), b = (int)(c & 255);
        return r > b + 0x40 && r >= g;
    }

    // how bright the tube draws a 95 colour: the darker it was, the brighter it glows
    public static float Glow(uint c)
    {
        if (Warm(c)) return 1.3f;
        int r = (int)((c >> 16) & 255), g = (int)((c >> 8) & 255), b = (int)(c & 255);
        float v = 1 - (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
        return v < 0.28f ? 0 : MathF.Pow((v - 0.28f) / 0.72f, 0.55f);
    }

    readonly Dictionary<uint, uint> _mapped = new();

    public override uint MapColor(uint c)
    {
        if ((c >> 24) == 0 || Signed(c)) return c;
        if (_mapped.TryGetValue(c, out uint m)) return m;
        m = Ph(Glow(c), c >> 24);
        if (_mapped.Count > 4096) _mapped.Clear();
        return _mapped[c] = m;
    }

    public override uint TextColor(uint c) => c;

    // ---- type ----

    public override string Font => "Consolas";      // only where the dot-matrix font has no letter for a text
    public override bool SmoothText => false;
    public override float DropRowH => 13;

    int _plain;         // drawing a text the font cannot spell: Consolas' own aliased glyphs instead

    public override void MapFont(ref string family, ref float size, ref FontWeight weight)
    {
        if (family == Theme.IconFont) return;
        if (_plain > 0) family = "Consolas";
    }

    public override PixelFont PixelFont(string family, float size, FontWeight weight) =>
        family == Theme.IconFont || _plain > 0 ? null : FontAt(ScaleOf(size));

    // the font's size for a pixel size: 1x up to 19 px (one size, like the terminal), then whole multiples (22 px: 2x)
    public static int ScaleOf(float size) => Math.Clamp((int)(size * 0.09f + 0.2f), 1, 8);
    static float SizeOf(int n) => n == 1 ? Ui.F95 : (n + 0.5f) / 0.09f;

    public static bool Covers(string s, int n = 1)
    {
        var f = FontAt(n);
        foreach (char ch in s)
            if (ch != ' ' && ch != '\n' && !f.Has(ch) && !f.Has(char.ToUpperInvariant(ch)) && !f.Has(char.ToLowerInvariant(ch))) return false;
        return true;
    }

    static Dictionary<char, string[]> _base;
    static readonly Dictionary<int, PixelFont> _fonts = new();

    // the 1x glyphs, CellH rows each: the table, then every accented Latin letter from its base and its mark
    static Dictionary<char, string[]> Base => _base ??= BuildBase();

    static Dictionary<char, string[]> BuildBase()
    {
        var g = new Dictionary<char, string[]>();
        foreach (var line in Glyphs.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8) continue;
            char ch = parts[0].Length == 1 ? parts[0][0] : (char)int.Parse(parts[0][2..], NumberStyles.HexNumber);
            var rows = new string[CellH];
            for (int i = 0; i < CellH; i++) rows[i] = ".....";
            for (int i = 1; i < parts.Length && i + 1 < CellH; i++) rows[i + 1] = parts[i];
            if (ch is (char)0x2588 or (char)0x2591 or (char)0x2592) { rows[0] = rows[4]; rows[1] = rows[3]; }   // blocks fill the cell
            g[ch] = rows;
        }
        for (int cp = 0xC0; cp <= 0x17F; cp++)
        {
            char ch = (char)cp;
            if (g.ContainsKey(ch)) continue;
            string d = ch.ToString().Normalize(NormalizationForm.FormD);
            if (d.Length < 2) continue;
            char b = d[0];
            bool lower = char.IsLower(ch);
            if (b == 'i') b = (char)0x131;             // the dotless i takes the mark
            if (!g.TryGetValue(b, out var baseRows)) continue;
            var rows = (string[])baseRows.Clone();
            bool ok = true;
            foreach (char m in d[1..])
            {
                if (Marks.TryGetValue(m, out var mk))
                {
                    int top = !lower || rows[2] != "....." || rows[3] != "....." ? 0 : 2;     // over a capital or an ascender: the top
                    rows[top] = Or(rows[top], mk[0]);
                    rows[top + 1] = Or(rows[top + 1], mk[1]);
                }
                else if (MarksBelow.TryGetValue(m, out var mb))
                {
                    rows[9] = Or(rows[9], mb[0]);
                    rows[10] = Or(rows[10], mb[1]);
                }
                else ok = false;
            }
            if (ok) g[ch] = rows;
        }
        return g;
    }

    static string Or(string a, string b)
    {
        var c = a.ToCharArray();
        for (int i = 0; i < c.Length && i < b.Length; i++) if (b[i] != '.') c[i] = '#';
        return new string(c);
    }

    // the font at n x n pixels per dot (from 3x up the bottom line of every dot dark: scanlines)
    public static PixelFont FontAt(int n)
    {
        n = Math.Clamp(n, 1, 8);
        if (_fonts.TryGetValue(n, out var f)) return f;
        Dictionary<char, string[]> glyphs;
        if (n == 1) glyphs = Base;
        else
        {
            glyphs = new Dictionary<char, string[]>();
            foreach (var (ch, rows) in Base)
            {
                if (n > 4 && ch > 0x17F) continue;          // the biggest: text and Latin letters only (a smaller atlas)
                glyphs[ch] = ScaleGlyph(rows, n, n >= 3);
            }
        }
        f = new PixelFont(CellH * n, CellAscent * n, glyphs) { Spacing = n, SpaceWidth = GlyphW * n, Smear = true, Fallback = '?' };
        _fonts[n] = f;
        return f;
    }

    static string[] ScaleGlyph(string[] rows, int n, bool scan)
    {
        var o = new string[rows.Length * n];
        for (int y = 0; y < rows.Length; y++)
        {
            var sb = new StringBuilder(rows[y].Length * n);
            foreach (char ch in rows[y]) sb.Append(ch, n);
            string lit = sb.ToString(), dark = new string('.', lit.Length);
            for (int k = 0; k < n; k++) o[y * n + k] = scan && k == n - 1 ? dark : lit;
        }
        return o;
    }

    // ---- drawing helpers (the screens in Terminal/ use them too) ----

    public static float W(string s, int n = 1, bool bold = false) => FontAt(n).Measure(Lang.T(s), bold);

    // text in r with the dot-matrix font at n x (Consolas' glyphs for letters it does not have)
    public void Put(Painter p, string s, RectangleF r, uint c, int n = 1, bool bold = false, TextAlignment h = TextAlignment.Leading,
        ParagraphAlignment v = ParagraphAlignment.Center, bool wrap = false)
    {
        if (string.IsNullOrEmpty(s)) return;
        s = Tty(Lang.T(s));                 // the language picked (the dot-matrix font has its letters)
        Painter.FitAll?.Invoke(p, s, r, SizeOf(n), bold ? FontWeight.Bold : FontWeight.Normal, Font, wrap, h, v, 0);
        if (Covers(s, n)) { FontAt(n).Draw(p, s, r, c, bold, h, v, wrap); return; }
        _plain++;
        try { p.Text(s, r, c, SizeOf(n), bold ? FontWeight.Bold : FontWeight.Normal, h, v, "Consolas", wrap); }
        finally { _plain--; }
    }

    // at x, y: the top of the text's cell
    public void Put(Painter p, string s, float x, float y, uint c, int n = 1, bool bold = false) =>
        Put(p, s, new RectangleF(x, y, W(s, n, bold) + 1, CellH * n), c, n, bold);

    public static string Up(string s) => s?.ToUpperInvariant();

    public static void Box(Painter p, RectangleF r, uint c)
    {
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 1), c);
        p.Fill(new RectangleF(r.X, r.Bottom - 1, r.Width, 1), c);
        p.Fill(new RectangleF(r.X, r.Y + 1, 1, r.Height - 2), c);
        p.Fill(new RectangleF(r.Right - 1, r.Y + 1, 1, r.Height - 2), c);
    }

    public static void DoubleBox(Painter p, RectangleF r, uint c)
    {
        Box(p, r, c);
        Box(p, RectangleF.Inflate(r, -2, -2), c);
    }

    // a lit bar: every other line a little darker (the scanlines show where the beam is on)
    public static void Stripes(Painter p, RectangleF r, uint c, uint dark)
    {
        p.Fill(r, c);
        for (float y = r.Y + 1; y < r.Bottom; y += 2) p.Fill(new RectangleF(r.X, y, r.Width, 1), dark);
    }

    public static void Lit(Painter p, RectangleF r, bool on = true) => Stripes(p, r, on ? INK : DIM, on ? LIT2 : Ph(0.4f));

    // a tall bracket for a field: [ at x (left) or ] (right), from y1 to y2
    public static void Bracket(Painter p, float x, float y1, float y2, bool left, uint c)
    {
        p.Fill(new RectangleF(x, y1, 1, y2 - y1), c);
        float sx = left ? x + 1 : x - 1;
        p.Fill(new RectangleF(sx, y1, 1, 1), c);
        p.Fill(new RectangleF(sx, y2 - 1, 1, 1), c);
    }

    // a horizontal rule, dotted the way a row of '-' looks
    public static void Dashes(Painter p, float x1, float x2, float y, uint c)
    {
        for (float x = x1; x + 5 <= x2; x += 6) p.Fill(new RectangleF(x, y, 5, 1), c);
    }

    // the tube's scanlines: every other pixel row a little darker (over a whole screen, once it is drawn)
    public static readonly uint ScanDark = Sign(0x2A000000);
    public static void Scanlines(Painter p, RectangleF r)
    {
        for (float y = r.Y + 1; y < r.Bottom; y += 2) p.Fill(new RectangleF(r.X, y, r.Width, 1), ScanDark);
    }

    // the monitor around the tube: 3 px of its case and the glass's rounded corners
    public static void Crt(Painter p, RectangleF r)
    {
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 3), Case);
        p.Fill(new RectangleF(r.X, r.Bottom - 3, r.Width, 3), Case);
        p.Fill(new RectangleF(r.X, r.Y + 3, 3, r.Height - 6), Case);
        p.Fill(new RectangleF(r.Right - 3, r.Y + 3, 3, r.Height - 6), Case);
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 1), CaseHi);
        p.Fill(new RectangleF(r.X, r.Y, 1, r.Height), CaseHi);
        p.Fill(new RectangleF(r.X, r.Bottom - 1, r.Width, 1), CaseLo);
        p.Fill(new RectangleF(r.Right - 1, r.Y, 1, r.Height), CaseLo);
        var g = RectangleF.Inflate(r, -3, -3);
        int[] round = { 3, 1, 1 };
        for (int i = 0; i < round.Length; i++)
        {
            p.Fill(new RectangleF(g.X, g.Y + i, round[i], 1), Case);
            p.Fill(new RectangleF(g.Right - round[i], g.Y + i, round[i], 1), Case);
            p.Fill(new RectangleF(g.X, g.Bottom - 1 - i, round[i], 1), Case);
            p.Fill(new RectangleF(g.Right - round[i], g.Bottom - 1 - i, round[i], 1), Case);
        }
    }

    // ---- text ----

    // the emoji a text may carry (the chatbox line's note) as the font's own signs
    public static string Tty(string s) =>
        s == null || s.IndexOf((char)0xD83C) < 0 ? s : s.Replace("\U0001F3B5", "\u266A").Replace("\U0001F3B6", "\u266B");

    public override void Text(Painter p, string s, RectangleF r, uint c, bool bold, TextAlignment al, bool enabled, bool wrap, float size)
    {
        if (string.IsNullOrEmpty(s)) return;
        s = Tty(Lang.T(s));
        bool plain = !Covers(s, ScaleOf(size));
        if (plain) _plain++;
        try { base.Text(p, s, r, c, bold, al, enabled, wrap, size); }
        finally { if (plain) _plain--; }
    }

    public override float TextWidth(Painter p, string s, bool bold, float size)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = Tty(s);
        bool plain = !Covers(s, ScaleOf(size));
        if (plain) _plain++;
        try { return base.TextWidth(p, s, bold, size); }
        finally { if (plain) _plain--; }
    }

    // a control's label: in capitals, the access key's letter brighter (a DOS menu's hot letter)
    public override void Label(Painter p, string s, RectangleF r, bool enabled, TextAlignment al, uint c, bool bold)
    {
        var (t, key) = Ui.Amp(s);
        if (string.IsNullOrEmpty(t)) return;
        t = Up(t);
        Text(p, t, r, c, bold, al, enabled, false, Ui.F95);
        if (key < 0 || !enabled || MapColor(c) != INK) return;
        float full = TextWidth(p, t, bold, Ui.F95);
        float x = al == TextAlignment.Center ? r.X + MathF.Floor((r.Width - full) / 2) : al == TextAlignment.Trailing ? r.Right - full : r.X;
        var (pre, ch) = Ui.AmpParts(t, key);
        x = MathF.Round(x + TextWidth(p, pre, bold, Ui.F95));
        Text(p, ch, new RectangleF(x, r.Y, 8, r.Height), HOT, bold, TextAlignment.Leading, true, false, Ui.F95);
    }

    public override void DisabledText(Painter p, string s, RectangleF r, float size, FontWeight w, TextAlignment h, ParagraphAlignment v) =>
        p.Text(s, r, DIM, size, w, h, v);

    // ---- frames ----

    public override void Bevel(Painter p, RectangleF r, Painter.Edge e)
    {
        switch (e)
        {
            case Painter.Edge.Window:
                if (r.X < 1) Crt(p, r);                 // a whole screen: the monitor
                else DoubleBox(p, r, FRAME);            // a window on it: a double-line box
                break;
            case Painter.Edge.Raised: Box(p, r, FRAME); break;
            case Painter.Edge.Pressed: Box(p, r, INK); break;
            case Painter.Edge.Page: Box(p, r, FRAME); break;
            default: Box(p, r, DIM); break;            // Sunken, Etched
        }
    }

    public override void EtchedLine(Painter p, float x1, float x2, float y) => p.Fill(new RectangleF(x1, y, x2 - x1, 1), DIM);

    public override void FillDesktop(Painter p, RectangleF r) => p.Fill(r, BG);

    // a window's title: a lit bar, the name in black capitals
    public override void Caption(Painter p, RectangleF r, string title, W95Icons.Id? icon, bool active)
    {
        Lit(p, r, active);
        Text(p, Fit(p, Up(title), r.Width - 60), new RectangleF(r.X + 5, r.Y, r.Width - 60, r.Height), BG, false, TextAlignment.Leading, true, false, Ui.F95);
    }

    public override void CaptionButton(Painter p, RectangleF r, CaptionKind kind, bool enabled, bool down, bool hover)
    {
        var (glyph, gx, gy) = kind switch
        {
            CaptionKind.Help => (HelpGlyph, 5f, 3f),
            CaptionKind.Min => (MinGlyph, 5f, 9f),
            CaptionKind.Max => (MaxGlyph, 3f, 3f),
            _ => (CloseGlyph, 4f, 3f),
        };
        if (down) { p.Fill(r, INK); Box(p, r, BG); }
        else { p.Fill(r, BG); if (hover) Box(p, r, HOT); }
        p.Pixels(glyph, r.X + gx, r.Y + gy, down ? BG : !enabled ? DIM : hover ? HOT : INK);
    }

    // a widget's box on the custom face: a line around it, its name on the top line
    public override RectangleF WidgetFrame(Painter p, RectangleF r, string title)
    {
        p.Fill(r, BG);
        if (title == null)
        {
            Box(p, r, FRAME);
            return RectangleF.Inflate(r, -3, -3);
        }
        Box(p, new RectangleF(r.X, r.Y + 5, r.Width, r.Height - 5), FRAME);
        string t = FrameTitle(p, title, r.Width - 12);
        float tw = W(t);
        p.Fill(new RectangleF(r.X + 3, r.Y, tw + 4, 11), BG);
        Put(p, t, r.X + 5, r.Y, INK);
        return new RectangleF(r.X + 3, r.Y + 11, r.Width - 6, r.Height - 14);
    }

    // a box's title cut to its width ("SEN..." on a 1x1), kept: it was cut again in every frame - new strings at rest
    readonly Dictionary<(string, float), string> _frameTitles = new();
    int _frameTitlesLang = -1;

    string FrameTitle(Painter p, string title, float w)
    {
        if (_frameTitlesLang != Lang.Version || _frameTitles.Count > 64) { _frameTitles.Clear(); _frameTitlesLang = Lang.Version; }
        if (_frameTitles.TryGetValue((title, w), out var t)) return t;
        return _frameTitles[(title, w)] = Fit(p, Up(title), w);
    }

    // ---- controls ----

    // a key or a button's face: a line around it (bright under the laser); held or latched: a dim glow inside
    public override RectangleF ButtonFace(Painter p, RectangleF r, bool isDefault, bool down, bool latched, bool hover)
    {
        p.Fill(r, down || latched ? SEL : BG);
        if (down || latched) Box(p, r, INK);
        else if (isDefault) DoubleBox(p, r, hover ? HOT : INK);
        else Box(p, r, hover ? INK : FRAME);
        return RectangleF.Inflate(r, -1, -1);
    }

    public override void PushButton(Painter p, RectangleF r, string label, W95Icons.Id? icon, bool enabled, bool isDefault, bool down,
        bool latched, bool hover)
    {
        bool lit = (down || latched) && enabled;
        if (lit) Lit(p, r);
        else
        {
            p.Fill(r, BG);
            if (isDefault) DoubleBox(p, r, hover && enabled ? HOT : INK);
            else Box(p, r, hover && enabled ? INK : FRAME);
        }
        string text = label;
        if (string.IsNullOrEmpty(text) && icon is { } ic) text = IconWord(ic);
        if (string.IsNullOrEmpty(text)) return;
        var (t, key) = Ui.Amp(text);
        t = Up(t);
        uint c = lit ? BG : !enabled ? DIM : hover ? HOT : INK;
        float avail = r.Width - 6;
        if (TextWidth(p, t, false, Ui.F95) > avail)
        {
            // a dialog's "..." left out where the word then fits whole: KIVÁLASZTÁS, not KIVÁLASZ... in a 75 px button
            string bare = t.EndsWith("...") ? t[..^3] : null;
            t = bare != null && TextWidth(p, bare, false, Ui.F95) <= avail ? bare : Fit(p, t, avail);
        }
        Text(p, t, new RectangleF(r.X + 1, r.Y, r.Width - 2, r.Height), c, false, TextAlignment.Center, true, false, Ui.F95);
        if (key >= 0 && enabled && !lit && key < t.Length && !t.EndsWith("..."))
        {
            float full = TextWidth(p, t, false, Ui.F95);
            var (pre, ch) = Ui.AmpParts(t, key);
            float x = MathF.Round(r.X + 1 + MathF.Floor((r.Width - 2 - full) / 2) + TextWidth(p, pre, false, Ui.F95));
            Text(p, ch, new RectangleF(x, r.Y, 8, r.Height), HOT, false, TextAlignment.Leading, true, false, Ui.F95);
        }
    }

    // what a 95 picture button says on a terminal
    public static string IconWord(W95Icons.Id id) => id switch
    {
        W95Icons.Id.Keyboard => "KBD",
        W95Icons.Id.Computer or W95Icons.Id.ComputerOff => "MON",
        W95Icons.Id.Center => "CTR",
        W95Icons.Id.Moon => "NITE",
        W95Icons.Id.Gear => "SETUP",
        W95Icons.Id.Clock => "CLOCK",
        W95Icons.Id.Folder => "DIR",
        W95Icons.Id.Note => "\u266A",
        W95Icons.Id.Window => "WIN",
        W95Icons.Id.Layouts => "LAYOUT",
        W95Icons.Id.Palette => "LOOK",
        W95Icons.Id.Delete => "DEL",
        W95Icons.Id.Download => "GET",
        W95Icons.Id.Dock => "HEX",
        W95Icons.Id.People => "ROOM",
        W95Icons.Id.Chat => "CHAT",
        W95Icons.Id.Theater => "CINE",
        W95Icons.Id.Mine => "*",
        W95Icons.Id.Flag => "F",
        W95Icons.Id.Info32 => "i",
        _ => "ORBIT",
    };

    public override void GlyphButton(Painter p, RectangleF r, string[] glyph, bool enabled, bool down, bool hover)
    {
        bool lit = down && enabled;
        if (lit) Lit(p, r);
        else { p.Fill(r, BG); Box(p, r, hover && enabled ? INK : FRAME); }
        float gw = glyph[0].Length, gh = glyph.Length;
        float gx = r.X + MathF.Floor((r.Width - gw) / 2), gy = r.Y + MathF.Floor((r.Height - gh) / 2);
        p.Pixels(glyph, gx, gy, lit ? BG : !enabled ? DIM : hover ? HOT : INK);
    }

    public override RectangleF KeyCap(Painter p, RectangleF r, bool oneShot, bool down, bool latched, bool hover) =>
        ButtonFace(p, r, oneShot, down, latched, hover);

    // [X] Label
    public override void CheckBox(Painter p, float x, float y, string label, bool value, bool enabled, bool held, bool hover)
    {
        bool lit = held && enabled;
        if (lit) Lit(p, new RectangleF(x - 4, y, 19, 13));
        uint c = lit ? BG : !enabled ? DIM : hover ? HOT : INK;
        Put(p, value ? "[X]" : "[ ]", x - 3, y + 1, c);
        Label(p, label, new RectangleF(x + 19, y - 1, 400, 15), enabled, TextAlignment.Leading, WindowText, false);
    }

    // (*) Label
    public override void Radio(Painter p, float x, float y, string label, bool on, bool enabled, bool held, bool hover)
    {
        bool lit = held && enabled;
        if (lit) Lit(p, new RectangleF(x - 4, y - 1, 19, 13));
        uint c = lit ? BG : !enabled ? DIM : hover ? HOT : INK;
        Put(p, on ? "(*)" : "( )", x - 3, y, c);
        Label(p, label, new RectangleF(x + 18, y - 2, 400, 15), enabled, TextAlignment.Leading, WindowText, false);
    }

    // a single-line box, its name on the top line
    public override void GroupBox(Painter p, RectangleF r, string title)
    {
        Box(p, new RectangleF(r.X, r.Y + 6, r.Width - 1, r.Height - 7), FRAME);
        if (string.IsNullOrEmpty(title)) return;
        float tw = TextWidth(p, Up(Ui.Amp(title).text), false, Ui.F95);
        p.Fill(new RectangleF(r.X + 5, r.Y, tw + 6, 13), BG);
        Label(p, title, new RectangleF(r.X + 8, r.Y, tw + 2, 13), true, TextAlignment.Leading, WindowText, false);
    }

    // [=====#------]: the part up to the thumb a double line, the rest dots, the thumb a lit block
    public override void Trackbar(Painter p, RectangleF r, float t, bool enabled, int ticks, bool hover, bool held)
    {
        var ch = new RectangleF(r.X + 6, r.Y + 8, r.Width - 12, 4);
        uint c = !enabled ? DIM : held || hover ? HOT : INK;
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 21), BG);
        Bracket(p, ch.X - 5, r.Y + 3, r.Y + 17, true, enabled ? FRAME : DIM);
        Bracket(p, ch.Right + 4, r.Y + 3, r.Y + 17, false, enabled ? FRAME : DIM);
        float tx = MathF.Round(ch.X + ch.Width * t);
        if (tx - 3 > ch.X)
        {
            p.Fill(new RectangleF(ch.X, ch.Y, tx - 3 - ch.X, 1), enabled ? INK : DIM);
            p.Fill(new RectangleF(ch.X, ch.Y + 2, tx - 3 - ch.X, 1), enabled ? INK : DIM);
        }
        for (float x = tx + 4; x < ch.Right; x += 2) p.Fill(new RectangleF(x, ch.Y + 1, 1, 1), DIM);
        p.Fill(new RectangleF(tx - 2, r.Y + 4, 5, 13), c);
        if (held) p.Fill(new RectangleF(tx, r.Y + 6, 1, 9), BG);
        if (ticks > 1)
            for (int i = 0; i < ticks; i++)
            {
                float x = MathF.Round(ch.X + (ch.Width - 1) * i / (ticks - 1));
                p.Fill(new RectangleF(x, r.Y + 20, 1, 1), DIM);
            }
    }

    // [#######---]: # as far as it goes, - the rest; a warning's # the brightest green
    public override void Progress(Painter p, RectangleF r, float frac, uint block)
    {
        p.Fill(r, BG);
        uint c = Warm(block) ? HOT : INK;
        int cells = (int)((r.Width + 1) / 6) - 2;
        if (cells < 3)
        {
            Box(p, r, DIM);
            float w = MathF.Round((r.Width - 4) * Math.Clamp(frac, 0, 1));
            if (w > 0) p.Fill(new RectangleF(r.X + 2, r.Y + 2, w, r.Height - 4), c);
            return;
        }
        int on = (int)MathF.Round(Math.Clamp(frac, 0, 1) * cells);
        if (on == 0 && frac >= 0.01f) on = 1;
        float x = r.X + MathF.Floor((r.Width - ((cells + 2) * 6 - 1)) / 2);
        var f = FontAt(1);
        float y = r.Y + MathF.Floor((r.Height - CellH) / 2);
        void Seg(string s, uint col)
        {
            if (s.Length == 0) return;
            float w = f.Measure(s, false);
            f.Draw(p, s, new RectangleF(x, y, w, CellH), col, false, TextAlignment.Leading, ParagraphAlignment.Near, false);
            x += w;
        }
        if (r.Height < CellH) p.PushClip(r);
        Seg("[", DIM);
        Seg(new string('#', on), c);
        Seg(new string('-', cells - on), DIM);
        Seg("]", DIM);
        if (r.Height < CellH) p.PopClip();
    }

    // a menu bar: the tabs lit, the open one dark (the way a text-mode menu shows where you are)
    public override void Tab(Painter p, RectangleF r, string label, bool selected, bool hover)
    {
        if (selected) r = new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
        if (selected) p.Fill(r, BG);
        else Lit(p, r);
        Label(p, label, new RectangleF(r.X, r.Y, r.Width, r.Height), true, TextAlignment.Center, selected ? INK : hover ? Ph(0.15f) : BG, false);
        if (hover && !selected) p.Fill(new RectangleF(r.X + 3, r.Bottom - 2, r.Width - 6, 1), BG);
    }

    public override void ListBox(Painter p, RectangleF r)
    {
        p.Fill(r, BG);
        Box(p, r, FRAME);
    }

    public override void ListSelection(Painter p, RectangleF row) => Lit(p, row);

    public override void ScrollBar(Painter p, RectangleF bar, RectangleF up, RectangleF down, RectangleF thumb, bool upHeld, bool downHeld)
    {
        p.FillChecker(bar, FAINT, BG);
        Arrow(p, up, UpGlyph, upHeld);
        Arrow(p, down, DownGlyph, downHeld);
        p.Fill(RectangleF.Inflate(thumb, -2, 0), MID);
    }

    static void Arrow(Painter p, RectangleF r, string[] glyph, bool held)
    {
        if (held) Lit(p, r);
        else { p.Fill(r, BG); Box(p, r, FRAME); }
        p.Pixels(glyph, r.X + 5, r.Y + 6, held ? BG : INK);
    }

    // [text      ]
    public override void EditBox(Painter p, RectangleF r, string text, bool enabled)
    {
        p.Fill(r, BG);
        uint fc = enabled ? FRAME : DIM;
        Bracket(p, r.X, r.Y + 2, r.Bottom - 2, true, fc);
        Bracket(p, r.Right - 1, r.Y + 2, r.Bottom - 2, false, fc);
        Text(p, text, new RectangleF(r.X + 5, r.Y + 2, r.Width - 8, r.Height - 4), enabled ? INK : DIM, false,
            TextAlignment.Leading, true, false, Ui.F95);
    }

    // [value     v]: lit while its list is open or it is held
    public override void ComboBox(Painter p, RectangleF r, string text, bool enabled, bool down, bool open, bool hover)
    {
        bool lit = (open || down) && enabled;
        if (lit) Lit(p, r);
        else p.Fill(r, BG);
        uint fc = lit ? BG : !enabled ? DIM : hover ? HOT : FRAME;
        Bracket(p, r.X, r.Y + 2, r.Bottom - 2, true, fc);
        Bracket(p, r.Right - 1, r.Y + 2, r.Bottom - 2, false, fc);
        float gy = MathF.Floor(r.Y + (r.Height - 4) / 2);
        p.Pixels(DownGlyph, r.Right - 12, gy, lit ? BG : enabled ? (hover ? HOT : MID) : DIM);
        Text(p, text, new RectangleF(r.X + 5, r.Y + 1, r.Width - 20, r.Height - 2), lit ? BG : !enabled ? DIM : hover ? HOT : INK, false,
            TextAlignment.Leading, true, false, Ui.F95);
    }

    public override void DropList(Painter p, RectangleF r, string[] items, int hot, float rowH)
    {
        p.Fill(r, BG);
        Box(p, r, INK);
        for (int i = 0; i < items.Length; i++)
        {
            var row = new RectangleF(r.X + 1, r.Y + 1 + i * rowH, r.Width - 2, rowH);
            bool on = i == hot;
            if (on) Lit(p, row);
            Text(p, items[i], new RectangleF(row.X + 4, row.Y, row.Width - 6, rowH), on ? BG : INK, false, TextAlignment.Leading, true, false, Ui.F95);
        }
    }

    // a status line's field: a | before it
    public override void StatusPane(Painter p, RectangleF r, string text, uint dot)
    {
        p.Fill(new RectangleF(r.X, r.Y + 2, 1, r.Height - 4), DIM);
        float x = r.X + 4;
        if (dot != 0) { p.Fill(new RectangleF(x, r.Y + (r.Height - 6) / 2, 6, 6), dot); x += 10; }
        Text(p, text, new RectangleF(x, r.Y, r.Right - x - 2, r.Height), WindowText, false, TextAlignment.Leading, true, false, Ui.F95);
    }

    public override void MenuItem(Painter p, float x, float y, string text)
    {
        string t = Up(text);
        Text(p, t, new RectangleF(x, y, TextWidth(p, t, false, Ui.F95) + 2, 15), INK, false, TextAlignment.Leading, true, false, Ui.F95);
        Text(p, t[..1], new RectangleF(x, y, 8, 15), HOT, false, TextAlignment.Leading, true, false, Ui.F95);
    }

    // ---- icons: the 95 pictures as a monochrome graphics terminal shows them ----
    // Each 95 pixel glows by how dark it was (the black outlines brightest, the white fills the black glass), quantised to three
    // levels of the phosphor; the light pixels are left out so the screen shows through. Minesweeper's own pictures stay as they are.

    static readonly Dictionary<(Painter, W95Icons.Id), Vortice.Direct2D1.ID2D1Bitmap> _icons = new();
    static System.Reflection.MethodInfo _iconMap, _iconPal;

    public override bool Icon(Painter p, W95Icons.Id id, float x, float y, int scale)
    {
        if (id is W95Icons.Id.Mine or W95Icons.Id.Flag) return false;
        if (!_icons.TryGetValue((p, id), out var bmp))
        {
            bmp = null;
            try
            {
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
                _iconMap ??= typeof(W95Icons).GetMethod("Map", F);
                _iconPal ??= typeof(W95Icons).GetMethod("Pal", F);
                if (_iconMap?.Invoke(null, new object[] { id }) is string[] map && map.Length > 1 && _iconPal != null)
                {
                    int h = map.Length, w = map[0].Length;
                    var px = new uint[w * h];
                    for (int yy = 0; yy < h; yy++)
                        for (int xx = 0; xx < w && xx < map[yy].Length; xx++)
                        {
                            uint c = (uint)_iconPal.Invoke(null, new object[] { map[yy][xx] });
                            if ((c >> 24) == 0) continue;
                            float g = Glow(c);
                            px[yy * w + xx] = g < 0.2f ? 0 : g > 1.05f ? HOT : g >= 0.75f ? INK : g >= 0.45f ? MID : DIM;
                        }
                    bmp = p.CreateBitmap(px, w, h);
                }
            }
            catch (Exception) { bmp = null; }
            _icons[(p, id)] = bmp;
        }
        if (bmp == null) return false;
        int n = W95Icons.Size(id) * scale;
        p.ImageNearest(bmp, new RectangleF(x, y, n, n));
        return true;
    }
}
