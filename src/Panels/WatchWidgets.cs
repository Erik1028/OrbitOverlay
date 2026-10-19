using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Orbit;

// The custom watch face: widgets on a 6 x 4 grid. What can go on the watch and in which sizes, the grid
// geometry for both styles, and the layout rules (inside the grid, no overlap, a start from the old faces).
public static class WatchWidgets
{
    public const int Cols = 6, Rows = 4;

    // Sizes are in cells, smallest first; Default is the index of the size a new one gets. Tone was its colour
    // in the modern style before the Bento look (0 = the accent, 1 = the logo's indigo, 2 = between the two,
    // 3 = Orbit's green); the watch now colours by role (WristPanel.RoleColor) and no longer reads it.
    // Extra: sizes only the modern style offers (the Bento S and L), after Sizes - Sizes keeps its order and Default, so
    // a saved face (the 95 one too) reads as it always did; the 95 face draws an extra one with its nearest picture.
    public sealed record Kind(string Id, string Name, string Title95, string Icon, W95Icons.Id Icon95, (int W, int H)[] Sizes,
        int Default = 0, bool Button = false, int Tone = 0, (int W, int H)[] Extra = null)
    {
        public (int W, int H)[] Union { get; } = Extra == null ? Sizes : Sizes.Concat(Extra).ToArray();

        // what a style offers: 95 its sizes, modern the extra ones too
        public (int W, int H)[] SizesFor(bool classic) => classic ? Sizes : Union;
    }

    static readonly (int, int)[] Btn = { (1, 1), (2, 1) };
    static readonly (int, int)[] Stat = { (1, 1), (2, 1) };

