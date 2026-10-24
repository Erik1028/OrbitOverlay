using System;
using System.Collections.Generic;

namespace Orbit;

// SlimeVR's alerts (Erik 10-24; the "SlimeVR alerts" setting on the VRChat tab, SlimeVrConfig; OrbitApp.SlimeVr.cs feeds it): a card in
// Orbit's own notifications and a buzz in the watch's hand when
// - a tracker's battery is under LowPct (15 %) for LowFor (2 s) while it is connected and not charging: once per tracker and drop, armed
//   again only once it has been charged back over RearmPct (25 %). A tracker seen for under SeenFor (10 s) does not count yet (the first
//   readings after it connects can say 0 %).
// - a tracker that was connected is disconnected or timed out (out of range) - or gone from SlimeVR's list - for LostFor (3 s): once; when
//   it comes back, a short "back" note (no buzz). One that goes to sleep (v22's Sleeping) is not lost; a flicker under 3 s says nothing.
// - lying down (Erik's long-standing SlimeVR trouble: lying twists his hip and legs): the head lies (MathUtil.IsLying) for LieFor (3 s),
//   and lies until it has been up for UpFor (2 s) - a glance up, a turn on the pillow does not end it. Once per lying-down, with SlimeVR
//   connected, the Yaw reset is offered (the widget puts it first; with the alerts on a card too, its button asking first - nothing is
//   ever reset by itself).
// SlimeVR not connected (its server gone or restarted, the link off): every tracker is forgotten - nothing is said about them, they start
// over with the next connection (its device ids are not kept across its restarts). The logic is here without SteamVR or SlimeVR: the
// --snapshot checks drive it with made-up snapshots and head poses (SlimeVrTest.cs). Nothing allocates at rest: a tracker's state is made
// once, a note only when one is said.
public sealed class SlimeAlerts
{
    public const int LowPct = 15, RearmPct = 25;
    public const double SeenFor = 10, LowFor = 2, LostFor = 3, LieFor = 3, UpFor = 2;

    public enum Kind { Low, Lost, Back }

    // what to say: the kind, the tracker's name (WristPanel.SlimeName), its battery, its status (0: gone from the list)
    public readonly record struct Note(Kind Kind, string Name, int Battery, int Status);

    // the setting: SlimeVrConfig.Alerts - never switched, on while a SlimeVR widget is on a watch page
    public static bool On(Config c) => c.SlimeVr?.Alerts ?? (c.Wrist.Face == "custom" && WatchPages.Any(c.Wrist, "slimevr"));

    // ---- the trackers ----

    sealed class Seen
    {
        public SlimeVrLink.Tracker Last;
        public double First, LowSince = -1, DownSince = -1;
        public bool Fired, Up, Told;                     // low said; seen connected; lost said
        public int Status;                               // its last status (-1: not in the last snapshot)
    }

    readonly Dictionary<(int, int), Seen> _seen = new();     // by the server's device id and the tracker's number
    SlimeVrLink.Snapshot _of;

