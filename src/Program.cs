using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Orbit;

static class Program
{
    public const string MutexName = @"Local\OrbitOverlaySingleton";

    // Modes:
    //   (none)            normal run: tray + settings window, overlays while SteamVR runs
    //   --steamvr         launched by SteamVR auto-start: start hidden in the tray
    //   --exit            ask the running Orbit to quit
    //   --no-elevate      run without admin rights this time even if 'Run as administrator' is on
    //   --from-task / --install-task / --restart   internal (elevated start, see Elevation)
    //   --snapshot <dir>  render every panel to PNG (no VR needed) and exit
    //   --clockshot <dir> only the clocks' designs (watch widgets): their checks and sheets
    //   --selftest        connect to a running SteamVR, check overlay/UV conventions, exit
    //   --scene [--dash]  dev: act as a running game for 12 s (closes the dashboard; --dash opens Orbit's tab)
    //   --osctest <port>  dev: send sample OSC messages
    //   --tasktest        dev: check the scheduled-task start path with a temporary, non-elevated task
    //   --filtertest      dev: laser smoothing jitter/lag per level
    //   --focustest       dev: text-field detection on an off-screen test window
    //   --leaktest <gpu|media|idle> [seconds]   dev: private memory of one background component
    //   --raytest         dev: LocalRaycast vs SteamVR's ComputeOverlayIntersection on a hidden overlay
    //   --updatetest      dev: GitHub release check + download + verify (does not install)
    //   --mixertest       dev: the default output device's apps as the mixer reads them (read only: nothing is set)
    //   --setup [--install-dir <dir>] [--no-config] [--quiet]   installer (also when the exe name contains "setup")
    //   (setup) --update --install-dir <dir>   started by "Check for updates": silent install over that copy, restart
    //   --uninstall [--quiet]   remove the installed copy (used by Settings > Apps)
    // set by the installer's --update when it starts the new version (shown as "updated from ...")
    public static string UpdatedFrom;

