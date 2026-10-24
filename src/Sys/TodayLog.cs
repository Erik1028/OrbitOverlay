using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Orbit;

// The day in VRChat, the recap's Today side (Erik 10-24: which worlds today and with whom, with the photos - the recap widget's opened
// view, WristToday.cs; the session's recap stays as it is). The day is the photos' day: 5 AM to 5 AM (PhotoWatcher.DayOf: a night in
// VR past midnight is one day), across sessions and restarts; a visit belongs to the day it began in. Each visit: the world, from - to
// and how long; the people met there (everyone who joined the instance while you were in it, you yourself left out, the friends
// first); the photos taken there. The day's: everyone met once, the friends among them, the photos, the time in worlds. A stop under a
// minute (VRChat's home world on the way to the next one: one stay in ten of Erik's log) is no visit, a rejoin of the same instance a
// moment later is the same one.
// Where it comes from, the more reliable first:
//  - VRCX's game log: gamelog_location (each instance you were in, the world's name, when you came; "time" - how long, in ms - written
//    when you leave it, 0 while you are there) and gamelog_join_leave (everyone joining and leaving, with their user id - you too).
//    VRCX writes it live from VRChat's own log while it runs; Orbit reads that very file for the friends already (the friends among the
//    people are its friend list, the same file); and it keeps a rollback journal, so an immutable read sees every row committed.
//  - else VR Gallery's sessions and session_players: made from the same VRChat logs, but a WAL database - an immutable read (the only
//    way Orbit opens another app's database) leaves out what still sits in vrgallery.db-wal, the evening's newest sessions until VR
//    Gallery checkpoints - and Erik's own app, not everyone's. The friends then from VRCX's list if there is one (an older VRCX with
//    no game log), else none are known.
//  - neither: the day is not known, the view says so. The photos need neither: VRChat's picture folder, counted by the times in the
//    names as the session's recap counts them (VRCX keeps no photos; VR Gallery's photos table would be its WAL's too).
// Read only and never locked (Vrcx.Open: SQLite's immutable mode through winsqlite3.dll), never VRCX's cookies, configs or anything of
// a login; nothing is written anywhere. Read on the Today reader's thread (TodayWatcher), never on a frame.
public static class TodayLog
{
    // Off: not read yet; NoSource: neither VRCX nor VR Gallery; Unreadable: the database did not open (VRCX writing that page)
    public enum State { Off, NoSource, Ok, Unreadable }

    public const int Keep = 8;                       // names kept of a visit / of the day (the friends first)

    public sealed class Visit
    {
        public string World = "", Location = "";     // World "": the log had no name for it
        public DateTime Start, End;                  // local; End: the read's time while you are still there
        public bool Here;                            // still there (VRChat runs, no world after it)
        public int People, Friends, Photos = -1;     // Photos -1: not counted (no picture folder)
        public List<string> Names = new();           // the people met there, the friends first
        public double Minutes => Math.Max(0, (End - Start).TotalMinutes);
        internal readonly List<(string key, string name, bool friend)> Met = new();
        internal readonly HashSet<string> Keys = new(StringComparer.Ordinal);
    }

    public sealed class Snapshot
    {
        public int Version;
        public State State;
        public string Source = "";                   // "VRCX", "VR Gallery"
        public string Why;                           // what failed (the log's)
        public DateTime Day;                         // the day (its 5 AM: Start(Day))
        public List<Visit> Visits = new();           // in order
        public int People, Friends, Photos = -1;     // the whole day's: everyone met once, the friends among them, the photos
        public List<string> Names = new();           // the day's people, the friends first
        public double Minutes;                       // the visits' stays together
        public bool Here => Visits.Count > 0 && Visits[^1].Here;
    }

    // the day's 5 AM
    public static DateTime Start(DateTime day) => day.AddHours(5);

