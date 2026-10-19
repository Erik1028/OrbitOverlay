using System;
using System.Collections.Generic;
using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Orbit;

// The light and the RGB in the retro styles that draw the shared 95 windows (95, XP, 7, the Mac; the Terminal has its own faces:
// Skins/Terminal/WristLights.Term.cs, and these windows opened in its skin). Made of the era's parts, each style's skin drawing them: a
// little window with a 16 / 32 px picture (pixel art in the VGA colours; XP's and 7's smooth), an "On" check box and a trackbar for the
// brightness, the quick choices as toolbar buttons or the Color dialog's boxes. A 1x1 is the switch itself (a tap). Opened, a dialog:
// the bulb's group with its picture and Turn on / off, the brightness, the whites as push buttons and the colours as boxes (the light);
// the effects as option buttons, the brightness, the colours (the RGB); where it is asked in the button row beside OK.
public sealed partial class WristPanel
{
    // ---- the pictures ----

    // the bulb, lit (y) and dark (s); the colour wheel is worked out (Wheel95)
    static readonly string[] Bulb95 =
    {
        "......kkkk......",
        "....kkyyyykk....",
        "...kyywwyyyyk...",
        "..kyywwyyyyyyk..",
        "..kywwyyyyyyyk..",
        "..kyyyyyyyyyyk..",
        "..kyyyyyoyyyyk..",
        "...kyyyoyoyyk...",
        "....kyyoyoyk....",
        ".....kyyoyk.....",
        ".....kssssk.....",
        ".....kggggk.....",
        ".....kssssk.....",
        ".....kggggk.....",
        "......kkkk......",
        "................",
    };

    static uint Vga95(char c, bool lit) => c switch
    {
        'k' => 0xFF000000, 'w' => 0xFFFFFFFF, 's' => 0xFFC0C0C0, 'g' => 0xFF808080, 'o' => lit ? 0xFF808000 : 0xFF808080,
        'y' => lit ? 0xFFFFFF00 : 0xFFC0C0C0, _ => 0,
    };

    readonly Dictionary<int, ID2D1Bitmap> _lx95 = new();

    // a picture at x, y: 16 or 32 px (32: every pixel doubled, as 1995 showed a small icon big); XP / 7: their smooth glyph
    void LampPic95(bool rgb, bool lit, float x, float y, int size)
    {
        if (Theme.Retro is RetroStyle.XP or RetroStyle.Aero)
        {
            if (rgb) { EffectWheel(x + size / 2f, y + size / 2f, size * 0.42f, !lit); return; }
            uint c = lit ? 0xFFF5C518 : 0xFF8A8A8A;
            if (lit) _p.Circle(x + size / 2f, y + size * 0.42f, size * 0.36f, 0x40FFE680);
            _p.Icon(BulbGlyph, new RectangleF(x, y, size, size), c, size * 0.86f);
            return;
        }
        int key = (rgb ? 1 : 0) * 4 + (lit ? 2 : 0) + (size >= 32 ? 1 : 0);
        if (!_lx95.TryGetValue(key, out var bmp)) _lx95[key] = bmp = _p.CreateBitmap(Pixels95(rgb, lit, size >= 32 ? 32 : 16), size >= 32 ? 32 : 16, size >= 32 ? 32 : 16);
        _p.ImageNearest(bmp, new RectangleF(MathF.Round(x), MathF.Round(y), size >= 32 ? 32 : 16, size >= 32 ? 32 : 16));
    }

