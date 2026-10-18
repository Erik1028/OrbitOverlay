using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using Valve.VR;

namespace Orbit;

// ORBIT_SNAP_ONLY=theaterperf --snapshot <dir>: what a frame of theater mode costs (Erik 10-18: "it must be optimized, get the maximum
// out of it"). No VR: VrOverlay.Fake - the overlays exist, every SteamVR call is counted (VrOverlay.Calls), none is sent. A made-up
// headset, a made-up monitor panel with a made-up 2560 x 1440 picture (60 new frames a second, handed over as DesktopPanel does; a cut
// to other colours twice a second, so the Ambilight's glow keeps easing as it does with a film), made-up music for the horizon, a made-up
// media session for the bar. A short run first warms the JIT and the fonts (the app has those warm by the time theater starts). The theater is started and run 90 frames a simulated second, each frame as
// the app's: Theater.Update, the bar's Update (the laser on the big screen: the bar stays up), the screen's placement, Cyber fx and
// capture hand-over, a GPU flush - the GPU idle before each one, like FrameCost. The 45 frames before theater (the style's textures
// made ready a piece a frame: TheaterWarm.cs), the first 3 s (coming in: the glide, the city's lights, the sweep) apart, then 20 s at
// rest; for each frame the CPU ms (and by section: PerfTrace), the bytes allocated, the SteamVR calls by
// kind (and the ones the setters spared), the texture bytes handed to SetTexture. Three times: Cyber with Motion Full, Cyber with Motion
// Reduced, Modern (the cinema hall). Then the one-off costs (each texture drawn the first time), and the panels at rest in VR terms (their
// Update with a fake overlay, paced at 90 Hz for a few seconds: how often each redraws and hands its texture over, the calls a frame).
// Lines in the log ("theaterperf: ...") and <dir>/theaterperf.tsv (scenario, what, average, worst) for the baseline table.
// ORBIT_THEATERPERF_SECONDS: the stretch at rest (20); ORBIT_THEATERPERF_PANELS: the panels' stretch (6; 0: left out).
static partial class Tools
{
    // a monitor panel as theater perf sees it: DesktopPanel.Update's placement, fade, sort order, Cyber fx and capture hand-over, with
    // a made-up picture whose frame number the tool advances instead of a capture
    sealed class PerfScreen : ITheaterScreen, IDisposable
    {
        readonly Gpu _g;
        public readonly VrOverlay Ov;
        public Surface Pic;
        public long Frame;                       // the capture's frame number (the tool: 60 a second)
        public Matrix4x4 HomeWorld;
        public int Handovers;                    // SetTexture calls (a new capture frame each)
        float _vis, _opacity = 1;
        bool _hasTexture;
        long _shownFrame = -1;
        double _nextFrame;
        CyberScreenFx _fx;

        public PerfScreen(Gpu g, string key)
        {
            _g = g;
            Ov = new VrOverlay(key, "OrbitOverlay theater perf screen");
            Ov.SetFlag(VROverlayFlags.IgnoreTextureAlpha, true);
            Ov.SetSortOrder(100);
        }

        public void Update(VrSession vr, double now, float dt)
        {
            long t0 = PerfTrace.Now();
            var th = Theater;
            bool want = !TheaterHidden;
            if (want)
            {
                Ov.SetSortOrder(th == null ? 100u : th.Behind ? 106u : 107u);
                Ov.SetAbsolute(th?.World ?? HomeWorld);
            }
            _vis = Motion.Step(_vis, want && _hasTexture, dt, 0.2f);
            if (_vis <= 0) Ov.Hide();
            else
            {
                if (want)
                {
                    Ov.SetWidth(th?.Width ?? 0.6f);
                    Ov.SetCurvature(th?.Curvature ?? 0.05f);
                    _opacity = th?.Opacity ?? 1;
                }
                Ov.SetAlpha(_opacity * (1 - MathF.Pow(1 - _vis, 3)));
                Ov.Show();
            }
            PerfTrace.Add(PerfTrace.ScreenPlace, t0);
            long t1 = PerfTrace.Now();
            if (Theme.Cyber || _fx != null) (_fx ??= new CyberScreenFx(_g, Ov.Key)).Update(Ov, _vis, want, th != null, TextureSize, now);
            PerfTrace.Add(PerfTrace.ScreenFx, t1);
            // the capture: polled at the headset's rate (the screen is looked at), a new frame handed over as it comes
            long t2 = PerfTrace.Now();
            if (want && DesktopPanel.CaptureDue(ref _nextFrame, now, 90) && Frame != _shownFrame)
            {
                _shownFrame = Frame;
                Ov.SetMouseScale(Pic.W, Pic.H);
                Ov.SetTexture(Pic.Tex);
                Handovers++;
                _hasTexture = true;
            }
            PerfTrace.Add(PerfTrace.ScreenPlace, t2);
        }

