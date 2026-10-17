using System;
using System.Drawing;
using T = Orbit.TerminalSkin;

namespace Orbit;

// The avatar toggles on the green terminal: a box, a line per button in the dot-matrix capitals - [X] HOODIE for a toggle, the name
// and [####----] to tap or drag along for a slider, the name with [<] 3 [>] for a stepper; "+3 MORE" when they do not all fit (a tap
// on the box opens it). A 1x1 is its first button: ON / OFF big, its name under it (a tap switches it). Opened: the shared window in
// this skin (WristAvatar95.cs). No VRChat, no config: "--" and why.
public sealed partial class WristPanel
{
    // TFit, kept with the avatar's other texts (a cut made every frame was garbage every frame)
    string AvFitT(string s, float w)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, 7f, -4);
        if (_avFits.TryGetValue(key, out var done)) return done;
        if (_avFits.Count > 512) _avFits.Clear();
        return _avFits[key] = TFit(s, w);
    }

    // a check box's label: cut, a real '&' of the name kept as one
    string AvLabelT(string s, float w)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, 7f, -5);
        if (_avFits.TryGetValue(key, out var done)) return done;
        if (_avFits.Count > 512) _avFits.Clear();
        return _avFits[key] = Ui.Esc95(TFit(s, w));
    }

    void TAvatar(RectangleF r, RectangleF c, WatchWidget w, PanelData d, int id)
    {
        AvNow(d);
        var link = d.Avatar;
        if (!AvReady) { TNone(c, Upper(c.Width >= 140 ? _avMsg : _avShort)); return; }
        var s = _avS;
        if (w.W == 1 && w.H == 1)
        {
            int b0 = _avShown[0];
            var p0 = s.Avatar.Buttons[b0];
            float v0 = s.Values[b0];
            bool known0 = !float.IsNaN(v0), on0 = known0 && v0 >= 0.5f;
            if (p0.Kind == AvatarKind.Toggle && _ui.Clickable(Ui.Id("wav1", id), r, out _, out _)) { link?.Toggle(b0); _dirty = true; }
            string value = !known0 ? "--" : p0.Kind == AvatarKind.Toggle ? Upper(on0 ? "On" : "Off") : Value95Of(p0, v0);
            TValue(c, value, AvFitT(Upper(p0.Label), c.Width - 2), !known0 ? T.DIM : p0.Kind != AvatarKind.Toggle || on0 ? T.HOT : T.INK);
            return;
        }
        // a line each (two columns on a wide box: the 4 x 3 page's Wide)
        const float rh = 14;
        int cols = c.Width >= 240 ? 2 : 1, rows = Math.Max(1, (int)((c.Height - 4) / rh)), n = _avShown.Count, room = rows * cols;
        int fit = n <= room ? n : rows >= 4 ? room - 1 : room;
        float top = c.Y + 3, cw = MathF.Floor((c.Width - 6 - (cols - 1) * 12) / cols);
        for (int i = 0; i < fit; i++)
        {
            int bi = _avShown[i];
            TAvRow(Ui.Id("wavt", id * 64 + i), new RectangleF(c.X + 3 + i / rows * (cw + 12), top + i % rows * rh, cw, rh - 1), s.Avatar.Buttons[bi], s.Values[bi], bi, link);
        }
        if (fit < n && fit < room) TCaps(AvFitT(Upper(MoreText(n - fit)), cw - 2), c.X + 5 + fit / rows * (cw + 12), top + fit % rows * rh + 3, T.DIM);
    }

    void TAvRow(string key, RectangleF row, AvatarParam p, float v, int bi, AvatarLink link)
    {
        bool known = !float.IsNaN(v);
        if (p.Kind == AvatarKind.Toggle)
        {
            bool on = known && v >= 0.5f;
            if (_ui.Check95(key, row.X + 2, row.Y, AvLabelT(Upper(p.Label), row.Width - 24), ref on)) { link?.Toggle(bi); _dirty = true; }
            return;
        }
        if (p.Kind == AvatarKind.Slider)
        {
            float half = MathF.Floor(row.Width * 0.45f);
            TCaps(AvFitT(Upper(p.Label), half - 6), row.X + 2, row.Y + 3, T.INK);
            float val = known ? Math.Clamp(v, 0, 1) : 0;
            if (TLxBar(key, new RectangleF(row.X + half, row.Y + 1, row.Width - half, 11), ref val, known)) { link?.Set(bi, val); _dirty = true; }
            return;
        }
        // the name, then [<] 3 [>] at the right
        string num = known ? IntOf(v) : "--";
        var up = new RectangleF(row.Right - 13, row.Y, 13, 13);
        var dn = new RectangleF(up.X - 4 - T.W("000") - 4 - 13, row.Y, 13, 13);
        TCaps(AvFitT(Upper(p.Label), dn.X - row.X - 6), row.X + 2, row.Y + 3, T.INK);
        if (TKey(Ui.Id(key, "-"), dn, "<")) { link?.Step(bi, -1); _dirty = true; }
        if (TKey(Ui.Id(key, "+"), up, ">")) { link?.Step(bi, +1); _dirty = true; }
        TCapsR(num, up.X - 6, row.Y + 3, known ? T.HOT : T.DIM);
    }
}
