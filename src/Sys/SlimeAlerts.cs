using System;
using System.Collections.Generic;
using System.Text;

namespace Orbit;

// SlimeVR's alerts (Erik 10-24; the "SlimeVR alerts" setting on the VRChat tab, SlimeVrConfig; OrbitApp.SlimeVr.cs feeds it): a card in
// Orbit's own notifications and a buzz in the watch's hand when
// - a tracker's battery is under LowPct (15 %) for LowFor (2 s) while it is connected and not charging: once per tracker and drop, armed
//   again only once it has been charged back over RearmPct (25 %). A tracker seen for under SeenFor (10 s) does not count yet (the first
//   readings after it connects can say 0 %). The battery is the board's (SlimeVR's device): an extension IMU on it (a thigh tracker's
//   ankle) tells the same one, so it is one note for the board, named after its IMUs ("Left thigh + Left ankle: 12 %").
// - a tracker that was connected is disconnected or timed out (out of range) - or gone from SlimeVR's list - for LostFor (3 s): once; when
//   it comes back, a short "back" note (no buzz). One that goes to sleep (v22's Sleeping) is not lost; a flicker under 3 s says nothing.
// - lying down (Erik's long-standing SlimeVR trouble: lying twists his hip and legs): the head lies (MathUtil.IsLyingDown) for LieFor (3 s),
//   and lies until it has been up for UpFor (2 s) - a glance up, a turn on the pillow does not end it. Once per lying-down, with SlimeVR
//   connected, the Yaw reset is offered (the widget puts it first; with the alerts on a card too, its button asking first - nothing is
//   ever reset by itself).
// What one frame says of the trackers is one note a kind (review 10-24: a tracker with an extension was two cards for one battery and one
// dropout, a router's hiccup timing out every tracker a card each - all of them in the watch's list): "Left thigh + Left ankle lost",
// "3 trackers lost" with their names; OrbitApp buzzes once a frame.
// SlimeVR not connected (its server gone or restarted, the link off): every tracker is forgotten - nothing is said about them, they start
// over with the next connection (its device ids are not kept across its restarts). The logic is here without SteamVR or SlimeVR: the
// --snapshot checks drive it with made-up snapshots and head poses (SlimeVrTest.cs). Nothing allocates at rest: a tracker's state is made
// once, a note only when one is said.
public sealed class SlimeAlerts
{
    public const int LowPct = 15, RearmPct = 25;
    public const double SeenFor = 10, LowFor = 2, LostFor = 3, LieFor = 3, UpFor = 2;

    public enum Kind { Low, Lost, Back }

    // what to say: the kind; the tracker's name (WristPanel.SlimeName) - a board's IMUs joined with " + ", of several boards said at once
    // their names with ", " (for Low each board's battery after it); the battery (the lowest); the status (the first one's; 0: gone from the
    // list); count: how many trackers (boards, as SlimeVR counts them) it is about
    public readonly record struct Note(Kind Kind, string Name, int Battery, int Status, int Count = 1);

    // the setting: SlimeVrConfig.Alerts - never switched, on while a SlimeVR widget is on a watch page
    public static bool On(Config c) => c.SlimeVr?.Alerts ?? (c.Wrist.Face == "custom" && WatchPages.Any(c.Wrist, "slimevr"));

    // ---- the trackers ----

    // a board (SlimeVR's device: its battery, its Wi-Fi) - each of its IMUs tells the same battery
    sealed class Board
    {
        public double First, LowSince = -1;
        public bool Fired, In, Low;                      // low said; in the last snapshot; low there
        public int Order;                                // its first IMU's place in the last snapshot it was in (head to feet)
    }

    // an IMU of it (a tracker in SlimeVR's list): connected, lost
    sealed class Seen
    {
        public Board Board;
        public SlimeVrLink.Tracker Last;
        public double DownSince = -1;
        public bool Up, Told;                            // seen connected; lost said
        public int Status;                               // its last status (-1: not in the last snapshot)
    }

