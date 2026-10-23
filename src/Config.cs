using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orbit;

// Where an overlay lives. Attach = world | left | right | head.
// M is the OpenVR 3x4 matrix (m0..m11): world transform for "world",
// otherwise the transform relative to that tracked device. Null = not placed yet.
public sealed class PlacementConfig
{
    public string Attach { get; set; } = "world";
    public float[] M { get; set; }
}

public sealed class WristConfig
{
    public bool Enabled { get; set; } = true;
    public string Hand { get; set; } = "left";
    public bool AlwaysVisible { get; set; } = false;
    public float Width { get; set; } = 0.13f;
    public bool ShowPerformance { get; set; } = true;
    public bool ShowBatteries { get; set; } = true;
    public bool ShowMedia { get; set; } = true;
    public string Face { get; set; } = "full";          // full | analog | minimal | time | custom
    public bool PerfGraph { get; set; } = false;        // frame-time graph instead of the numbers
    public float[] Offset { get; set; }   // relative to the hand; null = default watch spot
    public bool Locked { get; set; }      // the grip can't move or resize the watch (no accidental nudges)
    public List<WatchWidget> Widgets { get; set; }      // the custom face: widgets on a 6 x 4 grid (null = never set up)
    public string Background { get; set; } = "panel";   // custom face: panel (one watch panel) | cards (each widget its own card)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int[] MinesBest { get; set; }                // Minesweeper's best times in seconds: 9 x 9, 16 x 16 (0 = none; null until a win)
    // The custom face's pages (1.12): Widgets above is page 1, as it always was; these are written only once set, so a face
    // that never had pages saves exactly as before.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Grid { get; set; }                    // page 1's grid: null = 6x4 | 4x3
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Name { get; set; }                    // page 1's name (null = "Page 1")
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WatchPage> Pages { get; set; }          // pages 2, 3, ... (null = only page 1)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Page { get; set; }                       // the page the watch shows (0 = page 1)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PagesVersion { get; set; }               // 1 = the VRChat and Performance pages were added once (deleting them keeps them gone)
}

// A page of the custom watch face after the first (WristConfig.Widgets is page 1)
public sealed class WatchPage
{
    public string Name { get; set; }
    public string Grid { get; set; } = "6x4";           // 6x4 | 4x3
    public List<WatchWidget> Widgets { get; set; } = new();
}

