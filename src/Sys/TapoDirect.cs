using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Orbit;

// The Tapo bulb without Lumos (Erik 10-17: "Hexpad drives it without Lumos"): Tapo's local protocol - KLAP, as python-kasa speaks it
// to the L630 - straight to the bulb. The bulb checks the TP-Link account by one key, SHA256(SHA1(e-mail) + SHA1(password)), and that
// key is all OrbitOverlay keeps: made once from Hexpad's or Lumos's own settings (TapoAccount.Import, read only - the e-mail and the
// password hashed straight from the file's bytes, never made strings, the bytes cleared after) and kept DPAPI-protected for this Windows
// user in Config.Lights.Key. It cannot sign in to TP-Link.
// It answers in Lumos's words (the same paths and JSON), so LumosLink's commands, coalescing and reading stay as they are.
public sealed class TapoDirect : IDisposable
{
    readonly byte[] _auth;
    // only ever the bulb's own address: a redirect is not followed (whatever answers there must not send the handshake on to another
    // host - it is a "Bulb error 3xx" instead). An answer is at most 64 KB (the bulb's are under 4 KB; HttpClient would take up to 2 GB
    // of anything that answers there, every poll): a bigger one is not read ("The bulb's answer could not be read").
    readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(3) })
        { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = MaxAnswer };
    const int MaxAnswer = 64 * 1024;
    readonly string _uuid = Convert.ToBase64String(MD5.HashData(Guid.NewGuid().ToByteArray()));
    Session _s;

    sealed class Session
    {
        public string Host, Cookie;
        public byte[] Key, Iv, Sig;
        public int Seq;
        public DateTime Until;
    }

    sealed class AuthException : Exception { }
    sealed class BulbException(string m) : Exception(m) { }

    public TapoDirect(byte[] key) { _auth = key; }

    // its HttpClient and the connection to the bulb it keeps (LumosLink lets one go when a new key replaces it, and at the end)
    public void Dispose() { Disposed = true; _http.Dispose(); }
    public bool Disposed { get; private set; }

    // a LumosLink call ("GET /api/state?host=..", "POST /api/power {host, on}" ..): the bulb's state as Lumos would give it
    public (int code, string body) Handle(string method, string path, string json)
    {
        try
        {
            string host;
            Dictionary<string, object> set = null;
            if (method == "GET")
            {
                int q = path.IndexOf("host=", StringComparison.Ordinal);
                host = q < 0 ? "" : Uri.UnescapeDataString(path[(q + 5)..]);
            }
            else
            {
                using var d = JsonDocument.Parse(json);
                var r = d.RootElement;
                host = r.GetProperty("host").GetString() ?? "";
                int I(string k) => r.GetProperty(k).GetInt32();
                set = path switch
                {
                    "/api/power" => new() { ["device_on"] = r.GetProperty("on").GetBoolean() },
                    "/api/brightness" => new() { ["device_on"] = true, ["brightness"] = Math.Clamp(I("value"), 1, 100) },
                    "/api/color_temp" => new() { ["device_on"] = true, ["color_temp"] = I("temp") },
                    "/api/hsv" => new() { ["device_on"] = true, ["hue"] = I("h"), ["saturation"] = I("s"), ["brightness"] = Math.Clamp(I("v"), 1, 100), ["color_temp"] = 0 },
                    _ => throw new BulbException("Not a bulb command"),
                };
            }
            if (host.Length == 0) return (400, Error("No bulb set"));
            if (set != null) Call(host, "set_device_info", set);
            return (200, AsLumos(Call(host, "get_device_info", null)));
        }
        catch (AuthException) { _s = null; return (401, Error("Wrong Tapo account")); }
        catch (BulbException e) { _s = null; return (500, Error(e.Message)); }
        // (JsonElement's own: a value of another type or size than the bulb's - they must not show .NET's words on the watch; the last one:
        // an answer over MaxAnswer)
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException ||
                                  e is HttpRequestException { HttpRequestError: HttpRequestError.ConfigurationLimitExceeded })
        {
            _s = null;
            return (500, Error(Unreadable));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException or SocketException or IOException)
        {
            _s = null;
            return (504, Error("The bulb did not answer"));
        }
    }

    static string Error(string m) => JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = m });
    const string Unreadable = "The bulb's answer could not be read";

    // ---- the protocol ----

    JsonElement Call(string host, string method, Dictionary<string, object> args)
    {
        for (int attempt = 0; ; attempt++)
        {
            var s = _s;
            if (s == null || s.Host != host || DateTime.UtcNow > s.Until) s = _s = Handshake(host);
            var req = new Dictionary<string, object>
            {
                ["method"] = method,
                ["request_time_milis"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["terminal_uuid"] = _uuid,
            };
            if (args != null) req["params"] = args;
            int seq = ++s.Seq;
            using var msg = new HttpRequestMessage(HttpMethod.Post, $"http://{host}/app/request?seq={seq}")
                { Content = new ByteArrayContent(Encrypt(s, seq, JsonSerializer.SerializeToUtf8Bytes(req))) };
            msg.Headers.Add("Cookie", s.Cookie);
            using var res = _http.Send(msg);
            int status = (int)res.StatusCode;
            if (status is 401 or 403 && attempt == 0) { _s = null; continue; }      // the session ended: a new one
            if (status != 200) throw new BulbException("Bulb error " + status);
            using var doc = JsonDocument.Parse(Decrypt(s, seq, Body(res)));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new BulbException(Unreadable);
            int code = 0;
            if (root.TryGetProperty("error_code", out var ec) && ec.ValueKind == JsonValueKind.Number && !ec.TryGetInt32(out code)) throw new BulbException(Unreadable);
            if (code != 0)
            {
                if (attempt == 0 && code is 9999 or 1100 or -1005) { _s = null; continue; }     // session timed out / its keys: again
                throw new BulbException("Bulb error " + code);
            }
            return root.TryGetProperty("result", out var result) ? result.Clone() : default;
        }
    }

    Session Handshake(string host)
    {
        var local = RandomNumberGenerator.GetBytes(16);
        var (c1, b1, cookie, timeout) = Post(host, "/app/handshake1", local, null);
        if (c1 != 200 || b1.Length < 48) throw new BulbException("Bulb error " + c1);
        byte[] remote = b1[..16], said = b1[16..48];
        if (!CryptographicOperations.FixedTimeEquals(said, Sha(local, remote, _auth))) throw new AuthException();
        if (cookie == null) throw new BulbException("The bulb gave no session");
        var (c2, _, _, _) = Post(host, "/app/handshake2", Sha(remote, local, _auth), cookie);
        if (c2 != 200) throw new AuthException();
        var both = Join(local, remote, _auth);
        var iv = Sha("iv"u8.ToArray(), both);
        return new Session
        {
            Host = host,
            Cookie = cookie,
            Key = Sha("lsk"u8.ToArray(), both)[..16],
            Iv = iv[..12],
            Seq = BinaryPrimitives.ReadInt32BigEndian(iv.AsSpan(28)),
            Sig = Sha("ldk"u8.ToArray(), both)[..28],
            Until = DateTime.UtcNow.AddSeconds(Math.Max(60, timeout - 120)),
        };
    }

    (int code, byte[] body, string cookie, int timeout) Post(string host, string path, byte[] body, string cookie)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"http://{host}{path}") { Content = new ByteArrayContent(body) };
        if (cookie != null) msg.Headers.Add("Cookie", cookie);
        using var res = _http.Send(msg);
        string got = null;
        int timeout = 86400;
        if (res.Headers.TryGetValues("Set-Cookie", out var all))
            foreach (var v in all)
                foreach (var part in v.Split(';'))
                {
                    var t = part.Trim();
                    if (t.StartsWith("TP_SESSIONID=", StringComparison.Ordinal)) got = t;
                    else if (t.StartsWith("TIMEOUT=", StringComparison.Ordinal) && int.TryParse(t[8..], out int n)) timeout = n;
                }
        return ((int)res.StatusCode, Body(res), got, timeout);
    }

    static byte[] Body(HttpResponseMessage res)
    {
        using var ms = new MemoryStream();
        res.Content.ReadAsStream().CopyTo(ms);
        return ms.ToArray();
    }

    static byte[] IvFor(Session s, int seq)
    {
        var iv = new byte[16];
        s.Iv.CopyTo(iv, 0);
        BinaryPrimitives.WriteInt32BigEndian(iv.AsSpan(12), seq);
        return iv;
    }

    // AES-128-CBC with the sequence in the IV, signed: SHA256(sig + seq + ciphertext) + ciphertext
    static byte[] Encrypt(Session s, int seq, byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Key = s.Key;
        var ct = aes.EncryptCbc(plain, IvFor(s, seq), PaddingMode.PKCS7);
        var sq = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(sq, seq);
        return Join(Sha(s.Sig, sq, ct), ct);
    }

    static byte[] Decrypt(Session s, int seq, byte[] body)
    {
        if (body.Length <= 32) throw new BulbException("The bulb's answer was empty");
        using var aes = Aes.Create();
        aes.Key = s.Key;
        return aes.DecryptCbc(body.AsSpan(32), IvFor(s, seq), PaddingMode.PKCS7);
    }

    static byte[] Join(params byte[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var r = new byte[n];
        n = 0;
        foreach (var p in parts) { p.CopyTo(r, n); n += p.Length; }
        return r;
    }

    static byte[] Sha(params byte[][] parts) => SHA256.HashData(Join(parts));

    // the bulb's get_device_info as Lumos's /api/state answer (LumosLink.Parse)
    public static string AsLumos(JsonElement i)
    {
        // (a number that is no int - 50.5, 1e12 - is as good as none)
        int? Int(string k) => i.ValueKind == JsonValueKind.Object && i.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;
        bool Has(string k) => i.ValueKind == JsonValueKind.Object && i.TryGetProperty(k, out _);
        var o = new Dictionary<string, object>
        {
            ["is_on"] = i.ValueKind == JsonValueKind.Object && i.TryGetProperty("device_on", out var on) && on.ValueKind == JsonValueKind.True,
            ["has_light"] = Has("brightness"),
            ["has_color"] = Has("hue"),
            ["has_color_temp"] = Has("color_temp"),
        };
        if (Int("brightness") is int b) o["brightness"] = b;
        if (Has("hue")) o["hsv"] = new Dictionary<string, int> { ["h"] = Int("hue") ?? 0, ["s"] = Int("saturation") ?? 0, ["v"] = Int("brightness") ?? 0 };
        if (Has("color_temp")) o["color_temp"] = Int("color_temp") ?? 0;
        if (i.ValueKind == JsonValueKind.Object && i.TryGetProperty("color_temp_range", out var r) && r.ValueKind == JsonValueKind.Array && r.GetArrayLength() == 2 &&
            r[0].ValueKind == JsonValueKind.Number && r[1].ValueKind == JsonValueKind.Number && r[0].TryGetInt32(out int lo) && r[1].TryGetInt32(out int hi))
        {
            o["ct_min"] = lo;
            o["ct_max"] = hi;
        }
        if (i.ValueKind == JsonValueKind.Object && i.TryGetProperty("nickname", out var nick) && nick.ValueKind == JsonValueKind.String)
            try { o["alias"] = Encoding.UTF8.GetString(Convert.FromBase64String(nick.GetString() ?? "")); } catch (FormatException) { }
        return JsonSerializer.Serialize(o);
    }
}

