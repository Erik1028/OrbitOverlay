using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Orbit;

// The apps playing on the default output device, and the Windows volume in VR (1.14).
//
// The default output device is the console role's, the one the Windows volume icon controls (in VR: Virtual Desktop's own speakers).
// Its apps are its Core Audio sessions (IAudioSessionManager2 / ISimpleAudioVolume - the Volume Mixer's app sliders), one row per app
// (Chrome's tabs, a game's streams together), with their name and the exe's icon - the watch's mixer widget (WristMixer.cs).
//
// THE WINDOWS VOLUME IN VR. Virtual Desktop's speakers say they set the volume themselves (hardware volume) and then ignore it
// (measured on Erik's PC, 10-15): Windows' own slider, the keyboard's volume keys and Hexpad's knob - all of them set that endpoint
// volume - did nothing in VR (1.12.1 made only the watch's volume work, by setting every app's own volume). Now, while SteamVR runs,
// the mixer follows that endpoint volume and mute (the device's change callback, and a look 4 times a second as a safety net) and
// when someone else moves it, every app on the device follows - each by its own balance: an app you turned down (in Windows' mixer or
// on the watch) stays that much quieter, the others take the level itself, as on a device that works. An app that starts later gets
// the level when it appears; an app someone moves by itself is never moved back. A change Orbit makes (the watch's volume, the
// mixer's master) goes to the endpoint too - Windows' slider shows the truth - and to the apps at once, and is not applied twice.
// When this begins on a device the master takes the loudest app's level (what you hear): nothing gets louder or quieter by itself.
//
// WHICH DEVICES (General.VolumeToApps, no switch in the settings yet - a hand-edited config): "auto" (null, the default) - the devices
// known to ignore their volume: Virtual Desktop's, by name, when it says it sets the volume itself; "on" - every device that says
// so (only those can ignore it: on any other one Windows applies the endpoint volume itself, and following it twice would make
// everything quieter); "off" - none (the watch's volume then sets the endpoint volume, as Windows' slider does).
//
// Everything runs on the app's main loop (Tick); the device's callback only raises a flag. The tools never open a device: the watch's
// mixer is a made-up one (MixerFake) unless the app hands it this one (WristPanel.UseRealAudio).
public sealed class AudioMixer : IDisposable
{
    public static int DefaultChanges;       // the default output device changed (counted by the device's Follow: the key clicks reopen then)
    public static string Mode = "auto";            // General.VolumeToApps: auto | on | off (the app sets it every frame)

    // Does this device ignore its own volume (so the apps must follow it)? See WHICH DEVICES above.
    public static bool IgnoresOwnVolume(string name, bool hardwareVolume, string mode) => mode switch
    {
        "off" => false,
        "on" => hardwareVolume,
        _ => hardwareVolume && name != null && name.Contains("Virtual Desktop", StringComparison.OrdinalIgnoreCase),
    };

    readonly IMixerDevice _dev;
    readonly MixerSync _sync = new();
    public readonly bool Fake;                     // a made-up device (the tools): nothing real is touched
    public readonly List<MixerApp> Apps = new();   // active and loudest first, the rest after (a stable order)
    public int Version;                            // moves when something the watch shows changes
    public bool PerApp { get; private set; }       // the apps follow the master (the device ignores its own volume)
    public string DeviceName => _dev.Name;
    public bool Present => _dev.Present;
    public float Master { get; private set; } = -1;    // the device's volume (-1: no device) and mute, as last read
    public bool MasterMuted { get; private set; }
    public IMixerDevice Device => _dev;
    public MixerSync Sync => _sync;

    double _nextFollow, _nextEnum, _nextPoll, _nextSort, _wantedUntil = -1, _lastRead;
    bool _logged, _vr;

    public AudioMixer() : this(new CoreAudioDevice(), false) { }

    public AudioMixer(IMixerDevice dev, bool fake)
    {
        _dev = dev;
        Fake = fake;
    }

    // The watch shows a mixer (a widget or the opened view): keep the list fresh for the next moment
    public void Want(double now)
    {
        bool was = now < Volatile.Read(ref _wantedUntil);
        Volatile.Write(ref _wantedUntil, now + 1.5);
        if (!was) { _pollNow = true; _wake.Set(); }
        if (Fake) Tick(now, _vr);                    // the tools: no main loop - the made-up list now
    }

    // ---- the app's: Tick on a worker of its own ----
    // In VR (a device that ignores its own volume) every app's session is enumerated and read each second - Core Audio calls to the
    // audio service, ~45 of them with 15 apps: they held the main loop ~9 ms every second or two (Erik 10-18, the spike line). The
    // worker ticks under Gate; the main thread's changes (a slider, a mute, the master) take Gate, its readers (the watch's rows,
    // auto theater, the horizon's meter) only try it and keep what they had when the worker is busy.
    public readonly object Gate = new();
    Thread _worker;
    readonly AutoResetEvent _wake = new(false);
    volatile bool _stop, _vrOn, _pollNow;
    float _lastPeak = -1;

