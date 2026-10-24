using System;
using System.Numerics;

namespace Orbit;

// SlimeVR's body as its server computes it (Erik 10-24: "fekve elcsavarodik a csípőm és a lábam" - the LEFT leg, thigh and shin, turns
// 20-40 deg against the pelvis while he lies): the skeleton's bones from SolarXR's data feed (SlimeVrLink.cs asks for them only while the
// SlimeVR widget's Body side shows), made into one immutable pose per update on the link's thread - nothing of it is made on a frame. The
// watch draws it as a stick figure (WristSlimeBody.cs) and says how far each leg is turned against the pelvis.
// The space is SlimeVR's: X right, Y up, -Z forward, metres; a bone's rotation is global, at rest (identity) its tail straight down
// (-Y) - v21's rotation_g (its tail = head + rotation (0, -length, 0), Bone.kt), v22's rotation (rest-relative, BoneState.rotation; its
// head and tail come with it). Only the hip's and the legs' rotations are read (v22's feet and lower legs carry offsets: they are drawn
// from their positions).
// The twist of a leg: its rotation relative to the pelvis (conj(hip) * bone), the part of it about the pelvis' own up axis (the swing-
// twist split, 2 atan2(q.y, q.w)) - positive turns the toes to the left (the left leg out, the right leg in); its sideways lean: where
// that rotation takes the bone's down axis, asin of its x. Neither depends on how the body lies: standing, sitting and lying read the same.
public sealed class SlimePose
{
    public const int Parts = (int)SlimeVrLink.Part.Toe + 1;
    public const int LT = 0, LS = 1, RT = 2, RS = 3;          // Twist / Side: the left thigh, the left shin, the right thigh, the right shin

    public struct Bone
    {
        public bool Has;
        public Vector3 Head, Tail;
        public Quaternion Rot;
    }

    // what the reader fills, one per connection (reused: no garbage per update)
    public sealed class Raw
    {
        public readonly bool[] Has = new bool[Parts];
        public readonly Quaternion[] Rot = new Quaternion[Parts];
        public readonly Vector3[] Head = new Vector3[Parts], Tail = new Vector3[Parts];
        public int Count;

        public void Clear() { Array.Clear(Has); Count = 0; }
    }

    public readonly int Version;
    public readonly double At;                                // Clock.Now when it came
    public readonly Bone[] B = new Bone[Parts];               // by SlimeVrLink.Part
    public readonly float[] Twist = new float[4], Side = new float[4];      // degrees (NaN: that bone not sent)
    public readonly bool Has;                                 // a hip and at least one leg: something to draw and to read
    public readonly bool Lying, Rolled;                       // the pelvis lies (its down axis within 30 deg of the floor); on a side
    public readonly Vector3 R, U;                             // the pelvis' right and up (the front view's axes: the body seen face-on)
    public readonly Vector3 H;                                // the side view's horizontal (its vertical is the world's up)
    public readonly Vector3 Anchor;                           // the pelvis (between the thighs' heads): both views' centre
    public readonly float Upper, Leg, Height;                 // m: the top of the head to the pelvis, the pelvis to the heel, both
    public readonly float HipHead;                            // deg the pelvis faces away from the head (NaN: not known, lying)

