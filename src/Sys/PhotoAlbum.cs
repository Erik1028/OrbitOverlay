using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Orbit;

// The watch's gallery (Erik 10-24: "fotógaléria, mint vrgallery a csuklómon"): every VRChat photo of the folder the latest photo
// watches - the folder itself and all its month folders (yyyy-MM) - newest first, for the opened photo view's grid (WristGallery.cs).
// Listed by the files' names only (VRChat writes when in them) when the gallery opens, and again while it is open when a folder's
// time moved (every 5 s looked at); nothing is opened to list them. A grid cell's picture is Windows' own thumbnail - the shell's
// thumbnail cache, the one Explorer shows: made once, then had in a moment - else the PNG decoded from its file (streamed: a 4K photo
// is 8-40 MB, never a byte[] of it); at most KeepCells kept, the ones asked for last loaded first (scrolled past: dropped). A photo
// opened big: its 640 x 400 thumbnail the same way, and VRCX's metadata - its iTXt chunk sits before the picture data, so only the
// file's head is read. Closed: the thread asleep, every picture let go (the list kept for the next open).
public sealed class PhotoAlbum : IPhotoSource, IPhotoCells, IDisposable
{
    public const int CellW = 192, CellH = 108, KeepCells = 72, MaxThumbs = 4, ThumbW = 640, ThumbH = 400;
    const int Asks = 32;

    public string FolderOverride;                         // --snapshot's folder of made-up photos (null: VRChat's)

