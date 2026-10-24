using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Orbit;

// The made-up photos (the snapshot's pictures and checks): scenes drawn here - a sunset sea, a neon street, a meadow, a cosy room, an
// aurora, a mirror shot standing up - with invented worlds and invented people. Nothing of VRChat's real folder is ever read: the
// watcher's own check runs on a folder of these, written into the snapshot's directory.
public static class PhotoFake
{
    public static readonly (string world, string[] people)[] Meta =
    {
        ("Lantern Harbor at Dusk", new[] { "Mira", "Tobi", "Kite", "Vega", "Sable" }),
        ("Neon Arcade 2099", new[] { "Juniper", "Quill" }),
        ("Quiet Meadow", new[] { "Basil" }),
        ("Cloud Atelier - a very long world name that goes on past the edge of every card", new[] { "Lumen", "Moss", "Echo", "Wren", "Static", "Nova", "Pixel" }),
        ("Aurora Overlook", Array.Empty<string>()),
        (null, Array.Empty<string>()),
    };

    // scene k (0..5) as w x h pixels (5: standing up)
    public static Bitmap Scene(int k, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        void Grad(RectangleF r, Color a, Color b) { using var br = new LinearGradientBrush(r, a, b, 90f); g.FillRectangle(br, r); }
        void Disc(float cx, float cy, float rad, Color c) { using var br = new SolidBrush(c); g.FillEllipse(br, cx - rad, cy - rad, 2 * rad, 2 * rad); }
        void Box(float x, float y, float bw, float bh, Color c) { using var br = new SolidBrush(c); g.FillRectangle(br, x, y, bw, bh); }
        void Poly(Color c, params PointF[] pts) { using var br = new SolidBrush(c); g.FillPolygon(br, pts); }
        // an avatar's silhouette: a head with ears, a body
        void Avatar(float cx, float by, float s, Color c)
        {
            Disc(cx, by - 2.1f * s, 0.55f * s, c);
            Poly(c, new PointF(cx - 0.5f * s, by - 2.4f * s), new PointF(cx - 0.35f * s, by - 3.0f * s), new PointF(cx - 0.15f * s, by - 2.55f * s));
            Poly(c, new PointF(cx + 0.5f * s, by - 2.4f * s), new PointF(cx + 0.35f * s, by - 3.0f * s), new PointF(cx + 0.15f * s, by - 2.55f * s));
            using var br = new SolidBrush(c);
            g.FillEllipse(br, cx - 0.7f * s, by - 1.55f * s, 1.4f * s, 2.2f * s);
        }
        var rng = new Random(17 + k);
        switch (k % 6)
        {
            case 0:     // a sunset over the sea, two friends on the pier
                Grad(new RectangleF(0, 0, w, h * 0.62f), Color.FromArgb(255, 58, 36, 112), Color.FromArgb(255, 255, 140, 66));
                Disc(w * 0.62f, h * 0.58f, h * 0.13f, Color.FromArgb(255, 255, 214, 120));
                Grad(new RectangleF(0, h * 0.62f - 1, w, h * 0.38f + 1), Color.FromArgb(255, 40, 52, 110), Color.FromArgb(255, 14, 18, 44));
                for (int i = 0; i < 9; i++) Box(w * 0.62f - h * (0.12f - i * 0.008f), h * (0.65f + i * 0.035f), h * (0.24f - i * 0.016f), 2, Color.FromArgb(170, 255, 200, 120));
                Box(0, h * 0.8f, w * 0.45f, h * 0.05f, Color.FromArgb(255, 20, 14, 24));
                Avatar(w * 0.2f, h * 0.8f, h * 0.1f, Color.FromArgb(255, 20, 14, 24));
                Avatar(w * 0.3f, h * 0.8f, h * 0.085f, Color.FromArgb(255, 20, 14, 24));
                break;
            case 1:     // a neon street at night
                Grad(new RectangleF(0, 0, w, h), Color.FromArgb(255, 12, 10, 40), Color.FromArgb(255, 40, 12, 60));
                for (int i = 0; i < 9; i++)
                {
                    float bx = i * w / 8f - 20, bw = w / 8f + rng.Next(10, 30), bh = h * (0.35f + (float)rng.NextDouble() * 0.45f);
                    Box(bx, h - bh, bw, bh, Color.FromArgb(255, 22 + i * 3, 20, 48));
                    for (int y = (int)(h - bh + 10); y < h - 12; y += 14)
                        for (int x = (int)bx + 6; x < bx + bw - 8; x += 12)
                            if (rng.Next(3) == 0) Box(x, y, 6, 7, rng.Next(2) == 0 ? Color.FromArgb(255, 255, 220, 120) : Color.FromArgb(255, 90, 230, 255));
                }
                Box(w * 0.55f, h * 0.3f, w * 0.22f, h * 0.09f, Color.FromArgb(255, 255, 60, 170));
                Box(w * 0.57f, h * 0.32f, w * 0.18f, h * 0.05f, Color.FromArgb(255, 255, 170, 230));
                Avatar(w * 0.35f, h * 0.98f, h * 0.13f, Color.FromArgb(255, 8, 6, 20));
                break;
            case 2:     // a meadow under clouds
                Grad(new RectangleF(0, 0, w, h * 0.7f), Color.FromArgb(255, 70, 140, 230), Color.FromArgb(255, 190, 225, 255));
                for (int i = 0; i < 4; i++)
                {
                    float cx = w * (0.15f + i * 0.25f), cy = h * (0.18f + (i % 2) * 0.08f);
                    Disc(cx, cy, h * 0.07f, Color.White); Disc(cx + h * 0.07f, cy + 4, h * 0.06f, Color.White); Disc(cx - h * 0.07f, cy + 6, h * 0.05f, Color.White);
                }
                Disc(w * 0.25f, h * 1.25f, h * 0.75f, Color.FromArgb(255, 90, 170, 70));
                Disc(w * 0.8f, h * 1.3f, h * 0.8f, Color.FromArgb(255, 70, 150, 60));
                for (int i = 0; i < 40; i++) Disc(rng.Next(w), h * 0.75f + rng.Next((int)(h * 0.25f)), 2.5f, Color.FromArgb(255, 255, 240 - rng.Next(80), 90 + rng.Next(150)));
                Box(w * 0.66f, h * 0.42f, w * 0.012f, h * 0.2f, Color.FromArgb(255, 90, 60, 40));
                Disc(w * 0.666f, h * 0.4f, h * 0.09f, Color.FromArgb(255, 40, 120, 50));
                break;
            case 3:     // a cosy room, a window on the night
                Grad(new RectangleF(0, 0, w, h), Color.FromArgb(255, 150, 90, 60), Color.FromArgb(255, 70, 40, 30));
                Box(w * 0.55f, h * 0.15f, w * 0.3f, h * 0.4f, Color.FromArgb(255, 30, 40, 80));
                for (int i = 0; i < 14; i++) Disc(w * 0.56f + rng.Next((int)(w * 0.28f)), h * 0.17f + rng.Next((int)(h * 0.36f)), 1.4f, Color.White);
                Box(w * 0.69f, h * 0.15f, 4, h * 0.4f, Color.FromArgb(255, 120, 80, 50));
                Disc(w * 0.18f, h * 0.35f, h * 0.18f, Color.FromArgb(70, 255, 220, 140));
                Disc(w * 0.18f, h * 0.35f, h * 0.06f, Color.FromArgb(255, 255, 230, 160));
                Box(w * 0.1f, h * 0.68f, w * 0.6f, h * 0.2f, Color.FromArgb(255, 120, 50, 60));
                Box(w * 0.08f, h * 0.6f, w * 0.08f, h * 0.28f, Color.FromArgb(255, 105, 42, 52));
                Avatar(w * 0.42f, h * 0.78f, h * 0.11f, Color.FromArgb(255, 245, 235, 225));
                break;
            case 4:     // an aurora over the mountains
                Grad(new RectangleF(0, 0, w, h), Color.FromArgb(255, 6, 10, 30), Color.FromArgb(255, 20, 30, 60));
                for (int i = 0; i < 60; i++) Disc(rng.Next(w), rng.Next((int)(h * 0.6f)), 1.2f, Color.FromArgb(200, 255, 255, 255));
                for (int i = 0; i < 5; i++)
                {
                    using var br = new LinearGradientBrush(new RectangleF(0, h * 0.1f, w, h * 0.4f), Color.FromArgb(0, 80, 255, 160), Color.FromArgb(120, 80, 255, 160), 90f);
                    g.FillEllipse(br, w * (i * 0.2f - 0.1f), h * (0.12f + i * 0.03f), w * 0.4f, h * 0.18f);
                }
                Poly(Color.FromArgb(255, 14, 18, 28), new PointF(0, h), new PointF(0, h * 0.7f), new PointF(w * 0.22f, h * 0.48f), new PointF(w * 0.4f, h * 0.7f),
                    new PointF(w * 0.6f, h * 0.52f), new PointF(w * 0.85f, h * 0.72f), new PointF(w, h * 0.6f), new PointF(w, h));
                break;
            default:    // a mirror shot, standing up
                Grad(new RectangleF(0, 0, w, h), Color.FromArgb(255, 210, 200, 235), Color.FromArgb(255, 120, 110, 170));
                Box(w * 0.1f, h * 0.06f, w * 0.8f, h * 0.88f, Color.FromArgb(255, 235, 240, 250));
                Box(w * 0.13f, h * 0.08f, w * 0.74f, h * 0.84f, Color.FromArgb(255, 170, 190, 220));
                Avatar(w * 0.5f, h * 0.9f, h * 0.2f, Color.FromArgb(255, 60, 50, 90));
                Disc(w * 0.42f, h * 0.43f, h * 0.02f, Color.FromArgb(255, 255, 120, 180));
                break;
        }
        return bmp;
    }

