using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using T = Orbit.TerminalSkin;

namespace Orbit;

// The friends online and the session's recap on the terminal. The friends the way `who` lists the users logged in: the box says how
// many ("12 users"), the names under it; opened, `who -H` - NAME, LINE (the instance's kind: pub, fr+, inv...), TIME (since when),
// COMMENT (the world in brackets) - the ones with you bright, a starred one with a *. The recap as its log, `tail session.log`: the
// headset on, each world with its stay, the headset off and how long, the photos, worlds and people, the longest stay, who you met.
// Opened, [F1] SESSION / [F2] TODAY along its bottom (Erik 10-24; WristToday.cs): today as `last -s 05:00` lists logins - a world a
// line, when you came and left and (how long), or "still here"; who was there and the photos under it; the day's sums at the end.
public sealed partial class WristPanel
{
    // a text cut to w in the terminal's font, kept with the friends' other texts
    string FrTFit(string s, float w, int n = 1)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, (float)n, Vortice.DirectWrite.FontWeight.Normal, "term");
        if (_frFits.TryGetValue(key, out var done)) return done;
        if (_frFits.Count > 512) _frFits.Clear();
        return _frFits[key] = TFit(s, w, n);
    }

    // who's LINE: the instance's kind as a terminal line's name
    static string TLine(FriendsWatcher.Where k) => k switch
    {
        FriendsWatcher.Where.Here => "here",
        FriendsWatcher.Where.Public => "pub",
        FriendsWatcher.Where.FriendsPlus => "fr+",
        FriendsWatcher.Where.Friends => "frnd",
        FriendsWatcher.Where.InvitePlus => "inv+",
        FriendsWatcher.Where.Invite => "inv",
        FriendsWatcher.Where.Group => "grp",
        FriendsWatcher.Where.GroupPlus => "grp+",
        FriendsWatcher.Where.GroupPublic => "gpub",
        FriendsWatcher.Where.Private => "priv",
        FriendsWatcher.Where.Traveling => "trav",
        _ => "?",
    };

    // the users line ("12 USERS"), made with the list
    string _tUsers;
    int _tUsersN = -1, _tUsersLang = -1;
    string TUsers(int n)
    {
        if (n != _tUsersN || _tUsersLang != Lang.Version) (_tUsers, _tUsersN, _tUsersLang) = (T.Up(n == 1 ? Lang.T("1 user") : Lang.F("{0} users", n)), n, Lang.Version);
        return _tUsers;
    }

    // c: the box's inside, under its WHO line
    void TFriends(RectangleF r, RectangleF c, WatchWidget w, PanelData d)
    {
        FrNow(d);
        int n = _frRows.Count;
        if (c.Width < 70)
        {
            TValue(c, _frCount, FrTFit(Upper(_frNote[1]), c.Width - 2), n > 0 ? T.INK : T.DIM);
            return;
        }
        if (n == 0)
        {
            float my = MathF.Floor(c.Y + (c.Height - (c.Height >= 40 ? 18 : 7)) / 2);
            TCaps(FrTFit(Upper(_frMsg ?? "Nobody online"), c.Width - 6), c.X + 3, my, T.DIM);
            if (c.Height >= 40 && !string.IsNullOrEmpty(_frHint)) TCaps(FrTFit(Upper(_frHint), c.Width - 6), c.X + 3, my + 11, T.FAINT);
            return;
        }
        if (c.Height < 40)
        {
            // one row: how many users, the first names after it
            float top = MathF.Floor(c.Y + (c.Height - 18) / 2);
            TCaps(FrTFit(TUsers(n), c.Width - 6), c.X + 3, top, T.INK);
            TCaps(FrTFit(Upper(_frNames), c.Width - 6), c.X + 3, top + 11, _frHere > 0 ? T.HOT : T.MID);
            return;
        }
        // tall: who's lines - the name, the line, the world
        int rows = Math.Max(1, (int)((c.Height - 2) / 11));
        bool cut = n > rows;
        int list = cut ? rows - 1 : n;
        float y0 = c.Y + 2, nameW = MathF.Floor((c.Width - 6) * 0.45f);
        for (int i = 0; i < list; i++)
        {
            var e = _frRows[i];
            float y = y0 + i * 11;
            uint ink = e.Here ? T.HOT : T.INK;
            TCaps(FrTFit(Upper(e.Name), nameW - 4), c.X + 3, y, ink);
            TCaps(FrTFit(Upper(e.World), c.Right - 3 - (c.X + 3 + nameW)), c.X + 3 + nameW, y, e.Here ? T.HOT : T.MID);
        }
        if (cut) TCaps(FrTFit(Upper(MoreText(n - list)), c.Width - 6), c.X + 3, y0 + list * 11, T.DIM);
    }

    // c: the box's inside, under its LAST line
    void TRecap(RectangleF c, WatchWidget w, PanelData d)
    {
        var t = Rct(d);
        if (c.Width < 70)
        {
            TValue(c, t.Has ? t.LenClock : "--", t.Has ? Upper(Lang.T("in VR")) : FrTFit(Upper("none yet"), c.Width - 2), t.Has ? T.INK : T.DIM);
            return;
        }
        if (!t.Has)
        {
            TCaps(FrTFit(Upper(RecapNone), c.Width - 6), c.X + 3, MathF.Floor(c.Y + (c.Height - 7) / 2), T.DIM);
            return;
        }
        if (c.Height < 40)
        {
            float top = MathF.Floor(c.Y + (c.Height - 18) / 2);
            TCaps(FrTFit(Upper(t.Head95), c.Width - 6), c.X + 3, top, T.INK);
            TCaps(FrTFit(Upper(t.Stats), c.Width - 6), c.X + 3, top + 11, T.MID);
            return;
        }
        var log = TRecapLog(t);
        int rows = Math.Max(1, (int)((c.Height - 2) / 11));
        int from = Math.Max(0, log.Count - rows);         // the end of the log: the summary lines
        for (int i = from; i < log.Count; i++)
        {
            var (at, text, ink) = log[i];
            float y = c.Y + 2 + (i - from) * 11;
            TCaps(at, c.X + 3, y, T.DIM);
            float x = c.X + 3 + T.W(at) + 6;
            TCaps(FrTFit(text, c.Right - 3 - x), x, y, ink);
        }
    }

    // the session's log, made per recap and language: "[19:02]" and what happened
    readonly List<(string at, string text, uint ink)> _tLog = new();
    SessionRecap _tLogOf;
    int _tLogLang = -1;
    bool _tLog24;

    List<(string at, string text, uint ink)> TRecapLog(RcText t)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        if (ReferenceEquals(t.R, _tLogOf) && _tLogLang == Lang.Version && h24 == _tLog24) return _tLog;
        (_tLogOf, _tLogLang, _tLog24) = (t.R, Lang.Version, h24);
        _tLog.Clear();
        var r = t.R;
        if (r == null) return _tLog;
        string At(DateTime x) => "[" + x.ToString("HH:mm", CultureInfo.InvariantCulture) + "]";
        _tLog.Add((At(r.Start), T.Up(Lang.T("headset on")), T.MID));
        foreach (var w in r.Worlds)
            _tLog.Add((At(w.Start), T.Up(Lang.F("world {0} - {1}", w.Name, RecapStay(w.Minutes))), ReferenceEquals(w, r.Longest) ? T.HOT : T.INK));
        _tLog.Add((At(r.End), T.Up(Lang.F("headset off - {0} in VR", t.Len)), T.HOT));
        string end = At(r.End);
        _tLog.Add((end, T.Up(t.Line.Replace(" · ", " - ")), T.INK));
        if (t.Names.Length > 0) _tLog.Add((end, T.Up(t.Status95), T.MID));
        return _tLog;
    }

    // Opened: a terminal window - the friends as `who -H`, the recap as `tail session.log`; the stick scrolls a line a notch
    void TSocialDetail(RectangleF win, PanelData d, string kind)
    {
        _p.Fill(win, T.BG);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), kind == "friends" ? "Friends" : "Session Summary");
        if (_ui.CaptionClose95("wd95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 8, win.Y + 27, win.Width - 16, win.Height - 27 - 8);
        if (kind == "friends") TWho(c, d);
        else TRecapDetail(c, d);
    }

    // the recap: its two keys along the bottom, the session's log or today's logins over them
    void TRecapDetail(RectangleF c, PanelData d)
    {
        float ky = c.Bottom - 23;
        if (TKey("wrcses", new RectangleF(c.X, ky, 78, 23), "[F1]", !_rcToday, true, "SESSION")) RcSetToday(false);
        if (TKey("wrctoday", new RectangleF(c.X + 82, ky, 78, 23), "[F2]", _rcToday, true, "TODAY")) RcSetToday(true);
        var lc = new RectangleF(c.X, c.Y, c.Width, c.Height - 28);
        if (_rcToday) TLastDetail(lc, d);
        else TLogDetail(lc, d);
    }

    void TWho(RectangleF c, PanelData d)
    {
        FrNow(d);
        int n = _frRows.Count;
        TPut("$ who -H", c.X, c.Y, T.MID);
        float y0 = c.Y + 13;
        if (n == 0)
        {
            TPut(FrTFit(Upper(_frMsg ?? "Nobody online"), c.Width), c.X, y0 + 4, T.HOT);
            if (!string.IsNullOrEmpty(_frHint)) TPut(FrTFit(_frHint, c.Width), c.X, y0 + 15, T.DIM);
            TPut(FrTFit(_frStatus, c.Width), c.X, y0 + 30, T.DIM);
            TPut("$ _", c.X, y0 + 45, T.INK);
            return;
        }
        // the columns: NAME, LINE, TIME, COMMENT (who's own order)
        float nameX = c.X + 8, lineX = nameX + MathF.Floor(c.Width * 0.3f), timeX = lineX + 36, comX = timeX + 42, right = c.Right - 8;
        TPut("NAME", nameX, y0, T.DIM);
        TPut("LINE", lineX, y0, T.DIM);
        TPut("TIME", timeX, y0, T.DIM);
        TPut("COMMENT", comX, y0, T.DIM);
        float ly = y0 + 13, bottom = c.Bottom - 13;
        int rows = Math.Max(1, (int)((bottom - ly) / 11)), maxTop = Math.Max(0, n - rows);
        if (_ui.Wheel != 0 && maxTop > 0 && _ui.OverNow(c)) { _frTop -= Math.Sign(_ui.Wheel); _dirty = true; }
        _frTop = Math.Clamp(_frTop, 0, maxTop);
        for (int i = _frTop; i < Math.Min(n, _frTop + rows); i++)
        {
            var e = _frRows[i];
            float y = ly + (i - _frTop) * 11;
            uint ink = e.Here ? T.HOT : T.INK;
            if (e.Star) TPut("*", c.X, y, T.HOT);
            TPut(FrTFit(e.Name, lineX - nameX - 6), nameX, y, ink);
            TPut(TLine(e.Kind), lineX, y, e.Here ? T.HOT : T.MID);
            TPut(e.Since95.Length > 0 ? FrTFit(e.Since95, comX - timeX - 4) : "-", timeX, y, T.MID);
            TPut(FrTFit(e.World, right - comX), comX, y, ink);
        }
        if (maxTop > 0)
        {
            float h = rows * 11, th = MathF.Max(8, MathF.Round(h * rows / n)), ty = MathF.Round(ly + (h - th) * _frTop / maxTop);
            _p.Fill(new RectangleF(c.Right - 3, ly, 2, h), T.FAINT);
            _p.Fill(new RectangleF(c.Right - 4, ty, 4, th), T.INK);
        }
        // like `who -q`: how many, and where VRCX's list is from
        if (!ReferenceEquals(_tWhoOf, _frStatus95)) (_tWhoOf, _tWhoFoot) = (_frStatus95, TUsers(n) + "  " + Upper(_frStatus));
        TPut(FrTFit(_tWhoFoot, c.Width), c.X, c.Bottom - 9, T.DIM);
    }
    string _tWhoOf, _tWhoFoot;

    void TLogDetail(RectangleF c, PanelData d)
    {
        var t = Rct(d);
        TPut("$ tail session.log", c.X, c.Y, T.MID);
        float y0 = c.Y + 15;
        if (!t.Has)
        {
            TPut(FrTFit(Upper(RecapNone), c.Width), c.X, y0 + 2, T.HOT);
            TPut(FrTFit(RecapHint, c.Width), c.X, y0 + 13, T.DIM);
            TPut("$ _", c.X, y0 + 28, T.INK);
            return;
        }
        var log = TRecapLog(t);
        int n = log.Count, rows = Math.Max(1, (int)((c.Bottom - 13 - y0) / 11)), maxTop = Math.Max(0, n - rows);
        if (_ui.Wheel != 0 && maxTop > 0 && _ui.OverNow(c)) { _rcTop -= Math.Sign(_ui.Wheel); _dirty = true; }
        _rcTop = Math.Clamp(_rcTop, 0, maxTop);
        float tx = c.X + T.W("[00:00]") + 6;
        for (int i = _rcTop; i < Math.Min(n, _rcTop + rows); i++)
        {
            var (at, text, ink) = log[i];
            float y = y0 + (i - _rcTop) * 11;
            TPut(at, c.X, y, T.DIM);
            TPut(FrTFit(text, c.Right - 8 - tx), tx, y, ink);
        }
        if (maxTop > 0)
        {
            float h = rows * 11, th = MathF.Max(8, MathF.Round(h * rows / n)), ty = MathF.Round(y0 + (h - th) * _rcTop / maxTop);
            _p.Fill(new RectangleF(c.Right - 3, y0, 2, h), T.FAINT);
            _p.Fill(new RectangleF(c.Right - 4, ty, 4, th), T.INK);
        }
        TPut("$ _", c.X, c.Bottom - 9, T.INK);
    }

    // ---- today: `last` ----

    const string TLastCmd = "$ last -s 05:00";

    void TLastDetail(RectangleF c, PanelData d)
    {
        var t = Tdt(d);
        TPut(TLastCmd, c.X, c.Y, T.MID);
        float y0 = c.Y + 15;
        if (!t.Has)
        {
            TPut(FrTFit(Upper(t.Msg), c.Width), c.X, y0 + 2, T.HOT);
            if (t.Hint.Length > 0) TPut(FrTFit(t.Hint, c.Width), c.X, y0 + 13, T.DIM);
            TPut("$ _", c.X, y0 + 28, T.INK);
            return;
        }
        var log = TTodayLog(t);
        int n = log.Count, rows = Math.Max(1, (int)((c.Bottom - 13 - y0) / 11)), maxTop = Math.Max(0, n - rows);
        if (_ui.Wheel != 0 && maxTop > 0 && _ui.OverNow(c)) { _tdTop -= Math.Sign(_ui.Wheel); _dirty = true; }
        _tdTop = Math.Clamp(_tdTop, 0, maxTop);
        float right = c.Right - 8;
        for (int i = _tdTop; i < Math.Min(n, _tdTop + rows); i++)
        {
            var (text, when, ink, whenInk) = log[i];
            float y = y0 + (i - _tdTop) * 11, ww = when.Length > 0 ? T.W(when) : 0;
            if (ww > 0) TPut(when, right - ww, y, whenInk);
            if (text.Length > 0) TPut(FrTFit(text, right - c.X - (ww > 0 ? ww + 12 : 0)), c.X, y, ink);
        }
        if (maxTop > 0)
        {
            float h = rows * 11, th = MathF.Max(8, MathF.Round(h * rows / n)), ty = MathF.Round(y0 + (h - th) * _tdTop / maxTop);
            _p.Fill(new RectangleF(c.Right - 3, y0, 2, h), T.FAINT);
            _p.Fill(new RectangleF(c.Right - 4, ty, 4, th), T.INK);
        }
        TPut("$ _", c.X, c.Bottom - 9, T.INK);
    }

    // today's logins, made per snapshot and language: a world's line (its name; "19:43 - 21:10 (01:27)" or "23:58   STILL HERE" at the
    // right), who was there and the photos under it, dim; a blank line, then the day's sums and who you met (as `last` ends with
    // "wtmp begins")
    readonly List<(string text, string when, uint ink, uint whenInk)> _tToday = new();
    TodayLog.Snapshot _tTodayOf;
    int _tTodayLang = -1;

    List<(string text, string when, uint ink, uint whenInk)> TTodayLog(TdText t)
    {
        if (ReferenceEquals(t.S, _tTodayOf) && _tTodayLang == Lang.Version) return _tToday;
        (_tTodayOf, _tTodayLang) = (t.S, Lang.Version);
        _tToday.Clear();
        static string Hm(DateTime x) => x.ToString("HH:mm", CultureInfo.InvariantCulture);
        foreach (var r in t.Rows)
        {
            int m = Math.Max(0, (int)r.Minutes);
            string when = r.Here ? Hm(r.Start) + "   " + T.Up(Lang.T("still here")) : $"{Hm(r.Start)} - {Hm(r.End)} ({m / 60:00}:{m % 60:00})";
            _tToday.Add((T.Up(r.Name), when, r.Longest ? T.HOT : T.INK, r.Here ? T.HOT : T.MID));
            _tToday.Add(("  " + T.Up(r.Sub), T.Up(r.PhotosText), T.DIM, T.DIM));
        }
        _tToday.Add(("", "", T.DIM, T.DIM));
        _tToday.Add((T.Up(t.Line.Replace(" · ", " - ")), "", T.INK, T.DIM));
        if (t.Met.Length > 0) _tToday.Add((T.Up(t.Met.Replace(" · ", " - ")), "", T.MID, T.DIM));
        return _tToday;
    }
}