    readonly object _lock = new();
    readonly AutoResetEvent _wake = new(false);
    Thread _thread;
    volatile bool _stop, _active, _rescan;
    PhotoWatcher.Snapshot _pub = new();
    int _version, _cellVersion;
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
                // STA: the shell's thumbnails are COM (an STA thread is what Explorer's own extractors expect)
                _thread = new Thread(Run) { IsBackground = true, Name = "photo album", Priority = ThreadPriority.BelowNormal };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
            _wake.Set();
        }
    }

    public PhotoWatcher.Snapshot Get() { lock (_lock) return _pub; }

    // a photo was deleted (or the folder changed under it): listed again now
    public void Rescan() { _rescan = true; _wake.Set(); }

    // the opened view steps to a photo: its thumbnail (and metadata), when it has none
    public void Want(string path)
    {
        if (path == null || path == Volatile.Read(ref _want)) return;
        Volatile.Write(ref _want, path);
        _wake.Set();
    }

    // ---- the grid's cells (the frame asks; nothing allocated on its side) ----

    sealed class CellPic { public uint[] Px; public int W, H; public long Used; public double RetryAt; }
    readonly Dictionary<string, CellPic> _cells = new(StringComparer.OrdinalIgnoreCase);
    readonly string[] _asks = new string[Asks];           // the cells asked for, the last asked at the end
    int _askN;
    long _tick;

    public int CellVersion => Volatile.Read(ref _cellVersion);

    // a cell's picture (CellW x CellH at most), or default while it is made - then asked for, the last asked made first
    public (uint[] px, int w, int h) Cell(string path)
    {
        if (path == null) return default;
        lock (_lock)
        {
            if (_cells.TryGetValue(path, out var c) && c.Px != null) { c.Used = ++_tick; return (c.Px, c.W, c.H); }
            if (c != null && Clock.Now < c.RetryAt) return default;
            int at = -1;
            for (int i = 0; i < _askN; i++) if (ReferenceEquals(_asks[i], path) || string.Equals(_asks[i], path, StringComparison.OrdinalIgnoreCase)) { at = i; break; }
            if (at == _askN - 1 && at >= 0) return default;           // the last asked already
            if (at >= 0) { Array.Copy(_asks, at + 1, _asks, at, _askN - at - 1); _askN--; }
            else if (_askN == Asks) { Array.Copy(_asks, 1, _asks, 0, Asks - 1); _askN--; }       // the oldest ask dropped (scrolled past)
            _asks[_askN++] = path;
        }
        _wake.Set();
        return default;
    }

    string TakeAsk()
    {
        lock (_lock)
        {
            if (_askN == 0) return null;
            string p = _asks[--_askN];
            _asks[_askN] = null;
            return p;
        }
    }

    // ---- the worker ----

    readonly List<PhotoWatcher.Photo> _list = new();      // newest first
    readonly Dictionary<string, PhotoWatcher.Photo> _byPath = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, DateTime> _dirTimes = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _used = new();                  // the big thumbnails, the least recently wanted first
    readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    static readonly Regex MonthDir = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);
    string _root;
    bool _found, _listed;
    double _nextLook;

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
                double now = Clock.Now;
                if (!_listed || _rescan || now >= _nextLook)
                {
                    bool forced = !_listed || _rescan;
                    _rescan = false;
                    _nextLook = now + 5;
                    changed |= List(forced);
                }
                changed |= LoadWanted();
                // the cells asked for, the last asked first, a few between looks at the wanted photo (a step in the big view waits
                // for no grid)
                for (int k = 0; k < 6 && !_stop && _active && Volatile.Read(ref _want) == null; k++)
                {
                    string a = TakeAsk();
                    if (a == null) break;
                    MakeCell(a);
                }
            }
            catch (Exception e) { Log.Info("album: " + e.Message); _nextLook = Clock.Now + 10; }
            if (changed) Publish();
            bool more;
            lock (_lock) more = _askN > 0;
            if (!more && Volatile.Read(ref _want) == null) _wake.WaitOne(5000);
        }
    }

    void Publish()
    {
        var s = new PhotoWatcher.Snapshot { Version = ++_version, Ready = _listed, FolderFound = _found, Folder = _root ?? "", Source = this };
        s.Recent.AddRange(_list);
        lock (_lock) _pub = s;
    }

    // the folders' files by name, newest first (a folder whose time did not move is not listed again; forced: all of them)
    bool List(bool forced)
    {
        _root = FolderOverride ?? PhotoWatcher.PictureFolder();
        bool found = Directory.Exists(_root);
        var dirs = new List<string>();
        if (found)
        {
            dirs.Add(_root);
            foreach (var d in Directory.EnumerateDirectories(_root))
                if (MonthDir.IsMatch(Path.GetFileName(d))) dirs.Add(d);
        }
        bool same = _listed && !forced && found == _found && dirs.Count == _dirTimes.Count;
        if (same)
            foreach (var d in dirs)
                if (!_dirTimes.TryGetValue(d, out var t) || t != Directory.GetLastWriteTimeUtc(d)) { same = false; break; }
        if (same) return false;
        _found = found;
        _dirTimes.Clear();
        var all = new List<(string path, DateTime taken)>();
        foreach (var d in dirs)
        {
            _dirTimes[d] = Directory.GetLastWriteTimeUtc(d);
            foreach (var f in Directory.EnumerateFiles(d, "VRChat_*.png")) all.Add((f, PhotoWatcher.TakenOf(f)));
        }
        all.Sort((a, b) => b.taken.CompareTo(a.taken));
        // the photos listed before kept as they are (their metadata, their thumbnail)
        bool changed = !_listed || all.Count != _list.Count;
        _list.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, taken) in all)
        {
            if (!_byPath.TryGetValue(path, out var p)) { p = PhotoWatcher.Named(path, taken); _byPath[path] = p; changed = true; }
            _list.Add(p);
            seen.Add(path);
        }
        if (_byPath.Count != seen.Count)
        {
            var gone = new List<string>();
            foreach (var k in _byPath.Keys) if (!seen.Contains(k)) gone.Add(k);
            foreach (var k in gone) { _byPath.Remove(k); _used.Remove(k); lock (_lock) _cells.Remove(k); }
            changed = true;
        }
        if (!_listed) Log.Info(found ? $"album: {_list.Count} photo(s) in {dirs.Count} folder(s) of {_root}" : $"album: no folder {_root}");
        _listed = true;
        return changed;
    }

    // closed: every picture let go (the list stays, its metadata too: a few strings a photo)
    void LetGo()
    {
        bool any = false;
        for (int i = 0; i < _list.Count; i++)
        {
            if (_list[i].Thumb == null) continue;
            var n = _list[i].Copy();
            n.Thumb = null;
            _list[i] = n;
            _byPath[n.Path] = n;
            any = true;
        }
        _used.Clear();
        _failed.Clear();
        lock (_lock)
        {
            _cells.Clear();
            Array.Clear(_asks);
            _askN = 0;
        }
        Volatile.Write(ref _want, null);
        if (any) Publish();
    }

    void MakeCell(string path)
    {
        lock (_lock) if (_cells.TryGetValue(path, out var had) && (had.Px != null || Clock.Now < had.RetryAt)) return;
        var (px, w, h) = Picture(path, CellW, CellH);
        lock (_lock)
        {
            _cells[path] = new CellPic { Px = px, W = w, H = h, Used = ++_tick, RetryAt = px == null ? Clock.Now + 10 : 0 };
            // at most KeepCells: the least recently drawn let go
            while (_cells.Count > KeepCells)
            {
                string old = null;
                long least = long.MaxValue;
                foreach (var kv in _cells) if (kv.Value.Used < least) { least = kv.Value.Used; old = kv.Key; }
                if (old == null) break;
                _cells.Remove(old);
            }
        }
        if (px != null) Interlocked.Increment(ref _cellVersion);
    }

    bool LoadWanted()
    {
        string w = Interlocked.Exchange(ref _want, null);
        if (w == null || _failed.Contains(w)) return false;
        int i = _list.FindIndex(p => string.Equals(p.Path, w, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        var p = _list[i];
        Use(p.Path);
        if (p.Thumb != null) return false;
        var n = p.Copy();
        var (world, players) = ReadHeadMeta(p.Path);
        n.World = world;
        n.Players = players ?? Array.Empty<string>();
        var (px, tw, th) = Picture(p.Path, ThumbW, ThumbH);
        if (px == null) { _failed.Add(p.Path); Log.Info("album: " + p.Name + " could not be read as a picture"); }
        else (n.Thumb, n.TW, n.TH) = (px, tw, th);
        _list[i] = n;
        _byPath[n.Path] = n;
        Trim();
        return true;
    }

    void Use(string path)
    {
        _used.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _used.Add(path);
    }

    // at most MaxThumbs big thumbnails: the least recently wanted let go
    void Trim()
    {
        int have = 0;
        foreach (var p in _list) if (p.Thumb != null) have++;
        for (int k = 0; have > MaxThumbs && k < _used.Count; k++)
        {
            int i = _list.FindIndex(p => string.Equals(p.Path, _used[k], StringComparison.OrdinalIgnoreCase));
            if (i < 0 || _list[i].Thumb == null) continue;
            var n = _list[i].Copy();
            n.Thumb = null;
            _list[i] = n;
            _byPath[n.Path] = n;
            have--;
        }
    }

    // ---- a picture: the shell's thumbnail, else the file decoded ----

    internal static (uint[] px, int w, int h) Picture(string path, int maxW, int maxH)
    {
        var s = ShellThumb.Get(path, maxW, maxH);
        if (s.px != null) return s;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            var d = PhotoWatcher.Decode(fs, maxW, maxH);
            return (d.px, d.w, d.h);
        }
        catch (IOException) { return default; }
        catch (UnauthorizedAccessException) { return default; }
    }

    // VRCX's metadata from the file's head: the chunks before the first IDAT (VRCX writes its iTXt there), never the picture data
    internal static (string world, string[] players) ReadHeadMeta(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 12);
            var head = new MemoryStream();
            var buf = new byte[8];
            if (fs.Read(buf, 0, 8) != 8 || buf[0] != 0x89 || buf[1] != (byte)'P') return (null, null);
            head.Write(buf, 0, 8);
            while (head.Length < 1 << 20)
            {
                if (fs.Read(buf, 0, 8) != 8) break;
                int len = (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
                if (len < 0 || len > 1 << 20) break;
                if (buf[4] == 'I' && buf[5] == 'D' && buf[6] == 'A' && buf[7] == 'T') break;
                head.Write(buf, 0, 8);
                var body = new byte[len + 4];
                int got = 0;
                while (got < body.Length) { int r = fs.Read(body, got, body.Length - got); if (r <= 0) break; got += r; }
                if (got < body.Length) break;
                head.Write(body, 0, body.Length);
            }
            return PhotoWatcher.ReadMeta(head.ToArray());
        }
        catch (IOException) { return (null, null); }
        catch (UnauthorizedAccessException) { return (null, null); }
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(2000);
        // (_wake is left to the GC, as On this day's is: a picture still being made past the Join would come back to a disposed handle)
    }
}

