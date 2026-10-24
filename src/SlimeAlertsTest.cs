using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Orbit;

// --snapshot's part for SlimeVR's alerts and lying down (Sys/SlimeAlerts.cs, OrbitApp.SlimeVr.cs; ORBIT_SNAP_ONLY=slimealerts, and at the
// end of =slimevr): made-up trackers only - the link on SlimeVrTest's made-up SolarXR server (a free port of 127.0.0.1, never SlimeVR's
// 21110), a script of its updates (batteries crossing the thresholds, a tracker vanishing and coming back, a flicker, one asleep, one
// disconnected), made-up head poses (sitting, a look at the watch low at the lap, lying on a side). Checked: the right notes once each,
// lying only after its 3 s and once per lying-down, the hint drawn only while lying, no ResetRequest without the two taps (the watch's
// strip, the card's button), nothing allocated a frame at rest, the setting's switch on the VRChat page. Pictures in every style: the hint
// on the face and opened, the card at rest and asking, the VRChat page.
static class SlimeAlertsTools
{
    static bool Want(string line, bool ok) => Tools.Want(line, ok);

    // ---- the server's script: update n (2 made-up seconds each for the alerts' clock) ----

    static SlimeVrViewTools.FakeTracker T(SlimeVrLink.Part p, int bat, int status, int n, string custom = null, float volt = 3.9f) =>
        new(p, bat, -60, n + 1, status, custom, volt);              // (the ping is n + 1: every update another snapshot)

    static List<(int id, SlimeVrViewTools.FakeTracker t)> Script(int n)
    {
        var l = new List<(int, SlimeVrViewTools.FakeTracker)>
        {
            (1, T(SlimeVrLink.Part.UpperChest, 87, n >= 12 ? SlimeVrLink.Sleeping : SlimeVrLink.Ok, n)),          // asleep from 12: not lost
            (2, T(SlimeVrLink.Part.Hip, 10, SlimeVrLink.Ok, n, volt: 4.36f)),                                    // 10 % but charging: nothing
        };
        if (n < 7 || n >= 10) l.Add((3, T(SlimeVrLink.Part.LeftThigh, 41, SlimeVrLink.Ok, n)));                // gone 7..9: lost at 9, back at 10
        l.Add((4, T(SlimeVrLink.Part.RightThigh, 52, n == 12 ? SlimeVrLink.TimedOut : n >= 14 ? SlimeVrLink.Disconnected : SlimeVrLink.Ok, n)));
        l.Add((5, T(SlimeVrLink.Part.LeftAnkle, n < 4 ? 23 : n == 4 ? 16 : n <= 8 ? 14 : n == 9 ? 30 : 10, SlimeVrLink.Ok, n)));      // low 6, 30 at 9, low again 11
        l.Add((6, T(SlimeVrLink.Part.RightAnkle, 12, SlimeVrLink.Ok, n, "Right foot strap")));                 // low from the start: said at 5
        return l;
    }

    static byte[] Feed(int n)
    {
        var d = SolarXr.Dialect.V22;
        var devices = new List<FbObj> { SlimeVrViewTools.Device(d, 0, new(SlimeVrLink.Part.Head, -1, int.MinValue, -1, SlimeVrLink.Ok), true, "HMD") };
        foreach (var (id, t) in Script(n)) devices.Add(SlimeVrViewTools.Device(d, id, t, false, $"udp://192.168.1.{120 + id}/0"));
        return new FbObj().Objs(SolarXr.BundleFeed, new FbObj().U8(0, SolarXr.FeedUpdate).Obj(1, new FbObj().Objs(0, devices.ToArray()))).Finish();
    }

    static readonly string[] WantNotes =
    {
        "Low Right foot strap 12", "Low Left ankle 14", "Lost Left thigh 0", "Back Left thigh", "Low Left ankle 10", "Lost Right thigh 1",
    };

