using System;
using System.Numerics;

namespace Orbit;

// The eye cursor's calibration (Erik 10-24: "az eye tracking kurzor pontosságán még lehetne javítani" - the cursor is only worth having
// if it sits on what he looks at, and the Quest Pro's tracker puts the look a constant ~1 deg beside it, more near the top and the
// bottom; nothing corrected that). Settings > VRChat > Calibrate...: a board 1 m ahead (Panels/EyeCalibPanel.cs), a dot on its middle,
// then 12 deg up, down, left and right - each looked at until it moves on by itself (the look resting on it 1.2 s) or the trigger says
// so. What the tracker said against where the dot was, seen from the eyes of that moment (the head may move: each reading's dot is
// worked out on the head of its own time), makes the correction: kept in the config (GeneralConfig.EyeCalib, null: none) and put on every
// reading in EyeGaze before anything else looks at it.
//
// The correction: the look's yaw and pitch in the head (degrees; the yaw first, then the pitch) moved by a bias - and, only when the five
// points ask for it clearly, a 2x2 linear term (a gain per axis, a little crosstalk): yaw' = Xx yaw + Xy pitch + Yaw, pitch' = Yx yaw +
// Yy pitch + Pitch. Five points hold six numbers loosely: the linear term is kept only within sane limits (a gain 0.8-1.25, crosstalk
// under 0.12, the points spread at least 4 deg RMS each way) and when it leaves them at least 0.1 deg nearer than the bias alone - the
// noise never makes one. Applying it is a few sines a new reading (72 a second); nothing allocated.
public static class EyeCalib
{
    const float Deg = MathF.PI / 180;
    public const float MaxBias = 6;                 // degrees: a bias past this was not a look at the dots
    public const float MaxAfter = 1.0f;             // degrees RMS the points may stay off after the fit (else: try again)
    public const float GainMin = 0.8f, GainMax = 1.25f, CrossMax = 0.12f;
    public const float LinearWin = 0.1f;            // degrees RMS the linear term must win over the bias alone
    public const float MinSpread = 4;               // degrees RMS the points' looks spread each way (the head turned with the eyes: bias only)

    // the reasons a calibration is not kept (the board says them in the language picked)
    public const string WhyFar = "The look was far from the dots - look right at each one.";
    public const string WhyDisagree = "The points did not agree - keep your head still and try again.";
    public const string WhyNoLook = "No steady look at the dot - try again.";

    // one point's readings: where the tracker said the eyes looked (M, the mean) and how far the dot was from that (R, the median), the
    // readings' scatter round R - degrees; N readings; Auto: taken by itself (a rest), not by the trigger
    public struct Point
    {
        public float Mx, My, Rx, Ry, Spread;
        public int N;
        public bool Auto;
    }

    // the look's yaw and pitch in the head (radians; -Z ahead, right and up positive) and back
    public static float Yaw(Vector3 d) => MathF.Atan2(d.X, -d.Z);
    public static float Pitch(Vector3 d) => MathF.Atan2(d.Y, MathF.Sqrt(d.X * d.X + d.Z * d.Z));
    public static Vector3 Dir(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new Vector3(cp * MathF.Sin(yaw), MathF.Sin(pitch), -cp * MathF.Cos(yaw));
    }

    // a reading corrected (relative to the head, unit length); c null: as it is
    public static Vector3 Apply(EyeCalibConfig c, Vector3 d)
    {
        if (c == null) return d;
        float y = Yaw(d), p = Pitch(d);
        float y2 = c.Xx * y + c.Xy * p + c.Yaw * Deg, p2 = c.Yx * y + c.Yy * p + c.Pitch * Deg;
        return Dir(y2, Math.Clamp(p2, -1.5f, 1.5f));
    }

    static float Sq(float x) => x * x;

