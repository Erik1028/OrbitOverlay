using System;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;
using T = Orbit.TerminalSkin;

namespace Orbit;

// A notification on a green terminal: the way a message reaches every terminal of a Unix box (wall / write) - a little CRT with
// "Broadcast message from VRCX (tty1) (Fri Oct 16 18:39):", the title in capitals under it and the text. No pictures: the app's
// icon is a bitmap, the tube has none. In VR it switches on as the card comes, the message types in (Full) with the cursor
// running ahead of it, and the tube collapses as the card goes. A card's button (Notification.Action): the style's push button at the
// bottom right (bright while it asks), there once the message has typed.
public sealed partial class NotificationPanel
{
    partial void CardTerm(Notification c, float y, ref bool done)
    {
        done = true;
        var sk = T.It;
        var r = new RectangleF(10, y, TW / 2 - 20, CardH / 2);
        _p.Fill(r, T.BG);
        T.Crt(_p, r);
        float x = r.X + 10, w = r.Width - 20, ty = r.Y + 8;
        var now = DateTime.Now;
        string from = string.IsNullOrEmpty(c.Source) ? "orbit" : c.Source;
        string head = Lang.F("Broadcast message from {0} (tty1) ({1}):", from, now.ToString("ddd MMM d HH:mm", CultureInfo.InvariantCulture));
        if (T.W(head) > w) head = Lang.F("Broadcast message from {0}:", from);
        if (T.W(head) > w) head = sk.Fit(_p, head, w);
        string title = string.IsNullOrEmpty(c.Title) ? null : sk.Fit(_p, T.Up(c.Title), w);
        // the motion: since it came (its first frame is half-way through its fade-in), how far it is gone; the message typed in
        var g = RectangleF.Inflate(r, -3, -3);
        double clk = Clock.Now, since = _ov != null ? clk - c.Shown - 0.11 : -1;
        float off = c.Leaving ? Math.Clamp((1 - c.Vis) / 0.5f, 0, 1) : 0;
        var tube = T.TubeAt(since, off, false, g.Height);
        if (tube.Moving) AnimWake.At(clk);
        int left = int.MaxValue;            // characters still to show this frame
        if (since >= 0 && off <= 0 && Motion.Full)
        {
            double ts = since - T.OnDur;
            int typed = ts < 0 ? 0 : (int)(ts * 240);
            if (typed < head.Length + (title?.Length ?? 0) + (c.Content?.Length ?? 0)) { left = typed; AnimWake.At(clk); }
        }
        bool typing = left != int.MaxValue;
        float curX = -1, curY = 0;
        string Typed(string str, float cx, float cy)
        {
            if (!typing || str == null) return str;
            if (left >= str.Length) { left -= str.Length; return str; }
            string part = str[..Math.Max(0, left)];
            if (curX < 0 && cx >= 0) { curX = cx + T.W(part); curY = cy; }
            left = 0;
            return part;
        }
        sk.Put(_p, Typed(head, x, ty), x, ty, T.DIM);
        ty += 15;
        if (title != null)
        {
            sk.Put(_p, Typed(title, x, ty), x, ty, T.HOT);
            ty += 13;
        }
        if (!string.IsNullOrEmpty(c.Content))
            sk.Put(_p, Typed(c.Content, -1, 0), new RectangleF(x, ty, w - Act95Room(c), r.Bottom - 6 - ty), T.INK, 1, false, TextAlignment.Leading,
                ParagraphAlignment.Near, true);
        if (typing && curX >= 0 && left == 0 && curX + 6 <= g.Right) _p.Fill(new RectangleF(curX + 1, curY + 2, 5, 9), T.INK);
        if (c.Action != null && !typing) Act95(c, new RectangleF(r.Right - 10 - Act95W(c), r.Bottom - 8 - 23, Act95W(c), 23));
        T.Apply(_p, g, tube, x);
        T.Scanlines(_p, RectangleF.Inflate(r, -3, -3));
    }
}
