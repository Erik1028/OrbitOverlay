using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using Vortice.DirectWrite;
using Windows.Graphics.Capture;

namespace Orbit;

// The startup's one-time work that is not compiling, done on a thread of its own before SteamVR's first frames (Erik 10-24, the boot:
// line: #2 = the first monitor's capture 110 ms, the next ones 16 - the capture's classes, DLLs and CsWinRT's wrappers met for the first
// time; #6 = the watch's first draw 134 ms, its "time" alone 75 - the run's first text: the font files opened, the faces made, the shaper
// and the system fallback started). Start (the top of OrbitApp's constructor, while SteamVR connects): a capture item of its own, its
// event and size (MonitorCapture.WarmItem). GpuReady (the GPU made, the language and the style applied): a frame pool and a session on
// that item, never started (MonitorCapture.WarmSession), the clock's strings in the watch's culture, then each font of the style laid
// out on the shared DirectWrite factory (Painter.WarmFonts). Nothing here touches Direct2D, the immediate context, the Painter or Spikes
// (the main loop's only); the D3D device only through its own thread-safe methods. BootTick waits for it - half a second at most -
// before the first capture and the first draws, so what it loads is not fought over. One "warm-up:" line says what it did, how long.
static class BootWarm
{
    static volatile bool _capture, _fonts, _stop;
    public static bool CaptureReady => _capture;     // the capture's part done (or failed, or never started): the first capture may come
    public static bool FontsReady => _fonts;         // the fonts' part too: the first draws may come
    static Thread _t;
    // (set on the main thread before _go, read on the warm-up's after it)
    static Gpu _g;
    static List<(string family, FontStretch stretch, bool icon)> _list;
    static CultureInfo _culture;
    static readonly ManualResetEventSlim _go = new(false);
    static readonly List<IDisposable> _kept = new();  // the warm-up's formats and layouts: the factory keeps their faces cached while held

    // the top of OrbitApp's constructor (only there: the --snapshot tools never warm)
    public static void Start()
    {
        try
        {
            // Normal, not BelowNormal: VRChat keeps every core busy, and a starved warm-up would only make the first steps wait for it.
            // MTA: combase set up on the thread for the capture's classes (the multithreaded apartment outlives it)
            _t = new Thread(Run) { IsBackground = true, Name = "OrbitOverlay warm-up", Priority = ThreadPriority.Normal };
            _t.SetApartmentState(ApartmentState.MTA);
            _t.Start();
        }
        catch (Exception e) { _t = null; _capture = _fonts = true; Log.Warn("warm-up: " + e.Message); }
    }

    // the GPU made, the language and the style applied (the main thread): what to warm read here - the thread reads no Theme statics
    public static void GpuReady(Gpu g)
    {
        if (_t == null) return;
        _g = g;
        _culture = CultureInfo.CurrentCulture;       // the language's (Lang.Set sets the main thread's), else this PC's
        try
        {
            var list = new List<(string family, FontStretch stretch, bool icon)>();
            void add(string name, bool icon = false)
            {
                if (string.IsNullOrEmpty(name)) return;
                CyberHud.Resolve(name, out var real, out var stretch);      // as Painter.Format asks DirectWrite for it
                foreach (var f in list) if (f.family == real && f.stretch == stretch) return;
                list.Add((real, stretch, icon));
            }
            add(Theme.Font);
            add(Theme.Display);
            if (Theme.Cyber) add(CyberHud.Mono);
            // Look > Font: its scale measures the default pair against the chosen one (Painter.FontScale)
            if (Theme.UserFont != null && !Theme.Cyber && !Theme.Classic) { add(Theme.UiFont); add(Theme.DisplayFont); }
            add(Theme.IconFont, icon: true);
            _list = list;
        }
        catch (Exception e) { Log.Warn("warm-up fonts: " + e.Message); }
        _go.Set();
    }

    // OrbitApp.Dispose, before the Painter and the GPU go: a quit during the warm-up must not pull the factory out from under it
    public static void Stop()
    {
        if (_t == null) return;
        _stop = true;
        _go.Set();                                   // (still before the GPU: nothing more to do)
        if (!_t.Join(2000)) return;                  // stuck: its objects stay (the process is ending)
        foreach (var o in _kept) { try { o.Dispose(); } catch { } }
        _kept.Clear();
        _t = null;
    }

    static double Ms(long from) => Stopwatch.GetElapsedTime(from).TotalMilliseconds;

    static void Run()
    {
        long t0 = Stopwatch.GetTimestamp();
        double itemMs = 0, waitMs = 0, sessionMs = 0, cultureMs = 0, fontMs = 0;
        int faces = 0;
        string capErr = null, fontErr = null;
        try
        {
            // the capture's class, an item of the primary monitor, its event and size - no device needed (1 = MONITOR_DEFAULTTOPRIMARY)
            GraphicsCaptureItem item = null;
            try { item = MonitorCapture.WarmItem(Native.MonitorFromPoint(default, 1)); }
            catch (Exception e) { capErr = e.Message; }
            itemMs = Ms(t0);

            long w0 = Stopwatch.GetTimestamp();
            _go.Wait();
            waitMs = Ms(w0);
            if (_stop) return;

            // a pool and a session on that item: never started, closed at once
            if (_g != null && item != null)
            {
                long s0 = Stopwatch.GetTimestamp();
                try { MonitorCapture.WarmSession(_g, item); }
                catch (Exception e) { capErr = e.Message; }
                sessionMs = Ms(s0);
            }
            _capture = true;

            // the clock's and the date's strings in the watch's culture (WristCustom's Clk, Words, Upper; WristCyber's zone)
            long c0 = Stopwatch.GetTimestamp();
            try
            {
                var c = _culture ?? CultureInfo.CurrentCulture;
                var now = DateTime.Now;
                _ = now.ToString("dddd ddd MMMM MMM HH:mm:ss h:mm tt", c);
                _ = now.ToString(c.DateTimeFormat.MonthDayPattern, c);
                _ = c.TextInfo.ToUpper("warm \u00e1\u0151");
                _ = ISOWeek.GetWeekOfYear(now);
                _ = TimeZoneInfo.Local.GetUtcOffset(now);
            }
            catch { }
            cultureMs = Ms(c0);

            if (_g != null && _list != null)
            {
                long f0 = Stopwatch.GetTimestamp();
                try { faces = Painter.WarmFonts(_g.DWrite, _list, _kept); }
                catch (Exception e) { fontErr = e.Message; }
                fontMs = Ms(f0);
            }
        }
        catch (Exception e) { fontErr ??= e.Message; }
        finally { _capture = _fonts = true; }
        if (_stop) return;

        var names = new StringBuilder();
        if (_list != null)
            foreach (var f in _list)
            {
                if (names.Length > 0) names.Append(", ");
                names.Append(f.family);
                if (f.stretch != FontStretch.Normal) names.Append(' ').Append(f.stretch);
            }
        Log.Info($"warm-up: off the main loop, done {Ms(t0):0} ms after the start ({waitMs:0} of them waiting for the GPU) - the capture's " +
                 $"item {itemMs:0} ms, its pool + session {sessionMs:0} ms (none started), the clock's strings {cultureMs:0} ms, fonts {fontMs:0} ms " +
                 $"({faces} faces: {names})" + (capErr != null ? $"; capture not warmed: {capErr}" : "") +
                 (fontErr != null ? $"; fonts not warmed: {fontErr}" : ""));
    }
}
