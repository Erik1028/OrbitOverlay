using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// The SlimeVR widget's opened view, more of it (Erik 10-24: "a slimevr widgetbe több funkció" - lying twists his hip and legs; the
// LEFT leg, thigh and shin, turns 20-40 deg against the pelvis). Next to the Trackers list a Body side (the side picked stays while the
// app runs, like the recap's Today): SlimeVR's own body model as a small stick figure, live (the bones at 20 a second while it shows -
// Sys/SlimePose.cs), front (the body face-on, always upright) and side (the world's up: lying lies), each bone in its tracker's state
// (no tracker on it: inferred, faint and thinner); how far each leg is turned against the pelvis ("32° out", the shin and the sideways
// lean under it - amber from 15°, red from 30°); a tap on a limb (or a chip: All, Legs, Left leg, Right leg, Upper body) picks which
// trackers its Yaw / Full reset resets - two taps, as the other resets ("Reset left leg?").
// The guide (in the list's place, whichever side shows): a mounting reset asked for or running (from anywhere) shows the pose to take -
// SlimeVR's step (Erik's: "Take one step forward, then stand still", the server's phases big) or its ski pose (the countdown big) - as a
// drawing and a line, then "Done"; a reset the server would not take yet (v22 ignores a mounting reset without a full one in the last
// 2 minutes, a yaw reset without one since it started, without a word) asks for the full reset first ("Full reset first: stand straight,
// look ahead", Full reset lit), then lights the waiting one. Nothing is sent by itself: every reset still takes its two taps.
// Modern: Track panels, round bones, the readings big in a column; Cyber: plates with ticks, cyan bones, HUD chips and segment meters;
// the retro styles: a sunken well with the two figures in the style's ink, Twist and Reset group boxes with option buttons, a latched
// Body button beside OK (the recap's Today); the Terminal: WristSlimeVr.Term.cs. Nothing allocated a frame: the degrees and the times
// come from tables made when first drawn, the points live in fixed arrays.
public sealed partial class WristPanel
{
    // the app: send this reset of these body parts (kind 0 yaw / 1 full, group: SlimeGroups); null in the tools: only noted
    public Func<int, int, bool> OnSlimeResetParts;
    // the app (v22): stop the reset running; null in the tools
    public Func<bool> OnSlimeCancel;
    public int LastSlimeGroup { get; private set; } = -1;
    public int SlimeCancels { get; private set; }

    bool _svBody;                                // the opened view shows the Body side
    int _svGroup;                                // the trackers the Body side's resets are for (SlimeGroups)
    int _svArmGroup;                             // the group of the reset asking

    // the app asks for the bones only while this is true (OrbitApp.SlimeVr.cs)
    public bool SlimeBodyOn => _svBody && _open != null && !_closing && DetailIs("slimevr");

    void SvSetBody(bool on)
    {
        if (_svBody == on) return;
        _svBody = on;
        _svArmed = -1;
        _dirty = true;
    }

    void SvSetGroup(int g)
    {
        if (_svGroup == g) return;
        _svGroup = g;
        _svArmed = -1;                           // (a reset asking for the other group asks again)
        _dirty = true;
    }

    // a new snapshot (the countdown, the guide) or, while the Body side shows, a new pose: drawn as they come (up to 20 a second)
    int _svSnapV = -1, _svPoseV = -1;
    bool SlimeChanged(PanelData d)
    {
        int sv = d.SlimeVr?.Version ?? -1, pv = d.SlimePose?.Version ?? -1;
        bool ch = false;
        if (sv != _svSnapV) { _svSnapV = sv; ch = DetailIs("slimevr"); }
        if (pv != _svPoseV) { _svPoseV = pv; ch |= SlimeBodyOn; }
        return ch;
    }

    // the pose to draw: none sent, none since a moment (the bones stopped: "No pose yet", never a frozen figure)
    static SlimePose SvPose(PanelData d) => d.SlimePose is { Has: true } p && Clock.Now - p.At < 1.5 ? p : null;

    // ================= the guide =================

    enum Guide { None, FullFirst, Ready, Running, Done, Failed, Manual, Blocked }
    int _svFirst = -1;                           // the reset waiting for a full one (0 yaw, 2 mounting; -1: none)
    double _svFirstAt, _svNextBy, _svManualAt = -10, _svBlockedAt = -10;
    int _svNext = -1, _svBlocked;                // the reset lit after the full one (until _svNextBy); why mounting cannot (2, 3)

    void SvGuideReset() { _svFirst = _svNext = -1; _svManualAt = _svBlockedAt = -10; }

    // what the guide shows now; kind: the reset it is about (the full-first step: the one waiting)
    Guide SvGuideNow(PanelData d, out int kind)
    {
        kind = 2;
        var s = d.SlimeVr;
        if (s == null || !SvCan(d)) return Guide.None;
        double now = Clock.Now;
        // a mounting reset the server runs (Orbit's or one started anywhere else), done or failed a moment ago
        if (s.ResetKind == 2)
        {
            if (s.ResetState == 0 && now - s.ResetAt < 20) return Guide.Running;
            if (s.ResetState == 1 && now - s.ResetAt < 2.5) return Guide.Done;
            if (s.ResetState == 2 && now - s.ResetAt < 4) return Guide.Failed;
        }
        if (now - _svManualAt < 4) return Guide.Manual;
        if (now - _svBlockedAt < 4) return Guide.Blocked;
        if (_svFirst >= 0)
        {
            // the full reset done after the tap: the waiting one next, lit for its 2 minutes (a yaw reset: 1 minute)
            if (s.ResetKind == 1 && s.ResetState == 1 && s.ResetAt > _svFirstAt)
            {
                (_svNext, _svNextBy) = (_svFirst, s.ResetAt + (_svFirst == 2 ? 120 : 60));
                _svFirst = -1;
            }
            else if (now - _svFirstAt < 90) { kind = _svFirst; return Guide.FullFirst; }
            else _svFirst = -1;
        }
        if (SvArmed(2)) return Guide.Ready;
        if (_svNext == 2 && now < _svNextBy) return Guide.Ready;
        return Guide.None;
    }

    // a reset button lit by the guide: Full while the full one comes first, the waiting one after it
    bool SvGuideLit(int k, PanelData d)
    {
        var g = SvGuideNow(d, out _);
        return (g == Guide.FullFirst && k == 1) || (k == _svNext && Clock.Now < _svNextBy);
    }

    // a first tap the server would not take now (in the opened view): mounting with v22's method manual or not set says so; mounting
    // without the headset's position / trackers says why; mounting (yaw) without the full reset it needs asks for that first. true: taken
    bool SvGuideTap(int k, PanelData d)
    {
        var s = d.SlimeVr;
        if (s == null || SvArmed(k)) return false;
        double now = Clock.Now;
        if (k == 2 && s.Dialect == SolarXr.Dialect.V22 && s.MountMethod is 0 or 1) { _svManualAt = now; return true; }
        if (k == 2 && s.MountGuard is 2 or 3) { (_svBlockedAt, _svBlocked) = (now, s.MountGuard); return true; }
        if ((k == 2 && s.MountGuard == 1) || (k == 0 && s.YawGuard == 1)) { (_svFirst, _svFirstAt, _svNext) = (k, now, -1); return true; }
        return false;
    }

