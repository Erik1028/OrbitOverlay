using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Orbit;

// --snapshot's part for the light and RGB widgets (Sys/RoomLights.cs, WristLights*.cs) - never the real Lumos, Prisma, bulb or RGB:
//  - the data (once): the Lumos link on a made-up Lumos (its answers in Lumos's own shape, nothing on the network: the link's HTTP is
//    replaced) - read, toggle, brightness, a white, a colour, never /api/config, Lumos not running, no bulb set, Lumos's error; the
//    Prisma link through a REAL named pipe of the tool's own (another name than Prisma's) with a made-up settings.json - the commands as
//    Prisma's pipe gets them, the state read back, Prisma not running, its settings file never written.
//  - each style: a tap on a 1x1 switches it, a tap on a 2x1's card opens it, no garbage at rest; pictures wrist_lights_<case><tag>.png.
static class LightsViewTools
{
    // the light on: a warm white at 70 %; the RGB: a rainbow at 80 %
    public static LampState LampOn() => new()
    {
        Status = LinkStatus.Ready, On = true, Brightness = 70, HasColor = true, HasTemp = true, Temp = 2700, Name = "Desk lamp",
    };

    public static RgbState RgbOn(string effect = "RainbowWave") => new() { Status = LinkStatus.Ready, Effect = effect, Color = 0xFF00C8AA, Brightness = 80 };

    // made-up links for the pictures and the layout check: frozen (a command changes only what they show)
    public static void Fake(PanelData data, LampState l = null, RgbState r = null)
    {
        var lamp = new LumosLink();
        lamp.Freeze(l ?? LampOn());
        var rgb = new PrismaLink();
        rgb.Freeze(r ?? RgbOn());
        data.Lamp = lamp;
        data.Rgb = rgb;
    }

    static bool Wait(Func<bool> ok, int ms = 3000)
    {
        for (int i = 0; i < ms / 20 && !ok(); i++) Thread.Sleep(20);
        return ok();
    }

    // ---- the data ----

    public static void DataChecks(WristPanel wrist, PanelData data, string dir)
    {
        var keep = (data.Lamp, data.Rgb);
        try { LumosChecks(wrist, data); PrismaChecks(wrist, data, dir); }
        finally { (data.Lamp, data.Rgb) = keep; }
    }