    static uint[] Pixels95(bool rgb, bool lit, int n)
    {
        var px = new uint[n * n];
        int k = n / 16;
        if (!rgb)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) px[y * n + x] = Vga95(Bulb95[y / k][x / k], lit);
            return px;
        }
        // a ring of the six bright VGA colours round a black hub, outlined in black (grey when off)
        uint[] six = { 0xFFFF0000, 0xFFFFFF00, 0xFF00FF00, 0xFF00FFFF, 0xFF0000FF, 0xFFFF00FF };
        float c = n / 2f, ro = n * 0.47f, ri = n * 0.2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c, d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > ro) continue;
                if (d > ro - k || d < ri + k) { px[y * n + x] = d < ri ? 0 : 0xFF000000; continue; }
                if (d < ri) continue;
                float a = (MathF.Atan2(dx, -dy) / MathF.Tau + 1) % 1;
                px[y * n + x] = lit ? six[(int)(a * 6) % 6] : ((int)(a * 6) % 2 == 0 ? 0xFFC0C0C0 : 0xFF808080);
            }
        return px;
    }

    // XP / 7: the wheel smooth (six dots of every colour)
    void EffectWheel(float cx, float cy, float rad, bool dim) => EffectPic("RainbowWave", cx, cy, rad, 0, dim);

    // a text cut to w in the 95 font, kept with the lights' other texts
    string LxFit95(string s, float w, bool bold = false)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, Ui.F95, bold ? -1 : -2);
        if (_lxFits.TryGetValue(key, out var done)) return done;
        if (_lxFits.Count > 256) _lxFits.Clear();
        return _lxFits[key] = Fit(s, w, Ui.F95, bold);
    }

    // ---- the faces (c: the window's inside, the style's frame drawn) ----

    void Light95(RectangleF r, RectangleF c, WatchWidget w, PanelData d, int id)
    {
        var t = Lamp(d);
        var lamp = d.Lamp;
        if (w.W == 1)
        {
            // the switch: the whole little window
            if (_ui.Clickable(Ui.Id("wlt", id), r, out _, out _)) { lamp?.Toggle(); _dirty = true; }
            int s = c.Height >= 56 ? 32 : 16;
            float y = MathF.Floor(c.Y + (c.Height - s - 15) / 2);
            LampPic95(false, t.On, MathF.Floor(c.X + (c.Width - s) / 2), y, s);
            _ui.Text95(LxFit95(t.Ready ? t.Value : "--", c.Width - 2, true), new RectangleF(c.X, y + s + 1, c.Width, 14), t.Ready ? Blk95 : Gry95, true,
                TextAlignment.Center);
            return;
        }
        if (!t.Ready)
        {
            bool two = c.Height >= 30;
            float y = MathF.Floor(c.Y + (c.Height - (two ? 28 : 13)) / 2);
            _ui.Text95("--", new RectangleF(c.X, y, c.Width, 13), Gry95, true, TextAlignment.Center);
            if (two) _ui.Text95(LxFit95(c.Width >= 120 ? t.Msg : t.Short, c.Width - 4), new RectangleF(c.X, y + 15, c.Width, 13), Blk95, false, TextAlignment.Center);
            return;
        }
        bool tall = c.Height >= 70;
        float x = c.X + 2, top = c.Y + 2;
        if (tall && c.Width >= 120)
        {
            // a big one: the bulb at 32 px with its name and state beside it
            LampPic95(false, t.On, x, top, 32);
            _ui.Text95(LxFit95(t.Name, c.Width - 42, true), new RectangleF(x + 38, top + 2, c.Width - 42, 13), Blk95, true);
            _ui.Text95(LxFit95(t.State, c.Width - 42), new RectangleF(x + 38, top + 17, c.Width - 42, 13), Gry95);
            top += 36;
        }
        else LampPic95(false, t.On, c.Right - 18, top, 16);
        bool on = t.On;
        if (_ui.Check95(Ui.Id("wlk", id), x + 1, top + 1, "&On", ref on)) { lamp?.SetPower(on); _dirty = true; }
        if (!tall || c.Width < 120) _ui.Text95(t.On ? t.Pct : "", new RectangleF(x + 46, top, c.Right - 20 - x - 46, 13), Gry95, false, TextAlignment.Trailing);
        float v = t.Frac;
        float th = MathF.Min(26, c.Bottom - (top + 18) - (tall ? 22 : 0));
        if (_ui.Track95(Ui.Id("wlb", id), new RectangleF(c.X, top + 18, c.Width, th), ref v, 0, 1, t.Ready, 0)) { lamp?.SetBrightness(BrightOf(v)); _dirty = true; }
        if (!tall) return;
        // the quick whites and colours as the Color dialog's boxes
        int n = c.Width >= 120 ? 6 : 4;
        float bw = MathF.Floor((c.Width - 4 - (n - 1) * 3) / n), by = c.Bottom - 18;
        for (int i = 0; i < n; i++)
        {
            bool white = i < LampWhites.Length;
            int hi = white ? -1 : QuickHue(i - LampWhites.Length);
            uint col = white ? LampState.Kelvin(LampWhites[i]) : Theme.Hsv(LampHues[hi].h, LampHues[hi].s / 100f, 1);
            bool sel = white ? t.WhiteAt == i : t.HueAt == hi;
            if (!ColorBox95(Ui.Id("wlq", id * 8 + i), new RectangleF(c.X + 2 + i * (bw + 3), by, bw, 16), col, sel, white ? t.S.HasTemp : t.S.HasColor)) continue;
            if (white) lamp?.SetTemp(LampWhites[i]); else lamp?.SetColor(LampHues[hi].h, LampHues[hi].s);
            _dirty = true;
        }
    }

    void Rgb95(RectangleF r, RectangleF c, WatchWidget w, PanelData d, int id)
    {
        var t = Rgb(d);
        var rgb = d.Rgb;
        if (w.W == 1)
        {
            if (_ui.Clickable(Ui.Id("wrt", id), r, out _, out _)) { rgb?.Toggle(); _dirty = true; }
            int s = c.Height >= 56 ? 32 : 16;
            float y = MathF.Floor(c.Y + (c.Height - s - 15) / 2);
            LampPic95(true, t.Ready && !t.Off, MathF.Floor(c.X + (c.Width - s) / 2), y, s);
            _ui.Text95(LxFit95(t.Ready ? t.Short : "--", c.Width - 2, true), new RectangleF(c.X, y + s + 1, c.Width, 14), t.Ready ? Blk95 : Gry95, true,
                TextAlignment.Center);
            return;
        }
        if (!t.Ready)
        {
            bool two = c.Height >= 30;
            float y = MathF.Floor(c.Y + (c.Height - (two ? 28 : 13)) / 2);
            _ui.Text95("--", new RectangleF(c.X, y, c.Width, 13), Gry95, true, TextAlignment.Center);
            if (two) _ui.Text95(LxFit95(c.Width >= 120 ? t.Msg : t.ShortMsg, c.Width - 4), new RectangleF(c.X, y + 15, c.Width, 13), Blk95, false, TextAlignment.Center);
            return;
        }
        bool tall = c.Height >= 70;
        float top = c.Y + 1;
        if (tall)
        {
            // the effect running, by name (its shorter name where the whole does not fit: "Szivárvány", not "Szivárványhu...")
            string nm = t.NameShort != null && _ui.Width95(t.Name, true) > c.Width - 4 ? t.NameShort : t.Name;
            _ui.Text95(LxFit95(nm, c.Width - 4, true), new RectangleF(c.X + 2, top, c.Width - 4, 13), Blk95, true);
            top += 16;
        }
        // the four effects as toolbar buttons, the one running pressed in
        float bw = MathF.Floor((c.Width - 2) / 4);
        for (int i = 0; i < 4; i++)
        {
            var b = new RectangleF(c.X + 1 + i * bw, top, bw, 22);
            if (EffectButton95(Ui.Id("wrq", id * 4 + i), b, RgbState.Quick[i], t.QuickAt == i, t.Color)) { rgb?.SetEffect(RgbState.Quick[i]); _dirty = true; }
        }
        float v = t.Frac;
        float th = MathF.Min(26, c.Bottom - (top + 23) - (tall ? 20 : 0));
        if (_ui.Track95(Ui.Id("wrb", id), new RectangleF(c.X, top + 23, c.Width, th), ref v, 0, 1, !t.Off, 0)) { rgb?.SetBrightness(BrightOf(v)); _dirty = true; }
        if (!tall) return;
        int n = c.Width >= 120 ? 6 : 4;
        float cw = MathF.Floor((c.Width - 4 - (n - 1) * 3) / n), by = c.Bottom - 18;
        for (int i = 0; i < n; i++)
        {
            int ci = n == 6 ? i : QuickRgb(i);
            if (!ColorBox95(Ui.Id("wrc", id * 8 + i), new RectangleF(c.X + 2 + i * (cw + 3), by, cw, 16), RgbColors[ci], t.ColorAt == ci, true)) continue;
            rgb?.SetColor(RgbColors[ci]);
            _dirty = true;
        }
    }

    // a box of the Color dialog: the colour in a sunken frame; the one picked with a black frame round it (and the focus's dots)
    bool ColorBox95(string id, RectangleF r, uint col, bool sel, bool enabled)
    {
        bool click = _ui.Clickable(id, r, out _, out _) && enabled;
        var f = new RectangleF(MathF.Round(r.X), MathF.Round(r.Y), MathF.Round(r.Width), MathF.Round(r.Height));
        _p.Bevel(f, Painter.Edge.Sunken);
        _p.Fill(RectangleF.Inflate(f, -2, -2), enabled ? col : Theme.Mix(col, Theme.W95Face, 0.6f));
        if (sel)
        {
            _p.Stroke(RectangleF.Inflate(f, 1, 1), 0xFF000000, 1);
            _p.Stroke(RectangleF.Inflate(f, -3, -3), 0xFFFFFFFF, 1);
        }
        return click;
    }

    // a toolbar button with an effect's picture (16 px, pixel for pixel: a box of the colour, a rainbow's stripes, breathing's boxes in
    // boxes, off's power sign); latched: the one running
    bool EffectButton95(string id, RectangleF r, string effect, bool on, uint col)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held);
        var f = _p.ButtonFace95(r, false, held, on, hover);
        float x = MathF.Floor(f.X + (f.Width - 12) / 2) + (held || on ? 1 : 0), y = MathF.Floor(f.Y + (f.Height - 12) / 2) + (held || on ? 1 : 0);
        switch (effect)
        {
            case "Off":
                _p.Fill(new RectangleF(x + 5, y, 2, 6), 0xFF000000);
                _p.Fill(new RectangleF(x + 2, y + 2, 1, 6), 0xFF000000); _p.Fill(new RectangleF(x + 9, y + 2, 1, 6), 0xFF000000);
                _p.Fill(new RectangleF(x + 3, y + 8, 6, 1), 0xFF000000); _p.Fill(new RectangleF(x + 3, y + 1, 1, 1), 0xFF000000);
                _p.Fill(new RectangleF(x + 8, y + 1, 1, 1), 0xFF000000); _p.Fill(new RectangleF(x + 3, y + 10, 6, 1), 0xFF000000);
                _p.Fill(new RectangleF(x + 2, y + 9, 1, 1), 0xFF000000); _p.Fill(new RectangleF(x + 9, y + 9, 1, 1), 0xFF000000);
                break;
            case "RainbowWave":
                _p.Fill(new RectangleF(x, y, 12, 12), 0xFF000000);
                for (int i = 0; i < 5; i++) _p.Fill(new RectangleF(x + 1 + i * 2, y + 1, 2, 10), i switch { 0 => 0xFFFF0000, 1 => 0xFFFFFF00, 2 => 0xFF00FF00, 3 => 0xFF00FFFF, _ => 0xFF0000FF });
                break;
            case "Breathing":
                _p.Fill(new RectangleF(x, y, 12, 12), 0xFF000000);
                _p.Fill(new RectangleF(x + 1, y + 1, 10, 10), Theme.Mix(col, 0xFFFFFFFF, 0.55f));
                _p.Fill(new RectangleF(x + 3, y + 3, 6, 6), 0xFF000000);
                _p.Fill(new RectangleF(x + 4, y + 4, 4, 4), col);
                break;
            default:
                _p.Fill(new RectangleF(x, y, 12, 12), 0xFF000000);
                _p.Fill(new RectangleF(x + 1, y + 1, 10, 10), col);
                break;
        }
        return click;
    }

    // ---- opened, in the window the style draws around it (title, X, OK): c is its inside, the OK row under it ----

    // the bulb's group (its picture, its name, on / off, Turn on / off), the brightness, the whites, the colours; where it is asked in the
    // button row
    void Light95Detail(RectangleF c, PanelData d)
    {
        var t = Lamp(d);
        var lamp = d.Lamp;
        float sw = c.Right - 75 - 8 - c.X;
        _ui.Text95(LxFit95(t.Ready ? t.Status : t.Msg, sw), new RectangleF(c.X, c.Bottom + 10, sw, 13), t.Ready ? Blk95 : 0xFF800000);
        if (!t.Ready) { None95Detail(c, t.Msg, t.Hint); return; }
        var g = new RectangleF(c.X, c.Y, 118, c.Height);
        _ui.Group95(g, "Bulb");
        LampPic95(false, t.On, MathF.Floor(g.X + (g.Width - 32) / 2), g.Y + 18, 32);
        _ui.Text95(LxFit95(t.Name, g.Width - 12, true), new RectangleF(g.X + 6, g.Y + 54, g.Width - 12, 13), Blk95, true, TextAlignment.Center);
        _ui.Text95(LxFit95(t.State, g.Width - 12), new RectangleF(g.X + 6, g.Y + 68, g.Width - 12, 13), Gry95, false, TextAlignment.Center);
        if (_ui.Button95("wd95lp", new RectangleF(g.X + 9, g.Bottom - 31, g.Width - 18, 23), t.On ? "Turn o&ff" : "Turn &on")) { lamp?.Toggle(); _dirty = true; }
        float x = g.Right + 10, w = c.Right - x, y = c.Y;
        _ui.Label95("&Brightness:", new RectangleF(x, y + 2, w - 40, 13));
        _ui.Text95(t.On ? t.Pct : "", new RectangleF(x, y + 2, w, 13), Blk95, false, TextAlignment.Trailing);
        float v = t.Frac;
        if (_ui.Track95("wd95lb", new RectangleF(x, y + 16, w, 30), ref v, 0, 1, true, 11)) { lamp?.SetBrightness(BrightOf(v)); _dirty = true; }
        y += 52;
        _ui.Label95("White:", new RectangleF(x, y, w, 13), t.S.HasTemp);
        float bw = MathF.Floor((w - 8) / 3);
        for (int i = 0; i < 3; i++)
            if (_ui.Button95(Ui.Id("wd95lw", i), new RectangleF(x + i * (bw + 4), y + 14, bw, 23), LampWhiteNames95[i], t.S.HasTemp, false, t.WhiteAt == i))
            { lamp?.SetTemp(LampWhites[i]); _dirty = true; }
        y += 46;
        _ui.Label95("&Colour:", new RectangleF(x, y, w, 13), t.S.HasColor);
        float cw = MathF.Floor((w - 5 * 4) / 6);
        for (int i = 0; i < LampHues.Length; i++)
        {
            var (h, s) = LampHues[i];
            if (!ColorBox95(Ui.Id("wd95lc", i), new RectangleF(x + i * (cw + 4), y + 15, cw, 20), Theme.Hsv(h, s / 100f, 1), t.HueAt == i, t.S.HasColor)) continue;
            lamp?.SetColor(h, s);
            _dirty = true;
        }
        // what it does by itself (Sys/LightAuto.cs): a check box each - on with SteamVR, dimmed in theater mode, the cover's colour
        if (_cfg.Data.Lights is { } lc)
        {
            y += 39;
            // side by side as wide as each label is in this style (too wide for the row: "VR" for "SteamVR")
            var names = LampAutoNames;
            float Need(string[] n) { float s = -8; foreach (var l in n) s += 19 + _ui.LabelWidth95(Lang.T(l)) + 8; return s; }
            if (Need(names) > w) names = LampAutoShort;
            float cx = x;
            for (int i = 0; i < 3; i++)
            {
                bool ck = i == 0 ? lc.OnWithVr : i == 1 ? lc.TheaterDim : lc.CoverColor;
                float at = cx;
                cx += 19 + _ui.LabelWidth95(Lang.T(names[i])) + 8;
                if (!_ui.Check95(Ui.Id("wd95la", i), at, y, names[i], ref ck)) continue;
                if (i == 0) lc.OnWithVr = ck; else if (i == 1) lc.TheaterDim = ck; else lc.CoverColor = ck;
                _cfg.MarkDirty();
                _dirty = true;
            }
        }
    }

    static readonly string[] LampWhiteNames95 = { "&Warm", "&Neutral", "&Daylight" };
    static readonly string[] RgbQuick95 = { "&Static colour", "&Rainbow wave", "Br&eathing", "O&ff" };

    // the effects as option buttons (one Prisma runs that is none of them: written under them), the brightness, the colours
    void Rgb95Detail(RectangleF c, PanelData d)
    {
        var t = Rgb(d);
        var rgb = d.Rgb;
        float sw = c.Right - 75 - 8 - c.X;
        _ui.Text95(LxFit95(t.Ready ? t.Status : t.Msg, sw), new RectangleF(c.X, c.Bottom + 10, sw, 13), t.Ready ? Blk95 : 0xFF800000);
        if (!t.Ready) { None95Detail(c, t.Msg, t.Hint); return; }
        var g = new RectangleF(c.X, c.Y, 128, c.Height);
        _ui.Group95(g, "Effect");
        for (int i = 0; i < 4; i++)
            if (_ui.Radio95(Ui.Id("wd95re", i), g.X + 10, g.Y + 20 + i * 21, RgbQuick95[i], t.QuickAt == i)) { rgb?.SetEffect(RgbState.Quick[i]); _dirty = true; }
        if (t.QuickAt < 0) _ui.Text95(LxFit95(t.Name, g.Width - 20), new RectangleF(g.X + 10, g.Y + 20 + 4 * 21 + 2, g.Width - 20, 13), Gry95);
        float x = g.Right + 10, w = c.Right - x, y = c.Y;
        _ui.Label95("&Brightness:", new RectangleF(x, y + 2, w - 40, 13), !t.Off);
        _ui.Text95(t.Pct, new RectangleF(x, y + 2, w, 13), t.Off ? Gry95 : Blk95, false, TextAlignment.Trailing);
        float v = t.Frac;
        if (_ui.Track95("wd95rb", new RectangleF(x, y + 16, w, 30), ref v, 0, 1, !t.Off, 11)) { rgb?.SetBrightness(BrightOf(v)); _dirty = true; }
        y += 52;
        _ui.Label95("&Colour:", new RectangleF(x, y, w, 13));
        float cw = MathF.Floor((w - 5 * 4) / 6);
        for (int i = 0; i < RgbColors.Length; i++)
        {
            if (!ColorBox95(Ui.Id("wd95rc", i), new RectangleF(x + i * (cw + 4), y + 15, cw, 20), RgbColors[i], t.ColorAt == i, true)) continue;
            rgb?.SetColor(RgbColors[i]);
            _dirty = true;
        }
        LampPic95(true, !t.Off, MathF.Floor(x + (w - 32) / 2), y + 46, 32);
    }

    // no app: the information icon, what is wrong, what to do
    void None95Detail(RectangleF c, string msg, string hint)
    {
        W95Icons.Draw(_p, W95Icons.Id.Info32, c.X + 12, c.Y + 24);
        _ui.Text95(LxFit95(msg, c.Width - 64, true), new RectangleF(c.X + 56, c.Y + 26, c.Width - 60, 15), Blk95, true);
        if (!string.IsNullOrEmpty(hint)) _ui.Text95(hint, new RectangleF(c.X + 56, c.Y + 44, c.Width - 60, 56), Blk95, false, TextAlignment.Leading, true, true);
    }
}
