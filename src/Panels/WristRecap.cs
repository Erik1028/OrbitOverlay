using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The recap widget ("recap"; the session and its recap: Sys/SessionRecap.cs): the last VR session at a glance. A 1x1 is how long, with
// the day under it; a 2x1 how long and the photos / worlds beside it; a 2x2 how long, when, and four small readings - photos, worlds,
// people, the longest stay. A tap opens it: how long and when, the readings, every world visited with its stay as a bar (the longest
// lit), the people met. None yet: when one comes (after 20 minutes in VR, once the headset is off for 3 minutes).
// Modern: Bento cards. Cyber: the RECAP tag, the time in the condensed bold, plates with segment bars. The retro styles that draw the
// shared windows (95, XP, 7, the Mac): a little window and a "Session Summary" dialog of the era's fields and a details list of the
// worlds. The Terminal: its log (WristSocial.Term.cs). Nothing allocated a frame at rest (the texts are made per recap and language).
public sealed partial class WristPanel
{
    // ================= the texts =================

    sealed class RcText
    {
        public SessionRecap R;
        public int Lang = -1;
        public CultureInfo Culture;
        public bool H24;
        public DateTime Day;
        public bool Has;
        public string Len = "–", LenShort = "–", LenUnit, LenClock = "–", PeopleFull = "–", H, M, Day1 = "", Range = "", When = "", Photos = "–", Worlds = "–", People = "–", Longest = "–",
            PhotosText = "", WorldsText = "", PeopleText = "", FriendsText, LongestName = "", LongestLine = "", Line = "", Names = "", Stats = "",
            CyHead = "", Head95 = "", Status95 = "";
        public readonly string[] Note = new string[2];
        public readonly List<(string name, string at, string stay, float frac, bool longest)> Rows = new();
    }

    readonly RcText _rct = new();
    float _rcScroll, _rcMax;
    int _rcTop;                                  // (the Terminal's log: the first line shown)
    const string RecapList95 = "wr95recap";
    static readonly string[] CapRecap = { "Session recap", "Last session", "Recap" }, CapRecapS = { "Recap" };
    const string RecapNone = "No recap yet", RecapHint = "After 20 minutes in VR, once the headset is off for 3 minutes";

    // "1 h 42 min", "42 min" (also the card's)
    public static string RecapLength(TimeSpan t) =>
        t.TotalHours >= 1 ? Lang.F("{0} h {1:00} min", (int)t.TotalHours, t.Minutes) : Lang.F("{0} min", Math.Max(0, (int)t.TotalMinutes));

    static string RecapStay(double minutes) =>
        minutes >= 60 ? Lang.F("{0} h {1:00} min", (int)(minutes / 60), (int)minutes % 60) : Lang.F("{0} min", Math.Max(0, (int)minutes));

    static string RecapPhotos(int n) => n == 1 ? Lang.T("1 photo") : Lang.F("{0} photos", n);
    static string RecapWorlds(int n) => n == 1 ? Lang.T("1 world") : Lang.F("{0} worlds", n);

    // the card's line: "12 photos · 4 worlds · 23 people · longest: The Black Cat (48 min)" (what is known of it)
    public static string RecapLine(SessionRecap r)
    {
        var parts = new List<string>();
        if (r.Photos >= 0) parts.Add(RecapPhotos(r.Photos));
        if (r.Room)
        {
            parts.Add(RecapWorlds(r.WorldCount));
            parts.Add(RoomPanel.PeopleText(r.People));
            if (r.Longest is { } l) parts.Add(Lang.F("longest: {0} ({1})", l.Name, RecapStay(l.Minutes)));
        }
        return string.Join(" · ", parts);
    }

    RcText Rct(PanelData d)
    {
        var r = d.Recap;
        var t = _rct;
        var culture = CultureInfo.CurrentCulture;
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var today = DateTime.Today;
        if (ReferenceEquals(r, t.R) && t.Lang == Lang.Version && ReferenceEquals(culture, t.Culture) && h24 == t.H24 && today == t.Day) return t;
        (t.R, t.Lang, t.Culture, t.H24, t.Day) = (r, Lang.Version, culture, h24, today);
        RcMake(t, r, culture);
        return t;
    }

