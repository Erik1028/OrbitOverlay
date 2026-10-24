using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace Orbit;

// SlimeVR on the watch (the "slimevr" widget, WristSlimeVr.cs; Erik 10-24: "slimevr widget is lehetne"): the full-body trackers'
// batteries, signal and status, and SlimeVR's three resets - from the SlimeVR server's own API, SolarXR: the protocol its GUI talks,
// FlatBuffers messages over a WebSocket on ws://127.0.0.1:21110 (github.com/SlimeVR/SolarXR-Protocol, schema/*.fbs; the GUI's side:
// SlimeVR-Server gui/src/hooks/websocket-api.ts, datafeed-config.ts, reset.ts). Two dialects of it are out there (SolarXr below): the
// one of SlimeVR up to v21 and v22's (Erik runs v22.0.0-rc.2) - which one a server speaks is read from its first update.
// Connected only while a SlimeVR widget is on a watch page and the watch has been seen in the last 20 s (OrbitApp.SlimeVr.cs) - else
// nothing is open. On connecting it asks for one data feed (StartDataFeed, the same in both): the devices' hardware status (battery,
// RSSI, ping) and their trackers' info and status, at most one update a second (minimum_time_since_last 1000 ms) - no rotations,
// positions or bones. The server not there: tried again every 3 s, said once in the log. A reset goes out as that server's GUI sends it
// (an RpcMessage ResetRequest: the type, every tracker or the body parts picked; v22's with the GUI's delays), its answers (how long,
// done) come back on the same socket. All of it on a thread of its own: the watch takes immutable snapshots (a Version each), nothing is
// made on a frame. Nothing of SlimeVR's is written or changed but what a reset does.
// More (Erik 10-24: "a slimevr widgetbe több funkció"): once the dialect is known the feed is asked again in its own words - the status
// feed with the server guards (may it do a yaw / mounting reset now: v22 ignores a reset its guards do not allow, without a word), and
// while the widget's Body side shows a second feed, the skeleton's bones at 20 a second (SlimePose.cs) - both in one StartDataFeed, as
// SlimeVR's GUI asks (a new one replaces the old: the bones stop 2 s after the Body side closed). v22 also says its reset settings (the
// mounting method: Erik's is the step). A reset any client, keybind or tap started comes to every connection: the last one seen is kept
// (LastAt). The batteries' fall over the last 20 min gives each tracker's time left (SlimeBattery.cs), the server's own when it sends one.
public sealed class SlimeVrLink : IDisposable
{
    public const int ServerPort = 21110;                   // the SlimeVR server's WebSocket (SolarXR)
    const int Every = 3;                                   // s between two tries while the server is not there
    const int FeedMs = 1000;                               // the feed: at most one update a second
    const int Stale = 15;                                  // s without an update: the server is stuck - connect again

    // Off: not asked for; Connecting: asking (what was shown before stays); NotRunning: no server on the port; Ok: the feed comes
    public enum State { Off, Connecting, NotRunning, Ok }

    // SolarXR's ResetType (the same numbers in both dialects)
    public enum ResetKind { Yaw = 0, Full = 1, Mounting = 2 }

    // where a tracker is worn, from either dialect's BodyPart (their numbers differ) - in the order the watch lists them, head to feet
    public enum Part : byte
    {
        None, Head, Neck, UpperChest, Chest, LowerChest, Waist, UpperWaist, LowerWaist, Hip, LeftHip, RightHip, LeftThigh, RightThigh,
        LeftAnkle, RightAnkle, LeftFoot, RightFoot, LeftShoulder, RightShoulder, LeftUpperArm, RightUpperArm, LeftLowerArm, RightLowerArm,
        LeftHand, RightHand, Finger, Toe,
    }

    // SolarXR's TrackerStatus (v22 added Sleeping)
    public const int Disconnected = 1, Ok = 2, Busy = 3, Error = 4, Occluded = 5, TimedOut = 6, Sleeping = 7;

    public sealed class Tracker
    {
        public int Device, Num;                            // the server's device id (not kept across its restarts), the tracker's number
        public Part Part;
        public string CustomName = "", DisplayName = "", DeviceName = "";
        public int Battery = -1;                           // % (-1: not told)
        public float Voltage = float.NaN;                  // V
        public int Rssi = int.MinValue;                    // dBm (MinValue: not told)
        public int Ping = -1;                              // ms (-1: not told)
        public int Status;                                 // TrackerStatus (the constants above)
        public bool Imu, Hmd;
        public int ServerMin = -1;                         // the server's battery runtime estimate, minutes (-1: none sent)
        public int LeftMin = -1;                           // its battery's time left, minutes, rounded (-1: nothing to say; SlimeBattery)

        public bool Charging => Voltage > 4.3f;            // (the GUI's rule: a charging tracker reads over 4.3 V)
        public bool Connected => Status is Ok or Busy or Error or Occluded;

        public bool Same(Tracker o) =>
            o != null && Device == o.Device && Num == o.Num && Part == o.Part && Battery == o.Battery && Rssi == o.Rssi && Ping == o.Ping &&
            Status == o.Status && Charging == o.Charging && CustomName == o.CustomName && DisplayName == o.DisplayName && DeviceName == o.DeviceName &&
            LeftMin == o.LeftMin;
    }

    public sealed class Snapshot
    {
        public int Version;
        public State State;
        public SolarXr.Dialect Dialect;
        public readonly List<Tracker> Trackers = new();    // the body's trackers (IMUs, not the headset), head to feet
        public int Connected;                              // of them, the ones not disconnected
        // the last reset's answer: its kind (-1: none yet), 0 running / 1 done / 2 cancelled or failed, the ms left when it came, when
        // (Clock.Now)
        public int ResetKind = -1, ResetState;
        public int ResetLeftMs;
        public double ResetAt;
        // ... its body parts (a Part mask, 0: all), v22's step mounting phase (StepMountingStatus, 0: none), failed (v22's FAILED)
        public ulong ResetParts;
        public int ResetPhase;
        public bool ResetFailed;
        // the last reset seen done - Orbit's, or one the GUI, a keybind or a tap started (the server tells every connection): its kind
        // (-1: none), parts, when (Clock.Now); kept across a reconnect, forgotten when SlimeVR is not running
        public int LastKind = -1;
        public ulong LastParts;
        public double LastAt;
        // the server guards (-1: not known; 0 it may, 1 a full reset first, 2 the headset's position needed, 3 no trackers - v22's
        // ResetAvailability, v21's bools 0 / 1) and its mounting method (-1: not known; 0 unknown, 1 manual, 2 the pose, 3 the step - v21:
        // always the pose), the feet in a mounting reset
        public int YawGuard = -1, MountGuard = -1, MountMethod = -1;
        public bool MountFeet;
    }

    public readonly int Port;
    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    readonly ConcurrentQueue<(ResetKind kind, ulong parts, bool cancel)> _resets = new();
    Thread _thread;
    volatile bool _stop, _active;
    Snapshot _pub = new();
    SlimePose _pose;
    int _version, _sent, _poseVersion, _starts;
    readonly SlimeBattery _battery = new();
    // the link's thread's: this connection's guards and settings, the last reset done (Publish copies them)
    int _yawGuard = -1, _mountGuard = -1, _method = -1, _lastKind = -1;
    bool _feet;
    ulong _lastParts;
    double _lastAt;