// a source the grid asks for its cells' pictures (the album; made-up photos have their own thumbnails)
public interface IPhotoCells
{
    (uint[] px, int w, int h) Cell(string path);
    int CellVersion { get; }
}

// Windows' own thumbnail of a file (IShellItemImageFactory: the thumbnail cache Explorer fills - read at once when it has one, else
// made by the system's PNG extractor and kept there for next time). THUMBNAILONLY: no file-type icon instead. On an STA thread.
static class ShellThumb
{
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public ushort biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out BITMAP bmp);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] uint[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);

    const int SIIGBF_THUMBNAILONLY = 0x08;
    static Guid IidFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    static bool _broken;

    public static (uint[] px, int w, int h) Get(string path, int maxW, int maxH)
    {
        if (_broken || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) return default;
        IShellItemImageFactory f = null;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref IidFactory, out f) != 0 || f == null) return default;
            if (f.GetImage(new SIZE { cx = maxW, cy = maxH }, SIIGBF_THUMBNAILONLY, out hbm) != 0 || hbm == IntPtr.Zero) return default;
            if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return default;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w > maxW * 2 || h > maxH * 2) return default;           // (not what was asked for: the decode's turn)
            var px = new uint[w * h];
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            int got;
            try { got = GetDIBits(dc, hbm, 0, (uint)h, px, ref bi, 0); }
            finally { DeleteDC(dc); }
            if (got != h) return default;
            // a picture with no alpha comes with its alpha all 0: opaque then (with alpha: premultiplied already, as the photos' are)
            bool anyAlpha = false;
            for (int i = 0; i < px.Length && !anyAlpha; i++) anyAlpha = (px[i] >> 24) != 0;
            if (!anyAlpha) for (int i = 0; i < px.Length; i++) px[i] |= 0xFF000000;
            return (px, w, h);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or EntryPointNotFoundException or DllNotFoundException)
        {
            if (e is not COMException) { _broken = true; Log.Info("album: no shell thumbnails (" + e.Message + ")"); }
            return default;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            if (f != null) Marshal.ReleaseComObject(f);
        }
    }
}