    // The day 'now' is in, from VRCX's database (vrcxDb), else VR Gallery's (galleryDb), the photos from 'pictures'. meId: you
    // ("usr_..."; null: VRCX's account, else VR Gallery's most frequent player); vrchat: VRChat runs (the last visit may be going on).
    public static Snapshot Read(string vrcxDb, string galleryDb, string pictures, DateTime now, string meId, bool vrchat)
    {
        var day = PhotoWatcher.DayOf(now);
        DateTime from = Start(day), to = from.AddDays(1);
        var s = new Snapshot { Day = day };
        bool vrcx = !string.IsNullOrEmpty(vrcxDb) && File.Exists(vrcxDb), gallery = !string.IsNullOrEmpty(galleryDb) && File.Exists(galleryDb);
        bool done = false, failed = false;
        HashSet<string> friends = null;
        string me = string.IsNullOrEmpty(meId) ? null : meId;
        if (vrcx)
        {
            try { done = FromVrcx(s, vrcxDb, from, to, now, vrchat, ref me, out friends); }
            catch (IOException e) { failed = true; s.Why = "VRCX: " + e.Message; }
        }
        if (!done && gallery)
        {
            s.Visits.Clear();
            try { done = FromGallery(s, galleryDb, from, to, now, vrchat, me, friends); }
            catch (IOException e) { failed = true; s.Why = (s.Why != null ? s.Why + "; " : "") + "VR Gallery: " + e.Message; }
        }
        s.State = done ? State.Ok : failed ? State.Unreadable : State.NoSource;
        if (!done) s.Visits.Clear();
        else { Tidy(s); Sum(s); }
        CountPhotos(s, pictures, from, to);
        return s;
    }

    // ---- VRCX ----

    static bool FromVrcx(Snapshot s, string path, DateTime from, DateTime to, DateTime now, bool vrchat, ref string me, out HashSet<string> friends)
    {
        friends = null;
        var db = Vrcx.Open(path);
        try
        {
            string p = FriendsWatcher.Account(db, me);
            if (p != null)
            {
                friends = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in Vrcx.Query(db, $"SELECT user_id FROM \"{p}_friend_log_current\"", 1)) friends.Add(r[0]);
                me ??= MeOf(p);
            }
            if (!Has(db, "gamelog_location") || !Has(db, "gamelog_join_leave")) return false;      // an older VRCX: VR Gallery's then
            string f = Iso(from), t = Iso(to);
            var locs = Vrcx.Query(db, $"SELECT created_at, location, world_name, time FROM gamelog_location WHERE created_at >= '{f}' AND created_at < '{t}' ORDER BY created_at, id", 4);
            var after = Vrcx.Query(db, $"SELECT MIN(created_at) FROM gamelog_location WHERE created_at >= '{t}'", 1);
            DateTime? next0 = after.Count > 0 && Utc(after[0][0]) is var a0 && a0 != DateTime.MinValue ? a0 : null;
            // everyone's joins and leaves from the day's start to 12 hours past its end (the last visit may run on past 5 AM)
            var rows = locs.Count == 0 ? new List<string[]>()
                : Vrcx.Query(db, $"SELECT created_at, type, location, user_id, display_name FROM gamelog_join_leave WHERE created_at >= '{f}' AND created_at < '{Iso(to.AddHours(12))}' ORDER BY id", 5);
            // the visits: each arrival to its stay as VRCX wrote it when you left - else to the next arrival, to now (still there), or to
            // the last join or leave VRCX saw there (it was closed before you left)
            for (int i = 0; i < locs.Count; i++)
            {
                var l = locs[i];
                var start = Utc(l[0]);
                if (start == DateTime.MinValue) continue;
                DateTime? next = null;
                for (int j = i + 1; j < locs.Count && next == null; j++) { var n = Utc(locs[j][0]); if (n != DateTime.MinValue) next = n; }
                next ??= next0;
                long.TryParse(l[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms);
                var v = new Visit { World = l[2], Location = l[1], Start = start };
                if (ms > 0) v.End = start.AddMilliseconds(ms);
                else if (next != null) v.End = next.Value;
                else if (vrchat) { v.End = now; v.Here = true; }
                else v.End = LastSeen(rows, l[1], start);
                Clamp(v, next, now);
                s.Visits.Add(v);
            }
            foreach (var r in rows)
            {
                if (r[1] != "OnPlayerJoined") continue;
                if (VisitOf(s.Visits, r[2], Utc(r[0])) is { } v) Meet(v, r[3], r[4], me, friends);
            }
            s.Source = "VRCX";
            return true;
        }
        finally { Vrcx.Close(db); }
    }