    // the bones: asked for while this is true (the Body side shows - OrbitApp.SlimeVr.cs), and 2 s after
    public volatile bool WantPose;
    const int PoseMs = 50;                                 // the bones' feed: 20 a second
    const double PoseLinger = 2;                           // s they stay asked for after WantPose went off (flipping sides asks once)

    // port: the tools' made-up server (never SlimeVR's own); the app's link: ServerPort
    public SlimeVrLink(int port = ServerPort) { Port = port; }

    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            _active = value;
            if (value && _thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "slimevr", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
            _wake.Set();
        }
    }

    public Snapshot Get() { lock (_lock) return _pub; }

    // the newest skeleton (null: none since connecting - it keeps its At: an old one is "no pose yet" on the watch)
    public SlimePose GetPose() { lock (_lock) return _pose; }

    // the resets sent so far, the StartDataFeeds sent (the tools' counts)
    public int Sent => Volatile.Read(ref _sent);
    public int Starts => Volatile.Read(ref _starts);

    // A reset, sent on the socket by the link's thread (parts: a Part mask - just those trackers, 0: every one); false: not connected,
    // or the server's dialect not known yet (nothing is sent)
    public bool Reset(ResetKind k, ulong parts = 0)
    {
        var s = Get();
        if (!_active || s.State != State.Ok || s.Dialect == SolarXr.Dialect.Unknown) return false;
        _resets.Enqueue((k, parts, false));
        return true;
    }

    // v22: stop the reset running (CancelResetRequest; it answers CANCELED); false: not v22, not connected
    public bool CancelReset()
    {
        var s = Get();
        if (!_active || s.State != State.Ok || s.Dialect != SolarXr.Dialect.V22) return false;
        _resets.Enqueue((ResetKind.Mounting, 0, true));
        return true;
    }

    // a new snapshot: the state, the trackers (null: the last ones kept), the reset's answer kept unless given
    void Publish(State st, List<Tracker> trackers, SolarXr.Dialect dialect, SolarXr.ResetNews reset = default)
    {
        var old = _pub;
        var s = new Snapshot { State = st, Dialect = dialect };
        if (trackers != null) s.Trackers.AddRange(trackers);
        else if (st != State.NotRunning) s.Trackers.AddRange(old.Trackers);
        foreach (var t in s.Trackers) if (t.Connected) s.Connected++;
        if (reset.Has)
        {
            (s.ResetKind, s.ResetState, s.ResetLeftMs, s.ResetAt) = (reset.Type, reset.State, reset.LeftMs, Clock.Now);
            (s.ResetParts, s.ResetPhase, s.ResetFailed) = (reset.Parts, reset.Phase, reset.Failed);
            if (reset.State == 1) (_lastKind, _lastParts, _lastAt) = (reset.Type, reset.Parts, Clock.Now);
        }
        else
        {
            (s.ResetKind, s.ResetState, s.ResetLeftMs, s.ResetAt) = (old.ResetKind, old.ResetState, old.ResetLeftMs, old.ResetAt);
            (s.ResetParts, s.ResetPhase, s.ResetFailed) = (old.ResetParts, old.ResetPhase, old.ResetFailed);
        }
        if (st == State.NotRunning) (_lastKind, _lastParts, _lastAt) = (-1, 0, 0);
        (s.LastKind, s.LastParts, s.LastAt) = (_lastKind, _lastParts, _lastAt);
        (s.YawGuard, s.MountGuard, s.MountMethod, s.MountFeet) = (_yawGuard, _mountGuard, dialect == SolarXr.Dialect.V21 ? 2 : _method, _feet);
        s.Version = ++_version;
        lock (_lock) _pub = s;
    }

    // each tracker's time left: the server's estimate while it sends one (not charging), else the battery's fall (SlimeBattery)
    void Forecast(List<Tracker> got)
    {
        double now = Clock.Now;
        for (int i = 0; i < got.Count; i++)
        {
            var t = got[i];
            if (i == 0 || got[i - 1].Device != t.Device) _battery.Add(now, t.Device, t.Battery, t.Charging);
            t.LeftMin = t.Charging ? -1 : t.ServerMin > 0 ? SlimeBattery.Round(t.ServerMin) : _battery.Left(t.Device);
        }
    }

    static bool SameTrackers(List<Tracker> a, List<Tracker> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!a[i].Same(b[i])) return false;
        return true;
    }

    // ---- the worker ----

    void Run()
    {
        var buf = new byte[16 * 1024];
        var got = new List<Tracker>();
        var inbox = new SolarXr.Inbox();
        bool told = false;                       // "not running" said (again after it ran)
        var uri = new Uri($"ws://127.0.0.1:{Port}");
        while (!_stop)
        {
            if (!_active)
            {
                if (_pub.State != State.Off) Publish(State.Off, null, _pub.Dialect);
                _wake.WaitOne();
                continue;
            }
            if (_pub.State is State.Off) Publish(State.Connecting, null, _pub.Dialect);
            bool wasUp = false;
            var dialect = SolarXr.Dialect.Unknown;          // (each connection reads it again: the server may have been updated)
            (_yawGuard, _mountGuard, _method, _feet) = (-1, -1, -1, false);
            bool asked = false, posing = false;             // the feeds asked in the server's dialect; the bones among them
            double poseUntil = -1;
            System.Threading.Tasks.Task<WebSocketReceiveResult> recv = null;
            try
            {
                using var ws = new ClientWebSocket();
                using (var cts = new CancellationTokenSource(2000))
                    ws.ConnectAsync(uri, cts.Token).GetAwaiter().GetResult();
                ws.SendAsync(SolarXr.StartFeed(FeedMs), WebSocketMessageType.Binary, true, CancellationToken.None).Wait(2000);
                Interlocked.Increment(ref _starts);
                Log.Info($"slimevr: connected to {uri}, a feed asked for every {FeedMs} ms");
                told = false;
                wasUp = true;
                double last = Clock.Now;
                int have = 0;
                bool first = true;
                while (!_stop && _active && ws.State == WebSocketState.Open)
                {
                    recv ??= ws.ReceiveAsync(new ArraySegment<byte>(buf, have, buf.Length - have), CancellationToken.None);
                    if (recv.Wait(100))
                    {
                        var r = recv.Result;
                        recv = null;
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        have += r.Count;
                        if (!r.EndOfMessage)
                        {
                            if (have == buf.Length)
                            {
                                if (buf.Length >= 4 << 20) throw new InvalidOperationException("a message over 4 MB");
                                Array.Resize(ref buf, buf.Length * 2);
                            }
                            continue;
                        }
                        var was = dialect;
                        inbox.Clear();
                        if (r.MessageType == WebSocketMessageType.Binary)
                        {
                            try { SolarXr.Read(buf.AsSpan(0, have), got, inbox, ref dialect); }
                            catch (FormatException e) { Log.Info("slimevr: a message not read: " + e.Message); }
                        }
                        have = 0;
                        var news = inbox.Reset;
                        bool feed = inbox.Trackers;
                        if (dialect != was) Log.Info($"slimevr: the server speaks SolarXR {SolarXr.Name(dialect)}");
                        if (feed)
                        {
                            last = Clock.Now;
                            if (first && dialect != SolarXr.Dialect.Unknown) Log.Info($"slimevr: {got.Count} tracker(s) on the body");
                            if (dialect != SolarXr.Dialect.Unknown) { first = false; Forecast(got); }
                        }
                        if (inbox.Bones > 0)
                        {
                            last = Clock.Now;
                            var pose = new SlimePose(inbox.Raw, ++_poseVersion, Clock.Now, _pose);
                            lock (_lock) _pose = pose;
                        }
                        bool more = false;                   // guards or settings changed: a new snapshot
                        if (inbox.Guards && (inbox.YawGuard, inbox.MountGuard) != (_yawGuard, _mountGuard))
                        {
                            (_yawGuard, _mountGuard) = (inbox.YawGuard, inbox.MountGuard);
                            Log.Info($"slimevr: the server's guards - yaw {_yawGuard}, mounting {_mountGuard} (0: it may)");
                            more = true;
                        }
                        if (inbox.Settings && (inbox.MountMethod, inbox.MountFeet) != (_method, _feet))
                        {
                            (_method, _feet) = (inbox.MountMethod, inbox.MountFeet);
                            Log.Info($"slimevr: its mounting reset is {_method switch { 1 => "manual", 2 => "the pose", 3 => "the step", _ => "not set" }}{(_feet ? ", the feet too" : "")}");
                            more = true;
                        }
                        if (news.Has)
                            Log.Info($"slimevr: reset {(ResetKind)news.Type} {news.State switch { 0 => $"running, {news.LeftMs} ms left", 1 => "done", _ => news.Failed ? "failed" : "cancelled" }}" +
                                     $"{(news.Parts != 0 ? $" ({SlimeGroups.Name(news.Parts)})" : "")}{(news.Phase != 0 ? $", step {news.Phase}" : "")}");
                        if (news.Has || more || (feed && (_pub.State != State.Ok || _pub.Dialect != dialect || !SameTrackers(got, _pub.Trackers))))
                            Publish(feed ? State.Ok : _pub.State, feed ? got : null, dialect, news);
                    }
                    // the feeds in the server's own words once it is known (with the guards); the bones while they are wanted, and 2 s after
                    if (dialect != SolarXr.Dialect.Unknown)
                    {
                        double now = Clock.Now;
                        if (WantPose) poseUntil = now + PoseLinger;
                        bool pose = now < poseUntil;
                        if (!asked || pose != posing)
                        {
                            ws.SendAsync(SolarXr.StartFeeds(dialect, FeedMs, pose ? PoseMs : 0), WebSocketMessageType.Binary, true, CancellationToken.None).Wait(2000);
                            Interlocked.Increment(ref _starts);
                            if (!asked && dialect == SolarXr.Dialect.V22)
                                ws.SendAsync(SolarXr.ResetsSettingsRequest(), WebSocketMessageType.Binary, true, CancellationToken.None).Wait(2000);
                            Log.Info(!asked ? $"slimevr: the feed asked again {SolarXr.Name(dialect)}, with the server guards{(pose ? $", the bones every {PoseMs} ms" : "")}"
                                : pose ? $"slimevr: the bones asked for, every {PoseMs} ms" : "slimevr: the bones no longer asked for");
                            (asked, posing) = (true, pose);
                        }
                    }
                    while (_resets.TryDequeue(out var q))
                    {
                        var msg = q.cancel ? SolarXr.CancelReset() : SolarXr.ResetRequest(q.kind, dialect, q.parts, _method);
                        ws.SendAsync(msg, WebSocketMessageType.Binary, true, CancellationToken.None).Wait(2000);
                        if (q.cancel) { Log.Info("slimevr: the reset cancelled"); continue; }
                        Interlocked.Increment(ref _sent);
                        Log.Info($"slimevr: {q.kind} reset sent{(q.parts != 0 ? $" ({SlimeGroups.Name(q.parts)})" : "")}");
                    }
                    if (Clock.Now - last > Stale) { Log.Info($"slimevr: no update for {Stale} s - connecting again"); break; }
                }
                if (ws.State == WebSocketState.Open)
                {
                    try { ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).Wait(500); } catch { }
                }
                ws.Abort();
                if (wasUp && !_stop && _active) Log.Info("slimevr: the server went away");
            }
            catch (Exception e)
            {
                if (wasUp) Log.Info($"slimevr: the server went away ({Inner(e)})");
                else if (!told) Log.Info($"slimevr: SlimeVR not running (no server on {uri}: {Inner(e)}) - trying again every {Every} s");
                told = true;
            }
            try { recv?.Wait(100); } catch { }               // (the receive the socket's end cut short: seen, not left to the finalizer)
            while (_resets.TryDequeue(out _)) { }          // (a reset asked for meanwhile is not sent later)
            if (_stop) break;
            if (!_active) continue;
            if (_pub.State != State.NotRunning)
            {
                _battery.Clear();
                (_yawGuard, _mountGuard, _method, _feet) = (-1, -1, -1, false);
                Publish(State.NotRunning, null, SolarXr.Dialect.Unknown);
            }
            _wake.WaitOne(Every * 1000);
        }
    }

    static string Inner(Exception e)
    {
        while (e.InnerException != null && e is AggregateException or WebSocketException) e = e.InnerException;
        return e.Message;
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(2000);
    }
}