    // ---- the guide's words ----

    const string GuideFullFirst = "Full reset first", GuideStand = "Stand straight, look ahead", GuideStep = "Take one step forward, then stand still",
        GuidePose = "Ski pose: knees bent, lean forward, arms bent", GuideToes = "Stay up on your toes", GuideTapAgain = "Tap Mounting reset again to start",
        GuideFullThen = "Full reset twice, then the mounting reset", GuideNow = "Full reset done - now the mounting reset",
        GuideYawThen = "Full reset twice, then the yaw reset", GuideManual = "Mounting is manual in SlimeVR", GuideHead = "SlimeVR needs the headset's position",
        GuideCancelled = "Cancelled", GuideFailed = "Mounting reset failed", GuideCount = "SlimeVR counts down";
    static readonly string[] GuidePhases =
    {
        "Waiting for your step", "Waiting for your step", "Recording", "Processing", "Done",
        "Couldn't detect any movement. Try again.", "Tracker acceleration was invalid. Try again.", "Timed out waiting for movement. Try again.",
    };

    bool SvStep(PanelData d) => d.SlimeVr is { Dialect: SolarXr.Dialect.V22, MountMethod: 3 };

    // the guide's parts now: its title, its line, the big word or number (null: none), its colour level (0 normal, 1 done, 2 trouble),
    // the note under it; frac: the countdown's part gone (-1: none)
    (string title, string line, string big, int lv, string note, float frac) SvGuideText(PanelData d, Guide g, int kind)
    {
        var s = d.SlimeVr;
        bool step = SvStep(d);
        string how = step ? GuideStep : GuidePose, mount = SvResetNames[2];
        string toes = !step && s.MountFeet ? GuideToes : null;
        switch (g)
        {
            case Guide.FullFirst:
            {
                // the full reset's own countdown big while it runs
                string big = null;
                float frac = -1;
                if (s.ResetKind == 1 && s.ResetState == 0 && SvLeft(s) is var left and > 0) (big, frac) = (SvDigit(left), SvFrac(s));
                return (GuideFullFirst, GuideStand, big, 0, kind == 0 ? GuideYawThen : GuideFullThen, frac);
            }
            case Guide.Ready: return (mount, how, null, 0, SvArmed(2) ? GuideTapAgain : _svNext == 2 ? GuideNow : toes, -1);
            case Guide.Running:
            {
                if (step) return (mount, how, GuidePhases[Math.Clamp(s.ResetPhase, 0, 4)], s.ResetPhase == 4 ? 1 : 0, null, Math.Clamp(s.ResetPhase, 0, 4) / 4f);
                double left = SvLeft(s);
                return (mount, how, left > 0 ? SvDigit(left) : null, 0, toes ?? GuideCount, SvFrac(s));
            }
            case Guide.Done: return (mount, how, "Done", 1, null, 1);
            case Guide.Failed:
                return (mount, how, s.ResetPhase >= 5 ? GuidePhases[Math.Min(s.ResetPhase, 7)] : s.ResetFailed ? GuideFailed : GuideCancelled, 2, null, -1);
            case Guide.Manual: return (mount, GuideManual, null, 2, null, -1);
            case Guide.Blocked: return (mount, _svBlocked == 3 ? "No trackers" : GuideHead, null, 2, null, -1);
        }
        return (null, null, null, 0, null, -1);
    }

    // s left of the server's countdown, the part of it gone
    static double SvLeft(SlimeVrLink.Snapshot s) => s.ResetLeftMs / 1000.0 - (Clock.Now - s.ResetAt);
    static float SvFrac(SlimeVrLink.Snapshot s)
    {
        double total = s.ResetLeftMs / 1000.0;
        return total <= 0 ? 1 : (float)Math.Clamp(1 - SvLeft(s) / Math.Max(total, 3), 0, 1);
    }

    static readonly string[] SvDigits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
    static string SvDigit(double left) => SvDigits[Math.Clamp((int)Math.Ceiling(left), 0, 9)];

    // which drawing: 0 standing straight (the full reset), 1 the step, 2 the ski pose, 3 on its toes
    int SvGuideFigure(PanelData d, Guide g) => g == Guide.FullFirst ? 0 : SvStep(d) ? 1 : d.SlimeVr?.MountFeet == true ? 3 : 2;

    // the reset to cancel (v22, a mounting reset running): Cancel
    bool SvCanCancel(PanelData d, Guide g) => g == Guide.Running && d.SlimeVr?.Dialect == SolarXr.Dialect.V22;

    void SvCancel()
    {
        SlimeCancels++;
        OnSlimeCancel?.Invoke();
        _dirty = true;
    }

    // ================= the texts: degrees, times, groups =================

    // "32°", "32° out", "6° sideways"... per language, per whole degree (0-180), made when first drawn (upper: in capitals)
    readonly Dictionary<string, string[]> _svDegs = new();
    int _svDegLang = -1;
    string SvDeg(string format, float deg, bool upper = false)
    {
        if (_svDegLang != Lang.Version) { _svDegs.Clear(); _svDegLang = Lang.Version; }
        int n = float.IsNaN(deg) ? 0 : Math.Clamp((int)MathF.Round(MathF.Abs(deg)), 0, 180);
        if (!_svDegs.TryGetValue(format, out var a)) _svDegs[format] = a = new string[2 * 181];
        int i = upper ? 181 + n : n;
        return a[i] ??= upper ? Lang.F(format, n).ToUpper(CultureInfo.CurrentCulture) : Lang.F(format, n);
    }

    // a leg's twist: "32° out" / "4° in" (positive turns the toes to the left: out for the left leg), "0°"
    string SvTwist(bool left, float t, bool upper = false)
    {
        if (float.IsNaN(t)) return "–";
        if (MathF.Abs(t) < 0.5f) return SvDeg("{0}°", 0, upper);
        bool @out = left ? t > 0 : t < 0;
        return SvDeg(@out ? "{0}° out" : "{0}° in", t, upper);
    }

    // a level by a leg's worst turn: 0 fine, 1 from 15°, 2 from 30°
    static int SvTurnLv(float worst) => float.IsNaN(worst) ? 0 : worst >= 30 ? 2 : worst >= 15 ? 1 : 0;

    static readonly string[] SvGroupNames = SlimeGroups.Names;
    static readonly string[] SvGroupIds = { "wsvg0", "wsvg1", "wsvg2", "wsvg3", "wsvg4" };
    static readonly string[] SvLimbIds = { "wsvlimb0", "wsvlimb1", "wsvlimb2", "wsvlimb3", "wsvlimb4" };
    static readonly string[] SvBodyIds = { "wsvbr0", "wsvbr1" };
    // a reset asking for a group ("Reset left leg?"), the status line's ("Click {0} again to reset the left leg")
    static readonly string[] SvAskGroup = { "Reset?", "Reset legs?", "Reset left leg?", "Reset right leg?", "Reset upper body?" };
    static readonly string[] SvClickGroup =
    {
        "Click {0} again to reset", "Click {0} again to reset the legs", "Click {0} again to reset the left leg",
        "Click {0} again to reset the right leg", "Click {0} again to reset the upper body",
    };