    // the scene as a thumbnail (premultiplied BGRA), the way the watcher makes them
    public static (uint[] px, int w, int h) Pixels(int k, int w, int h)
    {
        using var bmp = Scene(k, w, h);
        var px = new uint[w * h];
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try { for (int y = 0; y < h; y++) Marshal.Copy(bd.Scan0 + y * bd.Stride, (int[])(object)px, y * w, w); }
        finally { bmp.UnlockBits(bd); }
        return (px, w, h);
    }

    // made-up photos taken today, the newest first: scene i, minutes ago, its metadata (an index into Meta)
    public static PhotoWatcher.Snapshot Snap(int n = 5, bool metaless = false)
    {
        var s = new PhotoWatcher.Snapshot { Version = 1, Ready = true, FolderFound = true, Folder = @"C:\made-up\VRChat" };
        var now = DateTime.Now;
        int[] ago = { 3, 9, 25, 47, 80, 120, 150 };
        for (int i = 0; i < n; i++)
        {
            int k = i % 6;
            bool tall = k == 5;
            var (px, w, h) = Pixels(k, tall ? 225 : 640, tall ? 400 : 360);
            var taken = now.AddMinutes(-ago[i % ago.Length] - i / ago.Length * 200);
            var (world, people) = metaless ? ((string)null, Array.Empty<string>()) : Meta[i % Meta.Length];
            string name = $"VRChat_{taken:yyyy-MM-dd_HH-mm-ss.fff}_{(tall ? "1080x1920" : "1920x1080")}.png";
            s.Recent.Add(new PhotoWatcher.Photo
            {
                Path = @"C:\made-up\VRChat\" + taken.ToString("yyyy-MM") + @"\" + name, Name = name, Taken = taken, Width = tall ? 1080 : 1920,
                Height = tall ? 1920 : 1080, World = world, Players = people, Thumb = px, TW = w, TH = h, Seen = now,
            });
        }
        return s;
    }