// A photo into the Recycle Bin (the watch's Delete, after its second tap) - never deleted for good: only a VRChat photo (VRChat_*.png)
// inside VRChat's photo folder, a file (not a folder, not a link), on a fixed disk whose Recycle Bin is on and big enough for it;
// moved by Windows' own file operation with undo (FOF_ALLOWUNDO), and FOF_WANTNUKEWARNING: were Windows to delete it for good after
// all, it asks first (on the desktop) instead of doing it - the same guard VR Gallery's own delete has.
public static class PhotoBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom, pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool GetVolumeNameForVolumeMountPointW(string mount, System.Text.StringBuilder name, int len);

    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

    // why it may not be moved (English, the panel says it in its language), or null: it may
    public static string Check(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return "Not a VRChat photo";
        string full, top;
        try { full = Path.GetFullPath(path); top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar; }
        catch (Exception) { return "Not a VRChat photo"; }
        if (!full.StartsWith(top, StringComparison.OrdinalIgnoreCase) || full.Contains("\\..", StringComparison.Ordinal)) return "Not in VRChat's photo folder";
        string name = Path.GetFileName(full);
        if (!name.StartsWith("VRChat_", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "Not a VRChat photo";
        // the photo and every folder between it and the photo folder: no link hopping elsewhere
        for (string p = full; p != null && p.Length >= top.Length - 1; p = Path.GetDirectoryName(p))
        {
            FileAttributes a;
            try { a = File.GetAttributes(p); }
            catch (FileNotFoundException) { return "The photo is not there any more"; }
            catch (DirectoryNotFoundException) { return "The photo is not there any more"; }
            catch (Exception) { return "The photo could not be looked at"; }
            if ((a & FileAttributes.ReparsePoint) != 0 && p.Length >= top.Length) return "Not a plain file";
            if (ReferenceEquals(p, full) && (a & FileAttributes.Directory) != 0) return "Not a plain file";
            if (p.Length <= top.Length) break;
        }
        try
        {
            string drive = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(drive) || drive.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(drive).DriveType != DriveType.Fixed)
                return "That drive has no Recycle Bin";
            if (!BinTakes(drive, new FileInfo(full).Length)) return "The Recycle Bin is off or too small there";
        }
        catch (Exception) { return "That drive has no Recycle Bin"; }
        return null;
    }

    // the drive's Recycle Bin is on (not "delete at once" - for this volume or by policy) and the photo fits in it whole
    static bool BinTakes(string drive, long bytes)
    {
        using (var pol = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"))
            if (pol?.GetValue("NoRecycleFiles") is int nr && nr != 0) return false;
        var vol = new System.Text.StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPointW(drive, vol, vol.Capacity)) return true;        // (no name: Windows' defaults - on)
        string v = vol.ToString();
        int a = v.IndexOf('{'), b = v.IndexOf('}');
        if (a < 0 || b < a) return true;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + v.Substring(a, b - a + 1));
        if (key == null) return true;
        if (key.GetValue("NukeOnDelete") is int nuke && nuke != 0) return false;
        if (key.GetValue("MaxCapacity") is int mb && mb > 0 && bytes >= (long)mb << 20) return false;
        return true;
    }

    // into the Recycle Bin (blocking: on a thread of its own); null: done - else why not
    public static string Recycle(string path, string root)
    {
        string why = Check(path, root);
        if (why != null) return why;
        string full = Path.GetFullPath(path);
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = full + "\0\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING),
        };
        int r = SHFileOperationW(ref op);
        if (r != 0 || op.fAnyOperationsAborted) { Log.Info($"photo bin: {Path.GetFileName(full)} not moved (0x{r:X})"); return "Windows did not move it"; }
        if (File.Exists(full)) return "Windows did not move it";
        Log.Info("photo bin: " + Path.GetFileName(full) + " moved to the Recycle Bin");
        return null;
    }
}
