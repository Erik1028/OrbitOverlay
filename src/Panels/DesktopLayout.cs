using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Orbit;

// Snapping monitors together into rows ("groups") and keeping a row seamless.
//
// SteamVR bends a curved overlay around a vertical axis that sits `r` metres in front of it (+Z),
// with r = width / (2*pi*curvature); the overlay's origin is the middle of the arc. So monitors
// sharing one radius and placed edge to edge along that circle form one continuous curved screen.
public sealed class DesktopLayout
{
    readonly List<DesktopPanel> _panels;
    readonly ConfigStore _cfg;

    public DesktopLayout(List<DesktopPanel> panels, ConfigStore cfg) { _panels = panels; _cfg = cfg; }

    public static float Radius(DesktopConfig c) =>
        c.Curvature < 1e-4f ? float.PositiveInfinity : c.Width / (2 * MathF.PI * c.Curvature);

    public static float CurvatureFor(float width, float radius) =>
        float.IsInfinity(radius) ? 0 : Flat(Math.Clamp(width / (2 * MathF.PI * radius), 0, 1));

    // Below this a bend is invisible (a radius of hundreds of metres), and SteamVR's own ray test turns
    // imprecise there (float, measured with --raytest): such panels are made exactly flat.
    public const float MinCurvature = 0.0005f;
    public static float Flat(float c) => c < MinCurvature ? 0 : c;

    static bool IsWorld(DesktopPanel p) => Placement.IsWorld(p.Cfg.Place) && p.Cfg.Place.M != null;
    static Matrix4x4 World(DesktopPanel p) => MathUtil.FromArray(p.Cfg.Place.M);
    static void SetWorld(DesktopPanel p, in Matrix4x4 m) => p.Cfg.Place.M = MathUtil.ToArray(m);

    // Where a monitor of width wN sits right (+1) or left (-1) of a monitor with transform W, width w, radius r.
    public static Matrix4x4 Neighbor(in Matrix4x4 W, float w, float r, float wN, int side) => Neighbor(W, w, r, wN, side, 0);

    // ... 'gap' metres further along the arc (gaze focus redraws a row with the gaps it was saved with, Panels/GazeFocus.cs)
    public static Matrix4x4 Neighbor(in Matrix4x4 W, float w, float r, float wN, int side, float gap)
    {
        float s = side * (w / 2 + wN / 2 + gap);
        if (float.IsInfinity(r)) return Matrix4x4.CreateTranslation(s, 0, 0) * W;
        float phi = s / r;
        var x = new Vector3(MathF.Cos(phi), 0, MathF.Sin(phi));
        var z = new Vector3(-MathF.Sin(phi), 0, MathF.Cos(phi));
        var pos = new Vector3(r * MathF.Sin(phi), 0, r - r * MathF.Cos(phi));
        return MathUtil.Basis(x, Vector3.UnitY, z, pos) * W;
    }

    public List<DesktopPanel> Members(string group) =>
        group == null ? new List<DesktopPanel>()
            : _panels.Where(p => p.Cfg.Group == group && p.Cfg.Enabled && IsWorld(p)).OrderBy(p => p.Cfg.GroupIndex).ToList();

    public int GroupSize(DesktopConfig c) => c.Group == null ? 1 : Members(c.Group).Count;

    // Re-chains the row around 'anchor' (which stays put): same radius for all, edge to edge.
    public void Reflow(DesktopPanel anchor)
    {
        if (anchor.Cfg.Group == null || !IsWorld(anchor)) return;
        var row = Members(anchor.Cfg.Group);
        int ai = row.IndexOf(anchor);
        if (ai < 0) return;
        for (int i = 0; i < row.Count; i++) row[i].Cfg.GroupIndex = i;
        float r = Radius(anchor.Cfg);
        foreach (int dir in new[] { 1, -1 })
        {
            var prev = anchor;
            var prevW = World(anchor);
            for (int i = ai + dir; i >= 0 && i < row.Count; i += dir)
            {
                var m = row[i];
                m.Cfg.Curvature = CurvatureFor(m.Cfg.Width, r);
                var w = Neighbor(prevW, prev.Cfg.Width, r, m.Cfg.Width, dir);
                SetWorld(m, w);
                prev = m;
                prevW = w;
            }
        }
        _cfg.MarkDirty();
    }

    // Takes one monitor out of its row; the rest closes up.
    public void Detach(DesktopPanel p)
    {
        string g = p.Cfg.Group;
        if (g == null) return;
        p.Cfg.Group = null;
        var rest = Members(g);
        if (rest.Count == 1) rest[0].Cfg.Group = null;
        else if (rest.Count > 1) Reflow(rest[0]);
        _cfg.MarkDirty();
    }

