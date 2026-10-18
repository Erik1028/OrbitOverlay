using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Orbit;

// On this day (the watch's widget, WristMemory.cs): VRChat's photos of this day in earlier years, from the same folder the latest photo
// watches (Pictures\VRChat\<yyyy-MM>\VRChat_<date>_<time>_<res>.png, or VRChat's picture_output_folder). Looked for once a day - when
// the day turns (5 AM, as the latest photo's day: a night past midnight is one day) and at the start - by the files' names only: for a
// year ago, two years ago and so on, that very day, else the nearest day within 3 (the earlier one first on a tie), at most 20 of it,
// newest first. Only those days' month folders are listed (and the folder itself, where VRChat put them before the month folders).
// The list: the nearest year first. A file is opened only for its thumbnail (at most 512 x 320, at most 4 kept) and its world and
// people (VRCX's metadata, read as the latest photo reads it): the first at once, the others when the opened view steps to them.
// Off (no widget, or the watch not seen): the thread asleep, the thumbnails let go - the names kept until the day turns.
public sealed class PhotoMemories : IPhotoSource, IDisposable
{
    public const int PerDay = 20, MaxYears = 15, Near = 3, MaxThumbs = 4, ThumbW = 512, ThumbH = 320;
    static readonly int[] Offsets = { 0, -1, 1, -2, 2, -3, 3 };       // the day itself, then the nearest (the earlier first)