// The bulb's key: made from the account Hexpad or Lumos already keeps (read only), kept DPAPI-protected (this Windows user only)
public static class TapoAccount
{
    static readonly byte[] Entropy = "OrbitOverlay.TapoKey.v1"u8.ToArray();

    // SHA256(SHA1(e-mail) + SHA1(password)): what the bulb checks (KLAP v2). (From strings: the tools' made-up accounts only - a real
    // one is never made a string, KeyFrom.)
    public static byte[] KeyFor(string user, string password) => KeyFor(Encoding.UTF8.GetBytes(user), Encoding.UTF8.GetBytes(password));

    public static byte[] KeyFor(ReadOnlySpan<byte> user, ReadOnlySpan<byte> password)
    {
        Span<byte> both = stackalloc byte[40];
        SHA1.HashData(user, both[..20]);
        SHA1.HashData(password, both[20..]);
        var key = SHA256.HashData(both);
        CryptographicOperations.ZeroMemory(both);          // (the password's SHA1 is as good as the password to the bulb)
        return key;
    }

    // from Hexpad's settings, else Lumos's: the protected key and where it came from (null: no account found). Nothing is logged.
    public static string Import(out string from)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var places = new (string who, string path, string section, string user)[]
        {
            ("Hexpad", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AjazzDock", "config.json"), "tapo", "email"),
            ("Lumos", Path.Combine(home, "Desktop", "project", "Lumos", "dist", "config.json"), null, "username"),
            ("Lumos", Path.Combine(home, "Desktop", "project", "Lumos", "config.json"), null, "username"),
        };
        foreach (var (who, path, section, userKey) in places)
        {
            var key = KeyFrom(path, section, userKey);
            if (key == null) continue;
            from = who;
            try { return Protect(key); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        from = null;
        return null;
    }

    // the key from one app's settings file - the account at its top, or in its 'section': userKey and "password" (null: none there). The
    // e-mail and the password are hashed straight from the bytes read and never made strings (a .NET string cannot be wiped: it would
    // stay readable in the process until the GC happens to reuse its memory); every buffer that held them is cleared after. Read the way
    // Weather reads Hexpad's file: shared (the app may be writing it right then) and a UTF-8 BOM skipped (Hexpad's has carried one).
    internal static byte[] KeyFrom(string path, string section, string userKey)
    {
        byte[] file = null, user = null, pass = null;
        int userLen = 0, passLen = 0;
        try
        {
            if (!File.Exists(path)) return null;
            int len;
            // (no FileStream buffer of its own: the bytes are only ever in 'file')
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 0))
            {
                if (fs.Length > 1 << 20) return null;               // (a settings file is a few KB)
                file = new byte[fs.Length];
                len = fs.ReadAtLeast(file, file.Length, throwOnEndOfStream: false);
            }
            var json = file.AsSpan(0, len);
            if (json.StartsWith("\uFEFF"u8)) json = json[3..];          // (a UTF-8 BOM)
            var rd = new Utf8JsonReader(json);
            if (!rd.Read() || rd.TokenType != JsonTokenType.StartObject) return null;
            if (section != null && !Into(ref rd, section)) return null;
            // the account's object, property by property (a value that is an object or a list skipped whole)
            while (rd.Read() && rd.TokenType == JsonTokenType.PropertyName)
            {
                bool isUser = rd.ValueTextEquals(userKey), isPass = !isUser && rd.ValueTextEquals("password"u8);
                rd.Read();
                if ((isUser || isPass) && rd.TokenType == JsonTokenType.String)
                {
                    var v = new byte[rd.ValueSpan.Length];          // unescaped into a buffer of ours (never longer than as written)
                    int n = rd.CopyString(v);
                    if (isUser) { Clear(user); (user, userLen) = (v, n); }
                    else { Clear(pass); (pass, passLen) = (v, n); }
                }
                else rd.Skip();
            }
            return userLen == 0 || passLen == 0 ? null : KeyFor(user.AsSpan(0, userLen), pass.AsSpan(0, passLen));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
        finally { Clear(file); Clear(user); Clear(pass); }
    }

    // from the top object's start onto the value of its property 'name', when that is an object
    static bool Into(ref Utf8JsonReader rd, string name)
    {
        while (rd.Read() && rd.TokenType == JsonTokenType.PropertyName)
        {
            bool it = rd.ValueTextEquals(name);
            rd.Read();
            if (it) return rd.TokenType == JsonTokenType.StartObject;
            rd.Skip();
        }
        return false;
    }

    static void Clear(byte[] b) { if (b != null) CryptographicOperations.ZeroMemory(b); }

    public static string Protect(byte[] key) => Convert.ToBase64String(ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));

    // null: none, or not this Windows user's
    public static byte[] Unprotect(string kept)
    {
        if (string.IsNullOrEmpty(kept)) return null;
        try { return ProtectedData.Unprotect(Convert.FromBase64String(kept), Entropy, DataProtectionScope.CurrentUser); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return null; }
    }
}
