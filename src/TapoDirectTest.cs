using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Orbit;

// --snapshot's part for the bulb driven straight (Sys/TapoDirect.cs: KLAP, the DPAPI-kept key) - never the real bulb, never a real
// account: a made-up bulb of the tool's own on 127.0.0.1 (a port the system gives, plain HTTP as the bulb speaks it) with a made-up
// account, the protocol worked out here on its own, as python-kasa's KlapTransportV2 does it: the key SHA256(SHA1(user) + SHA1(password)),
// handshake1 (the client's seed, the bulb's seed and SHA256(local + remote + key), its session cookie), handshake2 (SHA256(remote + local
// + key)), then lsk / iv / ldk from local + remote + key, the sequence from the iv's last 4 bytes (big-endian, signed), each request
// AES-128-CBC with iv[..12] + seq, signed SHA256(ldk[..28] + seq + ciphertext) - the bulb checks the sequence and the signature of every
// request and answers with the same sequence. Then: the state read as Lumos's, each command as the bulb's set_device_info, a session the
// bulb forgot (403) / timed out (error 9999) / an HTTP 401 - a new handshake and the call again, twice is an error; a garbled answer; a
// wrong account; no bulb there; the key kept DPAPI-protected and read back, a broken one refused; and LumosLink going straight to it.
static class TapoChecks
{
    // the made-up account (nobody's): only its key goes anywhere, and only to the made-up bulb
    const string User = "made-up@example.com", Password = "made-up password";

    // a bulb on 127.0.0.1: one request a connection (Connection: close), answered on its own thread
    sealed class FakeBulb : IDisposable
    {
        readonly TcpListener _l = new(IPAddress.Loopback, 0);
        readonly Thread _t;
        readonly byte[] _auth;
        volatile bool _stop;
        // the session: the seeds of the last handshake1, confirmed by handshake2, the keys worked out from them
        byte[] _local, _remote, _key, _iv, _sig;
        string _cookie;
        bool _confirmed;
        int _seq;
        int _sessions;

        public string Host { get; }
        public int Handshakes, Requests, BadRequests;            // handshake2s done, requests answered, requests refused (signature, sequence)
        public int Forget, Error9999, Status401, Garble;          // the next N requests: the session forgotten (403), error 9999, HTTP 401, a garbled answer
        public readonly List<(string method, string args)> Calls = new();     // what the bulb was asked: the method, its params as JSON
        // its state, as an L630 has it
        public bool On = true;
        public int Bright = 70, Hue = 30, Sat = 90, Temp = 2700;
        public string Name = "Made-up lámpa";

        public FakeBulb(byte[] auth)
        {
            _auth = auth;
            _l.Start();
            Host = "127.0.0.1:" + ((IPEndPoint)_l.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
            _t = new Thread(Serve) { IsBackground = true, Name = "made-up bulb" };
            _t.Start();
        }

        public void Dispose()
        {
            _stop = true;
            _l.Stop();
            _t.Join(1000);
        }

        static byte[] Sha(params byte[][] parts) => SHA256.HashData(parts.SelectMany(p => p).ToArray());
        static byte[] Be(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); return b; }

        void Serve()
        {
            while (!_stop)
            {
                TcpClient c;
                try { c = _l.AcceptTcpClient(); }
                catch (Exception) { return; }
                try
                {
                    using (c)
                    {
                        c.ReceiveTimeout = 3000;
                        var s = c.GetStream();
                        var (path, headers, body) = Read(s);
                        if (path == null) continue;
                        var (code, answer, extra) = Answer(path, headers, body);
                        string reason = code switch { 200 => "OK", 401 => "Unauthorized", 403 => "Forbidden", _ => "Bad Request" };
                        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Type: application/octet-stream\r\nContent-Length: {answer.Length}\r\n" +
                                                          extra + "Connection: close\r\n\r\n");
                        s.Write(head);
                        s.Write(answer);
                        s.Flush();
                    }
                }
                catch (Exception e) { Log.Warn("lights tapo: the made-up bulb: " + e.Message); }
            }
        }

