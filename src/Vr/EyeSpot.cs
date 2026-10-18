using System;
using System.Numerics;

namespace Orbit;

// The eye cursor's spot (Vr/Laser.cs, Eye cursor "Follow"; Erik 10-19: "a cursor that moves with the eye tracking, smoothed"): the point on a
// monitor the beam aims at, kept apart from the laser so the checks can drive it (GazeFocusTools.EyeCursorChecks).
//  - the eyes' tremble (within Dead of the spot) moves nothing;
//  - a small move of the look (reading along a line) is followed softly (FollowHz), to Dead short of it;
//  - a look elsewhere (past Jump) glides there in Glide, ease-out, and the hand's fine-tune starts again from that moment;
//  - the hand fine-tunes on top, one to one (Beam): once it has turned past Tune the spot stays where the hand put it until a look
//    elsewhere; while held (the trigger or the grip: a click, a drag) and while the eyes are off the screens it stays as well.
public struct EyeSpot
{
    // (Erik 10-19 "more sensitive": the dead zone 0.7 -> 0.35 deg, the follow 4 -> 9 Hz, a look elsewhere from 4 -> 2.5 deg, the glide 70 ->
    // 45 ms; the hand's fine-tune from 0.6 -> 1.2 deg - the hand's own tremble had frozen the spot)
    public const float Jump = 2.5f * MathF.PI / 180, Dead = 0.35f * MathF.PI / 180, Tune = 1.2f * MathF.PI / 180, FollowHz = 9, Glide = 0.045f;

    public Vector3 Anchor, GlideFrom, HandRef;
    public double GlideAt;
    public bool Tuned;

    // on: the spot where the eyes are, the hand as it is now
    public void Start(Vector3 look, Vector3 hand)
    {
        Anchor = GlideFrom = look;
        GlideAt = -10;
        HandRef = hand;
        Tuned = false;
    }

    // a frame: look = where the eyes are on a screen (seen false: nowhere), eye = where they look from, hand = the hand's direction
    public void Step(Vector3 look, bool seen, Vector3 eye, Vector3 hand, bool held, double clock, float dt)
    {
        if (!Tuned && Angle(hand, HandRef) > Tune) Tuned = true;
        if (held || !seen) return;
        var toLook = look - eye; var toAnchor = Anchor - eye;
        if (toLook.LengthSquared() < 1e-8f || toAnchor.LengthSquared() < 1e-8f) return;
        float a = Angle(Vector3.Normalize(toLook), Vector3.Normalize(toAnchor));
        if (a > Jump)
        {
            GlideFrom = Spot(clock);
            GlideAt = clock;
            Anchor = look;
            HandRef = hand;
            Tuned = false;
        }
        else if (!Tuned && a > Dead)
        {
            float k = (1 - MathF.Exp(-2 * MathF.PI * FollowHz * dt)) * (1 - Dead / a);
            Anchor = Vector3.Lerp(Anchor, look, k);
        }
    }

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

    public static float Angle(Vector3 a, Vector3 b) => MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f));

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
