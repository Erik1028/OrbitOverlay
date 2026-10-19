using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// The room widget opened: who is in your VRChat instance, on the watch. A tap on the room card grows it into this view (the
// Minesweeper way: the card itself gets bigger, WristDetail), its X or a tap beside it shrinks it back. The world and how long
// you have been there, everyone here as an initial with the name (starred, then friends, then the rest, newest first: the Room
// panel's order) and who left lately; the stick scrolls the list. The data is the Room panel's (RoomWatcher's snapshot in
// PanelData.Room). The floating Room panel stays, opened from Settings (where the stars are set). Modern: Bento's tokens (a
// room widget with a colour of its own keeps it here); Windows 95: a details list with a status bar, like the Room panel's.
public sealed partial class WristPanel
{
    float _roomScroll, _roomMax;                 // the opened list: px scrolled from its top, how far it goes
    RectangleF _roomClose95;                     // the 95 window's X (canvas px; --snapshot taps it)
    readonly List<RoomWatcher.Person> _roomHere = new();
    RoomWatcher.Snapshot _roomHereOf;
    int _roomStars = -1;
    const float RoomRowH = 52, RoomHeadH = 40;

    // the people here in the Room panel's order, sorted again only when the room (or the stars) changed
    List<RoomWatcher.Person> RoomHere(RoomWatcher.Snapshot s)
    {
        var starred = _cfg.Data.Room.Starred;
        if (ReferenceEquals(s, _roomHereOf) && starred.Count == _roomStars) return _roomHere;
        _roomHereOf = s;
        _roomStars = starred.Count;
        _roomHere.Clear();
        if (s != null && s.InRoom) _roomHere.AddRange(s.Here);
        RoomPanel.Order(_roomHere, starred);
        return _roomHere;
    }

    // how long you have been in this world (null: not in one, or the log did not say)
    static string RoomStay(RoomWatcher.Snapshot s, DateTime now)
    {
        if (s == null || !s.InRoom || s.Since == default) return null;
        return now - s.Since < TimeSpan.FromMinutes(1) ? Lang.T("just arrived") : Lang.F("here for {0}", RoomPanel.Ago(s.Since, now));
    }

    static string RoomWhen(DateTime t, DateTime now) => RoomPanel.AgoText(t, now);

    static string RoomCountText(int n) => RoomPanel.PeopleText(n);
    static string RoomFriendsText(int f) => RoomPanel.FriendsText(f);

    // ================= Modern =================