    // the last join or leave in that instance since you came (none: you came, nothing more is known)
    static DateTime LastSeen(List<string[]> rows, string loc, DateTime start)
    {
        var last = start;
        foreach (var r in rows)
        {
            if (r[2] != loc) continue;
            var at = Utc(r[0]);
            if (at > last) last = at;
        }
        return last;
    }

    // the visit of an instance at that time (a few seconds' slack: everyone leaves just as you go); the latest one if it was twice
    static Visit VisitOf(List<Visit> visits, string loc, DateTime at)
    {
        for (int i = visits.Count - 1; i >= 0; i--)
        {
            var v = visits[i];
            if (v.Location == loc && at >= v.Start.AddSeconds(-2) && at <= v.End.AddSeconds(5)) return v;
        }
        return null;
    }

    // usr<32 hex> (VRCX's table prefix) -> usr_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
    static string MeOf(string prefix)
    {
        string hex = prefix.Length >= 35 ? prefix[3..35] : "";
        return hex.Length == 32 ? $"usr_{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}" : null;
    }

    // ---- VR Gallery ----

    static bool FromGallery(Snapshot s, string path, DateTime from, DateTime to, DateTime now, bool vrchat, string me, HashSet<string> friends)
    {
        var db = Vrcx.Open(path);
        try
        {
            if (!Has(db, "sessions") || !Has(db, "session_players")) return false;
            string f = LocalIso(from), t = LocalIso(to);
            var ss = Vrcx.Query(db, $"SELECT id, world_name, start_at, end_at, world_id FROM sessions WHERE start_at >= '{f}' AND start_at < '{t}' ORDER BY start_at, id", 5);
            var after = Vrcx.Query(db, $"SELECT MIN(start_at) FROM sessions WHERE start_at >= '{t}'", 1);
            DateTime? next0 = after.Count > 0 && Local(after[0][0]) is var a0 && a0 != DateTime.MinValue ? a0 : null;
            if (me == null)
            {
                // you are in every session of yours: the player in the most of them (4 in 5 at least) - when nothing said who you are
                var top = Vrcx.Query(db, "SELECT user_id, COUNT(DISTINCT session_id), (SELECT COUNT(*) FROM sessions) FROM session_players WHERE user_id <> '' " +
                                         "GROUP BY user_id ORDER BY 2 DESC LIMIT 1", 3);
                if (top.Count == 1 && int.TryParse(top[0][1], out int n) && int.TryParse(top[0][2], out int all) && all >= 3 && n * 5 >= all * 4) me = top[0][0];
            }
            // the sessions: to their end; the last one, VRChat running and its end recent (VR Gallery moves it on as the log grows), to now
            var byId = new Dictionary<string, Visit>(StringComparer.Ordinal);
            for (int i = 0; i < ss.Count; i++)
            {
                var r = ss[i];
                var start = Local(r[2]);
                if (start == DateTime.MinValue) continue;
                DateTime? next = i + 1 < ss.Count && Local(ss[i + 1][2]) is var n1 && n1 != DateTime.MinValue ? n1 : i + 1 < ss.Count ? null : next0;
                var end = Local(r[3]);
                var v = new Visit { World = r[1], Location = r[4].Length > 0 ? r[4] : r[1], Start = start };     // (no instance: its world)
                bool last = i == ss.Count - 1 && next == null;
                if (last && vrchat && (end == DateTime.MinValue || now - end < TimeSpan.FromMinutes(10))) { v.End = now; v.Here = true; }
                else v.End = end != DateTime.MinValue ? end : next ?? start;
                Clamp(v, next, now);
                s.Visits.Add(v);
                byId[r[0]] = v;
            }
            if (byId.Count > 0)
                foreach (var r in Vrcx.Query(db, $"SELECT p.session_id, p.user_id, p.name FROM session_players p JOIN sessions s ON s.id = p.session_id " +
                                                 $"WHERE s.start_at >= '{f}' AND s.start_at < '{t}' ORDER BY p.join_at", 3))
                    if (byId.TryGetValue(r[0], out var v)) Meet(v, r[1], r[2], me, friends);
            s.Source = "VR Gallery";
            return true;
        }
        finally { Vrcx.Close(db); }
    }

    // ---- both ----

