using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Orbit;

// --snapshot's part for the SlimeVR widget (Sys/SlimeVrLink.cs, WristSlimeVr.cs): made-up trackers only, and the link checked against a
// made-up SolarXR server of the tool's own - a TcpListener on a free port of 127.0.0.1 speaking the WebSocket handshake, never SlimeVR's
// 21110 (SlimeVR's server is never connected to, nothing is sent to it). The server answers in either dialect, its updates made with the
// link's own builder; the reader is also run on two byte samples made the server's way (SlimeSamples).
static class SlimeVrViewTools
{
    static bool Want(string line, bool ok) => Tools.Want(line, ok);

    // Erik's six (10-24): the chest, the hip (charging), both thighs and ankles - one amber, one red
    public static SlimeVrLink.Snapshot Fake(bool trouble = false)
    {
        var s = new SlimeVrLink.Snapshot { Version = trouble ? 2 : 1, State = SlimeVrLink.State.Ok, Dialect = SolarXr.Dialect.V22 };
        void T(SlimeVrLink.Part p, int dev, int bat, int rssi, int ping, int status = SlimeVrLink.Ok, float volt = 3.9f) =>
            s.Trackers.Add(new SlimeVrLink.Tracker { Device = dev, Num = dev, Part = p, Battery = bat, Rssi = rssi, Ping = ping, Status = status, Voltage = volt, Imu = true,
                DisplayName = $"udp://192.168.1.{120 + dev}/0" });
        T(SlimeVrLink.Part.Chest, 1, 87, -48, 12);
        T(SlimeVrLink.Part.Hip, 2, 64, -55, 9, volt: 4.36f);
        T(SlimeVrLink.Part.LeftThigh, 3, 41, -61, 14);
        T(SlimeVrLink.Part.RightThigh, 4, 52, -58, 11);
        T(SlimeVrLink.Part.LeftAnkle, 5, 23, -72, 25, trouble ? SlimeVrLink.Busy : SlimeVrLink.Ok);
        T(SlimeVrLink.Part.RightAnkle, 6, 12, -83, 41, trouble ? SlimeVrLink.Disconnected : SlimeVrLink.Ok);
        foreach (var t in s.Trackers) if (t.Connected) s.Connected++;
        return s;
    }

    public static SlimeVrLink.Snapshot None() => new() { Version = 3, State = SlimeVrLink.State.NotRunning };

    // ---- the server's side, made with the link's own builder (FbObj): an update in either dialect, a reset's answer ----

    public readonly record struct FakeTracker(SlimeVrLink.Part Part, int Battery, int Rssi, int Ping, int Status, string Custom = null, float Volt = 3.9f);

    static readonly FakeTracker[] Six =
    {
        new(SlimeVrLink.Part.UpperChest, 87, -48, 12, SlimeVrLink.Ok), new(SlimeVrLink.Part.Hip, 64, -55, 9, SlimeVrLink.Ok, Volt: 4.36f),
        new(SlimeVrLink.Part.LeftThigh, 41, -61, 14, SlimeVrLink.Ok), new(SlimeVrLink.Part.RightThigh, 52, -58, 11, SlimeVrLink.Ok),
        new(SlimeVrLink.Part.LeftAnkle, 23, -72, 25, SlimeVrLink.Ok), new(SlimeVrLink.Part.RightAnkle, 12, -83, 41, SlimeVrLink.Ok, Custom: "Right foot strap"),
    };

    // the BodyPart numbers of each dialect
    static int Part22(SlimeVrLink.Part p) => p switch
    {
        SlimeVrLink.Part.Head => 1, SlimeVrLink.Part.UpperChest => 3, SlimeVrLink.Part.Hip => 7, SlimeVrLink.Part.LeftThigh => 8, SlimeVrLink.Part.RightThigh => 9,
        SlimeVrLink.Part.LeftAnkle => 10, SlimeVrLink.Part.RightAnkle => 11, _ => 0,
    };

    static int Part21(SlimeVrLink.Part p) => p switch
    {
        SlimeVrLink.Part.Head => 1, SlimeVrLink.Part.UpperChest => 22, SlimeVrLink.Part.Hip => 5, SlimeVrLink.Part.LeftThigh => 6, SlimeVrLink.Part.RightThigh => 7,
        SlimeVrLink.Part.LeftAnkle => 8, SlimeVrLink.Part.RightAnkle => 9, _ => 0,
    };

    // a DataFeedUpdate as the server sends it: the headset first (not an IMU), then a board per tracker; bump: every battery that much lower
    public static byte[] Update(SolarXr.Dialect d, FakeTracker[] trackers, int bump = 0)
    {
        var devices = new List<FbObj> { Device(d, 0, new FakeTracker(SlimeVrLink.Part.Head, -1, int.MinValue, -1, SlimeVrLink.Ok), true, "HMD") };
        for (int i = 0; i < trackers.Length; i++)
            devices.Add(Device(d, i + 1, trackers[i] with { Battery = Math.Max(0, trackers[i].Battery - bump) }, false, $"udp://192.168.1.{121 + i}/0"));
        return new FbObj().Objs(SolarXr.BundleFeed, new FbObj().U8(0, SolarXr.FeedUpdate).Obj(1, new FbObj().Objs(0, devices.ToArray()))).Finish();
    }