    // the correction from the points; null (and why) when they do not make one
    public static EyeCalibConfig Fit(ReadOnlySpan<Point> pts, out string why)
    {
        why = null;
        int n = pts.Length;
        if (n < 3) { why = WhyNoLook; return null; }
        // the bias alone: the mean offset. Before: how far off the points were with nothing; after: with the bias
        float bx = 0, by = 0;
        foreach (var q in pts) { bx += q.Rx; by += q.Ry; }
        bx /= n; by /= n;
        float before = 0, after = 0;
        foreach (var q in pts) { before += q.Rx * q.Rx + q.Ry * q.Ry; after += Sq(q.Rx - bx) + Sq(q.Ry - by); }
        var c = new EyeCalibConfig { Yaw = bx, Pitch = by, Before = MathF.Sqrt(before / n), After = MathF.Sqrt(after / n), Points = n };
        if (Linear(pts, out var l) && c.After - l.After >= LinearWin) { l.Before = c.Before; l.Points = n; c = l; }
        if (MathF.Sqrt(c.Yaw * c.Yaw + c.Pitch * c.Pitch) > MaxBias) { why = WhyFar; return null; }
        if (c.After > MaxAfter) { why = WhyDisagree; return null; }
        return c;
    }

    // the least-squares affine fit (E = A M + B, E = M + R) - false when the points do not spread enough or the fit is not a sane one
    static bool Linear(ReadOnlySpan<Point> pts, out EyeCalibConfig c)
    {
        c = null;
        int n = pts.Length;
        float mx = 0, my = 0, ex = 0, ey = 0;
        foreach (var q in pts) { mx += q.Mx; my += q.My; ex += q.Mx + q.Rx; ey += q.My + q.Ry; }
        mx /= n; my /= n; ex /= n; ey /= n;
        float sxx = 0, sxy = 0, syy = 0, sxe = 0, sye = 0, sxf = 0, syf = 0;
        foreach (var q in pts)
        {
            float dx = q.Mx - mx, dy = q.My - my, de = q.Mx + q.Rx - ex, df = q.My + q.Ry - ey;
            sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
            sxe += dx * de; sye += dy * de; sxf += dx * df; syf += dy * df;
        }
        float det = sxx * syy - sxy * sxy;
        if (sxx / n < MinSpread * MinSpread || syy / n < MinSpread * MinSpread || det < 0.5f * sxx * syy) return false;
        float a = (syy * sxe - sxy * sye) / det, b = (sxx * sye - sxy * sxe) / det;
        float cc = (syy * sxf - sxy * syf) / det, d = (sxx * syf - sxy * sxf) / det;
        if (a < GainMin || a > GainMax || d < GainMin || d > GainMax || MathF.Abs(b) > CrossMax || MathF.Abs(cc) > CrossMax) return false;
        float ox = ex - a * mx - b * my, oy = ey - cc * mx - d * my, res = 0;
        foreach (var q in pts) res += Sq(q.Mx + q.Rx - (a * q.Mx + b * q.My + ox)) + Sq(q.My + q.Ry - (cc * q.Mx + d * q.My + oy));
        c = new EyeCalibConfig { Yaw = ox, Pitch = oy, Xx = a, Xy = b, Yx = cc, Yy = d, After = MathF.Sqrt(res / n) };
        return true;
    }
}

// One calibration: the five points one after the other, each one's readings, then the fit. The board (Panels/EyeCalibPanel.cs) shows it
// and feeds it; the checks (GazeFocusTest.cs, --eyechecks) drive it with made-up readings. A point:
//  - Move: the dot glides there (MoveTime); no reading counts (the eyes are on their way);
//  - Rest: the readings kept in a ring; once they have rested on one spot for RestTime (their scatter round their mean within RestRms,
//    that mean within NearDeg of the dot; a stray reading past RestMax - a blink's edge, the tracker's own hiccup, 1 in 20 at most - left
//    out) - or the trigger is pressed (PressDelay after it: the press's own start) - Take;
//  - Take: the next TakeTime of readings is the point's (the dot shrinks meanwhile): RestTime + TakeTime = the 1.2 s of a steady look.
//    The offset is their median, the scatter and the look's place are of those within RestMax of it (the checks' 1 % of readings thrown
//    5 deg put a point's plain RMS at 1.17 deg - beside TakeSpread); fewer than 3 in 4 of them there, scattered past TakeSpread (the look
//    wandered off) or too few (a blink) - back to Rest.
// Nothing taken within PointTimeout: given up (Failed). The ring and every number are made once; a reading allocates nothing.
public sealed class EyeCalibRun
{
    const float Deg = MathF.PI / 180;
    public const float Distance = 1.0f;             // metres: the board ahead of the eyes
    public const float Spread = 12;                 // degrees: the four points round the middle
    public const double MoveTime = 0.45, RestTime = 0.7, TakeTime = 0.5, PressDelay = 0.1, PointTimeout = 20;
    public const float RestRms = 1.0f, RestMax = 3.0f, NearDeg = 6, TakeSpread = 1.2f;      // degrees
    public const int MinTake = 12;                  // readings a point needs (72 a second: 36 in TakeTime)
    public static readonly (float yaw, float pitch)[] Points = { (0, 0), (0, Spread), (0, -Spread), (-Spread, 0), (Spread, 0) };
    static readonly string[] Names = { "the middle", "up", "down", "left", "right" };
    public static int Count => Points.Length;