    public static PhotoWatcher.Snapshot None() => new() { Version = 1, Ready = true, FolderFound = true, Folder = @"C:\made-up\VRChat" };

    // a scene as a PNG file's bytes; meta: VRCX's metadata JSON in an iTXt chunk before IEND (made-up world and people)
    public static byte[] Png(int k, int w, int h, int meta = -1)
    {
        using var bmp = Scene(k, w, h);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        var b = ms.ToArray();
        if (meta < 0) return b;
        var (world, people) = Meta[meta];
        var json = new StringBuilder("{\"application\":\"VRCX\",\"version\":1,\"author\":{\"id\":\"usr_made-up\",\"displayName\":\"Photographer\"},");
        json.Append("\"world\":{\"id\":\"wrld_made-up\",\"name\":\"").Append(world).Append("\",\"instanceId\":\"wrld_made-up:12345~friends(usr_made-up)\"},\"players\":[");
        for (int i = 0; i < people.Length; i++) json.Append(i > 0 ? "," : "").Append("{\"id\":\"usr_").Append(i).Append("\",\"displayName\":\"").Append(people[i]).Append("\"}");
        json.Append("]}");
        var data = new List<byte>();
        data.AddRange(Encoding.Latin1.GetBytes("Description"));
        data.AddRange(new byte[] { 0, 0, 0, 0, 0 });              // the keyword's end, not compressed, method, no language, no translation
        data.AddRange(Encoding.UTF8.GetBytes(json.ToString()));
        var chunk = new List<byte>();
        int len = data.Count;
        chunk.AddRange(new[] { (byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len });
        var typed = new List<byte>(Encoding.ASCII.GetBytes("iTXt"));
        typed.AddRange(data);
        chunk.AddRange(typed);
        uint crc = Crc(typed.ToArray());
        chunk.AddRange(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc });
        var o = new List<byte>(b[..^12]);
        o.AddRange(chunk);
        o.AddRange(b[^12..]);
        return o.ToArray();
    }

