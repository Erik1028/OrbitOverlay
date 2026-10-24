using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Orbit;

// ORBIT_SNAP_ONLY=lights, the bulb straight (Sys/TapoDirect.cs) against a made-up Tapo bulb of the tool's own: a plain HTTP listener on
// 127.0.0.1 (a port Windows picks, nothing on the network) that speaks the bulb's side of KLAP with a made-up account - never the real
// bulb, never a real account. What it must do and must not:
//  - read the made-up bulb as Lumos would answer for it; a wrong key: "Wrong Tapo account";
//  - never follow a redirect off the bulb's address (a second listener of the tool's own counts what reaches it);
//  - never take more than 64 KB of an answer (a flood of 16 MB with no length said: what it allocated, what went out before it let go);
//  - an odd answer (a number too big or not whole, a list or a string for the answer) read as far as it goes, else "could not be read" -
//    never .NET's own words on the watch;
//  - LumosLink lets the bulb's link a new key replaced go (on its own thread, no call in flight), and its last one at the end;
//  - the key from an app's settings file (TapoAccount.KeyFrom, on made-up files of the tool's own - never Hexpad's or Lumos's): the
//    same key as from the account's words, with a UTF-8 BOM, with escapes, with the file held open by its writer; none where none is.
static class TapoSafetyTools
{
    // the bulb's side of KLAP (the account's key made up): handshake1, handshake2, then the requests, each answered by Answer - and when
    // Redirect is set, handshake1 answered with a 307 to there instead
    sealed class FakeBulb : IDisposable
    {
        readonly TcpListener _l = new(IPAddress.Loopback, 0);
        readonly Thread _t;
        volatile bool _stop;
        readonly byte[] _auth;
        byte[] _local, _remote;
        public volatile int Hits;                 // requests that reached it
        public volatile string Redirect;          // handshake1 answered with a 307 to this URL (null: as a bulb)
        public volatile int Flood;                // handshake1 answered with this many bytes, no length said (0: as a bulb)
        public long Sent;                         // of the flood: how much went out before the other side let go
        public Func<string, string> Answer;       // the method asked -> the bulb's answer before it is encrypted

        public FakeBulb(byte[] auth)
        {
            _auth = auth;
            _l.Start();
            _t = new Thread(Serve) { IsBackground = true, Name = "made-up bulb" };
            _t.Start();
        }

        public string Host => "127.0.0.1:" + ((IPEndPoint)_l.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public string Url => "http://" + Host;

        void Serve()
        {
            while (!_stop)
            {
                try
                {
                    using var c = _l.AcceptTcpClient();
                    c.ReceiveTimeout = c.SendTimeout = 3000;
                    using var s = c.GetStream();
                    var (path, body) = ReadRequest(s);
                    if (path == null) continue;
                    Hits++;
                    Reply(s, path, body);
                }
                catch (Exception) when (!_stop) { }
                catch (Exception) { return; }
            }
        }

        // a request: its path and body (HTTP/1.1 as HttpClient sends it: Content-Length, no 100-continue)
        static (string path, byte[] body) ReadRequest(NetworkStream s)
        {
            var head = new List<byte>();
            while (head.Count < 8192)
            {
                int b = s.ReadByte();
                if (b < 0) return (null, null);
                head.Add((byte)b);
                int n = head.Count;
                if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n') break;
            }
            var lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n");
            var first = lines[0].Split(' ');
            int len = 0;
            foreach (var l in lines)
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l[15..].Trim(), out len);
            var body = new byte[len];
            s.ReadExactly(body);
            return (first.Length > 1 ? first[1] : "", body);
        }

        static void Send(NetworkStream s, string status, string headers, byte[] body)
        {
            var h = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n{headers}Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            s.Write(h);
            s.Write(body);
        }

        // an answer that goes on until the connection closes, a chunk at a time, until the other side stops taking it
        void SendFlood(NetworkStream s, int bytes)
        {
            Interlocked.Exchange(ref Sent, 0);
            s.Write("HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n"u8);
            var chunk = new byte[64 * 1024];
            try
            {
                for (int n = 0; n < bytes; n += chunk.Length) { s.Write(chunk); Interlocked.Add(ref Sent, chunk.Length); }
            }
            catch (IOException) { }
        }