// The groups of body parts the watch resets on their own (Erik 10-24, the Body side: a yaw reset of just the left leg): every tracker
// (no body parts sent: as before), the legs, one leg, the upper body - the head left out: a partial reset turns those trackers to the
// head's heading (both dialects; the head is always the reference, v22 resets it first whatever is asked - harmless for a headset).
// In Orbit's Part numbers; SolarXr.Wire says them in the server's (v22 has no hip bones: left out there).
public static class SlimeGroups
{
    public const int All = 0, Legs = 1, LeftLeg = 2, RightLeg = 3, Upper = 4, Count = 5;
    public static readonly string[] Names = { "All", "Legs", "Left leg", "Right leg", "Upper body" };

    static readonly ulong Left = Bits(SlimeVrLink.Part.LeftHip, SlimeVrLink.Part.LeftThigh, SlimeVrLink.Part.LeftAnkle, SlimeVrLink.Part.LeftFoot),
        Right = Bits(SlimeVrLink.Part.RightHip, SlimeVrLink.Part.RightThigh, SlimeVrLink.Part.RightAnkle, SlimeVrLink.Part.RightFoot),
        Up = Bits(SlimeVrLink.Part.Neck, SlimeVrLink.Part.UpperChest, SlimeVrLink.Part.Chest, SlimeVrLink.Part.LowerChest, SlimeVrLink.Part.Waist,
            SlimeVrLink.Part.UpperWaist, SlimeVrLink.Part.LowerWaist, SlimeVrLink.Part.Hip, SlimeVrLink.Part.LeftShoulder, SlimeVrLink.Part.RightShoulder,
            SlimeVrLink.Part.LeftUpperArm, SlimeVrLink.Part.RightUpperArm, SlimeVrLink.Part.LeftLowerArm, SlimeVrLink.Part.RightLowerArm,
            SlimeVrLink.Part.LeftHand, SlimeVrLink.Part.RightHand),
        LeftCore = Bits(SlimeVrLink.Part.LeftThigh, SlimeVrLink.Part.LeftAnkle, SlimeVrLink.Part.LeftFoot),
        RightCore = Bits(SlimeVrLink.Part.RightThigh, SlimeVrLink.Part.RightAnkle, SlimeVrLink.Part.RightFoot);

    static ulong Bits(params SlimeVrLink.Part[] ps)
    {
        ulong m = 0;
        foreach (var p in ps) m |= SolarXr.Bit(p);
        return m;
    }

    // a group's parts (All: 0 - no body parts, every tracker)
    public static ulong Mask(int g) => g switch { Legs => Left | Right, LeftLeg => Left, RightLeg => Right, Upper => Up, _ => 0 };

