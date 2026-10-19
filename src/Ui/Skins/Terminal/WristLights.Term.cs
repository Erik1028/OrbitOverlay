using System;
using System.Drawing;
using Vortice.DirectWrite;
using T = Orbit.TerminalSkin;

namespace Orbit;

// The light and the RGB on the green terminal: a box each, the value in the dot-matrix font. A 1x1 is the switch (a tap): the brightness
// big, ON / OFF under it (the RGB: the effect's short name). Wider: [X] ON (the light) or the effect between [<] and [>] keys that step
// through static colour, rainbow wave, breathing and off (the RGB), the brightness as a bar of #s to tap or drag along; a 2x2 four whites /
// colours as keys under it (the tube has one colour: they are words). Opened: the shared windows in this skin (WristLights95.cs). No app:
// "--" and why.
public sealed partial class WristPanel
{
    // the 2x2's keys: two whites and two colours (the light), four colours (the RGB) - words that fit a key of six letters
    static readonly string[] TLampKeys = { "Warm", "Cool", "Purple", "Blue" }, TRgbColors = { "Red", "Green", "Blue", "Purple" };
    static readonly int[] TLampAt = { 0, 2, 5, 4 }, TRgbAt = { 0, 2, 4, 5 };        // LampWhites (the first two), LampHues; RgbColors
    static readonly string[] THashes = MakeRuns('#'), TDashes = MakeRuns('-');

    static string[] MakeRuns(char ch)
    {
        var a = new string[65];
        for (int i = 0; i < a.Length; i++) a[i] = new string(ch, i);
        return a;
    }

    void TLight(RectangleF r, RectangleF c, WatchWidget w, PanelData d, int id)
    {
        var t = Lamp(d);
        var lamp = d.Lamp;
        if (w.W == 1)
        {
            if (OpenTap(Ui.Id("wlt", id), r, w, out _)) _dirty = true;
            TValue(c, !t.Ready ? "--" : t.On ? t.Pct : Upper("Off"), TLxFit(!t.Ready ? Upper(t.Short) : t.On ? Upper("On") : Upper("Light"), c.Width - 2),
                !t.Ready ? T.DIM : t.On ? T.HOT : T.INK);
            return;
        }
        if (!t.Ready) { TNone(c, Upper(c.Width >= 140 ? t.Msg : t.Short)); return; }
        float top = MathF.Floor(c.Y + (w.H >= 2 ? 4 : (c.Height - 25) / 2));
        bool on = t.On;
        if (_ui.Check95(Ui.Id("wlk", id), c.X + 6, top, "&On", ref on)) { lamp?.SetPower(on); _dirty = true; }
        if (t.On) TCapsR(t.Pct, c.Right - 4, top + 3, T.HOT);
        float v = t.Frac;
        if (TLxBar(Ui.Id("wlb", id), new RectangleF(c.X + 3, top + 14, c.Width - 6, 11), ref v, t.On)) { lamp?.SetBrightness(BrightOf(v)); _dirty = true; }
        if (w.H < 2) return;
        // two whites and two colours as keys, two by two
        float kw = MathF.Floor((c.Width - 6 - 3) / 2), kh = MathF.Min(16, MathF.Floor((c.Bottom - top - 30 - 3) / 2));
        for (int i = 0; i < 4; i++)
        {
            var k = new RectangleF(c.X + 3 + i % 2 * (kw + 3), top + 30 + i / 2 * (kh + 3), kw, kh);
            bool white = i < 2;
            int at = TLampAt[i];
            bool sel = white ? t.WhiteAt == at : t.HueAt == at;
            if (!TKey(Ui.Id("wlq", id * 8 + i), k, Upper(Lang.C("light", TLampKeys[i])), sel, white ? t.S.HasTemp : t.S.HasColor)) continue;
            if (white) lamp?.SetTemp(LampWhites[at]); else lamp?.SetColor(LampHues[at].h, LampHues[at].s);
            _dirty = true;
        }
    }

