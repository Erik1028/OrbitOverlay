using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Valve.VR;

namespace Orbit;

// dev, READ ONLY (--statsbench): what the perf log's 10-second calls cost while SteamVR runs - the compositor's cumulative stats (an IPC
// to vrcompositor), the frame timing, GC.GetGCMemoryInfo - 200 of each, spread over 10 s, p50 / p99 / max. A background
// application: no overlay, nothing shown or changed (Erik 10-24: the perf log's frame took 10-13 ms every 10 s).
static class StatsBench
{
    public static int Run()
    {
        var err = EVRInitError.None;
        OpenVR.Init(ref err, EVRApplicationType.VRApplication_Background);
        if (err != EVRInitError.None) { Log.Error("statsbench: " + err); return 2; }
        try
        {
            var comp = OpenVR.Compositor;
            var stats = new Compositor_CumulativeStats();
            var timing = new Compositor_FrameTiming { m_nSize = (uint)Marshal.SizeOf<Compositor_FrameTiming>() };
            double[] a = new double[200], b = new double[200], c = new double[200];
            for (int i = 0; i < 200; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                comp.GetCumulativeStats(ref stats, (uint)Marshal.SizeOf<Compositor_CumulativeStats>());
                long t1 = Stopwatch.GetTimestamp();
                comp.GetFrameTiming(ref timing, 0);
                long t2 = Stopwatch.GetTimestamp();
                _ = GC.GetGCMemoryInfo(GCKind.Any);
                long t3 = Stopwatch.GetTimestamp();
                a[i] = (t1 - t0) * 1000.0 / Stopwatch.Frequency;
                b[i] = (t2 - t1) * 1000.0 / Stopwatch.Frequency;
                c[i] = (t3 - t2) * 1000.0 / Stopwatch.Frequency;
                Thread.Sleep(47 + i % 7);           // spread over ~10 s, not in step with the compositor's frames
            }
            static string S(double[] v)
            {
                var s = v.OrderBy(x => x).ToArray();
                return $"p50 {s[s.Length / 2]:0.000} p99 {s[(int)(s.Length * 0.99)]:0.000} max {s[^1]:0.000} ms";
            }
            Log.Info($"statsbench: GetCumulativeStats {S(a)}");
            Log.Info($"statsbench: GetFrameTiming {S(b)}");
            Log.Info($"statsbench: GC.GetGCMemoryInfo {S(c)}");
            return 0;
        }
        finally { OpenVR.Shutdown(); }
    }
}