    public string FolderOverride;                         // --snapshot's folder of made-up photos (null: VRChat's)
    public DateTime? NowOverride;                         // --snapshot: what time it is

    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    Thread _thread;
    volatile bool _stop, _active;
    PhotoWatcher.Snapshot _pub = new();
    int _version;
    string _want;

    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            _active = value;
            if (value && _thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "on this day", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
            _wake.Set();
        }
    }

    public PhotoWatcher.Snapshot Get() { lock (_lock) return _pub; }

    // the opened view steps to a photo: its thumbnail (and metadata), when it has none
    public void Want(string path)
    {
        if (path == null || path == Volatile.Read(ref _want)) return;
        Volatile.Write(ref _want, path);
        _wake.Set();
    }

    // ---- the worker ----

    readonly List<PhotoWatcher.Photo> _list = new();
    readonly List<string> _used = new();                  // the thumbnails, the least recently wanted first
    readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    DateTime _day = DateTime.MinValue;                    // the day the list is of (none yet)
    string _root;
    bool _found;

    void Run()
    {
        while (!_stop)
        {
            if (!_active)
            {
                LetGo();
                _wake.WaitOne();
                continue;
            }
            bool changed = false;
            try
            {
                var today = PhotoWatcher.DayOf(NowOverride ?? DateTime.Now);
                if (today != _day) { Rescan(today); changed = true; }
                if (_list.Count > 0 && _list[0].Thumb == null && !_failed.Contains(_list[0].Path)) changed |= Load(0);
                changed |= LoadWanted();
            }
            catch (Exception e) { Log.Info("memories: " + e.Message); }
            if (changed) Publish();
            _wake.WaitOne(30000);                         // (the day turning is noticed within half a minute)
        }
    }

    void Publish()
    {
        var s = new PhotoWatcher.Snapshot { Version = ++_version, Ready = _day != DateTime.MinValue, FolderFound = _found, Folder = _root ?? "", Source = this };
        s.Recent.AddRange(_list);
        lock (_lock) _pub = s;
    }

    void Rescan(DateTime today)
    {
        _day = today;
        _root = FolderOverride ?? PhotoWatcher.PictureFolder();
        _found = Directory.Exists(_root);
        _list.Clear();
        _used.Clear();
        _failed.Clear();
        if (_found) _list.AddRange(Find(_root, today));
        int years = 0;
        for (int i = 0; i < _list.Count; i++) if (i == 0 || _list[i].Years != _list[i - 1].Years) years++;
        Log.Info(_found ? $"memories: {today:MM-dd}: {_list.Count} photo(s) in {years} earlier year(s)" : $"memories: no folder {_root}");
    }

    // switched off: the thumbnails let go (the names stay until the day turns)
    void LetGo()
    {
        bool any = false;
        for (int i = 0; i < _list.Count; i++)
        {
            if (_list[i].Thumb == null) continue;
            var n = _list[i].Copy();
            n.Thumb = null;
            _list[i] = n;
            any = true;
        }
        _used.Clear();
        Volatile.Write(ref _want, null);
        if (any) Publish();
    }

    // The photos of this day in earlier years in that folder, the nearest year first, each year's newest first (on its own: the
    // snapshot's checks run it on a folder of made-up photos)
    public static List<PhotoWatcher.Photo> Find(string root, DateTime today)
    {
        var found = new List<PhotoWatcher.Photo>();
        var listed = new Dictionary<string, List<(string path, DateTime taken)>>(StringComparer.OrdinalIgnoreCase);
        List<(string path, DateTime taken)> Files(string dir)
        {
            if (listed.TryGetValue(dir, out var l)) return l;
            l = new List<(string, DateTime)>();
            if (Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFiles(dir, "VRChat_*.png")) l.Add((f, PhotoWatcher.TakenOf(f)));
            return listed[dir] = l;
        }
        string Month(DateTime d) => Path.Combine(root, d.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        var day = new List<(string path, DateTime taken)>();
        for (int n = 1; n <= MaxYears; n++)
        {
            var target = today.AddYears(-n);
            if (target.Year < 2014) break;                // before VRChat
            foreach (int o in Offsets)
            {
                var d = target.AddDays(o);
                day.Clear();
                // a photo of day d was taken on d or (before 5 AM) on the next: their month folders, and the folder itself
                Collect(Files(root), d, day);
                Collect(Files(Month(d)), d, day);
                if (d.AddDays(1).Month != d.Month) Collect(Files(Month(d.AddDays(1))), d, day);
                if (day.Count == 0) continue;
                day.Sort((a, b) => b.taken.CompareTo(a.taken));
                for (int i = 0; i < day.Count && i < PerDay; i++)
                {
                    var p = PhotoWatcher.Named(day[i].path, day[i].taken);
                    p.Years = n;
                    p.OtherDay = o != 0;
                    found.Add(p);
                }
                break;
            }
        }
        return found;
    }

    static void Collect(List<(string path, DateTime taken)> files, DateTime day, List<(string path, DateTime taken)> into)
    {
        foreach (var f in files) if (PhotoWatcher.DayOf(f.taken) == day) into.Add(f);
    }

    // a photo's file read once: its thumbnail and VRCX's metadata
    bool Load(int i)
    {
        var p = _list[i];
        Use(p.Path);
        if (p.Thumb != null) return false;
        var bytes = PhotoWatcher.ReadWhole(p.Path, out _);
        if (bytes == null) { _failed.Add(p.Path); Log.Info("memories: " + p.Name + " could not be read"); return false; }
        var n = p.Copy();
        var (world, players) = PhotoWatcher.ReadMeta(bytes);
        n.World = world;
        n.Players = players ?? Array.Empty<string>();
        var t = PhotoWatcher.Decode(bytes, ThumbW, ThumbH);
        if (t.px != null) (n.Thumb, n.TW, n.TH, n.Width, n.Height) = t;
        _list[i] = n;
        Trim();
        return true;
    }

    bool LoadWanted()
    {
        string w = Interlocked.Exchange(ref _want, null);
        if (w == null || _failed.Contains(w)) return false;
        for (int i = 0; i < _list.Count; i++)
            if (string.Equals(_list[i].Path, w, StringComparison.OrdinalIgnoreCase)) return Load(i);
        return false;
    }

    void Use(string path)
    {
        _used.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _used.Add(path);
    }

    // at most MaxThumbs thumbnails: the least recently wanted let go (never the first's: the widget shows it)
    void Trim()
    {
        int have = 0;
        foreach (var p in _list) if (p.Thumb != null) have++;
        for (int k = 0; have > MaxThumbs && k < _used.Count; k++)
        {
            int i = _list.FindIndex(p => string.Equals(p.Path, _used[k], StringComparison.OrdinalIgnoreCase));
            if (i <= 0 || _list[i].Thumb == null) continue;
            var n = _list[i].Copy();
            n.Thumb = null;
            _list[i] = n;
            have--;
        }
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(2000);
        // (_wake is left to the GC, as AvatarLink's and LumosLink's are: a step still running past the Join - a day's photos being looked
        // through - would come back to a disposed handle in its WaitOne and throw on a thread with no handler: the process gone mid-exit)
    }
}