// One widget of the custom watch face, in grid cells (see WatchWidgets).
// Its own look (modern style only; Bento.Make): written only once set, so a face never styled saves as it always did.
public sealed class WatchWidget
{
    public string Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 1;
    public int H { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Style { get; set; }                   // null = the Bento card | tint | solid | twotone | header | outline | glass
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? Color { get; set; }                    // null = automatic (the accent; purple / blue for CPU and memory)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? Color2 { get; set; }                   // two-tone's second colour (null = made from Color)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> Items { get; set; }             // a button group's buttons (button kinds' ids, 2-6)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Variant { get; set; }                 // a clock's design (WatchWidgets.Variants); null = its size's default
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Name { get; set; }                    // a folder's name (null = "Folder")
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WatchWidget> Children { get; set; }     // a folder's widgets, each where it is on the folder's own grid (GridSpec.Folder)
}

public sealed class DesktopConfig
{
    public string Id { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public float Width { get; set; } = 0.9f;
    public float Opacity { get; set; } = 1f;
    public float Curvature { get; set; } = 0f;
    public bool Mouse { get; set; } = true;
    public PlacementConfig Place { get; set; } = new();
    public string Group { get; set; }        // monitors snapped together share a group (one row)
    public int GroupIndex { get; set; }      // position in that row, left to right
}

// One application window mirrored into VR. Found again after a restart by program + window class
// (the last seen title breaks ties between several windows of the same program).
public sealed class WindowConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string Process { get; set; } = "";
    public string Class { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public float Width { get; set; } = 0.7f;
    public float Opacity { get; set; } = 1f;
    public float Curvature { get; set; } = 0f;
    public bool Mouse { get; set; } = true;
    public PlacementConfig Place { get; set; } = new();
}

public sealed class KeyboardConfig
{
    public bool Visible { get; set; } = false;
    public bool Sounds { get; set; } = true;
    public string SoundStyle { get; set; } = "click";  // click | muted (older values fall back to click)
    public float SoundVolume { get; set; } = 0.6f;
    public string Language { get; set; } = "windows";  // windows = follow the active Windows layout | en-US = built-in English
    public bool AutoShow { get; set; } = true;         // pops up when a click from VR lands in a text field
    public float Width { get; set; } = 0.62f;
    public PlacementConfig Place { get; set; } = new();
}

public sealed class SettingsPanelConfig
{
    public float Width { get; set; } = 0.85f;
    public PlacementConfig Place { get; set; } = new();
}

// Hexpad's stream dock as a floating panel (only when Hexpad runs; see Sys/HexpadLink.cs)
public sealed class DockConfig
{
    public bool Enabled { get; set; } = true;     // look for Hexpad (its pipe); nothing shows without it
    public bool Visible { get; set; }             // the floating panel is open
    public bool Input { get; set; } = true;       // the laser presses keys and turns knobs
    public float Width { get; set; } = 0.32f;
    public PlacementConfig Place { get; set; } = new();
}

// Who is in the VRChat instance (Sys/RoomWatcher.cs, Panels/RoomPanel.cs)
public sealed class RoomConfig
{
    public bool Enabled { get; set; } = true;            // follow VRChat's log while SteamVR runs
    public bool Visible { get; set; }                    // the floating panel is open
    public float Width { get; set; } = 0.5f;
    public PlacementConfig Place { get; set; } = new();
    public bool NotifyFriends { get; set; } = true;      // a card when a friend (VRCX) comes or goes
    public bool NotifyStarred { get; set; } = true;
    public bool NotifyEveryone { get; set; }
    public bool BuzzFriends { get; set; } = true;        // a friend joining: the watch's hand buzzes twice, the card stands out
    public List<string> Starred { get; set; } = new();   // user ids (usr_...), or names where the log has no id
}

// Theater mode (Panels/Theater.cs): the big screen's size and distance are kept from the last time
public sealed class TheaterConfig
{
    public float Width { get; set; } = 2.8f;
    public float Distance { get; set; } = 2.5f;
    public float Dim { get; set; } = 0.6f;               // how dark the world gets behind it (0 = not at all)
    public bool Curved { get; set; } = true;             // bent around you (else flat)
    public bool Bar { get; set; } = true;                // the bar under a monitor / window: Theater button + these settings
    public bool Ambilight { get; set; } = true;          // a glow round the screen in its picture's edge colours (Erik 10-18)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Auto { get; set; }                       // a video going full screen starts it by itself (Sys/AutoTheater.cs); off: not written
}

// Gaze focus (Panels/GazeFocus.cs, Erik 10-18): in a row of monitors the one you look at is drawn full size, the others smaller beside
// it. Null in Config = off with the defaults, written only once set.
public sealed class FocusConfig
{
    public bool Enabled { get; set; }
    public float Others { get; set; } = GazeFocus.OthersDefault;    // the others' width as a part of their own (0.4 .. 0.75)
    public bool Laser { get; set; } = true;          // a laser on a monitor focuses it too (off: only the look - Erik 10-18, with eye tracking)
}

// The VRChat chatbox as a status line (Sys/StatusLine.cs)
public sealed class ChatboxConfig
{
    public bool StatusLine { get; set; }                 // switched from the watch or the VRChat settings tab
    public bool ShowMedia { get; set; } = true;
    public bool ShowTime { get; set; }
    public bool ShowVrTime { get; set; }
}

// The weather widget (Sys/Weather.cs): where, and in what units. Null in Config = the defaults (Hexpad's place, °C), written only once set.
public sealed class WeatherConfig
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Location { get; set; }              // a city or "lat,lon"; null = the place set in Hexpad (none there: "Set a location")
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Units { get; set; }                 // null = °C and km/h | f = °F and mph
}

// The room light widget (Sys/RoomLights.cs): which bulb Lumos is asked about - its address, nothing of the account (Lumos holds that).
// Null in Config = none set, written only once set.
public sealed class LightsConfig
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Bulb { get; set; }                  // the Tapo bulb's IP address as Lumos knows it (null: "Set the bulb")
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string BulbMac { get; set; }               // its MAC (learnt once it answers straight): found again by it when the address moves
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Key { get; set; }                   // the bulb's key, DPAPI-protected (TapoAccount): straight to the bulb; null: through Lumos
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ViaLumos { get; set; }                // "Use Lumos" picked: the app does not go straight to the bulb by itself (OrbitApp)
    // what it does by itself (Sys/LightAuto.cs; switched in the light's opened view)
    public bool OnWithVr { get; set; } = true;        // on when SteamVR starts (Lumos's "Lights on with SteamVR")
    public bool TheaterDim { get; set; } = true;      // dimmed to 10 % while theater mode plays, its brightness back after
    public bool CoverColor { get; set; }              // the colour of the cover playing
}

