using System;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Orbit;

// Where your eyes look (Erik 10-18: "can the overlay gain anything from my eye tracking?" - "do the eye tracking support"). Virtual
// Desktop publishes the headset's face and eye tracking for VRCFaceTracking's module in a shared memory map, VirtualDesktop.BodyState:
// a FaceState (the module's own layout, read from its metadata) - 70 expression weights, then each eye's validity, pose (an
// orientation and a position, relative to the head) and confidence. OrbitOverlay only reads it (VRCFaceTracking and VRChat are not
// touched) once a frame - 360 bytes copied out of the map, no call to anyone.
//
// What it gives: the look's direction in the world (both eyes averaged), Valid while the eyes are tracked; a blink holds the last
// look for a while (HoldShut; Hold once the tracker says not tracked) before it lets go, then everything goes by the head again
// (VrSession.Gaze). The map is looked for while VR
// is connected, again every few seconds when it is not there (the streamer not running, "Forward tracking data" off).
//
// New readings only (Erik 10-24: "az eye tracking kurzor pontosságán még lehetne javítani"): the map has no time stamp and Virtual
// Desktop writes it at its own rate, so a read that finds the same poses as the last one is not a new look - Fresh says when it is,
// SampleAt when it was first seen. The smoothing runs on new readings only (a repeated one looked like a pause, then a jump). Shut
// eyes (EyesClosed, both past BlinkAt) are no look either, nor the moment after they open (BlinkTail): whatever the tracker says
// through the lids went straight into the cursor.
// Two holds (10-24 review): the tracker saying the eyes are not tracked lets go after Hold; shut eyes and a stream that stalls keep the
// last look HoldShut - closed eyes look nowhere else, and losing the eyes moves everything to the head (gaze focus reshuffled the row by
// the head, the eye cursor gave the screens back to the hand's laser) after a 0.3 s rest of the eyes, a laugh, a Wi-Fi hiccup. And the
// lids gate the readings only ShutMax, after that only a reading that jumps (a blink threw the look 13 deg): a squint or drowsy lids
// keep EyesClosed up - it is how open the lids are, 0.81-1.00 already from looking 32-38 deg down - and the look does not jump.
// (The --eyetest of 10-24: 72 new readings a second, a write every 11.5 ms mostly - some 90 Hz frames get none -, both eyes valid and
// sure all the time, the raw look's step from reading to reading 0.34 deg RMS.)
public sealed class EyeGaze : IDisposable
{
    public const string MapName = "VirtualDesktop.BodyState";
    public const double Hold = 0.25;                // seconds the last look is kept once the tracker says the eyes are not tracked
    public const double HoldShut = 1.5;             // ... while the eyes are shut, or no new reading comes (a stream that stalls)
    // the look smoothed like the laser (a One Euro filter on its direction): resting eyes tremble and the tracker is noisy - filtered at
    // ~2.5 Hz; a saccade (hundreds of degrees a second) opens it up so the look jumps with the eyes, not after them (gaze focus, the
    // watch's glance, the dim; the eye cursor takes the raw look - Vr/EyeSpot.cs)
    public const float MinCutoff = 2.5f, Beta = 0.1f;   // Hz, Hz per degree a second (Erik 10-19 "more sensitive": was 1.5 / 0.06)
    public const float MinConfidence = 0.3f;
    // seconds a reading is older than the moment it is first seen (the Quest's cameras, the tracker, Virtual Desktop's stream): the look
    // goes on the head of then (VrSession.HeadAt, the head as it was) - put on the head SteamVR predicts for the frame it ran ahead of a
    // head turn and slid back (~2 deg at 30 deg/s). EyeLag logs the measured value; once a few sessions have logged it, it goes here.
    public const double Latency = 0.040;
    public const float BlinkAt = 0.7f;              // EyesClosed on both eyes from here: shut
    public const double BlinkTail = 0.04;           // seconds after they open that are skipped too
    public const double ShutMax = 0.35;             // seconds shut lids gate every reading; after that only one 3 deg off the one before
    const float ShutJumpCos = 0.99863f;             // (cos 3 deg)
    const int EyesClosedL = 12, EyesClosedR = 13;   // the VD module's Expressions enum (EyesLookDown is 14 / 15), read from its metadata 10-24
    // ... unless the reading looks further down than 25 deg in the head: the --eyetest of 10-24 (Erik in VR) had EyesClosed 0.81-1.00 on
    // both eyes while the eyes looked 32-38 deg down (the lids follow the look) - as shut as its blink (1.00 / 1.00, the look thrown 13 deg
    // up for that reading); the taskbar must not count as a blink
    const float BlinkDownY = -0.4226f;              // sin -25 deg

