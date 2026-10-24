using System;
using System.Numerics;

namespace Orbit;

// The eye cursor's spot (Vr/Laser.cs, Eye cursor "Follow"; Erik 10-19: "a cursor that moves with the eye tracking, smoothed"): the point on a
// monitor the beam aims at, kept apart from the laser so the checks can drive it (GazeFocusTools.EyeCursorChecks, EyeSpotChecks).
// The mean of a fixation (Erik 10-24: "az eye tracking kurzor pontosságán még lehetne javítani"): between two saccades the eyes rest on
// one point and the tracker's noise (0.5-0.7 deg) scatters the readings round it - their mean is where the eyes are. The dead zone and
// the soft follow of 10-19 stopped 0.35 deg short of every look, and the head-frame filter in front of them took the noise for motion
// (7-13 px of jitter, the cursor moving on most frames). It is fed new readings only (Laser.EyeTick: unsmoothed, on the head of their
// own time), its points are in the room (the head turning moves nothing):
//  - a reading far off the fixation (past Break, 2.2 x the noise) is a saccade's if the next one lands near it: a new fixation there,
//    the spot glides to it (Glide, ease-out) and the hand's fine-tune starts again from that moment; a lone one is noise, dropped;
//  - the last 3 readings' mean past ShiftK x the noise: a small saccade (the next word) - a new fixation there;
//  - a slow move (the mean of the last ~7, Recent, off the fixation's by DriftK x the noise): a new fixation there; at half of that the
//    mean's memory shortens (SoftN), so less is left behind after a slow move;
//  - the spot takes the mean once the first SettleN readings are in, then only past Hyst (no creeping cursor, a still hover);
//  - every limit scales with the noise, learned during fixations (the readings' radial RMS round the mean).
// The sim (fixsim3.py, white noise 0.3-0.8 deg an axis, 72 readings a second): 0.06-0.16 deg RMS (2-5 px) at rest, the cursor moving
// 0.3-3.7 times a second, nothing left short; 2-4 deg saccades within 0.25 deg in 50-120 ms (the old spot 150-690 ms, never at 0.5 deg
// of noise); after a slow move 0.12-0.2 deg behind (the old one 0.35); false moves over 0.5 deg 0-2.2 a minute.
// (10-24 review: Erik's tracker is quieter than that - 0.17 deg an axis by the --eyetest -, the noise sat on its floor and a look 0.3-0.45
// deg over (the next toolbar icon) crept there in Hyst hops for up to a second: the floors lower (NoiseMin, DriftMin). And the noise is
// learned only in a fixation of LearnN readings: following moving content (text scrolled by the stick) restarts the fixation every
// 0.1-0.2 s, and the following itself was learned as noise - every limit up for ~3 s after.)
//  - the hand fine-tunes on top, one to one (Beam): once it has turned past Tune the spot stays where the hand put it until a look
//    elsewhere; while held (the trigger or the grip: a click, a drag) and while the eyes are off the screens it stays as well.
public struct EyeSpot
{
    const float Deg = MathF.PI / 180;
    public const float BreakK = 2.2f, BreakMin = 1.5f * Deg, BreakMax = 3f * Deg;   // one noise reading is past 2.2 x RMS ~0.8 % of the time
    public const float ConfirmK = 1.8f, ConfirmMin = 1f * Deg;      // two landed readings agree ~80 %, two mid-saccade ones (4 deg apart) not
    public const float ShiftK = 1.7f;                               // a 3-reading mean (RMS noise / sqrt 3) past 1.7 x noise: ~3 sigma
    public const float DriftK = 1.0f, DriftMin = 0.3f * Deg, DriftMax = 1.2f * Deg, RecentA = 0.15f;      // (DriftMin was 0.5 deg)
    public const float SoftK = 0.5f;
    public const int SoftN = 8, MaxN = 40, SettleN = 8, LearnN = 20;    // (40 readings: ~0.5 s)
    public const float Hyst = 0.12f * Deg;                          // ~4 px
    public const float NoiseStart = 0.7f * Deg, NoiseMin = 0.2f * Deg, NoiseMax = 1.5f * Deg, NoiseA = 0.005f;     // (~200 readings; NoiseMin was 0.3)
    public const float Tune = 1.2f * Deg, Glide = 0.035f;

    public Vector3 Anchor, GlideFrom, HandRef;
    public double GlideAt;
    public bool Tuned;
    public Vector3 Fix, Recent, Cand;               // the fixation's mean, the last ~7 readings' mean, a reading far off (a saccade's?)
    public int FixN;
    public int FixId;                               // a new one each fixation (Start, NewFix; not a Hyst hop): Laser.EyePress's double click
    public float Noise;                             // radians, the readings' radial RMS round the fixation
    Vector3 _r0, _r1, _r2;                          // the last 3 readings
    int _ringN, _ringI;
    bool _hasCand;

    public float BreakAt => Math.Clamp(BreakK * Noise, BreakMin, BreakMax);

    // on: the spot where the eyes are, the hand as it is now (the noise learned so far is kept)
    public void Start(Vector3 look, Vector3 hand)
    {
        Anchor = GlideFrom = Fix = Recent = look;
        FixN = 1;
        FixId++;
        _ringN = 0;
        _hasCand = false;
        GlideAt = -10;
        HandRef = hand;
        Tuned = false;
        if (Noise <= 0) Noise = NoiseStart;
    }

