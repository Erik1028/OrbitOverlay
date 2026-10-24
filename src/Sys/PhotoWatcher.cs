using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Orbit;

// The latest photo (the watch's "Latest photo" widget, WristPhoto.cs): VRChat's screenshots - Pictures\VRChat\<yyyy-MM>\VRChat_<date>_
// <time>_<res>.png, or the folder VRChat's own config.json names (picture_output_folder) - followed on a background thread: a
// FileSystemWatcher on the folder (its month folders too) for new .png files, a look at the folders every 4 s besides (a watcher can
// miss events), and the newest photos at the start. A new file is read only once it is whole: quiet for a second, its size and time
// the same twice, PNG's end chunk last (VRChat writes it, then VRCX writes it again with its metadata: a known photo that changes
// has its metadata read again; its picture stays). Read with every share allowed and closed at once - VRCX's own rewrite is never
// kept out. Each photo is a thumbnail of at most 640 x 400 (premultiplied BGRA; the full picture is never kept), its world and the
// people in it from the JSON VRCX (or VRChat itself) puts in a PNG text chunk. The list: the newest photo's day (5 AM to 5 AM: a
// night past midnight is one day), at most 20, newest first - the newest's thumbnail made at once, the others' when the opened view
// steps to them (Want), at most 6 kept. Nothing runs while Active is false (no widget on any page): no watcher, no thread work, no
// thumbnails held.
// (On this day's photos - Sys/PhotoMemories.cs - are the same Photo / Snapshot, with the reading and the thumbnails shared from here.)
public sealed class PhotoWatcher : IPhotoSource, IDisposable
{
    public const int MaxPhotos = 20, MaxThumbs = 6, ThumbW = 640, ThumbH = 400;

    // A photo as the watch shows it. Never changed once published: a change (its metadata, its thumbnail) is a new one in the next
    // snapshot (the thumbnail's pixels are shared between the two: the watch's bitmap of them stays).
    public sealed class Photo
    {
        public string Path = "", Name = "";
        public DateTime Taken;                   // from the name (VRChat writes it there), else the file's time
        public int Width, Height;                // the picture's own size (from the name; the file's once read; 0: unknown)
        public string World;                     // null: not known (no metadata)
        public string[] Players = Array.Empty<string>();
        public uint[] Thumb;                     // ThumbW x ThumbH at most, premultiplied BGRA; null: not made (yet)
        public int TW, TH;
        public bool Fresh;                       // taken while watching (not one found at the start): the widget pops
        public DateTime Seen;                    // when it came in
        public int Years;                        // On this day's: how many years ago (0: the latest photo's)
        public bool OtherDay;                    // On this day's: of the nearest day that had photos, not of this very date
        public Photo Copy() => (Photo)MemberwiseClone();
    }

    public sealed class Snapshot
    {
        public int Version;
        public bool Ready;                       // the folder was looked at (before: no answer yet, not "no photos")
        public bool FolderFound;
        public string Folder = "";
        public List<Photo> Recent = new();       // newest first
        public IPhotoSource Source;              // where to ask for a thumbnail (null: made-up photos, every one has its own)
    }

    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    Thread _thread;
    volatile bool _stop, _active, _recheck;
    Snapshot _pub = new();
    int _version;

