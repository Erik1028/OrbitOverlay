using System;
using System.Numerics;
using Valve.VR;

namespace Orbit;

// System.Numerics uses row vectors (p' = p * M, translation in M41..M43),
// OpenVR uses column vectors (p' = M * p, translation in m3/m7/m11), so the
// conversion is a transpose. Composition: "local then parent" = local * parent.
static class MathUtil
{
    public static Matrix4x4 FromHmd(in HmdMatrix34_t m) => new(
        m.m0, m.m4, m.m8, 0,
        m.m1, m.m5, m.m9, 0,
        m.m2, m.m6, m.m10, 0,
        m.m3, m.m7, m.m11, 1);

    public static HmdMatrix34_t ToHmd(in Matrix4x4 a) => new()
    {
        m0 = a.M11, m1 = a.M21, m2 = a.M31, m3 = a.M41,
        m4 = a.M12, m5 = a.M22, m6 = a.M32, m7 = a.M42,
        m8 = a.M13, m9 = a.M23, m10 = a.M33, m11 = a.M43,
    };

    public static float[] ToArray(in Matrix4x4 a) => new[]
    {
        a.M11, a.M21, a.M31, a.M41,
        a.M12, a.M22, a.M32, a.M42,
        a.M13, a.M23, a.M33, a.M43,
    };

    public static Matrix4x4 FromArray(float[] m) => m == null || m.Length < 12 ? Matrix4x4.Identity : new(
        m[0], m[4], m[8], 0,
        m[1], m[5], m[9], 0,
        m[2], m[6], m[10], 0,
        m[3], m[7], m[11], 1);

    public static Vector3 Right(in Matrix4x4 m) => new(m.M11, m.M12, m.M13);
    public static Vector3 Up(in Matrix4x4 m) => new(m.M21, m.M22, m.M23);
    public static Vector3 Back(in Matrix4x4 m) => new(m.M31, m.M32, m.M33);   // +Z: overlay normal / towards the viewer
    public static Vector3 Forward(in Matrix4x4 m) => -Back(m);                 // -Z: where a device points