        // a request: its path, its headers (lower-case names), its body
        static (string path, Dictionary<string, string> headers, byte[] body) Read(NetworkStream s)
        {
            var buf = new List<byte>();
            int b;
            while ((b = s.ReadByte()) >= 0)
            {
                buf.Add((byte)b);
                int n = buf.Count;
                if (n >= 4 && buf[n - 4] == '\r' && buf[n - 3] == '\n' && buf[n - 2] == '\r' && buf[n - 1] == '\n') break;
            }
            if (b < 0) return (null, null, null);
            var lines = Encoding.ASCII.GetString(buf.ToArray()).Split("\r\n");
            var first = lines[0].Split(' ');
            var headers = new Dictionary<string, string>();
            foreach (var l in lines.Skip(1))
            {
                int k = l.IndexOf(':');
                if (k > 0) headers[l[..k].Trim().ToLowerInvariant()] = l[(k + 1)..].Trim();
            }
            int len = headers.TryGetValue("content-length", out var cl) ? int.Parse(cl, CultureInfo.InvariantCulture) : 0;
            var body = new byte[len];
            for (int got = 0; got < len;)
            {
                int r = s.Read(body, got, len - got);
                if (r <= 0) break;
                got += r;
            }
            return (first.Length > 1 ? first[1] : "", headers, body);
        }

        (int code, byte[] body, string extra) Answer(string path, Dictionary<string, string> headers, byte[] body)
        {
            headers.TryGetValue("cookie", out var cookie);
            if (path == "/app/handshake1")
            {
                if (body.Length != 16) return (400, Array.Empty<byte>(), "");
                _local = body;
                _remote = RandomNumberGenerator.GetBytes(16);
                _cookie = "TP_SESSIONID=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + "_" + ++_sessions;
                _confirmed = false;
                return (200, _remote.Concat(Sha(_local, _remote, _auth)).ToArray(), $"Set-Cookie: {_cookie};TIMEOUT=86400\r\n");
            }
            if (path == "/app/handshake2")
            {
                if (_local == null || cookie != _cookie || !body.SequenceEqual(Sha(_remote, _local, _auth))) return (403, Array.Empty<byte>(), "");
                var both = _local.Concat(_remote).Concat(_auth).ToArray();
                _key = Sha("lsk"u8.ToArray(), both)[..16];
                var iv = Sha("iv"u8.ToArray(), both);
                _iv = iv[..12];
                _seq = BinaryPrimitives.ReadInt32BigEndian(iv.AsSpan(28));
                _sig = Sha("ldk"u8.ToArray(), both)[..28];
                _confirmed = true;
                Handshakes++;
                return (200, Array.Empty<byte>(), "");
            }
            if (!path.StartsWith("/app/request?seq=", StringComparison.Ordinal)) return (400, Array.Empty<byte>(), "");
            if (!_confirmed || cookie != _cookie) return (403, Array.Empty<byte>(), "");
            if (Forget > 0) { Forget--; _confirmed = false; return (403, Array.Empty<byte>(), ""); }      // the bulb restarted: no such session
            if (Status401 > 0) { Status401--; _confirmed = false; return (401, Array.Empty<byte>(), ""); }
            // the sequence: the next one, in the URL and in the signature; the signature over it and the ciphertext
            if (!int.TryParse(path["/app/request?seq=".Length..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int seq) || seq != _seq + 1 ||
                body.Length <= 32 || !body.AsSpan(0, 32).SequenceEqual(Sha(_sig, Be(seq), body[32..])))
            {
                BadRequests++;
                return (400, Array.Empty<byte>(), "");
            }
            _seq = seq;
            using var aes = Aes.Create();
            aes.Key = _key;
            var ivSeq = _iv.Concat(Be(seq)).ToArray();
            using var req = JsonDocument.Parse(aes.DecryptCbc(body.AsSpan(32), ivSeq, PaddingMode.PKCS7));
            var r = req.RootElement;
            string method = r.GetProperty("method").GetString();
            bool stamped = r.TryGetProperty("request_time_milis", out var ms) && ms.ValueKind == JsonValueKind.Number &&
                           r.TryGetProperty("terminal_uuid", out var tu) && tu.ValueKind == JsonValueKind.String;
            string args = r.TryGetProperty("params", out var p) ? p.GetRawText() : "";
            lock (Calls) Calls.Add((stamped ? method : method + " (no time / terminal)", args));
            Requests++;
            object answer;
            if (Error9999 > 0) { Error9999--; _confirmed = false; answer = new Dictionary<string, object> { ["error_code"] = 9999 }; }
            else if (method == "get_device_info") answer = new Dictionary<string, object> { ["error_code"] = 0, ["result"] = Info() };
            else if (method == "set_device_info")
            {
                foreach (var kv in p.EnumerateObject())
                    switch (kv.Name)
                    {
                        case "device_on": On = kv.Value.GetBoolean(); break;
                        case "brightness": Bright = kv.Value.GetInt32(); break;
                        case "hue": Hue = kv.Value.GetInt32(); break;
                        case "saturation": Sat = kv.Value.GetInt32(); break;
                        case "color_temp": Temp = kv.Value.GetInt32(); break;
                    }
                answer = new Dictionary<string, object> { ["error_code"] = 0 };
            }
            else answer = new Dictionary<string, object> { ["error_code"] = -1 };
            var ct = aes.EncryptCbc(JsonSerializer.SerializeToUtf8Bytes(answer), ivSeq, PaddingMode.PKCS7);
            if (Garble > 0) { Garble--; for (int i = ct.Length - 16; i < ct.Length; i++) ct[i] ^= 0x5A; }       // its last block garbled on the way
            return (200, Sha(_sig, Be(seq), ct).Concat(ct).ToArray(), "");
        }

        // get_device_info as an L630 answers it (made-up ids; the name base64, as the bulb keeps it)
        Dictionary<string, object> Info() => new()
        {
            ["device_id"] = "made-up-0000", ["fw_ver"] = "1.0.0 Build 000000 Rel.00000", ["hw_ver"] = "1.0", ["type"] = "SMART.TAPOBULB", ["model"] = "L630",
            ["mac"] = "00-00-00-00-00-00", ["color_temp_range"] = new[] { 2200, 6500 }, ["overheated"] = false, ["ip"] = "127.0.0.1", ["rssi"] = -50,
            ["signal_level"] = 3, ["device_on"] = On, ["brightness"] = Bright, ["hue"] = Hue, ["saturation"] = Sat, ["color_temp"] = Temp,
            ["dynamic_light_effect_enable"] = false, ["nickname"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(Name)), ["avatar"] = "bulb",
        };

        public (string method, string args) Last() { lock (Calls) return Calls.Count == 0 ? ("", "") : Calls[^1]; }
    }

