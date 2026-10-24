using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Orbit;

// --snapshot's part for the friends widget and the evening's recap (WristFriends.cs, WristRecap.cs; Sys/Friends.cs, Sys/SessionRecap.cs),
// on MADE-UP data only - never VRCX's, VRChat's or the pictures folder's own files:
//  - DataChecks: a made-up VRCX-shaped database written into the snapshot's folder (qa/vrcx_made_up.sqlite3) and read the way the app
//    reads VRCX's (Vrcx.Open: read only, immutable): who counts as online since VRCX's start - an Online, a move or an avatar change
//    after it, an Offline after them, an Online from before the start - where (the instance kinds, private, traveling), the
//    favourites; the watch's order (starred, with you, the worlds A-Z, private, traveling, by name); the session on a made-up clock (20
//    minutes or more, 3 minutes off, SteamVR gone, a break of 2 minutes); the recap's numbers from a made-up evening (worlds, stays,
//    people met, the friends among them, someone who came while the headset was off, the longest stay); photos counted from made-up
//    files; recap.json written and read back (in the snapshot's folder).
//  - Run (every style): a tap opens each, its X closes it, the stick scrolls the friends; no garbage a frame at rest; pictures
//    wrist_social_<case><tag>.png.
//  - Sheets: social_preview.png (both, every style, side by side), social_states.png (nothing to show).
static class SocialViewTools
{
    // ---- made-up data ----

    static readonly string[] Names =
    {
        "Nova", "Mira", "Juniper", "Basil", "Static", "Opal", "Wren", "Kite", "Pixel Fox", "Lumen", "Quill", "Sable", "Tansy", "Rune",
    };
    static readonly string[] Worlds = { "Lantern Harbor", "Pixel Plaza", "Moonlit Library", "Clockwork Garden", "Velvet Lounge" };

    static string Id(int i) => $"usr_aaaaaaaa-0000-4000-8000-{i:000000000000}";

    // the watch's made-up friends: a few with you, the worlds' kinds mixed, one private, one traveling, one starred, one favourite
    internal static FriendsWatcher.Snapshot Friends(int n = 12)
    {
        var s = new FriendsWatcher.Snapshot { Version = 500 + n, State = FriendsWatcher.State.Ok, VrcxSince = DateTime.Now.AddHours(-6), Total = 128 };
        var kinds = new[]
        {
            FriendsWatcher.Where.FriendsPlus, FriendsWatcher.Where.Public, FriendsWatcher.Where.Private, FriendsWatcher.Where.InvitePlus,
            FriendsWatcher.Where.Group, FriendsWatcher.Where.Traveling, FriendsWatcher.Where.Friends,
        };
        for (int i = 0; i < n; i++)
        {
            var k = kinds[i % kinds.Length];
            s.Online.Add(new FriendsWatcher.Friend
            {
                Id = Id(i), Name = Names[i % Names.Length] + (i >= Names.Length ? " " + (i / Names.Length + 1) : ""), Kind = k,
                World = k is FriendsWatcher.Where.Private or FriendsWatcher.Where.Traveling ? "" : Worlds[i % 4], Since = DateTime.Now.AddMinutes(-7 - 9 * i),
                Favorite = i == 3,
            });
        }
        return s;
    }

    // your made-up instance: two of the friends with you
    internal static RoomWatcher.Snapshot Room() => new()
    {
        Version = 900, LogFound = true, InRoom = true, World = "Velvet Lounge", VrcxFound = true, Friends = 128, Since = DateTime.Now.AddMinutes(-40),
        Here =
        {
            new RoomWatcher.Person { Id = Id(5), Name = "Opal", Joined = DateTime.Now.AddMinutes(-38), Friend = true },
            new RoomWatcher.Person { Id = Id(0), Name = "Nova", Joined = DateTime.Now.AddMinutes(-12), Friend = true },
            new RoomWatcher.Person { Id = "usr_bbbbbbbb-0000-4000-8000-000000000001", Name = "Quill", Joined = DateTime.Now.AddMinutes(-30) },
        },
    };

