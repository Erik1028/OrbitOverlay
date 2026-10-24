using System;
using System.Collections.Generic;

namespace Orbit;

// How long a SlimeVR tracker's battery lasts (Erik 10-24, the SlimeVR widget: "~2 h left" per tracker): SlimeVR v22 sends no estimate
// for trackers on Wi-Fi (its battery_runtime_estimate comes only from the HID dongle's packets; v21 passes on what a tracker reported),
// so the link keeps the last 20 minutes of each board's percentage - a sample every 30 s, 40 of them - and fits a line through them.
// Said only when it means something: 10 minutes of samples or more, 4 or more of them, 3 % gone or more, never charging in the window
// (charging, or the number going up, starts it over), a steady fall (1 to 60 % an hour, the samples within 2 % of the line). The
// percentage is a whole number and sags under load: a short or a noisy record says nothing. Pure: the time comes in (the tools feed
// made-up hours of it).
public sealed class SlimeBattery
{
    const int Ring = 40;
    const double Every = 30, Window = 20 * 60, MinSpan = 10 * 60;

    sealed class Board
    {
        public readonly double[] T = new double[Ring];
        public readonly float[] P = new float[Ring];
        public int N, Head;                       // samples kept, where the next goes
        public int Last = -1;                     // the last percentage seen
        public int Left = -1;                     // minutes (rounded; -1: nothing to say)
    }

    readonly Dictionary<int, Board> _boards = new();

    // a board's percentage now (the link's 1 Hz updates; only every 30 s is kept)
    public void Add(double now, int device, int pct, bool charging)
    {
        if (pct < 0) return;
        if (!_boards.TryGetValue(device, out var b)) _boards[device] = b = new Board();
        // charging, or up by 2 % or more (put on the charger and off again between two looks): a new record
        if (charging || (b.Last >= 0 && pct >= b.Last + 2)) { b.N = 0; b.Head = 0; b.Left = -1; }
        b.Last = pct;
        if (charging) return;
        int newest = (b.Head - 1 + Ring) % Ring;
        if (b.N > 0 && now - b.T[newest] < Every) return;
        b.T[b.Head] = now;
        b.P[b.Head] = pct;
        b.Head = (b.Head + 1) % Ring;
        b.N = Math.Min(Ring, b.N + 1);
        b.Left = Fit(b, now);
    }

    // minutes left (rounded), -1: nothing to say
    public int Left(int device) => _boards.TryGetValue(device, out var b) ? b.Left : -1;

    public void Clear() => _boards.Clear();

    static int Fit(Board b, double now)
    {
        // the samples in the window, oldest first
        int n = 0;
        double st = 0, sp = 0, stt = 0, stp = 0, t0 = double.NaN, first = 0, last = 0;
        for (int k = 0; k < b.N; k++)
        {
            int i = (b.Head - b.N + k + Ring) % Ring;
            if (now - b.T[i] > Window) continue;
            if (double.IsNaN(t0)) { t0 = b.T[i]; first = b.P[i]; }
            double t = (b.T[i] - t0) / 3600, p = b.P[i];
            st += t; sp += p; stt += t * t; stp += t * p;
            last = p;
            n++;
        }
        if (n < 4) return -1;
        double span = (b.T[(b.Head - 1 + Ring) % Ring] - t0);
        if (span < MinSpan || first - last < 3) return -1;
        double den = n * stt - st * st;
        if (den <= 1e-12) return -1;
        double slope = (n * stp - st * sp) / den, icpt = (sp - slope * st) / n;      // % an hour, %
        if (slope > -1 || slope < -60) return -1;
        double ss = 0;
        for (int k = 0; k < b.N; k++)
        {
            int i = (b.Head - b.N + k + Ring) % Ring;
            if (now - b.T[i] > Window) continue;
            double e = b.P[i] - (icpt + slope * (b.T[i] - t0) / 3600);
            ss += e * e;
        }
        if (Math.Sqrt(ss / n) > 2) return -1;
        return Round(last / -slope * 60);
    }

    // minutes, as the watch says them: 2 h and more in whole hours, 1-2 h in half hours, less in tens of minutes, under 10 "~5 min"
    public static int Round(double minutes)
    {
        if (double.IsNaN(minutes) || minutes <= 0) return -1;
        if (minutes >= 120) return (int)Math.Round(minutes / 60, MidpointRounding.AwayFromZero) * 60;
        if (minutes >= 60) return (int)Math.Round(minutes / 30, MidpointRounding.AwayFromZero) * 30;
        if (minutes >= 10) return Math.Max(10, (int)Math.Round(minutes / 10, MidpointRounding.AwayFromZero) * 10);
        return 5;
    }
}
