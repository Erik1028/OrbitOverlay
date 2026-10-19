using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit;

// The room light and the PC's RGB on the watch (the "light" and "rgb" widgets: Panels/WristLights*.cs) - through Erik's own apps that
// already drive them, never the devices themselves and never with anyone's account:
//  - the light: Lumos (the Tapo bulb's tray app) serves its flyout on http://127.0.0.1:8770 and answers /api/state, /api/power,
//    /api/brightness, /api/color_temp and /api/hsv for a bulb's address. Lumos holds the TP-Link account and signs in to the bulb
//    itself; Orbit only says which bulb (Config.Lights.Bulb, its IP address). /api/config is never asked (its answer carries the
//    account's e-mail), nor anything that writes Lumos's config (discover, connect, forget).
//    With a key kept (Config.Lights.Key, "Use account" in the editor) the same calls go straight to the bulb instead (TapoDirect.cs):
//    Lumos need not run then (Erik 10-17).
//  - the RGB: Prisma (RGBCommander.exe) takes a second launch's command line from its named pipe RGBCommander_Cmd_v1 - one command a
//    connection, the words joined with U+001F ("--effect" "rainbow", "--brightness" "55", "--toggle"), the same as its Stream Deck
//    buttons. The pipe has no answer, so what the widget shows is read from Prisma's own settings.json (read only; Prisma writes it
//    0.8 s after a change). OpenRGB's SDK port is never touched: Prisma streams the devices 25 times a second and would paint over it.
// A thread each, started by the first widget that wants it; a command is shown at once (the state it will be), sent on the thread, and
// only the newest of a kind waits there - a slider dragged is not sixty calls.
public enum LinkStatus { Off, Looking, NoApp, NoDevice, Error, Ready }

// What the light is (a new object for every change: the watch compares Version)
public sealed class LampState
{
    public int Version;
    public LinkStatus Status;
    public bool On;
    public int Brightness;                 // 1..100 (0: not known)
    public bool HasColor, HasTemp;
    public int Hue, Sat;                   // its colour, 0..360 / 0..100 (when Temp is 0)
    public int Temp;                       // its white in kelvin (0: a colour)
    public int TempMin = 2500, TempMax = 6500;
    public string Name = "";               // the bulb's name in the Tapo app
    public string Message = "";            // Lumos's words when the bulb did not answer
    public bool Pending;                   // shown before the bulb's answer
    public bool Ready => Status == LinkStatus.Ready;

    public LampState Copy() => (LampState)MemberwiseClone();

    // the light as a colour (the widget's glow): its white's warmth, or its colour
    public uint Rgb => Temp > 0 || !HasColor ? Kelvin(Temp > 0 ? Temp : 2700) : Theme.Hsv(Hue, Math.Clamp(Sat, 0, 100) / 100f, 1);

    // a white of that many kelvin as a colour (Tanner Helland's fit of the black body)
    public static uint Kelvin(int k)
    {
        double t = Math.Clamp(k, 1000, 40000) / 100.0;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        uint C(double v) => (uint)Math.Clamp((int)Math.Round(v), 0, 255);
        return 0xFF000000 | (C(r) << 16) | (C(g) << 8) | C(b);
    }
}

// The light through Lumos
public sealed class LumosLink : ILampControl, IDisposable
{
    public string Url = "http://127.0.0.1:8770";
    public Func<string, string, string, (int code, string body)> Http;     // method, path, JSON -> the answer (null: HttpClient)
    public double Poll = 5, Slow = 60, Retry = 5;                          // seconds: watched, not watched, Lumos not there