    // "Yaw reset 12 min ago" (and " (left leg)" for a part of the body), made once per minute; null: none seen
    readonly Dictionary<long, string> _svAgo = new();
    int _svAgoLang = -1;
    string SvLast(PanelData d, bool upper = false)
    {
        var s = d.SlimeVr;
        if (s == null || s.LastKind is < 0 or > 2 || s.State == SlimeVrLink.State.NotRunning) return null;
        double m = Math.Max(0, (Clock.Now - s.LastAt) / 60);
        int bucket = m < 1 ? 0 : m < 60 ? (int)m : 1000 + (int)(m / 60);
        int g = SlimeGroups.Of(s.LastParts);
        long key = (((long)s.LastKind * 10 + g) * 100000 + bucket) * 2 + (upper ? 1 : 0);
        if (_svAgoLang != Lang.Version) { _svAgo.Clear(); _svAgoLang = Lang.Version; }
        if (_svAgo.TryGetValue(key, out var t)) return t;
        var culture = CultureInfo.CurrentCulture;
        string ago = bucket == 0 ? Lang.T("just now") : bucket < 1000 ? Lang.F("{0} min ago", bucket) : Lang.F("{0} h ago", bucket - 1000);
        t = Lang.T(SvResetNames[s.LastKind]) + " " + ago;
        if (g != SlimeGroups.All) t += " (" + Lang.T(SvGroupNames[g]).ToLower(culture) + ")";
        if (upper) t = t.ToUpper(culture);
        if (_svAgo.Count > 256) _svAgo.Clear();
        return _svAgo[key] = t;
    }

    // two texts joined with " · " (the modern header), kept
    readonly Dictionary<(string, string), string> _svJoins = new();
    string SvJoin(string a, string b, string sep = " · ")
    {
        if (string.IsNullOrEmpty(b)) return a;
        if (string.IsNullOrEmpty(a)) return b;
        var key = (a, b);
        if (_svJoins.TryGetValue(key, out var j)) return j;
        if (_svJoins.Count > 128) _svJoins.Clear();
        return _svJoins[key] = Lang.T(a) + sep + Lang.T(b);
    }

    // ================= the figure =================

    // how a style draws the bones: OK / amber / red / asleep, inferred (no tracker on it), the picked group (and its halo), the joints
    struct SvInk
    {
        public uint Ok, Warn, Bad, Sleep, Faint, Sel, Halo, Head;
        public float W, WSel, WFaint;
        public int Joints;                       // 0 none, 1 discs, 2 squares
        public int Labels;                       // the twist by each knee: 0 none, 1 modern, 2 Cyber, 3 retro, 4 the Terminal
        public bool Round, Dash, Dots;           // round ends (modern); inferred dashed (Cyber) / dotted (retro)
    }

    // a tracker's state on each part (made with the rows, SvNow): -1 none (the bone is inferred), 0 OK, 1 busy / occluded, 2 lost or in
    // error, 3 asleep
    readonly sbyte[] _svPartLv = new sbyte[SlimePose.Parts];

    void SvPartLevels(SlimeVrLink.Snapshot s)
    {
        Array.Fill(_svPartLv, (sbyte)-1);
        if (s == null) return;
        foreach (var t in s.Trackers)
        {
            int i = (int)t.Part;
            if (i <= 0 || i >= SlimePose.Parts) continue;
            sbyte lv = t.Status switch
            {
                SlimeVrLink.Ok => 0, SlimeVrLink.Busy or SlimeVrLink.Occluded => 1, SlimeVrLink.Sleeping => 3, _ => 2,
            };
            if (_svPartLv[i] < 0 || (lv > _svPartLv[i] && lv != 3)) _svPartLv[i] = lv;
        }
        // a tracker on the hip bones (v21) colours its thigh's line from the pelvis; a tracker on the upper leg the hip side too
    }

    uint SvBoneInk(SlimeVrLink.Part p, in SvInk k, out bool inferred)
    {
        int lv = _svPartLv[(int)p];
        inferred = lv < 0;
        return lv switch { 0 => k.Ok, 1 => k.Warn, 2 => k.Bad, 3 => k.Sleep, _ => k.Faint };
    }

    // the projection of the figure being drawn: scale, the pelvis' place, which view
    float _svS, _svAx, _svAy;
    bool _svSideView;
    SlimePose _svProj;
    readonly Vector2[] _svA = new Vector2[SlimePose.Parts], _svB = new Vector2[SlimePose.Parts];

    Vector2 SvPt(Vector3 v)
    {
        var q = _svSideView ? _svProj.SideOf(v) : _svProj.Front(v);
        return new Vector2(_svAx + q.X * _svS, _svAy - q.Y * _svS);
    }

    // The pose in r - the front view (the body face-on, upright whatever it does) or the side view (along the floor: lying lies) - in the
    // style's ink: the lines from the joints the parts hang from first (thin), the bones, the head a ring, the picked group over the rest.
    // hits: a tap on a limb picks its group (the front view only)
    void SvFigure(RectangleF r, SlimePose p, bool side, in SvInk k, bool hits)
    {
        const float pad = 5;
        _svProj = p;
        _svSideView = side;
        if (!side)
        {
            _svS = (r.Height - 2 * pad) / MathF.Max(1, p.Height);
            _svAx = r.X + r.Width / 2;
            _svAy = r.Y + pad + p.Upper * _svS;
        }
        else
        {
            // a fixed scale that fits standing and lying; the body's middle (half way from the head's top to the heels) in the middle
            _svS = (MathF.Min(r.Width, r.Height) - 2 * pad) / MathF.Max(1, p.Height);
            var mid = p.SideOf(p.Anchor + p.U * ((p.Upper - p.Leg) / 2));
            _svAx = r.X + r.Width / 2 - mid.X * _svS;
            _svAy = r.Y + r.Height / 2 + mid.Y * _svS;
        }
        var drawn = SlimePose.Drawn;
        for (int j = 0; j < drawn.Length; j++)
        {
            int i = (int)drawn[j];
            if (!p.B[i].Has) continue;
            _svA[i] = SvPt(p.B[i].Head);
            _svB[i] = SvPt(p.B[i].Tail);
        }
        _p.PushClip(Theme.Classic ? r : RectangleF.Inflate(r, 2, 2));
        // the joints' lines: where a part's head is not on the joint it hangs from (the pelvis to the thighs, v21's shoulders)
        for (int j = 0; j < drawn.Length; j++)
        {
            var part = drawn[j];
            int i = (int)part;
            if (!p.B[i].Has || !p.Joint(part, out var at)) continue;
            if (Vector3.DistanceSquared(at, p.B[i].Head) < 0.03f * 0.03f) continue;
            var a = SvPt(at);
            uint c = part is SlimeVrLink.Part.LeftThigh or SlimeVrLink.Part.RightThigh ? SvBoneInk(SlimeVrLink.Part.Hip, k, out _) : k.Faint;
            SvLine(a, _svA[i], c, k.WFaint, k, false);
        }
        bool any = _svGroup != SlimeGroups.All;
        for (int pass = 0; pass < 2; pass++)
            for (int j = 0; j < drawn.Length; j++)
            {
                var part = drawn[j];
                int i = (int)part;
                if (!p.B[i].Has) continue;
                bool sel = any && SlimeGroups.Has(_svGroup, part) && part != SlimeVrLink.Part.Hip;
                if (sel != (pass == 1)) continue;
                if (part == SlimeVrLink.Part.Head) { SvHead(p, i, sel, k); continue; }
                uint c = SvBoneInk(part, k, out bool inferred);
                if (sel)
                {
                    if (k.Halo != 0) SvLine(_svA[i], _svB[i], k.Halo, k.WSel + 6, k, false);
                    SvLine(_svA[i], _svB[i], k.Sel, k.WSel, k, false);
                }
                else SvLine(_svA[i], _svB[i], c, inferred ? k.WFaint : k.W, k, inferred);
                if (k.Joints != 0) SvJoint(_svB[i], sel ? k.Sel : c, k);
            }
        if (!side && k.Labels != 0) { SvKnee(p, true, k, r); SvKnee(p, false, k, r); }
        _p.PopClip();
        if (hits && !side) SvLimbHits(p, r);
    }