    // a made-up evening: 1 h 42 min, three worlds (one twice), photos, people
    internal static SessionRecap Recap()
    {
        var end = DateTime.Now.AddMinutes(-26);
        var start = end.AddMinutes(-102);
        return new SessionRecap
        {
            Start = start, End = end, Photos = 12, Room = true, People = 23, Friends = 5,
            Names = new List<string> { "Opal", "Nova", "Juniper", "Basil", "Static", "Quill", "Sable", "Tansy" },
            Worlds = new List<RecapWorld>
            {
                new() { Name = "Velvet Lounge", Start = start.AddMinutes(1), Minutes = 34 },
                new() { Name = "Pixel Plaza", Start = start.AddMinutes(35), Minutes = 48 },
                new() { Name = "Moonlit Library", Start = start.AddMinutes(83), Minutes = 11 },
                new() { Name = "Velvet Lounge", Start = start.AddMinutes(94), Minutes = 8 },
            },
        };
    }

    // ---- the data's checks ----

    // writes a made-up database (winsqlite3, read-write: the test's own file only; the recap's Today writes its own with it: TodayViewTest.cs)
    internal static class MadeUp
    {
        const string Dll = "winsqlite3.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_open_v2(byte[] name, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr cb, IntPtr arg, out IntPtr err);

