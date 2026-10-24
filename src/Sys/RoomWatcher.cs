using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Orbit;

// Who is in your VRChat instance: VRChat's own log (output_log_*.txt in LocalLow\VRChat\VRChat), followed on a
// background thread. Friends come from VRCX's database when VRCX is installed (read-only, never locked: SQLite's
// "immutable" mode, through Windows' own winsqlite3.dll). Starred people are Orbit's own list (RoomConfig.Starred).
// Arriving in a world logs everyone already there ~15 s later in one burst: those become ONE "arrived" event, not one
// card per person; leaving a world logs everyone leaving: that is no event at all.
public sealed class RoomWatcher : IDisposable
{
    public sealed class Person
    {
        public string Id = "", Name = "";
        public DateTime Joined, Left;
        public bool Friend;
        public string Key => Id.Length > 0 ? Id : Name;
        public Person Copy() => (Person)MemberwiseClone();
    }

    public sealed class Snapshot
    {
        public int Version;
        public bool LogFound, InRoom, Arriving, VrcxFound;
        public string World = "", MeId = "";
        public DateTime Since;
        public int Friends;                          // in VRCX
        public List<Person> Here = new();            // joined order
        public List<Person> Gone = new();            // left this instance, newest first (at most 12)
    }

    public enum Kind { Arrived, Joined, Left }
    public readonly record struct RoomEvent(Kind Kind, Person[] People, string World);
    public readonly ConcurrentQueue<RoomEvent> Events = new();

