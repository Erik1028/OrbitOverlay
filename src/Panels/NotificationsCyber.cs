using System;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// Notifications in the Cyber style: HUD alerts - a dark card with the watch's dark-red edge and a yellow bar down its left side,
// the source as a yellow tag in monospace, INCOMING in cyan at its top-right, the icon in a bracketed well, the title in the
// condensed bold, the text under it. Motion: a new one scans in - a cyan line runs down the card and reveals it, the corners lock
// on (Full; Reduced: it fades in); one going folds up into its top edge as it fades.
public sealed partial class NotificationPanel
{
    static readonly string CyBell = ((char)0xEA8F).ToString();

    void DrawCyber()
    {
        _p.Begin(_surf.Bitmap, 0x00000000);
        float y = 0;
        foreach (var c in _cards)
        {
            float e = 1 - MathF.Pow(1 - c.Vis, 3);
            var r = new RectangleF(20, float.IsNaN(c.Y) ? y : c.Y, TW - 40, CardH);
            bool scan = CyberFx.Full && c.Vis < 1;
            float a = scan && !c.Leaving ? MathF.Min(1, e * 3) : e;
            uint A(uint col) => Theme.WithAlpha(col, a);
            float h = scan ? CardH * (c.Leaving ? e : CyberFx.EaseOut(c.Vis)) : CardH;
            if (scan) _p.PushClip(new RectangleF(r.X - 4, r.Y - 4, r.Width + 8, h + 4));
            CyberHud.CardShape(_p, r, A(Theme.WithAlpha(CyberHud.Page, 0.96f)), A(CyberHud.Edge), 1.5f, 8, 16);
            _p.Fill(new RectangleF(r.X + 1.5f, r.Y + 18, 4, r.Height - 36), A(CyberHud.Yellow));
            // the source: a yellow tag in monospace; INCOMING at the right
            string src = string.IsNullOrEmpty(c.Source) ? "MESSAGE" : c.Source.ToUpperInvariant();
            if (a > 0.98f) CyberHud.Tag(_p, r.X + 24, r.Y + 18, src, size: 15, h: 23);
            else CyberHud.Tag(_p, r.X + 24, r.Y + 18, src, A(CyberHud.Yellow), A(CyberHud.Ink), 15, 23);
            CyberHud.MonoAt(_p, "INCOMING", r.Right - 150, r.Y + 36, 126, A(CyberHud.Cyan), 14, FontWeight.Bold, TextAlignment.Trailing);
            var icon = new RectangleF(r.X + 24, r.Y + 58, 96, 96);
            if (c.Icon != null)
            {
                c.Bitmap ??= _p.CreateBitmap(c.Icon, NotificationListener.IconSize, NotificationListener.IconSize);
                _p.Image(c.Bitmap, icon, a);
            }
            else
            {
                _p.FillCut(icon, A(CyberHud.Card2), 0, 0, 12, 0);
                _p.Icon(c.Glyph ?? CyBell, icon, A(CyberHud.Cyan), 40);
            }
            CyberHud.Brackets(_p, RectangleF.Inflate(icon, 5, 5), A(CyberHud.Cyan), 12, 0, 2);
            float tx = icon.Right + 26, tw = r.Right - tx - 26;
            _p.Text(c.Title, new RectangleF(tx, r.Y + 52, tw, 42), A(CyberHud.White), 31, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Center,
                Theme.Display);
            _p.Text(c.Content, new RectangleF(tx, r.Y + 98, tw, 80), A(Theme.Mix(CyberHud.White, CyberHud.Grey, 0.35f)), 22, FontWeight.Normal,
                TextAlignment.Leading, ParagraphAlignment.Near, null, true);
            if (scan)
            {
                _p.PopClip();
                if (!c.Leaving)
                {
                    // the scan line at the reveal's edge, the corners locking on
                    CyberFx.ScanH(_p, r.X + 4, r.Right - 4, r.Y + h, CyberHud.Cyan, 1 - c.Vis * 0.6f);
                    float k = CyberFx.EaseOut(c.Vis);
                    CyberFx.Corners(_p, r, Theme.WithAlpha(CyberHud.Yellow, 1 - k * 0.7f), 16, 3, 10 * (1 - k));
                }
                else _p.Fill(new RectangleF(r.X + 6, r.Y + h - 1, r.Width - 12, 2), A(CyberHud.Cyan));
            }
            y += CardH + CardGap;
            if (y > TH - CardH) break;
        }
        _p.End();
    }
}