        public static bool Write(string path, string sql)
        {
            if (File.Exists(path)) File.Delete(path);
            if (sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out var db, 0x2 | 0x4, IntPtr.Zero) != 0) { sqlite3_close_v2(db); return false; }
            try { return sqlite3_exec(db, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, out _) == 0; }
            finally { sqlite3_close_v2(db); }
        }
    }

    static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static void DataChecks(WristPanel wrist, PanelData data, ConfigStore cfg, string dir)
    {
        string qa = Path.Combine(dir, "qa");
        Directory.CreateDirectory(qa);
        // ---- VRCX's database, made up: "VRCX" started 2 hours ago ----
        const string P = "usr0123456789abcdef0123456789abcdef";
        var since = DateTime.UtcNow.AddHours(-2);
        string At(double min) => Iso(since.AddMinutes(min));
        var sql = new StringBuilder();
        sql.Append($"CREATE TABLE {P}_friend_log_current (user_id TEXT PRIMARY KEY, display_name TEXT, trust_level TEXT, friend_number INTEGER);");
        sql.Append($"CREATE TABLE {P}_feed_online_offline (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, display_name TEXT, type TEXT, location TEXT, world_name TEXT, time INTEGER, group_name TEXT);");
        sql.Append($"CREATE TABLE {P}_feed_gps (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, display_name TEXT, location TEXT, world_name TEXT, previous_location TEXT, time INTEGER, group_name TEXT);");
        sql.Append($"CREATE TABLE {P}_feed_avatar (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, display_name TEXT, owner_id TEXT, avatar_name TEXT);");
        sql.Append($"CREATE TABLE {P}_feed_status (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, display_name TEXT, status TEXT, status_description TEXT);");
        sql.Append("CREATE TABLE favorite_friend (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, group_name TEXT);");
        sql.Append("CREATE TABLE configs (key TEXT PRIMARY KEY, value TEXT);");         // there, never read
        string[] who = { "Nova", "Mira", "Tobi", "Juniper", "Kite", "Moss", "Wren", "Echo", "Basil", "Static", "Opal", "Lark" };
        for (int i = 0; i < who.Length; i++) sql.Append($"INSERT INTO {P}_friend_log_current VALUES ('{Id(i)}', '{who[i]}', 'known', {i + 1});");
        int oo = 0, gp = 0;
        void On(int i, double min, string type, string loc, string world) =>
            sql.Append($"INSERT INTO {P}_feed_online_offline VALUES ({++oo}, '{At(min)}', '{Id(i)}', '{who[i]}', '{type}', '{loc}', '{world}', 0, '');");
        void Gps(int i, double min, string loc, string world) =>
            sql.Append($"INSERT INTO {P}_feed_gps VALUES ({++gp}, '{At(min)}', '{Id(i)}', '{who[i]}', '{loc}', '{world}', '', 0, '');");
        On(7, -60, "Online", "private", "");                                                          // Echo: before VRCX started - not known
        On(0, 10, "Online", "wrld_a1:101~hidden(usr_x)~region(eu)", "Lantern Harbor");                 // Nova: online, then moved on
        Gps(0, 40, "wrld_b2:202~region(eu)", "Pixel Plaza");
        On(1, 5, "Online", "private", "");                                                            // Mira: private
        On(2, -10, "Online", "private", "");                                                          // Tobi: online, then offline
        On(2, 20, "Offline", "private", "");
        Gps(3, 30, "wrld_c3:303~private(usr_j)~canRequestInvite~region(us)", "Moonlit Library");       // Juniper: moved (online from before)
        sql.Append($"INSERT INTO {P}_feed_avatar VALUES (1, '{At(50)}', '{Id(4)}', 'Kite', 'usr_x', 'A Hat');");     // Kite: a new avatar, nothing else
        On(5, 15, "Online", "private", "");                                                           // Moss: on and off again
        On(5, 25, "Offline", "private", "");
        On(6, 12, "Online", "traveling", "");                                                         // Wren: traveling
        On(8, 3, "Online", "wrld_d4:404~group(grp_1)~groupAccessType(plus)~region(eu)", "Clockwork Garden");    // Basil: a favourite, Group+
        On(9, 8, "Online", "wrld_b2:505~friends(usr_s)~region(eu)", "Pixel Plaza");                   // Static: starred, Friends
        Gps(11, 45, "wrld_b2:202~region(eu)", "Pixel Plaza");                                         // Lark: moved, then offline
        On(11, 50, "Offline", "wrld_b2:202~region(eu)", "Pixel Plaza");
        sql.Append($"INSERT INTO {P}_feed_online_offline VALUES ({++oo}, '{At(30)}', 'usr_cccccccc-0000-4000-8000-000000000001', 'Stranger', 'Online', 'private', '', 0, '');");
        sql.Append($"INSERT INTO favorite_friend VALUES (1, '{At(0)}', '{Id(8)}', 'Group_1');");
        string db = Path.Combine(qa, "vrcx_made_up.sqlite3");
        bool made = MadeUp.Write(db, sql.ToString());
        FriendsWatcher.Snapshot s = null;
        string err = null;
        try { s = FriendsWatcher.Read(db, since); }
        catch (Exception e) { err = e.Message; }
        string Of(string name) => s?.Online.FirstOrDefault(f => f.Name == name) is { } f ? $"{f.Kind}{(f.World.Length > 0 ? " " + f.World : "")}" : "offline";
        string online = s == null ? err : string.Join(", ", s.Online.Select(f => f.Name).OrderBy(x => x, StringComparer.Ordinal));
        const string wantOnline = "Basil, Juniper, Kite, Mira, Nova, Static, Wren";
        Tools.Want($"friends data: a made-up VRCX database written {made}, read {s != null}, {s?.Total} friends (want 12); " +
                   $"online since VRCX's start: {online} (want {wantOnline})", made && s != null && s.Total == 12 && online == wantOnline);
        var wantWhere = new (string name, string where)[]
        {
            ("Nova", "Public Pixel Plaza"), ("Juniper", "InvitePlus Moonlit Library"), ("Basil", "GroupPlus Clockwork Garden"), ("Static", "Friends Pixel Plaza"),
            ("Mira", "Private"), ("Wren", "Traveling"), ("Kite", "Unknown"), ("Tobi", "offline"), ("Moss", "offline"), ("Lark", "offline"), ("Echo", "offline"),
        };
        bool basilFav = s?.Online.FirstOrDefault(f => f.Name == "Basil")?.Favorite == true, stranger = s?.Online.All(f => f.Name != "Stranger") == true;
        Tools.Want($"friends data: Nova {Of("Nova")} (want Public Pixel Plaza), Juniper {Of("Juniper")} (want InvitePlus Moonlit Library), " +
                   $"Basil {Of("Basil")} (want GroupPlus Clockwork Garden), Static {Of("Static")} (want Friends Pixel Plaza), Mira {Of("Mira")} (want Private), " +
                   $"Wren {Of("Wren")} (want Traveling), Kite {Of("Kite")} (want Unknown); Tobi, Moss, Lark offline again, Echo from before the start: " +
                   $"{Of("Tobi")} {Of("Moss")} {Of("Lark")} {Of("Echo")} (want offline offline offline offline); Basil a favourite {basilFav}, a stranger left out {stranger} -",
                   wantWhere.All(w => Of(w.name) == w.where) && basilFav && stranger);
        // the instance kinds from VRChat's locations
        var kinds = new[]
        {
            ("wrld_1:1~region(eu)", FriendsWatcher.Where.Public), ("wrld_1:1~hidden(usr_1)~region(eu)", FriendsWatcher.Where.FriendsPlus),
            ("wrld_1:1~friends(usr_1)", FriendsWatcher.Where.Friends), ("wrld_1:1~private(usr_1)~canRequestInvite", FriendsWatcher.Where.InvitePlus),
            ("wrld_1:1~private(usr_1)", FriendsWatcher.Where.Invite), ("wrld_1:1~group(grp_1)~groupAccessType(public)", FriendsWatcher.Where.GroupPublic),
            ("wrld_1:1~group(grp_1)~groupAccessType(members)", FriendsWatcher.Where.Group), ("private", FriendsWatcher.Where.Private),
            ("traveling:traveling", FriendsWatcher.Where.Traveling), ("offline", FriendsWatcher.Where.Unknown), ("", FriendsWatcher.Where.Unknown),
        };
        int kindsOk = kinds.Count(k => FriendsWatcher.Instance(k.Item1) == k.Item2);
        Tools.Want($"friends data: instance kinds read right {kindsOk} of {kinds.Length} (want {kinds.Length} of {kinds.Length})", kindsOk == kinds.Length);
        // the watch's order: starred (Static: Orbit's star; Basil: VRCX's favourite), with you (Opal in your instance), the worlds A-Z,
        // private, traveling, not known
        var room = new RoomWatcher.Snapshot
        {
            Version = 901, LogFound = true, InRoom = true, World = "Velvet Lounge", VrcxFound = true, Since = DateTime.Now.AddMinutes(-20),
            Here = { new RoomWatcher.Person { Id = Id(10), Name = "Opal", Joined = DateTime.Now.AddMinutes(-18), Friend = true },
                     new RoomWatcher.Person { Id = "usr_cccccccc-0000-4000-8000-000000000002", Name = "Quill", Joined = DateTime.Now.AddMinutes(-5) } },
        };
        var merged = new List<FriendsWatcher.Friend>();
        if (s != null) FriendsWatcher.Merge(merged, s, room, new List<string> { Id(9) });
        const string wantOrder = "Basil, Static, Opal, Juniper, Nova, Mira, Wren, Kite";
        string order = string.Join(", ", merged.Select(f => f.Name));
        Tools.Want($"friends order: {order} (want {wantOrder})", order == wantOrder);
        // the same on the watch, its rows' words
        var stars0 = cfg.Data.Room.Starred;
        var (f0, r0) = (data.Friends, data.Room);
        cfg.Data.Room.Starred = new List<string> { Id(9) };
        (data.Friends, data.Room) = (s, room);
        int listed = wrist.FriendsShown(data);
        string watchOrder = wrist.FriendsOrder(data), opalLine = wrist.FriendsLine(data, 2), basilLine = wrist.FriendsLine(data, 0), miraLine = wrist.FriendsLine(data, 5);
        string wantOpal = "Velvet Lounge · " + Lang.T("with you"), wantBasil = "Clockwork Garden · " + Lang.T("Group+"), wantMira = Lang.T("Private world");
        Tools.Want($"friends view: the watch lists {listed} (want 8) in the order {watchOrder} (want {wantOrder}); Opal's line '{opalLine}' (want {wantOpal}), " +
                   $"Basil's '{basilLine}' (want {wantBasil}), Mira's '{miraLine}' (want {wantMira})",
                   listed == 8 && watchOrder == wantOrder && opalLine == wantOpal && basilLine == wantBasil && miraLine == wantMira);
        cfg.Data.Room.Starred = stars0;
        (data.Friends, data.Room) = (f0, r0);

        // ---- the session on a made-up clock ----
        var t0 = new DateTime(2026, 10, 16, 19, 0, 0);
        SessionRecap Run(SessionTracker tr, IEnumerable<(double min, bool connected, bool worn, RoomWatcher.Snapshot room)> steps, out bool card, out double endedAt)
        {
            card = false;
            endedAt = -1;
            foreach (var (min, c, w, rm) in steps)
            {
                var r = tr.Tick(t0.AddMinutes(min), c, w, rm, out bool cd);
                if (r != null) { card = cd; endedAt = min; return r; }
            }
            return null;
        }
        IEnumerable<(double, bool, bool, RoomWatcher.Snapshot)> Steps(double from, double to, bool connected, bool worn, double step = 0.25)
        {
            for (double m = from; m <= to + 1e-9; m += step) yield return (m, connected, worn, null);
        }
        // 25 minutes, the headset off: nothing after 2 minutes, the recap after 3 (its end the moment it came off), a card
        var a = new SessionTracker();
        var ra = Run(a, Steps(0, 25, true, true).Concat(Steps(25.25, 27.9, true, false)), out _, out _);
        bool quietAt2 = ra == null;
        ra = Run(a, Steps(28, 29, true, false), out bool cardA, out double atA);
        Tools.Want($"recap when: 25 min in VR, the headset off - nothing after 2 min {quietAt2}, the recap after 3 min {ra != null && atA <= 28.3}, a card {cardA}, " +
                   $"its length {ra?.Length.TotalMinutes:0.##} min (want 25.25) -",
                   quietAt2 && ra != null && atA <= 28.3 && cardA && Math.Abs(ra.Length.TotalMinutes - 25.25) < 1e-6);
        // 15 minutes: no recap at all
        var b = new SessionTracker();
        var rb = Run(b, Steps(0, 15, true, true).Concat(Steps(15.25, 25, true, false)), out _, out _);
        // 30 minutes, SteamVR gone with the headset on: a recap, no card
        var c2 = new SessionTracker();
        var rc = Run(c2, Steps(0, 30, true, true).Concat(Steps(30.25, 30.25, false, true)), out bool cardC, out _);
        // a break of 2 minutes is no end: 25 + 2 + 10 minutes, then off for good - one recap of 37 minutes
        var d2 = new SessionTracker();
        var rd = Run(d2, Steps(0, 25, true, true).Concat(Steps(25.25, 27, true, false)).Concat(Steps(27.25, 37, true, true)).Concat(Steps(37.25, 41, true, false)),
            out _, out double atD);
        Tools.Want($"recap when: 15 min in VR gives none {rb == null}; 30 min then SteamVR gone gives one {rc != null} without a card {!cardC}, " +
                   $"{rc?.Length.TotalMinutes:0.##} min (want 30.25); a 2 min break is no end - one recap of {rd?.Length.TotalMinutes:0.##} min (want 37.25) " +
                   $"{atD:0.##} min in (want 40.25) -",
                   rb == null && rc != null && !cardC && Math.Abs(rc.Length.TotalMinutes - 30.25) < 1e-6 && rd != null &&
                   Math.Abs(rd.Length.TotalMinutes - 37.25) < 1e-6 && Math.Abs(atD - 40.25) < 1e-6);
        // ---- the recap's numbers: a made-up evening ----
        RoomWatcher.Snapshot R(int v, string world, double since0, params (string name, bool friend)[] here)
        {
            var sn = new RoomWatcher.Snapshot { Version = v, LogFound = true, InRoom = world != null, World = world ?? "", Since = t0.AddMinutes(since0) };
            foreach (var (n, f) in here) sn.Here.Add(new RoomWatcher.Person { Id = "usr_" + n.ToLowerInvariant(), Name = n, Friend = f });
            return sn;
        }
        var lounge = R(1, "Velvet Lounge", 1, ("Opal", true), ("Quill", false));
        var plaza = R(2, "Pixel Plaza", 31, ("Nova", true), ("Pike", false), ("Quill", false));
        var away = R(3, null, 52);
        var library = R(4, "Moonlit Library", 53, ("Juniper", true), ("Sable", false));
        var late = R(5, "Moonlit Library", 53, ("Juniper", true), ("Sable", false), ("Late Comer", true));
        var ev = new SessionTracker();
        var steps = new List<(double, bool, bool, RoomWatcher.Snapshot)>();
        for (double m = 0; m <= 76; m += 0.5)
        {
            var rm = m < 1 ? null : m < 31 ? lounge : m < 52 ? plaza : m < 53 ? away : m < 72 ? library : late;
            steps.Add((m, true, m < 70, rm));
        }
        var re = Run(ev, steps, out _, out _);
        string worlds = re == null ? "none" : string.Join(", ", re.Worlds.Select(w => $"{w.Name} {w.Minutes:0}"));
        const string wantWorlds = "Velvet Lounge 30, Pixel Plaza 21, Moonlit Library 17";
        Tools.Want($"recap numbers: a made-up evening of {re?.Length.TotalMinutes:0} min (want 70): worlds {worlds} (want {wantWorlds}), {re?.WorldCount} visited " +
                   $"(want 3), the longest {re?.Longest?.Name} (want Velvet Lounge); people met {re?.People} (want 6), friends {re?.Friends} (want 3) - someone who " +
                   $"came while the headset was off not counted {re?.Names.Contains("Late Comer") == false} -",
                   re != null && Math.Abs(re.Length.TotalMinutes - 70) < 0.5 && worlds == wantWorlds && re.WorldCount == 3 && re.Longest?.Name == "Velvet Lounge" &&
                   re.People == 6 && re.Friends == 3 && !re.Names.Contains("Late Comer"));
        // photos: made-up files named the way VRChat names them, 3 in the session, 2 outside it
        string pics = Path.Combine(qa, "recap_photos");
        if (Directory.Exists(pics)) Directory.Delete(pics, true);
        string month = Path.Combine(pics, "2026-10");
        Directory.CreateDirectory(month);
        foreach (var at in new[] { "2026-10-16_18-40-02.100", "2026-10-16_19-05-12.120", "2026-10-16_19-33-40.500", "2026-10-16_20-02-09.001", "2026-10-16_21-30-00.000" })
            File.WriteAllBytes(Path.Combine(month, $"VRChat_{at}_1920x1080.png"), Array.Empty<byte>());
        int photos = re == null ? -2 : RecapStore.CountPhotos(pics, re.Start, re.End);
        int none = RecapStore.CountPhotos(Path.Combine(qa, "no_such_folder"), t0, t0.AddHours(1));
        // recap.json: written and read back (the snapshot's own file)
        string file = Path.Combine(qa, "recap_made_up.json");
        if (re != null) { re.Photos = photos; RecapStore.Save(file, re); }
        var back = RecapStore.Load(file);
        bool same = re != null && back != null && back.Start == re.Start && back.End == re.End && back.Photos == re.Photos && back.People == re.People &&
                    back.Friends == re.Friends && back.Worlds.Count == re.Worlds.Count && back.Longest?.Name == re.Longest?.Name;
        Tools.Want($"recap numbers: photos taken in the session {photos} (want 3), no folder {none} (want -1); recap.json written and read back the same {same} " +
                   $"(the card says '{(re == null ? "" : WristPanel.RecapLine(re))}') -", photos == 3 && none == -1 && same);
        var r0b = data.Recap;
        data.Recap = re;
        Log.Info($"recap view: the watch shows '{wrist.RecapShown(data)}'");
        data.Recap = r0b;
    }

    // ---- the views ----

    internal static void Draw2(WristPanel wrist, PanelData data) { wrist.Draw(data); wrist.Draw(data); }

    internal static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    internal static void Settle(WristPanel wrist, PanelData data)
    {
        for (int k = 0; k < 11; k++) { Thread.Sleep(40); wrist.Draw(data); }
    }

    internal static Vector2 Middle(System.Drawing.RectangleF c)
    {
        float s = Theme.Classic ? 2 : 1;
        return new Vector2((c.X + c.Width / 2) * s, (c.Y + c.Height / 2) * s);
    }

    internal static WatchWidget W(string type, int x, int y, int w, int h) => new() { Type = type, X = x, Y = y, W = w, H = h };

    // every size of both on the 6 x 4 grid, with a few others round them
    static List<WatchWidget> Face64() => new()
    {
        W("friends", 0, 0, 2, 2), W("recap", 2, 0, 2, 2), W("time", 4, 0, 2, 1), W("friends", 4, 1, 1, 1), W("recap", 5, 1, 1, 1),
        W("friends", 0, 2, 2, 1), W("recap", 2, 2, 2, 1), W("media", 4, 2, 2, 1),
        W("keyboard", 0, 3, 1, 1), W("desktops", 1, 3, 1, 1), W("night", 2, 3, 1, 1), W("settings", 3, 3, 1, 1), W("battery", 4, 3, 2, 1),
    };

    // the 4 x 3 page's: L, M, S
    static List<WatchWidget> Face43() => new()
    {
        W("friends", 0, 0, 2, 2), W("recap", 2, 0, 2, 1), W("friends", 2, 1, 1, 1), W("recap", 3, 1, 1, 1), W("friends", 0, 2, 2, 1), W("recap", 2, 2, 2, 1),
    };

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var keepData = (data.Friends, data.Room, data.Recap);
        var stars0 = cfg.Data.Room.Starred;
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            cfg.Data.Room.Starred = new List<string> { Id(4) };
            (data.Friends, data.Room, data.Recap) = (Friends(), Room(), Recap());
            Checks(wrist, data, wcfg, tag);
            Pictures(wrist, data, wcfg, gpu, dir, tag);
        }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (data.Friends, data.Room, data.Recap) = keepData;
            cfg.Data.Room.Starred = stars0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    static void Checks(WristPanel wrist, PanelData data, WristConfig wcfg, string tag)
    {
        bool classic = Theme.Classic;
        string xId = classic ? "wd95x" : "wclose";
        foreach (var kind in new[] { "friends", "recap" })
        {
            wcfg.Widgets = new List<WatchWidget> { W(kind, 0, 2, 2, 1) };
            wrist.Settle();
            Draw2(wrist, data);
            Tap(wrist, data, Middle(WatchWidgets.CellRect(0, 2, 2, 1, classic)));
            Settle(wrist, data);
            bool opened = wrist.IsOpen && wrist.DetailKind == kind;
            string scrolled = "";
            bool scrollOk = true;
            if (kind == "friends" && !classic)
            {
                // a long list: the stick scrolls it down and back up
                var many = Friends(30);
                var f0 = data.Friends;
                data.Friends = many;
                Draw2(wrist, data);
                var mid = Middle(WatchWidgets.CellRect(0, 0, WatchWidgets.Cols, WatchWidgets.Rows, classic));
                wrist.PointerMove(Hand.Left, mid);
                wrist.Draw(data);
                wrist.PointerScroll(Hand.Left, -2, mid);
                wrist.Draw(data);
                float down = wrist.FriendsScroll, downMax = wrist.FriendsScrollMax;
                wrist.PointerScroll(Hand.Left, 30, mid);
                wrist.Draw(data);
                float back = wrist.FriendsScroll;
                wrist.PointerLeave(Hand.Left);
                data.Friends = f0;
                Draw2(wrist, data);
                scrollOk = down > 0 && down <= downMax && back == 0;
                scrolled = $", the stick scrolled 30 friends down {down > 0 && down <= downMax} and back up {back == 0}";
            }
            var x = wrist.ControlPoint(data, xId);
            if (x is { } xp) Tap(wrist, data, xp);
            bool shut = x != null && !wrist.IsOpen;
            Settle(wrist, data);
            wrist.CloseForTest();
            Tools.Want($"{kind} view{tag}: a tap on the card opened it {opened}, its X closed it {shut && !wrist.DetailShown}{scrolled} -",
                       opened && shut && !wrist.DetailShown && scrollOk);
        }
        // no garbage a frame: both, every size, at rest (alone: the 95 family's clock makes its seconds' text)
        wcfg.Widgets = Face64().FindAll(x => x.Type is "friends" or "recap");
        long Bytes()
        {
            using var appOnly = Tools.AppAllocs();
            wrist.Settle();
            for (int i = 0; i < 4; i++) wrist.Draw(data);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) wrist.Draw(data);
            return (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
        }
        long rest = Bytes();
        wcfg.Grid = "4x3";
        wcfg.Widgets = Face43();
        long rest43 = Bytes();
        wcfg.Grid = null;
        wcfg.Widgets = Face64().FindAll(x => x.Type is "friends" or "recap");
        wrist.OpenForTest("friends");
        long openF = Bytes();
        wrist.CloseForTest();
        wrist.OpenForTest("recap");
        long openR = Bytes();
        wrist.CloseForTest();
        if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SocialAllocTypes("social" + tag, () => wrist.Draw(data));
        Tools.Want($"social view{tag}: a frame of the friends and recap sizes at rest makes {rest} B of garbage (want 0), on a 4 x 3 page {rest43} B (want 0) " +
                   $"(opened: the friends {openF} B, the recap {openR} B) -", rest == 0 && rest43 == 0);
    }

    static void Pictures(WristPanel wrist, PanelData data, WristConfig wcfg, Gpu gpu, string dir, string tag)
    {
        void Shot(string name, List<WatchWidget> face, string open, string grid = null)
        {
            wcfg.Grid = grid;
            wcfg.Widgets = face;
            wrist.Settle();
            if (open != null && !wrist.OpenForTest(open)) Log.Warn("social view: no widget " + open);
            Draw2(wrist, data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_social_{name}{tag}.png"));
            wrist.CloseForTest();
        }
        Shot("face", Face64(), null);
        Shot("face43", Face43(), null, "4x3");
        Shot("friends_open", Face64(), "friends");
        Shot("recap_open", Face64(), "recap");
        // nothing to show: VRCX not running, no recap yet
        var keep = (data.Friends, data.Room, data.Recap);
        (data.Friends, data.Room, data.Recap) = (new FriendsWatcher.Snapshot { Version = 1, State = FriendsWatcher.State.Closed }, null, null);
        Shot("none", Face64(), null);
        Shot("friends_none_open", Face64(), "friends");
        Shot("recap_none_open", Face64(), "recap");
        (data.Friends, data.Room, data.Recap) = keep;
        wcfg.Grid = null;
        Log.Info($"social view{tag}: 7 pictures (wrist_social_*{tag}.png)");
    }

    // ---- the sheets: every style side by side ----

    static readonly (string tag, string name)[] Styles =
    {
        ("", "Modern"), ("_cyber", "Cyber"), ("_95", "Windows 95"), ("_xp", "Windows XP"), ("_aero", "Windows 7"), ("_mac", "Mac"), ("_term", "Terminal"),
    };

    public static void Sheets(string dir)
    {
        Sheet(Path.Combine(dir, "social_preview.png"), dir, new[] { "face", "face43", "friends_open", "recap_open" });
        Sheet(Path.Combine(dir, "social_states.png"), dir, new[] { "none", "friends_none_open", "recap_none_open" });
    }

    static void Sheet(string path, string dir, string[] cases)
    {
        const int Cw = 400, Ch = 275, Gap = 10, Label = 22, Side = 110;
        using var bmp = new System.Drawing.Bitmap(Side + cases.Length * (Cw + Gap) + Gap, Styles.Length * (Ch + Label + Gap) + Gap + Label);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(255, 38, 40, 46));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        using var font = new System.Drawing.Font("Segoe UI", 10f);
        using var bold = new System.Drawing.Font("Segoe UI", 11f, System.Drawing.FontStyle.Bold);
        for (int j = 0; j < cases.Length; j++) g.DrawString(cases[j], font, System.Drawing.Brushes.Gainsboro, Side + Gap + j * (Cw + Gap), 4);
        int n = 0;
        for (int i = 0; i < Styles.Length; i++)
        {
            int y = Label + Gap + i * (Ch + Label + Gap);
            g.DrawString(Styles[i].name, bold, System.Drawing.Brushes.White, 8, y + Ch / 2 - 8);
            for (int j = 0; j < cases.Length; j++)
            {
                string f = Path.Combine(dir, $"wrist_social_{cases[j]}{Styles[i].tag}.png");
                if (!File.Exists(f)) continue;
                using var src = new System.Drawing.Bitmap(f);
                g.DrawImage(src, new System.Drawing.Rectangle(Side + Gap + j * (Cw + Gap), y, Cw, Ch));
                n++;
            }
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Log.Info($"social: {Path.GetFileName(path)} - {n} pictures of {Styles.Length * cases.Length}");
    }
}

static partial class Tools
{
    internal static void SocialAllocTypes(string what, Action draw) => AllocTypes(what, draw, null);

    // ORBIT_SNAP_ONLY=social: the friends' and the recap's data checks, their views' checks and pictures and their layout check (their
    // sizes and opened only) in every style, the sheets, in seconds
    static int SocialOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        int failed0 = WantsFailed, layout = 0;
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = SnapCulture;
        SocialViewTools.DataChecks(wrist, data, cfg, dir);
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            SocialViewTools.Run(wrist, data, cfg, gpu, dir, tag);
            foreach (var kind in new[] { "friends", "recap" })
            {
                string f0 = cfg.Data.Wrist.Face;
                layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag.Length > 0 ? tag[1..] : "modern", onlyKind: kind);
                cfg.Data.Wrist.Face = f0;
                cfg.Data.Wrist.Widgets = null;
            }
        }
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        SocialViewTools.Sheets(dir);
        CultureInfo.CurrentCulture = culture;
        Log.Info("snapshot (friends and recap only) written to " + dir);
        return layout > 0 ? 1 : WantsExit(failed0);
    }
}