    public static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z, Vector3 t) => new(
        x.X, x.Y, x.Z, 0,
        y.X, y.Y, y.Z, 0,
        z.X, z.Y, z.Z, 0,
        t.X, t.Y, t.Z, 1);

    public static Matrix4x4 Invert(in Matrix4x4 m) => Matrix4x4.Invert(m, out var r) ? r : Matrix4x4.Identity;

    // A transform at 'pos' whose +Z faces 'towards' (an overlay readable from there), kept upright.
    public static Matrix4x4 FacingUpright(Vector3 pos, Vector3 towards)
    {
        Vector3 z = towards - pos;
        z.Y = 0;
        if (z.LengthSquared() < 1e-6f) z = Vector3.UnitZ;
        z = Vector3.Normalize(z);
        Vector3 y = Vector3.UnitY;
        Vector3 x = Vector3.Normalize(Vector3.Cross(y, z));
        return Basis(x, y, z, pos);
    }

    // The same facing 'towards', its top towards 'up' instead of the sky (next to a monitor that lies parallel to rolled eyes, Erik
    // 10-24); false where that is undefined (towards at pos, up along the look).
    public static bool FacingWithUp(Vector3 pos, Vector3 towards, Vector3 up, out Matrix4x4 m)
    {
        m = Matrix4x4.Identity;
        Vector3 z = towards - pos;
        if (z.LengthSquared() < 1e-6f) return false;
        z = Vector3.Normalize(z);
        Vector3 y = up - Vector3.Dot(up, z) * z;
        if (y.LengthSquared() < 1e-6f) return false;
        y = Vector3.Normalize(y);
        m = Basis(Vector3.Cross(y, z), y, z, pos);
        return true;
    }

    // Yaw-only frame of the head: origin at the head, -Z = horizontal look direction.
    public static Matrix4x4 YawFrame(in Matrix4x4 head)
    {
        Vector3 f = Forward(head);
        f.Y = 0;
        if (f.LengthSquared() < 1e-6f) f = -Vector3.UnitZ;
        f = Vector3.Normalize(f);
        Vector3 z = -f;
        Vector3 x = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z));
        return Basis(x, Vector3.UnitY, z, head.Translation);
    }

    // Placement frame for "put things in front of me". Looking roughly level (within minPitchDeg)
    // it is the yaw-only frame, so panels stay upright and a small nod doesn't tilt them. Looking
    // clearly up or down (reclined, lying in bed) it follows the gaze: -Z = where you look,
    // +Y = towards the top of your head without roll (or the headset's own up when looking
    // almost straight up/down, where "level" is undefined).
    public static Matrix4x4 GazeFrame(in Matrix4x4 head, float minPitchDeg = 15)
    {
        Vector3 f = Vector3.Normalize(Forward(head));
        float pitch = MathF.Asin(Math.Clamp(f.Y, -1f, 1f)) * 180 / MathF.PI;
        if (MathF.Abs(pitch) < minPitchDeg) return YawFrame(head);
        Vector3 up = Vector3.UnitY - Vector3.Dot(Vector3.UnitY, f) * f;
        if (up.Length() < 0.35f)
        {
            up = Up(head);
            up -= Vector3.Dot(up, f) * f;
        }
        up = Vector3.Normalize(up);
        Vector3 z = -f;
        Vector3 x = Vector3.Normalize(Vector3.Cross(up, z));
        up = Vector3.Cross(z, x);
        return Basis(x, up, z, head.Translation);
    }

    // Lying on a side (Erik 10-24: "ha az oldalamon fekszek akkor is a szememmel párhuzamosan jelenjen meg az overlay") the head is
    // rolled 60-90°: past this the placement frame is the head's own. Sitting, a tilted head stays under it (resting on a hand, a look
    // at the watch: 10-25°; the neck's own side bend ends at ~45°).
    public const float LieRollDeg = 40;
    static long _lieLogMs;

    // The head lies: rolled past LieRollDeg, and looking down the eye line itself clearly off level too (past 35°). The roll
    // (atan2(right.y, up.y)) reads a bowed head turned on its neck as rolled, tan(roll) = sin(turn) * tan(bow): sitting, a look at the
    // watch low at the lap (bowed 60-70°, turned 20-30°) read 41-54° with the eyes only 19-28° off level (review 10-24) - upright stays
    // upright. Lying on a side the eye line is near vertical (past 35° still with the head turned 50° towards the floor; towards the
    // ceiling it looks up); lying back the look goes up, and a turned head counts by its roll alone. worn = false (VrSession.OnFace: the
    // headset off, maybe on its side on the desk): never.
    public static bool IsLying(in Matrix4x4 head, bool worn)
    {
        if (!worn) return false;
        Vector3 r = Right(head), u = Up(head);
        if (r.LengthSquared() < 1e-6f || u.LengthSquared() < 1e-6f) return false;
        r = Vector3.Normalize(r);
        u = Vector3.Normalize(u);
        return MathF.Abs(MathF.Atan2(r.Y, u.Y)) * 180 / MathF.PI > LieRollDeg && (MathF.Abs(r.Y) > 0.57f || Forward(head).Y > 0);
    }

    // Where Recenter, Reset position, Join and "bring here" put things: GazeFrame as ever (upright a nod or a slight tilt ignored,
    // reclined it follows the look), unless the head lies (IsLying) - then its full rotation, so the monitors lie parallel to the eyes
    // and what goes under them goes towards the chin. Roll = atan2(right.y, up.y): both scale with cos(pitch), so it reads the same at
    // any pitch; near straight up / down they vanish, but GazeFrame takes the headset's own up there anyway (the same frame). Decided
    // once, at the moment of placement (nothing follows the head after). what: names the placement in the log line when the head lay
    // (a line a second at most: Recenter's helpers ask again).
    public static Matrix4x4 PlacementFrame(in Matrix4x4 head, bool worn, string what = null)
    {
        if (!IsLying(head, worn)) return GazeFrame(head);
        Vector3 u = Up(head), z = Back(head);
        if (z.LengthSquared() < 1e-6f) return GazeFrame(head);
        z = Vector3.Normalize(z);
        Vector3 x = Vector3.Cross(u, z);
        if (x.LengthSquared() < 1e-6f) return GazeFrame(head);
        x = Vector3.Normalize(x);
        if (what != null && Environment.TickCount64 - _lieLogMs > 1000)
        {
            _lieLogMs = Environment.TickCount64;
            Log.Info($"{what}: lying (roll {MathF.Atan2(Right(head).Y, u.Y) * 180 / MathF.PI:0} deg) - placed parallel to the eyes");
        }
        return Basis(x, Vector3.Cross(z, x), z, head.Translation);
    }

    public static float Smoothstep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
