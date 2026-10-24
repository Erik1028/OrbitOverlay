using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Orbit;

// --snapshot's part for the recap's Today side (Sys/TodayLog.cs, WristToday.cs; ORBIT_SNAP_ONLY=today), on MADE-UP data only - never
// VRCX's, VR Gallery's, VRChat's or the pictures folder's own files:
//  - DataChecks: a made-up VRCX database (its game log and friend list) and a made-up VR Gallery database written into the snapshot's
//    folder (qa/today_*.sqlite3, with the friends' checks' writer: SocialViewTools.MadeUp) and made-up photo files, read the way the
//    app reads them (TodayLog.Read: read only, immutable) on a made-up clock past midnight: the day from 5 AM (a visit before it left
//    out, one past midnight in, a stop of 45 s no visit, a rejoin of the same instance one), each visit's end (its stay, the next arrival,
//    now while VRChat runs, the last join or leave seen when it does not or was started again since), a night of its own (qa/
//    today_vrcx_night.sqlite3: a stay not written and VRChat closed for hours after it, the hour the clocks go back), the people met
//    (you left out, a stranger of another instance too, the friends first, a name without an id, the
//    day's everyone once), the photos of each visit and of the day; VR Gallery's day when there is no VRCX (you known by the Room's
//    id, or as the player in every session), an older VRCX's friends with VR Gallery's day, neither: none, a broken file: not
//    readable; the databases' bytes the same after every read, no journal left beside them.
//  - Run (every style): the recap opened, its Today control switches it (the app's reader asked for it: RecapTodayOn), its rows;
//    no garbage a frame while it shows; Session again; its X closes it. Pictures wrist_today_<case><tag>.png (open, session, none,
//    open in Magyar), the sheet today_preview.png.
static class TodayViewTools
{
    const string P = "usr0123456789abcdef0123456789abcdef", Me = "usr_01234567-89ab-cdef-0123-456789abcdef";
    static string Id(int i) => $"usr_aaaaaaaa-0000-4000-8000-{i:000000000000}";
    static readonly DateTime Now0 = new(2026, 10, 17, 1, 30, 0);       // past midnight: still the 16th's day (5 AM to 5 AM)
    static readonly DateTime? VrOn = new DateTime(2026, 10, 16, 18, 30, 0).ToUniversalTime();       // VRChat started before the evening
    static DateTime At(int day, int h, int m, int s = 0) => new(2026, 10, day, h, m, s);
    static string Utc(DateTime local) => local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    static string Loc(DateTime local) => local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    // the made-up evening: friends Nova 0, Opal 1, Juniper 2 (and Kite 9, not met); Quill 3, Pike 4, Sable 5, Rune 6, Tansy (no id);
    // an early bird (7) before 5 AM, a stranger (8) in an instance you were not in
    static readonly (string world, string loc, DateTime at, double minutes)[] Visits =
    {
        ("Velvet Lounge", "wrld_v:1~region(eu)", At(16, 4, 40), 15),                      // the 15th's day: left out
        ("Lantern Harbor", "wrld_a:1~region(eu)", At(16, 19, 0), 42),
        ("Pixel Plaza", "wrld_b:2~hidden(usr_x)~region(eu)", At(16, 19, 43), 0),          // its stay not written: to the next arrival
        ("Moonlit Library", "wrld_c:3~region(us)", At(16, 21, 10), 30),
        ("Moonlit Library", "wrld_c:3~region(us)", At(16, 21, 40, 30), 24.5),             // rejoined 30 s later: the same visit
        ("Home Base", "wrld_h:1~private(usr_y)", At(16, 23, 57), 0.75),                   // 45 s at home on the way: no visit
        ("Lantern Harbor", "wrld_a:9~region(eu)", At(16, 23, 58), 0),                     // the last: still there (or VRCX closed)
    };

