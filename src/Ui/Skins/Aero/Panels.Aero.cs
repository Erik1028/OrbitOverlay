using System;
using System.Collections.Generic;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// Windows 7 on the smaller panels: the theater bar as a strip of glass, What's New as a task dialog (the blue main
// instruction, the white body, the grey button area), a notification as 7's balloon.

public sealed partial class TheaterBar
{
    partial void TheaterAero(ref bool done)
    {
        done = true;
        var sk = (AeroSkin)Skins.Aero;
        _canvas95 ??= new Surface(_p.Gpu, _w95, H95);
        _ui.Begin(_ptr, 2);
        _ui.Canvas95 = new SizeF(_w95, H95);
        _p.BeginPixel(_canvas95.Bitmap, 0x00000000);
        var strip = new RectangleF(0, 0, _w95, H95);
        _p.Smooth(true);
        _p.FillRound(strip, AeroSkin.GlassEdge, 6);
        _p.Smooth(false);
        var g = RectangleF.Inflate(strip, -1, -1);
        _p.PushRoundedClip(g, 5);
        AeroSkin.Glass7(_p, g);
        _p.PopRoundedClip();
        _p.Smooth(true);
        _p.StrokeRound(new RectangleF(g.X + 0.5f, g.Y + 0.5f, g.Width - 1, g.Height - 1), Theme.WithAlpha(0xFFFFFFFF, 0.55f), 1, 5);
        _p.Smooth(false);
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
        // a light divider in the glass, then the settings: two sliders and a check box
        _p.Fill(new RectangleF(x + 2, 5, 1, H95 - 10), Theme.WithAlpha(0xFF3E5A7E, 0.45f));
        _p.Fill(new RectangleF(x + 3, 5, 1, H95 - 10), Theme.WithAlpha(0xFFFFFFFF, 0.65f));
        x += 8;
        bool changed = false;
        sk.GlowText(_p, "Dim:", new RectangleF(x, 10, DimW95 + 2, 15), 0xFF000000, Ui.F95);
        x += DimW95;
        float dim = C.Dim;
        if (_ui.Track95("thb95.dim", new RectangleF(x, 4, Track95W, 28), ref dim, 0, 0.9f, true, 10)) { C.Dim = dim; changed = true; }
        x += Track95W + 8;
        sk.GlowText(_p, "Size:", new RectangleF(x, 10, SizeW95 + 2, 15), 0xFF000000, Ui.F95);
        x += SizeW95;
        float size = SizeNow;
        if (_ui.Track95("thb95.size", new RectangleF(x, 4, Track95W, 28), ref size, Theater.MinWidth, Theater.MaxWidth, true, 10)) { C.Width = size; changed = true; }
        x += Track95W + 8;
        bool curved = C.Curved;
        if (_ui.Check95("thb95.curve", x, 11, "&Curved", ref curved)) { C.Curved = curved; changed = true; }
        x += 19 + _ui.LabelWidth95("Curved") + 10;
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
}

public static partial class WhatsNewView
{
    static partial void WhatsNewAero(Ui ui, RectangleF r, IReadOnlyList<WhatsNew.Entry> list, string sub, ref bool closed, ref bool done)
    {
        done = true;
        var p = ui.P;
        // 7's dialog comes up with the quick zoom and fade (from 90 % and transparent, 200 ms), once, when it appears
        float e = XpAeroFx.Opening(XpAeroFx.Dialog, r, 0.2f);
        if (e < 1)
        {
            p.PushFade(e);
            p.PushScale(0.9f + 0.1f * e, r.X + r.Width / 2, r.Y + r.Height / 2);
        }
        closed = DialogAero(ui, r, list, sub);
        if (e < 1)
        {
            p.PopScale();
            p.PopFade();
        }
    }

    // the task dialog; true when OK or its X was pressed
    static bool DialogAero(Ui ui, RectangleF r, IReadOnlyList<WhatsNew.Entry> list, string sub)
    {
        var p = ui.P;
        var sk = (AeroSkin)Skins.Aero;
        var cl = AeroSkin.Window7(p, r, 26, 0xFFFFFFFF, 6);
        sk.GlowText(p, "What's New in OrbitOverlay", new RectangleF(r.X + 10, r.Y + 4, r.Width - 70, 18), 0xFF000000, 12);
        var rc = new RectangleF(r.Right - 7 - 43, r.Y + 1, 43, 18);
        bool close = ui.Clickable("wn95.x", RectangleF.Inflate(rc, 2, 3), out bool ch, out bool cd);
        AeroSkin.CaptionBox(p, rc, CaptionKind.Close, true, cd, ch);
        // a task dialog: its icon, the main instruction in blue, the text
        InfoIcon7(p, cl.X + 12, cl.Y + 10);
        p.Text("What has changed", new RectangleF(cl.X + 52, cl.Y + 6, cl.Width - 62, 22), AeroSkin.CpBlue, 15, FontWeight.Normal,
            TextAlignment.Leading, ParagraphAlignment.Center, sk.Font);
        ui.Text95(sub, new RectangleF(cl.X + 52, cl.Y + 28, cl.Width - 62, 28), 0xFF000000, false, TextAlignment.Leading, true, true);
        var foot = new RectangleF(cl.X, cl.Bottom - 40, cl.Width, 40);
        var box = new RectangleF(cl.X + 12, cl.Y + 60, cl.Width - 24, foot.Y - 8 - (cl.Y + 60));
        float textW = box.Width - 4 - 16 - 16;
        var lines = new List<(string text, bool bold, float indent)>();
        foreach (var e in list)
        {
            foreach (var l in Wrap95(ui, Lang.F("Version {0} - {1}", e.Version.ToString(3), e.Title), textW + 10, true)) lines.Add((l, true, 2));
            foreach (var pt in e.Points)
            {
                bool first = true;
                foreach (var l in Wrap95(ui, pt, textW)) { lines.Add(((first ? "• " : "") + l, false, first ? 6 : 14)); first = false; }
            }
            lines.Add(("", false, 0));
        }
        ui.List95("wn95.list", box, lines.Count, -1, (i, row, on) =>
        {
            var (text, bold, indent) = lines[i];
            ui.Text95(text, new RectangleF(row.X + indent, row.Y, row.Width - indent, row.Height), bold ? AeroSkin.HeadBlue : 0xFF000000, bold);
        }, 15);
        // the button area
        p.Fill(foot, 0xFFF0F0F0);
        p.Fill(new RectangleF(foot.X, foot.Y, foot.Width, 1), 0xFFDFDFDF);
        bool ok = ui.Button95("wn95.ok", new RectangleF(r.Right - 18 - 75, foot.Y + 9, 75, 23), "OK", true, isDefault: true);
        return ok || close;
    }