    public static readonly Kind[] Kinds =
    {
        // clocks
        new("time", "Time", "Clock", "", W95Icons.Id.Clock, new[] { (2, 1), (3, 1), (4, 1), (3, 2), (4, 2), (6, 2), (6, 3), (6, 4) }, 4,
            Extra: new[] { (1, 1), (2, 2) }),
        new("date", "Date", "Date", "", W95Icons.Id.Clock, new[] { (2, 1), (3, 1), (4, 1), (6, 1) }, 1, Tone: 0, Extra: new[] { (1, 1) }),
        new("calendar", "Calendar", "Calendar", "", W95Icons.Id.Clock, new[] { (1, 1), (2, 1), (2, 2) }, 0, Tone: 0),
        new("analog", "Analog clock", "Clock", "", W95Icons.Id.Clock, new[] { (2, 2), (3, 3), (4, 4) }, Tone: 0,
            Extra: new[] { (1, 1) }),         // 1x1: the squircle clock (WristCustom.AnalogS)
        new("timer", "Timer", "Timer", "", W95Icons.Id.Clock, new[] { (1, 1), (2, 1), (2, 2) }, 2, Tone: 0),
        new("stopwatch", "Stopwatch", "Stopwatch", "", W95Icons.Id.Clock, new[] { (1, 1), (2, 1) }, 1, Tone: 0),
        new("session", "Time in VR", "Session", "", W95Icons.Id.Computer, Stat, Tone: 1),
        // what is going on
        new("battery", "Batteries", "Power Meter", "", W95Icons.Id.Computer, new[] { (1, 1), (2, 1), (2, 2), (3, 2) }, 2, Tone: 3),
        // (Mixtape linked: its song, its buttons, and opened its whole player - the queue, the song list, albums, playlists, lyrics; the
        // Mixtape widget merged into it, Erik 10-19: WristMixtape.cs)
        new("media", "Now playing", "CD Player", "", W95Icons.Id.Note, new[] { (2, 1), (3, 1), (4, 1), (6, 1), (4, 2), (6, 2) }, 2, Tone: 1),
        new("volume", "Volume", "Volume Control", "", W95Icons.Id.Note, new[] { (1, 1), (2, 1), (3, 1) }, 1, Tone: 2),
        new("perf", "Performance", "Performance", "", W95Icons.Id.Computer, new[] { (4, 1), (6, 1) }, 1, Tone: 0),
        new("graph", "Frame graph", "Frame Graph", "", W95Icons.Id.Computer, new[] { (3, 1), (4, 1), (6, 1), (4, 2), (6, 2) }, 2, Tone: 0),
        new("fps", "Frame rate", "Frame Rate", "", W95Icons.Id.Computer, Stat, Tone: 0),
        new("reproj", "Reprojection", "Reprojection", "", W95Icons.Id.Computer, Stat, Tone: 1),
        new("gpu", "GPU time", "GPU Time", "", W95Icons.Id.Computer, Stat, Tone: 2),
        // the game's CPU time a frame (VrStats.GameCpuMs): how much its CPU holds it back (Erik 10-19)
        new("cputime", "CPU time", "CPU Time", "", W95Icons.Id.Computer, Stat, Tone: 0),
        new("vram", "VRAM", "Video Memory", "", W95Icons.Id.Computer, Stat, Tone: 1),
        new("cpu", "CPU", "CPU Usage", "", W95Icons.Id.Computer, Stat, Tone: 0),
        new("ram", "Memory", "Memory", "", W95Icons.Id.Computer, Stat, Tone: 2),
        // buttons
        new("keyboard", "Keyboard", "Keyboard", Theme.IcoKeyboard, W95Icons.Id.Keyboard, Btn, 0, true, 0),
        new("desktops", "Monitors", "Monitors", Theme.IcoMonitor, W95Icons.Id.Computer, Btn, 0, true, 0),
        new("recenter", "Recenter", "Recenter", Theme.IcoRecenter, W95Icons.Id.Center, Btn, 0, true, 0),
        new("layout", "Layout", "Layout", Theme.IcoLayouts, W95Icons.Id.Layouts, Btn, 0, true, 2),
        new("night", "Night mode", "Night", Theme.IcoNight, W95Icons.Id.Moon, Btn, 0, true, 1),
        new("settings", "Settings", "Settings", Theme.IcoSettings, W95Icons.Id.Gear, Btn, 0, true, 0),
        new("status", "Status line", "Status", "\uE8BD", W95Icons.Id.Chat, Btn, 1, true, 1),
        // one monitor / window as a big curved screen, the rest steps aside and the world dims
        new("theater", "Theater", "Theater", "\uE8B2", W95Icons.Id.Theater, Btn, 1, true, 1),
        // VRChat: who is in the instance; a tap grows it into the watch's opened view, the people there (WristRoom.cs)
        new("room", "Room", "Room", "\uE716", W95Icons.Id.People, new[] { (1, 1), (2, 1), (3, 1) }, 1, false, 2),
        // the stream dock (Hexpad): its keys live; a tap grows it into the watch's opened dock (WristDock.cs). Offered only while Hexpad runs.
        new("dock", "Hexpad", "Hexpad", "\uE80A", W95Icons.Id.Dock, new[] { (1, 1), (2, 1), (2, 2) }, 1, false, 1),
        // a game: Minesweeper - small first (the face, the mines left, the time, a hint of the board); a tap grows it into the
        // game (WristMines.cs)
        new("minesweeper", "Minesweeper", "Minesweeper", "\uE7C1", W95Icons.Id.Mine, new[] { (1, 1), (2, 2), (3, 2) }, 0, false, 0),
        // 1.12, the watch pages: the Windows microphone (a tap mutes it), the last notifications, a group of buttons in one card
        new("mic", "Microphone", "Microphone", "\uE720", W95Icons.Id.Note, new[] { (1, 1), (2, 1) }, 0),
        new("notifications", "Notifications", "Notifications", "\uEA8F", W95Icons.Id.Chat, new[] { (2, 1), (3, 1), (2, 2) }, 0),
        // the newest VRChat photo (Erik 10-17): it pops up when you take one; a tap grows it into a viewer of that day's (WristPhoto.cs)
        new("photo", "Latest photo", "Imaging", WristPanel.PhotoKindIcon, W95Icons.Id.Palette, new[] { (1, 1), (2, 1), (2, 2) }, 1),
        new("buttons", "Button group", "Buttons", "\uECA5", W95Icons.Id.Layouts, new[] { (2, 1), (3, 1), (2, 2), (6, 1) }, 0),
        // 1.14: the apps playing on the output device, each with its own volume (WristMixer.cs)
        new("mixer", "Volume mixer", "Volume Mixer", WristPanel.MixKindIcon, W95Icons.Id.Note, new[] { (2, 1), (2, 2), (3, 2) }, 1),
        // the weather now and the next hours / days from Open-Meteo (Sys/Weather.cs, WristWeather.cs); a tap opens the forecast
        new("weather", "Weather", "Weather", "\uE706", W95Icons.Id.Moon, new[] { (1, 1), (2, 1), (2, 2), (4, 1) }, 1),
        // a folder (Erik 10-17: "ennyi dologhoz kell egy mappa widget is"): widgets of any kind in it, on its own grid (GridSpec.Folder);
        // closed, its widgets' icons; a tap opens it over the page and they live there (WristFolder.cs, WatchFolders)
        new("folder", "Folder", "Folder", WatchFolders.Icon, W95Icons.Id.Folder, new[] { (1, 1), (2, 1), (2, 2) }, 0),
        // VRChat friends online and where, from VRCX's database (Sys/Friends.cs, WristFriends.cs); a tap opens the whole list
        new("friends", "Friends online", "Friends", "\uE8FA", W95Icons.Id.People, new[] { (1, 1), (2, 1), (2, 2) }, 1),
        // the last VR session's recap: how long, photos, worlds, people, the longest stay (Sys/SessionRecap.cs, WristRecap.cs)
        new("recap", "Session recap", "Summary", "\uE81C", W95Icons.Id.Hourglass, new[] { (1, 1), (2, 1), (2, 2) }, 1),
        // the room light through Lumos and the PC's RGB through Prisma (Sys/RoomLights.cs, WristLights.cs): a tap switches it; bigger, its
        // brightness and quick choices; opened, all of it
        new("light", "Light", "Light", "\uEA80", W95Icons.Id.Moon, new[] { (1, 1), (2, 1), (2, 2) }, 1),
        new("rgb", "RGB lighting", "RGB Lighting", "\uE790", W95Icons.Id.Palette, new[] { (1, 1), (2, 1), (2, 2) }, 1),
        // a VRChat photo of this day a year ago (or two, or more; the nearest day within 3 when that one has none), filling its card; a tap
        // opens it big, the stick steps through that day's photos and the other years (Sys/PhotoMemories.cs, WristMemory.cs)
        new("memory", "On this day", "On This Day", WristPanel.MemoryKindIcon, W95Icons.Id.Palette, new[] { (1, 1), (2, 1), (2, 2) }, 1),
        // the PC's temperatures: the GPU, the CPU, the GPU's hotspot and fan; a tap opens the last 10 minutes (Sys/Temps.cs, WristTemps.cs)
        new("temps", "Temperatures", "System Monitor", WristPanel.TempsKindIcon, W95Icons.Id.Computer, new[] { (1, 1), (2, 1), (2, 2) }, 1),
        // VRChat: buttons for the avatar worn, made from its OSC config; a tap sends the parameter over OSC (Erik 10-18; Sys/AvatarToggles.cs,
        // WristAvatar.cs); a tap on its header opens all of them
        new("avatar", "Avatar toggles", "Avatar", WristPanel.AvatarKindIcon, W95Icons.Id.People, new[] { (1, 1), (2, 1), (3, 1), (2, 2), (3, 2) }, 1),
    };

