using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The recap widget's opened view, Today (Erik 10-24: which worlds today and with whom, with the photos; the data: Sys/TodayLog.cs). The
// opened recap has two sides - Session, the last session's recap as it was (WristRecap.cs), and Today, the whole day from 5 AM across
// sessions and restarts: each world visited (its name, from - to, how long), who was there (the friends first, how many), the photos
// taken there; the day's time in worlds, worlds, people and photos; everyone met today along the bottom. Read only while Today shows
// (the app's TodayWatcher; else asleep). The side picked stays while the app runs.
// Modern: Session | Today chips left of the X (the gallery's All | Days), the readings on four tiles, the worlds a list of two lines
// each (a green dot: you are there now). Cyber: the same chips cut, five plates, the worlds' plate with segment bars. The retro styles
// that draw the shared windows (95, XP, 7, the Mac): a Today push button beside OK that stays pushed in (the gallery's Days is one
// too), the summary's fields and a details list (world, arrived, stay, people, photos) - a row picked says who was there in the
// status line. The Terminal: [F1] SESSION / [F2] TODAY keys, today as `last` lists logins (WristSocial.Term.cs). Nothing allocated a
// frame at rest: the texts are made per snapshot, language and clock style.
public sealed partial class WristPanel
{
    bool _rcToday;                               // the opened recap shows Today
    int _tdTop, _tdSel = -1;                     // (the Terminal's first line; the 95 list's row picked)
    const string TodayList95 = "wr95today", TodayGlyph = "";
    const string TodayReading = "Reading today's worlds", TodayNoSource = "No VRCX or VR Gallery",
        TodayNoSourceHint = "Install VRCX to see today's worlds and people", TodayNothing = "Nothing in VRChat today",
        TodayNothingHint = "The worlds you visit today show up here (the day starts at 5 AM)", TodayUnreadable = "Not readable right now";

    // the app reads the day only while this is true (OrbitApp.Social.cs)
    public bool RecapTodayOn => _rcToday && _open != null && !_closing && DetailIs("recap");

    void RcSetToday(bool on)
    {
        if (_rcToday == on) return;
        _rcToday = on;
        _rcScroll = 0;
        _rcTop = _tdTop = 0;
        _tdSel = -1;
        _ui.ListScrollTo95(TodayList95, 0);
        _ui.ListScrollTo95(RecapList95, 0);
        _dirty = true;
    }

    // Session | Today, right-aligned to 'right' at y (modern; Cyber's Press cuts them): returns their left edge
    float RcTabs(float right, float y)
    {
        float wt = MathF.Ceiling(_p.MeasureWidth(Lang.T("Today"), 17, FontWeight.SemiBold) + 30);
        float ws = MathF.Ceiling(_p.MeasureWidth(Lang.T("Session"), 17, FontWeight.SemiBold) + 30);
        var rt = new RectangleF(right - wt, y, wt, 40);
        var rs = new RectangleF(rt.X - 6 - ws, y, ws, 40);
        if (Press("wrcses", rs, null, "Session", !_rcToday, true, 18)) RcSetToday(false);
        if (Press("wrctoday", rt, null, "Today", _rcToday, true, 18)) RcSetToday(true);
        return rs.X;
    }

    // ================= the texts =================

    sealed class TdRow
    {
        public string Name = "", CyName = "", At = "", Range = "", CyRange = "", Stay = "", CyStay = "", People = "", Photos = "", PhotosText = "", CyPhotos = "",
            Sub = "", CySub = "", Who95 = "";
        public DateTime Start, End;
        public double Minutes;
        public float Frac;
        public bool Longest, Here;
    }

    sealed class TdText
    {
        public TodayLog.Snapshot S;
        public int Lang = -1;
        public CultureInfo Culture;
        public bool H24, Made;
        public bool Has;                         // worlds to list (else Msg, Hint)
        public string Msg = "", Hint = "", CyMsg = "", CyHint = "", Len = "–", LenClock = "–", When = "", Range = "", CyRange = "", Photos = "–", Worlds = "–",
            People = "–", Friends = "–", PeopleFull = "–", LongestLine = "", Met = "", CyMet = "", Line = "", Head95 = "", Status95 = "", CyHead = "";
        public readonly List<TdRow> Rows = new();
    }

