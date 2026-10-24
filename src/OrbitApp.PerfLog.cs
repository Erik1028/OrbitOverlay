using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Orbit;

// The perf log's lines are made on a thread of their own (Erik 10-24, the spike line: the frame that wrote them every 10 s took 7-13 ms
// on the main loop - the formatting, the process counters, the alloc line's sorting, the first window's JIT). The main loop only copies
// the window's numbers into a PerfWindow and starts the next window (PerfFrame, OrbitApp.cs); this thread writes the very same lines in
// the same order, in the main loop's culture (Lang: the Magyar UI writes 7,5). Nothing here reads the main loop's own state - it all
// comes in the window. Two windows are reused; a third is made only if this thread fell 20 s behind.
public sealed partial class OrbitApp
{
    // one window's numbers: everything its lines need (the main loop fills it, this thread writes it out, then it is free again)
    sealed class PerfWindow
    {
        public CultureInfo Culture;
        public double Secs, WaitSum;
        public int Frames;
        public readonly double[] Sum = new double[PerfNames.Length], Max = new double[PerfNames.Length];
        public readonly long[] AllocSum = new long[PerfNames.Length];
        // mem: and gc:
        public long ThreadBytes, AllBytes;
        public int Gc0, Gc1, Gc2;
        public TimeSpan Pause;
        public GCMemoryInfo Gc;
        // alloc: the window's sums (AllocWatch.Cut swaps them out, empty ones in; left empty again once the line is made)
        public ConcurrentDictionary<string, long> AllocTypes = new();
        public ConcurrentDictionary<int, long> AllocThreads = new();
        // raycast: and perf:
        public bool HasRays;
        public RayStats.Window Rays;
        public int GpuLate, GpuProbes;
        public bool Rates;                       // the compositor's frames known (CompositorRates)
        public uint GameFrames, CompFrames;
        public readonly Spikes.Slowest Spike = new();
        // show: (ShowTake; written only with the laser trace on)
        public bool Show, Trace;
        public int Loops, DashLoops, PrioLoops, Shown, NewTex, LateN, Aim0, Aim1;
        public long NewCaps;
        public uint Repro, Drop;
        public readonly float[] Late = new float[LateMax];
    }

    readonly ConcurrentQueue<PerfWindow> _perfFree = new(new[] { new PerfWindow(), new PerfWindow() }), _perfFull = new();
    readonly AutoResetEvent _perfWake = new(false);
    readonly StringBuilder _perfSa = new(), _perfSb = new();       // (the perf log's thread only)

    void PerfLogStart() =>
        new Thread(PerfLogRun) { IsBackground = true, Name = "OrbitOverlay perf log", Priority = ThreadPriority.BelowNormal }.Start();

    void PerfLogRun()
    {
        // what the main loop first runs at a window's end compiled here beforehand, so that frame meets no JIT (this thread's own
        // formatting is compiled on its first window - here, off the loop)
        try
        {
            const BindingFlags Own = BindingFlags.Instance | BindingFlags.NonPublic;
            Prepare(typeof(OrbitApp).GetMethod(nameof(PerfFrame), Own));
            Prepare(typeof(OrbitApp).GetMethod(nameof(CompositorRates), Own));
            Prepare(typeof(OrbitApp).GetMethod(nameof(ShowTake), Own));
            Prepare(typeof(Spikes).GetMethod(nameof(Spikes.Take)));
            Prepare(typeof(RayStats).GetMethod(nameof(RayStats.Take)));
            Prepare(typeof(AllocWatch).GetMethod(nameof(AllocWatch.Cut)));
            GC.GetGCMemoryInfo(GCKind.Any);
        }
        catch (Exception e) { Log.Warn("perf log warm-up: " + e.Message); }
        while (true)
        {
            _perfWake.WaitOne();
            while (_perfFull.TryDequeue(out var w))
            {
                try { PerfWrite(w); }
                catch (Exception e) { Log.Warn("perf log: " + e.Message); }
                finally
                {
                    w.AllocTypes.Clear(); w.AllocThreads.Clear();       // (empty before Cut swaps them in again, even if the line failed)
                    _perfFree.Enqueue(w);
                }
            }
        }
    }

    static void Prepare(MethodInfo m) { if (m != null) RuntimeHelpers.PrepareMethod(m.MethodHandle); }

