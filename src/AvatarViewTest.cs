using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Orbit;

// --snapshot's part for the avatar toggles (Sys/AvatarToggles.cs, WristAvatar*.cs, SettingsView.Avatar.cs) - never the real VRChat,
// its OSC folder, its log or its OSC port:
//  - the data (once): a made-up avatar config in VRChat's shape (built-ins with outputs only, face tracking, PhysBone outputs, VRCFury's
//    prefixes and internals, Orbit's own parameters, toggles, ints, a float) in a folder of the tool's own - the filter, the names, the
//    kinds, the default order; a made-up OSCQuery (HOST_INFO, /avatar's tree as VRChat answers it) through the link's Query; a recorder
//    in place of the OSC sender (a tap's address and type, a slider's drag sent at most 20 times a second; the bytes of OSC's T / i / f);
//    a tap held against an old read, then corrected when VRChat did not take it; VRChat not running; no OSCQuery: the avatar from a
//    made-up output_log (your own switch, not another player's; a later switch followed), else the config written last; no config.
//  - each style: a tap on a toggle and on a stepper sends it, a tap on the header opens all of them (the stick scrolls, a tap there
//    sends too), the X closes; no garbage a frame at rest; pictures wrist_avatar_<case><tag>.png; the layout check for its sizes.
//  - the editor with an avatar widget selected (Modern, Cyber, 95 with its dialog): pictures settings_avatar_<case>.png, a pick changes
//    the widget, no control crossing another.
// ORBIT_SNAP_ONLY=avatar runs it alone. --avatartest reads the REAL OSC folder and logs counts only (AvatarProbe).
static class AvatarViewTools
{
    public const string FoxId = "avtr_00000000-0000-4000-8000-00000000f0c5", CatId = "avtr_00000000-0000-4000-8000-0000000000ca";

    // the made-up fox's parameters: name, VRChat's type, writable (an input); what the filter leaves out at the end
    static readonly (string name, string type, bool input)[] FoxParams =
    {
        ("VelocityX", "Float", false), ("GestureLeft", "Int", false), ("Upright", "Float", false),
        ("VF12_Clothes/Hoodie", "Bool", true), ("VF3_VF12_Props/Hat", "Bool", true), ("Clothes/Hat", "Bool", true), ("glassesOn", "Bool", true),
        ("ToggleTail", "Bool", true), ("Outfit", "Int", true), ("HueShift", "Float", true), ("ShowWings", "Int", true), ("RGBMode", "Int", true),
        ("Lipstick", "Bool", true), ("EyeGlow", "Bool", true), ("Jump&Fall", "Bool", true), ("Go/Locomotion", "Bool", true),
        ("Clothes/Jacket", "Bool", true), ("Clothes/Boots", "Bool", true), ("Clothes/Gloves", "Bool", true), ("Clothes/Scarf", "Bool", true),
        ("Props/Phone", "Bool", true), ("Props/Guitar", "Bool", true), ("Props/Backpack", "Bool", true), ("Emotes/Wave", "Bool", true),
        ("Sparkles", "Bool", true), ("Collar", "Bool", true), ("Headphones", "Bool", true), ("Shades", "Bool", true),
        // left out
        ("EarsPhys_IsGrabbed", "Bool", true), ("EarsPhys_Angle", "Float", true), ("TailPhys_Stretch", "Float", true), ("FT/v2/JawOpen", "Float", true),
        ("EyeLeftX", "Float", true), ("MouthSmileLeft", "Float", true), ("v2/EyeLid", "Float", true), ("VF7_HueSync", "Float", true),
        ("OSCm/Proxy/JawOpen", "Float", true), ("isWristVisible", "Bool", true), ("VF4___internalFlag", "Bool", true),
    };

    static readonly (string name, string type, bool input)[] CatParams =
    {
        ("Whiskers", "Bool", true), ("Bell", "Bool", true), ("Size", "Float", true), ("FT/v2/EyeLeftX", "Float", true),
    };

    static string Q(string s) => System.Text.Json.JsonSerializer.Serialize(s);

    static string Json(string id, string name, (string name, string type, bool input)[] ps)
    {
        var sb = new StringBuilder("{\"id\":" + Q(id) + ",\"name\":" + Q(name) + ",\"parameters\":[");
        for (int i = 0; i < ps.Length; i++)
        {
            var (n, t, inp) = ps[i];
            string addr = Q("/avatar/parameters/" + n);
            sb.Append(i > 0 ? "," : "").Append("{\"name\":").Append(Q(n));
            if (inp) sb.Append(",\"input\":{\"address\":").Append(addr).Append(",\"type\":\"").Append(t).Append("\"}");
            sb.Append(",\"output\":{\"address\":").Append(addr).Append(",\"type\":\"").Append(t).Append("\"}}");
        }
        return sb.Append("]}").ToString();
    }

    // as VRChat writes it: UTF-8 with its BOM
    static byte[] Bytes(string json) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();

    public static AvatarConfigFile Fox() => AvatarFilter.Parse(Bytes(Json(FoxId, "Made-up Fox", FoxParams)), "fox.json");