    static void Clamp(Visit v, DateTime? next, DateTime now)
    {
        if (next != null && v.End > next.Value) v.End = next.Value;          // (a stay is up to the next arrival)
        if (v.End > now) v.End = now;
        if (v.End < v.Start) v.End = v.Start;
    }

    // a stop under a minute is left out (the one you are in now never); then a visit of the same instance as the one before it, beginning
    // within 2 minutes of its end, is that one going on (VRChat rejoining: a new row a moment later) - its people and its end added
    static void Tidy(Snapshot s)
    {
        s.Visits.RemoveAll(v => !v.Here && v.Minutes < 1);
        for (int i = s.Visits.Count - 1; i > 0; i--)
        {
            Visit a = s.Visits[i - 1], b = s.Visits[i];
            if (a.Location != b.Location || b.Start - a.End > TimeSpan.FromMinutes(2)) continue;
            if (b.End > a.End) a.End = b.End;
            a.Here |= b.Here;
            foreach (var m in b.Met) if (a.Keys.Add(m.key)) a.Met.Add(m);
            s.Visits.RemoveAt(i);
        }
    }

    // someone met in a visit (by their id; a row without one by their name), you yourself not
    static void Meet(Visit v, string id, string name, string me, HashSet<string> friends)
    {
        string key = id.Length > 0 ? id : name;
        if (key.Length == 0 || key == me || !v.Keys.Add(key)) return;
        v.Met.Add((key, name.Length > 0 ? name : id, id.Length > 0 && friends != null && friends.Contains(id)));
    }

    // each visit's people and the day's (everyone once), the friends first; the time in worlds
    static void Sum(Snapshot s)
    {
        var day = new List<(string key, string name, bool friend)>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in s.Visits)
        {
            v.People = v.Met.Count;
            Names(v.Met, v.Names, out v.Friends);
            foreach (var m in v.Met)
                if (!seen.TryGetValue(m.key, out int at)) { seen[m.key] = day.Count; day.Add(m); }
                else if (m.friend && !day[at].friend) day[at] = m;
            s.Minutes += v.Minutes;
        }
        s.People = day.Count;
        Names(day, s.Names, out s.Friends);
    }

    static void Names(List<(string key, string name, bool friend)> met, List<string> into, out int friends)
    {
        friends = 0;
        into.Clear();
        foreach (var m in met) if (m.friend) { friends++; if (into.Count < Keep) into.Add(m.name); }
        foreach (var m in met) if (!m.friend && into.Count < Keep) into.Add(m.name);
    }

    // VRChat's photos of the day (VRChat_<date>_<time>...png: the folder itself and its month folders of the day), and each visit's
    static void CountPhotos(Snapshot s, string folder, DateTime from, DateTime to)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        var dirs = new List<string> { folder };
        for (var m = new DateTime(from.Year, from.Month, 1); m < to; m = m.AddMonths(1))
        {
            string d = Path.Combine(folder, m.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            if (Directory.Exists(d)) dirs.Add(d);
        }
        s.Photos = 0;
        foreach (var v in s.Visits) v.Photos = 0;
        foreach (var d in dirs)
            foreach (var f in Directory.EnumerateFiles(d, "VRChat_*.png"))
            {
                var t = PhotoWatcher.TakenOf(f);
                if (t < from || t >= to) continue;
                s.Photos++;
                foreach (var v in s.Visits)
                    if (t >= v.Start && (t < v.End || v.Here && t <= v.End)) { v.Photos++; break; }
            }
    }

    static bool Has(IntPtr db, string table) =>
        Vrcx.Query(db, $"SELECT 1 FROM sqlite_master WHERE type='table' AND name='{table}'", 1).Count > 0;

    // VRCX's times: UTC, "2026-10-24T03:55:44.000Z"; VR Gallery's: local, "2026-10-24T05:55:44"
    static string Iso(DateTime local) => local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    static string LocalIso(DateTime local) => local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    static DateTime Utc(string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? DateTime.SpecifyKind(t.ToLocalTime(), DateTimeKind.Unspecified) : DateTime.MinValue;

    static DateTime Local(string s) =>
        !string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t)
            ? DateTime.SpecifyKind(t, DateTimeKind.Unspecified) : DateTime.MinValue;
}

