using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// Windows 7 Aero: a translucent glass frame and title bar with a glossy highlight, Segoe UI 9 pt, soft glossy
// blue-white buttons, Explorer-style settings (a breadcrumb bar and a command bar), the 7-style progress bars and sliders.
// Drawn at the real pixel size of 2009 (96 dpi): Win7's theme metrics - 3 px rounded push buttons with the two-tone gloss and
// the blue hover, 13 x 13 check boxes with the gradient well, the green progress bar, the pointed slider thumb, tabs that merge
// into a white page, the #3399FF selection. Text is grey-scale smoothed (Painter: SmoothText); shapes stay on whole pixels,
// their round corners smoothed.
public sealed partial class AeroSkin : Win95Skin
{
    public override RetroStyle Style => RetroStyle.Aero;
    public override string Id => "aero";
    public override string Tag => "aero";
    public override string Name => "Windows 7";

    // ---- the palette: Windows 7's system colours ----
    public override uint Face => 0xFFF0F0F0;
    public override uint Light => 0xFFE3E3E3;
    public override uint Hilight => 0xFFFFFFFF;
    public override uint Shadow => 0xFFA0A0A0;
    public override uint DarkShadow => 0xFF696969;
    public override uint Window => 0xFFFFFFFF;
    public override uint WindowText => 0xFF000000;
    public override uint ButtonText => 0xFF000000;
    public override uint GrayText => 0xFF6D6D6D;
    public override uint DimText => 0xFF4C4C4C;
    public override uint Highlight => 0xFF3399FF;
    public override uint HighlightText => 0xFFFFFFFF;
    public override uint ActiveCaption => 0xFF99B4D1;
    public override uint CaptionText => 0xFF000000;
    public override uint InactiveCaption => 0xFFBFCDDB;
    public override uint InactiveCaptionText => 0xFF434E54;
    public override uint Desktop => 0xFF1F63B5;           // the "Harmony" wallpaper's blue (FillDesktop draws its light)
    public override uint Laser => 0xFF3399FF;

    // ---- Windows 7's theme colours (aero.msstyles at 96 dpi) ----
    public const uint BtnBorder = 0xFF707070, BtnHotBorder = 0xFF3C7FB1, BtnDownBorder = 0xFF2C628B, BtnOffBorder = 0xFFADB2B5;
    public const uint FieldTop = 0xFFABADB3, FieldSide = 0xFFE2E3EA, FieldBottom = 0xFFE3E9EF, FieldHot = 0xFF5794BF;
    public const uint GroupLine = 0xFFD5DFE5, TabLine = 0xFF898C95, ListLine = 0xFF828790;
    public const uint LinkBlue = 0xFF0066CC, HeadBlue = 0xFF1E3287, CpBlue = 0xFF003399;
    public const uint OffText = 0xFF838383;
    public const uint GlassTop = 0xFFC9DDF3, GlassMid = 0xFFB4CFEC, GlassBottom = 0xFFA7C5E8, GlassEdge = 0xFF4F6F95;

    // 1995's literal colours in the shared screens, the way Windows 7 painted the same things
    public override uint MapColor(uint c) => c switch
    {
        0xFF000080 => 0xFF3399FF,        // navy: the selection / the "today" of a calendar
        0xFF008080 => 0xFF1F63B5,        // the teal desktop
        0xFF004040 => 0xFF143F73,
        0xFF00FFFF => 0xFF9CCBF5,
        0xFF800000 => 0xFFC42B1C,        // maroon warning text
        0xFF806000 => 0xFF9A6A00,
        0xFF808000 => 0xFFE0A100,        // a starred person's star
        _ => c,
    };

    // ---- on glass: the custom face's tiles are blue glass, their text white (the shared 95 widget bodies draw black) ----
    public bool Glass;
    public override uint TextColor(uint c)
    {
        if (!Glass) return c;
        return (c & 0xFFFFFF) switch
        {
            0x000000 => 0xFFFFFFFF,
            0x808080 or 0x6D6D6D => 0xFFC6DCF4,
            0x404040 or 0x4C4C4C => 0xFFE2EEFB,
            0x800000 or 0xC42B1C => 0xFFFFA48F,
            0x806000 or 0x9A6A00 => 0xFFFFD866,
            0x008000 => 0xFFA6F0A6,
            0x000080 or 0x3399FF => 0xFFFFFFFF,
            _ => c,
        };
    }

    // ---- type: Segoe UI, grey-scale smoothed; the big clock figures in Segoe UI Light ----
    public override string Font => "Segoe UI";
    public const string LightFont = "Segoe UI Light";
    public override void MapFont(ref string family, ref float size, ref FontWeight weight)
    {
        if (family == Font && size >= 26 && weight == FontWeight.Normal) family = LightFont;
    }
    public override PixelFont PixelFont(string family, float size, FontWeight weight) => null;
    public override bool SmoothText => true;

    public override float DropRowH => 15;

    // ---- helpers ----

    static uint A(uint c, float a) => Theme.WithAlpha(c, a);

