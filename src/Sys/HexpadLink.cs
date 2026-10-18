using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit;

// Hexpad (an app for the Ajazz AKP03 stream dock) publishes on the local pipe "Hexpad_mirror_v1" what the dock's
// six keys show, which page / folder it is on and what every key, round button and knob does; we can press keys,
// turn knobs and wake its idle screen. One JSON object per line (see Hexpad's dock/mirror.py).
// api 2 (additive, an api 1 Hexpad simply ignores what it does not know): the faces also drawn at 2x for us, each
// knob says whether it is a slider Hexpad can read and set ("abs") or only turn ("jog"), its value comes in
// "values", and we "set" a value or "jog" a knob by n detents (never accelerated).
// Its OTHER pipe, AjazzDock_ipc_v1, is only ever looked for by name, never opened: any message there but
// "status" pops Hexpad's window on the desktop.
public sealed class HexpadLink : IDisposable
{
    public const string PipeName = "Hexpad_mirror_v1", OldPipe = "AjazzDock_ipc_v1";
    public const int Keys = 6;

    public enum LinkState { Off, NotRunning, OldHexpad, Connecting, Unplugged, ScreenOff, Ok }

    public sealed class Control
    {
        public string Label = "", Left = "", Right = "", Push = "";
        public bool Danger;
        // a knob's turn: "abs" = Hexpad reads and sets a value (api 2), "jog" = turns only, "" = turning does nothing.
        // An api 1 Hexpad does not say: a knob with a left / right action is a "jog" one then.
        public string Slider = "", Kind = "";      // Kind: the turn action's type ("volume", "brightness", "monitor"...)
        public int Max = 100;
    }

    // a knob's value (api 2): V 0..1, null when Hexpad does not know it; Muted (volume), Off (monitors powered off)
    public sealed class Value
    {
        public float? V;
        public bool Muted, Off;
    }

    // one key's picture, upright, premultiplied BGRA; never changed once made
    public sealed class Face
    {
        public readonly uint[] Px;
        public readonly int W, H;
        public Face(uint[] px, int w, int h) { Px = px; W = w; H = h; }
    }

    // what the panel draws; faces, controls and values are replaced, never changed in place, so a copy of the
    // arrays is a safe picture
    public sealed class Snapshot
    {
        public int Version;
        public LinkState State;
        public int Api = 1;                 // what Hexpad's hello said (1 = no sliders, no sharp faces)
        public string[] Caps = Array.Empty<string>();
        public bool Ambient, Dark, InFolder;
        public string Profile = "", PageName = "", FolderName = "";
        public int Page, Pages = 1, FolderPage, FolderPages;
        public Face[] Faces = new Face[Keys];       // the device JPEGs (68 px): the dock's own pixels
        public Face[] HiFaces = new Face[Keys];     // api 2: the same faces drawn at 2x; null = only the device one
        public int[] FaceVersion = new int[Keys];
        public Dictionary<string, Control> Controls = new();
        public Dictionary<string, Value> Values = new();    // "enc0".."enc2" (abs sliders only)
        public string Echo;                 // the last physical input on the dock ("key3", "enc1"...)
        public double EchoAt = -10;

        public Snapshot Copy()
        {
            var s = (Snapshot)MemberwiseClone();
            s.Faces = (Face[])Faces.Clone();
            s.HiFaces = (Face[])HiFaces.Clone();
            s.FaceVersion = (int[])FaceVersion.Clone();
            return s;
        }
    }

    readonly object _lock = new();
    readonly Snapshot _s = new();
    readonly ConcurrentQueue<string> _out = new();
    readonly AutoResetEvent _wake = new(false);
    Thread _thread;
    volatile bool _stop, _active;
    volatile bool _present, _oldPresent;
    volatile int _api = 1;
    // what hello said about the device JPEGs
    int _rot = 90, _dx, _dy;
    bool _mirror;

    // slider values and jogs wait here for the worker: only the newest value per knob, the detents summed up
    readonly object _qlock = new();
    readonly Dictionary<string, (string kind, float v, bool final)> _set = new();
    readonly Dictionary<string, (float v, double at)> _setSent = new();
    readonly Dictionary<string, int> _jog = new();
    double _jogNext;