    static uint Crc(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte x in d)
        {
            c ^= x;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}

// --snapshot's part for the latest photo widget (WristPhoto.cs), on made-up photos only: the watcher on a folder of made-up PNGs (a new
// file shows up in the widget only once it is whole, VRCX's rewrite adds its world), a tap opens the newest, the arrows and the stick
// step through the day's photos, Open in VR Gallery asks for the photo shown (nothing is opened by a tool), X / OK close it, the
// empty state; the pictures wrist_photo_<case><tag>.png and, after the styles, photo_preview.png (every style's face and opened view).
static class PhotoViewTools
{
    static void Draw2(WristPanel wrist, PanelData data) { wrist.Draw(data); wrist.Draw(data); }

    static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    static void Settle(WristPanel wrist, PanelData data)
    {
        for (int k = 0; k < 11; k++) { Thread.Sleep(40); wrist.Draw(data); }
    }

    static Vector2 Middle(RectangleF c)
    {
        float s = Theme.Classic ? 2 : 1;
        return new Vector2((c.X + c.Width / 2) * s, (c.Y + c.Height / 2) * s);
    }

    static string Name(string path) => path == null ? "none" : Path.GetFileName(path);

    // a face around it: the widget's sizes and a few others
    static List<WatchWidget> Face() => new()
    {
        new() { Type = "time", X = 0, Y = 0, W = 2, H = 1 }, new() { Type = "photo", X = 2, Y = 0, W = 2, H = 2 }, new() { Type = "photo", X = 4, Y = 0, W = 2, H = 1 },
        new() { Type = "battery", X = 0, Y = 1, W = 1, H = 1 }, new() { Type = "photo", X = 1, Y = 1, W = 1, H = 1 },
        new() { Type = "notifications", X = 4, Y = 1, W = 2, H = 1 }, new() { Type = "media", X = 0, Y = 2, W = 4, H = 1 }, new() { Type = "fps", X = 4, Y = 2, W = 1, H = 1 },
        new() { Type = "volume", X = 5, Y = 2, W = 1, H = 1 },
        new() { Type = "keyboard", X = 0, Y = 3 }, new() { Type = "desktops", X = 1, Y = 3 }, new() { Type = "recenter", X = 2, Y = 3 },
        new() { Type = "layout", X = 3, Y = 3 }, new() { Type = "night", X = 4, Y = 3 }, new() { Type = "settings", X = 5, Y = 3 },
    };

    public static readonly List<(string tag, string face, string open)> Preview = new();