    // what the editor offers: the dock only when Hexpad is there (set by the app)
    public static bool DockAvailable;
    static readonly Kind[] _noDock = Array.FindAll(Kinds, k => k.Id != "dock");
    public static Kind[] Palette => DockAvailable ? Kinds : _noDock;
    // the editor's list (the Modern / Cyber buttons, the 95 family's list box) in ABC order of the names shown (Erik, 10-16)
    public static Kind[] PaletteAbc(Func<Kind, string> shown = null) =>
        Palette.OrderBy(k => shown?.Invoke(k) ?? Lang.T(k.Name), StringComparer.CurrentCultureIgnoreCase).ToArray();

    // The editor's categories (Erik 10-17: the list grew too long). Kinds still to come are named here already; a kind nobody
    // placed lands in More.
    public static readonly string[] Categories = { "Time", "System", "Media", "VRChat", "Controls", "More" };
    public static int CategoryOf(string id) => id switch
    {
        "time" or "date" or "calendar" or "analog" or "timer" or "stopwatch" or "session" => 0,
        "perf" or "graph" or "fps" or "reproj" or "gpu" or "cputime" or "vram" or "cpu" or "ram" or "battery" or "temps" => 1,
        "media" or "volume" or "mixer" or "mic" => 2,
        "room" or "notifications" or "status" or "friends" or "recap" or "photo" or "memory" or "avatar" => 3,
        "keyboard" or "desktops" or "recenter" or "layout" or "night" or "settings" or "theater" or "buttons" or "dock" or "light" or "rgb" => 4,
        _ => 5,
    };

