using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// The custom face in the Cyber style (Look > Style; CyberHud in src/Gfx/Cyber.cs) - Erik's concept: a near-black page in an
// olive frame, cards with cut corners and a dark-red hairline, a YELLOW TAG as each card's caption (black monospace capitals, a
// slanted end) with a monospace grey note at the right, values in Bahnschrift's condensed bold, segmented bars. The concept's
// own designs: the time on a yellow card (black digits with a red / cyan offset - static, no flicker - and hazard stripes along
// its bottom), the HUD dial (yellow quarters, a red second hand and a red arc of the minute so far), the date with its day in big
// yellow, the batteries as rows of segments, now playing with its yellow play square, the frames as grey bars with red spikes under
// a dashed yellow budget, the room, the microphone, the inbox, the buttons as cyan-outlined tiles with corner marks.
// Everything else (calendar, timer, stopwatch, volume, time in VR, Hexpad, Minesweeper, the opened views but the clock's and the
// notifications' - WristCyberDetail.cs, WristNotes.cs) is the modern design drawn through the Cyber forms of the shared parts
// (WristCustom: Caption / CaptionRow / Meter / Press / CardBack). The widgets' own looks
// (Style / Color of 1.11) do not apply in Cyber: one look, the concept's; a clock's other designs (Flip, Segment, Station...) still
// pick as before and draw in its colours - the size's default design is the Cyber one.
public sealed partial class WristPanel
{
    const uint CyY = CyberHud.Yellow, CyC = CyberHud.Cyan, CyR = CyberHud.Red, CyW = CyberHud.White, CyG = CyberHud.Grey, CyInk = CyberHud.Ink;
    const uint CyDial = 0xFF0E0D11, CyRing = 0xFF302C30, CyTick = 0xFF5E5954, CyNumeral = 0xFFB9B4AA, CyOther = 0xFF6E6964, CyTimeEdge = 0xFFE0574C;
    // the caption tag: 8 px in, 9 px down - clear of the 3-8 px band along a card's edge the layout check keeps for the card itself
    const float CyTagX = 8, CyTagY = 9;
    const float CyNote = 14;                          // notes, units, names: monospace 14 px (the concept's small text)
    static readonly string[] CapPower = { "Power" }, CapAudio = { "Audio" }, CapFrames = { "Frames" }, CapDate = { "Date" },
        CapTime = { "Time" }, CapClock = { "Clock" }, CapInbox = { "Inbox" }, CapButtons = { "Buttons" }, CapMicC = { "Mic" };

    // the tag text's baseline from the card's top (the notes beside it share it)
    float CyBase => CyTagY + (CyberHud.TagH + CyberHud.CapH(_p, CyberHud.TagSize, FontWeight.Bold)) / 2;

    // how tall the value font's digits are, per px of its size
    float CyCap => _p.DigitInk(FontWeight.Bold, Theme.Display).above;

    // the widget draws its Cyber design: a clock in its size's default design (any other design draws as before, in Cyber's colours)
    static bool CyberClock(WatchWidget w) => WatchWidgets.VariantOf(w) == WatchWidgets.DefaultVariant(w.Type, w.W, w.H);

    // ================= the page, the cards =================

    // the page: near black, an olive frame with its four corners cut
    void CyberPage()
    {
        var all = new RectangleF(0, 0, TW, TH);
        if (CyPageBooting) { CyberPageBoot(all); return; }      // the boot: the frame draws in (WristCyberFx.cs)
        CyberHud.PanelFrame(_p, all, Theme.OverlayBg);
    }

    // the page dots: short bars, the page in view a longer yellow one
    void CyberDots(int count, float pos, bool cards, float x0, float pitch, float cy)
    {
        float span = (count - 1) * pitch;
        if (cards) _p.Fill(new RectangleF(x0 - 16, cy - 8, span + 32, 16), 0xB3000000);       // a dark plate in the dots' own strip
        for (int i = 0; i < count; i++) _p.Fill(new RectangleF(x0 + i * pitch - 4, cy - 1.5f, 8, 3), CyOther);
        float ax = x0 + Math.Clamp(pos, 0, count - 1) * pitch;
        _p.Fill(new RectangleF(ax - 9, cy - 2, 18, 4), CyY);
        CySeam(pos);                                    // sliding: a scan line on the seam (WristCyberFx.cs)
    }

    // A card: its fill with the corners cut, the dark-red hairline (cyan round a live microphone); the time's yellow card; a muted
    // volume / microphone the danger card. Drawn as the card (the layout check's background), so the stripes and corner marks are too.
    void CyberCard(RectangleF r, WatchWidgets.Kind k, WatchWidget w, bool cards, bool danger)
    {
        uint fill = cards ? Theme.WithAlpha(CyberHud.Card, 0.96f) : CyberHud.Card;
        _haloCard = RectangleF.Empty;
        bool edge = CyEdgeShown;                        // booting: the outline is drawn in from its corners (WristCyberFx.cs)
        if (danger) { _cyEdgeC = CyberHud.DangerEdge; CyberHud.CardShape(_p, r, CyberHud.DangerFill, edge ? CyberHud.DangerEdge : 0); return; }
        if (k.Id == "time" && CyberClock(w)) { CyberTimeCard(r, edge); return; }
        bool live = k.Id == "mic" && Mic.Present && !Mic.Muted;
        _cyEdgeC = live ? CyberHud.CyanDim : CyberHud.Edge;
        CyberHud.CardShape(_p, r, fill, edge ? _cyEdgeC : 0);
        if (k.Id == "analog")
        {
            if (CyberClock(w)) { if (r.Width >= 150) CyberHud.Brackets(_p, r, CyY, 9, 6, 2, false, true); }      // the clock's corner mark
            else if (WatchWidgets.VariantOf(w) == "squircle") AnalogFace(r, k, w, Bento.Cur, fill, fill, cards);  // a picked squircle: its face
        }
    }

    // the time's card: yellow, its top-left corner cut, a red hairline offset down and right, hazard stripes along its bottom
    const float CyTimeOff = 3;
    RectangleF CyTimeRect(RectangleF r) => new(r.X, r.Y, r.Width - CyTimeOff, r.Height - CyTimeOff);
    float CyHazard(RectangleF r) => Math.Clamp(MathF.Round(r.Height * 0.07f), 7, 14);

    void CyberTimeCard(RectangleF r, bool edge = true)
    {
        var y = CyTimeRect(r);
        var hair = new RectangleF(r.X + CyTimeOff, r.Y + CyTimeOff, y.Width, y.Height);
        (_cyEdgeR, _cyEdgeC, _cyEdgeTL, _cyEdgeBR) = (hair, CyTimeEdge, 6.6f, 0);
        if (edge) _p.StrokeCut(RectangleF.Inflate(hair, -0.6f, -0.6f), CyTimeEdge, 1.25f, 6, 0, 0, 0);
        _p.FillCut(y, CyY, 7, 0, 0, 0);
        float hz = CyHazard(r), pitch = MathF.Max(8, hz * 1.25f);
        CyberHud.Hazard(_p, new RectangleF(y.X + 1, y.Bottom - hz - 1, y.Width - 2, hz), CyInk, pitch, CyStripes(pitch));     // they crawl
    }

    // ================= the shared parts in Cyber =================

    // the caption as a tag at the card's top-left (whatever x the card's layout asked for its caption column: it starts there);
    // the first form that fits. Returns the width it took from x.
    float CyberCaption(float x, RectangleF r, float maxW, string[] forms, uint fill = CyY, uint ink = CyInk)
    {
        float tx = MathF.Abs(x - (r.X + PadX(r))) < 0.5f ? r.X + CyTagX : x;
        float room = maxW + (x - tx);
        foreach (var f in ShortForms(forms))
        {
            if (string.IsNullOrEmpty(f)) continue;
            string s = Upper(f);
            float w = CyberHud.TagWidth(_p, s);
            if (w > room) continue;
            // the boot: the tag wipes in, then types its letters (WristCyberFx.cs)
            CyberHud.Tag(_p, tx, r.Y + CyTagY, s, CyberFx.EaseOut(CyIn(0.16f, 0.08f)), CyIn(0.22f, 0.025f * s.Length), fill, ink);
            return tx + w - x;
        }
        return 0;
    }

    float CyberCaptionWidth(string[] forms, float maxW)
    {
        foreach (var f in ShortForms(forms))
        {
            if (string.IsNullOrEmpty(f)) continue;
            float w = CyberHud.TagWidth(_p, Upper(f));
            if (w <= maxW) return w;
        }
        return -1;
    }

    // caption-style text (a small heading, AM / PM, a unit): monospace, its baseline at 'by'
    void CyberCapText(string s, float x, float by, float w, uint c, TextAlignment al)
    {
        float mw = CyberHud.MonoWidth(_p, s, CyNote);
        if (al == TextAlignment.Trailing) x -= mw;
        CyberHud.MonoAt(_p, s, x, by, MathF.Max(w, mw) + 6, c == Bento.Cap ? CyG : c, CyNote);
    }