    static readonly (DateTime at, bool join, string name, string id, string loc)[] JoinsLeaves =
    {
        (At(16, 4, 40, 15), true, "You", Me, "wrld_v:1~region(eu)"), (At(16, 4, 41), true, "Early Bird", Id(7), "wrld_v:1~region(eu)"),
        (At(16, 19, 0, 12), true, "You", Me, "wrld_a:1~region(eu)"), (At(16, 19, 0, 15), true, "Quill", Id(3), "wrld_a:1~region(eu)"),
        (At(16, 19, 0, 15), true, "Nova", Id(0), "wrld_a:1~region(eu)"), (At(16, 19, 20), true, "Pike", Id(4), "wrld_a:1~region(eu)"),
        (At(16, 19, 30), false, "Quill", Id(3), "wrld_a:1~region(eu)"), (At(16, 19, 41, 58), false, "Nova", Id(0), "wrld_a:1~region(eu)"),
        (At(16, 19, 43, 10), true, "You", Me, "wrld_b:2~hidden(usr_x)~region(eu)"), (At(16, 19, 43, 12), true, "Quill", Id(3), "wrld_b:2~hidden(usr_x)~region(eu)"),
        (At(16, 19, 43, 12), true, "Opal", Id(1), "wrld_b:2~hidden(usr_x)~region(eu)"), (At(16, 20, 0), true, "Sable", Id(5), "wrld_b:2~hidden(usr_x)~region(eu)"),
        (At(16, 20, 10), true, "Stranger", Id(8), "wrld_z:7~region(eu)"),
        (At(16, 21, 9, 55), false, "Quill", Id(3), "wrld_b:2~hidden(usr_x)~region(eu)"), (At(16, 21, 9, 55), false, "Opal", Id(1), "wrld_b:2~hidden(usr_x)~region(eu)"),
        (At(16, 21, 9, 55), false, "Sable", Id(5), "wrld_b:2~hidden(usr_x)~region(eu)"),                // (everyone left as you went: VRChat logs it)
        (At(16, 21, 10, 20), true, "You", Me, "wrld_c:3~region(us)"), (At(16, 21, 10, 22), true, "Juniper", Id(2), "wrld_c:3~region(us)"),
        (At(16, 21, 30), true, "Tansy", "", "wrld_c:3~region(us)"),
        (At(16, 21, 40, 35), true, "You", Me, "wrld_c:3~region(us)"), (At(16, 21, 41), true, "Tansy", "", "wrld_c:3~region(us)"),
        (At(16, 23, 57, 5), true, "You", Me, "wrld_h:1~private(usr_y)"),
        (At(16, 23, 58, 10), true, "You", Me, "wrld_a:9~region(eu)"), (At(16, 23, 58, 15), true, "Nova", Id(0), "wrld_a:9~region(eu)"),
        (At(17, 0, 10), true, "Rune", Id(6), "wrld_a:9~region(eu)"), (At(17, 1, 5), false, "Rune", Id(6), "wrld_a:9~region(eu)"),
    };

    // a night of its own (Erik's 10-24 day: 03:00 CEST on the 25th is 02:00 CET again; the times as instants, now 02:20 UTC): a stay
    // VRCX never wrote, VRChat closed 07:12 to 13:30 after it (no more than its 12 minutes, not the 6 hours to the next arrival); a 20
    // minute stay from 02:50 CEST that the next arrival at 02:10 CET follows (still 20 minutes); the last one going on since 02:10 CET
    static DateTime Z(int day, int h, int m, int s = 0) => new(2026, 10, day, h, m, s, DateTimeKind.Utc);
    static readonly DateTime NightNow = Z(25, 2, 20).ToLocalTime(), NightVr = Z(24, 23, 0);
    static readonly (string world, string loc, DateTime at, double minutes)[] NightVisits =
    {
        ("Sunrise Cafe", "wrld_s:1~region(eu)", Z(24, 5, 0), 0),
        ("Lantern Harbor", "wrld_a:2~region(eu)", Z(24, 11, 30), 30),
        ("Clocktower", "wrld_k:1~region(eu)", Z(25, 0, 50), 20),
        ("Moonlit Library", "wrld_c:4~region(us)", Z(25, 1, 10), 0),
    };
    static readonly (DateTime at, bool join, string name, string id, string loc)[] NightJoinsLeaves =
    {
        (Z(24, 5, 0, 10), true, "You", Me, "wrld_s:1~region(eu)"), (Z(24, 5, 4), true, "Wren", Id(10), "wrld_s:1~region(eu)"),
        (Z(24, 5, 12), false, "Wren", Id(10), "wrld_s:1~region(eu)"), (Z(24, 11, 30, 10), true, "You", Me, "wrld_a:2~region(eu)"),
        (Z(25, 0, 50, 10), true, "You", Me, "wrld_k:1~region(eu)"), (Z(25, 0, 51), true, "Wren", Id(10), "wrld_k:1~region(eu)"),
        (Z(25, 1, 10, 10), true, "You", Me, "wrld_c:4~region(us)"), (Z(25, 1, 11), true, "Wren", Id(10), "wrld_c:4~region(us)"),
    };

