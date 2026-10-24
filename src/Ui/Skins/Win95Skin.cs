using System;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// Windows 95, exactly as it was drawn before the styles came (1.12.3): 8 pt MS Sans Serif (11 px), the two-pixel 3D edges, navy
// captions, 75 x 23 push buttons, 13 x 13 check boxes, etched group boxes, trackbars with a pointed thumb, block progress bars,
// property sheet tabs, the dithered scroll bar. The other styles derive from it: what they do not override stays 1995 (in their
// palette - the bodies below read it).
public class Win95Skin : RetroSkin
{
    public override RetroStyle Style => RetroStyle.Win95;
    public override string Id => "classic";
    public override string Tag => "95";
    public override string Name => "Windows 95";

    // ---- the palette ----
    public override uint Face => 0xFFC0C0C0;
    public override uint Light => 0xFFDFDFDF;
    public override uint Hilight => 0xFFFFFFFF;
    public override uint Shadow => 0xFF808080;
    public override uint DarkShadow => 0xFF000000;
    public override uint Window => 0xFFFFFFFF;
    public override uint WindowText => 0xFF000000;
    public override uint ButtonText => 0xFF000000;
    public override uint GrayText => 0xFF808080;
    public override uint DimText => 0xFF404040;
    public override uint Highlight => 0xFF000080;
    public override uint HighlightText => 0xFFFFFFFF;
    public override uint ActiveCaption => 0xFF000080;
    public override uint CaptionText => 0xFFFFFFFF;
    public override uint InactiveCaption => 0xFF808080;
    public override uint InactiveCaptionText => 0xFFC0C0C0;
    public override uint Desktop => 0xFF008080;
    public override uint Laser => 0xFF1084D0;      // "same as accent": navy would vanish in a dark world

    public override uint MapColor(uint c) => c;
    public override uint TextColor(uint c) => c;

    // ---- type ----
    public override string Font => Theme.ClassicFont;      // MS Sans Serif's TrueType twin (Tahoma where it is missing)
    public override void MapFont(ref string family, ref float size, ref FontWeight weight) { }
    public override PixelFont PixelFont(string family, float size, FontWeight weight) => null;
    public override bool SmoothText => false;

    public override float DropRowH => 13;

    // ---- the pixel pictures ----
    protected static readonly string[] CheckGlyph =
    {
        "......k", ".....kk", "k...kkk", "kk.kkk.", "kkkkk..", ".kkk...", "..k....",
    };
    protected static readonly string[] CloseGlyph =
    {
        "kk....kk", ".kk..kk.", "..kkkk..", "...kk...", "..kkkk..", ".kk..kk.", "kk....kk",
    };
    protected static readonly string[] HelpGlyph = { ".kkkk.", "kk..kk", "...kk.", "..kk..", "..kk..", "......", "..kk..", };
    protected static readonly string[] MinGlyph = { "kkkkkk", "kkkkkk" };
    protected static readonly string[] MaxGlyph =
    {
        "kkkkkkkkk", "kkkkkkkkk", "k.......k", "k.......k", "k.......k", "k.......k", "k.......k", "kkkkkkkkk",
    };
    protected static readonly string[] UpGlyph = { "...k...", "..kkk..", ".kkkkk.", "kkkkkkk" };
    protected static readonly string[] DownGlyph = { "kkkkkkk", ".kkkkk.", "..kkk..", "...k..." };

    // ---- text ----

    public override void Text(Painter p, string s, RectangleF r, uint c, bool bold, TextAlignment al, bool enabled, bool wrap, float size)
    {
        if (string.IsNullOrEmpty(s)) return;
        var w = bold ? FontWeight.Bold : FontWeight.Normal;
        if (!enabled) { DisabledText(p, s, r, size, w, al, wrap ? ParagraphAlignment.Near : ParagraphAlignment.Center); return; }
        p.Text(s, r, TextColor(c), size, w, al, wrap ? ParagraphAlignment.Near : ParagraphAlignment.Center, Font, wrap);
    }

