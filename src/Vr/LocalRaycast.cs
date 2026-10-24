using System;
using System.Numerics;

namespace Orbit;

// A ray against one overlay the way SteamVR draws it, computed in-process instead of asking SteamVR
// (ComputeOverlayIntersection is an IPC round trip per panel per hand per frame, and stalls when the
// compositor is busy). Flat: a w x h quad in the overlay's local XY plane, front = +Z. Curved
// (curvature c > 0): a vertical cylinder section whose axis is r = w / (2*pi*c) in front (+Z) of
// the origin, the width being the arc length and u linear in the angle (measured with SteamVR).
// SteamVR counts a curved overlay only where the ray LEAVES the cylinder (its inner, concave face):
// rays from behind never hit it, and a ray that crosses the arc twice hits the second crossing
// (--raytest against ComputeOverlayIntersection). A flat overlay is hit from both sides.
// uv comes back with a top-left origin, like VrOverlay.Raycast. Allocates nothing.
// clampToEdge: a ray past the edge still "hits", at the nearest point of the edge (pointer capture: a panel keeps the
// beam of a hand that holds the trigger on it, so a drag can reach the very edge of the screen).
public static class LocalRaycast
{
    public static bool Intersect(in Matrix4x4 world, float width, float curvature, float texW, float texH,
        Vector3 origin, Vector3 dir, out float dist, out Vector2 uvTopLeft, out Vector3 point, out Vector3 normal,
        bool clampToEdge = false)
    {
        dist = 0; uvTopLeft = default; point = default; normal = default;
        if (!(width > 0) || !(texW > 0) || !(texH > 0)) return false;
        if (!Matrix4x4.Invert(world, out var inv)) return false;
        float h = width * texH / texW;
        var o = Vector3.Transform(origin, inv);
        var d = Vector3.TransformNormal(dir, inv);

        Vector3 p, n;
        float u;
        if (!(curvature > 0))
        {
            if (MathF.Abs(d.Z) < 1e-12f) return false;
            float t = -o.Z / d.Z;
            if (!(t > 0)) return false;
            p = o + d * t;
            if (clampToEdge) p = new Vector3(Math.Clamp(p.X, -width / 2, width / 2), Math.Clamp(p.Y, -h / 2, h / 2), 0);
            if (MathF.Abs(p.X) > width / 2 || MathF.Abs(p.Y) > h / 2) return false;
            u = p.X / width + 0.5f;
            n = Vector3.UnitZ;
        }
        else
        {
            // in double: a slight bend means a huge radius (c = 0.00001 on 0.3 m is a 4.8 km cylinder)
            double r = width / (2 * Math.PI * curvature);
            double half = Math.PI * curvature;                  // half the arc angle (= w / 2r)
            // circle x^2 + (z - r)^2 = r^2 in the local XZ plane, written without the r^2 - r^2 cancellation
            double ox = o.X, oz = o.Z, dx = d.X, dz = d.Z;
            double a = dx * dx + dz * dz;
            if (a < 1e-18) return false;
            double b = 2 * (ox * dx + (oz - r) * dz);
            double c = ox * ox + oz * oz - 2 * r * oz;
            double disc = b * b - 4 * a * c;
            if (disc < 0) return false;
            // stable roots; the larger one is where the ray leaves the cylinder
            double q = -0.5 * (b + (b >= 0 ? Math.Sqrt(disc) : -Math.Sqrt(disc)));
            double r1 = q / a, r2 = q != 0 ? c / q : r1;
            double tExit = Math.Max(r1, r2);
            if (!(tExit > 0)) return false;
            double px = ox + dx * tExit, py = o.Y + d.Y * tExit, pz = oz + dz * tExit;
            double th = Math.Atan2(px, r - pz);                 // 0 at the origin, positive towards +X
            if (clampToEdge)
            {
                th = Math.Clamp(th, -half, half);
                py = Math.Clamp(py, -h / 2, h / 2);
                px = r * Math.Sin(th);
                pz = r - r * Math.Cos(th);
            }
            if (Math.Abs(py) > h / 2) return false;
            if (Math.Abs(th) > half) return false;
            p = new Vector3((float)px, (float)py, (float)pz);
            u = (float)(th / (2 * half) + 0.5);
            n = Vector3.Normalize(new Vector3((float)-px, 0, (float)(r - pz)));   // towards the axis = the viewer's side
        }

        point = Vector3.Transform(p, world);
        dist = Vector3.Distance(origin, point);
        uvTopLeft = new Vector2(u, 0.5f - p.Y / h);
        normal = Vector3.Normalize(Vector3.TransformNormal(n, world));
        return true;
    }

    // the other way round: the point in the world where uv (top-left origin) of the overlay is (the eye cursor's spot kept on the pixel
    // it is on while the screen moves - Laser.EyeTick)
    public static Vector3 PointAt(in Matrix4x4 world, float width, float curvature, float texW, float texH, Vector2 uvTopLeft)
    {
        float h = texW > 0 ? width * texH / texW : 0;
        float y = (0.5f - uvTopLeft.Y) * h;
        Vector3 p;
        if (!(curvature > 0)) p = new Vector3((uvTopLeft.X - 0.5f) * width, y, 0);
        else
        {
            double r = width / (2 * Math.PI * curvature), th = (uvTopLeft.X - 0.5) * 2 * Math.PI * curvature;
            p = new Vector3((float)(r * Math.Sin(th)), y, (float)(r - r * Math.Cos(th)));
        }
        return Vector3.Transform(p, world);
    }
}
