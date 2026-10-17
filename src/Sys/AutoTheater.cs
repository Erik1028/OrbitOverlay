using System;
using System.Collections.Generic;

namespace Orbit;

// Layouts > Theater mode > Auto theater (Erik 10-18 picked it): a video that goes full screen on one of your monitors starts theater
// mode by itself, with that monitor as the big screen, and when the video leaves full screen theater stops again - but only a theater
// it started itself. One you started, or took over by switching the big screen to another monitor, is yours to end.
//
// What counts as "a video in full screen":
//  - the window in front covers one whole monitor and hardly any more (a maximised window is not full screen: it has a caption and its
//    invisible borders reach 8 px past the screen; one stretched over two monitors is not full screen on either);
//  - it is a video player's or a browser's (the allow-list below: most games run full screen too, and a game must never start it);
//  - that monitor has a panel in VR that can play (and the desktops are shown);
//  - something plays: the Windows media session says that player plays (the Media widget's source), or the player is making sound
//    (VLC and mpv have no media session). A browser in F11 on a page plays nothing: no theater.
// It must hold for 1.5 s before theater starts (a quick alt-tab through a full-screen video does nothing), and the window must be out
// of full screen for 1 s before theater stops (alt-tab away and back, or a player in exclusive mode minimising for a moment, keeps it).
// Theater ended by hand while the same video stays full screen: it does not start again until that full screen ends - else it would pop
// straight back up 1.5 s after the Exit.
//
// This class is only the decision. The app looks at the windows a few times a second (WindowLook) and hands it what it saw, so the tools
// feed it made-up windows, monitors, players and clocks (--snapshot, ORBIT_SNAP_ONLY=autotheater). A step allocates nothing.
public sealed class AutoTheater
{
    public const double Hold = 1.5;         // full screen (and playing) this long before it starts
    public const double Gone = 1.0;         // out of full screen this long before it stops - or before a by-hand Exit is forgotten
    public const double Every = 0.25;       // how often the app looks: 4 times a second

    public enum Act { None, Start, Stop }

    // a window as the app saw it this look (Hwnd 0: no window / it is gone)
    public struct Win
    {
        public nint Hwnd;
        public int L, T, R, B;              // GetWindowRect: physical pixels on the desktop
        public bool Framed;                 // it has a caption (a normal window)
        public bool Shown;                  // visible, not minimised, not cloaked (on another virtual desktop)
        public string Process;              // its program, lower case without ".exe" ("chrome", "vlc")
    }

    // a monitor (physical pixels) and whether its panel can play the big screen now
    public struct Screen
    {
        public int X, Y, W, H;
        public bool Panel;
    }

    nint _cand, _ours, _latch;
    int _candMon = -1;
    double _candSince, _outSince = -1, _latchOut = -1;
    Screen _oursAt;                         // the monitor it started on (the monitor list may be rebuilt meanwhile)
    bool _own, _stopping, _wasOn;

    public int Monitor { get; private set; } = -1;      // Act.Start: which of the screens
    public bool Own => _own;                            // theater is on and this started it
    public nint Latched => _latch;                      // ended by hand while this window stayed full screen: not for it again yet
    public bool LatchedNow { get; private set; }        // this step: theater was ended by hand over a full-screen video (the app logs it)
    // the window the app must look at by its handle as well as the one in front (0: none): the one playing, or the one held back
    public nint Watch => _own ? _ours : _latch;

    // everything forgotten: the setting turned off, SteamVR gone. A theater it started stays on - nothing stops it by itself after this.
    public void Reset()
    {
        _cand = _ours = _latch = 0;
        _candMon = -1;
        _outSince = _latchOut = -1;
        _own = _stopping = _wasOn = false;
        Monitor = -1;
        LatchedNow = false;
    }

    // Is "playing" worth asking about this look (the app's answer costs a lock and, for a player without a media session, the mixer's
    // meters)? Only when a full-screen player / browser is waiting to start theater.
    public bool WantsPlaying(bool theaterOn, bool ready, in Win front, IReadOnlyList<Screen> screens) =>
        !theaterOn && ready && !_own && front.Hwnd != _latch && Candidate(front, screens) >= 0;

