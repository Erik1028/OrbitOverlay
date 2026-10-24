using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Orbit;

// --snapshot's part for the Hexpad widget opened on the watch (WristDock.cs), on made-up dock data - never the real Hexpad: the
// watch's DockInput here has no link, what it would send is only written down. A tap on the dock card (every size) opens the
// watch's opened view - not the floating Hexpad panel -, a tap on a key sends it down and up, a key that could end the session
// asks first, the X shrinks it back and closes it; and the pictures wrist_open_dock_<case><tag>.png (connected, a key pressed, a
// slider dragged, the question, Hexpad not running, not connected). tag: "" modern, "_95" the 95 style.
static class DockViewTools
{
    // The link's faces while the dock is not seen (HexpadLink.Showing, Erik 10-24): made-up faces fed as Hexpad's lines (no pipe is
    // opened) - not seen, nothing is decoded and the snapshot does not change; seen again, the latest face of each key is there (a
    // newer one of the same key wins, a new device face clears the sharp one); seen, a face is decoded as it comes. Then what a faces
    // message makes while not seen (its texts kept in the keys' own buffers, 10-24: they were strings, ~1 MB/s): bytes a message after
    // a warm-up, and the faces kept that way decoded to the very pixels a seen link makes of the same message; the odd lines too ("hi"
    // before "keys", "t" last, an escaped '/', a null key, a text that is not base64, "dark")
    public static void LinkFacesChecks()
    {
        static string Jpeg(System.Drawing.Color c, int size)
        {
            using var bmp = new System.Drawing.Bitmap(size, size);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.Clear(c);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            return Convert.ToBase64String(ms.ToArray());
        }
        string red = Jpeg(System.Drawing.Color.Red, 68), blue = Jpeg(System.Drawing.Color.Blue, 68), green = Jpeg(System.Drawing.Color.Lime, 68),
            hi = Jpeg(System.Drawing.Color.Yellow, 136);
        static uint Mid(HexpadLink.Face f) => f == null ? 0 : f.Px[f.H / 2 * f.W + f.W / 2] & 0xFFFFFF;
        using var link = new HexpadLink();
        link.Showing = false;
        int v0 = link.Version;
        link.FeedForTest($"{{\"t\":\"faces\",\"keys\":{{\"0\":\"{red}\",\"1\":\"{blue}\"}},\"hi\":{{\"1\":\"{hi}\"}}}}");
        link.FeedForTest($"{{\"t\":\"faces\",\"keys\":{{\"0\":\"{green}\"}}}}");
        var s0 = link.Get();
        bool waited = link.Version == v0 && s0.Faces[0] == null && s0.Faces[1] == null;
        link.Showing = true;
        link.SeenForTest();
        var s1 = link.Get();
        bool latest = s1.Faces[0] != null && (Mid(s1.Faces[0]) & 0x00FF00) > 0x00C000 && (Mid(s1.Faces[0]) & 0xFF0000) < 0x400000;     // green, not red
        bool hiKept = s1.HiFaces[1] != null && s1.HiFaces[1].W == 136 && s1.Faces[1] != null && s1.HiFaces[0] == null;
        link.FeedForTest($"{{\"t\":\"faces\",\"keys\":{{\"1\":\"{red}\"}}}}");
        var s2 = link.Get();
        bool live = s2.Faces[1] != null && (Mid(s2.Faces[1]) & 0xFF0000) > 0xC00000 && s2.HiFaces[1] == null && s2.FaceVersion[1] > s1.FaceVersion[1];
        Log.Info($"hexpad faces: not seen - nothing decoded, the snapshot unchanged {waited} (want True); seen again - each key's latest {latest} (want True), " +
                 $"the sharp face kept, none where a newer device face came {hiKept} (want True); seen - a face decoded as it comes, its sharp one cleared {live} (want True)");

        // the odd lines, not seen: keys 4 and 5 shown first (seen), then a null and a text that is not base64 for them - both gone after
        link.FeedForTest($"{{\"t\":\"faces\",\"keys\":{{\"4\":\"{green}\",\"5\":\"{green}\"}}}}");
        bool shown45 = link.Get().Faces[4] != null && link.Get().Faces[5] != null;
        link.Showing = false;
        string esc = blue.Replace("/", "\\/");          // (JSON may write '/' as "\/": the text is the same)
        link.FeedForTest($"{{\"hi\":{{\"2\":\"{hi}\"}},\"keys\":{{\"2\":\"{red}\",\"3\":\"{esc}\",\"4\":null,\"5\":\"not base64!\"}},\"dark\":true,\"t\":\"faces\"}}");
        link.Showing = true;
        link.SeenForTest();
        var s3 = link.Get();
        bool odd = shown45 && esc != blue && s3.Faces[2] != null && (Mid(s3.Faces[2]) & 0xFF0000) > 0xC00000 && s3.HiFaces[2] is { W: 136 } &&
                   s3.Faces[3] != null && (Mid(s3.Faces[3]) & 0xFF) > 0xC0 && s3.Faces[4] == null && s3.Faces[5] == null && s3.Dark;

        // what a faces message makes, not seen: six device faces and six sharp ones a message, as Hexpad sends them while keys animate
        // (pictures with detail: JPEGs of a few KB, like real faces), fed as the reader's buffer holds a line; a warm-up first (the
        // keys' buffers grown once, the code compiled)
        var rng = new Random(7);
        string Busy(int size)
        {
            using var bmp = new System.Drawing.Bitmap(size, size);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));
                for (int k = 0; k < 24; k++)
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)), 1 + rng.Next(4)))
                        g.DrawLine(pen, rng.Next(size), rng.Next(size), rng.Next(size), rng.Next(size));
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            return Convert.ToBase64String(ms.ToArray());
        }
        var lines = new List<byte[]>();
        long strings = 0;
        for (int v = 0; v < 4; v++)
        {
            var sb = new StringBuilder("{\"t\":\"faces\",\"dark\":false,\"keys\":{");
            for (int i = 0; i < HexpadLink.Keys; i++)
            {
                string f = Busy(68);
                sb.Append(i > 0 ? "," : "").Append('"').Append(i).Append("\":\"").Append(f).Append('"');
                strings += 22 + 2L * f.Length;
            }
            sb.Append("},\"hi\":{");
            for (int i = 0; i < HexpadLink.Keys; i++)
            {
                string f = Busy(136);
                sb.Append(i > 0 ? "," : "").Append('"').Append(i).Append("\":\"").Append(f).Append('"');
                strings += 22 + 2L * f.Length;
            }
            lines.Add(Encoding.UTF8.GetBytes(sb.Append("}}").ToString()));
        }
        strings /= lines.Count;
        link.Showing = false;
        for (int k = 0; k < 5; k++) foreach (var l in lines) link.FeedForTest(l, l.Length);
        const int N = 400;
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < N; k++) { var l = lines[k % lines.Count]; link.FeedForTest(l, l.Length); }
        long per = (GC.GetAllocatedBytesForCurrentThread() - a0) / N;
        // seen again: each key's faces the very pixels a seen link decodes from the last message
        link.Showing = true;
        link.SeenForTest();
        var s4 = link.Get();
        using var seen = new HexpadLink();
        var lastLine = lines[(N - 1) % lines.Count];
        seen.FeedForTest(lastLine, lastLine.Length);
        var s5 = seen.Get();
        static bool Same(HexpadLink.Face a, HexpadLink.Face b) => a != null && b != null && a.W == b.W && a.H == b.H && a.Px.AsSpan().SequenceEqual(b.Px);
        bool same = true;
        for (int i = 0; i < HexpadLink.Keys; i++) same &= Same(s4.Faces[i], s5.Faces[i]) && Same(s4.HiFaces[i], s5.HiFaces[i]);
        Log.Info($"hexpad faces, not seen: a message of six device faces and six sharp ones ({lastLine.Length / 1024} KB) made {per} B (want under 64; " +
                 $"kept as strings, as before, its texts were {strings / 1024} KB); after {N} of them " +
                 $"each key's faces the very pixels a seen link decodes from the last {same} (want True); the odd lines - \"hi\" before \"keys\", \"t\" last, " +
                 $"an escaped '/', a null key, a text that is not base64, \"dark\" - kept as a seen link has them {odd} (want True)");
    }

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        string face0 = wcfg.Face, bg0 = wcfg.Background;
        var widgets0 = wcfg.Widgets;
        var dock0 = data.Dock;
        var faces0 = data.DockFaces;
        var act0 = wrist.OnAction;
        using var faces = new HexpadFaces();
        try
        {
            var fake = Fake();
            faces.Update(painter, fake);
            data.Dock = fake;
            data.DockFaces = faces;
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            Taps(wrist, data, wcfg, tag);
            Pictures(wrist, data, wcfg, gpu, dir, tag, fake);
        }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            wrist.DockIn.Pose(new HexpadLink.Snapshot());
            wrist.DockIn.AfterPose();
            wrist.OnAction = act0;
            data.Dock = dock0;
            data.DockFaces = faces0;
            wcfg.Face = face0;
            wcfg.Background = bg0;
            wcfg.Widgets = widgets0;
        }
    }

    // a colour per key with its number (the sharp faces with a ring too), a dangerous key, the knobs: two abs sliders and a jog
    internal static HexpadLink.Snapshot Fake()
    {
        var f = new HexpadLink.Snapshot
        {
            State = HexpadLink.LinkState.Ok, Api = 2, Caps = new[] { "hi", "values", "set", "jog" },
            Profile = "Default", PageName = "Home", Page = 0, Pages = 10, Version = 1,
        };
        uint[] cols = { 0xFF2D6CDF, 0xFFE0473C, 0xFF3CB371, 0xFFE8A33D, 0xFF8E5BD8, 0xFF2BB5B8 };
        for (int i = 0; i < HexpadLink.Keys; i++)
        {
            var px = new uint[68 * 68];
            for (int y = 0; y < 68; y++)
                for (int x = 0; x < 68; x++)
                    px[y * 68 + x] = x < 3 || y < 3 || x > 64 || y > 64 ? 0xFFFFFFFF : x > 8 && x < 8 + 6 * (i + 1) && y > 8 && y < 16 ? 0xFF000000 : cols[i];
            f.Faces[i] = new HexpadLink.Face(px, 68, 68);
            var hp = new uint[136 * 136];
            for (int y = 0; y < 136; y++)
                for (int x = 0; x < 136; x++)
                {
                    float d = MathF.Sqrt((x - 68) * (x - 68) + (y - 80) * (y - 80));
                    hp[y * 136 + x] = x < 6 || y < 6 || x > 129 || y > 129 ? 0xFFFFFFFF : x > 16 && x < 16 + 12 * (i + 1) && y > 16 && y < 32 ? 0xFF000000
                        : MathF.Abs(d - 34) < 1.5f ? 0xFFFFFFFF : cols[i];
                }
            f.HiFaces[i] = new HexpadLink.Face(hp, 136, 136);
            f.FaceVersion[i] = 1000 + i;
        }
        f.Controls = new Dictionary<string, HexpadLink.Control>
        {
            ["key1"] = new() { Label = "Clock" }, ["key2"] = new() { Label = "Folder" }, ["key3"] = new() { Label = "System: sleep", Danger = true },
            ["key4"] = new() { Label = "Media: play" }, ["key5"] = new() { Label = "Folder" }, ["key6"] = new() { Label = "Discord: mute" },
            ["btn7"] = new() { Label = "Page: prev" }, ["btn8"] = new() { Label = "Page: goto" }, ["btn9"] = new() { Label = "Page: next" },
            ["enc0"] = new() { Label = "Bright", Left = "Brightness: down", Right = "Brightness: up", Push = "Brightness: set", Slider = "abs", Kind = "brightness" },
            ["enc1"] = new() { Label = "Volume", Left = "Volume: down", Right = "Volume: up", Push = "Volume: mute", Slider = "abs", Kind = "volume" },
            ["enc2"] = new() { Label = "Light", Left = "Smartlight: brightness_down", Right = "Smartlight: brightness_up", Push = "Smartlight: toggle",
                               Slider = "jog", Kind = "smartlight" },
        };
        f.Values = new Dictionary<string, HexpadLink.Value> { ["enc0"] = new() { V = 0.3f }, ["enc1"] = new() { V = 0.42f } };
        return f;
    }

    static void Draw2(WristPanel wrist, PanelData data) { wrist.Draw(data); wrist.Draw(data); }

    static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
    }

    static void Settle(WristPanel wrist, PanelData data)
    {
        for (int k = 0; k < 11; k++) { Thread.Sleep(40); wrist.Draw(data); }
    }

    static Vector2 Middle(System.Drawing.RectangleF c)
    {
        float s = Theme.Classic ? 2 : 1;          // the 95 canvas is shown 2x
        return new Vector2((c.X + c.Width / 2) * s, (c.Y + c.Height / 2) * s);
    }

    static void Taps(WristPanel wrist, PanelData data, WristConfig wcfg, string tag)
    {
        string action = null;
        wrist.OnAction = a => action ??= a;
        var sent = wrist.DockIn.Sent;
        var miss = new List<string>();
        int n = 0;
        foreach (var (w, h) in WatchWidgets.Find("dock").SizesFor(Theme.Classic))
        {
            n++;
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "dock", X = 2, Y = 1, W = w, H = h } };
            wrist.Settle();
            Draw2(wrist, data);
            action = null;
            Tap(wrist, data, Middle(WatchWidgets.CellRect(2, 1, w, h, Theme.Classic)));
            if (!(wrist.IsOpen && wrist.DetailKind == "dock" && action == null)) miss.Add($"{w}x{h}{(action != null ? " sent " + action : "")}");
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
        }
        // the 2 x 1 opened for good: a key, a dangerous key, then its X
        wcfg.Widgets = new List<WatchWidget> { new() { Type = "dock", X = 2, Y = 1, W = 2, H = 1 } };
        wrist.Settle();
        Draw2(wrist, data);
        Tap(wrist, data, Middle(WatchWidgets.CellRect(2, 1, 2, 1, Theme.Classic)));
        Settle(wrist, data);
        sent.Clear();
        Tap(wrist, data, wrist.DockPoint("key4"));
        bool key = string.Join(",", sent) == "key4 down,key4 up";
        sent.Clear();
        Tap(wrist, data, wrist.DockPoint("key3"));
        bool asked = wrist.DockIn.Confirm == "key3" && sent.Count == 0;
        wrist.DockIn.ConfirmNo();
        Tap(wrist, data, wrist.DockClosePoint);
        bool shrinking = !wrist.IsOpen && wrist.DetailShown;
        Settle(wrist, data);
        bool closed = !wrist.DetailShown && action == null;
        wrist.PointerLeave(Hand.Left);
        Log.Info($"dock view{tag}: a tap on the dock card opened the watch's view, not the Hexpad panel, sizes: {n - miss.Count} of {n} (want {n} of {n})" +
                 (miss.Count > 0 ? ", not: " + string.Join(", ", miss) : ""));
        Log.Info($"dock view{tag}: a tap on a key sent it down and up {key} (want True), a key that could end the session asked first, nothing sent {asked} " +
                 $"(want True); its X shrank it back {shrinking} (want True) and closed it {closed} (want True)");
    }

    static void Pictures(WristPanel wrist, PanelData data, WristConfig wcfg, Gpu gpu, string dir, string tag, HexpadLink.Snapshot fake)
    {
        wcfg.Widgets = new List<WatchWidget>
        {
            new() { Type = "time", X = 0, Y = 0, W = 4, H = 2 }, new() { Type = "dock", X = 4, Y = 0, W = 2, H = 2 },
            new() { Type = "media", X = 0, Y = 2, W = 4, H = 1 }, new() { Type = "battery", X = 4, Y = 2, W = 2, H = 1 },
            new() { Type = "keyboard", X = 0, Y = 3, W = 2, H = 1 }, new() { Type = "desktops", X = 2, Y = 3, W = 2, H = 1 },
            new() { Type = "night", X = 4, Y = 3, W = 1, H = 1 }, new() { Type = "settings", X = 5, Y = 3, W = 1, H = 1 },
        };
        var notRunning = fake.Copy();
        notRunning.Version++;
        notRunning.State = HexpadLink.LinkState.NotRunning;
        var cases = new (string name, HexpadLink.Snapshot s, string hover, string pressed, string confirm, string drag, float at)[]
        {
            ("faces", fake, null, null, null, null, 0),
            ("pressed", fake, "key4", "key4", null, null, 0),
            ("drag", fake, null, null, null, "enc1", 0.7f),
            ("jog", fake, null, null, null, "enc2", 60),
            ("confirm", fake, null, null, "key3", null, 0),
            ("notrunning", notRunning, null, null, null, null, 0),
            ("none", null, null, null, null, null, 0),
        };
        foreach (var (name, s, hover, pressed, confirm, drag, at) in cases)
        {
            data.Dock = s;
            wrist.Settle();
            if (!wrist.OpenForTest("dock")) { Log.Warn("dock view: no dock widget"); return; }
            wrist.DockIn.Pose(s ?? new HexpadLink.Snapshot(), hover, pressed, confirm, drag, at);
            Draw2(wrist, data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_open_dock_{name}{tag}.png"));
            wrist.DockIn.AfterPose();
            wrist.DockIn.Pose(new HexpadLink.Snapshot());
            wrist.CloseForTest();
        }
        data.Dock = fake;
        Log.Info($"dock view{tag}: {cases.Length} pictures (wrist_open_dock_*{tag}.png)");
    }
}
