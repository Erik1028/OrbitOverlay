using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace Orbit;

// "Now playing" from the Windows media session (the same source as the volume flyout):
// title, artist, cover art and play state of whatever player is active (Spotify, browser, ...),
// plus play/pause and next/previous. Read on a background thread when Windows reports a change
// (session switched, new track, play/pause) and every 10 s as a safety net. It used to poll every
// second: each poll left WinRT objects behind that only a GC frees (~14 MB a minute, measured),
// and Orbit's VR loop now makes so little garbage that GCs became rare. Some players (live streams)
// fire change events constantly, so reads are also spaced (2 s, except right after a command) and
// a small gen0 GC every 20 reads lets the WinRT wrappers - and the native memory behind them - go.
public sealed class MediaInfo : IDisposable
{
    public const int ArtSize = 96;
    readonly object _lock = new();
    string _title, _artist;
    bool _playing, _has;
    uint[] _art;
    public int Version { get; private set; }
    GlobalSystemMediaTransportControlsSessionManager _mgr;
    volatile bool _stop;
    public volatile bool Active;           // set by the app while SteamVR is connected
    readonly AutoResetEvent _wake = new(false);

    public MediaInfo()
    {
        new Thread(Run) { IsBackground = true, Name = "media" }.Start();
    }

    public (bool has, string title, string artist, bool playing, uint[] art, int version) Snapshot()
    {
        lock (_lock) return (_has, _title, _artist, _playing, _art, Version);
    }

    // The player behind the session and whether it plays (auto theater, Sys/AutoTheater.cs: is it the full-screen window's player that
    // plays?). app: the session's SourceAppUserModelId - "Chrome", "MSEdge", "vlc.exe", Firefox's install hash...; null: no session.
    string _app;
    public (bool playing, string app) Source()
    {
        lock (_lock) return (_playing, _app);
    }

    GlobalSystemMediaTransportControlsSession _watched;
    public int Reads;            // how many times the session was read (tests)
    volatile bool _now;          // a command was sent: read again right away
    int _sinceCollect;

    void OnSessionEvent(object sender, object args) => _wake.Set();

    // follow the current session's change events (and drop the previous session's)
    void Watch(GlobalSystemMediaTransportControlsSession s)
    {
        if (ReferenceEquals(s, _watched)) return;
        try
        {
            if (_watched != null)
            {
                _watched.MediaPropertiesChanged -= OnSessionEvent;
                _watched.PlaybackInfoChanged -= OnSessionEvent;
            }
        }
        catch { }
        _watched = s;
        try
        {
            if (s != null)
            {
                s.MediaPropertiesChanged += OnSessionEvent;
                s.PlaybackInfoChanged += OnSessionEvent;
            }
        }
        catch (Exception e) { Log.Warn("media events: " + e.Message); }
    }

    void Run()
    {
        try { _mgr = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception e) { Log.Warn("media session: " + e.Message); return; }
        try { _mgr.CurrentSessionChanged += OnSessionEvent; } catch (Exception e) { Log.Warn("media events: " + e.Message); }
        string lastKey = null;
        double keyAt = 0;
        bool artOk = false;
        int artTries = 0;
        while (!_stop)
        {
            try
            {
                Reads++;
                var s = _mgr.GetCurrentSession();
                Watch(s);
                string app = null;
                try { app = s?.SourceAppUserModelId; } catch { }
                if (s == null)
                {
                    lastKey = null;            // the same track coming back must load its cover again
                    Set(false, null, null, false, null, null, null);
                }
                else
                {
                    var props = s.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();
                    bool playing = s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    string key = props?.Title + "\u0001" + props?.Artist;
                    uint[] art = null;
                    bool newKey = key != lastKey;
                    if (newKey) { keyAt = Clock.Now; artOk = false; artTries = 0; }
                    // players often send the title first and the cover a moment later: keep looking at the
                    // thumbnail for the first seconds of a track, and retry a failed load a few times
                    bool loadArt = props?.Thumbnail != null && (newKey || Clock.Now - keyAt < 8 || (!artOk && artTries < 3));
                    if (loadArt)
                    {
                        artTries++;
                        try { art = LoadArt(props.Thumbnail); artOk = true; } catch { art = null; }
                    }
                    lastKey = key;
                    Set(!string.IsNullOrEmpty(props?.Title), props?.Title, props?.Artist, playing, art, newKey || (loadArt && art != null) ? "new" : null, app);
                    ReadTimeline(s);
                }
            }
            catch (Exception e) { Log.Warn("media: " + e.Message); }
            if (++_sinceCollect >= 20)
            {
                _sinceCollect = 0;
                GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            }
            // a change event (or a command) wakes it; the timeout is only a safety net
            double readAt = Clock.Now;
            _wake.WaitOne(Active ? 10000 : 30000);
            // bursts of events: at most one read every 2 s (a command reads at once)
            while (!_stop && !_now && Clock.Now - readAt < 2.0) _wake.WaitOne(100);
            _now = false;
        }
    }