    // 7's information icon: a blue glossy disc with a white "i"
    static void InfoIcon7(Painter p, float x, float y)
    {
        AeroSkin.Disc(p, x + 16, y + 16, 15, 0xFF6FB3F2, 0xFF1A5DB5);
        p.Smooth(true);
        p.Circle(x + 16, y + 9.5f, 9, Theme.WithAlpha(0xFFFFFFFF, 0.25f));
        p.Ring(x + 16, y + 16, 15, 0xFF154C93, 1);
        p.Circle(x + 16, y + 9, 2.2f, 0xFFFFFFFF);
        p.Smooth(false);
        p.Fill(new RectangleF(x + 14, y + 13, 4, 11), 0xFFFFFFFF);
        p.Fill(new RectangleF(x + 12, y + 13, 3, 2), 0xFFFFFFFF);
        p.Fill(new RectangleF(x + 12, y + 23, 8, 2), 0xFFFFFFFF);
    }
}

public sealed partial class NotificationPanel
{
    // 7's notification balloon: white into blue-grey, a dark grey border, the title bold with its icon, the text under it
    partial void CardAero(Notification c, float y, ref bool done)
    {
        done = true;
        // 7's balloon fades in and out with its card (95 pops it up half way: the second half)
        float fade = XpAeroFx.Moves ? Math.Clamp((c.Vis - 0.5f) * 2, 0, 1) : 1;
        if (fade <= 0) return;
        if (fade < 1) _p.PushFade(fade);
        BalloonAero(c, y);
        if (fade < 1) _p.PopFade();
    }

    void BalloonAero(Notification c, float y)
    {
        var sk = (AeroSkin)Skins.Aero;
        var r = new RectangleF(10, y, TW / 2 - 20, CardH / 2);
        _p.Smooth(true);
        _p.FillRound(new RectangleF(r.X + 1, r.Y + 2, r.Width - 1, r.Height - 2), Theme.WithAlpha(0xFF000000, 0.25f), 6);
        _p.FillRound(new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 2), 0xFF767676, 6);
        _p.Smooth(false);
        var i = new RectangleF(r.X + 1, r.Y + 1, r.Width - 3, r.Height - 4);
        _p.GradientRaw(i, 0xFFFFFFFF, 0xFFF4F5F9, 0xFFE4E5F0, 5);
        var icon = new RectangleF(i.X + 12, i.Y + 12, 32, 32);
        if (c.Icon != null)
        {
            c.Bitmap ??= _p.CreateBitmap(c.Icon, NotificationListener.IconSize, NotificationListener.IconSize);
            _p.Image(c.Bitmap, icon, 1);
        }
        else
        {
            AeroSkin.Disc(_p, icon.X + 16, icon.Y + 16, 15, 0xFF6FB3F2, 0xFF1A5DB5);
            _p.Smooth(true);
            _p.Circle(icon.X + 16, icon.Y + 9, 2.2f, 0xFFFFFFFF);
            _p.Smooth(false);
            _p.Fill(new RectangleF(icon.X + 14, icon.Y + 13, 4, 11), 0xFFFFFFFF);
        }
        float tx = icon.Right + 12, tw = i.Right - tx - 26;
        _p.Text(c.Title, new RectangleF(tx, i.Y + 7, tw, 20), 0xFF000000, 12, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Center, sk.Font);
        // the balloon's close box: a small grey cross
        _p.Pixels(new[] { "k...k", ".k.k.", "..k..", ".k.k.", "k...k" }, i.Right - 16, i.Y + 10, 0xFF7A7A7A);
        float lh = sk.LineH(Ui.F95), ty = i.Y + 29, aw = Act95Room(c);       // (a button: the text leaves it the bottom right)
        foreach (var line in sk.WrapLines(_p, c.Content ?? "", i.Right - tx - 12 - aw, false, Ui.F95))
        {
            if (ty + lh > i.Bottom - (string.IsNullOrEmpty(c.Source) ? 4 : 18)) break;
            _p.Text(line, new RectangleF(tx, ty - 2, i.Right - tx - 12 - aw, lh + 4), 0xFF333333, Ui.F95, FontWeight.Normal, TextAlignment.Leading,
                ParagraphAlignment.Center, sk.Font);
            ty += lh;
        }
        if (!string.IsNullOrEmpty(c.Source))
            _p.Text(c.Source, new RectangleF(tx, i.Bottom - 19, i.Right - tx - 12 - aw, 18), 0xFF6D6D6D, Ui.F95, FontWeight.Normal, TextAlignment.Leading,
                ParagraphAlignment.Center, sk.Font);
        if (c.Action != null) Act95(c, new RectangleF(i.Right - 12 - Act95W(c), i.Bottom - 7 - 23, Act95W(c), 23));
    }
}