    readonly TdText _tdt = new();

    TdText Tdt(PanelData d)
    {
        var s = d.Today;
        var t = _tdt;
        var culture = CultureInfo.CurrentCulture;
        bool h24 = _cfg.Data.Appearance.Clock24h;
        if (t.Made && ReferenceEquals(s, t.S) && t.Lang == Lang.Version && ReferenceEquals(culture, t.Culture) && h24 == t.H24) return t;
        (t.Made, t.S, t.Lang, t.Culture, t.H24) = (true, s, Lang.Version, culture, h24);
        TdMake(t, s, culture);
        return t;
    }

    void TdMake(TdText t, TodayLog.Snapshot s, CultureInfo culture)
    {
        t.Rows.Clear();
        string today = Lang.T("Today");
        (t.Has, t.Hint) = (false, "");
        (t.Len, t.LenClock, t.Range, t.CyRange, t.Photos, t.Worlds, t.People, t.Friends, t.PeopleFull, t.LongestLine, t.Met, t.CyMet, t.Line) =
            ("–", "–", "", "", "–", "–", "–", "–", "–", "", "", "", "");
        t.When = today;
        // nothing to list: what to say instead (not read yet, no database, not readable, no world today)
        (string msg, string hint) = s == null || s.State == TodayLog.State.Off ? (TodayReading, "")
            : s.State == TodayLog.State.NoSource ? (TodayNoSource, TodayNoSourceHint)
            : s.State == TodayLog.State.Unreadable ? (TodayUnreadable, "Trying again in a moment")
            : s.Visits.Count == 0 ? (TodayNothing, TodayNothingHint) : (null, "");
        (t.Msg, t.Hint) = (msg ?? "", hint);
        if (msg != null)
        {
            (t.CyMsg, t.CyHint) = (Upper(msg), Upper(hint));
            t.Head95 = Lang.T(msg);
            t.Status95 = today;
            t.CyHead = Upper(today);
            return;
        }
        t.Has = true;
        var first = s.Visits[0];
        var last = s.Visits[^1];
        var len = TimeSpan.FromMinutes(s.Minutes);
        t.Len = RecapLength(len);
        int minutes = Math.Max(0, (int)len.TotalMinutes);
        t.LenClock = $"{minutes / 60}:{minutes % 60:00}";
        string now = Lang.T("now");
        t.Range = ShortClock(first.Start) + " – " + (last.Here ? now : ShortClock(last.End));
        t.CyRange = t.Range.ToUpper(culture);
        t.When = today + " · " + t.Range;
        t.Photos = s.Photos >= 0 ? s.Photos.ToString(culture) : "–";
        var worlds = new HashSet<string>(StringComparer.Ordinal);
        TodayLog.Visit longest = null;
        foreach (var v in s.Visits)
        {
            worlds.Add(v.World);
            if (longest == null || v.Minutes > longest.Minutes) longest = v;
        }
        t.Worlds = worlds.Count.ToString(culture);
        t.People = s.People.ToString(culture);
        t.Friends = s.Friends.ToString(culture);
        t.PeopleFull = s.Friends > 0 ? t.People + " (" + RoomPanel.FriendsText(s.Friends) + ")" : t.People;
        t.LongestLine = TdName(longest) + " · " + RecapStay(longest.Minutes);
        string names = TdWho(s.Names, s.People);
        t.Met = names.Length == 0 ? "" : Lang.F("Met: {0}", names) + (s.Friends > 0 ? " · " + RoomPanel.FriendsText(s.Friends) : "");
        t.CyMet = t.Met.ToUpper(culture);
        // the Terminal's sums: "12 photos · 4 worlds · 23 people · longest: The Black Cat (48 min)" (the session's line's words)
        var parts = new List<string>();
        if (s.Photos >= 0) parts.Add(RecapPhotos(s.Photos));
        parts.Add(RecapWorlds(worlds.Count));
        parts.Add(RoomPanel.PeopleText(s.People));
        parts.Add(Lang.F("longest: {0} ({1})", TdName(longest), RecapStay(longest.Minutes)));
        t.Line = string.Join(" · ", parts);
        t.Head95 = Lang.F("{0} in VRChat", t.Len);
        t.Status95 = t.Met.Length > 0 ? t.Met : t.When;
        t.CyHead = t.When.ToUpper(culture);
        double total = Math.Max(1, s.Minutes);
        foreach (var v in s.Visits)
        {
            var r = new TdRow { Start = v.Start, End = v.End, Minutes = v.Minutes, Longest = ReferenceEquals(v, longest), Here = v.Here };
            r.Name = TdName(v);
            r.CyName = r.Name.ToUpper(culture);
            r.At = ShortClock(v.Start);
            r.Range = r.At + " – " + (v.Here ? now : ShortClock(v.End));
            r.CyRange = r.Range.ToUpper(culture);
            r.Stay = RecapStay(v.Minutes);
            r.CyStay = r.Stay.ToUpper(culture);
            r.People = v.People.ToString(culture);
            r.Photos = v.Photos >= 0 ? v.Photos.ToString(culture) : "–";
            r.PhotosText = v.Photos > 0 ? RecapPhotos(v.Photos) : "";
            r.CyPhotos = r.PhotosText.ToUpper(culture);
            string who = TdWho(v.Names, v.People);
            r.Sub = v.People == 0 ? Lang.T("Nobody else there") : RoomPanel.PeopleText(v.People) + " · " + who;
            r.CySub = r.Sub.ToUpper(culture);
            r.Who95 = r.Name + ": " + (v.People == 0 ? Lang.T("Nobody else there") : who);
            r.Frac = (float)Math.Clamp(v.Minutes / total, 0, 1);
            t.Rows.Add(r);
        }
    }