    // the main loop, each frame: SteamVR on or off (the worker ticks every 30 ms while it is on or a mixer is wanted, else twice a second)
    public void Pump(double now, bool vr)
    {
        if (Fake) { Tick(now, vr); return; }
        bool was = _vrOn;
        _vrOn = vr;
        if (_worker == null)
        {
            _worker = new Thread(Work) { IsBackground = true, Name = "OrbitOverlay mixer" };
            _worker.Start();
        }
        else if (vr && !was) _wake.Set();
    }

    void Work()
    {
        while (!_stop)
        {
            double now = Clock.Now;
            lock (Gate) Tick(now, _vrOn);
            bool busy = _vrOn || now < Volatile.Read(ref _wantedUntil);
            _wake.WaitOne(busy ? 30 : 500);
        }
    }

    // the apps as they are (a copy, for the main thread): false while the worker is busy - keep what you had
    public bool TryApps(List<MixerApp> into)
    {
        if (!Monitor.TryEnter(Gate)) return false;
        try
        {
            into.Clear();
            into.AddRange(Apps);
            return true;
        }
        finally { Monitor.Exit(Gate); }
    }

    // The mixer's work: the default device (each second), the master sync while SteamVR runs (vr), the list while wanted - the app's
    // worker (Pump), the tools' own thread
    public void Tick(double now, bool vr)
    {
        _vr = vr;
        if (_pollNow) { _pollNow = false; _nextPoll = _nextEnum = 0; }
        bool wanted = now < Volatile.Read(ref _wantedUntil);
        // nothing to do: SteamVR is off and no mixer shows - not even the default device is looked at
        if (!vr && !wanted)
        {
            if (PerApp) { PerApp = false; _sync.Reset(); Version++; }
            return;
        }
        try
        {
            if (now >= _nextFollow)
            {
                _nextFollow = now + 1;
                if (_dev.Follow())
                {
                    _sync.Reset();
                    Master = -1;
                    _logged = false;
                    _nextEnum = _nextPoll = 0;
                    Version++;
                }
            }
            bool perApp = vr && _dev.Present && IgnoresOwnVolume(_dev.Name, _dev.HardwareVolume, Mode);
            if (perApp != PerApp)
            {
                PerApp = perApp;
                _sync.Reset();
                _nextEnum = _nextPoll = 0;
                if (perApp && !_logged)
                {
                    _logged = true;
                    Log.Info($"volume: '{_dev.Name}' ignores its own volume - the Windows volume (its slider, the volume keys, Hexpad's knob) moves its apps");
                }
                Version++;
            }
            bool notified = _dev.TakeNotified();
            if (!PerApp && !wanted) return;
            bool enumNow = now >= _nextEnum;
            if (enumNow)
            {
                _nextEnum = now + 1;
                _dev.Enumerate();
            }
            bool poll = notified || enumNow || now >= _nextPoll;
            if (!poll) return;
            _nextPoll = now + 0.25;
            bool read = false;
            if (PerApp && (enumNow || notified || _sync.Moved(_dev)))
            {
                _dev.Read(wanted);
                read = true;
                if (_sync.Step(_dev) > 0) Version++;
            }
            if (wanted)
            {
                if (!read) _dev.Read(true);
                Refresh(now);
            }
        }
        catch (Exception e)
        {
            Log.Warn("mixer: " + e.Message);
            _nextFollow = _nextEnum = _nextPoll = now + 5;
        }
    }

    // ---- what the watch does ----

    // one app's own volume (every session of it); with the sync on, its balance against the master from now on
    public void SetApp(MixerApp a, float level)
    {
        lock (Gate) SetAppNow(a, level);
    }

    void SetAppNow(MixerApp a, float level)
    {
        level = Math.Clamp(level, 0, 1);
        foreach (var s in a.Sessions)
        {
            if (s.Expired) continue;
            if (_dev.Write(s, level, s.Muted)) s.Level = level;
            if (PerApp) _sync.AppSet(s, level);
        }
        a.Level = level;
        Version++;
    }

    public void ToggleAppMute(MixerApp a)
    {
        lock (Gate) ToggleAppMuteNow(a);
    }

    void ToggleAppMuteNow(MixerApp a)
    {
        bool m = !a.Muted;
        foreach (var s in a.Sessions)
        {
            if (s.Expired) continue;
            if (_dev.Write(s, s.Level, m)) s.Muted = m;
            if (PerApp) _sync.AppMute(s, m);
        }
        a.Muted = m;
        Version++;
    }

    // The master (the watch's volume, the mixer's top row): on a device that ignores it, the endpoint and the apps at once (no echo);
    // else the endpoint, as Windows' slider does
    public void SetMaster(float level, bool mute)
    {
        lock (Gate) SetMasterNow(level, mute);
    }