    readonly AutoResetEvent _wake = new(false);
    readonly object _lock = new();
    Thread _thread;
    volatile bool _stop, _active, _watched, _frozen;
    volatile string _bulb = "";
    volatile LampState _pub = new() { Status = LinkStatus.Off };
    volatile TapoDirect _direct;            // a key kept: straight to the bulb (null: through Lumos)
    readonly ConcurrentQueue<TapoDirect> _replaced = new();     // the ones a new key replaced: let go on the thread, between two steps
    string _keyKept;
    int _version;
    HttpClient _http;
    // the commands waiting: the newest of each kind
    bool? _power;
    int _bright, _temp;
    (int h, int s)? _hsv;
    bool _toggle;
    DateTime _due = DateTime.MinValue;

    public LampState State => _pub;

    // a light widget is on a page
    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            _active = value;
            _due = DateTime.MinValue;
            Start();
            _wake.Set();
        }
    }

    // the watch is in view: read the bulb again now, then every Poll seconds
    public bool Watched
    {
        get => _watched;
        set
        {
            if (value == _watched) return;
            _watched = value;
            if (!value) return;
            _due = DateTime.MinValue;
            _wake.Set();
        }
    }

    // Config.Lights.Bulb (asked every frame: only a change wakes the thread)
    public void SetBulb(string host)
    {
        host = host?.Trim() ?? "";
        if (host == _bulb && (_thread != null || _frozen)) return;
        _bulb = host;
        _due = DateTime.MinValue;
        Start();
        _wake.Set();
    }

    // Config.Lights.Key (asked every frame: only a change counts): with one, straight to the bulb; without, through Lumos
    public bool Direct => _direct != null || Environment.GetEnvironmentVariable("ORBIT_FAKE_DIRECT") == "1";

    public void SetKey(string kept)
    {
        if (kept == _keyKept) return;
        _keyKept = kept;
        var key = TapoAccount.Unprotect(kept);
        var old = _direct;
        _direct = key == null ? null : new TapoDirect(key);
        if (old != null) _replaced.Enqueue(old);        // (not disposed here: the thread may be in a call with it)
        Log.Info("lights: " + (key != null ? "straight to the bulb (the Tapo key kept)" : kept != null ? "the kept Tapo key is not this Windows user's: through Lumos" : "through Lumos"));
        _said = LinkStatus.Off;
        _due = DateTime.MinValue;
        Start();
        _wake.Set();
    }

    // --snapshot: no thread; the state as given, and a command only changes it here
    public void Freeze(LampState s)
    {
        _frozen = true;
        Publish(s.Copy());
    }

    void Start()
    {
        if (_thread != null || _stop || _frozen) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "lumos" };
        _thread.Start();
    }

    // ---- the commands (from the watch's thread) ----

    public void Toggle()
    {
        var s = _pub;
        if (s.Ready) { SetPower(!s.On); return; }
        if (_frozen || s.Status is LinkStatus.NoDevice or LinkStatus.Off) return;
        // not read yet (or Lumos was away): read it, then switch
        lock (_lock) _toggle = true;
        _due = DateTime.MinValue;
        _wake.Set();
    }

    public void SetPower(bool on) => Command(s => s.On = on, () => _power = on);

    public void SetBrightness(int v)
    {
        v = Math.Clamp(v, 1, 100);
        Command(s => { s.Brightness = v; s.On = true; }, () => _bright = v);
    }

    public void SetTemp(int k) => Command(s => { s.Temp = Math.Clamp(k, s.TempMin, s.TempMax); s.On = true; }, () => { _temp = k; _hsv = null; });

    public void SetColor(int h, int sat) => Command(s => { s.Hue = h; s.Sat = sat; s.Temp = 0; s.On = true; }, () => { _hsv = (h, sat); _temp = 0; });

    // shown at once (the state it will be), sent on the thread; a light not reached takes none (a tap wakes the thread instead)
    void Command(Action<LampState> show, Action queue)
    {
        var s = _pub;
        if (!s.Ready)
        {
            if (!_frozen) { _due = DateTime.MinValue; _wake.Set(); }
            return;
        }
        var n = s.Copy();
        show(n);
        n.Pending = !_frozen;
        Publish(n);
        if (_frozen) return;
        lock (_lock) queue();
        _wake.Set();
    }

    // ---- the thread ----

    void Run()
    {
        while (!_stop)
        {
            try { Step(); }
            catch (Exception e) { Log.Warn("lights: " + e.Message); _due = DateTime.UtcNow.AddSeconds(Retry); }
            double wait = _active ? Math.Clamp((_due - DateTime.UtcNow).TotalSeconds, 0.05, 60) : 60;
            _wake.WaitOne(TimeSpan.FromSeconds(wait));
        }
    }

    LinkStatus _said = LinkStatus.Off;
    string _saidMsg;

    void Step()
    {
        // the bulb's links a new key replaced: their HttpClient and connection go here, where no call is in flight (each call takes
        // _direct anew, so the one a call had when the key changed is done with by the next step)
        while (_replaced.TryDequeue(out var gone)) gone.Dispose();
        string bulb = _bulb;
        if (bulb.Length == 0)
        {
            lock (_lock) { _power = null; _bright = _temp = 0; _hsv = null; _toggle = false; }
            if (_pub.Status != LinkStatus.NoDevice) Publish(new LampState { Status = LinkStatus.NoDevice });
            Said(LinkStatus.NoDevice);
            return;
        }
        if (!_active) return;
        bool? power; int bright, temp; (int h, int s)? hsv; bool toggle;
        lock (_lock)
        {
            (power, bright, temp, hsv, toggle) = (_power, _bright, _temp, _hsv, _toggle);
            (_power, _bright, _temp, _hsv, _toggle) = (null, 0, 0, null, false);
        }
        bool any = power != null || bright > 0 || temp > 0 || hsv != null;
        var now = DateTime.UtcNow;
        if (!any && !toggle && now < _due) return;
        if (_pub.Status is LinkStatus.Off or LinkStatus.NoDevice) Publish(new LampState { Status = LinkStatus.Looking });
        LampState got = null;
        if (toggle && !any)
        {
            // a tap before the bulb was read: read it, then the other way
            got = Ask("GET", "/api/state?host=" + Uri.EscapeDataString(bulb), null);
            if (got?.Ready == true) power = !got.On;
        }
        if (power != null) got = Ask("POST", "/api/power", Json(bulb, "on", power.Value ? "true" : "false")) ?? got;
        if (temp > 0 && got?.Status != LinkStatus.NoApp) got = Ask("POST", "/api/color_temp", Json(bulb, "temp", temp.ToString(CultureInfo.InvariantCulture))) ?? got;
        if (hsv is { } c && got?.Status != LinkStatus.NoApp)
            got = Ask("POST", "/api/hsv", "{\"host\":" + JsonSerializer.Serialize(bulb) + ",\"h\":" + c.h.ToString(CultureInfo.InvariantCulture) + ",\"s\":" +
                c.s.ToString(CultureInfo.InvariantCulture) + ",\"v\":" + Math.Max(1, _pub.Brightness).ToString(CultureInfo.InvariantCulture) + "}") ?? got;
        if (bright > 0 && got?.Status != LinkStatus.NoApp) got = Ask("POST", "/api/brightness", Json(bulb, "value", bright.ToString(CultureInfo.InvariantCulture))) ?? got;
        if (got == null) got = Ask("GET", "/api/state?host=" + Uri.EscapeDataString(bulb), null);
        bool more;
        lock (_lock) more = _power != null || _bright > 0 || _temp > 0 || _hsv != null || _toggle;
        // newer commands wait: the state they will make stays shown until their own answer
        if (!more || !got.Ready) Publish(got);
        Said(got.Status, got.Message);
        double next = got.Status switch
        {
            LinkStatus.Ready => _watched ? Poll : Slow,
            LinkStatus.NoApp => _watched ? Retry : Slow,
            _ => _watched ? 2 * Retry : Slow,
        };
        _due = DateTime.UtcNow.AddSeconds(next);
        if (more) _wake.Set();
    }

    static string Json(string bulb, string key, string value) => "{\"host\":" + JsonSerializer.Serialize(bulb) + ",\"" + key + "\":" + value + "}";

    // a call: the bulb's state from the answer, or why there is none
    LampState Ask(string method, string path, string json)
    {
        int code; string body;
        try { (code, body) = Call(method, path, json); }
        catch (Exception e) when (Refused(e) || (Http == null && _direct == null && !Listening()))
        {
            return new LampState { Status = LinkStatus.NoApp };
        }
        catch (Exception e)
        {
            return new LampState { Status = LinkStatus.Error, Message = e is TaskCanceledException or TimeoutException ? (_direct != null ? "The bulb did not answer" : "Lumos did not answer") : e.Message };
        }
        try { return Parse(code, body); }
        catch (Exception) { return new LampState { Status = LinkStatus.Error, Message = "Not Lumos on port 8770" }; }
    }

    // Lumos's port has a listener (Windows takes ~2 s to refuse a closed one on this PC: the connect gave up first and it looked like
    // Lumos not answering)
    bool Listening()
    {
        try
        {
            int port = new Uri(Url).Port;
            foreach (var ep in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                if (ep.Port == port) return true;
            return false;
        }
        catch (Exception) { return true; }
    }

    // nothing listens there: Lumos is not running
    static bool Refused(Exception e)
    {
        for (var x = e; x != null; x = x.InnerException)
            if (x is SocketException se && se.SocketErrorCode is SocketError.ConnectionRefused or SocketError.ConnectionReset) return true;
        return false;
    }

    // Lumos's answer: the bulb's state (state_dict), or {"error": "..."}
    public static LampState Parse(int code, string body)
    {
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        if (r.TryGetProperty("error", out var err))
            return new LampState { Status = LinkStatus.Error, Message = err.ValueKind == JsonValueKind.String ? err.GetString() ?? "" : "Error " + code };
        if (code >= 400 || !r.TryGetProperty("is_on", out var on)) return new LampState { Status = LinkStatus.Error, Message = "Error " + code };
        var s = new LampState { Status = LinkStatus.Ready, On = on.ValueKind == JsonValueKind.True };
        if (r.TryGetProperty("has_light", out var hl) && hl.ValueKind == JsonValueKind.False)
            return new LampState { Status = LinkStatus.Error, Message = "Not a light" };
        s.HasColor = r.TryGetProperty("has_color", out var hc) && hc.ValueKind == JsonValueKind.True;
        s.HasTemp = r.TryGetProperty("has_color_temp", out var ht) && ht.ValueKind == JsonValueKind.True;
        if (r.TryGetProperty("brightness", out var b) && b.ValueKind == JsonValueKind.Number) s.Brightness = Math.Clamp(b.GetInt32(), 0, 100);
        if (r.TryGetProperty("hsv", out var hsv) && hsv.ValueKind == JsonValueKind.Object)
        {
            if (hsv.TryGetProperty("h", out var h) && h.ValueKind == JsonValueKind.Number) s.Hue = h.GetInt32();
            if (hsv.TryGetProperty("s", out var sa) && sa.ValueKind == JsonValueKind.Number) s.Sat = sa.GetInt32();
        }
        if (r.TryGetProperty("color_temp", out var t) && t.ValueKind == JsonValueKind.Number) s.Temp = t.GetInt32();
        if (r.TryGetProperty("ct_min", out var t0) && t0.ValueKind == JsonValueKind.Number) s.TempMin = t0.GetInt32();
        if (r.TryGetProperty("ct_max", out var t1) && t1.ValueKind == JsonValueKind.Number) s.TempMax = t1.GetInt32();
        if (s.TempMax <= s.TempMin) (s.TempMin, s.TempMax) = (2500, 6500);
        if (r.TryGetProperty("alias", out var a) && a.ValueKind == JsonValueKind.String) s.Name = a.GetString() ?? "";
        return s;
    }

    (int, string) Call(string method, string path, string json)
    {
        if (Http != null) return Http(method, path, json);
        if (_direct is { } d) return d.Handle(method, path, json);
        // no proxy (a system proxy must not see a call to this PC); Lumos waits up to 20 s for a bulb that has to be signed in again
        _http ??= new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
            { Timeout = TimeSpan.FromSeconds(25), BaseAddress = new Uri(Url) };
        using var req = new HttpRequestMessage(method == "GET" ? HttpMethod.Get : HttpMethod.Post, path);
        req.Headers.Add("X-Lumos-Client", "OrbitOverlay");      // who asks (a header a web page cannot send without asking first)
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = _http.Send(req);
        using var rd = new StreamReader(res.Content.ReadAsStream(), Encoding.UTF8);
        return ((int)res.StatusCode, rd.ReadToEnd());
    }

    void Publish(LampState s)
    {
        s.Version = Interlocked.Increment(ref _version);
        _pub = s;
    }

    // the log says when it changes (never the bulb's state, never what Lumos holds)
    void Said(LinkStatus s, string msg = null)
    {
        if (s == _said && msg == _saidMsg) return;
        _said = s;
        _saidMsg = msg;
        bool d = _direct != null;
        Log.Info("lights: " + s switch
        {
            LinkStatus.Ready => d ? "the bulb answers (direct)" : "Lumos answers for the bulb",
            LinkStatus.NoApp => "Lumos is not running",
            LinkStatus.NoDevice => "no bulb set (Settings > Wrist)",
            _ => (d ? "the bulb (direct): " : "Lumos could not reach the bulb: ") + msg,
        });
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(500);
        _http?.Dispose();
        _direct?.Dispose();
        while (_replaced.TryDequeue(out var gone)) gone.Dispose();
    }

    internal TapoDirect DirectForTest => _direct;       // --snapshot: the one asked now (TapoSafetyTest.cs)
}