    static string FriendList(StringBuilder sql)
    {
        sql.Append($"CREATE TABLE {P}_friend_log_current (user_id TEXT PRIMARY KEY, display_name TEXT, trust_level TEXT, friend_number INTEGER);");
        foreach (var (name, i) in new[] { ("Nova", 0), ("Opal", 1), ("Juniper", 2), ("Kite", 9) })
            sql.Append($"INSERT INTO {P}_friend_log_current VALUES ('{Id(i)}', '{name}', 'known', {i + 1});");
        sql.Append("CREATE TABLE configs (key TEXT PRIMARY KEY, value TEXT);");          // there, never read
        return sql.ToString();
    }

    static string VrcxSql() => VrcxSql(Visits, JoinsLeaves);

    static string VrcxSql((string world, string loc, DateTime at, double minutes)[] visits, (DateTime at, bool join, string name, string id, string loc)[] joinsLeaves)
    {
        var sql = new StringBuilder();
        FriendList(sql);
        sql.Append("CREATE TABLE gamelog_location (id INTEGER PRIMARY KEY, created_at TEXT, location TEXT, world_id TEXT, world_name TEXT, time INTEGER, group_name TEXT);");
        sql.Append("CREATE TABLE gamelog_join_leave (id INTEGER PRIMARY KEY, created_at TEXT, type TEXT, display_name TEXT, location TEXT, user_id TEXT, time INTEGER);");
        int n = 0;
        foreach (var (world, loc, at, minutes) in visits)
            sql.Append($"INSERT INTO gamelog_location VALUES ({++n}, '{Utc(at)}', '{loc}', '{loc[..6]}', '{world}', {(long)(minutes * 60000)}, '');");
        n = 0;
        foreach (var (at, join, name, id, loc) in joinsLeaves)
            sql.Append($"INSERT INTO gamelog_join_leave VALUES ({++n}, '{Utc(at)}', '{(join ? "OnPlayerJoined" : "OnPlayerLeft")}', '{name}', '{loc}', '{id}', 0);");
        return sql.ToString();
    }

    // VR Gallery's: the same evening as sessions (the last one's end the log's latest line, 6 minutes ago), the players with you in each
    static string GallerySql()
    {
        var sql = new StringBuilder();
        sql.Append("CREATE TABLE sessions (id INTEGER PRIMARY KEY, log_file TEXT, world_id TEXT, world_name TEXT, start_at TEXT, end_at TEXT, instance_type TEXT, region TEXT);");
        sql.Append("CREATE TABLE session_players (session_id INTEGER, name TEXT, user_id TEXT, join_at TEXT, leave_at TEXT);");
        for (int i = 0; i < Visits.Length; i++)
        {
            var (world, loc, at, minutes) = Visits[i];
            var end = minutes > 0 ? at.AddMinutes(minutes) : i + 1 < Visits.Length ? Visits[i + 1].at : Now0.AddMinutes(-6);
            sql.Append($"INSERT INTO sessions VALUES ({i + 1}, 'output_log.txt', '{loc[..6]}', '{world}', '{Loc(at)}', '{Loc(end)}', 'public', 'eu');");
        }
        foreach (var (at, join, name, id, loc) in JoinsLeaves)
        {
            int s = Array.FindLastIndex(Visits, v => v.loc == loc && v.at <= at.AddSeconds(2));
            if (!join || s < 0) continue;
            sql.Append($"INSERT INTO session_players VALUES ({s + 1}, '{name}', '{id}', '{Loc(at)}', NULL);");
        }
        return sql.ToString();
    }

