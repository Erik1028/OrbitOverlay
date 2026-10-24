using System;
using System.Drawing;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// A notification in Windows XP: the notification area's balloon tip - the pale yellow InfoBk balloon with its black rounded edge
// and its stem pointing down at the tray, the information icon and the title in bold, the text under them, the close box in the
// corner; Tahoma 8 pt. A card's button (Notification.Action): XP's push button at the balloon's bottom right, the balloon as tall as the
// card then.
public sealed partial class NotificationPanel
{
    const uint XpInfoBk = 0xFFFFFFE1, XpInfoText = 0xFF000000;

    partial void CardXp(Notification c, float y, ref bool done)
    {
        done = true;
        // "Fade or slide ToolTips into view": the balloon fades in and out with its card (95 pops it up half way: the second half)
        float fade = XpAeroFx.Moves ? Math.Clamp((c.Vis - 0.5f) * 2, 0, 1) : 1;
        if (fade <= 0) return;
        if (fade < 1) _p.PushFade(fade);
        BalloonXp(c, y);
        if (fade < 1) _p.PopFade();
    }

    void BalloonXp(Notification c, float y)
    {
        var r = new RectangleF(10, y, TW / 2 - 20, CardH / 2);
        const float stem = 12;
        // as tall as its text (a balloon fits what it says), at most the card
        float aw = Act95Room(c);
        float textH = string.IsNullOrEmpty(c.Content) ? 0 : _p.MeasureHeight(c.Content, Ui.F95, FontWeight.Normal, r.Width - 20 - aw, Theme.RetroFont);
        float bh = c.Action != null ? r.Height - stem : Math.Clamp(MathF.Ceiling(8 + 16 + 6 + textH + 9), 44, r.Height - stem);
        var body = new RectangleF(r.X, r.Y, r.Width, bh);
        // the balloon: GDI's round rect (aliased, as XP drew its region), then the stem under it near the right end
        _p.FillRound(body, XpInfoBk, 7);
        _p.StrokeRound(new RectangleF(body.X + 0.5f, body.Y + 0.5f, body.Width - 1, body.Height - 1), XpInfoText, 1, 7);
        float tipX = body.Right - 48, sb = body.Bottom - 1;
        _p.Polygon(new[] { new Vector2(tipX - 18, sb), new Vector2(tipX + 1, sb), new Vector2(tipX + 1, sb + stem + 1) }, XpInfoBk);
        _p.Fill(new RectangleF(tipX - 17, sb, 18, 1), XpInfoBk);
        _p.Line(tipX - 18 + 0.5f, sb + 0.5f, tipX + 0.5f, sb + stem + 0.5f, XpInfoText, 1);
        _p.Fill(new RectangleF(tipX, sb, 1, stem + 1), XpInfoText);
        // the close box
        var x = new RectangleF(body.Right - 9 - 14, body.Y + 7, 14, 14);
        _p.Fill(new RectangleF(x.X + 1, x.Y + 1, x.Width - 2, x.Height - 2), 0xFFFFFFF4);
        _p.Fill(new RectangleF(x.X + 1, x.Y, x.Width - 2, 1), 0xFFC4C2AE);
        _p.Fill(new RectangleF(x.X + 1, x.Bottom - 1, x.Width - 2, 1), 0xFFC4C2AE);
        _p.Fill(new RectangleF(x.X, x.Y + 1, 1, x.Height - 2), 0xFFC4C2AE);
        _p.Fill(new RectangleF(x.Right - 1, x.Y + 1, 1, x.Height - 2), 0xFFC4C2AE);
        for (int i = 0; i < 6; i++)
        {
            _p.Fill(new RectangleF(x.X + 4 + i, x.Y + 4 + i, 1, 1), 0xFF6D6D6D);
            _p.Fill(new RectangleF(x.X + 9 - i, x.Y + 4 + i, 1, 1), 0xFF6D6D6D);
        }
        // the icon: the app's own, or XP's information icon
        float ix = body.X + 10, iy = body.Y + 8;
        if (c.Icon != null)
        {
            c.Bitmap ??= _p.CreateBitmap(c.Icon, NotificationListener.IconSize, NotificationListener.IconSize);
            _p.Image(c.Bitmap, new RectangleF(ix, iy, 16, 16), 1);
        }
        else InfoIconXp(ix, iy);
        // the title in bold, the sender grey at the right of it
        float tx = ix + 22, tr = x.X - 8;
        string src = string.IsNullOrEmpty(c.Source) ? null : c.Source;
        float sw = src == null ? 0 : _p.MeasureWidth(src, Ui.F95, FontWeight.Normal, Theme.RetroFont) + 2;
        if (sw > (tr - tx) * 0.4f) { src = null; sw = 0; }
        string title = string.IsNullOrEmpty(c.Title) ? src ?? "Notification" : c.Title;
        if (title == src) { src = null; sw = 0; }
        title = Theme.RetroSkin.Fit(_p, title, tr - tx - (sw > 0 ? sw + 8 : 0), Ui.F95, true);
        _p.Text(title, new RectangleF(tx, iy + 1, tr - tx, 14), XpInfoText, Ui.F95, FontWeight.Bold, TextAlignment.Leading,
            ParagraphAlignment.Center, Theme.RetroFont);
        if (src != null)
            _p.Text(src, new RectangleF(tr - sw, iy + 1, sw, 14), 0xFF7A7865, Ui.F95, FontWeight.Normal, TextAlignment.Leading,
                ParagraphAlignment.Center, Theme.RetroFont);
        // the text, from the balloon's left margin
        _p.PushClip(new RectangleF(body.X + 10, iy + 22, body.Width - 20 - aw, body.Bottom - 6 - (iy + 22)));
        _p.Text(c.Content, new RectangleF(body.X + 10, iy + 22, body.Width - 20 - aw, body.Bottom - 6 - (iy + 22)), XpInfoText, Ui.F95,
            FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Near, Theme.RetroFont, true);
        _p.PopClip();
        if (c.Action != null) Act95(c, new RectangleF(body.Right - 10 - Act95W(c), body.Bottom - 8 - 23, Act95W(c), 23));
    }

    // XP's 16 x 16 information icon: a blue ball, lighter at the top left, a white "i"
    void InfoIconXp(float x, float y)
    {
        _p.Smooth(true);
        _p.Circle(x + 8, y + 8, 7.6f, 0xFF1D4FB6);
        _p.GradientRaw(new RectangleF(x + 1.2f, y + 1.2f, 13.6f, 13.6f), 0xFF8EC0FF, 0xFF2563D8, 6.8f, 60);
        _p.Smooth(false);
        _p.Fill(new RectangleF(x + 7, y + 3, 2, 2), 0xFFFFFFFF);
        _p.Fill(new RectangleF(x + 6, y + 6, 3, 1), 0xFFFFFFFF);
        _p.Fill(new RectangleF(x + 7, y + 7, 2, 4), 0xFFFFFFFF);
        _p.Fill(new RectangleF(x + 6, y + 11, 4, 1), 0xFFFFFFFF);
    }
}