    static FbObj Device(SolarXr.Dialect d, int id, FakeTracker t, bool hmd, string display)
    {
        bool v22 = d == SolarXr.Dialect.V22;
        var hs = new FbObj().U8(0, 1);
        if (t.Battery >= 0) { hs.U8(v22 ? 7 : 6, t.Battery); hs.F32(v22 ? 6 : 5, t.Volt); }
        if (t.Rssi != int.MinValue) hs.I16(v22 ? 2 : 3, t.Rssi);
        if (t.Ping >= 0) hs.U16(v22 ? 1 : 2, t.Ping);
        FbObj info, td;
        if (v22)
        {
            info = new FbObj().Bool(0, !hmd).U8(2, Part22(t.Part)).U8(3, Part22(t.Part)).Str(6, display);
            if (t.Custom != null) info.Str(7, t.Custom);
            td = new FbObj().U16(0, id).U16(1, id).Obj(2, info).U8(3, t.Status);
        }
        else
        {
            info = new FbObj().U8(1, Part21(t.Part)).Bool(4, true).Bool(6, !hmd).Str(7, display).Bool(11, hmd);
            if (t.Custom != null) info.Str(8, t.Custom);
            td = new FbObj().Obj(0, new FbObj().U8(0, id).U8(1, 0)).Obj(1, info).U8(2, t.Status);
        }
        return (v22 ? new FbObj().U16(0, id) : new FbObj().U8(0, id)).Obj(3, hs).Objs(4, td);      // (v21's id: the DeviceId struct, a uint8)
    }

    // a reset's answer: state 0 running / 1 done, its countdown
    public static byte[] Answer(SolarXr.Dialect d, int type, int state, int progress, int duration) => d == SolarXr.Dialect.V22
        ? new FbObj().Objs(SolarXr.BundleRpc, new FbObj().U8(2, SolarXr.RpcResetAnswer).Obj(3, new FbObj().U8(0, type).U8(1, state).U8(3, 1)
            .Obj(4, new FbObj().I32(0, progress).I32(1, duration)))).Finish()
        : new FbObj().Objs(SolarXr.BundleRpc, new FbObj().U8(1, SolarXr.RpcResetAnswer).Obj(2, new FbObj().U8(0, type).U8(1, state).I32(3, progress).I32(4, duration))).Finish();

    // what a client's message asks: its StartDataFeed's first feed (ms, device_data, tracker info, status, rotation), its ResetRequests in
    // dialect d (type, delay or NaN, the body parts given)
    public static (bool start, int ms, bool device, bool info, bool status, bool rotation) ReadStart(byte[] msg)
    {
        var r = new FbRead(msg);
        int root = r.Root(), n = r.Vector(root, SolarXr.BundleFeed, out int h0);
        for (int i = 0; i < n; i++)
        {
            int h = r.Element(h0, i);
            if (r.U8(h, 0) != SolarXr.FeedStart) continue;
            int sdf = r.Table(h, 1);
            if (r.Vector(sdf, 0, out int c0) < 1) continue;
            int cfg = r.Element(c0, 0), mask = r.Table(cfg, 1), tm = mask == 0 ? 0 : r.Table(mask, 0);
            return (true, r.U16(cfg, 0), mask != 0 && r.U8(mask, 1) != 0, tm != 0 && r.U8(tm, 0) != 0, tm != 0 && r.U8(tm, 1) != 0, tm != 0 && r.U8(tm, 2) != 0);
        }
        return default;
    }

    public static List<(int type, float delay, int parts)> ReadResets(byte[] msg, SolarXr.Dialect d)
    {
        var l = new List<(int, float, int)>();
        var r = new FbRead(msg);
        int root = r.Root(), n = r.Vector(root, SolarXr.BundleRpc, out int h0);
        bool v22 = d == SolarXr.Dialect.V22;
        for (int i = 0; i < n; i++)
        {
            int h = r.Element(h0, i);
            if (r.U8(h, v22 ? 2 : 1) != SolarXr.RpcReset) continue;
            int t = r.Table(h, v22 ? 3 : 2);
            if (t == 0) continue;
            l.Add((r.U8(t, 0), r.Has(t, 2) ? r.F32(t, 2) : float.NaN, r.Vector(t, 1, out _)));
        }
        return l;
    }

    // ---- the made-up server ----

    public sealed class FakeServer : IDisposable
    {
        readonly TcpListener _l = new(IPAddress.Loopback, 0);
        readonly Thread _t;
        readonly object _lock = new();
        readonly List<byte[]> _got = new();
        volatile bool _stop;
        public readonly int Port;
        public readonly SolarXr.Dialect Dialect;
        public Func<int, byte[]> Feed;               // the n-th update
        public int Connections, Updates;