    // a leg's twist by its knee, outward of it (left of the left one), where it fits the panel
    void SvKnee(SlimePose p, bool left, in SvInk k, RectangleF r)
    {
        var thigh = left ? SlimeVrLink.Part.LeftThigh : SlimeVrLink.Part.RightThigh;
        float t = p.Twist[left ? SlimePose.LT : SlimePose.RT];
        if (float.IsNaN(t) || !p.B[(int)thigh].Has) return;
        var knee = _svB[(int)thigh];
        int lv = SvTurnLv(p.Worst(left));
        string s = k.Labels == 4 ? SvDeg("{0}", t) : SvDeg("{0}°", t);
        float w = k.Labels switch { 1 => _p.MeasureWidth(s, CapSize, FontWeight.SemiBold), 2 => CyMonoW(s, 13), 3 => _ui.Width95(s), _ => T4W(s) };
        float x = left ? knee.X - 6 - w : knee.X + 6;
        if (x < r.X || x + w > r.Right) return;
        switch (k.Labels)
        {
            case 1: TextAt(s, x, knee.Y + 5, w + 2, lv == 2 ? Bento.Bad : lv == 1 ? Bento.Warn : Sub, CapSize, FontWeight.SemiBold); break;
            case 2: CyMono(s, x, knee.Y + 5, w + 2, lv == 2 ? CyR : lv == 1 ? CyY : CyG, 13); break;
            case 3: _ui.Text95(s, new RectangleF(MathF.Floor(x), MathF.Floor(knee.Y - 6), w + 2, 13), lv == 2 ? 0xFF800000 : lv == 1 ? 0xFF808000 : Gry95, lv == 2); break;
            default: TPut(s, MathF.Floor(x), MathF.Floor(knee.Y - 3), lv > 0 ? TerminalSkin.HOT : TerminalSkin.DIM); break;
        }
    }

    static float T4W(string s) => TerminalSkin.W(s);

    void SvLine(Vector2 a, Vector2 b, uint c, float w, in SvInk k, bool inferred)
    {
        if (inferred && k.Dots) { SvDots(a, b, c); return; }
        if (inferred && k.Dash) { _p.DashedLine(a.X, a.Y, b.X, b.Y, c, w, 4, 3); return; }
        if (k.Round) _p.RoundLine(a.X, a.Y, b.X, b.Y, c, w);
        else _p.Line(a.X, a.Y, b.X, b.Y, c, w);
    }

    // a dotted line of whole pixels (the retro styles' inferred bones: 1995 had no dashed pens on a canvas like this)
    void SvDots(Vector2 a, Vector2 b, uint c)
    {
        float len = Vector2.Distance(a, b);
        int n = Math.Max(1, (int)(len / 2));
        for (int i = 0; i <= n; i++)
        {
            var q = Vector2.Lerp(a, b, i / (float)n);
            _p.Fill(new RectangleF(MathF.Floor(q.X), MathF.Floor(q.Y), 1, 1), c);
        }
    }

    void SvJoint(Vector2 at, uint c, in SvInk k)
    {
        if (k.Joints == 1) _p.Circle(at.X, at.Y, 3.5f, c);
        else _p.Fill(new RectangleF(at.X - 2.5f, at.Y - 2.5f, 5, 5), c);
    }

    // the head: a ring round its bone's top (the headset), the bone itself the neck's top
    void SvHead(SlimePose p, int i, bool sel, in SvInk k)
    {
        uint c = sel ? k.Sel : k.Head != 0 ? k.Head : SvBoneInk(SlimeVrLink.Part.Head, k, out _);
        var a = _svA[i];
        float rad = MathF.Max(3, 0.09f * _svS);
        var cen = Vector2.Lerp(a, _svB[i], 0.15f);
        if (Theme.Classic) { _p.Ring(MathF.Floor(cen.X) + 0.5f, MathF.Floor(cen.Y) + 0.5f, MathF.Round(rad), c, 1); return; }
        _p.Ring(cen.X, cen.Y, rad, c, MathF.Max(1.5f, k.W * 0.6f));
    }

    // the limbs as taps in the front view: the upper body (the spine, the arms), the pelvis (both legs), each leg - the more particular
    // later (a later hit area wins)
    void SvLimbHits(SlimePose p, RectangleF r)
    {
        float grow = Theme.Classic ? 3 : 8;
        SvHit(SlimeGroups.Upper, grow, r, HitUpper);
        SvHit(SlimeGroups.Legs, grow, r, HitPelvis);
        SvHit(SlimeGroups.LeftLeg, grow, r, HitLeft);
        SvHit(SlimeGroups.RightLeg, grow, r, HitRight);
    }

    static readonly SlimeVrLink.Part[] HitUpper =
        {
            SlimeVrLink.Part.Head, SlimeVrLink.Part.Neck, SlimeVrLink.Part.UpperChest, SlimeVrLink.Part.Chest, SlimeVrLink.Part.LowerChest, SlimeVrLink.Part.Waist,
            SlimeVrLink.Part.UpperWaist, SlimeVrLink.Part.LeftUpperArm, SlimeVrLink.Part.RightUpperArm, SlimeVrLink.Part.LeftLowerArm, SlimeVrLink.Part.RightLowerArm,
        },
        HitPelvis = { SlimeVrLink.Part.Hip, SlimeVrLink.Part.LowerWaist },
        HitLeft = { SlimeVrLink.Part.LeftThigh, SlimeVrLink.Part.LeftAnkle, SlimeVrLink.Part.LeftFoot },
        HitRight = { SlimeVrLink.Part.RightThigh, SlimeVrLink.Part.RightAnkle, SlimeVrLink.Part.RightFoot };

