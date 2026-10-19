using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The light and the RGB widgets ("light": the room's Tapo bulb through Lumos; "rgb": the PC's lighting through Prisma - Sys/RoomLights.cs).
// A 1x1 is a switch: a tap turns it on / off, its card glows with the light (the bulb's white or colour; the RGB's colour, a rainbow for an
// effect with colours of its own). A 2x1 adds a power button (the light) or the four quick effects - static colour, rainbow wave,
// breathing, off - (the RGB) and a brightness bar to drag; a 2x2 the quick whites and colours under them; the 4 x 3 page's S, M, L the
// same, bigger. A tap beside the controls opens it: on / off, the brightness, every white and colour (the light), the effects and colours
// (the RGB). An app that is not running: "–" and why ("Lumos isn't running"), nothing sent.
// Modern in the Bento look (the default look's card takes the light's colour), Cyber in its forms (the tag, segments, a lit outline);
// the retro styles: WristLights95.cs (95, XP, 7, the Mac), Skins/Terminal/WristLights.Term.cs.
public sealed partial class WristPanel
{
    static readonly string[] CapLight = { "Light" }, CapRgb = { "RGB lighting", "RGB" };
    const string BulbGlyph = "", PowerGlyph = "";

    // the quick whites and colours: the light's (kelvin; hue and saturation), the RGB's (Prisma takes RRGGBB)
    static readonly int[] LampWhites = { 2700, 4000, 6500 };
    static readonly string[] LampWhiteNames = { "Warm", "Neutral", "Daylight" };
    static readonly (int h, int s)[] LampHues = { (0, 100), (30, 100), (120, 90), (190, 90), (230, 100), (285, 80) };
    static readonly uint[] RgbColors = { 0xFFFF0000, 0xFFFF7A00, 0xFF00FF40, 0xFF00C8AA, 0xFF0050FF, 0xFFA000FF };

    // ---- what changed: the watch draws again when an app answers or a command was given (WristPanel.Update) ----

    int _lampSeen = -1, _rgbSeen = -1;

    bool LightsChanged(PanelData d)
    {
        int lv = d.Lamp?.State.Version ?? -1, rv = d.Rgb?.State.Version ?? -1;
        if (lv == _lampSeen && rv == _rgbSeen) return false;
        _lampSeen = lv;
        _rgbSeen = rv;
        return C.Face == "custom" && (HasWidget("light") || HasWidget("rgb") || DetailIs("light") || DetailIs("rgb"));
    }

    // ================= the texts =================
    // Made when the state, the language or the bulb's setting changes - a frame drawn again makes none (cut ones are kept too: LxFit)

    sealed class LampText
    {
        public LampState S;
        public int Lang = -1;
        public string Bulb;
        public bool Ready, On, CanWake, Direct;
        public uint Glow;
        public float Frac;
        public string Value, Note, Msg, Short, Hint, Icon, State, Pct, Name, White, Status;
        public int WhiteAt = -1, HueAt = -1;          // the quick white / colour it is now (-1: none of them)
    }

    sealed class RgbText
    {
        public RgbState S;
        public int Lang = -1;
        public bool Ready, Off;
        public uint Color;
        public float Frac;
        public string Effect, Value, Name, NameShort, Short, Msg, ShortMsg, Hint, Pct, Bright, Status;
        public int QuickAt = -1, ColorAt = -1;
    }

    readonly LampText _lxt = new();
    readonly RgbText _rxt = new();
    readonly Dictionary<(string, float, float, int), string> _lxFits = new();

    static readonly LampState NoLamp = new() { Status = LinkStatus.Off };
    static readonly RgbState NoRgb = new() { Status = LinkStatus.Off };

    LampText Lamp(PanelData d)
    {
        var s = d.Lamp?.State ?? NoLamp;
        var t = _lxt;
        string bulb = _cfg.Data.Lights?.Bulb;
        bool direct = d.Lamp?.Direct == true;          // a Tapo key kept: the bulb itself answers, Lumos is never asked
        if (ReferenceEquals(s, t.S) && t.Lang == Lang.Version && ReferenceEquals(bulb, t.Bulb) && direct == t.Direct) return t;
        (t.S, t.Lang, t.Bulb, t.Direct) = (s, Lang.Version, bulb, direct);
        _lxFits.Clear();
        _lxTwo.Clear();
        t.Ready = s.Ready;
        t.On = s.Ready && s.On;
        t.CanWake = s.Status is LinkStatus.Error or LinkStatus.Looking or LinkStatus.NoApp;
        t.Glow = s.Rgb;
        t.Frac = s.Brightness <= 0 ? 0 : (s.Brightness - 1) / 99f;
        t.Pct = Pct(Math.Clamp(s.Brightness, 0, 100));
        t.Value = !t.Ready ? "–" : t.On ? t.Pct : Lang.T("Off");
        t.Name = s.Name.Length > 0 ? s.Name : Lang.T("Tapo bulb");
        t.Note = !t.Ready ? null : s.Name.Length > 0 ? s.Name : Lang.T(t.On ? "On" : "Off");
        t.State = !t.Ready ? "–" : t.On ? Lang.F("On · {0}", t.Pct) : Lang.T("Off");
        (t.Msg, t.Short, t.Hint, t.Icon) = s.Status switch
        {
            LinkStatus.Ready => ("", "", "", BulbGlyph),
            LinkStatus.NoApp => ("Lumos isn't running", "No Lumos", "Start Lumos on this PC", ""),
            LinkStatus.NoDevice => ("Set the bulb", "No bulb", "Edit layout > Light", ""),
            LinkStatus.Error => ("Bulb not reachable", "No answer", s.Message, ""),
            _ => ("Connecting", "Connecting", direct ? bulb ?? "" : "Lumos", BulbGlyph),
        };
        t.WhiteAt = t.HueAt = -1;
        if (t.Ready && s.Temp > 0)
            for (int i = 0; i < LampWhites.Length; i++) if (Math.Abs(s.Temp - LampWhites[i]) <= 150) t.WhiteAt = i;
        if (t.Ready && s.Temp == 0 && s.HasColor)
            for (int i = 0; i < LampHues.Length; i++) if (Math.Abs(s.Hue - LampHues[i].h) <= 6 && Math.Abs(s.Sat - LampHues[i].s) <= 8) t.HueAt = i;
        t.White = !t.Ready ? "" : s.Temp > 0 ? (t.WhiteAt >= 0 ? Lang.F("{0} white · {1} K", Lang.C("light", LampWhiteNames[t.WhiteAt]), s.Temp) : Lang.F("White · {0} K", s.Temp))
            : s.HasColor ? Lang.T("Colour") : "";
        string via = direct ? Lang.T("Direct") : "Lumos";      // who answers: the editor's row says the same
        t.Status = bulb is { Length: > 0 } ? via + " · " + bulb : via;
        return t;
    }

