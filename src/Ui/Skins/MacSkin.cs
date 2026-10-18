using System;
using System.Collections.Generic;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// Classic Mac OS (System 7 / Mac OS 8 platinum): platinum grey, the pinstriped title bar with its close and zoom
// boxes, a Chicago-like bitmap font (Geneva-like for small text), chunky black-and-white controls, the default button's thick ring.
// Mac OS 8's Platinum appearance: #DDDDDD windows outlined in black, white / grey bevels, rounded push buttons, popup menus with
// their double arrow, tabs with rounded tops, sliders with a pointer thumb, the lavender-blue highlight. Chicago for titles,
// buttons, menus and bold text, Geneva for the rest (MacFonts.cs: both drawn by hand, neither comes with Windows); the bigger
// clock sizes are Chicago blown up with Scale2x / Scale3x, the way the bitmap era smoothed fat bits. No access-key underlines
// and no hover: the Mac had neither. Whole screens that the Mac laid out its own way are in Mac/*.Mac.cs.
public sealed partial class MacSkin : Win95Skin
{
    public override RetroStyle Style => RetroStyle.Mac;
    public override string Id => "mac";
    public override string Tag => "mac";
    public override string Name => "Classic Mac";

    // ---- the palette: Platinum ----
    public override uint Face => 0xFFDDDDDD;
    public override uint Light => 0xFFEEEEEE;
    public override uint Hilight => 0xFFFFFFFF;
    public override uint Shadow => 0xFF999999;
    public override uint DarkShadow => 0xFF000000;
    public override uint Window => 0xFFFFFFFF;
    public override uint WindowText => 0xFF000000;
    public override uint ButtonText => 0xFF000000;
    public override uint GrayText => 0xFF8A8A8A;
    public override uint DimText => 0xFF444444;
    public override uint Highlight => 0xFF333399;
    public override uint HighlightText => 0xFFFFFFFF;
    public override uint ActiveCaption => 0xFFDDDDDD;
    public override uint CaptionText => 0xFF000000;
    public override uint InactiveCaption => 0xFFEEEEEE;
    public override uint InactiveCaptionText => 0xFF8A8A8A;
    public override uint Desktop => 0xFF6D82AA;
    public override uint Laser => 0xFF8C9CF0;          // the highlight's lavender, light enough for a dark world

    // the greys between (Platinum's ramp)
    public const uint Grey1 = 0xFFCCCCCC, Grey2 = 0xFFBBBBBB, Grey3 = 0xFFAAAAAA, Grey5 = 0xFF777777, Grey6 = 0xFF555555, Grey7 = 0xFF444444;

    // the 1995 colours the shared screens still name: Windows' navy is the Mac's highlight, its grey the platinum face, its maroon
    // Mac red, its teal the desktop
    public override uint MapColor(uint c) => c switch
    {
        0xFF000080 => 0xFF333399,
        0xFFC0C0C0 => 0xFFDDDDDD,
        0xFFDFDFDF => 0xFFEEEEEE,
        0xFF800000 => 0xFFCC0000,
        0xFF008080 => 0xFF6D82AA,
        0xFF004040 => 0xFF2B3A5C,
        _ => c,
    };

    public override uint TextColor(uint c) => c == 0xFF808080 ? 0xFF6E6E6E : c;     // grey words stay readable on platinum

    // ---- type ----
    public override string Font => "Segoe UI";          // what DirectWrite falls back to off the pixel canvas; on it, the bitmap fonts below
    public override bool SmoothText => false;
    public override float DropRowH => 16;               // a Mac menu's row

    static PixelFont _chicago, _geneva;
    static readonly Dictionary<int, PixelFont> _big = new();
    static readonly char NoGlyph = (char)0xFFFF;
    public static PixelFont Chicago => _chicago ??= new PixelFont(13, 11, ChicagoGlyphs) { Spacing = 1, SpaceWidth = 4, Smear = false, Fallback = NoGlyph };
    public static PixelFont Geneva => _geneva ??= new PixelFont(13, 11, GenevaGlyphs) { Spacing = 1, SpaceWidth = 3, Smear = false, Fallback = NoGlyph };

    // bold = Chicago; plain = Geneva up to 15 px, Chicago from 16 (the keys, the big digits' small cousins); from 25 px Chicago blown up
    public override PixelFont PixelFont(string family, float size, FontWeight weight)
    {
        if (family == Theme.IconFont) return null;
        int s = (int)MathF.Floor(size * 0.08f + 0.0001f);
        if (s >= 2) return Big(Math.Min(s, 10));
        return weight >= FontWeight.SemiBold || size >= 15.5f ? Chicago : Geneva;
    }

    // Chicago s times as big, Scale2x / Scale3x smoothing its stairs (ASCII, and the accented letters of the languages OrbitOverlay
    // speaks: a big Magyar word - "Kész!" on a timer - in Chicago too, not in hollow boxes)
    const string BigExtra = "áéíóöőúüűÁÉÍÓÖŐÚÜŰ";

    static PixelFont Big(int s)
    {
        if (_big.TryGetValue(s, out var f)) return f;
        var glyphs = new Dictionary<char, string[]>();
        foreach (var (ch, rows) in ChicagoGlyphs)
        {
            if (ch > 126 && BigExtra.IndexOf(ch) < 0) continue;
            var g = new string[13];
            int w = rows[0].Length;
            for (int y = 0; y < 13; y++) g[y] = y < rows.Length ? rows[y] : new string('.', w);
            glyphs[ch] = Upscale(g, s);
        }
        f = new PixelFont(13 * s, 11 * s, glyphs) { Spacing = s, SpaceWidth = 4 * s, Smear = false, Fallback = NoGlyph };
        _big[s] = f;
        return f;
    }