    void SetMasterNow(float level, bool mute)
    {
        level = Math.Clamp(level, 0, 1);
        try
        {
            if (PerApp)
            {
                _dev.Read(false);
                _sync.Set(_dev, level, mute);
                Master = _sync.Master;
                MasterMuted = _sync.Mute;
                foreach (var a in Apps) Sum(a);
            }
            else if (_dev.WriteMaster(level, mute))
            {
                Master = level;
                MasterMuted = mute;
            }
            Version++;
        }
        catch (Exception e) { Log.Warn("mixer: " + e.Message); }
    }

    // ---- the list ----

    readonly Dictionary<string, MixerApp> _byKey = new();
    static readonly int SelfPid = Environment.ProcessId;
    readonly HashSet<MixerApp> _live = new();

    void Refresh(double now)
    {
        float dt = (float)Math.Clamp(now - _lastRead, 0, 2);
        _lastRead = now;
        if (_dev.ReadMaster(out float m, out bool mu))
        {
            if (MathF.Abs(m - Master) > 0.004f || mu != MasterMuted) Version++;
            Master = m;
            MasterMuted = mu;
        }
        else if (Master >= 0) { Master = -1; Version++; }
        _live.Clear();
        foreach (var a in Apps) a.Sessions.Clear();
        bool added = false;
        foreach (var s in _dev.Sessions)
        {
            // Orbit's own (its key clicks: the Keyboard tab has their volume) is not listed - the sync moves it like any other
            if (s.Expired || (s.Pid == SelfPid && !Fake)) continue;
            var id = _dev.Identify(s);
            if (!_byKey.TryGetValue(id.Key, out var a))
            {
                _byKey[id.Key] = a = new MixerApp { Key = id.Key, System = s.System, Loud = s.Peak };
                Apps.Add(a);
                added = true;
            }
            // its name and icons from its first session (two of an app may name themselves differently: no back and forth)
            bool first = a.Sessions.Count == 0;
            if (first && a.Name != id.Name) { a.Name = id.Name; Version++; }
            if (first && (!ReferenceEquals(a.Icon32, id.Icon32) || !ReferenceEquals(a.Icon16, id.Icon16)))
            {
                a.Icon32 = id.Icon32;
                a.Icon16 = id.Icon16;
                a.IconVersion++;
                Version++;
            }
            a.Sessions.Add(s);
            _live.Add(a);
        }
        // an app whose sessions are gone leaves the list
        int before = Apps.Count;
        Apps.RemoveAll(a => !_live.Contains(a));
        if (Apps.Count != before)
        {
            foreach (var k in new List<string>(_byKey.Keys)) if (!_live.Contains(_byKey[k])) _byKey.Remove(k);
            Version++;
        }
        float decay = MathF.Exp(-dt / 3f);
        foreach (var a in Apps)
        {
            Sum(a);
            a.Loud = MathF.Max(a.Peak, a.Loud * decay);
        }
        if (added || now >= _nextSort)
        {
            _nextSort = now + 2;
            if (Sort(Apps)) Version++;
        }
        if (added) Version++;
    }

    // an app as its sessions are: the loudest one's level, muted when all are, active when one is
    void Sum(MixerApp a)
    {
        float l = 0, pk = 0;
        bool muted = a.Sessions.Count > 0, active = false;
        foreach (var s in a.Sessions)
        {
            l = MathF.Max(l, s.Level);
            pk = MathF.Max(pk, s.Peak);
            muted &= s.Muted;
            active |= s.Active;
        }
        if (a.Sessions.Count == 0) return;
        if (MathF.Abs(l - a.Level) > 0.004f || muted != a.Muted || active != a.Active) Version++;
        a.Level = l;
        a.Muted = muted;
        a.Active = active;
        a.Peak = pk;
    }

    // The ones sounding first, the loudest of them first (another passes one only when it is clearly louder: rows do not swap back and
    // forth), then the ones playing silence, then the quiet ones as they were; Windows' system sounds last. Returns whether it changed.
    public static bool Sort(List<MixerApp> list)
    {
        bool moved = false;
        for (int i = 1; i < list.Count; i++)
            for (int j = i; j > 0 && Before(list[j], list[j - 1]); j--)
            {
                (list[j], list[j - 1]) = (list[j - 1], list[j]);
                moved = true;
            }
        return moved;
    }

    static int Rank(MixerApp a) => a.System ? 3 : a.Active && a.Loud > 0.02f ? 0 : a.Active ? 1 : 2;

    static bool Before(MixerApp a, MixerApp b)
    {
        int ra = Rank(a), rb = Rank(b);
        if (ra != rb) return ra < rb;
        return ra == 0 && a.Loud > b.Loud + 0.06f;
    }

    // What the device plays now, every app together (its own meter), 0..1; -1: no device / no meter. Theater's Cyber horizon breathes
    // with it (TheaterHorizon.cs): one COM call, asked once a frame only while that shows - the device is the one Tick follows anyway.
    // the device's meter now (theater's horizon, a frame); the last one while the worker ticks
    public float OutputPeak()
    {
        if (!Monitor.TryEnter(Gate)) return _lastPeak;
        try { return _lastPeak = _dev.Peak(); }
        finally { Monitor.Exit(Gate); }
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _worker?.Join(1000);
        lock (Gate) _dev.Dispose();
    }
}