    void RcMake(RcText t, SessionRecap r, CultureInfo culture)
    {
        t.Rows.Clear();
        t.Has = r != null;
        if (r == null)
        {
            (t.Len, t.LenShort, t.LenUnit, t.LenClock, t.Note[0], t.Note[1], t.Day1, t.Range, t.When) =
                ("–", "–", null, "–", Lang.T("none yet"), Lang.T("none yet"), "", "", "");
            t.CyHead = Upper(RecapNone);
            t.Head95 = Lang.T(RecapNone);
            t.Status95 = "";
            return;
        }
        var len = r.Length;
        t.Len = RecapLength(len);
        int minutes = Math.Max(0, (int)len.TotalMinutes);
        t.LenClock = $"{minutes / 60}:{minutes % 60:00}";
        (t.LenShort, t.LenUnit) = minutes >= 60 ? (t.LenClock, null) : (minutes.ToString(culture), "min");
        t.H = len.TotalHours >= 1 ? ((int)len.TotalHours).ToString(culture) : null;
        t.M = (len.TotalHours >= 1 ? len.Minutes : Math.Max(0, (int)len.TotalMinutes)).ToString(len.TotalHours >= 1 ? "00" : "0", culture);
        var day = r.End.Date;
        t.Day1 = day == DateTime.Today ? Lang.T("Today") : day == DateTime.Today.AddDays(-1) ? Lang.T("Yesterday")
            : Capital(r.End.ToString(culture.DateTimeFormat.MonthDayPattern, culture));
        t.Range = ShortClock(r.Start) + " – " + ShortClock(r.End);
        t.When = t.Day1 + " · " + t.Range;
        t.Note[0] = t.Day1;
        t.Note[1] = t.Range;
        t.Photos = r.Photos >= 0 ? r.Photos.ToString(culture) : "–";
        t.PhotosText = r.Photos >= 0 ? RecapPhotos(r.Photos) : Lang.T("photos not counted");
        int wc = r.WorldCount;
        t.Worlds = r.Room ? wc.ToString(culture) : "–";
        t.WorldsText = r.Room ? RecapWorlds(wc) : Lang.T("room not followed");
        t.People = r.Room ? r.People.ToString(culture) : "–";
        t.PeopleText = r.Room ? RoomPanel.PeopleText(r.People) : "";
        t.FriendsText = r.Room && r.Friends > 0 ? RoomPanel.FriendsText(r.Friends) : null;
        t.PeopleFull = t.FriendsText != null ? t.People + " (" + t.FriendsText + ")" : t.People;
        var l = r.Longest;
        t.Longest = l != null ? RecapStay(l.Minutes) : "–";
        t.LongestName = l?.Name ?? "";
        t.LongestLine = l != null ? l.Name + " · " + t.Longest : "";
        t.Line = RecapLine(r);
        t.Stats = r.Photos >= 0 && r.Room ? t.PhotosText + " · " + t.WorldsText : r.Photos >= 0 ? t.PhotosText : t.WorldsText;
        int shown = Math.Min(r.Names.Count, 3), rest = r.People - shown;
        t.Names = shown == 0 ? "" : string.Join(", ", r.Names.GetRange(0, shown)) + (rest > 0 ? Lang.F(" + {0}", rest) : "");
        double total = Math.Max(1, len.TotalMinutes);
        foreach (var w in r.Worlds)
            t.Rows.Add((w.Name, ShortClock(w.Start), RecapStay(w.Minutes), (float)Math.Clamp(w.Minutes / total, 0, 1), ReferenceEquals(w, l)));
        t.CyHead = Upper(t.When);
        t.Head95 = Lang.F("{0} in VR", t.Len);
        t.Status95 = t.Names.Length > 0 ? Lang.F("Met: {0}", t.Names) : t.When;
    }

    // ================= Modern =================