    public struct Snap
    {
        public DesktopPanel Other;
        public int Side;
        public Matrix4x4 World;
    }

    // A lone monitor near the left/right edge of another (same facing) snaps next to it.
    public Snap? FindSnap(DesktopPanel p, in Matrix4x4 world)
    {
        if (p.Cfg.Group != null && GroupSize(p.Cfg) > 1) return null;
        if (!Placement.IsWorld(p.Cfg.Place)) return null;
        Snap? best = null;
        float bestDist = 0.2f;
        Vector3 z = Vector3.Normalize(MathUtil.Back(world));
        foreach (var o in _panels)
        {
            if (o == p || !o.Cfg.Enabled || !IsWorld(o) || o.Ov == null || !o.Ov.Visible) continue;
            var ow = World(o);
            float r = Radius(o.Cfg);
            foreach (int side in new[] { 1, -1 })
            {
                var cand = Neighbor(ow, o.Cfg.Width, r, p.Cfg.Width, side);
                float dist = Vector3.Distance(cand.Translation, world.Translation);
                float facing = Vector3.Dot(Vector3.Normalize(MathUtil.Back(cand)), z);
                if (dist < bestDist && facing > 0.8f)
                {
                    bestDist = dist;
                    best = new Snap { Other = o, Side = side, World = cand };
                }
            }
        }
        return best;
    }

    // Puts p into o's row on the given side (pushing the rest of that side outwards).
    public void Join(DesktopPanel p, Snap s)
    {
        var o = s.Other;
        if (o.Cfg.Group == null)
        {
            o.Cfg.Group = Guid.NewGuid().ToString("N")[..8];
            o.Cfg.GroupIndex = 0;
        }
        var row = Members(o.Cfg.Group);
        int oi = row.IndexOf(o);
        if (oi < 0 || !_panels.Contains(p)) return;      // panels were rebuilt meanwhile (monitor change)
        row.Remove(p);
        oi = row.IndexOf(o);
        row.Insert(s.Side > 0 ? oi + 1 : oi, p);
        p.Cfg.Group = o.Cfg.Group;
        for (int i = 0; i < row.Count; i++) row[i].Cfg.GroupIndex = i;
        p.Cfg.Curvature = CurvatureFor(p.Cfg.Width, Radius(o.Cfg));
        Reflow(o);
    }

    // One seamless curved screen around the head, monitors in desk order. frame: MathUtil.PlacementFrame (lying on a side the
    // whole row rolls with it, Erik 10-24).
    public void JoinAll(in Matrix4x4 frame, float radius, float yOffset)
    {
        var row = _panels.Where(p => p.Cfg.Enabled).OrderBy(p => p.Mon.X).ThenBy(p => p.Mon.Y).ToList();
        if (row.Count == 0) return;
        string g = row.Count > 1 ? Guid.NewGuid().ToString("N")[..8] : null;
        float total = row.Sum(p => p.Cfg.Width);
        float acc = -total / 2;
        for (int i = 0; i < row.Count; i++)
        {
            var c = row[i].Cfg;
            float th = (acc + c.Width / 2) / radius;
            var local = Matrix4x4.CreateRotationY(-th) *
                        Matrix4x4.CreateTranslation(radius * MathF.Sin(th), yOffset, -radius * MathF.Cos(th));
            c.Place.Attach = "world";
            c.Place.M = MathUtil.ToArray(local * frame);
            c.Curvature = CurvatureFor(c.Width, radius);
            c.Group = g;
            c.GroupIndex = i;
            acc += c.Width;
        }
        _cfg.MarkDirty();
    }

    public void SeparateAll()
    {
        foreach (var p in _panels) p.Cfg.Group = null;
        _cfg.MarkDirty();
    }

    // Bends a monitor (and its row) so the curve is centred on the viewer.
    public void FitCurve(DesktopPanel p, Vector3 head)
    {
        if (!IsWorld(p)) return;
        var w = World(p);
        var d = w.Translation - head;
        // the radius across the panel's own up (SteamVR bends it round that axis): upright that is the level distance as before; a row
        // parallel to rolled eyes (Erik 10-24) or overhead lying back was measured nearly along its axis (lying back: a 0.3 m bend)
        var up = MathUtil.Up(w);
        float ul = up.Length();
        if (ul > 1e-3f && MathF.Abs(up.Y / ul) < 0.9999f) { up /= ul; d -= Vector3.Dot(d, up) * up; }
        else d.Y = 0;                                           // (upright: to the bit as before)
        float dist = Math.Max(0.3f, d.Length());
        p.Cfg.Curvature = CurvatureFor(p.Cfg.Width, dist);
        Reflow(p);
        _cfg.MarkDirty();
    }
}