    static string Say(SlimeAlerts.Note x) => x.Kind == SlimeAlerts.Kind.Back ? $"Back {x.Name}" : $"{x.Kind} {x.Name} {(x.Kind == SlimeAlerts.Kind.Low ? x.Battery : x.Status)}";

    static bool Until(Func<bool> ok, double seconds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ok()) { if (sw.Elapsed.TotalSeconds > seconds) return false; Thread.Sleep(20); }
        return true;
    }

    // head poses: sitting, a look at the watch low at the lap (bowed 65°, turned 25° on the neck: the roll alone reads 42°), lying on a side
    static Matrix4x4 Head(float yawDeg, float bowDeg, float rollDeg)
    {
        const float k = MathF.PI / 180;
        return Matrix4x4.CreateRotationZ(rollDeg * k) * Matrix4x4.CreateRotationY(yawDeg * k) * Matrix4x4.CreateRotationX(-bowDeg * k) * Matrix4x4.CreateTranslation(0, 1.1f, 0);
    }

    static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    static void Tap(NotificationPanel np, Vector2 p)
    {
        np.PointerMove(Hand.Left, p); np.Draw();
        np.PointerButton(Hand.Left, PtrButton.Primary, true, p); np.Draw();
        np.PointerButton(Hand.Left, PtrButton.Primary, false, p); np.Draw();
        np.PointerLeave(Hand.Left); np.Draw();
    }

    static List<WatchWidget> Face() => new()
    {
        new() { Type = "slimevr", X = 0, Y = 0, W = 2, H = 2 }, new() { Type = "slimevr", X = 2, Y = 0, W = 2, H = 1 }, new() { Type = "slimevr", X = 4, Y = 0, W = 1, H = 1 },
        new() { Type = "time", X = 5, Y = 0, W = 1, H = 1 }, new() { Type = "date", X = 2, Y = 1, W = 4, H = 1 },
        new() { Type = "media", X = 0, Y = 2, W = 6, H = 1 },
        new() { Type = "keyboard", X = 0, Y = 3 }, new() { Type = "desktops", X = 1, Y = 3 }, new() { Type = "recenter", X = 2, Y = 3 },
        new() { Type = "layout", X = 3, Y = 3 }, new() { Type = "night", X = 4, Y = 3 }, new() { Type = "settings", X = 5, Y = 3 },
    };

    static readonly List<(string tag, string[] files)> Pictures = new();

    public static int Only(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        int failed0 = Tools.WantsFailed;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = Tools.SnapCulture;
        Checks(wrist, data, cfg, gpu, painter, dir);
        System.Globalization.CultureInfo.CurrentCulture = culture;
        Log.Info("snapshot (slimealerts only) written to " + dir);
        return Tools.WantsExit(failed0);
    }

    public static void Checks(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, Painter painter, string dir)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var look0 = cfg.Data.Appearance;
        var (s0, r0, l0) = (data.SlimeVr, wrist.OnSlimeReset, data.SlimeLying);
        try
        {
            Settings(cfg);
            Notes();
            Lying();
            Taps(wrist, data, cfg, gpu, painter, dir);
            Looks(wrist, data, cfg, gpu, painter, dir);
            Tools.SlimeSettingsShots(cfg, gpu, painter, data, wrist, dir);
            Sheet(dir);
        }
        catch (Exception e) { Log.Error("slimevr alerts: " + e); Want("slimevr alerts: ran to the end", false); }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (data.SlimeVr, wrist.OnSlimeReset, data.SlimeLying) = (s0, r0, l0);
            cfg.Data.Appearance = look0;
            Theme.Apply(cfg.Data.Appearance);
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    // the setting: never switched it follows the widget; switched, it stays; null written as nothing
    static void Settings(ConfigStore cfg)
    {
        var c = cfg.Data;
        var keep = (c.SlimeVr, c.Wrist.Face, c.Wrist.Widgets, c.Wrist.Pages);
        try
        {
            c.SlimeVr = null;
            c.Wrist.Face = "custom"; c.Wrist.Pages = null;
            c.Wrist.Widgets = new List<WatchWidget> { new() { Type = "time", X = 0, Y = 0 } };
            bool noWidget = SlimeAlerts.On(c);
            c.Wrist.Widgets.Add(new WatchWidget { Type = "slimevr", X = 1, Y = 0 });
            bool withWidget = SlimeAlerts.On(c);
            c.SlimeVr = new SlimeVrConfig { Alerts = false };
            bool offAnyway = SlimeAlerts.On(c);
            string json = System.Text.Json.JsonSerializer.Serialize(new Config());
            Want($"slimevr alerts setting: never switched - without a SlimeVR widget {noWidget} (want False), with one {withWidget} (want True); switched off with " +
                 $"the widget {offAnyway} (want False); a fresh config writes no SlimeVr block {!json.Contains("SlimeVr")} -",
                 !noWidget && withWidget && !offAnyway && !json.Contains("SlimeVr"));
        }
        finally { (c.SlimeVr, c.Wrist.Face, c.Wrist.Widgets, c.Wrist.Pages) = keep; }
    }

    // the trackers on the made-up server: each note once, at the right update
    static void Notes()
    {
        using var server = new SlimeVrViewTools.FakeServer(SolarXr.Dialect.V22, Feed);
        if (server.Port == SlimeVrLink.ServerPort || server.Port <= 0) { Want($"slimevr alerts: the made-up server's port {server.Port} is not SlimeVR's", false); return; }
        using var link = new SlimeVrLink(server.Port);
        link.Active = true;
        var alerts = new SlimeAlerts();
        var notes = new List<SlimeAlerts.Note>();
        var said = new List<(int n, string what, string title)>();
        int lastN = -1, seen = 0;
        bool ran = Until(() =>
        {
            var s = link.Get();
            if (s.State != SlimeVrLink.State.Ok || s.Trackers.Count == 0) return false;
            int n = s.Trackers[0].Ping - 1;
            if (n == lastN) return false;
            lastN = n;
            seen++;
            notes.Clear();
            alerts.Trackers(1000 + 2.0 * n, s, notes);
            alerts.Trackers(1000 + 2.0 * n, s, notes);               // (the next frame: the same snapshot says nothing again)
            foreach (var x in notes) said.Add((n, Say(x), SlimeAlerts.Card(x).Title));
            return n >= 17;
        }, 40);
        link.Active = false;
        var got = said.Select(x => x.what).ToList();
        bool each = WantNotes.All(w => got.Count(g => g == w) == 1) && got.Count == WantNotes.Length;
        Log.Info("slimevr alerts: said " + string.Join("; ", said.Select(x => $"at {x.n}: {x.what} ('{x.title}')")));
        Want($"slimevr alerts on the made-up server (port {server.Port}): {seen} updates read to 17 {ran}; said {got.Count} note(s) (want {WantNotes.Length}, each once: " +
             $"{string.Join(", ", WantNotes)}) - nothing for the charging hip, the asleep chest, the thigh's 2 s flicker -", ran && each);
        var low = said.FirstOrDefault(x => x.what.StartsWith("Low Right foot", StringComparison.Ordinal));
        var lost = said.FirstOrDefault(x => x.what == "Lost Left thigh 0");
        Want($"slimevr alerts: when - the strap at {low.n} (want 5: seen 10 s), the left ankle at {said.FirstOrDefault(x => x.what == "Low Left ankle 14").n} (want 6: low " +
             $"2 s) and again at {said.FirstOrDefault(x => x.what == "Low Left ankle 10").n} (want 11: charged to 30 at 9), the thigh lost at {lost.n} (want 9: gone 3 s), back at " +
             $"{said.FirstOrDefault(x => x.what == "Back Left thigh").n} (want 10); the cards '{low.title}', '{lost.title}' -",
             low.n == 5 && lost.n == 9 && said.Any(x => x.what == "Low Left ankle 14" && x.n == 6) && said.Any(x => x.what == "Low Left ankle 10" && x.n == 11) &&
             said.Any(x => x.what == "Back Left thigh" && x.n == 10) && low.title == "Right foot strap: 12 %" && lost.title == "Left thigh lost");
        // SlimeVR gone: forgotten - a tracker already disconnected on the next connection was never seen connected: nothing
        var last = link.Get();
        notes.Clear();
        alerts.Trackers(2000, new SlimeVrLink.Snapshot { State = SlimeVrLink.State.NotRunning }, notes);
        int afterGone = notes.Count;
        var again = new SlimeVrLink.Snapshot { State = SlimeVrLink.State.Ok, Dialect = SolarXr.Dialect.V22 };
        again.Trackers.Add(new SlimeVrLink.Tracker { Device = 4, Num = 4, Part = SlimeVrLink.Part.RightThigh, Battery = 52, Status = SlimeVrLink.Disconnected, Imu = true });
        for (int i = 0; i < 10; i++) alerts.Trackers(2001 + i, again, notes);
        // nothing allocated a frame at rest (the same snapshot, the time going on)
        var steady = new SlimeVrLink.Snapshot { State = SlimeVrLink.State.Ok, Dialect = SolarXr.Dialect.V22 };
        steady.Trackers.AddRange(SlimeVrViewTools.Fake().Trackers);
        var quiet = new SlimeAlerts();
        var qn = new List<SlimeAlerts.Note>(8);
        for (int i = 0; i < 30; i++) quiet.Trackers(3000 + i * 0.1, steady, qn);
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) { quiet.Trackers(3010 + i * 0.011, steady, qn); quiet.Lie(3010 + i * 0.011, false, true, out _); }
        long per = (GC.GetAllocatedBytesForCurrentThread() - a0) / 200;
        Want($"slimevr alerts: SlimeVR gone says {afterGone} (want 0); on the next connection a tracker disconnected from the start says {notes.Count} (want 0); a frame " +
             $"at rest {per} B (want 0) -", afterGone == 0 && notes.Count == 0 && per == 0);
    }

    // lying: made-up head poses a tenth of a second apart
    static void Lying()
    {
        var sit = Head(0, 5, 0);
        var watch = Head(25, 65, 0);                 // bowed at the lap, turned on the neck
        var side = Head(0, 0, 80);                   // on a side
        bool sitLies = MathUtil.IsLying(sit, true), watchLies = MathUtil.IsLying(watch, true), sideLies = MathUtil.IsLying(side, true), offFace = MathUtil.IsLying(side, false);
        var a = new SlimeAlerts();
        int offers = 0;
        var log = new List<string>();
        double firstLie = -1, endLie = -1, offerAt = -1, offer2At = -1, lateOfferAt = -1;
        bool wasLying = false;
        // (seconds, pose, SlimeVR connected): sitting 5 s; on a side; up 1 s (a glance); on the side again; up 3 s; on the side; the watch look;
        // then lying with SlimeVR not there for 4 s, and connected after
        (double from, double to, Matrix4x4 pose, bool conn)[] script =
        {
            (0, 5, sit, true), (5, 12, side, true), (12, 13, sit, true), (13, 16, side, true), (16, 19, sit, true), (19, 24, side, true),
            (24, 30, watch, true), (30, 34, side, false), (34, 38, side, true),
        };
        foreach (var (from, to, pose, conn) in script)
            for (int k = (int)Math.Round(from * 10); k < (int)Math.Round(to * 10); k++)
            {
                double t = k / 10.0;
                bool lying = a.Lie(t, MathUtil.IsLying(pose, true), conn, out bool offer);
                if (lying && !wasLying) { if (firstLie < 0) firstLie = t; log.Add($"lying from {t:0.0}"); }
                if (!lying && wasLying) { if (endLie < 0) endLie = t; log.Add($"up from {t:0.0}"); }
                wasLying = lying;
                if (offer)
                {
                    offers++;
                    log.Add($"offered at {t:0.0}");
                    if (offerAt < 0) offerAt = t; else if (offer2At < 0) offer2At = t; else lateOfferAt = t;
                }
            }
        Log.Info("slimevr lying: " + string.Join(", ", log));
        Want($"slimevr lying poses: sitting {sitLies}, a look at the watch low at the lap {watchLies} (want False False), on a side {sideLies} (want True), " +
             $"on a side with the headset off {offFace} (want False) -", !sitLies && !watchLies && sideLies && !offFace);
        Want($"slimevr lying: lying from {firstLie:0.0} s (want 8.0: on the side from 5, 3 s), the 1 s glance up kept it, up from {endLie:0.0} (want 18.0: up from 16, 2 s); " +
             $"offered {offers} time(s) (want 3) - at {offerAt:0.0}, {offer2At:0.0} (want 8.0, 22.0), and lying without SlimeVR then with it at {lateOfferAt:0.0} (want 34.0) -",
             Math.Abs(firstLie - 8) < 0.05 && Math.Abs(endLie - 18) < 0.05 && offers == 3 && Math.Abs(offerAt - 8) < 0.05 && Math.Abs(offer2At - 22) < 0.05 &&
             Math.Abs(lateOfferAt - 34) < 0.05);
    }

    // the two taps: the watch's strip and the card's button send exactly one ResetRequest (yaw), a first tap none; lying alone sends none
    static void Taps(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, Painter painter, string dir)
    {
        using var server = new SlimeVrViewTools.FakeServer(SolarXr.Dialect.V22, n => SlimeVrViewTools.Update(SolarXr.Dialect.V22, new[]
        {
            new SlimeVrViewTools.FakeTracker(SlimeVrLink.Part.Hip, 64, -55, 9, SlimeVrLink.Ok), new SlimeVrViewTools.FakeTracker(SlimeVrLink.Part.LeftAnkle, 41, -61, 14, SlimeVrLink.Ok),
        }));
        if (server.Port == SlimeVrLink.ServerPort || server.Port <= 0) { Want($"slimevr taps: the made-up server's port {server.Port} is not SlimeVR's", false); return; }
        using var link = new SlimeVrLink(server.Port);
        link.Active = true;
        bool up = Until(() => link.Get() is { State: SlimeVrLink.State.Ok, Trackers.Count: 2, Dialect: SolarXr.Dialect.V22 }, 6);
        var wcfg = cfg.Data.Wrist;
        wcfg.Face = "custom"; wcfg.Background = "panel"; wcfg.Grid = null; wcfg.Pages = null; wcfg.Page = 0;
        wcfg.Widgets = new List<WatchWidget> { new() { Type = "slimevr", X = 0, Y = 0, W = 2, H = 2 } };
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        wrist.OnSlimeReset = k => link.Reset((SlimeVrLink.ResetKind)k);
        data.SlimeVr = link.Get();
        // lying a long while, nothing tapped: nothing sent (the card offered and drawn, the strip drawn)
        data.SlimeLying = true;
        wrist.Settle();
        for (int i = 0; i < 10; i++) wrist.Draw(data);
        using var np = new NotificationPanel(gpu, painter, cfg);
        var card = SlimeAlerts.YawCard(() => link.Reset(SlimeVrLink.ResetKind.Yaw));
        np.AddForPreview(card);
        for (int i = 0; i < 10; i++) np.Draw();
        Thread.Sleep(600);
        int idle = server.Resets(out _);
        // the strip: a first tap asks, the second sends
        int b0 = server.Resets(out _);
        var p = wrist.SlimeLiePoint(data);
        int afterOne = -1, afterTwo = -1, armed = -1;
        (int type, float delay, int parts) last = default;
        if (p is { } pp)
        {
            Tap(wrist, data, pp);
            armed = wrist.SlimeArmed;
            Thread.Sleep(600);
            afterOne = server.Resets(out _) - b0;
            Tap(wrist, data, pp);
            Until(() => server.Resets(out _) - b0 >= 1, 3);
            Thread.Sleep(400);
            afterTwo = server.Resets(out last) - b0;
        }
        Want($"slimevr lying strip (made-up server, port {server.Port}, up {up}): drawn {p != null}; lying and the card shown, untapped: {idle} ResetRequest(s) (want 0); a first " +
             $"tap asks (armed {armed}, want 0) and sends {afterOne} (want 0); the second sends {afterTwo} (want 1) of type {last.type} (want 0, yaw) -",
             up && p != null && idle == 0 && armed == 0 && afterOne == 0 && afterTwo == 1 && last.type == 0);
        // the card: the same
        int c0 = server.Resets(out _);
        var cp = np.ActionPoint();
        int cOne = -1, cTwo = -1, acts = -1;
        bool asked = false;
        if (cp is { } cpp)
        {
            Tap(np, cpp);
            asked = card.AskedAt > 0 && card.ActedAt < 0;
            Thread.Sleep(600);
            cOne = server.Resets(out _) - c0;
            Tap(np, cpp);
            Until(() => server.Resets(out _) - c0 >= 1, 3);
            Thread.Sleep(400);
            cTwo = server.Resets(out last) - c0;
            acts = np.Acts;
        }
        Want($"slimevr lying card: its button drawn {cp != null}; a first tap asks ({asked}) and sends {cOne} (want 0); the second sends {cTwo} (want 1) of type {last.type} " +
             $"(want 0), went out {card.ActOk}, {acts} act(s) counted (want 1) -", cp != null && asked && cOne == 0 && cTwo == 1 && last.type == 0 && card.ActOk && acts == 1);
        link.Active = false;
        wrist.OnSlimeReset = null;
        wrist.CloseForTest();
        wrist.PointerLeave(Hand.Left);
    }

    // per style: the hint only while lying (face and opened), 0 B a frame at rest; the pictures - the face lying, opened lying, the card at rest
    // and asking
    static void Looks(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, Painter painter, string dir)
    {
        var wcfg = cfg.Data.Wrist;
        wcfg.Face = "custom"; wcfg.Background = "panel"; wcfg.Grid = null; wcfg.Pages = null; wcfg.Page = 0;
        wrist.OnSlimeReset = null;
        foreach (var (style, tag) in new[] { ("modern", ""), (CyberHud.Id, "_cyber"), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            wcfg.Widgets = Face();
            data.SlimeVr = SlimeVrViewTools.Fake();
            // sitting: no hint, face or opened
            data.SlimeLying = false;
            wrist.CloseForTest();
            wrist.Settle();
            int h0 = wrist.SlimeHints;
            wrist.Draw(data); wrist.Draw(data);
            wrist.OpenForTest("slimevr");
            wrist.Draw(data); wrist.Draw(data);
            wrist.CloseForTest();
            int sitting = wrist.SlimeHints - h0;
            // lying: the face, then opened
            data.SlimeLying = true;
            wrist.Settle();
            h0 = wrist.SlimeHints;
            wrist.Draw(data); wrist.Draw(data);
            int onFace = wrist.SlimeHints - h0;
            string face = Path.Combine(dir, $"slimevr_lying{tag}.png");
            wrist.Surf.SavePng(gpu, face);
            using (Tools.AppAllocs())
            {
                wrist.OpenForTest("slimevr");
                wrist.Settle();
                h0 = wrist.SlimeHints;
                wrist.Draw(data); wrist.Draw(data);
                int opened = wrist.SlimeHints - h0;
                string open = Path.Combine(dir, $"slimevr_lying_open{tag}.png");
                wrist.Surf.SavePng(gpu, open);
                wrist.CloseForTest();
                // what a frame allocates at rest lying, with only the SlimeVR widgets on the face (the others' own checks are theirs), then opened
                wcfg.Widgets = Face().FindAll(x => x.Type == "slimevr");
                wrist.Settle();
                for (int i = 0; i < 12; i++) wrist.Draw(data);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20; i++) wrist.Draw(data);
                long perFace = (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
                if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SlimeAllocTypes("slimevr lying" + tag, () => wrist.Draw(data));
                wrist.OpenForTest("slimevr");
                for (int i = 0; i < 12; i++) wrist.Draw(data);
                a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20; i++) wrist.Draw(data);
                long perOpen = (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
                if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SlimeAllocTypes("slimevr lying opened" + tag, () => wrist.Draw(data));
                wrist.CloseForTest();
                wcfg.Widgets = Face();
                // not connected (SlimeVR not running) while lying: no hint
                data.SlimeVr = SlimeVrViewTools.None();
                wrist.Settle();
                h0 = wrist.SlimeHints;
                wrist.Draw(data); wrist.Draw(data);
                int gone = wrist.SlimeHints - h0;
                data.SlimeVr = SlimeVrViewTools.Fake();
                Want($"slimevr lying hint {(tag.Length == 0 ? "modern" : tag[1..])}: sitting {sitting} hint(s) drawn (want 0); lying, the face {onFace} (want > 0), opened {opened} " +
                     $"(want > 0); SlimeVR not running {gone} (want 0); a frame at rest lying {perFace} B, opened {perOpen} B (want 0, 0) -",
                     sitting == 0 && onFace > 0 && opened > 0 && gone == 0 && perFace == 0 && perOpen == 0);
                // the card: at rest, then asking (its first tap)
                using var np = new NotificationPanel(gpu, painter, cfg);
                np.AddForPreview(new Notification { Source = "SlimeVR", Title = Lang.F("{0} lost", Lang.T("Left thigh")), Content = Lang.T("Timed out - out of range?"),
                    Glyph = WristPanel.SlimeKindIcon });
                var card = SlimeAlerts.YawCard(null);
                np.AddForPreview(card);
                np.Draw();
                string rest = Path.Combine(dir, $"slimevr_card{tag}.png");
                np.Surface.SavePng(gpu, rest);
                var cp = np.ActionPoint();
                if (cp is { } cpp) Tap(np, cpp);
                np.Draw();
                string ask = Path.Combine(dir, $"slimevr_card_ask{tag}.png");
                np.Surface.SavePng(gpu, ask);
                Want($"slimevr lying card {(tag.Length == 0 ? "modern" : tag[1..])}: its button drawn {cp != null}, a first tap asks {card.AskedAt > 0 && card.ActedAt < 0} -",
                     cp != null && card.AskedAt > 0 && card.ActedAt < 0);
                Pictures.RemoveAll(x => x.tag == tag);
                Pictures.Add((tag, new[] { face, open, rest, ask }));
            }
        }
        data.SlimeLying = false;
    }

    // a style's VRChat page, for the sheet (Tools.SlimeSettingsShots)
    public static void AddSettings(string tag, string file)
    {
        int i = Pictures.FindIndex(x => x.tag == tag);
        if (i >= 0) Pictures[i] = (tag, Pictures[i].files.Append(file).ToArray());
    }

    // every style's pictures side by side, one row a style
    static void Sheet(string dir)
    {
        if (Pictures.Count == 0) return;
        const int W = 560, H = 385, Gap = 12, Label = 26;
        using var sheet = new Bitmap(5 * W + 6 * Gap, Pictures.Count * (H + Label + Gap) + Gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 32, 34, 40));
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int i = 0; i < Pictures.Count; i++)
            {
                var (t, files) = Pictures[i];
                float y = Gap + i * (H + Label + Gap);
                g.DrawString($"{(t.Length == 0 ? "modern" : t[1..])}: lying down - the face, opened, the card, the card asking (made-up trackers); the VRChat page, SlimeVR alerts on",
                    font, Brushes.White, Gap, y + 4);
                for (int k = 0; k < files.Length; k++)
                {
                    if (!File.Exists(files[k])) continue;
                    using var b = new Bitmap(files[k]);
                    float s = Math.Min((float)W / b.Width, (float)H / b.Height);
                    g.DrawImage(b, Gap + k * (W + Gap), y + Label, b.Width * s, b.Height * s);
                }
            }
        }
        string path = Path.Combine(dir, "slimealerts_preview.png");
        sheet.Save(path, ImageFormat.Png);
        Log.Info("slimevr alerts: every style side by side: " + path);
    }
}