    // the fox's values now (by its parameters' names: VRChat's tree has them all, the built-ins too)
    public static Dictionary<string, float> FoxValues() => new()
    {
        ["VelocityX"] = 0.2f, ["GestureLeft"] = 1, ["Upright"] = 1, ["VF12_Clothes/Hoodie"] = 1, ["VF3_VF12_Props/Hat"] = 0, ["Clothes/Hat"] = 1,
        ["glassesOn"] = 0, ["ToggleTail"] = 1, ["Outfit"] = 2, ["HueShift"] = 0.35f, ["ShowWings"] = 0, ["RGBMode"] = 3, ["Lipstick"] = 0,
        ["EyeGlow"] = 1, ["Jump&Fall"] = 0, ["Go/Locomotion"] = 1, ["Clothes/Jacket"] = 1, ["Clothes/Boots"] = 0, ["Clothes/Gloves"] = 1,
        ["Clothes/Scarf"] = 0, ["Props/Phone"] = 0, ["Props/Guitar"] = 1, ["Props/Backpack"] = 0, ["Emotes/Wave"] = 0, ["Sparkles"] = 1,
        ["Collar"] = 1, ["Headphones"] = 0, ["Shades"] = 1, ["EarsPhys_Angle"] = 0.4f, ["FT/v2/JawOpen"] = 0.1f,
    };

    // what the watch shows on its avatar widgets in the pictures (picked in the editor: one of each kind among them)
    public static readonly string[] Picks =
    {
        "VF12_Clothes/Hoodie", "Outfit", "HueShift", "glassesOn", "ToggleTail", "Clothes/Hat", "Lipstick", "EyeGlow", "ShowWings", "Clothes/Jacket",
        "Props/Guitar", "Sparkles", "Collar", "Shades",
    };

    // VRChat's /avatar answer: every parameter a node under /avatar/parameters (a name with / a node in a node), its type and value
    static string Tree(string id, (string name, string type, bool input)[] ps, Func<string, float> value)
    {
        var parms = new Dictionary<string, object>();
        foreach (var (n, t, _) in ps)
        {
            var at = parms;
            var parts = n.Split('/');
            string path = "/avatar/parameters";
            for (int i = 0; i < parts.Length; i++)
            {
                path += "/" + parts[i];
                if (i < parts.Length - 1)
                {
                    if (!at.TryGetValue(parts[i], out var node)) at[parts[i]] = node = new Dictionary<string, object> { ["FULL_PATH"] = path, ["ACCESS"] = 0, ["CONTENTS"] = new Dictionary<string, object>() };
                    at = (Dictionary<string, object>)((Dictionary<string, object>)node)["CONTENTS"];
                    continue;
                }
                float v = value(n);
                object val = t == "Bool" ? v >= 0.5f : t == "Int" ? (int)v : (object)v;
                at[parts[i]] = new Dictionary<string, object> { ["FULL_PATH"] = path, ["ACCESS"] = 3, ["TYPE"] = t == "Bool" ? (v >= 0.5f ? "T" : "F") : t == "Int" ? "i" : "f", ["VALUE"] = new[] { val } };
            }
        }
        var root = new Dictionary<string, object>
        {
            ["DESCRIPTION"] = "", ["FULL_PATH"] = "/avatar", ["ACCESS"] = 0,
            ["CONTENTS"] = new Dictionary<string, object>
            {
                ["change"] = new Dictionary<string, object> { ["FULL_PATH"] = "/avatar/change", ["ACCESS"] = 3, ["TYPE"] = "s", ["VALUE"] = new[] { id } },
                ["parameters"] = new Dictionary<string, object> { ["FULL_PATH"] = "/avatar/parameters", ["ACCESS"] = 0, ["CONTENTS"] = parms },
            },
        };
        return System.Text.Json.JsonSerializer.Serialize(root);
    }

    const string HostInfo = "{\"NAME\":\"VRChat-Client-0F0C5A\",\"OSC_IP\":\"127.0.0.1\",\"OSC_PORT\":9010,\"OSC_TRANSPORT\":\"UDP\",\"EXTENSIONS\":{\"ACCESS\":true,\"VALUE\":true}}";

    // a made-up link for the pictures and the layout check: frozen (a tap changes only what it shows, and goes to 'sent')
    public static AvatarLink Fake(PanelData data, AvatarState s = null, List<(string, object)> sent = null)
    {
        var l = new AvatarLink { Send = (a, v) => { if (sent != null) lock (sent) sent.Add((a, v)); } };
        l.Freeze(s ?? Live());
        data.Avatar = l;
        return l;
    }

    // the fox worn, live: its values from the made-up tree
    public static AvatarState Live(AvatarConfigFile a = null)
    {
        a ??= Fox();
        var vals = FoxValues();
        var v = new float[a.Buttons.Length];
        for (int i = 0; i < v.Length; i++) v[i] = vals.TryGetValue(a.Buttons[i].Name, out float x) ? x : 0;
        return new AvatarState { Status = AvatarStatus.Ready, Source = AvatarSource.Live, Avatar = a, Values = v };
    }

    // the fox from the log: no values known
    public static AvatarState NoLive()
    {
        var s = Live();
        for (int i = 0; i < s.Values.Length; i++) s.Values[i] = float.NaN;
        s.Source = AvatarSource.Log;
        return s;
    }