// The avatar toggles widget (Sys/AvatarToggles.cs): which of an avatar's parameters its widget shows, by the avatar's id - picked in the
// watch editor. An avatar not in it shows the filter's first ones. Null in Config = none picked yet, written only once set.
public sealed class AvatarsConfig
{
    public Dictionary<string, List<string>> Shown { get; set; } = new();     // avtr_... -> the parameters' names, in the order shown
    [JsonIgnore] public int Edits;                    // goes up with every pick (the watch makes its buttons again)
}

public sealed class GeneralConfig
{
    public string LaserHand { get; set; } = "both";
    public bool Haptics { get; set; } = true;
    public bool BlockGameInput { get; set; } = true;  // while a laser uses an Orbit panel, the game does not get that hand's buttons
    public int CaptureFps { get; set; } = 60;
    public bool DesktopsVisible { get; set; } = true;
    public bool AutoStart { get; set; } = true;       // wanted state; applied whenever SteamVR is reachable
    public bool QuickToggle { get; set; } = true;     // double-tap both grips = hide/show monitors + keyboard
    public bool Osc { get; set; } = true;             // XSOverlay-style avatar parameters (DXOverlay etc.)
    public int OscPort { get; set; } = 9000;
    public bool RunAsAdmin { get; set; } = true;      // lets the VR mouse/keyboard reach admin windows
    public bool Notifications { get; set; } = true;   // XSOverlay-format notifications on UDP 42069
    public bool SmartCapture { get; set; } = true;    // full frame rate only for monitors you look / point at
    public bool EyeTracking { get; set; } = true;     // the look from Virtual Desktop's eye tracking when it is there (Sys/EyeGaze.cs)
    public bool DimUnlooked { get; set; }             // the monitors you are not looking at (eye tracking) a little dimmer
    // the eye cursor (Vr/Laser.cs, with eye tracking): "follow" = the beam goes where your eyes are, the hand fine-tunes; "click" = a press on
    // a monitor while you look elsewhere on one turns the beam there; "off"
    public string EyeCursor { get; set; } = "follow";
    public bool PopOut { get; set; } = true;          // a window dragged off a monitor into the air becomes a panel of its own (OrbitApp.PopOut.cs)
    public bool PauseWhenOff { get; set; } = true;    // headset taken off: stop capturing and drawing
    public bool HeadsetPacing { get; set; } = true;   // the loop on its own clock at the headset's rate (off: once per game frame)
    public bool HighGpuPriority { get; set; } = true; // Orbit's small GPU jobs go ahead of the game's frames (admin)
    public bool RecenterOnShow { get; set; } = true;  // the monitors shown again: in front of you, as SteamVR's dashboard opens
    public float MonitorDistance { get; set; } = 1.2f; // metres: how far Recenter / Reset position / Join put the monitors (0.5 - 3)
    public int VirtualMonitors { get; set; }          // virtual monitors Orbit keeps (Sys/VirtualMonitors.cs); 0: it leaves the driver be
    public bool VirtualOnlyInVr { get; set; } = true; // ... only while SteamVR runs (Windows moves their windows back when they go)
    public string Raycast { get; set; } = "shadow";    // steamvr | shadow (compare, log) | local (no IPC)
    public string LastVersion { get; set; }            // the version that ran last: tells when an update happened
    public string WhatsNewFrom { get; set; }           // updated from this version, "What's new" not closed yet (null = nothing)
    public bool NotificationSound { get; set; } = true;
    public float NotificationAngle { get; set; } = 20f;  // degrees below the centre of view (first card)
    public int LaserSmoothing { get; set; } = 2;      // 0 off, 1 low, 2 medium, 3 high
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Language { get; set; }              // General > Language: null = English | hu (src/Lang/Lang.cs); written only once set
    // 1.14 (Sys/Alerts.cs, Vr/DoublePress.cs): written only once changed from the defaults, so a config that never touched them
    // saves exactly as before
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AlertsConfig Alerts { get; set; }          // low battery / low frame rate: a card + a buzz (null = the defaults)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string DoublePress { get; set; }           // B / Y pressed twice on an Orbit panel: null = keyboard | off | watch
    // 1.14, the Windows volume in VR (AudioMixer): which output devices' apps follow the Windows volume - null / "auto" = the ones known
    // to ignore their own volume (Virtual Desktop's), "on" = every device that says it sets its volume itself, "off" = none
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string VolumeToApps { get; set; }
}

