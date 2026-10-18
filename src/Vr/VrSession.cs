using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Valve.VR;

namespace Orbit;

public enum Hand { Left = 0, Right = 1 }

public struct HandState
{
    public bool PoseValid;
    public Matrix4x4 Aim;               // pointing pose in standing space, -Z = ray
    public bool Click, ClickPrev, Grab, GrabPrev, Secondary, SecondaryPrev;
    public Vector2 Stick;
    public bool ClickPressed => Click && !ClickPrev;
    public bool ClickReleased => !Click && ClickPrev;
    public bool GrabPressed => Grab && !GrabPrev;
    public bool SecondaryPressed => Secondary && !SecondaryPrev;
    public bool SecondaryReleased => !Secondary && SecondaryPrev;
}

// SteamVR connection: init/shutdown, device poses, SteamVR Input actions, events.
public sealed class VrSession : IDisposable
{
    public const uint InvalidIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
    public bool Connected { get; private set; }
    public CVRSystem Sys { get; private set; }
    public CVROverlay Ov { get; private set; }
    public CVRCompositor Comp { get; private set; }
    CVRInput _in;

    readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    public readonly Matrix4x4[] Device = new Matrix4x4[OpenVR.k_unMaxTrackedDeviceCount];
    public readonly bool[] DeviceValid = new bool[OpenVR.k_unMaxTrackedDeviceCount];
    public readonly bool[] DeviceConnected = new bool[OpenVR.k_unMaxTrackedDeviceCount];      // (the poses' own flag: no call to ask)
    public uint LeftIndex = InvalidIndex, RightIndex = InvalidIndex;
    public float DisplayHz = 90;
    public int AdapterIndex = -1;
    public readonly HandState[] Hands = new HandState[2];
    public string InputStatus = "not set up";

    ulong _set;
    readonly ulong[] _pointer = new ulong[2], _click = new ulong[2], _grab = new ulong[2],
        _secondary = new ulong[2], _scroll = new ulong[2], _haptic = new ulong[2];
    bool _inputOk;
    readonly VRActiveActionSet_t[] _sets = new VRActiveActionSet_t[1];
    // Per hand: while its laser uses one of our panels, our action set on that hand runs at SteamVR's global overlay
    // priority, so the game (VRChat) does not get the trigger, grip, stick or B / Y it would otherwise also see.
    // Set by the app every frame; one entry per hand, restricted to that hand.
    public readonly bool[] Suppress = new bool[2];
    // nothing of Orbit's shows and no hand is kept from the game (OrbitApp sets it each frame): the buttons and the aim read 30 times a
    // second, the hands' roles once a second - the poses (the watch's raise) still every frame. 11 calls to SteamVR a frame were made
    // for an Orbit nobody saw (Erik 10-18: "above all while it is off"); the first frame anything shows reads everything again.
    public bool Idle;
    int _idleTick;
    // where you look this frame (OrbitApp sets it after Update): your eyes' look in the world while eye tracking has them
    // (Sys/EyeGaze.cs), else the head's forward - gaze focus, smart capture, the watch's glance and the dim go by it
    public Vector3 Gaze = -Vector3.UnitZ, GazeOrigin;
    public bool EyesValid;
    readonly ulong[] _handSource = new ulong[2];
    readonly VRActiveActionSet_t[] _handSets = new VRActiveActionSet_t[2];
    bool _perHandFailed;
    const int OverlayGlobalPriority = 0x01000000;      // k_nActionSetOverlayGlobalPriorityMin
    float _vsyncToPhotons = 0.01f;
    float _predict;

    public Matrix4x4 Hmd => Device[0];

    // false a few seconds after the headset reports it is not being worn (proximity / standby);
    // runtimes that don't report it stay "worn"
    public bool HeadsetWorn { get; private set; } = true;
    double _offSince = -1;

