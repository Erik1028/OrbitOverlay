using System;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// The Room panel in the Cyber style: a ROOM tag, the count as a cyan readout and the world in grey monospace, the people as HUD
// rows - a dark plate with a thin edge, the star yellow when on, the name in the condensed type, FRIEND as a small cyan code, how
// long ago in monospace. Same places, ids and scrolling as the modern panel (RoomPanel.DrawModern).
public sealed partial class RoomPanel
{
    void CyberHeader()
    {
        float tw = CyberHud.Tag(_p, Pad + 4, 20, "ROOM", size: 16, h: 25);
        float hx = Pad + 4 + tw + 14;
        if (_s.InRoom)
        {
            string count = CountText(_s.Here.Count, FriendsHere(), true);     // in the language picked (it was English only), in capitals
            float cw = CyberHud.MonoWidth(_p, count, 15, FontWeight.Bold);
            CyberHud.MonoAt(_p, count, hx, 38, cw + 4, CyberHud.Cyan, 15, FontWeight.Bold);
            hx += cw + 18;
            float ww = TW - Pad - 100 - hx;
            if (ww > 60 && _s.World.Length > 0)
                _p.Text(Ellipsize(_s.World, ww, 15), new RectangleF(hx, 19, ww, 30), CyberHud.Grey, 15, FontWeight.Normal, TextAlignment.Leading,
                    ParagraphAlignment.Center, CyberHud.Mono);
        }
        _p.Fill(new RectangleF(Pad, 58, TW - 2 * Pad, 1), CyberHud.Line);
    }

    void CyberRow(RoomWatcher.Person p, RectangleF r, DateTime now, bool gone, int index)
    {
        bool star = Starred(p);
        float a = gone ? 0.45f : 1;
        _p.FillCut(r, Theme.WithAlpha(CyberHud.Card2, gone ? 0.5f : 1), 0, 0, 8, 0);
        _p.StrokeCut(RectangleF.Inflate(r, -0.6f, -0.6f), Theme.WithAlpha(p.Friend && !gone ? CyberHud.CyanDim : CyberHud.Line, a), 1.25f, 0, 0, 7.7f, 0);
        var sr = new RectangleF(r.X + 4, r.Y, 44, r.Height);
        if (_ui.Clickable(Ui.Id("rm.s", index), sr, out bool hover, out _)) ToggleStar(p);
        _p.Text(star ? "★" : "☆", sr, star ? CyberHud.Yellow : hover ? CyberHud.White : CyberHud.Grey, 22, FontWeight.Normal, TextAlignment.Center);
        if (hover) CyberHud.Brackets(_p, RectangleF.Inflate(sr, -4, -3), CyberHud.Cyan, 6, 0, 1.5f);
        float x = sr.Right + 4;
        string when = Lang.Upper(When(p, now, gone, 1));
        float ww = CyberHud.MonoWidth(_p, when, 13, FontWeight.SemiBold) + 16;
        float nw = r.Right - x - ww - (p.Friend ? 86 : 8);
        string name = Ellipsize(p.Name, nw, 18, FontWeight.SemiBold);
        _p.Text(name, new RectangleF(x, r.Y, nw, r.Height), gone ? CyberHud.Grey : CyberHud.White, 18, FontWeight.SemiBold);
        if (p.Friend)
        {
            float fx = x + MathF.Min(nw, _p.MeasureWidth(name, 18, FontWeight.SemiBold)) + 12;
            var fr = new RectangleF(fx, r.Y + (r.Height - 22) / 2, 70, 22);
            _p.StrokeCut(RectangleF.Inflate(fr, -0.6f, -0.6f), Theme.WithAlpha(CyberHud.Cyan, a), 1.25f, 0, 0, 5, 0);
            _p.Text("FRIEND", fr, Theme.WithAlpha(CyberHud.Cyan, a), 12, FontWeight.Bold, TextAlignment.Center, ParagraphAlignment.Center, CyberHud.Mono);
        }
        _p.Text(when, new RectangleF(r.Right - ww - 4, r.Y, ww, r.Height), CyberHud.Grey, 13, FontWeight.SemiBold, TextAlignment.Trailing,
            ParagraphAlignment.Center, CyberHud.Mono);
    }
}