    // A pose from the reader's bones; prev: the last one (its side view's direction eases from there: the figure does not jump)
    public SlimePose(Raw r, int version, double at, SlimePose prev = null)
    {
        Version = version;
        At = at;
        for (int i = 0; i < Parts; i++)
            if (r.Has[i]) B[i] = new Bone { Has = true, Head = r.Head[i], Tail = r.Tail[i], Rot = r.Rot[i] };
        ref var hip = ref B[(int)SlimeVrLink.Part.Hip];
        for (int i = 0; i < 4; i++) Twist[i] = Side[i] = float.NaN;
        bool legs = B[(int)SlimeVrLink.Part.LeftThigh].Has || B[(int)SlimeVrLink.Part.RightThigh].Has;
        Has = hip.Has && legs;
        Upper = Chain(SpineParts) + 0.10f;                       // (the head bone ends at the eyes: a little more for its top)
        float ll = Chain(LeftLeg), rl = Chain(RightLeg);
        Leg = MathF.Max(ll, rl);
        Height = Upper + Leg;
        HipHead = float.NaN;
        if (!hip.Has) { R = Vector3.UnitX; U = Vector3.UnitY; H = -Vector3.UnitZ; return; }
        var q = Quaternion.Normalize(hip.Rot);
        R = Vector3.Transform(Vector3.UnitX, q);
        U = Vector3.Transform(Vector3.UnitY, q);
        Lying = MathF.Abs(U.Y) < 0.5f;
        Rolled = MathF.Abs(R.Y) > 0.7f;
        // the side view looks along the pelvis' right axis laid on the floor (lying on the back keeps it level: the body lies along the
        // view); on a side that axis stands up - then along the body itself
        var rf = new Vector3(R.X, 0, R.Z);
        Vector3 h = rf.Length() >= 0.5f ? Vector3.Normalize(new Vector3(rf.Z, 0, -rf.X)) : Flat(U, -Vector3.UnitZ);
        if (prev != null && prev.Has && Vector3.Dot(prev.H, h) > -0.3f) h = Flat(Vector3.Lerp(prev.H, h, 0.35f), h);
        H = h;
        var lt = B[(int)SlimeVrLink.Part.LeftThigh];
        var rt = B[(int)SlimeVrLink.Part.RightThigh];
        Anchor = lt.Has && rt.Has ? (lt.Head + rt.Head) / 2 : hip.Tail;
        var inv = Quaternion.Conjugate(q);
        Read(inv, SlimeVrLink.Part.LeftThigh, LT);
        Read(inv, SlimeVrLink.Part.LeftAnkle, LS);
        Read(inv, SlimeVrLink.Part.RightThigh, RT);
        Read(inv, SlimeVrLink.Part.RightAnkle, RS);
        // the pelvis against the head, both laid on the floor (standing or sitting: lying, the head's forward points up)
        ref var head = ref B[(int)SlimeVrLink.Part.Head];
        if (head.Has && !Lying)
        {
            var hf = Vector3.Transform(-Vector3.UnitZ, Quaternion.Normalize(head.Rot));
            var pf = Vector3.Transform(-Vector3.UnitZ, q);
            var a = new Vector2(hf.X, hf.Z);
            var b = new Vector2(pf.X, pf.Z);
            if (a.Length() > 0.5f && b.Length() > 0.5f)
                HipHead = MathF.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b)) * 180 / MathF.PI;
        }
    }

    void Read(Quaternion inv, SlimeVrLink.Part p, int slot)
    {
        ref var b = ref B[(int)p];
        if (!b.Has) return;
        var q = inv * Quaternion.Normalize(b.Rot);
        float t = 2 * MathF.Atan2(q.Y, q.W) * 180 / MathF.PI;
        while (t > 180) t -= 360;
        while (t <= -180) t += 360;
        Twist[slot] = t;
        var d = Vector3.Transform(-Vector3.UnitY, q);
        Side[slot] = MathF.Asin(Math.Clamp(d.X, -1, 1)) * 180 / MathF.PI;
    }

    float Chain(SlimeVrLink.Part[] parts)
    {
        float s = 0;
        foreach (var p in parts) if (B[(int)p].Has) s += Vector3.Distance(B[(int)p].Head, B[(int)p].Tail);
        return s;
    }

    static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        var f = new Vector3(v.X, 0, v.Z);
        return f.LengthSquared() > 1e-6f ? Vector3.Normalize(f) : fallback;
    }

    // a point in the front view (the body seen face-on from the pelvis: right, up) and in the side view (along the floor, the world's
    // up), metres from the pelvis
    public Vector2 Front(Vector3 p) { var d = p - Anchor; return new Vector2(Vector3.Dot(d, R), Vector3.Dot(d, U)); }
    public Vector2 SideOf(Vector3 p) { var d = p - Anchor; return new Vector2(Vector3.Dot(d, H), d.Y); }


    // the larger of a leg's turns (thigh and shin, twist or lean): the warning's level; NaN: that leg not sent
    public float Worst(bool left)
    {
        float a = Turn(left ? LT : RT), b = Turn(left ? LS : RS);
        return float.IsNaN(a) ? b : float.IsNaN(b) ? a : MathF.Max(a, b);
    }

    float Turn(int i) => float.IsNaN(Twist[i]) ? float.NaN : MathF.Max(MathF.Abs(Twist[i]), MathF.Abs(Side[i]));

    // the parts the figure is made of (head to toe, then the arms) - v21's hip bones (LEFT_HIP, RIGHT_HIP) are not drawn: the line from
    // the pelvis to a thigh is them; fingers and toes are left out
    public static readonly SlimeVrLink.Part[] SpineParts =
    {
        SlimeVrLink.Part.Head, SlimeVrLink.Part.Neck, SlimeVrLink.Part.UpperChest, SlimeVrLink.Part.Chest, SlimeVrLink.Part.LowerChest,
        SlimeVrLink.Part.Waist, SlimeVrLink.Part.UpperWaist, SlimeVrLink.Part.LowerWaist, SlimeVrLink.Part.Hip,
    };
    public static readonly SlimeVrLink.Part[] LeftLeg = { SlimeVrLink.Part.LeftThigh, SlimeVrLink.Part.LeftAnkle };
    public static readonly SlimeVrLink.Part[] RightLeg = { SlimeVrLink.Part.RightThigh, SlimeVrLink.Part.RightAnkle };

    public static readonly SlimeVrLink.Part[] Drawn =
    {
        SlimeVrLink.Part.Head, SlimeVrLink.Part.Neck, SlimeVrLink.Part.UpperChest, SlimeVrLink.Part.Chest, SlimeVrLink.Part.LowerChest,
        SlimeVrLink.Part.Waist, SlimeVrLink.Part.UpperWaist, SlimeVrLink.Part.LowerWaist, SlimeVrLink.Part.Hip,
        SlimeVrLink.Part.LeftThigh, SlimeVrLink.Part.LeftAnkle, SlimeVrLink.Part.LeftFoot,
        SlimeVrLink.Part.RightThigh, SlimeVrLink.Part.RightAnkle, SlimeVrLink.Part.RightFoot,
        SlimeVrLink.Part.LeftShoulder, SlimeVrLink.Part.LeftUpperArm, SlimeVrLink.Part.LeftLowerArm, SlimeVrLink.Part.LeftHand,
        SlimeVrLink.Part.RightShoulder, SlimeVrLink.Part.RightUpperArm, SlimeVrLink.Part.RightLowerArm, SlimeVrLink.Part.RightHand,
    };

    // where a drawn part hangs from, the first of these that was sent: a parent's tail (Head: true, its head - the shoulders hang from
    // the top of the upper chest). v21's spine is head, neck, upper chest, chest, waist, hip; v22's has a lower chest and two waists.
    static readonly (SlimeVrLink.Part p, bool head)[][] From = MakeFrom();

    static (SlimeVrLink.Part, bool)[][] MakeFrom()
    {
        var f = new (SlimeVrLink.Part, bool)[Parts][];
        for (int i = 0; i < Parts; i++) f[i] = Array.Empty<(SlimeVrLink.Part, bool)>();
        void S(SlimeVrLink.Part p, params SlimeVrLink.Part[] tails)
        {
            f[(int)p] = new (SlimeVrLink.Part, bool)[tails.Length];
            for (int i = 0; i < tails.Length; i++) f[(int)p][i] = (tails[i], false);
        }
        S(SlimeVrLink.Part.Neck, SlimeVrLink.Part.Head);
        S(SlimeVrLink.Part.UpperChest, SlimeVrLink.Part.Neck, SlimeVrLink.Part.Head);
        S(SlimeVrLink.Part.Chest, SlimeVrLink.Part.UpperChest, SlimeVrLink.Part.Neck);
        S(SlimeVrLink.Part.LowerChest, SlimeVrLink.Part.UpperChest, SlimeVrLink.Part.Neck);
        S(SlimeVrLink.Part.Waist, SlimeVrLink.Part.Chest, SlimeVrLink.Part.UpperChest);
        S(SlimeVrLink.Part.UpperWaist, SlimeVrLink.Part.LowerChest, SlimeVrLink.Part.UpperChest);
        S(SlimeVrLink.Part.LowerWaist, SlimeVrLink.Part.UpperWaist, SlimeVrLink.Part.LowerChest);
        S(SlimeVrLink.Part.Hip, SlimeVrLink.Part.LowerWaist, SlimeVrLink.Part.Waist, SlimeVrLink.Part.UpperWaist, SlimeVrLink.Part.Chest,
            SlimeVrLink.Part.LowerChest, SlimeVrLink.Part.UpperChest);
        S(SlimeVrLink.Part.LeftThigh, SlimeVrLink.Part.Hip);
        S(SlimeVrLink.Part.RightThigh, SlimeVrLink.Part.Hip);
        S(SlimeVrLink.Part.LeftAnkle, SlimeVrLink.Part.LeftThigh);
        S(SlimeVrLink.Part.RightAnkle, SlimeVrLink.Part.RightThigh);
        S(SlimeVrLink.Part.LeftFoot, SlimeVrLink.Part.LeftAnkle);
        S(SlimeVrLink.Part.RightFoot, SlimeVrLink.Part.RightAnkle);
        f[(int)SlimeVrLink.Part.LeftShoulder] = new[] { (SlimeVrLink.Part.UpperChest, true), (SlimeVrLink.Part.Neck, false) };
        f[(int)SlimeVrLink.Part.RightShoulder] = new[] { (SlimeVrLink.Part.UpperChest, true), (SlimeVrLink.Part.Neck, false) };
        S(SlimeVrLink.Part.LeftUpperArm, SlimeVrLink.Part.LeftShoulder);
        S(SlimeVrLink.Part.RightUpperArm, SlimeVrLink.Part.RightShoulder);
        S(SlimeVrLink.Part.LeftLowerArm, SlimeVrLink.Part.LeftUpperArm);
        S(SlimeVrLink.Part.RightLowerArm, SlimeVrLink.Part.RightUpperArm);
        S(SlimeVrLink.Part.LeftHand, SlimeVrLink.Part.LeftLowerArm);
        S(SlimeVrLink.Part.RightHand, SlimeVrLink.Part.RightLowerArm);
        return f;
    }

    // the joint a part hangs from in this pose (false: none sent - the head)
    public bool Joint(SlimeVrLink.Part p, out Vector3 at)
    {
        foreach (var (q, head) in From[(int)p])
        {
            ref var b = ref B[(int)q];
            if (!b.Has) continue;
            at = head ? b.Head : b.Tail;
            return true;
        }
        at = default;
        return false;
    }
}
