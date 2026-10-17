using System;
using System.Collections.Generic;

namespace Orbit;

// Auto theater's side of the app (the decision: Sys/AutoTheater.cs). While SteamVR runs and Layouts > Theater mode > Auto theater is
// on, 4 looks a second at the window in front (and at the one it started theater for, by its handle - a video stays full screen on its
// monitor while you click something on another one): a video going full screen starts theater mode on that monitor's panel, the way
// the bar's Theater button does; leaving full screen stops it, the way Exit does.
// What plays: the Windows media session (the Media widget's, MediaInfo) when it is that player's; else the player's own sound on the
// output device (the mixer's meters - VLC and mpv have no media session), asked only while a full-screen video waits to start.
// One log line when it starts, stops, or is held back after an Exit by hand. A look allocates nothing.
public sealed partial class OrbitApp
{
    readonly AutoTheater _auto = new();
    readonly List<AutoTheater.Screen> _autoScreens = new();
    AutoTheater.Win _autoFront, _autoWatched;
    ITheaterScreen _autoScreen;             // the panel it started theater on (theater switched to another one: Erik's now)
    string _autoName;                       // the program it started for (the log)
    string _autoExeFor, _autoExe;           // "\vlc.exe": the mixer's key ends with it (made once per program)
    double _autoNext, _autoHeard = -10, _autoAsked = -10;

    // from Tick after the theater's own update, every frame (it looks only every 0.25 s)
    void UpdateAutoTheater(double now, bool awake)
    {
        if (now < _autoNext) return;
        _autoNext = now + AutoTheater.Every;
        if (!C.Theater.Auto) { AutoTheaterReset(); return; }
        // the monitors as they are on the desk; a panel that can play (and the desktops shown: hidden, they stay hidden)
        _autoScreens.Clear();
        bool shown = C.General.DesktopsVisible;
        foreach (var d in _desktops)
            _autoScreens.Add(new AutoTheater.Screen { X = d.Mon.X, Y = d.Mon.Y, W = d.Mon.W, H = d.Mon.H, Panel = shown && d.Playable });
        WindowLook.Look(Native.GetForegroundWindow(), ref _autoFront);
        nint watch = _auto.Watch;
        if (watch == _autoFront.Hwnd) _autoWatched = _autoFront;
        else WindowLook.Look(watch, ref _autoWatched);
        // ready: worn too - with "pause when off" turned off the panels stay awake, but a theater nobody sees should not start
        bool ready = awake && _vr.HeadsetWorn && _vr.HmdValid, on = _theater.On;
        bool playing = _auto.WantsPlaying(on, ready, _autoFront, _autoScreens) && AutoPlaying(now, _autoFront.Process);
        var act = _auto.Step(now, true, ready, on, on && _theater.Screen == _autoScreen, _autoFront, _autoWatched, playing, _autoScreens);
        if (_auto.LatchedNow) Log.Info("auto theater: ended by hand - not again until that video leaves full screen");
        if (act == AutoTheater.Act.Start)
        {
            var d = _desktops[_auto.Monitor];
            _autoScreen = d;
            _autoName = _autoFront.Process;
            Log.Info($"auto theater: {_autoName} went full screen on {d.TheaterLabel} - theater on");
            StartTheater(d);
        }
        else if (act == AutoTheater.Act.Stop)
        {
            Log.Info($"auto theater: {_autoName} left full screen - theater off");
            _autoScreen = null;
            StopTheater(glide: true);
        }
        if (!_auto.Own) _autoScreen = null;
    }

    // Does the front window's player play? Its own media session says (paused is paused) - or, with no session of its own (VLC, mpv;
    // or another app's session in front of it: Spotify), its sound: any of its streams on the output device peaking in the last 2 s.
    bool AutoPlaying(double now, string exe)
    {
        var (playing, app) = _media.Source();
        if (AutoTheater.SessionIs(exe, app)) return playing;
        // the first look after a pause: the mixer's list is as it was when it was last wanted (minutes ago, maybe - a meter from back
        // then would count as sound now and start theater over a paused video 1.5 s later). Its meters are read in this frame's mixer
        // tick: from the next look on they are now's.
        bool fresh = now - _autoAsked < AutoTheater.Every * 2;
        _autoAsked = now;
        if (!ReferenceEquals(exe, _autoExeFor))
        {
            _autoExeFor = exe;
            _autoExe = "\\" + exe + ".exe";
            _autoHeard = -10;
        }
        _mixer.Want(now);                   // the list kept fresh (with its meters) for the next looks
        if (!fresh) { _autoHeard = -10; return false; }
        foreach (var a in _mixer.Apps)
            if (a.Peak > 0.002f && a.Key != null && a.Key.EndsWith(_autoExe, StringComparison.OrdinalIgnoreCase)) _autoHeard = now;
        return now - _autoHeard < 2;
    }

    // the setting off, SteamVR gone: everything forgotten (a theater it started stays on - nothing ends it by itself after this)
    void AutoTheaterReset()
    {
        _auto.Reset();
        _autoScreen = null;
    }
}