    // One look. ready: SteamVR runs, the headset is on and tracked (else nothing new starts; a stop still happens). front: the window in
    // front; watched: the window Watch names, as it is now (Hwnd 0 if it is gone); playing: the front window's player plays (asked only
    // when WantsPlaying); theaterOn: theater mode is on; onOurs: on the screen this started it on.
    public Act Step(double now, bool enabled, bool ready, bool theaterOn, bool onOurs, in Win front, in Win watched, bool playing,
        IReadOnlyList<Screen> screens)
    {
        Monitor = -1;
        LatchedNow = false;
        bool stopping = _stopping;
        _stopping = false;
        bool endedByHand = _wasOn && !theaterOn && !stopping;
        _wasOn = theaterOn;
        if (!enabled) { Reset(); return Act.None; }

        // theater ended without this asking for it: the bar's Exit, the watch, the dock - or its screen went away. The video it was
        // for (or the full-screen one in front, for a theater started by hand) is held back until it leaves full screen.
        if (endedByHand)
        {
            nint w = _own ? _ours : Allowed(front.Process) && FullscreenOn(front, screens) >= 0 ? front.Hwnd : 0;
            if (w != 0) { _latch = w; _latchOut = -1; LatchedNow = true; }
        }
        // switched to another screen on the bar: Erik's theater now, it never stops it
        if (_own && theaterOn && !onOurs) { _latch = _ours; _latchOut = -1; }
        if (_own && (!theaterOn || !onOurs)) _own = false;

        // its own theater: stops once the window is out of full screen on that monitor for a second
        if (_own)
        {
            bool still = watched.Hwnd == _ours && watched.Shown && Covers(watched, _oursAt);
            if (still) _outSince = -1;
            else if (_outSince < 0) _outSince = now;
            else if (now - _outSince >= Gone - 1e-6)
            {
                _own = false;
                _stopping = true;
                _cand = 0;
                return Act.Stop;
            }
            return Act.None;
        }

        // held back: until that window has been out of full screen for a second (a blip of alt-tab does not end it)
        if (_latch != 0)
        {
            bool fs = watched.Hwnd == _latch && FullscreenOn(watched, screens) >= 0;
            if (fs) _latchOut = -1;
            else if (_latchOut < 0) _latchOut = now;
            else if (now - _latchOut >= Gone - 1e-6) { _latch = 0; _latchOut = -1; }
        }

        if (theaterOn || !ready) { _cand = 0; return Act.None; }
        int m = Candidate(front, screens);
        if (m < 0 || front.Hwnd == _latch) { _cand = 0; return Act.None; }
        if (front.Hwnd != _cand || m != _candMon) { _cand = front.Hwnd; _candMon = m; _candSince = now; }
        // the hold counts from when it went full screen; a video started later (paused at first) goes as soon as it plays
        if (!playing || now - _candSince < Hold - 1e-6) return Act.None;
        _own = true;
        _ours = front.Hwnd;
        _oursAt = screens[m];
        _outSince = -1;
        _cand = 0;
        _wasOn = true;              // the app starts it now; if it does not come on, that counts as an Exit (no retry 4 times a second)
        Monitor = m;
        return Act.Start;
    }

    // the monitor with a panel that a player's / browser's window fills, or -1
    public static int Candidate(in Win w, IReadOnlyList<Screen> screens)
    {
        if (!Allowed(w.Process)) return -1;
        int m = FullscreenOn(w, screens);
        return m >= 0 && screens[m].Panel ? m : -1;
    }

    // the monitor a window fills, or -1
    public static int FullscreenOn(in Win w, IReadOnlyList<Screen> screens)
    {
        if (w.Hwnd == 0 || !w.Shown) return -1;
        for (int i = 0; i < screens.Count; i++)
            if (Covers(w, screens[i])) return i;
        return -1;
    }

    // The whole monitor and hardly past it. A window without a caption may reach a few px over (some players do); one with a caption
    // must sit exactly on it (Movies & TV's frame in full screen) - a maximised one is short of the taskbar or 8 px past the edges.
    public static bool Covers(in Win w, in Screen s)
    {
        int over = w.Framed ? 1 : 12;
        int l = s.X, t = s.Y, r = s.X + s.W, b = s.Y + s.H;
        return w.L <= l + 1 && w.T <= t + 1 && w.R >= r - 1 && w.B >= b - 1 &&
               w.L >= l - over && w.T >= t - over && w.R <= r + over && w.B <= b + over;
    }

    // ---- who may start it ----

    // Video players and browsers, by program (lower case, no ".exe"), and a word of their media session's app id. Nothing else: a game
    // in full screen with music playing must not start it. Hash: Firefox and its kin - their session's app id is the install's hash
    // ("308046B0AF4A39CB"), not a name.
    static readonly (string Exe, string Session, bool Hash)[] Players =
    {
        ("vlc", "vlc", false), ("mpv", "mpv", false), ("mpvnet", "mpv", false), ("mpc-hc", "mpc", false), ("mpc-hc64", "mpc", false),
        ("mpc-be", "mpc", false), ("mpc-be64", "mpc", false), ("mpc-qt", "mpc", false), ("potplayer", "potplayer", false),
        ("potplayer64", "potplayer", false), ("potplayermini", "potplayer", false), ("potplayermini64", "potplayer", false),
        ("kmplayer", "kmplayer", false), ("kmplayer64x", "kmplayer", false), ("smplayer", "smplayer", false), ("stremio", "stremio", false),
        ("wmplayer", "mediaplayer", false),
        ("video.ui", "zunevideo", false),               // Movies & TV (its window is ApplicationFrameHost's: WindowLook finds the app)
        ("microsoft.media.player", "zunemusic", false), // Windows 11's Media Player
        ("chrome", "chrome", false), ("msedge", "edge", false), ("brave", "brave", false), ("opera", "opera", false),
        ("vivaldi", "vivaldi", false), ("chromium", "chromium", false), ("thorium", "thorium", false), ("arc", "arc", false),
        ("firefox", "firefox", true), ("librewolf", "librewolf", true), ("waterfox", "waterfox", true), ("floorp", "floorp", true),
        ("zen", "zen", true),
    };

    static int Find(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return -1;
        for (int i = 0; i < Players.Length; i++)
            if (string.Equals(Players[i].Exe, exe, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public static bool Allowed(string exe) => Find(exe) >= 0;

    // Does the media session's app (GlobalSystemMediaTransportControlsSession.SourceAppUserModelId: "Chrome", "MSEdge", "vlc.exe",
    // "Microsoft.ZuneVideo_8wekyb3d8bbwe!Microsoft.ZuneVideo", Firefox's "308046B0AF4A39CB") belong to this program? Spotify playing
    // behind a paused video in Chrome is not Chrome playing.
    public static bool SessionIs(string exe, string app)
    {
        int i = Find(exe);
        if (i < 0 || string.IsNullOrEmpty(app)) return false;
        if (app.Contains(Players[i].Session, StringComparison.OrdinalIgnoreCase)) return true;
        return Players[i].Hash && IsHash(app);
    }

    static bool IsHash(string s)
    {
        if (s.Length != 16) return false;
        foreach (char c in s)
            if (!char.IsAsciiHexDigit(c)) return false;
        return true;
    }
}