    static string Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "none";

    static string VisitsOf(TodayLog.Snapshot s, bool utc = false) =>
        s == null ? "none" : string.Join(", ", s.Visits.Select(v => $"{v.World} {(utc ? v.Start : v.Start.ToLocalTime()):HH:mm}-{(utc ? v.End : v.End.ToLocalTime()):HH:mm} " +
                                                                  $"{v.Minutes:0}{(v.Here ? " here" : "")}"));
    static string PeopleOf(TodayLog.Snapshot s) =>
        s == null ? "none" : string.Join("; ", s.Visits.Select(v => $"{string.Join(", ", v.Names)} {v.People}/{v.Friends}"));

    internal static TodayLog.Snapshot Made;          // the made-up day the views show (read from the made-up VRCX database)

    public static void DataChecks(string dir)
    {
        string qa = Path.Combine(dir, "qa");
        Directory.CreateDirectory(qa);
        string vrcx = Path.Combine(qa, "today_vrcx.sqlite3"), gallery = Path.Combine(qa, "today_gallery.sqlite3"), old = Path.Combine(qa, "today_vrcx_old.sqlite3");
        string broken = Path.Combine(qa, "today_broken.sqlite3"), none = Path.Combine(qa, "today_no_such.sqlite3"), night = Path.Combine(qa, "today_vrcx_night.sqlite3");
        bool made = SocialViewTools.MadeUp.Write(vrcx, VrcxSql()) & SocialViewTools.MadeUp.Write(gallery, GallerySql()) &
                    SocialViewTools.MadeUp.Write(old, FriendList(new StringBuilder())) & SocialViewTools.MadeUp.Write(night, VrcxSql(NightVisits, NightJoinsLeaves));
        File.WriteAllText(broken, "not a database, written by the snapshot");
        // the photos: before the day, three in the evening's first two worlds, one between worlds, one past midnight, one the next day
        string pics = Path.Combine(qa, "today_photos");
        if (Directory.Exists(pics)) Directory.Delete(pics, true);
        Directory.CreateDirectory(Path.Combine(pics, "2026-10"));
        File.WriteAllBytes(Path.Combine(pics, "VRChat_2026-10-16_04-50-00.000_1920x1080.png"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(pics, "VRChat_2026-10-16_19-10-00.000_1920x1080.png"), Array.Empty<byte>());
        foreach (var at in new[] { "2026-10-16_19-20-00.000", "2026-10-16_20-00-00.000", "2026-10-16_22-30-00.000", "2026-10-17_00-30-00.000", "2026-10-17_05-10-00.000" })
            File.WriteAllBytes(Path.Combine(pics, "2026-10", $"VRChat_{at}_1920x1080.png"), Array.Empty<byte>());
        // the night's: one while VRChat was closed, one past 3 AM CET (the names' times are local)
        string nightPics = Path.Combine(qa, "today_photos_night");
        if (Directory.Exists(nightPics)) Directory.Delete(nightPics, true);
        Directory.CreateDirectory(nightPics);
        foreach (var at in new[] { Z(24, 8, 0), Z(25, 2, 10) })
            File.WriteAllBytes(Path.Combine(nightPics, $"VRChat_{at.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss.fff", CultureInfo.InvariantCulture)}_1920x1080.png"), Array.Empty<byte>());
        string h0 = Hash(vrcx) + Hash(gallery) + Hash(old) + Hash(night);

        // ---- VRCX's game log ----
        var a = TodayLog.Read(vrcx, gallery, pics, Now0, null, VrOn);
        const string wantVisits = "Lantern Harbor 19:00-19:42 42, Pixel Plaza 19:43-21:10 87, Moonlit Library 21:10-22:05 55, Lantern Harbor 23:58-01:30 92 here";
        Tools.Want($"today data: a made-up VRCX database written {made}, read {a.State} from {a.Source} (want Ok VRCX), day {a.Day:MM-dd} (want 10-16); visits " +
                   $"{VisitsOf(a)} (want {wantVisits}) - the one before 5 AM left out, the one past midnight in, a stay not written ending at the next arrival, " +
                   $"a rejoin of the same instance one visit, a stop of 45 s none, the last going on while VRChat runs -", made && a.State == TodayLog.State.Ok && a.Source == "VRCX" && a.Day == new DateTime(2026, 10, 16) &&
                   VisitsOf(a) == wantVisits);
        const string wantPeople = "Nova, Quill, Pike 3/1; Opal, Quill, Sable 3/1; Juniper, Tansy 2/1; Nova, Rune 2/1";
        const string wantDay = "Nova, Opal, Juniper, Quill, Pike, Sable, Tansy, Rune";
        string dayNames = string.Join(", ", a.Names);
        Tools.Want($"today data: people met {PeopleOf(a)} (want {wantPeople}); the day {a.People} people (want 8), {a.Friends} friends (want 3), " +
                   $"{dayNames} (want {wantDay}) - you, the early bird and a stranger of another instance not counted, the friends first, a name without an id " +
                   $"kept, everyone once -", PeopleOf(a) == wantPeople && a.People == 8 && a.Friends == 3 && dayNames == wantDay);
        string photos = string.Join(", ", a.Visits.Select(v => v.Photos));
        Tools.Want($"today data: photos of the day {a.Photos} (want 5), by visit {photos} (want 2, 1, 0, 1); {a.Minutes:0} min in worlds (want 276) -",
                   a.Photos == 5 && photos == "2, 1, 0, 1" && Math.Abs(a.Minutes - 276) < 0.01);
        var b = TodayLog.Read(vrcx, gallery, pics, Now0, null, null);
        var lastB = b.Visits.Count > 0 ? b.Visits[^1] : null;
        Tools.Want($"today data: VRChat not running - the last visit ends at the last leave VRCX saw there: {lastB?.End.ToLocalTime():HH:mm} {lastB?.Minutes:0} min, here {lastB?.Here} " +
                   "(want 01:05 67 min, here False) -", lastB != null && lastB.End == At(17, 1, 5).ToUniversalTime() && !lastB.Here);
        // VRChat started again at 01:20 (it crashed in that world; VRCX was closed meanwhile): that visit is not going on
        var c = TodayLog.Read(vrcx, gallery, pics, Now0, null, At(17, 1, 20).ToUniversalTime());
        var lastC = c.Visits.Count > 0 ? c.Visits[^1] : null;
        var g4 = TodayLog.Read(none, gallery, pics, Now0, Me, At(17, 1, 20).ToUniversalTime());
        var lastG4 = g4.Visits.Count > 0 ? g4.Visits[^1] : null;
        Tools.Want($"today data: VRChat started again after the last visit began - VRCX's ends at the last leave seen {lastC?.End.ToLocalTime():HH:mm}, here {lastC?.Here} " +
                   $"(want 01:05, False); VR Gallery's at its end {lastG4?.End.ToLocalTime():HH:mm}, here {lastG4?.Here} (want 01:24, False) -",
                   lastC != null && lastC.End == At(17, 1, 5).ToUniversalTime() && !lastC.Here && lastG4 != null && lastG4.End == Now0.AddMinutes(-6).ToUniversalTime() && !lastG4.Here);
        Made = a;

        // ---- the night: a stay not written, then hours without VRChat; the hour the clocks go back ----
        var z = TodayLog.Read(night, null, nightPics, NightNow, null, NightVr);
        const string wantNight = "Sunrise Cafe 05:00-05:12 12, Lantern Harbor 11:30-12:00 30, Clocktower 00:50-01:10 20, Moonlit Library 01:10-02:20 70 here";
        string nightPhotos = string.Join(", ", z.Visits.Select(v => v.Photos));
        Tools.Want($"today data: a night of its own (UTC) - {VisitsOf(z, true)} (want {wantNight}): a stay not written and VRChat closed for 6 hours after it " +
                   $"to the last leave seen, a stay across the hour the clocks go back its 20 minutes; {z.Minutes:0} min in worlds (want 132); photos " +
                   $"{z.Photos}, by visit {nightPhotos} (want 2; 0, 0, 0, 1: none on the closed hours) -",
                   z.State == TodayLog.State.Ok && VisitsOf(z, true) == wantNight && Math.Abs(z.Minutes - 132) < 0.01 && z.Photos == 2 && nightPhotos == "0, 0, 0, 1");

        // ---- VR Gallery's, no VRCX ----
        var g = TodayLog.Read(none, gallery, pics, Now0, Me, VrOn);
        var g2 = TodayLog.Read(none, gallery, pics, Now0, null, VrOn);
        var g3 = TodayLog.Read(none, gallery, pics, Now0, Me, null);
        const string wantGallery = "Lantern Harbor 19:00-19:42 42, Pixel Plaza 19:43-21:10 87, Moonlit Library 21:10-22:05 55, Lantern Harbor 23:58-01:30 92 here";
        Tools.Want($"today data: no VRCX - VR Gallery's {g.State} {g.Source} (want Ok VR Gallery): {VisitsOf(g)} (want {wantGallery}); {g.People} people (want 8), " +
                   $"{g.Friends} friends (want 0: no friend list); you found as the player in every session {g2.People} people (want 8); VRChat not running: " +
                   $"the last to its end {g3.Visits.LastOrDefault()?.End.ToLocalTime():HH:mm} (want 01:24) -",
                   g.State == TodayLog.State.Ok && g.Source == "VR Gallery" && VisitsOf(g) == wantGallery && g.People == 8 && g.Friends == 0 && g2.People == 8 &&
                   g3.Visits.LastOrDefault()?.End == Now0.AddMinutes(-6).ToUniversalTime());
        // an older VRCX (a friend list, no game log): VR Gallery's day, VRCX's friends
        var o = TodayLog.Read(old, gallery, pics, Now0, null, VrOn);
        Tools.Want($"today data: an older VRCX without a game log - {o.State} {o.Source} (want Ok VR Gallery), {o.People} people (want 8), {o.Friends} friends " +
                   $"from VRCX's list (want 3) -", o.State == TodayLog.State.Ok && o.Source == "VR Gallery" && o.People == 8 && o.Friends == 3);
        // neither; a broken file
        var n = TodayLog.Read(none, Path.Combine(qa, "no_gallery.db"), pics, Now0, null, VrOn);
        var x = TodayLog.Read(broken, null, pics, Now0, null, VrOn);
        Tools.Want($"today data: neither database - {n.State} (want NoSource), {n.Visits.Count} visits (want 0), the photos still counted {n.Photos} (want 5); " +
                   $"a broken file {x.State} (want Unreadable); no picture folder {TodayLog.Read(vrcx, null, Path.Combine(qa, "no_pics"), Now0, null, VrOn).Photos} (want -1) -",
                   n.State == TodayLog.State.NoSource && n.Visits.Count == 0 && n.Photos == 5 && x.State == TodayLog.State.Unreadable &&
                   TodayLog.Read(vrcx, null, Path.Combine(qa, "no_pics"), Now0, null, VrOn).Photos == -1);
        // read only: the same bytes, nothing beside them
        string h1 = Hash(vrcx) + Hash(gallery) + Hash(old) + Hash(night);
        bool clean = !new[] { vrcx, gallery, old, night }.Any(f => File.Exists(f + "-journal") || File.Exists(f + "-wal") || File.Exists(f + "-shm"));
        Tools.Want($"today data: the databases' bytes the same after every read {h0 == h1}, no journal or WAL left beside them {clean} -", h0 == h1 && clean);

        // the reader: switched on, it reads on its own thread at once (today, not the made-up 16th: no visit); switched off, no more
        using var w = new TodayWatcher { VrcxDb = vrcx, GalleryDb = gallery, Pictures = pics };
        var t0 = DateTime.Now;
        w.Active = true;
        while (w.Get().State == TodayLog.State.Off && DateTime.Now - t0 < TimeSpan.FromSeconds(10)) System.Threading.Thread.Sleep(50);
        var first = w.Get();
        double took = (DateTime.Now - t0).TotalSeconds;
        w.Active = false;
        System.Threading.Thread.Sleep(5600);
        bool quiet = w.Get().Version == first.Version;
        Tools.Want($"today reader: switched on it read on its thread in {took:0.00} s: {first.State} {first.Source}, day {first.Day:MM-dd} (want Ok VRCX, " +
                   $"{PhotoWatcher.DayOf(DateTime.Now):MM-dd}); switched off it read no more {quiet} -",
                   first.State == TodayLog.State.Ok && first.Source == "VRCX" && first.Day == PhotoWatcher.DayOf(DateTime.Now) && took < 5 && quiet);
    }

    // ---- the views ----

    static readonly List<(string tag, string open, string hu)> Preview = new();

    static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var keepData = (data.Recap, data.Today);
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            (data.Recap, data.Today) = (SocialViewTools.Recap(), Made);
            Checks(wrist, data, wcfg, tag);
            Pictures(wrist, data, wcfg, gpu, dir, tag);
        }
        finally
        {
            wrist.TodayForTest(false);
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (data.Recap, data.Today) = keepData;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    static void Checks(WristPanel wrist, PanelData data, WristConfig wcfg, string tag)
    {
        bool classic = Theme.Classic, term = Theme.Retro == RetroStyle.Terminal;
        string todayId = classic && !term ? "wrc95today" : "wrctoday", sessionId = classic && !term ? "wrc95today" : "wrcses", xId = classic ? "wd95x" : "wclose";
        wrist.TodayForTest(false);
        wcfg.Widgets = new List<WatchWidget> { SocialViewTools.W("recap", 0, 2, 2, 1) };
        wrist.Settle();
        SocialViewTools.Draw2(wrist, data);
        SocialViewTools.Tap(wrist, data, SocialViewTools.Middle(WatchWidgets.CellRect(0, 2, 2, 1, classic)));
        SocialViewTools.Settle(wrist, data);
        bool opened = wrist.IsOpen && wrist.DetailKind == "recap", session = !wrist.RecapTodayOn;
        long Bytes()
        {
            using var appOnly = Tools.AppAllocs();
            wrist.Settle();
            for (int i = 0; i < 4; i++) wrist.Draw(data);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) wrist.Draw(data);
            return (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
        }
        long sessionBytes = Bytes();
        var tp = wrist.ControlPoint(data, todayId);
        if (tp is { } p) SocialViewTools.Tap(wrist, data, p);
        SocialViewTools.Settle(wrist, data);
        bool today = tp != null && wrist.RecapTodayShown && wrist.RecapTodayOn;
        int rows = wrist.TodayRowsShown(data);
        string shown = wrist.TodayShown(data);
        // no garbage a frame while it shows
        long bytes = Bytes();
        if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SocialAllocTypes("today" + tag, () => wrist.Draw(data));
        var sp = wrist.ControlPoint(data, sessionId);
        if (sp is { } q) SocialViewTools.Tap(wrist, data, q);
        SocialViewTools.Settle(wrist, data);
        bool back = sp != null && !wrist.RecapTodayShown && !wrist.RecapTodayOn;
        // Today again: a visit with photos tapped - the photo view's gallery with that visit's photos (no photo widget on the page: grown
        // out of the recap's card) (the Terminal lists the day as text: no link)
        bool linked = true;
        string linkNote = "n/a (Terminal)";
        if (!term)
        {
            var tp2 = wrist.ControlPoint(data, todayId);
            if (tp2 is { } p2) SocialViewTools.Tap(wrist, data, p2);
            SocialViewTools.Settle(wrist, data);
            var album0 = data.Album;
            data.Album = GalleryViewTools.Album(PhotoFake.Snap(5));
            var at = wrist.TodayPhotosPoint(data, 0);
            if (at is { } a2) SocialViewTools.Tap(wrist, data, a2);
            SocialViewTools.Draw2(wrist, data);
            linked = at != null && wrist.GalleryGridShown && wrist.DetailKind == "photo" && wrist.GalleryFilterShown == "Lantern Harbor";
            linkNote = $"{linked} (filter '{wrist.GalleryFilterShown}', open {wrist.DetailKind})";
            wrist.CloseForTest();
            data.Album = album0;
            wrist.Settle();
            wrist.OpenForTest("recap");
            SocialViewTools.Draw2(wrist, data);
        }
        var x = wrist.ControlPoint(data, xId);
        if (x is { } xp) SocialViewTools.Tap(wrist, data, xp);
        bool shut = x != null && !wrist.IsOpen;
        SocialViewTools.Settle(wrist, data);
        wrist.CloseForTest();
        Tools.Want($"today view{tag}: the recap opened {opened} on its session {session}, its Today control switched it {today} (the reader asked for: RecapTodayOn), " +
                   $"{rows} worlds listed (want 4) - '{shown}'; a frame while it shows makes {bytes} B of garbage (want 0; the session's side {sessionBytes} B); " +
                   $"Session again {back}, its X closed it {shut}; a visit tapped opened its photos {linkNote} -",
                   opened && session && today && rows == 4 && bytes == 0 && back && shut && linked);
    }

    static void Pictures(WristPanel wrist, PanelData data, WristConfig wcfg, Gpu gpu, string dir, string tag)
    {
        string Shot(string name, bool today = true)
        {
            wcfg.Widgets = new List<WatchWidget> { SocialViewTools.W("recap", 2, 0, 2, 2), SocialViewTools.W("friends", 0, 0, 2, 2), SocialViewTools.W("time", 4, 0, 2, 1) };
            wrist.Settle();
            if (!wrist.OpenForTest("recap")) Log.Warn("today view: no recap widget");
            wrist.TodayForTest(today);
            SocialViewTools.Draw2(wrist, data);
            string f = Path.Combine(dir, $"wrist_today_{name}{tag}.png");
            wrist.Surf.SavePng(gpu, f);
            wrist.TodayForTest(false);
            wrist.CloseForTest();
            return f;
        }
        string open = Shot("open");
        Shot("session", false);                // the session's side under Session | Today
        var keep = data.Today;
        data.Today = new TodayLog.Snapshot { Version = 2, State = TodayLog.State.NoSource, Day = Made?.Day ?? DateTime.Today };
        Shot("none");
        data.Today = keep;
        // in Magyar: the words fit (the culture back as the snapshot has it)
        Lang.Set("hu");
        string hu = Shot("open_hu");
        Lang.Set(null);
        CultureInfo.CurrentCulture = Tools.SnapCulture;
        Preview.Add((tag, open, hu));
        Log.Info($"today view{tag}: 4 pictures (wrist_today_*{tag}.png)");
    }

    // every style: Today opened, in English and in Magyar
    static void Sheet(string dir)
    {
        if (Preview.Count == 0) return;
        const int W = 640, H = 440, Gap = 12, Label = 26;
        using var sheet = new System.Drawing.Bitmap(2 * W + 3 * Gap, Preview.Count * (H + Label + Gap) + Gap);
        using (var g = System.Drawing.Graphics.FromImage(sheet))
        {
            g.Clear(System.Drawing.Color.FromArgb(255, 32, 34, 40));
            using var font = new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            for (int i = 0; i < Preview.Count; i++)
            {
                var (t, open, hu) = Preview[i];
                float y = Gap + i * (H + Label + Gap);
                g.DrawString($"{(t.Length == 0 ? "modern" : t[1..])}: the recap's Today (a made-up day), English and Magyar", font, System.Drawing.Brushes.White, Gap, y + 4);
                int k = 0;
                foreach (var f in new[] { open, hu })
                {
                    using (var bm = new System.Drawing.Bitmap(f)) g.DrawImage(bm, Gap + k * (W + Gap), y + Label, W, H);
                    k++;
                }
            }
        }
        sheet.Save(Path.Combine(dir, "today_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
        Log.Info($"today: today_preview.png - {Preview.Count} styles");
    }

    public static int Only(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir)
    {
        int failed0 = Tools.WantsFailed;
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = Tools.SnapCulture;
        DataChecks(dir);
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            Run(wrist, data, cfg, gpu, dir, tag);
        }
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        Sheet(dir);
        CultureInfo.CurrentCulture = culture;
        Log.Info("snapshot (the recap's Today only) written to " + dir);
        return Tools.WantsExit(failed0);
    }
}