    void UpdateWorn()
    {
        var lvl = Sys.GetTrackedDeviceActivityLevel(0);
        bool on = lvl is EDeviceActivityLevel.k_EDeviceActivityLevel_UserInteraction or EDeviceActivityLevel.k_EDeviceActivityLevel_Unknown;
        double now = Clock.Now;
        if (on) { _offSince = -1; if (!HeadsetWorn) { HeadsetWorn = true; Log.Info("headset back on"); } }
        else
        {
            if (_offSince < 0) _offSince = now;
            if (HeadsetWorn && now - _offSince > 3) { HeadsetWorn = false; Log.Info($"headset off ({lvl}) - pausing"); }
        }
    }
    public uint HandIndex(Hand h) => h == Hand.Left ? LeftIndex : RightIndex;
    public bool HmdValid => DeviceValid[0];

    static System.Diagnostics.Process _vrserver;

    public static bool RuntimeRunning()
    {
        // VR_Init with an overlay app type would START SteamVR; only connect when it already runs. Its process is kept once found and
        // asked whether it has ended (a wait of 0 ms) - listing every process on the PC every 2 s took ms on the VR loop's thread.
        try
        {
            if (_vrserver != null)
            {
                bool gone;
                try { gone = _vrserver.HasExited; }
                catch (Exception) { gone = true; }        // (not allowed to ask: looked up again below)
                if (!gone) return true;
                _vrserver.Dispose();
                _vrserver = null;
            }
            var found = System.Diagnostics.Process.GetProcessesByName("vrserver");
            foreach (var p in found)
            {
                if (_vrserver == null) { try { _ = p.HasExited; _vrserver = p; continue; } catch (Exception) { } }
                p.Dispose();
            }
            return found.Length > 0 && OpenVR.IsRuntimeInstalled();
        }
        catch { return false; }
    }

    public bool TryConnect(string actionManifestPath, out string error, string appKey = null)
    {
        error = null;
        EVRInitError e = EVRInitError.None;
        OpenVR.Init(ref e, EVRApplicationType.VRApplication_Overlay);
        if (e != EVRInitError.None) { error = e.ToString(); return false; }
        Sys = OpenVR.System; Ov = OpenVR.Overlay; Comp = OpenVR.Compositor; _in = OpenVR.Input;
        if (Sys == null || Ov == null) { error = "interfaces unavailable"; OpenVR.Shutdown(); return false; }

        int adapter = -1;
        Sys.GetDXGIOutputInfo(ref adapter);
        AdapterIndex = adapter;
        DisplayHz = FloatProp(0, ETrackedDeviceProperty.Prop_DisplayFrequency_Float, 90);
        _vsyncToPhotons = FloatProp(0, ETrackedDeviceProperty.Prop_SecondsFromVsyncToPhotons_Float, 0.01f);
        // started by hand but registered with SteamVR: claim the app key so its bindings apply
        try
        {
            if (appKey != null && OpenVR.Applications.IsApplicationInstalled(appKey))
                OpenVR.Applications.IdentifyApplication(0, appKey);
        }
        catch (Exception ex) { Log.Warn("IdentifyApplication: " + ex.Message); }
        SetupInput(actionManifestPath);
        Connected = true;
        Log.Info($"SteamVR connected: adapter {adapter}, {DisplayHz:0.#} Hz, input: {InputStatus}");
        return true;
    }

    void SetupInput(string path)
    {
        _inputOk = false;
        if (_in == null) { InputStatus = "IVRInput missing"; return; }
        var e = _in.SetActionManifestPath(path);
        if (e != EVRInputError.None) { InputStatus = "manifest: " + e; Log.Warn("SetActionManifestPath: " + e); return; }
        _in.GetActionSetHandle("/actions/orbit", ref _set);
        string[] side = { "left", "right" };
        for (int h = 0; h < 2; h++)
        {
            _in.GetActionHandle($"/actions/orbit/in/pointer_{side[h]}", ref _pointer[h]);
            _in.GetActionHandle($"/actions/orbit/in/click_{side[h]}", ref _click[h]);
            _in.GetActionHandle($"/actions/orbit/in/grab_{side[h]}", ref _grab[h]);
            _in.GetActionHandle($"/actions/orbit/in/secondary_{side[h]}", ref _secondary[h]);
            _in.GetActionHandle($"/actions/orbit/in/scroll_{side[h]}", ref _scroll[h]);
            _in.GetActionHandle($"/actions/orbit/out/haptic_{side[h]}", ref _haptic[h]);
        }
        _in.GetInputSourceHandle("/user/hand/left", ref _handSource[0]);
        _in.GetInputSourceHandle("/user/hand/right", ref _handSource[1]);
        _perHandFailed = false;
        _inputOk = _set != 0;
        InputStatus = _inputOk ? "ok" : "no action set";
    }