// The Today side's reader: a thread of its own, awake only while the recap's opened view shows Today (else asleep, nothing read). Read
// at once when it is switched on, then looked at every 5 s: read again when a database or the photo folder changed (at most every 20
// s: VRCX writes its log all evening), when the day turned or VRChat came or went, or every minute while you are still in a world (its
// stay grows). The view gets the snapshot; a frame never reads anything.
public sealed class TodayWatcher : IDisposable
{
    public string VrcxDb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "VRCX.sqlite3");
    public string GalleryDb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRGallery", "vrgallery.db");
    public string Pictures;                                // the photos' folder (null: VRChat's, PhotoWatcher.PictureFolder)
    public volatile string MeId;                           // you, "usr_..." (the Room's; null: VRCX's account)

    const int Look = 5, Again = 20, Live = 60;             // s
    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    Thread _thread;
    volatile bool _stop, _active;
    TodayLog.Snapshot _pub = new();
    int _version;

    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            // switched on another day than the last read's: that day is not shown meanwhile (read in a moment)
            if (value && Get() is { State: not TodayLog.State.Off } was && was.Day != PhotoWatcher.DayOf(DateTime.Now)) Publish(new TodayLog.Snapshot());
            _active = value;
            if (value && _thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "today", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
            _wake.Set();
        }
    }

    public TodayLog.Snapshot Get() { lock (_lock) return _pub; }

    void Publish(TodayLog.Snapshot s)
    {
        s.Version = Interlocked.Increment(ref _version);
        lock (_lock) _pub = s;
    }

    void Run()
    {
        DateTime read = default;
        int stamp = 0;
        bool fresh = false, vrchat = false;                // fresh: read since it was switched on
        string pics = null;
        while (!_stop)
        {
            if (!_active) { fresh = false; _wake.WaitOne(1000); continue; }
            try
            {
                var now = DateTime.Now;
                if (!fresh) pics = Pictures ?? PhotoWatcher.PictureFolder();
                bool vr = VrchatRunning();
                int st = HashCode.Combine(Stamp(VrcxDb), Stamp(GalleryDb), Stamp(GalleryDb + "-wal"), Stamp(pics),
                    Stamp(pics == null ? null : Path.Combine(pics, now.ToString("yyyy-MM", CultureInfo.InvariantCulture))));
                double since = (now - read).TotalSeconds;
                var pub = Get();
                if (!fresh || PhotoWatcher.DayOf(now) != pub.Day || vr != vrchat || (st != stamp && since >= Again) || (pub.Here && since >= Live))
                {
                    var s = TodayLog.Read(VrcxDb, GalleryDb, pics, now, MeId, vr);
                    if (!fresh || s.State != pub.State || s.Source != pub.Source)
                        Log.Info($"today: {s.State} {s.Source}, {s.Visits.Count} world(s), {s.People} people, {s.Photos} photos{(s.Why != null ? " - " + s.Why : "")}");
                    // the database being written that very moment: what was shown stays, tried again soon
                    if (s.State == TodayLog.State.Unreadable && pub.State == TodayLog.State.Ok && pub.Day == s.Day) read = now.AddSeconds(Look - Again);
                    else { Publish(s); read = now; }
                    (stamp, vrchat, fresh) = (st, vr, true);
                }
            }
            catch (Exception e)
            {
                Log.Info("today: not read: " + e.Message);
                if (Get().State != TodayLog.State.Ok) Publish(new TodayLog.Snapshot { State = TodayLog.State.Unreadable, Day = PhotoWatcher.DayOf(DateTime.Now), Why = e.Message });
                (read, fresh) = (DateTime.Now, true);
            }
            _wake.WaitOne(Look * 1000);
        }
    }

    static long Stamp(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        try { return File.GetLastWriteTimeUtc(path).Ticks; }          // (a folder's too; one not there: Windows' 1601)
        catch { return 0; }
    }

    static bool VrchatRunning()
    {
        try
        {
            var ps = Process.GetProcessesByName("VRChat");
            foreach (var p in ps) p.Dispose();
            return ps.Length > 0;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(2000);
        // (_wake is left to the GC, as the friends' reader leaves its own: a read still running past the Join would come back to a
        // disposed handle in its WaitOne)
    }
}