        void Reply(NetworkStream s, string path, byte[] body)
        {
            if (path == "/app/handshake1")
            {
                if (Redirect != null) { Send(s, "307 Temporary Redirect", "Location: " + Redirect + "\r\n", Array.Empty<byte>()); return; }
                if (Flood > 0) { SendFlood(s, Flood); return; }
                _local = body;
                _remote = RandomNumberGenerator.GetBytes(16);
                Send(s, "200 OK", "Set-Cookie: TP_SESSIONID=MADEUP01;TIMEOUT=86400\r\n", Join(_remote, Sha(_local, _remote, _auth)));
                return;
            }
            if (path == "/app/handshake2")
            {
                bool ok = _local != null && CryptographicOperations.FixedTimeEquals(body, Sha(_remote, _local, _auth));
                Send(s, ok ? "200 OK" : "403 Forbidden", "", Array.Empty<byte>());
                return;
            }
            int q = path.IndexOf("seq=", StringComparison.Ordinal);
            if (!path.StartsWith("/app/request", StringComparison.Ordinal) || q < 0 || _local == null) { Send(s, "404 Not Found", "", Array.Empty<byte>()); return; }
            int seq = int.Parse(path[(q + 4)..], System.Globalization.CultureInfo.InvariantCulture);
            var both = Join(_local, _remote, _auth);
            byte[] key = Sha("lsk"u8.ToArray(), both)[..16], iv = Sha("iv"u8.ToArray(), both)[..12], sig = Sha("ldk"u8.ToArray(), both)[..28];
            var full = new byte[16];
            iv.CopyTo(full, 0);
            BinaryPrimitives.WriteInt32BigEndian(full.AsSpan(12), seq);
            using var aes = Aes.Create();
            aes.Key = key;
            using var req = JsonDocument.Parse(aes.DecryptCbc(body.AsSpan(32), full, PaddingMode.PKCS7));
            string method = req.RootElement.GetProperty("method").GetString();
            var ct = aes.EncryptCbc(Encoding.UTF8.GetBytes(Answer?.Invoke(method) ?? "{\"error_code\":0}"), full, PaddingMode.PKCS7);
            var sq = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(sq, seq);
            Send(s, "200 OK", "", Join(Sha(sig, sq, ct), ct));
        }

        static byte[] Join(params byte[][] parts)
        {
            var r = new List<byte>();
            foreach (var p in parts) r.AddRange(p);
            return r.ToArray();
        }

        static byte[] Sha(params byte[][] parts) => SHA256.HashData(Join(parts));

        public void Dispose()
        {
            _stop = true;
            _l.Stop();
            _t.Join(1000);
        }
    }

    // the made-up bulb's get_device_info: on, 60 %, a warm white, named
    const string DeviceInfo = "{\"error_code\":0,\"result\":{\"device_on\":true,\"brightness\":60,\"hue\":30,\"saturation\":40,\"color_temp\":2700," +
                              "\"color_temp_range\":[2500,6500],\"nickname\":\"TWFkZS11cCBidWxi\"}}";