// General > Alerts (Sys/Alerts.cs). Null in GeneralConfig = all of these defaults.
public sealed class AlertsConfig
{
    public bool Battery { get; set; } = true;         // a controller's / tracker's battery under BatteryPct: once per drop
    public int BatteryPct { get; set; } = 15;
    public bool Fps { get; set; }                     // the game under FpsPct % of the headset's rate for 10 s (then 2 min quiet)
    public int FpsPct { get; set; } = 80;
    // the GPU or the CPU at or over HotC °C for 30 s (written only once set: an Alerts block from before saves as it did)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Hot { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HotC { get; set; }                    // null = 85

    public const int HotDefault = 85;
    [JsonIgnore] public int HotLimit => HotC ?? HotDefault;

    public bool IsDefault => Battery && BatteryPct == 15 && !Fps && FpsPct == 80 && !Hot && HotLimit == HotDefault;
}

// Layouts > Per game (Sys/GameRules.cs): when this SteamVR app starts, switch to that layout and / or watch page
public sealed class GameRule
{
    public string App { get; set; }                   // the SteamVR app key (steam.app.438100 = VRChat); "*" = any other app
    public string Name { get; set; }                  // its name, as SteamVR gave it (the settings show it)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Layout { get; set; }                // a layout profile's name; null = keep the one in use
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Page { get; set; }                    // the watch page (0 = page 1); null = keep the one shown
}

public sealed class AppearanceConfig
{
    public uint Accent { get; set; } = 0xFFFF5D93;      // the Bento theme (Look tab), also what "Default look" gives
    public uint PanelColor { get; set; } = 0xFF0A0B10;
    public int LookVersion { get; set; }               // 1 = the Bento redesign's colours were applied once (1.10); 0 = an older config
    public float PanelOpacity { get; set; } = 0.92f;
    public uint LaserColor { get; set; } = 0;          // 0 = same as accent
    public bool Clock24h { get; set; } = true;
    public float LaserThickness { get; set; } = 3.5f;  // mm
    public bool LaserAlways { get; set; } = false;     // beam also when not pointing at a panel
    public bool NightMode { get; set; } = false;       // every panel in VR dimmed
    public string Style { get; set; } = "modern";      // modern | classic (Windows 95) | xp | aero (Windows 7) | mac | terminal (Skins)
    public float NightBrightness { get; set; } = 0.45f;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Font { get; set; }                   // Look > Font (modern only): one of Theme.FontChoices; null = Segoe UI Variable
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Motion { get; set; }                 // Look > Motion: null = full | reduced | off (see Gfx/Motion.cs)
}

