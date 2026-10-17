using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Orbit;

// Avatar toggles (the watch's "avatar" widget, Panels/WristAvatar*.cs - Erik 10-18 picked it): buttons for the avatar worn in VRChat,
// made from VRChat's own OSC config of that avatar; a tap sends the parameter to VRChat over OSC. All of it read only:
//  - which parameters: VRChat writes LocalLow\VRChat\VRChat\OSC\usr_<you>\Avatars\avtr_<id>.json for an avatar worn with OSC on - its
//    parameters with the address and type VRChat takes ("input": writable) and the one it gives ("output"). Read, never written. What
//    nobody wants on a watch is left out (AvatarFilter: face tracking, PhysBone / contact outputs, VRCFury internals, sync helpers).
//  - which avatar is worn and the values now: VRChat's OSCQuery service - an HTTP server on a localhost port of VRChat.exe (found
//    among its TCP listeners, asked "/?HOST_INFO": its NAME is VRChat's) - answers /avatar with the avatar's id (/avatar/change) and
//    every parameter's value, so a toggle shows how it really is. UDP 9001 is never bound (Hexpad listens there). No OSCQuery (OSC off,
//    an older VRChat): the newest output_log's line about your own avatar switch, matched to a config by its name; else the config
//    written last (no values then: the buttons start off and say so).
//  - a tap: UDP to 127.0.0.1 - VRChat's OSC port from HOST_INFO, else General's OSC port (9000) - with the parameter's input address
//    and type. Shown at once and held 1.5 s; then what VRChat reads back wins (a toggle VRChat did not take goes back).
// A thread of its own, started by the first avatar widget: every 2.5 s while the watch is in view (sooner after a tap, to show what
// VRChat made of it), every 20 s when not, asleep when no page has the widget. VRChat not running: the widget says so.
public enum AvatarStatus { Off, Looking, NoVrchat, NoConfig, Ready }
public enum AvatarSource { None, Live, Log, Newest }
public enum AvatarKind { Toggle, Stepper, Slider }

// one parameter that became a button
public sealed class AvatarParam
{
    public string Name = "", Address = "", Label = "";
    public char Type;                       // OSC's tag: 'T' a bool, 'i' an int, 'f' a float
    public AvatarKind Kind;
    public int Rank;                        // the default order: toggles first, then steppers, sliders; a prefab's own after the avatar's
}

// one avatar's config, read (a new object when its file changes)
public sealed class AvatarConfigFile
{
    public string Id = "", Name = "", Path = "", User = "";
    public DateTime Written;
    public int Total, Writable;             // its parameters, and the ones VRChat takes
    public AvatarParam[] Buttons = Array.Empty<AvatarParam>();
    public int[] Order = Array.Empty<int>(); // Buttons by Rank (the default ones first)
    public readonly int[] Dropped = new int[(int)AvatarFilter.Why.Count];      // why the others are not buttons (--avatartest)
    public readonly Dictionary<string, int> ByAddress = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> ByName = new(StringComparer.Ordinal);
}

// What the widget shows (a new object for every change: the watch compares Version)
public sealed class AvatarState
{
    public int Version;
    public AvatarStatus Status;
    public AvatarSource Source;
    public AvatarConfigFile Avatar;         // null: none found
    public float[] Values = Array.Empty<float>();      // per Avatar.Buttons; NaN: not known
    public bool Pending;                    // a tap shown before VRChat's answer
    public bool Ready => Status == AvatarStatus.Ready && Avatar != null;
    public bool Live => Source == AvatarSource.Live;

    public AvatarState Copy() => (AvatarState)MemberwiseClone();
}

// Which parameters become buttons, and what they are called
public static class AvatarFilter
{
    public enum Why { Kept, NotWritable, BuiltIn, Orbit, FaceTracking, PhysBone, Internal, Helper, Count }

    // VRChat's own (they have no input; a config from before that rule might): never a button
    static readonly HashSet<string> BuiltIn = new(StringComparer.Ordinal)
    {
        "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "AngularY", "Grounded", "Seated", "AFK", "Upright", "IsLocal", "Viseme",
        "Voice", "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight", "TrackingType", "VRMode", "MuteSelf", "InStation",
        "Earmuffs", "IsOnFriendsList", "AvatarVersion", "ScaleModified", "ScaleFactor", "ScaleFactorInverse", "EyeHeightAsMeters",
        "EyeHeightAsPercent", "IsAnimatorEnabled", "PreviewMode", "VRCFaceBlendH", "VRCFaceBlendV",
    };

    // the XSOverlay-style ones Orbit itself sends (OrbitApp.SendOsc): a button would fight it
    static readonly HashSet<string> Own = new(StringComparer.OrdinalIgnoreCase)
    {
        "isOverlayOpen", "isKeyboardOpen", "isWristVisible", "openOverlayCount", "leftControllerBattery", "rightControllerBattery",
        "averageControllerBattery", "averageTrackerBattery",
    };