    // a new reading: look = where the eyes are on a screen (seen false: nowhere), eye = where they look from, hand = the hand's direction
    public void Step(Vector3 look, bool seen, Vector3 eye, Vector3 hand, bool held, double clock, float dt)
    {
        if (!Tuned && Angle(hand, HandRef) > Tune) Tuned = true;
        if (held || !seen) return;
        if ((look - eye).LengthSquared() < 1e-8f || (Fix - eye).LengthSquared() < 1e-8f) return;
        if (Noise <= 0) Noise = NoiseStart;
        Push(look);
        var dl = Dir(look, eye);
        float a = Ang(dl, Dir(Fix, eye));
        if (a > BreakAt)
        {
            if (_hasCand && Ang(dl, Dir(Cand, eye)) < MathF.Max(ConfirmMin, ConfirmK * Noise)) NewFix((Cand + look) / 2, 2, hand, clock);
            else { Cand = look; _hasCand = true; }
            return;
        }
        _hasCand = false;
        if (Tuned) return;
        if (_ringN >= 3)
        {
            var m3 = (_r0 + _r1 + _r2) / 3;
            if (Ang(Dir(m3, eye), Dir(Fix, eye)) > ShiftK * Noise) { NewFix(m3, 3, hand, clock); return; }
        }
        if (FixN >= LearnN) Noise = Math.Clamp(MathF.Sqrt(MathF.Max(0, Noise * Noise + (a * a - Noise * Noise) * NoiseA)), NoiseMin, NoiseMax);
        FixN = Math.Min(FixN + 1, MaxN);
        Fix += (look - Fix) / FixN;
        Recent += (look - Recent) * RecentA;
        float drift = Math.Clamp(DriftK * Noise, DriftMin, DriftMax), dr = Ang(Dir(Recent, eye), Dir(Fix, eye));
        if (dr > drift) { NewFix(Recent, 4, hand, clock); return; }
        if (dr > SoftK * drift) FixN = Math.Min(FixN, SoftN);
        if (FixN <= SettleN || Ang(Dir(Fix, eye), Dir(Anchor, eye)) > Hyst) Anchor = Fix;
    }

    // a new fixation at p (n readings in it): the spot glides there
    void NewFix(Vector3 p, int n, Vector3 hand, double clock)
    {
        GlideFrom = Spot(clock);
        GlideAt = clock;
        Anchor = Fix = Recent = p;
        FixN = n;
        FixId++;
        _hasCand = false;
        _ringN = 0;
        HandRef = hand;
        Tuned = false;
    }

    void Push(Vector3 look)
    {
        if (_ringI == 0) _r0 = look; else if (_ringI == 1) _r1 = look; else _r2 = look;
        _ringI = (_ringI + 1) % 3;
        if (_ringN < 3) _ringN++;
    }

    // the screen under the spot moved by d (gaze focus growing it, theater, a grab): every point of the spot with it
    public void Carry(Vector3 d)
    {
        Anchor += d; GlideFrom += d; Fix += d; Recent += d; Cand += d;
        _r0 += d; _r1 += d; _r2 += d;
    }

    public bool Gliding(double clock) => clock - GlideAt < Glide;

    // the spot now (gliding after a look elsewhere)
    public Vector3 Spot(double clock)
    {
        float t = (float)((clock - GlideAt) / Glide);
        if (t >= 1) return Anchor;
        if (t <= 0) return GlideFrom;
        return Vector3.Lerp(GlideFrom, Anchor, 1 - (1 - t) * (1 - t));
    }

    // the beam from origin: to the spot, turned by what the hand turned since the spot was set (one to one)
    public Vector3 Beam(Vector3 origin, Vector3 hand, double clock)
    {
        var to = Spot(clock) - origin;
        if (to.LengthSquared() < 1e-6f) return hand;
        return Vector3.Normalize(Vector3.Transform(Vector3.Normalize(to), FromTo(HandRef, hand)));
    }

    static Vector3 Dir(Vector3 p, Vector3 eye)
    {
        var d = p - eye;
        float l = d.Length();
        return l > 1e-6f ? d / l : -Vector3.UnitZ;
    }

    public static float Angle(Vector3 a, Vector3 b) => MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f));
    static float Ang(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));     // (exact for a hair too)

    // the turn that takes a to b (unit vectors)
    public static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b);
        if (d > 0.9999999f) return Quaternion.Identity;
        if (d < -0.999999f)
        {
            var axis = Vector3.Cross(Vector3.UnitX, a);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, a);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(a, b)), MathF.Acos(d));
    }
}

// The last moves of the screen under the eye cursor's spot (Laser.EyeTick carries the spot with them): when, and their sum so far. A
// reading is EyeGaze.Latency old - the eyes were on the pixel where it was then -, so what the screen moved since (Since) is added to it.
// (10-24 review: while gaze focus grew a monitor - the pixel 4-8 deg in 0.3 s - the readings fell 0.8-1.8 deg behind the carried spot, a
// new fixation started where the content had been and the cursor jumped back; the review's sim with this: 0.06 deg all the way.)
// Fixed arrays, nothing allocated after the start.
public sealed class EyeCarry
{
    public const int Keep = 16;                     // (16 frames: 0.13-0.18 s, the readings' lag ~0.04)
    readonly double[] _at = new double[Keep];
    readonly Vector3[] _sum = new Vector3[Keep];
    int _n, _i;
    Vector3 _total;

    public void Clear() { _n = 0; _total = Vector3.Zero; }

    // the screen moved by d at 'at' (the spot's clock)
    public void Note(double at, Vector3 d)
    {
        _total += d;
        _i = (_i + 1) % Keep;
        _at[_i] = at;
        _sum[_i] = _total;
        if (_n < Keep) _n++;
    }

    // what the screen moved after t
    public Vector3 Since(double t)
    {
        int i = _i;
        for (int k = 0; k < _n; k++)
        {
            if (_at[i] <= t) return _total - _sum[i];
            i = (i + Keep - 1) % Keep;
        }
        return _n < Keep ? _total : _total - _sum[(i + 1) % Keep];     // (every one kept is newer: all since the start / since the oldest)
    }
}
