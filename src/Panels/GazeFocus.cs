using System;
using System.Collections.Generic;
using System.Numerics;

namespace Orbit;

// What a monitor of a focused row draws this frame instead of its saved place and size. An override like TheaterView: the saved
// layout (Cfg.Place / Width / Curvature) is never written through it.
public sealed class FocusView
{
    public Matrix4x4 World;
    public float Width, Curvature;
}

// Gaze focus (Erik 10-18: "with three monitors the overlay is uncomfortably wide: while I look at the middle monitor the side ones
// should be small, and as I look at one of them the others shrink and that one is in focus"). In a row of two or more monitors
// (DesktopLayout's groups) the one you look at is drawn at its saved width; the others at a part of their width (Desktops > Others),
// each hugging the focused one along the row's arc with the row's own gaps, their centres on the row's line - so the row as a whole
// is narrower. A per-frame draw override exactly like theater mode's: nothing here is saved. While a hand holds a monitor every row
// goes back to its saved sizes, following the hand (the grab, the snap into a row and the join all work on the saved layout, so that
// is what you see while you arrange things), and the focus comes back after. Recenter / Reflow move the saved row and the override
// follows. Theater on: the focus is held where it is (no switching) and goes on after. A monitor that leaves its row (Separate,
// B / Y pulling it out, a profile switch) eases to its saved place by itself: nothing jumps.
//
// Which one: the head's forward ray against the panels as they are drawn (the laser's hit math, LocalRaycast - tilted rows and lying
// back work), carried a moment ahead along the head's turn, with hysteresis so it never flaps: the look must land inside a neighbour
// (past 5 % of its drawn width from the edge it shares with the focused one) and stay there 0.12 s; a laser on a monitor focuses it
// after 0.15 s (you are about to
// click there). Looking at nothing (the watch, the keyboard, the room) keeps the focus - typing never changes it. At start the
// focus is the primary monitor (else the middle of the row): the row's home.
//
// Where: the row's drawn state is one chain - the anchor's (the focused monitor's) drawn pose and every member's drawn width, the
// rest placed edge to edge from it along the row's arc (DesktopLayout.Neighbor) - so the row is seamless on every frame of a change.
// The home monitor, focused, sits at its saved place: the row never drifts from the saved layout, and looking back at the middle
// brings the row back to where it was. Any other monitor grows where it is drawn, about its own centre, so what you look at stays
// under your gaze (the chain hangs from it there, the rest tuck in beside it). A switch eases the anchor's pose and every width over
// 0.3 s (Motion reduced: a short linear move, off: at once), and a switch mid-way starts from what is drawn then: nothing jumps.
// The anchor's poses are kept relative to its saved pose, so a saved layout that moves meanwhile (a grab, Recenter) carries the
// drawn row along.
//
// Cost: at rest a few compares per monitor (has the saved layout changed? has a row changed?) and one ray test per row member; the
// views change only while something moves, so the overlays get no new transform (VrOverlay skips equal values anyway). Nothing is
// allocated per frame: the rows are rebuilt only when a monitor joins or leaves a row or is turned on / off.
public sealed class GazeFocus
{
    // Erik 10-18: "it reacts slowly to the look" - the look had to get 15 % into the small neighbour and stay 0.3 s, then 0.35 s of
    // easing: now 5 % and 0.12 s, the look carried a moment ahead along the head's turn (Lead), 0.3 s of easing
    public const float Dwell = 0.12f, LaserDwell = 0.15f;        // seconds a look / a laser must stay on a neighbour
    public const float Edge = 0.05f;                             // the part of a neighbour's drawn width next to the focused one that does not count
    public const float EaseTime = 0.3f, ShortTime = 0.2f;        // seconds: Motion full (eased) / reduced (linear)
    public const float GrabEaseTime = 0.35f;                     // a hand takes a monitor: the row back to its saved sizes, unhurried (it moves under the hand)
    public const float Lead = 0.1f, MaxLead = 10 * MathF.PI / 180;     // the look: where the head's turn takes it in 0.1 s, at most 10 deg ahead
    public const float OthersMin = 0.4f, OthersMax = 0.75f, OthersDefault = 0.55f;

    sealed class Member
    {
        public DesktopPanel Panel;
        public Row Row;
        public int Index;                             // its GroupIndex when the row was built
        public readonly FocusView View = new();
        // the saved layout as last seen: a move arrives as a new M array (Placement.SetWorld, Reflow, Recenter all make one)
        public float[] M;
        public float Width, Curvature;
        public Matrix4x4 Saved;
        public float Gap;                             // along the arc after the member before it, in the saved layout (0 after a Reflow)
        public float FromWidth, ToWidth, Drawn;       // the width's animation and what is drawn now
    }