    static bool Wait(Func<bool> ok, int ms = 2000)
    {
        for (int i = 0; i < ms / 20 && !ok(); i++) Thread.Sleep(20);
        return ok();
    }

    // ---- the data ----

    public static void DataChecks(WristPanel wrist, PanelData data, string dir)
    {
        var keep = data.Avatar;
        try { FilterChecks(); LinkChecks(wrist, data, dir); }
        finally { data.Avatar = keep; }
    }

    static void FilterChecks()
    {
        var a = Fox();
        string L(string name) => a.ByName.TryGetValue(name, out int i) ? a.Buttons[i].Label : "(left out)";
        AvatarKind? K(string name) => a.ByName.TryGetValue(name, out int i) ? a.Buttons[i].Kind : null;
        Log.Info($"avatar filter: {a.Total} parameters, {a.Writable} writable (want 39, 36), {a.Buttons.Length} buttons (want 25); left out: face tracking " +
                 $"{a.Dropped[(int)AvatarFilter.Why.FaceTracking]} (want 4), PhysBone {a.Dropped[(int)AvatarFilter.Why.PhysBone]} (want 3), helpers " +
                 $"{a.Dropped[(int)AvatarFilter.Why.Helper]} (want 2), internal {a.Dropped[(int)AvatarFilter.Why.Internal]} (want 1), Orbit's own " +
                 $"{a.Dropped[(int)AvatarFilter.Why.Orbit]} (want 1), not writable {a.Dropped[(int)AvatarFilter.Why.NotWritable]} (want 3)");
        Log.Info($"avatar filter: names '{L("VF12_Clothes/Hoodie")}' (want Hoodie), '{L("VF3_VF12_Props/Hat")}' / '{L("Clothes/Hat")}' (want Hat (Props) / Hat (Clothes)), " +
                 $"'{L("glassesOn")}' (want Glasses), '{L("ToggleTail")}' (want Tail), '{L("ShowWings")}' (want Wings), '{L("RGBMode")}' (want RGB Mode), '{L("Jump&Fall")}' (want Jump&Fall), " +
                 $"'{L("EyeGlow")}' (want Eye Glow), '{L("Lipstick")}' (want Lipstick), '{L("Go/Locomotion")}' (want Locomotion)");
        bool kinds = K("VF12_Clothes/Hoodie") == AvatarKind.Toggle && K("Outfit") == AvatarKind.Stepper && K("HueShift") == AvatarKind.Slider &&
                     K("ShowWings") == AvatarKind.Toggle && K("RGBMode") == AvatarKind.Stepper;
        bool gone = new[] { "EarsPhys_IsGrabbed", "EarsPhys_Angle", "FT/v2/JawOpen", "EyeLeftX", "MouthSmileLeft", "v2/EyeLid", "VF7_HueSync", "OSCm/Proxy/JawOpen",
            "isWristVisible", "VF4___internalFlag", "VelocityX" }.All(n => !a.ByName.ContainsKey(n));
        var def = new List<int>();
        AvatarFilter.Shown(a, null, def);
        bool toggles = def.Count == AvatarFilter.DefaultCount && def.All(i => a.Buttons[i].Kind == AvatarKind.Toggle) && def.All(i => a.Buttons[i].Name != "Go/Locomotion");
        var picked = new List<int>();
        AvatarFilter.Shown(a, new List<string> { "HueShift", "Outfit", "nope", "VF12_Clothes/Hoodie" }, picked);
        bool order = picked.Count == 3 && a.Buttons[picked[0]].Name == "HueShift" && a.Buttons[picked[2]].Name == "VF12_Clothes/Hoodie";
        Log.Info($"avatar filter: a bool a toggle, an int a stepper (one named Show... a toggle), a float a slider {kinds} (want True); face tracking, PhysBone, " +
                 $"helpers, internals, Orbit's own and the read-only ones left out {gone} (want True); the default {def.Count} are toggles, the prefab's last {toggles} " +
                 $"(want True); picks shown in their order, an unknown one skipped {order} (want True)");
        // the words: VRCFury's nested prefix, a run of capitals, a number
        Log.Info($"avatar filter: '{AvatarFilter.Label("VF1_VF22_Hats/topHat2")}' (want Top Hat 2), '{AvatarFilter.Label("toggle_hood_up")}' (want Hood up), " +
                 $"'{AvatarFilter.Label("LEDColor")}' (want LED Color), '{AvatarFilter.StripVf("VF12_")}' (want VF12_)");
    }