    public static void Run(string dir)
    {
        AccountFiles(Path.Combine(dir, "tapo_test"));
        var key = TapoAccount.KeyFor("made-up@example.com", "made-up password");
        using var bulb = new FakeBulb(key) { Answer = m => m == "get_device_info" ? DeviceInfo : "{\"error_code\":0}" };
        using var away = new FakeBulb(key) { Answer = m => DeviceInfo };      // where a redirect would send it
        LampState State(TapoDirect d) { var (c, b) = d.Handle("GET", "/api/state?host=" + Uri.EscapeDataString(bulb.Host), null); return LumosLink.Parse(c, b); }

        // the made-up bulb read as Lumos answers for it; switched on through set_device_info; a wrong key
        var direct = new TapoDirect(key);
        var s = State(direct);
        bool read = s.Ready && s.On && s.Brightness == 60 && s.HasColor && s.HasTemp && s.Temp == 2700 && s.TempMin == 2500 && s.TempMax == 6500 && s.Name == "Made-up bulb";
        var (pc, pb) = direct.Handle("POST", "/api/power", "{\"host\":" + JsonSerializer.Serialize(bulb.Host) + ",\"on\":true}");
        var w = State(new TapoDirect(TapoAccount.KeyFor("nobody@example.com", "wrong")));
        Log.Info($"lights direct: a made-up bulb read through KLAP {read} (want True), a command answered with its state {pc == 200 && LumosLink.Parse(pc, pb).Ready} " +
                 $"(want True), a wrong key says '{w.Message}' {w.Message == "Wrong Tapo account"} (want True)");

        // a redirect off the bulb's address: not followed - nothing reaches the other host, the bulb's error said as such
        bulb.Redirect = away.Url + "/app/handshake1";
        int awayHits = away.Hits;
        var r = State(new TapoDirect(key));
        bulb.Redirect = null;
        bool stayed = away.Hits == awayHits;
        Log.Info($"lights direct: a 307 from the bulb's address - the other host asked {away.Hits - awayHits} time(s), not followed {stayed} (want True), " +
                 $"the watch told '{r.Message}' {r.Status == LinkStatus.Error && r.Message == "Bulb error 307"} (want True)");

        // a flood from the bulb's address (16 MB, no length said): no more than 64 KB of it taken, the answer not read
        bulb.Flood = 16 << 20;
        long a0 = GC.GetTotalAllocatedBytes(true);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var f = State(new TapoDirect(key));
        double ms = sw.Elapsed.TotalMilliseconds;
        long took = GC.GetTotalAllocatedBytes(true) - a0;
        bulb.Flood = 0;
        Thread.Sleep(100);
        long sent = Interlocked.Read(ref bulb.Sent);
        Log.Info($"lights direct: a 16 MB answer - {took / 1024} KB allocated taking it, {sent / 1024} KB of it sent before it was let go - capped " +
                 $"{took < 4 << 20 && sent < 16 << 20} (want True), in {ms:0} ms; the watch told '{f.Message}' {f.Message == "The bulb's answer could not be read"} (want True)");

        // odd answers: read as far as they go, or "could not be read" - never .NET's words
        string Odd(string answer)
        {
            bulb.Answer = m => m == "get_device_info" ? answer : "{\"error_code\":0}";
            try { var o = State(new TapoDirect(key)); return o.Ready ? "read" : o.Message; }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }
        const string Unreadable = "The bulb's answer could not be read";
        string half = Odd("{\"error_code\":0,\"result\":{\"device_on\":true,\"brightness\":50.5,\"color_temp_range\":[2500.5,6500]}}");
        string huge = Odd("{\"error_code\":0,\"result\":{\"device_on\":true,\"brightness\":1e12,\"hue\":1e12}}");
        string code = Odd("{\"error_code\":1e12}"), list = Odd("[1,2,3]"), text = Odd("\"made up\"");
        bulb.Answer = m => m == "get_device_info" ? DeviceInfo : "{\"error_code\":0}";
        Log.Info($"lights direct: odd answers - a brightness of 50.5 '{half}', of 1e12 '{huge}' (want read, read), an error code of 1e12 '{code}', a list '{list}', " +
                 $"a string '{text}' (want '{Unreadable}' each) - {half == "read" && huge == "read" && code == Unreadable && list == Unreadable && text == Unreadable} (want True)");

        // the link through a kept key (DPAPI, in memory only): a new key replaces the bulb's link - the old one let go, the light still read;
        // the link closed: its last one let go too
        static bool Wait(Func<bool> ok, int ms = 3000)
        {
            for (int i = 0; i < ms / 20 && !ok(); i++) Thread.Sleep(20);
            return ok();
        }
        TapoDirect d1, d2;
        bool first, swapped, kept;
        using (var l = new LumosLink { Poll = 0.2, Retry = 0.2, Slow = 0.2 })
        {
            l.SetKey(TapoAccount.Protect(key));
            l.SetBulb(bulb.Host);
            l.Active = true;
            l.Watched = true;
            first = Wait(() => l.State.Ready);
            d1 = l.DirectForTest;
            l.SetKey(TapoAccount.Protect(key));             // (the same key protected again: another kept text, so a change)
            Wait(() => l.DirectForTest != d1);              // (10-24: the new link is made on the light's own thread)
            d2 = l.DirectForTest;
            l.Watched = false; l.Watched = true;
            swapped = d2 != d1 && Wait(() => d1.Disposed) && Wait(() => l.State.Ready);
            kept = !d2.Disposed;
        }
        Log.Info($"lights direct: through a kept key the link read the made-up bulb {first} (want True); a new key - the old link let go, the light still read " +
                 $"{swapped} (want True), the new one kept {kept} (want True); the link closed - its last one let go {d2.Disposed} (want True)");
    }

    // the key from made-up settings files shaped like Hexpad's (its "tapo" section) and Lumos's (at the top)
    static void AccountFiles(string td)
    {
        if (Directory.Exists(td)) Directory.Delete(td, true);
        Directory.CreateDirectory(td);
        string F(string name, string json, bool bom = false)
        {
            string p = Path.Combine(td, name);
            File.WriteAllBytes(p, [.. (bom ? "﻿"u8 : ""u8), .. Encoding.UTF8.GetBytes(json)]);
            return p;
        }
        bool Same(byte[] a, byte[] b) => a != null && b != null && a.AsSpan().SequenceEqual(b);
        var want = TapoAccount.KeyFor("made-up@example.com", "made-up password");
        string hexpad = "{\"weather\": \"Made-up Town\", \"tapo\": {\"host\": \"10.0.0.99\", \"email\": \"made-up@example.com\", \"password\": \"made-up password\"}, " +
                        "\"keys\": [{\"password\": \"not this one\"}]}";
        string plain = F("hexpad.json", hexpad), bom = F("hexpad_bom.json", hexpad, true);
        bool hex = Same(TapoAccount.KeyFrom(plain, "tapo", "email"), want), hexBom = Same(TapoAccount.KeyFrom(bom, "tapo", "email"), want);
        // Lumos's shape, the password with escapes in it; a nested "password" is not the account's
        string lumos = F("lumos.json", "{\"username\": \"made-up@example.com\", \"devices\": {\"password\": \"not this one\"}, \"password\": \"m\\u00e1de-up \\\"pass\\\" \\\\ word\"}");
        bool esc = Same(TapoAccount.KeyFrom(lumos, null, "username"), TapoAccount.KeyFor("made-up@example.com", "máde-up \"pass\" \\ word"));
        // held open by its writer (the app saving it right then)
        bool held;
        using (new FileStream(bom, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) held = Same(TapoAccount.KeyFrom(bom, "tapo", "email"), want);
        // none: no password, an empty one, the section not an object, not JSON, no file
        bool none = TapoAccount.KeyFrom(F("a.json", "{\"tapo\": {\"email\": \"made-up@example.com\"}}"), "tapo", "email") == null &&
                    TapoAccount.KeyFrom(F("b.json", "{\"tapo\": {\"email\": \"made-up@example.com\", \"password\": \"\"}}"), "tapo", "email") == null &&
                    TapoAccount.KeyFrom(F("c.json", "{\"tapo\": \"made-up\"}"), "tapo", "email") == null &&
                    TapoAccount.KeyFrom(F("d.json", "made-up, not JSON"), "tapo", "email") == null &&
                    TapoAccount.KeyFrom(Path.Combine(td, "none.json"), "tapo", "email") == null;
        // 10-19: the bulb's address from Lumos's list (the first device's host, an IP address only); the account typed in the dialog
        string lumosList = F("lumos_list.json", "{\"username\": \"made-up@example.com\", \"password\": \"made-up password\", \"devices\": " +
                             "[{\"host\": \"10.0.0.98\", \"alias\": \"Made-up lamp\"}, {\"host\": \"10.0.0.97\"}], \"steamvr_on\": true}", true);
        string addr = TapoAccount.BulbFrom(lumosList);
        bool noAddr = TapoAccount.BulbFrom(F("e.json", "{\"devices\": []}")) == null && TapoAccount.BulbFrom(F("f.json", "{\"devices\": [{\"host\": \"not an address\"}]}")) == null &&
                      TapoAccount.BulbFrom(F("g.json", "{\"host\": \"10.0.0.96\"}")) == null && TapoAccount.BulbFrom(Path.Combine(td, "none.json")) == null;
        char[] tu = " made-up@example.com ".ToCharArray(), tp = "made-up password".ToCharArray();
        bool typed = Same(TapoAccount.Unprotect(TapoAccount.FromTyped(tu, tp)), want);
        bool typedNone = TapoAccount.FromTyped("  ".AsSpan(), tp) == null && TapoAccount.FromTyped(tu, ReadOnlySpan<char>.Empty) == null;
        Directory.Delete(td, true);
        Log.Info($"lights account: the key from a settings file - Hexpad's shape {hex} (want True), with a UTF-8 BOM {hexBom} (want True), Lumos's shape with " +
                 $"escapes {esc} (want True), held open by its writer {held} (want True); none where there is none {none} (want True)");
        Tools.Want($"lights account: the bulb's address from Lumos's list '{addr}' (want '10.0.0.98'), none where there is none {noAddr} (want True); the account typed " +
                   $"(its e-mail trimmed) the same key {typed} (want True), an empty box none {typedNone} (want True)",
            addr == "10.0.0.98" && noAddr && typed && typedNone);
    }
}