    // a category's kinds (cat < 0: all), or with a search the ones whose name (as shown, or in English) or id holds it - ABC
    public static Kind[] PaletteFor(int cat, string search)
    {
        var all = PaletteAbc();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var cmp = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
            const System.Globalization.CompareOptions o = System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;
            string q = search.Trim();
            return Array.FindAll(all, k => cmp.IndexOf(Lang.T(k.Name), q, o) >= 0 || cmp.IndexOf(k.Name, q, o) >= 0 || cmp.IndexOf(k.Id, q, o) >= 0);
        }
        return cat < 0 ? all : Array.FindAll(all, k => CategoryOf(k.Id) == cat);
    }

    // (a loop, not Array.Find: the watch asks for every widget in every frame, and a lambda holding the id was made each time)
    public static Kind Find(string id)
    {
        foreach (var k in Kinds) if (k.Id == id) return k;
        return null;
    }

    // ---- geometry: modern = the 640 x 440 texture, 95 = the 320 x 220 canvas ----
    // The same gap between every two widgets and between a widget and the edge (modern 10 px: with the
    // watch's 34 px corners the cards' 24 px corners are concentric; 95: 4 px). Edges on whole pixels, so
    // every gap is exactly that wide (a cell may be a pixel wider than its neighbour instead).

    // margin, gap, size; mb: the bottom margin (a 95 face with pages has its little taskbar there)
    static (float m, float g, float w, float h, float mb) Metrics(bool classic, GridSpec grid = null) =>
        classic ? (4f, 4f, WristPanel.TW95, WristPanel.TH95, grid is { Paged: true } ? 4f + Taskbar95 : 4f)
                : (10f, 10f, WristPanel.TW, WristPanel.TH, grid is { Paged: true } ? PageStrip : 10f);

    // the 95 face's page buttons along the bottom of its 320 x 220 canvas (a face with pages only)
    public const float Taskbar95 = 22;
    // the modern face's page dots: a strip of their own under the cards (a face with pages only; they hung into the cards, Erik 10-16)
    public const float PageStrip = 26;

    // grid: the page's (null = 6 x 4, the only grid before pages)
    public static RectangleF CellRect(int x, int y, int w, int h, bool classic, GridSpec grid = null)
    {
        grid ??= GridSpec.G64;
        if (grid.Top > 0) return CellRect(x, y + grid.Top, w, h, classic, GridSpec.G64);      // a folder's grid: the page's cells under its title row
        var (m, g, W, H, mb) = Metrics(classic, grid);
        int cols = grid.Cols, rows = grid.Rows;
        float cw = (W - 2 * m - (cols - 1) * g) / cols, ch = (H - m - mb - (rows - 1) * g) / rows;
        float x0 = m + x * (cw + g), y0 = m + y * (ch + g);
        float x1 = m + (x + w) * (cw + g) - g, y1 = m + (y + h) * (ch + g) - g;
        return RectangleF.FromLTRB(MathF.Round(x0), MathF.Round(y0), MathF.Round(x1), MathF.Round(y1));
    }

    public static RectangleF Rect(WatchWidget w, bool classic, GridSpec grid = null) => CellRect(w.X, w.Y, w.W, w.H, classic, grid);

    // the cell under a point of the texture / canvas, clamped to the grid
    public static (int x, int y) CellAt(float px, float py, bool classic, GridSpec grid = null)
    {
        grid ??= GridSpec.G64;
        if (grid.Top > 0)
        {
            var (fx, fy) = CellAt(px, py, classic, GridSpec.G64);       // a folder's grid: the page's rows under its title row
            return (fx, Math.Clamp(fy - grid.Top, 0, grid.Rows - 1));
        }
        var (m, g, W, H, mb) = Metrics(classic, grid);
        int cols = grid.Cols, rows = grid.Rows;
        float cw = (W - 2 * m - (cols - 1) * g) / cols, ch = (H - m - mb - (rows - 1) * g) / rows;
        int cx = (int)MathF.Floor((px - m + g / 2) / (cw + g)), cy = (int)MathF.Floor((py - m + g / 2) / (ch + g));
        return (Math.Clamp(cx, 0, cols - 1), Math.Clamp(cy, 0, rows - 1));
    }

    // ---- layout rules ----

    // overflow-safe: a hand-edited config can hold int.MaxValue
    public static bool InGrid(int x, int y, int w, int h, GridSpec grid = null)
    {
        int cols = grid?.Cols ?? Cols, rows = grid?.Rows ?? Rows;
        return x >= 0 && y >= 0 && w >= 1 && h >= 1 && w <= cols && h <= rows && x <= cols - w && y <= rows - h;
    }

    // is this area inside the grid and free (ignoring 'skip', the widget being moved)?
    public static bool Free(IReadOnlyList<WatchWidget> list, WatchWidget skip, int x, int y, int w, int h, GridSpec grid = null)
    {
        if (!InGrid(x, y, w, h, grid)) return false;
        foreach (var o in list)
            if (o != skip && x < o.X + o.W && o.X < x + w && y < o.Y + o.H && o.Y < y + h) return false;
        return true;
    }

    public static WatchWidget At(IReadOnlyList<WatchWidget> list, int cx, int cy)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var o = list[i];
            if (cx >= o.X && cx < o.X + o.W && cy >= o.Y && cy < o.Y + o.H) return o;
        }
        return null;
    }

    // A new widget where it fits: in the preferred cell (any size that fits there), else its default size
    // anywhere, then any other size from the smallest (classic: only the sizes the 95 face has). Null when there is no room.
    public static WatchWidget Add(List<WatchWidget> list, Kind k, int prefX = -1, int prefY = -1, bool classic = false, GridSpec grid = null)
    {
        grid ??= GridSpec.G64;
        var sizes = new List<(int W, int H)>();
        if (grid.Cols == Cols) { sizes.Add(k.Sizes[k.Default]); sizes.AddRange(k.SizesFor(classic).Where((s, i) => i != k.Default)); }
        else sizes.AddRange(SizesOn(k, grid, classic));      // 4x3: its first size is the one a new one gets
        if (prefX >= 0)
            foreach (var (w, h) in sizes)
                if (Free(list, null, prefX, prefY, w, h, grid)) return Place(list, k, prefX, prefY, w, h);
        foreach (var (w, h) in sizes)
            for (int y = 0; y < grid.Rows; y++)
                for (int x = 0; x < grid.Cols; x++)
                    if (Free(list, null, x, y, w, h, grid)) return Place(list, k, x, y, w, h);
        return null;
    }

    static WatchWidget Place(List<WatchWidget> list, Kind k, int x, int y, int w, int h)
    {
        var n = new WatchWidget { Type = k.Id, X = x, Y = y, W = w, H = h };
        if (k.Id == "buttons") n.Items = new List<string>(GroupDefault);
        list.Add(n);
        return n;
    }

    // the allowed size (in that style) closest to what the pointer asks for (a corner drag)
    public static (int W, int H) SnapSize(Kind k, int wantW, int wantH, bool classic = false, GridSpec grid = null)
    {
        wantW = Math.Max(1, wantW);
        wantH = Math.Max(1, wantH);
        return SizesOn(k, grid, classic).OrderBy(s => Math.Abs(s.W - wantW) + Math.Abs(s.H - wantH)).ThenBy(s => s.W * s.H).First();
    }

    // the next smaller (-1) or larger (+1) allowed size (in that style) that fits where the widget is, or null
    public static (int W, int H)? Step(IReadOnlyList<WatchWidget> list, WatchWidget w, int dir, bool classic = false, GridSpec grid = null)
    {
        var k = Find(w.Type);
        if (k == null) return null;
        int area = w.W * w.H;
        var order = SizesOn(k, grid, classic).OrderBy(s => s.W * s.H).ThenBy(s => s.W).ToList();
        int at = order.FindIndex(s => s.W == w.W && s.H == w.H);
        if (at < 0)
        {
            // a size this version doesn't list: step from where its area would sit
            if (dir > 0) at = order.FindLastIndex(s => s.W * s.H <= area);
            else { at = order.FindIndex(s => s.W * s.H >= area); if (at < 0) at = order.Count; }
        }
        for (int i = at + dir; i >= 0 && i < order.Count; i += dir)
            if (Free(list, w, w.X, w.Y, order[i].W, order[i].H, grid)) return order[i];
        return null;
    }

    // ---- a start from the old faces, so the custom face first looks like what the user had ----

    public static readonly string[] Presets = { "full", "analog", "minimal", "time", "corners" };
    public static readonly string[] PresetNames = { "Full", "Analog", "Minimal", "Time only", "Corners" };

    public static List<WatchWidget> Seed(WristConfig c, string face)
    {
        var l = new List<WatchWidget>();
        void Put(string t, int x, int y, int w, int h) => l.Add(new WatchWidget { Type = t, X = x, Y = y, W = w, H = h });
        switch (face)
        {
            case "time":
                Put("time", 0, 0, 6, 4);
                return l;
            case "minimal":
                Put("time", 0, 0, 6, 3);
                break;
            case "corners":
                // a clock in the middle, a little widget in each corner (smartwatch complications)
                Put("time", 1, 0, 4, 2);
                Put("date", 1, 2, 4, 1);
                Put("battery", 0, 0, 1, 1);
                Put("fps", 5, 0, 1, 1);
                Put("volume", 0, 2, 1, 1);
                Put("gpu", 5, 2, 1, 1);
                break;
            case "analog":
                Put("analog", 0, 0, 2, 2);
                Put("time", 2, 0, 2, 1);
                Put("date", 2, 1, 2, 1);
                if (c.ShowBatteries) Put("battery", 4, 0, 2, 2);
                Row2(c, Put);
                break;
            default:
                if (c.ShowBatteries) { Put("time", 0, 0, 4, 2); Put("battery", 4, 0, 2, 2); }
                else Put("time", 0, 0, 6, 2);
                Row2(c, Put);
                break;
        }
        string[] buttons = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
        for (int i = 0; i < buttons.Length; i++) Put(buttons[i], i, 3, 1, 1);
        return l;
    }

    static void Row2(WristConfig c, Action<string, int, int, int, int> put)
    {
        if (c.ShowMedia && c.ShowPerformance)
        {
            if (c.PerfGraph) { put("media", 0, 2, 3, 1); put("graph", 3, 2, 3, 1); }
            else { put("media", 0, 2, 4, 1); put("fps", 4, 2, 1, 1); put("gpu", 5, 2, 1, 1); }
        }
        else if (c.ShowMedia) put("media", 0, 2, 6, 1);
        else if (c.ShowPerformance) put(c.PerfGraph ? "graph" : "perf", 0, 2, 6, 1);
    }

    // What a hand-edited (or newer) config got wrong: outside the grid or overlapping = dropped; a size this
    // version doesn't offer = the largest offered size inside its box (or the nearest, if that fits).
    // Unknown types are KEPT (a newer Orbit's widgets survive a round trip; they draw as nothing here).
    // Both styles' sizes count (the face is one list): a modern-only size survives a switch to 95 and back.
    // grid: the page's - a 4x3 page is checked against the 4x3 sizes, a 6x4 one against the 6x4 ones (never one by the other's)
    public static List<WatchWidget> Clean(List<WatchWidget> list, GridSpec grid = null)
    {
        var ok = new List<WatchWidget>();
        foreach (var w in list ?? new List<WatchWidget>())
        {
            if (w == null || string.IsNullOrEmpty(w.Type) || !InGrid(w.X, w.Y, w.W, w.H, grid)) continue;
            var k = Find(w.Type);
            var union = k == null ? null : grid == null || grid.Cols == Cols ? k.Union : SizesOn(k, grid, false);
            if (k != null && !union.Contains((w.W, w.H)))
            {
                var inside = union.Where(s => s.W <= w.W && s.H <= w.H).OrderByDescending(s => s.W * s.H).ToList();
                var s = inside.Count > 0 ? inside[0] : SnapSize(k, w.W, w.H, false, grid);
                if (!Free(ok, null, w.X, w.Y, s.W, s.H, grid)) continue;
                w.W = s.W;
                w.H = s.H;
            }
            if (w.Type == "folder" && w.Children != null) w.Children = WatchFolders.CleanChildren(w.Children);     // its own grid's rules
            if (Free(ok, null, w.X, w.Y, w.W, w.H, grid)) ok.Add(w);
        }
        return ok;
    }

    // ---- the clocks' designs (WatchWidget.Variant): the clock itself, separate from its look (Style / Color) ----
    // Modern only (the 95 face draws its 1995 clocks whatever is set). Fits: the sizes a design works at - the editor offers it
    // only there, and a widget at another size draws its size's default (the variant is kept: a size it fits brings it back).
    // An unknown one (a newer Orbit's) is kept as well and draws the default.
    public sealed record Variant(string Id, string Name, Func<int, int, bool> Fits);

    public static readonly Variant[] TimeVariants =
    {
        new("standard", "Standard", (w, h) => true),
        new("flip", "Flip", (w, h) => true),            // split-flap cards
        new("outline", "Outline", (w, h) => true),      // hollow digits
        new("stacked", "Stacked", (w, h) => true),      // hours over minutes (square and tall cards; side by side on wide ones)
        new("segment", "Segment", (w, h) => true),      // seven-segment LED digits
        new("words", "Words", (w, h) => w >= 2 && h >= 2),   // a word clock: every word printed, the time's lit (2x2 and up)
    };

    public static readonly Variant[] AnalogVariants =
    {
        new("squircle", "Squircle", (w, h) => true),    // the macOS widget clock (1x1's default)
        new("classic", "Classic", (w, h) => w >= 2),    // Orbit's dial (the default from 2x2)
        new("minimal", "Minimal", (w, h) => true),
        new("roman", "Roman", (w, h) => w >= 2),
        new("station", "Station", (w, h) => true),      // a Swiss railway clock
    };

    // a kind's designs (null: it has none)
    public static Variant[] VariantsOf(string kind) => kind switch { "time" => TimeVariants, "analog" => AnalogVariants, _ => null };

    // what a widget of that kind and size draws with no variant of its own
    public static string DefaultVariant(string kind, int w, int h) => kind == "analog" ? (w == 1 && h == 1 ? "squircle" : "classic") : "standard";

    // the design a widget draws: its own where it fits its size, else its size's default (null: a kind without designs)
    public static string VariantOf(WatchWidget w)
    {
        var vs = VariantsOf(w.Type);
        if (vs == null) return null;
        if (w.Variant != null)
            foreach (var v in vs)
                if (v.Id == w.Variant) return v.Fits(w.W, w.H) ? v.Id : DefaultVariant(w.Type, w.W, w.H);
        return DefaultVariant(w.Type, w.W, w.H);
    }

    // a design picked in the editor as it is stored: its size's default is no variant (nothing written)
    public static string StoreVariant(WatchWidget w, string id) => id == DefaultVariant(w.Type, w.W, w.H) ? null : id;

    // ================= 1.12: the 4 x 3 grid and the pages =================

    // The sizes on the 4 x 3 grid (the Bento grid: S 1x1, M 2x1, L 2x2, Wide 4x1), the same in both styles - the 95 face draws each
    // with its nearest 95 picture. The first is the one a new widget gets. A kind not listed (a newer one) gets them from its 6 x 4 sizes.
    static readonly (int, int) S = (1, 1), M = (2, 1), L = (2, 2), Wd = (4, 1);
    static readonly Dictionary<string, (int W, int H)[]> Sizes43Table = new()
    {
        ["time"] = new[] { L, S, M }, ["date"] = new[] { M, S }, ["calendar"] = new[] { S, M, L }, ["analog"] = new[] { L, S },
        ["timer"] = new[] { S, M, L }, ["stopwatch"] = new[] { M, S }, ["session"] = new[] { S, M },
        ["battery"] = new[] { S, M, L }, ["media"] = new[] { M, S, L, Wd }, ["volume"] = new[] { S, M },
        ["perf"] = new[] { Wd, M }, ["graph"] = new[] { L, M, Wd },
        ["fps"] = new[] { S, M }, ["reproj"] = new[] { S, M }, ["gpu"] = new[] { S, M }, ["cputime"] = new[] { S, M }, ["vram"] = new[] { S, M },
        ["cpu"] = new[] { S, M }, ["ram"] = new[] { S, M },
        ["room"] = new[] { M, S }, ["dock"] = new[] { M, L },
        ["photo"] = new[] { M, S, L },
        ["mic"] = new[] { S, M }, ["notifications"] = new[] { M, L }, ["buttons"] = new[] { M, L },
        ["mixer"] = new[] { L, M },
        ["weather"] = new[] { M, S, L, Wd },
        ["folder"] = new[] { S, M, L },
        ["friends"] = new[] { M, S, L }, ["recap"] = new[] { M, S, L },
        ["light"] = new[] { M, S, L }, ["rgb"] = new[] { M, S, L },
        ["memory"] = new[] { M, S, L }, ["temps"] = new[] { M, S, L },
        ["avatar"] = new[] { M, S, L, Wd },
    };
    static readonly Dictionary<string, (int W, int H)[]> _derived43 = new();

    public static (int W, int H)[] Sizes43(Kind k)
    {
        if (Sizes43Table.TryGetValue(k.Id, out var s)) return s;
        if (k.Button) return Sizes43Table["fps"];
        if (_derived43.TryGetValue(k.Id, out s)) return s;
        var l = new List<(int W, int H)>();
        foreach (var (w, h) in k.Sizes)
        {
            var u = h >= 2 ? L : w >= 4 ? Wd : w >= 2 ? M : S;
            if (!l.Contains(u)) l.Add(u);
        }
        return _derived43[k.Id] = l.ToArray();
    }

    // what the editor offers on a page of this grid (null = 6 x 4)
    public static (int W, int H)[] SizesOn(Kind k, GridSpec grid, bool classic) =>
        grid == null || grid.Cols == Cols ? k.SizesFor(classic) : Sizes43(k);

    // the button kinds a button group can hold, and a new group's
    public static readonly string[] GroupKinds = { "keyboard", "desktops", "recenter", "layout", "night", "settings", "status", "theater" };
    public static readonly string[] GroupDefault = { "desktops", "theater", "night" };

    // its buttons as it draws them: known button kinds only, at most 6 (none = the default three)
    public static List<string> GroupItems(WatchWidget w)
    {
        var l = new List<string>();
        if (w.Items != null)
            foreach (var id in w.Items)
                if (id != null && Array.IndexOf(GroupKinds, id) >= 0 && !l.Contains(id) && l.Count < 6) l.Add(id);
        if (l.Count == 0) l.AddRange(GroupDefault);
        return l;
    }

    // ---- the pages' presets: on either grid ("empty" too) ----
    public static readonly string[] PagePresets = { "empty", "everyday", "vrchat", "performance" };
    public static readonly string[] PagePresetNames = { "Empty", "Everyday", "VRChat", "Performance" };

    public static List<WatchWidget> PagePreset(string preset, GridSpec grid)
    {
        var l = new List<WatchWidget>();
        void Put(string t, int x, int y, int w = 1, int h = 1, params string[] items) =>
            l.Add(new WatchWidget { Type = t, X = x, Y = y, W = w, H = h, Items = items.Length > 0 ? new List<string>(items) : null });
        string[] six = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
        bool g43 = grid != null && grid.Cols == 4;
        switch (preset)
        {
            case "everyday" when g43:
                Put("time", 0, 0, 2, 2); Put("media", 2, 0, 2, 1); Put("battery", 2, 1); Put("fps", 3, 1);
                Put("timer", 0, 2); Put("volume", 1, 2); Put("buttons", 2, 2, 2, 1, six);
                break;
            case "vrchat" when g43:
                Put("time", 0, 0, 2, 2); Put("room", 2, 0, 2, 1); Put("mic", 2, 1); Put("battery", 3, 1);
                Put("notifications", 0, 2, 2, 1); Put("media", 2, 2, 2, 1);
                break;
            case "performance" when g43:
                Put("graph", 0, 0, 2, 2); Put("time", 2, 0, 2, 1); Put("cpu", 2, 1); Put("vram", 3, 1);
                Put("battery", 0, 2, 2, 1); Put("buttons", 2, 2, 2, 1, "desktops", "theater", "night");
                break;
            case "everyday":
                Put("time", 0, 0, 3, 2); Put("media", 3, 0, 3, 1); Put("battery", 3, 1); Put("fps", 4, 1); Put("volume", 5, 1);
                Put("timer", 0, 2, 2, 1); Put("calendar", 2, 2); Put("session", 3, 2); Put("cpu", 4, 2); Put("ram", 5, 2);
                for (int i = 0; i < six.Length; i++) Put(six[i], i, 3);
                break;
            case "vrchat":
                // the concept's VRChat page on 6 x 4: the time big, the room, the microphone and the batteries beside it; the
                // notifications and now playing; a row of buttons for VRChat (the keyboard for the chatbox, the status line, theater)
                Put("time", 0, 0, 3, 2); Put("room", 3, 0, 3, 1); Put("mic", 3, 1); Put("battery", 4, 1, 2, 1);
                Put("notifications", 0, 2, 3, 1); Put("media", 3, 2, 3, 1);
                Put("buttons", 0, 3, 6, 1, "keyboard", "desktops", "status", "theater", "night", "settings");
                break;
            case "performance":
                // the concept's Performance page on 6 x 4: the frame graph big, the time, CPU and VRAM beside it; the batteries and the
                // frame numbers; Monitors / Theater / Night
                Put("graph", 0, 0, 4, 2); Put("time", 4, 0, 2, 1); Put("cpu", 4, 1); Put("vram", 5, 1);
                Put("battery", 0, 2, 2, 1); Put("fps", 2, 2); Put("gpu", 3, 2); Put("reproj", 4, 2); Put("ram", 5, 2);
                Put("buttons", 0, 3, 6, 1, "desktops", "theater", "night");
                break;
        }
        return l;
    }

    public static string PresetName(string preset) => PagePresetNames[Math.Max(0, Array.IndexOf(PagePresets, preset))];

    // any preset's name: the old faces' and the pages'
    public static string PresetName2(string preset)
    {
        int i = Array.IndexOf(Presets, preset);
        return i >= 0 ? PresetNames[i] : PresetName(preset);
    }
}