// One saved arrangement ("Desk", "Bed", ...): where every panel was, its size and curve, night mode.
public sealed class LayoutProfile
{
    public string Name { get; set; } = "Desk";
    public bool OnWrist { get; set; } = true;          // the watch's layout button cycles through these
    public DateTime? Saved { get; set; }               // null = not used yet
    public bool NightMode { get; set; }
    public List<PanelSnapshot> Panels { get; set; } = new();
}

public sealed class PanelSnapshot
{
    public string Kind { get; set; }                   // desktop | window | keyboard
    public string Id { get; set; }
    public bool Enabled { get; set; } = true;
    public float Width { get; set; }
    public float Curvature { get; set; }
    public float Opacity { get; set; } = 1f;
    public PlacementConfig Place { get; set; }
    public string Group { get; set; }
    public int GroupIndex { get; set; }
}

public sealed class Config
{
    public int Version { get; set; } = 1;
    public WristConfig Wrist { get; set; } = new();
    public KeyboardConfig Keyboard { get; set; } = new();
    public GeneralConfig General { get; set; } = new();
    public AppearanceConfig Appearance { get; set; } = new();
    public SettingsPanelConfig SettingsPanel { get; set; } = new();
    public DockConfig Dock { get; set; } = new();
    public RoomConfig Room { get; set; } = new();
    public ChatboxConfig Chatbox { get; set; } = new();
    public TheaterConfig Theater { get; set; } = new();
    public List<DesktopConfig> Desktops { get; set; } = new();
    public List<WindowConfig> Windows { get; set; } = new();
    public List<LayoutProfile> Layouts { get; set; } = DefaultLayouts();
    public string ActiveLayout { get; set; } = "Desk";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GameRule> GameRules { get; set; }      // Layouts > Per game (null = none)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WeatherConfig Weather { get; set; }         // the weather widget's place and units (null = the defaults)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LightsConfig Lights { get; set; }           // the light widget's bulb (null = none set)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AvatarsConfig Avatars { get; set; }         // the avatar toggles' picks per avatar (null = none picked)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FocusConfig Focus { get; set; }             // Desktops > Focus the monitor you look at (null = off)

    public static List<LayoutProfile> DefaultLayouts() => new()
    {
        new() { Name = "Desk" }, new() { Name = "Bed" }, new() { Name = "Couch", OnWrist = false },
    };
}