// One app on the device as the mixer shows it: its sessions together
public sealed class MixerApp
{
    public string Key, Name;
    public uint[] Icon32, Icon16;       // the exe's icon, premultiplied BGRA, 32 x 32 / 16 x 16 (null: none - the watch draws its initial)
    public int IconVersion;
    public bool System;                 // Windows' system sounds
    public bool Active;                 // playing (a stream of it is running)
    public bool Muted;
    public float Level = 1;             // its own volume (its loudest session's), 0..1
    public float Peak;                  // its meter now
    internal float Loud;                // how loud it was lately (the order)
    internal readonly List<MixerSession> Sessions = new();
    public override string ToString() => Name;
}

// One stream of an app (a Core Audio session) and what the master sync remembers of it
public sealed class MixerSession
{
    public string Id;                   // its instance identifier
    public int Pid;
    public bool System;
    public string Display = "";         // the name the app gave it ("" = none)
    public float Level = 1;             // its own volume, 0..1
    public bool Muted, Active, Expired;
    public float Peak;
    // the sync's memory (MixerSync)
    internal bool Seen;                 // known to the sync (else it started since: it gets the master)
    internal float Ratio = 1;           // its level against the master's: its balance
    internal bool OwnMute;              // muted by itself (not by the master)
    internal float SetLevel = -1;       // what was written last: another value later = someone else moved it
    internal bool SetMute;
    // a Core Audio session's objects
    internal object Com;
    internal CoreAudio.ISimpleAudioVolume Vol;
    internal CoreAudio.IAudioMeterInformation Meter;
    internal bool Mark;
    internal string Key;                // which app it is (Identify, once)
}

// What the mixer needs from a device: Core Audio's default output device, or the tools' made-up one
public interface IMixerDevice : IDisposable
{
    string Name { get; }                // "Speakers (Virtual Desktop Audio)"; "" = none
    bool Present { get; }
    bool HardwareVolume { get; }        // it says it sets the volume itself (QueryHardwareSupport) - the only kind that can ignore it
    bool Follow();                      // the default device again: true when it is another one (or none) now
    bool ReadMaster(out float level, out bool mute);
    bool WriteMaster(float level, bool mute);       // as Orbit (its change is told apart from someone else's)
    bool TakeNotified();                // someone else changed the master since the last call (the device's callback)
    List<MixerSession> Sessions { get; }
    void Enumerate();                   // the sessions that started / ended
    void Read(bool peaks);              // their levels, mutes and states now (and meters)
    bool Write(MixerSession s, float level, bool mute);
    MixerIdentity Identify(MixerSession s);         // which app: its key, name and icons (they may come a moment later)
    float Peak() => -1;                 // what it plays now, every app together (its own meter), 0..1; -1: none (the tools' made-up one)
}

public readonly record struct MixerIdentity(string Key, string Name, uint[] Icon32, uint[] Icon16);

// The master sync: someone else moved the master -> every app follows by its balance; a new app gets the master; an app someone else
// moved keeps that as its balance (it is never moved back); Orbit's own change goes to the endpoint and the apps at once (no echo).
public sealed class MixerSync
{
    public bool Known;                  // the master was read since this began on the device
    public float Master = -1;
    public bool Mute;
    public int Writes;                  // sessions written (the tools count them)

    public void Reset() { Known = false; Master = -1; Mute = false; }

    // has the master moved since (cheap: the endpoint only)?
    public bool Moved(IMixerDevice d) => !Known || (d.ReadMaster(out float m, out bool mu) && (MathF.Abs(m - Master) > 0.002f || mu != Mute));

    // It begins on a device: the master takes the loudest app's level (what you hear) and mute when all are muted, each app its
    // balance against it - nothing is written to the apps
    public void Adopt(IMixerDevice d)
    {
        if (!d.ReadMaster(out float m, out bool mu)) return;
        float loud = -1;
        bool allMuted = true;
        int n = 0;
        foreach (var s in d.Sessions)
        {
            if (s.Expired) continue;
            n++;
            loud = MathF.Max(loud, s.Level);
            allMuted &= s.Muted;
        }
        if (n > 0)
        {
            loud = Math.Clamp(loud, 0, 1);
            if (MathF.Abs(loud - m) > 0.005f || allMuted != mu)
            {
                d.WriteMaster(loud, allMuted);
                if (!d.ReadMaster(out m, out mu)) { m = loud; mu = allMuted; }
            }
        }
        Master = m;
        Mute = mu;
        Known = true;
        foreach (var s in d.Sessions)
        {
            if (s.Expired) continue;
            s.Seen = true;
            s.Ratio = Master > 0.01f ? s.Level / Master : 1;
            s.OwnMute = s.Muted && !Mute;
            s.SetLevel = s.Level;
            s.SetMute = s.Muted;
        }
    }

