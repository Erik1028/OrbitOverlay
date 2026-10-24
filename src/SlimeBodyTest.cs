using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Orbit;

// --snapshot's part for the SlimeVR widget's Body side, its resets of some trackers, the mounting guide, the last reset and the batteries'
// time left (Erik 10-24; Sys/SlimePose.cs, Sys/SlimeBattery.cs, WristSlimeBody.cs): made-up skeletons and snapshots, the link against the
// tools' own made-up SolarXR server (SlimeVrTest.cs: a free port of 127.0.0.1 - SlimeVR's 21110 is never connected to), and byte samples
// made the servers' own way (SlimeBodySamples: v22's encoded by SlimeVR's own classes from its installed jar, run offline in an idle
// java of its own; v21's by its DataFeedBuilder's calls with the official FlatBuffers runtime).
static class SlimeBodyTools
{
    static bool Want(string line, bool ok) => Tools.Want(line, ok);

    // ---- the made-up skeleton (the samples' sx/skeleton.py, here in C#): SlimeVR's space, a 1.75 m body ----

    static Quaternion Axis(Vector3 a, float deg) => Quaternion.CreateFromAxisAngle(a, deg * MathF.PI / 180);

    static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b);
        if (d < -0.999999f) return new Quaternion(1, 0, 0, 0);
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1 + d));
    }

    // the bones of a body whose pelvis is turned by 'hip' and stands at 'origin' (between the thighs' heads): the left thigh twisted lt
    // degrees about its own axis, the left shin ls (against the pelvis), the right leg rt / rs - each dialect's bones (v21: a waist, the
    // hip bones; v22: a lower chest, two waists)
    public static List<SlimeVrViewTools.FakeBone> Skeleton(SolarXr.Dialect d, Quaternion hip, Vector3 origin, float lt = 32, float ls = 28, float rt = 0, float rs = 0)
    {
        bool v22 = d == SolarXr.Dialect.V22;
        var l = new List<(SlimeVrLink.Part p, Vector3 h, Vector3 t, float tw)>
        {
            (SlimeVrLink.Part.Head, new(0, 0.82f, 0), new(0, 0.72f, 0), 0), (SlimeVrLink.Part.Neck, new(0, 0.72f, 0), new(0, 0.62f, 0), 0),
            (SlimeVrLink.Part.UpperChest, new(0, 0.62f, 0), new(0, 0.48f, 0), 0),
        };
        if (v22)
            l.AddRange(new (SlimeVrLink.Part, Vector3, Vector3, float)[]
            {
                (SlimeVrLink.Part.LowerChest, new(0, 0.48f, 0), new(0, 0.32f, 0), 0), (SlimeVrLink.Part.UpperWaist, new(0, 0.32f, 0), new(0, 0.2f, 0), 0),
                (SlimeVrLink.Part.LowerWaist, new(0, 0.2f, 0), new(0, 0.1f, 0), 0), (SlimeVrLink.Part.Hip, new(0, 0.1f, 0), Vector3.Zero, 0),
            });
        else
            l.AddRange(new (SlimeVrLink.Part, Vector3, Vector3, float)[]
            {
                (SlimeVrLink.Part.Chest, new(0, 0.48f, 0), new(0, 0.32f, 0), 0), (SlimeVrLink.Part.Waist, new(0, 0.32f, 0), new(0, 0.1f, 0), 0),
                (SlimeVrLink.Part.Hip, new(0, 0.1f, 0), Vector3.Zero, 0),
                (SlimeVrLink.Part.LeftHip, Vector3.Zero, new(-0.13f, 0, 0), 0), (SlimeVrLink.Part.RightHip, Vector3.Zero, new(0.13f, 0, 0), 0),
            });
        foreach (bool left in new[] { true, false })
        {
            float sx = left ? -0.13f : 0.13f, t1 = left ? lt : rt, t2 = left ? ls : rs, ax = left ? -0.18f : 0.18f;
            var foot = Vector3.Transform(new Vector3(0, 0, -0.15f), Axis(Vector3.UnitY, t2));
            l.Add((left ? SlimeVrLink.Part.LeftThigh : SlimeVrLink.Part.RightThigh, new(sx, 0, 0), new(sx, -0.44f, 0), t1));
            l.Add((left ? SlimeVrLink.Part.LeftAnkle : SlimeVrLink.Part.RightAnkle, new(sx, -0.44f, 0), new(sx, -0.87f, 0), t2));
            l.Add((left ? SlimeVrLink.Part.LeftFoot : SlimeVrLink.Part.RightFoot, new(sx, -0.87f, 0), new(sx + foot.X, -0.87f, foot.Z), t2));
            l.Add((left ? SlimeVrLink.Part.LeftShoulder : SlimeVrLink.Part.RightShoulder, new(0, 0.62f, 0), new(ax, 0.58f, 0), 0));
            l.Add((left ? SlimeVrLink.Part.LeftUpperArm : SlimeVrLink.Part.RightUpperArm, new(ax, 0.58f, 0), new(ax, 0.3f, 0), 0));
            l.Add((left ? SlimeVrLink.Part.LeftLowerArm : SlimeVrLink.Part.RightLowerArm, new(ax, 0.3f, 0), new(ax, 0.04f, 0), 0));
            l.Add((left ? SlimeVrLink.Part.LeftHand : SlimeVrLink.Part.RightHand, new(ax, 0.04f, 0), new(ax, -0.04f, 0), 0));
        }
        var bones = new List<SlimeVrViewTools.FakeBone>();
        foreach (var (p, h, t, tw) in l)
        {
            var dir = t - h;
            bool foot = p is SlimeVrLink.Part.LeftFoot or SlimeVrLink.Part.RightFoot;
            bool side = foot || p is SlimeVrLink.Part.LeftHip or SlimeVrLink.Part.RightHip or SlimeVrLink.Part.LeftShoulder or SlimeVrLink.Part.RightShoulder;
            var local = v22 && foot ? Axis(Vector3.UnitY, tw) : side ? FromTo(-Vector3.UnitY, Vector3.Normalize(dir)) * Axis(Vector3.UnitY, tw) : Axis(Vector3.UnitY, tw);
            bones.Add(new(p, hip * local, origin + Vector3.Transform(h, hip), origin + Vector3.Transform(t, hip), dir.Length()));
        }
        return bones;
    }

    // a pose of the made-up skeleton, as the link makes one: standing (the pelvis turned 20° about the up axis), lying on the back, or
    // on its left side; drawn as long as the tools like (its time far ahead: never "no pose yet")
    public static SlimePose Pose(bool lying, float lt = 32, float ls = 28, int version = 1, bool rolled = false, SolarXr.Dialect d = SolarXr.Dialect.V21,
        float rt = -4, float rs = -2)
    {
        var yaw = Axis(Vector3.UnitY, 20);
        var hip = !lying ? yaw : rolled ? yaw * Axis(Vector3.UnitZ, 90) : yaw * Axis(Vector3.UnitX, 90);
        var origin = lying ? new Vector3(0.4f, 0.3f, -0.3f) : new Vector3(0.4f, 0.97f, -0.3f);
        return FromBones(Skeleton(d, hip, origin, lt, ls, rt, rs), version, Clock.Now + 1e5);
    }

    public static SlimePose FromBones(List<SlimeVrViewTools.FakeBone> bones, int version, double at)
    {
        var raw = new SlimePose.Raw();
        foreach (var b in bones)
        {
            int i = (int)b.Part;
            (raw.Has[i], raw.Rot[i], raw.Head[i], raw.Tail[i]) = (true, b.Rot, b.Head, b.Tail);
            raw.Count++;
        }
        return new SlimePose(raw, version, at);
    }

    static string Deg(float v) => float.IsNaN(v) ? "NaN" : v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    static bool Near(float v, float want, float tol = 0.5f) => !float.IsNaN(v) && MathF.Abs(v - want) <= tol;
    static string Parts(byte[] p) => p == null ? "-" : "[" + string.Join(",", p) + "]";

    // a snapshot like s, changed by 'edit' (a new object: the widget makes its rows again)
    public static SlimeVrLink.Snapshot Copy(SlimeVrLink.Snapshot s, Action<SlimeVrLink.Snapshot> edit = null)
    {
        var c = new SlimeVrLink.Snapshot
        {
            Version = s.Version + 1000, State = s.State, Dialect = s.Dialect, Connected = s.Connected, ResetKind = s.ResetKind, ResetState = s.ResetState,
            ResetLeftMs = s.ResetLeftMs, ResetAt = s.ResetAt, ResetParts = s.ResetParts, ResetPhase = s.ResetPhase, ResetFailed = s.ResetFailed,
            LastKind = s.LastKind, LastParts = s.LastParts, LastAt = s.LastAt, YawGuard = s.YawGuard, MountGuard = s.MountGuard, MountMethod = s.MountMethod,
            MountFeet = s.MountFeet,
        };
        c.Trackers.AddRange(s.Trackers);
        edit?.Invoke(c);
        return c;
    }

    // ---- the reader on the servers' own bytes, the made-up skeleton standing and lying ----

    public static void PoseChecks(WristPanel wrist)
    {
        try
        {
            var d = SolarXr.Dialect.V22;
            var got = new List<SlimeVrLink.Tracker>();
            var box = new SolarXr.Inbox();
            box.Clear();
            SolarXr.Read(Convert.FromHexString(SlimeBodySamples.Bones22), got, box, ref d);
            var p = new SlimePose(box.Raw, 1, Clock.Now);
            Want($"slimevr bones v22 (SlimeVR v22's own classes' bytes): {box.Bones} bones (want 21), the trackers' list touched {box.Trackers} (want False); twist - " +
                 $"left thigh {Deg(p.Twist[0])}° (want 32), left shin {Deg(p.Twist[1])}° (want 28), right {Deg(p.Twist[2])}° {Deg(p.Twist[3])}° (want 0 0), sideways " +
                 $"{Deg(p.Side[0])}° (want 0); lying {p.Lying} (want False); it reads '{wrist.SlimeTwistText(p, true)}' (want '32° out') -",
                 box.Bones == 21 && !box.Trackers && Near(p.Twist[0], 32) && Near(p.Twist[1], 28) && Near(p.Twist[2], 0) && Near(p.Twist[3], 0) && Near(p.Side[0], 0) &&
                 !p.Lying && wrist.SlimeTwistText(p, true) == "32° out");
            // v21: a bundle with the status feed and the bones' slot dangling, then one with the bones and the status' slot dangling
            d = SolarXr.Dialect.Unknown;
            got.Clear();
            box.Clear();
            SolarXr.Read(Convert.FromHexString(SlimeBodySamples.Dangle21), got, box, ref d);
            int trackers = got.Count;
            var thigh = got.FirstOrDefault(t => t.Part == SlimeVrLink.Part.LeftThigh);
            var (yaw, mount, guards) = (box.YawGuard, box.MountGuard, box.Guards);
            box.Clear();
            SolarXr.Read(Convert.FromHexString(SlimeBodySamples.Bones21), got, box, ref d);
            var p21 = new SlimePose(box.Raw, 2, Clock.Now);
            Want($"slimevr bones v21 (its DataFeedBuilder's calls, two feeds - each bundle with a dangling slot): dialect {d} (want V21), {trackers} tracker(s) " +
                 $"(want 4: the dangling slot skipped, not the bundle), guards {guards} - yaw {yaw}, mounting {mount} (want 0 1), the left leg's runtime estimate " +
                 $"{thigh?.ServerMin} min (want 130); the bones' bundle: {box.Bones} bones (want 22), the trackers still {got.Count} (want 4), twist {Deg(p21.Twist[0])}° " +
                 $"{Deg(p21.Twist[1])}° {Deg(p21.Twist[2])}° {Deg(p21.Twist[3])}° (want 32 28 0 0) -",
                 d == SolarXr.Dialect.V21 && trackers == 4 && guards && yaw == 0 && mount == 1 && thigh?.ServerMin == 130 && box.Bones == 22 && got.Count == 4 &&
                 Near(p21.Twist[0], 32) && Near(p21.Twist[1], 28) && Near(p21.Twist[2], 0) && Near(p21.Twist[3], 0));
            // v22's step mounting phase, reset settings, guards (its own classes' bytes)
            d = SolarXr.Dialect.V22;
            box.Clear();
            SolarXr.Read(Convert.FromHexString(SlimeBodySamples.Step22), got, box, ref d);
            var step = box.Reset;
            box.Clear();
            SolarXr.Read(Convert.FromHexString(SlimeBodySamples.Settings22), got, box, ref d);
            var (set, method, feet) = (box.Settings, box.MountMethod, box.MountFeet);
            box.Clear();
            SolarXr.Read(Convert.FromHexString(SlimeBodySamples.Guards22), got, box, ref d);
            Want($"slimevr v22 samples: the step mounting - type {step.Type} state {step.State} phase {step.Phase} (want 2 0 2: recording); the reset settings {set} - " +
                 $"method {method}, feet {feet} (want 3: the step, False - Erik's); the guards {box.Guards} - yaw {box.YawGuard}, mounting {box.MountGuard} (want 0 1: a " +
                 $"full reset first) -",
                 step.Has && step.Type == 2 && step.State == 0 && step.Phase == 2 && set && method == 3 && !feet && box.Guards && box.YawGuard == 0 && box.MountGuard == 1);
        }
        catch (Exception e) { Log.Error("slimevr bones samples: " + e); Want("slimevr bones samples: read", false); }
        // the made-up skeleton standing, lying on the back, on a side: the same twist; lying lies in the side view
        {
            var stand = Pose(false);
            var lie = Pose(true);
            var side = Pose(true, rolled: true);
            var sb = wrist.SlimeSideBox(stand, new RectangleF(0, 0, 160, 160));
            var lb = wrist.SlimeSideBox(lie, new RectangleF(0, 0, 160, 160));
            Want($"slimevr pose: standing twist {Deg(stand.Twist[0])}° / {Deg(stand.Twist[1])}°, lying {Deg(lie.Twist[0])}° / {Deg(lie.Twist[1])}°, on a side " +
                 $"{Deg(side.Twist[0])}° (want 32 / 28 each: the pose does not change it); lying {stand.Lying} {lie.Lying} {side.Lying}, on a side {lie.Rolled} {side.Rolled} " +
                 $"(want False True True, False True); the side view standing {sb.Width:0}x{sb.Height:0} px (tall), lying {lb.Width:0}x{lb.Height:0} px (want over 2x as " +
                 $"wide as high) -",
                 Near(stand.Twist[0], 32) && Near(stand.Twist[1], 28) && Near(lie.Twist[0], 32) && Near(lie.Twist[1], 28) && Near(side.Twist[0], 32) &&
                 !stand.Lying && lie.Lying && side.Lying && !lie.Rolled && side.Rolled && sb.Height > 2 * sb.Width && lb.Width > 2 * lb.Height);
        }
        // a bones-only update leaves the trackers alone (it was the trackers' list: "No trackers" 20 times a second)
        {
            var d = SolarXr.Dialect.Unknown;
            var got = new List<SlimeVrLink.Tracker>();
            var box = new SolarXr.Inbox();
            box.Clear();
            SolarXr.Read(SlimeVrViewTools.Update(SolarXr.Dialect.V22, SlimeVrViewTools.SixTrackers), got, box, ref d);
            int before = got.Count;
            var bones = SlimeVrViewTools.BoneTables(SolarXr.Dialect.V22, Skeleton(SolarXr.Dialect.V22, Quaternion.Identity, Vector3.UnitY));
            var msg = new FbObj().Objs(SolarXr.BundleFeed, new FbObj().U8(0, SolarXr.FeedUpdate).Obj(1, new FbObj().Objs(1, bones).U8(2, 1))).Finish();
            box.Clear();
            SolarXr.Read(msg, got, box, ref d);
            Want($"slimevr reader: a bones-only update - {box.Bones} bones (want 21), the trackers {before} -> {got.Count} (want 6 -> 6), told as trackers {box.Trackers} " +
                 $"(want False) -", box.Bones == 21 && before == 6 && got.Count == 6 && !box.Trackers);
        }
    }

    // ---- the builder: what the link sends, read back ----

    public static void BuilderChecks(string dir)
    {
        var f22 = SlimeVrViewTools.ReadConfigs(SolarXr.StartFeeds(SolarXr.Dialect.V22, 1000, 50), SolarXr.Dialect.V22);
        var f21 = SlimeVrViewTools.ReadConfigs(SolarXr.StartFeeds(SolarXr.Dialect.V21, 1000, 50), SolarXr.Dialect.V21);
        var o22 = SlimeVrViewTools.ReadConfigs(SolarXr.StartFeeds(SolarXr.Dialect.V22, 1000, 0), SolarXr.Dialect.V22);
        var o21 = SlimeVrViewTools.ReadConfigs(SolarXr.StartFeeds(SolarXr.Dialect.V21, 1000, 0), SolarXr.Dialect.V21);
        static string C(List<SlimeVrViewTools.Cfg> l) => l == null ? "none" : string.Join(" + ", l.Select(c => $"{c.Ms}ms{(c.Device ? " devices" : "")}{(c.Guards ? " guards" : "")}{(c.Bones ? " bones" : "")}"));
        var status = new SlimeVrViewTools.Cfg(1000, true, true, false);
        var bones = new SlimeVrViewTools.Cfg(50, false, false, true);
        Want($"slimevr StartFeeds: v22 '{C(f22)}', v21 '{C(f21)}' (want '1000ms devices guards + 50ms bones' each, the bone mask and the guards in each one's slots); " +
             $"without the bones '{C(o22)}', '{C(o21)}' (want '1000ms devices guards') -",
             f22?.Count == 2 && f22[0] == status && f22[1] == bones && f21?.Count == 2 && f21[0] == status && f21[1] == bones &&
             o22?.Count == 1 && o22[0] == status && o21?.Count == 1 && o21[0] == status);
        var left = SlimeGroups.Mask(SlimeGroups.LeftLeg);
        byte[] y22m = SolarXr.ResetRequest(SlimeVrLink.ResetKind.Yaw, SolarXr.Dialect.V22, left), y21m = SolarXr.ResetRequest(SlimeVrLink.ResetKind.Yaw, SolarXr.Dialect.V21, left);
        var y22 = SlimeVrViewTools.ReadResetParts(y22m, SolarXr.Dialect.V22).Single();
        var y21 = SlimeVrViewTools.ReadResetParts(y21m, SolarXr.Dialect.V21).Single();
        var up22 = SlimeVrViewTools.ReadResetParts(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Full, SolarXr.Dialect.V22, SlimeGroups.Mask(SlimeGroups.Upper)), SolarXr.Dialect.V22).Single();
        float mStep = SlimeVrViewTools.ReadResetParts(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Mounting, SolarXr.Dialect.V22, 0, 3), SolarXr.Dialect.V22).Single().delay;
        float mPose = SlimeVrViewTools.ReadResetParts(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Mounting, SolarXr.Dialect.V22, 0, 2), SolarXr.Dialect.V22).Single().delay;
        float mNone = SlimeVrViewTools.ReadResetParts(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Mounting, SolarXr.Dialect.V22), SolarXr.Dialect.V22).Single().delay;
        // the other dialect reads the header as no message (v21's landing trick holds with the parts' vector after it)
        var x21 = SlimeVrViewTools.RpcTypes(y21m, SolarXr.Dialect.V22);
        int x21r = SlimeVrViewTools.ReadResets(y21m, SolarXr.Dialect.V22).Count, x22r = SlimeVrViewTools.ReadResets(y22m, SolarXr.Dialect.V21).Count;
        var cancel = SlimeVrViewTools.RpcTypes(SolarXr.CancelReset(), SolarXr.Dialect.V22);
        var ask = SlimeVrViewTools.RpcTypes(SolarXr.ResetsSettingsRequest(), SolarXr.Dialect.V22);
        Want($"slimevr partial reset: yaw of the left leg v22 ({y22.type}, {y22.delay}, {Parts(y22.parts)}) (want (0, 0, [8,10,12])), v21 ({y21.type}, {y21.delay}, " +
             $"{Parts(y21.parts)}) (want (0, NaN, [23,6,8,10])); full of the upper body v22 {Parts(up22.parts)} (want [2,3,4,5,6,7,20,21,14,15,16,17,18,19] in any order); " +
             $"mounting's delay v22 - the step {mStep}, the pose {mPose}, not known {mNone} (want 0 3 3); v21's header read as v22: type {string.Join(",", x21)}, resets " +
             $"{x21r}, v22's read as v21: resets {x22r} (want 0, 0, 0); CancelResetRequest type {string.Join(",", cancel)} (want 132), ResetsSettingsRequest " +
             $"{string.Join(",", ask)} (want 23) -",
             y22.type == 0 && y22.delay == 0 && Parts(y22.parts) == "[8,10,12]" && y21.type == 0 && float.IsNaN(y21.delay) && Parts(y21.parts) == "[23,6,8,10]" &&
             up22.parts.OrderBy(x => x).SequenceEqual(new byte[] { 2, 3, 4, 5, 6, 7, 14, 15, 16, 17, 18, 19, 20, 21 }) && mStep == 0 && mPose == 3 && mNone == 3 &&
             x21.SequenceEqual(new[] { 0 }) && x21r == 0 && x22r == 0 && cancel.SequenceEqual(new[] { 132 }) && ask.SequenceEqual(new[] { 23 }));
        // The v22 lines read by SlimeVR v22.0.0-rc.2's own decoder (MessageBundle.decode from its jar, offline: sx/SxBones22.java read),
        // 10-24, shortened:
        //   feeds22: StartDataFeed(dataFeeds=[DataFeedConfig(minimumTimeSinceLast=1000, dataMask=DeviceDataMask(trackerData=TrackerDataMask(
        //     info=true, status=true, ...false), deviceData=true), boneMask=null, serverGuardsMask=true), DataFeedConfig(minimumTimeSinceLast=50,
        //     dataMask=null, boneMask=BoneMask(bodyPart=true, boneLength=false, rotation=true, orientation=false, headPosition=true,
        //     tailPosition=true, ...false), serverGuardsMask=false)])
        //   yawleft22: ResetRequest(resetType=YAW, bodyParts=[LEFT_UPPER_LEG, LEFT_LOWER_LEG, LEFT_FOOT], delay=0.0)
        //   mountstep22: ResetRequest(resetType=MOUNTING, bodyParts=null, delay=0.0)
        //   cancel22: CancelResetRequest; settingsask22: ResetsSettingsRequest
        File.WriteAllText(Path.Combine(dir, "slimevr_client_body.txt"),
            $"feeds22 {Convert.ToHexString(SolarXr.StartFeeds(SolarXr.Dialect.V22, 1000, 50))}\nyawleft22 {Convert.ToHexString(y22m)}\n" +
            $"mountstep22 {Convert.ToHexString(SolarXr.ResetRequest(SlimeVrLink.ResetKind.Mounting, SolarXr.Dialect.V22, 0, 3))}\n" +
            $"cancel22 {Convert.ToHexString(SolarXr.CancelReset())}\nsettingsask22 {Convert.ToHexString(SolarXr.ResetsSettingsRequest())}\n" +
            $"feeds21 {Convert.ToHexString(SolarXr.StartFeeds(SolarXr.Dialect.V21, 1000, 50))}\nyawleft21 {Convert.ToHexString(y21m)}\n");
    }

    // ---- the batteries' time left ----

    public static void BatteryChecks(WristPanel wrist, PanelData data)
    {
        static int Run(Func<int, (int pct, bool charging)> at, int to)
        {
            var b = new SlimeBattery();
            for (int s = 0; s <= to; s += 30) { var (p, c) = at(s); b.Add(1000 + s, 1, p, c); }
            return b.Left(1);
        }
        int steady = Run(s => ((int)Math.Round(80 - 12.0 * s / 1200), false), 1200);
        int flat = Run(s => (70, false), 1200);
        int charging = Run(s => ((int)Math.Round(80 - 12.0 * s / 1200), s > 900 && s < 1000), 1200);
        int noisy = Run(s => ((int)Math.Round(80 - 12.0 * s / 1200) + (s / 30 % 2 == 0 ? 4 : -4), false), 1200);
        int brief = Run(s => ((int)Math.Round(80 - 12.0 * s / 1200), false), 540);
        // the words on a row ("~2 h left" after its signal; the retro list's "~2h")
        var s1 = WithLeft(SlimeVrViewTools.Fake(), p => p == SlimeVrLink.Part.LeftThigh ? 120 : p == SlimeVrLink.Part.RightThigh ? 90 : p == SlimeVrLink.Part.LeftAnkle ? 40 : -1);
        var keep = data.SlimeVr;
        data.SlimeVr = s1;
        string lt = wrist.SlimeLeftText(data, 2), rt = wrist.SlimeLeftText(data, 3), la = wrist.SlimeLeftText(data, 4), ch = wrist.SlimeLeftText(data, 0);
        data.SlimeVr = keep;
        Want($"slimevr battery forecast: 80 -> 68 % over 20 min {steady} min (want 120: '~2 h left'); flat {flat}, charging in the window {charging}, noisy {noisy}, " +
             $"under 10 min {brief} (want -1 each: nothing said); the rows say '{lt}', '{rt}', '{la}', '{ch}' (want '~2 h left', '~1.5 h left', '~40 min left', '') -",
             steady == 120 && flat == -1 && charging == -1 && noisy == -1 && brief == -1 && lt == "~2 h left" && rt == "~1.5 h left" && la == "~40 min left" && ch == "");
    }

    // a snapshot like s0 with each tracker's time left (minutes, as SlimeBattery rounds them; -1: nothing to say)
    static SlimeVrLink.Snapshot WithLeft(SlimeVrLink.Snapshot s0, Func<SlimeVrLink.Part, int> left) => Copy(s0, c =>
    {
        c.Trackers.Clear();
        foreach (var t in s0.Trackers) c.Trackers.Add(new SlimeVrLink.Tracker
        {
            Device = t.Device, Num = t.Num, Part = t.Part, Battery = t.Battery, Rssi = t.Rssi, Ping = t.Ping, Status = t.Status, Voltage = t.Voltage, Imu = true,
            DisplayName = t.DisplayName, LeftMin = left(t.Part),
        });
    });

    // every tracker's time left but the hip's (it charges) - the longest lines the ankles' ("-72 dBm · 25 ms · ~40 min left")
    static SlimeVrLink.Snapshot Forecasts() => WithLeft(SlimeVrViewTools.Fake(), p => p switch
    {
        SlimeVrLink.Part.Chest => 180, SlimeVrLink.Part.LeftThigh => 120, SlimeVrLink.Part.RightThigh => 50, SlimeVrLink.Part.LeftAnkle => 40,
        SlimeVrLink.Part.RightAnkle => 90, _ => -1,
    });

    // the left leg's yaw reset half a minute ago (the Body side's last line)
    static SlimeVrLink.Snapshot LastLeft() => Copy(SlimeVrViewTools.Fake(), c =>
        (c.LastKind, c.LastAt, c.LastParts) = (0, Clock.Now - 30, SlimeGroups.Mask(SlimeGroups.LeftLeg)));

    // ---- the link on the made-up server: the bones asked only while wanted, a reset of the left leg, the guide, a reset from elsewhere ----

    public static void LinkChecks(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var look0 = cfg.Data.Appearance;
        var (s0, p0, r0, rp0, c0) = (data.SlimeVr, data.SlimePose, wrist.OnSlimeReset, wrist.OnSlimeResetParts, wrist.OnSlimeCancel);
        try
        {
            foreach (var d in new[] { SolarXr.Dialect.V22, SolarXr.Dialect.V21 })
            {
                bool v22 = d == SolarXr.Dialect.V22;
                var six = SlimeVrViewTools.SixTrackers.Select(t => t.Part == SlimeVrLink.Part.Hip ? t with { RuntimeMin = 130, Volt = 3.9f } : t).ToArray();
                var standing = Skeleton(d, Axis(Vector3.UnitY, 20), new Vector3(0.4f, 0.97f, -0.3f));
                using var server = new SlimeVrViewTools.FakeServer(d, null) { Devices = n => SlimeVrViewTools.Boards(d, six), Bones = t => standing, Speed = 0.3 };
                if (server.Port == SlimeVrLink.ServerPort || server.Port <= 0) { Want($"slimevr body link {d}: the made-up server's port {server.Port} is not SlimeVR's", false); continue; }
                using var link = new SlimeVrLink(server.Port);
                link.Active = true;
                bool up = SlimeVrViewTools.Until(() => link.Get() is { State: SlimeVrLink.State.Ok, Trackers.Count: 6 } s && s.Dialect == d, 5);
                bool asked = SlimeVrViewTools.Until(() => server.Starts() is { Count: >= 2 } st && st[^1] is { Count: 1 } l && l[0].Guards, 3);
                bool settings = !v22 || SlimeVrViewTools.Until(() => link.Get().MountMethod == 3 && server.SettingsAsked >= 1, 3);
                bool estimate = SlimeVrViewTools.Until(() => link.Get().Trackers.Any(t => t.Part == SlimeVrLink.Part.Hip && t.LeftMin == 120), 3);
                // the bones: asked while wanted (a second feed in the same StartDataFeed), 20 a second, the trackers kept all along
                var sw = System.Diagnostics.Stopwatch.StartNew();
                link.WantPose = true;
                bool two = SlimeVrViewTools.Until(() => server.Starts()[^1] is { Count: 2 } l && l[1].Bones, 1.5);
                double onAfter = sw.Elapsed.TotalSeconds;
                SlimeVrViewTools.Until(() => link.GetPose() != null, 1);
                int v0 = link.GetPose()?.Version ?? 0, least = 99;
                var t0 = System.Diagnostics.Stopwatch.StartNew();
                while (t0.Elapsed.TotalSeconds < 1) { least = Math.Min(least, link.Get().Trackers.Count); Thread.Sleep(15); }
                int v1 = link.GetPose()?.Version ?? 0;
                var pose = link.GetPose();
                // off: one feed again after the 2 s, then no more bones
                link.WantPose = false;
                sw.Restart();
                bool one = SlimeVrViewTools.Until(() => server.Starts()[^1] is { Count: 1 }, 4);
                double offAfter = sw.Elapsed.TotalSeconds;
                Thread.Sleep(300);
                int pu0 = server.PoseUpdates;
                Thread.Sleep(700);
                int pu1 = server.PoseUpdates;
                Want($"slimevr body link {d} (port {server.Port}): up {up}, the feed asked again with the guards {asked}, {(v22 ? "its reset settings asked and read" : "(no reset settings in v21)")} {settings}, the hip's " +
                     $"time left from the server's estimate {estimate} (130 min: '~2 h'); the Body side on: two feeds asked {two} after {onAfter:0.00} s (want within 1.5), " +
                     $"{v1 - v0} poses in 1 s (want 10 or more), the trackers never under {least} (want 6), the pose's twist {Deg(pose?.Twist[0] ?? float.NaN)}° (want 32); off: " +
                     $"one feed again {one} after {offAfter:0.0} s (want about 2), then {pu1 - pu0} bones update(s) in 0.7 s (want 0) -",
                     up && asked && settings && estimate && two && onAfter <= 1.5 && v1 - v0 >= 10 && least == 6 && Near(pose?.Twist[0] ?? float.NaN, 32) && one &&
                     offAfter >= 1.7 && offAfter <= 3.6 && pu1 - pu0 == 0);
                // through the watch: a tap on the left leg, then Yaw reset twice - exactly one ResetRequest, of the left leg's parts; its answer "Done"
                link.WantPose = true;
                SlimeVrViewTools.Until(() => link.GetPose() is { } lp && Clock.Now - lp.At < 0.5, 2);
                wcfg.Face = "custom"; wcfg.Background = "panel"; wcfg.Grid = null; wcfg.Pages = null; wcfg.Page = 0;
                wcfg.Widgets = new List<WatchWidget> { new() { Type = "slimevr", X = 0, Y = 0, W = 2, H = 2 } };
                wrist.OnSlimeReset = k => link.Reset((SlimeVrLink.ResetKind)k);
                wrist.OnSlimeResetParts = (k, g) => link.Reset((SlimeVrLink.ResetKind)k, SlimeGroups.Mask(g));
                wrist.OnSlimeCancel = () => link.CancelReset();
                foreach (var style in v22 ? new[] { "modern", "classic", "terminal" } : new[] { "modern" })
                {
                    cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                    Theme.Apply(cfg.Data.Appearance);
                    data.SlimeVr = link.Get();
                    data.SlimePose = link.GetPose();
                    wrist.Settle();
                    wrist.CloseForTest();
                    wrist.OpenForTest("slimevr");
                    wrist.SlimeSetBody(true);
                    wrist.SlimeSetGroup(SlimeGroups.All);
                    wrist.Draw(data); wrist.Draw(data);
                    var limb = wrist.ControlPoint(data, "wsvlimb2");
                    if (limb is { } lp2) SlimeVrViewTools.Tap(wrist, data, lp2);
                    int group = wrist.SlimeGroup;
                    int before = server.ResetsWithParts(out _);
                    data.SlimeVr = link.Get();
                    data.SlimePose = link.GetPose();
                    var yaw = wrist.ControlPoint(data, "wsvbr0");
                    if (yaw is not { } yp) { Want($"slimevr body reset {d} {style}: the Body side's Yaw reset drawn", false); continue; }
                    SlimeVrViewTools.Tap(wrist, data, yp);
                    int armed = wrist.SlimeArmed;
                    string asking = wrist.SlimeButton(data, 0);
                    Thread.Sleep(300);
                    int afterOne = server.ResetsWithParts(out _) - before;
                    data.SlimeVr = link.Get();
                    SlimeVrViewTools.Tap(wrist, data, yp);
                    bool sent = SlimeVrViewTools.Until(() => server.ResetsWithParts(out _) - before >= 1, 3);
                    bool answered = SlimeVrViewTools.Until(() => link.Get() is { ResetKind: 0, ResetState: 1 } s && Clock.Now - s.ResetAt < 2, 3);
                    Thread.Sleep(300);
                    int total = server.ResetsWithParts(out var last) - before;
                    data.SlimeVr = link.Get();
                    data.SlimePose = link.GetPose();
                    wrist.Draw(data);
                    string done = wrist.SlimeButton(data, 0);
                    wrist.Surf.SavePng(gpu, Path.Combine(dir, $"slimevr_body_reset_{(v22 ? "v22" : "v21")}_{style}.png"));
                    string want = v22 ? "[8,10,12]" : "[23,6,8,10]";
                    Want($"slimevr body reset {d} {style}: the left leg tapped {limb != null} - group {group} (want 2); a first tap on Yaw reset asks (armed {armed}, " +
                         $"'{asking}' - want 0, 'Reset left leg?') and sends {afterOne} (want 0); the second sends {total} (want 1): type {last.type} (want 0), delay " +
                         $"{last.delay} (want {(v22 ? "0" : "NaN")}), body parts {Parts(last.parts)} (want {want}); answered {answered}, the button '{done}' (want 'Done') -",
                         limb != null && group == SlimeGroups.LeftLeg && armed == 0 && asking == "Reset left leg?" && afterOne == 0 && sent && total == 1 && last.type == 0 &&
                         (v22 ? last.delay == 0 : float.IsNaN(last.delay)) && Parts(last.parts) == want && answered && done == "Done");
                    wrist.SlimeSetBody(false);
                    wrist.SlimeSetGroup(SlimeGroups.All);
                    wrist.CloseForTest();
                }
                cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                // the guide (v22: its guards silent): mounting with no full reset sends nothing and asks for the full one; Full twice, done;
                // then Mounting twice goes out (the step: delay 0) - its phases, then "Done"
                if (v22)
                {
                    server.MountGuard = 1;
                    bool guarded = SlimeVrViewTools.Until(() => link.Get().MountGuard == 1, 2.5);
                    data.SlimeVr = link.Get();
                    data.SlimePose = null;
                    wrist.Settle();
                    wrist.OpenForTest("slimevr");
                    wrist.Draw(data);
                    int b0 = server.ResetsWithParts(out _);
                    var mp = wrist.ControlPoint(data, "wsvr2");
                    var fp = wrist.ControlPoint(data, "wsvr1");
                    string g1 = null, g2 = null, g3 = null, g4 = null;
                    int none = -1, fullSent = -1;
                    (int type, float delay, byte[] parts) mlast = default;
                    var phases = new List<int>();
                    if (mp is { } m && fp is { } f)
                    {
                        SlimeVrViewTools.Tap(wrist, data, m);
                        g1 = wrist.SlimeGuide(data);
                        Thread.Sleep(300);
                        none = server.ResetsWithParts(out _) - b0;
                        SlimeVrViewTools.Tap(wrist, data, f);
                        SlimeVrViewTools.Tap(wrist, data, f);
                        SlimeVrViewTools.Until(() => server.ResetsWithParts(out _) - b0 >= 1, 2);
                        fullSent = server.ResetsWithParts(out var fl) - b0;
                        SlimeVrViewTools.Until(() => link.Get() is { ResetKind: 1, ResetState: 1 }, 3);
                        SlimeVrViewTools.Until(() => link.Get().MountGuard == 0, 2.5);
                        data.SlimeVr = link.Get();
                        wrist.Draw(data);
                        g2 = wrist.SlimeGuide(data);
                        wrist.Surf.SavePng(gpu, Path.Combine(dir, "slimevr_guide_next.png"));
                        SlimeVrViewTools.Tap(wrist, data, m);
                        SlimeVrViewTools.Tap(wrist, data, m);
                        SlimeVrViewTools.Until(() => server.ResetsWithParts(out _) - b0 >= 2, 2);
                        server.ResetsWithParts(out mlast);
                        var tw = System.Diagnostics.Stopwatch.StartNew();
                        while (tw.Elapsed.TotalSeconds < 3)
                        {
                            var s = link.Get();
                            if (s.ResetKind == 2 && (phases.Count == 0 || phases[^1] != s.ResetPhase)) phases.Add(s.ResetPhase);
                            if (s.ResetKind == 2 && s.ResetPhase == 2 && g3 == null)
                            {
                                data.SlimeVr = s;
                                wrist.Draw(data);
                                g3 = wrist.SlimeGuide(data);
                                wrist.Surf.SavePng(gpu, Path.Combine(dir, "slimevr_guide_recording.png"));
                            }
                            if (s.ResetKind == 2 && s.ResetState == 1) { data.SlimeVr = s; wrist.Draw(data); g4 = wrist.SlimeGuide(data); break; }
                            Thread.Sleep(15);
                        }
                    }
                    Want($"slimevr guide (v22, the made-up server's mounting guard on: {guarded}): a first tap on Mounting with no full reset - '{g1}' (want 'FullFirst: ...') " +
                         $"and {none} request(s) (want 0); Full twice sends {fullSent} (want 1), done - '{g2}' (want 'Ready: ...', Mounting lit); Mounting twice sends " +
                         $"({mlast.type}, {mlast.delay}) (want (2, 0): the step); its phases {string.Join(" ", phases)} (want 1 2 3 4) - '{g3}' (want 'Running: Recording'), " +
                         $"then '{g4}' (want 'Done: Done') -",
                         guarded && g1 != null && g1.StartsWith("FullFirst", StringComparison.Ordinal) && none == 0 && fullSent == 1 && g2 != null &&
                         g2.StartsWith("Ready", StringComparison.Ordinal) && mlast.type == 2 && mlast.delay == 0 && phases.Contains(2) && phases.Contains(4) &&
                         g3 == "Running: Recording" && g4 == "Done: Done");
                    // ... and Cancel while it runs: CancelResetRequest, the server answers CANCELED - "Cancelled"
                    string g5 = null;
                    bool cancelDrawn = false;
                    if (mp is { } m2)
                    {
                        int b1 = server.ResetsWithParts(out _);
                        SlimeVrViewTools.Tap(wrist, data, m2);
                        SlimeVrViewTools.Tap(wrist, data, m2);
                        if (SlimeVrViewTools.Until(() => server.ResetsWithParts(out _) > b1 && link.Get() is { ResetKind: 2, ResetState: 0 }, 2))
                        {
                            data.SlimeVr = link.Get();
                            wrist.Draw(data);
                            var cp = wrist.ControlPoint(data, "wsvcancel");
                            cancelDrawn = cp != null;
                            if (cp is { } cpp) SlimeVrViewTools.Tap(wrist, data, cpp);
                            SlimeVrViewTools.Until(() => link.Get() is { ResetKind: 2, ResetState: 2 }, 2);
                            data.SlimeVr = link.Get();
                            wrist.Draw(data);
                            g5 = wrist.SlimeGuide(data);
                        }
                    }
                    Want($"slimevr guide cancel (v22): Cancel drawn while the step runs {cancelDrawn}, {server.Cancels} CancelResetRequest(s) (want 1), then '{g5}' (want " +
                         $"'Failed: Cancelled') -", cancelDrawn && server.Cancels == 1 && g5 == "Failed: Cancelled");
                    wrist.CloseForTest();
                }
                // a reset from elsewhere (the GUI, a keybind): every client is told - "Yaw reset just now"; 12.5 minutes later "12 min ago"
                double before2 = Clock.Now;
                server.Push(v22 ? SlimeVrViewTools.Answer22(0, 1, 0, 0, null) : SlimeVrViewTools.Answer21(0, 1, 0, 0, null));
                bool told = SlimeVrViewTools.Until(() => link.Get() is { LastKind: 0 } s && s.LastAt >= before2, 2);
                data.SlimeVr = link.Get();
                string now = wrist.SlimeLastText(data);
                data.SlimeVr = Copy(link.Get(), c => c.LastAt = Clock.Now - 12.5 * 60);
                string later = wrist.SlimeLastText(data);
                data.SlimeVr = Copy(link.Get(), c => (c.LastAt, c.LastParts) = (Clock.Now - 30, SlimeGroups.Mask(SlimeGroups.LeftLeg)));
                string part = wrist.SlimeLastText(data);
                Want($"slimevr last reset {d}: one from elsewhere told {told} - '{now}' (want 'Yaw reset just now'); 12.5 min later '{later}' (want 'Yaw reset 12 min ago'); " +
                     $"of the left leg '{part}' (want 'Yaw reset just now (left leg)') -",
                     told && now == "Yaw reset just now" && later == "Yaw reset 12 min ago" && part == "Yaw reset just now (left leg)");
                link.WantPose = false;
                link.Active = false;
            }
        }
        catch (Exception e) { Log.Error("slimevr body link checks: " + e); Want("slimevr body link checks: ran to the end", false); }
        finally
        {
            wrist.SlimeSetBody(false);
            wrist.SlimeSetGroup(SlimeGroups.All);
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (data.SlimeVr, data.SlimePose, wrist.OnSlimeReset, wrist.OnSlimeResetParts, wrist.OnSlimeCancel) = (s0, p0, r0, rp0, c0);
            cfg.Data.Appearance = look0;
            Theme.Apply(cfg.Data.Appearance);
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    // ---- per style: the pictures, what a frame allocates, the layout's extra states ----

    // the snapshots of the guide's pictures: mounting before a full reset, the step recording (v22), the pose counting "2" (v21), done
    static SlimeVrLink.Snapshot NeedFull() => Copy(SlimeVrViewTools.Fake(), c => (c.MountGuard, c.YawGuard, c.MountMethod) = (1, 0, 3));
    static SlimeVrLink.Snapshot Stepping() => Copy(SlimeVrViewTools.Fake(), c =>
        (c.Dialect, c.MountMethod, c.ResetKind, c.ResetState, c.ResetPhase, c.ResetAt) = (SolarXr.Dialect.V22, 3, 2, 0, 2, Clock.Now));
    static SlimeVrLink.Snapshot Posing() => Copy(SlimeVrViewTools.Fake(), c =>
        (c.Dialect, c.MountMethod, c.ResetKind, c.ResetState, c.ResetLeftMs, c.ResetAt) = (SolarXr.Dialect.V21, 2, 2, 0, 1600, Clock.Now));
    static SlimeVrLink.Snapshot Mounted() => Copy(SlimeVrViewTools.Fake(), c =>
        (c.Dialect, c.MountMethod, c.ResetKind, c.ResetState, c.ResetPhase, c.ResetAt) = (SolarXr.Dialect.V22, 3, 2, 1, 4, Clock.Now + 1.5));

    public static readonly List<(string tag, string[] files)> Preview = new();
    static readonly string[] Shots = { "body", "body_lying", "body_ask", "body_last", "mount_needfull", "mount_step", "mount_pose", "mount_done" };

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var (s0, p0, r0, rp0, l0) = (data.SlimeVr, data.SlimePose, wrist.OnSlimeReset, wrist.OnSlimeResetParts, data.SlimeLying);
        var files = Shots.Select(s => Path.Combine(dir, $"wrist_open_slimevr_{s}{tag}.png")).ToArray();
        try
        {
            wcfg.Face = "custom"; wcfg.Background = "panel"; wcfg.Grid = null; wcfg.Pages = null; wcfg.Page = 0;
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "slimevr", X = 0, Y = 0, W = 2, H = 2 } };
            (wrist.OnSlimeReset, wrist.OnSlimeResetParts, data.SlimeLying) = (null, null, false);
            void Shot(int i) { wrist.Settle(); wrist.Draw(data); wrist.Draw(data); wrist.Surf.SavePng(gpu, files[i]); }
            // the Body side: standing, the left leg twisted; lying (one ankle busy, one lost); the left leg picked, Yaw reset asking
            data.SlimeVr = SlimeVrViewTools.Fake();
            data.SlimePose = Pose(false);
            wrist.OpenForTest("slimevr");
            wrist.SlimeSetBody(true);
            wrist.SlimeSetGroup(SlimeGroups.All);
            Shot(0);
            data.SlimeVr = SlimeVrViewTools.Fake(trouble: true);
            data.SlimePose = Pose(true);
            Shot(1);
            data.SlimeVr = SlimeVrViewTools.Fake();
            data.SlimePose = Pose(false);
            wrist.Draw(data);
            var limb = wrist.ControlPoint(data, "wsvlimb2");
            if (limb is { } lp) SlimeVrViewTools.Tap(wrist, data, lp);
            int group = wrist.SlimeGroup;
            var yaw = wrist.ControlPoint(data, "wsvbr0");
            int armed = -1, sentKind = -1, sentGroup = -1;
            if (yaw is { } yp)
            {
                SlimeVrViewTools.Tap(wrist, data, yp);
                armed = wrist.SlimeArmed;
                Shot(2);
                SlimeVrViewTools.Tap(wrist, data, yp);
                (sentKind, sentGroup) = (wrist.LastSlimeReset, wrist.LastSlimeGroup);
            }
            // the last reset (the left leg's yaw half a minute ago) under the legs, every group again
            wrist.CloseForTest();
            wrist.OpenForTest("slimevr");
            wrist.SlimeSetGroup(SlimeGroups.All);
            data.SlimeVr = LastLeft();
            Shot(3);
            // the guide (the Trackers side): mounting before a full reset; the step recording; the pose's countdown; done
            wrist.SlimeSetBody(false);
            wrist.SlimeSetGroup(SlimeGroups.All);
            wrist.CloseForTest();
            data.SlimePose = null;
            data.SlimeVr = NeedFull();
            wrist.OpenForTest("slimevr");
            wrist.Draw(data);
            var mp = wrist.ControlPoint(data, "wsvr2");
            if (mp is { } m) SlimeVrViewTools.Tap(wrist, data, m);
            string needFull = wrist.SlimeGuide(data);
            int needArmed = wrist.SlimeArmed;
            Shot(4);
            wrist.CloseForTest();
            wrist.OpenForTest("slimevr");
            data.SlimeVr = Stepping();
            Shot(5);
            string step = wrist.SlimeGuide(data);
            data.SlimeVr = Posing();
            Shot(6);
            string pose = wrist.SlimeGuide(data);
            data.SlimeVr = Mounted();
            Shot(7);
            string done = wrist.SlimeGuide(data);
            wrist.CloseForTest();
            Want($"slimevr body view{tag}: the left leg tapped {limb != null} - group {group} (want 2), Yaw reset asks ({armed}, want 0), the second tap is the left leg's " +
                 $"yaw ({sentKind} {sentGroup}, want 0 2); Mounting before a full reset - '{needFull}', armed {needArmed} (want 'FullFirst: ...', -1); the step " +
                 $"'{step}' (want 'Running: Recording'), the pose '{pose}' (want 'Running: 2'), done '{done}' (want 'Done: Done') -",
                 limb != null && group == SlimeGroups.LeftLeg && armed == 0 && sentKind == 0 && sentGroup == SlimeGroups.LeftLeg && needFull != null &&
                 needFull.StartsWith("FullFirst", StringComparison.Ordinal) && needArmed == -1 && step == "Running: Recording" && pose == "Running: 2" && done == "Done: Done");
            // what a frame allocates: the Body side at rest, the Body side with a new pose every frame, the guide counting (want 0 each)
            using var appOnly = Tools.AppAllocs();
            data.SlimeVr = SlimeVrViewTools.Fake();
            data.SlimePose = Pose(false);
            wrist.OpenForTest("slimevr");
            wrist.SlimeSetBody(true);
            wrist.Settle();
            for (int i = 0; i < 6; i++) wrist.Draw(data);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) wrist.Draw(data);
            long rest = (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            var poses = new[] { Pose(false, 31, 27, 11), Pose(false, 33, 29, 12), Pose(true, 35, 30, 13), Pose(true, 30, 26, 14, rolled: true) };
            for (int i = 0; i < 12; i++) { data.SlimePose = poses[i % 4]; wrist.Draw(data); }
            a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) { data.SlimePose = poses[i % 4]; wrist.Draw(data); }
            long moving = (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SlimeAllocTypes("slimevr body" + tag, () => wrist.Draw(data));
            wrist.SlimeSetBody(false);
            data.SlimePose = null;
            var counting = Copy(SlimeVrViewTools.Fake(), c => (c.Dialect, c.MountMethod, c.ResetKind, c.ResetState, c.ResetLeftMs, c.ResetAt) =
                (SolarXr.Dialect.V21, 2, 2, 0, 9000, Clock.Now));
            data.SlimeVr = counting;
            for (int i = 0; i < 6; i++) wrist.Draw(data);
            var frames = new long[20];
            a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) { long f0 = GC.GetAllocatedBytesForCurrentThread(); wrist.Draw(data); frames[i] = GC.GetAllocatedBytesForCurrentThread() - f0; }
            long guide = (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            if (guide != 0) Log.Info($"slimevr guide{tag}: the frames' bytes {string.Join(" ", frames)} at {Clock.Now - counting.ResetAt:0.000} s");
            if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SlimeAllocTypes("slimevr guide" + tag, () => wrist.Draw(data));
            wrist.CloseForTest();
            Want($"slimevr body view{tag}: a frame of the Body side at rest {rest} B, with a new pose every frame {moving} B, the guide counting down {guide} B (want 0 each)",
                rest == 0 && moving == 0 && guide == 0);
            SideChecks(wrist, data, painter, tag);
            Preview.RemoveAll(p => p.tag == tag);
            Preview.Add((tag, files));
        }
        catch (Exception e) { Log.Error("slimevr body view: " + e); Want($"slimevr body view{tag}: ran to the end", false); }
        finally
        {
            wrist.SlimeSetBody(false);
            wrist.SlimeSetGroup(SlimeGroups.All);
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            (data.SlimeVr, data.SlimePose, wrist.OnSlimeReset, wrist.OnSlimeResetParts, data.SlimeLying) = (s0, p0, r0, rp0, l0);
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    // Review 10-24, each style: the bones asked for only while the Body side is drawn (not under the guide, nor with no trackers); a
    // reset asked for the left leg is not sent by the guide's row (every tracker) - that asks again, and so does the Body side's after
    // it; modern and Cyber: the last reset's line clear of the chips (8 px), the left leg's twist big (24 px or more), the time left whole
    static void SideChecks(WristPanel wrist, PanelData data, Painter painter, string tag)
    {
        // the bones: the figure, no pose yet - asked; the guide, no trackers in the Body side's place - not
        data.SlimeVr = SlimeVrViewTools.Fake();
        data.SlimePose = Pose(false);
        wrist.CloseForTest();
        wrist.OpenForTest("slimevr");
        wrist.SlimeSetBody(true);
        wrist.SlimeSetGroup(SlimeGroups.All);
        wrist.Draw(data);
        bool figure = wrist.SlimeBodyOn;
        data.SlimePose = null;
        wrist.Draw(data);
        bool noPose = wrist.SlimeBodyOn;
        data.SlimeVr = Stepping();
        wrist.Draw(data);
        bool underGuide = wrist.SlimeBodyOn;
        data.SlimeVr = Copy(SlimeVrViewTools.Fake(), c => { c.Trackers.Clear(); c.Connected = 0; });
        wrist.Draw(data);
        bool empty = wrist.SlimeBodyOn;
        Want($"slimevr bones asked{tag}: the Body side drawn - the figure {figure}, no pose yet {noPose} (want True True); the guide in its place {underGuide}, no " +
             $"trackers {empty} (want False False) -", figure && noPose && !underGuide && !empty);
        // the left leg's Yaw reset asked; a guide comes - its Yaw reset (every tracker) asks again, sends nothing; the guide gone - the Body
        // side's asks again for the left leg; its second tap sends the left leg's yaw, once
        int sends = 0, sentGroup = -1;
        var (r0, rp0) = (wrist.OnSlimeReset, wrist.OnSlimeResetParts);
        wrist.OnSlimeReset = k => { sends++; sentGroup = SlimeGroups.All; return true; };
        wrist.OnSlimeResetParts = (k, g) => { sends++; sentGroup = g; return true; };
        try
        {
            data.SlimeVr = SlimeVrViewTools.Fake();
            data.SlimePose = Pose(false);
            wrist.CloseForTest();
            wrist.OpenForTest("slimevr");
            wrist.SlimeSetBody(true);
            wrist.SlimeSetGroup(SlimeGroups.LeftLeg);
            wrist.Draw(data);
            string asked = null, again = null, back = null;
            int armed1 = -1, armed2 = -1, sent1 = -1, sent2 = -1;
            if (wrist.ControlPoint(data, "wsvbr0") is { } yb)
            {
                SlimeVrViewTools.Tap(wrist, data, yb);
                asked = wrist.SlimeButton(data, 0);
                data.SlimeVr = Stepping();
                wrist.Draw(data);
                if (wrist.ControlPoint(data, "wsvr0") is { } yg)
                {
                    SlimeVrViewTools.Tap(wrist, data, yg);
                    (again, armed1, sent1) = (wrist.SlimeButton(data, 0), wrist.SlimeArmed, sends);
                }
                data.SlimeVr = SlimeVrViewTools.Fake();
                wrist.Draw(data);
                if (wrist.ControlPoint(data, "wsvbr0") is { } yb2)
                {
                    SlimeVrViewTools.Tap(wrist, data, yb2);
                    (back, armed2, sent2) = (wrist.SlimeButton(data, 0), wrist.SlimeArmed, sends);
                    SlimeVrViewTools.Tap(wrist, data, yb2);
                }
            }
            string left = Lang.T("Reset left leg?"), all = Lang.T("Reset?");
            Want($"slimevr second tap{tag}: the left leg asked '{asked}' (want '{left}'); the guide's Yaw reset tapped - '{again}', armed {armed1}, sent {sent1} " +
                 $"(want '{all}', 0, 0: asks for every tracker); the guide gone, the Body side's - '{back}', armed {armed2}, sent {sent2} (want '{left}', 0, 0); " +
                 $"its second tap: {sends} sent, group {sentGroup} (want 1, 2) -",
                 asked == left && again == all && armed1 == 0 && sent1 == 0 && back == left && armed2 == 0 && sent2 == 0 && sends == 1 && sentGroup == SlimeGroups.LeftLeg);
        }
        finally { (wrist.OnSlimeReset, wrist.OnSlimeResetParts) = (r0, rp0); }
        if (tag is "" or "_cyber")
        {
            var texts = new List<(string s, RectangleF r, float size)>();
            void Spy()
            {
                texts.Clear();
                painter.TextSpy = (s, r, _, size, _, _) => texts.Add((s, r, size));
                try { wrist.Draw(data); }
                finally { painter.TextSpy = null; }
            }
            // the Body side with the last reset: its line (whole: the header's has the count first) over the chips; the twist's biggest text
            data.SlimeVr = LastLeft();
            data.SlimePose = Pose(false);
            wrist.CloseForTest();
            wrist.OpenForTest("slimevr");
            wrist.SlimeSetBody(true);
            wrist.SlimeSetGroup(SlimeGroups.All);
            wrist.Draw(data);
            string last = wrist.SlimeLastText(data) ?? "?";
            Spy();
            float chipTop = wrist.ControlPoint(data, "wsvg0") is { } cp ? cp.Y - 20 : float.NaN;
            var lines = texts.Where(t => t.s.Equals(last, StringComparison.CurrentCultureIgnoreCase)).ToList();
            float bottom = lines.Count > 0 ? lines.Max(t => t.r.Bottom) : float.NaN;
            float big = texts.Where(t => t.s.StartsWith("32", StringComparison.Ordinal)).Select(t => t.size).DefaultIfEmpty(0).Max();
            Want($"slimevr body side{tag}: the last reset '{last}' drawn {lines.Count} time(s) (want 1), its line's bottom at {bottom:0.0} px, the chips' top at " +
                 $"{chipTop:0.0} px (want 8 px or more between); the left leg's twist {big:0} px (want 24 or more) -",
                 lines.Count == 1 && bottom + 8 <= chipTop && big >= 24);
            // the Trackers side with every time left: the ankles' (the longest lines) drawn whole; a frame of it (each line measured) 0 B
            data.SlimeVr = Forecasts();
            data.SlimePose = null;
            wrist.SlimeSetBody(false);
            wrist.Settle();
            for (int i = 0; i < 6; i++) wrist.Draw(data);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) wrist.Draw(data);
            long bytes = (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.SlimeAllocTypes("slimevr time left" + tag, () => wrist.Draw(data));
            string l4 = wrist.SlimeLeftText(data, 4) ?? "?", l5 = wrist.SlimeLeftText(data, 5) ?? "?";
            Spy();
            var r4 = texts.FirstOrDefault(t => t.s.Contains(l4, StringComparison.CurrentCultureIgnoreCase));
            var r5 = texts.FirstOrDefault(t => t.s.Contains(l5, StringComparison.CurrentCultureIgnoreCase));
            Want($"slimevr time left{tag}: the ankles' '{l4}' in '{r4.s}', '{l5}' in '{r5.s}' (want each there, nothing cut); a frame {bytes} B (want 0) -",
                 r4.s != null && !r4.s.Contains('…') && r5.s != null && !r5.s.Contains('…') && bytes == 0);
        }
        wrist.SlimeSetBody(false);
        wrist.SlimeSetGroup(SlimeGroups.All);
        wrist.CloseForTest();
    }

    // the layout check's extra states of the opened view (Tools.LayoutCheckRun): the Body side standing, lying, a group asking, the last
    // reset under the legs (the upper body picked: the pelvis lit with it); the trackers' time left; the guide
    public static List<(string name, Action<WristPanel, PanelData> set)> LayoutStates() => new()
    {
        ("body", (w, d) => { d.SlimeVr = SlimeVrViewTools.Fake(); d.SlimePose = Pose(false); w.SlimeSetBody(true); }),
        ("body_last", (w, d) => { d.SlimeVr = LastLeft(); d.SlimePose = Pose(false); w.SlimeSetBody(true); w.SlimeSetGroup(SlimeGroups.Upper); }),
        ("trackers_left", (w, d) => { d.SlimeVr = Forecasts(); d.SlimePose = null; }),
        ("body_lying", (w, d) => { d.SlimeVr = SlimeVrViewTools.Fake(trouble: true); d.SlimePose = Pose(true, rolled: true); w.SlimeSetBody(true); }),
        ("body_nopose", (w, d) => { d.SlimeVr = SlimeVrViewTools.Fake(); d.SlimePose = null; w.SlimeSetBody(true); }),
        ("mount_needfull", (w, d) =>
        {
            d.SlimeVr = NeedFull();
            w.Draw(d);
            if (w.ControlPoint(d, "wsvr2") is { } m) SlimeVrViewTools.Tap(w, d, m);
        }),
        ("mount_step", (w, d) => d.SlimeVr = Stepping()),
        ("mount_pose", (w, d) => d.SlimeVr = Posing()),
        ("mount_failed", (w, d) => d.SlimeVr = Copy(Stepping(), c => (c.ResetState, c.ResetPhase, c.ResetFailed) = (2, 7, true))),
    };

    // every style's Body side and guide side by side, one row a style
    public static void PreviewSheet(string dir)
    {
        if (Preview.Count == 0) return;
        const int W = 320, H = 220, Gap = 10, Label = 22;
        using var sheet = new Bitmap(Shots.Length * W + (Shots.Length + 1) * Gap, Preview.Count * (H + Label + Gap) + Gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 32, 34, 40));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int i = 0; i < Preview.Count; i++)
            {
                var (t, files) = Preview[i];
                float y = Gap + i * (H + Label + Gap);
                g.DrawString($"{(t.Length == 0 ? "modern" : t[1..])}: Body (standing, the left leg twisted), lying (on the back), the left leg's yaw asking, its last reset; the " +
                             "guide - a full reset first, the step recording, the pose counting, done (made-up)", font, Brushes.White, Gap, y + 3);
                for (int k = 0; k < files.Length; k++)
                    if (File.Exists(files[k])) using (var b = new Bitmap(files[k])) g.DrawImage(b, Gap + k * (W + Gap), y + Label, W, H);
            }
        }
        string path = Path.Combine(dir, "slimevr_body_preview.png");
        sheet.Save(path, ImageFormat.Png);
        Log.Info("slimevr: the Body side and the guide in every style: " + path);
    }
}

