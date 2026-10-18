using System;
using System.Diagnostics;
using System.Text;

namespace Orbit;

// The slowest frame of each perf window, by part (Erik 10-18: the watch took 6-11 ms as it came into view, the lasers up to 13 ms -
// where did that time go?). The main loop marks where each part ends - a mark is one Stopwatch read, ~25 ns - and the frame's parts
// are kept while it is the slowest so far; the perf line gets a "spike:" neighbour saying where that frame's time went, only when it
// took SpikeMs or more (a calm window says nothing).
public static class Spikes
{
    public const int Vr = 0, Cursor = 1, Rays = 2, Pointer = 3, LaserRest = 4, Extras = 5, Watch = 6, WatchDraw = 7, WatchSend = 8,
        Keyboard = 9, Settings = 10, Desktops = 11, Rest = 12, Count = 13;
    public const double SpikeMs = 3;
    static readonly string[] Names =
    {
        "vr", "cursor", "laser rays", "laser pointer (mouse / panel)", "laser beam / grab", "extras + block", "watch", "watch draw",
        "watch send", "keyboard", "settings + panels", "desktops", "dashboard + osc",
    };
    static readonly long[] _frame = new long[Count], _worst = new long[Count];
    static long _mark, _worstTotal;
    static int _worstFrames;
    static readonly StringBuilder _sb = new();

    // a frame starts
    public static void Begin()
    {
        Array.Clear(_frame);
        _mark = Stopwatch.GetTimestamp();
    }

    // the part that ran since the last mark ends now
    public static void Mark(int part)
    {
        long t = Stopwatch.GetTimestamp();
        _frame[part] += t - _mark;
        _mark = t;
    }

    // the frame ends: kept if it is the slowest of the window
    public static void End()
    {
        long total = 0;
        for (int i = 0; i < Count; i++) total += _frame[i];
        if (total >= _worstTotal * 0.95 && total >= Stopwatch.Frequency * SpikeMs / 1000) _worstFrames++;     // (how many came near it)
        if (total <= _worstTotal) return;
        _worstTotal = total;
        Array.Copy(_frame, _worst, Count);
    }

    // the window's slowest frame, its parts of 0.2 ms or more, slowest first - null when it took less than SpikeMs; a new window
    public static string Take()
    {
        double total = Ms(_worstTotal);
        string line = null;
        if (total >= SpikeMs)
        {
            _sb.Clear();
            _sb.Append($"spike: slowest frame {total:0.0} ms ({_worstFrames} over {SpikeMs:0} ms) =");
            Span<bool> used = stackalloc bool[Count];
            for (int n = 0; n < Count; n++)
            {
                int best = -1;
                for (int i = 0; i < Count; i++)
                    if (!used[i] && (best < 0 || _worst[i] > _worst[best])) best = i;
                if (best < 0 || Ms(_worst[best]) < 0.2) break;
                used[best] = true;
                _sb.Append($" {Names[best]} {Ms(_worst[best]):0.0}");
            }
            line = _sb.ToString();
        }
        _worstTotal = 0;
        _worstFrames = 0;
        Array.Clear(_worst);
        return line;
    }

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