    // set every tick: VR is on and something shows the dock (its panel or a watch widget)
    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            _active = value;
            if (_thread == null && value) Start();
            _wake.Set();
        }
    }

    // Hexpad's mirror pipe exists (checked every few seconds while VR runs, without connecting)
    public bool Present => _present;
    public bool Connected => Get().State is LinkState.Ok or LinkState.Unplugged or LinkState.ScreenOff;
    // Hexpad sets absolute values (api 2); an older one only turns knobs
    public bool CanSet => _api >= 2;

    public Snapshot Get() { lock (_lock) return _s.Copy(); }
    public int Version { get { lock (_lock) return _s.Version; } }

    // started with the app: while nothing shows the dock it only looks for Hexpad's pipe every 3 s
    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "hexpad link" };
        _thread.Start();
    }

    // ---- what we send ----
    public void Input(string id, bool down) => Send($"{{\"t\":\"input\",\"id\":\"{id}\",\"down\":{(down ? "true" : "false")}}}");
    public void Turn(string id, int dir) => Send($"{{\"t\":\"input\",\"id\":\"{id}\",\"turn\":{(dir > 0 ? 1 : -1)}}}");
    public void Wake() => Send("{\"t\":\"wake\"}");

    // a slider's value 0..1 for a knob whose turn is 'kind' (Hexpad drops it when the knob does something else by
    // now). The newest one per knob wins: at most ~30 a second, only a change of 0.5 % or more; final (on release)
    // always goes out. Only an api 2 Hexpad sets values.
    public void Set(string id, string kind, float value, bool final)
    {
        if (!_active || _api < 2 || string.IsNullOrEmpty(kind) || !float.IsFinite(value)) return;
        lock (_qlock) _set[id] = (kind, Math.Clamp(value, 0, 1), final);
        _wake.Set();
    }

    // turn a knob by n detents, without Hexpad's acceleration: up to 10 per message, ~20 messages a second.
    // An api 1 Hexpad gets single turns, one every 0.1 s (it would speed up turns that arrive together).
    public void Jog(string id, int steps)
    {
        if (!_active || steps == 0) return;
        lock (_qlock)
        {
            // a backlog stays short: an api 1 Hexpad gets at most 10 single turns (a second), api 2 three messages
            int cap = _api >= 2 ? 30 : 10;
            _jog.TryGetValue(id, out int n);
            n = Math.Clamp(n + steps, -cap, cap);
            if (n == 0) _jog.Remove(id); else _jog[id] = n;
        }
        _wake.Set();
    }

    void Send(string line)
    {
        if (!_active) return;
        _out.Enqueue(line);
        _wake.Set();
    }

    void ClearPending()
    {
        while (_out.TryDequeue(out _)) { }
        lock (_qlock) { _set.Clear(); _setSent.Clear(); _jog.Clear(); }
    }

    bool Pending { get { lock (_qlock) return _set.Count > 0 || _jog.Count > 0; } }

    // ---- the worker ----
    static bool PipeExists(string name)
    {
        try
        {
            foreach (var p in Directory.EnumerateFiles(@"\\.\pipe\"))
                if (p.EndsWith(name, StringComparison.OrdinalIgnoreCase) && p.Length >= name.Length + 9 && p[^(name.Length + 1)] == '\\')
                    return true;
        }
        catch { }
        return false;
    }

    void SetState(LinkState st)
    {
        lock (_lock)
        {
            if (_s.State == st) return;
            _s.State = st;
            _s.Version++;
        }
    }

    void Run()
    {
        double nextScan = 0;
        while (!_stop)
        {
            double now = Clock.Now;
            if (now >= nextScan)
            {
                nextScan = now + 3;
                _present = PipeExists(PipeName);
                _oldPresent = !_present && PipeExists(OldPipe);
            }
            if (!_active || !_present)
            {
                ClearPending();
                SetState(!_active ? LinkState.Off : _oldPresent ? LinkState.OldHexpad : LinkState.NotRunning);
                _wake.WaitOne(_active ? 1000 : 3000);
                continue;
            }
            try
            {
                SetState(LinkState.Connecting);
                Session();
            }
            catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException or UnauthorizedAccessException
                                         or JsonException or InvalidOperationException or AggregateException)
            {
                Log.Info("hexpad link: " + e.Message);
            }
            catch (Exception e)
            {
                Log.Warn("hexpad link: " + e);
            }
            if (!_stop) { SetState(LinkState.Connecting); _wake.WaitOne(2000); nextScan = 0; }
        }
        SetState(LinkState.Off);
    }

    // one connection: read lines, write what is queued, until Hexpad goes away or we are not needed any more
    void Session()
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(1500);
        // a new connection starts as api 1 until Hexpad's hello says more; nothing queued for the last one goes out
        ClearPending();
        _api = 1;
        lock (_lock) { _s.Api = 1; _s.Caps = Array.Empty<string>(); _s.Values = new(); _s.HiFaces = new Face[Keys]; _s.Version++; }
        // api 2 and "hi": 2 ask for sliders and the faces at 2x; an api 1 Hexpad ignores both
        Write(pipe, $"{{\"t\":\"hello\",\"client\":\"OrbitOverlay\",\"ver\":\"{Updater.CurrentText}\",\"api\":2,\"hi\":2}}");
        Log.Info("hexpad link: connected");
        var buf = new byte[64 * 1024];
        var line = new MemoryStream();
        Task<int> read = null;
        WaitHandle[] waits = null;
        while (!_stop && _active)
        {
            if (read == null)
            {
                read = pipe.ReadAsync(buf, 0, buf.Length);
                waits = new[] { ((IAsyncResult)read).AsyncWaitHandle, _wake };
            }
            // a line from Hexpad, or something to send (a press goes out at once; a slider value due later wants a
            // short nap)
            if (!read.IsCompleted) WaitHandle.WaitAny(waits, Pending ? 8 : 30);
            if (read.IsCompleted)
            {
                int n = read.Result;
                read.Dispose();
                read = null;
                if (n <= 0) throw new IOException("Hexpad closed the pipe");
                for (int i = 0; i < n; i++)
                {
                    if (buf[i] != (byte)'\n') { line.WriteByte(buf[i]); continue; }
                    if (line.Length > 0) Handle(line.GetBuffer().AsSpan(0, (int)line.Length));
                    line.SetLength(0);
                }
                if (line.Length > 16 * 1024 * 1024) throw new IOException("line too long");
            }
            while (_out.TryDequeue(out var msg)) Write(pipe, msg);
            Flush(pipe);
        }
        Log.Info("hexpad link: closed (not needed)");
    }

    static void Write(Stream pipe, string msg)
    {
        var b = Encoding.UTF8.GetBytes(msg + "\n");
        pipe.Write(b, 0, b.Length);
    }

    // the slider values and jogs that are due
    void Flush(Stream pipe)
    {
        List<string> lines = null;
        double now = Clock.Now;
        lock (_qlock)
        {
            if (_set.Count > 0)
                foreach (var id in new List<string>(_set.Keys))
                {
                    var (kind, v, final) = _set[id];
                    var last = _setSent.TryGetValue(id, out var ls) ? ls : (-1f, -10.0);
                    if (!final)
                    {
                        if (Math.Abs(v - last.Item1) < 0.005f) { _set.Remove(id); continue; }     // nothing new
                        if (now - last.Item2 < 1 / 30.0) continue;                             // next pass
                    }
                    (lines ??= new()).Add($"{{\"t\":\"set\",\"id\":\"{id}\",\"kind\":{JsonSerializer.Serialize(kind)}," +
                                          $"\"value\":{v.ToString("0.0###", CultureInfo.InvariantCulture)},\"final\":{(final ? "true" : "false")}}}");
                    // after a release the next drag starts afresh (even at the same value)
                    _setSent[id] = (final ? -1f : v, now);
                    _set.Remove(id);
                }
            if (_jog.Count > 0 && now >= _jogNext)
            {
                foreach (var id in new List<string>(_jog.Keys))
                {
                    int n = _jog[id];
                    int send = _api >= 2 ? Math.Clamp(n, -10, 10) : Math.Sign(n);
                    (lines ??= new()).Add(_api >= 2
                        ? $"{{\"t\":\"jog\",\"id\":\"{id}\",\"steps\":{send}}}"
                        : $"{{\"t\":\"input\",\"id\":\"{id}\",\"turn\":{send}}}");
                    if (n - send == 0) _jog.Remove(id); else _jog[id] = n - send;
                }
                _jogNext = now + (_api >= 2 ? 0.05 : 0.1);
            }
        }
        if (lines != null) foreach (var l in lines) Write(pipe, l);
    }

    void Handle(ReadOnlySpan<byte> json)
    {
        using var doc = JsonDocument.Parse(json.ToArray());
        var m = doc.RootElement;
        string t = m.TryGetProperty("t", out var tv) ? tv.GetString() : null;
        switch (t)
        {
            case "hello":
                _rot = Int(m, "rot", 90);
                _mirror = m.TryGetProperty("mirror", out var mv) && mv.ValueKind == JsonValueKind.True;
                if (m.TryGetProperty("shift", out var sh) && sh.ValueKind == JsonValueKind.Array && sh.GetArrayLength() == 2)
                {
                    _dx = Int(sh[0], 0);
                    _dy = Int(sh[1], 0);
                }
                int api = Math.Max(1, Int(m, "api", 1));
                var caps = new List<string>();
                if (m.TryGetProperty("caps", out var cv) && cv.ValueKind == JsonValueKind.Array)
                    foreach (var c in cv.EnumerateArray())
                        if (c.ValueKind == JsonValueKind.String) caps.Add(c.GetString());
                _api = api;
                lock (_lock) { _s.Api = api; _s.Caps = caps.ToArray(); _s.Version++; }
                Log.Info($"hexpad link: api {api}" + (caps.Count > 0 ? $" ({string.Join(", ", caps)})" : ""));
                break;
            case "state":
                lock (_lock)
                {
                    bool conn = Bool(m, "connected"), disp = Bool(m, "display_on", true);
                    _s.State = !conn ? LinkState.Unplugged : !disp ? LinkState.ScreenOff : LinkState.Ok;
                    _s.Ambient = Bool(m, "ambient");
                    _s.Profile = Str(m, "profile");
                    _s.Page = Int(m, "page", 0);
                    _s.Pages = Math.Max(1, Int(m, "pages", 1));
                    _s.PageName = Str(m, "page_name");
                    _s.InFolder = m.TryGetProperty("folder", out var f) && f.ValueKind == JsonValueKind.String;
                    _s.FolderName = Str(m, "folder_name");
                    _s.FolderPage = Int(m, "folder_page", 0);
                    _s.FolderPages = Int(m, "folder_pages", 0);
                    _s.Version++;
                }
                break;
            case "controls":
                var ctl = new Dictionary<string, Control>();
                foreach (var p in m.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object) continue;
                    var v = p.Value;
                    var c = new Control
                    {
                        Label = Str(v, "label"), Left = Str(v, "left"), Right = Str(v, "right"), Push = Str(v, "push"),
                        Danger = Bool(v, "danger"), Max = Math.Max(1, Int(v, "max", 100)),
                    };
                    c.Slider = v.TryGetProperty("slider", out var sv) && sv.ValueKind == JsonValueKind.String ? sv.GetString() ?? ""
                        : c.Left.Length > 0 || c.Right.Length > 0 ? "jog" : "";
                    // api 1 has no kind: the summary's "Page: next" says "page" (only the jog distance needs it)
                    c.Kind = v.TryGetProperty("kind", out var kv) && kv.ValueKind == JsonValueKind.String ? kv.GetString() ?? ""
                        : KindOf(c.Left.Length > 0 ? c.Left : c.Right);
                    ctl[p.Name] = c;
                }
                lock (_lock) { _s.Controls = ctl; _s.Version++; }
                break;
            case "values":
                var vals = new Dictionary<string, Value>();
                foreach (var p in m.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object) continue;
                    var v = p.Value;
                    float? x = null;
                    if (v.TryGetProperty("v", out var xv) && xv.ValueKind == JsonValueKind.Number && xv.TryGetDouble(out double d) && double.IsFinite(d))
                        x = (float)Math.Clamp(d, 0, 1);
                    vals[p.Name] = new Value { V = x, Muted = Bool(v, "muted"), Off = Bool(v, "off") };
                }
                lock (_lock) { _s.Values = vals; _s.Version++; }
                break;
            case "faces":
                bool dark = Bool(m, "dark");
                var dev = new List<(int, Face)>();
                var hi = new List<(int, Face)>();
                if (m.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Object)
                    foreach (var k in keys.EnumerateObject())
                    {
                        if (!int.TryParse(k.Name, out int i) || i < 0 || i >= Keys) continue;
                        dev.Add((i, k.Value.ValueKind == JsonValueKind.String ? Decode(k.Value.GetString(), true) : null));
                    }
                // only for a client that asked for them: null / absent = that key has only its device face now
                bool hasHi = m.TryGetProperty("hi", out var hk) && hk.ValueKind == JsonValueKind.Object;
                if (hasHi)
                    foreach (var k in hk.EnumerateObject())
                    {
                        if (!int.TryParse(k.Name, out int i) || i < 0 || i >= Keys) continue;
                        hi.Add((i, k.Value.ValueKind == JsonValueKind.String ? Decode(k.Value.GetString(), false) : null));
                    }
                lock (_lock)
                {
                    foreach (var (i, face) in dev)
                    {
                        _s.Faces[i] = face;
                        _s.HiFaces[i] = null;           // a new device face makes the old sharp one stale
                        _s.FaceVersion[i]++;
                    }
                    foreach (var (i, face) in hi)
                    {
                        _s.HiFaces[i] = face;
                        _s.FaceVersion[i]++;
                    }
                    _s.Dark = dark;
                    _s.Version++;
                }
                break;
            case "input":
                lock (_lock)
                {
                    _s.Echo = Str(m, "id");
                    _s.EchoAt = Clock.Now;
                    _s.Version++;
                }
                break;
        }
    }

    static string KindOf(string summary)
    {
        int i = string.IsNullOrEmpty(summary) ? -1 : summary.IndexOf(':');
        return i > 0 ? summary[..i].Trim().ToLowerInvariant() : "";
    }

    // a face JPEG to pixels. The device one back to the face Hexpad drew: undo the 90 degree turn, the mirror and
    // the calibration shift. The sharp (2x) one is sent upright, as drawn.
    Face Decode(string b64, bool device)
    {
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(b64));
            using var img = new Bitmap(ms);
            if (device)
            {
                img.RotateFlip(_rot switch { 90 => RotateFlipType.Rotate270FlipNone, 180 => RotateFlipType.Rotate180FlipNone,
                    270 => RotateFlipType.Rotate90FlipNone, _ => RotateFlipType.RotateNoneFlipNone });
                if (_mirror) img.RotateFlip(RotateFlipType.RotateNoneFlipX);
            }
            int w = img.Width, h = img.Height;
            if (w <= 0 || h <= 0 || w > 1024 || h > 1024) return null;
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                if (device) g.DrawImageUnscaled(img, -_dx, -_dy);
                else g.DrawImageUnscaled(img, 0, 0);
            }
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            var px = new uint[w * h];
            unsafe
            {
                fixed (uint* dst = px)
                    for (int y = 0; y < h; y++)
                        Buffer.MemoryCopy((byte*)data.Scan0 + y * data.Stride, dst + y * w, w * 4L, w * 4L);
            }
            bmp.UnlockBits(data);
            return new Face(px, w, h);
        }
        catch (Exception e)
        {
            Log.Info("hexpad link: bad face: " + e.Message);
            return null;
        }
    }

    static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static int Int(JsonElement e, string n, int def) => e.TryGetProperty(n, out var v) ? Int(v, def) : def;
    static int Int(JsonElement v, int def) =>
        v.ValueKind != JsonValueKind.Number ? def : v.TryGetInt32(out int i) ? i
        : v.TryGetDouble(out double d) && double.IsFinite(d) ? (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue) : def;
    static bool Bool(JsonElement e, string n, bool def = false) =>
        e.TryGetProperty(n, out var v) ? v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.False && def) : def;

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(2000);
        // (_wake is left to the GC, as AvatarLink's and LumosLink's are: a step still running past the Join - a read of the dock - would
        // come back to a disposed handle in its WaitOne and throw on a thread with no handler: the process gone mid-exit)
    }
}