// JSON config in %LOCALAPPDATA%\Orbit\config.json.
// Writes go to a temp file first and replace the old file, which is kept as config.json.bak.
// A file that fails to parse is renamed aside (never deleted) and defaults are used.
public sealed class ConfigStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path { get; }
    public Config Data { get; private set; }
    bool _dirty;
    double _dirtySince;

    public bool Fresh { get; }                 // there was no config file: a new install (its first start)

    public ConfigStore(string path = null)
    {
        Path = path ?? System.IO.Path.Combine(Log.Dir, "config.json");
        Fresh = !File.Exists(Path);
        Data = Load();
        WarmSave();
    }

    // The serializer makes its getters on its first save - ~40 ms, and that came on the main loop the first time something was saved
    // (Erik 10-18, the spike line: a monitor moved, 48.7 ms). A copy of the file (read again, never the live settings) is serialized once
    // on a worker at start: the first real save finds them made. Nothing is written.
    void WarmSave()
    {
        string path = Path;
        if (!File.Exists(path)) return;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var copy = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Json);
                if (copy != null) JsonSerializer.Serialize(copy, Json);
            }
            catch { }       // (only a warm-up)
        });
    }

    // set when the file exists but could not be read (locked): saving is refused, so a
    // temporary read error can't replace the user's settings with defaults
    bool _readOnly;

    Config Load()
    {
        string text = null;
        if (File.Exists(Path))
        {
            for (int attempt = 0; text == null; attempt++)
            {
                try { text = File.ReadAllText(Path); }
                catch (IOException) when (attempt < 20) { System.Threading.Thread.Sleep(100); }
                catch (Exception e)
                {
                    Log.Warn($"config can't be read ({e.Message}) - using defaults and NOT saving over it");
                    _readOnly = true;
                    return new Config();
                }
            }
        }
        try
        {
            if (text != null)
            {
                var cfg = JsonSerializer.Deserialize<Config>(text, Json);
                if (cfg != null)
                {
                    cfg.Wrist ??= new(); cfg.Keyboard ??= new(); cfg.General ??= new(); cfg.Appearance ??= new(); cfg.SettingsPanel ??= new(); cfg.SettingsPanel.Place ??= new();
                    cfg.Dock ??= new(); cfg.Dock.Place ??= new();
                    cfg.Room ??= new(); cfg.Room.Place ??= new(); cfg.Room.Starred ??= new(); cfg.Chatbox ??= new(); cfg.Theater ??= new();
                    cfg.Desktops = (cfg.Desktops ?? new()).Where(d => d != null && !string.IsNullOrEmpty(d.Id)).ToList();
                    foreach (var d in cfg.Desktops) d.Place ??= new();
                    cfg.Windows = (cfg.Windows ?? new()).Where(w => w != null && !string.IsNullOrEmpty(w.Id))
                        .GroupBy(w => w.Id).Select(g => g.First()).ToList();     // one overlay key per window
                    foreach (var w in cfg.Windows) { w.Place ??= new(); w.Title ??= ""; w.Process ??= ""; w.Class ??= ""; }
                    cfg.Layouts = (cfg.Layouts ?? new()).Where(l => l != null && !string.IsNullOrWhiteSpace(l.Name))
                        .GroupBy(l => l.Name).Select(g => g.First()).ToList();
                    if (cfg.Layouts.Count == 0) cfg.Layouts = Config.DefaultLayouts();
                    foreach (var l in cfg.Layouts) l.Panels = (l.Panels ?? new()).Where(s => s?.Id != null && s.Kind != null).ToList();
                    if (!cfg.Layouts.Any(l => l.Name == cfg.ActiveLayout)) cfg.ActiveLayout = cfg.Layouts[0].Name;
                    cfg.Keyboard.Place ??= new();
                    if (cfg.Wrist.Pages != null)
                    {
                        cfg.Wrist.Pages.RemoveAll(p => p == null);
                        foreach (var p in cfg.Wrist.Pages) p.Widgets ??= new();
                    }
                    cfg.GameRules?.RemoveAll(r => r == null || string.IsNullOrEmpty(r.App));
                    // the avatar toggles' picks (1.15): a null list (a hand-edited file) is an avatar never picked - the editor's chip
                    // click threw on it, and the entry stayed in the file for the next start
                    if (cfg.Avatars != null)
                    {
                        cfg.Avatars.Shown ??= new();
                        foreach (var k in cfg.Avatars.Shown.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) cfg.Avatars.Shown.Remove(k);
                    }
                    return cfg;
                }
            }
        }
        catch (JsonException e)
        {
            string bad = Path + $".bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            Log.Warn($"config unreadable ({e.Message}), moved to {bad}");
            try { File.Move(Path, bad); } catch { }
        }
        return new Config();
    }

    static double Now => Clock.Now;

    // The first start of another version (OrbitApp.NoteVersion): the config as the version that wrote it left it, kept next to it as
    // config.backup-<that version>.json before this one saves anything over it - config.json.bak is only the save before the last, and
    // two saves after an update nothing of the old file was left. Never written over (a copy already there stays as it is) and never
    // deleted: it is there to restore by hand. The copy made, or null (no file, a copy there already, or it failed: the log says why).
    public string KeepBackup(string version)
    {
        var safe = new string((version ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+').ToArray()).Trim('.');
        if (safe.Length == 0 || !File.Exists(Path)) return null;
        string copy = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? "",
            System.IO.Path.GetFileNameWithoutExtension(Path) + ".backup-" + safe + System.IO.Path.GetExtension(Path));
        if (File.Exists(copy)) return null;
        try
        {
            File.Copy(Path, copy, overwrite: false);
            return copy;
        }
        catch (Exception e)
        {
            Log.Warn($"update: no copy of the config ({e.Message})");
            return null;
        }
    }

    // the file as a save would write it now (--faceshot: a config that was only read must come out byte for byte the same)
    public string Serialized() => JsonSerializer.Serialize(Data, Json);

    double _lastChange;

    bool _soft, _failWarned;

    // a change worth keeping but not worth a disk write of its own (a mirrored window's title changes
    // all the time): saved together with the next real change, or when Orbit closes. A change all the same for the settings
    // (Changes: the theater's width after a resize in VR shows on Layouts > Per game)
    public void MarkSoftDirty() { _soft = true; Changes++; }

    public bool Dirty => _dirty;      // a change not on disk yet (the 95 sheet's Apply button)

    // how many times a change was marked (a grab marks one every frame): the settings draw again when it moved (SettingsView.LiveSig)
    public int Changes { get; private set; }

    public void MarkDirty()
    {
        if (!_dirty) _dirtySince = Now;
        _lastChange = Now;
        _dirty = true;
        Changes++;
    }

    // Saves once things have been quiet for a second (sliders fire every frame).
    // The app's (Background): the file is written on a worker (Erik 10-18, the spike line: a save took 13-16 ms on the main loop - the
    // write, the .bak's replace, the virus scanner looking). The text is made here, from the settings as they are now; the newest text
    // waiting is written there, never an older one over a newer (each text has its number). A write that fails marks the settings
    // changed again: the next save tries in a second. force (closing, Apply): written here, after a write in progress. The tools: here.
    public bool Background;
    readonly object _fileLock = new(), _slot = new();
    string _pendingJson;
    long _pendingSeq, _writtenSeq, _seq;
    bool _writerRunning;
    volatile bool _writeFailed;

    public void SaveIfDue(bool force = false)
    {
        if (_writeFailed) { _writeFailed = false; _dirty = true; _dirtySince = _lastChange = Now; }     // (the worker's write failed)
        // grabbing a panel marks dirty every frame: wait for a quiet second, but never longer than 10 s
        if (!(_dirty || (force && _soft)) || (!force && Now - _lastChange < 1.0 && Now - _dirtySince < 10.0)) return;
        if (_readOnly) { _dirty = _soft = false; Log.Warn("config not saved: it could not be read at start"); return; }
        _dirty = _soft = false;
        string json = JsonSerializer.Serialize(Data, Json);
        long seq = ++_seq;
        if (Background && !force)
        {
            lock (_slot)
            {
                _pendingJson = json;
                _pendingSeq = seq;
                if (_writerRunning) return;
                _writerRunning = true;
            }
            System.Threading.ThreadPool.UnsafeQueueUserWorkItem(_ => WriterLoop(), null);
            return;
        }
        lock (_slot) _pendingJson = null;                 // (this one is newer than anything waiting)
        bool ok;
        lock (_fileLock) ok = WriteFile(json, seq, force);
        if (!ok)
        {
            // someone has the file open (another Orbit starting, a backup tool): try again in a second
            _dirty = true;
            _dirtySince = _lastChange = Now;
        }
    }

    void WriterLoop()
    {
        while (true)
        {
            string json;
            long seq;
            lock (_slot)
            {
                if (_pendingJson == null) { _writerRunning = false; return; }
                json = _pendingJson;
                seq = _pendingSeq;
                _pendingJson = null;
            }
            bool ok;
            lock (_fileLock) ok = WriteFile(json, seq, false);
            if (!ok) _writeFailed = true;
        }
    }

    // the file written (under _fileLock): true = done, or a newer text was written already; false = it failed (the warning once)
    bool WriteFile(string json, long seq, bool force)
    {
        if (seq <= _writtenSeq) return true;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                string tmp = Path + ".tmp";
                File.WriteAllText(tmp, json, new System.Text.UTF8Encoding(false));
                if (File.Exists(Path))
                    File.Replace(tmp, Path, Path + ".bak");
                else
                    File.Move(tmp, Path);
                _writtenSeq = seq;
                _failWarned = false;
                return true;
            }
            catch (IOException) when (force && attempt < 5) { System.Threading.Thread.Sleep(100); }   // closing: try a little longer
            catch (Exception e)
            {
                if (!_failWarned) { _failWarned = true; Log.Warn("config save failed (will retry): " + e.Message); }
                return false;
            }
        }
    }
}