    static void LinkChecks(WristPanel wrist, PanelData data, string dir)
    {
        // a config folder, a log folder of the tool's own; the fox and the cat; the cat written later
        string root = Path.Combine(dir, "avatar_test");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string osc = Path.Combine(root, "OSC"), avatars = Path.Combine(osc, "usr_00000000-0000-4000-8000-0000000e0e0e", "Avatars"), logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(avatars);
        Directory.CreateDirectory(logs);
        string foxFile = Path.Combine(avatars, FoxId + ".json"), catFile = Path.Combine(avatars, CatId + ".json");
        File.WriteAllBytes(foxFile, Bytes(Json(FoxId, "Made-up Fox", FoxParams)));
        File.WriteAllBytes(catFile, Bytes(Json(CatId, "Made-up Cat", CatParams)));
        File.SetLastWriteTimeUtc(foxFile, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(catFile, DateTime.UtcNow.AddMinutes(-2));
        // a made-up VRChat: its OSCQuery answers from 'vals'; it takes what is sent ('takes'), or not
        var vals = new Dictionary<string, float>(FoxValues());
        bool running = true, query = true, takes = false;
        string worn = FoxId;
        var sent = new List<(string addr, object v)>();
        string Query(string path)
        {
            if (!query) return null;
            if (path == "/?HOST_INFO") return HostInfo;
            if (path != "/avatar") return null;
            lock (vals) return Tree(worn, worn == FoxId ? FoxParams : CatParams, n => vals.TryGetValue(n, out float v) ? v : 0);
        }
        void Send(string addr, object v)
        {
            lock (sent) sent.Add((addr, v));
            if (!takes) return;
            string name = addr["/avatar/parameters/".Length..];
            lock (vals) vals[name] = v switch { bool b => b ? 1 : 0, int i => i, float f => f, _ => 0 };
        }
        (string addr, object v) Last() { lock (sent) return sent.Count == 0 ? ("", null) : sent[^1]; }
        using var l = new AvatarLink { OscDir = osc, LogDir = logs, Query = Query, Running = () => running, Send = Send, Hold = 0.3, Confirm = 0.05, OscPort = 9000 };
        data.Avatar = l;
        l.ReadNow();
        var s = l.State;
        int I(string name) => s.Avatar != null && s.Avatar.ByName.TryGetValue(name, out int i) ? i : -1;
        int hood = I("VF12_Clothes/Hoodie"), outfit = I("Outfit"), hue = I("HueShift");
        bool live = s.Ready && s.Live && s.Avatar.Id == FoxId && hood >= 0 && s.Values[hood] == 1 && s.Values[outfit] == 2 && MathF.Abs(s.Values[hue] - 0.35f) < 0.001f;
        Log.Info($"avatar link: the avatar worn and its values from a made-up OSCQuery {live} (want True), the watch says '{wrist.AvatarSays(data)}' (want ready), " +
                 $"VRChat's OSC port from HOST_INFO {l.SendPort} (want 9010)");
        // a tap: shown at once and sent as a bool; VRChat does not take it: held against reads for 0.3 s, then back
        l.Toggle(hood);
        bool atOnce = l.State.Values[hood] == 0;
        var (a1, v1) = Last();
        bool asBool = a1 == "/avatar/parameters/VF12_Clothes/Hoodie" && v1 is bool b1 && !b1;
        l.ReadNow();
        bool held = l.State.Values[hood] == 0 && l.State.Pending;
        Thread.Sleep(350);
        l.ReadNow();
        bool back = l.State.Values[hood] == 1 && !l.State.Pending;
        // VRChat takes it: it stays
        takes = true;
        l.Toggle(hood);
        Thread.Sleep(350);
        l.ReadNow();
        bool stays = l.State.Values[hood] == 0;
        Log.Info($"avatar link: a tap shown at once {atOnce} (want True), sent as the bool False to its address {asBool} (want True), held against an older read " +
                 $"{held} (want True), back when VRChat did not take it {back} (want True), kept when it did {stays} (want True)");
        // a stepper: an int; a slider dragged: a float, at most 20 a second, its last value always
        l.Step(outfit, +1);
        var (a2, v2) = Last();
        bool asInt = a2 == "/avatar/parameters/Outfit" && v2 is int i2 && i2 == 3 && l.State.Values[outfit] == 3;
        int before;
        lock (sent) before = sent.Count;
        for (float v = 0.40f; v <= 0.901f; v += 0.05f) l.Set(hue, v);
        int during;
        lock (sent) during = sent.Count - before;
        Thread.Sleep(70);
        l.Flush(Clock.Now);
        var (a3, v3) = Last();
        int total;
        lock (sent) total = sent.Count - before;
        bool asFloat = a3 == "/avatar/parameters/HueShift" && v3 is float f3 && MathF.Abs(f3 - 0.9f) < 0.001f;
        Log.Info($"avatar link: a stepper sent the int 3 {asInt} (want True); a slider dragged through 11 values sent {total} (want fewer than 4), its last as the " +
                 $"float 0.9 {asFloat} (want True), {during} while dragging (want 1)");
        // OSC's bytes: T / F, i, f after the address; an address with letters ASCII has not, in UTF-8
        bool tags = Encoding.ASCII.GetString(OscSender.Encode("/avatar/parameters/X", true)).Contains(",T") &&
                    Encoding.ASCII.GetString(OscSender.Encode("/avatar/parameters/X", 3)).Contains(",i") &&
                    Encoding.ASCII.GetString(OscSender.Encode("/avatar/parameters/X", 0.5f)).Contains(",f");
        var utf = OscSender.Encode("/avatar/parameters/Füle", true);
        bool utf8 = Encoding.UTF8.GetString(utf).StartsWith("/avatar/parameters/Füle\0", StringComparison.Ordinal) && utf.Length % 4 == 0;
        Log.Info($"avatar osc: a bool T, an int i, a float f {tags} (want True), an address with ü in UTF-8, padded to 4 {utf8} (want True)");
        // another avatar worn: its buttons
        worn = CatId;
        l.ReadNow();
        bool cat = l.State.Ready && l.State.Avatar.Name == "Made-up Cat" && l.State.Avatar.Buttons.Length == 3;
        // one without a config
        worn = "avtr_00000000-0000-4000-8000-000000000000";
        l.ReadNow();
        bool none = l.State.Status == AvatarStatus.NoConfig;
        string sayNone = wrist.AvatarSays(data);
        // VRChat not running
        running = false;
        l.ReadNow();
        bool off = l.State.Status == AvatarStatus.NoVrchat;
        string sayOff = wrist.AvatarSays(data);
        Log.Info($"avatar link: another avatar's buttons {cat} (want True); one without a config {none} '{sayNone}' (want True No OSC config for this avatar); " +
                 $"VRChat not running {off} '{sayOff}' (want True VRChat isn't running)");
        // no OSCQuery: your own last switch in the newest log (another player's after it does not count), a later one followed; no
        // log: the config written last
        running = true;
        query = false;
        string log = Path.Combine(logs, "output_log_2026-10-18_09-00-00.txt");
        File.WriteAllText(log, "2026.10.18 09:00:01 Debug      -  User Authenticated: Made-up Tester (usr_00000000-0000-4000-8000-0000000e0e0e)\n" +
                               "2026.10.18 09:00:05 Debug      -  [Behaviour] Switching Made-up Tester to avatar Made-up Fox\n" +
                               "2026.10.18 09:00:09 Debug      -  [Behaviour] Switching Someone Else to avatar Made-up Cat\n");
        l.ReadNow();
        bool fromLog = l.State.Ready && l.State.Source == AvatarSource.Log && l.State.Avatar.Name == "Made-up Fox" && l.State.Values.All(float.IsNaN);
        File.AppendAllText(log, "2026.10.18 09:10:00 Debug      -  [Behaviour] Switching Made-up Tester to avatar Made-up Cat\n");
        l.ReadNow();
        bool followed = l.State.Source == AvatarSource.Log && l.State.Avatar?.Name == "Made-up Cat";
        File.Delete(log);
        l.ReadNow();
        bool newest = l.State.Source == AvatarSource.Newest && l.State.Avatar?.Name == "Made-up Cat";
        Log.Info($"avatar link: no OSCQuery - your own switch from the log, another player's not {fromLog} (want True), a later switch followed {followed} (want True); " +
                 $"no log - the config written last {newest} (want True)");
        // a local test build worn (its id no file name): live, its config the one of the name in the log
        query = true;
        worn = "local:00000000-0000-4000-8000-00000000f0c5";
        File.WriteAllText(Path.Combine(logs, "output_log_2026-10-18_10-00-00.txt"),
            "2026.10.18 10:00:01 Debug      -  User Authenticated: Made-up Tester (usr_00000000-0000-4000-8000-0000000e0e0e)\n" +
            "2026.10.18 10:00:05 Debug      -  [Behaviour] Switching Made-up Tester to avatar Made-up Fox\n");
        l.ReadNow();
        bool local = l.State.Ready && l.State.Live && l.State.Avatar?.Name == "Made-up Fox";
        Log.Info($"avatar link: a local test build worn - live, its config found by the log's name {local} (want True)");
        // the configs were only read
        bool same = File.ReadAllBytes(foxFile).SequenceEqual(Bytes(Json(FoxId, "Made-up Fox", FoxParams)));
        Log.Info($"avatar link: the configs read, never written {same} (want True)");
        // a tree of VRChat's with the paths nested (a / in a name), bools as true / false
        var (tid, tv) = AvatarLink.ParseTree(Tree(FoxId, FoxParams, n => FoxValues().TryGetValue(n, out float v) ? v : 0));
        // a bool told by its type tag alone, a path with an escaped space
        var (_, tv2) = AvatarLink.ParseTree("{\"FULL_PATH\":\"/avatar\",\"CONTENTS\":{\"parameters\":{\"FULL_PATH\":\"/avatar/parameters\",\"CONTENTS\":{" +
            "\"A\":{\"FULL_PATH\":\"/avatar/parameters/A\",\"TYPE\":\"T\"},\"B C\":{\"FULL_PATH\":\"/avatar/parameters/B%20C\",\"TYPE\":\"f\",\"VALUE\":[0.5]}}}}}");
        Log.Info($"avatar link: VRChat's tree read: the id {tid == FoxId} (want True), a nested bool {tv?.GetValueOrDefault("/avatar/parameters/VF12_Clothes/Hoodie")} " +
                 $"(want 1), an int {tv?.GetValueOrDefault("/avatar/parameters/Outfit")} (want 2), a bool by its type tag {tv2?.GetValueOrDefault("/avatar/parameters/A")} " +
                 $"(want 1), an escaped space {tv2?.GetValueOrDefault("/avatar/parameters/B C")} (want 0.5), HOST_INFO's port {AvatarLink.HostOscPort(HostInfo)} (want 9010)");
        Directory.Delete(root, true);
    }

    // ---- each style ----

    static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    static WatchWidget W(string type, int x, int y, int w = 1, int h = 1) => new() { Type = type, X = x, Y = y, W = w, H = h };

    static List<WatchWidget> Face64() => new()
    {
        W("avatar", 0, 0), W("avatar", 1, 0, 2), W("avatar", 3, 0, 3), W("avatar", 0, 1, 2, 2), W("avatar", 2, 1, 3, 2), W("time", 5, 1), W("battery", 5, 2),
        W("keyboard", 0, 3), W("desktops", 1, 3), W("night", 2, 3), W("avatar", 3, 3, 2), W("fps", 5, 3),
    };

    static List<WatchWidget> Face43() => new() { W("avatar", 0, 0, 2, 2), W("avatar", 2, 0, 2), W("avatar", 2, 1), W("time", 3, 1), W("avatar", 0, 2, 4) };

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background, data.Avatar);
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            bool classic = Theme.Classic, term = Theme.Retro == RetroStyle.Terminal && classic;
            var sent = new List<(string a, object v)>();
            string Last() { lock (sent) return sent.Count == 0 ? "" : $"{sent[^1].a} {sent[^1].v}"; }
            // a 2x2: a tap on its first tile (the hoodie, on) switches it off; on its stepper's + (the outfit) steps it up
            Fake(data, Live(), sent);
            wcfg.Widgets = new List<WatchWidget> { W("avatar", 0, 0, 2, 2), W("avatar", 2, 0) };
            wrist.Settle(); wrist.Draw(data); wrist.Draw(data);
            var t0 = wrist.AvatarTilePoint(data, 0);
            if (t0 is { } p0) Tap(wrist, data, p0);
            string hood = Last();
            var t1 = wrist.AvatarTilePoint(data, 1, "+");
            if (t1 is { } p1) Tap(wrist, data, p1);
            string step = Last();
            // the 1x1: its first button, a tap on it switches it back on
            var c1 = WatchWidgets.CellRect(2, 0, 1, 1, classic);
            float k = classic ? 2 : 1;
            Tap(wrist, data, new Vector2((c1.X + c1.Width / 2) * k, (c1.Y + c1.Height * 0.6f) * k));
            string one = Last();
            Log.Info($"avatar view{tag}: a tap on a toggle sent '{hood}' (want /avatar/parameters/VF12_Clothes/Hoodie False), on a stepper's + '{step}' " +
                     $"(want /avatar/parameters/Outfit 3), on the 1x1 '{one}' (want /avatar/parameters/VF12_Clothes/Hoodie True)");
            // a tap on the 2x2's header opens all of them; the stick scrolls; a tap in it sends; its X closes
            var card = WatchWidgets.CellRect(0, 0, 2, 2, classic);
            Tap(wrist, data, new Vector2((card.X + card.Width * 0.5f) * k, (card.Y + (classic ? 7 : 14)) * k));
            for (int i = 0; i < 11; i++) { Thread.Sleep(30); wrist.Draw(data); }
            bool opened = wrist.IsOpen && wrist.DetailKind == "avatar";
            int tiles = wrist.AvatarTilesDrawn;
            var mid = new Vector2(WristPanel.TW / 2, WristPanel.TH / 2);
            wrist.PointerMove(Hand.Left, mid);
            for (int i = 0; i < 3; i++) { wrist.PointerScroll(Hand.Left, -1, mid); wrist.Draw(data); }
            wrist.PointerLeave(Hand.Left); wrist.Draw(data);
            float scrolled = wrist.AvatarScroll;
            var a = data.Avatar.State.Avatar;
            int n0;
            lock (sent) n0 = sent.Count;
            var tp = classic ? wrist.ControlPoint(data, "wav95list/0") : wrist.ControlPoint(data, Ui.Id("wavd", a.ByName["Sparkles"]));
            if (tp is { } pp) Tap(wrist, data, pp);
            string inView;
            lock (sent) inView = sent.Count > n0 ? Last() : "nothing";
            var x = wrist.ControlPoint(data, classic ? "wd95x" : "wclose");
            if (x is { } xp) Tap(wrist, data, xp);
            for (int i = 0; i < 11; i++) { Thread.Sleep(30); wrist.Draw(data); }
            bool shut = !wrist.IsOpen;
            wrist.CloseForTest();
            Log.Info($"avatar view{tag}: a tap on the header opened it {opened} (want True) with {tiles} buttons drawn (want more than 0), the stick scrolled it " +
                     $"{scrolled > 0} (want True), a tap in it sent '{inView}' (want a parameter), its X closed it {shut} (want True)");
            // no garbage a frame at rest (only avatar widgets: the time's own text is not this widget's)
            wcfg.Widgets = Face64().Where(x => x.Type == "avatar").ToList();
            long Bytes()
            {
                wrist.Settle();
                for (int i = 0; i < 4; i++) wrist.Draw(data);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20; i++) wrist.Draw(data);
                return (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            }
            Fake(data, Live());
            long rest = Bytes();
            Fake(data, new AvatarState { Status = AvatarStatus.NoVrchat });
            long none = Bytes();
            Fake(data, Live());
            wrist.OpenForTest("avatar");
            long open = Bytes();
            wrist.CloseForTest();
            // (the light's opened window in this style: what the style's own window makes - 7's and the Terminal's are not 0)
            LightsViewTools.Fake(data);
            wcfg.Widgets = new List<WatchWidget> { W("light", 0, 0, 2, 1) };
            wrist.OpenForTest("light");
            long shared = Bytes();
            wrist.CloseForTest();
            Log.Info($"avatar view{tag}: a frame at rest makes {rest} B of garbage (want 0), without VRChat {none} B (want 0), opened {open} B (want 0, or no " +
                     $"more than the style's own window: the light's opened makes {shared} B)");
            // the pictures
            void Shot(string name, List<WatchWidget> face, AvatarState s, string grid = null, bool openIt = false)
            {
                Fake(data, s);
                wcfg.Grid = grid;
                wcfg.Widgets = face;
                wrist.Settle();
                if (openIt && !wrist.OpenForTest("avatar")) Log.Warn("avatar view: nothing to open");
                wrist.Draw(data); wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_avatar_{name}{tag}.png"));
                wrist.CloseForTest();
            }
            Shot("face", Face64(), Live());
            Shot("face43", Face43(), Live(), "4x3");
            Shot("open", Face64(), Live(), null, true);
            Shot("nolive", Face64(), NoLive());
            Shot("nolive_open", Face64(), NoLive(), null, true);
            Shot("none", Face64(), new AvatarState { Status = AvatarStatus.NoVrchat });
            Shot("noconfig_open", Face64(), new AvatarState { Status = AvatarStatus.NoConfig }, null, true);
            wcfg.Grid = null;
            Log.Info($"avatar view{tag}: 7 pictures (wrist_avatar_*{tag}.png)");
        }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background, data.Avatar) = keep;
        }
    }
}