    public enum Phase { Idle, Move, Rest, Take, Done, Failed, Cancelled }

    public Matrix4x4 Board { get; private set; } = Matrix4x4.Identity;     // the board: its middle Distance ahead, facing the eyes, upright
    public Phase State { get; private set; }
    public int Index { get; private set; }          // the point shown (the ones before it are taken)
    public readonly EyeCalib.Point[] Taken = new EyeCalib.Point[Points.Length];
    public EyeCalibConfig Result { get; private set; }
    public string Why { get; private set; }         // Failed: why (English; the board says it in the language picked)
    public int Version { get; private set; }        // up on every change the board shows
    public double LastReading { get; private set; } = -10;      // the last reading's time (the board: "not tracked" after a while)
    public bool Running => State is Phase.Move or Phase.Rest or Phase.Take;
    public bool Ended => State is Phase.Done or Phase.Failed or Phase.Cancelled;

    double _from, _pointFrom, _restFrom, _takeFrom;
    bool _auto;
    // the current point's readings (a ring): their time, the tracker's look and the dot's offset from it (degrees)
    const int Cap = 256;
    readonly double[] _t = new double[Cap];
    readonly float[] _mx = new float[Cap], _my = new float[Cap], _rx = new float[Cap], _ry = new float[Cap], _sort = new float[Cap];
    int _n, _i;

    // a point's place on the board (metres from its middle, x right, y up): seen from where the eyes were at the start, at its angles
    public static Vector2 Local(int i) =>
        new(Distance * MathF.Tan(Points[i].yaw * Deg), Distance * MathF.Tan(Points[i].pitch * Deg));

    public Vector3 TargetWorld(int i) { var l = Local(i); return Vector3.Transform(new Vector3(l.X, l.Y, 0), Board); }

    // a new calibration, the board placed in front of this head: its pitch followed, its roll not (GazeFrame with no level band - a head
    // bowed 10 deg would have had the middle point 10 deg up in it), lying on a side its full rotation (MathUtil.PlacementFrame)
    public void Start(in Matrix4x4 head, double now)
    {
        var frame = MathUtil.IsLying(head, true) ? MathUtil.PlacementFrame(head, true, "eye calibration") : MathUtil.GazeFrame(head, 0);
        Board = Matrix4x4.CreateTranslation(0, 0, -Distance) * frame;
        Index = 0;
        Result = null;
        Why = null;
        _from = _pointFrom = LastReading = now;
        _n = _i = 0;
        State = Phase.Move;
        Version++;
        Log.Info("eye calibration: started - 5 points 12 deg apart on a board 1 m ahead");
    }

    public void Cancel()
    {
        if (!Running) return;
        State = Phase.Cancelled;
        Version++;
        Log.Info($"eye calibration: cancelled at point {Index + 1} of {Count}");
    }

    // a frame: the time-driven steps (the glide's end, the take's end, giving up)
    public void Tick(double now)
    {
        if (State == Phase.Move && now - _from >= MoveTime) { State = Phase.Rest; _restFrom = now; _n = 0; Version++; }
        if (!Running) return;
        if (State == Phase.Take && now - _takeFrom >= TakeTime) TryTake(now);
        if (Running && now - _pointFrom > PointTimeout)
        {
            State = Phase.Failed;
            Why = EyeCalib.WhyNoLook;
            Version++;
            Log.Info($"eye calibration: nothing taken at point {Index + 1} ({Names[Index]}) in {PointTimeout:0} s - given up");
        }
    }