// A grid of the custom face: 6 x 4 (page 1 of every face from before pages) or 4 x 3 (the Bento grid: fewer, bigger cards).
// Paged: the face has more than one page - the bottom margin grows then (the 95 face's taskbar of pages, the modern page dots).
public sealed class GridSpec
{
    public readonly int Cols, Rows;
    public readonly string Id;
    public readonly bool Paged;
    public readonly int Top;                    // rows of the 6 x 4 page above its cells (a folder's title row); 0 = a page's own grid

    GridSpec(int cols, int rows, string id, bool paged, int top = 0) { Cols = cols; Rows = rows; Id = id; Paged = paged; Top = top; }

    public static readonly GridSpec G64 = new(6, 4, "6x4", false), G43 = new(4, 3, "4x3", false),
        G64Paged = new(6, 4, "6x4", true), G43Paged = new(4, 3, "4x3", true);

    // A folder's widgets (WatchFolders): the 6 x 4 page's own cells under its first row - the folder's name and its X take that row
    // when it is open - so every kind draws there exactly as it does on a page (its sizes, its designs, the layout check's rules).
    public static readonly GridSpec Folder = new(6, 3, "folder", false, 1);

    public static GridSpec Of(string id, bool paged = false) =>
        id == "4x3" ? (paged ? G43Paged : G43) : (paged ? G64Paged : G64);