    readonly Dictionary<int, Board> _boards = new();         // by the server's device id
    readonly Dictionary<(int, int), Seen> _seen = new();     // by the device id and the tracker's number
    readonly List<Seen> _low = new(), _lost = new(), _back = new();     // what this frame says (Say: one note each)
    SlimeVrLink.Snapshot _of;

    // every frame with the alerts on and the headset on: what the snapshot (and the time) has to say, added to 'notes'
    public void Trackers(double now, SlimeVrLink.Snapshot s, List<Note> notes)
    {
        if (s == null || s.State != SlimeVrLink.State.Ok || s.Dialect == SolarXr.Dialect.Unknown) { Forget(); return; }
        if (!ReferenceEquals(s, _of))
        {
            _of = s;
            foreach (var x in _seen.Values) x.Status = -1;
            foreach (var b in _boards.Values) (b.In, b.Low) = (false, false);
            for (int i = 0; i < s.Trackers.Count; i++)
            {
                var t = s.Trackers[i];
                if (!_boards.TryGetValue(t.Device, out var b)) _boards[t.Device] = b = new Board { First = now };
                if (!_seen.TryGetValue((t.Device, t.Num), out var x)) _seen[(t.Device, t.Num)] = x = new Seen { Board = b };
                (x.Last, x.Status) = (t, t.Status);
                // the battery (the board's): low while one of its IMUs is connected and it is not charging; armed again over RearmPct
                if (!b.In)
                {
                    (b.In, b.Order) = (true, i);
                    if (b.Fired && t.Battery > RearmPct) b.Fired = false;
                }
                if (t.Connected && !t.Charging && t.Battery >= 0 && t.Battery < LowPct) b.Low = true;
                // connected; lost (disconnected, timed out) once it was connected; asleep or no status: neither
                if (t.Connected)
                {
                    if (x.Told) _back.Add(x);
                    (x.Up, x.Told, x.DownSince) = (true, false, -1);
                }
                else if (t.Status is SlimeVrLink.Disconnected or SlimeVrLink.TimedOut) { if (x.Up && x.DownSince < 0) x.DownSince = now; }
                else (x.Up, x.Told, x.DownSince) = (false, false, -1);
            }
            foreach (var b in _boards.Values)                // (a board gone from the list is not low either)
                if (!b.Low) b.LowSince = -1;
                else if (b.LowSince < 0) b.LowSince = now;
            foreach (var x in _seen.Values)                  // gone from the list: lost too, once it was connected
                if (x.Status < 0 && x.Up && x.DownSince < 0) x.DownSince = now;
        }
        foreach (var b in _boards.Values)
            if (!b.Fired && b.LowSince >= 0 && now - b.LowSince >= LowFor && now - b.First >= SeenFor)
            {
                b.Fired = true;
                foreach (var x in _seen.Values) if (x.Board == b && x.Status >= 0) _low.Add(x);
            }
        foreach (var x in _seen.Values)
            if (x.Up && !x.Told && x.DownSince >= 0 && now - x.DownSince >= LostFor) { x.Told = true; _lost.Add(x); }
        Say(Kind.Low, _low, notes);
        Say(Kind.Lost, _lost, notes);
        Say(Kind.Back, _back, notes);
    }

    // a board's IMUs side by side, the boards head to feet
    static readonly Comparison<Seen> ByBoard = (a, b) =>
    {
        int c = a.Board.Order.CompareTo(b.Board.Order);
        if (c == 0) c = a.Last.Device.CompareTo(b.Last.Device);
        return c != 0 ? c : a.Last.Num.CompareTo(b.Last.Num);
    };

