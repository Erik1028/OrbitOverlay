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
// look for a moment (Hold) before it lets go, then everything goes by the head again (VrSession.Gaze). The map is looked for while VR
// is connected, again every few seconds when it is not there (the streamer not running, "Forward tracking data" off).
public sealed class EyeGaze : IDisposable
{
    public const string MapName = "VirtualDesktop.BodyState";
    public const double Hold = 0.25;                // seconds a blink keeps the last look
    // the look smoothed like the laser (a One Euro filter on its direction): resting eyes tremble and the tracker is noisy - filtered at
    // ~1.5 Hz; a saccade (hundreds of degrees a second) opens it up so the look jumps with the eyes, not after them
    public const float MinCutoff = 1.5f, Beta = 0.06f;  // Hz, Hz per degree a second
    public const float MinConfidence = 0.3f;

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

    // --snapshot: a made-up look instead of the map
    public Func<(bool valid, Vector3 dir)> Fake;

    // a frame: read the map (opened when it appears); now: Clock.Now
    public void Update(double now, bool wanted)
    {
        if (Fake != null)
        {
            var (v, d) = Fake();
            Valid = v;
            if (v) HeadDir = Vector3.Normalize(d);
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

    // one reading (the map's, or the checks' made-up one): the look, or a blink's hold
    public void Feed(in FaceState s, double now)
    {
        Last = s;
        if (Take(s, out var dir, out var origin, out float conf))
        {
            dir = Smooth(dir, now);
            HeadDir = dir;
            HeadOrigin = origin;
            Confidence = conf;
            _lastGood = now;
            Valid = true;
        }
        else Valid = now - _lastGood < Hold;        // a blink: the last look a moment longer
    }

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

    // the look in the world, from where the head is now
    public Vector3 WorldDir(in Matrix4x4 hmd) => Vector3.Normalize(Vector3.TransformNormal(HeadDir, hmd));
    public Vector3 WorldOrigin(in Matrix4x4 hmd) => Vector3.Transform(HeadOrigin, hmd);

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
        _view?.Dispose(); _view = null;
        _map?.Dispose(); _map = null;
    }

    public void Dispose() => Close();
}