    public bool Is43 => Cols == 4;
}

// The custom face's pages: page 1 is WristConfig.Widgets (with Grid and Name), the others WristConfig.Pages.
public static class WatchPages
{
    public static int Count(WristConfig c) => 1 + (c.Pages?.Count ?? 0);

    public static string GridId(WristConfig c, int i) => i <= 0 ? c.Grid ?? "6x4" : c.Pages[i - 1].Grid ?? "6x4";

    public static GridSpec Grid(WristConfig c, int i) => GridSpec.Of(GridId(c, i), Count(c) > 1);

    public static string Name(WristConfig c, int i)
    {
        string n = i <= 0 ? c.Name : c.Pages[i - 1].Name;
        return string.IsNullOrWhiteSpace(n) ? $"Page {i + 1}" : n;
    }

    // the name as the screens write it, in the language picked: the built-in pages' names ("Performance") and "Page 2" (a name of
    // your own stays as it is; Name stays English - a file's name, the tools' checks)
    public static string Label(WristConfig c, int i)
    {
        string n = i <= 0 ? c.Name : c.Pages[i - 1].Name;
        return string.IsNullOrWhiteSpace(n) ? Lang.F("Page {0}", i + 1) : Lang.T(n);
    }

    // a page's list (null: page 1 never set up)
    public static List<WatchWidget> Widgets(WristConfig c, int i) => i <= 0 ? c.Widgets : c.Pages[i - 1].Widgets;