    sealed class Row
    {
        public string Group;
        public readonly List<Member> Members = new();
        public int Focused, Anchor;                   // the chain hangs from the anchor: the focused one, the held one during a grab
        public int Default;                           // the row's home: the primary (else the middle) monitor - focused, it sits at its saved place
        public float Radius;
        public bool Plain;                            // the target is the saved layout itself (a grab, the feature turned off, a monitor leaving)
        public bool Grab;
        public bool Leaving;                          // one monitor that left its row: eases to its saved place by itself, then it is let go
        // the anchor's drawn pose eases from (FromQ, FromT) to (ToQ, ToT), both relative to its saved pose, T = 0 .. 1: the home
        // monitor, a grab, the feature off go to the saved pose itself; another focused monitor holds the pose it had (Hold)
        public Quaternion FromQ = Quaternion.Identity, ToQ = Quaternion.Identity;
        public Vector3 FromT, ToT;
        public bool Hold;
        public float T = 1;
        public bool Settled;                          // drawn = the target: nothing to compute
        public int Looked = -1, Pointed = -1;         // the neighbour the look is clearly inside / a laser is on, since when
        public double LookSince, PointSince;
    }

    readonly List<DesktopPanel> _panels;
    readonly List<Row> _rows = new();
    int _memberCount;                                 // members of the live rows (not the leaving ones)
    public int Rebuilds { get; private set; }         // the tests: rows made again (want: only when a monitor joins / leaves)
    public int Switches { get; private set; }

    public GazeFocus(List<DesktopPanel> panels) { _panels = panels; }

    public bool Active => _rows.Count > 0;

    // the focused monitor of p's row (null: p is not in a focused row) - the tests and the log
    public DesktopPanel FocusedOf(DesktopPanel p)
    {
        var m = Find(p);
        return m == null || m.Row.Leaving ? null : m.Row.Members[m.Row.Focused].Panel;
    }

