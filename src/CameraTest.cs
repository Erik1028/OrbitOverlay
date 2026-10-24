using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;

namespace Orbit;

// ORBIT_SNAP_ONLY=camera --snapshot <dir>: the watch's camera button (Sys/VrcCamera.cs). A face with the button at 1x1 and 2x1 beside
// the latest photo and the old faces' buttons, in all seven styles (camera_<style>.png, side by side on camera_sheet.png; Hungarian
// too, camera_hu_<style>.png), and a tap on each: what goes out lands in a made-up OSC sink - a UDP port of this run's own on
// 127.0.0.1, never VRChat's 9000 nor Hexpad's 9001 - and must be the very bytes VRChat reads: /usercamera/Capture with a true, then
// (VrcCamera.Release later, not before) with a false. A second tap while it is pressed sends nothing; a tap on another button sends
// nothing. Then the kind's layout check (nothing drawn outside its card) in every style. The tap goes the way the app's goes: the
// watch's OnAction with "camera" to VrcCamera.Shutter (OrbitApp.WristAction) - only the port is the sink's.
// VRChat's camera open or closed (10-24, Panels/WristCamera.cs): its mode comes from a made-up OSCQuery - VRChat's HOST_INFO and
// /usercamera/Mode answered as VRChat answered it that day, through the avatar link's own Query, the path the app's reads take
// (AvatarLink.StepCamera); nothing asks VRChat itself. Not known yet (the pictures above): as before. In every style VRChat says 0:
// the buttons grey (camera_closed_<style>.png), a tap on each sends nothing at all and the button says where to open it
// (camera_hint_<style>.png); then 1: the mode's name, a tap takes the photo again (camera_open_<style>.png; all three side by side
// on camera_states_sheet.png). The pictures must differ where the buttons are (closed from as before, the hint from closed, open
// from as before where the button has words). Then: the source going quiet keeps the last mode, VRChat quitting leaves it closed,
// a link that never had an answer stays not known - and the sink got nothing but the shutter's two messages (never a Mode).
static partial class Tools
{
    // the two messages byte for byte as OSC 1.0 writes them: the address with its NUL padded to 4, the type tag ",T" / ",F" padded
    // to 4, no argument bytes (a bool is all in its tag)
    static readonly byte[] CamPress = Encoding.ASCII.GetBytes("/usercamera/Capture\0,T\0\0");
    static readonly byte[] CamRelease = Encoding.ASCII.GetBytes("/usercamera/Capture\0,F\0\0");

    // the face of the pictures: the time, the latest photo, the camera at 1x1 (top right) and 2x1 (left), now playing, the six buttons
    static List<WatchWidget> CameraFace()
    {
        var l = new List<WatchWidget>();
        void Put(string t, int x, int y, int w = 1, int h = 1) => l.Add(new WatchWidget { Type = t, X = x, Y = y, W = w, H = h });
        Put("time", 0, 0, 3, 2); Put("photo", 3, 0, 2, 2); Put(VrcCamera.Id, 5, 0); Put("status", 5, 1);
        Put(VrcCamera.Id, 0, 2, 2, 1); Put("media", 2, 2, 4, 1);
        string[] six = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
        for (int i = 0; i < six.Length; i++) Put(six[i], i, 3);
        return l;
    }

    static int CameraOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        int failed0 = WantsFailed, layout = 0;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        string lang0 = Lang.Code;
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var onAction0 = wrist.OnAction;