    // every frame with the alerts on and the headset on: what the snapshot (and the time) has to say, added to 'notes'
    public void Trackers(double now, SlimeVrLink.Snapshot s, List<Note> notes)
    {
        if (s == null || s.State != SlimeVrLink.State.Ok || s.Dialect == SolarXr.Dialect.Unknown) { Forget(); return; }
        if (!ReferenceEquals(s, _of))
        {
            _of = s;
            foreach (var x in _seen.Values) x.Status = -1;
            for (int i = 0; i < s.Trackers.Count; i++)
            {
                var t = s.Trackers[i];
                if (!_seen.TryGetValue((t.Device, t.Num), out var x)) _seen[(t.Device, t.Num)] = x = new Seen { First = now };
                (x.Last, x.Status) = (t, t.Status);
                // the battery: low while connected and not charging; armed again over RearmPct
                if (x.Fired && t.Battery > RearmPct) x.Fired = false;
                bool low = t.Connected && !t.Charging && t.Battery >= 0 && t.Battery < LowPct;
                if (!low) x.LowSince = -1;
                else if (x.LowSince < 0) x.LowSince = now;
                // connected; lost (disconnected, timed out) once it was connected; asleep or no status: neither
                if (t.Connected)
                {
                    if (x.Told) notes.Add(new Note(Kind.Back, WristPanel.SlimeName(t), t.Battery, t.Status));
                    (x.Up, x.Told, x.DownSince) = (true, false, -1);
                }
                else if (t.Status is SlimeVrLink.Disconnected or SlimeVrLink.TimedOut) { if (x.Up && x.DownSince < 0) x.DownSince = now; }
                else (x.Up, x.Told, x.DownSince) = (false, false, -1);
            }
            foreach (var x in _seen.Values)                  // gone from the list: lost too, once it was connected
                if (x.Status < 0)
                {
                    x.LowSince = -1;
                    if (x.Up && x.DownSince < 0) x.DownSince = now;
                }
        }
        foreach (var x in _seen.Values)
        {
            if (!x.Fired && x.LowSince >= 0 && now - x.LowSince >= LowFor && now - x.First >= SeenFor)
            {
                x.Fired = true;
                notes.Add(new Note(Kind.Low, WristPanel.SlimeName(x.Last), x.Last.Battery, x.Last.Status));
            }
            if (x.Up && !x.Told && x.DownSince >= 0 && now - x.DownSince >= LostFor)
            {
                x.Told = true;
                notes.Add(new Note(Kind.Lost, WristPanel.SlimeName(x.Last), x.Last.Battery, Math.Max(0, x.Status)));
            }
        }
    }

    // SlimeVR gone, the alerts off: every tracker starts over
    public void Forget()
    {
        if (_of == null && _seen.Count == 0) return;
        _seen.Clear();
        _of = null;
    }

    // ---- lying down ----

    double _lieSince = -1, _upSince = -1;
    bool _lying, _offered;

    // every frame: head = the head lies now (MathUtil.IsLying; false without SteamVR or the headset off), connected = SlimeVR's trackers
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

    public static Notification Card(Note n) => n.Kind switch
    {
        Kind.Low => new Notification
        {
            Source = "SlimeVR", Title = $"{n.Name}: {n.Battery} %", Content = Lang.F("Under {0} % - time to charge it.", LowPct),
            Timeout = 6, Volume = 0.8f, Icon = Theme.Classic ? null : Alerts.BatteryIcon(),       // a retro card keeps its own icon
        },
        Kind.Lost => new Notification
        {
            Source = "SlimeVR", Title = Lang.F("{0} lost", n.Name),
            Content = Lang.T(n.Status == SlimeVrLink.TimedOut ? "Timed out - out of range?" : n.Status == 0 ? "Gone from SlimeVR's list."
                : "Disconnected - its battery, or out of range?"),
            Timeout = 6, Volume = 0.8f, Glyph = WristPanel.SlimeKindIcon,
        },
        _ => new Notification
        {
            Source = "SlimeVR", Title = Lang.F("{0} back", n.Name), Content = Lang.T("Connected again."), Timeout = 3, Volume = 0.4f,
            Glyph = WristPanel.SlimeKindIcon,
        },
    };

    // lying down: the Yaw reset on a card - its button asks first ("Reset?"), the second tap calls 'reset' (OrbitApp: SlimeVrLink.Reset)
    public static Notification YawCard(Func<bool> reset) => new()
    {
        Source = "SlimeVR", Title = Lang.T("Lying down? Yaw reset"), Content = Lang.T("Lying twists the hips and legs - a yaw reset sets them straight."),
        Glyph = YawGlyph, Timeout = 15, Volume = 0.5f, Action = "Yaw reset", Ask = "Reset?", Act = reset,
    };
}