    void UpdateActionSets()
    {
        uint size = (uint)Marshal.SizeOf<VRActiveActionSet_t>();
        bool any = Suppress[0] || Suppress[1];
        if (any && !_perHandFailed && _handSource[0] != 0 && _handSource[1] != 0)
        {
            for (int h = 0; h < 2; h++)
            {
                _handSets[h].ulActionSet = _set;
                _handSets[h].ulRestrictedToDevice = _handSource[h];
                _handSets[h].nPriority = Suppress[h] ? OverlayGlobalPriority : 0;
            }
            var e = _in.UpdateActionState(_handSets, size);
            if (e == EVRInputError.None) return;
            _perHandFailed = true;           // this runtime refuses one set per hand: block both hands while one is busy
            Log.Warn("UpdateActionState per hand: " + e + " - blocking game input for both hands while one uses Orbit");
        }
        _sets[0].ulActionSet = _set;
        _sets[0].nPriority = any ? OverlayGlobalPriority : 0;
        _in.UpdateActionState(_sets, size);
    }

    public float FloatProp(uint dev, ETrackedDeviceProperty p, float fallback)
    {
        var err = ETrackedPropertyError.TrackedProp_Success;
        float v = Sys.GetFloatTrackedDeviceProperty(dev, p, ref err);
        return err == ETrackedPropertyError.TrackedProp_Success ? v : fallback;
    }

    public bool BoolProp(uint dev, ETrackedDeviceProperty p)
    {
        var err = ETrackedPropertyError.TrackedProp_Success;
        bool v = Sys.GetBoolTrackedDeviceProperty(dev, p, ref err);
        return err == ETrackedPropertyError.TrackedProp_Success && v;
    }

    public void Update()
    {
        if (!Connected) return;
        float secs = 0; ulong frame = 0;
        Sys.GetTimeSinceLastVsync(ref secs, ref frame);
        float predict = Math.Max(0, 1f / DisplayHz - secs) + _vsyncToPhotons;
        _predict = predict;
        Sys.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, predict, _poses);
        for (int i = 0; i < _poses.Length; i++)
        {
            DeviceValid[i] = _poses[i].bPoseIsValid && _poses[i].bDeviceIsConnected;
            DeviceConnected[i] = _poses[i].bDeviceIsConnected;
            if (DeviceValid[i]) Device[i] = MathUtil.FromHmd(_poses[i].mDeviceToAbsoluteTracking);
        }
        UpdateWorn();
        _idleTick = Idle ? _idleTick + 1 : 0;
        bool readInput = !Idle || _idleTick % 3 == 1;
        if (!Idle || _idleTick % 90 == 1)
        {
            LeftIndex = Sys.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
            RightIndex = Sys.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
        }

        for (int h = 0; h < 2; h++)
        {
            ref var hs = ref Hands[h];
            hs.ClickPrev = hs.Click; hs.GrabPrev = hs.Grab; hs.SecondaryPrev = hs.Secondary;
        }