    void RoomDetail(RectangleF E, PanelData d)
    {
        var s = d.Room;
        var now = DateTime.Now;
        bool inRoom = s != null && s.InRoom;
        // the world is the title (a little smaller before it is cut), left of the X
        string world = inRoom && s.World.Length > 0 ? s.World : "VRChat room";
        float tw = E.Width - 24 - 86;
        float ts = MathF.Max(19, FitSize(world, tw, 24, FontWeight.SemiBold, null));
        _p.Text(Ellipsize(world, tw, ts, FontWeight.SemiBold), new RectangleF(E.X + 24, E.Y + 16, tw, 40), Bento.Text, ts, FontWeight.SemiBold);
        var here = RoomHere(s);
        int n = here.Count, f = 0, gone = s?.Gone.Count ?? 0;
        foreach (var p in here) if (p.Friend) f++;
        // under it: how many, the friends among them (green), how long you have been there
        float right = E.Right - 24, by = E.Y + 82, x = E.X + 24;
        float Seg(string t, float x0, uint c, FontWeight wt)
        {
            float room = right - x0;
            if (room < 24) return right;
            string e = Ellipsize(t, room, 19, wt);
            TextAt(e, x0, by, room + 2, c, 19, wt);
            return x0 + _p.MeasureWidth(e, 19, wt);
        }
        float Dot(float x0) => Seg("·", x0 + 9, Sub, FontWeight.Normal) + 9;
        if (inRoom)
        {
            x = Seg(RoomCountText(n), x, Bento.Text, FontWeight.SemiBold);
            if (f > 0) x = Seg(RoomFriendsText(f), Dot(x), Bento.Ok, FontWeight.SemiBold);
            if (RoomStay(s, now) is { } stay) Seg(stay, Dot(x), Sub, FontWeight.Normal);
        }
        // the list: two columns of people, then who left lately; the stick scrolls it a row at a time
        var L = new RectangleF(E.X + 24, E.Y + 104, E.Width - 48, 5 * RoomRowH);         // five rows whole
        bool empty = n == 0;
        int hereRows = (n + 1) / 2, goneRows = (gone + 1) / 2;
        float msgH = empty ? (gone > 0 ? 92 : L.Height) : 0;
        float content = msgH + hereRows * RoomRowH + (gone > 0 ? RoomHeadH + goneRows * RoomRowH : 0);
        float max = MathF.Max(0, content - L.Height);
        _roomMax = max;
        if (_ui.Wheel != 0 && !_ui.Inert) { _roomScroll -= _ui.Wheel * RoomRowH; _dirty = true; }
        _roomScroll = Math.Clamp(_roomScroll, 0, max);
        float rw = max > 0 ? L.Width - 14 : L.Width, colGap = 16, cw = (rw - colGap) / 2;
        float top = L.Y - _roomScroll;
        _p.PushClip(L);
        if (empty)
        {
            // why nobody is listed (the Room panel's words), and what would help (VRCX missing: the line along the bottom says it)
            string why = s == null ? "Not following VRChat" : RoomPanel.EmptyText(s);
            string hint = s == null ? "Turn on \"Follow VRChat's log\" in Settings" : null;
            float my = top + (msgH - (hint != null ? 64 : 34)) / 2;
            _p.Text(Ellipsize(why, L.Width, 22, FontWeight.SemiBold), new RectangleF(L.X, my, L.Width, 34), Bento.Text, 22, FontWeight.SemiBold,
                TextAlignment.Center);
            if (hint != null)
                _p.Text(Ellipsize(hint, L.Width, SmallText, FontWeight.Normal), new RectangleF(L.X, my + 36, L.Width, 28), Sub, SmallText, FontWeight.Normal,
                    TextAlignment.Center);
        }
        float y = top + msgH;
        for (int i = 0; i < n; i++)
        {
            float ry = y + i / 2 * RoomRowH;
            if (ry + RoomRowH < L.Y || ry > L.Bottom) continue;
            RoomPerson(here[i], new RectangleF(L.X + i % 2 * (cw + colGap), ry, cw, RoomRowH), now, false);
        }
        y += hereRows * RoomRowH;
        int below = 0;          // people not (wholly) in view under the list
        for (int i = 0; i < n; i++) if (top + msgH + (i / 2 + 1) * RoomRowH > L.Bottom + 0.5f) below++;
        if (gone > 0)
        {
            if (y + RoomHeadH >= L.Y && y <= L.Bottom) CapText("LEFT LATELY", L.X, y + 27, rw, Sub);
            y += RoomHeadH;
            for (int i = 0; i < gone; i++)
            {
                float ry = y + i / 2 * RoomRowH;
                if (ry + RoomRowH > L.Bottom + 0.5f) below++;
                if (ry + RoomRowH < L.Y || ry > L.Bottom) continue;
                RoomPerson(s.Gone[i], new RectangleF(L.X + i % 2 * (cw + colGap), ry, cw, RoomRowH), now, true);
            }
        }
        _p.PopClip();
        if (max > 0)
        {
            // a thin scroll bar on the right (the Room panel's)
            float bh = L.Height * L.Height / content, sy = L.Y + (L.Height - bh) * (_roomScroll / max);
            _p.Fill(new RectangleF(L.Right - 4, sy, 4, bh), Theme.WithAlpha(Bento.Text, 0.3f), 2);
        }
        // along the bottom: VRCX (where the friends come from); how many more are under the list
        float fb = E.Bottom - 22;
        string more = below > 0 ? Lang.F("+{0} more · the stick scrolls", below) : null;
        float mw = more == null ? 0 : MathF.Min(L.Width * 0.55f, _p.MeasureWidth(more, SmallText, FontWeight.SemiBold) + 2);
        if (more != null) TextAt(Ellipsize(more, mw, SmallText, FontWeight.SemiBold), L.Right - mw, fb, mw + 2, Sub, SmallText, FontWeight.SemiBold,
            TextAlignment.Trailing);
        if (s != null && s.LogFound)
        {
            string vrcx = s.VrcxFound ? $"VRCX: {RoomFriendsText(s.Friends)}" : "VRCX not found: friends are not marked";
            float vw = L.Width - mw - (more != null ? 20 : 0);
            TextAt(Ellipsize(vrcx, vw, SmallText, FontWeight.Normal), L.X, fb, vw + 2, Sub, SmallText, FontWeight.Normal);
        }
    }