// --avatartest: the REAL OSC folder read (read only) and only counts logged - how many avatars, writable parameters, buttons after the
// filter and why the others are not - never a name or an id
static class AvatarProbe
{
    public static int Run()
    {
        var l = new AvatarLink();
        string root = l.OscDir;
        if (!Directory.Exists(root)) { Log.Info("avatartest: no VRChat OSC folder"); return 1; }
        var users = Directory.GetDirectories(root, "usr_*");
        int n = 0, writable = 0, buttons = 0, unreadable = 0;
        var why = new int[(int)AvatarFilter.Why.Count];
        int[] kinds = new int[3];
        Log.Info($"avatartest: {users.Length} user folder(s)");
        foreach (var u in users)
        {
            string dir = Path.Combine(u, "Avatars");
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "avtr_*.json"))
            {
                n++;
                AvatarConfigFile a;
                try { a = AvatarFilter.Parse(File.ReadAllBytes(f)); }
                catch (Exception) { unreadable++; continue; }
                writable += a.Writable;
                buttons += a.Buttons.Length;
                for (int i = 0; i < why.Length; i++) why[i] += a.Dropped[i];
                int t = 0, st = 0, sl = 0;
                foreach (var b in a.Buttons) { if (b.Kind == AvatarKind.Toggle) t++; else if (b.Kind == AvatarKind.Stepper) st++; else sl++; }
                kinds[0] += t; kinds[1] += st; kinds[2] += sl;
                Log.Info($"avatartest: avatar {n}: {a.Total} parameters, {a.Writable} writable, {a.Buttons.Length} buttons ({t} toggles, {st} steppers, {sl} sliders)");
            }
        }
        Log.Info($"avatartest: {n} avatar(s) ({unreadable} unreadable), {writable} writable parameters, {buttons} buttons ({kinds[0]} toggles, {kinds[1]} steppers, " +
                 $"{kinds[2]} sliders); left out: face tracking {why[(int)AvatarFilter.Why.FaceTracking]}, PhysBone / contacts {why[(int)AvatarFilter.Why.PhysBone]}, " +
                 $"helpers {why[(int)AvatarFilter.Why.Helper]}, internals {why[(int)AvatarFilter.Why.Internal]}, VRChat's own {why[(int)AvatarFilter.Why.BuiltIn]}, " +
                 $"Orbit's own {why[(int)AvatarFilter.Why.Orbit]}, read only {why[(int)AvatarFilter.Why.NotWritable]}");
        return n > 0 ? 0 : 1;
    }
}

