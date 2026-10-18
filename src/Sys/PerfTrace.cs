using System;
using System.Diagnostics;

namespace Orbit;

// Where a frame's time goes, by section - the perf tools' (ORBIT_SNAP_ONLY=theaterperf, ToolsTheaterPerf.cs). Off by default: with
// On false a mark is one flag test, nothing is timed (the app pays nothing). Theater.Update marks its parts (the wall, the city's life,
// the Ambilight, the horizon); the tool marks what it calls round them and reads the sums.
public static class PerfTrace
{
    public static bool On;

    public const int Wall = 0, CityLife = 1, Ambilight = 2, Horizon = 3, TheaterRest = 4, Bar = 5, ScreenPlace = 6, ScreenFx = 7, Count = 8;
    public static readonly string[] Names =
    {
        "theater: wall + city / backdrop", "theater: city life", "theater: ambilight", "theater: horizon", "theater: the rest (fade, places)",
        "the bar (Update)", "the screen (place, capture hand-over)", "the screen's Cyber fx",
    };
    public static readonly long[] Ticks = new long[Count], Max = new long[Count];
    public static readonly int[] N = new int[Count];

    // the moment a section starts (0 while off)
    public static long Now() => On ? Stopwatch.GetTimestamp() : 0;

    // the section that started at 'from' ends now
    public static void Add(int section, long from)
    {
        if (!On) return;
        long d = Stopwatch.GetTimestamp() - from;
        Ticks[section] += d;
        if (d > Max[section]) Max[section] = d;
        N[section]++;
    }

    public static void Reset()
    {
        Array.Clear(Ticks);
        Array.Clear(Max);
        Array.Clear(N);
    }

    public static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