    static string[] Upscale(string[] g, int s)
    {
        if (s == 1) return g;
        if (s % 2 == 0 && s != 2 && s % 3 != 0) return Upscale(Scale2(g), s / 2);
        if (s == 2) return Scale2(g);
        if (s == 3) return Scale3(g);
        if (s % 3 == 0) return Upscale(Scale3(g), s / 3);
        // 5, 7: plain fat bits
        var o = new string[g.Length * s];
        for (int y = 0; y < g.Length; y++)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in g[y]) sb.Append(c == '.' ? '.' : 'k', s);
            for (int k = 0; k < s; k++) o[y * s + k] = sb.ToString();
        }
        return o;
    }

    static bool At(string[] g, int x, int y) => y >= 0 && y < g.Length && x >= 0 && x < g[y].Length && g[y][x] != '.';

    // EPX / Scale2x
    static string[] Scale2(string[] g)
    {
        int h = g.Length, w = g[0].Length;
        var o = new char[h * 2][];
        for (int i = 0; i < h * 2; i++) o[i] = new char[w * 2];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool p = At(g, x, y), a = At(g, x, y - 1), b = At(g, x + 1, y), c = At(g, x - 1, y), d = At(g, x, y + 1);
                bool e0 = c == a && c != d && a != b ? a : p, e1 = a == b && a != c && b != d ? b : p;
                bool e2 = d == c && d != b && c != a ? c : p, e3 = b == d && b != a && d != c ? d : p;
                o[y * 2][x * 2] = e0 ? 'k' : '.'; o[y * 2][x * 2 + 1] = e1 ? 'k' : '.';
                o[y * 2 + 1][x * 2] = e2 ? 'k' : '.'; o[y * 2 + 1][x * 2 + 1] = e3 ? 'k' : '.';
            }
        var r = new string[h * 2];
        for (int i = 0; i < r.Length; i++) r[i] = new string(o[i]);
        return r;
    }

    // Scale3x (AdvMAME3x)
    static string[] Scale3(string[] g)
    {
        int h = g.Length, w = g[0].Length;
        var o = new char[h * 3][];
        for (int i = 0; i < h * 3; i++) o[i] = new char[w * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool A = At(g, x - 1, y - 1), B = At(g, x, y - 1), C = At(g, x + 1, y - 1);
                bool D = At(g, x - 1, y), E = At(g, x, y), F = At(g, x + 1, y);
                bool G = At(g, x - 1, y + 1), H = At(g, x, y + 1), I = At(g, x + 1, y + 1);
                bool[] e = new bool[9];
                if (B != H && D != F)
                {
                    e[0] = D == B ? D : E;
                    e[1] = (D == B && E != C) || (B == F && E != A) ? B : E;
                    e[2] = B == F ? F : E;
                    e[3] = (D == B && E != G) || (D == H && E != A) ? D : E;
                    e[4] = E;
                    e[5] = (B == F && E != I) || (H == F && E != C) ? F : E;
                    e[6] = D == H ? D : E;
                    e[7] = (D == H && E != I) || (H == F && E != G) ? H : E;
                    e[8] = H == F ? F : E;
                }
                else for (int k = 0; k < 9; k++) e[k] = E;
                for (int k = 0; k < 9; k++) o[y * 3 + k / 3][x * 3 + k % 3] = e[k] ? 'k' : '.';
            }
        var r = new string[h * 3];
        for (int i = 0; i < r.Length; i++) r[i] = new string(o[i]);
        return r;
    }

    // text in Chicago (bold) or Geneva on the pixel canvas
    void Say(Painter p, string s, RectangleF r, uint c, bool chicago, TextAlignment al = TextAlignment.Center,
        ParagraphAlignment v = ParagraphAlignment.Center, float size = Ui.F95) =>
        p.Text(s, r, c, size, chicago ? FontWeight.Bold : FontWeight.Normal, al, v, Font);

    public float ChicagoWidth(Painter p, string s) => TextWidth(p, s, true, Ui.F95);

    // ---- text ----

    // no access keys on a Mac: the label is just its words
    public override void Label(Painter p, string s, RectangleF r, bool enabled, TextAlignment al, uint c, bool bold)
    {
        var (t, _) = Ui.Amp(s);
        if (string.IsNullOrEmpty(t)) return;
        Text(p, t, r, c, bold, al, enabled, false, Ui.F95);
    }

    // dimmed, not etched
    public override void DisabledText(Painter p, string s, RectangleF r, float size, FontWeight w, TextAlignment h, ParagraphAlignment v) =>
        p.Text(s, r, GrayText, size, w, h, v, Font);

    // ---- pixel shapes: rounded rectangles the way QuickDraw stepped them ----

    static readonly int[][] Corners =
    {
        Array.Empty<int>(), new[] { 1 }, new[] { 2, 1 }, new[] { 3, 1, 1 }, new[] { 4, 2, 1, 1 }, new[] { 5, 3, 2, 1, 1 }, new[] { 6, 4, 3, 2, 1, 1 },
    };

    static int Inset(int rad, int row, int h)
    {
        var cs = Corners[Math.Clamp(rad, 0, Corners.Length - 1)];
        int k = Math.Min(row, h - 1 - row);
        return k < cs.Length && k < h / 2 + 1 ? cs[k] : 0;
    }

    public static void FillRR(Painter p, RectangleF r, int rad, uint c)
    {
        int x = (int)MathF.Round(r.X), y = (int)MathF.Round(r.Y), w = (int)MathF.Round(r.Width), h = (int)MathF.Round(r.Height);
        if (w <= 0 || h <= 0) return;
        rad = Math.Min(rad, Math.Min(w, h) / 2);
        int n = Corners[Math.Clamp(rad, 0, Corners.Length - 1)].Length;
        for (int i = 0; i < n && i < h; i++)
        {
            int ins = Inset(rad, i, h);
            p.Fill(new RectangleF(x + ins, y + i, w - 2 * ins, 1), c);
            if (h - 1 - i > i) p.Fill(new RectangleF(x + ins, y + h - 1 - i, w - 2 * ins, 1), c);
        }
        if (h - 2 * n > 0) p.Fill(new RectangleF(x, y + n, w, h - 2 * n), c);
    }

    // the outline; tl / br colour the top-left and bottom-right halves (a bevel), one colour for a plain line
    public static void StrokeRR(Painter p, RectangleF r, int rad, uint tl, uint br)
    {
        int x = (int)MathF.Round(r.X), y = (int)MathF.Round(r.Y), w = (int)MathF.Round(r.Width), h = (int)MathF.Round(r.Height);
        if (w <= 0 || h <= 0) return;
        rad = Math.Min(rad, Math.Min(w, h) / 2);
        int n = Corners[Math.Clamp(rad, 0, Corners.Length - 1)].Length;
        int top0 = Inset(rad, 0, h);
        p.Fill(new RectangleF(x + top0, y, w - 2 * top0, 1), tl);
        if (h > 1) p.Fill(new RectangleF(x + top0, y + h - 1, w - 2 * top0, 1), br);
        for (int i = 1; i < h - 1; i++)
        {
            if (i >= n && i < h - n)
            {
                int len = h - 2 * n;
                p.Fill(new RectangleF(x, y + i, 1, len), tl);
                p.Fill(new RectangleF(x + w - 1, y + i, 1, len), br);
                i += len - 1;
                continue;
            }
            int ins = Inset(rad, i, h);
            int prev = Inset(rad, i < h / 2 ? i - 1 : i + 1, h);
            int run = Math.Max(1, prev - ins);
            uint c = i < h / 2 ? tl : br;          // the top corners light, the bottom ones dark
            p.Fill(new RectangleF(x + ins, y + i, run, 1), c);
            p.Fill(new RectangleF(x + w - ins - run, y + i, run, 1), c);
        }
    }

    static void Rect(Painter p, RectangleF r, uint tl, uint br) => Frame(p, r, tl, br, 1);

    // ---- frames ----

    public override void Bevel(Painter p, RectangleF r, Painter.Edge e)
    {
        var i1 = RectangleF.Inflate(r, -1, -1);
        switch (e)
        {
            case Painter.Edge.Window:           // a window: black outline, then the platinum frame's light and shadow
                Rect(p, r, DarkShadow, DarkShadow);
                Rect(p, i1, Hilight, Shadow);
                break;
            case Painter.Edge.Page:             // a tab control's pane
                Rect(p, r, Grey6, Grey6);
                Rect(p, i1, Hilight, Grey3);
                break;
            case Painter.Edge.Raised:           // a bevel button / placard
                StrokeRR(p, r, 1, Grey6, Grey6);
                Rect(p, i1, Hilight, Grey3);
                break;
            case Painter.Edge.Pressed:
                StrokeRR(p, r, 1, Grey6, Grey6);
                Rect(p, i1, Grey5, Grey2);
                break;
            case Painter.Edge.Sunken:           // a text field, a list, a picture well: the inset edge, then QuickDraw's black frame
                Rect(p, r, Shadow, Hilight);
                Rect(p, i1, Grey7, Grey7);
                break;
            default:                            // etched (a group box)
                Rect(p, r, Shadow, Hilight);
                Rect(p, i1, Hilight, Shadow);
                break;
        }
    }

    public override void FillDesktop(Painter p, RectangleF r) => p.Fill(r, Desktop);

    // the pinstripes: six engraved lines (grey with white under) across a title bar, between x0 and x1
    public void Stripes(Painter p, float x0, float x1, float y, int lines = 6, float gap = 2)
    {
        if (x1 - x0 < 1) return;
        for (int i = 0; i < lines; i++)
        {
            p.Fill(new RectangleF(x0, y + i * gap, x1 - x0, 1), Shadow);
            p.Fill(new RectangleF(x0, y + i * gap + 1, x1 - x0, 1), Hilight);
        }
    }

    // Window titles of 1995 that the Mac called otherwise
    public static string MacTitle(string t) => t switch
    {
        "Power Meter" => "Batteries",
        "CD Player" => "Now Playing",
        "Volume Control" => "Sound",
        "Date/Time" => "Date & Time",
        "Session" => "Time in VR",
        _ => t,
    };

    // a document window's title bar: pinstripes either side of the centred Chicago title (the boxes come after)
    public override void Caption(Painter p, RectangleF r, string title, W95Icons.Id? icon, bool active)
    {
        p.Fill(r, active ? ActiveCaption : InactiveCaption);
        title = MacTitle(title);
        float tw = string.IsNullOrEmpty(title) ? 0 : Math.Min(ChicagoWidth(p, title), r.Width - 70);
        string t = tw > 0 ? Fit(p, title, tw + 1, Ui.F95, true) : null;
        float tx = MathF.Floor(r.X + (r.Width - tw) / 2);
        if (active)
        {
            float sy = r.Y + 3;
            Stripes(p, r.X + 2, tw > 0 ? tx - 6 : r.Right - 2, sy);
            if (tw > 0) Stripes(p, tx + tw + 6, r.Right - 2, sy);
        }
        if (t != null) Say(p, t, new RectangleF(tx, r.Y + 1, tw + 2, r.Height - 2), active ? CaptionText : InactiveCaptionText, true,
            TextAlignment.Leading);
    }

    // ---- whole windows (the screens in Mac/*.Mac.cs) ----

    // a Platinum window: the black outline and raised frame, the title bar (stripes, the title centred), the dark line under it.
    // The boxes come after it - the close box on the LEFT (CloseX), the collapse box on the right (CollapseX). Returns the face
    // under the title bar.
    public RectangleF MacWindow(Painter p, RectangleF win, string title, bool active = true)
    {
        p.Fill(win, Face);
        Bevel(p, win, Painter.Edge.Window);
        Caption(p, new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), title, null, active);
        p.Fill(new RectangleF(win.X + 1, win.Y + 21, win.Width - 2, 1), Grey5);
        p.Fill(new RectangleF(win.X + 2, win.Y + 22, win.Width - 4, 1), Hilight);
        return new RectangleF(win.X + 2, win.Y + 22, win.Width - 4, win.Height - 24);
    }

    // where a window's boxes go (the 16 x 14 targets Ui.CaptionClose95 / CaptionMin95 take)
    public static float CloseX(RectangleF win) => win.X + 5;
    public static float CollapseX(RectangleF win) => win.Right - 5 - 16;
    public static float BoxY(RectangleF win) => win.Y + 5;

    // a white well with QuickDraw's black frame (a text field, a clock's face, a graph)
    public void Well(Painter p, RectangleF r, uint fill = 0xFFFFFFFF)
    {
        p.Fill(r, fill);
        Bevel(p, r, Painter.Edge.Sunken);
    }

    // a placard / an inset group: a light line outside a grey one (the Finder's info strip, a Mac OS 8 secondary group box)
    public void Inset(Painter p, RectangleF r)
    {
        Rect(p, r, Shadow, Hilight);
        Rect(p, RectangleF.Inflate(r, -1, -1), Hilight, Shadow);
    }

    // a filled circle on whole pixels (QuickDraw's PaintOval / FrameOval): the fill, then an edge 'edgeW' thick
    public static void Disc(Painter p, float cx, float cy, float r, uint fill, uint edge, int edgeW)
    {
        int R = (int)MathF.Round(r), x0 = (int)MathF.Round(cx), y0 = (int)MathF.Round(cy);
        int ri = R - edgeW;
        for (int dy = -R; dy <= R; dy++)
        {
            int ho = (int)MathF.Floor(MathF.Sqrt(MathF.Max(0, R * R - dy * dy)) + 0.5f);
            int hi = Math.Abs(dy) < ri ? (int)MathF.Floor(MathF.Sqrt(MathF.Max(0, ri * ri - dy * dy)) + 0.5f) : -1;
            if (hi >= 0)
            {
                if (fill != 0) p.Fill(new RectangleF(x0 - hi, y0 + dy, 2 * hi + 1, 1), fill);
                p.Fill(new RectangleF(x0 - ho, y0 + dy, ho - hi, 1), edge);
                p.Fill(new RectangleF(x0 + hi + 1, y0 + dy, ho - hi, 1), edge);
            }
            else p.Fill(new RectangleF(x0 - ho, y0 + dy, 2 * ho + 1, 1), edge);
        }
    }

    // a modal alert's frame (no title bar): the black outline, the raised Platinum border, the dark line inside it
    public static void AlertFrame(Painter p, RectangleF r)
    {
        p.Fill(r, 0xFFDDDDDD);
        Rect(p, r, 0xFF000000, 0xFF000000);
        Rect(p, RectangleF.Inflate(r, -1, -1), 0xFFFFFFFF, 0xFF999999);
        Rect(p, RectangleF.Inflate(r, -3, -3), 0xFF999999, 0xFFFFFFFF);
    }

    // the caution icon (32 x 32): the yellow triangle with its black edge and "!"
    public static void Caution(Painter p, float x, float y)
    {
        x = MathF.Round(x); y = MathF.Round(y);
        for (int i = 0; i < 28; i++)
        {
            int half = i * 15 / 27 + 1;
            float cx = x + 16, ry = y + 2 + i;
            if (i == 27) { p.Fill(new RectangleF(cx - half, ry, 2 * half, 2), 0xFF000000); break; }
            p.Fill(new RectangleF(cx - half, ry, 2 * half, 1), i < 2 ? 0xFF000000 : 0xFFFFCC00);
            p.Fill(new RectangleF(cx - half, ry, 2, 1), 0xFF000000);
            p.Fill(new RectangleF(cx + half - 2, ry, 2, 1), 0xFF000000);
        }
        p.Fill(new RectangleF(x + 15, y + 10, 2, 11), 0xFF000000);
        p.Fill(new RectangleF(x + 14, y + 11, 4, 8), 0xFF000000);
        p.Fill(new RectangleF(x + 14, y + 23, 4, 3), 0xFF000000);
    }

    // a dotted line across (QuickDraw's grey pattern pen): every other pixel
    public static void DotsH(Painter p, float x0, float x1, float y, uint c, int step = 2)
    {
        for (float x = x0; x < x1; x += step) p.Fill(new RectangleF(x, y, 1, 1), c);
    }

    // the boxes of a title bar: close (a plain box), zoom (a box in the box), collapse (the window shade's bar), help (Balloon Help's ?)
    static readonly string[] BoxHelp = { ".kkk.", "k...k", "...k.", "..k..", "..k..", ".....", "..k.." };

    public override void CaptionButton(Painter p, RectangleF r, CaptionKind kind, bool enabled, bool down, bool hover)
    {
        p.Fill(new RectangleF(r.X - 2, r.Y, r.Width + 4, r.Height), ActiveCaption);
        TitleBox(p, r.X + 2, r.Y + 1, 11, kind, enabled, down);
    }

    // one title bar box (n x n) at x, y
    public void TitleBox(Painter p, float x, float y, int n, CaptionKind kind, bool enabled, bool down)
    {
        var b = new RectangleF(x, y, n, n);
        if (!enabled) { Rect(p, b, Shadow, Shadow); return; }
        Rect(p, b, Grey6, Grey6);
        var i = RectangleF.Inflate(b, -1, -1);
        if (down)
        {
            p.Fill(i, Grey5);
            Rect(p, i, Grey6, Grey3);
        }
        else
        {
            p.Fill(i, Face);
            Rect(p, i, Hilight, Shadow);
        }
        uint ink = down ? Hilight : Grey6;
        switch (kind)
        {
            case CaptionKind.Max:
                p.Fill(new RectangleF(x + 1, y + 1, n / 2, n / 2), down ? Grey5 : Light);
                Rect(p, new RectangleF(x, y, n / 2 + 2, n / 2 + 2), Grey6, Grey6);
                break;
            case CaptionKind.Min:
                Rect(p, new RectangleF(x + 2, y + n / 2 - 1, n - 4, 3), ink, ink);
                break;
            case CaptionKind.Help:
                if (n >= 11) p.Pixels(BoxHelp, x + (n - 5) / 2, y + (n - 7) / 2, ink);
                break;
        }
    }

    // a floating window (Mac OS 8's utility window): a thin striped title bar with a small close box, or no title at all - then a
    // white card, the face's little information windows
    public override RectangleF WidgetFrame(Painter p, RectangleF r, string title)
    {
        if (title == null)
        {
            p.Fill(r, Window);
            Rect(p, r, DarkShadow, DarkShadow);
            Rect(p, RectangleF.Inflate(r, -1, -1), Hilight, Grey2);
            return RectangleF.Inflate(r, -3, -3);
        }
        p.Fill(r, Face);
        Rect(p, r, DarkShadow, DarkShadow);
        var bar = new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, 13);
        TitleBar(p, bar, MacTitle(title));
        p.Fill(new RectangleF(r.X + 1, bar.Bottom, r.Width - 2, 1), DarkShadow);
        var inner = new RectangleF(r.X + 1, bar.Bottom + 1, r.Width - 2, r.Bottom - 1 - bar.Bottom - 1);
        Rect(p, inner, Hilight, Shadow);
        return new RectangleF(r.X + 3, bar.Bottom + 3, r.Width - 6, r.Bottom - bar.Bottom - 6);
    }

    // a utility window's 13 px title bar in r: stripes, the small close box on the left, the title centred
    public void TitleBar(Painter p, RectangleF bar, string title, bool box = true)
    {
        p.Fill(bar, Face);
        float tw = 0;
        string t = null;
        if (!string.IsNullOrEmpty(title))
        {
            t = TitleFit(p, title, bar.Width, box);
            tw = ChicagoWidth(p, t);
        }
        float x0 = bar.X + (box ? 14 : 2), sy = bar.Y + 2;
        float tx = MathF.Floor(bar.X + (bar.Width - tw) / 2);
        tx = MathF.Min(MathF.Max(tx, x0 + 5), bar.Right - 4 - tw);
        if (box) TitleBox(p, bar.X + 3, bar.Y + 2, 9, CaptionKind.Close, true, false);
        if (t != null)
        {
            if (tx - 5 > x0 + 1) Stripes(p, x0, tx - 5, sy, 5);
            if (bar.Right - 2 > tx + tw + 6) Stripes(p, tx + tw + 5, bar.Right - 2, sy, 5);
            Say(p, t, new RectangleF(tx, bar.Y, tw + 2, bar.Height), CaptionText, true, TextAlignment.Leading);
        }
        else Stripes(p, x0, bar.Right - 2, sy, 5);
    }

    // the title as the bar shows it, kept for its width: a widget's title that does not fit ("System Monitor" on a 2 x 2) was cut
    // again in every frame - new strings at rest
    readonly Dictionary<(string, float, bool), string> _titleFits = new();
    int _titleLang = -1;

    string TitleFit(Painter p, string title, float width, bool box)
    {
        if (_titleLang != Lang.Version || _titleFits.Count > 64) { _titleFits.Clear(); _titleLang = Lang.Version; }
        if (_titleFits.TryGetValue((title, width, box), out var t)) return t;
        t = Fit(p, title, width - 34, Ui.F95, true);
        // too long for the middle ("Volume M..." on a 2 x 2): whole, off-centre between the close box and the edge, if it fits so
        float room = width - (box ? 14 : 2) - 10;
        if (t != title && ChicagoWidth(p, title) <= room) t = title;
        // still not whole: its last word, if that fits whole ("Volume Mixer" -> "Mixer")
        else if (t != title && title.LastIndexOf(' ') is int sp and > 0 && ChicagoWidth(p, title[(sp + 1)..]) <= room) t = title[(sp + 1)..];
        return _titleFits[(title, width, box)] = t;
    }

    // ---- push buttons ----

    enum Look { Up, Down, On, Dim }

    // a rounded Platinum push button in r (no ring); returns the face
    RectangleF ButtonBody(Painter p, RectangleF r, Look look, int rad)
    {
        uint fill, tl, br, edge = look == Look.Dim ? Shadow : DarkShadow;
        switch (look)
        {
            case Look.Down: fill = Grey3; tl = Grey5; br = Grey1; break;
            case Look.On: fill = Highlight; tl = 0xFF6666BB; br = 0xFF222266; break;
            default: fill = Face; tl = Hilight; br = Shadow; break;
        }
        var i1 = RectangleF.Inflate(r, -1, -1);
        FillRR(p, i1, rad - 1, br);
        FillRR(p, new RectangleF(i1.X, i1.Y, i1.Width - 1, i1.Height - 1), rad - 1, tl);
        var face = RectangleF.Inflate(i1, -1, -1);
        FillRR(p, face, rad - 2, fill);
        if (look is Look.Up or Look.Dim && face.Height > 6)
            p.Fill(new RectangleF(face.X + 1, face.Bottom - 1, face.Width - 2, 1), Grey1);      // the soft shadow under the face
        StrokeRR(p, r, rad, edge, edge);
        return face;
    }

    int Radius(RectangleF r) => r.Height >= 17 ? 4 : r.Height >= 12 ? 3 : 2;

    // a push button; the default one wears the thick black ring (inside r: the 95 metrics stay)
    public override RectangleF ButtonFace(Painter p, RectangleF r, bool isDefault, bool down, bool latched, bool hover)
    {
        var b = r;
        if (isDefault && r.Height >= 15)
        {
            int rr = Radius(r) + 1;
            StrokeRR(p, r, rr, DarkShadow, DarkShadow);
            StrokeRR(p, RectangleF.Inflate(r, -1, -1), rr - 1, DarkShadow, DarkShadow);
            if (r.Height >= 24) StrokeRR(p, RectangleF.Inflate(r, -2, -2), rr - 1, DarkShadow, DarkShadow);
            b = RectangleF.Inflate(r, r.Height >= 24 ? -4 : -3, r.Height >= 24 ? -4 : -3);
        }
        // latched here is a toggle whose label someone else draws in black: pushed in, not blue
        return ButtonBody(p, b, down ? Look.Down : latched ? Look.Down : Look.Up, Radius(b));
    }

    public override void PushButton(Painter p, RectangleF r, string label, W95Icons.Id? icon, bool enabled, bool isDefault, bool down,
        bool latched, bool hover)
    {
        var b = r;
        if (isDefault && enabled) b = ButtonFace(p, r, true, down, false, hover);
        else
        {
            var look = !enabled ? Look.Dim : down ? Look.Down : latched ? Look.On : Look.Up;
            b = ButtonBody(p, r, look, Radius(r));
        }
        uint tc = !enabled ? GrayText : latched && !down ? HighlightText : ButtonText;
        var (t, _) = Ui.Amp(label);
        if (icon is { } ic)
        {
            int n = W95Icons.Size(ic);
            float tw = string.IsNullOrEmpty(t) ? 0 : TextWidth(p, t, false, Ui.F95) + 4;
            float x = MathF.Round(b.X + (b.Width - n - tw) / 2), y = MathF.Round(b.Y + (b.Height - n) / 2);
            W95Icons.Draw(p, ic, x, y);
            if (tw > 0) Say(p, t, new RectangleF(x + n + 4, b.Y, tw, b.Height), tc, false, TextAlignment.Leading);
            return;
        }
        if (string.IsNullOrEmpty(t)) return;
        bool chicago = ChicagoWidth(p, t) <= b.Width - 6;
        Say(p, chicago ? t : Fit(p, t, b.Width - 4), b, tc, chicago);
    }

    public override void GlyphButton(Painter p, RectangleF r, string[] glyph, bool enabled, bool down, bool hover)
    {
        var b = ButtonBody(p, r, !enabled ? Look.Dim : down ? Look.Down : Look.Up, Radius(r));
        float gw = glyph[0].Length, gh = glyph.Length;
        p.Pixels(glyph, b.X + MathF.Floor((b.Width - gw) / 2), b.Y + MathF.Floor((b.Height - gh) / 2), enabled ? ButtonText : GrayText);
    }

    // a key of an Apple keyboard: a light cap, its sides falling away to the bottom
    public override RectangleF KeyCap(Painter p, RectangleF r, bool oneShot, bool down, bool latched, bool hover)
    {
        var o = RectangleF.Inflate(r, -1, -1);
        if (latched && !down)
        {
            // locked: black, its words white (the keyboard draws them)
            FillRR(p, o, 3, Grey7);
            FillRR(p, new RectangleF(o.X + 1, o.Y + 1, o.Width - 2, o.Height - 3), 3, DarkShadow);
            StrokeRR(p, o, 3, DarkShadow, DarkShadow);
            return new RectangleF(o.X + 2, o.Y + 1, o.Width - 4, o.Height - 4);
        }
        if (down)
        {
            FillRR(p, o, 3, Grey2);
            p.Fill(new RectangleF(o.X + 2, o.Y + 1, o.Width - 4, 1), Grey5);
            StrokeRR(p, o, 3, Grey6, Grey6);
            return new RectangleF(o.X + 2, o.Y + 2, o.Width - 4, o.Height - 4);
        }
        FillRR(p, o, 3, Grey2);                                                    // the sides
        var top = new RectangleF(o.X + 2, o.Y + 1, o.Width - 4, o.Height - 5);
        FillRR(p, top, 2, 0xFFF4F4F4);                                             // the top of the cap
        p.Fill(new RectangleF(top.X + 1, top.Y + top.Height - 3, top.Width - 2, 2), Light);
        StrokeRR(p, o, 3, Grey6, Grey6);
        if (oneShot) StrokeRR(p, RectangleF.Inflate(top, 1, 1), 2, DarkShadow, DarkShadow);
        return new RectangleF(top.X, top.Y - 1, top.Width, top.Height);
    }

    // ---- check boxes, radio buttons, group boxes ----

    public override void CheckBox(Painter p, float x, float y, string label, bool value, bool enabled, bool held, bool hover)
    {
        var box = new RectangleF(x, y, 12, 12);
        p.Fill(box, held && enabled ? Grey3 : Window);
        Rect(p, box, enabled ? DarkShadow : Shadow, enabled ? DarkShadow : Shadow);
        if (enabled && !held) Rect(p, RectangleF.Inflate(box, -1, -1), Grey1, Window);
        if (value)
        {
            uint ink = enabled ? DarkShadow : Shadow;
            for (int i = 0; i < 10; i++)
            {
                p.Fill(new RectangleF(x + 1 + i, y + 1 + i, 1, 1), ink);
                p.Fill(new RectangleF(x + 10 - i, y + 1 + i, 1, 1), ink);
            }
        }
        Label(p, label, new RectangleF(x + 18, y - 1, 400, 14), enabled, TextAlignment.Leading, WindowText, false);
    }

    static readonly string[] RadioRing =
    {
        "....kkkk....", "..kk....kk..", ".k........k.", ".k........k.", "k..........k", "k..........k",
        "k..........k", "k..........k", ".k........k.", ".k........k.", "..kk....kk..", "....kkkk....",
    };
    static readonly string[] RadioFill =
    {
        "............", "....kkkk....", "..kkkkkkkk..", "..kkkkkkkk..", ".kkkkkkkkkk.", ".kkkkkkkkkk.",
        ".kkkkkkkkkk.", ".kkkkkkkkkk.", "..kkkkkkkk..", "..kkkkkkkk..", "....kkkk....", "............",
    };
    static readonly string[] RadioDot = { ".kk.", "kkkk", "kkkk", ".kk." };

    public override void Radio(Painter p, float x, float y, string label, bool on, bool enabled, bool held, bool hover)
    {
        p.Pixels(RadioFill, x, y, held && enabled ? Grey3 : Window);
        p.Pixels(RadioRing, x, y, enabled ? DarkShadow : Shadow);
        if (on) p.Pixels(RadioDot, x + 4, y + 4, enabled ? DarkShadow : Shadow);
        Label(p, label, new RectangleF(x + 18, y - 2, 400, 15), enabled, TextAlignment.Leading, WindowText, false);
    }

    // Mac OS 8's primary group box: an engraved frame, its title in Chicago
    public override void GroupBox(Painter p, RectangleF r, string title)
    {
        float y = r.Y + 6;
        p.Stroke(new RectangleF(r.X + 0.5f, y + 0.5f, r.Width - 2, r.Bottom - y - 2), Shadow, 1);
        p.Stroke(new RectangleF(r.X + 1.5f, y + 1.5f, r.Width - 2, r.Bottom - y - 2), Hilight, 1);
        var (t, _) = Ui.Amp(title);
        if (string.IsNullOrEmpty(t)) return;
        t = Fit(p, t, r.Width - 20, Ui.F95, true);
        float tw = ChicagoWidth(p, t);
        p.Fill(new RectangleF(r.X + 6, r.Y, tw + 5, 13), Face);
        Say(p, t, new RectangleF(r.X + 8, r.Y, tw + 2, 13), WindowText, true, TextAlignment.Leading);
    }

    // ---- slider: a thin groove and the pointer thumb ----

    static readonly string[] ThumbEdge =
    {
        ".kkkkkkkkk.", "k.........k", "k.........k", "k.........k", "k.........k", "k.........k", "k.........k", "k.........k",
        "k.........k", ".k.......k.", "..k.....k..", "...k...k...", "....k.k....", ".....k.....",
    };
    static readonly string[] ThumbFill =
    {
        "...........", ".kkkkkkkkk.", ".kkkkkkkkk.", ".kkkkkkkkk.", ".kkkkkkkkk.", ".kkkkkkkkk.", ".kkkkkkkkk.", ".kkkkkkkkk.",
        ".kkkkkkkkk.", "..kkkkkkk..", "...kkkkk...", "....kkk....", ".....k.....", "...........",
    };
    static readonly string[] ThumbLight =
    {
        "...........", ".kkkkkkkk..", ".k.........", ".k.........", ".k.........", ".k.........", ".k.........", ".k.........",
        ".k.........", "..k........", "...k.......", "....k......", "...........", "...........",
    };
    static readonly string[] ThumbDark =
    {
        "...........", "...........", ".........k.", ".........k.", ".........k.", ".........k.", ".........k.", ".........k.",
        "........kk.", ".......k...", "......k....", ".....k.....", "...........", "...........",
    };

    public override void Trackbar(Painter p, RectangleF r, float t, bool enabled, int ticks, bool hover, bool held)
    {
        var ch = new RectangleF(r.X + 6, r.Y + 8, r.Width - 12, 4);
        var groove = new RectangleF(ch.X - 1, ch.Y, ch.Width + 2, 5);
        FillRR(p, groove, 2, enabled ? Window : Light);
        StrokeRR(p, groove, 2, enabled ? Grey5 : Shadow, enabled ? Grey5 : Shadow);
        p.Fill(new RectangleF(groove.X + 2, groove.Y + 1, groove.Width - 4, 1), Grey1);
        for (int i = 0; i < ticks; i++)
        {
            float tx = MathF.Round(ch.X + (ch.Width - 1) * i / (ticks - 1));
            p.Fill(new RectangleF(tx, r.Y + 18, 1, i == 0 || i == ticks - 1 ? 4 : 3), enabled ? Grey5 : Shadow);
        }
        float x = MathF.Round(ch.X + ch.Width * t) - 5, y = r.Y + 2;
        p.Pixels(ThumbFill, x, y, held ? Grey2 : Face);
        if (!held) { p.Pixels(ThumbLight, x, y, Hilight); p.Pixels(ThumbDark, x, y, Shadow); }
        p.Pixels(ThumbEdge, x, y, enabled ? DarkShadow : Shadow);
        if (enabled)
            for (int i = 0; i < 3; i++) p.Fill(new RectangleF(x + 3, y + 3 + i * 2, 5, 1), held ? Grey5 : Grey3);    // the grip
    }

    // ---- progress: Platinum's bar ----

    public override void Progress(Painter p, RectangleF r, float frac, uint block)
    {
        bool warn = block is 0xFF800000 or 0xFFCC0000;
        Rect(p, r, Grey7, Grey7);
        var inner = RectangleF.Inflate(r, -1, -1);
        p.Fill(inner, Light);
        p.Fill(new RectangleF(inner.X, inner.Y, inner.Width, 1), Grey2);
        float fw = MathF.Round(inner.Width * Math.Clamp(frac, 0, 1));
        if (fw < 2 && frac >= 0.01f) fw = 2;
        if (fw <= 0) return;
        var f = new RectangleF(inner.X, inner.Y, fw, inner.Height);
        uint top = warn ? 0xFFEE6666 : 0xFF7777CC, mid = warn ? 0xFFCC0000 : Highlight, bot = warn ? 0xFF880000 : 0xFF222266;
        p.Fill(f, mid);
        p.Fill(new RectangleF(f.X, f.Y, f.Width, 1), top);
        if (f.Height > 4) p.Fill(new RectangleF(f.X, f.Y + 1, f.Width, 1), Theme.Mix(top, mid, 0.5f));
        p.Fill(new RectangleF(f.X, f.Bottom - 1, f.Width, 1), bot);
        if (fw < inner.Width) p.Fill(new RectangleF(f.Right, f.Y, 1, f.Height), Grey6);
    }

    // ---- tabs: rounded tops, the front one the pane's colour and joined to it ----

    public override void Tab(Painter p, RectangleF r, string label, bool selected, bool hover)
    {
        int x = (int)r.X, y = (int)r.Y, w = (int)r.Width, h = (int)r.Height;
        var (t, _) = Ui.Amp(label);
        if (selected)
        {
            // joined to the pane under it: its outline's top row and light row there go
            p.Fill(new RectangleF(x + 1, y + 1, w - 2, h - 1), Face);
            TabOutline(p, x, y, w, h - 2, Grey6);
            p.Fill(new RectangleF(x + 2, y + 2, w - 5, 1), Hilight);
            p.Fill(new RectangleF(x + 1, y + 3, 1, h - 5), Hilight);
            p.Fill(new RectangleF(x + w - 2, y + 3, 1, h - 5), Grey3);
            p.Fill(new RectangleF(x, y + h - 2, 1, 1), Grey6);
            p.Fill(new RectangleF(x + w - 1, y + h - 2, 1, 1), Grey6);
            p.Fill(new RectangleF(x + 1, y + h - 2, 1, 1), Hilight);
            bool chicago = ChicagoWidth(p, t) <= w - 6;
            Say(p, t, new RectangleF(x, y + 3, w, 14), WindowText, chicago);
        }
        else
        {
            p.Fill(new RectangleF(x + 1, y + 1, w - 2, h - 1), Grey1);
            TabOutline(p, x, y, w, h, Grey6);
            p.Fill(new RectangleF(x + 2, y + 2, w - 5, 1), Light);
            p.Fill(new RectangleF(x + 1, y + 3, 1, h - 3), Light);
            p.Fill(new RectangleF(x + w - 2, y + 3, 1, h - 3), Shadow);
            Say(p, t, new RectangleF(x, y + 3, w, 14), WindowText, false);
        }
    }

    // left, top (rounded) and right edges, no bottom
    static void TabOutline(Painter p, int x, int y, int w, int h, uint c)
    {
        p.Fill(new RectangleF(x + 3, y, w - 6, 1), c);
        p.Fill(new RectangleF(x + 1, y + 1, 2, 1), c);
        p.Fill(new RectangleF(x + w - 3, y + 1, 2, 1), c);
        p.Fill(new RectangleF(x + 1, y + 2, 1, 1), c);
        p.Fill(new RectangleF(x + w - 2, y + 2, 1, 1), c);
        p.Fill(new RectangleF(x, y + 3, 1, h - 3), c);
        p.Fill(new RectangleF(x + w - 1, y + 3, 1, h - 3), c);
    }

    // ---- lists, scroll bars, fields ----

    public override void ScrollBar(Painter p, RectangleF bar, RectangleF up, RectangleF down, RectangleF thumb, bool upHeld, bool downHeld)
    {
        p.Fill(bar, Grey2);
        p.Fill(new RectangleF(bar.X, bar.Y, 1, bar.Height), Grey7);
        p.Fill(new RectangleF(bar.X + 1, bar.Y, 1, bar.Height), Shadow);
        Arrow(p, up, UpGlyph, upHeld);
        Arrow(p, down, DownGlyph, downHeld);
        var th = new RectangleF(thumb.X + 1, thumb.Y, thumb.Width - 1, thumb.Height);
        p.Fill(th, Face);
        Rect(p, th, Grey6, Grey6);
        Rect(p, RectangleF.Inflate(th, -1, -1), Hilight, Shadow);
        if (th.Height >= 14)
        {
            float gy = MathF.Floor(th.Y + th.Height / 2) - 3;
            for (int i = 0; i < 4; i++)
            {
                p.Fill(new RectangleF(th.X + 4, gy + i * 2, th.Width - 8, 1), Grey5);
                p.Fill(new RectangleF(th.X + 5, gy + i * 2 + 1, th.Width - 8, 1), Hilight);
            }
        }
    }

    void Arrow(Painter p, RectangleF r, string[] glyph, bool held)
    {
        var b = new RectangleF(r.X + 1, r.Y, r.Width - 1, r.Height);
        p.Fill(b, held ? Grey3 : Face);
        Rect(p, b, Grey6, Grey6);
        if (!held) Rect(p, RectangleF.Inflate(b, -1, -1), Hilight, Shadow);
        p.Pixels(glyph, b.X + 4, b.Y + 6, held ? Hilight : DarkShadow);
    }

    public override void EditBox(Painter p, RectangleF r, string text, bool enabled)
    {
        p.Fill(r, enabled ? Window : Light);
        Bevel(p, r, Painter.Edge.Sunken);
        Say(p, text, new RectangleF(r.X + 4, r.Y + 2, r.Width - 6, r.Height - 4), enabled ? WindowText : GrayText, false, TextAlignment.Leading);
    }

    // the popup menu button: a Platinum button, the double arrow on its right
    static readonly string[] Updown = { "..k..", ".kkk.", "kkkkk", ".....", ".....", "kkkkk", ".kkk.", "..k.." };

    public override void ComboBox(Painter p, RectangleF r, string text, bool enabled, bool down, bool open, bool hover)
    {
        var b = ButtonBody(p, r, !enabled ? Look.Dim : down || open ? Look.Down : Look.Up, 3);
        float sx = r.Right - 18;
        p.Fill(new RectangleF(sx, b.Y + 1, 1, b.Height - 2), down || open ? Grey5 : Shadow);
        p.Fill(new RectangleF(sx + 1, b.Y + 1, 1, b.Height - 2), down || open ? Grey2 : Hilight);
        p.Pixels(Updown, sx + 6, MathF.Floor(r.Y + (r.Height - 8) / 2), enabled ? DarkShadow : Shadow);
        var tr = new RectangleF(r.X + 7, r.Y, sx - r.X - 10, r.Height);
        bool chicago = ChicagoWidth(p, text ?? "") <= tr.Width;
        Say(p, chicago ? text : Fit(p, text, tr.Width), tr, enabled ? WindowText : GrayText, chicago, TextAlignment.Leading);
    }

    // the menu that pops up: platinum, a black edge and a drop shadow, the row under the pointer in the highlight colour
    public override void DropList(Painter p, RectangleF r, string[] items, int hot, float rowH)
    {
        p.Fill(new RectangleF(r.Right, r.Y + 2, 1, r.Height), Grey6);
        p.Fill(new RectangleF(r.X + 2, r.Bottom, r.Width - 1, 1), Grey6);
        p.Fill(r, Face);
        Rect(p, r, DarkShadow, DarkShadow);
        Rect(p, RectangleF.Inflate(r, -1, -1), Hilight, Shadow);
        bool chicago = true;
        foreach (var it in items) if (ChicagoWidth(p, it ?? "") > r.Width - 16) { chicago = false; break; }
        for (int i = 0; i < items.Length; i++)
        {
            var row = new RectangleF(r.X + 1, r.Y + 1 + i * rowH, r.Width - 2, rowH);
            bool on = i == hot;
            if (on) p.Fill(row, Highlight);
            string s = chicago ? items[i] : Fit(p, items[i], row.Width - 14);
            Say(p, s, new RectangleF(row.X + 9, row.Y, row.Width - 12, rowH), on ? HighlightText : WindowText, chicago, TextAlignment.Leading);
        }
    }

    // a placard: the Finder's little raised field
    public override void StatusPane(Painter p, RectangleF r, string text, uint dot)
    {
        Rect(p, r, Shadow, Shadow);
        Rect(p, RectangleF.Inflate(r, -1, -1), Hilight, Grey1);
        float x = r.X + 4;
        if (dot != 0) { p.Fill(new RectangleF(x, r.Y + (r.Height - 6) / 2, 6, 6), dot); x += 10; }
        Say(p, text, new RectangleF(x, r.Y, r.Right - x - 2, r.Height), WindowText, false, TextAlignment.Leading);
    }

    // a menu bar's title: Chicago, no underline
    public override void MenuItem(Painter p, float x, float y, string text) =>
        Say(p, text, new RectangleF(x, y, ChicagoWidth(p, text) + 2, 15), WindowText, true, TextAlignment.Leading);

    public override bool Icon(Painter p, W95Icons.Id id, float x, float y, int scale) => DrawMacIcon(p, id, x, y, scale);
}