    // what a frame allocates with the widget at rest (its three sizes) and opened at rest: nothing is the goal - the thumbnails are
    // bitmaps once, the words made when the photo or the minute changes
    public static void Perf(WristPanel wrist, PanelData data, ConfigStore cfg, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var photos0 = data.Photos;
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            wcfg.Widgets = new List<WatchWidget>
            {
                new() { Type = "photo", X = 0, Y = 0, W = 2, H = 2 }, new() { Type = "photo", X = 2, Y = 0, W = 2, H = 1 }, new() { Type = "photo", X = 4, Y = 0, W = 1, H = 1 },
            };
            data.Photos = PhotoFake.Snap(5);
            long Frame()
            {
                for (int i = 0; i < 6; i++) wrist.Draw(data);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 30; i++) wrist.Draw(data);
                return (GC.GetAllocatedBytesForCurrentThread() - a0) / 30;
            }
            wrist.Settle();
            long face = Frame();
            wrist.OpenForTest("photo");
            long open = Frame();
            wrist.CloseForTest();
            // the same window of another widget, for comparison (the style's own chrome)
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "volume", X = 0, Y = 0, W = 2, H = 1 } };
            wrist.Settle();
            wrist.OpenForTest("volume");
            long other = Frame();
            wrist.CloseForTest();
            Log.Info($"photo perf{tag}: the widget at rest {face} B a frame, opened {open} B a frame (want 0, 0; the volume opened: {other} B)");
        }
        finally
        {
            wrist.CloseForTest();
            data.Photos = photos0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var photos0 = data.Photos;
        var open0 = wrist.OnOpenPhoto;
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            Checks(wrist, data, wcfg, tag);
            Pictures(wrist, data, wcfg, gpu, dir, tag);
        }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            wrist.OnOpenPhoto = open0;
            data.Photos = photos0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    static void Checks(WristPanel wrist, PanelData data, WristConfig wcfg, string tag)
    {
        bool classic = Theme.Classic;
        string prevId = classic ? "wph95p" : "wphprev", nextId = classic ? "wph95n" : "wphnext", xId = classic ? "wd95x" : "wclose";
        string asked = null;
        wrist.OnOpenPhoto = p => asked = p;
        var snap = PhotoFake.Snap(5);
        data.Photos = snap;
        wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 0, Y = 2, W = 2, H = 1 } };
        wrist.Settle();
        Draw2(wrist, data);
        bool shows = wrist.PhotoWidgetShown == snap.Recent[0].Path;
        // a tap on the card opens it on the newest
        Tap(wrist, data, Middle(WatchWidgets.CellRect(0, 2, 2, 1, classic)));
        Settle(wrist, data);
        bool opened = wrist.IsOpen && wrist.DetailKind == "photo";
        bool newest = wrist.PhotoOpenShown == snap.Recent[0].Path;
        // the arrow: the one before; the stick down: the one before that, up: back; the other arrow: the newest again
        string Step(string id)
        {
            var at = wrist.ControlPoint(data, id);
            if (at is { } a) Tap(wrist, data, a);
            return at == null ? "no control" : Name(wrist.PhotoOpenShown);
        }
        bool prev = Step(prevId) == Name(snap.Recent[1].Path);
        var mid = Middle(WatchWidgets.CellRect(0, 0, WatchWidgets.Cols, WatchWidgets.Rows, classic));
        wrist.PointerMove(Hand.Left, mid); wrist.Draw(data);
        wrist.PointerScroll(Hand.Left, -3, mid); wrist.Draw(data);
        bool down = wrist.PhotoOpenShown == snap.Recent[2].Path;
        Thread.Sleep(400);
        wrist.PointerScroll(Hand.Left, 3, mid); wrist.Draw(data);
        bool up = wrist.PhotoOpenShown == snap.Recent[1].Path;
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
        bool next = Step(nextId) == Name(snap.Recent[0].Path);
        // Open in VR Gallery: asks the app for the photo shown (the tool's hand-over opens nothing)
        Step(prevId);
        var g = wrist.ControlPoint(data, "wphgal");
        if (g is { } gp) Tap(wrist, data, gp);
        bool gallery = asked == snap.Recent[1].Path && wrist.IsOpen;
        // a new photo while it is open on an older one: it stays there
        var stay = new PhotoWatcher.Snapshot { Version = 2, Ready = true, FolderFound = true };
        stay.Recent.Add(PhotoFake.Snap(6).Recent[5]);
        stay.Recent.AddRange(snap.Recent);
        data.Photos = stay;
        Draw2(wrist, data);
        bool stays = wrist.PhotoOpenShown == snap.Recent[1].Path;
        data.Photos = snap;
        Draw2(wrist, data);
        // its X closes it (the retro styles' OK / Close too)
        var x = wrist.ControlPoint(data, xId);
        if (x is { } xp) Tap(wrist, data, xp);
        bool shut = x != null && !wrist.IsOpen;
        Settle(wrist, data);
        string ok = "";
        if (classic && Theme.Retro != RetroStyle.Aero)
        {
            wrist.Settle();
            wrist.OpenForTest("photo");
            Draw2(wrist, data);
            var o = wrist.ControlPoint(data, "wd95ok");
            if (o is { } op) Tap(wrist, data, op);
            bool okShut = o != null && !wrist.IsOpen;
            Settle(wrist, data);
            ok = $"; its OK / Close closed it {okShut} (want True)";
        }
        wrist.CloseForTest();
        Log.Info($"photo view{tag}: the widget shows the newest {shows} (want True); a tap opened it {opened} (want True) on the newest {newest} (want True); " +
                 $"the arrow stepped to the one before {prev} (want True), the stick down {down} and up {up} (want True True), the other arrow back {next} " +
                 $"(want True); Open in VR Gallery asked for the photo shown {gallery} (want True); a new photo kept the view on its photo {stays} (want True); " +
                 $"its X closed it {shut} (want True){ok}");
        // none: the empty state, on the face and opened
        data.Photos = PhotoFake.None();
        wrist.Settle();
        Draw2(wrist, data);
        bool emptyFace = wrist.PhotoEmptyShown && wrist.PhotoWidgetShown == null;
        wrist.OpenForTest("photo");
        Draw2(wrist, data);
        bool emptyOpen = wrist.PhotoEmptyShown && wrist.PhotoOpenShown == null;
        var g2 = wrist.ControlPoint(data, "wphgal");
        asked = null;
        if (g2 is { } g2p) Tap(wrist, data, g2p);
        wrist.CloseForTest();
        Log.Info($"photo view{tag}: no photos - the widget's empty state {emptyFace} (want True), opened {emptyOpen} (want True), Open in VR Gallery does nothing " +
                 $"{asked == null} (want True)");
    }

    static void Pictures(WristPanel wrist, PanelData data, WristConfig wcfg, Gpu gpu, string dir, string tag)
    {
        string Shot(string name, PhotoWatcher.Snapshot s, bool open, int steps = 0)
        {
            data.Photos = s;
            wcfg.Widgets = Face();
            wrist.Settle();
            if (open && !wrist.OpenForTest("photo")) Log.Warn("photo view: no photo widget to open");
            Draw2(wrist, data);
            for (int i = 0; i < steps; i++)
            {
                var at = wrist.ControlPoint(data, Theme.Classic ? "wph95p" : "wphprev");
                if (at is { } a) Tap(wrist, data, a);
                wrist.PointerLeave(Hand.Left);
            }
            if (steps > 0) { wrist.Settle(); Draw2(wrist, data); }
            string file = Path.Combine(dir, $"wrist_photo_{name}{tag}.png");
            wrist.Surf.SavePng(gpu, file);
            wrist.CloseForTest();
            return file;
        }
        string face = Shot("face", PhotoFake.Snap(5), false);
        string open = Shot("open", PhotoFake.Snap(5), true);
        Shot("open_older", PhotoFake.Snap(5), true, 3);
        Shot("open_tall", PhotoFake.Snap(6), true, 5);
        Shot("open_nometa", PhotoFake.Snap(5, true), true);
        Shot("none", PhotoFake.None(), false);
        Shot("none_open", PhotoFake.None(), true);
        Preview.RemoveAll(p => p.tag == tag);
        Preview.Add((tag, face, open));
        Log.Info($"photo view{tag}: 7 pictures (wrist_photo_*{tag}.png)");
    }

    // every style's face and opened view side by side, one row a style: photo_preview.png
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
                var (t, face, open) = Preview[i];
                float y = Gap + i * (H + Label + Gap);
                string style = t.Length == 0 ? "modern" : t[1..];
                g.DrawString($"{style}: the face, the photo opened", font, Brushes.White, Gap, y + 4);
                using (var a = new Bitmap(face)) g.DrawImage(a, Gap, y + Label, W, H);
                using (var b = new Bitmap(open)) g.DrawImage(b, 2 * Gap + W, y + Label, W, H);
            }
        }
        string path = Path.Combine(dir, "photo_preview.png");
        sheet.Save(path, ImageFormat.Png);
        Log.Info($"photo view: the styles' faces and opened views side by side: {path}");
    }

    // The watcher itself on a folder of made-up photos (in the snapshot's directory): the newest found at the start; a new file written
    // in two parts is not taken while it is half there; once whole it shows up in the widget (it pops, Motion Full); VRCX's rewrite with
    // its metadata adds the world; switched off, it lets everything go
    public static void WatcherChecks(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var photos0 = data.Photos;
        bool still = Motion.Still;
        string root = Path.Combine(dir, "photo_folder");
        using var w = new PhotoWatcher { FolderOverride = root };
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            var now = DateTime.Now;
            string month = Path.Combine(root, now.ToString("yyyy-MM"));
            Directory.CreateDirectory(month);
            string NameAt(DateTime t, int k) => Path.Combine(month, $"VRChat_{t:yyyy-MM-dd_HH-mm-ss.fff}_{(k == 5 ? "360x640" : "640x360")}.png");
            // an empty folder: looked at, no photos; the first photo into it found (then taken out again, for what follows)
            wcfg.Face = "custom";
            w.Active = true;
            PhotoWatcher.Snapshot s = null;
            bool Wait(Func<PhotoWatcher.Snapshot, bool> ok, double seconds)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < seconds)
                {
                    s = w.Get();
                    if (ok(s)) return true;
                    Thread.Sleep(100);
                }
                return false;
            }
            bool none = Wait(x => x.Ready && x.FolderFound && x.Recent.Count == 0, 4);
            string first = NameAt(now.AddMinutes(-4), 4);
            File.WriteAllBytes(first, PhotoFake.Png(4, 640, 360));
            bool firstIn = Wait(x => x.Recent.Count == 1 && x.Recent[0].Path == first && x.Recent[0].Thumb != null, 8);
            w.Active = false;
            Wait(x => !x.Ready, 3);
            File.Delete(first);
            Log.Info($"photo watcher: an empty folder - looked at, no photos {none} (want True); the first photo into it found {firstIn} (want True)");
            // three of today's already there (an older day's one too), the newest with its metadata
            File.WriteAllBytes(Path.Combine(month, $"VRChat_{now.AddDays(-2):yyyy-MM-dd_HH-mm-ss.fff}_640x360.png"), PhotoFake.Png(4, 640, 360));
            string old1 = NameAt(now.AddMinutes(-3), 0), old2 = NameAt(now.AddMinutes(-2), 1), old3 = NameAt(now.AddMinutes(-1), 2);
            File.WriteAllBytes(old1, PhotoFake.Png(0, 640, 360));
            File.WriteAllBytes(old2, PhotoFake.Png(1, 640, 360));
            File.WriteAllBytes(old3, PhotoFake.Png(2, 640, 360, 2));
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 0, Y = 0, W = 2, H = 2 } };
            w.Active = true;
            bool start = Wait(x => x.Ready && x.Recent.Count == 3 && x.Recent[0].Path == old3 && x.Recent[0].Thumb != null, 6);
            bool startMeta = start && s.Recent[0].World == PhotoFake.Meta[2].world && s.Recent[0].Players.Length == PhotoFake.Meta[2].people.Length;
            data.Photos = s;
            wrist.Settle();
            Draw2(wrist, data);
            bool startShown = wrist.PhotoWidgetShown == old3;
            // a new one, written in two parts a second apart (VRChat writing it): not taken half written
            Motion.Still = false;
            string fresh = NameAt(DateTime.Now, 3);
            var bytes = PhotoFake.Png(3, 640, 360);
            using (var fs = new FileStream(fresh, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                fs.Write(bytes, 0, bytes.Length / 2);
                fs.Flush();
                Thread.Sleep(2600);
                s = w.Get();
                bool half = s.Recent.Exists(p => p.Path == fresh);
                fs.Write(bytes, bytes.Length / 2, bytes.Length - bytes.Length / 2);
                Log.Info($"photo watcher: a file half written was left alone {!half} (want True)");
            }
            bool arrived = Wait(x => x.Recent.Count > 0 && x.Recent[0].Path == fresh && x.Recent[0].Thumb != null, 8);
            bool freshFlag = arrived && s.Recent[0].Fresh && s.Recent[0].World == null;
            data.Photos = s;
            wrist.Draw(data);
            bool pops = wrist.PhotoPopping;
            Thread.Sleep(120);
            wrist.Draw(data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, "wrist_photo_pop.png"));      // a moment into the pop: the spring, the flash, the ring
            Draw2(wrist, data);
            bool shown = wrist.PhotoWidgetShown == fresh;
            wrist.Settle();
            Thread.Sleep(650);
            Draw2(wrist, data);
            Motion.Still = still;
            // VRCX writes it again with its metadata: the world and the people come, the picture stays
            var thumb = s.Recent[0].Thumb;
            File.WriteAllBytes(fresh, PhotoFake.Png(3, 640, 360, 0));
            bool meta = Wait(x => x.Recent.Count > 0 && x.Recent[0].Path == fresh && x.Recent[0].World == PhotoFake.Meta[0].world, 8);
            bool kept = meta && ReferenceEquals(s.Recent[0].Thumb, thumb) && s.Recent[0].Players.Length == PhotoFake.Meta[0].people.Length;
            data.Photos = s;
            Draw2(wrist, data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, "wrist_photo_watcher.png"));
            // the opened view asks for an older photo's thumbnail: made
            bool lazy = s.Recent.Count >= 3 && s.Recent[2].Thumb == null;
            w.Want(s.Recent[2].Path);
            bool lazyMade = Wait(x => x.Recent.Count >= 3 && x.Recent[2].Thumb != null, 5);
            // switched off: nothing kept
            w.Active = false;
            bool off = Wait(x => !x.Ready && x.Recent.Count == 0, 3);
            Log.Info($"photo watcher: the newest at the start {start} (want True), its world and people {startMeta} (want True), in the widget {startShown} " +
                     $"(want True); a new file shows up in the widget {arrived && shown} (want True), new and without metadata yet {freshFlag} (want True), " +
                     $"it popped {pops} (want True); VRCX's metadata read again {meta} (want True), its picture kept {kept} (want True); an older one's " +
                     $"thumbnail waits {lazy} (want True), made when asked {lazyMade} (want True); switched off, it lets go {off} (want True)");
        }
        catch (Exception e) { Log.Error("photo watcher: " + e); }
        finally
        {
            Motion.Still = still;
            w.Active = false;
            data.Photos = photos0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
            try { Thread.Sleep(200); if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
        // the pictures' own tags: VRChat's name formats read
        var t1 = PhotoWatcher.TakenOf(@"x\VRChat_2026-10-17_14-20-05.123_1920x1080.png");
        var t2 = PhotoWatcher.TakenOf(@"x\VRChat_1920x1080_2022-01-02_03-04-05.006.png");
        Log.Info($"photo watcher: the time from the name {t1:yyyy-MM-dd HH:mm:ss.fff} (want 2026-10-17 14:20:05.123), the old name {t2:yyyy-MM-dd HH:mm:ss.fff} " +
                 $"(want 2022-01-02 03:04:05.006); the day of 01:30 {PhotoWatcher.DayOf(new DateTime(2026, 10, 18, 1, 30, 0)):MM-dd} (want 10-17)");
    }
}

