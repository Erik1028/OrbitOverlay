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
        Keyboard = 9, Settings = 10, Desktops = 11, Rest = 12, App = 13, Mixer = 14, Window = 15, PerfLog = 16, Boot = 17, Count = 18;
    public const double SpikeMs = 3;
    static readonly string[] Names =
    {
        "vr", "cursor", "laser rays", "laser pointer (mouse / panel)", "laser beam / grab", "extras + block", "watch", "watch draw",
        "watch send", "keyboard", "settings + panels", "desktops", "dashboard + osc", "app (photos, friends, temps, avatar...)",
        "audio mixer", "window + config save", "gpu flush + perf log", "connect steps (overlays, warm-ups)",
    };
    static bool _on;                                 // between Begin and End (the loop runs without VR too: nothing is kept then)
    static readonly long[] _frame = new long[Count], _worst = new long[Count];
    static long _mark, _worstTotal;
    static int _worstFrames;
    // the slowest piece inside a part (a widget of the watch, the watch's flush): the frame keeps its slowest, the line names it
    static long _noteTicks, _worstNoteTicks;
    static string _note, _worstNote;
    static readonly StringBuilder _sb = new();       // (Line's: the perf log's thread only)

    // a window's slowest frame, copied for the perf log's thread (Take on the main loop, Line there)
    public sealed class Slowest
    {
        public readonly long[] Parts = new long[Count];
        public long Total, NoteTicks;
        public int Frames;
        public string Note;
    }

    // The first frames after SteamVR connected, every one of them (Erik 10-24, the startup hitch: the spike line names one frame of a
    // window and its slowest piece - spreading the connect's work over frames needs them all). BootBegin() at the connect; End() keeps each
    // frame's parts, Piece() each piece of 0.5 ms or more, in arrays made once; after BootFrames frames one "boot:" line lists the frames
    // of SpikeMs or more. Nothing is allocated until that line is written, once a connect.
    public const int BootFrames = 60;
    const int BootMax = 256;                         // pieces kept (the frame numbers fit a byte)
    const double BootBudgetMs = 15;                  // a frame's share of the connect's work (OrbitApp.BootTick)
    static readonly long[] _bootParts = new long[BootFrames * Count], _bootTotal = new long[BootFrames], _bootTicks = new long[BootMax];
    static readonly string[] _bootWhat = new string[BootMax];
    static readonly byte[] _bootFrame = new byte[BootMax];
    static int _bootLeft, _bootAt, _bootN;

    // SteamVR connected: the next BootFrames frames are kept
    public static void BootBegin()
    {
        Array.Clear(_bootParts);
        Array.Clear(_bootTotal);
        Array.Clear(_bootWhat);
        _bootAt = _bootN = 0;
        _bootLeft = BootFrames;
    }

    // a frame starts
    public static void Begin()
    {
        Array.Clear(_frame);
        _noteTicks = 0;
        _note = null;
        _on = true;
        _mark = Stopwatch.GetTimestamp();
    }

    // a piece that started at 'from' (Stopwatch.GetTimestamp) ends now: the frame's slowest is kept. what: a constant (no garbage)
    public static void Piece(string what, long from)
    {
        long d = Stopwatch.GetTimestamp() - from;
        if (_bootLeft > 0 && _on && _bootN < BootMax && d * 2000 >= Stopwatch.Frequency)
        {
            _bootWhat[_bootN] = what;
            _bootTicks[_bootN] = d;
            _bootFrame[_bootN++] = (byte)_bootAt;
        }
        if (d <= _noteTicks) return;
        _noteTicks = d;
        _note = what;
    }

    // the part that ran since the last mark ends now
    public static void Mark(int part)
    {
        if (!_on) return;
        long t = Stopwatch.GetTimestamp();
        _frame[part] += t - _mark;
        _mark = t;
    }

    // the frame ends: kept if it is the slowest of the window
    public static void End()
    {
        if (!_on) return;
        _on = false;
        long total = 0;
        for (int i = 0; i < Count; i++) total += _frame[i];
        if (_bootLeft > 0)
        {
            _bootTotal[_bootAt] = total;
            Array.Copy(_frame, 0, _bootParts, _bootAt * Count, Count);
            _bootAt++;
            if (--_bootLeft == 0) Log.Info(BootLine());
        }
        if (total >= _worstTotal * 0.95 && total >= Stopwatch.Frequency * SpikeMs / 1000) _worstFrames++;     // (how many came near it)
        if (total <= _worstTotal) return;
        _worstTotal = total;
        Array.Copy(_frame, _worst, Count);
        _worstNote = _note;
        _worstNoteTicks = _noteTicks;
    }

    // the window's slowest frame copied into 'into', and a new window (the main loop: nothing formatted here)
    public static void Take(Slowest into)
    {
        into.Total = _worstTotal;
        into.Frames = _worstFrames;
        into.Note = _worstNote;
        into.NoteTicks = _worstNoteTicks;
        Array.Copy(_worst, into.Parts, Count);
        _worstTotal = 0;
        _worstFrames = 0;
        _worstNote = null;
        _worstNoteTicks = 0;
        Array.Clear(_worst);
    }

    // a taken window's line: its parts of 0.2 ms or more, slowest first - null when it took less than SpikeMs (the perf log's thread)
    public static string Line(Slowest w)
    {
        double total = Ms(w.Total);
        if (total < SpikeMs) return null;
        _sb.Clear();
        _sb.Append($"spike: slowest frame {total:0.0} ms ({w.Frames} over {SpikeMs:0} ms) =");
        Span<bool> used = stackalloc bool[Count];
        for (int n = 0; n < Count; n++)
        {
            int best = -1;
            for (int i = 0; i < Count; i++)
                if (!used[i] && (best < 0 || w.Parts[i] > w.Parts[best])) best = i;
            if (best < 0 || Ms(w.Parts[best]) < 0.2) break;
            used[best] = true;
            _sb.Append($" {Names[best]} {Ms(w.Parts[best]):0.0}");
        }
        if (w.Note != null && Ms(w.NoteTicks) >= 0.3) _sb.Append($" | slowest piece: {w.Note} {Ms(w.NoteTicks):0.0}");
        return _sb.ToString();
    }

    // "boot: the first 60 frames after connecting - 9 over 3 ms, 1 over 15 ms, the slowest 41.0 ms | #0 41.0 ms = vr 12.1 desktops 9.0 ...
    // [vr: eyes 2.1, ...] | #2 ..." - each frame of SpikeMs or more: its parts of 0.5 ms or more, slowest first, then its pieces as they
    // came. (A StringBuilder of its own: _sb is the spike line's.)
    static string BootLine()
    {
        int over = 0, overBudget = 0;
        long slowest = 0;
        for (int f = 0; f < BootFrames; f++)
        {
            if (Ms(_bootTotal[f]) >= SpikeMs) over++;
            if (Ms(_bootTotal[f]) >= BootBudgetMs) overBudget++;
            if (_bootTotal[f] > slowest) slowest = _bootTotal[f];
        }
        var sb = new StringBuilder(4096);
        sb.Append($"boot: the first {BootFrames} frames after connecting - {over} over {SpikeMs:0} ms, {overBudget} over {BootBudgetMs:0} ms, " +
                  $"the slowest {Ms(slowest):0.0} ms");
        Span<bool> used = stackalloc bool[Count];
        for (int f = 0; f < BootFrames; f++)
        {
            if (Ms(_bootTotal[f]) < SpikeMs) continue;
            sb.Append($" | #{f} {Ms(_bootTotal[f]):0.0} ms =");
            int at = f * Count;
            used.Clear();
            for (int n = 0; n < Count; n++)
            {
                int best = -1;
                for (int i = 0; i < Count; i++)
                    if (!used[i] && (best < 0 || _bootParts[at + i] > _bootParts[at + best])) best = i;
                if (best < 0 || Ms(_bootParts[at + best]) < 0.5) break;
                used[best] = true;
                sb.Append($" {Names[best]} {Ms(_bootParts[at + best]):0.0}");
            }
            bool first = true;
            for (int k = 0; k < _bootN; k++)
            {
                if (_bootFrame[k] != f) continue;
                sb.Append(first ? " [" : ", ").Append($"{_bootWhat[k]} {Ms(_bootTicks[k]):0.0}");
                first = false;
            }
            if (!first) sb.Append(']');
        }
        return sb.ToString();
    }

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