    public string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "VRChat", "VRChat");
    public string VrcxDb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "VRCX.sqlite3");

    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    Thread _thread;
    volatile bool _stop, _active;
    Snapshot _pub = new();

    // the state, owned by the worker thread
    readonly List<Person> _here = new(), _gone = new();
    string _world = "", _me = "", _myName = "";
    DateTime _since;
    bool _inRoom, _leavingRoom, _arriving, _anyJoin, _logFound;
    double _arriveStart, _lastJoin;
    HashSet<string> _friends = new();
    bool _vrcx;
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
                _thread = new Thread(Run) { IsBackground = true, Name = "vrchat room" };
                _thread.Start();
            }
            _wake.Set();
        }
    }

    public Snapshot Get() { lock (_lock) return _pub; }
    public int Version { get { lock (_lock) return _pub.Version; } }

    void Publish()
    {
        var s = new Snapshot
        {
            Version = ++_version, LogFound = _logFound, InRoom = _inRoom, Arriving = _arriving, VrcxFound = _vrcx, World = _world,
            MeId = _me, Since = _since, Friends = _friends.Count,
        };
        foreach (var p in _here) s.Here.Add(p.Copy());
        foreach (var p in _gone) s.Gone.Add(p.Copy());
        lock (_lock) _pub = s;
    }

    // ---- the worker ----
    string _file;
    FileStream _fs;                                      // the followed log, kept open
    readonly byte[] _buf = new byte[1 << 16];
    readonly char[] _chars = new char[Encoding.UTF8.GetMaxCharCount(1 << 16)];
    long _pos;
    // the line being read, in a buffer of its own: only a line the watcher reads ([Behaviour] ..., VRCApplication: ...) becomes a
    // string (Erik 10-24: every line of VRChat's log was one - 17 MB a second while it caught up on a 49 MB log, the log growing ~17 MB
    // an hour in a long session); a line longer than LineMax (VRChat logs huge ones now and then) is skipped, the buffer stays small
    char[] _line = new char[512];
    int _lineLen;
    bool _overlong;
    const int LineMax = 8192;
    Decoder _dec = Encoding.UTF8.GetDecoder();
    double _nextFileCheck, _nextFriendCheck;
    DateTime _friendStamp;

    void Run()
    {
        while (!_stop)
        {
            if (!_active) { _wake.WaitOne(1000); continue; }
            double now = Clock.Now;
            bool changed = false;
            try
            {
                if (now >= _nextFriendCheck) { _nextFriendCheck = now + 300; changed |= RefreshFriends(); }
                if (now >= _nextFileCheck) { _nextFileCheck = now + 5; changed |= CheckFile(); }
                changed |= ReadNew(now);
                changed |= CheckArrival(now);
            }
            catch (Exception e)
            {
                Log.Info("room: " + e.Message);
                _fs?.Dispose();
                _fs = null;
                _file = null;
                _nextFileCheck = now + 10;
            }
            if (changed) Publish();
            _wake.WaitOne(400);
        }
    }

    // the newest log (VRChat starts a new one per launch); a new one = start over from its last room
    bool CheckFile()
    {
        string newest = null;
        DateTime t = DateTime.MinValue;
        if (Directory.Exists(LogDir))
            foreach (var f in Directory.EnumerateFiles(LogDir, "output_log_*.txt"))
            {
                var w = File.GetLastWriteTimeUtc(f);
                if (w > t) { t = w; newest = f; }
            }
        bool found = newest != null;
        if (newest == _file && (_fs != null || newest == null)) { if (found != _logFound) { _logFound = found; return true; } return false; }
        _fs?.Dispose();
        _fs = null;
        _file = newest;
        _logFound = found;
        ResetRoom();
        _inRoom = false;
        _world = "";
        _lineLen = 0; _overlong = false;
        _dec = Encoding.UTF8.GetDecoder();
        if (newest == null) return true;
        Log.Info("room: following " + Path.GetFileName(newest));
        var fs = _fs = Open(newest);
        FindMe(fs);
        // the history since the last room entered: the state only, no notifications
        long start = LastIndexOf(fs, Encoding.UTF8.GetBytes("[Behaviour] Entering Room: "));
        if (start >= 0)
        {
            start = LineStart(fs, start);
            fs.Position = start;
            ReadLines(fs, false, Clock.Now);
        }
        _pos = fs.Length;
        _lineLen = 0; _overlong = false;
        _dec = Encoding.UTF8.GetDecoder();
        return true;
    }

    static FileStream Open(string f) => new(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);

    bool ReadNew(double now)
    {
        if (_fs == null) return false;
        var fs = _fs;
        if (fs.Length < _pos) { _pos = 0; _lineLen = 0; _overlong = false; _dec = Encoding.UTF8.GetDecoder(); }   // rewritten: read it again
        if (fs.Length == _pos) return false;
        fs.Position = _pos;
        bool any = ReadLines(fs, true, now);
        _pos = fs.Position;
        return any;
    }

    bool ReadLines(FileStream fs, bool live, double now)
    {
        bool any = false;
        int n;
        while ((n = fs.Read(_buf, 0, _buf.Length)) > 0)
        {
            int c = _dec.GetChars(_buf, 0, n, _chars, 0);
            for (int i = 0; i < c; i++)
            {
                char ch = _chars[i];
                if (ch != '\n')
                {
                    if (_overlong) continue;
                    if (_lineLen == LineMax) { _overlong = true; continue; }
                    if (_lineLen == _line.Length) Array.Resize(ref _line, Math.Min(LineMax, _line.Length * 2));
                    _line[_lineLen++] = ch;
                    continue;
                }
                ReadOnlySpan<char> span = _line.AsSpan(0, _lineLen).TrimEnd('\r');
                bool skip = _overlong;
                _lineLen = 0;
                _overlong = false;
                if (skip || (span.IndexOf("[Behaviour] ", StringComparison.Ordinal) < 0 && span.IndexOf("VRCApplication: ", StringComparison.Ordinal) < 0)) continue;
                any |= Line(new string(span), live, now);
            }
        }
        return any;
    }

    // 2026.10.13 16:23:29 Debug      -  [Behaviour] OnPlayerJoined Name (usr_...)
    bool Line(string line, bool live, double now)
    {
        if (line.Length < 24 || !char.IsDigit(line[0]) || line[4] != '.') return false;
        if (line.Contains("VRCApplication: HandleApplicationQuit", StringComparison.Ordinal) ||
            line.Contains("VRCApplication: OnApplicationQuit", StringComparison.Ordinal))
        {
            // VRChat closed: it writes no leaves for anyone; the room is simply over (no events)
            _here.Clear();
            _inRoom = false;
            _arriving = false;
            _world = "";
            return true;
        }
        int b = line.IndexOf("[Behaviour] ", StringComparison.Ordinal);
        if (b < 0 || b > 48) return false;
        string m = line[(b + 12)..];
        DateTime ts = line.Length >= 19 && DateTime.TryParseExact(line[..19], "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var d) ? d : DateTime.Now;
        if (m.StartsWith("Entering Room: ", StringComparison.Ordinal))
        {
            ResetRoom();
            _world = m[15..].Trim();
            _since = ts;
            _inRoom = true;
            _arriving = live;
            _arriveStart = _lastJoin = now;
            return true;
        }
        if (m == "OnLeftRoom")
        {
            _leavingRoom = true;                     // everyone "leaves" now: not news
            _inRoom = false;
            foreach (var p in _here) p.Left = ts;
            _here.Clear();
            _arriving = false;
            return true;
        }
        if (m.StartsWith("OnPlayerJoined ", StringComparison.Ordinal))
        {
            var (name, id) = Split(m[15..]);
            _anyJoin = true;
            _lastJoin = now;
            if (IsMe(name, id) || _here.Exists(p => p.Key == (id.Length > 0 ? id : name))) return false;
            var person = new Person { Id = id, Name = name, Joined = ts, Friend = id.Length > 0 && _friends.Contains(id) };
            _here.Add(person);
            _gone.RemoveAll(p => p.Key == person.Key);
            if (live && !_arriving) Events.Enqueue(new RoomEvent(Kind.Joined, new[] { person.Copy() }, _world));
            return true;
        }
        if (m.StartsWith("OnPlayerLeft ", StringComparison.Ordinal))            // not OnPlayerLeftRoom (no space)
        {
            if (_leavingRoom) return false;
            var (name, id) = Split(m[13..]);
            string key = id.Length > 0 ? id : name;
            int i = _here.FindIndex(p => p.Key == key);
            if (i < 0) return false;
            var person = _here[i];
            _here.RemoveAt(i);
            person.Left = ts;
            _gone.Insert(0, person);
            if (_gone.Count > 12) _gone.RemoveAt(_gone.Count - 1);
            if (live && !_arriving) Events.Enqueue(new RoomEvent(Kind.Left, new[] { person.Copy() }, _world));
            return true;
        }
        return false;
    }

    // the people already there are logged in one burst after arriving; once it is over: one "arrived" event
    bool CheckArrival(double now)
    {
        if (!_arriving) return false;
        if (now - _arriveStart < 180 && (!_anyJoin || now - _lastJoin < 3)) return false;
        _arriving = false;
        Events.Enqueue(new RoomEvent(Kind.Arrived, _here.ConvertAll(p => p.Copy()).ToArray(), _world));
        return true;
    }

    void ResetRoom()
    {
        _here.Clear();
        _gone.Clear();
        _leavingRoom = false;
        _arriving = false;
        _anyJoin = false;
    }

    bool IsMe(string name, string id) => id.Length > 0 ? id == _me : _myName.Length > 0 && name == _myName;

    // "Name (usr_...)": the id from the LAST " (usr_" (a name may have brackets of its own)
    static (string name, string id) Split(string s)
    {
        s = s.Trim();
        int i = s.LastIndexOf(" (usr_", StringComparison.Ordinal);
        if (i > 0 && s.EndsWith(')')) return (s[..i], s[(i + 2)..^1]);
        return (s, "");
    }

    // "User Authenticated: Name (usr_...)" near the start of the log
    // (10-24: looked for in the bytes - only the line found becomes text; the whole 4 MB head was a 12 MB string at every start)
    void FindMe(FileStream fs)
    {
        fs.Position = 0;
        var buf = new byte[Math.Min(fs.Length, 4 << 20)];
        int n = fs.Read(buf, 0, buf.Length);
        var head = buf.AsSpan(0, n);
        foreach (var key in new[] { "post login checks for ", "User Authenticated: " })
        {
            int i = head.IndexOf(Encoding.UTF8.GetBytes(key));
            if (i < 0) continue;
            var rest = head[(i + Encoding.UTF8.GetByteCount(key))..];
            int e = rest.IndexOf((byte)'\n');
            var (name, id) = Split(Encoding.UTF8.GetString(e < 0 ? rest : rest[..e]).TrimEnd('\r'));
            _myName = name;
            if (id.Length > 0) _me = id;
            return;
        }
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

    static long LineStart(FileStream fs, long pos)
    {
        long s = Math.Max(0, pos - 512);
        var buf = new byte[pos - s];
        fs.Position = s;
        int n = fs.Read(buf, 0, buf.Length);
        for (int i = n - 1; i >= 0; i--) if (buf[i] == (byte)'\n') return s + i + 1;
        return s;
    }

    // ---- VRCX: friends (and who you are) ----
    bool RefreshFriends()
    {
        if (!File.Exists(VrcxDb)) { bool was = _vrcx; _vrcx = false; return was; }
        var stamp = File.GetLastWriteTimeUtc(VrcxDb);
        if (_vrcx && stamp == _friendStamp) return false;
        try
        {
            var (me, friends) = Vrcx.Friends(VrcxDb);
            _friendStamp = stamp;
            _friends = friends;
            if (me.Length > 0) _me = me;
            _vrcx = true;
            foreach (var p in _here) p.Friend = p.Id.Length > 0 && _friends.Contains(p.Id);
            Log.Info($"room: VRCX {friends.Count} friends");
            return true;
        }
        catch (Exception e)
        {
            Log.Info("room: VRCX not readable: " + e.Message);
            return false;
        }
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(2000);
        _fs?.Dispose();
        // (_wake is left to the GC, as AvatarLink's and LumosLink's are: a step still running past the Join - VRCX's database being read -
        // would come back to a disposed handle in its WaitOne and throw on a thread with no handler: the process gone mid-exit)
    }
}

// VRCX's database through Windows' built-in SQLite (System32\winsqlite3.dll): read-only and "immutable", so it never
// takes a lock VRCX could trip over. The friend list is the one usr<hex>_friend_log_current table (hex = your id).
// (The friends widget reads its feed the same way: Open / Query / Close, Sys/Friends.cs.)
static class Vrcx
{
    const string Dll = "winsqlite3.dll";
    const int OpenReadOnly = 0x1, OpenUri = 0x40, Row = 100;

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_open_v2(byte[] name, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr stmt, IntPtr tail);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_column_bytes(IntPtr stmt, int col);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_finalize(IntPtr stmt);

    static byte[] Z(string s) { var b = Encoding.UTF8.GetBytes(s + "\0"); return b; }

    // the database read-only and immutable (a file: URI, so the flag is SQLite's own); Close when done
    internal static IntPtr Open(string path)
    {
        string uri = new Uri(path).AbsoluteUri + "?immutable=1";
        if (sqlite3_open_v2(Z(uri), out var db, OpenReadOnly | OpenUri, IntPtr.Zero) != 0)
        {
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
            throw new IOException("sqlite open failed");
        }
        return db;
    }

    internal static void Close(IntPtr db) => sqlite3_close_v2(db);

    internal static List<string[]> Query(IntPtr db, string sql, int cols)
    {
        var rows = new List<string[]>();
        if (sqlite3_prepare_v2(db, Z(sql), -1, out var st, IntPtr.Zero) != 0) throw new IOException("sqlite prepare failed");
        try
        {
            while (sqlite3_step(st) == Row)
            {
                var r = new string[cols];
                for (int c = 0; c < cols; c++)
                {
                    var p = sqlite3_column_text(st, c);
                    r[c] = p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p, sqlite3_column_bytes(st, c));
                }
                rows.Add(r);
            }
        }
        finally { sqlite3_finalize(st); }
        return rows;
    }

    public static (string me, HashSet<string> friends) Friends(string path)
    {
        var db = Open(path);
        try
        {
            var tables = Query(db, "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'usr%\\_friend\\_log\\_current' ESCAPE '\\'", 1);
            var friends = new HashSet<string>();
            if (tables.Count != 1) return ("", friends);
            string t = tables[0][0];
            foreach (var r in Query(db, $"SELECT user_id FROM \"{t.Replace("\"", "")}\"", 1))
                if (r[0].StartsWith("usr_", StringComparison.Ordinal)) friends.Add(r[0]);
            // usr<32 hex>_friend_log_current -> usr_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
            string hex = t.Length >= 35 ? t[3..35] : "";
            string me = hex.Length == 32 ? $"usr_{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}" : "";
            return (me, friends);
        }
        finally { sqlite3_close_v2(db); }
    }
}