    void SvHit(int g, float grow, RectangleF clip, SlimeVrLink.Part[] parts)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var part in parts)
        {
            int i = (int)part;
            if (!_svProj.B[i].Has) continue;
            x0 = MathF.Min(x0, MathF.Min(_svA[i].X, _svB[i].X)); x1 = MathF.Max(x1, MathF.Max(_svA[i].X, _svB[i].X));
            y0 = MathF.Min(y0, MathF.Min(_svA[i].Y, _svB[i].Y)); y1 = MathF.Max(y1, MathF.Max(_svA[i].Y, _svB[i].Y));
        }
        if (x0 > x1) return;
        var hr = RectangleF.Intersect(RectangleF.FromLTRB(x0 - grow, y0 - grow, x1 + grow, y1 + grow), clip);
        if (hr.Width <= 0 || hr.Height <= 0) return;
        if (_ui.Clickable(SvLimbIds[g], hr, out _, out _)) SvSetGroup(_svGroup == g ? SlimeGroups.All : g);
    }

    // ---- the guide's drawings: side views in metres (x forward, y up, the feet at 0), as polylines and a head ----

    static readonly Vector2[][][] SvFigs =
    {
        // standing straight (the full reset)
        new[]
        {
            new Vector2[] { new(0, 0.95f), new(0, 1.5f) }, new Vector2[] { new(0, 0.95f), new(0, 0.5f), new(0, 0.07f), new(0.16f, 0.03f) },
            new Vector2[] { new(0, 1.44f), new(0.02f, 1.16f), new(0.04f, 0.9f) },
        },
        // the step forward, an arrow ahead
        new[]
        {
            new Vector2[] { new(0.06f, 0.93f), new(0.1f, 1.5f) },
            new Vector2[] { new(0.06f, 0.93f), new(-0.06f, 0.5f), new(-0.2f, 0.1f), new(-0.07f, 0.02f) },
            new Vector2[] { new(0.06f, 0.93f), new(0.24f, 0.52f), new(0.27f, 0.08f), new(0.42f, 0.03f) },
            new Vector2[] { new(0.09f, 1.44f), new(-0.04f, 1.2f), new(-0.08f, 0.96f) }, new Vector2[] { new(0.09f, 1.44f), new(0.2f, 1.2f), new(0.3f, 1.02f) },
            new Vector2[] { new(0.38f, 1.3f), new(0.68f, 1.3f) }, new Vector2[] { new(0.6f, 1.36f), new(0.68f, 1.3f), new(0.6f, 1.24f) },
        },
        // the ski pose: knees bent, leaning forward, arms bent
        new[]
        {
            new Vector2[] { new(-0.08f, 0.74f), new(0.18f, 1.26f) }, new Vector2[] { new(-0.08f, 0.74f), new(0.16f, 0.43f), new(0, 0.07f), new(0.16f, 0.03f) },
            new Vector2[] { new(0.15f, 1.21f), new(0.27f, 0.98f), new(0.44f, 1.08f) },
        },
        // ... up on its toes
        new[]
        {
            new Vector2[] { new(-0.08f, 0.8f), new(0.18f, 1.32f) }, new Vector2[] { new(-0.08f, 0.8f), new(0.17f, 0.49f), new(0.03f, 0.14f), new(0.13f, 0.02f) },
            new Vector2[] { new(0.15f, 1.27f), new(0.27f, 1.04f), new(0.44f, 1.14f) },
        },
    };
    static readonly Vector2[] SvFigHeads = { new(0, 1.62f), new(0.12f, 1.62f), new(0.25f, 1.38f), new(0.25f, 1.44f) };

    // a guide drawing fitted in r (its frame: -0.3 to 0.75 m across, 0 to 1.75 m up)
    void SvGuideFig(RectangleF r, int fig, uint ink, float w, bool round)
    {
        float s = MathF.Min((r.Height - 8) / 1.8f, (r.Width - 8) / 1.05f);
        float ox = r.X + r.Width / 2 - 0.22f * s, oy = r.Y + r.Height / 2 + 0.88f * s;
        foreach (var line in SvFigs[fig])
            for (int i = 1; i < line.Length; i++)
            {
                float x1 = ox + line[i - 1].X * s, y1 = oy - line[i - 1].Y * s, x2 = ox + line[i].X * s, y2 = oy - line[i].Y * s;
                if (round) _p.RoundLine(x1, y1, x2, y2, ink, w); else _p.Line(x1, y1, x2, y2, ink, w);
            }
        var h = SvFigHeads[fig];
        float hr = MathF.Max(3, 0.09f * s);
        if (Theme.Classic) _p.Ring(MathF.Floor(ox + h.X * s) + 0.5f, MathF.Floor(oy - h.Y * s) + 0.5f, MathF.Round(hr), ink, 1);
        else _p.Ring(ox + h.X * s, oy - h.Y * s, hr, ink, MathF.Max(1.5f, w * 0.6f));
    }

    // ================= Modern =================

    SvInk SvInkModern() => new()
    {
        Ok = Bento.Ok, Warn = Bento.Warn, Bad = Bento.Bad, Sleep = Bento.Faint, Faint = Bento.Faint, Sel = Bento.Accent,
        Halo = Theme.WithAlpha(Bento.Accent, 0.3f), Head = 0, W = 4, WSel = 4, WFaint = 3, Joints = 1, Round = true, Labels = 1,
    };

    SvInk SvInkCyber() => new()
    {
        Ok = CyC, Warn = CyWarn(), Bad = CyR, Sleep = CyberHud.Faint, Faint = CyberHud.Faint, Sel = CyY, Halo = Theme.WithAlpha(CyY, 0.25f),
        W = 3, WSel = 3.5f, WFaint = 2, Joints = 2, Dash = true, Labels = 2,
    };

    // Trackers | Body, right-aligned to 'right' at y (modern; Cyber's Press cuts them): returns their left edge
    float SvTabs(float right, float y)
    {
        float wb = MathF.Ceiling(_p.MeasureWidth(Lang.T("Body"), 17, FontWeight.SemiBold) + 30);
        float wt = MathF.Ceiling(_p.MeasureWidth(Lang.T("Trackers"), 17, FontWeight.SemiBold) + 30);
        var rb = new RectangleF(right - wb, y, wb, 40);
        var rt = new RectangleF(rb.X - 6 - wt, y, wt, 40);
        if (Press("wsvtab0", rt, null, "Trackers", !_svBody, true, 18)) SvSetBody(false);
        if (Press("wsvtab1", rb, null, "Body", _svBody, true, 18)) SvSetBody(true);
        return rt.X;
    }

    // the Body side (modern and Cyber share its places): the front and side views, the legs' readings beside them, the groups, the Yaw
    // and Full resets for them along the bottom
    void SvBody(RectangleF E, RectangleF B, PanelData d)
    {
        bool cy = Theme.Cyber;
        var p = SvPose(d);
        float top = E.Y + 78, chipsY = B.Y - 12 - 40, bottom = chipsY - 12;
        var front = new RectangleF(E.X + 24, top, 132, bottom - top);
        var side = new RectangleF(front.Right + 10, top, 168, bottom - top);
        SvPanel(front, "Front", cy);
        SvPanel(side, "Side", cy);
        var fa = new RectangleF(front.X + 6, front.Y + 30, front.Width - 12, front.Height - 36);
        var sa = new RectangleF(side.X + 6, side.Y + 30, side.Width - 12, side.Height - 36);
        if (p == null)
        {
            var all = RectangleF.Union(fa, sa);
            if (cy) CyMono(SvCut(Upper(SvNoPose), all.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), all.X + 8, all.Y + all.Height / 2, all.Width - 16, CyG);
            else _p.Text(SvCut(SvNoPose, all.Width - 16, SmallText, FontWeight.SemiBold), new RectangleF(all.X + 8, all.Y + all.Height / 2 - 14, all.Width - 16, 28),
                Sub, SmallText, FontWeight.SemiBold, TextAlignment.Center);
        }
        else
        {
            var ink = cy ? SvInkCyber() : SvInkModern();
            SvFigure(fa, p, false, ink, true);
            SvFigure(sa, p, true, ink, false);
        }
        // the legs' readings
        float x = side.Right + 24, w = E.Right - 24 - x;
        SvLegReading(x, top, w, p, true, cy);
        SvLegReading(x, top + 88, w, p, false, cy);
        SvBodyNote(x, top + 192, w, p, d, cy);
        // the groups, the resets
        float cx = E.X + 24;
        for (int g = 0; g < SlimeGroups.Count; g++)
        {
            float gw = MathF.Ceiling(_p.MeasureWidth(Lang.T(SvGroupNames[g]), 17, FontWeight.SemiBold) + 28);
            if (Press(SvGroupIds[g], new RectangleF(cx, chipsY, gw, 40), null, SvGroupNames[g], _svGroup == g, true, 18)) SvSetGroup(g);
            cx += gw + 8;
        }
        SvBodyButtons(B, d);
    }

    const string SvNoPose = "No pose yet";

    void SvPanel(RectangleF r, string cap, bool cy)
    {
        if (cy)
        {
            CyberHud.CardShape(_p, r, CyberHud.Card2, CyberHud.Line);
            // ticks along the plate's left edge (a scale), the caption a mono note
            for (float ty = r.Y + 34; ty < r.Bottom - 8; ty += 16) _p.Fill(new RectangleF(r.X + 3, ty, 4, 1), CyberHud.Line);
            CyMono(Upper(cap), r.X + 12, r.Y + 21, r.Width - 24, CyG);
            return;
        }
        _p.Fill(r, Bento.Track, 14);
        CapText(Upper(cap), r.X + 12, r.Y + 22, r.Width - 24, Sub);
    }

    // a leg: its name, its twist big ("32° out", amber from 15°, red from 30°), the shin and the sideways lean under it
    void SvLegReading(float x, float y, float w, SlimePose p, bool left, bool cy)
    {
        int t = left ? SlimePose.LT : SlimePose.RT, sh = left ? SlimePose.LS : SlimePose.RS;
        string name = left ? "Left leg" : "Right leg";
        bool has = p != null && !float.IsNaN(p.Twist[t]);
        int lv = has ? SvTurnLv(p.Worst(left)) : 0;
        if (cy)
        {
            uint c = lv == 2 ? CyR : lv == 1 ? CyY : CyC;
            CyMono(Upper(name), x, y + 16, w, CyG);
            string chip = has ? SvTwist(left, p.Twist[t], true) : "–";
            float cw = CyMonoW(chip) + 14;
            _p.Stroke(RectangleF.Inflate(new RectangleF(x, y + 26, cw, 24), -0.6f, -0.6f), c, 1.25f);
            CyMono(chip, x + 7, y + 43, cw, c);
            if (!has) return;
            float sx = x + cw + 10;
            SvPieces(sx, y + 43, x + w, cy, Upper("shin"), SvDeg("{0}°", p.Twist[sh], true), SvDeg("{0}° sideways", p.Side[t], true));
            CyberMeter(new RectangleF(x, y + 58, w, 5), Math.Clamp(p.Worst(left) / 45, 0, 1), c, left ? "wsvml" : "wsvmr", 15);
            return;
        }
        CapText(Upper(name), x, y + 16, w, Sub);
        if (!has) { TextAt("–", x, y + 52, w, Bento.Faint, 30, FontWeight.Bold, TextAlignment.Leading, Theme.Display); return; }
        uint vc = lv == 2 ? Bento.Bad : lv == 1 ? Bento.Warn : Bento.Text;
        TextAt(SvTwist(left, p.Twist[t]), x, y + 52, w + 2, vc, 30, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        SvPieces(x, y + 78, x + w, cy, "shin", SvDeg("{0}°", p.Twist[sh]), SvDeg("{0}° sideways", p.Side[t]));
    }

    // "shin 28° · 6° sideways" drawn in pieces (each from a table: no text made for the line)
    void SvPieces(float x, float by, float right, bool cy, string a, string b, string c)
    {
        if (SvPiece(ref x, by, right, cy, a) && SvPiece(ref x, by, right, cy, b) && SvPiece(ref x, by, right, cy, "·")) SvPiece(ref x, by, right, cy, c);
    }

    bool SvPiece(ref float x, float by, float right, bool cy, string piece)
    {
        string s = Lang.T(piece);
        float pw = cy ? CyMonoW(s) : _p.MeasureWidth(s, SmallText, FontWeight.SemiBold);
        if (x + pw > right) return false;
        if (cy) CyMono(s, x, by, pw + 2, CyG);
        else TextAt(s, x, by, pw + 2, Sub, SmallText, FontWeight.SemiBold);
        x += pw + 5;
        return true;
    }

    // under the legs: on a side (lie on your back for a yaw reset), lying, else how far the pelvis faces from the head; the last reset
    void SvBodyNote(float x, float by, float w, SlimePose p, PanelData d, bool cy)
    {
        string note = p == null ? null : p.Rolled ? SvFirstFit(SvRollHints, w, SmallText) : p.Lying ? "Lying"
            : !float.IsNaN(p.HipHead) ? SvDeg("Hip {0}° from head", p.HipHead) : null;
        bool warn = p != null && p.Rolled;
        if (note != null)
        {
            if (cy) CyMono(SvCut(Upper(note), w, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, by, w, warn ? CyY : CyG);
            else TextAt(SvCut(note, w, SmallText, FontWeight.SemiBold), x, by, w + 2, warn ? Bento.Warn : Sub, SmallText, FontWeight.SemiBold);
        }
        string last = SvLast(d);
        if (last == null) return;
        if (cy) CyMono(SvCut(Upper(last), w, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, by + 24, w, CyG);
        else TextAt(SvCut(last, w, SmallText, FontWeight.Normal), x, by + 26, w + 2, Sub, SmallText, FontWeight.Normal);
    }

    static readonly string[] SvRollHints = { "On your back, head straight, for a yaw reset", "On your back, head straight" };

    // the Body side's two resets, for the group picked: a first tap asks ("Reset left leg?"), the second sends
    void SvBodyButtons(RectangleF B, PanelData d)
    {
        bool can = SvCan(d);
        const float gap = 12;
        float bw = (B.Width - gap) / 2;
        for (int k = 0; k < 2; k++)
        {
            var (label, asking, counting) = SvButton(k, d, false);
            bool hi = SvGuideLit(k, d) && !asking && !counting;
            var rb = new RectangleF(B.X + k * (bw + gap), B.Y, bw, B.Height);
            label = SvCut(label, bw - 20 - 8 - 16 - 2, MathF.Min(18, MathF.Max(SmallText, B.Height * 0.3f)), FontWeight.SemiBold);
            if (Press(SvBodyIds[k], rb, SvIcons[k], label, asking || counting || hi, can, 20, false, accentFill: counting || (hi && !Theme.Cyber),
                    fill: hi ? CyberHud.Cyan : Bento.Warn))
                SvResetTap(k, d, _svGroup);
        }
    }

    // The guide in the list's place (modern, Cyber): the drawing on a panel at the left; the title, the line, the big word or number, the
    // note beside it; Cancel (v22) while a mounting reset runs
    void SvGuideCard(RectangleF G, PanelData d, Guide g, int kind)
    {
        bool cy = Theme.Cyber;
        var (title, line, big, lv, note, frac) = SvGuideText(d, g, kind);
        var fr = new RectangleF(G.X, G.Y, 168, G.Height);
        if (cy) CyberHud.CardShape(_p, fr, CyberHud.Card2, CyberHud.Line);
        else _p.Fill(fr, Bento.Track, 14);
        uint ink = cy ? CyC : lv == 1 ? Bento.Ok : Bento.Text;
        SvGuideFig(RectangleF.Inflate(fr, -10, -10), SvGuideFigure(d, g), ink, cy ? 3 : 4, !cy);
        float x = fr.Right + 24, w = G.Right - x;
        if (cy)
        {
            CyMono(Upper(title), x, G.Y + 18, w, CyG);
            CyMono(SvCut(Upper(line), w, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, G.Y + 46, w, CyY);
            if (big != null)
            {
                uint bc = lv == 1 ? CyC : lv == 2 ? CyR : CyW;
                if (big.Length == 1) CyValue(x, G.Y + 128, big, 64, bc);
                else CyMono(SvCut(Upper(big), w, 22, FontWeight.Bold, CyberHud.Mono), x, G.Y + 112, w, bc, 22, FontWeight.Bold);
            }
            if (frac >= 0) CyberMeter(new RectangleF(x, G.Y + 142, MathF.Min(w, 260), 6), frac, lv == 2 ? CyR : CyC, "wsvgm", 12);
            if (note != null) CyMono(SvCut(Upper(note), w, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, G.Y + 172, w, CyG);
        }
        else
        {
            CapText(Upper(title), x, G.Y + 22, w, Sub);
            _p.Text(line, new RectangleF(x, G.Y + 32, w, 52), lv == 2 && big == null ? Bento.Warn : Bento.Text, 19, FontWeight.SemiBold, TextAlignment.Leading,
                ParagraphAlignment.Near, null, true);
            if (big != null)
            {
                uint bc = lv == 1 ? Bento.Ok : lv == 2 ? Bento.Warn : Bento.Accent;
                if (big.Length == 1) TextAt(big, x, G.Y + 150, w, bc, 64, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
                else _p.Text(big, new RectangleF(x, G.Y + 92, w, 70), bc, 26, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Center, null, true);
            }
            if (frac >= 0 && lv != 1) Meter(new RectangleF(x, G.Y + 168, MathF.Min(w, 260), 6), frac, Bento.Accent, "wsvgm");
            if (note != null) _p.Text(note, new RectangleF(x, G.Y + 176, w - (SvCanCancel(d, g) ? 150 : 0), 48), Sub, SmallText, FontWeight.SemiBold,
                TextAlignment.Leading, ParagraphAlignment.Near, null, true);
        }
        if (SvCanCancel(d, g) && Press("wsvcancel", new RectangleF(G.Right - 140, G.Bottom - 46, 140, 46), null, "Cancel", false, true, 18)) SvCancel();
    }

    // ================= the retro styles (95, XP, 7, the Mac) =================

    // the style's ink for the figure: 95's and the Mac's black, XP's progress green, 7's text; amber and red as 1995 had them; the
    // picked group in the selection colour, a pixel wider
    SvInk SvInk95() => new()
    {
        Ok = Theme.Retro switch { RetroStyle.XP => 0xFF2FB52F, RetroStyle.Aero => 0xFF1E2B3A, _ => 0xFF000000 },
        Warn = 0xFF808000, Bad = 0xFF800000, Sleep = 0xFF808080, Faint = 0xFF808080, Sel = Theme.RetroSkin.Highlight, Head = 0,
        W = 2, WSel = 3, WFaint = 1, Joints = 0, Dots = true,        // (no knee labels: the narrow well has no room left of the left knee - the Twist box says it)
    };

    // The Body tab of the window: a sunken well with the front and side views (their captions), the Twist group (each leg's turn), the
    // Reset group - the five groups as option buttons, Yaw Reset and Full Reset as push buttons (held in while they ask)
    void SvBody95(RectangleF c, PanelData d)
    {
        var p = SvPose(d);
        var well = new RectangleF(c.X, c.Y, 124, c.Height);
        _p.Fill(well, Theme.RetroSkin.Window);
        _p.Bevel(well, Painter.Edge.Sunken);
        var inner = RectangleF.Inflate(well, -3, -3);
        var fa = new RectangleF(inner.X, inner.Y + 14, 54, inner.Height - 14);
        var sa = new RectangleF(inner.X + 56, inner.Y + 14, inner.Width - 56, inner.Height - 14);
        _ui.Text95(SvFit95("Front", fa.Width), new RectangleF(fa.X, inner.Y, fa.Width, 13), Gry95, false, TextAlignment.Center);
        _ui.Text95(SvFit95("Side", sa.Width), new RectangleF(sa.X, inner.Y, sa.Width, 13), Gry95, false, TextAlignment.Center);
        if (p == null)
        {
            var ls = SvLines95(SvNoPose, inner.Width - 8);
            for (int i = 0; i < ls.Length; i++)
                _ui.Text95(ls[i], new RectangleF(inner.X + 4, inner.Y + inner.Height / 2 - 14 + 13 * i, inner.Width - 8, 13), Gry95, false, TextAlignment.Center);
        }
        else
        {
            var ink = SvInk95();
            SvFigure(fa, p, false, ink, true);
            SvFigure(new RectangleF(sa.X, sa.Y, sa.Width, MathF.Min(sa.Height, sa.Width + 10)), p, true, ink, false);
        }
        // Twist
        float gx = well.Right + 6, gw = c.Right - gx;
        var tw = new RectangleF(gx, c.Y, gw, 46);
        _ui.Group95(tw, "Twist");
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            float y = tw.Y + 15 + i * 14;
            string name = left ? "Left leg:" : "Right leg:";
            float nw = _ui.Width95(Lang.T(name)) + 4;
            _ui.Text95(name, new RectangleF(tw.X + 8, y, nw, 13), Blk95);
            bool has = p != null && !float.IsNaN(p.Twist[left ? SlimePose.LT : SlimePose.RT]);
            int lv = has ? SvTurnLv(p.Worst(left)) : 0;
            string v = has ? SvTwist(left, p.Twist[left ? SlimePose.LT : SlimePose.RT]) : "--";
            _ui.Text95(SvFit95(v, tw.Right - 8 - (tw.X + 8 + nw), lv == 2), new RectangleF(tw.X + 8 + nw, y, tw.Right - 8 - (tw.X + 8 + nw), 13),
                lv == 2 ? 0xFF800000 : lv == 1 ? 0xFF808000 : Blk95, lv == 2);
        }
        // Reset: the groups in two columns, the two push buttons under them
        var rg = new RectangleF(gx, tw.Bottom + 4, gw, c.Bottom - tw.Bottom - 4);
        _ui.Group95(rg, Lang.C("slimevr", "Reset"));
        float col2 = MathF.Floor(rg.Width / 2) - 2;
        for (int g = 0; g < SlimeGroups.Count; g++)
        {
            float rx = rg.X + 8 + (g < 3 ? 0 : col2), ry = rg.Y + 15 + (g % 3) * 14;
            if (_ui.Radio95(SvGroupIds[g], rx, ry, SvGroupNames[g], _svGroup == g)) SvSetGroup(g);
        }
        bool can = SvCan(d), lie = SvLying(d);
        float bw = MathF.Floor((rg.Width - 16 - 4) / 2), by = rg.Bottom - 6 - 23;
        for (int k = 0; k < 2; k++)
        {
            var (label, asking, counting) = SvButton(k, d, true);
            bool hi = (k == 0 && lie) || SvGuideLit(k, d);
            // (its name by the English one: a language's shorter form of it before the ellipsis - "short|&Full Reset")
            if (label == Lang.T(SvResetNames95[k])) label = SvResetNames95[k];
            if (_ui.Button95(SvBodyIds[k], new RectangleF(rg.X + 8 + k * (bw + 4), by, bw, 23), SvFit95(label, bw - 8), can, hi && !asking && !counting,
                    asking || counting))
                SvResetTap(k, d, _svGroup);
        }
    }

    // The guide in the window (95, XP, 7, the Mac): a message box's layout - the drawing in a sunken well (Display Properties' monitor),
    // the icon, the title bold, the line, the big word or number, the countdown as a progress bar; Cancel (v22) while it runs
    void SvGuide95(RectangleF G, PanelData d, Guide g, int kind)
    {
        var (title, line, big, lv, note, frac) = SvGuideText(d, g, kind);
        var well = new RectangleF(G.X, G.Y, 76, G.Height);
        _p.Fill(well, Theme.RetroSkin.Window);
        _p.Bevel(well, Painter.Edge.Sunken);
        SvGuideFig(RectangleF.Inflate(well, -4, -4), SvGuideFigure(d, g), lv == 1 ? 0xFF008000 : SvInk95().Ok, 2, false);
        W95Icons.Draw(_p, W95Icons.Id.Info32, well.Right + 8, G.Y + 2);
        float x = well.Right + 48, w = G.Right - x;
        _ui.Text95(SvFit95(title, w, true), new RectangleF(x, G.Y + 2, w, 13), Blk95, true);
        var ls = SvLines95(line, w - 10);
        for (int i = 0; i < ls.Length; i++)
            _ui.Text95(ls[i], new RectangleF(x, G.Y + 17 + 13 * i, w, 13), lv == 2 && big == null ? 0xFF800000 : Blk95);
        float y = G.Y + 48;
        if (big != null)
        {
            uint bc = lv == 2 ? 0xFF800000 : lv == 1 ? 0xFF008000 : Blk95;
            // (16 px: XP smooths text from 17 px - reading the context's rendering parameters each time makes a COM wrapper a frame)
            if (big.Length == 1) _ui.Text95(big, new RectangleF(x, y, w, 26), bc, true, TextAlignment.Leading, size: 16);
            else
            {
                var bl = SvLines95(big, w - 10, true);
                for (int i = 0; i < bl.Length; i++) _ui.Text95(bl[i], new RectangleF(x, y + 13 * i, w, 13), bc, true);
            }
            y += 30;
        }
        if (note != null)
        {
            var nl = SvLines95(note, w - 10);
            for (int i = 0; i < nl.Length && y + 13 * (i + 1) <= G.Bottom - (frac >= 0 ? 16 : 0); i++) _ui.Text95(nl[i], new RectangleF(x, y + 13 * i, w, 13), Gry95);
        }
        bool cancel = SvCanCancel(d, g);
        if (cancel && _ui.Button95("wsvcancel", new RectangleF(G.Right - 75, G.Bottom - 23, 75, 23), "Cancel")) SvCancel();
        if (frac >= 0 && lv != 2)
            _ui.Progress95(new RectangleF(x, G.Bottom - (cancel ? 18 : 14), w - (cancel ? 81 : 0), 12), frac, lv == 1 ? 0xFF008000 : Theme.W95Navy);
    }

    // a text in the 95 font in lines of w (two at most, the last one cut to fit), made once per text, width and language
    readonly Dictionary<(string, float, bool), string[]> _svLines = new();
    int _svLinesLang = -1;
    string[] SvLines95(string s, float w, bool bold = false)
    {
        if (_svLinesLang != Lang.Version) { _svLines.Clear(); _svLinesLang = Lang.Version; }
        var key = (s, w, bold);
        if (_svLines.TryGetValue(key, out var done)) return done;
        if (_svLines.Count > 64) _svLines.Clear();
        var lines = new List<string>();
        string cur = "";
        foreach (var word in Lang.T(s).Split(' '))
        {
            string next = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || _ui.Width95(next, bold) <= w) cur = next;
            else { lines.Add(cur); cur = word; }
        }
        if (cur.Length > 0) lines.Add(cur);
        if (lines.Count > 2) { lines[1] = string.Join(" ", lines.GetRange(1, lines.Count - 1)); lines.RemoveRange(2, lines.Count - 2); }
        for (int i = 0; i < lines.Count; i++) lines[i] = Fit(lines[i], w, Ui.F95, bold);
        return _svLines[key] = lines.ToArray();
    }

    // ---- --snapshot's hooks ----

    public bool SlimeBodyShown => _svBody;
    public int SlimeGroup => _svGroup;
    public void SlimeSetBody(bool on) => SvSetBody(on);
    public void SlimeSetGroup(int g) => SvSetGroup(g);
    public string SlimeGuide(PanelData d) { SvNow(d); var g = SvGuideNow(d, out int k); return g == Guide.None ? null : $"{g}: {SvGuideText(d, g, k).big ?? SvGuideText(d, g, k).line}"; }
    public string SlimeLastText(PanelData d) => SvLast(d);
    public string SlimeButton(PanelData d, int k) { SvNow(d); return SvButton(k, d, false).label; }
    public string SlimeLeftText(PanelData d, int i) { SvNow(d); return i < _svRows.Count ? _svRows[i].Left : null; }
    public string SlimeTwistText(SlimePose p, bool left) => p == null ? null : SvTwist(left, p.Twist[left ? SlimePose.LT : SlimePose.RT]);

    // the side view's figure, measured: its points' width and height in px (a lying body is wide)
    public SizeF SlimeSideBox(SlimePose p, RectangleF r)
    {
        _svProj = p;
        _svSideView = true;
        _svS = (MathF.Min(r.Width, r.Height) - 10) / MathF.Max(1, p.Height);
        _svAx = _svAy = 0;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var part in SlimePose.Drawn)
        {
            if (!p.B[(int)part].Has) continue;
            foreach (var v in new[] { SvPt(p.B[(int)part].Head), SvPt(p.B[(int)part].Tail) })
            {
                x0 = MathF.Min(x0, v.X); x1 = MathF.Max(x1, v.X); y0 = MathF.Min(y0, v.Y); y1 = MathF.Max(y1, v.Y);
            }
        }
        return x0 > x1 ? SizeF.Empty : new SizeF(x1 - x0, y1 - y0);
    }
}