        public FakeServer(SolarXr.Dialect d, Func<int, byte[]> feed)
        {
            (Dialect, Feed) = (d, feed);
            _l.Start();
            Port = ((IPEndPoint)_l.LocalEndpoint).Port;
            _t = new Thread(Run) { IsBackground = true, Name = "made-up solarxr" };
            _t.Start();
        }

        public List<byte[]> Got() { lock (_lock) return new List<byte[]>(_got); }

        public int Resets(out (int type, float delay, int parts) last)
        {
            last = default;
            int n = 0;
            foreach (var m in Got())
                foreach (var x in ReadResets(m, Dialect)) { n++; last = x; }
            return n;
        }

        void Run()
        {
            while (!_stop)
            {
                TcpClient c;
                try { c = _l.AcceptTcpClient(); }
                catch { return; }
                try { Serve(c); }
                catch (Exception e) { if (!_stop) Log.Info("slimevr made-up server: " + e.Message); }
                finally { c.Dispose(); }
            }
        }

        void Serve(TcpClient c)
        {
            var s = c.GetStream();
            // the handshake (RFC 6455): the request's headers, Sec-WebSocket-Key answered with Sec-WebSocket-Accept
            var head = new StringBuilder();
            var one = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (s.Read(one, 0, 1) != 1 || head.Length > 8192) return;
                head.Append((char)one[0]);
            }
            string key = head.ToString().Split("\r\n").FirstOrDefault(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))?[18..].Trim();
            if (key == null) return;
            string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            s.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
            using var ws = WebSocket.CreateFromStream(s, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
            Interlocked.Increment(ref Connections);
            var buf = new byte[64 * 1024];
            int feedMs = -1, n = 0;
            double next = 0;
            System.Threading.Tasks.Task<WebSocketReceiveResult> recv = null;
            while (!_stop && ws.State == WebSocketState.Open)
            {
                recv ??= ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                if (recv.Wait(20))
                {
                    var r = recv.Result;
                    recv = null;
                    if (r.MessageType == WebSocketMessageType.Close) { ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).Wait(500); break; }
                    var msg = buf.AsSpan(0, r.Count).ToArray();
                    lock (_lock) _got.Add(msg);
                    var st = ReadStart(msg);
                    if (st.start) (feedMs, next) = (st.ms, Clock.Now);           // as the server: the feed from now on, every minimum_time_since_last
                    foreach (var (type, _, _) in ReadResets(msg, Dialect))       // a reset: answered as the server does (a yaw done at once)
                        ws.SendAsync(Answer(Dialect, type, type == 0 ? 1 : 0, 0, type == 0 ? 0 : 3000), WebSocketMessageType.Binary, true, CancellationToken.None).Wait(1000);
                }
                if (feedMs >= 0 && Clock.Now >= next)
                {
                    ws.SendAsync(Feed(n++), WebSocketMessageType.Binary, true, CancellationToken.None).Wait(1000);
                    Interlocked.Increment(ref Updates);
                    next = Clock.Now + Math.Max(feedMs, 20) / 1000.0;
                }
            }
            try { recv?.Wait(200); } catch { }
        }

        public void Dispose()
        {
            _stop = true;
            _l.Stop();
            _t.Join(3000);
        }
    }

    static bool Until(Func<bool> ok, double seconds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ok()) { if (sw.Elapsed.TotalSeconds > seconds) return false; Thread.Sleep(20); }
        return true;
    }

    static string Rows(SlimeVrLink.Snapshot s) => string.Join(", ", s.Trackers.Select(t =>
        $"{(t.CustomName.Length > 0 ? t.CustomName : t.Part.ToString())} {t.Battery}%{(t.Charging ? "+" : "")} {(t.Rssi == int.MinValue ? "-" : t.Rssi.ToString())}dBm {t.Ping}ms s{t.Status}"));

    static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    // ---- the checks ----

    public static void DataChecks(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir)
    {
        // the server's own bytes: the reader on the two samples (SlimeSamples), which dialect each is, the headset left out
        foreach (var (name, feed, answer, want) in new[]
        {
            ("v22", SlimeSamples.Feed22, SlimeSamples.Reset22, "UpperChest 87% -48dBm 12ms s2, Belt 100%+ -60dBm 8ms s1, LeftThigh 23% -71dBm 30ms s2, LeftAnkle 23% -71dBm 30ms s3"),
            ("v21", SlimeSamples.Feed21, SlimeSamples.Reset21, "Chest 87% -48dBm 12ms s2, Belt 100%+ -60dBm 8ms s1, LeftThigh 23% -71dBm 30ms s2, LeftAnkle 23% -71dBm 30ms s3"),
        })
        {
            try
            {
                var got = new List<SlimeVrLink.Tracker>();
                var news = default(SolarXr.ResetNews);
                var d = SolarXr.Dialect.Unknown;
                bool f = SolarXr.Read(Convert.FromHexString(feed), got, ref news, ref d);
                var snap = new SlimeVrLink.Snapshot();
                snap.Trackers.AddRange(got);
                string rows = Rows(snap);
                var d0 = d;
                SolarXr.Read(Convert.FromHexString(answer), got, ref news, ref d);
                Want($"slimevr sample {name} ({(name == "v22" ? "SlimeVR v22's own classes' bytes" : "v21's DataFeedBuilder calls, the official runtime")}): an update {f}, " +
                     $"dialect {d0} (want {(name == "v22" ? "V22" : "V21")}), trackers '{rows}' " +
                     $"(want '{want}'); the reset's answer: type {news.Type} state {news.State} {news.LeftMs} ms left (want type 1 state 0 3000) -",
                     f && d0 == (name == "v22" ? SolarXr.Dialect.V22 : SolarXr.Dialect.V21) && rows == want && news.Has && news.Type == 1 && news.State == 0 && news.LeftMs == 3000);
            }
            catch (Exception e) { Log.Error($"slimevr sample {name}: {e}"); Want($"slimevr sample {name}: read", false); }
        }
        // the builder: what the link sends, read back - and the two reset headers read in the other dialect are no message (0)
        {
            var st = ReadStart(SolarXr.StartFeed(1000));
            var r22 = ReadResets(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Full, SolarXr.Dialect.V22), SolarXr.Dialect.V22);
            var r21 = ReadResets(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Yaw, SolarXr.Dialect.V21), SolarXr.Dialect.V21);
            var x21 = ReadResets(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Mounting, SolarXr.Dialect.V21), SolarXr.Dialect.V22);
            var x22 = ReadResets(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Mounting, SolarXr.Dialect.V22), SolarXr.Dialect.V21);
            var v21 = new FbRead(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Mounting, SolarXr.Dialect.V21));
            int h = v21.Element(v21.Vector(v21.Root(), SolarXr.BundleRpc, out int h0) > 0 ? h0 : 0, 0);
            int asV22Type = v21.U8(h, 2);
            Want($"slimevr builder: StartDataFeed every {st.ms} ms (want 1000), device data {st.device}, tracker info {st.info}, status {st.status}, rotation {st.rotation} " +
                 $"(want True True True False); ResetRequest v22 {string.Join(" ", r22)} (want (1, 3, 0)), v21 {string.Join(" ", r21)} (want (0, NaN, 0)); " +
                 $"v21's header read as v22: type {asV22Type}, resets {x21.Count}; v22's read as v21: resets {x22.Count} (want 0, 0, 0) -",
                 st.start && st.ms == 1000 && st.device && st.info && st.status && !st.rotation && r22.Count == 1 && r22[0] == (1, 3f, 0) && r21.Count == 1 &&
                 r21[0].type == 0 && float.IsNaN(r21[0].delay) && asV22Type == 0 && x21.Count == 0 && x22.Count == 0);
            File.WriteAllText(Path.Combine(dir, "slimevr_client.txt"),
                $"start {Convert.ToHexString(SolarXr.StartFeed(1000))}\nreset22 {Convert.ToHexString(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Full, SolarXr.Dialect.V22))}\n" +
                $"reset21 {Convert.ToHexString(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Yaw, SolarXr.Dialect.V21))}\n" +
                $"update22 {Convert.ToHexString(Update(SolarXr.Dialect.V22, Six))}\n");
        }
        // a broken message: FormatException, never another one
        {
            string what;
            try { var g = new List<SlimeVrLink.Tracker>(); var n = default(SolarXr.ResetNews); var d = SolarXr.Dialect.Unknown; SolarXr.Read(new byte[] { 200, 1, 0, 0, 9 }, g, ref n, ref d); what = "read"; }
            catch (FormatException) { what = "FormatException"; }
            catch (Exception e) { what = e.GetType().Name; }
            Want($"slimevr reader: a broken message gives {what} (want FormatException) -", what == "FormatException");
        }
        // the link on the made-up server, each dialect: connects, asks for the feed, reads the trackers, publishes a change; a double tap
        // on Yaw reset in the opened view sends exactly one ResetRequest of type yaw (the modern style, 95 and the Terminal on v22)
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var look0 = cfg.Data.Appearance;
        var (s0, r0) = (data.SlimeVr, wrist.OnSlimeReset);
        try
        {
            foreach (var d in new[] { SolarXr.Dialect.V22, SolarXr.Dialect.V21 })
            {
                using var server = new FakeServer(d, n => Update(d, Six, n == 0 ? 0 : 5));
                if (server.Port == SlimeVrLink.ServerPort || server.Port <= 0) { Want($"slimevr link {d}: the made-up server's port {server.Port} is not SlimeVR's", false); continue; }
                using var link = new SlimeVrLink(server.Port);
                link.Active = true;
                bool up = Until(() => link.Get() is { State: SlimeVrLink.State.Ok, Trackers.Count: 6 }, 5);
                var first = link.Get();
                var start = server.Got().Select(ReadStart).FirstOrDefault(x => x.start);
                bool changed = Until(() => link.Get().Version > first.Version && link.Get().Trackers.Count == 6 && link.Get().Trackers[0].Battery == 82, 4);
                Want($"slimevr link {d} (port {server.Port}): connected and read {up}, dialect {first.Dialect}, asked every {start.ms} ms with device data {start.device}, " +
                     $"info {start.info}, status {start.status}; trackers '{Rows(first)}'; the next update published {changed} (chest 87 -> 82) -",
                     up && first.Dialect == d && start.ms == 1000 && start.device && start.info && start.status && changed &&
                     Rows(first) == "UpperChest 87% -48dBm 12ms s2, Hip 64%+ -55dBm 9ms s2, LeftThigh 41% -61dBm 14ms s2, RightThigh 52% -58dBm 11ms s2, " +
                                    "LeftAnkle 23% -72dBm 25ms s2, Right foot strap 12% -83dBm 41ms s2");
                // the double tap, through the watch
                wcfg.Face = "custom";
                wcfg.Background = "panel";
                wcfg.Grid = null; wcfg.Pages = null; wcfg.Page = 0;
                wcfg.Widgets = new List<WatchWidget> { new() { Type = "slimevr", X = 0, Y = 0, W = 2, H = 2 } };
                wrist.OnSlimeReset = k => link.Reset((SlimeVrLink.ResetKind)k);
                foreach (var style in d == SolarXr.Dialect.V22 ? new[] { "modern", "classic", "terminal" } : new[] { "modern" })
                {
                    cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                    Theme.Apply(cfg.Data.Appearance);
                    int before = server.Resets(out _);
                    data.SlimeVr = link.Get();
                    wrist.Settle();
                    wrist.CloseForTest();
                    wrist.OpenForTest("slimevr");
                    wrist.Draw(data); wrist.Draw(data);
                    var p = wrist.ControlPoint(data, "wsvr0");
                    if (p is not { } pp) { Want($"slimevr reset {d} {style}: the Yaw reset button drawn", false); continue; }
                    Tap(wrist, data, pp);
                    int asked = wrist.SlimeArmed;
                    Thread.Sleep(600);
                    int afterOne = server.Resets(out _) - before;
                    data.SlimeVr = link.Get();
                    Tap(wrist, data, pp);
                    bool sent = Until(() => server.Resets(out _) - before >= 1, 3);
                    Thread.Sleep(500);
                    int total = server.Resets(out var last) - before;
                    bool answered = Until(() => link.Get().ResetKind == 0 && link.Get().ResetState == 1, 3);
                    data.SlimeVr = link.Get();
                    wrist.Draw(data);
                    string file = Path.Combine(dir, $"slimevr_reset_{(d == SolarXr.Dialect.V22 ? "v22" : "v21")}_{style}.png");
                    wrist.Surf.SavePng(gpu, file);
                    Want($"slimevr reset {d} {style}: the first tap asks (armed {asked}, want 0) and sends {afterOne} (want 0); the second sends {total} ResetRequest(s) " +
                         $"(want 1) of type {last.type} (want 0, yaw), delay {last.delay} (want {(d == SolarXr.Dialect.V22 ? "0" : "NaN: the server's own")}), body parts {last.parts} " +
                         $"(want 0: all); the server's answer seen {answered} -",
                         asked == 0 && afterOne == 0 && sent && total == 1 && last.type == 0 && last.parts == 0 && answered &&
                         (d == SolarXr.Dialect.V22 ? last.delay == 0 : float.IsNaN(last.delay)));
                    wrist.CloseForTest();
                }
                link.Active = false;
                bool closed = Until(() => link.Get().State == SlimeVrLink.State.Off, 3);
                Want($"slimevr link {d}: switched off it closes ({closed}), {server.Connections} connection(s) made (want 1), {server.Updates} update(s) in " +
                     $"~{server.Updates - 1} s (the feed asked for at most one a second) -", closed && server.Connections == 1);
            }
            // nothing on the port: "SlimeVR not running", quietly
            var free = new TcpListener(IPAddress.Loopback, 0);
            free.Start();
            int port = ((IPEndPoint)free.LocalEndpoint).Port;
            free.Stop();
            if (port != SlimeVrLink.ServerPort)
            {
                using var none = new SlimeVrLink(port);
                none.Active = true;
                bool told = Until(() => none.Get().State == SlimeVrLink.State.NotRunning, 4);
                data.SlimeVr = none.Get();
                cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                string msg = wrist.SlimeMessage(data);
                Want($"slimevr link: nothing on port {port} - {none.Get().State} (want NotRunning), the widget says '{msg}' (want 'SlimeVR not running'), a reset sends nothing " +
                     $"{!none.Reset(SlimeVrLink.ResetKind.Full)} -", told && msg == "SlimeVR not running" && !none.Reset(SlimeVrLink.ResetKind.Full));
                none.Active = false;
            }
        }
        catch (Exception e) { Log.Error("slimevr link checks: " + e); Want("slimevr link checks: ran to the end", false); }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (data.SlimeVr, wrist.OnSlimeReset) = (s0, r0);
            cfg.Data.Appearance = look0;
            Theme.Apply(cfg.Data.Appearance);
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    // per style: the face with all three sizes, opened, a reset asking, its countdown; SlimeVR not running
    public static readonly List<(string tag, string face, string open, string ask, string none)> Preview = new();

    static List<WatchWidget> Face() => new()
    {
        new() { Type = "slimevr", X = 0, Y = 0, W = 2, H = 2 }, new() { Type = "slimevr", X = 2, Y = 0, W = 2, H = 1 }, new() { Type = "slimevr", X = 4, Y = 0, W = 1, H = 1 },
        new() { Type = "battery", X = 5, Y = 0, W = 1, H = 1 }, new() { Type = "time", X = 2, Y = 1, W = 2, H = 1 }, new() { Type = "date", X = 4, Y = 1, W = 2, H = 1 },
        new() { Type = "slimevr", X = 0, Y = 2, W = 2, H = 1 }, new() { Type = "media", X = 2, Y = 2, W = 4, H = 1 },
        new() { Type = "keyboard", X = 0, Y = 3 }, new() { Type = "desktops", X = 1, Y = 3 }, new() { Type = "recenter", X = 2, Y = 3 },
        new() { Type = "layout", X = 3, Y = 3 }, new() { Type = "night", X = 4, Y = 3 }, new() { Type = "settings", X = 5, Y = 3 },
    };

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var (s0, r0) = (data.SlimeVr, wrist.OnSlimeReset);
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null; wcfg.Pages = null; wcfg.Page = 0;
            wcfg.Widgets = Face();
            data.SlimeVr = Fake();
            wrist.OnSlimeReset = null;
            wrist.Settle();
            wrist.Draw(data); wrist.Draw(data);
            string face = Path.Combine(dir, $"wrist_slimevr{tag}.png");
            wrist.Surf.SavePng(gpu, face);
            int shown = wrist.SlimeShown(data);
            string lowest = wrist.SlimeLowest(data), row0 = wrist.SlimeRow(data, 0), row5 = wrist.SlimeRow(data, 5);
            // opened; a first tap on Full reset asks, the second is the one the app would send; then the server's countdown on it
            wrist.OpenForTest("slimevr");
            wrist.Draw(data); wrist.Draw(data);
            string open = Path.Combine(dir, $"wrist_open_slimevr{tag}.png");
            wrist.Surf.SavePng(gpu, open);
            var full = wrist.ControlPoint(data, "wsvr1");
            int armed = -1, sentTo = -1;
            string ask = Path.Combine(dir, $"wrist_open_slimevr_ask{tag}.png");
            if (full is { } fp)
            {
                Tap(wrist, data, fp);
                armed = wrist.SlimeArmed;
                wrist.Settle();                    // (the button's fill eases over: shown where it goes)
                wrist.Draw(data); wrist.Draw(data);
                wrist.Surf.SavePng(gpu, ask);
                Tap(wrist, data, fp);
                sentTo = wrist.LastSlimeReset;
                var counting = Fake();
                (counting.ResetKind, counting.ResetState, counting.ResetLeftMs, counting.ResetAt) = (1, 0, 3000, Clock.Now);
                data.SlimeVr = counting;
                wrist.Settle();
                wrist.Draw(data); wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_open_slimevr_count{tag}.png"));
            }
            wrist.CloseForTest();
            // one busy, one off
            data.SlimeVr = Fake(trouble: true);
            wrist.OpenForTest("slimevr");
            wrist.Settle();
            wrist.Draw(data); wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_open_slimevr_trouble{tag}.png"));
            wrist.CloseForTest();
            // SlimeVR not running
            data.SlimeVr = None();
            wrist.Settle();
            wrist.Draw(data); wrist.Draw(data);
            string none = Path.Combine(dir, $"wrist_slimevr_none{tag}.png");
            wrist.Surf.SavePng(gpu, none);
            string msg = wrist.SlimeMessage(data);
            wrist.OpenForTest("slimevr");
            wrist.Draw(data); wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_open_slimevr_none{tag}.png"));
            wrist.CloseForTest();
            // what a frame allocates at rest with only these widgets on the face, then opened (want 0)
            data.SlimeVr = Fake();
            wcfg.Widgets = Face().FindAll(x => x.Type == "slimevr");
            using var appOnly = Tools.AppAllocs();
            wrist.Settle();
            for (int i = 0; i < 6; i++) wrist.Draw(data);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) wrist.Draw(data);
            long perFrame = (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            wrist.OpenForTest("slimevr");
            for (int i = 0; i < 6; i++) wrist.Draw(data);
            a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) wrist.Draw(data);
            long perOpen = (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SlimeAllocTypes("slimevr opened" + tag, () => wrist.Draw(data));
            wrist.CloseForTest();
            if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SlimeAllocTypes("slimevr" + tag, () => wrist.Draw(data));
            Want($"slimevr view{tag}: {shown} trackers (want 6), the emptiest '{lowest}' (want '12%'), the first row '{row0}', the last '{row5}'; a first tap on Full " +
                 $"reset asks ({armed}, want 1), the second goes out ({sentTo}, want 1); not running: '{msg}' (want 'SlimeVR not running') -",
                 shown == 6 && lowest == "12%" && armed == 1 && sentTo == 1 && msg == "SlimeVR not running");
            Want($"slimevr view{tag}: a frame at rest with only these widgets on the face {perFrame} B (want 0), opened {perOpen} B (want 0)", perFrame == 0 && perOpen == 0);
            Preview.RemoveAll(p => p.tag == tag);
            Preview.Add((tag, face, open, ask, none));
        }
        catch (Exception e) { Log.Error("slimevr view: " + e); Want($"slimevr view{tag}: ran to the end", false); }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (data.SlimeVr, wrist.OnSlimeReset) = (s0, r0);
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    // every style's face, opened, a reset asking and SlimeVR not running side by side, one row a style
    public static void PreviewSheet(string dir)
    {
        if (Preview.Count == 0) return;
        const int W = 640, H = 440, Gap = 12, Label = 26;
        using var sheet = new Bitmap(4 * W + 5 * Gap, Preview.Count * (H + Label + Gap) + Gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 32, 34, 40));
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int i = 0; i < Preview.Count; i++)
            {
                var (t, face, open, ask, none) = Preview[i];
                float y = Gap + i * (H + Label + Gap);
                g.DrawString($"{(t.Length == 0 ? "modern" : t[1..])}: the face (2x2, 2x1, 1x1), opened, Full reset asking, SlimeVR not running (made-up trackers)", font,
                    Brushes.White, Gap, y + 4);
                int k = 0;
                foreach (var f in new[] { face, open, ask, none })
                {
                    if (File.Exists(f)) using (var b = new Bitmap(f)) g.DrawImage(b, Gap + k * (W + Gap), y + Label, W, H);
                    k++;
                }
            }
        }
        string path = Path.Combine(dir, "slimevr_preview.png");
        sheet.Save(path, ImageFormat.Png);
        Log.Info("slimevr: every style side by side: " + path);
    }
}