    // the group a mask reads as (an echo of the server's: v21 answers a yaw of every tracker with its own list of them all)
    public static int Of(ulong m)
    {
        if (m == 0) return All;
        bool l = (m & LeftCore) != 0, r = (m & RightCore) != 0, u = (m & Up) != 0;
        return l && r && u ? All : l && r ? Legs : l && !u ? LeftLeg : r && !u ? RightLeg : u && !l && !r ? Upper : All;
    }

    public static bool Has(int g, SlimeVrLink.Part p) => g == All || (Mask(g) & SolarXr.Bit(p)) != 0;

    public static string Name(ulong m) => Names[Of(m)];
}

// SolarXR, by hand: the few tables Orbit reads and writes, a small FlatBuffers reader and builder (no package: the installer stays as it
// is). Two dialects (each union field takes two slots: its type, then the table):
//  V21 - SlimeVR up to v21, the protocol repository's main (00c38a6d); the slots checked against its generated Java
//    (protocol/java/src/solarxr_protocol/**: builder.addX(slot, ...)):
//    DeviceData { id 0 (struct DeviceId: a uint8), custom_name 1, hardware_info 2, hardware_status 3, trackers 4 }
//    HardwareStatus { error_status 0, tps 1, ping 2 (uint16), rssi 3 (int16), mcu_temp 4, battery_voltage 5, battery_pct_estimate 6, ... }
//    TrackerData { tracker_id 0 (table TrackerId { device_id 0, tracker_num 1 }), info 1, status 2, rotation 3, ... }
//    TrackerInfo { imu_type 0, body_part 1, poll_rate 2, mounting_orientation 3, editable 4, is_computed 5, is_imu 6, display_name 7,
//      custom_name 8, allow_drift_compensation 9, mounting_reset_orientation 10, is_hmd 11, ... }
//    RpcMessageHeader { tx_id 0 (struct), message_type 1, message 2 }; RpcMessage ResetResponse 4 { reset_type 0, status 1 (started 0,
//      finished 1), body_parts 2, progress 3 (ms), duration 4 (ms) }
//  V22 - SlimeVR v22 (its rewrite builds SolarXR 2c8d9c3f, 2026-10-20; checked against that commit's generated Java):
//    DeviceData { id 0 (uint16), custom_name 1, hardware_info 2, hardware_status 3, trackers 4, origin 5 }
//    HardwareStatus { error_status 0, ping 1, rssi 2, rssi_min 3, rssi_max 4, mcu_temp 5, battery_voltage 6, battery_pct_estimate 7, ... }
//    TrackerData { device_id 0 (uint16), tracker_id 1 (uint16), info 2, status 3, rotation 4, ... }
//    TrackerInfo { is_imu 0, imu_type 1, body_part 2, intended_body_part 3, mounting_orientation 4, mounting_reset_orientation 5,
//      display_name 6, custom_name 7, ... } (no is_hmd: the headset is not an IMU); BodyPart renumbered (UPPER_CHEST 3 ... HIP 7 ...)
//    RpcMessageHeader { tx_id 0 (uint32), reply_to 1, message_type 2, message 3 }; RpcMessage ResetStatusResponse 4 { reset_type 0,
//      lifecycle 1 (running 0, done 1, cancelled 2, failed 3), body_parts 2, detail 3+4 (CountdownDetail 1 { progress 0, duration 1 }) }
//  The same in both: MessageBundle { data_feed_msgs 0, rpc_msgs 1, ... }; DataFeedMessageHeader { message_type 0, message 1 } -
//    StartDataFeed 2, DataFeedUpdate 3; StartDataFeed { data_feeds 0 }; DataFeedConfig { minimum_time_since_last 0 (uint16), data_mask 1 };
//    DeviceDataMask { tracker_data 0, device_data 1 }; TrackerDataMask { info 0, status 1 }; DataFeedUpdate { devices 0 };
//    RpcMessage ResetRequest 3 { reset_type 0, body_parts 1, delay 2 (float, null) }.
// The two headers read each other as "no message" (V21's slot 1 is V22's reply_to, never set here; V22 reads V21's type from slot 2,
// where V21 has the message's offset - laid out here so its low byte is 0, NONE): a reset sent in the wrong dialect does nothing.
// The bones, the guards, v22's reset settings (Erik 10-24, the Body side; the slots from the two commits' schema files):
//  V21 - DataFeedConfig { ..., synthetic_trackers_mask 2, bone_mask 3 (bool), stay_aligned_pose_mask 4, server_guards_mask 5 (bool) };
//    DataFeedUpdate { devices 0, synthetic_trackers 1, bones 2, stay_aligned_pose 3, index 4, server_guards 5 }; Bone { body_part 0,
//    rotation_g 1 (struct Quat), bone_length 2, head_position_g 3 (struct Vec3f) }; ServerGuards { canDoMounting 0, canDoYawReset 1 };
//    HardwareStatus' battery_runtime_estimate 11 (int64, us). Its server sends every config's due update in one bundle with a slot for
//    each config - one not due is a 0 offset, pointing at the message's end (DataFeedHandler.sendDataFeedUpdate): read element by element.
//  V22 - DataFeedConfig { ..., bone_mask 2 (table BoneMask { body_part 0, bone_length 1, rotation 2, orientation 3, head_position 4,
//    tail_position 5, ... }), server_guards_mask 3 (bool) }; DataFeedUpdate { devices 0, bones 1, index 2, server_guards 3 }; Bone
//    { body_part 0, bone_length 1, rotation 2, orientation 3, head_position 4, tail_position 5 }; ServerGuards { yaw_reset 0,
//    mounting_reset 1 (ResetAvailability) }; battery_runtime_estimate 12; RpcMessage ResetsSettingsRequest 23, ResetsSettingsResponse 24
//    { reset_mounting_feet 0, arms_mounting_reset_mode 1, ..., mounting_method 5 }, CancelResetRequest 132; ResetStatusResponse's detail
//    2: StepMountingDetail { status 0 }.
//  The first StartDataFeed asks only what both read alike (V22's server_guards_mask is V21's bone_mask, V22's bone_mask V21's
//  synthetic_trackers_mask): the one in the server's words follows once its dialect is known. An update is told by what it holds (its
//  devices, its bones, its guards), not by its index.
public static class SolarXr
{
    public enum Dialect { Unknown, V21, V22 }

    public static string Name(Dialect d) => d switch { Dialect.V21 => "as SlimeVR up to v21", Dialect.V22 => "as SlimeVR v22", _ => "(not known yet)" };

    public const int BundleFeed = 0, BundleRpc = 1;
    public const int FeedStart = 2, FeedUpdate = 3;
    public const int RpcReset = 3, RpcResetAnswer = 4, RpcSettingsAsk = 23, RpcSettings = 24, RpcCancel = 132;

    // a reset's answer: its type, 0 running / 1 done / 2 cancelled or failed, the ms left; its body parts (a Part mask, 0: all), v22's
    // step mounting phase (0: none), failed (v22's FAILED)
    public struct ResetNews { public bool Has; public int Type, State, LeftMs, Phase; public ulong Parts; public bool Failed; }

    // what one message said (one per connection, cleared before each): its trackers (Read's list holds them), its bones (Raw), the
    // server's guards, v22's reset settings, a reset's answer
    public sealed class Inbox
    {
        public bool Trackers, Guards, Settings, MountFeet;
        public int Bones, YawGuard, MountGuard, MountMethod;
        public ResetNews Reset;
        public readonly SlimePose.Raw Raw = new();