        public TheaterView Theater { get; set; }
        public bool TheaterHidden { get; set; }
        public string TheaterLabel => "DELL U2723QE";
        public bool Playable => true;
        public bool HomeInWorld => true;
        public string TheaterState => "made up";
        public float TheaterFade => _vis;
        public void TheaterVanish() => _vis = 0;
        public Surface TheaterPicture(out long frame) { frame = Frame; return _hasTexture ? Pic : null; }
        public bool Home(out Matrix4x4 world, out float width, out float curvature, out float opacity)
        {
            world = HomeWorld; width = 0.6f; curvature = 0.05f; opacity = 1;
            return true;
        }
        public VrOverlay Overlay => Ov;
        public bool Interactive => Ov.Visible && _vis > 0.8f;
        public bool AcceptsFrom(Hand h) => true;
        public Vector2 TextureSize => new(Pic.W, Pic.H);
        public void PointerMove(Hand h, Vector2 px) { }
        public void PointerLeave(Hand h) { }
        public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px) { }
        public void PointerScroll(Hand h, float notches, Vector2 px) { }
        public bool Grabbable => true;
        public Matrix4x4 WorldTransform => Theater?.World ?? HomeWorld;
        public void GrabBegin(bool detach) { }
        public void GrabMove(Matrix4x4 world) { }
        public void GrabStickX(float v, float dt) { }
        public void GrabTwoHandBegin() { }
        public void GrabTwoHandScale(float ratio) { }
        public bool GrabSnapPreview => false;
        public void GrabEnd() { }
        public void Dispose() { _fx?.Dispose(); Ov.Dispose(); }
    }

    // what a stretch of frames cost: the frames' CPU ms and bytes (sum and worst), the SteamVR calls by kind, the textures handed over
    sealed class PerfRun
    {
        public int Frames, MaxCalls, WorstAt = -1;
        public double SumMs, MaxMs, FirstMs = -1;
        public long SumBytes, MaxBytes, FirstBytes, TexBytes, TexSame;
        public long TheaterBytes, BarBytes, ScreenBytes;        // the bytes by call (Frame adds them)
        public readonly long[] Sent = new long[VrOverlay.Calls.Kinds], Spared = new long[VrOverlay.Calls.Kinds];
        readonly int[] _s0 = new int[VrOverlay.Calls.Kinds], _p0 = new int[VrOverlay.Calls.Kinds];
        long _tex0, _same0;

        public void Before()
        {
            Array.Copy(VrOverlay.Calls.Sent, _s0, _s0.Length);
            Array.Copy(VrOverlay.Calls.Spared, _p0, _p0.Length);
            _tex0 = VrOverlay.Calls.TextureBytes;
            _same0 = VrOverlay.Calls.TextureSame;
        }

        public void After(double ms, long bytes)
        {
            if (FirstMs < 0) { FirstMs = ms; FirstBytes = bytes; }
            if (ms > MaxMs) WorstAt = Frames;
            Frames++;
            SumMs += ms; MaxMs = Math.Max(MaxMs, ms);
            SumBytes += bytes; MaxBytes = Math.Max(MaxBytes, bytes);
            int calls = 0;
            for (int k = 0; k < _s0.Length; k++)
            {
                int d = VrOverlay.Calls.Sent[k] - _s0[k];
                Sent[k] += d; calls += d;
                Spared[k] += VrOverlay.Calls.Spared[k] - _p0[k];
            }
            MaxCalls = Math.Max(MaxCalls, calls);
            TexBytes += VrOverlay.Calls.TextureBytes - _tex0;
            TexSame += VrOverlay.Calls.TextureSame - _same0;
        }

        public long SentTotal { get { long n = 0; foreach (long x in Sent) n += x; return n; } }
        public long SparedTotal { get { long n = 0; foreach (long x in Spared) n += x; return n; } }
    }

    static int TheaterPerfOnly(ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        var look0 = cfg.Data.Appearance;
        bool live0 = CyberFx.Live, still0 = Motion.Still;
        var fxPainter0 = CyberScreenFx.Painter;
        var rows = new List<string> { "scenario\twhat\taverage\tworst" };
        VrOverlay.Fake = true;
        VrOverlay.Calls.Sizes = true;
        PerfTrace.On = true;
        try
        {
            Motion.Still = false;
            CyberFx.Live = true;                           // Cyber's effects run as on the headset
            CyberScreenFx.Painter = painter;               // the screens' HUD frames are drawn (the app sets it; the tools have none)
            double seconds = double.TryParse(Environment.GetEnvironmentVariable("ORBIT_THEATERPERF_SECONDS"), out var s) && s > 0 ? s : 20;
            double panels = double.TryParse(Environment.GetEnvironmentVariable("ORBIT_THEATERPERF_PANELS"), out var p) && p >= 0 ? p : 6;
            cfg.Data.Appearance = new AppearanceConfig { Style = CyberHud.Id, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            TheaterPerfRun("warm-up", cfg, painter, gpu, 1, rows, warm: true);     // the JIT and the fonts: not in the numbers
            var runs = new (string name, string style, string motion)[] { ("Cyber Full", CyberHud.Id, null), ("Cyber Reduced", CyberHud.Id, "reduced"), ("Modern Full", null, null) };
            foreach (var (name, style, motion) in runs)
            {
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Motion = motion, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                TheaterPerfRun(name, cfg, painter, gpu, seconds, rows);
            }
            cfg.Data.Appearance = new AppearanceConfig { Style = CyberHud.Id, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            TheaterPerfOnce(cfg, painter, gpu, rows);
            if (panels > 0) PanelsAtRestVr(cfg, painter, gpu, panels, rows);
        }
        catch (Exception e) { Log.Error("theaterperf: " + e); }
        finally
        {
            VrOverlay.Fake = false;
            VrOverlay.Calls.Sizes = false;
            VrOverlay.Calls.Reset();
            PerfTrace.On = false;
            PerfTrace.Reset();
            CyberScreenFx.Painter = fxPainter0;
            CyberFx.Live = live0;
            Motion.Still = still0;
            cfg.Data.Appearance = look0;
            Theme.Apply(look0);
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        File.WriteAllLines(Path.Combine(dir, "theaterperf.tsv"), rows);
        Log.Info("theaterperf: done - " + Path.Combine(dir, "theaterperf.tsv"));
        return 0;
    }

    static void TheaterPerfRun(string name, ConfigStore cfg, Painter painter, Gpu gpu, double seconds, List<string> rows, bool warm = false)
    {
        const float Dt = 1 / 90f;
        var vr = new VrSession();
        vr.Device[0] = Matrix4x4.CreateTranslation(0, 1.6f, 0);        // level, facing -z, 1.1 m from the monitor
        vr.DeviceValid[0] = true;
        // the picture: coloured edges (the Ambilight has something to sample), a gradient in the middle
        var (pic, _) = AmbPicture(gpu, 2560, 1440, (x, y) => x < 560 ? Rgb(200, 60, 40) : x >= 2000 ? Rgb(40, 90, 220) : Rgb(40 + 80 * x / 2560, 50 + 60 * y / 1440, 80));
        var (picA, _) = AmbPicture(gpu, 2560, 1440, (x, y) => x < 560 ? Rgb(200, 60, 40) : x >= 2000 ? Rgb(40, 90, 220) : Rgb(40 + 80 * x / 2560, 50 + 60 * y / 1440, 80));
        var (picB, _) = AmbPicture(gpu, 2560, 1440, (x, y) => y < 300 ? Rgb(230, 190, 40) : y >= 1140 ? Rgb(40, 200, 90) : Rgb(60, 60 + 60 * x / 2560, 90 + 60 * y / 1440));
        using var scr = new PerfScreen(gpu, "erik.orbit.desktop.0") { Pic = pic, HomeWorld = Matrix4x4.CreateTranslation(0, 1.4f, -1.1f) };
        var live = new List<ITheaterScreen> { scr };
        var choices = new List<ITheaterScreen> { new TheaterTest.FakeScreen { Name = "AOC 24G4X" }, scr, new TheaterTest.FakeScreen { Name = "Xiaomi Monitor" } };
        var tcfg = cfg.Data.Theater;
        using var th = new Theater(gpu, painter) { LifeSeed = 1018, AmbilightOn = true };
        th.CreateOverlay();
        double clock = 0;                                              // the simulated seconds (music, the media's position)
        th.SoundLevel = () => PSignal(PSig.Music, clock);
        using var bar = new TheaterBar(gpu, painter, cfg);
        bar.CreateOverlay();
        var media = new TheaterBar.BarMedia { Has = true, Title = "Midnight City", Artist = "M83", Playing = true, Timeline = true, Pos = 134, Dur = 243, CanSeek = true };
        bar.ReadMedia = () => { var m = media; m.Pos = Math.Min(m.Dur, 134 + clock); return m; };
        bar.KeyboardShown = () => false;
        double now = Clock.Now;                                        // simulated from here on, 1/90 s a frame
        long thTicks = 0, thMax = 0;

        int frameNo = 0;
        void Frame(PerfRun run)
        {
            // the picture: a cut to the other colours twice a second (the capture's own copy, not under test: before the frame)
            if (frameNo++ % 45 == 0) gpu.Ctx.CopyResource(pic.Tex, (frameNo / 45) % 2 == 1 ? picA.Tex : picB.Tex);
            GpuIdle(gpu);
            run?.Before();
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            scr.Frame = (long)(clock * 60);                            // 60 new capture frames a second
            long tt = Stopwatch.GetTimestamp();
            th.Update(vr, Dt, tcfg.Dim, live);
            long d = Stopwatch.GetTimestamp() - tt, b1 = GC.GetAllocatedBytesForCurrentThread();
            thTicks += d; thMax = Math.Max(thMax, d);
            long tb = PerfTrace.Now();
            bar.Update(vr, now, Dt, th, choices, scr, false, false);  // the laser on the big screen: the bar stays up
            PerfTrace.Add(PerfTrace.Bar, tb);
            long b2 = GC.GetAllocatedBytesForCurrentThread();
            scr.Update(vr, now, Dt);
            gpu.Ctx.Flush();
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            long b3 = GC.GetAllocatedBytesForCurrentThread();
            if (run != null)
            {
                run.After(ms, b3 - b0);
                run.TheaterBytes += b1 - b0; run.BarBytes += b2 - b1; run.ScreenBytes += b3 - b2;
            }
            clock += Dt;
            now += Dt;
        }

        // before theater: the monitor at its place for a moment (its first frame shown, faded in) - and with VR connected and theater off,
        // Theater makes the style's textures on these idle frames, a piece a frame (TheaterWarm.cs): their cost is here, not in theater's
        // first frame. Measured like the rest (the pre-frames' worst is the biggest piece).
        int gcPre = GC.CollectionCount(0);
        var pre = new PerfRun();
        var pieces = new System.Text.StringBuilder();
        for (int i = 0; i < 45; i++)
        {
            double was = pre.SumMs;
            Frame(pre);
            if (pre.SumMs - was >= 0.5) pieces.Append($" frame {i + 1}: {pre.SumMs - was:0.0};");      // (a piece made: the city, the band ...)
            if (!Theme.Cyber) Thread.Sleep(1);     // (a backdrop is drawn on a thread, ~35 ms: in the app these 45 frames are 0.5 s and it lands in them)
        }
        int gcPreN = GC.CollectionCount(0) - gcPre;
        PerfTrace.Reset();
        thTicks = thMax = 0;
        int gc0 = GC.CollectionCount(0);
        th.Start(scr, scr, vr, tcfg);
        var intro = new PerfRun();
        for (int i = 0; i < 270; i++) Frame(intro);
        if (warm)
        {
            // the JIT and the fonts warmed: a short stretch at rest and out again, nothing reported
            for (int i = 0; i < (int)Math.Round(seconds * 90); i++) Frame(null);
            th.Stop(vr, tcfg);
            for (int i = 0; i < 90; i++) Frame(null);
            pic.Dispose(); picA.Dispose(); picB.Dispose();
            return;
        }
        Log.Info($"theaterperf: {name} before theater (0.5 s, the textures made ready a piece a frame), 45 frames: a frame {pre.SumMs / 45:0.000} ms " +
                 $"(worst {pre.MaxMs:0.0} at frame {pre.WorstAt + 1}), {pre.SumBytes / 45:0} B (worst {pre.MaxBytes}); GCs {gcPreN}; the frames over 0.5 ms:{pieces}");
        rows.Add($"{name} before theater (0.5 s)\tframe ms\t{pre.SumMs / 45:0.0000}\t{pre.MaxMs:0.0000}");
        rows.Add($"{name} before theater (0.5 s)\tbytes a frame\t{pre.SumBytes / 45:0}\t{pre.MaxBytes}");
        Report(name, "coming in (3 s)", intro, thTicks, thMax, rows);
        Log.Info($"theaterperf: {name} frame 1 (what is still drawn then: the Ambilight's shape; before TheaterWarm.cs the city / the backdrop started, the band and its lamps): " +
                 $"{intro.FirstMs:0.0} ms, {intro.FirstBytes} B; the band without its lamps + its read-back {th.LifeBuildMs:0.0} ms of frames, the lamps listed {th.LampsEndMs:0.0} ms, " +
                 $"the backdrop's thread {th.BackdropMs} ms; GCs {GC.CollectionCount(0) - gc0}");
        PerfTrace.Reset();
        thTicks = thMax = 0;
        gc0 = GC.CollectionCount(0);
        var rest = new PerfRun();
        int n = (int)Math.Round(seconds * 90);
        int hand0 = scr.Handovers, sends0 = th.LifeSends, samples0 = th.AmbilightForTest?.Sampled ?? 0, composed0 = th.AmbilightForTest?.Composed ?? 0;
        for (int i = 0; i < n; i++) Frame(rest);
        Report(name, $"at rest ({seconds:0} s)", rest, thTicks, thMax, rows);
        long screenBytes = (long)(scr.Handovers - hand0) * pic.W * pic.H * 4;
        Log.Info($"theaterperf: {name} at rest: the screen's capture handed over {(scr.Handovers - hand0) / seconds:0.0} times a second ({screenBytes / seconds / 1e6:0.0} MB/s), " +
                 $"the rest {(rest.TexBytes - screenBytes) / seconds / 1e6:0.00} MB/s: the band sent {(th.LifeSends - sends0) / seconds:0.00} a second, the Ambilight sampled " +
                 $"{((th.AmbilightForTest?.Sampled ?? 0) - samples0) / seconds:0.0} and its glow written {((th.AmbilightForTest?.Composed ?? 0) - composed0) / seconds:0.0} a second; GCs {GC.CollectionCount(0) - gc0}");
        rows.Add($"{name} at rest\ttexture MB/s without the screen\t{(rest.TexBytes - screenBytes) / seconds / 1e6:0.000}\t");
        rows.Add($"{name} at rest\tband sends/s\t{(th.LifeSends - sends0) / seconds:0.00}\t");
        rows.Add($"{name} at rest\tambilight samples/s\t{((th.AmbilightForTest?.Sampled ?? 0) - samples0) / seconds:0.0}\t");
        rows.Add($"{name} at rest\tambilight glow writes/s\t{((th.AmbilightForTest?.Composed ?? 0) - composed0) / seconds:0.0}\t");
        // going out (1 s), for the record
        th.Stop(vr, tcfg);
        PerfTrace.Reset();
        thTicks = thMax = 0;
        var outro = new PerfRun();
        for (int i = 0; i < 90; i++) Frame(outro);
        Report(name, "going out (1 s)", outro, thTicks, thMax, rows);
        pic.Dispose(); picA.Dispose(); picB.Dispose();
    }

    // the lines and the rows of a stretch
    static void Report(string name, string phase, PerfRun r, long thTicks, long thMax, List<string> rows)
    {
        double f = Math.Max(1, r.Frames), sec = r.Frames / 90.0;
        string sc = $"{name} {phase}";
        void Row(string what, double avg, double worst) => rows.Add($"{sc}\t{what}\t{avg:0.0000}\t{worst:0.0000}");
        var C = VrOverlay.Calls.Names;
        Log.Info($"theaterperf: {sc}, {r.Frames} frames: a frame {r.SumMs / f:0.000} ms (worst {r.MaxMs:0.000} at frame {r.WorstAt + 1}), {r.SumBytes / f:0} B (worst {r.MaxBytes}; " +
                 $"Theater.Update {r.TheaterBytes / f:0}, the bar {r.BarBytes / f:0}, the screen {r.ScreenBytes / f:0}); " +
                 $"SteamVR calls {r.SentTotal / f:0.00} a frame (worst {r.MaxCalls}), {r.SparedTotal / f:0.00} spared; textures handed over {r.TexBytes / sec / 1e6:0.0} MB/s " +
                 $"({r.Sent[VrOverlay.Calls.Texture] / sec:0.0} SetTexture/s, {r.TexSame / sec:0.0} of them the same handle)");
        var kinds = new System.Text.StringBuilder();
        for (int k = 0; k < C.Length; k++)
            if (r.Sent[k] > 0 || r.Spared[k] > 0) kinds.Append($" {C[k]} {r.Sent[k] / sec:0.0}/s (spared {r.Spared[k] / sec:0.0}/s);");
        Log.Info($"theaterperf: {sc}, calls a second by kind:{kinds}");
        var secs = new System.Text.StringBuilder();
        double theaterMs = PerfTrace.Ms(thTicks) / f, parts = 0;
        for (int i = 0; i < 4; i++) parts += PerfTrace.Ms(PerfTrace.Ticks[i]) / f;
        for (int i = 0; i < PerfTrace.Count; i++)
        {
            double avg = i == PerfTrace.TheaterRest ? theaterMs - parts : PerfTrace.Ms(PerfTrace.Ticks[i]) / f;
            double worst = i == PerfTrace.TheaterRest ? PerfTrace.Ms(thMax) : PerfTrace.Ms(PerfTrace.Max[i]);
            secs.Append($" {PerfTrace.Names[i]} {avg:0.0000} (worst {worst:0.000});");
            Row(PerfTrace.Names[i] + " ms", avg, worst);
        }
        Log.Info($"theaterperf: {sc}, a frame by section (ms; the rest's worst is Theater.Update's whole):{secs}");
        Row("frame ms", r.SumMs / f, r.MaxMs);
        Row("Theater.Update ms", theaterMs, PerfTrace.Ms(thMax));
        Row("bytes a frame", r.SumBytes / f, r.MaxBytes);
        Row("bytes a frame: Theater.Update", r.TheaterBytes / f, 0);
        Row("bytes a frame: the bar", r.BarBytes / f, 0);
        Row("bytes a frame: the screen", r.ScreenBytes / f, 0);
        Row("SteamVR calls a frame", r.SentTotal / f, r.MaxCalls);
        Row("SteamVR calls spared a frame", r.SparedTotal / f, 0);
        Row("texture MB/s", r.TexBytes / sec / 1e6, 0);
        Row("SetTexture/s", r.Sent[VrOverlay.Calls.Texture] / sec, 0);
        for (int k = 0; k < C.Length; k++) if (r.Sent[k] > 0) Row(C[k] + "/s", r.Sent[k] / sec, 0);
    }

    // ---- the one-off costs: each texture the first time (cold: with the JIT), then again (warm); CPU = the calls, GPU = until it is done ----
    static void TheaterPerfOnce(ConfigStore cfg, Painter painter, Gpu gpu, List<string> rows)
    {
        void Once(string what, Action draw)
        {
            GpuIdle(gpu);
            long t0 = Stopwatch.GetTimestamp();
            draw();
            double cpu = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            gpu.Ctx.Flush();
            GpuIdle(gpu);
            double all = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            long t1 = Stopwatch.GetTimestamp();
            draw();
            double warm = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
            gpu.Ctx.Flush();
            GpuIdle(gpu);
            Log.Info($"theaterperf once: {what}: {cpu:0.0} ms CPU, {all:0.0} ms until the GPU is done; drawn again {warm:0.0} ms CPU");
            rows.Add($"once\t{what} (ms CPU cold / warm)\t{cpu:0.00}\t{warm:0.00}");
            rows.Add($"once\t{what} (ms until the GPU is done, cold)\t{all:0.00}\t");
        }
        using (var th = new Theater(gpu, painter) { LifeSeed = 1018 })
        {
            Once($"the city wall 1024 x {Theater.CityH} (CityRows: the sky, the dim, the stars)", () => th.GridForTest(0));
            Once($"the floor grid's three {Theater.FloorTexW} x {Theater.FloorTexH} (worked out, uploaded)", () => th.FloorDrawForTest());
            Once("the skyline band 4096 x 512 (DrawSkyline, its lamps recorded)", () => th.SkylineForTest());
            Log.Info($"theaterperf once: of the band's draw, the lamps' list {th.LampsEndMs:0.0} ms ({th.LampCount} lamps)");
            Once("the band without its lamps (city life's base)", () => th.SkylineDrawMsForTest(true));
            Once("the horizon's ring 4096 x 64 and a spark 512 x 128 (pixels + upload)", () => th.EnsureHorizonTex());
            Once("the cars' textures", () => th.CarTexturesForTest());
        }
        Once("the ring's pixels alone (HorizonPulse.RingPixels)", () => HorizonPulse.RingPixels());
        Once("a spark's pixels alone (HorizonPulse.SparkPixels)", () => HorizonPulse.SparkPixels());
        Once("the Modern backdrop (the hall, 1024 x 2048; on a thread in VR)", () => Theater.PaintBackdrop(Theater.Backdrop.Hall));
        using (var amb = new Ambilight(gpu, painter))
        {
            float aspect = 16 / 9f;
            Once("the Ambilight's shape (16:9: the glow's weights)", () => { amb.Shape(aspect); aspect += 0.05f; });
            var (pic, _) = AmbPicture(gpu, 2560, 1440, (x, y) => Rgb(x * 255 / 2560, 80, y * 255 / 1440));
            Once("the Ambilight's sampling of a 2560 x 1440 picture", () => amb.Sample(pic));
            pic.Dispose();
        }
        using (var bar = new TheaterBar(gpu, painter, cfg))
        {
            var a = new TheaterTest.FakeScreen { Name = "AOC 24G4X" };
            var b = new TheaterTest.FakeScreen { Name = "DELL U2723QE" };
            var choices = new List<ITheaterScreen> { a, b, new TheaterTest.FakeScreen { Name = "Xiaomi Monitor" } };
            var view = new TheaterView { Size = 2.8f };
            var media = new TheaterBar.BarMedia { Has = true, Title = "Midnight City", Artist = "M83", Playing = true, Timeline = true, Pos = 134, Dur = 243, CanSeek = true };
            bar.ReadMedia = () => media;
            bar.VisForTest = 1;
            bar.MediaTickForTest(1000);
            Once("the Cyber bar with the media console (DrawFor)", () => bar.DrawFor(choices, b, view));
        }
        using (var fx = new CyberScreenFx(gpu, "perf"))
        {
            var tex = new Vector2(2560, 1440);
            Once("the screen's Cyber frame 1024 x 604 (the lock-on, DrawForTest)", () => fx.DrawForTest(painter, tex, 1, false, -1, 0.1, -1));
        }
    }

    // ---- the panels at rest, in VR terms: Update with a fake overlay, paced at 90 Hz (their timers run on the clock) ----
    static void PanelsAtRestVr(ConfigStore cfg, Painter painter, Gpu gpu, double seconds, List<string> rows)
    {
        var vr = new VrSession();
        vr.Device[0] = Matrix4x4.CreateTranslation(0, 1.6f, 0);
        vr.DeviceValid[0] = true;
        // the hands: the left one up in front of the face (the watch is looked at), the right one down
        vr.Device[1] = Matrix4x4.CreateRotationX(-1.2f) * Matrix4x4.CreateTranslation(-0.15f, 1.35f, -0.35f);
        vr.Device[2] = Matrix4x4.CreateTranslation(0.25f, 1.0f, -0.3f);
        vr.DeviceValid[1] = vr.DeviceValid[2] = true;
        vr.LeftIndex = 1;
        vr.RightIndex = 2;
        var data = new PanelData { VrConnected = true, VramTotalGB = 16, FakeGpuLoad = 63, FakeVram = 9.4f, VrSince = DateTime.Now.AddMinutes(-83) };
        data.Vr.AppFps = 86; data.Vr.ReprojPct = 4; data.Vr.GpuMs = 9.3f; data.Vr.DisplayHz = 90;
        data.Vr.Batteries.Add(("HMD", 0.71f, false));
        data.Vr.Batteries.Add(("L", 0.82f, false));
        data.Vr.Batteries.Add(("R", 0.18f, true));
        data.FakeMedia = (true, "Midnight City", "M83", true);
        for (int i = 0; i < VrStats.FrameHistory; i++) data.Vr.FrameMs[i] = 7.5f + (i % 7) * 0.3f;
        data.Vr.FrameHead = 0; data.Vr.FrameCount = VrStats.FrameHistory;
        data.Room = RoomViewTools.Room(12, 2);
        data.Notes = NotesViewTools.Fake();
        data.Photos = PhotoFake.Snap();
        using var faces = new HexpadFaces();
        var dockSnap = DockViewTools.Fake();
        faces.Update(painter, dockSnap);
        data.Dock = dockSnap;
        data.DockFaces = faces;
        var roomSnap = RoomViewTools.Room(12, 2);
        var inFront = MathUtil.ToArray(Matrix4x4.CreateTranslation(0, 1.3f, -0.7f));
        bool always0 = cfg.Data.Wrist.AlwaysVisible;
        bool kbVis0 = cfg.Data.Keyboard.Visible, dockVis0 = cfg.Data.Dock.Visible, roomVis0 = cfg.Data.Room.Visible;
        float[] kbM0 = cfg.Data.Keyboard.Place.M, dockM0 = cfg.Data.Dock.Place.M, roomM0 = cfg.Data.Room.Place.M, spM0 = cfg.Data.SettingsPanel.Place.M;
        try
        {
            cfg.Data.Wrist.AlwaysVisible = true;
            cfg.Data.Keyboard.Visible = cfg.Data.Dock.Visible = cfg.Data.Room.Visible = true;
            cfg.Data.Keyboard.Place.Attach = cfg.Data.Dock.Place.Attach = cfg.Data.Room.Place.Attach = cfg.Data.SettingsPanel.Place.Attach = "world";
            cfg.Data.Keyboard.Place.M = cfg.Data.Dock.Place.M = cfg.Data.Room.Place.M = cfg.Data.SettingsPanel.Place.M = inFront;
            foreach (string style in new[] { null, CyberHud.Id })
            {
                string name = style == null ? "Modern" : "Cyber";
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                using (var wrist = new WristPanel(gpu, painter, cfg) { CyberLive = true })
                {
                    wrist.Mic.Fake = true;
                    wrist.Mic.Present = true;
                    wrist.CreateOverlay(vr);
                    PanelRun($"{name} watch ({cfg.Data.Wrist.Face})", gpu, seconds, (now, dt) => wrist.Update(vr, now, dt, data), rows, settle: 9);
                }
                using (var kb = new KeyboardPanel(gpu, painter, cfg))
                {
                    kb.CreateOverlay(vr);
                    PanelRun($"{name} keyboard", gpu, seconds, (now, dt) => kb.Update(vr, now), rows);
                }
                var host = new FakeHost { Cfg = cfg, GpuName = "Made-up GPU", MonitorList = new List<MonitorInfo>(), Data = data };
                using (var sp = new SettingsPanel(gpu, painter, cfg, host, null))
                {
                    sp.CreateOverlay(vr);
                    sp.Open();
                    PanelRun($"{name} settings VR panel", gpu, seconds, (now, dt) => sp.Update(vr, now, dt), rows);
                }
                using (var dock = new DockPanel(gpu, painter, cfg, null, faces))
                {
                    dock.CreateOverlay(vr);
                    PanelRun($"{name} dock panel", gpu, seconds, (now, dt) => dock.Update(vr, now, dt, dockSnap), rows);
                }
                using (var room = new RoomPanel(gpu, painter, cfg))
                {
                    room.CreateOverlay(vr);
                    PanelRun($"{name} room panel", gpu, seconds, (now, dt) => room.Update(vr, now, dt, roomSnap), rows);
                }
            }
        }
        finally
        {
            cfg.Data.Wrist.AlwaysVisible = always0;
            cfg.Data.Keyboard.Visible = kbVis0; cfg.Data.Dock.Visible = dockVis0; cfg.Data.Room.Visible = roomVis0;
            cfg.Data.Keyboard.Place.M = kbM0; cfg.Data.Dock.Place.M = dockM0; cfg.Data.Room.Place.M = roomM0; cfg.Data.SettingsPanel.Place.M = spM0;
        }
    }

    // a panel's Update at 90 Hz on the real clock: 'settle' seconds first (its opening animation; the watch: Cyber's loops run 8 s after
    // a page is shown), then 'seconds' measured - the frames' CPU ms, bytes, the calls, and how often it drew (SetTexture)
    static void PanelRun(string name, Gpu gpu, double seconds, Action<double, float> update, List<string> rows, double settle = 2)
    {
        var run = new PerfRun();
        double last = Clock.Now, next = last, end = last + settle + seconds, measureFrom = last + settle;
        while (Clock.Now < end)
        {
            next += 1 / 90.0;
            while (Clock.Now < next - 0.002) Thread.Sleep(1);
            while (Clock.Now < next) Thread.SpinWait(20);
            GpuIdle(gpu);
            double now = Clock.Now;
            float dt = (float)Math.Clamp(now - last, 0, 0.1);
            last = now;
            bool measure = now >= measureFrom;
            if (measure) run.Before();
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            update(now, dt);
            gpu.Ctx.Flush();
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (measure) run.After(ms, GC.GetAllocatedBytesForCurrentThread() - b0);
        }
        double f = Math.Max(1, run.Frames), sec = Math.Max(0.001, run.Frames / 90.0);
        var C = VrOverlay.Calls.Names;
        var kinds = new System.Text.StringBuilder();
        for (int k = 0; k < C.Length; k++)
            if (run.Sent[k] > 0) kinds.Append($" {C[k]} {run.Sent[k] / sec:0.0}/s;");
        Log.Info($"theaterperf panel: {name} at rest, {run.Frames} frames: a frame {run.SumMs / f:0.000} ms (worst {run.MaxMs:0.000}), {run.SumBytes / f:0} B (worst {run.MaxBytes}); " +
                 $"drawn and handed over {run.Sent[VrOverlay.Calls.Texture] / sec:0.0} times a second ({run.TexBytes / sec / 1e6:0.0} MB/s); SteamVR calls {run.SentTotal / f:0.00} a frame " +
                 $"(worst {run.MaxCalls}), {run.SparedTotal / f:0.00} spared:{kinds}");
        rows.Add($"panel {name}\tframe ms\t{run.SumMs / f:0.0000}\t{run.MaxMs:0.0000}");
        rows.Add($"panel {name}\tbytes a frame\t{run.SumBytes / f:0}\t{run.MaxBytes}");
        rows.Add($"panel {name}\tredraws/s (SetTexture)\t{run.Sent[VrOverlay.Calls.Texture] / sec:0.00}\t");
        rows.Add($"panel {name}\ttexture MB/s\t{run.TexBytes / sec / 1e6:0.00}\t");
        rows.Add($"panel {name}\tSteamVR calls a frame\t{run.SentTotal / f:0.00}\t{run.MaxCalls}");
        rows.Add($"panel {name}\tSteamVR calls spared a frame\t{run.SparedTotal / f:0.00}\t");
    }
}
