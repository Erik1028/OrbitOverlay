using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Threading;

namespace Orbit;

// --snapshot's part for the photo view's gallery and Delete (WristGallery.cs, Sys/PhotoAlbum.cs), on made-up photos only: Gallery opens
// the grid, the stick scrolls it, a tap opens a photo big, its arrow steps through all of them, Gallery goes back to the grid and to the
// photo again; Delete asks on its first tap, on the second hands the photo to the app (a made-up hand-over: nothing is ever deleted by a
// tool) and the view steps off it, a refusal says why; the album itself on a folder of made-up PNGs; what the bin refuses. Pictures:
// wrist_gallery_<case><tag>.png and gallery_preview.png (every style's grid and Delete asking).
static class GalleryViewTools
{
    static void Draw2(WristPanel wrist, PanelData data) { wrist.Draw(data); wrist.Draw(data); }

    static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    static bool TapId(WristPanel wrist, PanelData data, string id)
    {
        var at = wrist.ControlPoint(data, id);
        if (at is not { } a) return false;
        Tap(wrist, data, a);
        return true;
    }

    static void Settle(WristPanel wrist, PanelData data)
    {
        for (int k = 0; k < 11; k++) { Thread.Sleep(40); wrist.Draw(data); }
    }

    static void Scroll(WristPanel wrist, PanelData data, float n)
    {
        float s = Theme.Classic ? 2 : 1;
        var mid = new Vector2(WristPanel.TW / 2f, WristPanel.TH / 2f);
        wrist.PointerMove(Hand.Left, mid); wrist.Draw(data);
        wrist.PointerScroll(Hand.Left, n, mid); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    // the album: the latest photo's five (today), then made-up days - yesterday, three days ago, last month, a year ago
    static PhotoWatcher.Snapshot _album;
    static PhotoWatcher.Snapshot _albumOf;

    public static PhotoWatcher.Snapshot Album(PhotoWatcher.Snapshot latest)
    {
        if (ReferenceEquals(_albumOf, latest) && _album != null) return _album;
        var s = new PhotoWatcher.Snapshot { Version = 1, Ready = true, FolderFound = true, Folder = @"C:\made-up\VRChat" };
        s.Recent.AddRange(latest.Recent);
        var now = DateTime.Now;
        int k = 0;
        foreach (var (days, n) in new[] { (1, 7), (3, 9), (40, 6), (366, 4) })
            for (int i = 0; i < n; i++, k++)
            {
                int scene = (k * 5 + 1) % 6;
                bool tall = scene == 5;
                var (px, w, h) = PhotoFake.Pixels(scene, tall ? 180 : 320, tall ? 320 : 180);
                var taken = now.Date.AddDays(-days).AddHours(21).AddMinutes(-i * 13);
                var (world, people) = PhotoFake.Meta[k % PhotoFake.Meta.Length];
                string name = $"VRChat_{taken:yyyy-MM-dd_HH-mm-ss.fff}_{(tall ? "1080x1920" : "1920x1080")}.png";
                s.Recent.Add(new PhotoWatcher.Photo
                {
                    Path = @"C:\made-up\VRChat\" + taken.ToString("yyyy-MM") + @"\" + name, Name = name, Taken = taken, Width = tall ? 1080 : 1920,
                    Height = tall ? 1920 : 1080, World = world, Players = people, Thumb = px, TW = w, TH = h, Seen = now,
                });
            }
        (_album, _albumOf) = (s, latest);
        return s;
    }

    public static readonly List<(string tag, string grid, string ask)> Preview = new();

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var photos0 = data.Photos;
        var album0 = data.Album;
        var del0 = wrist.OnDeletePhoto;
        var open0 = wrist.OnOpenPhoto;
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            wrist.OnOpenPhoto = _ => { };
            Checks(wrist, data, wcfg, tag);
            Pictures(wrist, data, wcfg, gpu, dir, tag);
        }
        catch (Exception e) { Log.Error("gallery view" + tag + ": " + e); }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            wrist.OnDeletePhoto = del0;
            wrist.OnOpenPhoto = open0;
            data.Photos = photos0;
            data.Album = album0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    static void Checks(WristPanel wrist, PanelData data, WristConfig wcfg, string tag)
    {
        bool classic = Theme.Classic;
        string prevId = classic ? "wph95p" : "wphprev";
        var snap = PhotoFake.Snap(5);
        var album = Album(snap);
        data.Photos = snap;
        data.Album = album;
        string deleted = null, answer = null;
        wrist.OnDeletePhoto = (p, done) => { deleted = p; done(answer); };
        wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 0, Y = 2, W = 2, H = 1 } };
        wrist.Settle();
        wrist.OpenForTest("photo");
        Draw2(wrist, data);
        // Gallery: the grid (scrolled to the photo shown: the newest, at the top)
        bool all = TapId(wrist, data, "wphall");
        bool grid = all && wrist.GalleryGridShown && wrist.GalleryScroll == 0;
        // the stick scrolls it
        Scroll(wrist, data, -6);
        bool scrolled = wrist.GalleryScroll > 0;
        // a tap on a photo: big, in all the photos
        bool tapped = TapId(wrist, data, "wgc2");
        Draw2(wrist, data);
        string pick = wrist.LastGalleryPick;
        int at = pick == null ? -1 : album.Recent.FindIndex(p => p.Path == pick);
        bool picked = tapped && at >= 0 && !wrist.GalleryGridShown && wrist.GalleryOpenShown == pick;
        if (!picked) Log.Info($"gallery view{tag}: (the tap: control found {tapped}, picked {pick ?? "none"} at {at}, grid {wrist.GalleryGridShown}, shown {wrist.GalleryOpenShown ?? "none"})");
        // its arrow: the one before it, across the days
        TapId(wrist, data, prevId);
        bool older = at >= 0 && at + 1 < album.Recent.Count && wrist.GalleryOpenShown == album.Recent[at + 1].Path;
        // Gallery: the grid again; again: back to the photo
        TapId(wrist, data, "wphall");
        bool back = wrist.GalleryGridShown;
        TapId(wrist, data, "wphall");
        Draw2(wrist, data);
        bool again = !wrist.GalleryGridShown && at >= 0 && wrist.GalleryOpenShown == album.Recent[at + 1].Path;
        // Delete: the first tap asks (nothing handed over), the second hands it to the app - the view steps to the one before it
        string target = wrist.GalleryOpenShown;
        TapId(wrist, data, "wphdel");
        bool asks = wrist.DeleteArmedShown && deleted == null;
        TapId(wrist, data, "wphdel");
        Draw2(wrist, data);
        bool moved = target != null && deleted == target && wrist.GalleryNote == "Moved to the Recycle Bin" && wrist.GalleryOpenShown != target;
        // one Windows would not move: why, the view stays on it
        answer = "Windows did not move it";
        deleted = null;
        string t2 = wrist.GalleryOpenShown;
        TapId(wrist, data, "wphdel");
        TapId(wrist, data, "wphdel");
        Draw2(wrist, data);
        bool refused = t2 != null && deleted == t2 && wrist.GalleryNote == answer && wrist.GalleryOpenShown == t2;
        // closed and opened again: the latest photo's own view, not the gallery; Delete there too
        wrist.CloseForTest();
        wrist.Settle();
        wrist.OpenForTest("photo");
        Draw2(wrist, data);
        bool fresh = !wrist.GalleryOn && wrist.PhotoOpenShown == snap.Recent[0].Path;
        answer = null;
        deleted = null;
        TapId(wrist, data, "wphdel");
        TapId(wrist, data, "wphdel");
        Draw2(wrist, data);
        bool latestDel = deleted == snap.Recent[0].Path && wrist.PhotoOpenShown == snap.Recent[1].Path;
        // no album yet (it is being listed): the grid waits, nothing breaks
        data.Album = null;
        TapId(wrist, data, "wphall");
        Draw2(wrist, data);
        bool waits = wrist.GalleryGridShown && !wrist.PhotoEmptyShown;
        wrist.CloseForTest();
        Log.Info($"gallery view{tag}: Gallery opened the grid {grid} (want True), the stick scrolled it {scrolled} (want True), a tap opened that photo big " +
                 $"{picked} (want True), its arrow stepped to the one before it {older} (want True), Gallery went back to the grid {back} and to the photo " +
                 $"again {again} (want True True); Delete asked first {asks} (want True), then handed it over and stepped off it {moved} (want True), " +
                 $"a refusal said why and stayed {refused} (want True); opened again: the latest photo's view {fresh} (want True), its Delete too " +
                 $"{latestDel} (want True); no album yet: the grid waits {waits} (want True)");
    }

    static void Pictures(WristPanel wrist, PanelData data, WristConfig wcfg, Gpu gpu, string dir, string tag)
    {
        var snap = PhotoFake.Snap(5);
        data.Photos = snap;
        data.Album = Album(snap);
        wrist.OnDeletePhoto = (p, done) => done(null);
        wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 2, Y = 0, W = 2, H = 2 } };
        string Save(string name)
        {
            string file = Path.Combine(dir, $"wrist_gallery_{name}{tag}.png");
            wrist.Surf.SavePng(gpu, file);
            return file;
        }
        wrist.Settle();
        wrist.OpenForTest("photo");
        Draw2(wrist, data);
        TapId(wrist, data, "wphall");
        wrist.Settle();
        Settle(wrist, data);
        string grid = Save("grid");
        Scroll(wrist, data, -9);
        Settle(wrist, data);
        Save("scrolled");
        TapId(wrist, data, "wgc1");
        wrist.Settle();
        Settle(wrist, data);
        Save("open");
        TapId(wrist, data, "wphdel");
        Settle(wrist, data);
        string ask = Save("delete");
        TapId(wrist, data, "wphdel");
        Draw2(wrist, data);
        Save("deleted");
        wrist.CloseForTest();
        Preview.RemoveAll(p => p.tag == tag);
        Preview.Add((tag, grid, ask));
        Log.Info($"gallery view{tag}: 5 pictures (wrist_gallery_*{tag}.png)");
    }

    // what a frame allocates with the grid at rest: nothing is the goal (the layout, the headings, the bitmaps kept)
    public static void Perf(WristPanel wrist, PanelData data, ConfigStore cfg, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var photos0 = data.Photos;
        var album0 = data.Album;
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 0, Y = 0, W = 2, H = 2 } };
            var snap = PhotoFake.Snap(5);
            data.Photos = snap;
            data.Album = Album(snap);
            long Frame()
            {
                for (int i = 0; i < 6; i++) wrist.Draw(data);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 30; i++) wrist.Draw(data);
                return (GC.GetAllocatedBytesForCurrentThread() - a0) / 30;
            }
            wrist.Settle();
            wrist.OpenForTest("photo");
            TapId(wrist, data, "wphall");
            long grid = Frame();
            Scroll(wrist, data, -9);
            long scrolled = Frame();
            TapId(wrist, data, "wgc1");
            long open = Frame();
            wrist.CloseForTest();
            Log.Info($"gallery perf{tag}: the grid at rest {grid} B a frame, scrolled {scrolled} B, a photo of it open {open} B (want 0, 0, 0)");
        }
        finally
        {
            wrist.CloseForTest();
            data.Photos = photos0;
            data.Album = album0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    // every style's grid and its Delete asking, side by side: gallery_preview.png
    public static void PreviewSheet(string dir)
    {
        if (Preview.Count == 0) return;
        const int W = 640, H = 440, Gap = 12, Label = 26;
        using var sheet = new Bitmap(2 * W + 3 * Gap, Preview.Count * (H + Label + Gap) + Gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 32, 34, 40));
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int i = 0; i < Preview.Count; i++)
            {
                var (t, grid, ask) = Preview[i];
                float y = Gap + i * (H + Label + Gap);
                string style = t.Length == 0 ? "modern" : t[1..];
                g.DrawString($"{style}: the gallery's grid, a photo of it with Delete asking", font, Brushes.White, Gap, y + 4);
                using (var a = new Bitmap(grid)) g.DrawImage(a, Gap, y + Label, W, H);
                using (var b = new Bitmap(ask)) g.DrawImage(b, 2 * Gap + W, y + Label, W, H);
            }
        }
        string path = Path.Combine(dir, "gallery_preview.png");
        sheet.Save(path, ImageFormat.Png);
        Log.Info($"gallery view: the styles' grids and Delete side by side: {path}");
    }

    // a made-up PNG with VRCX's metadata where VRCX writes it: right after IHDR, before the picture data
    static byte[] PngHead(int k, int w, int h, int meta)
    {
        var tail = PhotoFake.Png(k, w, h, meta);          // (its chunk before IEND: moved to after IHDR)
        var plain = PhotoFake.Png(k, w, h);
        int chunkLen = tail.Length - plain.Length;
        var chunk = tail.AsSpan(plain.Length - 12, chunkLen).ToArray();
        var o = new List<byte>(plain.AsSpan(0, 33).ToArray());
        o.AddRange(chunk);
        o.AddRange(plain.AsSpan(33).ToArray());
        return o.ToArray();
    }

    // The album on a folder of made-up photos (the snapshot's directory): every one listed, newest first, from the folder and its month
    // folders; a cell's picture made (the shell's thumbnail or the file decoded) within its bounds; a photo wanted big gets its picture
    // and the metadata from its head; a file gone listed again without it; switched off, the pictures let go. Then what the bin
    // refuses - checked only: nothing is ever moved by the tool.
    public static void AlbumChecks(string dir)
    {
        string root = Path.Combine(dir, "album_folder");
        using var a = new PhotoAlbum { FolderOverride = root };
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            var now = DateTime.Now;
            string Month(DateTime t) { string m = Path.Combine(root, t.ToString("yyyy-MM")); Directory.CreateDirectory(m); return m; }
            string Put(string folder, DateTime t, byte[] png)
            {
                string f = Path.Combine(folder, $"VRChat_{t:yyyy-MM-dd_HH-mm-ss.fff}_640x360.png");
                File.WriteAllBytes(f, png);
                return f;
            }
            var t0 = now.AddMinutes(-5);
            string newest = Put(Month(t0), t0, PngHead(2, 640, 360, 2));
            Put(Month(now.AddMinutes(-30)), now.AddMinutes(-30), PhotoFake.Png(0, 640, 360));
            Put(Month(now.AddDays(-1)), now.AddDays(-1), PhotoFake.Png(1, 640, 360));
            string gone = Put(Month(now.AddDays(-40)), now.AddDays(-40), PhotoFake.Png(3, 640, 360));
            Put(Month(now.AddDays(-41)), now.AddDays(-41), PhotoFake.Png(4, 640, 360));
            Put(root, now.AddYears(-2), PhotoFake.Png(0, 640, 360));          // (before the month folders: in the folder itself)
            File.WriteAllText(Path.Combine(root, "notes.txt"), "not a photo");
            a.Active = true;
            PhotoWatcher.Snapshot s = null;
            bool Wait(Func<PhotoWatcher.Snapshot, bool> ok, double seconds)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < seconds)
                {
                    s = a.Get();
                    if (ok(s)) return true;
                    Thread.Sleep(50);
                }
                return false;
            }
            bool listed = Wait(x => x.Ready && x.Recent.Count == 6, 5);
            bool order = listed && s.Recent[0].Path == newest;
            for (int i = 1; order && i < s.Recent.Count; i++) order = s.Recent[i - 1].Taken >= s.Recent[i].Taken;
            // a cell: asked, then made
            var sw0 = System.Diagnostics.Stopwatch.StartNew();
            (uint[] px, int w, int h) cell = a.Cell(newest);
            bool askedFirst = cell.px == null;
            while (cell.px == null && sw0.Elapsed.TotalSeconds < 8) { Thread.Sleep(50); cell = a.Cell(newest); }
            bool cellOk = cell.px != null && cell.w <= PhotoAlbum.CellW && cell.h <= PhotoAlbum.CellH && cell.px.Length == cell.w * cell.h;
            double cellMs = sw0.Elapsed.TotalMilliseconds;
            // the photo big: its picture and the head's metadata
            a.Want(newest);
            bool big = Wait(x => x.Recent.Count > 0 && x.Recent[0].Thumb != null, 8);
            bool meta = big && s.Recent[0].World == PhotoFake.Meta[2].world && s.Recent[0].Players.Length == PhotoFake.Meta[2].people.Length;
            // a file gone: listed again without it
            File.Delete(gone);
            a.Rescan();
            bool relisted = Wait(x => x.Recent.Count == 5 && !x.Recent.Exists(p => p.Path == gone), 5);
            // off: the pictures let go
            a.Active = false;
            bool letGo = Wait(x => !x.Recent.Exists(p => p.Thumb != null), 3) && a.Cell(newest).px == null;
            Log.Info($"photo album: listed every photo {listed} (want True), newest first {order} (want True); a cell asked for first {askedFirst} (want True), " +
                     $"made {cellOk} (want True) in {cellMs:0} ms; the photo big {big} (want True) with its world and people from its head {meta} (want True); " +
                     $"a deleted file listed again without it {relisted} (want True); switched off, the pictures let go {letGo} (want True)");
            // the bin's checks (never Recycle: a tool moves nothing)
            Directory.CreateDirectory(Path.Combine(root, "VRChat_folder.png"));
            string outside = Path.Combine(dir, "VRChat_2026-01-01_00-00-00.000_640x360.png");
            File.WriteAllBytes(outside, PhotoFake.Png(0, 64, 36));
            string c1 = PhotoBin.Check(outside, root);
            string c2 = PhotoBin.Check(Path.Combine(root, "notes.txt"), root);
            string c3 = PhotoBin.Check(Path.Combine(root, "VRChat_folder.png"), root);
            string c4 = PhotoBin.Check(gone, root);
            string c5 = PhotoBin.Check(Path.Combine(root, "..", "album_folder", "..", Path.GetFileName(outside)), root);
            string c6 = PhotoBin.Check(newest, root);
            File.Delete(outside);
            Log.Info($"photo bin: outside the folder \"{c1}\" (want Not in VRChat's photo folder), not a photo \"{c2}\" (want Not a VRChat photo), a folder " +
                     $"\"{c3}\" (want Not a plain file), gone \"{c4}\" (want The photo is not there any more), a path climbing out \"{c5}\" (want Not in " +
                     $"VRChat's photo folder); a photo of the folder \"{c6 ?? "may be moved"}\" (want may be moved, on a fixed disk with its Recycle Bin on)");
        }
        catch (Exception e) { Log.Error("photo album: " + e); }
        finally
        {
            a.Active = false;
            try { Thread.Sleep(200); if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    // --albumbench (read only: nothing is written, moved or kept): the album on VRChat's real folder - how long the listing takes, then
    // the newest 24 cells asked for as the grid asks (how many came from the shell's thumbnail cache and how fast), and one photo big
    public static int Bench()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var a = new PhotoAlbum();
        a.Active = true;
        PhotoWatcher.Snapshot s = a.Get();
        while (!s.Ready && sw.Elapsed.TotalSeconds < 20) { Thread.Sleep(20); s = a.Get(); }
        double listMs = sw.Elapsed.TotalMilliseconds;
        Log.Info($"albumbench: {s.Recent.Count} photo(s) listed in {listMs:0} ms (folder found {s.FolderFound})");
        int n = Math.Min(24, s.Recent.Count), made = 0;
        sw.Restart();
        var firstAt = new double[n];
        for (int i = 0; i < n; i++) a.Cell(s.Recent[i].Path);
        while (made < n && sw.Elapsed.TotalSeconds < 60)
        {
            made = 0;
            for (int i = 0; i < n; i++)
            {
                var c = a.Cell(s.Recent[i].Path);
                if (c.px != null) { made++; if (firstAt[i] == 0) firstAt[i] = sw.Elapsed.TotalMilliseconds; }
            }
            Thread.Sleep(10);
        }
        Array.Sort(firstAt);
        Log.Info($"albumbench: {made}/{n} cells in {sw.Elapsed.TotalMilliseconds:0} ms (first {(n > 0 ? firstAt[0] : 0):0} ms, median {(n > 0 ? firstAt[n / 2] : 0):0} ms)");
        // each way timed on its own, on the 24th-48th photos (not yet asked: no cache of this process)
        var shell = new List<double>();
        var dec = new List<double>();
        for (int i = n; i < Math.Min(n + 6, s.Recent.Count); i++)
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var th = new Thread(() => { var r = PhotoAlbum.Picture(s.Recent[i].Path, PhotoAlbum.CellW, PhotoAlbum.CellH); });
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            th.Join();
            shell.Add(t0.Elapsed.TotalMilliseconds);
            t0.Restart();
            using (var fs = new FileStream(s.Recent[i].Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
                PhotoWatcher.Decode(fs, PhotoAlbum.CellW, PhotoAlbum.CellH);
            dec.Add(t0.Elapsed.TotalMilliseconds);
        }
        string Ms(List<double> l) => l.Count == 0 ? "-" : string.Join(", ", l.ConvertAll(x => x.ToString("0")));
        Log.Info($"albumbench: a cell the album's way (shell first) {Ms(shell)} ms; the file decoded {Ms(dec)} ms");
        if (s.Recent.Count > 0)
        {
            sw.Restart();
            a.Want(s.Recent[0].Path);
            while (sw.Elapsed.TotalSeconds < 10 && a.Get().Recent[0].Thumb == null) Thread.Sleep(10);
            var p0 = a.Get().Recent[0];
            Log.Info($"albumbench: the newest big in {sw.Elapsed.TotalMilliseconds:0} ms ({p0.TW}x{p0.TH}, world known {p0.World != null}, {p0.Players.Length} people)");
        }
        a.Active = false;
        return 0;
    }
}