    // after d.Read(): returns the sessions written
    public int Step(IMixerDevice d)
    {
        if (!Known) { Adopt(d); return 0; }
        if (!d.ReadMaster(out float m, out bool mu)) return 0;
        bool changed = MathF.Abs(m - Master) > 0.002f || mu != Mute;
        int w = 0;
        foreach (var s in d.Sessions)
        {
            if (s.Expired) continue;
            if (!s.Seen)
            {
                // an app that started since: the master's level and mute
                s.Seen = true;
                s.Ratio = 1;
                s.OwnMute = false;
                w += Apply(d, s, m, mu);
                continue;
            }
            Notice(s);
            if (changed) w += Apply(d, s, m, mu);
        }
        Master = m;
        Mute = mu;
        Writes += w;
        return w;
    }

    // Orbit sets the master: the endpoint, then every app by its balance (and the master is what the endpoint reads back, so the next
    // look sees no change)
    public int Set(IMixerDevice d, float level, bool mute)
    {
        if (!Known) Adopt(d);
        foreach (var s in d.Sessions)
        {
            if (s.Expired) continue;
            if (s.Seen) Notice(s);
            else { s.Seen = true; s.Ratio = 1; s.OwnMute = false; }
        }
        d.WriteMaster(level, mute);
        if (d.ReadMaster(out float m, out bool mu)) { level = m; mute = mu; }
        int w = 0;
        foreach (var s in d.Sessions) if (!s.Expired) w += Apply(d, s, level, mute);
        Master = level;
        Mute = mute;
        Known = true;
        Writes += w;
        return w;
    }

    // someone else moved it since it was written: that is its balance now (and a mute of its own while the master is on)
    void Notice(MixerSession s)
    {
        if (s.SetLevel >= 0 && MathF.Abs(s.Level - s.SetLevel) > 0.004f)
        {
            s.Ratio = Master > 0.01f ? s.Level / Master : 1;
            s.SetLevel = s.Level;
        }
        if (s.Muted != s.SetMute)
        {
            if (!Mute) s.OwnMute = s.Muted;
            s.SetMute = s.Muted;
        }
    }

    int Apply(IMixerDevice d, MixerSession s, float m, bool mu)
    {
        float l = Math.Clamp(s.Ratio * m, 0, 1);
        bool mt = mu || s.OwnMute;
        s.SetLevel = l;
        s.SetMute = mt;
        if (MathF.Abs(s.Level - l) < 0.0005f && s.Muted == mt) return 0;
        if (!d.Write(s, l, mt)) return 0;
        s.Level = l;
        s.Muted = mt;
        return 1;
    }

    // the watch set one app: its balance against the master from now on
    public void AppSet(MixerSession s, float level)
    {
        s.Seen = true;
        s.Ratio = Master > 0.01f ? level / Master : 1;
        s.SetLevel = level;
    }

    public void AppMute(MixerSession s, bool mute)
    {
        s.Seen = true;
        if (!Mute) s.OwnMute = mute;
        s.SetMute = mute;
    }
}

// ================= Core Audio =================

static class CoreAudio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorCom { }

    // Windows hands every caller in the process the same enumerator object: "new MMDeviceEnumeratorCom()" in one class gives back
    // the object already wrapped as ANOTHER class's coclass type (SystemVolume's, SystemMic's) and throws "Specified cast is not
    // valid" (VR 10-16: the mixer never started on Virtual Desktop; 1.12.3's microphone). Untyped, then cast to the interface.
    static readonly Type EnumeratorType = Type.GetTypeFromCLSID(typeof(MMDeviceEnumeratorCom).GUID, true);
    internal static object NewEnumerator() => Activator.CreateInstance(EnumeratorType);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    internal interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int i, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out int pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
}

// The default output device through Core Audio (the console role, followed every second like the watch's volume)
sealed class CoreAudioDevice : IMixerDevice
{
    // Orbit's own changes carry this: the device's callback tells them from someone else's
    public static readonly Guid Context = new("6F72B174-0B17-4C6D-9D2A-4F7262697421");
    static Guid _ctx = Context;
    const int eRender = 0, eConsole = 0, CLSCTX_ALL = 23, HardwareSupportVolume = 1;

    CoreAudio.IMMDeviceEnumerator _en;
    CoreAudio.IMMDevice _dev;
    CoreAudio.IAudioEndpointVolume _vol;
    CoreAudio.IAudioSessionManager2 _mgr;
    CoreAudio.IAudioMeterInformation _meter;     // the device's own meter: made the first time it is asked for (Peak)
    bool _meterTried;
    Callback _cb;
    string _id;
    bool _warned;
    public string Name { get; private set; } = "";
    public bool Present => _vol != null;
    public bool HardwareVolume { get; private set; }
    public List<MixerSession> Sessions { get; } = new();
    readonly Dictionary<object, MixerSession> _byCom = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<string, MixerSession> _byId = new();