// What the RGB is (a new object for every change)
public sealed class RgbState
{
    public int Version;
    public LinkStatus Status;
    public string Effect = "Static";       // Prisma's name for it (MainForm.Effect): Static, RainbowWave, Breathing, Off ...
    public uint Color = 0xFF00C8AA;        // its colour (Static, Breathing)
    public int Brightness = 100;           // 1..100
    public bool Pending;
    public bool Ready => Status == LinkStatus.Ready;
    public bool Off => Effect == "Off";

    public RgbState Copy() => (RgbState)MemberwiseClone();

    // the quick choices (the widget's buttons) and what Prisma's command line calls them
    public static readonly string[] Quick = { "Static", "RainbowWave", "Breathing", "Off" };
    public static string Token(string effect) => effect switch
    {
        "Static" => "static", "RainbowWave" => "rainbow", "ColorCycle" => "cycle", "Breathing" => "breathing", "Strobe" => "strobe",
        "Gradient" => "gradient", "Comet" => "comet", "Twinkle" => "twinkle", "Ambient" => "ambient", "Surprise" => "surprise", "Fire" => "fire",
        "Music" => "music", _ => "off",
    };

    // its name on the watch (English; the table has the Magyar), and a short one for a small card
    public static string Name(string effect) => effect switch
    {
        "Static" => "Static colour", "RainbowWave" => "Rainbow wave", "ColorCycle" => "Colour cycle", "Breathing" => "Breathing", "Strobe" => "Strobe",
        "Gradient" => "Gradient", "Comet" => "Comet", "Twinkle" => "Twinkle", "Ambient" => "Screen sync", "Surprise" => "Surprise me", "Fire" => "Fire",
        "Music" => "Music", "Off" => "Off", _ => effect ?? "",
    };

