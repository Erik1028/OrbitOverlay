using System;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// The avatar toggles in the retro styles that draw the shared 95 windows (95, XP, 7, the Mac; the Terminal has its own face:
// Skins/Terminal/WristAvatar.Term.cs, and this window opened in its skin). Made of the era's parts, each style's skin drawing them: a
// little window with a row per button - a check box for a toggle, a value with - / + push buttons for a stepper, a value for a slider
// (the window opens for its trackbar); a 1x1 is its first button as a push button that stays in while it is on. Opened, a dialog: every
// button of the avatar in a list (a check box, its name, its value), the one picked with its control at the right (the check box, the
// - / + buttons, a trackbar); where the values are from in the button row beside OK.
public sealed partial class WristPanel
{
    const string AvatarList95 = "wav95list";
    int _av95Sel = -1;

    // a text cut to w in the 95 font, kept with the avatar's other texts
    string AvFit95(string s, float w, bool bold = false)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, Ui.F95, bold ? -1 : -2);
        if (_avFits.TryGetValue(key, out var done)) return done;
        if (_avFits.Count > 512) _avFits.Clear();
        return _avFits[key] = Fit(s, w, Ui.F95, bold);
    }

    // the same for a check box's or a push button's label: a real '&' of the name kept as one (no access key)
    string AvLabel95(string s, float w)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, Ui.F95, -3);
        if (_avFits.TryGetValue(key, out var done)) return done;
        if (_avFits.Count > 512) _avFits.Clear();
        return _avFits[key] = Ui.Esc95(Fit(s, w, Ui.F95, false));
    }

    // a value as the 95 rows write it: a slider's per cent, a stepper's number ("--": not known)
    static string Value95Of(AvatarParam p, float v) =>
        float.IsNaN(v) ? "--" : p.Kind == AvatarKind.Slider ? Pct((int)MathF.Round(Math.Clamp(v, 0, 1) * 100)) : IntOf(v);

    // c: the window's inside (the style's frame is drawn)
    void Avatar95(RectangleF r, RectangleF c, WatchWidget w, PanelData d, int id)
    {
        AvNow(d);
        var link = d.Avatar;
        if (!AvReady)
        {
            bool two = c.Height >= 30;
            float y = MathF.Floor(c.Y + (c.Height - (two ? 28 : 13)) / 2);
            _ui.Text95("--", new RectangleF(c.X, y, c.Width, 13), Gry95, true, TextAlignment.Center);
            if (two) _ui.Text95(AvFit95(c.Width >= 120 ? _avMsg : _avShort, c.Width - 4), new RectangleF(c.X, y + 15, c.Width, 13), Blk95, false, TextAlignment.Center);
            return;
        }
        var s = _avS;
        if (w.W == 1 && w.H == 1)
        {
            // its first button: a toggle its name over a push button that stays in while it is on; another kind its value and name
            int b0 = _avShown[0];
            var p0 = s.Avatar.Buttons[b0];
            float v0 = s.Values[b0];
            if (p0.Kind == AvatarKind.Toggle)
            {
                bool on0 = !float.IsNaN(v0) && v0 >= 0.5f;
                float ty = MathF.Floor(c.Y + (c.Height - 13 - 2 - 23) / 2);
                _ui.Text95(AvFit95(p0.Label, c.Width - 2), new RectangleF(c.X, ty, c.Width, 13), Blk95, false, TextAlignment.Center);
                var br = new RectangleF(c.X + 2, ty + 15, c.Width - 4, 23);
                if (_ui.Button95(Ui.Id("wav95b", id), br, on0 ? "On" : "Off", true, false, on0)) { link?.Toggle(b0); _dirty = true; }
                return;
            }
            Value95(c, Value95Of(p0, v0), AvFit95(p0.Label, c.Width - 2), Blk95);
            return;
        }
        // a row each; a tall one that cannot show all of them says "+3 more" on its last row (a tap on the window opens it)
        const float rh = 16;
        int rows = Math.Max(1, (int)((c.Height - 2) / rh)), n = _avShown.Count;
        int fit = n <= rows ? n : rows >= 4 ? rows - 1 : rows;
        float cw = c.Width - 4, top = n > rows ? c.Y + 2 : MathF.Floor(c.Y + 2 + (c.Height - 2 - n * rh) / 2);
        for (int i = 0; i < fit; i++)
        {
            int bi = _avShown[i];
            AvRow95(Ui.Id("wav95", id * 64 + i), new RectangleF(c.X + 2, top + i * rh, cw, rh), s.Avatar.Buttons[bi], s.Values[bi], bi, link);
        }
        if (fit < n && fit < rows) _ui.Text95(AvFit95(MoreText(n - fit), cw), new RectangleF(c.X + 4, top + fit * rh + 1, cw, 13), Gry95);
    }

    // a button's row: a check box (a toggle), its name with the value and - / + (a stepper), its name and value (a slider: the window has
    // its trackbar)
    void AvRow95(string key, RectangleF row, AvatarParam p, float v, int bi, AvatarLink link)
    {
        bool known = !float.IsNaN(v);
        if (p.Kind == AvatarKind.Toggle)
        {
            bool on = known && v >= 0.5f;
            if (_ui.Check95(key, row.X + 1, row.Y + 1, AvLabel95(p.Label, row.Width - 24), ref on)) { link?.Toggle(bi); _dirty = true; }
            return;
        }
        string val = Value95Of(p, v);
        float vw = _ui.Width95(val) + 4, bw = p.Kind == AvatarKind.Stepper ? 2 * 15 + 2 : 0;
        float lw = row.Width - vw - bw - 9;
        _ui.Text95(AvFit95(p.Label, lw), new RectangleF(row.X + 2, row.Y + 1, lw, 13), Blk95);
        _ui.Text95(val, new RectangleF(row.Right - bw - vw - 4, row.Y + 1, vw, 13), known ? Blk95 : Gry95, false, TextAlignment.Trailing);
        if (p.Kind != AvatarKind.Stepper) return;
        if (_ui.Button95(Ui.Id(key, "-"), new RectangleF(row.Right - 31, row.Y, 15, 15), "-")) { link?.Step(bi, -1); _dirty = true; }
        if (_ui.Button95(Ui.Id(key, "+"), new RectangleF(row.Right - 15, row.Y, 15, 15), "+")) { link?.Step(bi, +1); _dirty = true; }
    }

    // ---- opened, in the window the style draws around it (title, X, OK): c is its inside, the OK row under it ----

    AvatarParam[] _av95Params = Array.Empty<AvatarParam>();
    float[] _av95Values = Array.Empty<float>();
    int[] _av95Order = Array.Empty<int>();
    Action<int, RectangleF, bool> _avRow95;

    void Avatar95Detail(RectangleF c, PanelData d)
    {
        AvNow(d);
        AvFromTop();
        var s = _avS;
        var link = d.Avatar;
        float sw = c.Right - 75 - 8 - c.X;
        _ui.Text95(AvFit95(_avStatus, sw), new RectangleF(c.X, c.Bottom + 10, sw, 13), s.Ready && !s.Live ? 0xFF800000 : Blk95);
        if (!s.Ready || s.Avatar.Buttons.Length == 0) { None95Detail(c, _avMsg ?? "Nothing to toggle", _avHint); return; }
        var a = s.Avatar;
        (_av95Params, _av95Values, _av95Order) = (a.Buttons, s.Values, a.Order);
        int n = a.Order.Length;
        if (_av95Sel < 0 || _av95Sel >= n) _av95Sel = 0;
        // the list: the avatar's name over it, a row per button
        const float pw = 124;
        var lr = new RectangleF(c.X, c.Y + 16, c.Width - pw - 8, c.Height - 16);
        _ui.Text95(AvFit95(_avName, lr.Width, true), new RectangleF(c.X, c.Y, lr.Width, 13), Blk95, true);
        _avRow95 ??= AvListRow95;
        int hit = _ui.List95(AvatarList95, lr, n, _av95Sel, _avRow95);
        _avTiles = Math.Min(n, (int)((lr.Height - 4) / 16));
        if (hit >= 0)
        {
            _av95Sel = hit;
            int hb = a.Order[hit];
            if (a.Buttons[hb].Kind == AvatarKind.Toggle) link?.Toggle(hb);      // a toggle's row switches it too (its check box)
            _dirty = true;
        }
        // the one picked: its control
        int bi = a.Order[_av95Sel];
        var p = a.Buttons[bi];
        float v = s.Values[bi];
        bool known = !float.IsNaN(v);
        var g = new RectangleF(lr.Right + 8, c.Y, pw, c.Height);
        _ui.Group95(g, p.Kind switch { AvatarKind.Toggle => "Toggle", AvatarKind.Stepper => "Number", _ => "Slider" });
        float x = g.X + 10, w = g.Width - 20, y = g.Y + 20;
        _ui.Text95(AvFit95(p.Label, w, true), new RectangleF(x, y, w, 13), Blk95, true);
        y += 22;
        switch (p.Kind)
        {
            case AvatarKind.Toggle:
                bool on = known && v >= 0.5f;
                if (_ui.Check95("wav95on", x, y + 2, "&On", ref on)) { link?.Toggle(bi); _dirty = true; }
                break;
            case AvatarKind.Stepper:
                _ui.Text95(Value95Of(p, v), new RectangleF(x, y + 4, 40, 13), known ? Blk95 : Gry95, true);
                if (_ui.Button95("wav95dn", new RectangleF(x + w - 54, y, 26, 23), "-")) { link?.Step(bi, -1); _dirty = true; }
                if (_ui.Button95("wav95up", new RectangleF(x + w - 26, y, 26, 23), "+")) { link?.Step(bi, +1); _dirty = true; }
                break;
            default:
                float tv = known ? Math.Clamp(v, 0, 1) : 0;
                if (_ui.Track95("wav95tb", new RectangleF(g.X + 4, y, g.Width - 8, 30), ref tv, 0, 1, true, 11)) { link?.Set(bi, tv); _dirty = true; }
                _ui.Text95(Value95Of(p, known ? tv : v), new RectangleF(x, y + 32, w, 13), known ? Blk95 : Gry95, false, TextAlignment.Center);
                break;
        }
    }

    // a row of the opened list (a method of its own, kept as one delegate: a lambda here was made every frame)
    void AvListRow95(int i, RectangleF r, bool sel)
    {
        int bi = _av95Order[i];
        var p = _av95Params[bi];
        float v = bi < _av95Values.Length ? _av95Values[bi] : float.NaN;
        bool known = !float.IsNaN(v);
        uint ink = sel ? Wht95 : Blk95;
        float lx = r.X + 3;
        if (p.Kind == AvatarKind.Toggle)
        {
            Theme.RetroSkin.CheckBox(_p, r.X + 2, r.Y + 1, "", known && v >= 0.5f, known, false, false);
            lx = r.X + 20;
        }
        string val = p.Kind == AvatarKind.Toggle ? null : Value95Of(p, v);
        float vw = val == null ? 0 : _ui.Width95(val) + 6;
        _ui.Text95(AvFit95(p.Label, r.Right - lx - vw - 4), new RectangleF(lx, r.Y + 1, r.Right - lx - vw - 2, 13), ink);
        if (val != null) _ui.Text95(val, new RectangleF(r.Right - vw - 2, r.Y + 1, vw, 13), sel ? Wht95 : known ? Blk95 : Gry95, false, TextAlignment.Trailing);
    }
}