    public override float TextWidth(Painter p, string s, bool bold, float size) =>
        p.MeasureWidth(s, size, bold ? FontWeight.Bold : FontWeight.Normal, Font);

    public override void Label(Painter p, string s, RectangleF r, bool enabled, TextAlignment al, uint c, bool bold)
    {
        var (t, key) = Ui.Amp(s);
        if (string.IsNullOrEmpty(t)) return;
        Text(p, t, r, c, bold, al, enabled, false, Ui.F95);
        if (key < 0) return;
        float full = TextWidth(p, t, bold, Ui.F95);
        float x = al == TextAlignment.Center ? r.X + MathF.Floor((r.Width - full) / 2) : al == TextAlignment.Trailing ? r.Right - full : r.X;
        var (pre, ch) = Ui.AmpParts(t, key);
        x = MathF.Round(x + TextWidth(p, pre, bold, Ui.F95));
        float uw = Math.Max(1, MathF.Round(TextWidth(p, ch, bold, Ui.F95)) - 1);
        float y = MathF.Floor(r.Y + (r.Height - 13) / 2) + 11;
        if (!enabled) p.Fill(new RectangleF(x + 1, y + 1, uw, 1), Hilight);
        p.Fill(new RectangleF(x, y, uw, 1), enabled ? TextColor(c) : Shadow);
    }

    // greyed-out text the 95 way: etched - white, then grey on top one pixel up-left
    public override void DisabledText(Painter p, string s, RectangleF r, float size, FontWeight w, TextAlignment h, ParagraphAlignment v)
    {
        float px = p.Px;
        p.Text(s, new RectangleF(r.X + px, r.Y + px, r.Width, r.Height), Hilight, size, w, h, v);
        p.Text(s, r, Shadow, size, w, h, v);
    }

    // ---- frames ----

    // the two-pixel 3D edges of the era: Raised = a button, Window = a window frame (Page: a property sheet's page, drawn the same),
    // Pressed = a button held down, Sunken = a text box / list / colour well, Etched = a group box line
    public override void Bevel(Painter p, RectangleF r, Painter.Edge e)
    {
        float px = p.Px;
        var (tl1, br1, tl2, br2) = e switch
        {
            Painter.Edge.Raised => (Hilight, DarkShadow, Light, Shadow),
            Painter.Edge.Window or Painter.Edge.Page => (Light, DarkShadow, Hilight, Shadow),
            Painter.Edge.Pressed => (DarkShadow, Hilight, Shadow, Light),
            Painter.Edge.Sunken => (Shadow, Hilight, DarkShadow, Light),
            _ => (Shadow, Hilight, Hilight, Shadow),
        };
        Frame(p, r, tl1, br1, px);
        Frame(p, RectangleF.Inflate(r, -px, -px), tl2, br2, px);
    }

    protected static void Frame(Painter p, RectangleF r, uint tl, uint br, float px)
    {
        p.Fill(new RectangleF(r.X, r.Y, r.Width, px), tl);
        p.Fill(new RectangleF(r.X, r.Y, px, r.Height), tl);
        p.Fill(new RectangleF(r.X, r.Bottom - px, r.Width, px), br);
        p.Fill(new RectangleF(r.Right - px, r.Y, px, r.Height), br);
    }

    // a groove across: the line between sections of a 95 dialog
    public override void EtchedLine(Painter p, float x1, float x2, float y)
    {
        float px = p.Px;
        p.Fill(new RectangleF(x1, y, x2 - x1, px), Shadow);
        p.Fill(new RectangleF(x1, y + px, x2 - x1, px), Hilight);
    }

    public override void FillDesktop(Painter p, RectangleF r) => p.Fill(r, Desktop);