    // the endpoint's change callback: on an audio thread - it only raises the flag (someone else's change, not Orbit's)
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class Callback : CoreAudio.IAudioEndpointVolumeCallback
    {
        public int Notified;
        public int OnNotify(IntPtr data)
        {
            try
            {
                if (data != IntPtr.Zero && Marshal.PtrToStructure<Guid>(data) == Context) return 0;
            }
            catch { }
            Interlocked.Exchange(ref Notified, 1);
            return 0;
        }
    }

    public bool TakeNotified() => _cb != null && Interlocked.Exchange(ref _cb.Notified, 0) != 0;

    public bool Follow()
    {
        try
        {
            _en ??= (CoreAudio.IMMDeviceEnumerator)CoreAudio.NewEnumerator();
            string id = null;
            if (_en.GetDefaultAudioEndpoint(eRender, eConsole, out var d) == 0 && d != null)
            {
                id = d.GetId(out string s) == 0 ? s : null;
                Marshal.ReleaseComObject(d);
            }
            if (id == _id && (_vol != null || id == null)) return false;
            Acquire();
            AudioMixer.DefaultChanges++;
            return true;
        }
        catch (Exception e)
        {
            Warn(e.Message);
            Release();
            if (_en != null) { try { Marshal.ReleaseComObject(_en); } catch { } _en = null; }
            _id = null;
            return true;
        }
    }

    void Acquire()
    {
        Release();
        if (_en.GetDefaultAudioEndpoint(eRender, eConsole, out _dev) != 0 || _dev == null) { _id = null; Name = ""; return; }
        _id = _dev.GetId(out string id) == 0 ? id : null;
        Name = FriendlyName(_id);
        var vid = typeof(CoreAudio.IAudioEndpointVolume).GUID;
        if (_dev.Activate(ref vid, CLSCTX_ALL, IntPtr.Zero, out object o) == 0) _vol = o as CoreAudio.IAudioEndpointVolume;
        var mid = typeof(CoreAudio.IAudioSessionManager2).GUID;
        if (_dev.Activate(ref mid, CLSCTX_ALL, IntPtr.Zero, out object m) == 0) _mgr = m as CoreAudio.IAudioSessionManager2;
        // a device that cannot tell is taken as one that sets its volume itself (1.12.1 took Virtual Desktop's by its name alone)
        HardwareVolume = _vol == null || _vol.QueryHardwareSupport(out uint mask) != 0 || (mask & HardwareSupportVolume) != 0;
        if (_vol != null)
        {
            try
            {
                var cb = new Callback();
                if (_vol.RegisterControlChangeNotify(cb) == 0) _cb = cb;
            }
            catch (Exception e) { Log.Warn("mixer: no change callback (" + e.Message + ") - looking 4 times a second"); }
        }
        _warned = false;
        Log.Info($"mixer: following '{Name}'{(HardwareVolume ? " (it sets its volume itself)" : "")}{(_mgr == null ? " (no apps' volumes)" : "")}");
    }

    public bool ReadMaster(out float level, out bool mute)
    {
        level = -1;
        mute = false;
        try
        {
            if (_vol == null || _vol.GetMasterVolumeLevelScalar(out level) != 0) { level = -1; return false; }
            mute = _vol.GetMute(out bool m) == 0 && m;
            return true;
        }
        catch (Exception e) { Warn(e.Message); level = -1; return false; }
    }

    // The device's peak now (all its apps mixed). Its meter is made on the first ask - only theater's Cyber horizon asks, so nobody
    // else ever has one - and again after the device changes (Release); one that cannot be made is not tried again until then.
    public float Peak()
    {
        if (_dev == null) return -1;
        try
        {
            if (_meter == null)
            {
                if (_meterTried) return -1;
                _meterTried = true;
                var pid = typeof(CoreAudio.IAudioMeterInformation).GUID;
                if (_dev.Activate(ref pid, CLSCTX_ALL, IntPtr.Zero, out object o) != 0 || o == null) return -1;
                _meter = o as CoreAudio.IAudioMeterInformation;
                if (_meter == null) { Marshal.ReleaseComObject(o); return -1; }
            }
            return _meter.GetPeakValue(out float pk) == 0 ? Math.Clamp(pk, 0, 1) : -1;
        }
        catch (Exception e)
        {
            Warn("meter: " + e.Message);
            ReleaseMeter();
            _meterTried = true;
            return -1;
        }
    }

    void ReleaseMeter()
    {
        if (_meter != null) { try { Marshal.ReleaseComObject(_meter); } catch { } _meter = null; }
        _meterTried = false;
    }

    public bool WriteMaster(float level, bool mute)
    {
        try
        {
            if (_vol == null) return false;
            int a = _vol.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 1), ref _ctx), b = _vol.SetMute(mute, ref _ctx);
            if (a != 0 || b != 0) Warn($"refused the master (0x{a:X8}, 0x{b:X8})");
            return a == 0;
        }
        catch (Exception e) { Warn(e.Message); return false; }
    }