    [STAThread]
    static int Main(string[] args)
    {
        int uf = Array.IndexOf(args, "--updated-from");
        if (uf >= 0 && uf + 1 < args.Length) UpdatedFrom = args[uf + 1];
        bool has(string a) => args.Contains(a);
        if (has("--tempstest")) { Log.Init("orbit-tool.log"); return TempSensors.Probe(); }      // the real temperature sources read twice, read only (Sys/Temps.cs)
        if (has("--avatartest")) { Log.Init("orbit-tool.log"); return AvatarProbe.Run(); }      // VRChat's real OSC configs read, only counts logged (AvatarViewTest.cs)
        bool tool = has("--snapshot") || has("--animstrip") || has("--faceshot") || has("--clockshot") || has("--selftest") || has("--scene") || has("--osctest") || has("--tasktest") || has("--filtertest") || has("--focustest") || has("--leaktest") || has("--raytest") || has("--updatetest") || has("--wintest") || has("--docktest") || has("--roomtest") || has("--theatertest") || has("--perfbench") || has("--ovdump") || has("--pacetest") || has("--pacertest") || has("--capturetest") || has("--voltest") || has("--mixertest") || has("--tapotest") || has("--eyetest") || has("--mixtapetest") || has("--virtualstatus") || has("--tapofind") || has("--tapofollow") || has("--statsbench") || has("--albumbench");
        if (tool) return RunTool(args);

        // install / uninstall (the same exe: named "OrbitSetup.exe" it is the installer)
        if (has("--remove-task"))
            return Installer.RemoveTaskFor(args[Array.IndexOf(args, "--remove-task") + 1]);
        if (has("--uninstall"))
        {
            ApplicationConfiguration.Initialize();
            return Installer.Uninstall(has("--quiet"), !has("--no-stop"));
        }
        if (Installer.IsSetupMode(args))
        {
            ApplicationConfiguration.Initialize();
            return Installer.RunSetup(args);
        }

        if (has("--exit"))
        {
            NamedObjects.Signal(OrbitApp.ExitEventName);
            return 0;
        }

        bool hidden = has("--steamvr");
        bool restart = has("--restart");
        if (!restart && NamedObjects.InstanceExists(MutexName))
        {
            // already running: bring its settings window up (unless SteamVR started us)
            if (!hidden) NamedObjects.Signal(OrbitApp.ShowEventName);
            return 0;
        }

        ApplicationConfiguration.Initialize();

        bool wantAdmin = new ConfigStore().Data.General.RunAsAdmin;
        if (wantAdmin && !Elevation.IsElevated && !has("--from-task") && !has("--no-elevate"))
        {
            Log.Init("orbit-launch.log");
            string pass = (hidden ? "--steamvr" : "--show") + (restart ? " --restart" : "") +
                          (UpdatedFrom != null ? " --updated-from " + UpdatedFrom : "");
            if (Elevation.RunTask(pass)) return 0;
            // no task yet (first run, or the exe moved): one UAC prompt, the elevated copy installs it.
            // Not from SteamVR though - a prompt on the desktop while in VR helps nobody.
            if (!hidden && Elevation.RelaunchWithPrompt("--install-task " + pass)) return 0;
            Log.Warn("continuing without admin rights");
        }

        if (restart)
        {
            // the previous instance is shutting down; wait for it to let go
            for (int i = 0; i < 100 && NamedObjects.InstanceExists(MutexName); i++) Thread.Sleep(100);
        }
        if (!NamedObjects.TryTakeInstance(MutexName))
        {
            if (!hidden) NamedObjects.Signal(OrbitApp.ShowEventName);
            return 0;
        }

        Log.Init("orbit.log");
        // multicore JIT (10-24): the methods this start compiles are noted in startup.jitprofile, and the next start compiles them on
        // background cores while it starts - the first frames after SteamVR connects no longer compile every method they meet on the main
        // loop (the log's boot: line: the first frame 78 ms, the watch's first draw 134 ms). ReadyToRun did the same but grew the
        // installer from 52 to 74 MB.
        try
        {
            System.Runtime.ProfileOptimization.SetProfileRoot(Log.Dir);
            System.Runtime.ProfileOptimization.StartProfile("startup.jitprofile");
        }
        catch (Exception e) { Log.Warn("multicore JIT: " + e.Message); }
        AppFiles.EnsureNextToExe();    // SteamVR manifest + input bindings next to the exe
        bool elevated = Elevation.IsElevated;
        Log.Info($"Orbit {typeof(Program).Assembly.GetName().Version} start, args: {string.Join(' ', args)}, elevated: {elevated}");
        try
        {
            if (elevated && wantAdmin && (has("--install-task") || !Elevation.TaskReady()))
                Elevation.InstallTask();

            using var app = new OrbitApp(startHidden: hidden);
            app.Run();
            return 0;
        }
        catch (Exception e)
        {
            Log.Error("fatal: " + e);
            MessageBox.Show(e.Message, Lang.T("OrbitOverlay crashed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // dev: Virtual Desktop's eye tracking, READ ONLY (Sys/EyeGaze.cs) - 8 s of what the map says, 5 lines a second: look around (ahead, left,
    // right, up, down) and blink while it runs; nothing is written anywhere
    static int EyeProbe()
    {
        using var g = new EyeGaze();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int lines = 0, valid = 0;
        while (sw.Elapsed.TotalSeconds < 8)
        {
            double now = sw.Elapsed.TotalSeconds;
            g.Update(now, true);
            if (now * 5 >= lines)
            {
                lines++;
                var s = g.Last;
                if (!g.Present) { Log.Info("eyes: no VirtualDesktop.BodyState map (the streamer, 'Forward tracking data' on?)"); }
                else
                {
                    var d = g.HeadDir;
                    float yaw = MathF.Atan2(d.X, -d.Z) * 180 / MathF.PI, pitch = MathF.Asin(Math.Clamp(d.Y, -1, 1)) * 180 / MathF.PI;
                    var ql = s.LeftEyePose.Orientation; var qr = s.RightEyePose.Orientation;
                    if (g.Valid) valid++;
                    Log.Info($"eyes: {(g.Valid ? "valid" : "-----")} look {yaw,6:0.0} right {pitch,6:0.0} up | L valid {s.LeftEyeIsValid} conf {s.LeftEyeConfidence:0.00} |q| {ql.Length():0.000} " +
                             $"at {s.LeftEyePose.Position.X:0.000},{s.LeftEyePose.Position.Y:0.000},{s.LeftEyePose.Position.Z:0.000} | R valid {s.RightEyeIsValid} conf {s.RightEyeConfidence:0.00} " +
                             $"|q| {qr.Length():0.000} | flags {s.FaceFlags} follow {s.EyeFollowingBlendshapesValid}");
                }
            }
            System.Threading.Thread.Sleep(10);
        }
        Log.Info($"eyes: {valid} of {lines} readings valid");
        return g.Present ? 0 : 1;
    }

    // dev: the bulb straight (TapoDirect), READ ONLY - its state with the key from Hexpad's / Lumos's account (made in memory, not kept),
    // then with a wrong key (must say "Wrong Tapo account"). Logs the state's shape only: never the account, the key or the bulb's name.
    static int TapoProbe(string host)
    {
        if (host.Length == 0) { Log.Warn("tapo: --tapotest <bulb IP>"); return 2; }
        var kept = TapoAccount.Import(out string from);
        var key = TapoAccount.Unprotect(kept);
        if (key == null) { Log.Warn("tapo: no account in Hexpad or Lumos"); return 1; }
        var t0 = DateTime.UtcNow;
        var (code, body) = new TapoDirect(key).Handle("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);
        var s = LumosLink.Parse(code, body);
        Log.Info($"tapo: key from {from} - {code} in {(DateTime.UtcNow - t0).TotalMilliseconds:0} ms, status {s.Status}{(s.Ready ? "" : " (" + s.Message + ")")}, " +
                 $"on {s.On}, brightness {(s.Brightness > 0)}, colour {s.HasColor}, white {s.HasTemp} {s.TempMin}-{s.TempMax} K, named {s.Name.Length > 0}");
        var (wc, wb) = new TapoDirect(TapoAccount.KeyFor("nobody@example.com", "wrong")).Handle("GET", "/api/state?host=" + Uri.EscapeDataString(host), null);
        var w = LumosLink.Parse(wc, wb);
        Log.Info($"tapo: a wrong key - {wc}, '{w.Message}' (want 'Wrong Tapo account')");
        return s.Ready && w.Message == "Wrong Tapo account" ? 0 : 1;
    }

    // dev: the bulb found again (Sys/RoomLights.cs Follow): a link with the key, given a dead address (192.168.0.87 when none), READ ONLY -
    // state reads and TP-Link's probe; want a move to where the bulb is within 60 s. Nothing is put in any config.
    static int TapoFollow(string dead)
    {
        var kept = TapoAccount.Import(out _);
        if (TapoAccount.Unprotect(kept) == null) { Log.Warn("tapo: no account in Hexpad or Lumos"); return 1; }
        using var link = new LumosLink();
        link.SetKey(kept);
        link.SetBulb(string.IsNullOrEmpty(dead) || dead.StartsWith("--") ? "192.168.0.87" : dead);
        link.Active = true;
        link.Watched = true;
        var t0 = DateTime.UtcNow;
        for (int i = 0; i < 60 && (link.Found == null || link.Found.From == link.Found.To); i++) Thread.Sleep(1000);
        var f = link.Found;
        Log.Info(f == null || f.From == f.To ? "tapofollow: not found in 60 s"
            : $"tapofollow: {f.From} -> {f.To} ({f.Mac}) in {(DateTime.UtcNow - t0).TotalSeconds:0} s, state {link.State.Status}");
        return f != null && f.To != f.From ? 0 : 1;
    }

    // dev: the watch's volume control on this PC's default speakers (per-app on Virtual Desktop's, through the mixer as in VR); "dip":
    // 5 % down for 2 s, then back
    static int VolTest(bool dip)
    {
        using var mx = new AudioMixer();
        mx.Tick(0, true);
        var v = new SystemVolume { Engine = mx };
        v.Sample(0);
        Log.Info($"voltest: '{v.DeviceName}' level {v.Level:0.00} muted {v.Muted} per-app {v.PerApp}");
        if (v.Level < 0) return 1;
        float l0 = v.Level;
        v.Set(l0);
        if (dip)
        {
            v.Set(l0 - 0.05f);
            Thread.Sleep(2000);
            v.Sample(10);
            Log.Info($"voltest: after the dip level {v.Level:0.00} (want {l0 - 0.05f:0.00})");
            v.Set(l0);
        }
        v.Sample(20);
        Log.Info($"voltest: back at {v.Level:0.00} (want {l0:0.00})");
        v.Dispose();
        return 0;
    }

    static int RunTool(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Log.Init("orbit-tool.log");
        Log.Info($"Orbit {typeof(Program).Assembly.GetName().Version} tool, args: {string.Join(' ', args)}, elevated: {Elevation.IsElevated}");
        try
        {
            Motion.Still = !args.Contains("--perfbench") && !args.Contains("--animstrip");     // pictures: loops held at rest
            // --lang hu (or ORBIT_SNAP_LANG): the pictures in another language (ToolsLang.cs); never written to a config
            int lg = Array.IndexOf(args, "--lang");
            string lang = lg >= 0 && lg + 1 < args.Length ? args[lg + 1] : Environment.GetEnvironmentVariable("ORBIT_SNAP_LANG");
            if (!string.IsNullOrEmpty(lang)) Lang.Set(lang);
            if (args.Contains("--animstrip"))       // the styles' animations as frames (ToolsAnim.cs) - --only-style modern|95|xp|aero
            {
                int i = Array.IndexOf(args, "--animstrip"), o = Array.IndexOf(args, "--only-style");
                return Tools.AnimStrips(i + 1 < args.Length ? args[i + 1] : "animstrip", o >= 0 && o + 1 < args.Length ? args[o + 1] : null);
            }
            if (args.Contains("--snapshot"))
            {
                int i = Array.IndexOf(args, "--snapshot"), o = Array.IndexOf(args, "--only-style");    // --only-style 95|xp|aero|mac|term
                return Tools.Snapshot(i + 1 < args.Length ? args[i + 1] : "snapshot", o >= 0 && o + 1 < args.Length ? args[o + 1] : null);
            }
            if (args.Contains("--clockshot"))
            {
                int i = Array.IndexOf(args, "--clockshot");
                return Tools.ClockShot(i + 1 < args.Length ? args[i + 1] : "clockshot");
            }
            if (args.Contains("--faceshot"))
            {
                int i = Array.IndexOf(args, "--faceshot");
                return Tools.FaceShot(i + 1 < args.Length ? args[i + 1] : "faceshot");
            }
            if (args.Contains("--roomtest"))
            {
                int i = Array.IndexOf(args, "--roomtest");
                return Tools.RoomTest(i + 1 < args.Length ? args[i + 1] : "roomtest");
            }
            if (args.Contains("--voltest"))
                return VolTest(args.Contains("dip"));
            if (args.Contains("--mixertest"))
                return MixerViewTools.Probe();
            if (args.Contains("--eyetest"))
                return EyeProbe();
            if (args.Contains("--mixtapetest"))
                return MixtapeLinkTest.Run(args.Contains("live"));
            if (args.Contains("--tapotest"))
            {
                int i = Array.IndexOf(args, "--tapotest");
                return TapoProbe(i + 1 < args.Length ? args[i + 1] : "");
            }
            if (args.Contains("--capturetest"))
                return CaptureTest.Run();
            if (args.Contains("--pacertest"))
                return PaceTest.Pacer();
            if (args.Contains("--pacetest"))
            {
                int i = Array.IndexOf(args, "--pacetest");
                return PaceTest.Run(i + 1 < args.Length ? double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture) : 20);
            }
            if (args.Contains("--virtualstatus")) return VirtualMonitors.Status();      // read only: the driver's device and its settings file
            if (args.Contains("--statsbench")) return StatsBench.Run();             // read only: the perf log's SteamVR calls, timed
            if (args.Contains("--albumbench")) return GalleryViewTools.Bench();     // read only: the gallery's album on VRChat's real folder, timed
            if (args.Contains("--tapofollow"))                                          // read only: a dead address found again
            {
                int i = Array.IndexOf(args, "--tapofollow");
                return TapoFollow(i + 1 < args.Length ? args[i + 1] : "");
            }
            if (args.Contains("--tapofind"))                                            // read only: the Tapo devices that answer TP-Link's probe
            {
                var all = TapoFind.Find(2000);
                foreach (var f in all) Log.Info("tapofind: " + f);
                Log.Info($"tapofind: {all.Count} answered");
                return all.Count > 0 ? 0 : 1;
            }
            if (args.Contains("--ovdump"))
            {
                int i = Array.IndexOf(args, "--ovdump");
                return OverlayDump.Run(i + 1 < args.Length ? double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture) : 60);
            }
            if (args.Contains("--perfbench"))
            {
                int i = Array.IndexOf(args, "--perfbench"), s = Array.IndexOf(args, "--bench-style"), v = Array.IndexOf(args, "--bench-variant");
                return PerfBench.Run(i + 1 < args.Length ? args[i + 1] : "perfbench", s >= 0 && s + 1 < args.Length ? args[s + 1] : null,
                    v >= 0 && v + 1 < args.Length ? args[v + 1] : null);
            }
            if (args.Contains("--theatertest"))
            {
                int i = Array.IndexOf(args, "--theatertest");
                return TheaterTest.Run(i + 1 < args.Length ? args[i + 1] : "theatertest");
            }
            if (args.Contains("--docktest"))
            {
                int i = Array.IndexOf(args, "--docktest");
                return Tools.DockTest(i + 1 < args.Length ? args[i + 1] : "docktest");
            }
            if (args.Contains("--wintest"))
            {
                int i = Array.IndexOf(args, "--wintest");
                return Tools.WinTest(i + 1 < args.Length ? args[i + 1] : "wintest");
            }
            if (args.Contains("--osctest"))
                return Tools.OscTest(int.Parse(args[Array.IndexOf(args, "--osctest") + 1]));
            if (args.Contains("--scene"))
                return Tools.FakeScene(args.Contains("--dash"));
            if (args.Contains("--tasktest"))
                return Tools.TaskTest();
            if (args.Contains("--filtertest"))
                return Tools.FilterTest();
            if (args.Contains("--focustest"))
                return Tools.FocusTest();
            if (args.Contains("--raytest"))
                return RayTest.Run();
            if (args.Contains("--updatetest"))
                return Updater.Test();
            if (args.Contains("--leaktest"))
            {
                int li = Array.IndexOf(args, "--leaktest");
                string what = li + 1 < args.Length ? args[li + 1] : "idle";
                int secs = li + 2 < args.Length && int.TryParse(args[li + 2], out int s2) ? s2 : 60;
                return Tools.LeakTest(what, secs);
            }
            return Tools.SelfTest();
        }
        catch (Exception e)
        {
            Log.Error("tool failed: " + e);
            return 1;
        }
    }
}