    void TRgb(RectangleF r, RectangleF c, WatchWidget w, PanelData d, int id)
    {
        var t = Rgb(d);
        var rgb = d.Rgb;
        if (w.W == 1)
        {
            if (OpenTap(Ui.Id("wrt", id), r, w, out _)) _dirty = true;
            TValue(c, !t.Ready ? "--" : t.Off ? Upper("Off") : t.Pct, TLxFit(!t.Ready ? Upper(t.ShortMsg) : Upper(t.Short), c.Width - 2),
                !t.Ready ? T.DIM : t.Off ? T.INK : T.HOT);
            return;
        }
        if (!t.Ready) { TNone(c, Upper(c.Width >= 140 ? t.Msg : t.ShortMsg)); return; }
        float top = MathF.Floor(c.Y + (w.H >= 2 ? 3 : (c.Height - 29) / 2));
        // the effect between the keys that step it (the four quick ones; one of Prisma's others: < goes to the first)
        var kl = new RectangleF(c.X + 3, top, 15, 15);
        var kr = new RectangleF(c.Right - 18, top, 15, 15);
        int q = t.QuickAt;
        if (TKey(Ui.Id("wrl", id), kl, "<")) { rgb?.SetEffect(RgbState.Quick[q < 0 ? 0 : (q + 3) % 4]); _dirty = true; }
        if (TKey(Ui.Id("wrr", id), kr, ">")) { rgb?.SetEffect(RgbState.Quick[(q + 1) % 4]); _dirty = true; }
        TCapsC(TLxFit(Upper(t.Short), c.Width - 44), c, top + 4, t.Off ? T.INK : T.HOT);
        float v = t.Frac;
        if (TLxBar(Ui.Id("wrb", id), new RectangleF(c.X + 3, top + 18, c.Width - 6, 11), ref v, !t.Off)) { rgb?.SetBrightness(BrightOf(v)); _dirty = true; }
        if (w.H < 2) return;
        // the effect by name (its shorter name where the whole does not fit: SZIVÁRVÁNY, not SZIVÁRVÁNYHUL...), four colours as keys
        string nm = Upper(t.Name);
        if (t.NameShort != null && T.W(nm) > c.Width - 8) nm = Upper(t.NameShort);
        TCaps(TLxFit(nm, c.Width - 8), c.X + 4, top + 36, T.MID);
        float kw = MathF.Floor((c.Width - 6 - 3) / 2), kh = MathF.Min(16, MathF.Floor((c.Bottom - top - 50 - 3) / 2));
        for (int i = 0; i < 4; i++)
        {
            var k = new RectangleF(c.X + 3 + i % 2 * (kw + 3), top + 48 + i / 2 * (kh + 3), kw, kh);
            int ci = TRgbAt[i];
            if (!TKey(Ui.Id("wrc", id * 8 + i), k, Upper(TRgbColors[i]), t.ColorAt == ci)) continue;
            rgb?.SetColor(RgbColors[ci]);
            _dirty = true;
        }
    }

    // the brightness as [#####-----] across r, the % after it: a tap or a drag along it sets it (true, with the new value 0..1)
    bool TLxBar(string id, RectangleF r, ref float v, bool on)
    {
        _ui.Clickable(id, r, out bool hover, out bool held);
        bool changed = false;
        float x0 = r.X + 6, span = r.Width - 12;
        if (held && span > 0)
        {
            _ui.DragHeld = true;
            float nv = Math.Clamp((_ui.Mouse.X - x0) / span, 0, 1);
            if (MathF.Abs(nv - v) > 0.004f) { v = nv; changed = true; }
        }
        int cells = Math.Clamp((int)(span / 6), 1, THashes.Length - 1);
        int lit = on ? (int)MathF.Round(Math.Clamp(v, 0, 1) * cells) : 0;
        if (on && lit == 0 && v > 0.005f) lit = 1;
        float capTop = r.Y + 2;
        TCaps("[", r.X, capTop, hover || held ? T.INK : T.DIM);
        if (lit > 0) TCaps(THashes[lit], x0, capTop, held ? T.HOT : T.INK);
        if (cells > lit) TCaps(TDashes[cells - lit], x0 + lit * 6, capTop, T.DIM);
        TCaps("]", x0 + cells * 6, capTop, hover || held ? T.INK : T.DIM);
        return changed;
    }

    // no app: -- and why, in the middle of the box
    void TNone(RectangleF c, string why)
    {
        float top = MathF.Floor(c.Y + (c.Height - 18) / 2);
        TCapsC("--", c, top, T.DIM);
        TCapsC(TLxFit(why, c.Width - 6), c, top + 11, T.MID);
    }

    // TFit, kept with the lights' other texts (a cut made every frame was garbage every frame)
    string TLxFit(string s, float w, int n = 1)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, (float)n, -3);
        if (_lxFits.TryGetValue(key, out var done)) return done;
        if (_lxFits.Count > 256) _lxFits.Clear();
        return _lxFits[key] = TFit(s, w, n);
    }
}