    public override void Caption(Painter p, RectangleF r, string title, W95Icons.Id? icon, bool active)
    {
        p.Fill(r, active ? ActiveCaption : InactiveCaption);
        float tx = r.X + 3;
        if (icon is { } ic) { W95Icons.Draw(p, ic, r.X + 2, r.Y + (r.Height - 16) / 2); tx = r.X + 21; }
        Text(p, title, new RectangleF(tx, r.Y, r.Width - (tx - r.X) - 40, r.Height), active ? CaptionText : InactiveCaptionText, true,
            TextAlignment.Leading, true, false, Ui.F95);
    }

    public override void CaptionButton(Painter p, RectangleF r, CaptionKind kind, bool enabled, bool down, bool hover)
    {
        var (glyph, gx, gy) = kind switch
        {
            CaptionKind.Help => (HelpGlyph, 5f, 3f),
            CaptionKind.Min => (MinGlyph, 4f, 9f),
            CaptionKind.Max => (MaxGlyph, 3f, 2f),
            _ => (CloseGlyph, 4f, 3f),
        };
        p.Fill(r, Face);
        Bevel(p, r, down ? Painter.Edge.Pressed : Painter.Edge.Raised);
        float o = down ? 1 : 0;
        if (!enabled) p.Pixels(glyph, r.X + gx + 1, r.Y + gy + 1, Hilight);
        p.Pixels(glyph, r.X + gx + o, r.Y + gy + o, enabled ? ButtonText : Shadow);
    }

    // a little 95 window: with a caption when it is tall enough, a plain raised panel when not
    public override RectangleF WidgetFrame(Painter p, RectangleF r, string title)
    {
        p.Fill(r, Face);
        Bevel(p, r, Painter.Edge.Window);
        var c = RectangleF.Inflate(r, -3, -3);
        if (title != null)
        {
            var cap = new RectangleF(c.X, c.Y, c.Width, 18);
            p.Fill(cap, ActiveCaption);
            Text(p, Fit(p, title, cap.Width - 6, Ui.F95, true), new RectangleF(cap.X + 3, cap.Y, cap.Width - 4, cap.Height), CaptionText, true,
                TextAlignment.Leading, true, false, Ui.F95);
            c = new RectangleF(c.X + 1, c.Y + 19, c.Width - 2, c.Height - 20);
        }
        return c;
    }

    // ---- controls ----