    public static string Short(string effect) => effect switch
    {
        "Static" => "Static", "RainbowWave" => "Rainbow", "ColorCycle" => "Cycle", "Breathing" => "Breathe", "Ambient" => "Screen", "Surprise" => "Surprise",
        _ => Name(effect),
    };

    // the effect paints with the colour picked (else its own colours)
    public static bool UsesColor(string effect) => effect is "Static" or "Breathing" or "Strobe" or "Comet" or "Twinkle";
}

// The RGB through Prisma
public sealed class PrismaLink : IDisposable
{
    public string Pipe = "RGBCommander_Cmd_v1";
    public string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RGBCommander", "settings.json");
    public double Look = 1, Check = 3;                      // seconds: its settings looked at, the pipe looked for
    public const char Sep = (char)0x1F;                     // Prisma's RemoteControl.Sep

    readonly AutoResetEvent _wake = new(false);
    readonly object _lock = new();
    readonly List<string> _queue = new();
    Thread _thread;
    volatile bool _stop, _active, _frozen;
    volatile RgbState _pub = new() { Status = LinkStatus.Off };
    int _version;
    string _lastOn = "Static";                              // what --toggle turns back on (Prisma keeps its own; this is a guess for the screen)

    public RgbState State => _pub;

    // an RGB widget is on a page
    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            _active = value;
            Start();
            _wake.Set();
        }
    }

    // --snapshot: no thread; the state as given, and a command only changes it here
    public void Freeze(RgbState s)
    {
        _frozen = true;
        if (!s.Off) _lastOn = s.Effect;
        Publish(s.Copy());
    }

    void Start()
    {
        if (_thread != null || _stop || _frozen) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "prisma" };
        _thread.Start();
    }

    // ---- the commands (from the watch's thread) ----

    public void Toggle() => Command(s => s.Effect = s.Off ? _lastOn : "Off", "--toggle");

    public void SetEffect(string effect) => Command(s => s.Effect = effect, "--effect" + Sep + RgbState.Token(effect));

    public void SetBrightness(int v)
    {
        v = Math.Clamp(v, 1, 100);
        Command(s => s.Brightness = v, "--brightness" + Sep + v.ToString(CultureInfo.InvariantCulture), true);
    }

    // a colour: Prisma shows it (an effect with colours of its own, or Off, becomes the static colour)
    public void SetColor(uint rgb) => Command(s =>
    {
        s.Color = 0xFF000000 | rgb;
        if (s.Effect is "RainbowWave" or "ColorCycle" or "Off" or "Ambient" or "Surprise" or "Fire") s.Effect = "Static";
    }, "--color" + Sep + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture));

    void Command(Action<RgbState> show, string cmd, bool replaceLast = false)
    {
        var s = _pub;
        if (!s.Ready) { if (!_frozen) _wake.Set(); return; }
        var n = s.Copy();
        show(n);
        if (!n.Off) _lastOn = n.Effect;
        n.Pending = !_frozen;
        Publish(n);
        if (_frozen) return;
        lock (_lock)
        {
            // a slider: only its newest value waits
            if (replaceLast && _queue.Count > 0 && _queue[^1].StartsWith("--brightness", StringComparison.Ordinal)) _queue[^1] = cmd;
            else _queue.Add(cmd);
        }
        _wake.Set();
    }

    // ---- the thread ----

    DateTime _nextCheck = DateTime.MinValue, _fileTime = DateTime.MinValue, _sentAt = DateTime.MinValue;
    int _misses;
    bool _running;
    LinkStatus _said = LinkStatus.Off;

    void Run()
    {
        while (!_stop)
        {
            try { Step(); }
            catch (Exception e) { Log.Warn("prisma: " + e.Message); }
            _wake.WaitOne(TimeSpan.FromSeconds(_active ? Look : 60));
        }
    }

    void Step()
    {
        if (!_active) return;
        var now = DateTime.UtcNow;
        // is it running: its pipe there (looked for, never opened - a connection would be a command); twice not there = gone (the pipe
        // is made again after every command)
        if (now >= _nextCheck)
        {
            _nextCheck = now.AddSeconds(Check);
            bool there = PipeThere(Pipe);
            _misses = there ? 0 : _misses + 1;
            bool was = _running;
            _running = there || (_running && _misses < 2);
            if (_running != was) _fileTime = DateTime.MinValue;       // read its settings again when it comes back
        }
        // the commands, one connection each
        while (_running)
        {
            string cmd;
            lock (_lock)
            {
                if (_queue.Count == 0) break;
                cmd = _queue[0];
                _queue.RemoveAt(0);
            }
            try
            {
                Send(cmd);
                _sentAt = DateTime.UtcNow;
            }
            catch (Exception e)
            {
                Log.Info("prisma: a command not taken (" + e.Message + ")");
                if (e is TimeoutException or FileNotFoundException) { _running = false; _misses = 2; }
                lock (_lock) _queue.Clear();
                break;
            }
        }
        if (!_running)
        {
            lock (_lock) _queue.Clear();
            if (_pub.Status != LinkStatus.NoApp) Publish(new RgbState { Status = LinkStatus.NoApp });
            Said(LinkStatus.NoApp);
            return;
        }
        // what it is: its settings, read when they change (a command's own change only once it is written: until then the screen keeps what
        // it was told - no flicker back to the old one)
        DateTime ft;
        try { ft = File.GetLastWriteTimeUtc(SettingsFile); } catch { ft = DateTime.MinValue; }
        bool hold = DateTime.UtcNow < _sentAt.AddSeconds(2.5) && ft < _sentAt;
        bool queued;
        lock (_lock) queued = _queue.Count > 0;
        if ((ft != _fileTime || !_pub.Ready || _pub.Pending) && !hold && !queued)
        {
            _fileTime = ft;
            var s = Read(SettingsFile);
            if (!s.Off) _lastOn = s.Effect;
            Publish(s);
        }
        Said(LinkStatus.Ready);
    }

    void Send(string cmd)
    {
        using var client = new NamedPipeClientStream(".", Pipe, PipeDirection.Out);
        client.Connect(1500);
        var bytes = Encoding.UTF8.GetBytes(cmd);
        client.Write(bytes, 0, bytes.Length);
        client.Flush();
    }

    // Prisma's settings.json, read only (shared: Prisma swaps it in atomically): the effect, the colour, the brightness - nothing else
    public static RgbState Read(string path)
    {
        var s = new RgbState { Status = LinkStatus.Ready };
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(f);
            var r = doc.RootElement;
            if (r.TryGetProperty("Effect", out var e) && e.ValueKind == JsonValueKind.String) s.Effect = e.GetString() ?? "Static";
            if (r.TryGetProperty("ColorArgb", out var c) && c.ValueKind == JsonValueKind.Number) s.Color = 0xFF000000 | ((uint)c.GetInt32() & 0xFFFFFF);
            if (r.TryGetProperty("Brightness", out var b) && b.ValueKind == JsonValueKind.Number) s.Brightness = Math.Clamp(b.GetInt32(), 1, 100);
        }
        catch (Exception) { }               // none yet / being written: Prisma's defaults
        return s;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WaitNamedPipeW(string name, uint timeout);

    // a pipe of that name has an instance (waiting or busy) - asked of the pipe file system, nothing connects
    public static bool PipeThere(string name)
    {
        if (WaitNamedPipeW(@"\\.\pipe\" + name, 1)) return true;
        return Marshal.GetLastWin32Error() == 121;          // ERROR_SEM_TIMEOUT: there, busy with another
    }

    void Publish(RgbState s)
    {
        s.Version = Interlocked.Increment(ref _version);
        _pub = s;
    }

    void Said(LinkStatus s)
    {
        if (s == _said) return;
        _said = s;
        Log.Info(s == LinkStatus.Ready ? "rgb: Prisma is running" : "rgb: Prisma is not running");
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(500);
    }
}