    // where in the track it is: the session's timeline, read with the rest (a seek shows at the next read) and run on from the
    // moment the player last updated it while it plays (a player that never stamps it: from when it was read)
    double _pos, _dur;
    DateTimeOffset _posAt;
    bool _tlOk;

    void ReadTimeline(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            var tl = s.GetTimelineProperties();
            double dur = (tl.EndTime - tl.StartTime).TotalSeconds, pos = (tl.Position - tl.StartTime).TotalSeconds;
            var at = tl.LastUpdatedTime;
            var now = DateTimeOffset.Now;
            if (at > now || now - at > TimeSpan.FromHours(12)) at = now;
            lock (_lock) { _dur = dur; _pos = pos; _posAt = at; _tlOk = dur >= 1 && pos >= 0; }
        }
        catch { lock (_lock) _tlOk = false; }
    }

    // ok: the player says where it is (pos and dur in seconds)
    public (bool ok, double pos, double dur) Timeline()
    {
        lock (_lock)
        {
            if (!_tlOk || !_has) return (false, 0, 0);
            double p = _pos + (_playing ? (DateTimeOffset.Now - _posAt).TotalSeconds : 0);
            return (true, Math.Clamp(p, 0, _dur), _dur);
        }
    }

    void Set(bool has, string title, string artist, bool playing, uint[] art, string artChanged, string app)
    {
        lock (_lock)
        {
            _app = app;         // with the play state it belongs to (a new player with the old one's state would start auto theater)
            bool changed = has != _has || title != _title || artist != _artist || playing != _playing || artChanged != null;
            if (!changed) return;
            _has = has; _title = title; _artist = artist; _playing = playing;
            if (artChanged != null || !has) _art = art;
            Version++;
        }
    }

    static uint[] LoadArt(Windows.Storage.Streams.IRandomAccessStreamReference thumb)
    {
        using var ras = thumb.OpenReadAsync().AsTask().GetAwaiter().GetResult();
        using var stream = ras.AsStreamForRead();
        using var src = Image.FromStream(stream);
        return CoverFit(src, ArtSize);
    }

    // any image -> size x size premultiplied BGRA, scaled to fill the square (cropping the overhang)
    public static uint[] CoverFit(Image src, int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float s = Math.Max(size / (float)src.Width, size / (float)src.Height);
            float w = src.Width * s, h = src.Height * s;
            g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
        }
        var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var px = new uint[size * size];
        for (int y = 0; y < size; y++)
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, Ints(px), y * size, size);
        bmp.UnlockBits(data);
        return px;
    }

    // Marshal.Copy has no uint[] overload; reinterpret the same array as int[]
    static int[] Ints(uint[] a) => System.Runtime.CompilerServices.Unsafe.As<int[]>(a);

    public void Command(string what)
    {
        var mgr = _mgr;
        if (mgr == null) return;
        Task.Run(async () =>
        {
            try
            {
                var s = mgr.GetCurrentSession();
                if (s == null) return;
                switch (what)
                {
                    case "play": await s.TryTogglePlayPauseAsync(); break;
                    case "next": await s.TrySkipNextAsync(); break;
                    case "prev": await s.TrySkipPreviousAsync(); break;
                }
                await Task.Delay(150);
                _now = true;
                _wake.Set();
            }
            catch (Exception e) { Log.Warn("media command: " + e.Message); }
        });
    }

    public void Dispose() { _stop = true; _wake.Set(); }
}