        if (_inputOk && !readInput) { }           // (idle, between two reads: the hands as last read - no edges made up)
        else if (_inputOk)
        {
            UpdateActionSets();
            for (int h = 0; h < 2; h++)
            {
                ref var hs = ref Hands[h];
                var pose = new InputPoseActionData_t();
                _in.GetPoseActionDataRelativeToNow(_pointer[h], ETrackingUniverseOrigin.TrackingUniverseStanding, _predict, ref pose,
                    (uint)Marshal.SizeOf<InputPoseActionData_t>(), 0);
                hs.PoseValid = pose.bActive && pose.pose.bPoseIsValid;
                if (hs.PoseValid) hs.Aim = MathUtil.FromHmd(pose.pose.mDeviceToAbsoluteTracking);
                else
                {
                    // controller without a binding for the tip pose: aim with its raw pose
                    uint idx = h == 0 ? LeftIndex : RightIndex;
                    hs.PoseValid = idx < Device.Length && DeviceValid[idx];
                    if (hs.PoseValid) hs.Aim = Device[idx];
                }
                hs.Click = Digital(_click[h]);
                hs.Grab = Digital(_grab[h]);
                hs.Secondary = Digital(_secondary[h]);
                var a = new InputAnalogActionData_t();
                _in.GetAnalogActionData(_scroll[h], ref a, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), 0);
                hs.Stick = a.bActive ? new Vector2(a.x, a.y) : Vector2.Zero;
            }
        }
        else
        {
            // no SteamVR Input: aim with the raw controller pose, no buttons
            uint[] idx = { LeftIndex, RightIndex };
            for (int h = 0; h < 2; h++)
            {
                ref var hs = ref Hands[h];
                hs.PoseValid = idx[h] < Device.Length && DeviceValid[idx[h]];
                if (hs.PoseValid) hs.Aim = Device[idx[h]];
            }
        }
    }

    bool Digital(ulong action)
    {
        var d = new InputDigitalActionData_t();
        _in.GetDigitalActionData(action, ref d, (uint)Marshal.SizeOf<InputDigitalActionData_t>(), 0);
        return d.bActive && d.bState;
    }

    public void Haptic(Hand h, float amplitude, float seconds = 0.012f)
    {
        if (_inputOk) _in.TriggerHapticVibrationAction(_haptic[(int)h], 0, seconds, 160, amplitude, 0);
    }

    public bool PollEvent(ref VREvent_t ev) => Sys.PollNextEvent(ref ev, (uint)Marshal.SizeOf<VREvent_t>());

    public uint IndexFor(string attach) => attach switch
    {
        "left" => LeftIndex,
        "right" => RightIndex,
        "head" => 0,
        _ => InvalidIndex,
    };

    public bool TryDeviceWorld(uint index, out Matrix4x4 m)
    {
        if (index < Device.Length && DeviceValid[index]) { m = Device[index]; return true; }
        m = Matrix4x4.Identity;
        return false;
    }

    public void Disconnect(bool acknowledgeQuit)
    {
        if (!Connected) return;
        Connected = false;
        try { if (acknowledgeQuit) Sys.AcknowledgeQuit_Exiting(); } catch { }
        try { OpenVR.Shutdown(); } catch { }
        Sys = null; Ov = null; Comp = null; _in = null;
        Log.Info("SteamVR disconnected");
    }

    public void Dispose() => Disconnect(false);
}

// Compositor timing + device batteries, refreshed once a second.
public sealed class VrStats
{
    public float AppFps = -1, ReprojPct = -1, DroppedPerSec = -1, GpuMs = -1, DisplayHz = 90;
    public readonly List<(string label, float pct, bool charging)> Batteries = new();
    public int TrackerCount;
    // 1.14, the alerts (Sys/Alerts.cs): every controller / tracker with a battery on its own (Batteries sums the trackers up);
    // AppFps's second: the game loading or not answering (the compositor's loading / timed-out frames), the scene app's process
    public readonly List<BatteryDevice> Devices = new();
    public bool AppStalled;
    public uint ScenePid;
    public double FpsAt = -1;                     // when AppFps was last measured
    public double ReadAt = -1;                    // when all of this was read (once a second)

    // per-frame history for the wrist graph (~5 s at 90 Hz)
    public const int FrameHistory = 450;
    public readonly float[] FrameMs = new float[FrameHistory];
    public readonly byte[] FrameFlag = new byte[FrameHistory];   // 0 ok, 1 reprojected, 2 dropped
    public int FrameHead, FrameCount;
    uint _lastFrameIndex;
    Compositor_FrameTiming _ft;