    // a 95 push button: raised; held = black frame + flat grey edge; latched (a toggle that is in) = dithered + pushed in;
    // isDefault = the black frame of a dialog's default button. Returns the face.
    public override RectangleF ButtonFace(Painter p, RectangleF r, bool isDefault, bool down, bool latched, bool hover)
    {
        var b = r;
        if (isDefault || down)
        {
            p.Stroke(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), DarkShadow, 1);
            b = RectangleF.Inflate(r, -1, -1);
        }
        if (latched && !down) p.FillChecker(b, Hilight, Face);
        else p.Fill(b, Face);
        if (down)
            p.Stroke(new RectangleF(b.X + 0.5f, b.Y + 0.5f, b.Width - 1, b.Height - 1), Shadow, 1);
        else
            Bevel(p, b, latched ? Painter.Edge.Pressed : Painter.Edge.Raised);
        return b;
    }

    public override void PushButton(Painter p, RectangleF r, string label, W95Icons.Id? icon, bool enabled, bool isDefault, bool down,
        bool latched, bool hover)
    {
        var b = ButtonFace(p, r, isDefault, down, latched, hover);
        float o = down || latched ? 1 : 0;
        if (icon is { } ic)
        {
            int n = W95Icons.Size(ic);
            float tw = string.IsNullOrEmpty(label) ? 0 : LabelWidth(p, label) + 4;
            float x = b.X + (b.Width - n - tw) / 2 + o, y = b.Y + (b.Height - n) / 2 + o;
            if (enabled) W95Icons.Draw(p, ic, MathF.Round(x), MathF.Round(y));
            else W95Icons.DrawDisabled(p, ic, MathF.Round(x), MathF.Round(y));      // embossed: grey with a white edge (DrawState's disabled)
            if (tw > 0) Label(p, label, new RectangleF(MathF.Round(x + n + 4), b.Y + o, tw, b.Height), enabled, TextAlignment.Leading, ButtonText, false);
        }
        else
            Label(p, label, new RectangleF(b.X + o, b.Y + o, b.Width, b.Height), enabled, TextAlignment.Center, ButtonText, false);
    }

    public override void GlyphButton(Painter p, RectangleF r, string[] glyph, bool enabled, bool down, bool hover)
    {
        var b = ButtonFace(p, r, false, down, false, hover);
        float o = down ? 1 : 0, gw = glyph[0].Length, gh = glyph.Length;
        float gx = b.X + MathF.Floor((b.Width - gw) / 2) + o, gy = b.Y + MathF.Floor((b.Height - gh) / 2) + o;
        if (!enabled) p.Pixels(glyph, gx + 1, gy + 1, Hilight);
        p.Pixels(glyph, gx, gy, enabled ? ButtonText : Shadow);
    }

    public override RectangleF KeyCap(Painter p, RectangleF r, bool oneShot, bool down, bool latched, bool hover) =>
        ButtonFace(p, r, oneShot, down, latched, hover);

    public override void CheckBox(Painter p, float x, float y, string label, bool value, bool enabled, bool held, bool hover)
    {
        var box = new RectangleF(x, y, 13, 13);
        p.Fill(box, enabled && !(held && enabled) ? Window : Face);
        Bevel(p, box, Painter.Edge.Sunken);
        if (value) p.Pixels(CheckGlyph, x + 3, y + 3, enabled ? WindowText : GrayText);
        Label(p, label, new RectangleF(x + 19, y - 1, 400, 15), enabled, TextAlignment.Leading, WindowText, false);
    }

    public override void Radio(Painter p, float x, float y, string label, bool on, bool enabled, bool held, bool hover)
    {
        W95Icons.Draw(p, on ? W95Icons.Id.RadioOn : W95Icons.Id.RadioOff, x, y);
        Label(p, label, new RectangleF(x + 18, y - 2, 400, 15), enabled, TextAlignment.Leading, WindowText, false);
    }

    public override void GroupBox(Painter p, RectangleF r, string title)
    {
        float y = r.Y + 6;
        p.Stroke(new RectangleF(r.X + 0.5f, y + 0.5f, r.Width - 2, r.Bottom - y - 2), Shadow, 1);
        p.Stroke(new RectangleF(r.X + 1.5f, y + 1.5f, r.Width - 2, r.Bottom - y - 2), Hilight, 1);
        if (string.IsNullOrEmpty(title)) return;
        float tw = LabelWidth(p, title);
        p.Fill(new RectangleF(r.X + 6, r.Y, tw + 4, 13), Face);
        Label(p, title, new RectangleF(r.X + 8, r.Y, tw + 2, 13), true, TextAlignment.Leading, WindowText, false);
    }

    public override void Trackbar(Painter p, RectangleF r, float t, bool enabled, int ticks, bool hover, bool held)
    {
        var ch = new RectangleF(r.X + 6, r.Y + 8, r.Width - 12, 4);
        // the channel: a 4 pixel groove
        p.Fill(ch, Window);
        Bevel(p, ch, Painter.Edge.Sunken);
        // tick marks under it
        for (int i = 0; i < ticks; i++)
        {
            float tx = MathF.Round(ch.X + (ch.Width - 1) * i / (ticks - 1));
            p.Fill(new RectangleF(tx, r.Y + 24, 1, i == 0 || i == ticks - 1 ? 4 : 3), WindowText);
        }
        // the thumb: 11 wide, square top, pointing down
        float x = MathF.Round(ch.X + ch.Width * t) - 5, y = r.Y;
        p.Fill(new RectangleF(x, y, 11, 15), Face);
        for (int i = 0; i < 5; i++) p.Fill(new RectangleF(x + i + 1, y + 15 + i, 9 - 2 * i, 1), Face);
        p.Fill(new RectangleF(x, y, 10, 1), Hilight);                 // top + left light edge
        p.Fill(new RectangleF(x, y, 1, 15), Hilight);
        p.Fill(new RectangleF(x + 10, y, 1, 15), DarkShadow);           // right dark edge
        p.Fill(new RectangleF(x + 9, y + 1, 1, 14), Shadow);
        for (int i = 0; i < 5; i++)
        {
            p.Fill(new RectangleF(x + i, y + 15 + i, 1, 1), Hilight);                 // the point: light on the left
            p.Fill(new RectangleF(x + 10 - i, y + 15 + i, 1, 1), DarkShadow);         // dark on the right
            if (i < 4) p.Fill(new RectangleF(x + 9 - i, y + 15 + i, 1, 1), Shadow);
        }
        p.Fill(new RectangleF(x + 5, y + 20, 1, 1), DarkShadow);
        if (!enabled) p.Fill(new RectangleF(x + 3, y + 3, 5, 9), Face);
    }

    // blocks
    public override void Progress(Painter p, RectangleF r, float frac, uint block)
    {
        p.Fill(r, Face);
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 1), Shadow);
        p.Fill(new RectangleF(r.X, r.Y, 1, r.Height), Shadow);
        p.Fill(new RectangleF(r.X, r.Bottom - 1, r.Width, 1), Hilight);
        p.Fill(new RectangleF(r.Right - 1, r.Y, 1, r.Height), Hilight);
        var inner = RectangleF.Inflate(r, -2, -2);
        float bw = Math.Max(4, MathF.Round(inner.Height * 0.66f));
        int n = (int)((inner.Width + 2) / (bw + 2));
        int on = (int)MathF.Round(Math.Clamp(frac, 0, 1) * n);
        if (on == 0 && frac >= 0.01f) on = 1;           // a low battery still shows its one red block
        float x0 = inner.X + MathF.Floor((inner.Width + 2 - n * (bw + 2)) / 2);     // the leftover split on both ends
        for (int i = 0; i < on; i++) p.Fill(new RectangleF(x0 + i * (bw + 2), inner.Y, bw, inner.Height), block);
    }

    public override void Tab(Painter p, RectangleF r, string label, bool selected, bool hover)
    {
        p.Fill(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 1), Face);
        p.Fill(new RectangleF(r.X, r.Y + 2, 1, r.Height - 2), Hilight);              // left
        p.Fill(new RectangleF(r.X + 1, r.Y + 1, 1, 1), Hilight);                      // the chamfered corner
        p.Fill(new RectangleF(r.X + 2, r.Y, r.Width - 4, 1), Hilight);               // top
        p.Fill(new RectangleF(r.Right - 2, r.Y + 1, 1, 1), DarkShadow);
        float rh = r.Height - 2 - (selected ? 2 : 0);
        p.Fill(new RectangleF(r.Right - 1, r.Y + 2, 1, rh), DarkShadow);       // right
        p.Fill(new RectangleF(r.Right - 2, r.Y + 2, 1, rh), Shadow);
        Label(p, label, new RectangleF(r.X, r.Y + (selected ? 1 : 2), r.Width, 14), true, TextAlignment.Center, WindowText, false);
    }

    public override void ListBox(Painter p, RectangleF r)
    {
        p.Fill(r, Window);
        Bevel(p, r, Painter.Edge.Sunken);
    }

    public override void ListSelection(Painter p, RectangleF row) => p.Fill(row, Highlight);

    public override void ScrollBar(Painter p, RectangleF bar, RectangleF up, RectangleF down, RectangleF thumb, bool upHeld, bool downHeld)
    {
        p.FillChecker(bar, Hilight, Face);
        ArrowButton(p, up, UpGlyph, upHeld);
        ArrowButton(p, down, DownGlyph, downHeld);
        p.Fill(thumb, Face);
        Bevel(p, thumb, Painter.Edge.Raised);
    }

    protected void ArrowButton(Painter p, RectangleF r, string[] glyph, bool held)
    {
        p.Fill(r, Face);
        if (held) p.Stroke(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Shadow, 1);
        else Bevel(p, r, Painter.Edge.Raised);
        float o = held ? 1 : 0;
        p.Pixels(glyph, r.X + 4 + o, r.Y + 6 + o, ButtonText);
    }

    // read-only here: Orbit types with its own keyboard, not into its settings
    public override void EditBox(Painter p, RectangleF r, string text, bool enabled)
    {
        p.Fill(r, enabled ? Window : Face);
        Bevel(p, r, Painter.Edge.Sunken);
        Text(p, text, new RectangleF(r.X + 4, r.Y + 2, r.Width - 6, r.Height - 4), enabled ? WindowText : GrayText, false,
            TextAlignment.Leading, true, false, Ui.F95);
    }

    public override void ComboBox(Painter p, RectangleF r, string text, bool enabled, bool down, bool open, bool hover)
    {
        p.Fill(r, enabled ? Window : Face);
        Bevel(p, r, Painter.Edge.Sunken);
        var b = new RectangleF(r.Right - 18, r.Y + 2, 16, r.Height - 4);
        p.Fill(b, Face);
        if (down) p.Stroke(new RectangleF(b.X + 0.5f, b.Y + 0.5f, b.Width - 1, b.Height - 1), Shadow, 1);
        else Bevel(p, b, Painter.Edge.Raised);
        float o = down ? 1 : 0, gy = MathF.Floor(b.Y + (b.Height - 4) / 2);
        if (!enabled) p.Pixels(DownGlyph, b.X + 5, gy + 1, Hilight);
        p.Pixels(DownGlyph, b.X + 4 + o, gy + o, enabled ? ButtonText : Shadow);
        var tr = new RectangleF(r.X + 3, r.Y + 3, b.X - r.X - 4, r.Height - 6);
        if (open) p.Fill(tr, Highlight);
        Text(p, text, new RectangleF(tr.X + 2, tr.Y, tr.Width - 2, tr.Height), open ? HighlightText : enabled ? WindowText : GrayText, false,
            TextAlignment.Leading, true, false, Ui.F95);
    }

    public override void DropList(Painter p, RectangleF r, string[] items, int hot, float rowH)
    {
        p.Fill(r, Window);
        p.Stroke(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), DarkShadow, 1);
        for (int i = 0; i < items.Length; i++)
        {
            var row = new RectangleF(r.X + 1, r.Y + 1 + i * rowH, r.Width - 2, rowH);
            bool on = i == hot;
            if (on) p.Fill(row, Highlight);
            Text(p, items[i], new RectangleF(row.X + 2, row.Y, row.Width - 4, rowH), on ? HighlightText : WindowText, false,
                TextAlignment.Leading, true, false, Ui.F95);
        }
    }

    // status bar panes: thin sunken fields
    public override void StatusPane(Painter p, RectangleF r, string text, uint dot)
    {
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 1), Shadow);
        p.Fill(new RectangleF(r.X, r.Y, 1, r.Height), Shadow);
        p.Fill(new RectangleF(r.X, r.Bottom - 1, r.Width, 1), Hilight);
        p.Fill(new RectangleF(r.Right - 1, r.Y, 1, r.Height), Hilight);
        float x = r.X + 4;
        if (dot != 0) { p.Fill(new RectangleF(x, r.Y + (r.Height - 6) / 2, 6, 6), dot); x += 10; }
        Text(p, text, new RectangleF(x, r.Y, r.Right - x - 2, r.Height), WindowText, false, TextAlignment.Leading, true, false, Ui.F95);
    }

    // a menu bar's item, its access key underlined
    public override void MenuItem(Painter p, float x, float y, string text)
    {
        Text(p, text, new RectangleF(x, y, TextWidth(p, text, false, Ui.F95) + 2, 15), WindowText, false, TextAlignment.Leading, true, false, Ui.F95);
        float u = TextWidth(p, text[..1], false, Ui.F95);
        p.Fill(new RectangleF(x, y + 12, u, 1), WindowText);
    }

    public override bool Icon(Painter p, W95Icons.Id id, float x, float y, int scale) => false;
}