    // a person: their initial (a friend ringed in green, the room card's way), the name, then starred / friend / when
    void RoomPerson(RoomWatcher.Person p, RectangleF r, DateTime now, bool gone)
    {
        const float rad = 19;
        float cx = r.X + rad + 2, cy = r.Y + r.Height / 2;
        if (gone) _p.PushFade(0.45f);
        Avatar(cx, cy, rad, p.Name, p.Friend && !gone);
        if (gone) _p.PopFade();
        float x = cx + rad + 12, w = r.Right - x;
        TextAt(Ellipsize(p.Name, w, 19, FontWeight.SemiBold), x, r.Y + 23, w + 2, gone ? Sub : Bento.Text, 19, FontWeight.SemiBold);
        float mb = r.Y + 44;
        if (_cfg.Data.Room.Starred.Contains(p.Key))
        {
            TextAt("★", x, mb, 24, Bento.WarnInk, SmallText, FontWeight.Normal);
            x += _p.MeasureWidth("★", SmallText, FontWeight.Normal) + 6;
        }
        if (p.Friend && !gone)
        {
            TextAt("friend", x, mb, r.Right - x, Bento.Ok, SmallText, FontWeight.SemiBold);
            x += _p.MeasureWidth("friend", SmallText, FontWeight.SemiBold) + 7;
            TextAt("·", x, mb, 12, Sub, SmallText, FontWeight.Normal);
            x += _p.MeasureWidth("·", SmallText, FontWeight.Normal) + 7;
        }
        string when = gone ? RoomPanel.LeftText(p.Left, now, 2) : RoomWhen(p.Joined, now);
        if (r.Right - x > 20) TextAt(Ellipsize(when, r.Right - x, SmallText, FontWeight.Normal), x, mb, r.Right - x + 2, Sub, SmallText, FontWeight.Normal);
    }

    // ================= Windows 95 =================