    public void SampleFrame(VrSession vr)
    {
        _ft.m_nSize = (uint)Marshal.SizeOf<Compositor_FrameTiming>();
        if (!vr.Comp.GetFrameTiming(ref _ft, 0) || _ft.m_nFrameIndex == _lastFrameIndex) return;
        _lastFrameIndex = _ft.m_nFrameIndex;
        FrameMs[FrameHead] = _ft.m_flTotalRenderGpuMs;
        FrameFlag[FrameHead] = (byte)(_ft.m_nNumDroppedFrames > 0 ? 2
            : _ft.m_nNumFramePresents > 1 || (_ft.m_nReprojectionFlags & 0x3) != 0 ? 1 : 0);
        FrameHead = (FrameHead + 1) % FrameHistory;
        if (FrameCount < FrameHistory) FrameCount++;
        AddLong(Clock.Now, _ft.m_flTotalRenderGpuMs, FrameFlag[(FrameHead + FrameHistory - 1) % FrameHistory], _ft.m_flClientFrameIntervalMs);
    }

    // ---- the last 30 s (the frame graph on a 4 x 3 page): a bucket per 0.1 s holding its slowest frame, and the frame-to-frame
    // times of those 30 s in a histogram (0.25 ms steps) for the 1 % low ----
    public const int LongBuckets = 300;
    public readonly float[] LongMs = new float[LongBuckets];
    public readonly byte[] LongFlag = new byte[LongBuckets];
    public readonly long[] LongTick = InitTicks();
    public long LongNow = -1;                  // the newest bucket's tick (0.1 s steps)
    const int Bins = 512, Ring = 4096;
    readonly int[] _binCount = new int[Bins];
    readonly short[] _ringBin = new short[Ring];
    readonly long[] _ringTick = new long[Ring];
    int _ringHead, _ringLen, _binTotal;

    static long[] InitTicks() { var a = new long[LongBuckets]; Array.Fill(a, -1L); return a; }

    public void AddLong(double now, float ms, byte flag, float intervalMs)
    {
        long tick = (long)(now * 10);
        int i = (int)(tick % LongBuckets);
        if (LongTick[i] != tick) { LongTick[i] = tick; LongMs[i] = 0; LongFlag[i] = 0; }
        if (ms > LongMs[i]) LongMs[i] = ms;
        if (flag > LongFlag[i]) LongFlag[i] = flag;
        LongNow = tick;
        // older than 30 s, or the ring full: out of the histogram
        while (_ringLen > 0)
        {
            int tail = (_ringHead - _ringLen + Ring) % Ring;
            if (_ringLen < Ring && _ringTick[tail] > tick - LongBuckets) break;
            _binCount[_ringBin[tail]]--;
            _binTotal--;
            _ringLen--;
        }
        if (intervalMs <= 0) return;
        short bin = (short)Math.Clamp((int)(intervalMs * 4), 0, Bins - 1);
        _ringBin[_ringHead] = bin;
        _ringTick[_ringHead] = tick;
        _ringHead = (_ringHead + 1) % Ring;
        _ringLen++;
        _binCount[bin]++;
        _binTotal++;
    }

    // the frame rate of the slowest 1 % of the last 30 s of frames (-1: too few)
    public float OnePercentLow()
    {
        if (_binTotal < 50) return -1;
        int need = Math.Max(1, _binTotal / 100), acc = 0;
        for (int b = Bins - 1; b >= 0; b--)
        {
            acc += _binCount[b];
            if (acc >= need) return 1000f / ((b + 0.5f) / 4f);
        }
        return -1;
    }
    public float LeftBattery = -1, RightBattery = -1, TrackerAverage = -1;
    Compositor_CumulativeStats _prev;
    double _prevT = -1, _next, _nextBatt;
    public bool Watched = true;          // the watch shows: the batteries every second (else every 5 s: the alerts need no more)
    readonly Compositor_FrameTiming[] _timings = new Compositor_FrameTiming[30];