    static void LumosChecks(WristPanel wrist, PanelData data)
    {
        // a made-up Lumos: the bulb's state as Lumos's state_dict writes it, every call kept
        var calls = new List<string>();
        bool on = false, down = false, fail = false;
        int bright = 40, temp = 4000;
        (int, string) Lumos(string method, string path, string json)
        {
            lock (calls) calls.Add(method + " " + path + " " + json);
            if (down) throw new System.Net.Http.HttpRequestException("refused (made up)", new System.Net.Sockets.SocketException(10061));
            if (fail) return (500, "{\"error\": \"Could not reach the bulb. (made up)\"}");
            if (path == "/api/power") on = json.Contains("\"on\":true");
            if (path == "/api/brightness") { bright = int.Parse(json.Split("\"value\":")[1].TrimEnd('}')); on = true; }
            if (path == "/api/color_temp") { temp = int.Parse(json.Split("\"temp\":")[1].TrimEnd('}')); on = true; }
            if (path == "/api/hsv") { temp = 0; on = true; }
            return (200, "{\"host\": \"10.0.0.99\", \"alias\": \"Made-up lamp\", \"model\": \"L630\", \"mac\": \"00-00-00-00-00-00\", \"rssi\": -50, " +
                         $"\"is_on\": {(on ? "true" : "false")}, \"has_light\": true, \"has_color\": true, \"has_color_temp\": true, \"has_brightness\": true, " +
                         $"\"brightness\": {bright}, \"hsv\": {{\"h\": 285, \"s\": 80, \"v\": {bright}}}, \"color_temp\": {temp}, \"ct_min\": 2500, \"ct_max\": 6500}}");
        }
        int Count(string what) { lock (calls) return calls.Count(c => c.Contains(what)); }
        using (var l = new LumosLink { Http = Lumos, Poll = 0.2, Retry = 0.2, Slow = 0.2 })
        {
            l.SetBulb("10.0.0.99");
            l.Active = true;
            l.Watched = true;
            bool read = Wait(() => l.State.Ready) && !l.State.On && l.State.Brightness == 40 && l.State.Name == "Made-up lamp" && l.State.Temp == 4000;
            l.Toggle();
            bool shown = l.State.On;                                      // at once, before Lumos answers
            bool toggled = Wait(() => on && Count("POST /api/power") == 1 && !l.State.Pending) && l.State.On;
            data.Lamp = l;
            string val = wrist.LightShown(data);
            for (int v = 10; v <= 55; v += 5) l.SetBrightness(v);        // a slider dragged
            bool brightened = Wait(() => bright == 55 && !l.State.Pending) && l.State.Brightness == 55;
            int sends = Count("/api/brightness");
            l.SetTemp(2700);
            bool warm = Wait(() => temp == 2700 && !l.State.Pending) && l.State.Temp == 2700;
            l.SetColor(285, 80);
            bool colour = Wait(() => temp == 0 && !l.State.Pending) && l.State.Temp == 0 && l.State.Hue == 285;
            bool noConfig = Count("/api/config") == 0 && Count("/api/discover") == 0 && Count("/api/connect") == 0 && Count("/api/forget") == 0;
            bool host = Count("10.0.0.99") == calls.Count;
            Tools.Want($"lights lumos: read from a made-up Lumos {read}, a tap shows on at once {shown} and switched the bulb on {toggled}, " +
                       $"the watch shows {val} (want 40%) -", read && shown && toggled && val == "40%");
            Tools.Want($"lights lumos: the brightness dragged reached 55 {brightened} in {sends} call(s) of 10 values (want fewer than 10), a warm white " +
                       $"{warm}, a colour {colour}; never asked its config / discover / connect / forget {noConfig}, only about the bulb set {host} -",
                       brightened && sends < 10 && warm && colour && noConfig && host);
            // Lumos's own error: shown with its words
            fail = true;
            l.Watched = false; l.Watched = true;
            bool err = Wait(() => l.State.Status == LinkStatus.Error) && l.State.Message.Contains("Could not reach");
            data.Lamp = l;
            string says = wrist.LightShown(data);
            Tools.Want($"lights lumos: an error from Lumos shown as such {err}, the watch says '{says}' (want Bulb not reachable) -", err && says == "Bulb not reachable");
            // Lumos not running: nothing to switch, '-' and why
            fail = false;
            down = true;
            l.Watched = false; l.Watched = true;
            bool gone = Wait(() => l.State.Status == LinkStatus.NoApp);
            int before = calls.Count;
            l.SetBrightness(90);
            bool none = l.State.Status == LinkStatus.NoApp && l.State.Brightness != 90;
            string saysGone = wrist.LightShown(data);
            Tools.Want($"lights lumos: not running {gone}, the watch says '{saysGone}' (want Lumos isn't running), a command shows nothing it cannot do {none} -",
                       gone && saysGone == "Lumos isn't running" && none);
        }
        // no bulb set: nothing asked at all
        calls.Clear();
        down = false;
        using (var l = new LumosLink { Http = Lumos, Poll = 0.2, Retry = 0.2 })
        {
            l.SetBulb("");
            l.Active = true;
            bool nob = Wait(() => l.State.Status == LinkStatus.NoDevice);
            Thread.Sleep(150);
            data.Lamp = l;
            string saysNone = wrist.LightShown(data);
            Tools.Want($"lights lumos: no bulb set says '{saysNone}' (want Set the bulb), asked nothing {calls.Count == 0 && nob} -", saysNone == "Set the bulb" && calls.Count == 0 && nob);
        }
        // Lumos's answer read as Lumos writes it (a real state_dict's keys; an error)
        var p = LumosLink.Parse(200, "{\"host\": \"10.0.0.98\", \"alias\": \"Lamp\", \"is_on\": true, \"has_light\": true, \"has_color\": true, \"has_color_temp\": true, " +
                                     "\"brightness\": 100, \"hsv\": null, \"color_temp\": 6500, \"ct_min\": 2500, \"ct_max\": 6500}");
        var e = LumosLink.Parse(500, "{\"error\": \"Authentication failed.\"}");
        Tools.Want($"lights lumos: parsed on / 100 / 6500 K {p.Ready && p.On && p.Brightness == 100 && p.Temp == 6500}, an error {e.Status} (want Error) -",
                   p.Ready && p.On && p.Brightness == 100 && p.Temp == 6500 && e.Status == LinkStatus.Error);
    }

