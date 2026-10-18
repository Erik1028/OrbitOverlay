using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Orbit;

// GPU 3D load and dedicated VRAM use from the WDDM performance counters (what Task Manager shows).
// Sampled once a second on a background thread; the wildcard query can take a few ms.
public sealed class GpuStats : IDisposable
{
    public volatile float Load3D = -1;          // percent
    public volatile float VramUsedGB = -1;
    public volatile bool Active = true;         // off: no samples (the wildcard query costs a few ms) - OrbitApp: while the watch shows
    readonly Thread _t;
    volatile bool _stop;

    public GpuStats()
    {
        _t = new Thread(Run) { IsBackground = true, Name = "gpu-stats", Priority = ThreadPriority.BelowNormal };
        _t.Start();
    }

    void Run()
    {
        if (Native.PdhOpenQueryW(null, IntPtr.Zero, out var q) != 0) return;
        try
        {
            Native.PdhAddEnglishCounterW(q, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out var engine);
            Native.PdhAddEnglishCounterW(q, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out var mem);
            Native.PdhCollectQueryData(q);
            while (!_stop)
            {
                Thread.Sleep(1000);
                if (!Active) continue;
                if (Native.PdhCollectQueryData(q) != 0) continue;
                double load = Sum(engine, out _);
                Sum(mem, out double maxMem);
                Load3D = (float)Math.Min(100, load);
                VramUsedGB = (float)(maxMem / (1024.0 * 1024 * 1024));
            }
        }
        catch (Exception e) { Log.Warn("gpu stats: " + e.Message); }
        finally { Native.PdhCloseQuery(q); }
    }

    static double Sum(IntPtr counter, out double max)
    {
        max = 0;
        uint size = 0;
        uint rc = Native.PdhGetFormattedCounterArrayW(counter, Native.PDH_FMT_DOUBLE | Native.PDH_FMT_NOCAP100, ref size, out uint count, IntPtr.Zero);
        if (rc != Native.PDH_MORE_DATA || size == 0) return 0;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (Native.PdhGetFormattedCounterArrayW(counter, Native.PDH_FMT_DOUBLE | Native.PDH_FMT_NOCAP100, ref size, out count, buf) != 0)
                return 0;
            double sum = 0;
            // PDH_FMT_COUNTERVALUE_ITEM_W: name ptr (8) + { CStatus (4) + pad (4) + double (8) }
            for (int i = 0; i < count; i++)
            {
                IntPtr item = buf + i * 24;
                uint status = (uint)Marshal.ReadInt32(item, 8);
                if (status != 0) continue;
                double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                sum += v;
                if (v > max) max = v;
            }
            return sum;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Dispose() { _stop = true; }
}