    public void Enumerate()
    {
        if (_mgr == null) { Clear(); return; }
        CoreAudio.IAudioSessionEnumerator list = null;
        try
        {
            if (_mgr.GetSessionEnumerator(out list) != 0 || list == null) return;
            foreach (var s in Sessions) s.Mark = false;
            list.GetCount(out int n);
            for (int i = 0; i < n; i++)
            {
                if (list.GetSession(i, out object o) != 0 || o == null) continue;
                // the same session again: the enumerator usually hands back the same object (the same wrapper) - one reference less;
                // else by its instance id
                if (_byCom.TryGetValue(o, out var known)) { known.Mark = true; Marshal.ReleaseComObject(o); continue; }
                var c2 = o as CoreAudio.IAudioSessionControl2;
                string id = c2 != null && c2.GetSessionInstanceIdentifier(out string sid) == 0 ? sid : null;
                if (id == null || c2.GetState(out int st) != 0 || st == 2) { Marshal.ReleaseComObject(o); continue; }
                if (_byId.TryGetValue(id, out known)) { known.Mark = true; Marshal.ReleaseComObject(o); continue; }
                var s = new MixerSession
                {
                    Id = id, Com = o, Vol = o as CoreAudio.ISimpleAudioVolume, Meter = o as CoreAudio.IAudioMeterInformation, Mark = true,
                    Active = st == 1,
                };
                if (s.Vol == null) { Marshal.ReleaseComObject(o); continue; }
                s.Pid = c2.GetProcessId(out int pid) == 0 ? pid : 0;
                s.System = c2.IsSystemSoundsSession() == 0;
                s.Display = c2.GetDisplayName(out string dn) == 0 && dn != null && !dn.StartsWith('@') ? dn.Trim() : "";
                if (s.Vol.GetMasterVolume(out float l) == 0) s.Level = l;
                s.Muted = s.Vol.GetMute(out bool mu) == 0 && mu;
                Sessions.Add(s);
                _byCom[o] = s;
                _byId[id] = s;
            }
            for (int i = Sessions.Count - 1; i >= 0; i--)
                if (!Sessions[i].Mark) Drop(i);
        }
        catch (Exception e) { Warn(e.Message); }
        finally { if (list != null) Marshal.ReleaseComObject(list); }
    }

    public void Read(bool peaks)
    {
        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            var s = Sessions[i];
            try
            {
                if (s.Com is CoreAudio.IAudioSessionControl2 c2 && c2.GetState(out int st) == 0)
                {
                    s.Active = st == 1;
                    if (st == 2) { Drop(i); continue; }
                }
                if (s.Vol.GetMasterVolume(out float l) == 0) s.Level = l;
                s.Muted = s.Vol.GetMute(out bool m) == 0 && m;
                if (peaks) s.Peak = s.Meter != null && s.Meter.GetPeakValue(out float pk) == 0 ? Math.Clamp(pk, 0, 1) : 0;
            }
            catch { Drop(i); }
        }
    }

    public bool Write(MixerSession s, float level, bool mute)
    {
        try
        {
            if (s.Vol == null) return false;
            int a = s.Vol.SetMasterVolume(Math.Clamp(level, 0, 1), ref _ctx), b = s.Vol.SetMute(mute, ref _ctx);
            return a == 0 && b == 0;
        }
        catch { return false; }
    }

    public MixerIdentity Identify(MixerSession s)
    {
        if (s.System) return new MixerIdentity("sys", "System sounds", null, null);
        string exe = AppIdentity.ExeOf(s.Pid);
        var info = AppIdentity.Get(exe);
        // one row per app: by its exe - SteamVR's processes (and Steam's) under their one name
        string key = s.Key ??= AppIdentity.KnownName(exe) is { } kn ? "app:" + kn : exe != null ? exe.ToLowerInvariant() : "pid:" + s.Pid;
        string name = s.Display.Length > 0 ? s.Display : info?.Name ?? AppIdentity.Stem(exe) ?? $"App {s.Pid}";
        return info != null && info.Done ? new MixerIdentity(key, name, info.Icon32, info.Icon16) : new MixerIdentity(key, name, null, null);
    }

    void Drop(int i)
    {
        var s = Sessions[i];
        Sessions.RemoveAt(i);
        _byId.Remove(s.Id);
        if (s.Com != null)
        {
            _byCom.Remove(s.Com);
            try { Marshal.ReleaseComObject(s.Com); } catch { }
        }
        s.Com = null;
        s.Vol = null;
        s.Meter = null;
        s.Expired = true;
    }

    void Clear() { for (int i = Sessions.Count - 1; i >= 0; i--) Drop(i); }

    void Warn(string what)
    {
        if (_warned) return;
        _warned = true;
        Log.Warn($"mixer: '{Name}' {what}");
    }

    // "Speakers (Virtual Desktop Audio)": the device's name as the Sound settings show it (registry, read only)
    static string FriendlyName(string id)
    {
        try
        {
            if (string.IsNullOrEmpty(id)) return "";
            string g = id[id.LastIndexOf('{')..];
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{g}\Properties");
            string dev = k?.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string;
            string iface = k?.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string;
            return dev == null ? id : string.IsNullOrEmpty(iface) ? dev : $"{dev} ({iface})";
        }
        catch { return id ?? ""; }
    }

    void Release()
    {
        Clear();
        if (_cb != null && _vol != null) { try { _vol.UnregisterControlChangeNotify(_cb); } catch { } }
        _cb = null;
        ReleaseMeter();
        if (_mgr != null) { try { Marshal.ReleaseComObject(_mgr); } catch { } _mgr = null; }
        if (_vol != null) { try { Marshal.ReleaseComObject(_vol); } catch { } _vol = null; }
        if (_dev != null) { try { Marshal.ReleaseComObject(_dev); } catch { } _dev = null; }
        HardwareVolume = false;
    }

    public void Dispose()
    {
        Release();
        if (_en != null) { try { Marshal.ReleaseComObject(_en); } catch { } _en = null; }
    }
}