    [StructLayout(LayoutKind.Sequential)]
    public struct Pose { public Quaternion Orientation; public Vector3 Position; }

    // VirtualDesktop.FaceTracking.FaceState as the module lays it out (sequential, 360 bytes)
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FaceState
    {
        public byte FaceFlags;
        public byte EyeFollowingBlendshapesValid;
        public fixed float ExpressionWeights[70];
        public fixed float ExpressionConfidences[2];
        public byte LeftEyeIsValid;
        public byte RightEyeIsValid;
        public Pose LeftEyePose;
        public Pose RightEyePose;
        public float LeftEyeConfidence;
        public float RightEyeConfidence;
    }

    public static readonly int Size = Marshal.SizeOf<FaceState>();

    MemoryMappedFile _map;
    MemoryMappedViewAccessor _view;
    double _nextTry;
    double _lastGood = -10;

    public bool Present => _view != null;          // the map is there (the streamer runs)
    public bool Valid { get; private set; }         // the eyes are tracked now (or were a blink ago)
    public Vector3 HeadDir { get; private set; } = -Vector3.UnitZ;      // the look, relative to the head (-Z ahead)
    public Vector3 HeadOrigin { get; private set; }                     // between the eyes, relative to the head
    public float Confidence { get; private set; }
    public FaceState Last;                           // (the probe: what was read last)
    public bool Fresh { get; private set; }         // this frame's read was a new look
    public bool NewReading { get; private set; }    // ... a new reading at all (shut eyes, an unsure one too: the probe's rate)
    public double SampleAt { get; private set; }    // when the last new look was first seen (Clock)
    public Vector3 RawDir { get; private set; } = -Vector3.UnitZ;       // the last new look, unsmoothed, relative to the head
    public int PerSec { get; private set; }         // new readings a second (Virtual Desktop's own rate)

    Pose _pl, _pr;                                   // the last reading, to see a new one
    float _cl, _cr;
    byte _vbl, _vbr;
    bool _have;
    int _count;
    double _countAt, _closedAt = -10;
    bool _lids, _untracked;                          // the last new reading: shut lids / not a look (the tracker: not tracked, unsure)
    double _shutSince = -10;                         // when the lids shut (the first of a run of shut readings)
    Vector3 _lastLook = -Vector3.UnitZ;              // the last new reading's look, gated or not (ShutJumpCos)

    // --snapshot: a made-up look instead of the map
    public Func<(bool valid, Vector3 dir)> Fake;

    // a frame: read the map (opened when it appears); now: Clock.Now
    public void Update(double now, bool wanted)
    {
        Fresh = NewReading = false;
        if (Fake != null)
        {
            var (v, d) = Fake();
            Valid = v;
            Fresh = v;
            SampleAt = now;
            if (v) HeadDir = RawDir = Vector3.Normalize(d);
            return;
        }
        if (!wanted) { Close(); Valid = false; return; }
        if (_view == null)
        {
            if (now < _nextTry) { Valid = false; return; }
            _nextTry = now + 5;
            if (!Open()) { Valid = false; return; }
        }
        FaceState s;
        try { _view.Read(0, out s); }
        catch (Exception) { Close(); Valid = false; return; }
        Feed(s, now);
    }

    // one read (the map's, or the checks' made-up one): a new look, or the last one held - until Hold once the tracker says not tracked,
    // until HoldShut through shut eyes and the same reading again (the stream stalled)
    public void Feed(in FaceState s, double now)
    {
        Last = s;
        Fresh = false;
        NewReading = !_have || !Same(s);
        if (NewReading)
        {
            _have = true;
            _pl = s.LeftEyePose; _pr = s.RightEyePose;
            _cl = s.LeftEyeConfidence; _cr = s.RightEyeConfidence;
            _vbl = s.LeftEyeIsValid; _vbr = s.RightEyeIsValid;
            _count++;
            bool look = Take(s, out var dir, out var origin, out float conf);
            _untracked = !look;
            bool lids = look && Closed(s) && dir.Y > BlinkDownY;               // shut lids (looking far down they follow the look: a look)
            if (lids && !_lids) _shutSince = now;
            _lids = lids;
            bool jump = look && Vector3.Dot(dir, _lastLook) < ShutJumpCos;
            if (look) _lastLook = dir;
            if (lids && (now - _shutSince < ShutMax || jump)) _closedAt = now;      // a blink (after ShutMax: only a reading thrown off)
            else if (look && now - _closedAt >= BlinkTail)
            {
                RawDir = dir;
                HeadDir = Smooth(dir, now);
                HeadOrigin = origin;
                Confidence = conf;
                _lastGood = SampleAt = now;
                Fresh = true;
            }
        }
        if (now - _countAt >= 1) { PerSec = _count; _count = 0; _countAt = now; }
        Valid = now - _lastGood < (_untracked ? Hold : HoldShut);      // a blink, a stall: the last look a while longer
    }

