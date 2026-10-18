using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Orbit;

// Friends online (the watch's "friends" widget, WristFriends.cs): how many of your VRChat friends are online and where, from VRCX's
// database - read only and never locked (Vrcx.Open: SQLite's immutable mode through Windows' winsqlite3.dll, as the room's friend
// list), never its cookies, configs or anything of a login: only the friend list and the feed tables below.
// VRCX keeps no "online now" column. Its feed logs the changes it SAW while it ran: usr<id>_feed_online_offline (Online / Offline, the
// location then), usr<id>_feed_gps (a friend moving to another instance, with the world's name), usr<id>_feed_avatar / _feed_status
// (something they changed) - and nothing at its own start for the friends already online then. So a friend counts as online when,
// since VRCX started (its process's start time), their last Online / Offline was Online, or they moved / changed avatar or status
// after their last Offline. Not seen: a friend online since before VRCX started who has done nothing since. VRCX closed: nobody is
// known as online (the widget says so). Where: their newest instance since then - the world, its kind (Public, Friends+, Friends,
// Invite+, Invite, Group; a private one has no world name), since when. The friends in your own instance are added on the watch (the
// Room's data: Merge). Read on a thread of its own, at most every 30 s and only when the file changed, while a friends widget is on
// a page (never on a frame). Nothing is written anywhere.
public sealed class FriendsWatcher : IDisposable
{
    // where a friend is: with you (the Room's data), an instance of VRChat's kinds, private, traveling, not known
    public enum Where { Unknown, Here, Public, FriendsPlus, Friends, InvitePlus, Invite, Group, GroupPlus, GroupPublic, Private, Traveling }

    // Off: not read yet; NoVrcx: no database; Closed: VRCX is not running (nothing it says is live); Unreadable: the database did not open
    public enum State { Off, NoVrcx, Closed, Ok, Unreadable }

    public sealed class Friend
    {
        public string Id = "", Name = "", World = "";      // World: the world's name ("": a private instance, traveling, not known)
        public Where Kind;
        public DateTime Since;                             // local time: since when they are there (the change that put them there)
        public bool Favorite;                              // one of VRCX's favourites
        public bool Star;                                  // the watch's list (Merge): starred in Orbit (the Room's stars) or a favourite
        public Friend Copy() => (Friend)MemberwiseClone();
    }

    public sealed class Snapshot
    {
        public int Version;
        public State State;
        public DateTime VrcxSince;                         // local: VRCX running since (what it can know of)
        public int Total;                                  // friends in VRCX
        public List<Friend> Online = new();                // in the database's order (Merge sorts)
    }

