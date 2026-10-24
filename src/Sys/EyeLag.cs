using System;
using System.Numerics;

namespace Orbit;

// How far the eye readings lag behind the head, measured in normal use (Erik 10-24: "az eye tracking kurzor pontosságán még lehetne
// javítani"). The look goes on the head of the reading's time (EyeGaze.Latency, a guess of 40 ms); a guess 20 ms off still leaves
// ~0.6 deg at 30 deg/s of head turn. While the head turns and the eyes hold a point (no saccade), the point looked at stands still in
// the room - but only on the head of the right moment: on any other the look sweeps with the head. So each new reading is put on the
// head of 16 moments (0-120 ms back) and how much the point 0.8 m out moves in the room is added up for each; the moment it moves
// least is the lag. A log line once there is enough of it (300 readings in head turns, then every 5 minutes); nothing uses the value
// yet. ~16 head look-ups a reading, nothing allocated.
sealed class EyeLag
{
    const int N = 16;
    const double Step = 0.008;                      // seconds between two candidates (0 .. 120 ms)
    const float MinTurn = 10, MaxTurn = 90;         // degrees a second of head turn that count (slower: nothing to see; faster: blur)
    const float SaccadeDeg = 1.5f;                  // the look in the head moving more than this between two readings: a saccade
    readonly Vector3[] _s = new Vector3[N];         // each candidate's point in the room, lightly smoothed
    readonly double[] _cost = new double[N];
    double _prevAt = -1, _nextLog;
    Vector3 _prevDir;
    int _used;

    // a new reading: its look and eyes relative to the head, when it was first seen (EyeGaze.SampleAt); now: Clock.Now
    public void Add(VrSession vr, Vector3 dirInHead, Vector3 originInHead, double at, double now)
    {
        double dt = at - _prevAt;
        bool cont = _prevAt >= 0 && dt > 0 && dt < 0.05;
        bool use = false;
        if (cont)
        {
            var f0 = MathUtil.Forward(vr.HeadAt(at - EyeGaze.Latency - dt));
            var f1 = MathUtil.Forward(vr.HeadAt(at - EyeGaze.Latency));
            float turn = EyeSpot.Angle(f0, f1) * 180 / MathF.PI / (float)dt;
            use = turn >= MinTurn && turn <= MaxTurn && EyeSpot.Angle(_prevDir, dirInHead) * 180 / MathF.PI < SaccadeDeg;
        }
        var probe = originInHead + dirInHead * 0.8f;
        for (int k = 0; k < N; k++)
        {
            var p = Vector3.Transform(probe, vr.HeadAt(at - k * Step));
            if (!cont) { _s[k] = p; continue; }
            var step = (p - _s[k]) * 0.3f;
            _s[k] += step;
            if (use) _cost[k] += step.LengthSquared() / (dt * dt);
        }
        if (use) _used++;
        _prevAt = at;
        _prevDir = dirInHead;
        if (now < _nextLog || _used < 300) return;
        _nextLog = now + 300;
        int best = 0;
        for (int k = 1; k < N; k++) if (_cost[k] < _cost[best]) best = k;
        double off = 0;
        if (best > 0 && best < N - 1)
        {
            double a = _cost[best - 1], b = _cost[best], c = _cost[best + 1], den = a - 2 * b + c;
            if (den > 1e-12) off = Math.Clamp(0.5 * (a - c) / den, -0.5, 0.5);
        }
        double min = Math.Max(1e-12, _cost[best]);
        Log.Info($"eyes: the readings' lag behind the head ~{(best + off) * Step * 1000:0} ms ({_used} readings in head turns; the look's " +
                 $"movement in the room on the head of 0 / {best * Step * 1000:0} / {(N - 1) * Step * 1000:0} ms back: {_cost[0] / min:0.00} / 1 / " +
                 $"{_cost[N - 1] / min:0.00}; {EyeGaze.Latency * 1000:0} ms in use; Virtual Desktop {vr.EyeRate} readings a second, the cursor's " +
                 $"noise {Laser.SpotNoiseDeg:0.00} deg)");
        for (int k = 0; k < N; k++) _cost[k] *= 0.5;
        _used /= 2;
    }
}