// Which app a process is: its exe (now), its name and icons (read once per exe, off the main loop: file reads)
static class AppIdentity
{
    public sealed class Info
    {
        public volatile bool Done;
        public string Name;
        public uint[] Icon32, Icon16;
    }

    static readonly ConcurrentDictionary<string, Info> _byExe = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<int, (string exe, double at)> _exeOf = new();

    // a few processes whose file says little: SteamVR's (vrserver, the compositor...) as one
    static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vrserver"] = "SteamVR", ["vrcompositor"] = "SteamVR", ["vrmonitor"] = "SteamVR", ["vrdashboard"] = "SteamVR", ["vrwebhelper"] = "SteamVR",
        ["vrstartup"] = "SteamVR", ["steamwebhelper"] = "Steam", ["steam"] = "Steam", ["vrchat"] = "VRChat",
    };

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, char[] name, ref int size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint sizes);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    // the exe behind a process (kept a minute: a process id is reused only after it ended)
    public static string ExeOf(int pid)
    {
        if (pid <= 0) return null;
        double now = Clock.Now;
        if (_exeOf.TryGetValue(pid, out var e) && now - e.at < 60) return e.exe;
        string exe = null;
        IntPtr h = OpenProcess(0x1000, false, pid);          // PROCESS_QUERY_LIMITED_INFORMATION
        if (h != IntPtr.Zero)
        {
            try
            {
                var buf = new char[1024];
                int n = buf.Length;
                if (QueryFullProcessImageName(h, 0, buf, ref n)) exe = new string(buf, 0, n);
            }
            finally { CloseHandle(h); }
        }
        if (_exeOf.Count > 256) _exeOf.Clear();
        _exeOf[pid] = (exe, now);
        return exe;
    }

    public static string KnownName(string exe) =>
        !string.IsNullOrEmpty(exe) && Known.TryGetValue(Path.GetFileNameWithoutExtension(exe), out var k) ? k : null;

    // "vrchat" -> the known name, else the file name as it is ("Discord")
    public static string Stem(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        string stem = Path.GetFileNameWithoutExtension(exe);
        return Known.TryGetValue(stem, out var k) ? k : stem.Length > 0 ? char.ToUpperInvariant(stem[0]) + stem[1..] : null;
    }

    public static Info Get(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        if (_byExe.TryGetValue(exe, out var i)) return i;
        i = new Info();
        if (!_byExe.TryAdd(exe, i)) return _byExe[exe];
        ThreadPool.QueueUserWorkItem(_ => Load(exe, i));
        return i;
    }

    static void Load(string exe, Info i)
    {
        try
        {
            string stem = Path.GetFileNameWithoutExtension(exe);
            string name = Known.TryGetValue(stem, out var k) ? k : null;
            if (name == null)
            {
                var v = FileVersionInfo.GetVersionInfo(exe);
                string d = v.FileDescription?.Trim();
                if (string.IsNullOrEmpty(d) || d.Length > 40 || d.Contains(".exe", StringComparison.OrdinalIgnoreCase)) d = v.ProductName?.Trim();
                name = string.IsNullOrEmpty(d) || d.Length > 40 ? Stem(exe) : d;
            }
            i.Name = name;
            if (SHDefExtractIcon(exe, 0, 0, out IntPtr large, out IntPtr small, (16u << 16) | 32u) == 0)
            {
                i.Icon32 = Pixels(large, 32);
                i.Icon16 = Pixels(small, 16);
                if (large != IntPtr.Zero) DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
        }
        catch { }
        finally { i.Done = true; }
    }

    // an icon as premultiplied BGRA pixels, n x n
    static uint[] Pixels(IntPtr icon, int n)
    {
        if (icon == IntPtr.Zero) return null;
        using var ic = System.Drawing.Icon.FromHandle(icon);
        using var src = ic.ToBitmap();
        using var bmp = new System.Drawing.Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, n, n);
        }
        var px = new uint[n * n];
        var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, n, n), System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try { Marshal.Copy(bd.Scan0, (int[])(object)px, 0, px.Length); }
        finally { bmp.UnlockBits(bd); }
        return px;
    }
}