    // what this frame says of a kind: one note - a board's IMUs joined with " + ", the boards with ", " (Low: each one's battery after it
    // when there are several). Allocates only when it says something of several (one: its name as the widget has it).
    static void Say(Kind k, List<Seen> l, List<Note> notes)
    {
        if (l.Count == 0) return;
        if (l.Count > 1) l.Sort(ByBoard);
        int boards = 0, battery = int.MaxValue;
        for (int i = 0; i < l.Count; i++)
            if (i == 0 || l[i].Board != l[i - 1].Board) { boards++; battery = Math.Min(battery, l[i].Last.Battery); }
        string name = WristPanel.SlimeName(l[0].Last);
        if (l.Count > 1)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < l.Count; i++)
            {
                var t = l[i].Last;
                if (i > 0) sb.Append(l[i].Board == l[i - 1].Board ? " + " : ", ");
                sb.Append(WristPanel.SlimeName(t));
                if (k == Kind.Low && boards > 1 && (i == l.Count - 1 || l[i + 1].Board != l[i].Board)) sb.Append(' ').Append(t.Battery).Append(" %");
            }
            name = sb.ToString();
        }
        notes.Add(new Note(k, name, battery, Math.Max(0, l[0].Status), boards));
        l.Clear();
    }

    // SlimeVR gone, the alerts off: every tracker starts over
    public void Forget()
    {
        if (_of == null && _seen.Count == 0) return;
        _seen.Clear();
        _boards.Clear();
        _of = null;
    }

    // ---- lying down ----

    double _lieSince = -1, _upSince = -1;
    bool _lying, _offered;

    // every frame: head = the head lies now (MathUtil.IsLyingDown; false without SteamVR or the headset off), connected = SlimeVR's trackers
    // are there. True while lying (LieFor in, UpFor out); offer: true once per lying-down - its first frame with SlimeVR connected
    public bool Lie(double now, bool head, bool connected, out bool offer)
    {
        if (head)
        {
            _upSince = -1;
            if (_lieSince < 0) _lieSince = now;
            if (!_lying && now - _lieSince >= LieFor) (_lying, _offered) = (true, false);
        }
        else if (!_lying) _lieSince = -1;
        else
        {
            if (_upSince < 0) _upSince = now;
            if (now - _upSince >= UpFor) (_lying, _lieSince, _upSince) = (false, -1, -1);
        }
        offer = _lying && connected && !_offered;
        if (offer) _offered = true;
        return _lying;
    }

    // ---- what the cards say ----

    public const string YawGlyph = "\uE7AD";             // Segoe's rotate: the widget's Yaw reset

    // (several trackers: their count on top, their names under it)
    public static Notification Card(Note n) => n.Kind switch
    {
        Kind.Low => new Notification
        {
            Source = "SlimeVR", Title = n.Count > 1 ? Lang.F("{0} trackers under {1} %", n.Count, LowPct) : $"{n.Name}: {n.Battery} %",
            Content = n.Count > 1 ? n.Name : Lang.F("Under {0} % - time to charge it.", LowPct),
            Timeout = 6, Volume = 0.8f, Icon = Theme.Classic ? null : Alerts.BatteryIcon(),       // a retro card keeps its own icon
        },
        Kind.Lost => new Notification
        {
            Source = "SlimeVR", Title = n.Count > 1 ? Lang.F("{0} trackers lost", n.Count) : Lang.F("{0} lost", n.Name),
            Content = n.Count > 1 ? n.Name : Lang.T(n.Status == SlimeVrLink.TimedOut ? "Timed out - out of range?" : n.Status == 0 ? "Gone from SlimeVR's list."
                : "Disconnected - its battery, or out of range?"),
            Timeout = 6, Volume = 0.8f, Glyph = WristPanel.SlimeKindIcon,
        },
        _ => new Notification
        {
            Source = "SlimeVR", Title = n.Count > 1 ? Lang.F("{0} trackers back", n.Count) : Lang.F("{0} back", n.Name),
            Content = n.Count > 1 ? n.Name : Lang.T("Connected again."), Timeout = 3, Volume = 0.4f, Glyph = WristPanel.SlimeKindIcon,
        },
    };

    // lying down: the Yaw reset on a card - its button asks first ("Reset?"), the second tap calls 'reset' (OrbitApp: SlimeVrLink.Reset)
    public static Notification YawCard(Func<bool> reset) => new()
    {
        Source = "SlimeVR", Title = Lang.T("Lying down? Yaw reset"), Content = Lang.T("Lying twists the hips and legs - a yaw reset sets them straight."),
        Glyph = YawGlyph, Timeout = 15, Volume = 0.5f, Action = "Yaw reset", Ask = "Reset?", Act = reset,
    };
}