    public void Update(VrSession vr, double now)
    {
        if (!vr.Connected || now < _next) return;
        _next = now + 1.0;
        ReadAt = now;
        DisplayHz = vr.DisplayHz;

        var s = new Compositor_CumulativeStats();
        vr.Comp.GetCumulativeStats(ref s, (uint)Marshal.SizeOf<Compositor_CumulativeStats>());
        if (_prevT > 0 && s.m_nPid == _prev.m_nPid)
        {
            double dt = now - _prevT;
            float presents = s.m_nNumFramePresents - _prev.m_nNumFramePresents;
            float reproj = s.m_nNumReprojectedFrames - _prev.m_nNumReprojectedFrames;
            float dropped = s.m_nNumDroppedFrames - _prev.m_nNumDroppedFrames;
            if (presents > 0)
            {
                ReprojPct = 100f * reproj / presents;
                AppFps = (float)((presents - reproj - dropped) / dt);
                DroppedPerSec = (float)(dropped / dt);
                FpsAt = now;
            }
            AppStalled = s.m_nNumFramePresentsLoading != _prev.m_nNumFramePresentsLoading || s.m_nNumFramePresentsTimedOut != _prev.m_nNumFramePresentsTimedOut;
        }
        else AppStalled = true;          // another game (or none): no rate of its own yet
        ScenePid = s.m_nPid;
        _prev = s; _prevT = now;

        _timings[0].m_nSize = (uint)Marshal.SizeOf<Compositor_FrameTiming>();
        uint n = vr.Comp.GetFrameTimings(_timings);
        if (n > 0)
        {
            float sum = 0;
            for (int i = 0; i < n; i++) sum += _timings[i].m_flTotalRenderGpuMs;
            GpuMs = sum / n;
        }

        // the batteries: three property reads a device (each a call to SteamVR) - every second only while the watch can show them
        if (now < _nextBatt) return;
        _nextBatt = now + (Watched ? 1 : 5);
        Batteries.Clear();
        Devices.Clear();
        TrackerCount = 0;
        LeftBattery = RightBattery = TrackerAverage = -1;
        float trackerMin = 2, trackerSum = 0;
        bool trackerCharging = false;
        for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
        {
            if (!vr.DeviceConnected[i]) continue;          // (the poses say it: 64 calls a second saved)
            var cls = vr.Sys.GetTrackedDeviceClass(i);
            if (cls != ETrackedDeviceClass.HMD && cls != ETrackedDeviceClass.Controller && cls != ETrackedDeviceClass.GenericTracker) continue;
            if (!vr.BoolProp(i, ETrackedDeviceProperty.Prop_DeviceProvidesBatteryStatus_Bool)) continue;
            float pct = vr.FloatProp(i, ETrackedDeviceProperty.Prop_DeviceBatteryPercentage_Float, -1);
            if (pct < 0) continue;
            bool chg = vr.BoolProp(i, ETrackedDeviceProperty.Prop_DeviceIsCharging_Bool);
            if (cls == ETrackedDeviceClass.GenericTracker)
            {
                Devices.Add(new BatteryDevice(i, BatteryDevice.Tracker, pct, chg));
                TrackerCount++;
                trackerSum += pct;
                if (pct < trackerMin) { trackerMin = pct; trackerCharging = chg; }
                continue;
            }
            string label = cls == ETrackedDeviceClass.HMD ? "HMD"
                : vr.Sys.GetControllerRoleForTrackedDeviceIndex(i) switch
                {
                    ETrackedControllerRole.LeftHand => "L",
                    ETrackedControllerRole.RightHand => "R",
                    _ => "C",
                };
            Batteries.Add((label, pct, chg));
            if (cls == ETrackedDeviceClass.Controller)
                Devices.Add(new BatteryDevice(i, label == "L" ? BatteryDevice.Left : label == "R" ? BatteryDevice.Right : BatteryDevice.Other, pct, chg));
            if (label == "L") LeftBattery = pct;
            if (label == "R") RightBattery = pct;
        }
        if (TrackerCount > 0) TrackerAverage = trackerSum / TrackerCount;
        Batteries.Sort((a, b) => Order(a.label).CompareTo(Order(b.label)));
        if (TrackerCount > 0) Batteries.Add(($"Trk×{TrackerCount}", trackerMin, trackerCharging));
    }

    static int Order(string l) => l switch { "HMD" => 0, "L" => 1, "R" => 2, _ => 3 };
}

// a controller or tracker with a battery (VrStats.Devices): its device index, which one, the charge (0..1)
public readonly record struct BatteryDevice(uint Index, byte Kind, float Pct, bool Charging)
{
    public const byte Left = 1, Right = 2, Other = 3, Tracker = 4;
}