    // --snapshot's own folder of made-up pictures (null: VRChat's)
    public string FolderOverride;

    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            _active = value;
            if (value && _thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "vrchat photos", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
            _wake.Set();
        }
    }

    public Snapshot Get() { lock (_lock) return _pub; }
    public int Version { get { lock (_lock) return _pub.Version; } }

    // the opened view steps to a photo: its thumbnail, when it has none (the worker makes it; the snapshot after says so)
    string _want;
    public void Want(string path)
    {
        if (path == null || path == Volatile.Read(ref _want)) return;
        Volatile.Write(ref _want, path);
        _wake.Set();
    }

    // a photo was deleted (the watch's Delete): the folder looked at now - the photo dropped at once, not at the next poll
    public void Recheck() { _recheck = true; _wake.Set(); }

    // ---- the worker ----

    sealed class Pending { public double Last; public (long, DateTime) Stamp; public int Tries; }

    readonly List<Photo> _list = new();                   // newest first
    readonly Dictionary<string, (long len, DateTime at)> _stamps = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _events = new();                // from the watcher's thread
    readonly object _evLock = new();
    readonly List<string> _used = new();                  // the thumbnails, the least recently wanted first
    readonly Dictionary<string, DateTime> _dirTimes = new(StringComparer.OrdinalIgnoreCase);
    FileSystemWatcher _fsw;
    string _root;
    bool _started, _found;
    DateTime _day;                                        // the list's day
    double _nextPoll;
    const double Quiet = 1.0;                             // s without a change before a new file is read

    void Run()
    {
        while (!_stop)
        {
            if (!_active)
            {
                if (_started) Sleep();
                _wake.WaitOne();
                continue;
            }
            double now = Clock.Now;
            bool changed = false;
            try
            {
                if (!_started) { Start(now); changed = true; }
                changed |= TakeEvents(now);
                if (now >= _nextPoll || _recheck) { _recheck = false; _nextPoll = now + 4; changed |= Poll(now); }
                changed |= Settle(now);
                changed |= LoadWanted();
            }
            catch (Exception e)
            {
                Log.Info("photos: " + e.Message);
                _nextPoll = now + 10;
            }
            if (changed) Publish();
            _wake.WaitOne(_pending.Count > 0 ? 250 : 1000);
        }
        StopWatching();
    }

    void Publish()
    {
        var s = new Snapshot { Version = ++_version, Ready = _started, FolderFound = _found, Folder = _root ?? "", Source = this };
        s.Recent.AddRange(_list);
        lock (_lock) _pub = s;
    }

    // switched off: the watcher closed, the photos and their thumbnails let go (looked at again from the start next time)
    void Sleep()
    {
        StopWatching();
        _started = false;
        _list.Clear();
        _stamps.Clear();
        _pending.Clear();
        _used.Clear();
        _dirTimes.Clear();
        lock (_evLock) _events.Clear();
        Volatile.Write(ref _want, null);
        var s = new Snapshot { Version = ++_version };
        lock (_lock) _pub = s;
    }

    void StopWatching()
    {
        var w = _fsw;
        _fsw = null;
        if (w == null) return;
        try { w.EnableRaisingEvents = false; } catch { }
        w.Dispose();
    }

    // the folder, its watcher, the newest photos
    void Start(double now)
    {
        _started = true;
        _root = FolderOverride ?? PictureFolder();
        _found = Directory.Exists(_root);
        _nextPoll = now + 4;
        if (!_found) { Log.Info($"photos: no folder {_root} (looked for again every 4 s)"); return; }
        try
        {
            _fsw = new FileSystemWatcher(_root, "*.png")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _fsw.Created += OnFile;
            _fsw.Changed += OnFile;
            _fsw.Renamed += OnFile;
            _fsw.Error += (_, e) => { Log.Info("photos: the folder watcher lost events (" + e.GetException()?.Message + ") - the poll finds them"); _wake.Set(); };
            _fsw.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            Log.Info("photos: no folder watcher (" + e.Message + ") - polling only");
            StopWatching();
        }
        Scan();
        Log.Info($"photos: watching {_root}, " + (_list.Count == 0 ? "no photos yet" : $"{_list.Count} photo(s) of {_day:yyyy-MM-dd}"));
    }

    void OnFile(object sender, FileSystemEventArgs e)
    {
        if (!IsPhoto(e.FullPath)) return;
        lock (_evLock) _events.Add(e.FullPath);
        _wake.Set();
    }

    static bool IsPhoto(string path)
    {
        string n = System.IO.Path.GetFileName(path);
        return n != null && n.StartsWith("VRChat_", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    // VRChat's own config (LocalLow\VRChat\VRChat\config.json) can name another folder; else Pictures\VRChat
    public static string PictureFolder()
    {
        try
        {
            string cfg = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "VRChat", "VRChat",
                "config.json");
            if (File.Exists(cfg) && new FileInfo(cfg).Length < 1 << 20)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("picture_output_folder", out var v) &&
                    v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                    return v.GetString();
            }
        }
        catch (Exception e) { Log.Info("photos: VRChat's config.json not read: " + e.Message); }
        string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(pics)) pics = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        return System.IO.Path.Combine(pics, "VRChat");
    }

    // the folders new photos land in: the folder itself and its two newest month folders (yyyy-MM)
    readonly List<string> _dirs = new();
    static readonly Regex MonthDir = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);

    List<string> Dirs()
    {
        _dirs.Clear();
        if (!Directory.Exists(_root)) return _dirs;
        _dirs.Add(_root);
        var months = new List<string>();
        foreach (var d in Directory.EnumerateDirectories(_root))
            if (MonthDir.IsMatch(System.IO.Path.GetFileName(d))) months.Add(d);
        months.Sort(StringComparer.OrdinalIgnoreCase);
        for (int i = months.Count - 1; i >= 0 && i >= months.Count - 2; i--) _dirs.Add(months[i]);
        return _dirs;
    }

    // the start: the newest photo's day out of those folders (the names say when; nothing is opened but the newest)
    void Scan()
    {
        var all = new List<(string path, DateTime taken)>();
        foreach (var d in Dirs())
        {
            _dirTimes[d] = Directory.GetLastWriteTimeUtc(d);
            foreach (var f in Directory.EnumerateFiles(d, "VRChat_*.png")) all.Add((f, TakenOf(f)));
        }
        all.Sort((a, b) => b.taken.CompareTo(a.taken));
        _list.Clear();
        if (all.Count == 0) return;
        _day = DayOf(all[0].taken);
        foreach (var (path, taken) in all)
        {
            if (_list.Count >= MaxPhotos || DayOf(taken) != _day) break;
            _list.Add(Named(path, taken));
        }
        // the newest whole, now (one being written: the poll's turn)
        var first = _list[0];
        var bytes = ReadWhole(first.Path, out var stamp);
        if (bytes == null) { _pending[first.Path] = new Pending { Last = Clock.Now }; return; }
        _stamps[first.Path] = stamp;
        _list[0] = Filled(first, bytes, true);
        Use(first.Path);
    }

    internal static Photo Named(string path, DateTime taken)
    {
        var p = new Photo { Path = path, Name = System.IO.Path.GetFileName(path), Taken = taken, Seen = DateTime.Now };
        var m = SizeInName.Match(p.Name);
        if (m.Success)
        {
            p.Width = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            p.Height = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        return p;
    }

    // a photo with what its file says: the metadata, and (thumb) its thumbnail
    static Photo Filled(Photo p, byte[] bytes, bool thumb)
    {
        var n = p.Copy();
        var (world, players) = ReadMeta(bytes);
        n.World = world;
        n.Players = players ?? Array.Empty<string>();
        if (thumb && n.Thumb == null)
        {
            var t = Decode(bytes);
            if (t.px != null) (n.Thumb, n.TW, n.TH, n.Width, n.Height) = t;
            else Log.Info("photos: " + p.Name + " could not be read as a picture");
        }
        return n;
    }

    bool TakeEvents(double now)
    {
        lock (_evLock)
        {
            if (_events.Count == 0) return false;
            foreach (var path in _events)
            {
                if (_pending.TryGetValue(path, out var pe)) pe.Last = now;
                else _pending[path] = new Pending { Last = now };
            }
            _events.Clear();
        }
        return false;
    }

    // every 4 s: the folder back (or gone), a folder whose time moved looked through for photos the watcher did not report, the
    // photos of the list still there, the newest's file not changed behind the watcher's back
    bool Poll(double now)
    {
        bool changed = false;
        bool found = Directory.Exists(_root);
        if (found != _found)
        {
            // it came (or went): start over on it
            StopWatching();
            _list.Clear();
            _pending.Clear();
            _dirTimes.Clear();
            Start(now);
            return true;
        }
        if (!found) return false;
        DateTime newest = _list.Count > 0 ? _list[0].Taken : DateTime.MinValue;
        foreach (var d in Dirs())
        {
            var t = Directory.GetLastWriteTimeUtc(d);
            if (_dirTimes.TryGetValue(d, out var was) && was == t) continue;
            _dirTimes[d] = t;
            foreach (var f in Directory.EnumerateFiles(d, "VRChat_*.png"))
            {
                if (_stamps.ContainsKey(f) || _pending.ContainsKey(f) || Index(f) >= 0) continue;
                if (_list.Count > 0 && TakenOf(f) < newest.AddMinutes(-1)) continue;
                _pending[f] = new Pending { Last = now - Quiet / 2 };
            }
        }
        for (int i = _list.Count - 1; i >= 0; i--)
        {
            if (File.Exists(_list[i].Path)) continue;
            Forget(_list[i].Path);
            _list.RemoveAt(i);
            changed = true;
        }
        if (_list.Count > 0 && _stamps.TryGetValue(_list[0].Path, out var st) && !_pending.ContainsKey(_list[0].Path))
        {
            var fi = new FileInfo(_list[0].Path);
            if (fi.Exists && (fi.Length != st.len || fi.LastWriteTimeUtc != st.at)) _pending[_list[0].Path] = new Pending { Last = now };
        }
        return changed;
    }

    // the files that have been quiet long enough: read once whole, then in the list (a known one: its metadata again)
    readonly List<string> _ready = new();
    bool Settle(double now)
    {
        if (_pending.Count == 0) return false;
        bool changed = false;
        _ready.Clear();
        foreach (var (path, pe) in _pending) if (now - pe.Last >= Quiet) _ready.Add(path);
        foreach (var path in _ready)
        {
            var pe = _pending[path];
            var fi = new FileInfo(path);
            if (!fi.Exists) { _pending.Remove(path); continue; }
            var stamp = (fi.Length, fi.LastWriteTimeUtc);
            if (stamp != pe.Stamp) { pe.Stamp = stamp; pe.Last = now; continue; }      // it moved since: another quiet second
            var bytes = ReadWhole(path, out var st);
            if (bytes == null)
            {
                // not whole yet (still written, or written again): later, a minute at most
                pe.Last = now;
                if (++pe.Tries > 60) { _pending.Remove(path); Log.Info("photos: " + System.IO.Path.GetFileName(path) + " never became a whole PNG"); }
                continue;
            }
            _pending.Remove(path);
            changed |= Take(path, bytes, st);
        }
        return changed;
    }

    bool Take(string path, byte[] bytes, (long, DateTime) stamp)
    {
        _stamps[path] = stamp;
        int i = Index(path);
        if (i >= 0)
        {
            // known (VRCX wrote its metadata into it): what it says now; a thumbnail only if it should have one and has none
            _list[i] = Filled(_list[i], bytes, i == 0);
            if (i == 0) Use(path);
            return true;
        }
        var taken = TakenOf(path);
        var day = DayOf(taken);
        if (_list.Count > 0 && day < _day) return false;                     // an older day's photo (copied in): not today's list
        if (_list.Count >= MaxPhotos && day == _day && taken <= _list[^1].Taken) return false;
        if (_list.Count == 0 || day > _day)
        {
            foreach (var p in _list) Forget(p.Path);
            _list.Clear();
            _day = day;
        }
        var n = Named(path, taken);
        n.Fresh = (DateTime.Now - taken).TotalMinutes < 10;
        n = Filled(n, bytes, true);
        int at = 0;
        while (at < _list.Count && _list[at].Taken > taken) at++;
        _list.Insert(at, n);
        Use(path);
        while (_list.Count > MaxPhotos) { Forget(_list[^1].Path); _list.RemoveAt(_list.Count - 1); }
        Trim();
        Log.Info($"photos: {n.Name} {(n.Thumb != null ? $"{n.TW}x{n.TH}" : "no picture")}{(n.World != null ? ", in " + n.World : "")}");
        return true;
    }

    // the opened view asked for one: its thumbnail made
    bool LoadWanted()
    {
        string w = Interlocked.Exchange(ref _want, null);
        if (w == null) return false;
        int i = Index(w);
        if (i < 0) return false;
        Use(w);
        if (_list[i].Thumb != null) return false;
        var bytes = ReadWhole(w, out var st);
        if (bytes == null) return false;
        _stamps[w] = st;
        _list[i] = Filled(_list[i], bytes, true);
        Trim();
        return true;
    }

    int Index(string path)
    {
        for (int i = 0; i < _list.Count; i++) if (string.Equals(_list[i].Path, path, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    void Use(string path)
    {
        _used.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _used.Add(path);
    }

    void Forget(string path)
    {
        _stamps.Remove(path);
        _used.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }

    // at most MaxThumbs thumbnails: the least recently wanted let go (never the newest's)
    void Trim()
    {
        int have = 0;
        foreach (var p in _list) if (p.Thumb != null) have++;
        for (int k = 0; have > MaxThumbs && k < _used.Count; k++)
        {
            int i = Index(_used[k]);
            if (i <= 0 || _list[i].Thumb == null) continue;
            var n = _list[i].Copy();
            n.Thumb = null;
            _list[i] = n;
            have--;
        }
    }

    // ---- the file ----

    static readonly byte[] PngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    static readonly byte[] PngEnd = { 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

    // the file's bytes when it is a whole PNG (its signature first, IEND last, its size and time the same after reading); null: not
    // (yet). Opened with every share allowed and closed at once: VRCX writing its metadata into it is never kept out.
    internal static byte[] ReadWhole(string path, out (long, DateTime) stamp)
    {
        stamp = default;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            var before = (fi.Length, fi.LastWriteTimeUtc);
            byte[] b;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
            {
                long n = fs.Length;
                if (n < PngSig.Length + PngEnd.Length + 25 || n > 512L << 20) return null;
                b = new byte[n];
                int got = 0;
                while (got < b.Length)
                {
                    int r = fs.Read(b, got, b.Length - got);
                    if (r <= 0) return null;
                    got += r;
                }
            }
            if (!b.AsSpan(0, PngSig.Length).SequenceEqual(PngSig) || !b.AsSpan(b.Length - PngEnd.Length).SequenceEqual(PngEnd)) return null;
            fi.Refresh();
            if (!fi.Exists || (fi.Length, fi.LastWriteTimeUtc) != before) return null;
            stamp = before;
            return b;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // "VRChat_2026-10-17_14-20-05.123_1920x1080.png" (and the old "VRChat_1920x1080_2022-01-01_12-00-00.000.png")
    static readonly Regex TakenInName = new(@"(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})-(\d{2})(?:\.(\d{1,3}))?", RegexOptions.Compiled);
    static readonly Regex SizeInName = new(@"_(\d{2,5})x(\d{2,5})(?:[_.]|$)", RegexOptions.Compiled);

    public static DateTime TakenOf(string path)
    {
        var m = TakenInName.Match(System.IO.Path.GetFileName(path) ?? "");
        if (m.Success)
        {
            try
            {
                int G(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
                int ms = m.Groups[7].Success ? int.Parse(m.Groups[7].Value.PadRight(3, '0'), CultureInfo.InvariantCulture) : 0;
                return new DateTime(G(1), G(2), G(3), G(4), G(5), G(6), ms);
            }
            catch (ArgumentOutOfRangeException) { }
        }
        try { return File.GetLastWriteTime(path); } catch { return DateTime.MinValue; }
    }

    // a photo's day: 5 AM to 5 AM (a night in VR past midnight is one day)
    public static DateTime DayOf(DateTime t) => t.AddHours(-5).Date;

    // ---- the metadata: VRCX's (or VRChat's) JSON in a tEXt / zTXt / iTXt chunk ----
    //   {"application": "VRCX", "version": 1, "author": {...}, "world": {"id": "wrld_...", "name": "...", "instanceId": "..."},
    //    "players": [{"id": "usr_...", "displayName": "..."}, ...]}

    public static (string world, string[] players) ReadMeta(byte[] b)
    {
        int at = PngSig.Length;
        while (at + 12 <= b.Length)
        {
            int len = (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
            if (len < 0 || at + 12L + len > b.Length) break;
            string type = Encoding.ASCII.GetString(b, at + 4, 4);
            if (type is "tEXt" or "zTXt" or "iTXt")
            {
                string text = ChunkText(type, b, at + 8, len);
                if (text != null && text.IndexOf('{') >= 0 && Json(text) is { } meta) return meta;
            }
            if (type == "IEND") break;
            at += 12 + len;
        }
        return (null, null);
    }

    static string ChunkText(string type, byte[] b, int at, int len)
    {
        try
        {
            int end = at + len, z = Array.IndexOf(b, (byte)0, at, len);       // the keyword ends at the first 0
            if (z < 0) return null;
            if (type == "tEXt") return Encoding.Latin1.GetString(b, z + 1, end - z - 1);
            if (type == "zTXt") return Encoding.Latin1.GetString(Inflate(b, z + 2, end - z - 2));
            // iTXt: compressed?, method, the language tag \0, the translated keyword \0, the text (UTF-8)
            bool packed = b[z + 1] != 0;
            int lang = Array.IndexOf(b, (byte)0, z + 3, end - z - 3);
            if (lang < 0) return null;
            int tk = Array.IndexOf(b, (byte)0, lang + 1, end - lang - 1);
            if (tk < 0) return null;
            return packed ? Encoding.UTF8.GetString(Inflate(b, tk + 1, end - tk - 1)) : Encoding.UTF8.GetString(b, tk + 1, end - tk - 1);
        }
        catch { return null; }
    }

    static byte[] Inflate(byte[] b, int at, int len)
    {
        using var z = new ZLibStream(new MemoryStream(b, at, len, false), CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    static (string, string[])? Json(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text.AsMemory(text.IndexOf('{')));
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            string app = r.TryGetProperty("application", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString().ToLowerInvariant() : "";
            bool hasWorld = r.TryGetProperty("world", out var w) && w.ValueKind == JsonValueKind.Object;
            if (app is not ("vrchat" or "vrcx" or "screenshotmanager") && !hasWorld) return null;
            string world = hasWorld && w.TryGetProperty("name", out var wn) && wn.ValueKind == JsonValueKind.String ? Tidy(wn.GetString()) : null;
            var players = new List<string>();
            if (r.TryGetProperty("players", out var ps) && ps.ValueKind == JsonValueKind.Array)
                foreach (var p in ps.EnumerateArray())
                    if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String &&
                        Tidy(dn.GetString()) is { } name)
                        players.Add(name);
            return (world, players.ToArray());
        }
        catch (JsonException) { return null; }
    }

    static string Tidy(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Replace('\r', ' ').Replace('\n', ' ').Trim();

    // ---- the thumbnail: the PNG decoded once, drawn into at most 640 x 400 (high-quality bicubic), the full picture let go ----

    public static (uint[] px, int w, int h, int fullW, int fullH) Decode(byte[] bytes) => Decode(bytes, ThumbW, ThumbH);

    // ... into at most maxW x maxH (On this day keeps smaller ones)
    public static (uint[] px, int w, int h, int fullW, int fullH) Decode(byte[] bytes, int maxW, int maxH)
    {
        using var ms = new MemoryStream(bytes, false);
        return Decode(ms, maxW, maxH);
    }

    // ... from a stream (the gallery's: the file itself, never a byte[] of a 4K photo)
    public static (uint[] px, int w, int h, int fullW, int fullH) Decode(Stream ms, int maxW, int maxH)
    {
        try
        {
            using var img = Image.FromStream(ms, false, false);
            int W = img.Width, H = img.Height;
            if (W <= 0 || H <= 0) return default;
            float s = MathF.Min(1, MathF.Min((float)maxW / W, (float)maxH / H));
            int w = Math.Max(1, (int)MathF.Round(W * s)), h = Math.Max(1, (int)MathF.Round(H * s));
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using var ia = new ImageAttributes();
                ia.SetWrapMode(WrapMode.TileFlipXY);              // no dark seam along the edges
                g.DrawImage(img, new Rectangle(0, 0, w, h), 0, 0, W, H, GraphicsUnit.Pixel, ia);
            }
            var px = new uint[w * h];
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(bd.Scan0 + y * bd.Stride, (int[])(object)px, y * w, w);
            }
            finally { bmp.UnlockBits(bd); }
            return (px, w, h, W, H);
        }
        catch (Exception e)
        {
            Log.Info("photos: not decoded: " + e.Message);
            return default;
        }
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(2000);
        StopWatching();
        // (_wake is left to the GC, as AvatarLink's and LumosLink's are: a step still running past the Join - a big photo being decoded -
        // would come back to a disposed handle in its WaitOne and throw on a thread with no handler: the process gone mid-exit, and the
        // folder watcher's events Set it from their own threads)
    }
}

// what a photo snapshot asks for a thumbnail the opened view steps to (the latest photo's watcher, On this day's finder)
public interface IPhotoSource
{
    void Want(string path);
}

// "Open in VR Gallery" (the photo's opened view, only on a tap): VR Gallery (github.com/Erik1028/VRGallery) has no way to be handed a
// photo - its command line has only --data-dir, --index, --tray, --no-index and --shot (a test picture) - so it is started as it is
// (a second start brings the one running to the front). Where it is: its installer's folder (%LOCALAPPDATA%\Programs\VRGallery), a
// "VR Gallery" shortcut on the desktop or in the Start menu (a build of one's own: Erik's points into his dist folder), the "start
// with Windows" entry it writes. Not there: the photo opens in Windows' own viewer. Both through Explorer, so neither starts with
// Orbit's administrator rights; on a thread of its own (a shortcut read, a process started: not on the frame).
public static class PhotoGallery
{
    public const string Exe = "VRGallery.exe";

    public static void Open(string photo)
    {
        if (string.IsNullOrEmpty(photo)) return;
        var t = new Thread(() =>
        {
            if (!File.Exists(photo)) { Log.Info("photos: " + System.IO.Path.GetFileName(photo) + " is not there any more"); return; }
            string exe = Find();
            Launch(exe ?? photo, exe != null ? "VR Gallery" : "Windows' viewer");
        }) { IsBackground = true, Name = "open photo" };
        t.SetApartmentState(ApartmentState.STA);            // (WScript.Shell, for a shortcut)
        t.Start();
    }

    static void Launch(string target, string what)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target + "\"") { UseShellExecute = false });
            Log.Info($"photos: {what} opened ({System.IO.Path.GetFileName(target)})");
        }
        catch (Exception e) { Log.Warn("photos: could not open " + target + ": " + e.Message); }
    }

    // VR Gallery's exe (null: not installed); looked for again at most every 30 s
    static string _found;
    static double _foundAt = -100;

    public static string Find()
    {
        double now = Clock.Now;
        if (now - _foundAt < 30 && (_found == null || File.Exists(_found))) return _found;
        _foundAt = now;
        _found = Look();
        return _found;
    }

    static string Look()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string installed = System.IO.Path.Combine(local, "Programs", "VRGallery", Exe);
        if (File.Exists(installed)) return installed;
        foreach (var dir in new[]
                 {
                     Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonDesktopDirectory,
                     Environment.SpecialFolder.CommonPrograms,
                 })
        {
            string lnk = System.IO.Path.Combine(Environment.GetFolderPath(dir), "VR Gallery.lnk");
            if (File.Exists(lnk) && Target(lnk) is { } t) return t;
        }
        try
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (run?.GetValue("VRGallery") is string cmd)
            {
                string p = cmd.StartsWith('"') ? cmd[1..Math.Max(1, cmd.IndexOf('"', 1))] : cmd.Split(' ')[0];
                if (IsGallery(p)) return p;
            }
        }
        catch (Exception e) { Log.Info("photos: the Run key not read: " + e.Message); }
        return null;
    }

    static bool IsGallery(string p) =>
        !string.IsNullOrEmpty(p) && string.Equals(System.IO.Path.GetFileName(p), Exe, StringComparison.OrdinalIgnoreCase) && File.Exists(p);

    static string Target(string lnk)
    {
        try
        {
            dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            try
            {
                dynamic l = sh.CreateShortcut(lnk);
                string t = l.TargetPath;
                return IsGallery(t) ? t : null;
            }
            finally { Marshal.FinalReleaseComObject(sh); }
        }
        catch (Exception e)
        {
            Log.Info("photos: shortcut not read: " + e.Message);
            return null;
        }
    }
}