// Two byte samples made the servers' way (no fixture of SlimeVR's own was found in its repositories): Feed22 / Reset22 encoded by SlimeVR
// v22.0.0-rc.2's own SolarXR classes from its slimevr.jar (the Kotlin data classes, JvmFlatBufferWriter) - a headset, a chest tracker, a
// board with two (the thigh's extension on the ankle, busy), a disconnected charging one named "Belt"; a full reset counting 3 s. Feed21 /
// Reset21 the same as SlimeVR up to v21 builds them, call for call (DataFeedBuilder.kt, RPCResetHandler.kt), with the official FlatBuffers
// runtime (its builder is the server's Java one's algorithm).
static class SlimeSamples
{
    public const string Feed22 =
        "04000000deffffff04000000010000000c00000008000a0009000400080000000c00000000030600080004000600000004000000040000001803000014020000e00000000400000008feffff00000002" +
        "0c000000a8000000000003000100000004000000f6fdffff0000020124000000040003001c004400430040003f003e002c001c001800140013000000000004001c000000000000000000000000000000" +
        "0000000130000000380000000000000000000000000000000000803f000000008104353f000000008104353f00000707040000010400000042656c7400000000150000007564703a2f2f3139322e3136" +
        "382e312e3132332f30000000e4fdffff0000006433338b40c7ffc0ffc4ff0800100018001600100000000c0008000700100000000000000210000000f000000000010000000002000200000074000000" +
        "04000000d6feffff000002030800000003000200b4feffff000000000000000000000000000000012c0000000000000000000000000000000000803f000000008104353f000000008104353f00000a0a" +
        "04000001150000007564703a2f2f3139322e3136382e312e3132322f3100000042ffffff00000202080000000200020020ffffff000000000000000000000000000000012c0000000000000000000000" +
        "000000000000803f000000008104353f000000008104353f0000080804000001150000007564703a2f2f3139322e3136382e312e3132322f3000000004ffffff000000173d0a6740bcffb5ffb9ff1e00" +
        "080000004c656674206c656700000000100014001200000000000c000800070010000000000000020c000000d00000000000010001000000280000000000220010000e000c0008000700000000000000" +
        "00000000000000000000000000000600220000000000020224000000010001001c0040003f003c003b003a00280018001400000013000000000004001c00000000000000000000000000000000000001" +
        "2c0000000000000000000000000000000000803f000000008104353f000000008104353f0000030304000001150000007564703a2f2f3139322e3136382e312e3132312f300000001400140000001200" +
        "10000e000c00000008000700140000000000005752b87e40d3ffccffd0ff0c00100010000000000000000c0008000700100000000000000108000000a00000000100000028000000000022000c000000" +
        "000008000700000000000000000000000000000000000000000006002200000000000102200000001c003c00000000003b003a00280018001400000013000000000004001c0000000000000000000000" +
        "0000000000000001280000000000000000000000000000000000803f000000008104353f000000008104353f0000010103000000484d44000400040004000000";