static partial class Tools
{
    // the VRChat page in every style with SlimeVR alerts on (never switched, a SlimeVR widget on the face): beside the Room panel's button,
    // on the retro sheets a box under the eye tracking; a click on it switches them off (written), a second on again
    internal static void SlimeSettingsShots(ConfigStore cfg, Gpu gpu, Painter painter, PanelData data, WristPanel wrist, string dir)
    {
        var c = cfg.Data;
        var keep = (c.SlimeVr, c.Wrist.Face, c.Wrist.Widgets, c.Wrist.Pages, c.Appearance);
        var host = new FakeHost { Cfg = cfg, GpuName = gpu.AdapterName, MonitorList = Array.Empty<MonitorInfo>(), Data = data, Wrist = wrist };
        var view = new SettingsView(host) { Tab = SettingsView.TabVrchat };
        var ui = new Ui(painter);
        using var surf = new Surface(gpu, 1500, 960);
        var ptr = new PointerState();
        try
        {
            foreach (var (style, tag) in new[] { ("modern", ""), (CyberHud.Id, "_cyber"), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term") })
            {
                c.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(c.Appearance);
                c.SlimeVr = null;
                c.Wrist.Face = "custom"; c.Wrist.Pages = null;
                c.Wrist.Widgets = new List<WatchWidget> { new() { Type = "slimevr", X = 0, Y = 0, W = 2, H = 2 } };
                bool retro = Theme.Classic;
                RectangleF? at = null;
                void Frame()
                {
                    if (retro) { view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal); return; }
                    ui.Begin(ptr, 1.5f);
                    painter.Begin(surf.Bitmap, Theme.Bg, 1.5f);
                    view.Draw(ui);
                    painter.End();
                    ui.End();
                }
                ui.ResetAnimations();
                ptr.Leave();
                ui.HitSpy = (k, r) => { if (k is "vc.slime" or "vc95.slime") at = r; };
                Frame(); Frame();
                ui.HitSpy = null;
                string file = Path.Combine(dir, $"settings_vrchat_slime{tag}.png");
                surf.SavePng(gpu, file);
                SlimeAlertsTools.AddSettings(tag, file);
                bool? first = null, second = null;
                if (at is { } a)
                {
                    float s = retro ? 2 : 1.5f;
                    void Click() { ptr.Move((a.X + 12) * s, (a.Y + a.Height / 2) * s); Frame(); ptr.Press(); Frame(); ptr.Release(); Frame(); Frame(); }
                    Click();
                    first = c.SlimeVr?.Alerts;
                    Click();
                    second = c.SlimeVr?.Alerts;
                    ptr.Leave(); Frame();
                }
                Want($"slimevr alerts setting {(tag.Length == 0 ? "modern" : tag[1..])}: drawn {at != null}, on with the widget; a click switches it off ({first?.ToString() ?? "null"}, " +
                     $"want False), a second on ({second?.ToString() ?? "null"}, want True) -", at != null && first == false && second == true);
            }
        }
        finally
        {
            ui.HitSpy = null;
            (c.SlimeVr, c.Wrist.Face, c.Wrist.Widgets, c.Wrist.Pages, c.Appearance) = keep;
            Theme.Apply(c.Appearance);
        }
    }
}