    static string TdName(TodayLog.Visit v) => v.World.Length > 0 ? v.World : Lang.T("Somewhere in VRChat");

    // "Nova, Opal, Kite + 9": three names (the friends first) and how many more
    static string TdWho(List<string> names, int people)
    {
        int shown = Math.Min(names.Count, 3), rest = people - shown;
        return shown == 0 ? "" : string.Join(", ", names.GetRange(0, shown)) + (rest > 0 ? Lang.F(" + {0}", rest) : "");
    }

    // ================= Modern =================

    // Opened, Today: when under the title; the day's time in VRChat, worlds, people and photos on four tiles; every world visited today
    // - its name, from - to, how long; who was there and the photos taken there under it - the stick scrolling them; who you met today
    // along the bottom
    void TodayDetail(RectangleF E, PanelData d)
    {
        var t = Tdt(d);
        float x = E.X + 24, right = E.Right - 24;
        if (!t.Has)
        {
            _rcMax = 0;
            var L0 = new RectangleF(x, E.Y + 100, right - x, E.Height - 140);
            float my = L0.Y + (L0.Height - 90) / 2;
            _p.Icon(TodayGlyph, new RectangleF(L0.X, my - 50, L0.Width, 48), Bento.Faint, 38);
            _p.Text(FrCut(t.Msg, L0.Width, 22, FontWeight.SemiBold), new RectangleF(L0.X, my + 6, L0.Width, 34), Bento.Text, 22, FontWeight.SemiBold,
                TextAlignment.Center);
            if (t.Hint.Length > 0)
                _p.Text(FrCut(t.Hint, L0.Width, SmallText, FontWeight.Normal), new RectangleF(L0.X, my + 42, L0.Width, 28), Sub, SmallText, FontWeight.Normal,
                    TextAlignment.Center);
            return;
        }
        TextAt(FrCut(t.When, right - x, 19, FontWeight.Normal), x, E.Y + 82, right - x + 2, Sub, 19, FontWeight.Normal);
        // the day's readings, a tile each
        float ty = E.Y + 98, cw = (right - x - 3 * 12) / 4, ch = 62;
        RcTile(new RectangleF(x, ty, cw, ch), "In VRChat", t.Len);
        RcTile(new RectangleF(x + (cw + 12), ty, cw, ch), "Worlds", t.Worlds);
        RcTile(new RectangleF(x + 2 * (cw + 12), ty, cw, ch), "People", t.People);
        RcTile(new RectangleF(x + 3 * (cw + 12), ty, cw, ch), "Photos", t.Photos);
        // the worlds, two lines each
        float ly = ty + ch + 12;
        CapText(Upper("Worlds today"), x, ly + 16, right - x, Sub);
        var L = new RectangleF(x, ly + 26, right - x, E.Bottom - 48 - (ly + 26));
        int n = t.Rows.Count;
        const float RowH = 56;
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
            var row = t.Rows[i];
            var wt = row.Longest ? FontWeight.SemiBold : FontWeight.Normal;
            // the world (a green dot: you are there now), from - to, how long
            float sw = _p.MeasureWidth(row.Stay, SmallText, FontWeight.SemiBold);
            TextAt(row.Stay, L.X + rw - sw, y + 22, sw + 2, row.Longest || row.Here ? Bento.Text : Sub, SmallText, FontWeight.SemiBold);
            float aw = _p.MeasureWidth(row.Range, SmallText, FontWeight.Normal);
            TextAt(row.Range, L.X + rw - sw - 12 - aw, y + 22, aw + 2, Sub, SmallText, FontWeight.Normal);
            float nx = L.X;
            if (row.Here) { _p.Circle(nx + 5, y + 15, 5, Bento.Ok); nx += 16; }
            float nw = L.X + rw - sw - aw - 24 - nx;
            if (nw > 20) TextAt(FrCut(row.Name, nw, 19, wt), nx, y + 22, nw + 2, Bento.Text, 19, wt);
            // under it: who was there (the friends first, how many), the photos taken there
            float pw = row.PhotosText.Length > 0 ? _p.MeasureWidth(row.PhotosText, SmallText, FontWeight.Normal) : 0;
            if (pw > 0) TextAt(row.PhotosText, L.X + rw - pw, y + 45, pw + 2, Sub, SmallText, FontWeight.Normal);
            float ww = rw - pw - (pw > 0 ? 12 : 0);
            if (ww > 20) TextAt(FrCut(row.Sub, ww, SmallText, FontWeight.Normal), L.X, y + 45, ww + 2, Sub, SmallText, FontWeight.Normal);
            if (i < n - 1) _p.Fill(new RectangleF(L.X, y + RowH - 3, rw, 1), Bento.Track);
        }
        _p.PopClip();
        if (max > 0)
        {
            float bh = MathF.Max(24, L.Height * L.Height / content), by = L.Y + (L.Height - bh) * (_rcScroll / max);
            _p.Fill(new RectangleF(L.Right - 4, by, 4, bh), Theme.WithAlpha(Bento.Text, 0.3f), 2);
        }
        if (t.Met.Length > 0) TextAt(FrCut(t.Met, right - x, SmallText, FontWeight.Normal), x, E.Bottom - 22, right - x + 2, Sub, SmallText, FontWeight.Normal);
    }

    // ================= Cyber =================
    // IN VRCHAT and the day's readings as plates, the worlds' plate - a row each: the name, from - to, how long, its share of the day as
    // segments (the longest yellow, a cyan mark: you are there now), who was there and the photos under it in monospace

    void CyberTodayDetail(RectangleF c, PanelData d)
    {
        var t = Tdt(d);
        if (!t.Has)
        {
            _rcMax = 0;
            CyMono(FrCut(t.CyMsg, c.Width, 17, FontWeight.SemiBold, CyberHud.Mono), c.X, c.Y + c.Height / 2, c.Width + 2, CyG, 17);
            if (t.CyHint.Length > 0)
                CyMono(FrCut(t.CyHint, c.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), c.X, c.Y + c.Height / 2 + 24, c.Width + 2, CyberHud.Faint);
            return;
        }
        float gap = 10, pw = (c.Width - 4 * gap) / 5, ph = 92;
        for (int i = 0; i < 5; i++)
        {
            var p = new RectangleF(MathF.Round(c.X + i * (pw + gap)), c.Y, MathF.Round(pw), ph);
            var (tag, value, col) = i switch
            {
                0 => ("In VRChat", t.LenClock, CyC),
                1 => ("Worlds", t.Worlds, CyW),
                2 => ("People", t.People, CyW),
                3 => ("Friends", t.Friends, CyW),
                _ => ("Photos", t.Photos, CyY),
            };
            CyPlate(p, tag, null, 0);
            CyValue(p.X + 14, p.Bottom - 16, value, CyFit(value, p.Width - 28, 40), col);
        }
        var wp = new RectangleF(c.X, c.Y + ph + 12, c.Width, c.Bottom - (c.Y + ph + 12) - 26);
        CyPlate(wp, "Worlds today", t.CyRange, CyG);
        var L = new RectangleF(wp.X + 14, wp.Y + 36, wp.Width - 28, wp.Height - 44);
        int n = t.Rows.Count;
        const float RowH = 42;
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
            var row = t.Rows[i];
            float by = y + 17, by2 = y + 35, nx = L.X;
            if (row.Here) { _p.Fill(new RectangleF(nx, by - 11, 6, 6), CyC); nx += 12; }
            TextAt(FrCut(row.CyName, L.X + nameW - nx, 18, FontWeight.SemiBold, Theme.Display), nx, by, L.X + nameW - nx + 2, row.Longest ? CyY : CyW, 18,
                FontWeight.SemiBold, TextAlignment.Leading, Theme.Display);
            float sw = CyMonoW(row.CyStay), aw = CyMonoW(row.CyRange);
            CyMono(row.CyStay, L.X + rw, by, sw + 2, row.Longest ? CyY : CyC, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
            CyMono(row.CyRange, L.X + rw - sw - 12, by, aw + 2, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
            float bw = L.X + rw - sw - aw - 24 - segX;
            if (bw > 30) CyberHud.Segments(_p, new RectangleF(segX, by - 9, bw, 6), MathF.Max(0.04f, row.Frac), row.Longest ? CyY : CyC, CyberHud.Track, 20, 2);
            float qw = row.CyPhotos.Length > 0 ? CyMonoW(row.CyPhotos) : 0;
            if (qw > 0) CyMono(row.CyPhotos, L.X + rw, by2, qw + 2, CyW, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
            float ww = rw - qw - (qw > 0 ? 12 : 0);
            if (ww > 30) CyMono(FrCut(row.CySub, ww, CyNote, FontWeight.SemiBold, CyberHud.Mono), L.X, by2, ww + 2, CyG);
        }
        _p.PopClip();
        if (t.CyMet.Length > 0)
            CyMono(FrCut(t.CyMet, c.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), c.X, c.Bottom - 6, c.Width + 2, CyG);
    }

    // ================= the retro styles (the shared 95 windows: 95, XP, 7, the Mac) =================

    // Opened, Today pushed in: the clock, the day's time in VRChat bold, when; the summary's fields; the worlds in a details list (world,
    // arrived, stay, people, photos) - a row picked says who was there in the status line beside OK, else who you met today
    Action<int, RectangleF, bool> _tdRow95;
    TdText _tdDraw95;
    float _tdAtX95, _tdStayX95, _tdPplX95, _tdPicX95;

    void Today95Detail(RectangleF c, PanelData d)
    {
        var t = Tdt(d);
        float sx = c.X + 75 + 8, sw = c.Right - 75 - 8 - sx;
        W95Icons.Draw(_p, W95Icons.Id.Clock, c.X + 4, c.Y + 4);
        if (!t.Has)
        {
            _ui.Text95(FrFit95(t.Msg, c.Width - 60, true), new RectangleF(c.X + 48, c.Y + 6, c.Width - 52, 15), Blk95, true);
            if (t.Hint.Length > 0) _ui.Text95(t.Hint, new RectangleF(c.X + 48, c.Y + 24, c.Width - 52, 30), Blk95, false, TextAlignment.Leading, true, true);
            return;
        }
        if (_tdSel >= t.Rows.Count) _tdSel = -1;
        _ui.Text95(FrFit95(_tdSel >= 0 ? t.Rows[_tdSel].Who95 : t.Status95, sw), new RectangleF(sx, c.Bottom + 10, sw, 13), Blk95);
        _ui.Text95(FrFit95(t.Head95, c.Width - 52, true, 13), new RectangleF(c.X + 48, c.Y + 2, c.Width - 52, 18), Blk95, true, size: 13);
        _ui.Text95(FrFit95(t.When, c.Width - 52), new RectangleF(c.X + 48, c.Y + 21, c.Width - 52, 13), Blk95);
        // the readings, a field each (two columns): the session's fields, the day's values
        float fy = c.Y + 42, colW = MathF.Floor((c.Width - 8) / 2);
        Field95(c.X, fy, colW, "Photos taken:", t.Photos);
        Field95(c.X + colW + 8, fy, colW, "Worlds visited:", t.Worlds);
        Field95(c.X, fy + 22, colW, "People met:", t.PeopleFull);
        Field95(c.X + colW + 8, fy + 22, colW, "Longest stay:", t.LongestLine.Length > 0 ? t.LongestLine : "--");
        // the worlds
        float ly = fy + 48;
        const float AtW = 44, StayW = 58, PplW = 42, PicW = 42;
        var head = new RectangleF(c.X, ly, c.Width, 16);
        float atX = head.Right - AtW - StayW - PplW - PicW;
        Head95(new RectangleF(head.X, head.Y, atX - head.X, 16), Lang.C("vrchat", "World"));
        Head95(new RectangleF(atX, head.Y, AtW, 16), "Arrived");
        Head95(new RectangleF(atX + AtW, head.Y, StayW, 16), "Stay");
        Head95(new RectangleF(atX + AtW + StayW, head.Y, PplW, 16), "People");
        Head95(new RectangleF(atX + AtW + StayW + PplW, head.Y, PicW, 16), "Photos");
        var box = new RectangleF(c.X, head.Bottom, c.Width, c.Bottom - head.Bottom);
        (_tdDraw95, _tdAtX95, _tdStayX95, _tdPplX95, _tdPicX95) = (t, atX, atX + AtW, atX + AtW + StayW, atX + AtW + StayW + PplW);
        _tdRow95 ??= TdRow95;
        int hit = _ui.List95(TodayList95, box, t.Rows.Count, _tdSel, _tdRow95);
        if (hit >= 0)
        {
            _tdSel = hit == _tdSel ? -1 : hit;          // the same row again: who you met today again
            _dirty = true;
        }
    }

    void TdRow95(int i, RectangleF r, bool on)
    {
        var row = _tdDraw95.Rows[i];
        uint ink = on ? Wht95 : Blk95;
        float nw = _tdAtX95 - r.X - 6;
        _ui.Text95(FrFit95(row.Name, nw, row.Longest), new RectangleF(r.X + 2, r.Y, nw, r.Height), ink, row.Longest);
        TdCol95(row.At, _tdAtX95, _tdStayX95 - _tdAtX95, r, ink);
        TdCol95(row.Stay, _tdStayX95, _tdPplX95 - _tdStayX95, r, ink);
        TdCol95(row.People, _tdPplX95, _tdPicX95 - _tdPplX95, r, ink);
        TdCol95(row.Photos, _tdPicX95, 42, r, ink);
    }

    // a column's text of a row, cut to the column (and to the row: the scroll bar)
    void TdCol95(string s, float x, float w, RectangleF r, uint ink)
    {
        w = MathF.Min(r.Right, x + w) - x - 4;
        if (w > 8) _ui.Text95(FrFit95(s, w), new RectangleF(x + 2, r.Y, w, r.Height), ink);
    }

    // ---- --snapshot's hooks ----

    public bool RecapTodayShown => _rcToday && DetailIs("recap");
    public string TodayShown(PanelData d) { var t = Tdt(d); return t.Has ? $"{t.Len} | {t.Line}" : t.Head95; }
    public int TodayRowsShown(PanelData d) => Tdt(d).Rows.Count;
    public void TodayForTest(bool on) => RcSetToday(on);
}