    // the trigger: this point now (the readings from PressDelay after the press, or after the glide)
    public void Mark(double now)
    {
        if (State is not (Phase.Move or Phase.Rest)) return;
        _takeFrom = Math.Max(now, State == Phase.Move ? _from + MoveTime : now) + PressDelay;
        _auto = false;
        State = Phase.Take;
        Version++;
    }

    // a new reading: the look as the tracker gave it (relative to the head, no calibration), the eyes' place in the head, the head of the
    // reading's time (world)
    public void Feed(Vector3 tracker, Vector3 origin, in Matrix4x4 head, double now)
    {
        LastReading = now;
        if (State is not (Phase.Rest or Phase.Take)) return;
        var eye = Vector3.Transform(origin, head);
        var to = Vector3.TransformNormal(TargetWorld(Index) - eye, MathUtil.Invert(head));
        if (to.LengthSquared() < 1e-8f) return;
        to = Vector3.Normalize(to);
        float mx = EyeCalib.Yaw(tracker) / Deg, my = EyeCalib.Pitch(tracker) / Deg;
        _t[_i] = now; _mx[_i] = mx; _my[_i] = my;
        _rx[_i] = EyeCalib.Yaw(to) / Deg - mx; _ry[_i] = EyeCalib.Pitch(to) / Deg - my;
        _i = (_i + 1) % Cap;
        if (_n < Cap) _n++;
        if (State == Phase.Rest && now - _restFrom >= RestTime && Rested(now))
        {
            _takeFrom = now;
            _auto = true;
            State = Phase.Take;
            Version++;
        }
    }

    // the readings of the last RestTime on one spot, near the dot
    bool Rested(double now)
    {
        double from = now - RestTime;
        int k = 0;
        float sx = 0, sy = 0;
        for (int j = 0, at = (_i - 1 + Cap) % Cap; j < _n && _t[at] >= from; j++, at = (at - 1 + Cap) % Cap) { sx += _rx[at]; sy += _ry[at]; k++; }
        if (k < RestTime * 30) return false;           // (30 a second at least: a stream that stutters does not count)
        sx /= k; sy /= k;
        if (sx * sx + sy * sy > NearDeg * NearDeg) return false;
        float ss = 0;
        int far = 0;
        for (int j = 0, at = (_i - 1 + Cap) % Cap; j < k; j++, at = (at - 1 + Cap) % Cap)
        {
            float d = (_rx[at] - sx) * (_rx[at] - sx) + (_ry[at] - sy) * (_ry[at] - sy);
            if (d > RestMax * RestMax) far++;
            else ss += d;
        }
        return far * 20 <= k && ss / (k - far) <= RestRms * RestRms;
    }

