using System;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// A notification in the Classic Mac style: what the Notification Manager put up - a Platinum window titled with the sender,
// the note icon (or the app's own), the title in Chicago and the words in Geneva. A card's button (Notification.Action): Platinum's push
// button in the window's bottom right corner, the default ring round it at rest.
public sealed partial class NotificationPanel
{
    partial void CardMac(Notification c, float y, ref bool done)
    {
        done = true;
        var m = (MacSkin)Skins.Mac;
        var r = new RectangleF(10, y, TW / 2 - 20, CardH / 2);
        m.MacWindow(_p, r, string.IsNullOrEmpty(c.Source) ? "Notification" : c.Source);
        var icon = new RectangleF(r.X + 14, r.Y + 33, 32, 32);
        bool shown = MacIconShown(c);               // blinking as it arrives
        if (shown && c.Icon != null)
        {
            c.Bitmap ??= _p.CreateBitmap(c.Icon, NotificationListener.IconSize, NotificationListener.IconSize);
            _p.Image(c.Bitmap, icon, 1);
        }
        else if (shown) W95Icons.Draw(_p, W95Icons.Id.Info32, icon.X, icon.Y);
        float tx = icon.Right + 14, tw = r.Right - tx - 12;
        _p.Text(c.Title, new RectangleF(tx, r.Y + 31, tw, 15), 0xFF000000, Ui.F95, FontWeight.Bold,
            TextAlignment.Leading, ParagraphAlignment.Center, Theme.RetroFont);
        _p.Text(c.Content, new RectangleF(tx, r.Y + 48, tw - Act95Room(c), r.Bottom - r.Y - 52), 0xFF000000, Ui.F95, FontWeight.Normal,
            TextAlignment.Leading, ParagraphAlignment.Near, Theme.RetroFont, true);
        if (c.Action != null) Act95(c, new RectangleF(r.Right - 14 - Act95W(c), r.Bottom - 10 - 23, Act95W(c), 23));
    }
}