        public void Clear()
        {
            (Trackers, Guards, Settings, MountFeet, Bones, YawGuard, MountGuard, MountMethod, Reset) = (false, false, false, false, 0, -1, -1, -1, default);
            Raw.Clear();
        }
    }

    // StartDataFeed: one feed - the devices' hardware status (device_data) and their trackers' info and status, at most every 'ms'
    public static byte[] StartFeed(int ms) => new FbObj()
        .Objs(BundleFeed, new FbObj().U8(0, FeedStart).Obj(1, new FbObj()
            .Objs(0, new FbObj().U16(0, ms).Obj(1, new FbObj()
                .Obj(0, new FbObj().Bool(0, true).Bool(1, true))
                .Bool(1, true)))))
        .Finish();

    // StartDataFeed in the server's dialect: the status feed (as StartFeed) with the server guards, every 'ms'; poseMs > 0: a second feed,
    // the skeleton's bones every poseMs (v21: its bone_mask bool; v22: BoneMask - the body part, the rotation, the head and the tail).
    // As SlimeVR's GUI asks: one message, its feeds by their place (each update's index says which); a new one replaces them all.
    public static byte[] StartFeeds(Dialect d, int ms, int poseMs)
    {
        bool v22 = d == Dialect.V22;
        var status = new FbObj().U16(0, ms).Obj(1, new FbObj()
                .Obj(0, new FbObj().Bool(0, true).Bool(1, true))
                .Bool(1, true))
            .Bool(v22 ? 3 : 5, true);
        var feeds = poseMs <= 0 ? new[] { status } : new[]
        {
            status, v22 ? new FbObj().U16(0, poseMs).Obj(2, new FbObj().Bool(0, true).Bool(2, true).Bool(4, true).Bool(5, true))
                : new FbObj().U16(0, poseMs).Bool(3, true),
        };
        return new FbObj().Objs(BundleFeed, new FbObj().U8(0, FeedStart).Obj(1, new FbObj().Objs(0, feeds))).Finish();
    }

    // ResetRequest, as that server's GUI sends it: the type, no body parts (every tracker) or the ones picked (parts: a Part mask, in the
    // dialect's numbers - a part it has no number for is left out); v22's GUI gives the delay (yaw 0 s, full 3 s, mounting 0 s for the
    // step and 3 s for the pose - its server takes none as 0), v21's leaves it out (its server's own delays). mountMethod: v22's
    // (-1: not known - the pose's 3 s)
    public static byte[] ResetRequest(SlimeVrLink.ResetKind k, Dialect d, ulong parts = 0, int mountMethod = -1)
    {
        var req = new FbObj().U8(0, (int)k);
        if (parts != 0) req.Bytes(1, Wire(parts, d));
        if (d == Dialect.V22)
        {
            float delay = k switch { SlimeVrLink.ResetKind.Yaw => 0, SlimeVrLink.ResetKind.Mounting when mountMethod == 3 => 0, _ => 3 };
            return new FbObj().Objs(BundleRpc, new FbObj().U8(2, RpcReset).Obj(3, req.F32(2, delay))).Finish();
        }
        return new FbObj().Objs(BundleRpc, new FbObj().U8(1, RpcReset).Obj(2, req, landing: 256)).Finish();
    }

    // v22's CancelResetRequest and ResetsSettingsRequest (empty tables)
    public static byte[] CancelReset() => new FbObj().Objs(BundleRpc, new FbObj().U8(2, RpcCancel).Obj(3, new FbObj())).Finish();
    public static byte[] ResetsSettingsRequest() => new FbObj().Objs(BundleRpc, new FbObj().U8(2, RpcSettingsAsk).Obj(3, new FbObj())).Finish();

    // A message from the server: its data feed update's body trackers into 'into', head to feet (true: it had one), a reset's answer into
    // 'reset'. d: the server's dialect - read from the first update with a tracker in it (until then nothing is listed). A device that
    // cannot be read is left out (v21 writes a 0 offset for one without trackers); a broken bundle throws FormatException.
    public static bool Read(ReadOnlySpan<byte> msg, List<SlimeVrLink.Tracker> into, ref ResetNews reset, ref Dialect d)
    {
        var box = new Inbox();
        box.Clear();
        Read(msg, into, box, ref d);
        if (box.Reset.Has) reset = box.Reset;
        return box.Trackers;
    }

    // ... all of it into 'box' (cleared by the caller): the trackers (into, only from an update with devices - one with only bones leaves
    // the list alone), the bones, the guards, v22's reset settings, a reset's answer. A broken bundle throws FormatException; a broken
    // element of it is skipped (v21's dangling slots).
    public static void Read(ReadOnlySpan<byte> msg, List<SlimeVrLink.Tracker> into, Inbox box, ref Dialect d)
    {
        var r = new FbRead(msg);
        int root = r.Root();
        int n = r.Vector(root, BundleFeed, out int h0);
        for (int i = 0; i < n; i++)
        {
            try
            {
                int h = r.Element(h0, i);
                if (r.U8(h, 0) != FeedUpdate) continue;
                int u = r.Table(h, 1);
                if (u == 0) continue;
                if (r.Has(u, 0))
                {
                    if (d == Dialect.Unknown) d = Strict(r, u, Dialect.V22) ? Dialect.V22 : Strict(r, u, Dialect.V21) ? Dialect.V21 : Dialect.Unknown;
                    into.Clear();
                    box.Trackers = true;
                    if (d == Dialect.Unknown) continue;
                    int nd = r.Vector(u, 0, out int d0);
                    for (int j = 0; j < nd; j++)
                    {
                        try { Device(r, r.Element(d0, j), d, into); }
                        catch (FormatException) { }
                    }
                    into.Sort(Order);
                }
                if (d == Dialect.Unknown) continue;
                bool v22 = d == Dialect.V22;
                if (r.Vector(u, v22 ? 1 : 2, out int b0) is var nb and > 0) Bones(r, b0, nb, d, box);
                if (r.Table(u, v22 ? 3 : 5) is var g and not 0)
                {
                    box.Guards = true;
                    if (v22) (box.YawGuard, box.MountGuard) = (r.U8(g, 0), r.U8(g, 1));
                    else (box.YawGuard, box.MountGuard) = (r.U8(g, 1) != 0 ? 0 : 1, r.U8(g, 0) != 0 ? 0 : 1);
                }
            }
            catch (FormatException) { }                 // (v21: a config not due this tick - its slot points at the message's end)
        }
        if (d == Dialect.Unknown) return;
        int m = r.Vector(root, BundleRpc, out int m0);
        for (int i = 0; i < m; i++)
        {
            try
            {
                int h = r.Element(m0, i);
                bool v22 = d == Dialect.V22;
                int type0 = r.U8(h, v22 ? 2 : 1);
                int t = r.Table(h, v22 ? 3 : 2);
                if (t == 0) continue;
                if (v22 && type0 == RpcSettings)
                {
                    (box.Settings, box.MountFeet, box.MountMethod) = (true, r.U8(t, 0) != 0, r.U8(t, 5));
                    continue;
                }
                if (type0 != RpcResetAnswer) continue;
                int type = r.U8(t, 0), st = r.U8(t, 1), progress = 0, duration = 0, phase = 0;
                if (!v22) (progress, duration) = (r.I32(t, 3), r.I32(t, 4));
                else if (r.U8(t, 3) == 1 && r.Table(t, 4) is var cd and not 0) (progress, duration) = (r.I32(cd, 0), r.I32(cd, 1));
                else if (r.U8(t, 3) == 2 && r.Table(t, 4) is var sd and not 0) phase = r.U8(sd, 0);
                ulong parts = 0;
                int np = r.Vector(t, 2, out int p0);
                for (int k = 0; k < np; k++) parts |= Bit(v22 ? Part22(r.ByteAt(p0 + k)) : Part21(r.ByteAt(p0 + k)));
                box.Reset = new ResetNews
                {
                    Has = true, Type = type, State = st == 0 ? 0 : st == 1 ? 1 : 2, LeftMs = Math.Max(0, duration - progress), Phase = phase, Parts = parts,
                    Failed = v22 && st == 3,
                };
            }
            catch (FormatException) { }
        }
    }