    // the Room window: its caption, a details list (star, name, joined) with its scroll bar, a status bar
    void Room95Detail(RectangleF win, PanelData d)
    {
        var s = d.Room;
        var now = DateTime.Now;
        bool inRoom = s != null && s.InRoom;
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        string title = inRoom && s.World.Length > 0 ? Lang.F("Room - {0}", s.World) : "Room";
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), Fit(title, win.Width - 6 - 21 - 40, Ui.F95, true), W95Icons.Id.People);
        _roomClose95 = new RectangleF(win.Right - 5 - 16, win.Y + 5, 16, 14);
        if (_ui.CaptionClose95("wr95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        var here = RoomHere(s);
        int n = here.Count, gone = s?.Gone.Count ?? 0, count = n + gone, f = 0;
        foreach (var p in here) if (p.Friend) f++;
        var starred = _cfg.Data.Room.Starred;
        float x0 = win.X + 6, w = win.Width - 12, sy = win.Bottom - 4 - 20;
        const float JoinW = 86;
        // the column headers of a 1995 details view
        var head = new RectangleF(x0, win.Y + 26, w, 16);
        Head95(new RectangleF(head.X, head.Y, 20, 16), "");
        Head95(new RectangleF(head.X + 20, head.Y, head.Width - 20 - JoinW, 16), "Name");
        Head95(new RectangleF(head.Right - JoinW, head.Y, JoinW, 16), "Joined");
        var box = new RectangleF(x0, head.Bottom, w, sy - 4 - head.Bottom);
        if (count == 0)
        {
            _p.Fill(box, Wht95);
            _p.Bevel(box, Painter.Edge.Sunken);
            string why = s == null ? Lang.T("Not following VRChat. Turn on \"Follow VRChat's log\" in Settings.") : Lang.T(RoomPanel.EmptyText(s)) + ".";
            if (s != null && s.LogFound && !s.VrcxFound) why += Lang.T(" Install VRCX to see your friends first.");
            _ui.Text95(why, new RectangleF(box.X + 8, box.Y + 6, box.Width - 16, box.Height - 12), Blk95, false, TextAlignment.Leading, true, true);
        }
        else
            _ui.List95("wr95list", box, count, -1, (i, r, on) =>
            {
                bool g = i >= n;
                var p = g ? s.Gone[i - n] : here[i];
                bool star = starred.Contains(p.Key);
                uint fg = g ? Gry95 : Blk95;
                _ui.Text95(star ? "★" : "☆", new RectangleF(r.X + 2, r.Y, 16, r.Height), star ? 0xFF808000 : Gry95);
                bool bold = p.Friend && !g;
                float nw = r.Width - 20 - JoinW - 4;
                _ui.Text95(Fit(p.Name + (p.Friend ? Lang.T("  (friend)") : ""), nw, Ui.F95, bold), new RectangleF(r.X + 20, r.Y, nw, r.Height), fg, bold);
                string when = g ? RoomPanel.LeftText(p.Left, now, 0) : RoomPanel.Ago(p.Joined, now);
                // the last column is under the scroll bar's header part when there is one: the time stays whole in what is left
                float jw = MathF.Min(JoinW - 2, r.Right - (head.Right - JoinW) - 2);
                _ui.Text95(Fit(when, jw), new RectangleF(head.Right - JoinW + 2, r.Y, jw, r.Height), fg);
            });
        // the status bar: how many, the friends (VRCX), how long you have been there
        float p1 = 92, p2 = 92;
        string a = s == null ? "Not following" : !s.LogFound ? "No VRChat log" : !s.InRoom ? "Not in a world" : RoomCountText(n);
        _ui.Pane95(new RectangleF(win.X + 4, sy, p1, 20), Fit(a, p1 - 8));
        bool vrcx = s?.VrcxFound == true;
        _ui.Pane95(new RectangleF(win.X + 4 + p1 + 2, sy, p2, 20), Fit(vrcx ? RoomFriendsText(f) : "No VRCX", p2 - 18), vrcx ? 0xFF008000 : 0);
        string stay = RoomStay(s, now);
        float p3x = win.X + 4 + p1 + p2 + 4, p3w = win.Right - 4 - p3x;
        _ui.Pane95(new RectangleF(p3x, sy, p3w, 20), stay == null ? "" : Fit(char.ToUpperInvariant(stay[0]) + stay[1..], p3w - 8));
    }

    void Head95(RectangleF r, string text)
    {
        _p.Fill(r, Theme.W95Face);
        _p.Bevel(r, Painter.Edge.Raised);
        if (text.Length > 0) _ui.Text95(text, new RectangleF(r.X + 4, r.Y, r.Width - 6, r.Height));
    }

    // ---- --snapshot's hooks ----

    public bool DetailShown => _open != null;            // opened, or still shrinking back into its tile
    public string DetailKind => _open == null ? null : OpenKindOf(_open.Type);
    public float RoomScroll => _roomScroll;
    public float RoomScrollMax => _roomMax;

    // the opened view's X (modern: the opened views' own, top right; 95: the room window's)
    public Vector2 DetailClosePoint
    {
        get
        {
            if (Theme.Classic && DetailIs("room")) return new Vector2((_roomClose95.X + _roomClose95.Width / 2) * 2, (_roomClose95.Y + _roomClose95.Height / 2) * 2);
            return MinesClosePoint;
        }
    }
}