    static void PrismaChecks(WristPanel wrist, PanelData data, string dir)
    {
        // a pipe of the tool's own, read as Prisma's RemoteControl reads it; it writes a made-up settings.json the way Prisma does after a change
        string pipe = "OrbitTest_Prisma_" + Environment.ProcessId, file = Path.Combine(dir, "prisma_test_settings.json");
        File.WriteAllText(file, "{\"Effect\": \"Breathing\", \"ColorArgb\": -16725846, \"Speed\": 50, \"Brightness\": 70, \"Profiles\": []}");
        // what the made-up Prisma wrote last and when: the file must be just that at the end (a link that wrote it would change either)
        var written = (text: File.ReadAllText(file), at: File.GetLastWriteTimeUtc(file));
        var got = new List<string>();
        string effect = "Breathing"; int bright = 70;
        bool stop = false;
        void Serve()
        {
            while (!stop)
            {
                try
                {
                    using var s = new NamedPipeServerStream(pipe, PipeDirection.In, 1);
                    s.WaitForConnection();
                    using var rd = new StreamReader(s);
                    string t = rd.ReadToEnd();
                    if (stop || t.Length == 0) continue;
                    lock (got) got.Add(t);
                    var w = t.Split(PrismaLink.Sep);
                    if (w[0] == "--effect") effect = w[1] switch { "rainbow" => "RainbowWave", "static" => "Static", "off" => "Off", _ => "Breathing" };
                    if (w[0] == "--brightness") bright = int.Parse(w[1]);
                    if (w[0] == "--toggle") effect = effect == "Off" ? "RainbowWave" : "Off";
                    Thread.Sleep(30);
                    string json = $"{{\"Effect\": \"{effect}\", \"ColorArgb\": -16725846, \"Brightness\": {bright}}}";
                    lock (got)
                    {
                        File.WriteAllText(file, json);
                        written = (json, File.GetLastWriteTimeUtc(file));
                    }
                }
                catch (Exception) { Thread.Sleep(30); }
            }
        }
        var server = new Thread(Serve) { IsBackground = true };
        server.Start();
        string Last() { lock (got) return got.Count == 0 ? "" : got[^1].Replace(PrismaLink.Sep, ' '); }
        int Got() { lock (got) return got.Count; }
        try
        {
            Thread.Sleep(100);
            using var l = new PrismaLink { Pipe = pipe, SettingsFile = file, Look = 0.05, Check = 0.1 };
            l.Active = true;
            bool read = Wait(() => l.State.Ready) && l.State.Effect == "Breathing" && l.State.Brightness == 70 && (l.State.Color & 0xFFFFFF) == 0x00C8AA;
            data.Rgb = l;
            string shown0 = wrist.RgbShown(data);
            l.SetEffect("RainbowWave");
            bool atOnce = l.State.Effect == "RainbowWave";
            bool fx = Wait(() => Got() == 1 && l.State.Effect == "RainbowWave" && !l.State.Pending) && Last() == "--effect rainbow";
            for (int v = 20; v <= 55; v += 5) l.SetBrightness(v);
            bool br = Wait(() => bright == 55 && !l.State.Pending) && l.State.Brightness == 55 && Last() == "--brightness 55";
            int n = Got();
            l.Toggle();
            bool off = Wait(() => effect == "Off" && l.State.Off && !l.State.Pending) && Last() == "--toggle";
            l.SetColor(0xFF0050FF);
            bool col = Wait(() => Got() == n + 2) && Last() == "--color 0050FF";
            Tools.Want($"lights prisma: read from a made-up settings.json {read}, the watch shows '{shown0}' (want Breathing); a rainbow picked shows at once {atOnce} " +
                       $"and Prisma's pipe got '--effect rainbow' {fx}; a slider dragged sent '--brightness 55' last {br} in {n - 1} command(s) " +
                       $"(want fewer than 8); a tap's '--toggle' {off}; a colour '--color 0050FF' {col} -",
                       read && shown0 == "Breathing" && atOnce && fx && br && n - 1 < 8 && off && col);
        }
        finally
        {
            stop = true;
            try { using var c = new NamedPipeClientStream(".", pipe, PipeDirection.Out); c.Connect(300); } catch { }
            server.Join(1000);
        }
        // Prisma not running: no pipe of that name
        using (var l = new PrismaLink { Pipe = pipe + "_none", SettingsFile = file, Look = 0.05, Check = 0.05 })
        {
            l.Active = true;
            bool gone = Wait(() => l.State.Status == LinkStatus.NoApp);
            data.Rgb = l;
            string says = wrist.RgbShown(data);
            Tools.Want($"lights prisma: not running {gone}, the watch says '{says}' (want Prisma isn't running) -", gone && says == "Prisma isn't running");
        }
        string text = File.ReadAllText(file);
        var at = File.GetLastWriteTimeUtc(file);
        File.Delete(file);
        bool untouched;
        lock (got) untouched = text == written.text && at == written.at;
        Tools.Want("lights prisma: the settings file read, never written by the link (as the made-up Prisma wrote it last, its text and its time)", untouched);
    }