    public const string Reset22 =
        "04000000c0ffffff0400000001000000100000000c000a0000000000090004000c0000001400000000040e000c000b00000000000a0004000e0000001000000000000101080008000000040008000000" +
        "b80b0000";

    public const string Feed21 =
        "04000000daffffff04000000010000000c00000008000e000700080008000000000000030c000000000006000800040006000000040000000400000010040000bc02000030010000040000000efcffff" +
        "1000000014000000700000000000000301000000780000004efdffff00000900020015007b01a8c0100000003400000020000000380000001000000041413a42423a43433a30303a31313a3300000000" +
        "0a000000323032352d30362d3031000007000000536c696d6556520005000000302e362e33000000c2feffff0000c4ff0800006533338b400000000122fdffff0000000128000000040000000efcffff" +
        "00031a00340026003300000014003200000025002c002800000004001a0000000000000000000000000000000000803f000000008104353f000000008104353f000104000c0000001400000000000105" +
        "0400000042656c7400000000150000007564703a2f2f3139322e3136382e312e3132332f30000e0018001300140008000c0004000e000000240000002c00000098000000000000020400000008000000" +
        "4c656674206c656700000000020000008c000000f40000008efeffff00000900020015007a01a8c0100000003400000020000000380000001000000041413a42423a43433a30303a31313a3200000000" +
        "0a000000323032352d30362d3031000007000000536c696d6556520005000000302e362e330012001400130000000800060000000c000b00120000000000b9ff1e0000173d0a67400000000172feffff" +
        "00000003180000000c0000000800080006000700080000000000020172feffff0000000000000000000000000000803f000000008104353f000000008104353f00010400080000000000010815000000" +
        "7564703a2f2f3139322e3136382e312e3132322f31000000defeffff000000021000000004000000a2fdffff00000002d6feffff0000000000000000000000000000803f000000008104353f00000000" +
        "8104353f000104000800000000000106150000007564703a2f2f3139322e3136382e312e3132322f30000000befeffff1000000034000000a00000000000000101000000b000000000001e0020000800" +
        "00000000140000001c0000000c000000060010000a0018001e00000000000900020015007901a8c0100000003400000020000000380000001000000041413a42423a43433a30303a31313a3100000000" +
        "0a000000323032352d30362d3031000007000000536c696d6556520005000000302e362e330012001200110000000800060000000c000b00120000000000d0ff0c00005752b87e4000010a0010000c00" +
        "080007000a000000000000022800000004000000f6feffff00011a00300026002f00000014002e000000250028000000000004001a0000000000000000000000000000000000803f000000008104353f" +
        "000000008104353f000104000800000000000103150000007564703a2f2f3139322e3136382e312e3132312f30000e0014001300000008000c0004000e00000010000000340000008c00000000000000" +
        "010000009000000000001e0018000600000000000c000000140000000000000004000800000010001e00000009000200100000003400000020000000380000001000000041413a42423a43433a30303a" +
        "31313a30000000000a000000323032352d30362d3031000007000000536c696d6556520005000000302e362e33000600060005000600000000010a0012000c00080007000a0000000000000234000000" +
        "0c000000000006000800070006000000000000001c00100000000f00000000000e0000000000080000000000000007001c00000000000001080000000000010103000000484d4400";

