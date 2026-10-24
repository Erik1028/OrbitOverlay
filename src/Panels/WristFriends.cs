using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The friends widget ("friends"; the data: Sys/Friends.cs - VRCX's database, read only): how many of your VRChat friends are online.
// A 1x1 is the count with a green dot; a 2x1 the count and their initials (the 4 x 3 page's M: and the first names); a 2x2 a short
// list, name · world (the 4 x 3 page's L: and how long ago). A tap opens all of them: the starred first (the Room panel's stars,
// VRCX's favourites), then by world (with you, the worlds A-Z, private, traveling), then by name - each with the world, its kind
// (Public, Friends+, Invite...) and how long they have been there; the stick scrolls it. The friends in your own instance are in the
// list (the Room's data), ringed green. VRCX missing or closed: the widget says so (nothing it saw is live then).
// Modern: Bento cards, initials in the avatar colours. Cyber: the yellow FRIENDS tag, the count in the condensed bold, a segment per
// friend, the list as plates. The retro styles that draw the shared windows (95, XP, 7, the Mac): a little window with the count, the
// names in their font; opened, a details list (star, name, world, since) with a status line beside OK. The Terminal: who
// (WristSocial.Term.cs). Nothing allocated a frame at rest: the texts are made when the list, the room, the stars or the language
// change, cut texts kept.
public sealed partial class WristPanel
{
    // ================= the texts =================

    sealed class FrRow
    {
        public string Key = "", Name = "", Line = "", World = "", WorldDot = "", Type = "", Since95 = "";
        public FriendsWatcher.Where Kind;
        public DateTime Since;
        public bool Star, Here;
    }

    readonly List<FriendsWatcher.Friend> _frMerged = new();
    readonly List<FrRow> _frRows = new(), _frPool = new();
    FriendsWatcher.Snapshot _frOf;
    RoomWatcher.Snapshot _frRoomOf;
    bool _frMade, _frH24;
    int _frStars = -1, _frLang = -1, _frHere;
    CultureInfo _frCulture;
    // the count; the 1x1's note; what to say instead of a list (null: the list) and its hint; the status line; "12 online"; the
    // first names; Cyber's note
    string _frCount = "–", _frMsg, _frHint, _frStatus = "", _frOnline = "", _frNames = "", _frCy = "", _frHereText, _frStatus95 = "", _frCyHead = "";
    readonly string[] _frNote = new string[2];
    readonly Dictionary<(string, float, float, FontWeight, string), string> _frFits = new();
    float _frScroll, _frMax;
    const float FrRowH = 58;
    const string FriendsList95 = "wf95list";
    static readonly string[] CapFriends = { "Friends online", "Friends" }, CapFriendsS = { "Friends" };

    // the opened lists from their top (OpenDetail)
    void SocialFromTop()
    {
        _frScroll = 0;
        _rcScroll = 0;
        _ui.ListScrollTo95(FriendsList95, 0);
        _ui.ListScrollTo95(RecapList95, 0);
        _ui.ListScrollTo95(TodayList95, 0);
        _frTop = 0;
        _rcTop = _tdTop = 0;
        _tdSel = -1;
    }

    // a text cut to w, kept until the list changes (Ellipsize makes a new string each time it cuts)
    string FrCut(string s, float w, float size, FontWeight wt, string family = null)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, size, wt, family);
        if (_frFits.TryGetValue(key, out var done)) return done;
        if (_frFits.Count > 512) _frFits.Clear();
        return _frFits[key] = Ellipsize(s, w, size, wt, family);
    }

    // the same in the 95 font (the retro styles)
    string FrFit95(string s, float w, bool bold = false, float size = Ui.F95)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, size, bold ? FontWeight.Bold : FontWeight.Normal, "95");
        if (_frFits.TryGetValue(key, out var done)) return done;
        if (_frFits.Count > 512) _frFits.Clear();
        return _frFits[key] = Fit(s, w, size, bold);
    }

    static string FrType(FriendsWatcher.Where k) => k switch
    {
        FriendsWatcher.Where.Here => "with you",
        FriendsWatcher.Where.Public => "Public",
        FriendsWatcher.Where.FriendsPlus => "Friends+",
        FriendsWatcher.Where.Friends => "Friends",
        FriendsWatcher.Where.InvitePlus => "Invite+",
        FriendsWatcher.Where.Invite => "Invite",
        FriendsWatcher.Where.Group => "Group",
        FriendsWatcher.Where.GroupPlus => "Group+",
        FriendsWatcher.Where.GroupPublic => "Group Public",
        _ => "",
    };

    // where, said without a world: a private instance, traveling, not known
    static string FrNoWorld(FriendsWatcher.Where k) => k switch
    {
        FriendsWatcher.Where.Private => "Private world",
        FriendsWatcher.Where.Traveling => "Traveling",
        _ => "Somewhere in VRChat",
    };

    // the list and its words, made again only when the friends, the room, the stars, the language or the clock style changed
    void FrNow(PanelData d)
    {
        var s = d.Friends;
        var starred = _cfg.Data.Room.Starred;
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        if (_frMade && ReferenceEquals(s, _frOf) && ReferenceEquals(d.Room, _frRoomOf) && starred.Count == _frStars && _frLang == Lang.Version &&
            ReferenceEquals(culture, _frCulture) && h24 == _frH24) return;
        (_frMade, _frOf, _frRoomOf, _frStars, _frLang, _frCulture, _frH24) = (true, s, d.Room, starred.Count, Lang.Version, culture, h24);
        _frFits.Clear();
        FriendsWatcher.Merge(_frMerged, s, d.Room, starred);
        foreach (var r in _frRows) _frPool.Add(r);
        _frRows.Clear();
        _frHere = 0;
        foreach (var f in _frMerged)
        {
            var r = _frPool.Count > 0 ? _frPool[^1] : new FrRow();
            if (_frPool.Count > 0) _frPool.RemoveAt(_frPool.Count - 1);
            bool here = f.Kind == FriendsWatcher.Where.Here;
            if (here) _frHere++;
            (r.Key, r.Name, r.Kind, r.Since, r.Star, r.Here) = (f.Id.Length > 0 ? f.Id : f.Name, f.Name, f.Kind, f.Since, f.Star, here);
            r.Type = Lang.C("vrchat", FrType(f.Kind));              // ("vrchat|Public": a world or a friend called Public stays so)
            r.World = f.World.Length > 0 ? f.World : Lang.T(FrNoWorld(f.Kind));
            r.Line = f.World.Length > 0 && r.Type.Length > 0 ? r.World + " · " + r.Type : r.World;
            r.WorldDot = " · " + r.World;
            r.Since95 = f.Since == default ? "" : ShortClock(f.Since);
            _frRows.Add(r);
        }
        int n = _frRows.Count;
        var st = s?.State ?? FriendsWatcher.State.Off;
        bool ok = st == FriendsWatcher.State.Ok;
        (_frMsg, _frHint, _frNote[0], _frNote[1]) = st switch
        {
            FriendsWatcher.State.NoVrcx => ("VRCX not found", "Install VRCX to see your friends online", "no VRCX", "no VRCX"),
            FriendsWatcher.State.Closed => ("VRCX is not running", "Start VRCX to see who is online", "VRCX off", "off"),
            FriendsWatcher.State.Unreadable => ("VRCX not readable", "Trying again in a moment", "VRCX?", "?"),
            FriendsWatcher.State.Off => ("Looking for VRCX", "", "looking", "…"),
            _ => ("Nobody online", "", "online", "online"),
        };
        if (n > 0) { _frMsg = null; (_frNote[0], _frNote[1]) = ("online", "online"); }
        _frCount = ok || n > 0 ? n.ToString(culture) : "–";
        _frOnline = n == 1 ? Lang.T("1 online") : Lang.F("{0} online", n);
        _frHereText = _frHere == 0 ? null : _frHere == 1 ? Lang.T("1 with you") : Lang.F("{0} with you", _frHere);
        _frStatus = ok ? Lang.F("VRCX since {0}", ShortClock(s.VrcxSince)) : Lang.T(st switch
        {
            FriendsWatcher.State.NoVrcx => "VRCX not found",
            FriendsWatcher.State.Closed => "VRCX is not running",
            FriendsWatcher.State.Unreadable => "VRCX not readable",
            _ => "Looking for VRCX",
        });
        // the first names: two, then how many more
        _frNames = n == 0 ? "" : n == 1 ? _frRows[0].Name : n == 2 ? _frRows[0].Name + ", " + _frRows[1].Name
            : Lang.F("{0}, {1} + {2}", _frRows[0].Name, _frRows[1].Name, n - 2);
        _frCy = n > 0 || ok ? Upper(_frOnline) : Upper(_frNote[0]);
        _frStatus95 = n > 0 ? _frOnline + "  ·  " + _frStatus : _frStatus;
        _frCyHead = n > 0 ? _frCy + "  //  " + Upper(_frStatus) : Upper(_frStatus);
    }

    // ================= Modern =================

    // the widget: a 1x1, a one-row card, a tall one (the 4 x 3 page's bigger: _big)
    void FriendsOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        FrNow(d);
        int n = _frRows.Count;
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        uint vc = n == 0 ? Bento.Faint : Bento.Text;
        if (w.W == 1 && w.H == 1)
        {
            // the count, a green dot after it (online), what it is under it
            Caption(r, iw, CapFriendsS);
            float vw = ValueUnit(x, r.Y + SBase, iw - 16, _frCount, null, Hero, 20, vc);
            if (n > 0) FrDot(x + vw + 10, r.Y + SBase - Hero * 0.36f, _big ? 6 : 5);
            Note(x, r.Y + NoteBase, iw, _frNote);
            return;
        }
        if (w.H == 1)
        {
            // the count big, then the friends as initials (the starred and those with you first) - and their names where there is room
            CaptionRow(r, n == 0 && _frMsg != null && _frMsg != "Nobody online" ? Lang.T(_frNote[0]) : null, CapFriends);
            float by = r.Y + MBase;
            float vw = ValueUnit(x, by, 90, _frCount, null, _big ? 48 : 40, 24, vc);
            float dx = x + vw + 10;
            if (n > 0) FrDot(dx, by - (_big ? 17 : 14), _big ? 6 : 5);
            float lx = dx + 14, room = r.Right - pad - lx;
            if (n == 0)
            {
                string why = _frMsg ?? "Nobody online";
                TextAt(FrCut(why, room, SmallText, FontWeight.SemiBold), lx, by, room + 2, Sub, SmallText, FontWeight.SemiBold);
                return;
            }
            float rad = _big ? 15 : 13, pitch = _big ? 23 : 19, cy = by - rad + 3;
            bool names = _big && room >= 190;
            int fit = Math.Max(1, (int)((room - (names ? 110 : 30) - 2 * rad) / pitch) + 1), shown = Math.Min(n, Math.Min(fit, 6));
            for (int i = 0; i < shown; i++) Avatar(lx + rad + i * pitch, cy, rad, _frRows[i].Name, _frRows[i].Here);
            float tx = lx + 2 * rad + (shown - 1) * pitch + 10, tr = r.Right - pad - tx;
            string more = names ? _frNames : n > shown ? MoreText(n - shown) : null;
            if (more != null && tr > 24)
                TextAt(FrCut(more, tr, SmallText, FontWeight.SemiBold), tx, cy + 6, tr + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        // a tall one: the count as the note, a row each - the initial, the name and where (and how long ago, where it fits)
        CaptionRow(r, n > 0 ? _frOnline : null, CapFriendsS);
        float top = r.Y + 40, bottom = r.Bottom - 8, rh = _big ? 42 : 36;
        if (n == 0) { FrEmpty(new RectangleF(x, top, iw, bottom - top), 19); return; }
        int rows = Math.Max(1, (int)((bottom - top) / rh));
        bool cut = n > rows;
        int list = cut ? rows - 1 : n;
        var now = DateTime.Now;
        float ar = _big ? 14 : 12;
        bool ages = iw >= 240;
        for (int i = 0; i < list; i++)
        {
            var e = _frRows[i];
            float cy = top + rh * i + rh / 2, by = cy + 6;
            Avatar(x + ar, cy, ar, e.Name, e.Here);
            float tx = x + 2 * ar + 9, right = r.Right - pad;
            if (ages)
            {
                string age = Age(e.Since, now);
                float aw = _p.MeasureWidth(age, SmallText, FontWeight.SemiBold);
                TextAt(age, right - aw, by, aw + 2, Sub, SmallText, FontWeight.SemiBold);
                right -= aw + 10;
            }
            FrNameWorld(e, tx, by, right - tx, SmallText);
        }
        if (cut) TextAt(FrCut(MoreText(n - list), iw, SmallText, FontWeight.SemiBold), x, top + rh * list + rh / 2 + 6, iw + 2, Sub, SmallText, FontWeight.SemiBold);
    }

    // a friend's name (bold; a star before a starred one) and " · world" after it in grey, both cut to w
    void FrNameWorld(FrRow e, float x, float by, float w, float size)
    {
        if (w < 20) return;
        if (e.Star)
        {
            TextAt("★", x, by, 20, Bento.Text, size - 2, FontWeight.SemiBold);      // (the card's text colour: amber vanishes on an amber card)
            float sw = _p.MeasureWidth("★", size - 2, FontWeight.SemiBold) + 4;
            x += sw;
            w -= sw;
        }
        float nw = _p.MeasureWidth(e.Name, size, FontWeight.SemiBold);
        float keep = MathF.Min(nw, MathF.Max(w * 0.55f, w - _p.MeasureWidth(" · Abc", size, FontWeight.SemiBold)));
        string name = FrCut(e.Name, keep + 0.5f, size, FontWeight.SemiBold);
        TextAt(name, x, by, keep + 2, Bento.Text, size, FontWeight.SemiBold);
        float ux = x + _p.MeasureWidth(name, size, FontWeight.SemiBold), room = x + w - ux;
        if (room < 34) return;
        TextAt(FrCut(e.WorldDot, room, size, FontWeight.SemiBold), ux, by, room + 2, Sub, size, FontWeight.SemiBold);
    }

    // online: a green dot (a ring of the card round it)
    void FrDot(float cx, float cy, float rad)
    {
        HaloDisc(cx, cy, rad + 2);
        _p.Circle(cx, cy, rad, Bento.Ok);
    }

    // "+3 more", made once per number
    readonly Dictionary<int, string> _moreText = new();
    int _moreLang = -1;
    string MoreText(int n)
    {
        if (_moreLang != Lang.Version) { _moreText.Clear(); _moreLang = Lang.Version; }
        if (!_moreText.TryGetValue(n, out var s)) _moreText[n] = s = Lang.F("+{0} more", n);
        return s;
    }

    // "+3 more · the stick scrolls" (the opened lists' bottom line), made once per number
    readonly Dictionary<int, string> _scrollMore = new();
    int _scrollMoreLang = -1;
    string ScrollMore(int n)
    {
        if (_scrollMoreLang != Lang.Version) { _scrollMore.Clear(); _scrollMoreLang = Lang.Version; }
        if (!_scrollMore.TryGetValue(n, out var s)) _scrollMore[n] = s = Lang.F("+{0} more · the stick scrolls", n);
        return s;
    }

    // nobody to list: why (VRCX missing / closed, nobody online) and what would help, in the middle of r
    void FrEmpty(RectangleF r, float size)
    {
        string why = _frMsg ?? "Nobody online";
        bool hint = !string.IsNullOrEmpty(_frHint) && r.Height >= 70;
        float my = r.Y + (r.Height - (hint ? size + 34 : size + 6)) / 2;
        _p.Text(FrCut(why, r.Width, size, FontWeight.SemiBold), new RectangleF(r.X, my, r.Width, size + 10), Bento.Text, size, FontWeight.SemiBold,
            TextAlignment.Center);
        if (!hint) return;
        _p.Text(FrCut(_frHint, r.Width, SmallText, FontWeight.SemiBold), new RectangleF(r.X, my + size + 10, r.Width, 26), Sub, SmallText, FontWeight.SemiBold,
            TextAlignment.Center);
    }

    // opened: the count after the name, how many are with you and where VRCX's list is from under it, everyone in two columns - the
    // initial (ringed green: with you), the name (a star: starred), where and since when; the stick scrolls it
    void FriendsDetail(RectangleF E, PanelData d)
    {
        if (Theme.Cyber) { CyberFriendsDetail(E, d); return; }
        FrNow(d);
        int n = _frRows.Count;
        var now = DateTime.Now;
        float nx = E.X + 24 + _p.MeasureWidth(Lang.T(WatchWidgets.Find("friends").Name), 24, FontWeight.SemiBold) + 12;
        if (n > 0) TextAt(_frCount, nx, E.Y + 45, 60, Sub, 24, FontWeight.SemiBold);
        // under the title: how many with you (green), where the list is from
        float sx = E.X + 24, sy = E.Y + 82, right = E.Right - 24;
        if (_frHereText != null)
        {
            float hw = _p.MeasureWidth(_frHereText, 19, FontWeight.SemiBold);
            TextAt(_frHereText, sx, sy, hw + 2, Bento.Ok, 19, FontWeight.SemiBold);
            sx += hw + 9;
            TextAt("·", sx, sy, 12, Sub, 19, FontWeight.Normal);
            sx += _p.MeasureWidth("·", 19, FontWeight.Normal) + 9;
        }
        TextAt(FrCut(_frStatus, right - sx, 19, FontWeight.Normal), sx, sy, right - sx + 2, Sub, 19, FontWeight.Normal);
        var L = new RectangleF(E.X + 24, E.Y + 100, E.Width - 48, E.Bottom - 40 - (E.Y + 100));
        if (n == 0) { _frMax = 0; FrEmpty(L, 22); return; }
        int rows = (n + 1) / 2;
        float content = rows * FrRowH, max = MathF.Max(0, content - L.Height);
        _frMax = max;
        if (_ui.Wheel != 0 && !_ui.Inert) { _frScroll -= _ui.Wheel * FrRowH; _dirty = true; }
        _frScroll = Math.Clamp(_frScroll, 0, max);
        float rw = max > 0 ? L.Width - 14 : L.Width, gap = 18, cw = (rw - gap) / 2, top = L.Y - _frScroll;
        _p.PushClip(L);
        int below = 0;
        for (int i = 0; i < n; i++)
        {
            float ry = top + i / 2 * FrRowH;
            if (ry + FrRowH > L.Bottom + 0.5f) below++;
            if (ry + FrRowH < L.Y || ry > L.Bottom) continue;
            FrPerson(_frRows[i], new RectangleF(L.X + i % 2 * (cw + gap), ry, cw, FrRowH), now);
        }
        _p.PopClip();
        if (max > 0)
        {
            float bh = MathF.Max(24, L.Height * L.Height / content), by = L.Y + (L.Height - bh) * (_frScroll / max);
            _p.Fill(new RectangleF(L.Right - 4, by, 4, bh), Theme.WithAlpha(Bento.Text, 0.3f), 2);
        }
        if (below > 0)
        {
            string more = ScrollMore(below);
            TextAt(FrCut(more, L.Width, SmallText, FontWeight.SemiBold), L.Right - L.Width, E.Bottom - 22, L.Width + 2, Sub, SmallText, FontWeight.SemiBold,
                TextAlignment.Trailing);
        }
    }

    // a friend in the opened list: the initial, the name (★ before a starred one), how long ago at the right; where under them
    void FrPerson(FrRow e, RectangleF r, DateTime now)
    {
        const float rad = 19;
        float cx = r.X + rad + 2, cy = r.Y + r.Height / 2;
        Avatar(cx, cy, rad, e.Name, e.Here);
        float x = cx + rad + 12, right = r.Right;
        string age = Age(e.Since, now);
        float aw = _p.MeasureWidth(age, SmallText, FontWeight.SemiBold);
        TextAt(age, right - aw, r.Y + 24, aw + 2, Sub, SmallText, FontWeight.SemiBold);
        float nx = x, nw = right - aw - 10 - x;
        if (e.Star)
        {
            TextAt("★", nx, r.Y + 24, 24, Bento.WarnInk, SmallText, FontWeight.Normal);
            float sw = _p.MeasureWidth("★", SmallText, FontWeight.Normal) + 6;
            nx += sw;
            nw -= sw;
        }
        if (nw > 20) TextAt(FrCut(e.Name, nw, 19, FontWeight.SemiBold), nx, r.Y + 24, nw + 2, Bento.Text, 19, FontWeight.SemiBold);
        float w = right - x;
        if (e.Here) TextAt(FrCut(e.Line, w, SmallText, FontWeight.SemiBold), x, r.Y + 46, w + 2, Bento.Ok, SmallText, FontWeight.SemiBold);
        else TextAt(FrCut(e.Line, w, SmallText, FontWeight.Normal), x, r.Y + 46, w + 2, Sub, SmallText, FontWeight.Normal);
    }

    // ================= Cyber =================
    // FRIENDS on its tag, "12 ONLINE" the cyan note; the count in the condensed bold with a cyan block (live) after it, a segment per
    // friend (the ones with you cyan), their names in cyan; a tall card a row each - the name, where in monospace, how long ago.

    void CyberFriends(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        FrNow(d);
        int n = _frRows.Count;
        float pad = PadX(r), x = r.X + pad, right = r.Right - pad, iw = right - x;
        bool small = w.W == 1 && w.H == 1;
        CyberCaptionRow(r, x, right, small ? null : _frCy, n > 0 ? CyC : CyG, small ? CapFriendsS : CapFriends);
        float bottom = r.Bottom - 13, top = r.Y + 34;
        if (n == 0 && (small || w.H == 1))
        {
            CyValue(x, bottom, _frCount, MathF.Floor(MathF.Min(bottom - top, _big ? 56 : 42) / CyCap), CyberHud.Faint);
            string why = Upper(small ? _frNote[1] : _frMsg ?? "Nobody online");
            float wx = x + CyValueW(_frCount, MathF.Floor(MathF.Min(bottom - top, _big ? 56 : 42) / CyCap)) + 10;
            if (right - wx > 24) CyMono(FrCut(why, right - wx, CyNote, FontWeight.SemiBold, CyberHud.Mono), wx, bottom, right - wx, CyG);
            return;
        }
        if (small || w.H == 1)
        {
            float vs = MathF.Floor(MathF.Min(bottom - top, _big ? 56 : 42) / CyCap);
            float vw = CyValue(x, bottom, _frCount, vs, CyW, Ui.Id("wfrc", id));
            // live: a cyan block at the top right of the count
            float bs = _big ? 8 : 7;
            _p.Fill(new RectangleF(x + vw + 5, bottom - vs * CyCap, bs, bs), CyC);
            if (small)
            {
                string note = Upper(_frNote[1]);
                float nx = x + vw + bs + 10;
                if (CyMonoW(note) <= right - nx) CyMono(note, nx, bottom, right - nx, CyG);
                return;
            }
            float cx = x + vw + bs + 16, cw = right - cx;
            if (cw < 30) return;
            // a segment per friend (as many as fit), those with you cyan; their names over it
            float segH = 6, segY = bottom - segH;
            int seg = Math.Min(n, Math.Max(1, (int)(cw / 13)));
            float bw = MathF.Min(cw, seg * 13);
            CyberHud.Segments(_p, new RectangleF(cx, segY, bw, segH), 1, CyOther, 0, seg);
            if (_frHere > 0) CyberHud.Segments(_p, new RectangleF(cx, segY, bw, segH), Math.Min(1, _frHere / (float)seg), CyC, 0, seg);
            float ns = _big ? 22 : 18;
            TextAt(FrCut(_frNames, cw, ns, FontWeight.SemiBold, Theme.Display), cx, segY - 9, cw + 2, CyC, ns, FontWeight.SemiBold, TextAlignment.Leading,
                Theme.Display);
            return;
        }
        // tall: a row each
        float rt = r.Y + 36, rb = r.Bottom - 8, rh = _big ? 40 : 34;
        if (n == 0)
        {
            CyMono(FrCut(Upper(_frMsg ?? "Nobody online"), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Y + MathF.Min(70, r.Height * 0.45f), iw + 2, CyG);
            if (!string.IsNullOrEmpty(_frHint))
                CyMono(FrCut(Upper(_frHint), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Y + MathF.Min(70, r.Height * 0.45f) + 20, iw + 2, CyberHud.Faint);
            return;
        }
        int rows = Math.Max(1, (int)((rb - rt) / rh));
        bool cut = n > rows;
        int list = cut ? rows - 1 : n;
        var now = DateTime.Now;
        for (int i = 0; i < list; i++)
        {
            var e = _frRows[i];
            float cy = rt + rh * i + rh / 2, by = cy + 6;
            _p.Fill(new RectangleF(x, cy - 7, 3, 14), e.Here ? CyC : e.Star ? CyY : CyOther);
            float tx = x + 10, rr = right;
            if (iw >= 240)
            {
                string age = Age(e.Since, now);
                float aw = CyMonoW(age);
                CyMono(age, right, by, aw + 2, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
                rr -= aw + 10;
            }
            float nw = MathF.Min(_p.MeasureWidth(e.Name, 18, FontWeight.SemiBold, Theme.Display), (rr - tx) * 0.55f);
            TextAt(FrCut(e.Name, nw + 0.5f, 18, FontWeight.SemiBold, Theme.Display), tx, by, nw + 2, CyW, 18, FontWeight.SemiBold, TextAlignment.Leading, Theme.Display);
            float wx = tx + nw + 8;
            if (rr - wx > 30) CyMono(FrCut(Upper(e.World), rr - wx, CyNote, FontWeight.SemiBold, CyberHud.Mono), wx, by, rr - wx, e.Here ? CyC : CyG);
        }
        if (cut) CyMono(FrCut(Upper(MoreText(n - list)), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, rt + rh * list + rh / 2 + 6, iw + 2, CyG);
    }

    // opened: beside the tag how many and where from; plates in two columns - the name in the condensed capitals, where in monospace,
    // its kind in a red chip, how long ago in cyan; the ones with you outlined cyan, a starred one with a yellow mark
    void CyberFriendsDetail(RectangleF E, PanelData d)
    {
        FrNow(d);
        CyFrame();
        int n = _frRows.Count;
        var now = DateTime.Now;
        float nx = E.X + 24 + CyberHud.TagWidth(_p, Upper(WatchWidgets.Find("friends").Name), 18) + 14, nr = E.Right - 22 - 46 - 14;
        if (nr - nx > 40) CyMono(FrCut(_frCyHead, nr - nx, CyNote, FontWeight.SemiBold, CyberHud.Mono), nx, E.Y + 23 + (26 + CyberHud.CapH(_p, CyNote)) / 2, nr - nx,
            n > 0 ? CyC : CyG);
        var L = new RectangleF(E.X + 24, E.Y + 74, E.Width - 48, E.Height - 74 - 22);
        if (n == 0)
        {
            _frMax = 0;
            CyMono(FrCut(Upper(_frMsg ?? "Nobody online"), L.Width, 17, FontWeight.SemiBold, CyberHud.Mono), L.X, L.Y + L.Height / 2, L.Width + 2, CyG, 17);
            if (!string.IsNullOrEmpty(_frHint))
                CyMono(FrCut(Upper(_frHint), L.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), L.X, L.Y + L.Height / 2 + 24, L.Width + 2, CyberHud.Faint);
            return;
        }
        const float ph = 60, pg = 10;
        int rows = (n + 1) / 2;
        float content = rows * (ph + pg) - pg, max = MathF.Max(0, content - L.Height);
        _frMax = max;
        if (_ui.Wheel != 0 && !_ui.Inert) { _frScroll -= _ui.Wheel * (ph + pg); _dirty = true; }
        _frScroll = Math.Clamp(_frScroll, 0, max);
        float rw = max > 0 ? L.Width - 14 : L.Width, gap = 12, cw = (rw - gap) / 2;
        _p.PushClip(L);
        for (int i = 0; i < n; i++)
        {
            float y = L.Y - _frScroll + i / 2 * (ph + pg);
            if (y + ph < L.Y || y > L.Bottom) continue;
            var e = _frRows[i];
            var p = new RectangleF(L.X + i % 2 * (cw + gap), y, cw, ph);
            CyberHud.CardShape(_p, p, CyberHud.Card2, e.Here ? CyberHud.CyanDim : CyberHud.Edge);
            if (e.Star) _p.Fill(new RectangleF(p.X + 1, p.Y + 10, 3, p.Height - 20), CyY);
            float x = p.X + 14, right = p.Right - 12;
            string age = Age(e.Since, now);
            float aw = CyMonoW(age);
            CyMono(age, right, p.Y + 24, aw + 2, CyC, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
            float nw = right - aw - 10 - x;
            if (nw > 20) TextAt(FrCut(Upper(e.Name), nw, 19, FontWeight.SemiBold, Theme.Display), x, p.Y + 26, nw + 2, CyW, 19, FontWeight.SemiBold,
                TextAlignment.Leading, Theme.Display);
            float cx = x;
            if (e.Type.Length > 0 && !e.Here)
            {
                string chip = Upper(e.Type);
                float chw = CyMonoW(chip) + 10;
                if (chw < (right - x) * 0.5f)
                {
                    var cr = new RectangleF(x, p.Y + 34, chw, 19);
                    _p.Stroke(RectangleF.Inflate(cr, -0.6f, -0.6f), CyR, 1.25f);
                    CyMono(chip, x + 5, cr.Y + 14, chw, CyR);
                    cx = cr.Right + 8;
                }
            }
            if (right - cx > 24) CyMono(FrCut(Upper(e.World), right - cx, CyNote, FontWeight.SemiBold, CyberHud.Mono), cx, p.Y + 48, right - cx, e.Here ? CyC : CyG);
        }
        _p.PopClip();
        if (max > 0)
        {
            float bh = MathF.Max(24, L.Height * L.Height / content), by = L.Y + (L.Height - bh) * (_frScroll / max);
            _p.Fill(new RectangleF(L.Right - 4, by, 4, bh), CyOther);
        }
    }

    // ================= the retro styles (the shared 95 windows: 95, XP, 7, the Mac) =================

    // c: the window's inside (the style's frame is drawn). A 1x1: the count bold, "online" under it, a green light; one row: "12 friends
    // online" bold, the first names under it; taller: a row each, the name and where (grey), how long ago at the right
    void Friends95(RectangleF c, WatchWidget w, PanelData d)
    {
        FrNow(d);
        int n = _frRows.Count;
        if (c.Width < 70)
        {
            Value95(c, _frCount, _frNote[1], Blk95);
            if (n > 0)
            {
                float vy = MathF.Floor(c.Y + (c.Height - 28) / 2), vw = _ui.Width95(_frCount, true, 13);
                FrLight95(MathF.Floor(c.X + (c.Width + vw) / 2) + 3, vy + 4);
            }
            return;
        }
        if (c.Height < 60)
        {
            float y = MathF.Floor(c.Y + c.Height / 2 - 14);
            if (n > 0) FrLight95(c.X + 4, y + 3);
            float tx = c.X + (n > 0 ? 14 : 4), tw = c.Right - 4 - tx;
            _ui.Text95(FrFit95(n > 0 ? _frOnline : _frMsg ?? "Nobody online", tw, true), new RectangleF(tx, y, tw, 13), Blk95, true);
            _ui.Text95(FrFit95(n > 0 ? _frNames : _frHint ?? "", c.Width - 8), new RectangleF(c.X + 4, y + 15, c.Width - 8, 13), n > 0 ? 0xFF008000 : Gry95);
            return;
        }
        var inner = RectangleF.Inflate(c, -3, -2);
        if (n == 0)
        {
            float my = MathF.Floor(inner.Y + (inner.Height - 28) / 2);
            _ui.Text95(FrFit95(_frMsg ?? "Nobody online", inner.Width, true), new RectangleF(inner.X, my, inner.Width, 13), Blk95, true, TextAlignment.Center);
            if (!string.IsNullOrEmpty(_frHint)) _ui.Text95(FrFit95(_frHint, inner.Width), new RectangleF(inner.X, my + 15, inner.Width, 13), Gry95, false, TextAlignment.Center);
            return;
        }
        int rows = Math.Max(1, (int)(inner.Height / 14));
        bool cut = n > rows;
        int list = cut ? rows - 1 : n;
        var now = DateTime.Now;
        for (int i = 0; i < list; i++)
        {
            var e = _frRows[i];
            float y = inner.Y + i * 14;
            string age = Age(e.Since, now);
            float aw = _ui.Width95(age) + 4;
            float nw = MathF.Min(_ui.Width95(e.Name, e.Here), (inner.Width - aw) * 0.55f);
            string name = FrFit95(e.Name, nw + 0.5f, e.Here);
            _ui.Text95(name, new RectangleF(inner.X, y, nw + 2, 13), e.Here ? 0xFF008000 : Blk95, e.Here);
            float wx = inner.X + _ui.Width95(name, e.Here) + 6, ww = inner.Right - aw - wx;
            if (ww > 16) _ui.Text95(FrFit95(e.World, ww), new RectangleF(wx, y, ww, 13), Gry95);
            _ui.Text95(age, new RectangleF(inner.Right - aw, y, aw, 13), Gry95, false, TextAlignment.Trailing);
        }
        if (cut) _ui.Text95(FrFit95(MoreText(n - list), inner.Width), new RectangleF(inner.X, inner.Y + list * 14, inner.Width, 13), Gry95);
    }

    // a little green light (online): 95's 16-colour green with a dark edge
    void FrLight95(float x, float y)
    {
        _p.Fill(new RectangleF(x, y, 6, 6), 0xFF008000);
        _p.Fill(new RectangleF(x + 1, y + 1, 4, 4), 0xFF00C000);
        _p.Fill(new RectangleF(x + 1, y + 1, 2, 2), 0xFF80FF80);
    }

    // Opened, in the window the style draws (title, X, OK): c is its inside, the OK row under it. A details list under its column
    // headers - a star (starred / favourite), the name (bold and green: with you), where, since when - and the status beside OK.
    int _frTop;                                  // (the Terminal's who: the first line shown)
    FrRow[] _frDraw95 = Array.Empty<FrRow>();
    Action<int, RectangleF, bool> _frRow95;
    float _frCol95, _frWhenW95, _frWhenX95;

    void Friends95Detail(RectangleF c, PanelData d)
    {
        FrNow(d);
        int n = _frRows.Count;
        float sw = c.Right - 75 - 8 - c.X;
        _ui.Text95(FrFit95(_frStatus95, sw), new RectangleF(c.X, c.Bottom + 10, sw, 13), Blk95);
        if (n == 0)
        {
            W95Icons.Draw(_p, W95Icons.Id.Info32, c.X + 12, c.Y + 24);
            _ui.Text95(FrFit95(_frMsg ?? "Nobody online", c.Width - 64, true), new RectangleF(c.X + 56, c.Y + 26, c.Width - 60, 15), Blk95, true);
            if (!string.IsNullOrEmpty(_frHint)) _ui.Text95(FrFit95(_frHint, c.Width - 64), new RectangleF(c.X + 56, c.Y + 44, c.Width - 60, 13), Blk95);
            return;
        }
        const float WhenW = 62;
        float nameW = MathF.Floor((c.Width - 20 - WhenW) * 0.42f);
        var head = new RectangleF(c.X, c.Y, c.Width, 16);
        Head95(new RectangleF(head.X, head.Y, 20, 16), "");
        Head95(new RectangleF(head.X + 20, head.Y, nameW, 16), "Name");
        Head95(new RectangleF(head.X + 20 + nameW, head.Y, head.Width - 20 - nameW - WhenW, 16), Lang.C("vrchat", "World"));
        Head95(new RectangleF(head.Right - WhenW, head.Y, WhenW, 16), "Since");
        var box = new RectangleF(c.X, head.Bottom, c.Width, c.Bottom - head.Bottom);
        if (_frDraw95.Length < n) _frDraw95 = new FrRow[Math.Max(n, 16)];
        _frRows.CopyTo(_frDraw95);
        (_frCol95, _frWhenW95, _frWhenX95) = (nameW, WhenW, head.Right - WhenW);
        _frRow95 ??= FrRow95;
        _ui.List95(FriendsList95, box, n, -1, _frRow95);
    }

    // a row of the opened list (a method of its own, kept as one delegate: a lambda here was made every frame)
    void FrRow95(int i, RectangleF r, bool on)
    {
        var e = _frDraw95[i];
        _ui.Text95(e.Star ? "★" : "☆", new RectangleF(r.X + 2, r.Y, 16, r.Height), e.Star ? 0xFF808000 : Gry95);
        float nw = _frCol95 - 4;
        _ui.Text95(FrFit95(e.Name, nw, e.Here), new RectangleF(r.X + 20, r.Y, nw, r.Height), e.Here ? 0xFF008000 : Blk95, e.Here);
        // the last column is under the scroll bar's header part when there is one: the time keeps what is left of it
        float wx = r.X + 20 + _frCol95, tx = _frWhenX95 + 2, tw = MathF.Min(_frWhenW95 - 2, r.Right - tx);
        _ui.Text95(FrFit95(e.Line, tx - wx - 6), new RectangleF(wx, r.Y, tx - wx - 4, r.Height), Blk95);
        if (tw > 8) _ui.Text95(FrFit95(e.Since95, tw), new RectangleF(tx, r.Y, tw, r.Height), Blk95);
    }

    // ---- --snapshot's hooks ----

    public float FriendsScroll => _frScroll;
    public float FriendsScrollMax => _frMax;
    public int FriendsShown(PanelData d) { FrNow(d); return _frRows.Count; }
    public string FriendsOrder(PanelData d)
    {
        FrNow(d);
        var sb = new System.Text.StringBuilder();
        foreach (var r in _frRows) sb.Append(sb.Length > 0 ? ", " : "").Append(r.Name);
        return sb.ToString();
    }
    public string FriendsLine(PanelData d, int i) { FrNow(d); return i < _frRows.Count ? _frRows[i].Line : null; }
}