    // a window's lines, the very text the main loop wrote up to 10-24 (tools read them): built first, then logged back to back
    void PerfWrite(PerfWindow w)
    {
        if (!ReferenceEquals(CultureInfo.CurrentCulture, w.Culture)) CultureInfo.CurrentCulture = w.Culture;
        double secs = w.Secs;
        _perfSa.Clear();
        for (int i = 0; i < PerfNames.Length; i++) _perfSa.Append($" {PerfNames[i]} {w.AllocSum[i] / w.Frames}");
        string mem = $"mem: main thread {w.ThreadBytes / secs / 1024:0.0} KB/s, " +
                     $"all threads {w.AllBytes / secs / 1024:0.0} KB/s, " +
                     $"GC {w.Gc0}/{w.Gc1}/{w.Gc2} " +
                     $"({w.Pause.TotalMilliseconds:0.0} ms pause), heap {w.Gc.HeapSizeBytes / 1048576.0:0.0} MB, " +
                     $"{ProcessCounters()}, gc committed {w.Gc.TotalCommittedBytes / 1048576.0:0} MB | bytes/loop:{_perfSa}";      // (Environment.WorkingSet: no - it may read every process too)
        string al = _allocWatch.TakeLine(w.AllocTypes, w.AllocThreads, secs);
        var gi = w.Gc;
        string gc = null;
        if (gi.Index > 0 && gi.Index != _lastGcLogged)
            gc = $"gc: last #{gi.Index} gen{gi.Generation}{(gi.Concurrent ? " background" : "")}{(gi.Compacted ? " compacting" : "")}, " +
                 $"pause {gi.PauseDurations[0].TotalMilliseconds:0.0} ms, promoted {gi.PromotedBytes / 1024} KB, " +
                 $"load {gi.MemoryLoadBytes * 100 / Math.Max(1, gi.TotalAvailableMemoryBytes)} %, finalization pending {gi.FinalizationPendingCount}";
        _lastGcLogged = gi.Index;
        string rays = w.HasRays ? RayStats.Line(w.Rays) : null;
        _perfSb.Clear();
        double total = 0;
        for (int i = 0; i < PerfNames.Length; i++)
        {
            total += w.Sum[i];
            _perfSb.Append($" {PerfNames[i]} {w.Sum[i] / w.Frames:0.00}/{w.Max[i]:0.0}");
        }
        string gpuq = w.GpuProbes > 0 ? $", gpu late {100.0 * w.GpuLate / w.GpuProbes:0.0}%" : "";
        string rates = w.Rates ? $" (game {w.GameFrames / secs:0}, compositor {w.CompFrames / secs:0} fps)" : "";
        string perf = $"perf: {w.Frames / secs:0} loops/s{rates}, work {total / w.Frames:0.00} ms, wait {w.WaitSum / w.Frames:0.0} ms{gpuq} | avg/max ms:{_perfSb}";
        string spike = Spikes.Line(w.Spike);
        string show = w.Show && w.Trace ? ShowText(w) : null;
        Log.Info(mem);
        if (al != null) Log.Info(al);
        if (gc != null) Log.Info(gc);
        if (rays != null) Log.Info(rays);
        Log.Info(perf);
        if (spike != null) Log.Info(spike);
        if (show != null) Log.Info(show);
    }

    // the show: line from ShowTake's numbers (OrbitApp.cs: what it tells)
    static string ShowText(PerfWindow w)
    {
        double secs = w.Secs;
        float[] late = w.Late;
        int n = w.LateN, loops = w.Loops;
        Array.Sort(late, 0, n);
        float p99 = n == 0 ? 0 : late[Math.Min(n - 1, (int)(n * 0.99))], max = n == 0 ? 0 : late[n - 1];
        return $"show: dashboard open {100.0 * w.DashLoops / loops:0}%, compositor reprojected {w.Repro / secs:0}/s dropped {w.Drop / secs:0}/s, " +
               $"loop woke late p99 {p99:0.0} max {max:0.0} ms, monitors shown {w.Shown} with {w.NewCaps / secs:0} new pictures/s, " +
               $"textures to SteamVR {w.NewTex / secs:0}/s, GPU priority high {100.0 * w.PrioLoops / loops:0}%, " +
               $"tip pose missing L {w.Aim0 / secs:0.0}/s R {w.Aim1 / secs:0.0}/s";
    }
}