    // the caption row: the tag, and the note at the right - monospace capitals, cut to what is left (left out under 36 px)
    void CyberCaptionRow(RectangleF r, float x, float right, string note, uint noteColor, string[] forms, uint tagFill = CyY, string noteShort = null)
    {
        float iw = right - x;
        string n = string.IsNullOrEmpty(note) ? null : Upper(note);
        float nw = n == null ? 0 : CyberHud.MonoWidth(_p, n, CyNote);
        float cw = CyberCaption(x, r, nw > 0 ? MathF.Max(iw * 0.4f, iw - nw - 10) : iw, forms, tagFill);
        if (n == null) return;
        float left = x + (cw > 0 ? cw + 10 : 0), room = right - left;
        if (room < 36) return;
        n = Ellipsize(n, room, CyNote, FontWeight.SemiBold, CyberHud.Mono, noteShort == null ? null : Upper(noteShort));
        uint c = noteColor == Bento.Cap ? CyG : noteColor;
        float k = CyNoteIn(n);                          // typed in: the boot, a new note (WristCyberFx.cs)
        if (k < 1) { CyTyped(n, right, r.Y + CyBase, c, k); return; }
        CyberHud.MonoAt(_p, n, left, r.Y + CyBase, room + 2, c, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
    }

    // a reading's line: segments (it glides between values like the modern line)
    void CyberMeter(RectangleF bar, float frac, uint tint, string key, int n = 0)
    {
        float f = _ui.Spring(key, frac < 0 ? 0 : Math.Clamp(frac, 0, 1), 140, 20, 0.08f) * CyIn(0.28f, 0.3f);     // the boot fills it block by block
        float h = MathF.Max(4, bar.Height);
        CyberHud.Segments(_p, new RectangleF(bar.X, bar.Bottom - h, bar.Width, h), frac < 0 ? 0 : f, tint, CyberHud.Track, n);
    }

    // A button inside a widget (play, +, the opened views' presets): a square with its bottom-right corner cut. The main one (on, in
    // the accent: play, start) is the concept's yellow square with a black glyph; a toggle that is on (a muted speaker) its own colour;
    // the others dark with a cyan outline and a cyan glyph. Springy when pressed, like the modern discs.
    bool CyberPress(string id, RectangleF r, string icon, string label, bool on, bool enabled, float iconSize, bool accentFill, uint fill)
    {
        bool click = _ui.Clickable(id, MinHit(r), out bool hover, out bool held) && enabled;
        if (click) CyTap(id);
        float s = _ui.Spring(Ui.Id(id, "#s"), held && enabled ? 0.9f : 1f, 420, 18);
        var b = new RectangleF(r.X + r.Width * (1 - s) / 2, r.Y + r.Height * (1 - s) / 2, r.Width * s, r.Height * s);
        float cut = MathF.Round(MathF.Min(b.Width, b.Height) * 0.22f);
        uint onFill = accentFill ? CyY : fill;
        uint bg = !enabled ? Theme.WithAlpha(CyberHud.Card2, 0.6f) : on ? onFill : hover ? CyberHud.Card3 : CyberHud.Card2;
        _p.FillCut(b, bg, 0, 0, cut, 0);
        if (!on) _p.StrokeCut(RectangleF.Inflate(b, -0.6f, -0.6f), !enabled ? CyberHud.Line : hover ? CyC : CyberHud.CyanDim, 1.25f, 0, 0, MathF.Max(0, cut - 0.3f), 0);
        uint fg = !enabled ? CyberHud.Faint : on ? (Theme.Luma(onFill) > 0.5f ? CyInk : CyW) : CyC;
        float ls = MathF.Min(18, MathF.Max(SmallText, r.Height * 0.3f));
        if (!string.IsNullOrEmpty(label) && icon != null)
        {
            float tw = _p.MeasureWidth(label, ls, FontWeight.SemiBold);
            float total = iconSize + 8 + tw;
            if (total > b.Width - 16)
            {
                label = Ellipsize(label, b.Width - 32 - iconSize, ls, FontWeight.SemiBold);
                tw = _p.MeasureWidth(label, ls, FontWeight.SemiBold);
                total = iconSize + 8 + tw;
            }
            float x = b.X + (b.Width - total) / 2;
            _p.Icon(icon, new RectangleF(x, b.Y, iconSize, b.Height), fg, iconSize);
            _p.Text(label, new RectangleF(x + iconSize + 8, b.Y, tw + 4, b.Height), on ? fg : CyW, ls, FontWeight.SemiBold);
        }
        else if (icon != null) _p.Icon(icon, b, fg, MathF.Round(iconSize * s));
        else if (label != null)
            _p.Text(Ellipsize(label, b.Width - 12, ls, FontWeight.SemiBold), b, on ? fg : CyW, ls, FontWeight.SemiBold, TextAlignment.Center);
        CyTapFx(id, b, 0, cut);
        return click;
    }

    // ---- small helpers ----

    // a value in the condensed bold, its baseline at 'by'; a trailing % smaller (the concept's "4%"). Returns its width.
    float CyValue(float x, float by, string v, float size, uint c, string key = null)
    {
        if (string.IsNullOrEmpty(v)) return 0;
        float fl = CyFlick(key, v);                     // it changed: a short flicker (WristCyberFx.cs)
        if (_cyU < 99) v = CyberFx.CountUp(v, CyberFx.EaseOut(CyIn(0.26f, 0.32f)));      // the boot: it counts up
        bool pct = v.Length > 1 && v[^1] == '%';
        string num = pct ? CyNum(v) : v;
        float w = _p.MeasureWidth(num, size, FontWeight.Bold, Theme.Display);
        if (fl > 0)
        {
            float o = MathF.Max(1.5f, size * 0.05f) * fl;
            TextAt(num, x - o, by, w + 4, Theme.WithAlpha(0xFFE8483F, 0.8f * fl), size, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
            TextAt(num, x + o, by, w + 4, Theme.WithAlpha(0xFF4FD6DE, 0.8f * fl), size, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
            c = Theme.WithAlpha(c, 1 - 0.3f * fl);
        }
        TextAt(num, x, by, w + 4, c, size, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        if (!pct) return w;
        float ps = MathF.Round(size * 0.6f);
        float pw = _p.MeasureWidth("%", ps, FontWeight.Bold, Theme.Display);
        TextAt("%", x + w + 1, by, pw + 4, c, ps, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        return w + 1 + pw;
    }

    float CyValueW(string v, float size)
    {
        if (string.IsNullOrEmpty(v)) return 0;
        bool pct = v.Length > 1 && v[^1] == '%';
        float w = _p.MeasureWidth(pct ? CyNum(v) : v, size, FontWeight.Bold, Theme.Display);
        return pct ? w + 1 + _p.MeasureWidth("%", MathF.Round(size * 0.6f), FontWeight.Bold, Theme.Display) : w;
    }

    // "86%" -> "86", kept (a reading's value changes twice a second, not every frame)
    readonly Dictionary<string, string> _cyNums = new();
    string CyNum(string v)
    {
        if (_cyNums.TryGetValue(v, out var n)) return n;
        if (_cyNums.Count > 256) _cyNums.Clear();
        return _cyNums[v] = v[..^1];
    }

    // the biggest size (up to max) a value fits maxW in
    float CyFit(string v, float maxW, float max)
    {
        float w100 = CyValueW(v, 100);
        float s = w100 <= 0 ? max : MathF.Min(max, 100 * maxW / w100);
        return MathF.Max(8, MathF.Floor(s * 2) / 2);
    }

    // Trailing: the text ends at x (its box is the w px before it)
    void CyMono(string s, float x, float by, float w, uint c, float size = CyNote, FontWeight wt = FontWeight.SemiBold, TextAlignment al = TextAlignment.Leading) =>
        CyberHud.MonoAt(_p, s, al == TextAlignment.Trailing ? x - w : x, by, w, c, size, wt, al);

    float CyMonoW(string s, float size = CyNote, FontWeight wt = FontWeight.SemiBold) => CyberHud.MonoWidth(_p, s, size, wt);

    // the first form that fits maxW in monospace (null: none)
    string CyMonoFit(string[] forms, float maxW, float size = CyNote, FontWeight wt = FontWeight.SemiBold)
    {
        foreach (var f in forms) if (!string.IsNullOrEmpty(f) && CyMonoW(f, size, wt) <= maxW) return f;
        return null;
    }

    // a level's colour: red when it needs attention (pulsing softly: WristCyberFx.cs), else its normal one
    uint CyLevel(StatLevel l, uint normal) => l == StatLevel.Ok ? normal : CyWarn();

    // the time zone as the concept writes it: "UTC+02:00", "UTC+2", "+2" - made when the offset changes
    TimeSpan _cyOffset = TimeSpan.MaxValue;
    readonly string[] _cyUtc = new string[3];
    string[] UtcForms(DateTime now)
    {
        var o = TimeZoneInfo.Local.GetUtcOffset(now);
        if (o != _cyOffset)
        {
            _cyOffset = o;
            string sign = o < TimeSpan.Zero ? "-" : "+";
            var a = o.Duration();
            _cyUtc[0] = $"UTC{sign}{a.Hours:00}:{a.Minutes:00}";
            _cyUtc[1] = a.Minutes == 0 ? $"UTC{sign}{a.Hours}" : $"UTC{sign}{a.Hours}:{a.Minutes:00}";
            _cyUtc[2] = a.Minutes == 0 ? $"{sign}{a.Hours}" : $"{sign}{a.Hours}:{a.Minutes:00}";
        }
        return _cyUtc;
    }

    // ================= the designs =================

    bool CyberWidget(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d, DateTime now)
    {
        if (k.Button) { CyberButton(id, w, k, r, d); return true; }
        switch (k.Id)
        {
            case "time" when CyberClock(w): CyberTime(r, w, now); return true;
            case "analog" when CyberClock(w): CyberDial(r, now); return true;
            case "date": CyberDate(r, w, now); return true;
            case "calendar" when w.W == 1 && w.H == 1: CyberDate(r, w, now); return true;
            case "battery": CyberBattery(r, w, d, id); return true;
            case "media": CyberMedia(r, w, d, id); return true;
            case "graph": CyberGraph(r, w, d); return true;
            case "fps" or "reproj" or "gpu" or "vram" or "cpu" or "ram":
                if (w.W == 1 && w.H == 1) CyberStat(r, k.Id, d, Ui.Id("wsg", id));
                else CyberWide(r, k.Id, d, Ui.Id("wsg", id));
                return true;
            case "room": CyberRoom(r, w, d, id); return true;
            case "mic": CyberMic(r, w, id); return true;
            case "notifications": CyberInbox(r, d); return true;
            case "buttons": CyberGroup(r, w, d, id); return true;
            case "photo": CyberPhoto(r, w, d, id); return true;
            case "memory": CyberPhoto(r, w, d, id); return true;
            case "weather": CyberWeather(r, w, d, id); return true;
            case "friends": CyberFriends(r, w, d, id); return true;
            case "recap": CyberRecap(r, w, d, id); return true;
            case "light": CyberLight(r, w, d, id); return true;
            case "rgb": CyberRgb(r, w, d, id); return true;
        }
        return false;
    }

    // ---- the time: the yellow card ----
    // One row: the zone at the top right, the digits big, the seconds small on their baseline. Two rows and up: the TIME tag (black on
    // the yellow), the digits huge, "SEC 32" and the date along the bottom. A 1x1: the seconds over the digits. Black digits with a red
    // and a cyan copy a little off them, a slice through their middle shifted sideways - drawn the same every time (no flicker).
    // hero: a preset face's big card (WristCyberFaces.cs) - the digits centred, the date along the bottom a black tag
    void CyberTime(RectangleF r, WatchWidget w, DateTime now, bool hero = false)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        string t = Clk(now, h24 ? "HH:mm" : "h:mm", culture), sec = Clk(now, "ss", culture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        Words(now);
        var y = CyTimeRect(r);
        float hz = CyHazard(r), pad = PadX(r), x = r.X + pad, right = y.Right - pad, iw = right - x;
        float floor = y.Bottom - hz - 1 - 7;              // the lowest ink: 7 px over the stripes
        bool small = w.W == 1 && w.H == 1, tall = r.Height >= 150;
        float cap = CyCap, top, bottom;
        string widest = h24 ? "00:00" : "12:00";
        float w100 = _p.MeasureWidth(widest, 100, FontWeight.Bold, Theme.Display);
        float sideW = 0, secS = 0;
        if (tall)
        {
            // the tag, the zone; along the bottom the seconds and the date
            float tw = CyberCaption(r.X + pad, r, iw * 0.5f, CapTime, CyInk, CyY);
            var zone = CyMonoFit(UtcForms(now), right - (x + tw + 12), CyNote, FontWeight.Bold);
            if (zone != null) CyMono(zone, right, r.Y + CyBase, CyMonoW(zone, CyNote, FontWeight.Bold) + 2, CyInk, CyNote, FontWeight.Bold, TextAlignment.Trailing);
            float bs = r.Height >= 300 ? 18 : 16;
            string secText = Lang.T("SEC ") + sec;
            float sw = CyMonoW(secText, bs, FontWeight.Bold);
            CyMono(secText, x, floor, sw + 2, CyInk, bs, FontWeight.Bold);
            string date = CyMonoFit(CyDateForms(now), iw - sw - 16 - (hero ? 2 * CyberHud.TagPad + CyberHud.TagSlant : 0), bs, FontWeight.Bold);
            if (date != null && hero)
            {
                // its text on the seconds' baseline (the boot wipes it in and types it, as a caption)
                float th = MathF.Round(bs * 1.34f), tgw = CyberHud.TagWidth(_p, date, bs);
                CyberHud.Tag(_p, right - tgw, floor - (th + CyberHud.CapH(_p, bs, FontWeight.Bold)) / 2, date, CyberFx.EaseOut(CyIn(0.2f, 0.08f)),
                    CyIn(0.26f, 0.02f * date.Length), CyInk, CyY, bs, th);
            }
            else if (date != null) CyMono(date, right, floor, iw, CyInk, bs, FontWeight.Bold, TextAlignment.Trailing);
            top = r.Y + 36;
            bottom = floor - CyberHud.CapH(_p, bs, FontWeight.Bold) - (hero ? 18 : 12);      // (the date tag reaches above its text)
            if (ampm != null) { secS = bs; sideW = CyMonoW(ampm, bs, FontWeight.Bold) + 10; }
        }
        else if (small)
        {
            // the seconds (and AM / PM) along the top
            CyMono(sec, right, r.Y + CyBase, 40, CyInk, CyNote, FontWeight.Bold, TextAlignment.Trailing);
            if (ampm != null) CyMono(ampm, x, r.Y + CyBase, 40, CyInk, CyNote, FontWeight.Bold);
            top = r.Y + 32;
            bottom = floor;
        }
        else
        {
            // the zone at the top right; the seconds (with AM / PM over them) after the digits
            var zone = CyMonoFit(UtcForms(now), iw, CyNote, FontWeight.Bold);
            if (zone != null) CyMono(zone, right, r.Y + CyBase, iw, CyInk, CyNote, FontWeight.Bold, TextAlignment.Trailing);
            top = r.Y + 32;
            bottom = floor;
            secS = _big ? 22 : 18;
            sideW = MathF.Max(CyMonoW("00", secS, FontWeight.Bold), ampm == null ? 0 : CyMonoW(ampm, CyNote, FontWeight.Bold)) + 8;
        }
        // the offset copies and the slice reach 2.4 offsets past the digits (CyGlitch: at most 10 px)
        float size = MathF.Min((bottom - top) / cap, 100 * (iw - sideW - 4) / (w100 + 7.5f));
        size = MathF.Max(16, MathF.Floor(size * 2) / 2);
        float slack = MathF.Max(8, MathF.Min(12, MathF.Ceiling(size * 0.075f)));
        size = MathF.Max(16, MathF.Floor(MathF.Min(size, 100 * (iw - sideW - slack) / w100) * 2) / 2);
        float by = top + (bottom - top + size * cap) / 2;  // the digits centred between the two
        var (g, seed) = CyTimeGlitch(now);              // the boot, a new minute: it glitches (WristCyberFx.cs)
        float dx = !hero ? x : x + MathF.Max(0, (iw - (sideW > 0 ? sideW + slack : 0) - _p.MeasureWidth(t, size, FontWeight.Bold, Theme.Display)) / 2);
        float dw = CyGlitch(t, dx, by, size, y, g, seed);
        if (sideW > 0 && !small)
        {
            float sx = dx + dw + slack;
            if (!tall) CyMono(sec, sx, by, sideW, CyInk, secS, FontWeight.Bold);
            if (ampm != null) CyMono(ampm, sx, by - size * cap + CyberHud.CapH(_p, tall ? secS : CyNote, FontWeight.Bold), sideW, CyInk,
                tall ? secS : CyNote, FontWeight.Bold);
        }
    }

    // the date along the bottom of a tall time card, longest first: "FRI 16 OCT 2026", "16 OCT 2026", "16 OCT"
    DateTime _cyDateDay;
    CultureInfo _cyDateCulture;
    readonly string[] _cyDate = new string[3];
    string[] CyDateForms(DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        if (now.Date != _cyDateDay || !ReferenceEquals(culture, _cyDateCulture))
        {
            _cyDateDay = now.Date;
            _cyDateCulture = culture;
            string ddd = Upper(now.ToString("ddd", culture)).TrimEnd('.'), mmm = Month3(now);
            _cyDate[0] = $"{ddd} {now.Day} {mmm} {now.Year}";
            _cyDate[1] = $"{now.Day} {mmm} {now.Year}";
            _cyDate[2] = $"{now.Day} {mmm}";
        }
        return _cyDate;
    }

    // a month's first three letters, UPPERCASE ("OCT", "SZE")
    static string Month3(DateTime now)
    {
        string m = now.ToString("MMMM", CultureInfo.CurrentCulture);
        return Upper(m.Length > 3 ? m[..3] : m);
    }

    // The digits black with a red copy up-left and a cyan copy down-right (see-through: the contrast check reads the black), then a
    // slice through their middle shifted right. Returns the digits' width.
    // g > 0: glitching (CyTimeGlitch) - the split wider and jittered, the slice further, up to three more slices shifted sideways,
    // all of it kept in the card; seed: the step (the same picture for the same step)
    float CyGlitch(string s, float x, float by, float size, RectangleF card, float g = 0, int seed = 0)
    {
        float o = Math.Clamp(MathF.Round(size * 0.03f * 2) / 2, 1.5f, 4);
        var (b, h) = _p.TextLine(size, FontWeight.Bold, Theme.Display);
        float w = _p.MeasureWidth(s, size, FontWeight.Bold, Theme.Display);
        bool glitch = g > 0.01f;
        float om = glitch ? 1 + 1.3f * g * (0.4f + CyberFx.Hash(seed, 1)) : 1, jx = glitch ? (CyberFx.Hash(seed, 2) - 0.5f) * 5 * g : 0;
        void Layers(float dx)
        {
            var at = new RectangleF(x + dx, by - b, w + 6, h);
            float oo = o * om;
            _p.Text(s, new RectangleF(at.X - oo, at.Y - oo * 0.5f, at.Width, at.Height), 0xD9E8483F, size, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Near, Theme.Display);
            _p.Text(s, new RectangleF(at.X + oo, at.Y + oo * 0.5f, at.Width, at.Height), 0xD94FD6DE, size, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Near, Theme.Display);
            _p.Text(s, at, CyInk, size, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Near, Theme.Display);
        }
        if (glitch) _p.PushClip(RectangleF.Inflate(card, -1, -1));
        Layers(jx);
        // the slice: a band at 45 % of the digits' height, its copy pushed right
        float cap = size * CyCap, bh = MathF.Max(2, MathF.Round(size * 0.06f)), sy = MathF.Round(by - cap * 0.52f);
        var band = new RectangleF(x - o - 1, sy, w + 2 * o + 6, bh);
        _p.PushClip(band);
        _p.Fill(band, CyY);
        Layers(MathF.Max(2, o * 1.4f) + (glitch ? (CyberFx.Hash(seed, 3) - 0.3f) * 8 * g : 0));
        _p.PopClip();
        if (glitch)
        {
            for (int i = 0; i < 3; i++)
            {
                if (CyberFx.Hash(seed, 10 + i) > 0.25f + 0.6f * g) continue;
                float sh = MathF.Max(2, MathF.Round(cap * (0.05f + 0.13f * CyberFx.Hash(seed, 20 + i))));
                var sl = new RectangleF(card.X, MathF.Round(by - cap * (0.05f + 0.9f * CyberFx.Hash(seed, 30 + i))), card.Width, sh);
                _p.PushClip(sl);
                _p.Fill(sl, CyY);
                Layers(jx + (CyberFx.Hash(seed, 40 + i) - 0.5f) * 16 * g);
                _p.PopClip();
            }
            _p.PopClip();
        }
        return w;
    }

    // ---- the HUD dial ----
    // The tag and the zone over it from 2x2; the dial as big as the card allows under them (a 1x1: the dial alone, no numerals)
    void CyberDial(RectangleF r, DateTime now)
    {
        RectangleF box;
        bool small = r.Width < 140 || r.Height < 140;
        if (!small)
        {
            float pad = PadX(r);
            CyberCaptionRow(r, r.X + pad, r.Right - pad, UtcForms(now)[1], CyG, CapClock);
            box = RectangleF.FromLTRB(r.X + 10, r.Y + 31, r.Right - 10, r.Bottom - 10);
        }
        else box = RectangleF.Inflate(r, -9, -9);
        float ra = MathF.Min(box.Width, box.Height) / 2;
        float aw = MathF.Max(2, MathF.Round(ra * 0.03f * 2) / 2);
        float rad = (ra - aw / 2 - 0.5f) / 1.07f;
        CyberDialFace(box.X + box.Width / 2, box.Y + box.Height / 2, rad, now, aw);
    }

    // the dial: a disc a touch darker than the card in a thin ring; the minute ticks grey, the five-minute ones white, the quarters
    // yellow blocks; 00 / 15 / 30 / 45 (big enough dials); the hour hand a yellow taper, the minute hand white, the second hand red with
    // an arrow head, a red diamond at the pivot; outside, a red arc of the minute so far
    void CyberDialFace(float cx, float cy, float rad, DateTime now, float aw)
    {
        _p.Circle(cx, cy, rad, CyDial);
        _p.Ring(cx, cy, rad, CyRing, MathF.Max(1.2f, rad * 0.014f));
        for (int i = 0; i < 60; i++)
        {
            float a = i / 60f * MathF.Tau, dx = MathF.Sin(a), dy = -MathF.Cos(a);
            float r0, r1, lw;
            uint c;
            if (i % 15 == 0) { r0 = rad * 0.79f; r1 = rad * 0.93f; lw = MathF.Max(3, rad * 0.07f); c = CyY; }
            else if (i % 5 == 0) { r0 = rad * 0.82f; r1 = rad * 0.93f; lw = MathF.Max(1.5f, rad * 0.022f); c = CyW; }
            else if (rad >= 45) { r0 = rad * 0.88f; r1 = rad * 0.93f; lw = MathF.Max(1, rad * 0.011f); c = CyTick; }
            else continue;
            _p.Line(cx + dx * r0, cy + dy * r0, cx + dx * r1, cy + dy * r1, c, lw);
        }
        if (rad >= 70)
        {
            float ns = MathF.Max(14, MathF.Round(rad * 0.14f));
            float nb = CyberHud.CapH(_p, ns) / 2;
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * MathF.Tau, nx = cx + rad * 0.6f * MathF.Sin(a), ny = cy - rad * 0.6f * MathF.Cos(a);
                CyMono(CyQuarters[i], nx - 30, ny + nb, 60, CyNumeral, ns, FontWeight.SemiBold, TextAlignment.Center);
            }
        }
        var (ht, mt, st) = ClockArt.Turns(now);
        if (_cyU < 99) { float e = CyberFx.EaseOut(CyIn(0.18f, 0.4f)); ht *= e; mt *= e; st *= e; }     // the boot: the hands sweep to the time
        if (st > 0) _p.Arc(cx, cy, rad * 1.07f, 0, st, CyR, aw);
        // the hour hand: a yellow taper from a short tail
        {
            float a = ht * MathF.Tau, dx = MathF.Sin(a), dy = -MathF.Cos(a), nx = -dy, ny = dx;
            float f = -rad * 0.08f, to = rad * 0.52f, w0 = MathF.Max(5, rad * 0.085f), w1 = MathF.Max(2, rad * 0.03f);
            var hp = _cyHand;          // (the same points in a kept array: no array a frame)
            hp[0] = new Vector2(cx + dx * f + nx * w0 / 2, cy + dy * f + ny * w0 / 2); hp[1] = new Vector2(cx + dx * to + nx * w1 / 2, cy + dy * to + ny * w1 / 2);
            hp[2] = new Vector2(cx + dx * to - nx * w1 / 2, cy + dy * to - ny * w1 / 2); hp[3] = new Vector2(cx + dx * f - nx * w0 / 2, cy + dy * f - ny * w0 / 2);
            _p.Polygon(hp, CyY);
        }
        {
            float a = mt * MathF.Tau, dx = MathF.Sin(a), dy = -MathF.Cos(a);
            _p.Line(cx - dx * rad * 0.06f, cy - dy * rad * 0.06f, cx + dx * rad * 0.8f, cy + dy * rad * 0.8f, CyW, MathF.Max(2, rad * 0.024f));
        }
        {
            float a = st * MathF.Tau, dx = MathF.Sin(a), dy = -MathF.Cos(a), nx = -dy, ny = dx;
            float tip = rad * 0.88f, head = MathF.Max(5, rad * 0.1f), hw = MathF.Max(2.5f, rad * 0.035f);
            _p.Line(cx - dx * rad * 0.16f, cy - dy * rad * 0.16f, cx + dx * (tip - head * 0.6f), cy + dy * (tip - head * 0.6f), CyR, MathF.Max(1.2f, rad * 0.013f));
            var tp = _cyTip;
            tp[0] = new Vector2(cx + dx * tip, cy + dy * tip); tp[1] = new Vector2(cx + dx * (tip - head) + nx * hw, cy + dy * (tip - head) + ny * hw);
            tp[2] = new Vector2(cx + dx * (tip - head) - nx * hw, cy + dy * (tip - head) - ny * hw);
            _p.Polygon(tp, CyR);
        }
        float dm = MathF.Max(4, rad * 0.065f);
        var dia = _cyDia;
        dia[0] = new Vector2(cx, cy - dm); dia[1] = new Vector2(cx + dm, cy); dia[2] = new Vector2(cx, cy + dm); dia[3] = new Vector2(cx - dm, cy); dia[4] = new Vector2(cx, cy - dm);
        _p.Polygon(dia, 4, CyDial);
        _p.Polyline(dia, 5, CyR, MathF.Max(1.5f, rad * 0.02f));
    }

    static readonly string[] CyQuarters = { "00", "15", "30", "45" };
    readonly Vector2[] _cyHand = new Vector2[4], _cyTip = new Vector2[3], _cyDia = new Vector2[5];      // the dial's shapes, filled each frame

    // ---- the date: the day big in yellow, the month and the weekday beside it ----
    void CyberDate(RectangleF r, WatchWidget w, DateTime now)
    {
        float pad = PadX(r), x = r.X + pad, right = r.Right - pad, iw = right - x;
        CyberCaption(x, r, iw, CapDate);
        CyDateBody(x, right, r.Y + 34, r.Bottom - 12, w.W == 1, now);
    }

    // the date's lines from x to right, the day's digits between top and bottom (its baseline) - the date card's, the analog face's clock's
    void CyDateBody(float x, float right, float top, float bottom, bool s1, DateTime now)
    {
        Words(now);
        string day = _dayNum, wd = Upper(_capWeekday[1]).TrimEnd('.');
        float cap = CyCap;
        // the day as tall as the space allows (the concept's: about 40 px of digit on a one-row card)
        float ds = MathF.Floor(MathF.Min(bottom - top, _big ? 64 : s1 ? 34 : 42) / cap);
        float dw = _p.MeasureWidth(day, ds, FontWeight.Bold, Theme.Display);
        float digitTop = bottom - ds * cap;
        TextAt(day, x, bottom, dw + 4, CyY, ds, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        float mx = x + dw + (s1 ? 7 : 12), mw = right - mx;
        string month = s1 ? Month3(now) : Upper(_capMonth[0]);
        float ms = FitSize(month, mw, s1 ? 18 : _big ? 30 : 26, FontWeight.Bold, Theme.Display);
        if (!s1 && ms < 17) { month = Month3(now); ms = FitSize(month, mw, 26, FontWeight.Bold, Theme.Display); }
        ms = MathF.Max(14, ms);
        TextAt(month, mx, digitTop + ms * cap, mw + 2, CyW, ms, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        // the weekday in cyan, the week (and the year where it fits) grey after it, on the day's baseline
        float wdw = CyMonoW(wd, CyNote, FontWeight.Bold);
        CyMono(wd, mx, bottom, wdw + 2, CyC, CyNote, FontWeight.Bold);
        if (s1) return;
        string[] rest = { Lang.F(" · W{0} · {1}", ISOWeek.GetWeekOfYear(now), now.Year), Lang.F(" · W{0}", ISOWeek.GetWeekOfYear(now)) };
        string more = CyMonoFit(rest, mw - wdw);
        if (more != null) CyMono(more, mx + wdw, bottom, mw - wdw + 2, CyG);
    }

    // ---- a reading as a 1x1: the tag, the value big (its unit grey after it), five segments ----
    void CyberStat(RectangleF r, string id, PanelData d, string key)
    {
        var (frac, value, _, _, lv) = Reading(id, d);
        bool none = value == "–";
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        CyberCaption(x, r, iw + pad - CyTagX, SmallCaption(id));
        string unit = none ? null : id switch { "fps" => FpsUnit(d.Vr.DisplayHz), "gpu" => "ms", "vram" or "ram" => "GB", _ => null };
        float barH = _big ? 6 : 5, barY = r.Bottom - (_big ? 18 : 15) - barH;
        float by = barY - (_big ? 13 : 9), top = r.Y + 33;
        float uw = unit == null ? 0 : CyMonoW(unit) + 3;
        float vs = MathF.Min((by - top) / CyCap, _big ? 58 : 40);
        float fit = CyFit(value, iw - uw, vs);
        if (fit < 24 && uw > 0) { uw = 0; fit = CyFit(value, iw, vs); }
        uint role = id is "vram" or "ram" ? CyC : CyY;
        float vw = CyValue(x, by, value, fit, none ? CyberHud.Faint : CyLevel(lv, CyW), key);
        if (uw > 0) CyMono(unit, x + vw + 3, by, uw + 2, CyG);
        CyberMeter(new RectangleF(x, barY, iw, barH), frac, none ? CyberHud.Faint : CyLevel(lv, role), key, 5);
    }

    // ---- a reading on a wider card: the tag and its note, the value bigger, ten segments ----
    void CyberWide(RectangleF r, string id, PanelData d, string key)
    {
        var (frac, value, unit, caption, lv) = Reading(id, d);
        bool none = value == "–";
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        CyberCaptionRow(r, x, r.Right - pad, none ? null : caption, CyG, WideCaption(id));
        string u = none || unit is not ("fps" or "ms" or "GB") ? null : unit;
        if (id == "fps" && !none) u = FpsUnit(d.Vr.DisplayHz);
        float barH = _big ? 7 : 6, barY = r.Bottom - (_big ? 18 : 15) - barH;
        float by = barY - (_big ? 13 : 9), top = r.Y + 33;
        float uw = u == null ? 0 : CyMonoW(u) + 4;
        float vs = CyFit(value, iw - uw, MathF.Min((by - top) / CyCap, _big ? 62 : 42));
        uint role = id is "vram" or "ram" ? CyC : CyY;
        float vw = CyValue(x, by, value, vs, none ? CyberHud.Faint : CyLevel(lv, CyW), key);
        if (uw > 0) CyMono(u, x + vw + 4, by, uw + 2, CyG);
        CyberMeter(new RectangleF(x, barY, iw, barH), frac, none ? CyberHud.Faint : CyLevel(lv, role), key);
    }

    // ---- the batteries: POWER ----
    // A 1x1: the emptiest, big. Two rows and up: a row each - the name, the percentage, ten segments. One row wider: a column each - the
    // name, the percentage, a short line. Cyan above half, yellow below, red when low (Orbit's battery rule).
    uint CyBattery(float pct, StatLevel lv) => lv != StatLevel.Ok ? CyWarn() : pct >= 0.5f ? CyC : CyY;

    int _cyDevN = -1, _cyDevLang = -1;
    string _cyDev;
    string DevNote(int n)
    {
        if (n != _cyDevN || _cyDevLang != Lang.Version) { _cyDevN = n; _cyDevLang = Lang.Version; _cyDev = n <= 0 ? null : Lang.F("{0} DEV", n); }
        return _cyDev;
    }

    void CyberBattery(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var bats = d.Vr.Batteries;
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad, right = r.Right - pad;
        bool one = w.W == 1 && w.H == 1;
        if (one)
        {
            CyberCaption(x, r, iw + pad - CyTagX, CapPower);
            float barY0 = r.Bottom - (_big ? 18 : 15) - (_big ? 6 : 5), by0 = barY0 - (_big ? 13 : 9);
            var bar0 = new RectangleF(x, barY0, iw, _big ? 6 : 5);
            if (bats.Count == 0)
            {
                CyValue(x, by0, "–", 34, CyberHud.Faint);
                CyberMeter(bar0, -1, CyberHud.Faint, Ui.Id("wbt", id * 8), 5);
                return;
            }
            var b = bats[Emptiest(bats)];
            int shown = ShownPct(b.pct);
            var lv = BatteryLevel(shown);
            string pct = Pct(shown), name = Upper(Bat(b.label));
            float nw = CyMonoW(name) + (b.charging ? 14 : 0);
            float vs = CyFit(pct, iw - nw - 4, MathF.Min((by0 - r.Y - 33) / CyCap, _big ? 58 : 40));
            if (vs < 24) { nw = 0; vs = CyFit(pct, iw, MathF.Min((by0 - r.Y - 33) / CyCap, 40)); }
            float vw = CyValue(x, by0, pct, vs, lv == StatLevel.Ok ? CyW : CyWarn(), Ui.Id("wbt", id * 8));
            if (nw > 0)
            {
                float nx = right - nw;
                if (b.charging) { _p.Icon(Bolt, new RectangleF(nx - 2, by0 - 13, 14, 15), CyC, 12); nx += 12; }
                CyMono(name, nx, by0, right - nx + 2, CyG);
            }
            CyberMeter(bar0, b.pct, CyBattery(b.pct, lv), Ui.Id("wbt", id * 8), 5);
            return;
        }
        CyberCaptionRow(r, x, right, DevNote(bats.Count), CyG, CapPower);
        if (bats.Count == 0)
        {
            CyMono(d.VrConnected ? "NO BATTERY INFO" : "STEAMVR OFF", x, r.Y + 62, iw, CyG);
            return;
        }
        float top = r.Y + 34, bottom = r.Bottom - 12;
        if (bottom - top >= 2 * 36)
        {
            // rows (two columns when they do not fit one)
            const float gapX = 16;
            int maxRows = Math.Max(1, (int)((bottom - top) / 36)), maxCols = Math.Max(1, (int)((iw + gapX) / (110 + gapX)));
            var pick = PickBatteries(bats, Math.Min(maxRows * maxCols, 8));
            int n = pick.Count, cols = 1;
            while ((n + cols - 1) / cols > maxRows) cols++;
            int rows = (n + cols - 1) / cols;
            float cw = (iw - (cols - 1) * gapX) / cols, rh = MathF.Min(_big ? 54 : 46, (bottom - top) / rows);
            for (int j = 0; j < n; j++)
                CyBatteryRow(new RectangleF(x + j % cols * (cw + gapX), top + j / cols * rh, cw, rh), bats[pick[j]], Ui.Id("wbt", id * 8 + j));
            return;
        }
        // columns: as many as fit 40 px each (the emptiest ones)
        {
            const float gapX = 8;
            int maxCols = Math.Max(1, Math.Min(4, (int)((iw + gapX) / (34 + gapX))));
            var pick = PickBatteries(bats, maxCols);
            int n = pick.Count;
            float cw = (iw - (n - 1) * gapX) / n;
            float barY = r.Bottom - (_big ? 18 : 14) - 4, vb = barY - 6, nb = r.Y + (_big ? 56 : 49);
            for (int j = 0; j < n; j++)
            {
                var b = bats[pick[j]];
                float cx = x + j * (cw + gapX);
                int shown = ShownPct(b.pct);
                var lv = BatteryLevel(shown);
                string name = Upper(Bat(b.label));
                float nx = cx;
                if (b.charging) { _p.Icon(Bolt, new RectangleF(nx - 2, nb - 13, 14, 15), CyC, 12); nx += 12; }
                if (CyMonoW(name) > cx + cw - nx) name = Upper(Bat(b.label).Split('×')[0]);
                if (CyMonoW(name) <= cx + cw - nx) CyMono(name, nx, nb, cx + cw - nx + 2, CyG);
                string pct = Pct(shown);
                float vs = CyFit(pct, cw, _big ? 34 : MathF.Min(26, (vb - nb - 6) / CyCap));
                CyValue(cx, vb, pct, vs, lv == StatLevel.Ok ? CyW : CyWarn(), Ui.Id("wbt", id * 8 + j));
                float f = _ui.Spring(Ui.Id("wbt", id * 8 + j), Math.Clamp(b.pct, 0, 1), 140, 20, 0.08f) * CyIn(0.28f, 0.3f);
                _p.Fill(new RectangleF(cx, barY, cw, 4), CyberHud.Track);
                if (f > 0.005f) _p.Fill(new RectangleF(cx, barY, MathF.Max(3, cw * f), 4), CyBattery(b.pct, lv));
            }
        }
    }

    // a device as a row: its name (a cyan bolt in front while charging), the percentage at the right, ten segments under both
    void CyBatteryRow(RectangleF c, (string label, float pct, bool charging) b, string key)
    {
        int shown = ShownPct(b.pct);
        var lv = BatteryLevel(shown);
        float barH = 6, ts = MathF.Min(_big ? 26 : 22, MathF.Max(17, (c.Height - 18) / CyCap * 0.85f));
        float barY = c.Bottom - 4 - barH, by = barY - 7;
        string pct = Pct(shown);
        float pw = CyValueW(pct, ts);
        CyValue(c.Right - pw, by, pct, ts, lv == StatLevel.Ok ? CyW : CyWarn(), key);
        float nx = c.X, ns = MathF.Max(17, ts - 4);
        if (b.charging) { float bs = MathF.Min(14, ns - 4); _p.Icon(Bolt, new RectangleF(nx - 2, by - bs - 1, bs + 4, bs + 2), CyC, bs); nx += bs + 4; }
        float room = c.Right - pw - 10 - nx;
        TextAt(Ellipsize(Upper(Bat(b.label)), room, ns, FontWeight.SemiBold, Theme.Display), nx, by, room + 2, CyW, ns, FontWeight.SemiBold,
            TextAlignment.Leading, Theme.Display);
        CyberMeter(new RectangleF(c.X, barY, c.Width, barH), b.pct, CyBattery(b.pct, lv), key, Math.Max(5, Math.Min(12, (int)(c.Width / 17))));
    }

    // ---- now playing: AUDIO ----
    // The tag with PLAYING / PAUSED in cyan at the right; the cover in a dark-red box (the concept's cyan equalizer without one), the
    // title in the condensed capitals, the artist and where in the track in monospace grey, a yellow line with its marker; play / pause
    // the yellow square at the right (previous / next beside it on the widest).
    void CyberMedia(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var (has, title, artist, playing) = MediaNow(d);
        var (tok, pos, dur) = Timeline(d);
        tok &= has && dur > 0;
        float pad = PadX(r), x = r.X + pad, right = r.Right - pad;
        bool tall = r.Height >= 150, s1 = w.W == 1;
        string state = !has ? "Idle" : playing ? "Playing" : "Paused";
        CyberCaptionRow(r, x, right, state, has && playing ? CyC : CyG, CapAudio);
        string play = playing ? PauseFill : PlayFill;
        var ids = MediaIds;
        float top = r.Y + 33, bottom = r.Bottom - 10;
        if (tall)
        {
            // the cover big on the left, the text over previous / play / next on the right
            float s = MathF.Min(bottom - top, r.Width * 0.42f);
            if (r.Width - 2 * pad - s - 14 < 170) s = MathF.Max(0, r.Width - 2 * pad - 14 - 170);
            float tx = x;
            if (s >= 60) { CyCover(new RectangleF(x, top + (bottom - top - s) / 2, s, s)); tx = x + s + 14; }
            float tw = right - tx;
            const float big = 52, sm = 42, gap = 10;
            float bby = bottom - big;
            if (CyberPress(Ui.Id("wcm", id * 3), new RectangleF(tx, bby + (big - sm) / 2, sm, sm), "\uE892", null, false, has, 18, true, 0)) Act(ids[0]);
            if (CyberPress(Ui.Id("wcm", id * 3 + 1), new RectangleF(tx + sm + gap, bby, big, big), play, null, true, has, 20, true, 0)) Act(ids[1]);
            if (CyberPress(Ui.Id("wcm", id * 3 + 2), new RectangleF(tx + sm + gap + big + gap, bby + (big - sm) / 2, sm, sm), "\uE893", null, false, has, 18, true, 0))
                Act(ids[2]);
            float ty = top + 22;
            if (!has) { CyMono("NOT PLAYING", tx, ty, tw, CyG); return; }
            float ts = _big ? 26 : 24;
            var (l1, l2) = TwoLines(Upper(title ?? ""), tw, ts, FontWeight.Bold);
            TextAt(l1, tx, ty, tw + 2, CyW, ts, FontWeight.Bold);
            if (l2.Length > 0) { ty += ts + 4; TextAt(l2, tx, ty, tw + 2, CyW, ts, FontWeight.Bold); }
            CyMediaSub(tx, ty + 24, tw, artist, tok, pos, dur);
            if (tok) CyProgress(new RectangleF(tx, ty + 38, tw, 3), pos / dur);
            return;
        }
        // one row: play at the right (previous / next too on a six-wide card), the cover box from three cells
        float bs = _big ? 52 : 44, mid = top + (bottom - top) / 2;
        float bx = right - bs;
        if (CyberPress(Ui.Id("wcm", id * 3 + 1), new RectangleF(bx, mid - bs / 2, bs, bs), play, null, true, has, 20, true, 0)) Act(ids[1]);
        if (w.W >= 6)
        {
            const float sm = 38, gap = 8;
            bx -= gap + sm;
            if (CyberPress(Ui.Id("wcm", id * 3 + 2), new RectangleF(bx, mid - sm / 2, sm, sm), "\uE893", null, false, has, 16, true, 0)) Act(ids[2]);
            bx -= gap + sm;
            if (CyberPress(Ui.Id("wcm", id * 3), new RectangleF(bx, mid - sm / 2, sm, sm), "\uE892", null, false, has, 16, true, 0)) Act(ids[0]);
        }
        float x0 = x;
        if (w.W >= 3)
        {
            float s = bottom - top - 2;
            CyCover(new RectangleF(x, top + 1, s, s));
            x0 = x + s + 12;
        }
        float tw2 = bx - 12 - x0;
        if (tw2 < 40) return;
        if (s1)
        {
            // a 4 x 3 S: the title over two lines beside nothing - the play square is under it, at the right
            if (!has) { CyMono("IDLE", x0, top + 22, tw2, CyG); return; }
            var (a1, a2) = TwoLines(Upper(title ?? ""), r.Width - 2 * pad, 17, FontWeight.Bold);
            TextAt(a1, x, top + 18, r.Width - 2 * pad + 2, CyW, 17, FontWeight.Bold);
            if (a2.Length > 0) TextAt(a2, x, top + 38, r.Width - 2 * pad + 2, CyW, 17, FontWeight.Bold);
            return;
        }
        if (!has) { CyMono("NOT PLAYING", x0, top + 24, tw2, CyG); return; }
        float tsz = MathF.Max(17, FitSize(Upper(title ?? ""), tw2, _big ? 26 : 22, FontWeight.Bold, Theme.Display));
        TextAt(Ellipsize(Upper(title ?? ""), tw2, tsz, FontWeight.Bold, Theme.Display), x0, top + (_big ? 28 : 21), tw2 + 2, CyW, tsz, FontWeight.Bold,
            TextAlignment.Leading, Theme.Display);
        CyMediaSub(x0, top + (_big ? 52 : 39), tw2, artist, tok, pos, dur);
        if (tok) CyProgress(new RectangleF(x0, top + (_big ? 64 : 46), tw2, 3), pos / dur);
    }

    // the artist and the time in the track ("02:14 / 04:03" at the right) in monospace grey; the artist cut to leave the time whole
    void CyMediaSub(float x, float by, float w, string artist, bool tok, double pos, double dur)
    {
        string time = tok ? TimelineText(pos, dur) : null;
        float tw = time == null ? 0 : CyMonoW(time);
        if (time != null && tw <= w) CyMono(time, x + w, by, tw + 2, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
        else tw = -12;
        float aw = w - tw - 12;
        string a = Upper(artist ?? "");
        if (a.Length > 0 && aw >= 30) CyMono(Ellipsize(a, aw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, by, aw + 2, CyG);
    }

    // the line under the title: yellow so far, a tick at where it is
    void CyProgress(RectangleF bar, double frac)
    {
        float f = (float)Math.Clamp(frac, 0, 1) * CyberFx.EaseOut(CyIn(0.28f, 0.3f));
        _p.Fill(bar, CyberHud.Track);
        _p.Fill(new RectangleF(bar.X, bar.Y, bar.Width * f, bar.Height), CyY);
        _p.Fill(new RectangleF(bar.X + bar.Width * f - 1, bar.Y - 3, 2, bar.Height + 6), CyY);
    }

    // the cover in its dark-red box; without one the concept's equalizer: cyan bars leaning like the segments
    static readonly float[] EqBars = { 0.5f, 0.85f, 0.6f, 0.95f, 0.4f };
    readonly Vector2[] _eq = new Vector2[20];

    void CyCover(RectangleF box)
    {
        _p.Fill(box, CyberHud.Card2);
        if (_art != null)
        {
            _p.PushClip(box);
            _p.Image(_art, RectangleF.Inflate(box, -2, -2));
            _p.PopClip();
        }
        else
        {
            float bw = box.Width * 0.09f, gap = box.Width * 0.06f, h = box.Height * 0.5f, lean = bw * 0.6f;
            float x0 = box.X + (box.Width - (5 * bw + 4 * gap + lean)) / 2, base0 = box.Y + box.Height * 0.75f;
            int k = 0;
            for (int i = 0; i < 5; i++)
            {
                float bx = x0 + i * (bw + gap), bh = h * EqBars[i] * MathF.Max(0.08f, CyberFx.EaseOut(CyIn(0.2f + 0.04f * i, 0.2f)));
                _eq[k++] = new Vector2(bx + lean, base0 - bh);
                _eq[k++] = new Vector2(bx + lean + bw, base0 - bh);
                _eq[k++] = new Vector2(bx + bw, base0);
                _eq[k++] = new Vector2(bx, base0);
            }
            _p.Quads(_eq, 5, CyC);
        }
        _p.Stroke(RectangleF.Inflate(box, -0.6f, -0.6f), CyberHud.Edge, 1.25f);
    }

    // ---- the frames: FRAMES ----
    // The note "86 FPS · 9.3 MS · 4%" (the numbers white, red with the worst of their levels); the frames as grey bars - the slowest
    // of each slot - with red spikes where one was flagged or over budget; the budget a dashed yellow line with its value at the right
    void CyberGraph(RectangleF r, WatchWidget w, PanelData d)
    {
        var v = d.Vr;
        float pad = PadX(r), x = r.X + pad, right = r.Right - pad, iw = right - x;
        bool tall = r.Height >= 150;
        float hz = v.DisplayHz, budget = 1000f / Math.Max(1, hz);
        float cw = CyberCaption(x, r, iw * 0.45f, CapFrames);
        CyFrameNote(r, x + cw + 12, right, v);
        var g = RectangleF.FromLTRB(x, r.Y + 37, right, tall ? r.Bottom - 66 : r.Bottom - 13);
        _p.Fill(new RectangleF(g.X, g.Y, g.Width, 1), 0xFF2C282A);
        int n = v.FrameCount;
        if (n <= 0) { CyMono("WAITING FOR FRAMES", x, g.Y + g.Height / 2 + 6, iw, CyG); return; }
        int bars = Math.Max(8, (int)(g.Width / 4.6f));
        float pitch = g.Width / bars, bw = MathF.Max(1.5f, pitch - 1.4f), topMs = budget * 1.6f;
        int per = Math.Max(1, n / bars);
        for (int b = 0; b < bars; b++)
        {
            float ms = 0;
            byte flag = 0;
            int from = n - (bars - b) * per;
            if (from + per <= 0) continue;
            for (int i = Math.Max(0, from); i < from + per; i++)
            {
                int idx = (v.FrameHead - n + i + VrStats.FrameHistory) % VrStats.FrameHistory;
                ms = MathF.Max(ms, v.FrameMs[idx]);
                flag = Math.Max(flag, v.FrameFlag[idx]);
            }
            bool spike = flag > 0 || ms > budget;
            float h = Math.Clamp(ms / topMs, 0.05f, 1) * g.Height;
            if (spike) h = MathF.Max(h, g.Height * 0.92f);
            if (_cyU < 99) { h *= CyberFx.EaseOut(CyIn(0.18f + 0.3f * b / bars, 0.12f)); if (h < 0.5f) continue; }     // the boot
            _p.Fill(new RectangleF(g.X + b * pitch, g.Bottom - h, bw, h), spike ? CyR : CyberHud.Bars);
        }
        float by = MathF.Round(g.Bottom - g.Height * (budget / topMs)) + 0.5f;
        _p.DashedLine(g.X, by, g.Right, by, CyY, 1.5f, 5, 4);
        string bt = BudgetText(budget);
        float btw = CyMonoW(bt) + 4;
        _p.Fill(new RectangleF(g.Right - btw - 2, by - 17, btw + 2, 15), CyberHud.Card);
        CyMono(bt, g.Right, by - 4, btw, CyY, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
        if (!tall) return;
        // under it: the frame rate (of the headset's), the slowest 1 % as a frame rate, how much was reprojected
        float fps = v.AppFps, low = v.OnePercentLow(), rp = v.ReprojPct;
        float col = iw / 3, vb = r.Bottom - 30, lb = r.Bottom - 12;
        void Col(float cx, string val, string label, StatLevel lv, bool okv)
        {
            CyValue(cx, vb, val, MathF.Min(_big ? 34 : 30, CyFit(val, col - 10, 30)), okv ? CyLevel(lv, CyW) : CyberHud.Faint);
            CyMono(label, cx, lb, col - 6, CyG);
        }
        Col(x, fps < 0 ? "–" : CyFig(0, fps, "0"), "FPS", FpsLevel(fps, hz), fps >= 0);
        Col(x + col, low < 0 ? "–" : CyFig(1, low, "0"), "1% LOW", low < 0 ? StatLevel.Ok : FpsLevel(low, hz), low >= 0);
        Col(x + 2 * col, rp < 0 ? "–" : CyFig(2, rp, "0", "%"), "REPROJ", ReprojLevel(rp), rp >= 0);
    }

    // a frame number as text, made again only when its value changes (a new string per number per draw before: 460 B a frame); a
    // slot per place it is shown, so two places never share one
    readonly (float v, string s)[] _cyFigs = new (float, string)[6];
    readonly (string s, uint c)[] _cyNoteParts = new (string, uint)[7];

    string CyFig(int slot, float v, string fmt, string suffix = null)
    {
        ref var e = ref _cyFigs[slot];
        if (e.s == null || e.v != v) { e.v = v; e.s = suffix == null ? v.ToString(fmt, CultureInfo.CurrentCulture) : v.ToString(fmt, CultureInfo.CurrentCulture) + suffix; }
        return e.s;
    }

    float _cyBudget = -1;
    string _cyBudgetText;
    string BudgetText(float budget)
    {
        if (budget != _cyBudget) { _cyBudget = budget; _cyBudgetText = budget.ToString("0.0", CultureInfo.InvariantCulture); }
        return _cyBudgetText;
    }

    // the frame numbers at the right of the caption row, as many of them as fit: "86 FPS · 9.3 MS · 4%"
    void CyFrameNote(RectangleF r, float left, float right, VrStats v)
    {
        float fps = v.AppFps, ms = v.GpuMs, rp = v.ReprojPct, hz = v.DisplayHz;
        float by = r.Y + CyBase;
        if (fps < 0) { if (right - left >= CyMonoW("NO FRAMES")) CyMono("NO FRAMES", right, by, right - left, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing); return; }
        var fl = FpsLevel(fps, hz);
        var gl = GpuLevel(ms, 1000f / Math.Max(1, hz));
        var rl = ReprojLevel(rp);
        var parts = _cyNoteParts;          // (the array kept, its numbers made again only when they change)
        parts[0] = (CyFig(3, fps, "0"), CyLevel(fl, CyW)); parts[1] = (" FPS", CyG); parts[2] = (" · ", CyG);
        parts[3] = (CyFig(4, ms, "0.0"), CyLevel(gl, CyW)); parts[4] = (" MS", CyG); parts[5] = (" · ", CyG);
        parts[6] = (CyFig(5, Math.Max(0, rp), "0", "%"), CyLevel(rl, CyW));
        int count = parts.Length;
        float Total(int k) { float t = 0; for (int i = 0; i < k; i++) t += CyMonoW(parts[i].s); return t; }
        while (count > 0 && Total(count) > right - left) count = count == 7 ? 5 : count == 5 ? 2 : 0;
        if (count == 0) return;
        float xx = right - Total(count);
        for (int i = 0; i < count; i++)
        {
            float pw = CyMonoW(parts[i].s);
            CyMono(parts[i].s, xx, by, pw + 2, parts[i].c);
            xx += pw;
        }
    }

    // ---- the room: ROOM ----
    // The world and how long you have been there as the note; how many big, the friends' names in cyan, a segment per person (the
    // friends first, cyan), "3 FRIENDS"
    RoomWatcher.Snapshot _cyRoomSeen;
    bool _cyRoomKnown;
    int _cyRoomLang = -1;
    string _cyRoomNames, _cyRoomFriends;
    void CyRoomNow(PanelData d)
    {
        if (_cyRoomKnown && ReferenceEquals(d.Room, _cyRoomSeen) && _cyRoomLang == Lang.Version) return;
        _cyRoomKnown = true;
        _cyRoomLang = Lang.Version;
        _cyRoomSeen = d.Room;
        _cyRoomNames = _cyRoomFriends = null;
        var s = d.Room;
        if (s == null || !s.InRoom) return;
        var names = new List<string>(3);
        int f = 0;
        foreach (var p in s.Here) { if (!p.Friend) continue; f++; if (names.Count < 3) names.Add(p.Name); }
        if (f > 0) _cyRoomNames = string.Join(" · ", names) + (f > names.Count ? $" +{f - names.Count}" : "");
        _cyRoomFriends = Lang.Upper(RoomPanel.FriendsText(f));
    }

    // "The Black Cat · 1H 05M": made again only when the world or the minute changes (three strings a frame before)
    string _cyRoomNote;
    (string world, int min, int lang) _cyRoomNoteFor = (null, -1, -1);

    string RoomNote(PanelData d, DateTime now)
    {
        var s = d.Room;
        if (s == null || !s.InRoom) return null;
        string world = string.IsNullOrEmpty(s.World) ? Lang.T("A world") : s.World;
        if (s.Since == default) return world;
        var t = now - s.Since;
        int min = (int)Math.Floor(t.TotalMinutes);
        if (_cyRoomNote != null && ReferenceEquals(world, _cyRoomNoteFor.world) && min == _cyRoomNoteFor.min && Lang.Version == _cyRoomNoteFor.lang) return _cyRoomNote;
        _cyRoomNoteFor = (world, min, Lang.Version);
        string stay = t.TotalHours >= 1 ? Lang.F("{0}H {1:00}M", (int)t.TotalHours, t.Minutes) : Lang.F("{0}M", Math.Max(0, (int)t.TotalMinutes));
        return _cyRoomNote = world + " · " + stay;
    }

    void CyberRoom(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        if (_ui.Clickable(Ui.Id("wrm", id), r, out _, out _)) OpenDetail(w);
        RoomNow(d);
        CyRoomNow(d);
        int n = _roomPeople, f = _roomFriends;
        float pad = PadX(r), x = r.X + pad, right = r.Right - pad, iw = right - x;
        CyberCaptionRow(r, x, right, w.W >= 2 ? RoomNote(d, DateTime.Now) : null, CyG, CapRoom);
        float bottom = r.Bottom - 13, top = r.Y + 34;
        if (n < 0)
        {
            CyValue(x, bottom, "–", 34, CyberHud.Faint);
            string why = d.Room?.LogFound == false ? "NO VRCHAT" : "NOT IN A WORLD";
            float wx = x + 26;
            if (CyMonoW(why) <= right - wx) CyMono(why, wx, bottom, right - wx, CyG);
            return;
        }
        float vs = MathF.Floor(MathF.Min(bottom - top, _big ? 56 : 42) / CyCap);
        float vw = _p.MeasureWidth(_roomValue, vs, FontWeight.Bold, Theme.Display);
        TextAt(_roomValue, x, bottom, vw + 4, CyW, vs, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        float cx = x + vw + 12, cwid = right - cx;
        if (cwid < 30) return;
        // a segment per person (as many as fit), the friends first
        float segH = 6, segY = bottom - segH;
        string fr = f > 0 ? _cyRoomFriends : null;
        float fw = fr != null && w.W >= 2 ? CyMonoW(fr) + 10 : 0;
        int seg = Math.Min(n, Math.Max(1, (int)((cwid - fw) / 13)));
        if (seg > 0)
        {
            float bw = MathF.Min(cwid - fw, seg * 13);
            CyberHud.Segments(_p, new RectangleF(cx, segY, bw, segH), 1, CyOther, 0, seg);
            if (f > 0) CyberHud.Segments(_p, new RectangleF(cx, segY, bw, segH), Math.Min(1, f / (float)seg), CyC, 0, seg);
        }
        if (fw > 0) CyMono(fr, right, bottom, fw, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
        // the friends' names over it (or how many are here)
        float ns = _big ? 22 : 18, nby = segY - 9;
        string line = _cyRoomNames ?? (n == 1 ? Lang.T("1 here") : Lang.F("{0} here", n));
        TextAt(Ellipsize(line, cwid, ns, FontWeight.SemiBold, Theme.Display), cx, nby, cwid + 2, _cyRoomNames != null ? CyC : CyG, ns, FontWeight.SemiBold,
            TextAlignment.Leading, Theme.Display);
    }

    // ---- the microphone: MIC ----
    // Live: the card's outline cyan, the microphone and LIVE in cyan, its level as segments. Muted: the danger card, MUTED in red.
    void CyberMic(RectangleF r, WatchWidget w, int id)
    {
        string mk = Ui.Id("wmic", id);
        if (_ui.Clickable(mk, r, out _, out _)) { Mic.ToggleMute(); _dirty = true; CyTap(mk); }
        CyTapFx(mk, r, CyberHud.CardTL, CyberHud.CardBR);
        bool has = Mic.Present, muted = has && Mic.Muted;
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad;
        float tagW = CyberCaption(x, r, iw + pad - CyTagX, CapMicC);
        if (has && !muted && r.Right - pad - 8 > x + tagW + 6)
        {
            // live: a cyan dot at the right of the tag row - it breathes (2.4 s; WristCyberFx.cs)
            float br = 1;
            if (CyLoops) { br = 1 - 0.7f * CyLoopAmp * (0.5f - 0.5f * MathF.Cos((float)(_cyT % 2.4 / 2.4) * MathF.Tau)); CyWantLoop(); }
            float dy = r.Y + CyTagY + CyberHud.TagH / 2;
            _p.Fill(new RectangleF(r.Right - pad - 8, dy - 4, 8, 8), Theme.WithAlpha(CyC, br * CyIn(0.4f, 0.1f)));
        }
        string word = !has ? "NO MIC" : muted ? "MUTED" : "LIVE";
        uint c = !has ? CyberHud.Faint : muted ? CyR : CyC;
        float barH = 5, barY = r.Bottom - (_big ? 18 : 15) - barH, by = barY - (_big ? 13 : 9);
        float gs = _big ? 30 : 24;
        _p.Icon(muted ? MicOffGlyph : MicGlyph, new RectangleF(x - 3, by - gs + 1, gs + 4, gs + 2), c, gs - 2);
        float tx = x + gs + 4, room = r.Right - pad - tx;
        float ws = MathF.Max(17, FitSize(word, room, _big ? 36 : 30, FontWeight.Bold, Theme.Display));
        if (ws * 0.5f * word.Length <= room * 1.4f) TextAt(word, tx, by, room + 2, c, ws, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        if (!has || muted) return;
        float lvl = Mic.Peak <= 0 ? 0 : MathF.Min(1, MathF.Sqrt(Mic.Peak));
        CyberHud.Segments(_p, new RectangleF(x, barY, iw, barH), lvl, CyC, CyberHud.Track, w.W >= 2 ? 10 : 5);
    }

    // ---- the notifications: INBOX ----
    // How many as the note (cyan); a row each - where from in a red chip, what, how long ago
    int _cyNotesVersion = -2;
    bool _cyNotesFirst = true;
    readonly List<(string chip, string text, DateTime at)> _cyNotes = new();
    string _cyNew;
    void CyNotesNow(PanelData d)
    {
        int v = d.Notes?.Version ?? -1;
        if (v == _cyNotesVersion) return;
        _cyNotesVersion = v;
        _cyNotes.Clear();
        _cyNew = null;
        if (d.Notes == null) return;
        foreach (var it in d.Notes.Items)
        {
            if (_cyNotes.Count >= 8) break;
            string t = it.Title?.Trim() ?? "", c = it.Content?.Trim() ?? "";
            string text = it.Source == "Room" || c.Length == 0 ? (t.Length > 0 ? t : c) : c;
            string chip = Upper(string.IsNullOrEmpty(it.Source) ? "Orbit" : it.Source);
            if (chip.Length > 8) chip = chip[..8];
            _cyNotes.Add((chip, text, it.At));
        }
        int n = d.Notes.Items.Count;
        if (n > 0) _cyNew = Lang.F("{0} NEW", n);
        if (n > 0) CyInboxSeen(d.Notes.Items[0].At, _cyNotesFirst);
        _cyNotesFirst = false;
    }

    void CyberInbox(RectangleF r, PanelData d)
    {
        CyNotesNow(d);
        float pad = PadX(r), x = r.X + pad, right = r.Right - pad, iw = right - x;
        var (blink, lit) = CyInboxBlink();              // a new one: the tag and "N NEW" blink (WristCyberFx.cs)
        CyberCaptionRow(r, x, right, _cyNew, blink && !lit ? Theme.WithAlpha(CyC, 0.25f) : CyC, CapInbox, blink && lit ? CyC : CyY);
        if (_cyNotes.Count == 0)
        {
            CyMono(Ellipsize("NO NOTIFICATIONS", iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Y + MathF.Min(64, r.Height * 0.5f + 14), iw + 2, CyG);
            return;
        }
        float top = r.Y + 32, bottom = r.Bottom - 6;          // a one-row card: two rows (the concept's)
        int slots = Math.Max(1, (int)((bottom - top) / 26));
        float rh = (bottom - top) / slots;
        var now = DateTime.Now;
        for (int i = 0; i < Math.Min(slots, _cyNotes.Count); i++)
        {
            var (chip, text, at) = _cyNotes[i];
            float cy = top + rh * (i + 0.5f), by = cy + CyberHud.CapH(_p, CyNote) / 2;
            float chw = CyMonoW(chip) + 10, chh = 21;
            float tx = x;
            if (iw > 180)
            {
                var cr = new RectangleF(x, cy - chh / 2, chw, chh);
                _p.Stroke(RectangleF.Inflate(cr, -0.6f, -0.6f), CyR, 1.25f);
                CyMono(chip, x + 5, by, chw, CyR);
                tx = cr.Right + 9;
            }
            string age = Age(at, now).Replace(" ", "");
            float aw = CyMonoW(age);
            CyMono(age, right, by, aw + 2, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
            float tw = right - aw - 10 - tx;
            float typed = i == 0 && blink ? CyInboxRow() : 1;      // the newest types in
            if (typed < 1) _p.PushClip(new RectangleF(tx, cy - 14, tw * typed, 28));
            if (tw > 20)
                TextAt(Ellipsize(text, tw, 17, FontWeight.SemiBold, Theme.Display), tx, cy + 6, tw + 2, CyW, 17, FontWeight.SemiBold, TextAlignment.Leading, Theme.Display);
            if (typed < 1) _p.PopClip();
        }
    }

    // ---- a group of buttons: BUTTONS ----
    // The tag, the tiles under it: one row - the icon over the name (icons only where a name would not fit); two rows tall - two
    // columns, the icon beside the name. The tiles are the concept's: dark, a cyan outline with corner marks; on = filled cyan.
    void CyberGroup(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        GroupIdsOf(w);
        int n = _groupIds.Count;
        CyberCaption(r.X + PadX(r), r, r.Width - 2 * PadX(r), CapButtons);
        const float inset = 9, gap = 6;
        var a = RectangleF.FromLTRB(r.X + inset, r.Y + 33, r.Right - inset, r.Bottom - inset);
        int cols, rows, mode;              // mode 0: icon over name, 1: icon only, 2: icon beside name
        if (a.Height >= 120) { cols = n <= 3 && a.Width < 300 ? 1 : 2; rows = (n + cols - 1) / cols; mode = 2; }
        else if (a.Width / n >= 50) { cols = n; rows = 1; mode = 0; }
        else { cols = (n + 1) / 2; rows = 2; mode = 1; }
        float tw = (a.Width - (cols - 1) * gap) / cols, th = (a.Height - (rows - 1) * gap) / rows;
        if (mode == 0)
        {
            if (th < 48) mode = 1;
            // the layout's own name does not count: it is shortened ("DESK…"), the other names stay (Erik's long layout names
            // took every name away)
            else foreach (var gid in _groupIds) if (gid != "layout" && CyMonoW(Upper(GroupLabel(gid, d)), CyNote, FontWeight.Bold) > tw - 10) { mode = 1; break; }
        }
        else if (mode == 2)
            foreach (var gid in _groupIds)
                if (gid != "layout" && CyMonoW(Upper(GroupLabel(gid, d)), CyNote, FontWeight.Bold) > tw - 24 - 22 - 10) { mode = 0; break; }
        for (int j = 0; j < n; j++)
        {
            var t = new RectangleF(a.X + j % cols * (tw + gap), a.Y + j / cols * (th + gap), tw, th);
            CyberTile(Ui.Id("wgb", id * 8 + j), t, _groupIds[j], d, mode, false);
        }
    }

    // one tile of a group (or a button widget: frame = its card's cuts and corner marks)
    void CyberTile(string key, RectangleF t, string kid, PanelData d, int mode, bool card)
    {
        var k = WatchWidgets.Find(kid);
        if (k == null) return;
        bool on = IsOn(kid, d);
        bool click = _ui.Clickable(key, card ? t : MinHit(t), out bool hover, out bool held);
        float s = _ui.Spring(Ui.Id(key, "#s"), held ? 0.92f : 1f, 420, 18);
        bool scaled = MathF.Abs(s - 1) > 0.001f;
        if (scaled) _p.PushScale(s, t.X + t.Width / 2, t.Y + t.Height / 2);
        float tl = card ? CyberHud.CardTL + 2 : 5, br = card ? CyberHud.CardBR : 9;
        // on / off: the cyan fill wipes in from the left (and out to it), a light edge leading (WristCyberFx.cs)
        float f = CyOnOff(key, on);
        if (f <= 0.004f || f >= 0.996f) CyTileFace(t, k, kid, d, mode, card, f >= 0.996f, hover, tl, br);
        else
        {
            CyTileFace(t, k, kid, d, mode, card, false, hover, tl, br);
            float wx = t.X + t.Width * f;
            _p.PushClip(new RectangleF(t.X - 1, t.Y - 1, wx - t.X + 1, t.Height + 2));
            CyTileFace(t, k, kid, d, mode, card, true, hover, tl, br);
            _p.PopClip();
            _p.Fill(new RectangleF(wx - 1, t.Y + 2, 2, t.Height - 4), 0xE6FFFFFF);
        }
        CyTapFx(key, t, tl, br);
        if (scaled) _p.PopScale();
        if (click) { CyTap(key); Act(kid); }
    }

    void CyTileFace(RectangleF t, WatchWidgets.Kind k, string kid, PanelData d, int mode, bool card, bool on, bool hover, float tl, float br)
    {
        _p.FillCut(t, on ? CyC : hover ? CyberHud.Card3 : card ? CyberHud.Card : CyberHud.Card2, tl, 0, br, 0);
        if (!on && (!card || CyEdgeShown))              // a button card booting: its outline is drawn in (WristCyberFx.cs)
            _p.StrokeCut(RectangleF.Inflate(t, -0.6f, -0.6f), hover ? CyC : CyberHud.CyanDim, 1.25f, MathF.Max(0, tl - 0.3f), 0, MathF.Max(0, br - 0.3f), 0);
        if (card || t.Height >= 54) CyberHud.Brackets(_p, t, on ? CyInk : CyC, card ? 10 : 7, card ? 7 : 5, 2);
        uint ic = on ? CyInk : CyC, lc = on ? CyInk : CyG;
        string icon = kid == "desktops" && !d.DesktopsVisible ? Theme.IcoHide : k.Icon;
        string label = Upper(GroupLabel(kid, d));
        float isz = card ? 28 : 22;
        if (kid == "layout" && card && mode != 2)
        {
            // the layout: its name big, where it is among the layouts in cyan under it ("‹ 1/3 ›")
            var ls = _cfg.Data.Layouts;
            int li = -1;
            for (int i = 0; i < ls.Count; i++) if (ls[i].Name == d.LayoutName) { li = i; break; }    // (a lambda holding d was made for every tile)
            string at = li < 0 ? null : $"‹ {li + 1}/{ls.Count} ›";
            float ns = MathF.Max(17, FitSize(label, t.Width - 16, 30, FontWeight.Bold, Theme.Display));
            float cy0 = t.Y + t.Height * 0.52f;
            TextAt(Ellipsize(label, t.Width - 16, ns, FontWeight.Bold, Theme.Display), t.X + 8, cy0, t.Width - 14, on ? CyInk : CyW, ns, FontWeight.Bold,
                TextAlignment.Center, Theme.Display);
            if (at != null) CyMono(at, t.X + 4, cy0 + 22, t.Width - 8, on ? CyInk : CyC, CyNote, FontWeight.SemiBold, TextAlignment.Center);
        }
        else if (mode == 2)
        {
            float lw = CyMonoW(label, CyNote, FontWeight.Bold), room = t.Width - 24 - isz - 10;
            if (lw > room) { label = Ellipsize(label, room, CyNote, FontWeight.Bold, CyberHud.Mono); lw = CyMonoW(label, CyNote, FontWeight.Bold); }
            float lx = t.X + (t.Width - (isz + 10 + lw)) / 2;
            _p.Icon(icon, new RectangleF(lx - 2, t.Y, isz + 4, t.Height), ic, isz);
            CyMono(label, lx + isz + 10, t.Y + t.Height / 2 + CyberHud.CapH(_p, CyNote, FontWeight.Bold) / 2, lw + 2, on ? CyInk : CyW, CyNote, FontWeight.Bold);
        }
        else if (mode == 0)
        {
            float lb = t.Bottom - (card ? 14 : 9);
            _p.Icon(icon, new RectangleF(t.X, t.Y + (lb - CyberHud.CapH(_p, CyNote, FontWeight.Bold) - t.Y) / 2 - isz / 2 - 1, t.Width, isz + 4), ic, isz);
            if (CyMonoW(label, CyNote, FontWeight.Bold) > t.Width - 10) label = Ellipsize(label, t.Width - 10, CyNote, FontWeight.Bold, CyberHud.Mono);
            CyMono(label, t.X + 4, lb, t.Width - 8, card ? lc : on ? CyInk : CyW, CyNote, FontWeight.Bold, TextAlignment.Center);
        }
        else _p.Icon(icon, t, ic, isz);
    }

    // a button widget: the tile is its card - the icon over its name (a wide one: beside it); the layout its name and place
    void CyberButton(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d)
    {
        int mode = w.W >= 2 ? 2 : 0;
        if (mode == 0 && CyMonoW(Upper(GroupLabel(k.Id, d)), CyNote, FontWeight.Bold) > r.Width - 12) mode = 1;
        CyberTile(Ui.Id("wcb", id), r, k.Id, d, mode, true);
    }
}