    void RecapOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var t = Rct(d);
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        uint vc = t.Has ? Bento.Text : Bento.Faint;
        if (w.W == 1 && w.H == 1)
        {
            Small(r, CapRecapS, t.LenShort, t.LenUnit, 0, 0, vc, null);
            Note(x, r.Y + NoteBase, iw, t.Note);
            return;
        }
        CaptionRow(r, t.Has ? t.Day1 : null, CapRecap);
        if (w.H == 1)
        {
            float by = r.Y + MBase;
            if (!t.Has)
            {
                ValueText(x, by, "–", 40, vc, null);
                float nx = x + 30;
                TextAt(FrCut(RecapNone, r.Right - pad - nx, SmallText, FontWeight.SemiBold), nx, by, r.Right - pad - nx + 2, Sub, SmallText, FontWeight.SemiBold);
                return;
            }
            float vw = RcTime(t, x, by, MathF.Min(iw, _big ? 190 : 175), _big ? 44 : 40);
            // beside it: the photos over the worlds (where there is room)
            float lx = x + vw + 14, room = r.Right - pad - lx;
            if (room >= 80)
            {
                TextAt(FrCut(t.PhotosText, room, SmallText, FontWeight.SemiBold), lx, by - 22, room + 2, Sub, SmallText, FontWeight.SemiBold);
                TextAt(FrCut(t.WorldsText, room, SmallText, FontWeight.SemiBold), lx, by, room + 2, Sub, SmallText, FontWeight.SemiBold);
            }
            return;
        }
        if (!t.Has)
        {
            ValueText(x, r.Y + (_big ? 86 : 74), "–", 40, vc, null);
            _p.Text(FrCut(RecapNone, iw, SmallText, FontWeight.SemiBold), new RectangleF(x, r.Y + (_big ? 100 : 88), iw, 26), Bento.Text, SmallText, FontWeight.SemiBold);
            _p.Text(RecapHint, new RectangleF(x, r.Y + (_big ? 126 : 112), iw, r.Bottom - 10 - r.Y - (_big ? 126 : 112)), Sub, SmallText, FontWeight.SemiBold,
                TextAlignment.Leading, ParagraphAlignment.Near, null, true);
            return;
        }
        // how long, when; four readings under them, two by two
        float vy = r.Y + (_big ? 86 : 72);
        RcTime(t, x, vy, iw, _big ? 48 : 38);
        TextAt(FrCut(t.Range, iw, SmallText, FontWeight.SemiBold), x, vy + 24, iw + 2, Sub, SmallText, FontWeight.SemiBold);
        float gy = vy + (_big ? 44 : 36), gh = r.Bottom - 10 - gy, cw = (iw - 10) / 2, ch = gh / 2;
        RcMini(new RectangleF(x, gy, cw, ch), "Photos", t.Photos);
        RcMini(new RectangleF(x + cw + 10, gy, cw, ch), "Worlds", t.Worlds);
        RcMini(new RectangleF(x, gy + ch, cw, ch), "People", t.People);
        RcMini(new RectangleF(x + cw + 10, gy + ch, cw, ch), "Longest", t.Longest);
    }

    // the time in VR: hours and minutes bold with their units small ("1 h 42 min"), as big as fits w (size down to 24); its width
    float RcTime(RcText t, float x, float by, float w, float size)
    {
        string hu = Lang.C("unit", "h");
        float uw = _p.MeasureWidth(hu, SmallText, FontWeight.SemiBold), mw = _p.MeasureWidth("min", SmallText, FontWeight.SemiBold);
        float nw = (t.H != null ? _p.MeasureWidth(t.H, 100, FontWeight.Bold, Theme.Display) : 0) + _p.MeasureWidth(t.M, 100, FontWeight.Bold, Theme.Display);
        float units = (t.H != null ? uw + 4 + 10 : 0) + 4 + mw;
        float vs = MathF.Max(24, MathF.Min(size, MathF.Floor(100 * (w - units) / nw * 2) / 2));
        float x0 = x;
        if (t.H != null)
        {
            x += ValueText(x, by, t.H, vs, Bento.Text, null) + 4;
            TextAt(hu, x, by, uw + 2, Sub, SmallText, FontWeight.SemiBold);
            x += uw + 10;
        }
        x += ValueText(x, by, t.M, vs, Bento.Text, null) + 4;
        TextAt("min", x, by, mw + 2, Sub, SmallText, FontWeight.SemiBold);
        return x + mw - x0;
    }

    // a reading's caption in capitals cut to w - the language's shorter word first where the whole does not fit ("short|Longest":
    // Magyar "REKORD", not "LEGHOSS…"; the capitals have no short form of their own to find), kept as FrCut keeps its cuts
    readonly Dictionary<(string, float), string> _rcCaps = new();
    int _rcCapsLang = -1;

    string RcCap(string cap, float w)
    {
        if (_rcCapsLang != Lang.Version || _rcCaps.Count > 32) { _rcCaps.Clear(); _rcCapsLang = Lang.Version; }
        if (_rcCaps.TryGetValue((cap, w), out var done)) return done;
        string c = Upper(cap);
        if (_p.MeasureWidth(c, CapSize, FontWeight.SemiBold) > w && Lang.Short(cap) is { } sh) c = Upper(sh);
        return _rcCaps[(cap, w)] = FrCut(c, w, CapSize, FontWeight.SemiBold);
    }

    // a small reading: its caption over its value
    void RcMini(RectangleF r, string cap, string value)
    {
        float by = r.Y + MathF.Min(r.Height * 0.42f, 22);
        CapText(RcCap(cap, r.Width), r.X, by, r.Width, Sub);
        float vs = MathF.Max(17, FitSize(value, r.Width, _big ? 28 : 22, FontWeight.Bold, Theme.Display));
        TextAt(FrCut(value, r.Width, vs, FontWeight.Bold, Theme.Display), r.X, MathF.Min(r.Bottom - 4, by + vs + 4), r.Width + 2, Bento.Text, vs, FontWeight.Bold,
            TextAlignment.Leading, Theme.Display);
    }

    // Opened: when under the title; on the left how long (big) and four readings; on the right every world visited - its name, when
    // you got there and how long you stayed, a bar of its share of the session (the longest in the accent); the people met along the
    // bottom. The stick scrolls the worlds.
    void RecapDetail(RectangleF E, PanelData d)
    {
        if (Theme.Cyber) { CyberRecapDetail(E, d); return; }
        var t = Rct(d);
        float x = E.X + 24, right = E.Right - 24;
        if (!t.Has)
        {
            _rcMax = 0;
            var L0 = new RectangleF(x, E.Y + 100, right - x, E.Height - 140);
            float my = L0.Y + (L0.Height - 90) / 2;
            _p.Icon("", new RectangleF(L0.X, my - 50, L0.Width, 48), Bento.Faint, 38);
            _p.Text(FrCut(RecapNone, L0.Width, 22, FontWeight.SemiBold), new RectangleF(L0.X, my + 6, L0.Width, 34), Bento.Text, 22, FontWeight.SemiBold,
                TextAlignment.Center);
            _p.Text(FrCut(RecapHint, L0.Width, SmallText, FontWeight.Normal), new RectangleF(L0.X, my + 42, L0.Width, 28), Sub, SmallText, FontWeight.Normal,
                TextAlignment.Center);
            return;
        }
        TextAt(FrCut(t.When, right - x, 19, FontWeight.Normal), x, E.Y + 82, right - x + 2, Sub, 19, FontWeight.Normal);
        // left: how long, the readings
        const float LeftW = 228;
        float ly = E.Y + 106;
        CapText(Upper("Time in VR"), x, ly + 16, LeftW, Sub);
        RcTime(t, x, ly + 70, LeftW, 52);
        float gy = ly + 92, cw = (LeftW - 12) / 2, ch = 62;
        RcTile(new RectangleF(x, gy, cw, ch), "Photos", t.Photos);
        RcTile(new RectangleF(x + cw + 12, gy, cw, ch), "Worlds", t.Worlds);
        RcTile(new RectangleF(x, gy + ch + 12, cw, ch), "People", t.People);
        RcTile(new RectangleF(x + cw + 12, gy + ch + 12, cw, ch), "Longest", t.Longest);
        // right: the worlds
        float wx = x + LeftW + 28, ww = right - wx;
        CapText(Upper("Worlds visited"), wx, ly + 16, ww, Sub);
        var L = new RectangleF(wx, ly + 28, ww, E.Bottom - 48 - (ly + 28));
        int n = t.Rows.Count;
        if (n == 0)
        {
            _rcMax = 0;
            _p.Text(FrCut(d.Recap.Room ? "No world this time" : "The room was not followed", L.Width, SmallText, FontWeight.Normal),
                new RectangleF(L.X, L.Y + 8, L.Width, 26), Sub, SmallText);
        }
        else
        {
            const float RowH = 52;
            float content = n * RowH, max = MathF.Max(0, content - L.Height);
            _rcMax = max;
            if (_ui.Wheel != 0 && !_ui.Inert) { _rcScroll -= _ui.Wheel * RowH; _dirty = true; }
            _rcScroll = Math.Clamp(_rcScroll, 0, max);
            float rw = max > 0 ? L.Width - 14 : L.Width;
            _p.PushClip(L);
            for (int i = 0; i < n; i++)
            {
                float y = L.Y - _rcScroll + i * RowH;
                if (y + RowH < L.Y || y > L.Bottom) continue;
                var (name, at, stay, frac, longest) = t.Rows[i];
                float sw = _p.MeasureWidth(stay, SmallText, FontWeight.SemiBold);
                TextAt(stay, L.X + rw - sw, y + 22, sw + 2, longest ? Bento.Text : Sub, SmallText, FontWeight.SemiBold);
                float aw = _p.MeasureWidth(at, SmallText, FontWeight.Normal);
                TextAt(at, L.X + rw - sw - 12 - aw, y + 22, aw + 2, Sub, SmallText, FontWeight.Normal);
                float nw = rw - sw - aw - 24;
                if (nw > 20) TextAt(FrCut(name, nw, 19, longest ? FontWeight.SemiBold : FontWeight.Normal), L.X, y + 22, nw + 2, Bento.Text, 19,
                    longest ? FontWeight.SemiBold : FontWeight.Normal);
                var bar = new RectangleF(L.X, y + 32, rw, 6);
                _p.Fill(bar, Bento.Track, 3);
                _p.Fill(new RectangleF(bar.X, bar.Y, MathF.Max(6, bar.Width * frac), 6), longest ? Bento.Accent : Theme.WithAlpha(Bento.Text, 0.45f), 3);
            }
            _p.PopClip();
            if (max > 0)
            {
                float bh = MathF.Max(24, L.Height * L.Height / content), by = L.Y + (L.Height - bh) * (_rcScroll / max);
                _p.Fill(new RectangleF(L.Right - 4, by, 4, bh), Theme.WithAlpha(Bento.Text, 0.3f), 2);
            }
        }
        // along the bottom: who you met
        if (t.Names.Length > 0)
        {
            string met = t.Status95;
            TextAt(FrCut(met, right - x, SmallText, FontWeight.Normal), x, E.Bottom - 22, right - x + 2, Sub, SmallText, FontWeight.Normal);
        }
    }

    // a reading on a tile of its own (the opened view)
    void RcTile(RectangleF r, string cap, string value)
    {
        _p.Fill(r, Bento.Tile, 14);
        if (Bento.Edge != 0) _p.Stroke(RectangleF.Inflate(r, -0.6f, -0.6f), Bento.Edge, Bento.EdgeWidth, 13.4f);
        CapText(RcCap(cap, r.Width - 24), r.X + 12, r.Y + 22, r.Width - 24, Sub);
        float vs = MathF.Max(17, FitSize(value, r.Width - 24, 26, FontWeight.Bold, Theme.Display));
        TextAt(FrCut(value, r.Width - 24, vs, FontWeight.Bold, Theme.Display), r.X + 12, r.Y + 52, r.Width - 22, Bento.Text, vs, FontWeight.Bold,
            TextAlignment.Leading, Theme.Display);
    }

    // ================= Cyber =================
    // RECAP on its tag, the day as its note; the time in the condensed bold, cyan; the readings as monospace lines; opened: plates -
    // IN VR, the four readings, the worlds with their stays as segment bars (the longest yellow), who you met.

    void CyberRecap(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var t = Rct(d);
        float pad = PadX(r), x = r.X + pad, right = r.Right - pad, iw = right - x;
        bool small = w.W == 1 && w.H == 1;
        CyberCaptionRow(r, x, right, small || !t.Has ? null : t.Day1, CyG, small ? CapRecapS : CapRecap);
        float bottom = r.Bottom - 13, top = r.Y + 34;
        if (!t.Has)
        {
            CyValue(x, bottom, "–", 34, CyberHud.Faint);
            float wx = x + 26;
            string none = Upper(small ? "none yet" : RecapNone);
            if (right - wx > 24) CyMono(FrCut(none, right - wx, CyNote, FontWeight.SemiBold, CyberHud.Mono), wx, bottom, right - wx, CyG);
            return;
        }
        if (small || w.H == 1)
        {
            float vs = MathF.Floor(MathF.Min(bottom - top, _big ? 56 : 42) / CyCap);
            vs = MathF.Min(vs, CyFit(t.LenClock, small ? iw : iw * 0.5f, vs));
            float vw = CyValue(x, bottom, t.LenClock, vs, CyC, Ui.Id("wrcv", id));
            if (small)
            {
                string day = Upper(t.Day1);
                if (CyMonoW(day) <= iw) CyMono(day, x, bottom - vs * CyCap - 8, iw + 2, CyG);
                return;
            }
            float lx = x + vw + 14, room = right - lx;
            if (room < 40) return;
            CyMono(FrCut(Upper(t.PhotosText), room, CyNote, FontWeight.SemiBold, CyberHud.Mono), lx, bottom - 20, room + 2, CyW);
            CyMono(FrCut(Upper(t.WorldsText), room, CyNote, FontWeight.SemiBold, CyberHud.Mono), lx, bottom, room + 2, CyG);
            return;
        }
        // tall: the time, then the readings as lines - their name left, the value right
        float vy = r.Y + (_big ? 82 : 70);
        CyValue(x, vy, t.LenClock, MathF.Min(_big ? 44 : 36, CyFit(t.LenClock, iw, 44)), CyC, Ui.Id("wrcv", id));
        float ly = vy + 12, lh = (r.Bottom - 10 - ly) / 4;
        CyLine(x, right, ly + lh * 1 - 4, "Photos", t.Photos);
        CyLine(x, right, ly + lh * 2 - 4, "Worlds", t.Worlds);
        CyLine(x, right, ly + lh * 3 - 4, "People", t.People);
        CyLine(x, right, ly + lh * 4 - 4, "Longest", t.Longest);
    }

    // a reading as a monospace line: its name grey at the left, its value white at the right, a dotted rule between
    void CyLine(float x, float right, float by, string name, string value)
    {
        string n = Upper(name);
        float nw = CyMonoW(n), vw = CyMonoW(value);
        CyMono(n, x, by, nw + 2, CyG);
        CyMono(value, right, by, vw + 2, CyW, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
        for (float dx = x + nw + 8; dx < right - vw - 8; dx += 5) _p.Fill(new RectangleF(dx, by - 3, 2, 2), CyberHud.Line);
    }

    void CyberRecapDetail(RectangleF E, PanelData d)
    {
        var t = Rct(d);
        CyFrame();
        float nx = E.X + 24 + CyberHud.TagWidth(_p, Upper(WatchWidgets.Find("recap").Name), 18) + 14, nr = E.Right - 22 - 46 - 14;
        if (nr - nx > 40) CyMono(FrCut(t.CyHead, nr - nx, CyNote, FontWeight.SemiBold, CyberHud.Mono), nx, E.Y + 23 + (26 + CyberHud.CapH(_p, CyNote)) / 2, nr - nx, CyG);
        var c = new RectangleF(E.X + 24, E.Y + 74, E.Width - 48, E.Height - 74 - 22);
        if (!t.Has)
        {
            _rcMax = 0;
            CyMono(FrCut(Upper(RecapNone), c.Width, 17, FontWeight.SemiBold, CyberHud.Mono), c.X, c.Y + c.Height / 2, c.Width + 2, CyG, 17);
            CyMono(FrCut(Upper(RecapHint), c.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), c.X, c.Y + c.Height / 2 + 24, c.Width + 2, CyberHud.Faint);
            return;
        }
        // IN VR and the four readings along the top
        float gap = 10, pw = (c.Width - 4 * gap) / 5, ph = 92;
        for (int i = 0; i < 5; i++)
        {
            var p = new RectangleF(MathF.Round(c.X + i * (pw + gap)), c.Y, MathF.Round(pw), ph);
            var (tag, value, col) = i switch
            {
                0 => ("In VR", t.LenClock, CyC),
                1 => ("Photos", t.Photos, CyW),
                2 => ("Worlds", t.Worlds, CyW),
                3 => ("People", t.People, CyW),
                _ => ("Longest", t.Longest, CyY),
            };
            CyPlate(p, tag, null, 0);
            CyValue(p.X + 14, p.Bottom - 16, value, CyFit(value, p.Width - 28, 40), col);
        }
        // the worlds: a plate, a row each - the name, when, how long, its share as segments (the longest yellow)
        var wp = new RectangleF(c.X, c.Y + ph + 12, c.Width, c.Bottom - (c.Y + ph + 12) - 26);
        CyPlate(wp, "Worlds visited", t.When.Length > 0 ? Upper(t.Range) : null, CyG);
        var L = new RectangleF(wp.X + 14, wp.Y + 36, wp.Width - 28, wp.Height - 44);
        int n = t.Rows.Count;
        if (n == 0)
        {
            _rcMax = 0;
            CyMono(Upper(d.Recap.Room ? "No world this time" : "The room was not followed"), L.X, L.Y + 18, L.Width, CyG);
        }
        else
        {
            const float RowH = 30;
            float content = n * RowH, max = MathF.Max(0, content - L.Height);
            _rcMax = max;
            if (_ui.Wheel != 0 && !_ui.Inert) { _rcScroll -= _ui.Wheel * RowH; _dirty = true; }
            _rcScroll = Math.Clamp(_rcScroll, 0, max);
            float rw = max > 0 ? L.Width - 12 : L.Width, nameW = MathF.Floor(rw * 0.4f), segX = L.X + nameW + 12;
            _p.PushClip(L);
            for (int i = 0; i < n; i++)
            {
                float y = L.Y - _rcScroll + i * RowH;
                if (y + RowH < L.Y || y > L.Bottom) continue;
                var (name, at, stay, frac, longest) = t.Rows[i];
                float by = y + 19;
                TextAt(FrCut(Upper(name), nameW, 18, FontWeight.SemiBold, Theme.Display), L.X, by, nameW + 2, longest ? CyY : CyW, 18, FontWeight.SemiBold,
                    TextAlignment.Leading, Theme.Display);
                string st = Upper(stay);
                float sw = CyMonoW(st), aw = CyMonoW(at);
                CyMono(st, L.X + rw, by, sw + 2, longest ? CyY : CyC, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
                CyMono(at, L.X + rw - sw - 12, by, aw + 2, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
                float bw = L.X + rw - sw - aw - 24 - segX;
                if (bw > 30) CyberHud.Segments(_p, new RectangleF(segX, by - 9, bw, 6), MathF.Max(0.04f, frac), longest ? CyY : CyC, CyberHud.Track, 20, 2);
            }
            _p.PopClip();
        }
        if (t.Names.Length > 0)
            CyMono(FrCut(Upper(t.Status95), c.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), c.X, c.Bottom - 6, c.Width + 2, CyG);
    }

    // ================= the retro styles (the shared 95 windows: 95, XP, 7, the Mac) =================

    // c: the window's inside. A 1x1: how long bold, "in VR" under it; one row: how long bold, the photos and worlds under it; taller:
    // the time in VR, then a line each - photos, worlds, people, the longest stay
    void Recap95(RectangleF c, WatchWidget w, PanelData d)
    {
        var t = Rct(d);
        if (c.Width < 70)
        {
            Value95(c, t.Has ? t.LenClock : "--", t.Has ? "in VR" : "none yet", Blk95);
            return;
        }
        if (!t.Has)
        {
            float my = MathF.Floor(c.Y + (c.Height - (c.Height >= 60 ? 28 : 13)) / 2);
            _ui.Text95(FrFit95(RecapNone, c.Width - 8, true), new RectangleF(c.X + 4, my, c.Width - 8, 13), Blk95, true, TextAlignment.Center);
            if (c.Height >= 60) _ui.Text95(RecapHint, new RectangleF(c.X + 4, my + 15, c.Width - 8, c.Bottom - my - 15), Gry95, false, TextAlignment.Center, true, true);
            return;
        }
        if (c.Height < 60)
        {
            float y = MathF.Floor(c.Y + c.Height / 2 - 14);
            _ui.Text95(FrFit95(t.Head95, c.Width - 8, true), new RectangleF(c.X + 4, y, c.Width - 8, 13), Blk95, true);
            _ui.Text95(FrFit95(t.Stats, c.Width - 8), new RectangleF(c.X + 4, y + 15, c.Width - 8, 13), Gry95);
            return;
        }
        var inner = RectangleF.Inflate(c, -4, -3);
        _ui.Text95(FrFit95(t.Head95, inner.Width, true, 13), new RectangleF(inner.X, inner.Y, inner.Width, 16), Blk95, true, size: 13);
        _ui.Text95(FrFit95(t.When, inner.Width), new RectangleF(inner.X, inner.Y + 17, inner.Width, 13), Gry95);
        float y0 = inner.Y + 36;
        int rows = Math.Max(0, (int)((inner.Bottom - y0) / 14));
        for (int i = 0; i < Math.Min(rows, 4); i++)
        {
            var (name, value) = i switch { 0 => ("Photos:", t.Photos), 1 => ("Worlds:", t.Worlds), 2 => ("People:", t.People), _ => ("Longest:", t.LongestName) };
            float y = y0 + i * 14, nw = _ui.Width95(Lang.T(name)) + 6;
            _ui.Text95(name, new RectangleF(inner.X, y, nw, 13), Blk95);
            _ui.Text95(FrFit95(value, inner.Width - nw), new RectangleF(inner.X + nw, y, inner.Width - nw, 13), Blk95, true);
        }
    }

    // Opened ("Session Summary"), in the window the style draws: the hourglass, how long in VR bold, when; the readings as a column of
    // fields; the worlds in a details list (world, arrived, stay); who you met beside OK
    Action<int, RectangleF, bool> _rcRow95;
    RcText _rcDraw95;
    float _rcAtX95, _rcStayX95;

    void Recap95Detail(RectangleF c, PanelData d)
    {
        var t = Rct(d);
        float sw = c.Right - 75 - 8 - c.X;
        if (t.Has) _ui.Text95(FrFit95(t.Status95, sw), new RectangleF(c.X, c.Bottom + 10, sw, 13), Blk95);
        W95Icons.Draw(_p, W95Icons.Id.Hourglass, c.X + 4, c.Y + 4);
        if (!t.Has)
        {
            _ui.Text95(FrFit95(RecapNone, c.Width - 60, true), new RectangleF(c.X + 48, c.Y + 6, c.Width - 52, 15), Blk95, true);
            _ui.Text95(RecapHint, new RectangleF(c.X + 48, c.Y + 24, c.Width - 52, 30), Blk95, false, TextAlignment.Leading, true, true);
            return;
        }
        _ui.Text95(FrFit95(t.Head95, c.Width - 52, true, 13), new RectangleF(c.X + 48, c.Y + 2, c.Width - 52, 18), Blk95, true, size: 13);
        _ui.Text95(FrFit95(t.When, c.Width - 52), new RectangleF(c.X + 48, c.Y + 21, c.Width - 52, 13), Blk95);
        // the readings, a field each (two columns)
        float fy = c.Y + 42, colW = MathF.Floor((c.Width - 8) / 2);
        Field95(c.X, fy, colW, "Photos taken:", t.Photos);
        Field95(c.X + colW + 8, fy, colW, "Worlds visited:", t.Worlds);
        Field95(c.X, fy + 22, colW, "People met:", t.PeopleFull);
        Field95(c.X + colW + 8, fy + 22, colW, "Longest stay:", t.LongestLine.Length > 0 ? t.LongestLine : "--");
        // the worlds
        float ly = fy + 48;
        const float AtW = 62, StayW = 70;
        var head = new RectangleF(c.X, ly, c.Width, 16);
        Head95(new RectangleF(head.X, head.Y, head.Width - AtW - StayW, 16), Lang.C("vrchat", "World"));
        Head95(new RectangleF(head.Right - AtW - StayW, head.Y, AtW, 16), "Arrived");
        Head95(new RectangleF(head.Right - StayW, head.Y, StayW, 16), "Stay");
        var box = new RectangleF(c.X, head.Bottom, c.Width, c.Bottom - head.Bottom);
        (_rcDraw95, _rcAtX95, _rcStayX95) = (t, head.Right - AtW - StayW, head.Right - StayW);
        _rcRow95 ??= RcRow95;
        _ui.List95(RecapList95, box, t.Rows.Count, -1, _rcRow95);
    }

    // a field of the summary: its name, its value in a sunken box
    void Field95(float x, float y, float w, string name, string value)
    {
        float nw = MathF.Min(_ui.Width95(Lang.T(name)) + 6, w * 0.6f);
        _ui.Text95(FrFit95(name, nw), new RectangleF(x, y + 3, nw, 13), Blk95);
        var box = new RectangleF(x + nw, y, w - nw, 19);
        _ui.Edit95(box, FrFit95(value, box.Width - 8));
    }

    void RcRow95(int i, RectangleF r, bool on)
    {
        var (name, at, stay, _, longest) = _rcDraw95.Rows[i];
        float aw = _rcAtX95 - r.X - 6;
        _ui.Text95(FrFit95(name, aw, longest), new RectangleF(r.X + 2, r.Y, aw, r.Height), Blk95, longest);
        _ui.Text95(FrFit95(at, _rcStayX95 - _rcAtX95 - 4), new RectangleF(_rcAtX95 + 2, r.Y, _rcStayX95 - _rcAtX95 - 4, r.Height), Blk95);
        float sw = MathF.Min(r.Right, _rcStayX95 + 70) - _rcStayX95 - 2;
        if (sw > 8) _ui.Text95(FrFit95(stay, sw), new RectangleF(_rcStayX95 + 2, r.Y, sw, r.Height), Blk95);
    }

    // ---- --snapshot's hooks ----

    public float RecapScroll => _rcScroll;
    public float RecapScrollMax => _rcMax;
    public string RecapShown(PanelData d) { var t = Rct(d); return t.Has ? $"{t.Len} | {t.Line}" : t.Head95; }
}