    public string VrcxDb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "VRCX.sqlite3");
    public volatile string MeId;                           // the account to read (the room's "usr_..."; null: the only one, else the newest)

    const int Every = 30;                                  // s between two looks
    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    Thread _thread;
    volatile bool _stop, _active;
    Snapshot _pub = new();
    int _version;

    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            _active = value;
            if (value && _thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "vrcx friends" };
                _thread.Start();
            }
            _wake.Set();
        }
    }

    public Snapshot Get() { lock (_lock) return _pub; }

    void Publish(Snapshot s)
    {
        s.Version = ++_version;
        lock (_lock) _pub = s;
    }

    // ---- the worker ----

    void Run()
    {
        DateTime stamp = default, since = default;
        string me = null;
        bool warned = false;
        while (!_stop)
        {
            if (!_active) { _wake.WaitOne(1000); continue; }
            try
            {
                if (!File.Exists(VrcxDb))
                {
                    if (_pub.State != State.NoVrcx) Publish(new Snapshot { State = State.NoVrcx });
                    stamp = default;
                }
                else if (VrcxStarted() is not { } started)
                {
                    if (_pub.State != State.Closed) Publish(new Snapshot { State = State.Closed });
                    stamp = default;
                }
                else
                {
                    var st = File.GetLastWriteTimeUtc(VrcxDb);
                    string m = MeId;
                    if (st != stamp || started != since || m != me || _pub.State != State.Ok)
                    {
                        var s = Read(VrcxDb, started, m);
                        (stamp, since, me) = (st, started, m);
                        if (!warned || _pub.State != State.Ok) Log.Info($"friends: VRCX since {started.ToLocalTime():HH:mm}, {s.Online.Count} of {s.Total} online");
                        warned = true;
                        Publish(s);
                    }
                }
            }
            catch (Exception e)
            {
                // VRCX writing that very page: the next look reads it whole (what was shown stays)
                Log.Info("friends: VRCX not readable: " + e.Message);
                if (_pub.State != State.Ok) Publish(new Snapshot { State = State.Unreadable });
                stamp = default;
            }
            _wake.WaitOne(Every * 1000);
        }
    }

    // VRCX's start (UTC; null: not running) - its feed is live only from then
    static DateTime? VrcxStarted()
    {
        Process[] ps;
        try { ps = Process.GetProcessesByName("VRCX"); }
        catch { return null; }
        DateTime? first = null;
        foreach (var p in ps)
        {
            try { var t = p.StartTime.ToUniversalTime(); if (first == null || t < first) first = t; }
            catch { first ??= DateTime.UtcNow.AddHours(-12); }         // running, its start not readable: the last 12 hours
            finally { p.Dispose(); }
        }
        return first;
    }

    // ---- the database ----

    // Everyone online now as far as VRCX knows, from its feed since sinceUtc (VRCX's start). meId: the account ("usr_..."; null: the
    // only one, else the one with the newest feed).
    public static Snapshot Read(string path, DateTime sinceUtc, string meId = null)
    {
        var s = new Snapshot { State = State.Ok, VrcxSince = sinceUtc.ToLocalTime() };
        var db = Vrcx.Open(path);
        try
        {
            string p = Account(db, meId);
            if (p == null) return s;                                   // VRCX never logged in: no friends
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in Vrcx.Query(db, $"SELECT user_id, display_name FROM \"{p}_friend_log_current\"", 2))
                if (r[0].StartsWith("usr_", StringComparison.Ordinal)) names[r[0]] = r[1];
            s.Total = names.Count;
            string since = sinceUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            // each friend's newest Online / Offline, newest instance, newest change of avatar or status - since VRCX started
            var onOff = new Dictionary<string, (bool on, DateTime at, string loc, string world)>(StringComparer.Ordinal);
            foreach (var r in Newest(db, p + "_feed_online_offline", "type, created_at, location, world_name", since, 4))
                onOff[r[0]] = (r[1] == "Online", Utc(r[2]), r[3], r[4]);
            var gps = new Dictionary<string, (DateTime at, string loc, string world)>(StringComparer.Ordinal);
            foreach (var r in Newest(db, p + "_feed_gps", "created_at, location, world_name", since, 3))
                gps[r[0]] = (Utc(r[1]), r[2], r[3]);
            var did = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (string t in new[] { p + "_feed_avatar", p + "_feed_status" })
                foreach (var r in TryQuery(db, $"SELECT user_id, MAX(created_at) FROM \"{t}\" WHERE created_at >= '{since}' GROUP BY user_id", 2))
                {
                    var at = Utc(r[1]);
                    if (!did.TryGetValue(r[0], out var was) || at > was) did[r[0]] = at;
                }
            var fav = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in TryQuery(db, "SELECT user_id FROM favorite_friend", 1)) fav.Add(r[0]);
            foreach (var (id, name) in names)
            {
                bool hasOo = onOff.TryGetValue(id, out var oo), hasGps = gps.TryGetValue(id, out var g), hasDid = did.TryGetValue(id, out var act);
                DateTime off = hasOo && !oo.on ? oo.at : DateTime.MinValue;
                DateTime moved = hasGps ? g.at : DateTime.MinValue, changed = hasDid ? act : DateTime.MinValue;
                bool on = (hasOo && oo.on) || (moved > off) || (changed > off);
                if (!on) continue;
                // where: the newest of the Online (its location then) and the move; a change of avatar alone says nothing of where
                string loc = null, world = "";
                DateTime at;
                if (hasGps && (!hasOo || !oo.on || g.at >= oo.at)) (loc, world, at) = (g.loc, g.world, g.at);
                else if (hasOo && oo.on) (loc, world, at) = (oo.loc, oo.world, oo.at);
                else at = changed;
                var kind = Instance(loc);
                s.Online.Add(new Friend
                {
                    Id = id, Name = name, Kind = kind, Since = at.ToLocalTime(), Favorite = fav.Contains(id),
                    World = kind is Where.Private or Where.Traveling or Where.Unknown ? "" : world ?? "",
                });
            }
        }
        finally { Vrcx.Close(db); }
        return s;
    }

    // the newest row of each friend in a feed table since then (its id first)
    static List<string[]> Newest(IntPtr db, string table, string cols, string since, int n) =>
        TryQuery(db, $"SELECT user_id, {cols} FROM \"{table}\" WHERE id IN (SELECT MAX(id) FROM \"{table}\" WHERE created_at >= '{since}' GROUP BY user_id)", n + 1);

    // a table an older / newer VRCX does not have: nothing from it
    static List<string[]> TryQuery(IntPtr db, string sql, int cols)
    {
        try { return Vrcx.Query(db, sql, cols); }
        catch (IOException) { return new List<string[]>(); }
    }

    // the account's table prefix "usr<32 hex>": the one asked for, the only one, or the one whose feed is the newest (null: none)
    static string Account(IntPtr db, string meId)
    {
        var all = new List<string>();
        foreach (var r in Vrcx.Query(db, "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'usr%\\_friend\\_log\\_current' ESCAPE '\\'", 1))
        {
            string t = r[0];
            if (t.Length != 35 + 19) continue;
            bool hex = true;
            for (int i = 3; i < 35; i++) hex &= Uri.IsHexDigit(t[i]) && !char.IsUpper(t[i]);
            if (hex) all.Add(t[..35]);
        }
        if (all.Count == 0) return null;
        if (!string.IsNullOrEmpty(meId))
        {
            string want = "usr" + meId.Replace("usr_", "").Replace("-", "");
            if (all.Contains(want)) return want;
        }
        if (all.Count == 1) return all[0];
        string best = all[0], newest = "";
        foreach (var a in all)
            foreach (var r in TryQuery(db, $"SELECT MAX(created_at) FROM \"{a}_feed_online_offline\"", 1))
                if (string.CompareOrdinal(r[0], newest) > 0) (best, newest) = (a, r[0]);
        return best;
    }

    static DateTime Utc(string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;

    // VRChat's location: "wrld_...:12345~hidden(usr_...)~region(eu)" - the kind from its tags; "private", "traveling", "offline"
    public static Where Instance(string loc)
    {
        if (string.IsNullOrEmpty(loc) || loc == "offline") return Where.Unknown;
        if (loc == "private") return Where.Private;
        if (loc.StartsWith("traveling", StringComparison.Ordinal)) return Where.Traveling;
        if (!loc.StartsWith("wrld_", StringComparison.Ordinal)) return Where.Unknown;
        if (loc.Contains("~private(", StringComparison.Ordinal)) return loc.Contains("~canRequestInvite", StringComparison.Ordinal) ? Where.InvitePlus : Where.Invite;
        if (loc.Contains("~friends(", StringComparison.Ordinal)) return Where.Friends;
        if (loc.Contains("~hidden(", StringComparison.Ordinal)) return Where.FriendsPlus;
        if (loc.Contains("~group(", StringComparison.Ordinal))
            return loc.Contains("~groupAccessType(public)", StringComparison.Ordinal) ? Where.GroupPublic
                : loc.Contains("~groupAccessType(plus)", StringComparison.Ordinal) ? Where.GroupPlus : Where.Group;
        return Where.Public;
    }

    // ---- the watch's list ----

    // The online friends and the friends in your own instance (the Room's data: online whatever VRCX saw, "here"), the starred first
    // (Orbit's stars - the Room panel's - and VRCX's favourites), then by where (with you, the worlds A-Z, private, traveling, not
    // known), then by name. Made only when one of them changed (the watch keeps it).
    public static void Merge(List<Friend> into, Snapshot s, RoomWatcher.Snapshot room, List<string> starred)
    {
        into.Clear();
        if (s != null)
            foreach (var f in s.Online)
            {
                var c = f.Copy();
                c.Star = f.Favorite || (starred != null && (starred.Contains(f.Id) || starred.Contains(f.Name)));
                into.Add(c);
            }
        if (room != null && room.InRoom)
            foreach (var p in room.Here)
            {
                if (!p.Friend) continue;
                var f = into.Find(x => x.Id.Length > 0 && x.Id == p.Id);
                if (f == null)
                {
                    f = new Friend { Id = p.Id, Name = p.Name };
                    f.Star = starred != null && starred.Contains(p.Key);
                    into.Add(f);
                }
                (f.Kind, f.World, f.Since) = (Where.Here, room.World, p.Joined);
            }
        into.Sort(Order);
    }

    static readonly Comparison<Friend> Order = (a, b) =>
    {
        if (a.Star != b.Star) return a.Star ? -1 : 1;
        int ra = Rank(a.Kind), rb = Rank(b.Kind);
        if (ra != rb) return ra.CompareTo(rb);
        int w = string.Compare(a.World, b.World, StringComparison.CurrentCultureIgnoreCase);
        if (w != 0) return w;
        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    };

    static int Rank(Where k) => k switch { Where.Here => 0, Where.Private => 2, Where.Traveling => 3, Where.Unknown => 4, _ => 1 };

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(2000);
        // (_wake is left to the GC, as AvatarLink's and LumosLink's are: a step still running past the Join - VRCX's feed being queried -
        // would come back to a disposed handle in its WaitOne and throw on a thread with no handler: the process gone mid-exit)
    }
}