    // ---- each style ----

    static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    static Vector2 Middle(System.Drawing.RectangleF c, float fy = 0.5f)
    {
        float s = Theme.Classic ? 2 : 1;
        return new Vector2((c.X + c.Width / 2) * s, (c.Y + c.Height * fy) * s);
    }

    static WatchWidget W(string type, int x, int y, int w, int h) => new() { Type = type, X = x, Y = y, W = w, H = h };

    static List<WatchWidget> Face64() => new()
    {
        W("light", 0, 0, 1, 1), W("light", 1, 0, 2, 1), W("light", 0, 1, 2, 2), W("rgb", 3, 0, 1, 1), W("rgb", 4, 0, 2, 1), W("rgb", 2, 1, 2, 2),
        W("time", 4, 1, 2, 1), W("battery", 4, 2, 2, 1), W("keyboard", 0, 3, 1, 1), W("desktops", 1, 3, 1, 1), W("night", 2, 3, 1, 1),
    };

    static List<WatchWidget> Face43() => new() { W("light", 0, 0, 2, 2), W("light", 2, 0, 2, 1), W("light", 2, 1, 1, 1), W("rgb", 3, 1, 1, 1), W("rgb", 0, 2, 2, 1), W("rgb", 2, 2, 2, 1) };
    static List<WatchWidget> Face43b() => new() { W("rgb", 0, 0, 2, 2), W("time", 2, 0, 2, 1), W("rgb", 2, 1, 1, 1), W("light", 3, 1, 1, 1), W("light", 0, 2, 2, 1), W("fps", 2, 2, 1, 1) };

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background, data.Lamp, data.Rgb);
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            Fake(data);
            bool classic = Theme.Classic;
            // a tap on a 1x1 switches it; a tap on a 2x1's card (its caption) opens it, its X closes it
            wcfg.Widgets = new List<WatchWidget> { W("light", 0, 0, 1, 1), W("rgb", 1, 0, 1, 1), W("light", 0, 1, 2, 1), W("rgb", 2, 1, 2, 1) };
            wrist.Settle(); wrist.Draw(data); wrist.Draw(data);
            Tap(wrist, data, Middle(WatchWidgets.CellRect(0, 0, 1, 1, classic)));
            bool lampOff = !data.Lamp.State.On;
            Tap(wrist, data, Middle(WatchWidgets.CellRect(1, 0, 1, 1, classic)));
            bool rgbOff = data.Rgb.State.Off;
            Tap(wrist, data, Middle(WatchWidgets.CellRect(1, 0, 1, 1, classic)));
            bool rgbBack = data.Rgb.State.Effect == "RainbowWave";
            // (95: the window's top right, by its little bulb - the rest of it is its check box and trackbar)
            var card = WatchWidgets.CellRect(0, 1, 2, 1, classic);
            Tap(wrist, data, classic ? new Vector2((card.Right - 26) * 2, (card.Y + 7) * 2) : Middle(card, 0.16f));
            for (int k = 0; k < 11; k++) { Thread.Sleep(30); wrist.Draw(data); }
            bool opened = wrist.IsOpen && wrist.DetailKind == "light";
            var x = wrist.ControlPoint(data, classic ? "wd95x" : "wclose");
            if (x is { } xp) Tap(wrist, data, xp);
            for (int k = 0; k < 11; k++) { Thread.Sleep(30); wrist.Draw(data); }
            bool shut = !wrist.IsOpen;
            wrist.CloseForTest();
            Tools.Want($"lights view{tag}: a tap on the 1x1 light switched it off {lampOff}, on the 1x1 RGB off {rgbOff} and back to its rainbow {rgbBack}; " +
                       $"a tap on a 2x1's card opened it {opened}, its X closed it {shut} -", lampOff && rgbOff && rgbBack && opened && shut);
            // no garbage a frame at rest
            Fake(data);
            wcfg.Widgets = Face64().Where(w => w.Type is "light" or "rgb").ToList();
            long Bytes()
            {
                wrist.Settle();
                for (int i = 0; i < 4; i++) wrist.Draw(data);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20; i++) wrist.Draw(data);
                return (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            }
            long rest = Bytes();
            if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SocialAllocTypes("lights" + tag, () => wrist.Draw(data));
            Fake(data, new LampState { Status = LinkStatus.NoApp }, new RgbState { Status = LinkStatus.NoApp });
            long none = Bytes();
            if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SocialAllocTypes("lights none" + tag, () => wrist.Draw(data));
            Tools.Want($"lights view{tag}: a frame of every size at rest makes {rest} B of garbage (want 0), without the apps {none} B (want 0)", rest == 0 && none == 0);
            // the pictures
            void Shot(string name, List<WatchWidget> face, LampState l, RgbState r, string open = null, string grid = null)
            {
                Fake(data, l, r);
                wcfg.Grid = grid;
                wcfg.Widgets = face;
                wrist.Settle();
                if (open != null && !wrist.OpenForTest(open)) Log.Warn("lights view: nothing to open: " + open);
                wrist.Draw(data); wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_lights_{name}{tag}.png"));
                wrist.CloseForTest();
            }
            var offL = LampOn(); offL.On = false;
            var colL = LampOn(); colL.Temp = 0; colL.Hue = 285; colL.Sat = 80;
            Shot("face", Face64(), null, null);
            Shot("face43", Face43(), null, RgbOn("Static"), null, "4x3");
            Shot("face43b", Face43b(), colL, RgbOn("Breathing"), null, "4x3");
            Shot("off", Face64(), offL, RgbOn("Off"));
            Shot("none", Face64(), new LampState { Status = LinkStatus.NoApp }, new RgbState { Status = LinkStatus.NoApp });
            Shot("light_open", Face64(), null, null, "light");
            Shot("rgb_open", Face64(), null, RgbOn("Static"), "rgb");
            Shot("none_open", Face64(), new LampState { Status = LinkStatus.NoDevice }, null, "light");
            wcfg.Grid = null;
            Log.Info($"lights view{tag}: 8 pictures (wrist_lights_*{tag}.png)");
        }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background, data.Lamp, data.Rgb) = keep;
        }
    }
}

static partial class Tools
{
    // ORBIT_SNAP_ONLY=lights: the links' checks, the views' checks and pictures and the layout check (their sizes and opened only), in every style
    static int LightsOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        int failed0 = WantsFailed, layout = 0;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        LightsViewTools.DataChecks(wrist, data, dir);
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            LightsViewTools.Run(wrist, data, cfg, gpu, dir, tag);
            foreach (var kind in new[] { "light", "rgb" })
            {
                string f0 = cfg.Data.Wrist.Face;
                layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag.Length > 0 ? tag[1..] : "modern", onlyKind: kind);
                cfg.Data.Wrist.Face = f0;
                cfg.Data.Wrist.Widgets = null;
            }
        }
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        layout += StylesRun(wrist, data, painter, cfg, gpu, dir);           // every kind in every look (the light's and the RGB's among them)
        System.Globalization.CultureInfo.CurrentCulture = culture;
        Log.Info("snapshot (lights only) written to " + dir);
        return layout > 0 ? 1 : WantsExit(failed0);
    }
}