    // Once a frame, after the lasers and before the monitors draw. cfg null or off: the rows ease back to their saved layout and let go.
    // theater: a screen plays or is on its way in / out - no switching meanwhile; hold: a hand holds a mouse button on a monitor (a drag
    // across screens must not have the row move under the beam). laserA / B: what the two lasers point at.
    public void Update(VrSession vr, double now, float dt, FocusConfig cfg, bool theater, bool hold, IPointerTarget laserA, IPointerTarget laserB)
    {
        bool on = cfg is { Enabled: true };
        if (!on && _rows.Count == 0) return;                                     // the usual frame with the feature off: nothing
        float others = Math.Clamp(cfg?.Others ?? OthersDefault, OthersMin, OthersMax);
        bool othersChanged = others != _others;
        _others = others;
        if (on && Stale()) Rebuild();                                          // off: the rows only ease back to the saved layout and let go
        // a hand holds a monitor (any of them, not the theater screen): every row at its saved sizes meanwhile - the snap into a row
        // and the join go by the saved layout, so that is what you see while you arrange things
        bool grab = false;
        foreach (var p in _panels) if (p.Grabbing) { grab = true; break; }
        LookAhead(vr, dt);
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            var row = _rows[i];
            if (row.Leaving && !IsPlaced(row.Members[0].Panel)) { Release(row); _rows.RemoveAt(i); continue; }     // put on a hand meanwhile
            Step(row, vr, now, dt, on, grab, othersChanged, theater || hold, laserA as DesktopPanel, laserB as DesktopPanel);
            if ((!on || row.Leaving) && row.T >= 1)                              // back at its saved layout: let go of the row
            {
                Release(row);
                _rows.RemoveAt(i);
                if (!row.Leaving) _memberCount -= row.Members.Count;
            }
        }
    }

    // ---- the rows ----

    static bool IsPlaced(DesktopPanel p) => Placement.IsWorld(p.Cfg.Place) && p.Cfg.Place.M != null;
    static bool IsMember(DesktopPanel p) => p.Cfg.Enabled && p.Cfg.Group != null && IsPlaced(p);

    int Siblings(DesktopPanel p)
    {
        int n = 0;
        foreach (var o in _panels) if (IsMember(o) && o.Cfg.Group == p.Cfg.Group) n++;
        return n;
    }

    Member Find(DesktopPanel p)
    {
        foreach (var r in _rows)
            foreach (var m in r.Members)
                if (m.Panel == p) return m;
        return null;
    }

    // has a monitor joined or left a row, or a row its order? (a lone grouped monitor is no row; one on its way out is no member)
    bool Stale()
    {
        int inRows = 0;
        foreach (var p in _panels)
        {
            var m = Find(p);
            bool live = m != null && !m.Row.Leaving;
            bool want = IsMember(p) && Siblings(p) >= 2;
            if (!want) { if (live) return true; continue; }
            inRows++;
            if (!live || m.Row.Group != p.Cfg.Group || m.Index != p.Cfg.GroupIndex) return true;
        }
        return inRows != _memberCount;
    }

    // The rows as they are now. A row that was there keeps what it drew (the views go on from there, the focus stays on the same
    // monitor); a monitor new to a row starts where it is saved; one that left a row eases to its saved place by itself (a Leaving row
    // of one) and is let go there. Rare: a monitor joining / leaving a row, a panel rebuild.
    void Rebuild()
    {
        Rebuilds++;
        var fresh = new List<Row>();
        foreach (var p in _panels)
        {
            if (!IsMember(p) || Siblings(p) < 2) continue;
            Row row = null;
            foreach (var r in fresh) if (r.Group == p.Cfg.Group) { row = r; break; }
            if (row == null) fresh.Add(row = new Row { Group = p.Cfg.Group, Focused = -1 });      // -1: not kept from before
            var old = Find(p);
            // one of this row before, or on its way out of a row: it goes on from where it is drawn
            var m = old != null && (old.Row.Group == row.Group || old.Row.Leaving) ? old : new Member { Panel = p };
            m.Row = row;
            m.Index = p.Cfg.GroupIndex;
            int at = row.Members.Count;
            while (at > 0 && row.Members[at - 1].Index > m.Index) at--;
            row.Members.Insert(at, m);
        }
        foreach (var r in _rows)
        {
            if (r.Leaving)
            {
                if (r.Members[0].Row == r) fresh.Add(r);                       // still on its way (not back in a row)
                continue;
            }
            // the same row again: the focus stays on its monitor if that is still in it, the animation goes on from what is drawn
            Row again = null;
            foreach (var f in fresh) if (!f.Leaving && f.Group == r.Group) { again = f; break; }
            if (again != null)
            {
                var focused = r.Members[r.Focused].Panel;
                again.Focused = -1;
                for (int i = 0; i < again.Members.Count; i++) if (again.Members[i].Panel == focused) again.Focused = i;
            }
            // the ones not in a row any more: each eases from where it is drawn to its saved place, alone (Separate, B / Y pulling
            // one out, a profile switch - no jump); one not in the world any more, or a panel gone (monitors changed), is let go at once
            foreach (var m in r.Members)
            {
                if (m.Row != r) continue;
                if (!IsPlaced(m.Panel) || !_panels.Contains(m.Panel)) { if (m.Panel.Focus == m.View) m.Panel.Focus = null; continue; }
                var alone = new Row { Group = r.Group, Leaving = true, Plain = true };
                m.Row = alone;
                alone.Members.Add(m);
                fresh.Add(alone);
            }
        }
        _rows.Clear();
        _memberCount = 0;
        foreach (var row in fresh)
        {
            _rows.Add(row);
            if (!row.Leaving) _memberCount += row.Members.Count;
            row.Default = DefaultFocus(row);
            if (row.Focused < 0) row.Focused = row.Default;
            row.Anchor = row.Focused;
            row.Looked = row.Pointed = -1;
            foreach (var m in row.Members)
            {
                m.M = null;                                   // read the saved layout again
                m.Panel.Focus = m.View;
            }
            Refresh(row);
            foreach (var m in row.Members)
                if (m.Drawn <= 0)                             // new to the row (or just made): drawn where it is saved
                {
                    m.Drawn = m.Width;
                    m.View.World = m.Saved; m.View.Width = m.Width; m.View.Curvature = m.Curvature;
                }
            Targets(row);
            Retarget(row);                                    // a new row eases from its saved layout into the focus layout
        }
    }

    static int DefaultFocus(Row row)
    {
        for (int i = 0; i < row.Members.Count; i++) if (row.Members[i].Panel.Mon.Primary) return i;
        return row.Members.Count / 2;
    }

    void Release(Row row)
    {
        foreach (var m in row.Members) if (m.Panel.Focus == m.View) m.Panel.Focus = null;
    }

    // ---- the saved layout as seen now ----

    // true when a member's saved place, width or bend changed since the last frame (a grab's Reflow does it every frame)
    bool Refresh(Row row)
    {
        bool changed = false;
        foreach (var m in row.Members)
        {
            var c = m.Panel.Cfg;
            if (ReferenceEquals(c.Place.M, m.M) && c.Width == m.Width && c.Curvature == m.Curvature) continue;
            m.M = c.Place.M;
            m.Saved = MathUtil.FromArray(m.M);
            m.Width = c.Width;
            m.Curvature = c.Curvature;
            changed = true;
        }
        if (!changed) return false;
        var a = row.Members[row.Anchor];
        row.Radius = DesktopLayout.Radius(a.Panel.Cfg);
        // the row's own gaps: where each member sits along the arc past "edge to edge" with the one before it (0 after a Reflow)
        for (int i = 1; i < row.Members.Count; i++)
        {
            var prev = row.Members[i - 1];
            var m = row.Members[i];
            var ideal = DesktopLayout.Neighbor(prev.Saved, prev.Width, row.Radius, m.Width, 1);
            float gap = Vector3.Dot(m.Saved.Translation - ideal.Translation, MathUtil.Right(ideal));
            m.Gap = MathF.Abs(gap) < 0.5f ? gap : 0;
        }
        return true;
    }

    void Targets(Row row)
    {
        for (int i = 0; i < row.Members.Count; i++)
        {
            var m = row.Members[i];
            m.ToWidth = row.Plain || i == row.Focused ? m.Width : m.Width * _others;
        }
    }

    // a new animation from what is drawn now: the anchor's drawn pose relative to its saved one, every width as drawn. The home monitor
    // (and a grab, the feature off, one leaving) goes to its saved pose - the row never drifts; another focused monitor holds the pose it
    // has, so what you look at stays under your gaze while it grows
    void Retarget(Row row)
    {
        var a = row.Members[row.Anchor];
        var rel = a.View.World * MathUtil.Invert(a.Saved);
        row.FromQ = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rel));
        row.FromT = rel.Translation;
        row.Hold = !row.Plain && row.Focused != row.Default;
        row.ToQ = row.Hold ? row.FromQ : Quaternion.Identity;
        row.ToT = row.Hold ? row.FromT : Vector3.Zero;
        foreach (var m in row.Members) m.FromWidth = m.Drawn;
        row.T = Motion.Any ? 0 : 1;                           // Look > Motion off: at once
        row.Settled = false;
    }

    static float Lerp(float a, float b, float e) => e >= 1 ? b : a + (b - a) * e;

    // ---- a frame of one row ----

    void Step(Row row, VrSession vr, double now, float dt, bool on, bool grab, bool othersChanged, bool held, DesktopPanel laserA, DesktopPanel laserB)
    {
        bool moved = Refresh(row);                            // first: a retarget below is relative to the saved pose as it is now
        if (!row.Leaving)
        {
            // a hand holds a monitor: the row goes back to its saved sizes (the grab edits the saved layout, Reflow and all), hanging
            // from the held one if it is of this row; let go: the focus comes back
            bool plain = grab || !on;
            if (grab != row.Grab || plain != row.Plain)
            {
                row.Grab = grab;
                row.Plain = plain;
                row.Anchor = row.Focused;
                if (grab) for (int i = 0; i < row.Members.Count; i++) if (row.Members[i].Panel.Grabbing) row.Anchor = i;
                row.Looked = row.Pointed = -1;
                Targets(row);
                Retarget(row);
            }
            // ---- which one (not while a monitor is held, a button is held, or in theater) ----
            if (!grab && on && !held) Look(row, vr, now, laserA, laserB);
            else row.Looked = row.Pointed = -1;
        }
        if (moved || othersChanged) { Targets(row); row.Settled = false; }     // the saved layout moved / the slider: the override follows

        // ---- the drawn chain ----
        if (row.T < 1)
        {
            row.T = Math.Min(1, row.T + dt / (Motion.Full ? (grab ? GrabEaseTime : EaseTime) : ShortTime));
            row.Settled = false;
        }
        if (row.Settled) return;
        // eased out (it moves the frame the switch starts - an ease-in-out sat still for the first frames; Erik 10-18: "very responsive")
        float e = row.T >= 1 ? 1 : Motion.Full ? 1 - (1 - row.T) * (1 - row.T) : row.T;
        Chain(row, e);
        row.Settled = row.T >= 1;
    }

    float _others = OthersDefault;

    // where the look is going: the head's forward carried Lead seconds ahead along its (smoothed) turn, at most MaxLead - a deliberate
    // turn to a side monitor counts on its way there; held still it is the look itself; a quick sweep past is gone before the dwell
    Vector3 _lookDir = -Vector3.UnitZ, _prevFwd, _turn;
    bool _prevOk;

    void LookAhead(VrSession vr, float dt)
    {
        if (!vr.HmdValid) { _prevOk = false; return; }
        if (vr.EyesValid) { _lookDir = vr.Gaze; _prevOk = false; return; }       // the eyes: where you look already (Sys/EyeGaze.cs)
        var f = MathUtil.Forward(vr.Hmd);
        if (_prevOk && dt > 1e-4f) _turn = Vector3.Lerp(_turn, (f - _prevFwd) / dt, 0.35f);
        else _turn = Vector3.Zero;
        _prevFwd = f;
        _prevOk = true;
        var lead = _turn * Lead;
        float len = lead.Length();
        if (len > MaxLead) lead *= MaxLead / len;
        _lookDir = Vector3.Normalize(f + lead);
    }

    void Look(Row row, VrSession vr, double now, DesktopPanel laserA, DesktopPanel laserB)
    {
        int hit = -1;
        float u = 0, best = float.MaxValue;
        if (vr.HmdValid)
        {
            var o = vr.EyesValid ? vr.GazeOrigin : vr.Hmd.Translation;
            var d = _lookDir;
            for (int i = 0; i < row.Members.Count; i++)
            {
                var m = row.Members[i];
                var ts = m.Panel.TextureSize;
                if (LocalRaycast.Intersect(m.View.World, m.View.Width, m.View.Curvature, ts.X, ts.Y, o, d, out float dist, out var uv, out _, out _)
                    && dist < best)
                {
                    best = dist; hit = i; u = uv.X;
                }
            }
        }
        if (hit < 0 || hit == row.Focused) row.Looked = -1;              // the focused one, or nothing: the focus stays
        else
        {
            // inside the neighbour: past 5 % of its drawn width from the edge on the focused one's side (Edge)
            bool inside = hit > row.Focused ? u > Edge : u < 1 - Edge;
            if (!inside) row.Looked = -1;
            else if (row.Looked != hit) { row.Looked = hit; row.LookSince = now; }
            else if (now - row.LookSince >= Dwell) { Switch(row, hit); return; }
        }
        // a laser on another monitor of the row: a short dwell, then that one (you are about to click there)
        int pointed = -1;
        for (int i = 0; i < row.Members.Count && pointed < 0; i++)
        {
            var p = row.Members[i].Panel;
            if (i != row.Focused && (p == laserA || p == laserB)) pointed = i;
        }
        if (pointed < 0) row.Pointed = -1;
        else if (row.Pointed != pointed) { row.Pointed = pointed; row.PointSince = now; }
        else if (now - row.PointSince >= LaserDwell) Switch(row, pointed);
    }

    void Switch(Row row, int to)
    {
        Switches++;
        row.Focused = to;
        row.Anchor = to;
        row.Looked = row.Pointed = -1;
        Targets(row);
        Retarget(row);
    }

    // The row as drawn at e (0 = the animation's start, 1 = the target): the anchor's pose, then the others edge to edge from it along
    // the arc, each at its width now. The focused monitor at its full width keeps its saved bend exactly.
    void Chain(Row row, float e)
    {
        var a = row.Members[row.Anchor];
        var A = a.Saved;
        if (e < 1 || row.Hold)
        {
            var q = e >= 1 ? row.ToQ : Quaternion.Slerp(row.FromQ, row.ToQ, e);
            var t = e >= 1 ? row.ToT : Vector3.Lerp(row.FromT, row.ToT, e);
            A = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t) * a.Saved;
        }
        foreach (var m in row.Members) m.Drawn = Lerp(m.FromWidth, m.ToWidth, e);
        float r = row.Radius;
        Set(a.View, A, a.Drawn, a.Drawn == a.Width ? a.Curvature : DesktopLayout.CurvatureFor(a.Drawn, r));
        var prevW = A;
        float prevWidth = a.Drawn;
        for (int i = row.Anchor + 1; i < row.Members.Count; i++)
        {
            var m = row.Members[i];
            var w = DesktopLayout.Neighbor(prevW, prevWidth, r, m.Drawn, 1, m.Gap);
            Set(m.View, w, m.Drawn, DesktopLayout.CurvatureFor(m.Drawn, r));
            prevW = w; prevWidth = m.Drawn;
        }
        prevW = A; prevWidth = a.Drawn;
        for (int i = row.Anchor - 1; i >= 0; i--)
        {
            var m = row.Members[i];
            var w = DesktopLayout.Neighbor(prevW, prevWidth, r, m.Drawn, -1, row.Members[i + 1].Gap);
            Set(m.View, w, m.Drawn, DesktopLayout.CurvatureFor(m.Drawn, r));
            prevW = w; prevWidth = m.Drawn;
        }
    }

    static void Set(FocusView v, in Matrix4x4 world, float width, float curvature)
    {
        v.World = world; v.Width = width; v.Curvature = curvature;
    }
}