    // the take's time is up: the readings since _takeFrom make the point - or back to Rest
    void TryTake(double now)
    {
        int k = 0;
        for (int j = 0, at = (_i - 1 + Cap) % Cap; j < _n && _t[at] >= _takeFrom; j++, at = (at - 1 + Cap) % Cap) k++;
        if (k < MinTake)
        {
            if (now - _takeFrom > 4 * TakeTime) { State = Phase.Rest; _restFrom = now; Version++; }      // (a long blink: look again)
            return;
        }
        var pt = new EyeCalib.Point { Auto = _auto };
        pt.Rx = Median(_rx, k);
        pt.Ry = Median(_ry, k);
        float mx = 0, my = 0, ss = 0;
        int kk = 0;
        for (int j = 0, at = (_i - 1 + Cap) % Cap; j < k; j++, at = (at - 1 + Cap) % Cap)
        {
            float d = (_rx[at] - pt.Rx) * (_rx[at] - pt.Rx) + (_ry[at] - pt.Ry) * (_ry[at] - pt.Ry);
            if (d > RestMax * RestMax) continue;                // (a stray reading: not the look's place, not its scatter)
            mx += _mx[at]; my += _my[at]; ss += d; kk++;
        }
        pt.N = kk;
        pt.Mx = mx / Math.Max(1, kk); pt.My = my / Math.Max(1, kk);
        pt.Spread = MathF.Sqrt(ss / Math.Max(1, kk));
        if (kk < MinTake || kk * 4 < k * 3 || pt.Spread > TakeSpread)
        {
            Log.Info($"eye calibration: point {Index + 1} ({Names[Index]}) scattered {pt.Spread:0.00} deg ({kk} of {k} readings together) - looking again");
            State = Phase.Rest; _restFrom = now; _n = 0; Version++;
            return;
        }
        Taken[Index] = pt;
        Log.Info($"eye calibration: point {Index + 1} of {Count} ({Names[Index]}) {(pt.Auto ? "by itself" : "by the trigger")} - {kk} of {k} readings, " +
                 $"the look {pt.Mx:0.0} deg {pt.My:0.0} deg in the head, the dot {pt.Rx:+0.00;-0.00} deg {pt.Ry:+0.00;-0.00} deg off it, scatter {pt.Spread:0.00} deg");
        Index++;
        _n = 0;
        if (Index < Count)
        {
            State = Phase.Move;
            _from = _pointFrom = now;
            Version++;
            return;
        }
        Result = EyeCalib.Fit(Taken, out var why);
        Why = why;
        State = Result != null ? Phase.Done : Phase.Failed;
        Version++;
        if (Result != null)
            Log.Info($"eye calibration: done - yaw {Result.Yaw:+0.00;-0.00} deg, pitch {Result.Pitch:+0.00;-0.00} deg" +
                     (Result.Linear ? $", linear {Result.Xx:0.000} {Result.Xy:+0.000;-0.000} / {Result.Yx:+0.000;-0.000} {Result.Yy:0.000}" : ", bias only") +
                     $"; the points {Result.Before:0.00} deg off before, {Result.After:0.00} deg after");
        else Log.Info($"eye calibration: not kept - {why}");
    }

    // the median of the newest k of a ring's values (a copy sorted; the ring stays as it is)
    float Median(float[] ring, int k)
    {
        for (int j = 0, at = (_i - 1 + Cap) % Cap; j < k; j++, at = (at - 1 + Cap) % Cap) _sort[j] = ring[at];
        Array.Sort(_sort, 0, k);
        return (k & 1) == 1 ? _sort[k / 2] : (_sort[k / 2 - 1] + _sort[k / 2]) / 2;
    }

    // where the dot is now (metres on the board) and its size (1: full; 0: none) - gliding, breathing while it waits, shrinking as it takes
    public Vector2 Dot(double now, out float size)
    {
        size = 0;
        if (!Running) return Vector2.Zero;
        var at = Local(Index);
        switch (State)
        {
            case Phase.Move:
                float t = (float)Math.Clamp((now - _from) / MoveTime, 0, 1);
                t = 1 - (1 - t) * (1 - t) * (1 - t);
                size = 1;
                return Vector2.Lerp(Local(Math.Max(0, Index - 1)), at, t);
            case Phase.Rest:
                size = 1 + 0.12f * MathF.Sin((float)((now - _restFrom) * 2 * Math.PI * 1.2));
                return at;
            default:
                float k = (float)Math.Clamp((now - _takeFrom) / TakeTime, 0, 1);
                size = 1 - 0.55f * k * k * (3 - 2 * k);
                return at;
        }
    }

    // the take's progress (0..1; the board's pictures)
    public float TakeFrac(double now) => State == Phase.Take ? (float)Math.Clamp((now - _takeFrom) / TakeTime, 0, 1) : 0;

    // --snapshot: a made-up moment of a calibration (the board drawn as it would be then)
    public void Picture(in Matrix4x4 board, int index, Phase state, double now, EyeCalibConfig result = null, string why = null)
    {
        Board = board;
        Index = Math.Clamp(index, 0, Count - 1);
        State = state;
        Result = result;
        Why = why;
        _from = _pointFrom = _restFrom = _takeFrom = now;
        LastReading = now;
        Version++;
    }
}