// Byte samples made the servers' own way (SlimeVR's repositories have no fixtures of these): Bones22 / Step22 / Settings22 / Guards22
// encoded by SlimeVR v22.0.0-rc.2's own SolarXR classes from its slimevr.jar (sx/SxBones22.java, run offline in an idle java of its own
// with its bundled runtime: no server started, nothing on the network) - the bones of sx/skeleton.py's body (the pelvis turned 20°, the
// left thigh twisted +32°, the left shin +28°) as createBone fills them with Orbit's mask; a step mounting recording; Erik's reset
// settings (STEP); the guards with mounting NEEDS_FULL_RESET. Dangle21 / Bones21 as SlimeVR up to v21 builds them, call for call
// (sx/v21bones.py: DataFeedBuilder.createBonesData / createServerGuard, DataFeedHandler.sendDataFeedUpdate with two feeds asked - each
// bundle with a slot for both, the one not due a 0 offset).
static class SlimeBodySamples
{
    public const string Bones22 = 
        "0c00000000000600080004000600000004000000010000000c00000008000a0009000400080000001000000000030a000c000000080007000a00000000000001040000001500000024040000e0030000" +
        "ac030000780300004403000010030000dc020000a802000074020000400200000c020000d8010000a4010000700100003c01000008010000d4000000a00000006c000000380000000400000040fcffff" +
        "7db3113f7b146e3feb1eb9be7db3113fae47813feb1eb9be00000000d5d0313e000000005d1c7c3f0000001370fcffff7db3113fae47813feb1eb9be7db3113f5c8fa23feb1eb9be00000000d5d0313e" +
        "000000005d1c7c3f00000011a0fcffff7db3113f5c8fa23feb1eb9be7db3113f6666c63feb1eb9be00000000d5d0313e000000005d1c7c3f0000000fd0fcffff7db3113f6666c63feb1eb9becdcccc3e" +
        "1f85cb3f9a9999be3f87de3d24b40a3eacc01d3f2ea8443f0000001500fdffff2b14f13ecdcccc3d9488f8be47ac053fcdcccc3d745db0be00000000d5d0313e000000005d1c7c3f0000000d30fdffff" +
        "47ac053fcdcccc3d745db0be47ac053f14ae073f745db0be00000000d5d0313e000000005d1c7c3f0000000b60fdffff47ac053f14ae073f745db0be47ac053fec51783f745db0be00000000d5d0313e" +
        "000000005d1c7c3f0000000990fdffff41656c3e7b146e3f912874be41656c3eae47813f912874be00000000d5d0313e000000005d1c7c3f00000012c0fdffff41656c3eae47813f912874be41656c3e" +
        "5c8fa23f912874be00000000d5d0313e000000005d1c7c3f00000010f0fdffff41656c3e5c8fa23f912874be41656c3e6666c63f912874be00000000d5d0313e000000005d1c7c3f0000000e20feffff" +
        "41656c3e6666c63f912874becdcccc3e1f85cb3f9a9999be3f87debd24b40a3eacc01dbf2ea8443f0000001450feffff5f5c2a3ecdcccc3d7139b6be0b418e3ecdcccc3dbfd582be00000000c83fd03e" +
        "000000001ede693f0000000c80feffff0b418e3ecdcccc3dbfd582be0b418e3e14ae073fbfd582be00000000c83fd03e000000001ede693f0000000ab0feffff0b418e3e14ae073fbfd582be0b418e3e" +
        "ec51783fbfd582be000000002d72e03e000000005d17663f00000008e0feffffcdcccc3eec51783f9a9999becdcccc3ec3f5883f9a9999be00000000d5d0313e000000005d1c7c3f0000000710ffffff" +
        "cdcccc3ec3f5883f9a9999becdcccc3e8fc2953f9a9999be00000000d5d0313e000000005d1c7c3f0000000640ffffffcdcccc3e8fc2953f9a9999becdcccc3eb81ea53f9a9999be00000000d5d0313e" +
        "000000005d1c7c3f0000000570ffffffcdcccc3eb81ea53f9a9999becdcccc3e9a99b93f9a9999be00000000d5d0313e000000005d1c7c3f00000004a0ffffffcdcccc3e9a99b93f9a9999becdcccc3e" +
        "1f85cb3f9a9999be00000000d5d0313e000000005d1c7c3f00000003d0ffffffcdcccc3e1f85cb3f9a9999becdcccc3eec51d83f9a9999be00000000d5d0313e000000005d1c7c3f0000000210003000" +
        "2f0000001c0000001000040010000000cdcccc3eec51d83f9a9999becdcccc3eb81ee53f9a9999be00000000d5d0313e000000005d1c7c3f00000001";
    public const string Step22 = 
        "0c0000000800080000000400080000000400000001000000100000000c000a0000000000090004000c0000001400000000040e000a00090000000000080004000e0000000c0000000202060008000700" +
        "0600000000000002";
    public const string Settings22 = "0c0000000800080000000400080000000400000001000000100000000c000c00000000000b0004000c0000001800000000000018100008000000000000000700000006001000000000000301";
    public const string Guards22 = 
        "0c00000000000600080004000600000004000000010000000c00000008000c000b0004000800000014000000000000030c00080000000000000004000c0000000c000000080008000000070008000000" +
        "00000001";
    public const string Dangle21 = 
        "10000000000000000000060008000400060000000400000002000000100000009803000008000c000700080008000000000000031400000010000c000400000000000000000008001000000018000000" +
        "0c0000000800080000000700080000000000000104000000cc020000f8010000b80000000400000052fdffff0c0000002400000000000003010000002c00000000001200140013000000080006000000" +
        "0c000b00120000000000c4ff080000409a9979400000000116feffff00000002100000000400000052fdffff000000030efeffff0000000000000000000000000000803f000000008104353f00000000" +
        "8104353f000104000800000000000105150000007564703a2f2f3139322e3136382e312e3132332f30000e0014000f0010000000080004000e0000002000000044000000000000020400000008000000" +
        "4c656674206c6567000000000200000044000000ac0000001c0020001f00000014001200000018001700000000000000000004001c000000008eead001000000000000000000b9ff1e0000360ad77340" +
        "00000001f2feffff00000002180000000c00000008000800060007000800000000000201f2feffff0000000000000000000000000000803f000000008104353f000000008104353f0001040008000000" +
        "00000108150000007564703a2f2f3139322e3136382e312e3132322f310000005effffff0000000210000000040000009afeffff0000000256ffffff0000000000000000000000000000803f00000000" +
        "8104353f000000008104353f000104000800000000000106150000007564703a2f2f3139322e3136382e312e3132322f300000003effffff0c0000002400000000000001010000003400000000001200" +
        "1200110000000800060000000c000b00120000000000d0ff0c00005752b87e4000010a0010000c00080007000a0000000000000228000000040000006effffff00011a00300026002f00000014002e00" +
        "0000250028000000000004001a0000000000000000000000000000000000803f000000008104353f000000008104353f000104000800000000000103150000007564703a2f2f3139322e3136382e312e" +
        "3132312f30000e0010000f0000000000080004000e0000000c0000001800000000000000010000001c00000000000600060005000600000000010a0012000c00080007000a0000000000000234000000" +
        "0c000000000006000800070006000000000000001c00100000000f00000000000e0000000000080000000000000007001c00000000000001080000000000010103000000484d4400";
    public const string Bones21 = 
        "0c0000000000060008000400060000000400000002000000100400000c00000008000e000700080008000000000000031400000000000e000c00000000000800000007000e0000000000000104000000" +
        "16000000ac03000074030000480300001c030000f0020000c4020000980200006c0200004002000014020000e8010000bc0100009001000064010000380100000c010000e0000000b400000088000000" +
        "5c0000003000000004000000b8fcffff0ad7a33d000000137db3113fae47813feb1eb9be00000000d5d0313e000000005d1c7c3fe0fcffffb81e853e0000000f7db3113f5c8fa23feb1eb9be00000000" +
        "d5d0313e000000005d1c7c3f08fdffff295c8f3e000000117db3113f6666c63feb1eb9be00000000d5d0313e000000005d1c7c3f30fdffffffd03c3e00000015cdcccc3e1f85cb3f9a9999be3f87de3d" +
        "24b40a3eacc01d3f2ea8443f58fdffff9a99193e0000000b47ac053fcdcccc3d745db0beed44323f2c78fb3d2c78fbbded44323f80fdfffff628dc3e0000000947ac053f14ae073f745db0be00000000" +
        "d5d0313e000000005d1c7c3fa8fdffffae47e13e0000000747ac053fec51783f745db0be00000000d5d0313e000000005d1c7c3fd0fdffff0ad7a33d0000001241656c3eae47813f912874be00000000" +
        "d5d0313e000000005d1c7c3ff8fdffffb81e853e0000000e41656c3e5c8fa23f912874be00000000d5d0313e000000005d1c7c3f20feffff295c8f3e0000001041656c3e6666c63f912874be00000000" +
        "d5d0313e000000005d1c7c3f48feffffffd03c3e00000014cdcccc3e1f85cb3f9a9999be3f87debd24b40a3eacc01dbf2ea8443f70feffff9a99193e0000000a0b418e3ecdcccc3dbfd582be915e253f" +
        "1f41933e1f4193be915e253f98fefffff628dc3e000000080b418e3e14ae073fbfd582be00000000c83fd03e000000001ede693fc0feffffae47e13e000000060b418e3eec51783fbfd582be00000000" +
        "2d72e03e000000005d17663fe8feffffb81e053e00000018cdcccc3eec51783f9a9999be2c78fb3d2c78fb3ded44323fed44323f10ffffffb81e053e00000017cdcccc3eec51783f9a9999be2c78fbbd" +
        "2c78fb3ded4432bfed44323f38ffffffcdcccc3d00000005cdcccc3ec3f5883f9a9999be00000000d5d0313e000000005d1c7c3f60ffffffae47613e00000004cdcccc3eb81ea53f9a9999be00000000" +
        "d5d0313e000000005d1c7c3f88ffffff0ad7233e00000003cdcccc3e9a99b93f9a9999be00000000d5d0313e000000005d1c7c3fb0ffffff295c0f3e00000016cdcccc3e1f85cb3f9a9999be00000000" +
        "d5d0313e000000005d1c7c3fd8ffffffcdcccc3d00000002cdcccc3eec51d83f9a9999be00000000d5d0313e000000005d1c7c3f0c0028000b00180004000c000c000000cdcccc3d00000001cdcccc3e" +
        "b81ee53f9a9999be00000000d5d0313e000000005d1c7c3f";
}