    // the bones of an update into box.Raw (fingers, toes and the unassigned left out); one that does not make sense - a part out of
    // range, a number not finite, a rotation not of length 1, a position kilometres off - drops them all (a newer schema's renumbering
    // draws nothing rather than garbage)
    static void Bones(FbRead r, int b0, int nb, Dialect d, Inbox box)
    {
        var raw = box.Raw;
        bool v22 = d == Dialect.V22;
        Span<float> q = stackalloc float[4], h = stackalloc float[3], t = stackalloc float[3];
        int got = 0;
        for (int k = 0; k < nb; k++)
        {
            int b = r.Element(b0, k);
            int wire = r.U8(b, 0);
            if (wire > 61) { raw.Clear(); return; }
            var part = v22 ? Part22(wire) : Part21(wire);
            if (part is SlimeVrLink.Part.None or SlimeVrLink.Part.Finger or SlimeVrLink.Part.Toe) continue;
            if (!r.Floats(b, v22 ? 2 : 1, q) || !r.Floats(b, v22 ? 4 : 3, h)) continue;
            var rot = new System.Numerics.Quaternion(q[0], q[1], q[2], q[3]);
            var head = new System.Numerics.Vector3(h[0], h[1], h[2]);
            System.Numerics.Vector3 tail;
            if (v22)
            {
                if (!r.Floats(b, 5, t)) continue;
                tail = new System.Numerics.Vector3(t[0], t[1], t[2]);
            }
            else
            {
                float len = r.F32(b, 2);
                tail = head + System.Numerics.Vector3.Transform(new System.Numerics.Vector3(0, -len, 0), System.Numerics.Quaternion.Normalize(rot));
            }
            float ql = rot.Length();
            if (!float.IsFinite(ql) || MathF.Abs(ql - 1) > 0.05f || !Sane(head) || !Sane(tail)) { raw.Clear(); return; }
            int i = (int)part;
            raw.Has[i] = true;
            raw.Rot[i] = System.Numerics.Quaternion.Normalize(rot);
            raw.Head[i] = head;
            raw.Tail[i] = tail;
            got++;
        }
        raw.Count = got;
        box.Bones = got;
    }