        // the sink: a port the system picks, read with a timeout (nothing came = null)
        using var sink = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)sink.Client.LocalEndPoint).Port;
        sink.Client.ReceiveTimeout = 1500;
        Want($"camera: the made-up OSC sink on 127.0.0.1:{port}, not VRChat's 9000 nor 9001 -", port != 9000 && port != 9001);
        var received = new List<byte[]>();          // everything the sink got (never a Mode: the end's check)
        byte[] Next()
        {
            try { var from = new IPEndPoint(IPAddress.Any, 0); var b = sink.Receive(ref from); received.Add(b); return b; }
            catch (SocketException) { return null; }
        }
        int Waiting() { System.Threading.Thread.Sleep(120); return sink.Available; }
        static string Show(byte[] b) => b == null ? "nothing" : $"{b.Length} bytes \"{Encoding.ASCII.GetString(b).Replace("\0", "\\0")}\"";
        static bool Same(byte[] a, byte[] b) => a != null && a.AsSpan().SequenceEqual(b);

        using var osc = new OscSender();
        var cam = new VrcCamera(osc);
        var actions = new List<string>();
        double clock = 1000;            // the check's own clock: WARP's frames are slow, the real one would run past the release mid-tap
        wrist.OnAction = a => { actions.Add(a); if (a == VrcCamera.Id) cam.Shutter(clock, port); };

        // VRChat's camera mode: VRChat's own answer of 10-24 parsed, then a made-up OSCQuery serving 'served' (< 0: no answer) while
        // 'running' - each style starts with a new link (nothing read yet: not known)
        const string Real0 = "{\"FULL_PATH\":\"/usercamera/Mode\",\"ACCESS\":3,\"TYPE\":\"i\",\"VALUE\":[0]}";
        int p0 = AvatarLink.ParseMode(Real0), p1 = AvatarLink.ParseMode(Real0.Replace("[0]", "[1]")),
            pNone = AvatarLink.ParseMode("{\"FULL_PATH\":\"/usercamera/Mode\",\"ACCESS\":3,\"TYPE\":\"i\"}"), pBad = AvatarLink.ParseMode("<html>404</html>");
        Want($"camera: VRChat's answer parsed - {p0} (want 0), with a 1 {p1} (want 1), no VALUE {pNone}, not JSON {pBad} (want -1, -1) -",
            p0 == 0 && p1 == 1 && pNone == VrcCamera.Unknown && pBad == VrcCamera.Unknown);
        int served = VrcCamera.Unknown;
        bool running = true;
        string hostInfo = "{\"NAME\":\"VRChat-Client-TEST\",\"OSC_IP\":\"127.0.0.1\",\"OSC_PORT\":" + port + ",\"OSC_TRANSPORT\":\"UDP\"}";
        AvatarLink NewLink() => new()
        {
            Running = () => running,
            Query = path => path == "/?HOST_INFO" ? hostInfo
                : path == VrcCamera.ModePath && served >= 0 ? Real0.Replace("[0]", "[" + served + "]") : null,
        };
        var camData0 = data.Camera;
        var camHint0 = wrist.CamHintFor;
        data.Camera = cam;
        // pixels that differ between two pictures inside a widget's card
        int Diff(string a, string b, WatchWidget w)
        {
            var c = WatchWidgets.Rect(w, Theme.Classic, WatchPages.Grid(wcfg, wrist.PageNow));
            using var A = new System.Drawing.Bitmap(a);
            using var B = new System.Drawing.Bitmap(b);
            int n = 0;
            for (int y = (int)(c.Top * S()); y < (int)(c.Bottom * S()) && y < A.Height; y++)
                for (int x = (int)(c.Left * S()); x < (int)(c.Right * S()) && x < A.Width; x++)
                    if (A.GetPixel(x, y) != B.GetPixel(x, y)) n++;
            return n;
        }

        float S() => Theme.Classic ? 2 : 1;
        Vector2 Centre(WatchWidget w)
        {
            var c = WatchWidgets.Rect(w, Theme.Classic, WatchPages.Grid(wcfg, wrist.PageNow));
            return new Vector2((c.X + c.Width / 2) * S(), (c.Y + c.Height / 2) * S());
        }
        void Tap(Vector2 p)
        {
            wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
            wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
            wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
            wrist.PointerLeave(Hand.Left); wrist.Draw(data);
        }
        void Face()
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            wcfg.Widgets = CameraFace();
        }
        string Shot(string file)
        {
            wrist.Settle();
            wrist.Draw(data);
            wrist.Draw(data);
            string path = Path.Combine(dir, file);
            wrist.Surf.SavePng(gpu, path);
            return path;
        }

        // a tap that takes the photo: the press now, nothing more before the release, the release on time
        void Shutters(string where, WatchWidget w)
        {
                    actions.Clear();
                    int shots0 = cam.Shots;
                    clock += 10;
                    Tap(Centre(w));
                    var press = Next();
                    Want($"{where}: a tap -> actions {string.Join(",", actions)} (want camera), the sink got {Show(press)} (want {Show(CamPress)}) -",
                        actions.SequenceEqual(new[] { VrcCamera.Id }) && Same(press, CamPress) && cam.Shots == shots0 + 1);
                    // still pressed: a second tap goes nowhere, nor does a frame before the release is due (half of it)
                    actions.Clear();
                    Tap(Centre(w));
                    cam.Update(clock + VrcCamera.Release / 2);
                    int early = Waiting();
                    Want($"{where}: a second tap while pressed -> actions {string.Join(",", actions)}, {early} byte(s) waiting, shots {cam.Shots - shots0} (want camera, 0, 1) -",
                        actions.SequenceEqual(new[] { VrcCamera.Id }) && early == 0 && cam.Shots == shots0 + 1);
                    cam.Update(clock + VrcCamera.Release + 0.01);
                    var release = Next();
                    cam.Update(clock + 2 * VrcCamera.Release);
                    int after = Waiting();
                    Want($"{where}: {VrcCamera.Release} s later the sink got {Show(release)} (want {Show(CamRelease)}), then {after} byte(s) more (want 0) -",
                        Same(release, CamRelease) && after == 0);
        }

        var styles = new[] { ("modern", "modern"), (CyberHud.Id, "cyber"), ("classic", "95"), ("xp", "xp"), ("aero", "aero"), ("mac", "mac"), ("terminal", "term") };
        var files = new List<string>();
        var states = new List<string>();
        try
        {
            foreach (var (style, tag) in styles)
            {
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                cam.Link?.Dispose();
                cam.Link = NewLink();
                served = VrcCamera.Unknown;
                wrist.CamHintFor = 0;
                Face();
                string before = Shot($"camera_{tag}.png");
                files.Add(before);
                var cams = wcfg.Widgets.Where(x => x.Type == VrcCamera.Id).ToList();
                Want($"camera {tag}: nothing read yet - mode {cam.Mode}, drawn {wrist.CameraLook(data)} (want -1, Normal: as before) -",
                    cam.Mode == VrcCamera.Unknown && wrist.CameraLook(data) == "Normal");
                foreach (var w in cams) Shutters($"camera {tag} {w.W}x{w.H}", w);

                // VRChat says 0: greyed; a tap on each sends nothing and says where (the hint held for its picture)
                served = VrcCamera.Off;
                cam.Link.ReadCameraNow();
                string closed = Shot($"camera_closed_{tag}.png"), lookClosed = wrist.CameraLook(data);
                foreach (var w in cams)
                {
                    actions.Clear();
                    int shots0 = cam.Shots, refused0 = cam.Refused;
                    clock += 10;
                    Tap(Centre(w));
                    cam.Update(clock + 2 * VrcCamera.Release);
                    int got = Waiting();
                    Want($"camera {tag} {w.W}x{w.H} closed: a tap -> actions {string.Join(",", actions)} (want camera), {got} byte(s) in the sink, " +
                        $"shots {cam.Shots - shots0}, refused {cam.Refused - refused0} (want 0, 0, 1) -",
                        actions.SequenceEqual(new[] { VrcCamera.Id }) && got == 0 && cam.Shots == shots0 && cam.Refused == refused0 + 1);
                }
                wrist.CamHintFor = 600;
                string hint = Shot($"camera_hint_{tag}.png"), lookHint = wrist.CameraLook(data);
                layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag + "-hint", onlyKind: VrcCamera.Id);      // its longest words

                // VRChat says 1: the mode's name, a tap takes the photo again
                served = 1;
                cam.Link.ReadCameraNow();
                wrist.CamHintFor = 0;
                Face();
                string open = Shot($"camera_open_{tag}.png"), lookOpen = wrist.CameraLook(data);
                states.Add(closed); states.Add(hint); states.Add(open);
                Want($"camera {tag}: VRChat says 0 -> mode {VrcCamera.Off} drawn {lookClosed}, tapped {lookHint}; says 1 -> mode {cam.Mode} drawn {lookOpen} " +
                    "(want Closed, Hint; 1 Open) -", lookClosed == "Closed" && lookHint == "Hint" && cam.Mode == 1 && lookOpen == "Open");
                foreach (var w in cams)
                {
                    // (95 and the Mac: a 1x1 is its picture alone, open or not known alike)
                    bool words = w.W >= 2 || tag is not ("95" or "mac");
                    int dc = Diff(before, closed, w), dh = Diff(closed, hint, w), dopen = Diff(before, open, w);
                    Want($"camera {tag} {w.W}x{w.H}: pixels changed in its card - closed {dc}, the hint {dh}, open {dopen}{(words ? "" : " (a picture alone)")} " +
                        "(want > 0 each) -", dc > 0 && dh > 0 && (dopen > 0 || !words));
                }
                foreach (var w in cams) Shutters($"camera {tag} {w.W}x{w.H} open", w);
                // another button: its own action, nothing to VRChat's camera
                actions.Clear();
                Tap(Centre(wcfg.Widgets.First(x => x.Type == "recenter")));
                int other = Waiting();
                Want($"camera {tag}: a tap on Recenter -> actions {string.Join(",", actions)} (want recenter), {other} byte(s) in the sink (want 0) -",
                    actions.SequenceEqual(new[] { "recenter" }) && other == 0);
                cam.Link.Dispose();
                cam.Link = null;              // (the layout check as before: not known)
                layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag, onlyKind: VrcCamera.Id);
            }
            CameraSheet(Path.Combine(dir, "camera_sheet.png"), files);
            CameraSheet(Path.Combine(dir, "camera_states_sheet.png"), states, 3);

            // the source going quiet keeps what it said last; closed again; VRChat quitting leaves it closed (not back to not known); open
            // when VRChat quit: closed; a link that never had an answer: not known
            cam.Link = NewLink();
            running = true;
            served = 2; cam.Link.ReadCameraNow(); int mOpen = cam.Mode;
            served = VrcCamera.Unknown; cam.Link.ReadCameraNow(); int mQuiet = cam.Mode;
            served = VrcCamera.Off; cam.Link.ReadCameraNow(); int mClosed = cam.Mode;
            running = false; cam.Link.ReadCameraNow(); int mGone = cam.Mode;
            cam.Link.Dispose();
            cam.Link = NewLink();
            running = true; served = 6; cam.Link.ReadCameraNow(); int mDrone = cam.Mode;
            running = false; cam.Link.ReadCameraNow(); int mDroneGone = cam.Mode;
            cam.Link.Dispose();
            cam.Link = NewLink();
            running = true; served = VrcCamera.Unknown; cam.Link.ReadCameraNow(); int mNever = cam.Mode;
            running = false; cam.Link.ReadCameraNow(); int mNeverGone = cam.Mode;
            Want($"camera: says 2 -> {mOpen}, then quiet -> {mQuiet} (want 2, 2); 0 -> {mClosed}, VRChat quits -> {mGone} (want 0, 0); " +
                $"6 then VRChat quits -> {mDrone}, {mDroneGone} (want 6, 0); never an answer -> {mNever}, VRChat quits -> {mNeverGone} (want -1, -1) -",
                mOpen == 2 && mQuiet == 2 && mClosed == 0 && mGone == 0 && mDrone == 6 && mDroneGone == 0 && mNever == -1 && mNeverGone == -1);
            Want($"camera: the mode's names - {VrcCamera.ModeName(1)}, {VrcCamera.ModeName(2)}, {VrcCamera.ModeName(6)}, a later one {VrcCamera.ModeName(9)} " +
                "(want Photo, Stream, Drone, Camera) -", VrcCamera.ModeName(1) == "Photo" && VrcCamera.ModeName(2) == "Stream" &&
                VrcCamera.ModeName(6) == "Drone" && VrcCamera.ModeName(9) == "Camera");
            cam.Link.Dispose();
            cam.Link = null;
            running = true;

            // Hungarian: its words, and the face in every style - not known, closed, the hint, open (camera_hu_states_sheet.png)
            Lang.Set("hu");
            Want($"camera: Hungarian - \"{Lang.T("Camera")}\", \"{Lang.T("CAMERA")}\", \"{Lang.T("CAM")}\" (want Kamera, KAMERA, KAM) -",
                Lang.T("Camera") == "Kamera" && Lang.T("CAMERA") == "KAMERA" && Lang.T("CAM") == "KAM");
            Want($"camera: Hungarian, open or closed - \"{Lang.T("Open the camera")}\", \"{Lang.T("Closed")}\", \"{Lang.T("Open it in VRChat")}\", " +
                $"\"{Lang.T("IN VRCHAT")}\", \"{Lang.T("CAM OFF")}\", \"{Lang.T("Stream")}\", \"{Lang.T("PHOTO")}\" " +
                "(want Nyisd meg a kamerát, Zárva, Nyisd meg a VRChatben, VRCHATBEN, KAM KI, Közvetítés, FOTÓ) -",
                Lang.T("Open the camera") == "Nyisd meg a kamerát" && Lang.T("Closed") == "Zárva" && Lang.T("Open it in VRChat") == "Nyisd meg a VRChatben" &&
                Lang.T("IN VRCHAT") == "VRCHATBEN" && Lang.T("CAM OFF") == "KAM KI" && Lang.T("Stream") == "Közvetítés" && Lang.T("PHOTO") == "FOTÓ");
            // the table's check of the camera's own lines (no English twice, nothing translated twice over; others' lines are not this tag's)
            string[] mine = { "Open the camera", "short|Open the camera", "Closed", "Open it in VRChat", "short|Open it in VRChat", "short|In VRChat",
                "Stream", "Emoji", "Multilayer", "Drone", "CAM OFF" };
            var bad = Lang.Check("hu").Where(b => mine.Any(m => b.Contains("'" + m + "'", StringComparison.Ordinal))).ToList();
            Want($"camera: the Hungarian table's check of its lines - {bad.Count} problem(s) {string.Join("; ", bad.Take(3))} (want 0) -", bad.Count == 0);
            var hu = new List<string>();
            foreach (var (style, tag) in styles)        // (every style: the longer Hungarian words where each has room)
            {
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                cam.Link = NewLink();
                served = VrcCamera.Unknown;
                wrist.CamHintFor = 0;
                Face();
                Shot($"camera_hu_{tag}.png");
                served = VrcCamera.Off;
                cam.Link.ReadCameraNow();
                hu.Add(Shot($"camera_hu_closed_{tag}.png"));
                foreach (var w in wcfg.Widgets.Where(x => x.Type == VrcCamera.Id).ToList()) { clock += 10; Tap(Centre(w)); }
                wrist.CamHintFor = 600;
                hu.Add(Shot($"camera_hu_hint_{tag}.png"));
                served = 2;
                cam.Link.ReadCameraNow();
                wrist.CamHintFor = 0;
                hu.Add(Shot($"camera_hu_open_{tag}.png"));
                cam.Link.Dispose();
                cam.Link = null;
            }
            CameraSheet(Path.Combine(dir, "camera_hu_states_sheet.png"), hu, 3);

            // everything the sink got: the shutter's press and release, nothing else - never a Mode, never anything while closed
            while (sink.Available > 0 && Next() != null) { }
            int odd = received.Count(b => !Same(b, CamPress) && !Same(b, CamRelease));
            bool mode = received.Any(b => Encoding.ASCII.GetString(b).Contains("/usercamera/Mode"));
            Want($"camera: the sink got {received.Count} message(s), {odd} not the shutter's, a Mode among them {mode} (want 0, False) -", odd == 0 && !mode);
        }
        finally
        {
            cam.Link?.Dispose();
            cam.Link = null;
            data.Camera = camData0;
            wrist.CamHintFor = camHint0;
            Lang.Set(lang0);
            wrist.OnAction = onAction0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        Log.Info($"snapshot (camera only) written to {dir}: {cam.Shots} shutter(s) to the sink, {cam.Refused} tap(s) while closed sent nothing");
        return layout > 0 ? 1 : WantsExit(failed0);
    }

    // the pictures 'cols' to a row (the styles two, their closed / hint / open three), each with its name
    static void CameraSheet(string path, List<string> files, int cols = 2)
    {
        const int W = WristPanel.TW, H = WristPanel.TH, Label = 22, Gap = 10;
        int rows = (files.Count + cols - 1) / cols;
        using var bmp = new System.Drawing.Bitmap(cols * W + (cols + 1) * Gap, rows * (H + Label + Gap) + Gap);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(255, 40, 44, 52));
        using var font = new System.Drawing.Font("Segoe UI", 11);
        for (int i = 0; i < files.Count; i++)
        {
            int x = Gap + i % cols * (W + Gap), y = Gap + i / cols * (H + Label + Gap);
            g.DrawString(Path.GetFileNameWithoutExtension(files[i]), font, System.Drawing.Brushes.Gainsboro, x, y);
            using var src = new System.Drawing.Bitmap(files[i]);
            g.DrawImage(src, new System.Drawing.Rectangle(x, y + Label, W, H));
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
