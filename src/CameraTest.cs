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
        byte[] Next()
        {
            try { var from = new IPEndPoint(IPAddress.Any, 0); return sink.Receive(ref from); }
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

        var styles = new[] { ("modern", "modern"), (CyberHud.Id, "cyber"), ("classic", "95"), ("xp", "xp"), ("aero", "aero"), ("mac", "mac"), ("terminal", "term") };
        var files = new List<string>();
        try
        {
            foreach (var (style, tag) in styles)
            {
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                Face();
                files.Add(Shot($"camera_{tag}.png"));
                foreach (var w in wcfg.Widgets.Where(x => x.Type == VrcCamera.Id).ToList())
                {
                    string where = $"camera {tag} {w.W}x{w.H}";
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
                // another button: its own action, nothing to VRChat's camera
                actions.Clear();
                Tap(Centre(wcfg.Widgets.First(x => x.Type == "recenter")));
                int other = Waiting();
                Want($"camera {tag}: a tap on Recenter -> actions {string.Join(",", actions)} (want recenter), {other} byte(s) in the sink (want 0) -",
                    actions.SequenceEqual(new[] { "recenter" }) && other == 0);
                layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag, onlyKind: VrcCamera.Id);
            }
            CameraSheet(Path.Combine(dir, "camera_sheet.png"), files);

            // Hungarian: its words, and the face in three styles (the 2x1's label, Terminal's capitals)
            Lang.Set("hu");
            Want($"camera: Hungarian - \"{Lang.T("Camera")}\", \"{Lang.T("CAMERA")}\", \"{Lang.T("CAM")}\" (want Kamera, KAMERA, KAM) -",
                Lang.T("Camera") == "Kamera" && Lang.T("CAMERA") == "KAMERA" && Lang.T("CAM") == "KAM");
            foreach (var (style, tag) in new[] { ("modern", "modern"), ("classic", "95"), ("terminal", "term") })
            {
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                Face();
                Shot($"camera_hu_{tag}.png");
            }
        }
        finally
        {
            Lang.Set(lang0);
            wrist.OnAction = onAction0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        Log.Info($"snapshot (camera only) written to {dir}: {cam.Shots} shutter(s) to the sink");
        return layout > 0 ? 1 : WantsExit(failed0);
    }

    // the styles' pictures two to a row, each with its name
    static void CameraSheet(string path, List<string> files)
    {
        const int W = WristPanel.TW, H = WristPanel.TH, Label = 22, Gap = 10;
        int rows = (files.Count + 1) / 2;
        using var bmp = new System.Drawing.Bitmap(2 * W + 3 * Gap, rows * (H + Label + Gap) + Gap);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(255, 40, 44, 52));
        using var font = new System.Drawing.Font("Segoe UI", 11);
        for (int i = 0; i < files.Count; i++)
        {
            int x = Gap + i % 2 * (W + Gap), y = Gap + i / 2 * (H + Label + Gap);
            g.DrawString(Path.GetFileNameWithoutExtension(files[i]), font, System.Drawing.Brushes.Gainsboro, x, y);
            using var src = new System.Drawing.Bitmap(files[i]);
            g.DrawImage(src, new System.Drawing.Rectangle(x, y + Label, W, H));
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