    public const string Reset21 =
        "0c00000008000800000004000800000004000000010000001000000000000a000e000000070008000a000000000000041400000000000e000c000b0000000000000004000e000000b80b000000000001";
}

static partial class Tools
{
    internal static void SlimeAllocTypes(string what, Action draw) => AllocTypes(what, draw, null);

    // ORBIT_SNAP_ONLY=slimevr: the link's checks (the samples, the builder, a made-up server in each dialect, the double tap), the widget in
    // every style with its pictures and allocation checks, its layout check (every size, the 4 x 3 page's, opened), the preview sheet
    static int SlimeVrOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        int failed0 = WantsFailed, layout = 0;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        SlimeVrViewTools.DataChecks(wrist, data, cfg, gpu, dir);
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            SlimeVrViewTools.Run(wrist, data, cfg, gpu, dir, tag);
            data.SlimeVr = SlimeVrViewTools.Fake();
            string f0 = cfg.Data.Wrist.Face;
            layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag.Length > 0 ? tag[1..] : "modern", onlyKind: "slimevr");
            cfg.Data.Wrist.Face = f0;
            cfg.Data.Wrist.Widgets = null;
        }
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        SlimeVrViewTools.PreviewSheet(dir);
        System.Globalization.CultureInfo.CurrentCulture = culture;
        Log.Info($"layout slimevr: {layout} problem(s) in every style");
        Log.Info("snapshot (slimevr only) written to " + dir);
        return layout > 0 ? 1 : WantsExit(failed0);
    }
}