    static bool Sane(System.Numerics.Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && v.LengthSquared() < 1e4f;

    public static ulong Bit(SlimeVrLink.Part p) => p == SlimeVrLink.Part.None ? 0 : 1UL << (int)p;

    // the body parts of a mask in the dialect's numbers (Part21 / Part22 the other way round; a part it has no number for is left out)
    public static byte[] Wire(ulong parts, Dialect d)
    {
        var l = new List<byte>();
        for (int p = 1; p < SlimePose.Parts; p++)
        {
            if ((parts & (1UL << p)) == 0) continue;
            int w = d == Dialect.V22 ? Wire22((SlimeVrLink.Part)p) : Wire21((SlimeVrLink.Part)p);
            if (w > 0) l.Add((byte)w);
        }
        return l.ToArray();
    }

    static int Wire21(SlimeVrLink.Part p)
    {
        for (int w = 1; w <= 24; w++) if (Part21(w) == p) return w;
        return 0;
    }

    static int Wire22(SlimeVrLink.Part p)
    {
        for (int w = 1; w <= 21; w++) if (Part22(w) == p) return w;
        return 0;
    }

    // the slots of a dialect: TrackerData's info and status; TrackerInfo's body part, is_imu, display / custom name (is_hmd: -1 none);
    // HardwareStatus' ping, rssi, voltage, battery, battery runtime estimate
    readonly record struct Slots(int Info, int Status, int Part, int Imu, int Hmd, int Display, int Custom, int Ping, int Rssi, int Volt, int Battery, int Runtime);
    static readonly Slots S21 = new(1, 2, 1, 6, 11, 7, 8, 2, 3, 5, 6, 11), S22 = new(2, 3, 2, 0, -1, 6, 7, 1, 2, 6, 7, 12);

    static void Device(FbRead r, int dev, Dialect d, List<SlimeVrLink.Tracker> into)
    {
        var s = d == Dialect.V22 ? S22 : S21;
        int id = r.U8(dev, 0);                       // (v21's DeviceId struct is a uint8, v22's a uint16: its low byte)
        string name = r.Str(dev, 1) ?? "";
        int hs = r.Table(dev, 3);
        int battery = -1, rssi = int.MinValue, ping = -1, runtime = -1;
        float volt = float.NaN;
        if (hs != 0)
        {
            // (the GUI's reading: over 100 is full, over 200 a byte that was -1 - empty)
            if (r.Has(hs, s.Battery)) { int b = r.U8(hs, s.Battery); battery = b > 200 ? 0 : Math.Min(100, b); }
            if (r.Has(hs, s.Rssi)) rssi = (short)r.U16(hs, s.Rssi);
            if (r.Has(hs, s.Ping)) ping = r.U16(hs, s.Ping);
            if (r.Has(hs, s.Volt)) volt = r.F32(hs, s.Volt);
            // (the runtime estimate in microseconds; the GUIs show it when it is over 0)
            if (r.Has(hs, s.Runtime) && r.I64(hs, s.Runtime) is var us and > 0 and < 1_000_000L * 3600 * 100) runtime = (int)(us / 60_000_000);
        }
        int nt = r.Vector(dev, 4, out int t0);
        for (int k = 0; k < nt; k++)
        {
            int td = r.Element(t0, k);
            var t = new SlimeVrLink.Tracker { Device = id, DeviceName = name, Battery = battery, Rssi = rssi, Ping = ping, Voltage = volt, Status = r.U8(td, s.Status),
                ServerMin = runtime };
            if (d == Dialect.V22) t.Num = r.U16(td, 1);
            else if (r.Table(td, 0) is var tid and not 0) t.Num = r.U8(tid, 1);
            int info = r.Table(td, s.Info);
            if (info != 0)
            {
                int part = r.U8(info, s.Part);
                t.Part = d == Dialect.V22 ? Part22(part) : Part21(part);
                t.Imu = r.U8(info, s.Imu) != 0;
                t.Hmd = s.Hmd >= 0 && r.U8(info, s.Hmd) != 0;
                t.DisplayName = r.Str(info, s.Display) ?? "";
                t.CustomName = r.Str(info, s.Custom) ?? "";
            }
            if (t.Imu && !t.Hmd) into.Add(t);          // the body's trackers: not the headset, not a computed one
        }
    }

    // Does this update read as that dialect: at least one tracker, and every tracker of every device that can be read sane there (its
    // info a table with a sane vtable, a display name, a status, a body part and an is_imu in range)? Read in the other dialect, a slot
    // holding a scalar is followed as an offset and lands outside the message or on nonsense.
    static bool Strict(FbRead r, int u, Dialect d)
    {
        var s = d == Dialect.V22 ? S22 : S21;
        int trackers = 0;
        int nd = r.Vector(u, 0, out int d0);
        for (int j = 0; j < nd; j++)
        {
            int dev, nt, t0;
            try { dev = r.Element(d0, j); nt = r.Vector(dev, 4, out t0); }
            catch (FormatException) { continue; }        // (a device either dialect cannot read)
            try
            {
                for (int k = 0; k < nt; k++)
                {
                    int td = r.Element(t0, k);
                    if (!r.VtableOk(td, 15)) return false;
                    if (d == Dialect.V21 && (r.Table(td, 0) is var tid && (tid == 0 || !r.VtableOk(tid, 2)))) return false;
                    int info = r.Table(td, s.Info);
                    if (info == 0 || !r.VtableOk(info, 14) || r.U8(td, s.Status) > 7 || r.U8(info, s.Imu) > 1 || r.U8(info, s.Part) > 61) return false;
                    if (s.Hmd >= 0 && r.U8(info, s.Hmd) > 1) return false;
                    if (r.Str(info, s.Display) == null) return false;
                    trackers++;
                }
            }
            catch (FormatException) { return false; }
        }
        return trackers > 0;
    }

    public static SlimeVrLink.Part Part21(int p) => p switch
    {
        1 => SlimeVrLink.Part.Head, 2 => SlimeVrLink.Part.Neck, 3 => SlimeVrLink.Part.Chest, 4 => SlimeVrLink.Part.Waist, 5 => SlimeVrLink.Part.Hip,
        6 => SlimeVrLink.Part.LeftThigh, 7 => SlimeVrLink.Part.RightThigh, 8 => SlimeVrLink.Part.LeftAnkle, 9 => SlimeVrLink.Part.RightAnkle,
        10 => SlimeVrLink.Part.LeftFoot, 11 => SlimeVrLink.Part.RightFoot, 14 => SlimeVrLink.Part.LeftLowerArm, 15 => SlimeVrLink.Part.RightLowerArm,
        16 => SlimeVrLink.Part.LeftUpperArm, 17 => SlimeVrLink.Part.RightUpperArm, 18 => SlimeVrLink.Part.LeftHand, 19 => SlimeVrLink.Part.RightHand,
        20 => SlimeVrLink.Part.LeftShoulder, 21 => SlimeVrLink.Part.RightShoulder, 22 => SlimeVrLink.Part.UpperChest, 23 => SlimeVrLink.Part.LeftHip,
        24 => SlimeVrLink.Part.RightHip, >= 25 and <= 54 => SlimeVrLink.Part.Finger, _ => SlimeVrLink.Part.None,
    };

    public static SlimeVrLink.Part Part22(int p) => p switch
    {
        1 => SlimeVrLink.Part.Head, 2 => SlimeVrLink.Part.Neck, 3 => SlimeVrLink.Part.UpperChest, 4 => SlimeVrLink.Part.LowerChest,
        5 => SlimeVrLink.Part.UpperWaist, 6 => SlimeVrLink.Part.LowerWaist, 7 => SlimeVrLink.Part.Hip, 8 => SlimeVrLink.Part.LeftThigh,
        9 => SlimeVrLink.Part.RightThigh, 10 => SlimeVrLink.Part.LeftAnkle, 11 => SlimeVrLink.Part.RightAnkle, 12 => SlimeVrLink.Part.LeftFoot,
        13 => SlimeVrLink.Part.RightFoot, 14 => SlimeVrLink.Part.LeftUpperArm, 15 => SlimeVrLink.Part.RightUpperArm, 16 => SlimeVrLink.Part.LeftLowerArm,
        17 => SlimeVrLink.Part.RightLowerArm, 18 => SlimeVrLink.Part.LeftHand, 19 => SlimeVrLink.Part.RightHand, 20 => SlimeVrLink.Part.LeftShoulder,
        21 => SlimeVrLink.Part.RightShoulder, >= 22 and <= 51 => SlimeVrLink.Part.Finger, >= 52 and <= 61 => SlimeVrLink.Part.Toe, _ => SlimeVrLink.Part.None,
    };

    // head to feet, the arms after, the fingers and toes, then the trackers not assigned to a part
    static readonly Comparison<SlimeVrLink.Tracker> Order = (a, b) =>
    {
        int c = Rank(a.Part).CompareTo(Rank(b.Part));
        if (c != 0) return c;
        c = a.Device.CompareTo(b.Device);
        return c != 0 ? c : a.Num.CompareTo(b.Num);
    };

    static int Rank(SlimeVrLink.Part p) => p == SlimeVrLink.Part.None ? 99 : (int)p;
}

// Reading a FlatBuffers message: positions in it, every one checked against its length (outside: FormatException).
readonly ref struct FbRead
{
    readonly ReadOnlySpan<byte> _b;
    public FbRead(ReadOnlySpan<byte> b) => _b = b;

    void Need(long at, int n)
    {
        if (at < 0 || at + n > _b.Length) throw new FormatException($"an offset outside the message ({at} + {n} of {_b.Length})");
    }

    int U16At(long at) { Need(at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(_b.Slice((int)at)); }
    int I32At(long at) { Need(at, 4); return BinaryPrimitives.ReadInt32LittleEndian(_b.Slice((int)at)); }

    // where a uoffset at 'at' points
    int Follow(long at)
    {
        long t = at + (uint)I32At(at);
        Need(t, 4);
        return (int)t;
    }

    // the root table
    public int Root() => Follow(0);

    // a field's position in a table (0: not there)
    public int Field(int table, int id)
    {
        long vt = (long)table - I32At(table);
        int size = U16At(vt), o = 4 + 2 * id;
        if (o + 2 > size) return 0;
        int f = U16At(vt + o);
        if (f == 0) return 0;
        Need((long)table + f, 1);
        return table + f;
    }

    // a table's vtable looks like one (even, 4 bytes or more, not more fields than 'fields' and some a newer schema may add; its table
    // at least the soffset)
    public bool VtableOk(int table, int fields)
    {
        long vt = (long)table - I32At(table);
        int size = U16At(vt), tsize = U16At(vt + 2);
        return size >= 4 && size % 2 == 0 && size <= 4 + 2 * (fields + 8) && tsize >= 4;
    }

    public bool Has(int table, int id) => Field(table, id) != 0;

    // a table / string / vector field: where it points (0: not there)
    public int Table(int table, int id)
    {
        int f = Field(table, id);
        return f == 0 ? 0 : Follow(f);
    }

    // a vector field: its length (0: not there) and its first element's position
    public int Vector(int table, int id, out int first)
    {
        first = 0;
        int v = Table(table, id);
        if (v == 0) return 0;
        int n = I32At(v);
        if (n < 0) throw new FormatException("a vector's length below 0");
        Need((long)v + 4, n);           // (each element at least a byte)
        first = v + 4;
        return n;
    }

    // a vector of tables' i-th table
    public int Element(int first, int i) => Follow(first + 4L * i);

    public int U8(int table, int id, int def = 0)
    {
        int f = Field(table, id);
        return f == 0 ? def : _b[f];
    }

    public int U16(int table, int id, int def = 0)
    {
        int f = Field(table, id);
        return f == 0 ? def : U16At(f);
    }

    public int I32(int table, int id, int def = 0)
    {
        int f = Field(table, id);
        return f == 0 ? def : I32At(f);
    }

    public float F32(int table, int id, float def = 0)
    {
        int f = Field(table, id);
        return f == 0 ? def : BitConverter.Int32BitsToSingle(I32At(f));
    }

    public long I64(int table, int id, long def = 0)
    {
        int f = Field(table, id);
        if (f == 0) return def;
        Need(f, 8);
        return BinaryPrimitives.ReadInt64LittleEndian(_b.Slice(f));
    }

    // a struct of floats inline in a table (a Quat, a Vec3f): its 'into.Length' floats (false: not there)
    public bool Floats(int table, int id, Span<float> into)
    {
        int f = Field(table, id);
        if (f == 0) return false;
        Need(f, 4 * into.Length);
        for (int i = 0; i < into.Length; i++) into[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_b.Slice(f + 4 * i)));
        return true;
    }

    // a byte at a position (a vector of ubytes' element: Vector checked its length)
    public int ByteAt(int at) { Need(at, 1); return _b[at]; }

    public string Str(int table, int id)
    {
        int s = Table(table, id);
        if (s == 0) return null;
        int n = I32At(s);
        if (n < 0) throw new FormatException("a string's length below 0");
        Need((long)s + 4, n);
        return n == 0 ? "" : Encoding.UTF8.GetString(_b.Slice(s + 4, n));
    }
}

// Building a FlatBuffers message: a table's fields by id (scalars, or what an offset points to - a table, a vector of tables, a string,
// bytes), then Finish lays it out front to back - each table's vtable just before it, what it points to after it (a uoffset only points
// forward), every value on its own size. Valid for any FlatBuffers reader (they follow the offsets; where things sit is free).
sealed class FbObj
{
    internal readonly List<(int Id, int Size, ulong Bits, object Ref, int Landing)> Fields = new();