    static bool Wait(Func<bool> ok, int ms = 3000)
    {
        for (int i = 0; i < ms / 20 && !ok(); i++) Thread.Sleep(20);
        return ok();
    }

    public static void Run()
    {
        var key = TapoAccount.KeyFor(User, Password);
        using var bulb = new FakeBulb(key);
        string host = bulb.Host;
        var d = new TapoDirect(key);
        (int code, LampState s) Ask(string method, string path, string json)
        {
            var (c, b) = d.Handle(method, path, json);
            return (c, LumosLink.Parse(c, b));
        }
        string Q(string s) => JsonSerializer.Serialize(s);

        // the state, read as Lumos's answer; read again on the same session
        var (c0, s0) = Ask("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);
        int hs0 = bulb.Handshakes;
        var (c1, s1) = Ask("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);
        bool read = c0 == 200 && s0.Ready && s0.On && s0.Brightness == 70 && s0.HasColor && s0.HasTemp && s0.Temp == 2700 && s0.Hue == 30 && s0.Sat == 90 &&
                    s0.TempMin == 2200 && s0.TempMax == 6500 && s0.Name == "Made-up lámpa";
        bool asked = bulb.Last() == ("get_device_info", "") && bulb.Calls.All(x => !x.method.Contains("no time"));
        Tools.Want($"lights tapo: a made-up bulb on {host} (KLAP worked out on its own) - the state read {c0} (want 200) as Lumos's: on, 70 %, a colour and " +
                   $"a white, 2700 K in {s0.TempMin}-{s0.TempMax} K (want 2200-6500), named '{s0.Name}' (want Made-up lámpa) {read}; asked get_device_info with its " +
                   $"time and terminal {asked}; read again on the same session {c1 == 200 && s1.Ready && hs0 == 1 && bulb.Handshakes == 1} (handshakes {bulb.Handshakes}, " +
                   $"want 1); every request's sequence and signature right {bulb.BadRequests == 0} -",
                   read && asked && c1 == 200 && s1.Ready && hs0 == 1 && bulb.Handshakes == 1 && bulb.BadRequests == 0);

        // the commands: each as the bulb's set_device_info, then the state
        string Set() { var calls = bulb.Calls; lock (calls) { var x = calls.LastOrDefault(c => c.method == "set_device_info"); return x.args ?? ""; } }
        // (its keys in order: the bulb's params compared as JSON, whatever order they were written in)
        static string J(string json)
        {
            try { return JsonSerializer.Serialize(JsonSerializer.Deserialize<SortedDictionary<string, JsonElement>>(json)); }
            catch (JsonException) { return "(none)"; }
        }
        var (ch, sh) = Ask("POST", "/api/hsv", $"{{\"host\":{Q(host)},\"h\":285,\"s\":80,\"v\":55}}");
        string hsv = J(Set());
        bool colour = ch == 200 && sh.Ready && sh.Hue == 285 && sh.Sat == 80 && sh.Brightness == 55 && sh.Temp == 0 &&
                      hsv == J("{\"device_on\":true,\"hue\":285,\"saturation\":80,\"brightness\":55,\"color_temp\":0}");
        var (cw, sw) = Ask("POST", "/api/color_temp", $"{{\"host\":{Q(host)},\"temp\":4000}}");
        string white = J(Set());
        bool warm = cw == 200 && sw.Temp == 4000 && white == J("{\"device_on\":true,\"color_temp\":4000}");
        var (cb, sb) = Ask("POST", "/api/brightness", $"{{\"host\":{Q(host)},\"value\":150}}");
        string bright = J(Set());
        bool brighter = cb == 200 && sb.Brightness == 100 && bright == J("{\"device_on\":true,\"brightness\":100}");
        var (cp, sp) = Ask("POST", "/api/power", $"{{\"host\":{Q(host)},\"on\":false}}");
        string power = J(Set());
        bool off = cp == 200 && sp.Ready && !sp.On && power == J("{\"device_on\":false}");
        int before = bulb.Requests;
        var (cx, sx) = Ask("POST", "/api/config", $"{{\"host\":{Q(host)}}}");
        var (cn, sn) = Ask("GET", "/api/state?host=", null);
        bool refused = cx == 500 && sx.Message == "Not a bulb command" && cn == 400 && sn.Message == "No bulb set" && bulb.Requests == before;
        Tools.Want($"lights tapo: a colour sent as {hsv} {colour}, a white as {white} {warm}, 150 % as {bright} {brighter}, off as {power} {off}; " +
                   $"not a bulb command ({cx} '{sx.Message}') and no bulb set ({cn} '{sn.Message}') ask the bulb nothing {refused} -",
                   colour && warm && brighter && off && refused && bulb.BadRequests == 0);

        // a session ended on the bulb's side: forgotten (403), timed out (error 9999), an HTTP 401 - a new handshake, the call again
        bool Again(Action fault, out int handshakes)
        {
            Ask("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);       // a session first (an error before - "Not a bulb command" - let the last one go)
            int h0 = bulb.Handshakes;
            fault();
            var (c, s) = Ask("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);
            handshakes = bulb.Handshakes - h0;
            return c == 200 && s.Ready && handshakes == 1;
        }
        bool forgot = Again(() => bulb.Forget = 1, out int hf), timedOut = Again(() => bulb.Error9999 = 1, out int ht), unauth = Again(() => bulb.Status401 = 1, out int hu);
        bulb.Error9999 = 2;
        var (c99, s99) = Ask("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);
        bulb.Error9999 = 0;
        bool twice = c99 == 500 && s99.Message == "Bulb error 9999";
        Tools.Want($"lights tapo: the session forgotten by the bulb (403) read again after a new handshake {forgot} ({hf}), timed out (error 9999) {timedOut} ({ht}), " +
                   $"an HTTP 401 {unauth} ({hu}) (want one handshake each); timed out twice in a row: {c99} '{s99.Message}' (want 500 Bulb error 9999) -",
                   forgot && timedOut && unauth && twice && bulb.BadRequests == 0);

        // a garbled answer: an error, then the next call on a new session reads it again
        bulb.Garble = 1;
        var (cg, sg) = Ask("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);
        int hg = bulb.Handshakes;
        var (ca, sa) = Ask("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);
        bool garbled = cg == 500 && sg.Message == "The bulb's answer could not be read" && ca == 200 && sa.Ready && bulb.Handshakes == hg + 1;
        // another account's key: the bulb's handshake1 answer does not match it
        var (cwk, swk) = new TapoDirect(TapoAccount.KeyFor("nobody@example.com", "wrong")).Handle("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);
        var wrong = LumosLink.Parse(cwk, swk);
        // no bulb at that address: a port nothing listens on
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        string gone = "127.0.0.1:" + ((IPEndPoint)free.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
        free.Stop();
        var (cgo, sgo) = d.Handle("GET", "/api/state?host=" + Uri.EscapeDataString(gone), null);
        var none = LumosLink.Parse(cgo, sgo);
        Tools.Want($"lights tapo: a garbled answer {cg} '{sg.Message}' (want 500 The bulb's answer could not be read), the next call read on a new session {ca == 200 && sa.Ready}; " +
                   $"a wrong account {cwk} '{wrong.Message}' (want 401 Wrong Tapo account); no bulb there {cgo} '{none.Message}' (want 504 The bulb did not answer) -",
                   garbled && cwk == 401 && wrong.Message == "Wrong Tapo account" && cgo == 504 && none.Message == "The bulb did not answer");

        // the key kept: DPAPI for this Windows user, read back the same; a broken one, not base64, none - refused (null)
        string kept = TapoAccount.Protect(key);
        var back = TapoAccount.Unprotect(kept);
        var raw = Convert.FromBase64String(kept);
        raw[raw.Length / 2] ^= 0x40;
        bool broken = TapoAccount.Unprotect(Convert.ToBase64String(raw)) == null && TapoAccount.Unprotect("not base64 at all!") == null &&
                      TapoAccount.Unprotect(null) == null && TapoAccount.Unprotect("") == null;
        bool keyOk = TapoAccount.KeyFor(User, Password).SequenceEqual(SHA256.HashData(SHA1.HashData(Encoding.UTF8.GetBytes(User)).Concat(SHA1.HashData(Encoding.UTF8.GetBytes(Password))).ToArray()));
        Tools.Want($"lights tapo: the key SHA256(SHA1(e-mail) + SHA1(password)) {keyOk}, kept protected and read back the same {back != null && back.SequenceEqual(key)}, " +
                   $"a broken / not base64 / empty one refused {broken}, the kept text not the key {!kept.Contains(Convert.ToBase64String(key))} -",
                   keyOk && back != null && back.SequenceEqual(key) && broken && !kept.Contains(Convert.ToBase64String(key)));

        // LumosLink with the key kept: no Lumos asked, straight to the bulb - read, a colour sent, the state as the bulb has it
        int calls0 = bulb.Calls.Count;
        using (var l = new LumosLink { Url = "http://127.0.0.1:1", Poll = 0.2, Retry = 0.2, Slow = 0.2 })
        {
            l.SetKey("not a kept key");
            bool viaLumos = !l.Direct;
            l.SetKey(kept);
            l.SetBulb(host);
            l.Active = true;
            l.Watched = true;
            bool ready = Wait(() => l.State.Ready) && l.Direct && l.State.Name == "Made-up lámpa";
            l.SetColor(120, 50);
            bool sent = Wait(() => !l.State.Pending && bulb.Hue == 120) && l.State.Hue == 120 && l.State.Sat == 50 && J(Set()).Contains("\"hue\":120");
            Tools.Want($"lights tapo: LumosLink with the key kept goes straight to the bulb {l.Direct} (a key not this user's: through Lumos {viaLumos}), read it {ready}, " +
                       $"a colour picked reached it {sent}, {bulb.Calls.Count - calls0} call(s) -", l.Direct && viaLumos && ready && sent && bulb.BadRequests == 0);
        }
    }
}
