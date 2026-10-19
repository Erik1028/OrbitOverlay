using System;
using System.Runtime.InteropServices;
using Valve.VR;

namespace Orbit;

// --pacetest <seconds>: what the overlay loop is paced by, measured live and read-only (no overlay is created). Per
// second: how often WaitFrameSync returns (what OrbitOverlay's loop runs at), headset vsyncs (the display), the
// compositor's presents and the game's frame submits. If WaitFrameSync follows the game rather than the display, the
// overlays' content (mirrors, cursor, a grabbed panel, animations) updates only as often as the game renders.
static class PaceTest
{
    static readonly Compositor_FrameTiming[] _timings = new Compositor_FrameTiming[128];

    // --pacertest: OrbitOverlay's own loop clock without VR - 90 Hz for 3 s with a little work each tick, like the real
    // loop: how many ticks, how even (interval mean / spread / worst), and whether the high-resolution timer is there
    public static int Pacer()
    {
        Native.timeBeginPeriod(1);
        try
        {
            using var p = new FramePacer();
            var gaps = new System.Collections.Generic.List<double>();
            double t0 = Clock.Now, last = t0;
            p.Wait(90);
            last = Clock.Now;
            while (Clock.Now - t0 < 3)
            {
                double w = Clock.Now;
                while (Clock.Now - w < 0.0008) { }        // ~0.8 ms of "work"
                p.Wait(90);
                double n = Clock.Now;
                gaps.Add((n - last) * 1000);
                last = n;
            }
            gaps.Sort();
            double mean = 0; foreach (var g in gaps) mean += g; mean /= gaps.Count;
            double sd = 0; foreach (var g in gaps) sd += (g - mean) * (g - mean); sd = Math.Sqrt(sd / gaps.Count);
            Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"pacertest: high-resolution timer {(p.HighResolution ? "yes" : "NO")}, {gaps.Count / 3.0:0.0} ticks/s, interval mean {mean:0.00} ms (target 11.11), " +
                $"sd {sd:0.00}, min {gaps[0]:0.00}, p99 {gaps[(int)(gaps.Count * 0.99)]:0.00}, max {gaps[^1]:0.00}"));
            bool ok = p.HighResolution && Math.Abs(gaps.Count / 3.0 - 90) < 2 && Math.Abs(mean - 11.11) < 0.3;
            Log.Info("pacertest: " + (ok ? "ALL OK" : "FAILED"));
            return ok ? 0 : 1;
        }
        finally { Native.timeEndPeriod(1); }
    }

    public static int Run(double seconds)
    {
        var err = EVRInitError.None;
        OpenVR.Init(ref err, EVRApplicationType.VRApplication_Overlay);
        if (err != EVRInitError.None) { Log.Error("pacetest: " + err); return 2; }
        try
        {
            var sys = OpenVR.System;
            var ov = OpenVR.Overlay;
            var comp = OpenVR.Compositor;
            var perr = ETrackedPropertyError.TrackedProp_Success;
            float hz = sys.GetFloatTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_DisplayFrequency_Float, ref perr);
            Log.Info($"pacetest: display {hz:0} Hz, {seconds:0} s");
            uint size = (uint)Marshal.SizeOf<Compositor_CumulativeStats>();
            var st = new Compositor_CumulativeStats();
            comp?.GetCumulativeStats(ref st, size);
            uint presents0 = st.m_nNumFramePresents, submits0 = st.m_nNumFrameSubmits, repro0 = st.m_nNumReprojectedFrames, dropped0 = st.m_nNumDroppedFrames;
            float since = 0;
            ulong vs0 = 0, vs;
            sys.GetTimeSinceLastVsync(ref since, ref vs0);
            double t0 = Clock.Now, win = t0, waitSum = 0, waitMax = 0;
            int waits = 0, timeouts = 0;
            var phase = new int[8];          // where in the display frame WaitFrameSync returns (eighths of a frame)
            while (Clock.Now - t0 < seconds)
            {
                double a = Clock.Now;
                var e = ov.WaitFrameSync(30);
                double w = (Clock.Now - a) * 1000;
                if (e == EVROverlayError.None) waits++; else timeouts++;
                waitSum += w;
                waitMax = Math.Max(waitMax, w);
                vs = 0;
                sys.GetTimeSinceLastVsync(ref since, ref vs);
                phase[Math.Clamp((int)(since * hz * 8), 0, 7)]++;
                if (Clock.Now - win >= 1)
                {
                    double span = Clock.Now - win;
                    comp?.GetCumulativeStats(ref st, size);
                    // GPU per frame over the last second: the game's own render, and the compositor's (it draws every overlay)
                    double gpuApp = 0, gpuComp = 0, cpuComp = 0, cpuGame = 0;
                    int nt = 0, nc = 0;
                    if (comp != null)
                    {
                        for (int i = 0; i < _timings.Length; i++) _timings[i].m_nSize = (uint)Marshal.SizeOf<Compositor_FrameTiming>();
                        uint got = comp.GetFrameTimings(_timings);
                        double newest = got > 0 ? _timings[got - 1].m_flSystemTimeInSeconds : 0;
                        for (int i = 0; i < got; i++)
                        {
                            if (newest - _timings[i].m_flSystemTimeInSeconds > span) continue;
                            gpuApp += _timings[i].m_flTotalRenderGpuMs; gpuComp += _timings[i].m_flCompositorRenderGpuMs; cpuComp += _timings[i].m_flCompositorRenderCpuMs;
                            nt++;
                            float gc = VrStats.GameCpuMs(_timings[i]);
                            if (gc >= 0) { cpuGame += gc; nc++; }
                        }
                    }
                    if (nt > 0)
                        Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"pacetest: GPU per frame: game {gpuApp / nt:0.00} ms, compositor {gpuComp / nt:0.00} ms (budget {1000 / hz:0.0} ms), compositor CPU {cpuComp / nt:0.00} ms, game CPU {(nc > 0 ? cpuGame / nc : -1):0.00} ms, {nt} frames"));
                    Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"pacetest: WaitFrameSync {waits / span:0}/s (avg {waitSum / Math.Max(1, waits + timeouts):0.0} ms, max {waitMax:0.0}, timeouts {timeouts}) | " +
                        $"vsync {(vs - vs0) / span:0}/s | compositor presents {(st.m_nNumFramePresents - presents0) / span:0}/s, " +
                        $"game submits {(st.m_nNumFrameSubmits - submits0) / span:0}/s, reprojected {(st.m_nNumReprojectedFrames - repro0) / span:0}/s, " +
                        $"dropped {(st.m_nNumDroppedFrames - dropped0) / span:0}/s | returns at frame phase (1/8): {string.Join(" ", phase)}"));
                    presents0 = st.m_nNumFramePresents; submits0 = st.m_nNumFrameSubmits; repro0 = st.m_nNumReprojectedFrames; dropped0 = st.m_nNumDroppedFrames;
                    vs0 = vs;
                    waits = timeouts = 0;
                    waitSum = waitMax = 0;
                    Array.Clear(phase);
                    win = Clock.Now;
                }
            }
            return 0;
        }
        finally { OpenVR.Shutdown(); }
    }
}