    RgbText Rgb(PanelData d)
    {
        var s = d.Rgb?.State ?? NoRgb;
        var t = _rxt;
        if (ReferenceEquals(s, t.S) && t.Lang == Lang.Version) return t;
        (t.S, t.Lang) = (s, Lang.Version);
        _lxFits.Clear();
        t.Ready = s.Ready;
        t.Off = s.Ready && s.Off;
        t.Effect = t.Ready ? s.Effect : "Off";
        t.Color = s.Color | 0xFF000000;
        t.Frac = (Math.Clamp(s.Brightness, 1, 100) - 1) / 99f;
        t.Pct = Pct(Math.Clamp(s.Brightness, 1, 100));
        t.Name = t.Ready ? Lang.C("rgb", RgbState.Name(s.Effect)) : "";          // ("rgb|Static": not a friend called Static)
        t.Short = t.Ready ? Lang.C("rgb", RgbState.Short(s.Effect)) : "–";
        t.NameShort = t.Ready ? Lang.Short(RgbState.Name(s.Effect)) : null;      // a card's note where the whole name does not fit ("Szivárvány")
        t.Value = !t.Ready ? "–" : t.Off ? Lang.T("Off") : t.Pct;
        t.Bright = Lang.F("Brightness {0}", t.Pct);
        (t.Msg, t.ShortMsg, t.Hint) = s.Status switch
        {
            LinkStatus.Ready => ("", "", ""),
            LinkStatus.NoApp => ("Prisma isn't running", "No Prisma", "Start Prisma on this PC"),
            _ => ("Connecting", "Connecting", "Prisma"),
        };
        t.QuickAt = t.Ready ? Array.IndexOf(RgbState.Quick, s.Effect) : -1;
        t.ColorAt = -1;
        if (t.Ready && RgbState.UsesColor(s.Effect))
            for (int i = 0; i < RgbColors.Length; i++) if ((RgbColors[i] & 0xFFFFFF) == (s.Color & 0xFFFFFF)) t.ColorAt = i;
        t.Status = t.Ready ? "Prisma · " + t.Name : "Prisma";
        return t;
    }