    FbObj Add(int id, int size, ulong bits, object r, int landing = 0) { Fields.Add((id, size, bits, r, landing)); return this; }
    public FbObj U8(int id, int v) => Add(id, 1, (byte)v, null);
    public FbObj Bool(int id, bool v) => Add(id, 1, v ? 1UL : 0, null);
    public FbObj U16(int id, int v) => Add(id, 2, (ushort)v, null);
    public FbObj I16(int id, int v) => Add(id, 2, (ushort)(short)v, null);
    public FbObj I32(int id, int v) => Add(id, 4, (uint)v, null);
    public FbObj F32(int id, float v) => Add(id, 4, BitConverter.SingleToUInt32Bits(v), null);
    public FbObj I64(int id, long v) => Add(id, 8, (ulong)v, null);
    // landing: the table is placed so that its offset from this field is a multiple of it (V21's reset header: SolarXr)
    public FbObj Obj(int id, FbObj t, int landing = 0) => Add(id, 4, 0, t, landing);
    // (a null element: a slot pointing at the message's end - v21's server writes one for a feed not due: the tools' made-up server)
    public FbObj Objs(int id, params FbObj[] t) => Add(id, 4, 0, t);
    public FbObj Str(int id, string s) => Add(id, 4, 0, s ?? "");
    public FbObj Bytes(int id, byte[] b) => Add(id, 4, 0, b);
    // a struct of floats inline (a Quat: 4, a Vec3f: 3), on 4 bytes with the other 4-byte fields
    public FbObj Floats(int id, params float[] v) => Add(id, 4 * v.Length, 0, v);

    public byte[] Finish()
    {
        var w = new Writer();
        w.Reserve(4);                       // the root's uoffset
        int t = w.Table(this);
        w.Put(0, 4, (uint)t);
        foreach (int slot in w.Dangling) w.Put(slot, 4, (uint)(w.Length - slot));
        return w.ToArray();
    }

    sealed class Writer
    {
        byte[] _b = new byte[256];
        int _n;
        public readonly List<int> Dangling = new();
        public int Length => _n;

        public int Reserve(int n)
        {
            if (_n + n > _b.Length) Array.Resize(ref _b, Math.Max(_b.Length * 2, _n + n));
            int at = _n;
            _n += n;
            return at;
        }

        void Pad(int align) { while (_n % align != 0) Reserve(1); }

        public void Put(int at, int size, ulong bits)
        {
            for (int i = 0; i < size; i++) _b[at + i] = (byte)(bits >> (8 * i));
        }

        public byte[] ToArray() => _b.AsSpan(0, _n).ToArray();

        // from / landing: the table's position minus 'from' must be a multiple of 'landing' (0: anywhere)
        public int Table(FbObj o, int from = 0, int landing = 0)
        {
            var f = o.Fields;
            int maxId = -1;
            foreach (var x in f) maxId = Math.Max(maxId, x.Id);
            // the inline part: the soffset, then the fields from the biggest down, each on its own size
            var off = new int[f.Count];
            int pos = 4;
            foreach (int size in new[] { 8, 4, 2, 1 })
                for (int i = 0; i < f.Count; i++)
                {
                    bool inline = f[i].Ref is float[];
                    if (inline ? size != 4 : f[i].Size != size) continue;
                    pos = (pos + size - 1) / size * size;
                    off[i] = pos;
                    pos += f[i].Size;
                }
            int vsize = 4 + 2 * (maxId + 1), align = f.Exists(x => x.Size == 8) ? 8 : 4;
            Pad(2);
            if (landing > 0)
            {
                int p = 0;
                while ((((_n + p + vsize + align - 1) / align * align) - from) % landing != 0)
                    if ((p += 2) > landing + 8) throw new InvalidOperationException("no place for a table landing there");
                Reserve(p);
            }
            int vt = Reserve(vsize);
            Put(vt, 2, (ulong)vsize);
            Put(vt + 2, 2, (ulong)pos);
            for (int i = 0; i < f.Count; i++) Put(vt + 4 + 2 * f[i].Id, 2, (ulong)off[i]);
            Pad(align);
            int t = Reserve(pos);
            Put(t, 4, (uint)(t - vt));                    // the vtable is at t - soffset
            for (int i = 0; i < f.Count; i++) if (f[i].Ref == null) Put(t + off[i], f[i].Size, f[i].Bits);
            for (int i = 0; i < f.Count; i++)
                if (f[i].Ref is float[] fl)
                    for (int k = 0; k < fl.Length; k++) Put(t + off[i] + 4 * k, 4, BitConverter.SingleToUInt32Bits(fl[k]));
            for (int i = 0; i < f.Count; i++)
            {
                if (f[i].Ref == null || f[i].Ref is float[]) continue;
                int at = t + off[i];
                int child = f[i].Ref switch
                {
                    FbObj c => Table(c, at, f[i].Landing),
                    FbObj[] cs => Vector(cs),
                    string s => Bytes(Encoding.UTF8.GetBytes(s), true),
                    byte[] b => Bytes(b, false),
                    _ => throw new ArgumentException("a field FlatBuffers has no form for"),
                };
                Put(at, 4, (uint)(child - at));
            }
            return t;
        }

        int Vector(FbObj[] cs)
        {
            Pad(4);
            int v = Reserve(4 + 4 * cs.Length);
            Put(v, 4, (uint)cs.Length);
            for (int i = 0; i < cs.Length; i++)
            {
                int slot = v + 4 + 4 * i;
                if (cs[i] == null) { Dangling.Add(slot); continue; }
                int c = Table(cs[i]);
                Put(slot, 4, (uint)(c - slot));
            }
            return v;
        }

        // a string (its terminating 0 after it) or a vector of bytes
        int Bytes(byte[] b, bool text)
        {
            Pad(4);
            int at = Reserve(4 + b.Length + (text ? 1 : 0));
            Put(at, 4, (uint)b.Length);
            b.CopyTo(_b, at + 4);
            return at;
        }
    }
}