static partial class Tools
{
    // ORBIT_SNAP_ONLY=photo: the latest photo's checks, pictures and layout check (its sizes and opened only) in every style, the watcher
    // on its made-up folder, the preview sheet - in seconds
    static int PhotoOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = Tools.SnapCulture;
        PhotoViewTools.WatcherChecks(wrist, data, cfg, gpu, dir);
        GalleryViewTools.AlbumChecks(dir);
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            PhotoViewTools.Run(wrist, data, cfg, gpu, dir, tag);
            PhotoViewTools.Perf(wrist, data, cfg, tag);
            GalleryViewTools.Run(wrist, data, cfg, gpu, dir, tag);
            GalleryViewTools.Perf(wrist, data, cfg, tag);
            if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1")
            {
                // what the opened view allocates, by type (ToolsPerf's sampler)
                var w0 = (cfg.Data.Wrist.Face, cfg.Data.Wrist.Widgets);
                cfg.Data.Wrist.Face = "custom";
                cfg.Data.Wrist.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 0, Y = 0, W = 2, H = 1 } };
                wrist.Settle();
                wrist.OpenForTest("photo");
                AllocTypes("photo opened" + tag, () => wrist.Draw(data), null);
                wrist.CloseForTest();
                (cfg.Data.Wrist.Face, cfg.Data.Wrist.Widgets) = w0;
            }
            string f0 = cfg.Data.Wrist.Face;
            LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag.Length > 0 ? tag[1..] : "modern", onlyKind: "photo");
            cfg.Data.Wrist.Face = f0;
            cfg.Data.Wrist.Widgets = null;
        }
        PhotoViewTools.PreviewSheet(dir);
        GalleryViewTools.PreviewSheet(dir);
        System.Globalization.CultureInfo.CurrentCulture = culture;
        Log.Info("snapshot (photo only) written to " + dir);
        return 0;
    }
}