    // the start of a name (after VRCFury's VF12_): face tracking, OSC apps' inputs, smoothing and proxies, internals
    static readonly (string p, Why why)[] Starts =
    {
        ("FT/", Why.FaceTracking), ("v2/", Why.FaceTracking), ("SRanipal", Why.FaceTracking), ("VRCFT", Why.FaceTracking),
        ("OSCm/", Why.Helper), ("Smoothing/", Why.Helper), ("Proxy/", Why.Helper), ("OSC/", Why.Helper), ("VRCOSC", Why.Helper),
        ("ChatBox/", Why.Helper), ("HR/", Why.Helper), ("Heartrate", Why.Helper), ("HeartRate", Why.Helper), ("Contact/", Why.PhysBone),
        ("VFH/", Why.Internal), ("__", Why.Internal), ("TC_", Why.Internal),      // (TC_: VRCFury's tracking control)
    };

    static readonly string[] Ends = { "_IsGrabbed", "_Angle", "_Stretch", "_Squish", "_IsPosed", "_Contact", "_Proximity" };

    // face tracking's words at the start of a name part (VRCFaceTracking's unified expressions, SRanipal's): a word on its own - "Lip"
    // before "Funnel" is face tracking, "Lipstick" is a toggle
    static readonly string[] FaceWords =
    {
        "EyeLeft", "EyeRight", "EyeLid", "EyeX", "EyeY", "EyesX", "EyesY", "EyesWiden", "EyesSqueeze", "EyesDilation", "EyesPupil", "EyeSquint",
        "EyeWide", "EyeDilation", "EyeTracking", "EyeTrackingActive", "LeftEye", "RightEye", "CombinedEye", "Brow", "Cheek", "Jaw", "Mouth",
        "Lip", "Nose", "Tongue", "Pupil", "SmileFrown", "SmileSad", "LipTracking", "FaceTracking", "ExpressionTracking",
    };