    // a text cut to w, kept until the texts are made again
    string LxFit(string s, float w, float size, FontWeight wt = FontWeight.SemiBold, string fam = null)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, size, (int)wt * 31 + (fam == null ? 0 : fam.GetHashCode()));
        if (_lxFits.TryGetValue(key, out var done)) return done;
        if (_lxFits.Count > 256) _lxFits.Clear();
        return _lxFits[key] = Ellipsize(s, w, size, wt, fam);
    }

    // over two lines (TwoLines, kept)
    readonly Dictionary<(string, float, float, FontWeight), (string, string)> _lxTwo = new();
    (string, string) LxTwo(string s, float w, float size, FontWeight wt)
    {
        if (_lxTwo.TryGetValue((s, w, size, wt), out var done)) return done;
        if (_lxTwo.Count > 64) _lxTwo.Clear();
        return _lxTwo[(s, w, size, wt)] = TwoLines(s, w, size, wt);
    }

    // brightness 0..1 on a bar -> 1..100 % (and back: LampText.Frac)
    static int BrightOf(float v) => 1 + (int)MathF.Round(Math.Clamp(v, 0, 1) * 99);

    // the glow's colour as ink on this card: as it is on a dark card, darker on a light one (a look of its own)
    static uint GlowInk(uint glow) => Theme.Luma(Bento.Text) < 0.5f ? Theme.Mix(glow, 0xFF000000, 0.45f) : glow;

    // ================= Modern =================

    // The card of a light / RGB widget in the default look: the card's colours with the light in them, strongest at the top (a card, so
    // the layout check's background has it too); off or not there: the plain card. An RGB effect with colours of its own: a rainbow.
    void LampBack(RectangleF r, WatchWidgets.Kind k, PanelData d, bool cards)
    {
        ref readonly var L = ref Bento.Cur;
        uint f0 = L.Fill0, f1 = L.Fill1;
        if (k.Id == "light")
        {
            var t = Lamp(d);
            if (t.On) { f0 = Theme.Mix(f0, t.Glow, 0.30f); f1 = Theme.Mix(f1, t.Glow, 0.10f); }
        }
        else
        {
            var t = Rgb(d);
            if (t.Ready && !t.Off)
            {
                if (RgbState.UsesColor(t.Effect)) { f0 = Theme.Mix(f0, t.Color, 0.30f); f1 = Theme.Mix(f1, t.Color, 0.10f); }
                else
                {
                    // a rainbow across the card's top: red - green - blue, the card's own colour under it
                    uint a = Theme.Mix(f0, 0xFFFF3040, 0.26f), b = Theme.Mix(f0, 0xFF30FF60, 0.22f), c = Theme.Mix(f0, 0xFF3070FF, 0.28f);
                    if (cards) { a = Theme.WithAlpha(a, 0.96f); b = Theme.WithAlpha(b, 0.96f); c = Theme.WithAlpha(c, 0.96f); }
                    _p.FillGradient(r, a, b, c, CardRadius, 0);
                    _p.FillGradient(r, f1 & 0x00FFFFFF, Theme.WithAlpha(f1, 0.9f), CardRadius, 90, 0.85f);
                    LampEdge(r, L);
                    return;
                }
            }
        }
        if (cards) { f0 = Theme.WithAlpha(f0, 0.96f); f1 = Theme.WithAlpha(f1, 0.96f); }
        _p.FillGradient(r, f0, f1, CardRadius, 90);
        LampEdge(r, L);
    }

    void LampEdge(RectangleF r, in Bento.Look L)
    {
        if (L.Border == 0 || L.BorderW <= 0) return;
        float i = L.BorderW * 0.48f;
        _p.Stroke(RectangleF.Inflate(r, -i, -i), L.Border, L.BorderW, CardRadius - i);
    }

    void LightOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var t = Lamp(d);
        var lamp = d.Lamp;
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad, right = r.Right - pad;
        uint ink = Styled(w) ? Bento.Text : GlowInk(t.Glow);         // a look of its own: its text colour (the glow would not read on it)
        if (w.W == 1)
        {
            // a switch: the whole card (it opens nothing - a bigger one does)
            if (_ui.Clickable(Ui.Id("wlt", id), r, out _, out bool held)) { lamp?.Toggle(); _dirty = true; }
            if (held) _p.Fill(r, Theme.WithAlpha(t.On ? t.Glow : Bento.Accent, 0.14f), CardRadius);
            Caption(r, iw, CapLight);
            LxIconValue(x, right, r.Y + SBase, BulbGlyph, t.On ? ink : t.Ready ? Sub : Bento.Faint, t.Value, t.Ready ? Bento.Text : Bento.Faint);
            if (t.On) Meter(new RectangleF(x, r.Bottom - (_big ? 18 : 16), iw, _big ? 5 : 4), t.Frac, ink, Ui.Id("wlm", id));
            else if (!t.Ready) TextAt(LxFit(t.Short, iw, SmallText), x, r.Bottom - (_big ? 14 : 12), iw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        CaptionRow(r, t.Note, CapLight);
        if (w.H >= 2 && !t.Ready) { LxNone(r, t.Icon, t.Msg, t.Hint); return; }
        // the power button, the brightness beside it
        float bs = _big ? 54 : 36, top = r.Y + 38;           // (under a header look's band)
        var disc = new RectangleF(x, top, bs, bs);
        if (Press(Ui.Id("wlp", id), disc, PowerGlyph, null, t.On, t.Ready || t.CanWake, _big ? 22 : 18, true, accentFill: false, fill: t.Glow))
        { lamp?.Toggle(); _dirty = true; }
        float vx = disc.Right + 12;
        ValueUnit(vx, top + bs / 2 + Hero * DigitH / 2, right - vx, t.Value, null, Hero, 20, t.Ready ? Bento.Text : Bento.Faint);
        if (!t.Ready)
        {
            TextAt(LxFit(t.Msg, iw, SmallText), x, r.Bottom - (_big ? 16 : 13), iw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        // the brightness to drag (one row: along the bottom; two: under the button, the whites and colours under it)
        float sy = w.H == 1 ? r.Bottom - 15 - (_big ? 4 : 0) : top + bs + (_big ? 30 : 24);
        var bar = new RectangleF(x + KnobR, sy - 2.5f, iw - 2 * KnobR, 5);
        float v = t.Frac;
        if (LxSlider(Ui.Id("wls", id), bar, ref v, ink, true)) { lamp?.SetBrightness(BrightOf(v)); _dirty = true; }
        if (w.H == 1) return;
        float ly = sy + (_big ? 40 : 32);
        if (t.White.Length > 0) TextAt(LxFit(t.White, iw, SmallText), x, ly, iw + 2, Sub, SmallText, FontWeight.SemiBold);
        // the quick whites, then colours (4 on a 2x2, 6 on the 4 x 3 page's L)
        int n = _big ? 6 : 4;
        float sw = _big ? 36 : 32, cy = r.Bottom - 14 - sw / 2 - (_big ? 6 : 0), step = (iw - sw) / (n - 1);
        for (int i = 0; i < n; i++)
        {
            float cx = x + sw / 2 + i * step;
            bool white = i < LampWhites.Length, sel = white ? t.WhiteAt == i : t.HueAt == QuickHue(i - LampWhites.Length);
            var (h, s) = white ? default : LampHues[QuickHue(i - LampWhites.Length)];
            uint col = white ? LampState.Kelvin(LampWhites[i]) : Theme.Hsv(h, s / 100f, 1);
            bool can = white ? t.S.HasTemp : t.S.HasColor;
            if (!LxSwatch(Ui.Id("wlq", id * 8 + i), cx, cy, sw / 2, col, sel, can)) continue;
            if (white) lamp?.SetTemp(LampWhites[i]); else lamp?.SetColor(h, s);
            _dirty = true;
        }
    }

    // the face's colours after its whites: purple, blue, red (the opened view has them all)
    static int QuickHue(int i) => i switch { 0 => 5, 1 => 4, _ => 0 };

    void RgbOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var t = Rgb(d);
        var rgb = d.Rgb;
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad, right = r.Right - pad;
        if (w.W == 1)
        {
            if (_ui.Clickable(Ui.Id("wrt", id), r, out _, out bool held)) { rgb?.Toggle(); _dirty = true; }
            if (held) _p.Fill(r, Theme.WithAlpha(Bento.Accent, 0.14f), CardRadius);
            Caption(r, iw, CapRgb);
            float gs = Hero, by = r.Y + SBase, mid = by - Hero * DigitH / 2;
            EffectPic(t.Effect, x + gs / 2, mid, gs / 2, t.Color, !t.Ready);
            float tx = x + gs + 6, room = right - tx;
            float vs = MathF.Max(SmallText, FitSize(t.Value, room, _big ? 30 : 22, FontWeight.Bold, Theme.Display));
            TextAt(LxFit(t.Value, room, vs, FontWeight.Bold, Theme.Display), tx, by, room + 2, t.Ready ? Bento.Text : Bento.Faint, vs, FontWeight.Bold,
                TextAlignment.Leading, Theme.Display);
            TextAt(LxFit(t.Ready ? t.Short : t.ShortMsg, iw, SmallText), x, r.Bottom - (_big ? 14 : 12), iw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        CaptionRow(r, t.Ready ? t.Name : null, CapRgb, noteShort: t.NameShort);
        if (w.H >= 2 && !t.Ready) { LxNone(r, "", t.Msg, t.Hint); return; }
        // the four quick effects
        float cs = _big ? 50 : 36, top = r.Y + 38, gap = (iw - 4 * cs) / 3;     // (under a header look's band)
        for (int i = 0; i < 4; i++)
        {
            var b = new RectangleF(x + i * (cs + gap), top, cs, cs);
            bool on = t.QuickAt == i;
            if (Press(Ui.Id("wrq", id * 4 + i), b, null, null, on, t.Ready, 18, true)) { rgb?.SetEffect(RgbState.Quick[i]); _dirty = true; }
            EffectPic(RgbState.Quick[i], b.X + cs / 2, b.Y + cs / 2, cs * 0.26f, t.Color, !t.Ready, on);
        }
        if (!t.Ready)
        {
            TextAt(LxFit(t.Msg, iw, SmallText), x, r.Bottom - (_big ? 16 : 13), iw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        float sy;
        if (w.H == 1) sy = r.Bottom - 15 - (_big ? 4 : 0);
        else
        {
            // two rows: the brightness written over its bar
            float ly = top + cs + (_big ? 34 : 28);
            TextAt(LxFit(Lang.T("Brightness"), iw - 50, SmallText), x, ly, iw - 48, Sub, SmallText, FontWeight.SemiBold);
            TextAt(t.Pct, x, ly, iw, t.Off ? Bento.Faint : Bento.Text, SmallText, FontWeight.Bold, TextAlignment.Trailing);
            sy = ly + (_big ? 22 : 18);
        }
        var bar = new RectangleF(x + KnobR, sy - 2.5f, iw - 2 * KnobR, 5);
        float v = t.Frac;
        if (LxSlider(Ui.Id("wrs", id), bar, ref v, t.Off ? Bento.Faint : Bento.Accent, !t.Off)) { rgb?.SetBrightness(BrightOf(v)); _dirty = true; }
        if (w.H == 1) return;
        int n = _big ? 6 : 4;
        float sw = _big ? 36 : 32, cy = r.Bottom - 14 - sw / 2 - (_big ? 6 : 0), step = (iw - sw) / (n - 1);
        for (int i = 0; i < n; i++)
        {
            int ci = _big ? i : QuickRgb(i);
            if (!LxSwatch(Ui.Id("wrc", id * 8 + i), x + sw / 2 + i * step, cy, sw / 2, RgbColors[ci], t.ColorAt == ci, true)) continue;
            rgb?.SetColor(RgbColors[ci]);
            _dirty = true;
        }
    }

    // a 2x2's four colours: red, green, blue, purple
    static int QuickRgb(int i) => i switch { 0 => 0, 1 => 2, 2 => 4, _ => 5 };

    // a picture as big as a value at the left (gs: Hero), the value beside it (smaller until it fits)
    void LxIconValue(float x, float right, float by, string glyph, uint ic, string value, uint vc)
    {
        float gs = Hero, mid = by - Hero * DigitH / 2;
        _p.Icon(glyph, new RectangleF(x - 4, mid - gs / 2 - 1, gs + 8, gs + 2), ic, gs);
        float tx = x + gs + 6, room = right - tx;
        float vs = MathF.Max(SmallText, FitSize(value, room, _big ? 30 : 22, FontWeight.Bold, Theme.Display));
        TextAt(LxFit(value, room, vs, FontWeight.Bold, Theme.Display), tx, by, room + 2, vc, vs, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
    }

    // a tall card without its app: the icon, what is wrong (two lines), the hint along the bottom
    void LxNone(RectangleF r, string icon, string msg, string hint)
    {
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad;
        _p.Icon(icon, new RectangleF(x - 2, r.Y + 40, 44, 44), Sub, 34);
        var (l1, l2) = LxTwo(msg, iw, 20, FontWeight.SemiBold);
        TextAt(l1, x, r.Y + 112, iw + 2, Bento.Text, 20, FontWeight.SemiBold);
        TextAt(l2, x, r.Y + 136, iw + 2, Bento.Text, 20, FontWeight.SemiBold);
        if (!string.IsNullOrEmpty(hint)) TextAt(LxFit(hint, iw, CapSize), x, r.Bottom - 16, iw + 2, Sub, CapSize, FontWeight.SemiBold);
    }

    // A bar with a knob on a card: drag along it (or tap) - true, with the new value 0..1, while it moves; the stick over it moves it 5 %.
    // Held, the watch does not take it for a page swipe. Cyber: segments with a cyan mark.
    bool LxSlider(string id, RectangleF bar, ref float v, uint col, bool enabled)
    {
        _ui.Clickable(id, new RectangleF(bar.X - KnobR - 2, bar.Y - 11, bar.Width + 2 * KnobR + 4, bar.Height + 22), out bool hover, out bool held);
        bool changed = false;
        if (held && enabled)
        {
            _ui.DragHeld = true;
            float nv = Math.Clamp((_ui.Mouse.X - bar.X) / bar.Width, 0, 1);
            if (MathF.Abs(nv - v) > 0.004f) { v = nv; changed = true; }
        }
        else if (hover && enabled && _ui.Wheel != 0)
        {
            v = Math.Clamp(v + 0.05f * MathF.Sign(_ui.Wheel), 0, 1);
            changed = true;
        }
        float vv = Math.Clamp(v, 0, 1), kx = bar.X + bar.Width * vv, cy = bar.Y + bar.Height / 2;
        if (Theme.Cyber)
        {
            CyberHud.Segments(_p, bar, enabled ? vv : 0, !enabled ? CyberHud.Faint : held || hover ? CyC : CyY, CyberHud.Track);
            if (enabled) _p.Fill(new RectangleF(MathF.Round(kx) - 1.5f, cy - 7, 3, 14), CyC);
            return changed;
        }
        float h = bar.Height;
        _p.Fill(bar, Bento.Track, h / 2);
        if (!enabled) return changed;
        _p.Fill(new RectangleF(bar.X, bar.Y, MathF.Max(h, kx - bar.X), h), col, h / 2);
        float kr = held ? KnobR : hover ? KnobR - 0.5f : KnobR - 1.5f;
        HaloDisc(kx, cy, kr + 2);
        _p.Circle(kx, cy, kr, Bento.Text == Bento.White ? 0xFFFFFFFF : col);
        return changed;
    }

    // a colour to pick: a disc (Cyber: a square with its corner cut), a ring round the one it is now
    bool LxSwatch(string id, float cx, float cy, float rad, uint col, bool on, bool enabled)
    {
        var hit = new RectangleF(cx - rad - 3, cy - rad - 3, 2 * rad + 6, 2 * rad + 6);
        bool click = _ui.Clickable(id, hit, out bool hover, out bool held) && enabled;
        float s = _ui.Spring(Ui.Id(id, "#s"), held && enabled ? 0.88f : 1f, 420, 18), rr = rad * s;
        uint c = enabled ? col : Theme.WithAlpha(col, 0.3f);
        if (Theme.Cyber)
        {
            var b = new RectangleF(cx - rr, cy - rr, 2 * rr, 2 * rr);
            float cut = MathF.Round(rr * 0.45f);
            _p.FillCut(b, c, 0, 0, cut, 0);
            if (on || hover) _p.StrokeCut(RectangleF.Inflate(b, 2.5f, 2.5f), on ? CyY : CyC, 1.5f, 0, 0, cut + 1, 0);
            return click;
        }
        if (on) _p.Ring(cx, cy, rad + 3.5f, Bento.Text, 2);
        else if (hover && enabled) _p.Ring(cx, cy, rad + 3, Theme.WithAlpha(Bento.Text, 0.5f), 1.5f);
        _p.Circle(cx, cy, rr, c);
        _p.Ring(cx, cy, rr - 0.5f, Theme.WithAlpha(0xFF000000, 0.22f), 1);
        return click;
    }

    // An effect as a little picture: the static colour a disc of it, breathing rings of it, a rainbow six dots of every colour round a
    // ring, off the power sign; dim: grey (no app). sel: on the accent's fill (the picture keeps its colours, a ring round a disc)
    void EffectPic(string effect, float cx, float cy, float rad, uint col, bool dim, bool sel = false)
    {
        uint ink = Theme.Cyber ? (sel ? CyInk : CyC) : sel ? Bento.OnAccent : Bento.Text;
        if (dim) { col = Bento.Faint; ink = Bento.Faint; }
        switch (effect)
        {
            case "Off":
                _p.Icon(PowerGlyph, new RectangleF(cx - rad * 1.3f, cy - rad * 1.3f, rad * 2.6f, rad * 2.6f), dim ? Bento.Faint : ink, MathF.Round(rad * 1.6f));
                return;
            case "RainbowWave" or "ColorCycle" or "Gradient" or "Ambient" or "Surprise" or "Music":
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathF.Tau / 6;
                    uint hc = dim ? Bento.Faint : Theme.Hsv(i * 60, 0.85f, 1);
                    _p.Circle(cx + MathF.Sin(a) * rad * 0.68f, cy - MathF.Cos(a) * rad * 0.68f, rad * 0.32f, hc);
                }
                return;
            case "Breathing":
                _p.Circle(cx, cy, rad * 0.42f, col);
                _p.Ring(cx, cy, rad * 0.72f, Theme.WithAlpha(col, 0.7f), MathF.Max(1.5f, rad * 0.14f));
                _p.Ring(cx, cy, rad * 0.98f, Theme.WithAlpha(col, 0.35f), MathF.Max(1.2f, rad * 0.1f));
                return;
            case "Fire":
                _p.Circle(cx, cy, rad * 0.9f, dim ? col : 0xFFFF6A1A);
                _p.Circle(cx, cy + rad * 0.2f, rad * 0.45f, dim ? col : 0xFFFFD040);
                return;
            default:
                if (sel && !Theme.Cyber) _p.Ring(cx, cy, rad * 0.95f, Theme.WithAlpha(ink, 0.6f), 1.5f);
                _p.Circle(cx, cy, rad * 0.78f, col);
                return;
        }
    }

    // ---- opened ----

    // c: the opened card's inside (the heading "Light" over it, the X at its right). The bulb's tile at the left: its glow, its name, on /
    // off and a button that switches it; at the right the brightness to drag, three whites and six colours; where it is asked along the
    // bottom. No Lumos / no bulb set: why, and what to do.
    void LightDetail(RectangleF c, PanelData d)
    {
        var t = Lamp(d);
        var lamp = d.Lamp;
        if (!t.Ready) { LxNoneDetail(c, t.Icon, t.Msg, t.Hint, t.S.Status == LinkStatus.Error); return; }
        uint ink = GlowInk(t.Glow);
        var hero = new RectangleF(c.X, c.Y, 236, c.Height);
        LxHero(hero, t.On ? t.Glow : 0);
        float hx = hero.X + hero.Width / 2, gy = hero.Y + 86;
        if (t.On)
        {
            _p.Circle(hx, gy, 60, Theme.WithAlpha(t.Glow, Theme.Cyber ? 0.14f : 0.2f));
            _p.Circle(hx, gy, 42, Theme.WithAlpha(t.Glow, Theme.Cyber ? 0.22f : 0.3f));
        }
        _p.Icon(BulbGlyph, new RectangleF(hx - 40, gy - 40, 80, 80), t.On ? (Theme.Cyber ? CyY : ink) : Theme.Cyber ? CyG : Sub, 60);
        LxHeroText(hero, t.Name, t.State, t.On);
        if (Press("wdlp", new RectangleF(hero.X + 18, hero.Bottom - 18 - 54, hero.Width - 36, 54), PowerGlyph, t.On ? "Turn off" : "Turn on", t.On, true, 20,
                accentFill: false, fill: t.Glow))
        { lamp?.Toggle(); _dirty = true; }
        // the brightness
        float rx = hero.Right + 28, rw = c.Right - rx, y = c.Y;
        CapText(Upper("Brightness"), rx, y + 16, rw - 60, Sub);
        TextAt(t.On ? t.Pct : "–", rx, y + 16, rw, Theme.Cyber ? CyW : Bento.Text, SmallText, FontWeight.Bold, TextAlignment.Trailing);
        var bar = new RectangleF(rx + KnobR, y + 38, rw - 2 * KnobR, 8);
        float v = t.Frac;
        if (LxSlider("wdls", bar, ref v, ink, true)) { lamp?.SetBrightness(BrightOf(v)); _dirty = true; }
        // the whites
        y = c.Y + 76;
        CapText(Upper("White"), rx, y + 16, rw, Sub);
        float tw = (rw - 2 * 10) / 3;
        for (int i = 0; i < 3; i++)
        {
            var tile = new RectangleF(rx + i * (tw + 10), y + 26, tw, 58);
            bool on = t.WhiteAt == i;
            if (Press(Ui.Id("wdlw", i), tile, null, null, on, t.S.HasTemp, 18)) { lamp?.SetTemp(LampWhites[i]); _dirty = true; }
            _p.Circle(tile.X + tile.Width / 2, tile.Y + 19, 8, LampState.Kelvin(LampWhites[i]));
            uint fg = !t.S.HasTemp ? Bento.Faint : on ? (Theme.Cyber ? CyInk : Bento.OnAccent) : Theme.Cyber ? CyW : Bento.Text;
            TextAt(LxFit(Lang.C("light", LampWhiteNames[i]), tile.Width - 10, SmallText), tile.X + 5, tile.Bottom - 10, tile.Width - 10, fg, SmallText, FontWeight.SemiBold,
                TextAlignment.Center);
        }
        // the colours
        y = c.Y + 176;
        CapText(Upper("Colour"), rx, y + 16, rw, Sub);
        float sw = 40, step = (rw - sw) / (LampHues.Length - 1);
        for (int i = 0; i < LampHues.Length; i++)
        {
            var (h, s) = LampHues[i];
            if (!LxSwatch(Ui.Id("wdlc", i), rx + sw / 2 + i * step, y + 26 + sw / 2 + 4, sw / 2, Theme.Hsv(h, s / 100f, 1), t.HueAt == i, t.S.HasColor)) continue;
            lamp?.SetColor(h, s);
            _dirty = true;
        }
        // what it does by itself (Sys/LightAuto.cs): on with SteamVR, dimmed in theater mode, the cover's colour - a switch each
        if (_cfg.Data.Lights is { } lc)
        {
            y = c.Y + 244;
            CapText(Upper("Auto"), rx, y + 16, rw, Sub);
            float aw = (rw - 2 * 8) / 3;
            for (int i = 0; i < 3; i++)
            {
                bool on = i == 0 ? lc.OnWithVr : i == 1 ? lc.TheaterDim : lc.CoverColor;
                if (!Press(Ui.Id("wdla", i), new RectangleF(rx + i * (aw + 8), y + 26, aw, 30), null, LampAutoNames[i], on, true, 14)) continue;
                if (i == 0) lc.OnWithVr = !on; else if (i == 1) lc.TheaterDim = !on; else lc.CoverColor = !on;
                _cfg.MarkDirty();
                _dirty = true;
            }
        }
        LxStatus(rx, c.Bottom - 4, rw, t.Status, t.S.Pending);
    }

    static readonly string[] LampAutoNames = { "SteamVR", "Theater", "Cover" }, LampAutoShort = { "VR", "Theater", "Cover" };

    // The RGB opened: its effect big on the left (the picture, its name, the brightness) with a button that switches it all off / on; at
    // the right the four effects as tiles, the brightness to drag, six colours.
    void RgbDetail(RectangleF c, PanelData d)
    {
        var t = Rgb(d);
        var rgb = d.Rgb;
        if (!t.Ready) { LxNoneDetail(c, "", t.Msg, t.Hint, false); return; }
        var hero = new RectangleF(c.X, c.Y, 236, c.Height);
        LxHero(hero, t.Off ? 0 : RgbState.UsesColor(t.Effect) ? t.Color : 0xFF7F6CFF);
        float hx = hero.X + hero.Width / 2, gy = hero.Y + 86;
        EffectPic(t.Effect, hx, gy, 52, t.Color, false);
        LxHeroText(hero, t.Name, t.Off ? "" : t.Bright, !t.Off);
        if (Press("wdrp", new RectangleF(hero.X + 18, hero.Bottom - 18 - 54, hero.Width - 36, 54), PowerGlyph, t.Off ? "Turn on" : "Turn off", !t.Off, true, 20))
        { rgb?.Toggle(); _dirty = true; }
        float rx = hero.Right + 28, rw = c.Right - rx, y = c.Y;
        CapText(Upper("Effect"), rx, y + 16, rw, Sub);
        float tw = (rw - 10) / 2, th = 50;
        for (int i = 0; i < 4; i++)
        {
            var tile = new RectangleF(rx + i % 2 * (tw + 10), y + 26 + i / 2 * (th + 8), tw, th);
            bool on = t.QuickAt == i;
            if (Press(Ui.Id("wdre", i), tile, null, null, on, true, 18)) { rgb?.SetEffect(RgbState.Quick[i]); _dirty = true; }
            EffectPic(RgbState.Quick[i], tile.X + 24, tile.Y + th / 2, 13, t.Color, false, on);
            uint fg = on ? (Theme.Cyber ? CyInk : Bento.OnAccent) : Theme.Cyber ? CyW : Bento.Text;
            TextAt(LxFit(Lang.C("rgb", RgbState.Name(RgbState.Quick[i])), tw - 50, SmallText), tile.X + 44, tile.Y + th / 2 + 6, tw - 48, fg, SmallText, FontWeight.SemiBold);
        }
        y = c.Y + 150;
        CapText(Upper("Brightness"), rx, y + 16, rw - 60, Sub);
        TextAt(t.Pct, rx, y + 16, rw, t.Off ? Bento.Faint : Theme.Cyber ? CyW : Bento.Text, SmallText, FontWeight.Bold, TextAlignment.Trailing);
        var bar = new RectangleF(rx + KnobR, y + 38, rw - 2 * KnobR, 8);
        float v = t.Frac;
        if (LxSlider("wdrs", bar, ref v, Bento.Accent, !t.Off)) { rgb?.SetBrightness(BrightOf(v)); _dirty = true; }
        y = c.Y + 214;
        CapText(Upper("Colour"), rx, y + 16, rw, Sub);
        float sw = 40, step = (rw - sw) / (RgbColors.Length - 1);
        for (int i = 0; i < RgbColors.Length; i++)
        {
            if (!LxSwatch(Ui.Id("wdrc", i), rx + sw / 2 + i * step, y + 26 + sw / 2 + 4, sw / 2, RgbColors[i], t.ColorAt == i, true)) continue;
            rgb?.SetColor(RgbColors[i]);
            _dirty = true;
        }
        LxStatus(rx, c.Bottom - 4, rw, t.Status, t.S.Pending);
    }

    // the opened views' left tile: the light's colour in it (glow 0: the plain tile); Cyber: a plate with yellow brackets
    void LxHero(RectangleF hero, uint glow)
    {
        if (Theme.Cyber)
        {
            CyberHud.CardShape(_p, hero, CyberHud.Card2, glow != 0 ? CyberHud.CyanDim : CyberHud.Edge);
            CyberHud.Brackets(_p, hero, glow != 0 ? CyY : CyG, 12, 6, 2);
            return;
        }
        if (glow == 0) _p.Fill(hero, Bento.Tile, 18);
        else _p.FillGradient(hero, Theme.Mix(Bento.Tile, glow, 0.34f), Theme.Mix(Bento.Tile, glow, 0.08f), 18, 90);
    }

    // its name and state under the picture (Cyber: the condensed capitals and monospace)
    void LxHeroText(RectangleF hero, string name, string state, bool on)
    {
        float w = hero.Width - 28, x = hero.X + 14;
        if (Theme.Cyber)
        {
            string n = Upper(name);
            TextAt(LxFit(n, w, 22, FontWeight.Bold, Theme.Display), x, hero.Y + 178, w, on ? CyW : CyG, 22, FontWeight.Bold, TextAlignment.Center, Theme.Display);
            if (!string.IsNullOrEmpty(state)) CyMono(LxFit(Upper(state), w, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, hero.Y + 202, w, on ? CyY : CyG, CyNote,
                FontWeight.SemiBold, TextAlignment.Center);
            return;
        }
        TextAt(LxFit(name, w, 22), x, hero.Y + 178, w, Bento.Text, 22, FontWeight.SemiBold, TextAlignment.Center);
        if (!string.IsNullOrEmpty(state)) TextAt(LxFit(state, w, SmallText), x, hero.Y + 204, w, Sub, SmallText, FontWeight.SemiBold, TextAlignment.Center);
    }

    // where it is asked, small along the bottom ("Lumos · 192.168.0.10"; a command on its way: a dot before it)
    void LxStatus(float x, float by, float w, string s, bool pending)
    {
        if (Theme.Cyber) { CyMono(LxFit(Upper(s), w, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, by, w, pending ? CyY : CyG); return; }
        if (pending) _p.Circle(x + 4, by - 5, 3.5f, Bento.Accent);
        TextAt(LxFit(s, w - (pending ? 14 : 0), SmallText), x + (pending ? 14 : 0), by, w, Sub, SmallText, FontWeight.SemiBold);
    }

    // no app: its icon, what is wrong, what to do (Lumos's own words for a bulb it could not reach, over two lines)
    void LxNoneDetail(RectangleF c, string icon, string msg, string hint, bool wrapHint)
    {
        float my = c.Y + (c.Height - 150) / 2;
        uint ic = Theme.Cyber ? CyG : Bento.Faint, mc = Theme.Cyber ? CyW : Bento.Text, hc = Theme.Cyber ? CyG : Sub;
        _p.Icon(icon, new RectangleF(c.X, my, c.Width, 52), ic, 40);
        _p.Text(LxFit(Theme.Cyber ? Upper(msg) : msg, c.Width, 22), new RectangleF(c.X, my + 58, c.Width, 34), mc, 22, FontWeight.SemiBold, TextAlignment.Center);
        if (string.IsNullOrEmpty(hint)) return;
        if (wrapHint)
            _p.Text(hint, new RectangleF(c.X + 40, my + 94, c.Width - 80, 60), hc, SmallText, FontWeight.Normal, TextAlignment.Center, ParagraphAlignment.Near, null, true);
        else _p.Text(LxFit(hint, c.Width, SmallText), new RectangleF(c.X, my + 94, c.Width, 28), hc, SmallText, FontWeight.SemiBold, TextAlignment.Center);
    }

    // ================= Cyber =================

    // LIGHT: the tag, the bulb's name as its note; on, the card's outline yellow and ON in the condensed capitals, the brightness in
    // segments; off, OFF in grey; the power square, the brightness to drag and the whites / colours as cut squares on the bigger ones
    void CyberLight(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var t = Lamp(d);
        var lamp = d.Lamp;
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad, right = r.Right - pad;
        if (t.On) CyLit(r, CyY);
        string note = !t.Ready ? (w.W >= 2 ? t.Short : null) : w.W >= 2 ? t.Note : null;
        CyberCaptionRow(r, x, right, note, !t.Ready ? CyR : t.On ? CyY : CyG, CapLight);
        if (w.W == 1)
        {
            string mk = Ui.Id("wlt", id);
            if (_ui.Clickable(mk, r, out _, out _)) { lamp?.Toggle(); _dirty = true; CyTap(mk); }
            CyTapFx(mk, r, CyberHud.CardTL, CyberHud.CardBR);
            CyWord(r, x, right, BulbGlyph, !t.Ready ? "–" : t.On ? "ON" : "OFF", !t.Ready ? CyberHud.Faint : t.On ? CyY : CyG);
            float barY = r.Bottom - (_big ? 18 : 15) - 5;
            if (t.On) CyberHud.Segments(_p, new RectangleF(x, barY, iw, 5), t.Frac * CyIn(0.28f, 0.3f), CyY, CyberHud.Track, _big ? 8 : 5);
            else if (!t.Ready) CyMono(LxFit(Upper(t.Short), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Bottom - (_big ? 15 : 12), iw + 2, CyR);
            return;
        }
        if (w.H >= 2 && !t.Ready) { CyLxNone(r, t.Msg, t.Hint); return; }
        float bs = _big ? 54 : 42, top = r.Y + (_big ? 38 : 31);
        var sq = new RectangleF(x, top, bs, bs);
        if (Press(Ui.Id("wlp", id), sq, PowerGlyph, null, t.On, t.Ready || t.CanWake, _big ? 22 : 18)) { lamp?.Toggle(); _dirty = true; }
        float vx = sq.Right + 12;
        CyValue(vx, top + bs / 2 + (_big ? 14 : 11), t.Value, CyFit(t.Value, right - vx, _big ? 46 : 36), !t.Ready ? CyberHud.Faint : t.On ? CyY : CyG,
            Ui.Id("wlv", id));
        if (!t.Ready)
        {
            CyMono(LxFit(Upper(t.Msg), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Bottom - (_big ? 16 : 13), iw + 2, CyR);
            return;
        }
        float sy = w.H == 1 ? r.Bottom - 16 - (_big ? 4 : 0) : top + bs + (_big ? 30 : 24);
        var bar = new RectangleF(x, sy - 4, iw, 8);
        float v = t.Frac;
        if (LxSlider(Ui.Id("wls", id), bar, ref v, CyY, true)) { lamp?.SetBrightness(BrightOf(v)); _dirty = true; }
        if (w.H == 1) return;
        float ly = sy + (_big ? 38 : 30);
        if (t.White.Length > 0) CyMono(LxFit(Upper(t.White), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, ly, iw + 2, CyG);
        int n = _big ? 6 : 4;
        float sw = _big ? 34 : 30, cy = r.Bottom - 15 - sw / 2 - (_big ? 6 : 0), step = (iw - sw) / (n - 1);
        for (int i = 0; i < n; i++)
        {
            bool white = i < LampWhites.Length, sel = white ? t.WhiteAt == i : t.HueAt == QuickHue(i - LampWhites.Length);
            var (h, s) = white ? default : LampHues[QuickHue(i - LampWhites.Length)];
            uint col = white ? LampState.Kelvin(LampWhites[i]) : Theme.Hsv(h, s / 100f, 1);
            if (!LxSwatch(Ui.Id("wlq", id * 8 + i), x + sw / 2 + i * step, cy, sw / 2, col, sel, white ? t.S.HasTemp : t.S.HasColor)) continue;
            if (white) lamp?.SetTemp(LampWhites[i]); else lamp?.SetColor(h, s);
            _dirty = true;
        }
    }

    // RGB: the tag, the effect as its note (cyan); the effect's picture and the brightness on a 1x1; the four effects as cut squares (the
    // one running yellow), the brightness to drag, the colours on the bigger ones
    void CyberRgb(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var t = Rgb(d);
        var rgb = d.Rgb;
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad, right = r.Right - pad;
        if (t.Ready && !t.Off) CyLit(r, CyC);
        CyberCaptionRow(r, x, right, !t.Ready ? (w.W >= 2 ? t.ShortMsg : null) : w.W >= 2 ? t.Name : null, !t.Ready ? CyR : CyC, CapRgb,
            noteShort: t.Ready ? t.NameShort : null);
        if (w.W == 1)
        {
            string mk = Ui.Id("wrt", id);
            if (_ui.Clickable(mk, r, out _, out _)) { rgb?.Toggle(); _dirty = true; CyTap(mk); }
            CyTapFx(mk, r, CyberHud.CardTL, CyberHud.CardBR);
            float gs = _big ? 32 : 26, by = r.Y + (_big ? 82 : 66);
            EffectPic(t.Effect, x + gs / 2, by - gs / 2 + 2, gs / 2, t.Color, !t.Ready);
            float tx = x + gs + 6;
            CyValue(tx, by, t.Value, CyFit(t.Value, right - tx, _big ? 40 : 30), !t.Ready ? CyberHud.Faint : t.Off ? CyG : CyW, Ui.Id("wrv", id));
            CyMono(LxFit(Upper(t.Ready ? t.Short : t.ShortMsg), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Bottom - (_big ? 15 : 12), iw + 2,
                t.Ready ? CyC : CyR);
            return;
        }
        if (w.H >= 2 && !t.Ready) { CyLxNone(r, t.Msg, t.Hint); return; }
        float cs = _big ? 50 : 40, top = r.Y + (_big ? 38 : 31), gap = (iw - 4 * cs) / 3;
        for (int i = 0; i < 4; i++)
        {
            var b = new RectangleF(x + i * (cs + gap), top, cs, cs);
            bool on = t.QuickAt == i;
            if (Press(Ui.Id("wrq", id * 4 + i), b, null, null, on, t.Ready, 18)) { rgb?.SetEffect(RgbState.Quick[i]); _dirty = true; }
            EffectPic(RgbState.Quick[i], b.X + cs / 2, b.Y + cs / 2, cs * 0.26f, t.Color, !t.Ready, on);
        }
        if (!t.Ready)
        {
            CyMono(LxFit(Upper(t.Msg), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Bottom - (_big ? 16 : 13), iw + 2, CyR);
            return;
        }
        float sy;
        if (w.H == 1) sy = r.Bottom - 16 - (_big ? 4 : 0);
        else
        {
            float ly = top + cs + (_big ? 32 : 26);
            CyMono(LxFit(Upper("Brightness"), iw - 50, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, ly, iw - 48, CyG);
            CyMono(t.Pct, right, ly, 48, t.Off ? CyG : CyW, CyNote, FontWeight.Bold, TextAlignment.Trailing);
            sy = ly + (_big ? 20 : 16);
        }
        var bar = new RectangleF(x, sy - 4, iw, 8);
        float v = t.Frac;
        if (LxSlider(Ui.Id("wrs", id), bar, ref v, CyC, !t.Off)) { rgb?.SetBrightness(BrightOf(v)); _dirty = true; }
        if (w.H == 1) return;
        int n = _big ? 6 : 4;
        float sw = _big ? 34 : 30, cy = r.Bottom - 15 - sw / 2 - (_big ? 6 : 0), step = (iw - sw) / (n - 1);
        for (int i = 0; i < n; i++)
        {
            int ci = _big ? i : QuickRgb(i);
            if (!LxSwatch(Ui.Id("wrc", id * 8 + i), x + sw / 2 + i * step, cy, sw / 2, RgbColors[ci], t.ColorAt == ci, true)) continue;
            rgb?.SetColor(RgbColors[ci]);
            _dirty = true;
        }
    }

    // a card that is on: its outline drawn again in the light's colour (over the dark-red one)
    void CyLit(RectangleF r, uint c)
    {
        if (!CyEdgeShown) return;
        _p.StrokeCut(RectangleF.Inflate(r, -0.625f, -0.625f), c, 1.25f, CyberHud.CardTL - 0.25f, 0, CyberHud.CardBR - 0.25f, 0);
    }

    // a 1x1's state: a small picture and the word in the condensed capitals beside it
    void CyWord(RectangleF r, float x, float right, string glyph, string word, uint c)
    {
        float gs = _big ? 30 : 24, by = r.Y + (_big ? 82 : 66);
        _p.Icon(glyph, new RectangleF(x - 3, by - gs + 1, gs + 4, gs + 2), c, gs - 2);
        float tx = x + gs + 4, room = right - tx;
        float ws = MathF.Max(17, FitSize(word, room, _big ? 38 : 30, FontWeight.Bold, Theme.Display));
        TextAt(LxFit(word, room, ws, FontWeight.Bold, Theme.Display), tx, by, room + 2, c, ws, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
    }

    // a tall card without its app, in monospace capitals
    void CyLxNone(RectangleF r, string msg, string hint)
    {
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad;
        var (l1, l2) = LxTwo(Upper(msg), iw, 20, FontWeight.Bold);
        TextAt(l1, x, r.Y + 78, iw + 2, CyW, 20, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        TextAt(l2, x, r.Y + 102, iw + 2, CyW, 20, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        if (!string.IsNullOrEmpty(hint)) CyMono(LxFit(Upper(hint), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Bottom - 16, iw + 2, CyberHud.Faint);
    }

    // ---- --snapshot's hooks ----

    // what the watch shows now: the light's value ("70%", "Off", "–") and why there is none
    public string LightShown(PanelData d) { var t = Lamp(d); return t.Ready ? t.Value : t.Msg; }
    public string RgbShown(PanelData d) { var t = Rgb(d); return t.Ready ? t.Name : t.Msg; }
}