    // a glossy two-part fill: top half a -> b, bottom half c -> d (Windows 7's buttons, tabs, the glass)
    public static void Gloss(Painter p, RectangleF r, uint a, uint b, uint c, uint d)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        float h = MathF.Round(r.Height * 0.5f);
        p.GradientRaw(new RectangleF(r.X, r.Y, r.Width, h), a, b);
        p.GradientRaw(new RectangleF(r.X, r.Y + h, r.Width, r.Height - h), c, d);
    }

    // a rounded box: its border, a light inner line, a two-part gloss inside
    public static void RoundBox(Painter p, RectangleF r, float rad, uint border, uint inner, uint a, uint b, uint c, uint d)
    {
        if (r.Width < 3 || r.Height < 3) { p.Fill(r, border); return; }
        p.Smooth(true);
        p.FillRound(r, border, rad);
        p.FillRound(RectangleF.Inflate(r, -1, -1), inner, MathF.Max(0, rad - 1));
        p.Smooth(false);
        Gloss(p, RectangleF.Inflate(r, -2, -2), a, b, c, d);
    }

    static RectangleF Grow(RectangleF r, float dy) => new(r.X, r.Y - dy, r.Width, r.Height + 2 * dy);

    // ---- text ----

    public override void Text(Painter p, string s, RectangleF r, uint c, bool bold, TextAlignment al, bool enabled, bool wrap, float size)
    {
        if (string.IsNullOrEmpty(s)) return;
        var w = bold ? FontWeight.Bold : FontWeight.Normal;
        uint col = enabled ? TextColor(c) : Glass ? 0xFFA9C4E4 : OffText;
        if (wrap) { Wrapped(p, s, r, col, w, al, size); return; }
        // Segoe UI's line is taller than MS Sans Serif's 13 px: the box grows so descenders are not cut (the text stays centred)
        p.Text(s, Grow(r, 3), col, size, w, al, ParagraphAlignment.Center, Font);
    }

    public int LineH(float size) => (int)MathF.Round(size * 1.28f);

    // word-wrapped text at Segoe UI's line pitch (as many lines as fit; the box grows a little for the last one's descenders)
    void Wrapped(Painter p, string s, RectangleF r, uint col, FontWeight w, TextAlignment al, float size)
    {
        int lh = LineH(size);
        float y = r.Y;
        foreach (var line in WrapLines(p, s, r.Width, w == FontWeight.Bold, size))
        {
            if (y + lh > r.Bottom + 4) break;
            p.Text(line, new RectangleF(r.X, y - 2, r.Width, lh + 4), col, size, w, al, ParagraphAlignment.Center, Font);
            y += lh;
        }
    }

    public List<string> WrapLines(Painter p, string s, float width, bool bold, float size)
    {
        s = Lang.T(s);                     // cut in the language picked
        var lines = new List<string>();
        foreach (var para in s.Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(' '))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && TextWidth(p, next, bold, size) > width) { lines.Add(line); line = word; }
                else line = next;
            }
            lines.Add(line);
        }
        return lines;
    }

    // Windows 7 hides the access keys until Alt is held: the label without its underline
    public override void Label(Painter p, string s, RectangleF r, bool enabled, TextAlignment al, uint c, bool bold)
    {
        var (t, _) = Ui.Amp(s);
        if (string.IsNullOrEmpty(t)) return;
        Text(p, t, r, c, bold, al, enabled, false, Ui.F95);
    }

    // greyed-out text: flat grey, no etching
    public override void DisabledText(Painter p, string s, RectangleF r, float size, FontWeight w, TextAlignment h, ParagraphAlignment v) =>
        p.Text(s, v == ParagraphAlignment.Center ? Grow(r, 3) : r, Glass ? 0xFFA9C4E4 : OffText, size, w, h, v, Font);

    // ---- frames ----

    public override void Bevel(Painter p, RectangleF r, Painter.Edge e)
    {
        switch (e)
        {
            case Painter.Edge.Raised:
                // a details list's column header (the shared screens draw them as 16 px raised cells): 7's flat white header
                if (r.Height == 16 && r.Width >= 24) { HeaderCell(p, r); break; }
                Face7(p, r, false, false, false, false, false);
                break;
            case Painter.Edge.Pressed: Face7(p, r, false, true, false, false, false); break;

            case Painter.Edge.Sunken: Field(p, r, false); break;
            case Painter.Edge.Window:
                GlassFrame(p, r);
                if (TopLevel(p, r)) RoundCorners(p, r);
                break;
            case Painter.Edge.Page: Outline(p, r, TabLine); break;
            default:
                p.Smooth(true);
                p.StrokeRound(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), GroupLine, 1, 3);
                p.Smooth(false);
                break;
        }
    }

    // Explorer's column header: white into a hair of grey, a light line under it, a separator at its right
    static void HeaderCell(Painter p, RectangleF r)
    {
        p.GradientRaw(r, 0xFFFFFFFF, 0xFFF7F8FA, 0xFFF1F2F4);
        p.Fill(new RectangleF(r.X, r.Bottom - 1, r.Width, 1), 0xFFD5D5D5);
        p.Fill(new RectangleF(r.Right - 1, r.Y, 1, r.Height - 1), 0xFFE3E8EE);
    }

    static void Outline(Painter p, RectangleF r, uint c)
    {
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 1), c);
        p.Fill(new RectangleF(r.X, r.Bottom - 1, r.Width, 1), c);
        p.Fill(new RectangleF(r.X, r.Y + 1, 1, r.Height - 2), c);
        p.Fill(new RectangleF(r.Right - 1, r.Y + 1, 1, r.Height - 2), c);
    }

    // a text box's border: darker on top, light on the sides and bottom (hot: blue)
    public static void Field(Painter p, RectangleF r, bool hot)
    {
        if (hot)
        {
            p.Fill(new RectangleF(r.X, r.Y, r.Width, 1), 0xFF3D7BAD);
            p.Fill(new RectangleF(r.X, r.Y + 1, 1, r.Height - 1), 0xFFB5CFE7);
            p.Fill(new RectangleF(r.Right - 1, r.Y + 1, 1, r.Height - 1), 0xFFB5CFE7);
            p.Fill(new RectangleF(r.X + 1, r.Bottom - 1, r.Width - 2, 1), 0xFFB7D9ED);
            return;
        }
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 1), FieldTop);
        p.Fill(new RectangleF(r.X, r.Y + 1, 1, r.Height - 1), FieldSide);
        p.Fill(new RectangleF(r.Right - 1, r.Y + 1, 1, r.Height - 1), FieldSide);
        p.Fill(new RectangleF(r.X + 1, r.Bottom - 1, r.Width - 2, 1), FieldBottom);
    }

    // the glass frame around a window (3 px here): a dark outer line, glass, the light line round the client area
    public static void GlassFrame(Painter p, RectangleF r)
    {
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 3), GlassTop);
        p.Fill(new RectangleF(r.X, r.Bottom - 3, r.Width, 3), GlassBottom);
        p.GradientRaw(new RectangleF(r.X, r.Y, 3, r.Height), GlassTop, GlassBottom);
        p.GradientRaw(new RectangleF(r.Right - 3, r.Y, 3, r.Height), GlassTop, GlassBottom);
        Outline(p, r, GlassEdge);
        Outline(p, RectangleF.Inflate(r, -1, -1), A(0xFFFFFFFF, 0.55f));
        Outline(p, RectangleF.Inflate(r, -2, -2), 0xFF8BA4C3);
    }

    // a window that is its panel's whole canvas (the Room and Hexpad panels: drawn on a clear canvas from its top left corner)
    static bool TopLevel(Painter p, RectangleF r) => p.PixelMode && r.X == 0 && r.Y == 0 && r.Width >= 40 && r.Height >= 40;

    // its corners rounded as 7's are: cut out of the square the panel filled (to clear), the frame drawn round there
    void RoundCorners(Painter p, RectangleF r)
    {
        const float R = 6;
        var dc = p.Gpu.D2D;
        for (int i = 0; i < 4; i++)
        {
            var c = new RectangleF(i % 2 == 0 ? r.X : r.Right - R, i < 2 ? r.Y : r.Bottom - R, R, R);
            dc.PrimitiveBlend = Vortice.Direct2D1.PrimitiveBlend.Copy;
            p.Fill(c, 0x00000000);
            dc.PrimitiveBlend = Vortice.Direct2D1.PrimitiveBlend.SourceOver;
            p.PushClip(c);
            p.Smooth(true);
            p.FillRound(r, GlassEdge, R);
            p.FillRound(RectangleF.Inflate(r, -1, -1), Theme.Mix(i < 2 ? GlassTop : GlassBottom, 0xFFFFFFFF, 0.4f), R - 1);
            p.FillRound(RectangleF.Inflate(r, -2, -2), i < 2 ? GlassTop : GlassBottom, R - 2);
            p.Smooth(false);
            Outline(p, RectangleF.Inflate(r, -2, -2), 0xFF8BA4C3);
            p.Fill(RectangleF.Inflate(r, -3, -3), Face);
            p.PopClip();
        }
    }

    // glass: the frame's colour, a sheen over its top half and the faint diagonal streaks of 7's frame
    public static void Glass7(Painter p, RectangleF r, bool active = true)
    {
        if (active) p.GradientRaw(r, GlassTop, GlassMid, GlassBottom);
        else p.GradientRaw(r, 0xFFDCE6F1, 0xFFD3DEEB, 0xFFCCD8E6);
        p.Fill(new RectangleF(r.X, r.Y, r.Width, MathF.Round(r.Height * 0.45f)), A(0xFFFFFFFF, 0.22f));
        p.PushClip(r);
        p.Smooth(true);
        var s = _streak ??= new Vector2[4];     // (one per thread, reused: two new arrays a streak a frame were the light card's garbage)
        for (float sx = r.X + 40; sx < r.Right + r.Height; sx += 150)
        {
            Streak(s, sx, sx + 22, r);
            p.Polygon(s, 4, A(0xFFFFFFFF, 0.13f));
            Streak(s, sx + 30, sx + 36, r);
            p.Polygon(s, 4, A(0xFFFFFFFF, 0.10f));
        }
        p.Smooth(false);
        p.PopClip();
    }

    [ThreadStatic] static Vector2[] _streak, _thumbOuter, _thumbInner;

    static void Streak(Vector2[] s, float x0, float x1, RectangleF r)
    {
        float lean = r.Height * 0.7f;
        s[0] = new Vector2(x0, r.Y); s[1] = new Vector2(x1, r.Y); s[2] = new Vector2(x1 - lean, r.Bottom); s[3] = new Vector2(x0 - lean, r.Bottom);
    }

    // a line between sections: Windows 7's light separator
    public override void EtchedLine(Painter p, float x1, float x2, float y) => p.Fill(new RectangleF(x1, y, x2 - x1, 1), GroupLine);

    // the "Harmony" wallpaper: deep blue, lighter towards the top left, a soft glow
    public override void FillDesktop(Painter p, RectangleF r) => Harmony(p, r);

    public static void Harmony(Painter p, RectangleF r)
    {
        p.GradientRaw(r, 0xFF2F7ED3, 0xFF1A5AAC, 0xFF0E3A7C);
        p.PushClip(r);
        p.Smooth(true);
        float cx = r.X + r.Width * 0.3f, cy = r.Y + r.Height * 0.25f, rad = MathF.Max(r.Width, r.Height) * 0.55f;
        for (int i = 0; i < 6; i++) p.Circle(cx, cy, rad * (1 - i * 0.15f), A(0xFF7FB8F0, 0.06f));
        p.Smooth(false);
        p.PopClip();
    }

    // a title bar: glass from the frame's top edge down to the client area, the title in black with the white glow of 7
    public override void Caption(Painter p, RectangleF r, string title, W95Icons.Id? icon, bool active)
    {
        var g = new RectangleF(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 3);
        bool top = TopLevel(p, new RectangleF(r.X - 3, r.Y - 3, r.Width + 6, 100));
        if (top) p.PushRoundedClip(new RectangleF(g.X, g.Y, g.Width, g.Height + 20), 5);
        Glass7(p, g, active);
        if (top) p.PopRoundedClip();
        p.Fill(new RectangleF(r.X - 1, r.Bottom, r.Width + 2, 1), 0xFF8BA4C3);
        float tx = r.X + 4;
        if (icon is { } ic) { W95Icons.Draw(p, ic, r.X + 2, r.Y + (r.Height - 16) / 2); tx = r.X + 22; }
        GlowText(p, title, new RectangleF(tx, r.Y, r.Width - (tx - r.X) - 40, r.Height), active ? CaptionText : InactiveCaptionText, 12);
    }

    // black text with the soft white glow 7 puts behind a title on glass
    public void GlowText(Painter p, string s, RectangleF r, uint c, float size, TextAlignment al = TextAlignment.Leading, bool bold = false)
    {
        if (string.IsNullOrEmpty(s)) return;
        var w = bold ? FontWeight.Bold : FontWeight.Normal;
        var g = Grow(r, 3);
        uint halo = A(0xFFFFFFFF, 0.30f);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (dx != 0 || dy != 0) p.Text(s, new RectangleF(g.X + dx, g.Y + dy, g.Width, g.Height), halo, size, w, al, ParagraphAlignment.Center, Font);
        p.Text(s, g, c, size, w, al, ParagraphAlignment.Center, Font);
    }

    // a caption box: 7's small glass buttons, the close box red
    public override void CaptionButton(Painter p, RectangleF r, CaptionKind kind, bool enabled, bool down, bool hover) =>
        CaptionBox(p, r, kind, enabled, down, hover);

    public static void CaptionBox(Painter p, RectangleF r, CaptionKind kind, bool enabled, bool down, bool hover)
    {
        bool close = kind == CaptionKind.Close;
        // the glow comes up quickly and dies away slowly, as 7's title bar buttons; the close box's red spills round it
        float h = XpAeroFx.Fade(XpAeroFx.Caption, r, hover && enabled && !down, GlowIn, GlowOut);
        if (close && h > 0 && !down) CloseGlow(p, r, h);
        if (down)
        {
            if (close) RoundBox(p, r, 3, 0xFF5A1F14, 0xFFD87A66, 0xFFC7563E, 0xFFAE3A22, 0xFF8E200E, 0xFFB7442A);
            else RoundBox(p, r, 3, 0xFF3E5672, 0xFFB7CDE6, 0xFF9DB6D4, 0xFF87A3C6, 0xFF5E80AA, 0xFF7A9AC2);
        }
        else
        {
            if (h < 1)
            {
                if (close) RoundBox(p, r, 3, 0xFF6B2A1E, 0xFFEBB2A4, 0xFFE1A08E, 0xFFD06D56, 0xFFC0381C, 0xFFD45A3D);
                else RoundBox(p, r, 3, 0xFF5A7496, 0xFFEAF1FA, 0xFFDCE7F4, 0xFFCBD9EC, 0xFFAFC4E1, 0xFFC5D6EC);
            }
            if (h > 0)
            {
                if (h < 1) p.PushFade(h);
                if (close) RoundBox(p, r, 3, 0xFF6B2A1E, 0xFFFFC8B8, 0xFFF4A28D, 0xFFE8735A, 0xFFD2391A, 0xFFF0784F);
                else RoundBox(p, r, 3, 0xFF3E5672, 0xFFF4F9FF, 0xFFDDEBFA, 0xFFC9DDF5, 0xFF8FB8E8, 0xFFB9D6F5);
                if (h < 1) p.PopFade();
            }
        }
        uint ink = enabled ? 0xFFFFFFFF : 0xFFE0E8F0, edge = close ? 0xFF6B2A1E : 0xFF35506F;
        float cx = MathF.Floor(r.X + r.Width / 2), cy = MathF.Floor(r.Y + r.Height / 2);
        string[] g = kind switch { CaptionKind.Close => Close7, CaptionKind.Min => Min7, CaptionKind.Max => Max7, _ => Help7 };
        float gx = cx - MathF.Floor(g[0].Length / 2f), gy = kind == CaptionKind.Min ? cy + 2 : cy - MathF.Floor(g.Length / 2f);
        // the glyph white with a dark rim, as 7 drew them
        p.Pixels(g, gx - 1, gy, A(edge, 0.55f));
        p.Pixels(g, gx + 1, gy, A(edge, 0.55f));
        p.Pixels(g, gx, gy - 1, A(edge, 0.55f));
        p.Pixels(g, gx, gy + 1, A(edge, 0.75f));
        p.Pixels(g, gx, gy, ink);
    }

    // the caption boxes' glyphs, made once (a new array of them in every frame was an opened 7 window's only garbage)
    static readonly string[] Close7 = { "kk...kk", ".kk.kk.", "..kkk..", ".kk.kk.", "kk...kk" }, Min7 = { "kkkkkk", "kkkkkk" },
        Max7 = { "kkkkkkkk", "kkkkkkkk", "k......k", "k......k", "k......k", "kkkkkkkk" }, Help7 = { ".kkk.", "k...k", "...k.", "..k..", ".....", "..k.." };

    // the red glow round a close box under the pointer (to its sides and below: the window's top edge stays clean)
    static void CloseGlow(Painter p, RectangleF r, float h)
    {
        p.Smooth(true);
        p.FillRound(new RectangleF(r.X - 3, r.Y + 1, r.Width + 6, r.Height + 2), A(0xFFFF4A1E, 0.16f * h), 6);
        p.FillRound(new RectangleF(r.X - 1.5f, r.Y + 1, r.Width + 3, r.Height + 0.5f), A(0xFFFF6A3C, 0.22f * h), 4.5f);
        p.Smooth(false);
    }

    // a tile of the custom face: a gadget on the desktop - blue glass, rounded, its title small and white
    public override RectangleF WidgetFrame(Painter p, RectangleF r, string title)
    {
        Tile(p, r);
        var c = RectangleF.Inflate(r, -3, -3);
        if (title != null)
        {
            bool g = Glass;
            Glass = true;
            Text(p, Fit(p, title, c.Width - 8), new RectangleF(c.X + 4, c.Y + 1, c.Width - 6, 16), 0xFF000000, false, TextAlignment.Leading, true, false, Ui.F95);
            Glass = g;
            c = new RectangleF(c.X + 1, c.Y + 19, c.Width - 2, c.Height - 20);
        }
        return c;
    }

    public static void Tile(Painter p, RectangleF r, uint top = 0xFF4F97E2, uint bottom = 0xFF2A69BE)
    {
        // a gadget's edge: a dark line that lets the desktop through, a light rim inside it, then the glass
        p.Smooth(true);
        p.FillRound(r, A(0xFF03183A, 0.6f), 4);
        p.Smooth(false);
        var i = RectangleF.Inflate(r, -1, -1);
        p.GradientRaw(i, Theme.Mix(top, 0xFFFFFFFF, 0.45f), Theme.Mix(top, 0xFFFFFFFF, 0.15f), bottom, 3);
        p.GradientRaw(RectangleF.Inflate(i, -1, -1), top, Theme.Mix(top, bottom, 0.5f), bottom, 3);
        p.Smooth(true);
        p.FillRound(new RectangleF(i.X + 1, i.Y + 1, i.Width - 2, MathF.Round((i.Height - 2) * 0.42f)), A(0xFFFFFFFF, 0.09f), 2);
        p.Smooth(false);
    }

    // ---- controls ----

    // 7's push button: a 3 px rounded border, a white inner line, the two-part gloss; blue when the pointer is on it, deeper when
    // held; a default button's border blue with a light-blue inner glow; latched (a toggle that is in) stays in the hot blue
    public override RectangleF ButtonFace(Painter p, RectangleF r, bool isDefault, bool down, bool latched, bool hover) =>
        Face7(p, r, isDefault, down, latched, hover, true);

    // 7's hover glow: up in a moment, away slowly
    public const float GlowIn = 0.12f, GlowOut = 0.4f;

    // fx: the hover glow fades and a default button throbs (not where a panel draws only on changes: the keyboard, a header)
    RectangleF Face7(Painter p, RectangleF r, bool isDefault, bool down, bool latched, bool hover, bool fx)
    {
        float h = !fx ? (hover && !down ? 1 : 0) : XpAeroFx.Fade(XpAeroFx.Hot, r, hover && !down, GlowIn, GlowOut);
        if (down) { RoundBox(p, r, 3, BtnDownBorder, 0xFF9EB9D0, 0xFFE5F4FC, 0xFFC4E5F6, 0xFF98D1EF, 0xFF68B3DB); return RectangleF.Inflate(r, -1, -1); }
        if (h >= 1) { HotBox(p, r); return RectangleF.Inflate(r, -1, -1); }
        if (latched) RoundBox(p, r, 3, BtnHotBorder, 0xFFE9F5FD, 0xFFDCEFFC, 0xFFC9E8FA, 0xFFA9DBF6, 0xFF90CDF0);
        else if (isDefault)
        {
            RoundBox(p, r, 3, BtnHotBorder, 0xFF7FD6F8, 0xFFF2F2F2, 0xFFEBEBEB, 0xFFDDDDDD, 0xFFCFCFCF);
            if (fx && h <= 0) Throb(p, r);
        }
        else RoundBox(p, r, 3, BtnBorder, 0xFFFCFCFC, 0xFFF2F2F2, 0xFFEBEBEB, 0xFFDDDDDD, 0xFFCFCFCF);
        if (h > 0)
        {
            p.PushFade(h);
            HotBox(p, r);
            p.PopFade();
        }
        return RectangleF.Inflate(r, -1, -1);
    }

    static void HotBox(Painter p, RectangleF r) => RoundBox(p, r, 3, BtnHotBorder, 0xFFFCFEFF, 0xFFEAF6FD, 0xFFD9F0FC, 0xFFBEE6FD, 0xFFA7D9F5);

    // Vista's and 7's default button throbs: its face glows blue and back, slowly (a breath every 2.4 s), drawn 15 times a second
    // while it is on show (Motion Full only)
    public const double ThrobPeriod = 2.4;

    static void Throb(Painter p, RectangleF r)
    {
        if (!XpAeroFx.Loops) return;
        float a = 0.5f - 0.5f * MathF.Cos((float)(XpAeroFx.Now % ThrobPeriod / ThrobPeriod * 2 * Math.PI));
        XpAeroFx.WantTick(15);
        if (a < 0.02f) return;
        p.PushFade(0.85f * a);
        HotBox(p, r);
        p.PopFade();
    }

    // a disabled button's face: flat, nearly white, a light grey border (the watch's camera button while VRChat's camera is closed too)
    public void OffFace(Painter p, RectangleF r) => RoundBox(p, r, 3, BtnOffBorder, 0xFFFBFBFB, 0xFFF4F4F4, 0xFFF4F4F4, 0xFFF4F4F4, 0xFFF4F4F4);

    public override void PushButton(Painter p, RectangleF r, string label, W95Icons.Id? icon, bool enabled, bool isDefault, bool down,
        bool latched, bool hover)
    {
        bool g = Glass;
        Glass = false;
        RectangleF b;
        if (enabled) b = ButtonFace(p, r, isDefault, down, latched, hover);
        else { OffFace(p, r); b = RectangleF.Inflate(r, -1, -1); }
        if (icon is { } ic)
        {
            int n = W95Icons.Size(ic);
            float tw = string.IsNullOrEmpty(label) ? 0 : LabelWidth(p, label) + 4;
            float x = b.X + (b.Width - n - tw) / 2, y = b.Y + (b.Height - n) / 2;
            if (enabled) W95Icons.Draw(p, ic, MathF.Round(x), MathF.Round(y));
            else
            {
                p.PushFade(0.45f);
                W95Icons.Draw(p, ic, MathF.Round(x), MathF.Round(y));
                p.PopFade();
            }
            if (tw > 0) Label(p, label, new RectangleF(MathF.Round(x + n + 4), b.Y, tw, b.Height), enabled, TextAlignment.Leading, ButtonText, false);
        }
        else
            Label(p, label, b, enabled, TextAlignment.Center, ButtonText, false);
        Glass = g;
    }

    public override void GlyphButton(Painter p, RectangleF r, string[] glyph, bool enabled, bool down, bool hover)
    {
        bool g = Glass;
        Glass = false;
        RectangleF b;
        if (enabled) b = ButtonFace(p, r, false, down, false, hover);
        else { OffFace(p, r); b = RectangleF.Inflate(r, -1, -1); }
        float gw = glyph[0].Length, gh = glyph.Length;
        float gx = b.X + MathF.Floor((b.Width - gw) / 2), gy = b.Y + MathF.Floor((b.Height - gh) / 2);
        p.Pixels(glyph, gx, gy, enabled ? 0xFF1E2B3A : OffText);
        Glass = g;
    }

    public override RectangleF KeyCap(Painter p, RectangleF r, bool oneShot, bool down, bool latched, bool hover) =>
        Face7(p, r, oneShot, down, latched, hover, false);

    // 13 x 13: a grey border, a white ring, the grey well (blue under the pointer), a dark check
    static readonly string[] Check7 = { "......kk", ".....kkk", "k...kkk.", "kk.kkk..", "kkkkk...", ".kkk....", "..k....." };

    public override void CheckBox(Painter p, float x, float y, string label, bool value, bool enabled, bool held, bool hover)
    {
        var box = new RectangleF(x, y, 13, 13);
        Box7(p, box, enabled, held, hover, false);
        if (value) p.Pixels(Check7, x + 3, y + 3, enabled ? 0xFF21364F : 0xFF9AA3AE);
        Label(p, label, new RectangleF(x + 19, y - 1, 400, 15), enabled, TextAlignment.Leading, WindowText, false);
    }

    static void Box7(Painter p, RectangleF box, bool enabled, bool held, bool hover, bool round)
    {
        // the blue under the pointer fades up and away
        float h = XpAeroFx.Fade(XpAeroFx.Check, box, enabled && hover && !held, GlowIn, GlowOut);
        uint border = !enabled ? 0xFFBCBCBC : held ? 0xFF2C628B : h >= 1 ? 0xFF3C7FB1 : h <= 0 ? 0xFF8E8F8F : Theme.Mix(0xFF8E8F8F, 0xFF3C7FB1, h);
        var (a, b) = !enabled ? (0xFFE6E6E6, 0xFFF6F6F6) : held ? (0xFF7CB9E8, 0xFFD6EEFD) : h >= 1 ? (0xFFA7D7F7, 0xFFEAF6FD)
            : h <= 0 ? (0xFFD6D8DB, 0xFFFDFDFD) : (Theme.Mix(0xFFD6D8DB, 0xFFA7D7F7, h), Theme.Mix(0xFFFDFDFD, 0xFFEAF6FD, h));
        if (round)
        {
            float cx = box.X + 6.5f, cy = box.Y + 6.5f;
            p.Smooth(true);
            p.Circle(cx, cy, 6.5f, border);
            p.Circle(cx, cy, 5.5f, 0xFFF6F6F6);
            p.Circle(cx, cy, 4.5f, a);
            p.Circle(cx + 0.6f, cy + 0.6f, 3.4f, b);
            p.Smooth(false);
            return;
        }
        p.Fill(box, border);
        p.Fill(RectangleF.Inflate(box, -1, -1), 0xFFF6F6F6);
        // the well: 9 x 9, darker at its top left, light at its bottom right (pixel by pixel along the diagonal)
        var w = RectangleF.Inflate(box, -2, -2);
        int n = (int)w.Width;
        for (int d = 0; d <= 2 * (n - 1); d++)
        {
            uint c = Theme.Mix(a, b, d / (2f * (n - 1)));
            for (int i = Math.Max(0, d - n + 1); i <= Math.Min(d, n - 1); i++) p.Fill(new RectangleF(w.X + i, w.Y + d - i, 1, 1), c);
        }
    }

    public override void Radio(Painter p, float x, float y, string label, bool on, bool enabled, bool held, bool hover)
    {
        var box = new RectangleF(x, y, 13, 13);
        Box7(p, box, enabled, held, hover, true);
        if (on)
        {
            p.Smooth(true);
            p.Circle(x + 6.5f, y + 6.5f, 3, enabled ? 0xFF1B4E7A : 0xFF9AA3AE);
            p.Circle(x + 6f, y + 6f, 1.4f, enabled ? 0xFF6FB3E8 : 0xFFC9CED4);
            p.Smooth(false);
        }
        Label(p, label, new RectangleF(x + 18, y - 2, 400, 15), enabled, TextAlignment.Leading, WindowText, false);
    }

    // the settings' pages draw their groups as Explorer's group headers (blue text, a line to the right) - SettingsView.Aero sets it
    public bool HeaderGroups;
    public uint GroupBg = 0;      // what is behind a group's title (0: the face)

    public override void GroupBox(Painter p, RectangleF r, string title)
    {
        if (HeaderGroups)
        {
            float hw = string.IsNullOrEmpty(title) ? 0 : LabelWidth(p, title);
            if (hw > 0) Label(p, title, new RectangleF(r.X, r.Y - 1, hw + 4, 15), true, TextAlignment.Leading, HeadBlue, false);
            float lx = r.X + hw + (hw > 0 ? 6 : 0), ly = r.Y + 7;
            if (r.Right - 2 > lx) p.GradientRaw(new RectangleF(lx, ly, r.Right - 2 - lx, 1), 0xFFC9D6E8, 0xFFF4F7FB, 0, 0);
            return;
        }
        float y = r.Y + 6;
        p.Smooth(true);
        p.StrokeRound(new RectangleF(r.X + 0.5f, y + 0.5f, r.Width - 2, r.Bottom - y - 2), Glass ? A(0xFFFFFFFF, 0.45f) : GroupLine, 1, 3);
        p.Smooth(false);
        if (string.IsNullOrEmpty(title)) return;
        float tw = LabelWidth(p, title);
        if (!Glass) p.Fill(new RectangleF(r.X + 6, r.Y, tw + 4, 13), GroupBg != 0 ? GroupBg : Face);
        Label(p, title, new RectangleF(r.X + 8, r.Y, tw + 2, 13), true, TextAlignment.Leading, Glass ? 0xFF000000 : 0xFF1E395B, false);
    }

    // 7's slider: a thin grey track, the glossy thumb pointing down at the ticks (blue under the pointer)
    public override void Trackbar(Painter p, RectangleF r, float t, bool enabled, int ticks, bool hover, bool held)
    {
        var ch = new RectangleF(r.X + 6, r.Y + 8, r.Width - 12, 4);
        p.Fill(ch, Glass ? A(0xFF0B2E5C, 0.55f) : 0xFFE7EAEA);
        Outline(p, ch, Glass ? A(0xFFFFFFFF, 0.55f) : 0xFFB0B3B8);
        for (int i = 0; i < ticks; i++)
        {
            float tx = MathF.Round(ch.X + (ch.Width - 1) * i / (ticks - 1));
            p.Fill(new RectangleF(tx, r.Y + 24, 1, i == 0 || i == ticks - 1 ? 4 : 3), Glass ? A(0xFFFFFFFF, 0.7f) : 0xFF8C8C8C);
        }
        float x = MathF.Round(ch.X + ch.Width * t) - 5, y = r.Y + 1;
        // the thumb's blue under the pointer fades (in steps: a few gradient brushes)
        float h = XpAeroFx.Step(XpAeroFx.Fade(XpAeroFx.Thumb, r, enabled && hover && !held, GlowIn, GlowOut), 6);
        uint border = !enabled ? 0xFFB5B5B5 : held ? BtnDownBorder : Theme.Mix(0xFF707070, BtnHotBorder, h);
        var (a, b, c) = !enabled ? (0xFFF4F4F4, 0xFFF0F0F0, 0xFFEAEAEA) : held ? (0xFFE5F4FC, 0xFFC4E5F6, 0xFF8AC8EA)
            : h >= 1 ? (0xFFF2FAFE, 0xFFD9F0FC, 0xFFA9DBF6) : h <= 0 ? (0xFFF8F8F8, 0xFFE9E9E9, 0xFFD3D3D3)
            : (Theme.Mix(0xFFF8F8F8, 0xFFF2FAFE, h), Theme.Mix(0xFFE9E9E9, 0xFFD9F0FC, h), Theme.Mix(0xFFD3D3D3, 0xFFA9DBF6, h));
        // the outline (body 11 x 14, the point 5 rows), then the gloss inside
        // (into two arrays kept per thread: a new pair a thumb a frame was the light card's garbage at rest)
        var outer = _thumbOuter ??= new Vector2[7];
        outer[0] = new Vector2(x + 1, y); outer[1] = new Vector2(x + 10, y); outer[2] = new Vector2(x + 11, y + 1); outer[3] = new Vector2(x + 11, y + 14);
        outer[4] = new Vector2(x + 5.5f, y + 19.5f); outer[5] = new Vector2(x, y + 14); outer[6] = new Vector2(x, y + 1);
        var inner = _thumbInner ??= new Vector2[5];
        inner[0] = new Vector2(x + 1, y + 1); inner[1] = new Vector2(x + 10, y + 1); inner[2] = new Vector2(x + 10, y + 13.6f);
        inner[3] = new Vector2(x + 5.5f, y + 18.1f); inner[4] = new Vector2(x + 1, y + 13.6f);
        p.Smooth(true);
        p.Polygon(outer, border);               // (the kept-geometry one: it copies the points it keeps)
        p.Polygon(inner, c);
        p.Smooth(false);
        p.GradientRaw(new RectangleF(x + 1, y + 1, 9, 13), a, b, c);
        p.Fill(new RectangleF(x + 1, y + 1, 9, 1), 0xFFFFFFFF);
    }

    // the green bar (a warning one red) in a grey trough, the gloss on its top half
    public override void Progress(Painter p, RectangleF r, float frac, uint block)
    {
        bool bad = block == 0xFF800000;
        Bar7(p, r, frac, bad ? BarRed : BarGreen);
    }

    public static readonly uint[] BarGreen = { 0xFF9CEAA6, 0xFF4ED163, 0xFF1EB23A, 0xFF37C953, 0xFF1D8F31 };
    public static readonly uint[] BarRed = { 0xFFF4A79E, 0xFFE2554A, 0xFFC7261C, 0xFFDC3E33, 0xFF9E1C14 };
    public static readonly uint[] BarYellow = { 0xFFFBE7A2, 0xFFF4C842, 0xFFE0A800, 0xFFEFC020, 0xFFB08200 };
    public static readonly uint[] BarBlue = { 0xFFB5DBFA, 0xFF5BAEF0, 0xFF2B83D6, 0xFF4A9EEA, 0xFF1F65A8 };

    public static void Bar7(Painter p, RectangleF r, float frac, uint[] bar)
    {
        p.Smooth(true);
        p.FillRound(r, 0xFFB2B2B2, 2);
        p.Smooth(false);
        var i = RectangleF.Inflate(r, -1, -1);
        p.GradientRaw(i, 0xFFF3F3F3, 0xFFE6E6E6, 0xFFDCDCDC);
        float w = MathF.Round(i.Width * Math.Clamp(frac, 0, 1));
        if (w <= 0 && frac >= 0.01f) w = 1;
        if (w <= 0) return;
        var f = new RectangleF(i.X, i.Y, w, i.Height);
        Gloss(p, f, bar[0], bar[1], bar[2], bar[3]);
        if (w >= 3)
        {
            p.Fill(new RectangleF(f.Right - 1, f.Y, 1, f.Height), A(bar[4], 0.6f));
            p.Fill(new RectangleF(f.X, f.Y, 1, f.Height), A(bar[4], 0.35f));
        }
        if (w >= 8) Sheen(p, f);
    }

    // 7's progress bar sheen: a soft light sweeping through the filled part, left to right, every 2.6 s (Motion Full only), drawn
    // 24 times a second during its 1.3 s sweep and not at all between two
    public const double SheenPeriod = 2.6, SheenSweep = 1.3;

    static void Sheen(Painter p, RectangleF f)
    {
        if (!XpAeroFx.Loops) return;
        double ph = XpAeroFx.Now % SheenPeriod;
        if (ph >= SheenSweep) { XpAeroFx.Want(XpAeroFx.Now - ph + SheenPeriod); return; }
        XpAeroFx.WantTick(24);
        float bw = MathF.Round(Math.Clamp(f.Width * 0.4f, 10, 36));
        float k = (float)(ph / SheenSweep);
        float x = MathF.Round(f.X - bw + k * (f.Width + bw));
        p.PushClip(f);
        p.GradientRaw(new RectangleF(x, f.Y, bw, f.Height), 0x00FFFFFF, 0x8CFFFFFF, 0x00FFFFFF, 0, 0);
        p.PopClip();
    }

    // a tab: the two-part gloss, blue under the pointer; the selected one white, merging into the white page
    public override void Tab(Painter p, RectangleF r, string label, bool selected, bool hover)
    {
        var o = new RectangleF(r.X, r.Y, r.Width, r.Height + (selected ? 1 : 2));
        p.Smooth(true);
        p.FillRound(o, TabLine, 2);
        p.Smooth(false);
        var i = new RectangleF(o.X + 1, o.Y + 1, o.Width - 2, o.Height - 1);
        // the blue under the pointer fades up and away (the selected one comes 2 px bigger: its own key, no fade when picked)
        float h = selected ? 0 : XpAeroFx.Fade(XpAeroFx.Tab, r, hover, GlowIn, GlowOut);
        if (selected) p.Fill(i, 0xFFFFFFFF);
        else if (h >= 1) Gloss(p, i, 0xFFEAF6FD, 0xFFD9F0FC, 0xFFBEE6FD, 0xFFA7D9F5);
        else
        {
            Gloss(p, i, 0xFFF2F2F2, 0xFFEBEBEB, 0xFFDDDDDD, 0xFFCFCFCF);
            if (h > 0)
            {
                p.PushFade(h);
                Gloss(p, i, 0xFFEAF6FD, 0xFFD9F0FC, 0xFFBEE6FD, 0xFFA7D9F5);
                p.PopFade();
            }
        }
        if (!selected) p.Fill(new RectangleF(i.X, i.Y, i.Width, 1), 0xFFFCFCFC);
        Label(p, label, new RectangleF(r.X, r.Y + (selected ? 1 : 2), r.Width, 14), true, TextAlignment.Center, WindowText, false);
    }

    public override void ListBox(Painter p, RectangleF r)
    {
        p.Fill(r, Window);
        Outline(p, r, ListLine);
    }

    public override void ListSelection(Painter p, RectangleF row) => p.Fill(row, Highlight);

    // 7's scroll bar: a light trough, the arrows as small dark triangles, a glossy thumb with its grip
    public override void ScrollBar(Painter p, RectangleF bar, RectangleF up, RectangleF down, RectangleF thumb, bool upHeld, bool downHeld)
    {
        p.GradientRaw(bar, 0xFFE3E3E3, 0xFFF4F4F4, 0xFFE8E8E8, 0, 0);
        Arrow7(p, up, true, upHeld);
        Arrow7(p, down, false, downHeld);
        if (thumb.Height < 6) return;
        var t = new RectangleF(thumb.X + 1, thumb.Y + 1, thumb.Width - 2, thumb.Height - 2);
        p.Smooth(true);
        p.FillRound(t, 0xFF9A9A9A, 2);
        p.Smooth(false);
        p.GradientRaw(RectangleF.Inflate(t, -1, -1), 0xFFF3F3F3, 0xFFE8E8E8, 0xFFD6D6D6, 0, 0);
        p.Fill(new RectangleF(t.X + 1, t.Y + 1, t.Width - 2, 1), 0xFFFFFFFF);
        if (t.Height > 14)
        {
            float gy = MathF.Floor(t.Y + t.Height / 2) - 3;
            for (int i = 0; i < 4; i++)
            {
                p.Fill(new RectangleF(t.X + 4, gy + i * 2, t.Width - 8, 1), 0xFF7C7C7C);
                p.Fill(new RectangleF(t.X + 4, gy + i * 2 + 1, t.Width - 8, 1), 0xFFFFFFFF);
            }
        }
    }

    static readonly string[] Up7 = { "...k...", "..kkk..", ".kkkkk.", "kkkkkkk" };
    static readonly string[] Down7 = { "kkkkkkk", ".kkkkk.", "..kkk..", "...k..." };

    void Arrow7(Painter p, RectangleF r, bool up, bool held)
    {
        if (held) Face7(p, RectangleF.Inflate(r, -1, -1), false, true, false, false, false);
        p.Pixels(up ? Up7 : Down7, r.X + 4, r.Y + 6, held ? 0xFF1C3D5C : 0xFF4D6185);
    }

    public override void EditBox(Painter p, RectangleF r, string text, bool enabled)
    {
        p.Fill(r, enabled ? Window : Face);
        Field(p, r, false);
        bool g = Glass;
        Glass = false;
        Text(p, text, new RectangleF(r.X + 4, r.Y + 2, r.Width - 6, r.Height - 4), enabled ? WindowText : GrayText, false, TextAlignment.Leading, true,
            false, Ui.F95);
        Glass = g;
    }

    static readonly string[] Drop7 = { "kkkkkkk", ".kkkkk.", "..kkk..", "...k..." };

    // a drop-down list: in 7 it is a button with the small black arrow on its right, the choice on its face
    public override void ComboBox(Painter p, RectangleF r, string text, bool enabled, bool down, bool open, bool hover)
    {
        bool g = Glass;
        Glass = false;
        if (!enabled) OffFace(p, r);
        else ButtonFace(p, r, false, down || open, false, hover);
        if (open) _openCombo = r;
        p.Pixels(Drop7, r.Right - 14, MathF.Floor(r.Y + (r.Height - 4) / 2), enabled ? 0xFF000000 : OffText);
        Text(p, text, new RectangleF(r.X + 5, r.Y + 1, r.Width - 24, r.Height - 2), WindowText, false, TextAlignment.Leading, enabled, false, Ui.F95);
        Glass = g;
    }

    // the combo box whose list is open (drawn just before its list): which way the list slides out
    RectangleF _openCombo;

    // 7's "Slide open combo boxes": the list slides out of its box (down, or up when it opens above it) in 165 ms
    public override void DropList(Painter p, RectangleF r, string[] items, int hot, float rowH)
    {
        float e = XpAeroFx.Opening(XpAeroFx.Drop, r, 0.165f);
        if (e < 1)
        {
            bool up = r.Bottom <= _openCombo.Y + 1;
            float dy = MathF.Round((1 - e) * r.Height) * (up ? 1 : -1);
            p.PushClip(new RectangleF(r.X, r.Y, r.Width + 2, r.Height + 2));
            DropListAt(p, new RectangleF(r.X, r.Y + dy, r.Width, r.Height), items, hot, rowH);
            p.PopClip();
            return;
        }
        DropListAt(p, r, items, hot, rowH);
    }

    void DropListAt(Painter p, RectangleF r, string[] items, int hot, float rowH)
    {
        bool g = Glass;
        Glass = false;
        p.Fill(new RectangleF(r.X + 2, r.Y + 2, r.Width, r.Height), A(0xFF000000, 0.18f));      // 7's drop shadow
        p.Fill(r, Window);
        Outline(p, r, 0xFF646464);
        for (int i = 0; i < items.Length; i++)
        {
            var row = new RectangleF(r.X + 1, r.Y + 1 + i * rowH, r.Width - 2, rowH);
            bool on = i == hot;
            if (on) p.Fill(row, Highlight);
            Text(p, items[i], new RectangleF(row.X + 3, row.Y, row.Width - 5, rowH), on ? HighlightText : WindowText, false,
                TextAlignment.Leading, true, false, Ui.F95);
        }
        Glass = g;
    }

    // a status bar's pane: no sunken field in 7 - a light gloss and a separator at its right
    public override void StatusPane(Painter p, RectangleF r, string text, uint dot)
    {
        if (!Glass)
        {
            p.GradientRaw(r, 0xFFF8FAFD, 0xFFEEF2F8);
            p.Fill(new RectangleF(r.Right - 1, r.Y + 2, 1, r.Height - 4), 0xFFC5CED9);
            p.Fill(new RectangleF(r.Right, r.Y + 2, 1, r.Height - 4), 0xFFFFFFFF);
        }
        else
        {
            p.Smooth(true);
            p.FillRound(r, A(0xFF0B2E5C, 0.35f), 2);
            p.Smooth(false);
        }
        float x = r.X + 4;
        if (dot != 0)
        {
            p.Smooth(true);
            p.Circle(x + 3, r.Y + r.Height / 2, 3, dot);
            p.Smooth(false);
            x += 10;
        }
        Text(p, text, new RectangleF(x, r.Y, r.Right - x - 2, r.Height), WindowText, false, TextAlignment.Leading, true, false, Ui.F95);
    }

    public override void MenuItem(Painter p, float x, float y, string text) =>
        Text(p, text, new RectangleF(x, y, TextWidth(p, text, false, Ui.F95) + 2, 15), WindowText, false, TextAlignment.Leading, true, false, Ui.F95);
}