    // the same reading as the last one: Virtual Desktop has not written a new one since (exact: a new one always differs somewhere)
    bool Same(in FaceState s) =>
        s.LeftEyePose.Orientation == _pl.Orientation && s.LeftEyePose.Position == _pl.Position &&
        s.RightEyePose.Orientation == _pr.Orientation && s.RightEyePose.Position == _pr.Position &&
        s.LeftEyeConfidence == _cl && s.RightEyeConfidence == _cr && s.LeftEyeIsValid == _vbl && s.RightEyeIsValid == _vbr;

    // both eyes shut (a blink): what the tracker says then is not a look
    static unsafe bool Closed(in FaceState s) =>
        s.ExpressionWeights[EyesClosedL] >= BlinkAt && s.ExpressionWeights[EyesClosedR] >= BlinkAt;

    Vector3 _smooth;
    double _smoothAt = -1;
    float _speed;                                   // degrees a second, itself smoothed

    Vector3 Smooth(Vector3 raw, double now)
    {
        float dt = (float)(now - _smoothAt);
        if (_smoothAt < 0 || dt <= 0 || dt > 0.25f) { _smoothAt = now; _smooth = raw; _speed = 0; return raw; }     // (after a gap: as it is)
        _smoothAt = now;
        float deg = MathF.Acos(Math.Clamp(Vector3.Dot(_smooth, raw), -1, 1)) * 180 / MathF.PI;
        _speed += (deg / dt - _speed) * Alpha(dt, 1);
        float a = Alpha(dt, MinCutoff + Beta * _speed);
        _smooth = Vector3.Normalize(Vector3.Lerp(_smooth, raw, a));
        return _smooth;
    }

    static float Alpha(float dt, float cutoff) => 1 - MathF.Exp(-2 * MathF.PI * cutoff * dt);

    // the look from one reading: both eyes that are valid and sure enough, averaged; false when neither is (or the numbers are not a
    // pose - a reading torn by the writer, another program's map)
    public static bool Take(in FaceState s, out Vector3 dir, out Vector3 origin, out float conf)
    {
        dir = -Vector3.UnitZ; origin = Vector3.Zero; conf = 0;
        Vector3 sum = Vector3.Zero, at = Vector3.Zero;
        int n = 0;
        if (s.LeftEyeIsValid != 0 && Eye(s.LeftEyePose, s.LeftEyeConfidence, out var dl)) { sum += dl; at += s.LeftEyePose.Position; conf += s.LeftEyeConfidence; n++; }
        if (s.RightEyeIsValid != 0 && Eye(s.RightEyePose, s.RightEyeConfidence, out var dr)) { sum += dr; at += s.RightEyePose.Position; conf += s.RightEyeConfidence; n++; }
        if (n == 0 || sum.LengthSquared() < 1e-6f) return false;
        dir = Vector3.Normalize(sum);
        origin = at / n;
        conf /= n;
        if (origin.LengthSquared() > 0.04f) origin = Vector3.Zero;      // (not an eye's place in the head: the middle of the head)
        return dir.Z < 0.2f;                                              // (a look behind you: not an eye)
    }

    static bool Eye(in Pose p, float conf, out Vector3 dir)
    {
        dir = default;
        var q = p.Orientation;
        float len = q.Length();
        if (!float.IsFinite(len) || MathF.Abs(len - 1) > 0.05f) return false;
        if (!float.IsFinite(conf) || conf < MinConfidence) return false;
        dir = Vector3.Transform(-Vector3.UnitZ, Quaternion.Normalize(q));
        return true;
    }

    // the look in the world, from where the head is (the head of the reading's time: VrSession.HeadAt)
    public Vector3 WorldDir(in Matrix4x4 hmd) => Vector3.Normalize(Vector3.TransformNormal(HeadDir, hmd));
    public Vector3 WorldOrigin(in Matrix4x4 hmd) => Vector3.Transform(HeadOrigin, hmd);
    public Vector3 RawWorldDir(in Matrix4x4 hmd) => Vector3.Normalize(Vector3.TransformNormal(RawDir, hmd));

    bool Open()
    {
        try
        {
            _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            _view = _map.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read);
            Log.Info("eyes: Virtual Desktop's eye tracking found");
            return true;
        }
        catch (Exception)
        {
            Close();
            return false;
        }
    }

    void Close()
    {
        if (_view != null) Log.Info("eyes: Virtual Desktop's eye tracking gone");
        PerSec = 0;
        _view?.Dispose(); _view = null;
        _map?.Dispose(); _map = null;
    }

    public void Dispose() => Close();
}