    // is this kind on any page (the dock's faces, the frame samples: ready before you swipe to it)
    public static bool Any(WristConfig c, string type)
    {
        for (int i = 0; i < Count(c); i++)
        {
            var l = Widgets(c, i);
            if (l == null) continue;
            foreach (var w in l) if (w?.Type == type) return true;
            foreach (var w in l) if (w?.Children != null && WatchFolders.Has(w.Children, type)) return true;      // in a folder
        }
        return false;
    }

    public static WatchPage Make(string preset, string grid) =>
        new() { Name = WatchWidgets.PresetName(preset), Grid = grid, Widgets = WatchWidgets.PagePreset(preset, GridSpec.Of(grid)) };

    // The first start of 1.12. A new install (no config yet): the custom face, its pages VRChat and Performance on the 6 x 4 grid.
    // A config from before: a copy of config.json first (config.json.bak-pages-<time>, next to it), then the VRChat and Performance
    // pages after the pages it has - page 1, its own face, is not touched. Once: PagesVersion remembers it, so pages deleted later
    // stay deleted. Only the config: this runs in the app's constructor (no window yet).
    public static bool Migrate(ConfigStore store)
    {
        var w = store.Data.Wrist;
        if (w.PagesVersion >= 1) return false;
        if (store.Fresh)
        {
            w.Face = "custom";
            w.Grid = null;
            w.Name = "VRChat";
            w.Widgets = WatchWidgets.PagePreset("vrchat", GridSpec.G64);
            w.Pages = new List<WatchPage> { Make("performance", "6x4") };
            Log.Info("pages: a new install - the VRChat and Performance pages");
        }
        else
        {
            string bak = store.Path + $".bak-pages-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                if (System.IO.File.Exists(store.Path)) System.IO.File.Copy(store.Path, bak, false);
            }
            catch (Exception e)
            {
                Log.Warn($"pages: no copy of the config ({e.Message}) - the new pages wait for the next start");
                return false;
            }
            w.Pages ??= new List<WatchPage>();
            w.Pages.Add(Make("vrchat", "6x4"));
            w.Pages.Add(Make("performance", "6x4"));
            Log.Info($"pages: the VRChat and Performance pages added after page {Count(w) - 2} (a copy of the config: {bak})");
        }
        w.PagesVersion = 1;
        store.MarkDirty();
        return true;
    }

    // Erik 10-19: "a mixtape es now playing widgetet ossze kellene olvasztani" - the Mixtape widget went into Now playing (it shows
    // Mixtape's song and opens its whole player while Mixtape plays: WristMixtape.cs). A face from before: each Mixtape widget becomes a
    // Now playing one in its place (one row high on the 6 x 4 grid and in a folder: Now playing has no 2 x 2 / 3 x 2 there), or goes
    // when its page (its folder) has a Now playing already. A copy of config.json first (config.json.bak-mixtape-<time>); a face with
    // none: nothing (so it runs once).
    public static bool MergeMixtape(ConfigStore store)
    {
        var w = store.Data.Wrist;
        static bool Has(List<WatchWidget> l) => l != null && l.Exists(x => x?.Type == "mixtape" || Has(x?.Children));
        bool any = false;
        for (int i = 0; i < Count(w); i++) any |= Has(Widgets(w, i));
        if (!any) return false;
        if (!store.Fresh)
        {
            string bak = store.Path + $".bak-mixtape-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                if (System.IO.File.Exists(store.Path)) System.IO.File.Copy(store.Path, bak, false);
            }
            catch (Exception e)
            {
                Log.Warn($"mixtape: no copy of the config ({e.Message}) - the Mixtape widgets wait for the next start");
                return false;
            }
        }
        int turned = 0, dropped = 0;
        void Merge(List<WatchWidget> l, bool g43)
        {
            if (l == null) return;
            bool media = l.Exists(x => x?.Type == "media");
            for (int j = l.Count - 1; j >= 0; j--)
            {
                var x = l[j];
                if (x?.Children != null) Merge(x.Children, false);
                if (x?.Type != "mixtape") continue;
                if (media) { l.RemoveAt(j); dropped++; continue; }
                x.Type = "media";
                if (!g43) x.H = 1;          // (2 x 1, 3 x 1; the 4 x 3 grid's M / L / Wd are Now playing sizes too)
                media = true;               // a second one on the same page: this is its Now playing now
                turned++;
            }
        }
        for (int i = 0; i < Count(w); i++) Merge(Widgets(w, i), GridId(w, i) == "4x3");
        Log.Info($"mixtape: the Mixtape widget merged into Now playing - {turned} turned into Now playing, {dropped} gone (a Now playing beside it)");
        store.MarkDirty();
        return true;
    }
}