    // a word of a name that makes it a helper nobody taps (a sync, a bit of a packed value, smoothing ...)
    static readonly HashSet<string> HelperWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sync", "Synced", "Bit", "Bits", "Seed", "Buffer", "Pointer", "Proxy", "Smoothed", "Smoothing", "Smooth", "Debug", "Internal", "Temp", "Tmp",
        "Velocity", "Drift", "Sim",
    };

    // prefabs whose many parameters would crowd the avatar's own toggles out of the default ones (still all in the list)
    static readonly string[] Prefabs = { "Go/", "QP/", "HUDSettings/", "DiscordButton/", "LaserPointer/", "Spyglass/", "Spin/", "Touch/", "State/" };

    // an int is a toggle when its name says it switches something (VRChat's config has no range)
    static readonly HashSet<string> SwitchWords = new(StringComparer.OrdinalIgnoreCase) { "Toggle", "On", "Off", "Enable", "Enabled", "Show", "Hide", "Active" };

    public static Why Check(string name, bool writable)
    {
        if (!writable) return Why.NotWritable;
        if (string.IsNullOrEmpty(name)) return Why.Internal;
        if (BuiltIn.Contains(name)) return Why.BuiltIn;
        if (Own.Contains(name)) return Why.Orbit;
        string n = StripVf(name);
        if (BuiltIn.Contains(n)) return Why.BuiltIn;          // (behind VRCFury's prefix too)
        foreach (var (p, why) in Starts) if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return why;
        foreach (var e in Ends) if (n.EndsWith(e, StringComparison.Ordinal)) return Why.PhysBone;
        if (n.Contains("FullController", StringComparison.Ordinal)) return Why.Internal;      // VRCFury's own wiring
        foreach (var part in n.Split('/'))
        {
            if (part.StartsWith("__", StringComparison.Ordinal)) return Why.Internal;
            foreach (var f in FaceWords)
                if (part.StartsWith(f, StringComparison.Ordinal) && (part.Length == f.Length || !char.IsLower(part[f.Length]))) return Why.FaceTracking;
            foreach (var w in Words(part)) if (HelperWords.Contains(w)) return Why.Helper;
        }
        return Why.Kept;
    }

    // VRCFury's prefixes, nested ones too: "VF12_VF3_Props/Hat" -> "Props/Hat"
    public static string StripVf(string n)
    {
        while (n.Length > 3 && n[0] == 'V' && n[1] == 'F')
        {
            int i = 2;
            while (i < n.Length && char.IsDigit(n[i])) i++;
            if (i == 2 || i >= n.Length - 1 || n[i] != '_') break;
            n = n[(i + 1)..];
        }
        return n;
    }

    // a name part's words: split at _ - . and spaces, before a capital after a small letter, before the last of a run of capitals
    // ("RGBMode": RGB, Mode) and before a number ("Outfit2": Outfit, 2)
    public static List<string> Words(string s)
    {
        var l = new List<string>();
        var sb = new StringBuilder();
        void Cut() { if (sb.Length > 0) { l.Add(sb.ToString()); sb.Clear(); } }
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '_' or '-' or '.' or ' ' or '/') { Cut(); continue; }
            if (sb.Length > 0)
            {
                char p = sb[^1];
                bool next = i + 1 < s.Length && char.IsLower(s[i + 1]);
                if (char.IsUpper(c) && (char.IsLower(p) || char.IsDigit(p) || (char.IsUpper(p) && next))) Cut();
                else if (char.IsDigit(c) && char.IsLetter(p)) Cut();
            }
            sb.Append(c);
        }
        Cut();
        return l;
    }

    // what a name says about switching, before or after what it switches ("ToggleTail", "glassesOn"): a button says that itself
    static readonly HashSet<string> LeadWords = new(StringComparer.OrdinalIgnoreCase) { "Toggle", "Tog", "Show", "Enable" },
        TailWords = new(StringComparer.OrdinalIgnoreCase) { "On", "Toggle", "Enable", "Enabled", "Visible", "Active" };

    // its name on the watch: VRCFury's prefix and the path off, the words apart, the first a capital ("VF12_Clothes/hoodieUp" ->
    // "Hoodie Up"); a "Toggle" / "Show" before it or an "On" / "Toggle" after it dropped ("glassesOn" -> "Glasses")
    public static string Label(string name)
    {
        string n = StripVf(name);
        int slash = n.TrimEnd('/').LastIndexOf('/');
        string seg = slash >= 0 ? n[(slash + 1)..].TrimEnd('/') : n;
        var w = Words(seg);
        if (w.Count > 1 && LeadWords.Contains(w[0])) w.RemoveAt(0);
        if (w.Count > 1 && TailWords.Contains(w[^1])) w.RemoveAt(w.Count - 1);
        string s = string.Join(" ", w);
        if (s.Length == 0) s = seg.Length > 0 ? seg : name;
        return char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
    }

    // the part before the last ("Clothes" for "Clothes/Hat"): what tells two Hats apart
    static string Group(string name)
    {
        var parts = StripVf(name).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? Label(parts[^2]) : null;
    }

    public static AvatarKind KindOf(char type, string name)
    {
        if (type == 'T') return AvatarKind.Toggle;
        if (type == 'f') return AvatarKind.Slider;
        foreach (var w in Words(StripVf(name))) if (SwitchWords.Contains(w)) return AvatarKind.Toggle;
        return AvatarKind.Stepper;
    }

    static int RankOf(AvatarKind k, string name)
    {
        int r = k switch { AvatarKind.Toggle => 0, AvatarKind.Stepper => 1, _ => 2 };
        string n = StripVf(name);
        foreach (var p in Prefabs) if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return r + 3;
        return r;
    }

    static char TypeOf(string t) => t switch { "Bool" => 'T', "Int" => 'i', "Float" => 'f', _ => '\0' };

    // VRChat's config of one avatar (UTF-8, with or without its BOM)
    public static AvatarConfigFile Parse(byte[] json, string path = "")
    {
        int skip = json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF ? 3 : 0;
        using var doc = JsonDocument.Parse(json.AsMemory(skip));
        var r = doc.RootElement;
        var a = new AvatarConfigFile { Path = path ?? "" };
        if (r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) a.Id = id.GetString() ?? "";
        if (r.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String) a.Name = nm.GetString() ?? "";
        var list = new List<AvatarParam>();
        if (r.TryGetProperty("parameters", out var ps) && ps.ValueKind == JsonValueKind.Array)
            foreach (var p in ps.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                a.Total++;
                string name = p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                string addr = null; char type = '\0';
                if (p.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Object)
                {
                    if (inp.TryGetProperty("address", out var ad) && ad.ValueKind == JsonValueKind.String) addr = ad.GetString();
                    if (inp.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String) type = TypeOf(ty.GetString());
                }
                bool writable = !string.IsNullOrEmpty(addr) && addr.StartsWith('/') && type != '\0';
                if (writable) a.Writable++;
                var why = Check(name, writable);
                if (why != Why.Kept) { a.Dropped[(int)why]++; continue; }
                if (a.ByAddress.ContainsKey(addr)) continue;
                var k = KindOf(type, name);
                a.ByAddress[addr] = list.Count;
                a.ByName[name] = list.Count;
                list.Add(new AvatarParam { Name = name, Address = addr, Type = type, Kind = k, Label = Label(name), Rank = RankOf(k, name) });
            }
        // two of the same name ("Clothes/Hat", "Props/Hat"): the part before it after each
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in list) seen[b.Label] = seen.TryGetValue(b.Label, out int c) ? c + 1 : 1;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in list)
        {
            if (seen[b.Label] > 1 && Group(b.Name) is { } g && !g.Equals(b.Label, StringComparison.OrdinalIgnoreCase)) b.Label = b.Label + " (" + g + ")";
            for (int i = 2; !used.Add(b.Label); i++) b.Label = Label(b.Name) + " " + i.ToString(CultureInfo.InvariantCulture);
        }
        a.Buttons = list.ToArray();
        var order = new int[a.Buttons.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (x, y) => a.Buttons[x].Rank != a.Buttons[y].Rank ? a.Buttons[x].Rank.CompareTo(a.Buttons[y].Rank) : x.CompareTo(y));
        a.Order = order;
        return a;
    }

    // the buttons a widget shows: the ones picked for this avatar in the editor (AvatarsConfig.Shown), else the first of the default
    // order; into 'into' (its old contents dropped), how many
    public const int DefaultCount = 12;

    public static int Shown(AvatarConfigFile a, List<string> picked, List<int> into)
    {
        into.Clear();
        if (a == null) return 0;
        if (picked != null)
        {
            foreach (var name in picked)
                if (name != null && a.ByName.TryGetValue(name, out int i) && !into.Contains(i)) into.Add(i);
            return into.Count;
        }
        for (int i = 0; i < a.Order.Length && into.Count < DefaultCount; i++) into.Add(a.Order[i]);
        return into.Count;
    }
}