static partial class Tools
{
    // ORBIT_SNAP_ONLY=avatar: the link's checks, the views' checks and pictures and the layout check (its sizes and opened), every style;
    // the editor with an avatar widget selected
    static int AvatarOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        AvatarViewTools.DataChecks(wrist, data, dir);
        // the pictures' picks: one of each kind among the first
        (cfg.Data.Avatars ??= new AvatarsConfig()).Shown[AvatarViewTools.FoxId] = new List<string>(AvatarViewTools.Picks);
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            AvatarViewTools.Run(wrist, data, cfg, gpu, dir, tag);
            AvatarViewTools.Fake(data);
            string f0 = cfg.Data.Wrist.Face;
            LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag.Length > 0 ? tag[1..] : "modern", onlyKind: "avatar");
            cfg.Data.Wrist.Face = f0;
            cfg.Data.Wrist.Widgets = null;
        }
        AvatarEditor(wrist, data, cfg, painter, gpu, dir);
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        AvatarViewTools.Fake(data);
        StylesRun(wrist, data, painter, cfg, gpu, dir);           // every kind in every look (the avatar's among them): edges, bands, contrast
        System.Globalization.CultureInfo.CurrentCulture = culture;
        Log.Info("snapshot (avatar only) written to " + dir);
        return 0;
    }

    // the watch editor with an avatar widget selected: Modern, Cyber (chips under the watch), 95 (its line, its dialog); a pick changes
    // what the widget shows; no control crosses another
    static void AvatarEditor(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        using var kb = new KeyboardPanel(gpu, painter, cfg);
        var host = new FakeHost { Cfg = cfg, GpuName = gpu.AdapterName, MonitorList = new List<MonitorInfo>(), Data = data, Wrist = wrist, Keyboard = kb };
        var view = new SettingsView(host);
        var ui = new Ui(painter);
        using var surf = new Surface(gpu, 1500, 960);
        var ptr = new PointerState();
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page);
        var picks0 = cfg.Data.Avatars.Shown[AvatarViewTools.FoxId];
        var hits = new List<(string id, RectangleF r)>();
        try
        {
            wcfg.Face = "custom";
            wcfg.Grid = null; wcfg.Pages = null; wcfg.Page = 0;
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "avatar", X = 0, Y = 0, W = 3, H = 2 }, new() { Type = "time", X = 3, Y = 0, W = 3, H = 1 } };
            AvatarViewTools.Fake(data);
            view.Tab = SettingsView.TabWrist;
            view.EditingWatch = true;
            view.SelectKindForTest("avatar");
            int Crossings()
            {
                var list = hits.GroupBy(h => h.id).Select(g => g.Last()).Where(h => h.r.Width > 0 && h.r.Height > 0).ToList();
                int n = 0;
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var x = RectangleF.Intersect(list[i].r, list[j].r);
                        bool inside = RectangleF.Union(list[i].r, list[j].r) == list[i].r || RectangleF.Union(list[i].r, list[j].r) == list[j].r;
                        if (x.Width < 1.5f || x.Height < 1.5f || inside) continue;
                        n++;
                        Log.Warn($"avatar editor: '{list[i].id}' crosses '{list[j].id}'");
                    }
                return n;
            }
            void Draw(bool classic)
            {
                wrist.Settle();
                wrist.Draw(data);
                wrist.Draw(data);
                hits.Clear();
                ui.HitSpy = (id, r) => hits.Add((id, r));
                ui.ResetAnimations();
                if (classic) view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
                else { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
                ui.HitSpy = null;
            }
            foreach (var (style, name) in new[] { ((string)null, "modern"), (CyberHud.Id, "cyber"), ("classic", "95"), ("xp", "xp"), ("aero", "aero"), ("mac", "mac"), ("terminal", "term") })
            {
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                bool classic = Theme.Classic;
                view.SelectKindForTest("avatar");
                Draw(classic);
                surf.SavePng(gpu, Path.Combine(dir, $"settings_avatar_{name}.png"));
                int crossed = Crossings();
                int dialog = 0;
                if (classic)
                {
                    view.AvatarDialogForTest(true);
                    Draw(true);
                    surf.SavePng(gpu, Path.Combine(dir, $"settings_avatar_{name}_dialog.png"));
                    dialog = Crossings();
                    view.AvatarDialogForTest(false);
                }
                Log.Info($"avatar editor {name}: {crossed} control(s) crossing another (want 0){(classic ? $", the dialog {dialog} (want 0)" : "")}");
            }
            // the desktop window: letters typed narrow the chips ("ha": Hat (Props), Hat (Clothes), Shades)
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            var ask0 = view.AskFile;
            view.AskFile = (_, _, _) => { };
            view.SelectKindForTest("avatar");
            view.TypeKey('h'); view.TypeKey('a');
            Draw(false);
            surf.SavePng(gpu, Path.Combine(dir, "settings_avatar_search.png"));
            int chips = hits.Where(h => h.id.StartsWith("wavp", StringComparison.Ordinal)).Select(h => h.id).Distinct().Count();
            view.TypeKey((char)27);
            Draw(false);
            int all = hits.Where(h => h.id.StartsWith("wavp", StringComparison.Ordinal)).Select(h => h.id).Distinct().Count();
            view.AskFile = ask0;
            Log.Info($"avatar editor: \"ha\" typed in the desktop window leaves {chips} chips (want 3), Esc all of the first page {all} (want 16)");
            // a pick in the editor: the widget shows it at its end (the first pick starts from what the widget showed)
            cfg.Data.Avatars.Shown.Remove(AvatarViewTools.FoxId);
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            string before = wrist.AvatarShownLabels(data);
            var fox = data.Avatar.State.Avatar;
            view.PickForTest(ui, fox, fox.ByName["HueShift"]);
            wrist.Draw(data);
            string after = wrist.AvatarShownLabels(data);
            var mine = cfg.Data.Avatars.Shown.GetValueOrDefault(AvatarViewTools.FoxId);
            Log.Info($"avatar editor: a pick added Hue Shift after the {before.Split(", ").Length} shown {after == before + ", Hue Shift"} (want True), kept for this " +
                     $"avatar only {mine?.Count == 13 && cfg.Data.Avatars.Shown.Count == 1} (want True)");
        }
        finally
        {
            ui.HitSpy = null;
            view.SelectKindForTest(null);
            view.EditingWatch = false;
            cfg.Data.Avatars.Shown[AvatarViewTools.FoxId] = picks0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page) = keep;
        }
    }
}
