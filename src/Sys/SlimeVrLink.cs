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
// (an RpcMessage ResetRequest: the type, every tracker; v22's with the GUI's delays), its answers (how long, done) come back on the same
// socket. All of it on a thread of its own: the watch takes immutable snapshots (a Version each), nothing is made on a frame. Nothing of
// SlimeVR's is written or changed but what a reset does.
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

        public bool Charging => Voltage > 4.3f;            // (the GUI's rule: a charging tracker reads over 4.3 V)
        public bool Connected => Status is Ok or Busy or Error or Occluded;

        public bool Same(Tracker o) =>
            o != null && Device == o.Device && Num == o.Num && Part == o.Part && Battery == o.Battery && Rssi == o.Rssi && Ping == o.Ping &&
            Status == o.Status && Charging == o.Charging && CustomName == o.CustomName && DisplayName == o.DisplayName && DeviceName == o.DeviceName;
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
    }

    public readonly int Port;
    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    readonly ConcurrentQueue<ResetKind> _resets = new();
    Thread _thread;
    volatile bool _stop, _active;
    Snapshot _pub = new();
    int _version, _sent;

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

    // the resets sent so far (the tools' count)
    public int Sent => Volatile.Read(ref _sent);

    // A reset, sent on the socket by the link's thread; false: not connected, or the server's dialect not known yet (nothing is sent)
    public bool Reset(ResetKind k)
    {
        var s = Get();
        if (!_active || s.State != State.Ok || s.Dialect == SolarXr.Dialect.Unknown) return false;
        _resets.Enqueue(k);
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
        if (reset.Has) (s.ResetKind, s.ResetState, s.ResetLeftMs, s.ResetAt) = (reset.Type, reset.State, reset.LeftMs, Clock.Now);
        else (s.ResetKind, s.ResetState, s.ResetLeftMs, s.ResetAt) = (old.ResetKind, old.ResetState, old.ResetLeftMs, old.ResetAt);
        s.Version = ++_version;
        lock (_lock) _pub = s;
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
            System.Threading.Tasks.Task<WebSocketReceiveResult> recv = null;
            try
            {
                using var ws = new ClientWebSocket();
                using (var cts = new CancellationTokenSource(2000))
                    ws.ConnectAsync(uri, cts.Token).GetAwaiter().GetResult();
                ws.SendAsync(SolarXr.StartFeed(FeedMs), WebSocketMessageType.Binary, true, CancellationToken.None).Wait(2000);
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
                        var news = default(SolarXr.ResetNews);
                        bool feed = false;
                        var was = dialect;
                        if (r.MessageType == WebSocketMessageType.Binary)
                        {
                            try { feed = SolarXr.Read(buf.AsSpan(0, have), got, ref news, ref dialect); }
                            catch (FormatException e) { Log.Info("slimevr: a message not read: " + e.Message); }
                        }
                        have = 0;
                        if (dialect != was) Log.Info($"slimevr: the server speaks SolarXR {SolarXr.Name(dialect)}");
                        if (feed)
                        {
                            last = Clock.Now;
                            if (first && dialect != SolarXr.Dialect.Unknown) Log.Info($"slimevr: {got.Count} tracker(s) on the body");
                            if (dialect != SolarXr.Dialect.Unknown) first = false;
                        }
                        if (news.Has)
                            Log.Info($"slimevr: reset {(ResetKind)news.Type} {news.State switch { 0 => $"running, {news.LeftMs} ms left", 1 => "done", _ => "cancelled" }}");
                        if (news.Has || (feed && (_pub.State != State.Ok || _pub.Dialect != dialect || !SameTrackers(got, _pub.Trackers))))
                            Publish(feed ? State.Ok : _pub.State, feed ? got : null, dialect, news);
                    }
                    while (_resets.TryDequeue(out var k))
                    {
                        ws.SendAsync(SolarXr.ResetRequest(k, dialect), WebSocketMessageType.Binary, true, CancellationToken.None).Wait(2000);
                        Interlocked.Increment(ref _sent);
                        Log.Info($"slimevr: {k} reset sent");
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
            if (_pub.State != State.NotRunning) Publish(State.NotRunning, null, SolarXr.Dialect.Unknown);
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
public static class SolarXr
{
    public enum Dialect { Unknown, V21, V22 }

    public static string Name(Dialect d) => d switch { Dialect.V21 => "as SlimeVR up to v21", Dialect.V22 => "as SlimeVR v22", _ => "(not known yet)" };

    public const int BundleFeed = 0, BundleRpc = 1;
    public const int FeedStart = 2, FeedUpdate = 3;
    public const int RpcReset = 3, RpcResetAnswer = 4;

    // a reset's answer: its type, 0 running / 1 done / 2 cancelled or failed, the ms left
    public struct ResetNews { public bool Has; public int Type, State, LeftMs; }

    // StartDataFeed: one feed - the devices' hardware status (device_data) and their trackers' info and status, at most every 'ms'
    public static byte[] StartFeed(int ms) => new FbObj()
        .Objs(BundleFeed, new FbObj().U8(0, FeedStart).Obj(1, new FbObj()
            .Objs(0, new FbObj().U16(0, ms).Obj(1, new FbObj()
                .Obj(0, new FbObj().Bool(0, true).Bool(1, true))
                .Bool(1, true)))))
        .Finish();

    // ResetRequest, as that server's GUI sends it: the type, no body parts (every tracker); v22's GUI gives the delay (yaw 0 s, full and
    // mounting 3 s - its server takes none as 0), v21's leaves it out (its server's own delays)
    public static byte[] ResetRequest(SlimeVrLink.ResetKind k, Dialect d)
    {
        var req = new FbObj().U8(0, (int)k);
        if (d == Dialect.V22) return new FbObj().Objs(BundleRpc, new FbObj().U8(2, RpcReset).Obj(3, req.F32(2, k == SlimeVrLink.ResetKind.Yaw ? 0 : 3))).Finish();
        return new FbObj().Objs(BundleRpc, new FbObj().U8(1, RpcReset).Obj(2, req, landing: 256)).Finish();
    }

    // A message from the server: its data feed update's body trackers into 'into', head to feet (true: it had one), a reset's answer into
    // 'reset'. d: the server's dialect - read from the first update with a tracker in it (until then nothing is listed). A device that
    // cannot be read is left out (v21 writes a 0 offset for one without trackers); a broken bundle throws FormatException.
    public static bool Read(ReadOnlySpan<byte> msg, List<SlimeVrLink.Tracker> into, ref ResetNews reset, ref Dialect d)
    {
        var r = new FbRead(msg);
        int root = r.Root();
        bool feed = false;
        int n = r.Vector(root, BundleFeed, out int h0);
        for (int i = 0; i < n; i++)
        {
            int h = r.Element(h0, i);
            if (r.U8(h, 0) != FeedUpdate) continue;
            int u = r.Table(h, 1);
            if (u == 0) continue;
            if (d == Dialect.Unknown) d = Strict(r, u, Dialect.V22) ? Dialect.V22 : Strict(r, u, Dialect.V21) ? Dialect.V21 : Dialect.Unknown;
            into.Clear();
            feed = true;
            if (d == Dialect.Unknown) continue;
            int nd = r.Vector(u, 0, out int d0);
            for (int j = 0; j < nd; j++)
            {
                try { Device(r, r.Element(d0, j), d, into); }
                catch (FormatException) { }
            }
            into.Sort(Order);
        }
        if (d == Dialect.Unknown) return feed;
        int m = r.Vector(root, BundleRpc, out int m0);
        for (int i = 0; i < m; i++)
        {
            int h = r.Element(m0, i);
            bool v22 = d == Dialect.V22;
            if (r.U8(h, v22 ? 2 : 1) != RpcResetAnswer) continue;
            int t = r.Table(h, v22 ? 3 : 2);
            if (t == 0) continue;
            int type = r.U8(t, 0), st = r.U8(t, 1), progress = 0, duration = 0;
            if (!v22) (progress, duration) = (r.I32(t, 3), r.I32(t, 4));
            else if (r.U8(t, 3) == 1 && r.Table(t, 4) is var cd and not 0) (progress, duration) = (r.I32(cd, 0), r.I32(cd, 1));
            reset = new ResetNews { Has = true, Type = type, State = st == 0 ? 0 : st == 1 ? 1 : 2, LeftMs = Math.Max(0, duration - progress) };
        }
        return feed;
    }

    // the slots of a dialect: TrackerData's info and status; TrackerInfo's body part, is_imu, display / custom name (is_hmd: -1 none);
    // HardwareStatus' ping, rssi, voltage, battery
    readonly record struct Slots(int Info, int Status, int Part, int Imu, int Hmd, int Display, int Custom, int Ping, int Rssi, int Volt, int Battery);
    static readonly Slots S21 = new(1, 2, 1, 6, 11, 7, 8, 2, 3, 5, 6), S22 = new(2, 3, 2, 0, -1, 6, 7, 1, 2, 6, 7);

    static void Device(FbRead r, int dev, Dialect d, List<SlimeVrLink.Tracker> into)
    {
        var s = d == Dialect.V22 ? S22 : S21;
        int id = r.U8(dev, 0);                       // (v21's DeviceId struct is a uint8, v22's a uint16: its low byte)
        string name = r.Str(dev, 1) ?? "";
        int hs = r.Table(dev, 3);
        int battery = -1, rssi = int.MinValue, ping = -1;
        float volt = float.NaN;
        if (hs != 0)
        {
            // (the GUI's reading: over 100 is full, over 200 a byte that was -1 - empty)
            if (r.Has(hs, s.Battery)) { int b = r.U8(hs, s.Battery); battery = b > 200 ? 0 : Math.Min(100, b); }
            if (r.Has(hs, s.Rssi)) rssi = (short)r.U16(hs, s.Rssi);
            if (r.Has(hs, s.Ping)) ping = r.U16(hs, s.Ping);
            if (r.Has(hs, s.Volt)) volt = r.F32(hs, s.Volt);
        }
        int nt = r.Vector(dev, 4, out int t0);
        for (int k = 0; k < nt; k++)
        {
            int td = r.Element(t0, k);
            var t = new SlimeVrLink.Tracker { Device = id, DeviceName = name, Battery = battery, Rssi = rssi, Ping = ping, Voltage = volt, Status = r.U8(td, s.Status) };
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

    static SlimeVrLink.Part Part21(int p) => p switch
    {
        1 => SlimeVrLink.Part.Head, 2 => SlimeVrLink.Part.Neck, 3 => SlimeVrLink.Part.Chest, 4 => SlimeVrLink.Part.Waist, 5 => SlimeVrLink.Part.Hip,
        6 => SlimeVrLink.Part.LeftThigh, 7 => SlimeVrLink.Part.RightThigh, 8 => SlimeVrLink.Part.LeftAnkle, 9 => SlimeVrLink.Part.RightAnkle,
        10 => SlimeVrLink.Part.LeftFoot, 11 => SlimeVrLink.Part.RightFoot, 14 => SlimeVrLink.Part.LeftLowerArm, 15 => SlimeVrLink.Part.RightLowerArm,
        16 => SlimeVrLink.Part.LeftUpperArm, 17 => SlimeVrLink.Part.RightUpperArm, 18 => SlimeVrLink.Part.LeftHand, 19 => SlimeVrLink.Part.RightHand,
        20 => SlimeVrLink.Part.LeftShoulder, 21 => SlimeVrLink.Part.RightShoulder, 22 => SlimeVrLink.Part.UpperChest, 23 => SlimeVrLink.Part.LeftHip,
        24 => SlimeVrLink.Part.RightHip, >= 25 and <= 54 => SlimeVrLink.Part.Finger, _ => SlimeVrLink.Part.None,
    };

    static SlimeVrLink.Part Part22(int p) => p switch
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
    public FbObj Objs(int id, params FbObj[] t) => Add(id, 4, 0, t);
    public FbObj Str(int id, string s) => Add(id, 4, 0, s ?? "");
    public FbObj Bytes(int id, byte[] b) => Add(id, 4, 0, b);

    public byte[] Finish()
    {
        var w = new Writer();
        w.Reserve(4);                       // the root's uoffset
        int t = w.Table(this);
        w.Put(0, 4, (uint)t);
        return w.ToArray();
    }

    sealed class Writer
    {
        byte[] _b = new byte[256];
        int _n;

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
                    if (f[i].Size != size) continue;
                    pos = (pos + size - 1) / size * size;
                    off[i] = pos;
                    pos += size;
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
            {
                if (f[i].Ref == null) continue;
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
                int c = Table(cs[i]), slot = v + 4 + 4 * i;
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