// The avatar widget's link to VRChat (see the top of this file)
public sealed class AvatarLink : IDisposable
{
    public string OscDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "VRChat", "VRChat", "OSC");
    public string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "VRChat", "VRChat");
    public Func<string, string> Query;      // an OSCQuery path -> its answer, null when none (null: VRChat's own service, found by its port)
    public Func<bool> Running;              // VRChat runs (null: its process looked for)
    public Action<string, object> Send;     // a parameter to VRChat: its address and a bool / int / float (null: nothing is sent)
    public int OscPort = 9000;              // General's OSC port: where a tap goes when VRChat's HOST_INFO says none
    public double Poll = 2.5, Slow = 20, Confirm = 0.6, Hold = 1.5;      // seconds: watched, not watched, after a tap, a tap shown

    readonly AutoResetEvent _wake = new(false);
    readonly object _lock = new();
    Thread _thread;
    volatile bool _stop, _active, _watched, _frozen;
    volatile AvatarState _pub = new() { Status = AvatarStatus.Off };
    int _version, _hostPort, _queryPort;
    DateTime _due = DateTime.MinValue, _nextScan = DateTime.MinValue;
    HttpClient _http;
    // a tap: shown until Hold is over (address -> the value, until when); a slider's newest value, waiting for its turn to be sent
    readonly Dictionary<string, (float v, DateTime until)> _held = new(StringComparer.Ordinal);
    readonly Dictionary<string, (AvatarParam p, float v)> _out = new(StringComparer.Ordinal);
    readonly Dictionary<string, double> _sentAt = new(StringComparer.Ordinal);
    readonly List<string> _sendNow = new();
    const double SendGap = 0.05;            // a slider dragged: at most 20 values a second per parameter (the last one always)

    public AvatarState State => _pub;

    // VRChat's OSC port: HOST_INFO's, else General's
    public int SendPort => _hostPort > 0 ? _hostPort : OscPort;

    // an avatar widget is on a page
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

    // the watch is in view: read again now, then every Poll seconds
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

    // --snapshot: no thread; the state as given, and a tap only changes it here (and goes to Send, a recorder there)
    public void Freeze(AvatarState s)
    {
        _frozen = true;
        Publish(s.Copy());
    }

    // --snapshot: a read as the thread would make it, now (a made-up OSCQuery, log and config folder)
    public void ReadNow() { lock (_step) Step(force: true); }
    readonly object _step = new();

    void Start()
    {
        if (_thread != null || _stop || _frozen) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "vrchat avatar" };
        _thread.Start();
    }

    // ---- the commands (from the watch's thread) ----

    public void Toggle(int i)
    {
        var s = _pub;
        if (!s.Ready || i < 0 || i >= s.Values.Length) return;
        float v = s.Values[i];
        Set(i, float.IsNaN(v) || v < 0.5f ? 1 : 0);
    }

    public void Step(int i, int dir)
    {
        var s = _pub;
        if (!s.Ready || i < 0 || i >= s.Values.Length) return;
        float v = s.Values[i];
        Set(i, (float.IsNaN(v) ? 0 : MathF.Round(v)) + dir);
    }

    // a button's new value: shown at once (held Hold seconds against reads from before VRChat took it), sent now - a slider's drag
    // at most every SendGap seconds, its last value by Flush
    public void Set(int i, float v)
    {
        AvatarParam p;
        lock (_lock)
        {
            var s = _pub;
            if (!s.Ready || i < 0 || i >= s.Avatar.Buttons.Length || i >= s.Values.Length) return;
            p = s.Avatar.Buttons[i];
            v = p.Kind switch
            {
                AvatarKind.Toggle => v >= 0.5f ? 1 : 0,
                AvatarKind.Stepper when p.Type == 'i' => Math.Clamp(MathF.Round(v), 0, 255),
                _ => Math.Clamp(v, 0, 1),
            };
            if (s.Values[i] == v) return;
            var n = s.Copy();
            n.Values = (float[])s.Values.Clone();
            n.Values[i] = v;
            n.Pending = !_frozen;
            Publish(n);
            _held[p.Address] = (v, DateTime.UtcNow.AddSeconds(Hold));
            _out[p.Address] = (p, v);
            if (p.Kind != AvatarKind.Slider) _sentAt.Remove(p.Address);      // a tap goes at once, however soon after the last

        }
        Flush(Clock.Now);
        if (_frozen) return;
        _due = DateTime.UtcNow.AddSeconds(Confirm);         // read back soon: what VRChat made of it
        _wake.Set();
    }

    // what waits goes out (a slider's newest value once its gap is over); the app calls it every frame, a tap at once
    public void Flush(double now)
    {
        lock (_lock)
        {
            if (_out.Count == 0) return;
            _sendNow.Clear();
            foreach (var (addr, _) in _out)
                if (!_sentAt.TryGetValue(addr, out double at) || now - at >= SendGap) _sendNow.Add(addr);
            foreach (var addr in _sendNow)
            {
                var (p, v) = _out[addr];
                _out.Remove(addr);
                _sentAt[addr] = now;
                Emit(p, v);
            }
        }
    }

    // OSC's own types: a bool T / F, an int i, a float f
    void Emit(AvatarParam p, float v)
    {
        var send = Send;
        if (send == null) return;
        object value = p.Type switch { 'T' => v >= 0.5f, 'i' => (int)MathF.Round(v), _ => (object)v };
        try { send(p.Address, value); }
        catch (Exception e) { Log.Warn("avatar: not sent (" + e.Message + ")"); }
    }

    // ---- the thread ----

    void Run()
    {
        while (!_stop)
        {
            try { lock (_step) Step(false); }
            catch (Exception e)
            {
                if (e.Message != _failed) Log.Warn("avatar: " + e.Message);       // (once, not every 5 s)
                _failed = e.Message;
                _due = DateTime.UtcNow.AddSeconds(5);
            }
            double wait = _active ? Math.Clamp((_due - DateTime.UtcNow).TotalSeconds, 0.05, 60) : 60;
            _wake.WaitOne(TimeSpan.FromSeconds(wait));
        }
    }

    AvatarStatus _said = AvatarStatus.Off;
    AvatarSource _saidSrc;
    string _saidId, _failed;

    void Step(bool force)
    {
        if (!force && (!_active || DateTime.UtcNow < _due)) return;
        _due = DateTime.UtcNow.AddSeconds(_watched ? Poll : Slow);
        if (_pub.Status == AvatarStatus.Off) Publish(new AvatarState { Status = AvatarStatus.Looking });
        // (VRChat's service answering is VRChat running: the process list is looked at only without it)
        if (!(Running?.Invoke() ?? (_queryPort != 0 || VrchatRunning())))
        {
            _queryPort = _hostPort = 0;
            lock (_lock)
            {
                _held.Clear();
                if (_pub.Status != AvatarStatus.NoVrchat) Publish(new AvatarState { Status = AvatarStatus.NoVrchat });
            }
            Said(AvatarStatus.NoVrchat, AvatarSource.None, null);
            return;
        }
        // the avatar worn and its values: OSCQuery; else the log's last switch, else the config written last
        string id = null;
        Dictionary<string, float> values = null;
        string tree = Ask("/avatar");
        if (tree != null) (id, values) = ParseTree(tree);
        AvatarConfigFile a = null;
        var src = AvatarSource.None;
        if (id != null) { a = FindById(id); src = AvatarSource.Live; }
        else
        {
            var (name, user) = LogAvatar();
            if (name != null) { a = FindByName(name, user); src = AvatarSource.Log; }
            if (a == null && name == null) { a = Newest(user); src = AvatarSource.Newest; }
        }
        if (a == null) values = null;
        var status = a == null ? AvatarStatus.NoConfig : AvatarStatus.Ready;
        lock (_lock)
        {
            var old = _pub;
            var now = DateTime.UtcNow;
            // a tap's hold that is over: what VRChat says wins again (another avatar's too: it would keep the reads coming fast)
            if (_held.Count > 0)
                foreach (var addr in new List<string>(_held.Keys))
                    if (now >= _held[addr].until) _held.Remove(addr);
            var vals = new float[a?.Buttons.Length ?? 0];
            for (int i = 0; i < vals.Length; i++)
            {
                var p = a.Buttons[i];
                float v = float.NaN;
                if (values != null && values.TryGetValue(p.Address, out float got)) v = got;
                else if (values == null && ReferenceEquals(old.Avatar, a) && i < old.Values.Length) v = old.Values[i];     // no live values: what was set
                if (_held.TryGetValue(p.Address, out var h)) v = h.v;
                vals[i] = v;
            }
            bool pending = _held.Count > 0;
            if (old.Status != status || old.Source != src || !ReferenceEquals(old.Avatar, a) || old.Pending != pending || !Same(old.Values, vals))
                Publish(new AvatarState { Status = status, Source = src, Avatar = a, Values = vals, Pending = pending });
            if (pending) _due = DateTime.UtcNow.AddSeconds(Math.Min(Confirm, Hold));
        }
        Said(status, src, a?.Id);
    }

    static bool Same(float[] a, float[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!(a[i] == b[i] || (float.IsNaN(a[i]) && float.IsNaN(b[i])))) return false;
        return true;
    }

    // ---- VRChat's OSCQuery ----

    // a path's answer: the made-up one of a test, else VRChat's service (its port looked for again at most every 10 s)
    string Ask(string path)
    {
        if (Query != null)
        {
            string host = Query("/?HOST_INFO");
            _hostPort = host != null ? HostOscPort(host) : 0;
            return host != null ? Query(path) : null;
        }
        if (_queryPort == 0 && DateTime.UtcNow >= _nextScan)
        {
            _nextScan = DateTime.UtcNow.AddSeconds(10);
            _queryPort = FindService(out _hostPort);
        }
        if (_queryPort == 0) return null;
        string body = Get(_queryPort, _queryV6, path);
        if (body == null) { _queryPort = 0; _hostPort = 0; }
        return body;
    }

    bool _queryV6;

    // VRChat.exe's localhost TCP listeners, each asked "/?HOST_INFO": the one that says it is VRChat's
    int FindService(out int oscPort)
    {
        oscPort = 0;
        var pids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("VRChat")) { pids.Add(p.Id); p.Dispose(); }
        if (pids.Count == 0) return 0;
        foreach (var (port, v6) in Listeners(pids))
        {
            string host = Get(port, v6, "/?HOST_INFO", 700);      // (another of VRChat's ports may not speak HTTP at all: not long)
            if (host == null || !IsVrchat(host)) continue;
            oscPort = HostOscPort(host);
            _queryV6 = v6;
            Log.Info($"avatar: VRChat's OSCQuery on port {port}, its OSC on {(oscPort > 0 ? oscPort : OscPort)}");
            return port;
        }
        return 0;
    }

    static bool IsVrchat(string host)
    {
        try
        {
            using var doc = JsonDocument.Parse(host);
            return doc.RootElement.TryGetProperty("NAME", out var n) && n.ValueKind == JsonValueKind.String &&
                   (n.GetString() ?? "").StartsWith("VRChat", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    // HOST_INFO's OSC_PORT: where VRChat takes OSC (0: not said)
    public static int HostOscPort(string host)
    {
        try
        {
            using var doc = JsonDocument.Parse(host);
            return doc.RootElement.TryGetProperty("OSC_PORT", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int v) && v is > 0 and < 65536 ? v : 0;
        }
        catch (Exception) { return 0; }
    }

    string Get(int port, bool v6, string path, int ms = 2000)
    {
        // no proxy (a system proxy must not see a call to this PC), a short wait: it is on this PC or not there at all
        _http ??= new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromMilliseconds(500) }) { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://{(v6 ? "[::1]" : "127.0.0.1")}:{port.ToString(CultureInfo.InvariantCulture)}{path}");
            using var cts = new CancellationTokenSource(ms);
            using var res = _http.Send(req, cts.Token);
            if (!res.IsSuccessStatusCode) return null;
            using var rd = new StreamReader(res.Content.ReadAsStream(), Encoding.UTF8);
            return rd.ReadToEnd();
        }
        catch (Exception) { return null; }
    }

    // /avatar's tree: /avatar/change's value (the id), every /avatar/parameters/... value (a bool as 0 / 1)
    public static (string id, Dictionary<string, float> values) ParseTree(string json)
    {
        string id = null;
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            void Walk(JsonElement n, int depth)
            {
                if (n.ValueKind != JsonValueKind.Object || depth > 12) return;
                if (n.TryGetProperty("FULL_PATH", out var fp) && fp.ValueKind == JsonValueKind.String && n.TryGetProperty("VALUE", out var val) &&
                    val.ValueKind == JsonValueKind.Array && val.GetArrayLength() > 0)
                {
                    string path = fp.GetString() ?? "";
                    var v = val[0];
                    if (path == "/avatar/change" && v.ValueKind == JsonValueKind.String) id = v.GetString();
                    else if (path.StartsWith("/avatar/parameters/", StringComparison.Ordinal))
                        switch (v.ValueKind)
                        {
                            case JsonValueKind.True: values[path] = 1; break;
                            case JsonValueKind.False: values[path] = 0; break;
                            case JsonValueKind.Number: values[path] = (float)v.GetDouble(); break;
                        }
                }
                if (n.TryGetProperty("CONTENTS", out var c) && c.ValueKind == JsonValueKind.Object)
                    foreach (var kid in c.EnumerateObject()) Walk(kid.Value, depth + 1);
            }
            Walk(doc.RootElement, 0);
        }
        catch (Exception) { return (null, null); }
        return (string.IsNullOrEmpty(id) ? null : id, values);
    }

    // ---- the TCP listeners of a process (iphlpapi's owner table: nothing is opened to read it) ----

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

    static List<(int port, bool v6)> Listeners(HashSet<int> pids)
    {
        var ports = new List<(int, bool)>();
        foreach (int af in new[] { 2, 23 })                  // IPv4, IPv6
        {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, 3, 0);      // TCP_TABLE_OWNER_PID_LISTENER
            if (size <= 0) continue;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buf, ref size, false, af, 3, 0) != 0) continue;
                int n = Marshal.ReadInt32(buf);
                int rowSize = af == 2 ? 24 : 56, portAt = af == 2 ? 8 : 20, pidAt = af == 2 ? 20 : 52;
                for (int i = 0; i < n; i++)
                {
                    IntPtr row = buf + 4 + i * rowSize;
                    if (!pids.Contains(Marshal.ReadInt32(row + pidAt))) continue;
                    int raw = Marshal.ReadInt32(row + portAt);
                    int port = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
                    if (port > 0 && !ports.Contains((port, af == 23))) ports.Add((port, af == 23));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return ports;
    }

    static bool VrchatRunning()
    {
        var ps = Process.GetProcessesByName("VRChat");
        foreach (var p in ps) p.Dispose();
        return ps.Length > 0;
    }

    // ---- the configs (read only; each read again only when its file changes) ----

    readonly Dictionary<string, AvatarConfigFile> _configs = new(StringComparer.OrdinalIgnoreCase);

    AvatarConfigFile Load(string path)
    {
        DateTime t;
        try { t = File.GetLastWriteTimeUtc(path); } catch (Exception) { return null; }
        if (_configs.TryGetValue(path, out var a) && a.Written == t) return a;
        try
        {
            byte[] bytes;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { bytes = new byte[f.Length]; f.ReadExactly(bytes); }
            a = AvatarFilter.Parse(bytes, path);
            a.Written = t;
            a.User = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)) ?? "");
            if (_configs.Count > 64) _configs.Clear();
            _configs[path] = a;
            return a;
        }
        catch (Exception e)
        {
            Log.Info("avatar: a config not readable (" + e.GetType().Name + ")");
            return null;
        }
    }

    // usr_*\Avatars, the one of the user named first (the log's) when it has one
    IEnumerable<string> AvatarDirs(string user)
    {
        if (!Directory.Exists(OscDir)) yield break;
        if (!string.IsNullOrEmpty(user))
        {
            string mine = Path.Combine(OscDir, user, "Avatars");
            if (Directory.Exists(mine)) { yield return mine; yield break; }
        }
        foreach (var u in Directory.EnumerateDirectories(OscDir, "usr_*"))
        {
            string d = Path.Combine(u, "Avatars");
            if (Directory.Exists(d)) yield return d;
        }
    }

    AvatarConfigFile FindById(string id)
    {
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        AvatarConfigFile best = null;
        foreach (var dir in AvatarDirs(null))
        {
            string f = Path.Combine(dir, id + ".json");
            if (File.Exists(f) && Load(f) is { } a && (best == null || a.Written > best.Written)) best = a;
        }
        return best;
    }

    AvatarConfigFile FindByName(string name, string user)
    {
        AvatarConfigFile best = null;
        foreach (var dir in AvatarDirs(user))
            foreach (var f in Directory.EnumerateFiles(dir, "avtr_*.json"))
                if (Load(f) is { } a && a.Name == name && (best == null || a.Written > best.Written)) best = a;
        return best;
    }

    AvatarConfigFile Newest(string user)
    {
        string newest = null;
        DateTime t = DateTime.MinValue;
        foreach (var dir in AvatarDirs(user))
            foreach (var f in Directory.EnumerateFiles(dir, "avtr_*.json"))
            {
                var w = File.GetLastWriteTimeUtc(f);
                if (w > t) { t = w; newest = f; }
            }
        return newest == null ? null : Load(newest);
    }

    // ---- the log: your own last avatar switch (followed: only what was written since is read again) ----

    string _logFile, _me = "", _meId = "", _logAvatar;
    long _logPos;

    (string name, string user) LogAvatar()
    {
        try
        {
            string newest = null;
            DateTime t = DateTime.MinValue;
            if (Directory.Exists(LogDir))
                foreach (var f in Directory.EnumerateFiles(LogDir, "output_log_*.txt"))
                {
                    var w = File.GetLastWriteTimeUtc(f);
                    if (w > t) { t = w; newest = f; }
                }
            if (newest == null) return (null, null);
            using var fs = new FileStream(newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string key = "] Switching " + _me + " to avatar ";
            if (newest != _logFile || fs.Length < _logPos)
            {
                // a new log: who you are, then your last switch in it (looked for from its end), then only what is written after
                _logFile = newest;
                _logAvatar = null;
                (_me, _meId) = FindMe(fs);
                key = "] Switching " + _me + " to avatar ";
                long len = fs.Length, at = _me.Length > 0 ? LastIndexOf(fs, Encoding.UTF8.GetBytes(key)) : -1;
                if (at >= 0)
                {
                    fs.Position = at;
                    var line = new byte[Math.Min(len - at, 4096)];
                    int got = fs.Read(line, 0, line.Length);
                    string s = Encoding.UTF8.GetString(line, 0, got);
                    int e = s.IndexOf('\n');
                    _logAvatar = s[key.Length..(e < 0 ? s.Length : e)].TrimEnd('\r').Trim();
                }
                _logPos = len;
            }
            if (_me.Length > 0 && fs.Length > _logPos)
            {
                fs.Position = _logPos;
                var buf = new byte[Math.Min(fs.Length - _logPos, 8 << 20)];
                int n = fs.Read(buf, 0, buf.Length);
                // whole lines only: a line still being written waits for the next look
                int end = Array.LastIndexOf(buf, (byte)'\n', n - 1);
                if (end >= 0)
                {
                    string text = Encoding.UTF8.GetString(buf, 0, end + 1);
                    int i = text.LastIndexOf(key, StringComparison.Ordinal);
                    if (i >= 0)
                    {
                        int e = text.IndexOf('\n', i);
                        _logAvatar = text[(i + key.Length)..(e < 0 ? text.Length : e)].TrimEnd('\r').Trim();
                    }
                    _logPos += end + 1;
                }
            }
            return (_logAvatar, _meId.Length > 0 ? _meId : null);
        }
        catch (Exception) { return (null, null); }
    }

    // "User Authenticated: Name (usr_...)" near the log's start: your name (the switch lines say it) and id (your config folder)
    static (string name, string id) FindMe(FileStream fs)
    {
        fs.Position = 0;
        var buf = new byte[Math.Min(fs.Length, 4 << 20)];
        int n = fs.Read(buf, 0, buf.Length);
        string head = Encoding.UTF8.GetString(buf, 0, n);
        foreach (var key in new[] { "User Authenticated: ", "post login checks for " })
        {
            int i = head.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) continue;
            int e = head.IndexOf('\n', i);
            string s = head[(i + key.Length)..(e < 0 ? head.Length : e)].TrimEnd('\r').Trim();
            int u = s.LastIndexOf(" (usr_", StringComparison.Ordinal);
            if (u > 0 && s.EndsWith(')')) return (s[..u], s[(u + 2)..^1]);
            return (s, "");
        }
        return ("", "");
    }

    // the last occurrence of a byte pattern, read backwards in 1 MB steps (logs reach 50+ MB)
    static long LastIndexOf(FileStream fs, byte[] pat)
    {
        const int step = 1 << 20;
        var buf = new byte[step + pat.Length];
        long end = fs.Length;
        while (end > 0)
        {
            long start = Math.Max(0, end - step);
            int len = (int)Math.Min(buf.Length, fs.Length - start);
            fs.Position = start;
            int n = fs.Read(buf, 0, len);
            for (int i = n - pat.Length; i >= 0; i--)
            {
                int k = 0;
                while (k < pat.Length && buf[i + k] == pat[k]) k++;
                if (k == pat.Length) return start + i;
            }
            end = start;
        }
        return -1;
    }

    void Publish(AvatarState s)
    {
        s.Version = Interlocked.Increment(ref _version);
        _pub = s;
    }

    // the log says when it changes (never a parameter, never the avatar's name)
    void Said(AvatarStatus s, AvatarSource src, string id)
    {
        if (s == _said && src == _saidSrc && id == _saidId) return;
        (_said, _saidSrc, _saidId) = (s, src, id);
        var a = _pub.Avatar;
        Log.Info("avatar: " + s switch
        {
            AvatarStatus.Ready => $"{a?.Buttons.Length ?? 0} button(s) of {a?.Writable ?? 0} writable parameter(s), " + src switch
            {
                AvatarSource.Live => "live from VRChat's OSCQuery",
                AvatarSource.Log => "the avatar from VRChat's log (no live values)",
                _ => "the config written last (no live values)",
            },
            AvatarStatus.NoVrchat => "VRChat is not running",
            AvatarStatus.NoConfig => "no OSC config for the avatar worn",
            _ => "looking",
        });
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(1000);
        _http?.Dispose();
    }
}
