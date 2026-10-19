using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The custom face: the widgets the user put on the 6 x 4 grid (WatchWidgets), in both styles.
// Modern: the Bento look (Bento.cs) - rounded cards with a caption, the value big and bold, colours by role - and motion
// (widgets cascade in when you look at the watch, spring into a new place, digits roll, lines fill).
// Windows 95: little 95 windows on a teal desktop (or windows only), no motion - 1995 had none.
// Cyber (Look > Style): the modern face, its cards, captions, lines and buttons in Cyber's forms and the concept's own designs
// (WristCyber.cs); a widget's own look (Style / Color) does not apply there.
public sealed partial class WristPanel
{
    bool _layoutChecked, _layoutSeen;
    public readonly SystemStats Stats = new();
    public readonly SystemVolume Volume = new() { Fake = true };     // made-up until the app hands it the real device (UseRealAudio)

    // the page in view (pages: WristPages.cs)
    List<WatchWidget> Layout => PageList(CurPage);

    // --snapshot's time-family pictures: a fixed moment (the longest month and day names, the widest digits); null = the clock
    public DateTime? TestNow;
    // ORBIT_SNAP_NOW (the tools; never the app): every watch's clock held at this moment unless a tool sets its own, so two runs'
    // pictures can be held against each other pixel by pixel (ToolsLang.Snapshot)
    public static DateTime? TestNowDefault;
    DateTime Now => TestNow ?? TestNowDefault ?? DateTime.Now;
    public float TestScale = 1;          // --snapshot's close-ups: the modern watch drawn bigger (what fits of it)

    // on the page in view (and the one sliding in beside it)
    bool HasWidget(string type)
    {
        if (Has(WatchPages.Widgets(C, CurPage), type)) return true;
        return _slideOther >= 0 && _slideOther < WatchPages.Count(C) && Has(WatchPages.Widgets(C, _slideOther), type);
    }

    static bool Has(List<WatchWidget> list, string type)
    {
        if (list == null) return false;
        foreach (var w in list) if (w?.Type == type) return true;      // a hand-edited list can hold a null
        foreach (var w in list) if (w?.Children != null && WatchFolders.Has(w.Children, type)) return true;     // in a folder there (WristFolder.cs)
        return false;
    }

    // the frame-time samples are only collected while something shows them - a graph on any page (its 30 s are ready when you
    // swipe to it)
    public bool WantsFrames => C.Face == "custom" ? WatchPages.Any(C, "graph") || DetailIs("perf") : C.ShowPerformance && C.PerfGraph && C.Face is "full" or "analog";

    // something on the page in view moves with the frames (redrawn 10 times a second)
    bool FramesShown => HasWidget("graph") || DetailIs("perf");

    // stable ids for a widget's animations (the list order changes while editing)
    readonly Dictionary<WatchWidget, int> _wid = new();
    int _widNext;
    int Wid(WatchWidget w)
    {
        if (!_wid.TryGetValue(w, out int id)) _wid[w] = id = ++_widNext;
        return id;
    }

    float FitSize(string s, float maxW, float maxSize, FontWeight w, string family)
    {
        if (string.IsNullOrEmpty(s)) return maxSize;
        float w100 = _p.MeasureWidth(s, 100, w, family);
        float size = w100 <= 0 ? maxSize : MathF.Max(8, MathF.Min(maxSize, 100 * maxW / w100));
        return MathF.Floor(size * 2) / 2;          // half-pixel steps: a handful of text formats, not one per size
    }

    // a title over two lines: as many words as fit on the first, the rest (cut if it must be) on the second
    (string, string) TwoLines(string s, float maxW, float size, FontWeight wt)
    {
        s = Lang.T(s);                     // cut in the language picked
        if (_p.MeasureWidth(s, size, wt) <= maxW) return (s, "");
        int cut = -1;
        for (int i = s.IndexOf(' '); i > 0; i = s.IndexOf(' ', i + 1))
        {
            if (_p.MeasureWidth(s[..i], size, wt) > maxW) break;
            cut = i;
        }
        if (cut < 0) return (Ellipsize(s, maxW, size, wt), "");
        return (s[..cut], Ellipsize(s[(cut + 1)..].TrimStart(), maxW, size, wt));
    }

    // "A long song ti…" (never splits an emoji)
    // (shorter: the short form of a text made already - translated, so the table cannot be asked for it)
    string Ellipsize(string s, float maxW, float size, FontWeight wt, string family = null, string shorter = null)
    {
        string sh = shorter ?? Lang.Short(s);
        s = Lang.T(s);                     // cut in the language picked (its short form first, where it has one)
        if (string.IsNullOrEmpty(s) || _p.MeasureWidth(s, size, wt, family) <= maxW) return s;
        if (sh != null && _p.MeasureWidth(s = sh, size, wt, family) <= maxW) return s;
        int lo = 0, hi = s.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_p.MeasureWidth(s[..mid].TrimEnd() + "…", size, wt, family) <= maxW) lo = mid; else hi = mid - 1;
        }
        if (lo > 0 && char.IsHighSurrogate(s[lo - 1])) lo--;
        return s[..lo].TrimEnd() + "…";
    }

    (bool has, string title, string artist, bool playing) MediaNow(PanelData d)
    {
        bool has; string title, artist; bool playing; uint[] art = null; int ver = -2;
        if (d.FakeMedia is { } f) { (has, title, artist, playing) = f; if (d.FakeArt != null) (art, ver) = (d.FakeArt, -3); }
        else if (d.Media != null) (has, title, artist, playing, art, ver) = d.Media.Snapshot();
        else { has = false; title = artist = null; playing = false; }
        // Mixtape's song when it plays, or is paused with no other player playing (WristMixtape.cs): its title, its buttons, its cover
        _mtLead = MixtapeLeads(has, title, playing);
        if (_mtLead)
        {
            var st = Mixtape.State;
            has = st.Track != null;
            (title, artist, playing) = (st.Track?.Title, st.Track?.Artist, st.Playing);
            art = has ? Mixtape.Cover(st.Track.Cover, MtCardCover) : null;      // (asked for: it comes with a later frame)
            ver = -4;
        }
        if (ver != _artVersion || !ReferenceEquals(art, _artSource))
        {
            _artVersion = ver;
            // the version also moves on play / pause and title changes: only a new picture is rebuilt (and fades in)
            if (!ReferenceEquals(art, _artSource) || (art == null) != (_art == null))
            {
                _artSource = art;
                // the cover before stays a moment: the new one cross-fades over it (MediaOrbit)
                _artOld?.Dispose();
                _artOld = _art;
                _art = art != null ? _p.CreateBitmap(art, (int)MathF.Sqrt(art.Length), (int)MathF.Sqrt(art.Length)) : null;      // (square: 96 or Mixtape's 128)
                _artFade = !_settled;
            }
        }
        return (has, title, artist, playing);
    }
    bool _artFade;                             // a new cover: it fades in over the old one (the next frame's media cards)
    uint[] _artSource;
    Vortice.Direct2D1.ID2D1Bitmap _artOld;     // the cover before, under the new one while it fades in

    static bool IsOn(string id, PanelData d) =>
        (id == "keyboard" && d.KeyboardVisible) || (id == "desktops" && d.DesktopsVisible) || (id == "night" && d.NightMode) ||
        (id == "status" && d.StatusLine) || (id == "theater" && d.Theater);

    // the buttons that are on or off (IsOn); recenter, layout and settings only do something
    static bool IsToggle(string id) => id is "keyboard" or "desktops" or "night" or "status" or "theater";

    void SampleSources()
    {
        double now = Clock.Now;
        if (HasWidget("cpu") || HasWidget("ram")) Stats.Sample(now);
        if (HasWidget("volume")) Volume.Sample(now);
        if (HasWidget("mic")) Mic.Sample(now);
        if (MixerOn) MixerFrame(now);
        if (MixtapeOn) MixtapeFrame();              // the Mixtape link's wishes (WristMixtape.cs)
    }

    // ---- timer and stopwatch: one of each, shared by their widgets; they keep running while the watch is hidden ----

    enum TimerState { Idle, Running, Paused, Done }
    TimerState _timer;
    int _timerTotal = 300;                 // seconds
    double _timerEnd, _timerLeftPaused;
    double _swStart, _swAccum;
    bool _swRunning;

    double TimerLeft(double now) => _timer switch
    {
        TimerState.Running => Math.Max(0, _timerEnd - now),
        TimerState.Paused => _timerLeftPaused,
        TimerState.Done => 0,
        _ => _timerTotal,
    };

    void TimerTap()
    {
        double now = Clock.Now;
        switch (_timer)
        {
            case TimerState.Idle: _timerEnd = now + _timerTotal; _timer = TimerState.Running; break;
            case TimerState.Running: _timerLeftPaused = Math.Max(0, _timerEnd - now); _timer = TimerState.Paused; break;
            case TimerState.Paused: _timerEnd = now + _timerLeftPaused; _timer = TimerState.Running; break;
            default: _timer = TimerState.Idle; break;
        }
        _dirty = true;
    }

    void TimerAdjust(int minutes)
    {
        double now = Clock.Now;
        if (_timer == TimerState.Running) _timerEnd = Math.Max(now + 1, _timerEnd + minutes * 60);
        else if (_timer == TimerState.Paused) _timerLeftPaused = Math.Max(1, _timerLeftPaused + minutes * 60);
        else { _timerTotal = Math.Clamp(_timerTotal + minutes * 60, 60, 99 * 60); _timer = TimerState.Idle; }
        _dirty = true;
    }

    void TimerReset() { _timer = TimerState.Idle; _dirty = true; }

    double StopwatchTime(double now) => _swAccum + (_swRunning ? now - _swStart : 0);

    void StopwatchTap()
    {
        double now = Clock.Now;
        if (_swRunning) { _swAccum += now - _swStart; _swRunning = false; }
        else { _swStart = now; _swRunning = true; }
        _dirty = true;
    }

    void StopwatchReset() { _swRunning = false; _swAccum = 0; _dirty = true; }

    // called every frame from Update (also while the watch is hidden)
    void TickTimers(double now)
    {
        if (_timer == TimerState.Running && now >= _timerEnd)
        {
            _timer = TimerState.Done;
            _dirty = true;
            Act("timer.done");
        }
    }

    // something on the face changes faster than twice a second
    bool FastWidgets => (_timer == TimerState.Running && HasWidget("timer")) || (_swRunning && HasWidget("stopwatch")) || _open != null ||
                        (MinesTicking && HasWidget("minesweeper"));

    static string Clock2(double seconds)
    {
        int s = (int)Math.Ceiling(Math.Max(0, seconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    static string StopwatchText(double t, bool tenths)
    {
        int cs = (int)(t * 10);
        int m = cs / 600, s = cs / 10 % 60;
        return tenths ? $"{m}:{s:00}.{cs % 10}" : $"{m}:{s:00}";
    }

    // ================= Modern =================
    // The Bento look (Bento.cs): rounded cards on the watch page, a small UPPERCASE caption at the top-left of each, the value
    // big, bold and left-aligned with one line under it; buttons are tiles. Colours by role - the accent for time and the frame
    // numbers, purple for the CPU, blue for memory - and Ok / Warn / Bad only when a value needs attention, then its text too.
    // Sized for reading at arm's length in VR (the watch is about 13 cm wide): captions 15 px, any other text 17 px or more.
    // And motion: widgets cascade in when you look at the watch, spring into a new place, digits roll.

    static uint Sub => Bento.Sub;              // the widget's look's grey (Bento.Use)
    const float CardRadius = Bento.CardRadius;
    const float CapSize = 15, CapTrack = 1;    // captions: UPPERCASE, SemiBold, 1 px between the letters
    const float CapBase = 25;                  // the caption row's baseline, from the card's top
    const float SmallText = 17;                // the smallest mixed-case text (units, notes, names)
    // The value's baseline on a 1x1 / on a one-row wide card, and a 1x1's value: every one this big (a long one shrinks only when it
    // alone cannot fit). On a 4 x 3 page the cards are taller (133 px): lower and bigger there.
    bool _big;                                 // the widget being drawn is on a 4 x 3 page
    float SBase => _big ? 86 : 64;
    float MBase => _big ? 94 : 66;
    float Hero => _big ? 40 : 30;
    float NoteBase => _big ? 112 : 85;         // a 1x1's note under its value
    const float DigitH = 0.7f;                 // how tall digits are, in em (Segoe UI Variable Display)
    const float DigitTop = 0.72f, DigitSpan = 0.735f;   // the digits' real top (the 1's flag) and top to the round ones' overshoot, in em
    const string PlayFill = "\uF5B0", PauseFill = "\uF8AE";   // the play disc's glyphs: Segoe Fluent's filled play / pause
    const string MuteGlyph = "\uE74F";         // a speaker with a cross: a muted volume's value
    const string NotPlaying = "Not playing";  // no media: one wording on every size and opened ("Nothing playing" is too long for a 2x1)
    double _introStart = -10;
    bool _rolling;

    static uint OnColor(uint fill) => Theme.Luma(fill) > 0.55f ? 0xFF0B1220 : 0xFFFFFFFF;

    // (English writes "October 15, Thursday"; a language that writes its months and days small - Magyar: "csütörtök, okt. 15." - keeps them)
    static string Capital(string s) => string.IsNullOrEmpty(s) || Lang.Has("date|small") ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];

    // UPPERCASE forms, made once (captions, device names): a ToUpper per caption per frame was most of a frame's garbage
    static readonly Dictionary<string, string> _upper = new();
    static CultureInfo _upperCulture;
    static int _upperLang = -1;

    // (in the language picked: a caption measured, cut or typed letter by letter is the translated one)
    static string Upper(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var culture = CultureInfo.CurrentCulture;
        if (!ReferenceEquals(culture, _upperCulture) || _upperLang != Lang.Version) { _upper.Clear(); _upperCulture = culture; _upperLang = Lang.Version; }
        if (!_upper.TryGetValue(s, out var u))
        {
            if (_upper.Count > 256) _upper.Clear();
            _upper[s] = u = Lang.T(s).ToUpper(culture);
        }
        return u;
    }

    // a battery's label as the watch writes it ("L" / "R" are letters of the language: the table's "battery|L")
    static string Bat(string label) => Lang.C("battery", label);

    // "0%" .. "100%", made once
    static readonly string[] PctText = MakePct();
    static string[] MakePct()
    {
        var a = new string[101];
        for (int i = 0; i <= 100; i++) a[i] = i.ToString(CultureInfo.InvariantCulture) + "%";
        return a;
    }
    static string Pct(int n) => n >= 0 && n <= 100 ? PctText[n] : n.ToString(CultureInfo.InvariantCulture) + "%";

    // the captions: each kind's forms, longest first (one array each, not one per frame)
    static readonly string[] CapBattery = { "Battery", "Batt" }, CapBatteries = { "Batteries", "Battery" }, CapTimer = { "Timer" },
        CapStopwatch = { "Stopwatch", "Watch" }, CapSession = { "In VR" }, CapSessionWide = { "Time in VR", "In VR" }, CapRoom = { "Room" },
        CapVolume = { "Volume" }, CapMedia = { "Now playing", "Playing" }, CapGraph = { "Frame time", "Frames" }, CapToday = { "Today" },
        CapHexpad = { "Hexpad" }, CapFps = { "FPS" }, CapReproj = { "Reproj" }, CapGpu = { "GPU" }, CapVram = { "VRAM" }, CapCpu = { "CPU" },
        CapRam = { "RAM" }, WideFps = { "Frame rate", "FPS" }, WideReproj = { "Reprojection", "Reproj" }, WideGpu = { "GPU time", "GPU" },
        WideRam = { "Memory", "RAM" };

    static string[] SmallCaption(string id) => id switch
    {
        "fps" => CapFps, "reproj" => CapReproj, "gpu" => CapGpu, "vram" => CapVram, "cpu" => CapCpu, _ => CapRam,
    };

    static string[] WideCaption(string id) => id switch
    {
        "fps" => WideFps, "reproj" => WideReproj, "gpu" => WideGpu, "vram" => CapVram, "cpu" => CapCpu, _ => WideRam,
    };

    static readonly string[] MediaIds = { "media.prev", "media.play", "media.next" };

    // The words of the date widgets, made when the day (or the language) changes - not on every frame
    DateTime _wordsDay;
    CultureInfo _wordsCulture;
    int _wordsLang = -1;
    string _dateLine, _dayName, _monthDay, _weekNote, _weekYear, _year, _dayNum;
    readonly string[] _capMonth = new string[2], _capWeekday = new string[3], _capDayDate = new string[2], _dateForms = new string[3],
        _monthNames = new string[2];

    void Words(DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        if (now.Date == _wordsDay && ReferenceEquals(culture, _wordsCulture) && _wordsLang == Lang.Version) return;
        _wordsDay = now.Date;
        _wordsCulture = culture;
        _wordsLang = Lang.Version;
        _dateLine = Capital(DateText(now));
        _dayName = Capital(now.ToString("dddd", culture));
        string md = culture.DateTimeFormat.MonthDayPattern, mdShort = md.Replace("MMMM", "MMM");
        _monthDay = now.ToString(md, culture);
        _weekNote = Lang.F("week {0}", ISOWeek.GetWeekOfYear(now));
        _year = now.Year.ToString(culture);
        _weekYear = $"{_weekNote} · {_year}";
        _dayNum = now.Day.ToString(culture);
        string ddd = now.ToString("ddd", culture);
        _capMonth[0] = now.ToString("MMMM", culture);
        _capMonth[1] = now.ToString("MMM", culture);
        _monthNames[0] = Capital(_capMonth[0]);
        _monthNames[1] = Capital(_capMonth[1]);
        _capWeekday[0] = now.ToString("dddd", culture);
        _capWeekday[1] = ddd;
        _capWeekday[2] = ddd.Substring(0, Math.Min(2, ddd.Length));
        _capDayDate[0] = ddd + " " + _dayNum;
        _capDayDate[1] = ddd;
        // the clock's header, longest first: the date as the watch writes it ("October 15, Thursday"), shorter, the date alone
        _dateForms[0] = _dateLine;
        _dateForms[1] = Capital(Lang.Has("date|middle") ? now.ToString(Lang.T("date|middle"), culture) : now.ToString(mdShort, culture) + ", " + ddd);
        _dateForms[2] = Capital(now.ToString(mdShort, culture));
    }

    // no motion in progress: everything drawn where it ends up (snapshots, a reset)
    public void Settle()
    {
        _ui.ResetAnimations();
        _layoutSeen = false;
        _introStart = -10;
        _rolls.Clear();
        _wordFades.Clear();
        _settled = true;
        CyberSettleFx();
    }
    bool _settled;               // Settle() was just called: the next frame shows what changed as it is (no cover fading in)

    // replays the cascade (the watch just came into view) - only with Look > Motion on Full; Cyber: the HUD boots instead
    public void Intro()
    {
        if (Theme.Cyber) { CyberBoot(); return; }       // Cyber: the HUD boots instead (WristCyberFx.cs; Full only, it asks Motion)
        if (Motion.Full) _introStart = Clock.Now;
        _dirty = true;
    }

    public bool IntroRunning => Clock.Now - _introStart < 1.2;      // --snapshot's Motion check

    public bool Animating => _ui.Animating || _rolling || Clock.Now - _introStart < 1.2 || CyberAnimating;

    // A charging bolt's ink: it breathes, slow and soft (0.55..1 over 2.4 s; Motion full only - the watch then draws 30 times a second
    // while one shows: AnimWake, WristPanel.Update), else steady. --snapshot holds it still.
    uint BoltInk(uint c)
    {
        if (!Motion.Full || Motion.Still) return c;
        AnimWake.At(Clock.Now + 1 / 30.0);
        float k = 0.775f + 0.225f * MathF.Cos((float)(Clock.Now % 2.4 / 2.4) * MathF.Tau);
        return Theme.WithAlpha(c, k);
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.5f, c3 = c1 + 1;
        t -= 1;
        return 1 + c3 * t * t * t + c1 * t * t;
    }

    // Every string on the custom face (opened views included) is cut or sized to fit its place, so it is drawn without a clip per
    // string (Painter.ClipText) - the clips were a third of a frame. The layout check (--snapshot) would see one that sticks out.
    void DrawCustom(PanelData d)
    {
        _p.ClipText = false;
        try { DrawCustomFace(d); }
        finally { _p.ClipText = true; Bento.Reset(); }
    }

    // --snapshot's checks: Backgrounds = only the cards (what a widget's content sits on); the painter's NoText = all but text
    public bool Backgrounds;

    // A widget's own look (WatchWidget.Style / Color / Color2), worked out once per style, colours, night and look
    readonly Dictionary<(int, uint, uint, bool, bool, bool), Bento.Look> _looks = new();
    int _looksVersion = -1;

    static bool Styled(WatchWidget w) => w.Style != null || w.Color != null;

    Bento.Look LookOf(WatchWidget w, string kind, bool night, bool cards)
    {
        int style = Bento.StyleIndex(w.Style);
        bool own = w.Color.HasValue;
        return LookOf(style, own ? w.Color.Value | 0xFF000000 : NaturalColor(kind), style == 3 ? w.Color2 ?? 0 : 0, own, night, cards);
    }

    Bento.Look LookOf(int style, uint c, uint c2, bool own, bool night, bool cards)
    {
        if (_looksVersion != Theme.Version || _looks.Count > 64) { _looks.Clear(); _looksVersion = Theme.Version; }
        var key = (style, c, c2, own, night && style > 0, cards && style == 6);
        if (!_looks.TryGetValue(key, out var look)) _looks[key] = look = Bento.Make(style, c, c2, own, key.Item5, key.Item6);
        return look;
    }

    // a widget's colour when it has none of its own: purple for the CPU, blue for memory, the accent for everything else
    public static uint NaturalColor(string id) => id switch { "cpu" => Bento.DataPurple, "ram" or "vram" => Bento.DataBlue, _ => Theme.Accent };

    // A widget's card in its look: the fill (the default: a touch lighter at the top), a glass sheen, a header's band, the edge
    // (a hairline; a tint's in its colour, an outline's 2 px). A muted volume is the danger card whatever its look.
    void CardBack(RectangleF r, WatchWidgets.Kind k, WatchWidget w, in Bento.Look L, bool cards, bool danger)
    {
        if (danger)
        {
            _p.Fill(r, Bento.DangerFill, CardRadius);
            _p.Stroke(RectangleF.Inflate(r, -0.6f, -0.6f), Bento.DangerEdge, Bento.EdgeWidth, CardRadius - 0.6f);
            return;
        }
        // the cards are opaque on the page; floating ones (no page behind) let a little of the world through
        uint f0 = cards ? Theme.WithAlpha(L.Fill0, 0.96f) : L.Fill0, f1 = cards ? Theme.WithAlpha(L.Fill1, 0.96f) : L.Fill1;
        if (k.Id == "analog" && WatchWidgets.VariantOf(w) == "squircle") { AnalogFace(r, k, w, L, f0, f1, cards); return; }
        _p.FillGradient(r, f0, f1, CardRadius, L.Angle, L.Span);
        (_haloF0, _haloF1, _haloAngle, _haloSpan, _haloCard) = (f0, f1, L.Angle, L.Span, r);
        if (L.Sheen != 0) _p.FillGradient(r, L.Sheen, L.Sheen & 0x00FFFFFF, CardRadius, 90, 0.5f);
        if (L.Band != 0)
        {
            _p.PushClip(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, BandHeight(k, w, r) + 1));
            _p.Fill(r, L.Band, CardRadius);
            _p.PopClip();
        }
        if (L.Border != 0 && L.BorderW > 0)
        {
            float i = L.BorderW * 0.48f;
            _p.Stroke(RectangleF.Inflate(r, -i, -i), L.Border, L.BorderW, CardRadius - i);
        }
    }

    // a header's band: the caption row (17 px from 10 px down, and room under it); the date line of a tall clock; a strip
    // over a card without a caption (the dial, the dock's keys) or whose caption row holds more (the cover, round buttons
    // from 31 px down: a band would run into them)
    bool StripBand(WatchWidgets.Kind k, WatchWidget w) =>
        k.Id is "analog" or "media" or "buttons" || (k.Id == "dock" && _dockKeys) || (k.Id is "volume" or "stopwatch" or "minesweeper" && w.W >= 2) ||
        (k.Id == "mixer" && w.H == 1 && !_big) || (k.Id == "weather" && (w.W == 1 || (w.H == 1 && w.W >= 4)));     // (its picture / hours in the caption row)

    float BandHeight(WatchWidgets.Kind k, WatchWidget w, RectangleF r)
    {
        if (StripBand(k, w)) return 6;
        if (k.Id == "time" && w.H >= 2)
        {
            float ds = MathF.Round(Math.Clamp(r.Height * 0.085f, SmallText, 24));
            return MathF.Max(34, MathF.Ceiling(9 + _p.TextLine(ds, FontWeight.SemiBold).baseline + ds * 0.24f + 6));
        }
        return 34;
    }
    bool _dockKeys;

    // --snapshot's band rule: how tall a widget's header band is (0: none)
    public float BandOf(WatchWidget w)
    {
        var k = WatchWidgets.Find(w.Type);
        return k == null || k.Button || Bento.StyleIndex(w.Style) != 4 ? 0 : BandHeight(k, w, WatchWidgets.Rect(w, false, WatchPages.Grid(C, CurPage)));
    }

    // The card behind a satellite or a knob (a gap around it): its own fill there - the card's gradient laid over the card, so
    // a top light or a two-tone matches what is under it - or nothing (glass: a disc of it would show)
    uint _halo, _haloF0, _haloF1;
    float _haloAngle, _haloSpan;
    RectangleF _haloCard;                      // empty: a flat card (_halo)

    void HaloDisc(float cx, float cy, float rad)
    {
        if (_halo == 0) return;
        if (_haloCard.IsEmpty) _p.Circle(cx, cy, rad, _halo);
        else _p.CircleGradient(cx, cy, rad, _haloCard, _haloF0, _haloF1, _haloAngle, _haloSpan, (_haloF0 >> 24) < 0xFF);
    }

    void DrawCustomFace(PanelData d)
    {
        int count = WatchPages.Count(C), cur = CurPage;
        var list = Layout;
        long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
        SampleSources();
        Spikes.Piece("watch: volume / mic / cpu read", s0);
        Bento.Reset();
        bool cards = C.Background == "cards";
        if (!cards && Theme.Cyber) CyberPage();         // Cyber: the olive frame (WristCyber.cs)
        else if (!cards)
        {
            _p.Fill(new RectangleF(0, 0, TW, TH), Theme.OverlayBg, Bento.PageRadius);
            _p.Stroke(new RectangleF(1, 1, TW - 2, TH - 2), Bento.PageLine, Bento.PageEdge, Bento.PageRadius - 1);
        }
        _dockKeys = DockShows(d);
        bool muted = Volume.Level >= 0 && Volume.Muted;
        var now = Now;
        double t = Clock.Now - _introStart;
        _rolling = false;
        if (_wid.Count > WidgetTotal() + 32)
        {
            // forget only the widgets that are gone: a live widget must keep its id (and so its animation state),
            // or it would pop in again
            var live = new HashSet<WatchWidget>();
            for (int i = 0; i < count; i++) if (WatchPages.Widgets(C, i) is { } l) live.UnionWith(l);
            FolderKidsInto(live);                           // the widgets in folders (WristFolder.cs)
            foreach (var gone in new List<WatchWidget>(_wid.Keys)) if (!live.Contains(gone)) _wid.Remove(gone);
        }
        // the page in view: between two while it slides (a swipe follows the laser, then springs to its page); both drawn then
        float pos = PagePosition(count, cur);
        bool sliding = pos != cur;
        _slideOther = !sliding ? -1 : pos < cur ? (int)MathF.Floor(pos) : (int)MathF.Ceiling(pos);
        _ui.Inert = _open != null || sliding || _swiping;      // behind an opened widget (or while it slides) the watch is only a picture
        if (EditFolderOn(list)) _ui.Inert = true;             // the editor's folder over it (WristFolder.cs)
        if (sliding && !cards) _p.PushRoundedClip(new RectangleF(1, 1, TW - 2, TH - 2), Bento.PageRadius - 1);
        int p0 = (int)MathF.Floor(pos);
        for (int pi = p0; pi <= p0 + 1; pi++)
        {
            if (pi < 0 || pi >= count) continue;
            float ox = MathF.Round((pi - pos) * TW);
            if (MathF.Abs(ox) >= TW) continue;
            DrawPage(pi == cur ? list : PageList(pi), WatchPages.Grid(C, pi), ox, d, cards, muted, now, t, pi == cur && !sliding && _seenPage == cur);
        }
        if (sliding && !cards) _p.PopRoundedClip();
        _ui.Inert = false;
        _big = false;
        if (count > 1) PageDots(count, pos, cards);
        _grid = WatchPages.Grid(C, cur);
        FolderEditView(d, list);                            // the folder the editor works in, open over the page (WristFolder.cs)
        long dd0 = System.Diagnostics.Stopwatch.GetTimestamp();
        DrawDetail(d, list);
        Spikes.Piece("watch: opened widget", dd0);
        _layoutSeen = true;
        _seenPage = cur;
        _artFade = _notesSlide = _settled = false;          // a new cover / notification was taken by the cards drawn this frame
        if (t < 1.2) _ui.Animating = true;
    }

    // One page's widgets, moved ox px sideways (a page sliding in). popIn: a widget new on it pops in (the editor added it) - not
    // on a page that slides into view
    void DrawPage(List<WatchWidget> list, GridSpec grid, float ox, PanelData d, bool cards, bool muted, DateTime now, double t, bool popIn)
    {
        _grid = grid;
        _big = grid.Is43;
        bool micMuted = Mic.Present && Mic.Muted;
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            var k = WatchWidgets.Find(w.Type);
            if (k == null) continue;
            long w0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int id = Wid(w);
            var target = WatchWidgets.Rect(w, false, grid);
            // spring to a new place (the editor moved / resized it); a new one pops in
            string kx = Ui.Id("wax", id), ky = Ui.Id("way", id), kw = Ui.Id("waw", id), kh = Ui.Id("wah", id), ks = Ui.Id("was", id);
            if (popIn && _layoutSeen && !_ui.HasSpring(ks)) _ui.SpringFrom(ks, 0.4f);
            var r = new RectangleF(_ui.Spring(kx, target.X, 260, 26) + ox, _ui.Spring(ky, target.Y, 260, 26),
                _ui.Spring(kw, target.Width, 260, 26), _ui.Spring(kh, target.Height, 260, 26));
            float pop = _ui.Spring(ks, 1, 300, 18);
            // a card that opens (CardTap) lifts a touch under the laser and gives under a press - 0.97 and a light veil, like the
            // tiles; read before its own call this frame (the scale is set before it draws)
            float lift = 1, veil = 0;
            if (!Backgrounds && !Theme.Cyber && !k.Button && DetailOf(k.Id) != null)      // (Cyber: its own, WristCyber)
            {
                bool held = _ui.HeldNow(Ui.Id("wco", id)), over = !held && !Motion.Still && _ui.OverNow(r);
                lift = _ui.Spring(Ui.Id("wcl", id), held ? 0.97f : over ? 1.012f : 1, 400, 28);
                veil = _ui.Anim(Ui.Id("wcv", id), held ? 1 : over ? 0.4f : 0, 22);
            }
            // the cascade when the watch comes into view: row by row, a little spring each - tighter (Erik 10-18: "very responsive"):
            // the whole face in ~0.65 s, it was ~0.85
            float delay = (w.Y * grid.Cols + w.X) * 0.014f;
            float p = (float)Math.Clamp((t - delay) / 0.32, 0, 1);
            float scale = pop * lift * (0.82f + 0.18f * EaseOutBack(p));
            float alpha = MathF.Min(1, p * 1.6f);
            bool anim = scale < 0.999f || scale > 1.001f || alpha < 0.999f;
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            if (anim)
            {
                _p.PushScale(scale, cx, cy);
                if (alpha < 0.999f) _p.PushFade(alpha);
            }
            // its look while it draws (a muted volume or microphone is the danger card, in the default look)
            // (Cyber: one look, the concept's - a widget's own Style / Color does not apply)
            bool danger = (muted && k.Id == "volume") || (micMuted && k.Id == "mic"), styled = Styled(w) && !danger && !Theme.Cyber;
            if (styled)
            {
                var look = LookOf(w, k.Id, d.NightMode, cards);
                Bento.Use(look.Band != 0 && StripBand(k, w) ? Bento.Strip(look) : look);
            }
            _halo = Bento.Halo;
            _haloCard = RectangleF.Empty;
            if (Theme.Cyber) CyCardBegin(id, w, k, r);       // Cyber: the boot's clip (WristCyberFx.cs)
            // a card with a faint hairline (without it the cards merge into one dark slab in the headset); a button is its card; the
            // 4 x 3 now playing is its cover's colours
            if (CoverCard(k, w, styled)) CoverBack(r, d, id);
            else if (k.Id == "folder" && !styled && !Theme.Cyber) FolderBack(r, cards);        // frosted (WristFolder.cs)
            else if (PhotoCard(k, d)) PhotoBack(r, w, d);         // the photo is the card (WristPhoto.cs)
            else if (!k.Button && Theme.Cyber) CyberCard(r, k, w, cards, danger);
            else if (k.Id == "weather" && !styled && !danger) WeatherBack(r, d, cards);     // the sky of the weather in its card (WristWeather.cs)
            else if (k.Id is "light" or "rgb" && !styled && !danger) LampBack(r, k, d, cards);     // the light's colour in its card (WristLights.cs)
            else if (!k.Button) CardBack(r, k, w, Bento.Cur, cards, danger);
            if (!Backgrounds)
            {
                CardTap(id, w, k, r);
                Widget(i, id, w, k, r, d, now);
            }
            if (veil > 0.004f) _p.Fill(r, Theme.WithAlpha(0x10FFFFFF, veil), CardRadius);      // hover / press: a light veil
            if (Theme.Cyber) CyCardEnd(id, r);              // Cyber: the boot's lines, a scan sweep
            Spikes.Piece(k.Id, w0);                         // (the slowest widget of a slow frame: Sys/Spikes.cs)
            if (styled) Bento.Reset();
            if (anim)
            {
                if (alpha < 0.999f) _p.PopFade();
                _p.PopScale();
            }
        }
    }

    void Widget(int i, int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d, DateTime now)
    {
        if (Theme.Cyber && CyberWidget(id, w, k, r, d, now)) return;       // the concept's own designs (WristCyber.cs)
        if (_big && Widget43(id, w, k, r, d)) return;          // the designs made for the 4 x 3 page's bigger cards
        switch (k.Id)
        {
            case "time": TimeOrbit(r, w, now, d, id); break;
            case "date": DateOrbit(r, w, now); break;
            case "calendar": CalendarOrbit(r, w, now); break;
            case "analog": AnalogClock(r, w, now); break;          // its design (WristClocks): the squircle on a 1x1, the dial from 2x2
            case "timer": TimerOrbit(r, w, id); break;
            case "stopwatch": StopwatchOrbit(r, w, id); break;
            case "session": SessionOrbit(r, w, d, id); break;
            case "battery": BatteryOrbit(r, w, d, id); break;
            case "media":
                if (CoverCard(k, w, Styled(w))) MediaCover(r, w, d, id);      // its cover's colours (a look of its own: the classic card)
                else MediaOrbit(r, w, d, id);
                break;
            case "dock": DockOrbit(r, w, d, id); break;
            case "room":
                if (w.W >= 3) RoomM43(r, w, d, id);              // the world big, its people as initials
                else RoomOrbit(r, w, d, id);
                break;
            case "volume": VolumeOrbit(r, w, id); break;
            case "graph": GraphLine(r, w, d); break;
            case "cpu" when w.W == 1 && w.H == 1: CpuS43(r, d); break;
            case "minesweeper": MinesOrbit(r, w, id); break;
            case "mic": MicOrbit(r, w, id); break;
            case "notifications": NotesOrbit(r, w, d, id); break;
            case "buttons": GroupOrbit(r, w, d, id); break;
            case "mixer": MixerOrbit(r, w, id); break;
            case "photo": PhotoOrbit(r, w, d, id); break;
            case "weather": WeatherOrbit(r, w, d, id); break;
            case "folder": FolderOrbit(r, w, d, id); break;          // its widgets' icons; Cyber's too (WristFolder.cs)
            case "friends": FriendsOrbit(r, w, d, id); break;         // VRCX's friends online (WristFriends.cs)
            case "recap": RecapOrbit(r, w, d, id); break;             // the last session (WristRecap.cs)
            case "light": LightOrbit(r, w, d, id); break;
            case "rgb": RgbOrbit(r, w, d, id); break;
            case "memory": PhotoOrbit(r, w, d, id); break;          // On this day: the latest photo's card on its own photos (WristMemory.cs)
            case "temps": TempsOrbit(r, w, d, id); break;           // (WristTemps.cs; Cyber through the same helpers)
            case "avatar": AvatarOrbit(r, w, d, id); break;         // the avatar's toggles (WristAvatar.cs; Cyber through the same helpers)
            case "perf":
                // four small readings side by side, each with its own caption
                float qw = r.Width / 4;
                for (int j = 0; j < 4; j++) StatS(new RectangleF(r.X + j * qw, r.Y, qw, r.Height), PerfIds[j], d, Ui.Id("wg", id * 8 + j));
                break;
            default:
                if (k.Button) ButtonCell(id, w, k, r, d);
                else StatOrbit(r, w, k, d, id);
                break;
        }
    }

    static readonly string[] PerfIds = { "fps", "reproj", "gpu", "vram" };

    // When a reading needs attention: the thresholds of both styles (95 paints them with Theme.Warn / Bad, see StatInfo;
    // modern with Bento's, and the value's text too). From values already read: a number another thread moves, read twice,
    // can disagree with itself (a value in one colour, its line in another).
    static StatLevel FpsLevel(float fps, float hz) => fps >= 0 && fps < hz * 0.85f ? StatLevel.Warn : StatLevel.Ok;
    static StatLevel ReprojLevel(float pct) => pct > 25 ? StatLevel.Bad : pct > 8 ? StatLevel.Warn : StatLevel.Ok;
    static StatLevel GpuLevel(float ms, float budget) => ms > budget ? StatLevel.Bad : ms > budget * 0.85f ? StatLevel.Warn : StatLevel.Ok;
    static StatLevel VramLevel(float used, float total) => used > 0 && total > 0 && used > total * 0.92f ? StatLevel.Warn : StatLevel.Ok;
    static StatLevel CpuLevel(float c) => c > 0.9f ? StatLevel.Bad : c > 0.75f ? StatLevel.Warn : StatLevel.Ok;
    static StatLevel RamLevel(float used, float total) => used > 0 && total > 0 && used > total * 0.9f ? StatLevel.Warn : StatLevel.Ok;

    // the 95 face's colour for a level (it compares against these constants)
    static uint Tint95(StatLevel l, uint normal) => l switch { StatLevel.Bad => Theme.Bad, StatLevel.Warn => Theme.Warn, _ => normal };

    // A reading, each of its numbers read once: its fill (0..1 or -1 = no data), the value, its unit, a short caption (the widget's
    // name is shown beside it) and how it is. Its texts are made again only when its numbers change (Lang.Memo).
    (float frac, string value, string unit, string caption, StatLevel level) Reading(string id, PanelData d)
    {
        var v = d.Vr;
        float hz = v.DisplayHz, budget = 1000f / Math.Max(1, hz);
        switch (id)
        {
            case "fps":
                float fps = v.AppFps;
                return (fps < 0 ? -1 : fps / Math.Max(1, hz), fps < 0 ? "–" : _rdFps.Of(fps, static f => $"{f:0}"), "fps", _rdHz.F("of {0:0} Hz", hz),
                    FpsLevel(fps, hz));
            case "reproj":
                float rp = v.ReprojPct;
                return (rp < 0 ? -1 : MathF.Min(1, rp / 50), rp < 0 ? "–" : _rdRp.Of(rp, static f => $"{f:0}%"), "reproj.", Lang.T("of frames"), ReprojLevel(rp));
            case "gpu":
                float ms = v.GpuMs;
                return (ms < 0 ? -1 : MathF.Min(1, ms / budget), ms < 0 ? "–" : _rdMs.Of(ms, static f => $"{f:0.0}"), "ms", _rdBudget.F("of {0:0.0} ms", budget),
                    GpuLevel(ms, budget));
            case "vram":
                float vram = d.VramUsed, vt = d.VramTotalGB;
                return (vram < 0 || vt <= 0 ? -1 : vram / vt, vram < 0 ? "–" : _rdVram.Of(vram, static f => $"{f:0.0}"), "GB",
                    vt > 0 ? _rdVt.F("of {0:0} GB", vt) : Lang.T("in use"), VramLevel(vram, vt));
            case "cpu":
                float c = Stats.Cpu;
                return (c, c < 0 ? "–" : _rdCpu.Of(c, static f => $"{f * 100:0}%"), "CPU", Lang.T("total load"), CpuLevel(c));
            default:
                float used = Stats.RamUsedGB, total = Stats.RamTotalGB;
                return (used < 0 || total <= 0 ? -1 : used / total, used < 0 ? "–" : _rdRam.Of(used, static f => $"{f:0.0}"), "GB",
                    total > 0 ? _rdRamT.F("of {0:0} GB", total) : Lang.T("in use"), RamLevel(used, total));
        }
    }

    Lang.Memo<float> _rdFps, _rdHz, _rdRp, _rdMs, _rdBudget, _rdVram, _rdVt, _rdCpu, _rdRam, _rdRamT;

    // the 95 face's form of a reading: its colour Orbit's amber / red when it matters
    (float frac, string value, string unit, string caption, uint tint) StatInfo(string id, PanelData d)
    {
        var (frac, value, unit, caption, lv) = Reading(id, d);
        uint normal = id switch
        {
            "fps" or "cpu" => Theme.Accent,
            "reproj" or "vram" => Theme.Accent2,
            _ => Theme.Mix(Theme.Accent, Theme.Accent2, 0.5f),
        };
        return (frac, value, unit, caption, Tint95(lv, normal));
    }

    // a reading's colour when all is well: the accent for the frame numbers, purple for the CPU, blue for memory (lifted where
    // the card is lighter) - or the widget's own
    static uint RoleColor(string id) =>
        Bento.Data != 0 ? Bento.Data : id switch { "cpu" => Bento.Cur.Cpu, "ram" or "vram" => Bento.Cur.Mem, _ => Bento.Accent };

    // ---- the parts every card is made of ----

    static float PadX(RectangleF r) => r.Width < 120 ? 11 : 14;

    // text with its baseline at 'by'
    void TextAt(string s, float x, float by, float w, uint c, float size, FontWeight wt, TextAlignment al = TextAlignment.Leading, string family = null)
    {
        if (string.IsNullOrEmpty(s)) return;
        var (b, h) = _p.TextLine(size, wt, family);
        _p.Text(s, new RectangleF(x, by - b, w, h), c, size, wt, al, ParagraphAlignment.Near, family);
    }

    // A card's caption: UPPERCASE, letter-spaced, grey, at the top-left (from x) - the first of its forms that fits maxW.
    // Never cut: one that fits nowhere is left out. Returns the width it took. badge: on a coloured card (Bento.Badge) a level
    // that needs attention is a dot after it - its value keeps the card's text colour (a red value on a red card would vanish).
    float Caption(float x, RectangleF r, float maxW, string[] forms, StatLevel badge = StatLevel.Ok, uint color = 0)
    {
        if (Theme.Cyber) return CyberCaption(x, r, maxW, forms);      // a yellow tag
        bool dot = badge != StatLevel.Ok && Bento.CapBadge;
        if (dot) maxW -= DotW;
        foreach (var f in ShortForms(forms))
        {
            if (string.IsNullOrEmpty(f)) continue;
            string s = Upper(f);
            float w = _p.MeasureTracked(s, CapSize, FontWeight.SemiBold, CapTrack);
            if (w > maxW) continue;
            CapText(s, x, r.Y + CapBase, w, color != 0 ? color : Bento.Cap);
            if (!dot) return w;
            StatusDot(x + w + 10, r.Y + CapBase - 5, badge);
            return w + DotW;
        }
        if (dot) StatusDot(x + 7, r.Y + CapBase - 5, badge);
        return dot ? DotW - 3 : 0;
    }

    float Caption(RectangleF r, float maxW, string[] forms, StatLevel badge = StatLevel.Ok, uint color = 0) =>
        Caption(r.X + PadX(r), r, maxW, forms, badge, color);

    // a caption's forms, and after them a shorter one a language has of the last ("short|Volume": Magyar "Hang" where "Hangerő" does
    // not fit a 1x1) - English: the forms as they are
    static readonly Dictionary<string[], string[]> _shortForms = new();
    static int _shortLang = -1;
    static string[] ShortForms(string[] forms)
    {
        if (Lang.English || forms == null || forms.Length == 0) return forms;
        if (_shortLang != Lang.Version) { _shortForms.Clear(); _shortLang = Lang.Version; }
        if (_shortForms.TryGetValue(forms, out var all)) return all;
        string key = "short|" + forms[^1];
        all = Lang.Has(key) ? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Append(forms, Lang.T(key))) : forms;
        _shortForms[forms] = all;
        return all;
    }

    const float DotW = 17;                     // a status badge after a caption, with its gap

    // a level as a badge (a coloured card, where its colour as text would not read): a dark disc with a "!" in the status colour.
    // The colour always sits on dark, never on the fill - an amber dot on an amber card reads as an empty ring - so the badge is
    // filled on every card and amber and red tell apart; on a dark card a ring of the text colour round it
    void StatusDot(float cx, float cy, StatLevel l)
    {
        uint c = l == StatLevel.Bad ? Bento.Bad : Bento.Warn;
        _p.Circle(cx, cy, 7, Bento.BadgeRing);
        _p.Circle(cx, cy, 5.8f, Bento.Dark);
        _p.Fill(new RectangleF(cx - 1.25f, cy - 4.5f, 2.5f, 5.5f), c, 1.25f);
        _p.Circle(cx, cy + 3.3f, 1.4f, c);
    }

    // how wide the first form of a caption that fits maxW is (-1: none fits)
    float CaptionWidth(string[] forms, float maxW)
    {
        if (Theme.Cyber) return CyberCaptionWidth(forms, maxW);
        foreach (var f in ShortForms(forms))
        {
            if (string.IsNullOrEmpty(f)) continue;
            float w = _p.MeasureTracked(Upper(f), CapSize, FontWeight.SemiBold, CapTrack);
            if (w <= maxW) return w;
        }
        return -1;
    }

    // caption-style text (UPPERCASE already), its baseline at 'by' - also the opened views' small headings; Trailing: it ends at x
    void CapText(string s, float x, float by, float w, uint c, TextAlignment al = TextAlignment.Leading)
    {
        if (Theme.Cyber) { CyberCapText(s, x, by, w, c, al); return; }
        var (b, h) = _p.TextLine(CapSize, FontWeight.SemiBold);
        if (al == TextAlignment.Trailing)
        {
            w = _p.MeasureTracked(s, CapSize, FontWeight.SemiBold, CapTrack);
            x -= w;
        }
        _p.TrackedText(s, new RectangleF(x, by - b, w + 6, h), c, CapSize, FontWeight.SemiBold, CapTrack, TextAlignment.Leading, ParagraphAlignment.Near);
    }

    // the caption row: the caption on the left (its first form that fits beside the note), a note on the right in what is
    // left (grey 17 px, cut to fit - its shorter form first, when it has one - left out when there is no room)
    void CaptionRow(RectangleF r, string note, string[] forms, StatLevel badge = StatLevel.Ok, string noteShort = null) =>
        CaptionRow(r, r.X + PadX(r), r.Right - PadX(r), note, Bento.Cap, forms, badge, noteShort);

    void CaptionRow(RectangleF r, float x, float right, string note, uint noteColor, string[] forms, StatLevel badge = StatLevel.Ok,
        string noteShort = null)
    {
        if (Theme.Cyber) { CyberCaptionRow(r, x, right, note, noteColor, forms, noteShort: noteShort); return; }
        float iw = right - x;
        float nw = string.IsNullOrEmpty(note) ? 0 : _p.MeasureWidth(note, SmallText, FontWeight.SemiBold);
        float cw = Caption(x, r, nw > 0 ? MathF.Max(iw * 0.4f, iw - nw - 10) : iw, forms, badge);
        if (nw <= 0) return;
        float room = iw - (cw > 0 ? cw + 10 : 0);
        if (room < 36) return;
        TextAt(Ellipsize(note, room, SmallText, FontWeight.SemiBold, null, noteShort), right - room, r.Y + CapBase, room, noteColor, SmallText,
            FontWeight.SemiBold, TextAlignment.Trailing);
    }

    // A value and its unit on one baseline: the value bold (as big as 'size', smaller down to 'min' until both fit maxW), the
    // unit grey after it (left out when even the smallest value leaves it no room). roll: the value rolls when it changes.
    // Returns the width it took.
    float ValueUnit(float x, float by, float maxW, string value, string unit, float size, float min, uint vc, string roll = null)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        float uw = string.IsNullOrEmpty(unit) ? 0 : _p.MeasureWidth(unit, SmallText, FontWeight.SemiBold) + 4;
        float vs = FitSize(value, maxW - uw, size, FontWeight.Bold, Theme.Display);
        if (vs < min && uw > 0) { uw = 0; vs = FitSize(value, maxW, size, FontWeight.Bold, Theme.Display); }
        vs = MathF.Max(min, vs);
        float vw = ValueText(x, by, value, vs, vc, roll);
        if (uw > 0) TextAt(unit, x + vw + 4, by, uw + 2, Sub, SmallText, FontWeight.SemiBold);
        return vw + uw;
    }

    // a value, bold, its baseline at 'by' (roll: it rolls when it changes); returns its width
    float ValueText(float x, float by, string value, float vs, uint vc, string roll)
    {
        float vw = _p.MeasureWidth(value, vs, FontWeight.Bold, Theme.Display);
        var (vb, vh) = _p.TextLine(vs, FontWeight.Bold, Theme.Display);
        var vr = new RectangleF(x, by - vb, vw + 4, vh);
        if (roll != null) RollText(roll, value, vr, vc, vs, FontWeight.Bold, Theme.Display, TextAlignment.Leading);
        else _p.Text(value, vr, vc, vs, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Near, Theme.Display);
        return vw;
    }

    // A 1x1's value and unit: the value Hero big (smaller only when it alone cannot fit), the unit on its baseline 17 px, else
    // 15 px; us = 0: no room beside it (the caller puts it up in the caption row, or leaves it out)
    (float vs, float us) HeroFit(string value, string unit, float iw)
    {
        float vs = MathF.Max(20, FitSize(value, iw, Hero, FontWeight.Bold, Theme.Display));
        if (string.IsNullOrEmpty(unit)) return (vs, 0);
        float vw = _p.MeasureWidth(value, vs, FontWeight.Bold, Theme.Display) + 4;
        if (vw + _p.MeasureWidth(unit, SmallText, FontWeight.SemiBold) <= iw) return (vs, SmallText);
        if (vw + _p.MeasureWidth(unit, CapSize, FontWeight.SemiBold) <= iw) return (vs, CapSize);
        return (vs, 0);
    }

    // A plain line: the track and, from the left, the value in colour (it glides when the value changes)
    void Meter(RectangleF bar, float frac, uint tint, string key)
    {
        if (Theme.Cyber) { CyberMeter(bar, frac, tint, key); return; }      // segments
        float f = _ui.Spring(key, frac < 0 ? 0 : Math.Clamp(frac, 0, 1), 140, 20, 0.08f);
        float h = bar.Height;
        _p.Fill(bar, Bento.Track, h / 2);
        if (frac < 0 || f < 0.002f) return;
        _p.Fill(new RectangleF(bar.X, bar.Y, MathF.Max(h, bar.Width * MathF.Min(f, 1)), h), tint, h / 2);
    }

    // The 1x1 card (and a column of the perf strip): caption, the value with its unit, one line under it (meter = null: none).
    // Every 1x1's value is as big as the others (Hero): a unit that does not fit beside it moves up into the caption row, at the
    // right (or, no room there either, is left out - the caption says what it is).
    // unitUp: the unit always goes up there (RAM and VRAM side by side must look the same whatever the number's width)
    void Small(RectangleF r, string[] caption, string value, string unit, float frac, uint bar, uint vc, string meter, string roll = null,
        bool unitUp = false, StatLevel lv = StatLevel.Ok)
    {
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad, by = r.Y + SBase;
        var (vs, us) = HeroFit(value, unitUp ? null : unit, iw);
        float uw = us == 0 && !string.IsNullOrEmpty(unit) ? _p.MeasureTracked(unit, CapSize, FontWeight.SemiBold, CapTrack) : 0;
        float dw = lv != StatLevel.Ok && Bento.CapBadge ? DotW : 0;
        if (uw > 0 && CaptionWidth(caption, iw - uw - 6 - dw) >= 0)
        {
            Caption(r, iw - uw - 6, caption, lv);
            CapText(unit, r.Right - pad - uw, r.Y + CapBase, uw + 2, Bento.Cap);
        }
        else Caption(r, iw, caption, lv);
        if (!string.IsNullOrEmpty(value))
        {
            float vw = ValueText(x, by, value, vs, vc, roll);
            if (us > 0) TextAt(unit, x + vw + 4, by, iw - vw - 2, Sub, us, FontWeight.SemiBold);
        }
        if (meter != null) Meter(new RectangleF(x, r.Bottom - 16, iw, 4), frac, bar, meter);
    }

    // a one-row wide card: caption and a note, the value bigger with its unit, a thicker line under it
    void Wide(RectangleF r, string[] caption, string note, string value, string unit, float frac, uint bar, uint vc, string meter, string roll = null,
        StatLevel lv = StatLevel.Ok)
    {
        float pad = PadX(r), iw = r.Width - 2 * pad;
        CaptionRow(r, note, caption, lv);
        ValueUnit(r.X + pad, r.Y + MBase, iw, value, unit, _big ? 52 : 40, 24, vc, roll);
        if (meter != null) Meter(new RectangleF(r.X + pad, r.Bottom - 17, iw, 6), frac, bar, meter);
    }

    // a reading as a 1x1
    void StatS(RectangleF r, string id, PanelData d, string key)
    {
        var (frac, value, _, _, lv) = Reading(id, d);
        bool none = value == "–";
        string unit = none ? null : id switch { "fps" => FpsUnit(d.Vr.DisplayHz), "gpu" => "ms", "vram" or "ram" => "GB", _ => null };
        Small(r, SmallCaption(id), value, unit, frac, Bento.Level(lv, RoleColor(id)), none ? Bento.Faint : Bento.Level(lv, Bento.Text), key,
            unitUp: id is "ram" or "vram", lv: lv);
    }

    // "/90": a 1x1 frame rate's unit, made when the headset's rate changes
    string _fpsUnit;
    float _fpsUnitHz = -1;
    string FpsUnit(float hz)
    {
        if (hz != _fpsUnitHz) { _fpsUnitHz = hz; _fpsUnit = $"/{hz:0}"; }
        return _fpsUnit;
    }

    // A value with a plain line under it (filled to how much it is), the unit or a note small under the line - the opened views'
    // big readings. vc: the value's colour (Warn / Bad when it needs attention, like the cards); unitIcon: a glyph before the
    // unit (a charging bolt); roll: the key of a value that rolls when it changes (a timer), null for plain text
    void Gauge(RectangleF r, float frac, uint tint, string key, string value, string unit, uint vc, float valueSize = 0, string roll = null,
        string unitIcon = null)
    {
        float pad = MathF.Max(10, r.Width * 0.12f), iw = r.Width - 2 * pad;
        if (iw < 16) return;
        bool hasValue = !string.IsNullOrEmpty(value), hasUnit = !string.IsNullOrEmpty(unit);
        float lt = Math.Clamp(r.Height * 0.045f, 4, 10);
        float vs = valueSize > 0 ? valueSize : hasValue ? FitSize(value, iw, Math.Clamp(r.Height * 0.34f, 14, 96), FontWeight.SemiBold, Theme.Display) : 0;
        float iconW = unitIcon != null ? 16 : 0;
        float us = hasUnit ? MathF.Max(SmallText, FitSize(unit, r.Width - 12 - iconW, Math.Clamp(r.Height * 0.13f, SmallText, 22), FontWeight.SemiBold, null)) : 0;
        float vh = vs * 1.1f, gap = MathF.Max(5, vs * 0.14f), uh = us * 1.4f;
        float total = (hasValue ? vh + gap : 0) + lt + (hasUnit ? gap + uh : 0);
        float y = r.Y + (r.Height - total) / 2;
        if (hasValue)
        {
            var vr = new RectangleF(r.X, y, r.Width, vh);
            // plain text: a frame rate changes every half second, rolling it would never stop
            if (roll != null) RollText(roll, value, vr, vc, vs, FontWeight.SemiBold, Theme.Display);
            else _p.Text(value, vr, vc, vs, FontWeight.SemiBold, TextAlignment.Center, ParagraphAlignment.Center, Theme.Display);
            y += vh + gap;
        }
        Meter(new RectangleF(r.X + pad, y, iw, lt), frac, tint, key);
        if (!hasUnit) return;
        var ur = new RectangleF(r.X, y + lt + gap, r.Width, uh);
        if (unitIcon == null) { _p.Text(unit, ur, Sub, us, FontWeight.SemiBold, TextAlignment.Center); return; }
        float tw = MathF.Min(_p.MeasureWidth(unit, us, FontWeight.SemiBold), r.Width - 12 - iconW);
        float ux = r.X + (r.Width - iconW - tw) / 2;
        _p.Icon(unitIcon, new RectangleF(ux - 2, ur.Y, iconW + 2, uh), unitIcon == Bolt ? BoltInk(Bento.Text) : Bento.Text, 15);
        _p.Text(unit, new RectangleF(ux + iconW, ur.Y, tw + 2, uh), Sub, us, FontWeight.SemiBold);
    }

    // Text that rolls when it changes: the digits that changed slide up and out while the new ones slide in.
    // Same length: only the changed characters move.
    sealed class Roll { public string From = "", To = ""; public double Start = -10; }
    readonly Dictionary<string, Roll> _rolls = new();

    void RollText(string key, string s, RectangleF r, uint c, float size, FontWeight wt, string fam, TextAlignment al = TextAlignment.Center)
    {
        if (!_rolls.TryGetValue(key, out var ro)) _rolls[key] = ro = new Roll { From = s, To = s };
        else if (ro.To != s) { ro.From = ro.To; ro.To = s; ro.Start = Clock.Now; }
        float t = (float)((Clock.Now - ro.Start) / 0.45);
        if (t >= 1 || ro.From == ro.To || Theme.Classic || !Motion.Full)      // rolling digits: Motion full only
        {
            _p.Text(s, r, c, size, wt, al, ParagraphAlignment.Center, fam);
            return;
        }
        _rolling = true;
        float e = EaseOutBack(Math.Clamp(t, 0, 1));
        float a = Math.Clamp(e, 0, 1);
        float lift = size * 0.9f;
        _p.PushClip(RectangleF.Inflate(r, 4, 0));
        string from = ro.From;
        if (from.Length == s.Length)
        {
            float tw = _p.MeasureWidth(s, size, wt, fam);
            float x0 = al == TextAlignment.Center ? r.X + (r.Width - tw) / 2 : al == TextAlignment.Trailing ? r.Right - tw : r.X;
            for (int i = 0; i < s.Length; i++)
            {
                float x = x0 + (i == 0 ? 0 : _p.MeasureWidth(s[..i], size, wt, fam));
                float cw = _p.MeasureWidth(s.Substring(i, 1), size, wt, fam) + 6;
                if (s[i] == from[i])
                {
                    _p.Text(s.Substring(i, 1), new RectangleF(x, r.Y, cw, r.Height), c, size, wt, TextAlignment.Leading, ParagraphAlignment.Center, fam);
                    continue;
                }
                _p.Text(from.Substring(i, 1), new RectangleF(x, r.Y - lift * e, cw, r.Height), Theme.WithAlpha(c, 1 - a),
                    size, wt, TextAlignment.Leading, ParagraphAlignment.Center, fam);
                _p.Text(s.Substring(i, 1), new RectangleF(x, r.Y + lift * (1 - e), cw, r.Height), Theme.WithAlpha(c, a),
                    size, wt, TextAlignment.Leading, ParagraphAlignment.Center, fam);
            }
        }
        else
        {
            _p.Text(from, new RectangleF(r.X, r.Y - lift * e, r.Width, r.Height), Theme.WithAlpha(c, 1 - a), size, wt, al,
                ParagraphAlignment.Center, fam);
            _p.Text(s, new RectangleF(r.X, r.Y + lift * (1 - e), r.Width, r.Height), Theme.WithAlpha(c, a), size, wt, al,
                ParagraphAlignment.Center, fam);
        }
        _p.PopClip();
    }

    // at least this big to hit with a laser, whatever is drawn
    static RectangleF MinHit(RectangleF r, float min = 40) =>
        RectangleF.Inflate(r, MathF.Max(0, (min - r.Width) / 2), MathF.Max(0, (min - r.Height) / 2));

    // Orbit's buttons, springy when pressed: round ones inside a widget are discs (Tile; the main one in the accent), square ones
    // (the opened views' presets) tiles with a hairline. On, it is filled: with the accent (accentFill), else with 'fill' (a muted
    // speaker's red, a finished timer's amber).
    bool Press(string id, RectangleF r, string icon, string label, bool on, bool enabled = true, float iconSize = 24, bool circle = false,
        bool accentFill = true, uint fill = 0)
    {
        if (Theme.Cyber) return CyberPress(id, r, icon, label, on, enabled, iconSize, accentFill, fill);
        bool click = _ui.Clickable(id, MinHit(r), out bool hover, out bool held) && enabled;
        float s = _ui.Spring(Ui.Id(id, "#s"), held && enabled ? 0.9f : 1f, 420, 18);
        float a = _ui.Anim(Ui.Id(id, "#on"), on ? 1 : 0, 16);           // on / off: the fill eases over
        var b = new RectangleF(r.X + r.Width * (1 - s) / 2, r.Y + r.Height * (1 - s) / 2, r.Width * s, r.Height * s);
        float rad = circle ? MathF.Min(b.Width, b.Height) / 2 : MathF.Min(CardRadius * s, MathF.Min(b.Width, b.Height) / 2);
        uint rest = Bento.Tile, onFill = accentFill ? Bento.AccentFill : fill;
        uint bg = !enabled ? Theme.WithAlpha(rest, 0.5f) : Theme.Mix(hover ? Bento.TileHover : rest, onFill, a);
        _p.Fill(b, bg, rad);
        if (!circle && a < 1 && Bento.Edge != 0) _p.Stroke(RectangleF.Inflate(b, -0.6f, -0.6f), Theme.WithAlpha(Bento.Edge, 1 - a), Bento.EdgeWidth, rad);
        uint fg = !enabled ? Bento.Faint : Theme.Mix(Bento.Text, accentFill ? Bento.OnAccent : OnColor(onFill), a);
        // a label is mixed case: 17 px and up (the size it springs to while held included)
        float ls = MathF.Min(18, MathF.Max(SmallText, r.Height * 0.3f));
        if (!string.IsNullOrEmpty(label) && icon != null)
        {
            float tw = _p.MeasureWidth(label, ls, FontWeight.SemiBold);
            float total = iconSize + 8 + tw;
            if (total > b.Width - 16)
            {
                label = Ellipsize(label, b.Width - 32 - iconSize, ls, FontWeight.SemiBold);
                tw = _p.MeasureWidth(label, ls, FontWeight.SemiBold);
                total = iconSize + 8 + tw;
            }
            float x = b.X + (b.Width - total) / 2;
            _p.Icon(icon, new RectangleF(x, b.Y, iconSize, b.Height), fg, iconSize);
            _p.Text(label, new RectangleF(x + iconSize + 8, b.Y, tw + 4, b.Height), fg, ls, FontWeight.SemiBold);
        }
        else if (icon != null) _p.Icon(icon, b, fg, MathF.Round(iconSize * s));
        else if (label != null)
            _p.Text(Ellipsize(label, b.Width - 12, ls, FontWeight.SemiBold), b, fg, ls, FontWeight.SemiBold, TextAlignment.Center);
        return click;
    }

    // A button that is its card: Tile when off, the accent when on (its label bold then); a 1x1 has its icon at the top-left and
    // its name at the bottom, a wide one both side by side. Springy when pressed. In the widget's look (Bento.Cur): solid and
    // two-tone are a dark version of their fill when off, the fill with a check when on - and a button with no on / off (toggle
    // false: recenter, settings) is the fill, as the look promises.
    bool TileButton(string id, RectangleF r, string icon, string label, bool on, bool wide, bool toggle)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held);
        float s = _ui.Spring(Ui.Id(id, "#s"), held ? 0.92f : 1f, 420, 18);
        float a = _ui.Anim(Ui.Id(id, "#on"), on ? 1 : 0, 16);           // on / off: the on fill fades over the off look
        bool scaled = MathF.Abs(s - 1) > 0.001f;
        if (scaled) _p.PushScale(s, r.X + r.Width / 2, r.Y + r.Height / 2);
        ref readonly var L = ref Bento.Cur;
        bool full = !on && !toggle && L.Check;
        if (a >= 1) _p.FillGradient(r, L.On0, L.On1, CardRadius, L.Angle, L.Span);
        else if (full)
        {
            float hv = hover ? 0.12f : 0;          // under the laser: a touch toward its text colour
            _p.FillGradient(r, Theme.Mix(L.On0, L.OnText, hv), Theme.Mix(L.On1, L.OnText, hv), CardRadius, L.Angle, L.Span);
        }
        else if (hover) _p.Fill(r, L.OffHover, CardRadius);
        else
        {
            _p.FillGradient(r, L.Off0, L.Off1, CardRadius, L.Angle, L.Span);
            if (L.Sheen != 0) _p.FillGradient(r, L.Sheen, L.Sheen & 0x00FFFFFF, CardRadius, 90, 0.5f);
            if (L.Band != 0)
            {
                // a header: a strip of its colour along the top
                _p.PushClip(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, 7));
                _p.Fill(r, L.Band, CardRadius);
                _p.PopClip();
            }
        }
        if (a > 0 && a < 1)
        {
            // half-way: the on fill over the off look (a layer only while it changes - the gradient's brush stays the cached one)
            _p.PushFade(a);
            _p.FillGradient(r, L.On0, L.On1, CardRadius, L.Angle, L.Span);
            _p.PopFade();
        }
        if (a < 1 && !full && L.OffBorder != 0)
        {
            float bi = L.OffBorderW * 0.48f;
            _p.Stroke(RectangleF.Inflate(r, -bi, -bi), Theme.WithAlpha(L.OffBorder, 1 - a), L.OffBorderW, CardRadius - bi);
        }
        uint fg = full ? L.OnText : Theme.Mix(L.OffText, L.OnText, a);
        var lw = a > 0.5f ? FontWeight.Bold : FontWeight.SemiBold;
        float pad = PadX(r);
        bool check = a > 0 && L.Check;
        if (check)
        {
            // off is coloured too: on also says so with a check, top right (a wide one: right, centred)
            const float cr = 9;
            float ccx = r.Right - pad - cr + 2, ccy = wide ? r.Y + r.Height / 2 : r.Y + 24;
            _p.Circle(ccx, ccy, cr, Theme.WithAlpha(fg, a));
            _p.Icon(Theme.IcoCheck, new RectangleF(ccx - cr, ccy - cr, 2 * cr, 2 * cr), Theme.WithAlpha(Theme.Mix(L.On0, L.On1, 0.5f), a), 11);
        }
        if (wide)
        {
            const float ic = 22;
            if (icon != null) _p.Icon(icon, new RectangleF(r.X + pad - 2, r.Y, ic + 4, r.Height), fg, ic);
            float lx = r.X + pad + (icon != null ? ic + 10 : 0), room = r.Right - pad - lx - (check ? 26 : 0);
            float ls = MathF.Max(15, FitSize(label, room, SmallText, lw, null));
            _p.Text(Ellipsize(label, room, ls, lw), new RectangleF(lx, r.Y, room + 2, r.Height), fg, ls, lw);
        }
        else
        {
            const float ic = 24;
            if (icon != null) _p.Icon(icon, new RectangleF(r.X + pad - 2, r.Y + 10, ic + 4, ic + 4), fg, ic);
            float iw = r.Width - 2 * pad;
            float ls = MathF.Max(15, FitSize(label, iw, SmallText, lw, null));
            TextAt(Ellipsize(label, iw, ls, lw), r.X + pad, r.Bottom - 15, iw + 2, fg, ls, lw);
        }
        if (scaled) _p.PopScale();
        return click;
    }

    // a tile's name: the kind's, shorter on a 1x1
    // (a language's own short name for a 1x1 where it has one: "tile|Keyboard")
    // (a language's shorter one where the whole does not fit: "short|Keyboard" - Ellipsize / Fit try it)
    static string TileName(WatchWidgets.Kind k, bool wide) => wide ? k.Name : k.Id switch { "night" => "Night", "status" => "Status", _ => k.Name };

    void ButtonCell(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d)
    {
        string icon = k.Id == "desktops" && !d.DesktopsVisible ? Theme.IcoHide : k.Icon;
        string label = k.Id == "layout" ? d.LayoutName : TileName(k, w.W >= 2);
        if (TileButton(Ui.Id("wcb", id), r, icon, label, IsOn(k.Id, d), w.W >= 2, IsToggle(k.Id))) Act(k.Id);
    }

    // ---- the widgets ----

    // The time: the date on top (the one header in its own words: grey, 17 px and up; a caption would only say "time") with the
    // time in VR at its right while SteamVR is on, the time big, bold and left. Two rows and more: the seconds as a line along
    // the bottom with ":42" at its end, the digits 10 px from the date line (its descenders included) and from the seconds
    // line - they take the size of the space left between them. One row: TimeM; a 1x1: TimeS.
    void TimeOrbit(RectangleF r, WatchWidget w, DateTime now, PanelData d, int id)
    {
        if (TimeVariant(r, w, now, d, id)) return;          // a design of its own (WristClocks)
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        string t = Clk(now, h24 ? "HH:mm" : "h:mm", culture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        float pad = PadX(r), iw = r.Width - 2 * pad;
        Words(now);
        if (w.W == 1 && w.H == 1) { TimeS(r, t, ampm, now, id); return; }
        bool tall = w.H >= 2;
        float ds = tall ? MathF.Round(Math.Clamp(r.Height * 0.085f, SmallText, 24)) : SmallText;
        var (db, _) = _p.TextLine(ds, FontWeight.SemiBold);
        float dBase = r.Y + 9 + db;
        DateHeader(r.X + pad, dBase, iw, ds, VrNote(d));
        const float Gap = 10;
        float top = dBase + ds * 0.24f + Gap;           // under the date's descenders (a "y", a "g")
        if (!tall)
        {
            TimeM(r, t, ampm, h24, now, top, r.Bottom - 14, id);
            return;
        }
        float lt = r.Height >= 300 ? 6 : 4;
        string sec = Clk(now, "ss", culture, ":");
        float sw = _p.MeasureWidth(":00", SmallText, FontWeight.SemiBold) + 2;
        var line = new RectangleF(r.X + pad, r.Bottom - 18 - lt / 2, iw - sw - 10, lt);
        _p.Fill(line, Bento.Track, lt / 2);
        float f = now.Second / 60f;
        if (f > 0) _p.Fill(new RectangleF(line.X, line.Y, MathF.Max(lt, line.Width * f), lt), Bento.Accent, lt / 2);
        float secBase = line.Y + lt / 2 + SmallText * DigitH / 2;
        TextAt(sec, r.Right - pad - sw - 2, secBase, sw + 2, Sub, SmallText, FontWeight.SemiBold, TextAlignment.Trailing);
        float bottom = MathF.Min(line.Y, secBase - SmallText * DigitTop) - Gap;      // above the line and the ":42" at its end
        // AM / PM small beside it
        float ss = 0, sw2 = 0;
        float w100 = _p.MeasureWidth(t, 100, FontWeight.Bold, Theme.Display);
        float size = MathF.Min(100 * iw / w100, (bottom - top) / DigitSpan);
        if (ampm != null)
        {
            ss = MathF.Max(SmallText, MathF.Round(size * 0.22f));
            sw2 = _p.MeasureWidth(ampm, ss, FontWeight.SemiBold) + 6;
            size = MathF.Min(100 * (iw - sw2) / w100, (bottom - top) / DigitSpan);
        }
        size = MathF.Max(17, MathF.Floor(size * 2) / 2);
        float by = top + size * DigitTop + (bottom - top - size * DigitSpan) / 2;      // the digits centred between the two
        var (tb, th) = _p.TextLine(size, FontWeight.Bold, Theme.Display);
        float tw = _p.MeasureWidth(t, size, FontWeight.Bold, Theme.Display);
        RollText(Ui.Id("wtt", id), t, new RectangleF(r.X + pad, by - tb, tw + 2, th), Bento.Text, size, FontWeight.Bold, Theme.Display,
            TextAlignment.Leading);
        if (ampm != null) TextAt(ampm, r.X + pad + tw + 6, by, sw2, Sub, ss, FontWeight.SemiBold);
    }

    // One row: the time 44 px (smaller only where even the widest time would not fit, so it keeps its size all day), the seconds
    // small on its baseline; on a 12-hour clock AM / PM over them, at the digits' top. Centred between the date line and the
    // card's bottom.
    void TimeM(RectangleF r, string t, string ampm, bool h24, DateTime now, float top, float bottom, int id)
    {
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        string sec = Clk(now, "ss", CultureInfo.CurrentCulture);
        float aw = ampm == null ? 0 : _p.MeasureTracked(ampm, CapSize, FontWeight.SemiBold, CapTrack);
        float sw = MathF.Max(_p.MeasureWidth("00", SmallText, FontWeight.SemiBold), aw) + 6;
        float size = MathF.Max(24, FitSize(h24 ? "00:00" : "12:00", iw - sw, _big ? 64 : 44, FontWeight.Bold, Theme.Display));
        float by = top + size * DigitTop + (bottom - top - size * DigitSpan) / 2;
        float tw = ValueText(x, by, t, size, Bento.Text, Ui.Id("wtt", id));
        TextAt(sec, x + tw + 6, by, sw, Sub, SmallText, FontWeight.SemiBold);
        if (ampm != null) CapText(ampm, x + tw + 6, by - size * DigitH + CapSize * DigitH, aw, Sub);      // on the card, not a band
    }

    // A 1x1: the weekday and the day as its caption (AM / PM at its right on a 12-hour clock), the time as big as the other 1x1
    // values (the widest time decides), under it the minute so far as a line - it fills up once a minute, a step a second (a
    // spring would redraw the watch all the time)
    void TimeS(RectangleF r, string t, string ampm, DateTime now, int id)
    {
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        float aw = ampm == null ? 0 : _p.MeasureTracked(ampm, CapSize, FontWeight.SemiBold, CapTrack);
        Caption(r, aw > 0 ? iw - aw - 8 : iw, _capDayDate);
        if (aw > 0) CapText(ampm, r.Right - pad - aw, r.Y + CapBase, aw + 2, Bento.Cap);
        float vs = MathF.Max(24, FitSize(ampm == null ? "00:00" : "12:00", iw, Hero, FontWeight.Bold, Theme.Display));
        ValueText(x, r.Y + SBase, t, vs, Bento.Text, Ui.Id("wtt", id));
        var bar = new RectangleF(x, r.Bottom - 16, iw, 4);
        _p.Fill(bar, Bento.Track, 2);
        if (now.Second > 0) _p.Fill(new RectangleF(bar.X, bar.Y, MathF.Max(4, bar.Width * now.Second / 60f), 4), Bento.Accent, 2);
    }

    // The clock's header: the date in the first of its forms that fits beside the time in VR (at the right, the same grey), else
    // the date alone (cut only when even its shortest form cannot fit)
    void DateHeader(float x, float by, float iw, float size, string meta)
    {
        float mw = meta == null ? 0 : _p.MeasureWidth(meta, size, FontWeight.SemiBold);
        string date = null;
        if (mw > 0)
        {
            foreach (var f in _dateForms)
                if (_p.MeasureWidth(f, size, FontWeight.SemiBold) <= iw - mw - 12) { date = f; break; }
            if (date == null) mw = 0;
        }
        if (date == null)
        {
            foreach (var f in _dateForms)
                if (_p.MeasureWidth(f, size, FontWeight.SemiBold) <= iw) { date = f; break; }
            date ??= Ellipsize(_dateForms[^1], iw, size, FontWeight.SemiBold);
        }
        TextAt(date, x, by, iw + 2, Bento.Cap, size, FontWeight.SemiBold);
        if (mw > 0) TextAt(meta, x + iw - mw - 2, by, mw + 2, Bento.Cap, size, FontWeight.SemiBold, TextAlignment.Trailing);
    }

    // "VR 1:23": the time in VR as the clock's header writes it (null while SteamVR is off), made once a minute
    int _vrNoteMin = -1;
    string _vrNote;
    string VrNote(PanelData d)
    {
        if (d.VrSince is not { } since) return null;
        int m = Math.Max(0, (int)(Now - since).TotalMinutes);
        if (m != _vrNoteMin) { _vrNoteMin = m; _vrNote = $"VR {m / 60}:{m % 60:00}"; }
        return _vrNote;
    }

    // The date. A 1x1 (the calendar's too): DateS. Wider: TODAY with the week (and the year on the widest), the weekday in the
    // accent (accent text only this big and bold), the date big under it.
    void DateOrbit(RectangleF r, WatchWidget w, DateTime now)
    {
        Words(now);
        if (w.W == 1) { DateS(r); return; }
        float pad = PadX(r), iw = r.Width - 2 * pad;
        CaptionRow(r, w.W >= 6 ? _weekYear : _weekNote, CapToday);
        TextAt(Ellipsize(_dayName, iw, SmallText, FontWeight.Bold), r.X + pad, r.Y + 49, iw + 2, Bento.Cur.AccentText, SmallText, FontWeight.Bold);
        float bs = MathF.Max(20, FitSize(_monthDay, iw, 24, FontWeight.Bold, Theme.Display));
        TextAt(Ellipsize(_monthDay, iw, bs, FontWeight.Bold, Theme.Display), r.X + pad, r.Y + 81, iw + 2, Bento.Text, bs, FontWeight.Bold,
            TextAlignment.Leading, Theme.Display);
    }

    // The date as a 1x1: the weekday (the first of its forms that fits) as the caption in the accent - on a header's band or
    // under a glass sheen in the caption row's own white (the band is the accent; the sheen washes a colour out) - the day 38 px,
    // the month 15 px under it (14 when long, else its short form)
    void DateS(RectangleF r)
    {
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        Caption(r, iw, _capWeekday, color: Bento.Cur.Band != 0 || Bento.Cur.Sheen != 0 ? Bento.CapText : Bento.Cur.AccentText);
        ValueText(x, r.Y + (_big ? 88 : 66), _dayNum, _big ? 48 : 38, Bento.Text, null);
        Note(x, r.Y + NoteBase, iw, _monthNames);
    }

    // A 1x1's note under its value ("September", "since 17:16"): 15 px, down to 14 for a long one, else its next form. Under
    // 15 px its grey is a touch lighter (toward the text colour), so the smaller letters still read on every card. A font picked
    // in Look > Font takes the next form rather than going under 15 px (a narrower one would fit a long form there).
    void Note(float x, float by, float iw, string[] forms)
    {
        string s = forms[0];
        float size = FitSize(s, iw, 15, FontWeight.SemiBold, null), least = Theme.OwnFont ? 15 : 14;
        for (int i = 1; i < forms.Length && size < least; i++) { s = forms[i]; size = FitSize(s, iw, 15, FontWeight.SemiBold, null); }
        size = MathF.Max(14, size);
        // the grey still reads (4.5:1) on every card colour: a step toward the text colour where it would not (a two-tone purple: 4.2)
        uint c = size < 15 ? Theme.Mix(Sub, Bento.Text, 0.3f) : Sub, b0 = Bento.Cur.Fill0 | 0xFF000000, b1 = Bento.Cur.Fill1 | 0xFF000000;
        // the card's gradient: either end may be under it, and so may the way between (two hues mixed can be darker than both:
        // a solid purple's middle gave 4.4)
        float Worst(uint ink)
        {
            float w = float.MaxValue;
            for (int i = 0; i <= 4; i++) w = MathF.Min(w, Bento.Contrast(ink, Theme.Mix(b0, b1, i / 4f) | 0xFF000000));
            return w;
        }
        // aimed a little over 4.5: a card over the watch's dark back is drawn at 0.96, a shade darker than its colours
        const float Want = 4.7f;
        for (float t = 0.3f; t <= 1.01f && Worst(c) < Want; t += 0.175f) c = Theme.Mix(Sub, Bento.Text, t);
        if (Worst(c) < Want) c = Worst(0xFF000000) >= Worst(0xFFFFFFFF) ? 0xFF000000 : 0xFFFFFFFF;
        TextAt(Ellipsize(s, iw, size, FontWeight.SemiBold), x, by, iw + 2, c, size, FontWeight.SemiBold);
    }

    void CalendarOrbit(RectangleF r, WatchWidget w, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        float pad = PadX(r), iw = r.Width - 2 * pad;
        Words(now);
        if (w.H >= 2)
        {
            // the month at a glance: its name as the caption with the year beside it, the days under them, today a filled circle
            // in the accent, the weekend grey
            CaptionRow(r, _year, _capMonth);
            var g = new RectangleF(r.X + 10, r.Y + 36, r.Width - 20, r.Height - 46);
            int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
            var day1 = new DateTime(now.Year, now.Month, 1);
            int offset = ((int)day1.DayOfWeek - first + 7) % 7, days = DateTime.DaysInMonth(now.Year, now.Month);
            int weeks = (offset + days + 6) / 7;
            float cw = g.Width / 7, rh = g.Height / (weeks + 1);
            for (int c = 0; c < 7; c++)
            {
                _p.Text(DayHead(culture, (DayOfWeek)((first + c) % 7)), new RectangleF(g.X + c * cw, g.Y, cw, rh), Sub, CapSize,
                    FontWeight.SemiBold, TextAlignment.Center);
            }
            for (int dd = 1; dd <= days; dd++)
            {
                int cell = offset + dd - 1, col = cell % 7, row = cell / 7 + 1;
                var cr = new RectangleF(g.X + col * cw, g.Y + row * rh, cw, rh);
                var dow = (DayOfWeek)((first + col) % 7);
                bool today = dd == now.Day, weekend = dow is DayOfWeek.Saturday or DayOfWeek.Sunday;
                if (today)
                {
                    float d2 = MathF.Min(cw, rh) - 1;
                    TodayMark(cr.X + cw / 2, cr.Y + rh / 2, d2 / 2);
                }
                _p.Text(DayNumber(dd), cr, today ? Bento.OnAccent : weekend ? Sub : Bento.Text, CapSize, today ? FontWeight.Bold : FontWeight.SemiBold,
                    TextAlignment.Center);
            }
            return;
        }
        if (w.W >= 2) WeekStrip(r, now);
        else DateS(r);
    }

    // a 2x1: this week as a strip - the month as its caption with the year at the right, the weekday letters, the days (today a
    // filled circle in the accent, the weekend grey, the last or next month's days faint)
    void WeekStrip(RectangleF r, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        float pad = PadX(r), cw = (r.Width - 2 * pad) / 7;
        CaptionRow(r, _year, _capMonth);
        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var day0 = now.Date.AddDays(-(((int)now.DayOfWeek - first + 7) % 7));
        for (int c = 0; c < 7; c++)
        {
            var day = day0.AddDays(c);
            float x = r.X + pad + c * cw;
            bool today = day == now.Date, weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            _p.Text(DayHead(culture, day.DayOfWeek), new RectangleF(x, r.Y + 36, cw, 20), Sub, CapSize, FontWeight.SemiBold, TextAlignment.Center);
            if (today) TodayMark(x + cw / 2, r.Y + 72, 12);
            uint c2 = today ? Bento.OnAccent : day.Month != now.Month ? Bento.Faint : weekend ? Sub : Bento.Text;
            _p.Text(DayNumber(day.Day), new RectangleF(x, r.Y + 60, cw, 24), c2, SmallText, today ? FontWeight.Bold : FontWeight.SemiBold,
                TextAlignment.Center);
        }
    }

    // today in a calendar: a filled circle in the accent (Cyber: a square with a cut corner)
    void TodayMark(float cx, float cy, float rad)
    {
        if (!Theme.Cyber) { _p.Circle(cx, cy, rad, Bento.AccentFill); return; }
        float s = MathF.Round(rad * 1.8f);
        _p.FillCut(new RectangleF(MathF.Round(cx - s / 2), MathF.Round(cy - s / 2), s, s), Bento.AccentFill, 0, 0, MathF.Round(s * 0.3f), 0);
    }

    // "1" .. "31", made once; the month grid's weekday heads ("H", "SZE"), UPPERCASE, two letters at most
    static readonly string[] DayNumbers = MakeDays();
    static string[] MakeDays()
    {
        var a = new string[32];
        for (int i = 0; i < a.Length; i++) a[i] = i.ToString(CultureInfo.InvariantCulture);
        return a;
    }
    static string DayNumber(int d) => d >= 0 && d < DayNumbers.Length ? DayNumbers[d] : d.ToString(CultureInfo.InvariantCulture);

    static readonly string[] _heads = new string[7];
    static CultureInfo _headsCulture;

    static string DayHead(CultureInfo culture, DayOfWeek dow)
    {
        if (!ReferenceEquals(culture, _headsCulture))
        {
            _headsCulture = culture;
            for (int i = 0; i < 7; i++)
            {
                string n = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)i);
                _heads[i] = n.Substring(0, Math.Min(2, n.Length)).ToUpper(culture);
            }
        }
        return _heads[(int)dow];
    }

    // Orbit's dial: dots for the hours (bigger on the quarters), the hands in the text colour, and the seconds
    // as the only coloured thing: a satellite circling the rim
    void AnalogOrbit(RectangleF r, DateTime now, uint tint)
    {
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rad = MathF.Min(r.Width, r.Height) / 2 - 12;
        float rim = rad * 0.94f;
        _p.Arc(cx, cy, rim, 0, 1, Bento.Track, MathF.Max(2, rad * 0.02f));
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathF.Tau;
            bool quarter = i % 3 == 0;
            float dr = rad * 0.76f;
            _p.Circle(cx + dr * MathF.Sin(a), cy - dr * MathF.Cos(a), quarter ? MathF.Max(3, rad * 0.05f) : MathF.Max(2, rad * 0.026f),
                quarter ? Bento.Text : Sub);
        }
        float sec = now.Second, m = now.Minute + sec / 60f, h = now.Hour % 12 + m / 60f;
        void Hand(float turns, float len, float width)
        {
            float a = turns * MathF.Tau;
            _p.RoundLine(cx, cy, cx + len * MathF.Sin(a), cy - len * MathF.Cos(a), Bento.Text, width);
        }
        Hand(h / 12, rad * 0.46f, MathF.Max(5, rad * 0.075f));
        Hand(m / 60, rad * 0.66f, MathF.Max(3.5f, rad * 0.048f));
        float sa = sec / 60 * MathF.Tau, sx = cx + rim * MathF.Sin(sa), sy = cy - rim * MathF.Cos(sa);
        HaloDisc(sx, sy, MathF.Max(5, rad * 0.075f));
        _p.Circle(sx, sy, MathF.Max(3.5f, rad * 0.05f), tint);
        _p.Circle(cx, cy, MathF.Max(4, rad * 0.06f), Bento.Text);
        _p.Circle(cx, cy, MathF.Max(2, rad * 0.03f), tint);
    }

    // ---- the 1x1 analog clock: the macOS widget clock Erik showed - a squircle face, ticks reaching into its corners ----
    // Measured on his picture, in s = half the face's width: the face a superellipse (n 4); the ticks end on a squarer one (n 4.7)
    // 0.1 s in from the edge - so they grow longer toward the corners - the hour ones from a circle (0.71 s), the minute ones 0.14 s
    // long; 12 / 3 / 6 / 9 about 0.2 s tall, 0.55 s out; the hands a thin neck, then a wide rounded bar.
    const float FaceN = 4, TickN = 4.7f, TickInset = 0.10f;
    const uint AppleTop = 0xFF262626, AppleBottom = 0xFF101010;    // the face: near black, a little lighter at the top
    const uint AppleInk = 0xFFF8F8F8, AppleTick = 0xFF7A7A7A;      // the hour ticks, numerals and hands; the minute ticks
    const uint AppleOrange = 0xFFFF8C32;                            // the second hand (the picture's orange: redder than #FF9500)
    static readonly string[] Numerals = { "12", "3", "6", "9" };

    // The face is the card (CardBack draws it), the same rounded square as every card (Erik 10-17: the squircle stood out): in the
    // default look the picture's - its near black, a light rim strongest along the top - and with a look of its own that look's card.
    // The ticks are printed on it, drawn with the card, ending on its shape drawn in a little: they sit closer to its edge than
    // anything else a card holds (the layout checks see them as the card).
    void AnalogFace(RectangleF r, WatchWidgets.Kind k, WatchWidget w, in Bento.Look L, uint f0, uint f1, bool cards)
    {
        bool apple = Bento.StyleIndex(w.Style) == 0;
        if (apple)
        {
            float fa = cards ? 0.96f : 1;
            _p.FillGradient(r, Theme.WithAlpha(AppleTop, fa), Theme.WithAlpha(AppleBottom, fa), CardRadius, 90, 1);
            var rim = RectangleF.Inflate(r, -0.6f, -0.6f);
            _p.Stroke(rim, 0x14FFFFFF, 1.2f, CardRadius - 0.6f);
            _p.PushClip(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height * 0.4f));       // stronger along the top
            _p.Stroke(rim, 0x40FFFFFF, 1.2f, CardRadius - 0.6f);
            _p.PopClip();
        }
        else
        {
            _p.FillGradient(r, f0, f1, CardRadius, L.Angle, L.Span);
            if (L.Sheen != 0) _p.FillGradient(r, L.Sheen, L.Sheen & 0x00FFFFFF, CardRadius, 90, 0.5f);
            if (L.Band != 0)
            {
                _p.PushClip(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, BandHeight(k, w, r) + 1));
                _p.Fill(r, L.Band, CardRadius);
                _p.PopClip();
            }
            if (L.Border != 0 && L.BorderW > 0)
            {
                float i = L.BorderW * 0.48f;
                _p.Stroke(RectangleF.Inflate(r, -i, -i), L.Border, L.BorderW, CardRadius - i);
            }
        }
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = MathF.Min(r.Width, r.Height) / 2;
        float ia = r.Width / 2 - TickInset * s, ib = r.Height / 2 - TickInset * s, ic = MathF.Max(0, CardRadius - TickInset * s);
        float hw = MathF.Max(1.9f, s * 0.04f), mw = MathF.Max(1, s * 0.02f);
        uint hc = apple ? AppleInk : L.Text, mc = apple ? AppleTick : L.Sub;
        for (int i = 0; i < 60; i++)
        {
            float t = i / 60f * MathF.Tau, dx = MathF.Sin(t), dy = -MathF.Cos(t);
            // where this ray meets the card's shape drawn in by the inset
            float ro = RayToRoundRect(dx, dy, ia, ib, ic);
            bool hour = i % 5 == 0;
            float ri = hour ? s * 0.71f : ro - s * 0.14f;
            _p.Line(cx + dx * ri, cy + dy * ri, cx + dx * ro, cy + dy * ro, hour ? hc : mc, hour ? hw : mw);
        }
    }

    // On the face: 12, 3, 6 and 9; the hour and minute hands - a thin neck from the pivot, then a wide rounded bar, a gap of the face's
    // colour round them so they read over the ticks and numerals - and the seconds: a thin orange hand from a short tail past the
    // pivot, its orange ring with a dark pin on the white pivot. It ticks once a second (WristPanel.Update draws just after each).
    // A colour of its own replaces the orange; on a look's card the ink is the look's text (the orange too where it would not read).
    // from the middle along (dx, dy): how far to a rounded rectangle of half sizes a x b and corner radius rc
    static float RayToRoundRect(float dx, float dy, float a, float b, float rc)
    {
        float ux = MathF.Abs(dx), uy = MathF.Abs(dy);
        float d = MathF.Min(ux > 1e-6f ? a / ux : float.MaxValue, uy > 1e-6f ? b / uy : float.MaxValue);
        float ca = a - rc, cb = b - rc;
        if (rc > 0 && ux * d > ca && uy * d > cb)
        {
            float uc = ux * ca + uy * cb;                                 // the corner's arc: |d u - c| = rc, the far root
            d = uc + MathF.Sqrt(MathF.Max(0, uc * uc - (ca * ca + cb * cb) + rc * rc));
        }
        return d;
    }

    void AnalogS(RectangleF r, WatchWidget w, DateTime now)
    {
        bool apple = Bento.StyleIndex(w.Style) == 0;
        ref readonly var L = ref Bento.Cur;
        uint face = apple ? Theme.Mix(AppleTop, AppleBottom, 0.5f) : Theme.Mix(L.Fill0, L.Fill1, 0.5f);
        uint ink = apple ? AppleInk : Bento.Text;
        uint sec = w.Color.HasValue ? Bento.Accent
            : apple || Bento.Contrast(AppleOrange, face | 0xFF000000) >= 3 ? AppleOrange : Bento.Accent;
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = MathF.Min(r.Width, r.Height) / 2;
        float ns = MathF.Max(14, MathF.Round(s * 0.283f * 2) / 2);
        float ox = r.Width / 2 * 0.575f, oy = r.Height / 2 * 0.553f, nb = ns * DigitH / 2;       // the digits' middle on each spot
        TextAt(Numerals[0], cx - 20, cy - oy + nb, 40, ink, ns, FontWeight.SemiBold, TextAlignment.Center, Theme.Display);
        TextAt(Numerals[1], cx + ox - 20, cy + nb, 40, ink, ns, FontWeight.SemiBold, TextAlignment.Center, Theme.Display);
        TextAt(Numerals[2], cx - 20, cy + oy + nb, 40, ink, ns, FontWeight.SemiBold, TextAlignment.Center, Theme.Display);
        TextAt(Numerals[3], cx - ox - 20, cy + nb, 40, ink, ns, FontWeight.SemiBold, TextAlignment.Center, Theme.Display);
        float m = now.Minute + now.Second / 60f, h = now.Hour % 12 + m / 60f;
        float neck = MathF.Max(1.5f, s * 0.034f), bar = MathF.Max(3.2f, s * 0.066f), gap = 1.4f;
        void Hand(float turns, float from, float to)
        {
            float a = turns * MathF.Tau, dx = MathF.Sin(a), dy = -MathF.Cos(a);
            float b0 = s * from + bar / 2, b1 = s * to - bar / 2;
            _p.RoundLine(cx, cy, cx + dx * b0, cy + dy * b0, face, neck + gap);
            _p.RoundLine(cx + dx * b0, cy + dy * b0, cx + dx * b1, cy + dy * b1, face, bar + gap);
            _p.RoundLine(cx, cy, cx + dx * b0, cy + dy * b0, ink, neck);
            _p.RoundLine(cx + dx * b0, cy + dy * b0, cx + dx * b1, cy + dy * b1, ink, bar);
        }
        Hand(h / 12, 0.12f, 0.475f);
        Hand(m / 60, 0.135f, 0.805f);
        float sa = now.Second / 60f * MathF.Tau, sx = MathF.Sin(sa), sy = -MathF.Cos(sa);
        _p.RoundLine(cx - sx * s * 0.095f, cy - sy * s * 0.095f, cx + sx * s * 0.80f, cy + sy * s * 0.80f, sec, MathF.Max(1.2f, s * 0.022f));
        _p.Circle(cx, cy, MathF.Max(3.2f, s * 0.067f), ink);
        _p.Circle(cx, cy, MathF.Max(2, s * 0.042f), sec);
        _p.Circle(cx, cy, MathF.Max(0.85f, s * 0.018f), face);
    }

    void TimerOrbit(RectangleF r, WatchWidget w, int id)
    {
        double now = Clock.Now, left = TimerLeft(now);
        float frac = _timer == TimerState.Idle ? 1 : (float)(left / Math.Max(1, Math.Max(_timerTotal, left)));
        bool done = _timer == TimerState.Done;
        var tl = done ? StatLevel.Warn : StatLevel.Ok;
        uint bar = Bento.Level(tl, Bento.Accent);
        uint vc = done ? Bento.Level(tl, Bento.Text) : _timer == TimerState.Paused ? Sub : Bento.Text;
        string txt = done ? "Done" : Clock2(left);
        string state = _timer switch { TimerState.Running => "running", TimerState.Paused => "paused", TimerState.Done => "time is up", _ => "tap to start" };
        float pad = PadX(r), iw = r.Width - 2 * pad;
        if (w.H >= 2)
        {
            // the time big over its line; - (reset once it runs) / start-pause / + at the bottom
            CaptionRow(r, state, CapTimer, tl);
            const float bs = 44, gap = 14;
            float by = r.Bottom - 14 - bs, x0 = r.X + (r.Width - 3 * bs - 2 * gap) / 2;
            ValueUnit(r.X + pad, r.Y + 94, iw, txt, null, 52, 28, vc, Ui.Id("wtx", id));
            Meter(new RectangleF(r.X + pad, r.Y + 112, iw, 6), frac, bar, Ui.Id("wtr", id));
            bool idle = _timer == TimerState.Idle, tap = false;
            if (Press(Ui.Id("wtm", id), new RectangleF(x0, by, bs, bs), idle ? "\uE738" : "\uE72C", null, false, true, 18, true))
            {
                if (idle) TimerAdjust(-1); else TimerReset();
            }
            if (Press(Ui.Id("wtp", id), new RectangleF(x0 + bs + gap, by, bs, bs), _timer == TimerState.Running ? PauseFill : PlayFill, null, true,
                    true, 18, true)) tap = true;
            if (Press(Ui.Id("wtq", id), new RectangleF(x0 + 2 * (bs + gap), by, bs, bs), "\uE710", null, false, true, 18, true)) TimerAdjust(1);
            if (tap) TimerTap();
            return;
        }
        // a tap on the card opens the timer
        if (w.W >= 2) Wide(r, CapTimer, state, txt, null, frac, bar, vc, Ui.Id("wtr", id), Ui.Id("wtx", id), tl);
        else Small(r, CapTimer, txt, null, frac, bar, vc, Ui.Id("wtr", id), Ui.Id("wtx", id), lv: tl);
    }

    void StopwatchOrbit(RectangleF r, WatchWidget w, int id)
    {
        double t = StopwatchTime(Clock.Now);
        bool wide = w.W >= 2;
        string txt = StopwatchText(t, wide);
        uint vc = _swRunning ? Bento.Text : Sub;
        float pad = PadX(r), iw = r.Width - 2 * pad;
        if (wide)
        {
            // the time left, reset (once stopped) and start-stop right; a dot after the caption while it runs
            int nb = t > 0 && !_swRunning ? 2 : 1;
            const float bs = 44, gap = 8;
            float bx = r.Right - pad - nb * bs - (nb - 1) * gap, by = r.Y + 32;
            float cw = Caption(r, iw, CapStopwatch);
            if (_swRunning) _p.Circle(r.X + pad + cw + 10, r.Y + CapBase - 5, 4, Bento.CapDot);
            ValueUnit(r.X + pad, by + bs / 2 + 13, bx - 10 - r.X - pad, txt, null, 36, 20, vc);
            if (nb == 2 && Press(Ui.Id("wsr", id), new RectangleF(bx, by, bs, bs), "\uE72C", null, false, true, 18, true)) StopwatchReset();
            if (Press(Ui.Id("wsp", id), new RectangleF(bx + (nb - 1) * (bs + gap), by, bs, bs), _swRunning ? PauseFill : PlayFill, null, true,
                    true, 18, true))
                StopwatchTap();
            return;
        }
        // a 1x1: its caption in words like the others ("WATCH" when "STOPWATCH" is too long), a dot after it while it runs; a tap on
        // the card opens it
        float cw1 = Caption(r, iw - 14, CapStopwatch);
        if (_swRunning) _p.Circle(r.X + pad + cw1 + 9, r.Y + CapBase - 5, 4, Bento.CapDot);
        ValueUnit(r.X + pad, r.Y + SBase, iw, txt, null, Hero, 20, vc);
    }

    // "14:20" or "2:20 PM", the way the clock widgets write it
    string ShortClock(DateTime t) =>
        _cfg.Data.Appearance.Clock24h ? t.ToString("HH:mm", CultureInfo.CurrentCulture)
            : t.ToString("h:mm", CultureInfo.CurrentCulture) + Lang.T(t.Hour < 12 ? " AM" : " PM");

    static string SessionText(PanelData d)
    {
        if (d.VrSince is not { } since) return "–";
        var span = DateTime.Now - since;
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}" : Lang.F("{0} min", Math.Max(0, (int)span.TotalMinutes));
    }

    // The time in VR as a value and a unit - "1:23", or "42" "min" in the first hour - and as hours and minutes ("1" h "23" min;
    // no hours in the first one). The words are made once a minute.
    int _sessMinutes = -1;
    string _sessValue, _sessUnit, _sessH, _sessM;
    (string value, string unit) SessionValue(PanelData d)
    {
        if (d.VrSince is not { } since) return ("–", null);
        var span = Now - since;
        int minutes = Math.Max(0, (int)span.TotalMinutes);
        if (minutes != _sessMinutes)
        {
            _sessMinutes = minutes;
            if (minutes >= 60) { _sessValue = $"{minutes / 60}:{minutes % 60:00}"; _sessUnit = null; }
            else { _sessValue = minutes.ToString(CultureInfo.InvariantCulture); _sessUnit = "min"; }
            _sessH = minutes >= 60 ? (minutes / 60).ToString(CultureInfo.InvariantCulture) : null;
            _sessM = (minutes % 60).ToString(CultureInfo.InvariantCulture);
        }
        return (_sessValue, _sessUnit);
    }

    // The time in VR. A 1x1: "1:23" (or "42 min" in the first hour) as big as the other 1x1 values, since when under it. Wider:
    // "1 h 23 min" - the numbers bold, the units grey on their baseline - with the start in the caption row. SteamVR off: a
    // faint dash.
    void SessionOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var (val, unit) = SessionValue(d);
        bool off = d.VrSince == null;
        uint vc = off ? Bento.Faint : Bento.Text;
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        if (w.W >= 2)
        {
            CaptionRow(r, off ? null : SinceNote(d.VrSince.Value)[0], CapSessionWide);
            float by = r.Y + MBase;
            if (off) { ValueText(x, by, val, 40, vc, null); return; }
            // as big as fits (40 px, down to 30): every part measured at 100 px once
            string hu = Lang.C("unit", "h");           // "1 h 23 min" - the hour's unit by itself is no text of the table (a key's letter)
            float uw = _p.MeasureWidth(hu, SmallText, FontWeight.SemiBold), mw = _p.MeasureWidth("min", SmallText, FontWeight.SemiBold);
            float nw = (_sessH != null ? _p.MeasureWidth(_sessH, 100, FontWeight.Bold, Theme.Display) : 0) +
                       _p.MeasureWidth(_sessM, 100, FontWeight.Bold, Theme.Display);
            float units = (_sessH != null ? uw + 4 + 10 : 0) + 4 + mw;
            float vs = MathF.Max(30, MathF.Min(40, MathF.Floor(100 * (iw - units) / nw * 2) / 2));
            if (_sessH != null)
            {
                x += ValueText(x, by, _sessH, vs, vc, Ui.Id("wseh", id)) + 4;
                TextAt(hu, x, by, uw + 2, Sub, SmallText, FontWeight.SemiBold);
                x += uw + 10;
            }
            x += ValueText(x, by, _sessM, vs, vc, Ui.Id("wsem", id)) + 4;
            TextAt("min", x, by, mw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        Small(r, CapSession, val, unit, 0, 0, vc, null, Ui.Id("wse", id));
        if (!off) Note(x, r.Y + NoteBase, iw, SinceNote(d.VrSince.Value));      // a 12-hour start drops its AM / PM before it is cut
    }

    // "since 7:08 AM" and a shorter "since 7:08", made when the start or the clock style changes
    DateTime _sinceAt;
    bool _since24;
    int _sinceLang = -1;
    readonly string[] _sinceNote = new string[2];
    string[] SinceNote(DateTime since)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        if (since != _sinceAt || h24 != _since24 || _sinceNote[0] == null || _sinceLang != Lang.Version)
        {
            _sinceAt = since;
            _since24 = h24;
            _sinceLang = Lang.Version;
            _sinceNote[0] = Lang.F("since {0}", ShortClock(since));
            _sinceNote[1] = Lang.F("since {0}", since.ToString(h24 ? "HH:mm" : "h:mm", CultureInfo.CurrentCulture));
        }
        return _sinceNote;
    }

    // The clock's texts (the time, the seconds, a day's name), made once a second for each format and culture they are asked in: a
    // frame drawn again within the second makes none (a ToString each, every frame, before). prefix: put in front (":42").
    readonly Dictionary<(string fmt, string prefix), (long sec, CultureInfo c, string s)> _clk = new();

    string Clk(DateTime now, string fmt, CultureInfo c, string prefix = null)
    {
        long sec = now.Ticks / TimeSpan.TicksPerSecond;
        if (_clk.TryGetValue((fmt, prefix), out var e) && e.sec == sec && ReferenceEquals(e.c, c)) return e.s;
        string s = prefix == null ? now.ToString(fmt, c) : prefix + now.ToString(fmt, c);
        if (_clk.Count > 64) _clk.Clear();
        _clk[(fmt, prefix)] = (sec, c, s);
        return s;
    }

    // which batteries fit: all of them, or the emptiest ones (kept in their usual order). The list is the same one every call (no
    // garbage a frame): read it before the next call.
    List<int> PickBatteries(List<(string label, float pct, bool charging)> bats, int n)
    {
        var order = _pickOrder;
        order.Clear();
        for (int i = 0; i < bats.Count; i++) order.Add(i);
        if (bats.Count <= n) return order;
        _pickBats = bats;
        order.Sort(_byCharge ??= (a, b) => _pickBats[a].pct.CompareTo(_pickBats[b].pct));
        _pickBats = null;
        _pickKeep.Clear();
        for (int i = 0; i < n; i++) _pickKeep.Add(order[i]);
        _pickKeep.Sort();
        return _pickKeep;
    }

    readonly List<int> _pickOrder = new(), _pickKeep = new();
    List<(string label, float pct, bool charging)> _pickBats;
    Comparison<int> _byCharge;           // emptiest first: made once (a lambda holding the list was made every call)

    // the modern watch's battery rule, on the percentage as it is shown (so "20%" never shows in two colours): Ok above 30 %,
    // Warn 16-30 %, Bad at 15 % or less. Charging adds a bolt, never another colour.
    // The modern faces (the fixed ones too) write the percentage from this number, so its text and its colour cannot disagree.
    static int ShownPct(float pct) => (int)Math.Round((double)(pct * 100), MidpointRounding.AwayFromZero);

    static StatLevel BatteryLevel(int shown) => shown <= 15 ? StatLevel.Bad : shown <= 30 ? StatLevel.Warn : StatLevel.Ok;

    static StatLevel BatteryLevel(float pct) => BatteryLevel(ShownPct(pct));

    // the emptiest device (the first of equals)
    static int Emptiest(List<(string label, float pct, bool charging)> bats)
    {
        int best = 0;
        for (int i = 1; i < bats.Count; i++) if (bats[i].pct < bats[best].pct) best = i;
        return best;
    }

    // a device's name the way the cards write it: UPPERCASE grey 15 px, a white bolt in front while charging; returns its width
    // (inCaption: in the caption row, in its colours - a header's band has its own)
    float DeviceName(float x, float by, string name, bool charging, float maxW, bool inCaption = false)
    {
        float x0 = x;
        if (charging) { _p.Icon(Bolt, new RectangleF(x - 2, by - 16, 16, 17), BoltInk(inCaption ? Bento.CapText : Bento.Text), 15); x += 14; }
        string s = Ellipsize(name, maxW - (x - x0), CapSize, FontWeight.SemiBold);
        TextAt(s, x, by, maxW - (x - x0) + 2, inCaption ? Bento.Cap : Sub, CapSize, FontWeight.SemiBold);
        return x - x0 + _p.MeasureWidth(s, CapSize, FontWeight.SemiBold);
    }

    const string Bolt = "\uE945";

    void BatteryOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var bats = d.Vr.Batteries;
        float pad = PadX(r), iw = r.Width - 2 * pad;
        if (w.W == 1 && w.H == 1)
        {
            // The emptiest one: its percentage as big as every 1x1's value (red / amber text when low; on a coloured card a badge
            // beside it) and its line; which one it is (a bolt in front while charging) up in the caption row at the right - or,
            // with no room for both, as the caption.
            var line = new RectangleF(r.X + pad, r.Bottom - 16, iw, 4);
            if (bats.Count == 0)
            {
                Caption(r, iw, CapBattery);
                ValueUnit(r.X + pad, r.Y + SBase, iw, "–", null, Hero, 20, Bento.Faint);
                Meter(line, -1, Bento.Ok, Ui.Id("wbt", id * 8));
                return;
            }
            var b = bats[Emptiest(bats)];
            int shown = ShownPct(b.pct);
            var lv = BatteryLevel(shown);
            string name = Upper(Bat(b.label));
            float meta = _p.MeasureWidth(name, CapSize, FontWeight.SemiBold) + (b.charging ? 14 : 0);
            float mx = r.X + pad;
            if (CaptionWidth(CapBattery, iw - meta - 8) >= 0)
            {
                Caption(r, iw - meta - 8, CapBattery);
                mx = r.Right - pad - meta;
            }
            DeviceName(mx, r.Y + CapBase, name, b.charging, r.Right - pad - mx, true);
            string pct = Pct(shown);
            float dw = lv != StatLevel.Ok && Bento.Badge ? DotW : 0, vs = HeroFit(pct, null, iw - dw).vs;
            float vw = ValueText(r.X + pad, r.Y + SBase, pct, vs, Bento.Level(lv, Bento.Text), null);
            if (dw > 0) StatusDot(r.X + pad + vw + 11, r.Y + SBase - vs * DigitH / 2, lv);
            Meter(line, b.pct, Bento.Level(lv, Bento.Ok), Ui.Id("wbt", id * 8));
            return;
        }
        Caption(r, iw, CapBatteries);
        if (bats.Count == 0)
        {
            string s = d.VrConnected ? "No battery info" : "SteamVR off";
            TextAt(Ellipsize(s, iw, SmallText, FontWeight.SemiBold), r.X + pad, r.Y + 58, iw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        // as many as fit, a row each (the emptiest ones first): rows of 26 px or more (40 on a tall card), columns of 80 px or more
        bool big = w.H >= 2;
        var area = new RectangleF(r.X + pad, r.Y + 34, iw, r.Bottom - 10 - (r.Y + 34));
        float gapX = big ? 14 : 10;
        int maxRows = Math.Max(1, (int)(area.Height / (big ? 40 : 26))), maxCols = Math.Max(1, (int)((area.Width + gapX) / (80 + gapX)));
        var pick = PickBatteries(bats, Math.Min(maxRows * maxCols, 8));
        int n = pick.Count, cols = 1;
        while ((n + cols - 1) / cols > maxRows) cols++;
        int rows = (n + cols - 1) / cols;
        float cw = (area.Width - (cols - 1) * gapX) / cols, ch = MathF.Min(big ? 52 : 30, area.Height / rows);
        // a narrow cell on a tall card: the name over the percentage (side by side they would cut the name)
        int style = !big ? 0 : cw >= 120 ? 1 : ch >= 44 ? 2 : 0;
        for (int j = 0; j < n; j++)
            BatteryCell(new RectangleF(area.X + j % cols * (cw + gapX), area.Y + j / cols * ch, cw, ch), bats[pick[j]], Ui.Id("wbt", id * 8 + j), style);
    }

    // a device on a card: its name (UPPERCASE grey; a bolt in front while charging) and its percentage (red / amber when low)
    // on one baseline, its line under them
    // style 0: small, side by side (a one-row card: four of them in 2 x 2, digits as small as captions); 1: big, side by side;
    // 2: big, the name over the percentage (a narrow cell)
    void BatteryCell(RectangleF c, (string label, float pct, bool charging) b, string key, int style)
    {
        bool big = style > 0;
        float lt = big ? 6 : 4, ps = style == 1 ? 22 : style == 2 ? 20 : CapSize;
        var bar = new RectangleF(c.X, c.Bottom - lt - 3, c.Width, lt);
        float by = bar.Y - (big ? 7 : 5);
        int shown = ShownPct(b.pct);
        string pct = Pct(shown);
        float pw = _p.MeasureWidth(pct, ps, FontWeight.Bold, Theme.Display);
        var lv = BatteryLevel(shown);
        uint pc = Bento.Level(lv, Bento.Text);
        bool dot = lv != StatLevel.Ok && Bento.Badge;           // a coloured card: a badge beside a low one
        float ny = by, nr = c.Right - pw - 5 - (dot ? 22 : 0), dy = by - ps * DigitH / 2;     // the name 6 px from a badge
        if (style == 2)
        {
            TextAt(pct, c.X, by, pw + 4, pc, ps, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
            if (dot) StatusDot(c.X + pw + 11, dy, lv);
            ny = by - ps * DigitH - 9;
            nr = c.Right;
        }
        else
        {
            TextAt(pct, c.Right - pw - 4, by, pw + 4, pc, ps, FontWeight.Bold, TextAlignment.Trailing, Theme.Display);
            if (dot) StatusDot(c.Right - pw - 4 - 10, dy, lv);
        }
        DeviceName(c.X, ny, Upper(Bat(b.label)), b.charging, nr - c.X);
        Meter(bar, b.pct, Bento.Level(lv, Bento.Ok), key);
    }

    // the opened view's battery rows: the text size of rows this big - one size for all of them, small enough that every name
    // fits beside its percentage (never under 17 px for the percentage, 15 px for the names)
    float BatteryRowSize(SizeF cell, List<(string label, float pct, bool charging)> bats, int count)
    {
        float ts = Math.Clamp(cell.Height * 0.36f, SmallText, 34), iw = cell.Width - 20;
        for (int i = 0; i < count; i++)
        {
            var b = bats[i];
            float need = _p.MeasureWidth(Pct(ShownPct(b.pct)), ts, FontWeight.SemiBold, Theme.Display) + 8 + (b.charging ? RowBolt(ts) : 0) +
                _p.MeasureWidth(Upper(Bat(b.label)), RowName(ts), FontWeight.SemiBold);
            if (need > iw) ts = MathF.Max(SmallText, MathF.Floor(ts * iw / need * 0.95f * 2) / 2);
        }
        return ts;
    }

    static float RowName(float ts) => MathF.Max(CapSize, MathF.Round(ts * 0.7f));
    static float RowBolt(float ts) => RowName(ts) + 2;

    // A device as a row of the opened view, written like the cards: its name on the left (UPPERCASE grey, a white bolt in front
    // while charging), the percentage on the right (amber / red when low), the line under both
    void BatteryRow(RectangleF cell, (string label, float pct, bool charging) b, string key, float ts)
    {
        const float ip = 10;
        int shown = ShownPct(b.pct);
        var lv = BatteryLevel(shown);
        string pct = Pct(shown), name = Upper(Bat(b.label));
        float lt = Math.Clamp(cell.Height * 0.08f, 4, 8), iw = cell.Width - 2 * ip;
        float th = ts * 1.3f, gap = MathF.Max(4, ts * 0.2f), y = cell.Y + (cell.Height - th - gap - lt) / 2;
        var tr = new RectangleF(cell.X + ip, y, iw, th);
        float pw = _p.MeasureWidth(pct, ts, FontWeight.SemiBold, Theme.Display), ns = RowName(ts);
        float x = tr.X;
        if (b.charging)
        {
            float bw = RowBolt(ts);
            _p.Icon(Bolt, new RectangleF(x - 2, tr.Y, bw + 2, th), BoltInk(Bento.Text), ns);
            x += bw;
        }
        _p.Text(Ellipsize(name, tr.Right - pw - 8 - x, ns, FontWeight.SemiBold), new RectangleF(x, tr.Y, tr.Right - x, th), Sub, ns, FontWeight.SemiBold);
        _p.Text(pct, tr, Bento.Level(lv, Bento.Text), ts, FontWeight.SemiBold, TextAlignment.Trailing, ParagraphAlignment.Center, Theme.Display);
        Meter(new RectangleF(tr.X, tr.Bottom + gap, tr.Width, lt), b.pct, Bento.Level(lv, Bento.Ok), key);
    }

    // who is in the VRChat instance: how many, and the friends among them; a tap grows it into the watch's opened view (WristRoom.cs)
    // who is in the room, counted once per room snapshot (a StringBuilder and its string on every frame before)
    (int people, int friends, string names) _roomCount;
    RoomWatcher.Snapshot _roomCountFor;
    bool _roomCounted;

    (int people, int friends, string names) RoomCount(PanelData d)
    {
        if (_roomCounted && ReferenceEquals(d.Room, _roomCountFor)) return _roomCount;
        _roomCounted = true;
        _roomCountFor = d.Room;
        return _roomCount = CountRoom(d);
    }

    static (int people, int friends, string names) CountRoom(PanelData d)
    {
        var s = d.Room;
        if (s == null || !s.InRoom) return (-1, 0, "");
        int f = 0;
        var names = new System.Text.StringBuilder();
        foreach (var p in s.Here)
        {
            if (!p.Friend) continue;
            if (f < 2) names.Append(names.Length > 0 ? ", " : "").Append(p.Name);
            f++;
        }
        if (f > 2) names.Append($" +{f - 2}");
        return (s.Here.Count, f, names.ToString());
    }

    // the 95 room window's two lines ("3 people" / "2 friends: Mira, Tobi") and the small one's value, made again only when they change
    string _room95Head, _room95Line, _room95Value, _room95Cap;
    (int n, int f, string names, string world, int lang) _room95For = (-2, -1, null, null, -1);

    void Room95Texts(int n, int f, string names, string world)
    {
        var key = (n, f, names, world, Lang.Version);
        if (_room95Head != null && key == _room95For) return;
        _room95For = key;
        _room95Head = n < 0 ? "Not in a world" : n == 1 ? "1 person" : Lang.F("{0} people", n);
        _room95Line = f > 0 ? (f == 1 ? Lang.T("1 friend") : Lang.F("{0} friends", f)) + ": " + names : n < 0 ? "" : world;
        _room95Value = n < 0 ? "--" : n.ToString();
        _room95Cap = f > 0 ? (f == 1 ? "1 friend" : Lang.F("{0} friends", f)) : "in room";
    }

    const string Person = "\uE77B";

    // the modern room card's counts and words, worked out once per room snapshot (a new one comes with every change)
    RoomWatcher.Snapshot _roomSeen;
    bool _roomKnown;
    int _roomPeople = -1, _roomFriends;
    string _roomNames = "", _roomValue = "–", _roomFriendsText;

    void RoomNow(PanelData d)
    {
        if (_roomKnown && ReferenceEquals(d.Room, _roomSeen)) return;
        _roomKnown = true;
        _roomSeen = d.Room;
        (_roomPeople, _roomFriends, _roomNames) = RoomCount(d);
        _roomValue = _roomPeople < 0 ? "–" : _roomPeople.ToString(CultureInfo.InvariantCulture);
        _roomFriendsText = _roomFriends > 0 ? _roomFriends.ToString(CultureInfo.InvariantCulture) : null;
    }

    void RoomOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        bool click = _ui.Clickable(Ui.Id("wrm", id), r, out _, out bool held);
        if (held) _p.Fill(r, Theme.WithAlpha(Bento.Accent, 0.12f), CardRadius);
        if (click) OpenDetail(w);
        RoomNow(d);
        int n = _roomPeople, f = _roomFriends;
        string value = _roomValue;
        uint vc = n < 0 ? Bento.Faint : Bento.Text;
        float pad = PadX(r), iw = r.Width - 2 * pad;
        if (w.W >= 2)
        {
            // the world as the note; how many big, the friends among them (green) or what it is beside it
            string cap = n < 0 ? (d.Room?.LogFound == false ? "no VRChat" : "not in a world") : n == 1 ? "person here" : "people here";
            CaptionRow(r, n < 0 ? null : d.Room.World, CapRoom);
            float vw = ValueUnit(r.X + pad, r.Y + MBase, 90, value, null, 40, 24, vc);
            float lx = r.X + pad + vw + 12, room = r.Right - pad - lx;
            string line = f > 0 ? _roomNames : cap;
            TextAt(Ellipsize(line, room, SmallText, FontWeight.SemiBold), lx, r.Y + MBase, room + 2, f > 0 ? Bento.Ok : Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        // A 1x1: how many; after it the friends among them (a green person and how many), else "here". Its one graphic: a dot per
        // person along the bottom (the friends first, green; as many as fit), empty ones while there is no room to count.
        Caption(r, iw, CapRoom);
        string fc = _roomFriendsText;
        float fw = fc == null ? 0 : _p.MeasureWidth(fc, SmallText, FontWeight.SemiBold) + 20;
        float used = ValueUnit(r.X + pad, r.Y + SBase, iw - fw, value, n < 0 || fc != null ? null : "here", Hero, 20, vc);
        const float dot = 7, pitch = 11;
        int fit = (int)((iw - dot) / pitch) + 1, shown = n < 0 ? fit : Math.Min(n, fit);
        uint other = Bento.Ok == Bento.Text ? Bento.Faint : Sub;     // a coloured card: the friends in its text colour, the rest fainter
        for (int i = 0; i < shown; i++)
            _p.Circle(r.X + pad + dot / 2 + i * pitch, r.Bottom - 14, dot / 2, n < 0 ? Bento.Track : i < f ? Bento.Ok : other);
        if (fc == null) return;
        float fx = r.X + pad + used + 6;
        _p.Icon(Person, new RectangleF(fx - 1, r.Y + SBase - 16, 18, 18), Bento.Ok, 15);
        TextAt(fc, fx + 17, r.Y + SBase, fw, Bento.Ok, SmallText, FontWeight.SemiBold);
    }

    void Room95Widget(RectangleF r, RectangleF c, PanelData d, WatchWidget w, int id)
    {
        if (_ui.Clickable(Ui.Id("wrm", id), r, out _, out _)) OpenDetail(w);
        var (n, f, names) = RoomCount(d);
        Room95Texts(n, f, names, n < 0 ? null : d.Room.World);
        if (w.W >= 2)
        {
            _ui.Text95(Fit(_room95Head, c.Width - 8, Ui.F95, true), new RectangleF(c.X + 4, c.Y + c.Height / 2 - 14, c.Width - 8, 13), Blk95, true);
            _ui.Text95(Fit(_room95Line, c.Width - 8), new RectangleF(c.X + 4, c.Y + c.Height / 2 + 1, c.Width - 8, 13), f > 0 ? 0xFF008000 : Gry95);
            return;
        }
        Value95(c, _room95Value, _room95Cap, Blk95);
    }

    static bool DockShows(PanelData d) =>
        d.Dock != null && d.DockFaces != null && d.DockFaces.Any &&
        d.Dock.State is HexpadLink.LinkState.Ok or HexpadLink.LinkState.ScreenOff;

    static string DockState(PanelData d) => d.Dock?.State switch
    {
        HexpadLink.LinkState.NotRunning => "not running",
        HexpadLink.LinkState.OldHexpad => "update Hexpad",
        HexpadLink.LinkState.Unplugged => "unplugged",
        HexpadLink.LinkState.ScreenOff => "screen off",
        HexpadLink.LinkState.Connecting => "connecting",
        _ => "",
    };

    static string DockWhere(HexpadLink.Snapshot s) => s.InFolder ? s.FolderName : s.PageName;

    // why the dock is not shown, in words that fit (17 px): Hexpad's own, else "offline", else cut
    string DockWhy(string st, float maxW)
    {
        if (_p.MeasureWidth(st, SmallText, FontWeight.SemiBold) <= maxW) return st;
        if (_p.MeasureWidth("offline", SmallText, FontWeight.SemiBold) <= maxW) return "offline";
        return Ellipsize(st, maxW, SmallText, FontWeight.SemiBold);
    }

    // the six keys of the dock, 3 x 2, in r (centred); returns the grid's rect
    RectangleF DockGrid(RectangleF r, PanelData d, float gap, float radius, bool pixel)
    {
        float cell = MathF.Floor(MathF.Min((r.Width - 2 * gap) / 3, (r.Height - gap) / 2));
        float gw = 3 * cell + 2 * gap, gh = 2 * cell + gap;
        float x0 = MathF.Floor(r.X + (r.Width - gw) / 2), y0 = MathF.Floor(r.Y + (r.Height - gh) / 2);
        for (int i = 0; i < HexpadLink.Keys; i++)
        {
            var k = new RectangleF(x0 + i % 3 * (cell + gap), y0 + i / 3 * (cell + gap), cell, cell);
            _p.Fill(k, 0xFF000000, radius);
            var bmp = pixel ? d.DockFaces.Bitmaps[i] : d.DockFaces.Best(i);      // 95: the dock's own pixels
            if (bmp == null || d.Dock.Dark) continue;
            if (pixel) { _p.ImageNearest(bmp, k); continue; }
            _p.PushRoundedClip(k, radius);
            _p.Image(bmp, k);
            _p.PopRoundedClip();
        }
        return new RectangleF(x0, y0, gw, gh);
    }

    // Hexpad's stream dock on the watch: its keys as they are right now (no caption: the keys say what it is); a tap opens it
    // as a panel
    void DockOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        bool click = _ui.Clickable(Ui.Id("wdk", id), r, out bool hover, out bool held);
        if (held) _p.Fill(r, Theme.WithAlpha(Bento.Accent, 0.12f), CardRadius);
        if (click) OpenDetail(w);                // grows into the watch's opened dock (WristDock.cs)
        float pad = PadX(r);
        if (!DockShows(d))
        {
            // Not there: laid out like every card - its caption at the top-left, the dock's glyph where a value would be, and why
            // in a 17 px line ("not connected", or what Hexpad says; "offline" where neither fits)
            float iw = r.Width - 2 * pad;
            Caption(r, iw, CapHexpad);
            string st = DockState(d);
            if (st.Length == 0) st = "not connected";
            const float gs = 28;
            if (w.W >= 2 && w.H == 1)
            {
                // the glyph and the line side by side
                _p.Icon("\uE80A", new RectangleF(r.X + pad - 3, r.Y + SBase - 25, gs + 6, gs + 4), Sub, gs);
                float tx = r.X + pad + gs + 12, tw = r.Right - pad - tx;
                TextAt(DockWhy(st, tw), tx, r.Y + SBase - 4, tw + 2, Sub, SmallText, FontWeight.SemiBold);
                return;
            }
            // the glyph, the line under it (along the bottom of a 1x1)
            _p.Icon("\uE80A", new RectangleF(r.X + pad - 3, r.Y + SBase - 25, gs + 6, gs + 4), Sub, gs);
            TextAt(DockWhy(st, iw), r.X + pad, w.H >= 2 ? r.Y + SBase + 30 : r.Bottom - 13, iw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        var s2 = d.Dock;
        if (w.W >= 2 && w.H == 1)
        {
            // the keys on the left; the page (or folder) and where in the pages on the right
            var g = DockGrid(new RectangleF(r.X + 10, r.Y + 10, (r.Height - 20) * 1.55f, r.Height - 20), d, 4, 6, false);
            float tx = g.Right + 12, tw = r.Right - 12 - tx;
            TextAt(Ellipsize(DockWhere(s2), tw, SmallText, FontWeight.SemiBold), tx, r.Y + r.Height / 2 - 4, tw + 2, Bento.Text, SmallText, FontWeight.SemiBold);
            string page = s2.Ambient ? "idle" : s2.InFolder ? "folder" : $"{s2.Page + 1} / {s2.Pages}";
            TextAt(Ellipsize(page, tw, SmallText, FontWeight.SemiBold), tx, r.Y + r.Height / 2 + 20, tw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        if (w.H >= 2)
        {
            var g = DockGrid(new RectangleF(r.X + 12, r.Y + 12, r.Width - 24, r.Height - 52), d, 6, 8, false);
            string where = DockWhere(s2) + (s2.InFolder ? "" : $"  ·  {s2.Page + 1}/{s2.Pages}");
            _p.Text(Ellipsize(where, r.Width - 28, SmallText, FontWeight.SemiBold), new RectangleF(r.X + 14, r.Bottom - 36, r.Width - 28, 24), Sub, SmallText,
                FontWeight.SemiBold, TextAlignment.Center);
            return;
        }
        DockGrid(RectangleF.Inflate(r, -9, -9), d, 3, 4, false);
    }

    // Now playing: the cover on the left when there is room (three cells and up, or two rows), the caption, title and artist,
    // play / pause (the accent) - with previous / next too when it is six wide or tall
    void MediaOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var (has, title, artist, playing) = MediaNow(d);
        float pad = PadX(r);
        bool tall = w.H >= 2;
        float x = r.X + pad;
        if (w.W >= 3 || tall)
        {
            const float m = 12;
            float s = tall ? MathF.Min(r.Height - 2 * m, r.Width * 0.45f) : r.Height - 2 * m;
            if (tall) s = MathF.Min(s, r.Width - 2 * m - 14 - 170);      // a narrow tall card (the 4 x 3 L): room for its three buttons
            var cover = new RectangleF(r.X + m, r.Y + (r.Height - s) / 2, s, s);
            string fk = Ui.Id("wma", id);
            if (_artFade) _ui.SpringFrom(fk, 0);      // a new cover cross-fades over the one before (no cover: the note fades in)
            float fade = Math.Clamp(_ui.Spring(fk, 1, 170, 26), 0, 1);
            float cr = MathF.Max(10, s * 0.1f);
            _p.Fill(cover, Bento.Tile, cr);
            bool old = fade < 0.999f && _artOld != null;
            if (_art != null || old)
            {
                _p.PushRoundedClip(cover, cr);
                if (old) _p.Image(_artOld, cover, _art != null ? 1 : 1 - fade);
                if (_art != null) _p.Image(_art, cover, fade);
                _p.PopRoundedClip();
            }
            if (_art == null) _p.Icon("\uE8D6", cover, Theme.WithAlpha(Sub, fade), s * 0.36f);
            x = cover.Right + 14;
        }
        var ids = MediaIds;
        string playIcon = playing ? PauseFill : PlayFill;
        float right;          // where the text column ends
        if (tall)
        {
            // previous / play / next at the bottom of the text column
            const float big = 52, sm = 44, gap = 10;
            float by = r.Bottom - 12 - big;
            if (Press(Ui.Id("wcm", id * 3), new RectangleF(x, by + (big - sm) / 2, sm, sm), "\uE892", null, false, has, 18, true))
                Act(ids[0]);
            if (Press(Ui.Id("wcm", id * 3 + 1), new RectangleF(x + sm + gap, by, big, big), playIcon, null, true, has, 20, true))
                Act(ids[1]);
            if (Press(Ui.Id("wcm", id * 3 + 2), new RectangleF(x + sm + gap + big + gap, by + (big - sm) / 2, sm, sm), "\uE893", null, false,
                    has, 18, true))
                Act(ids[2]);
            right = r.Right - pad;
        }
        else
        {
            // one row: play (and previous / next on a six-wide one) on the right; under the caption when there is no cover
            int nb = w.W >= 6 ? 3 : 1;
            const float big = 48, sm = 44, gap = 8;
            float total = nb == 3 ? big + 2 * (sm + gap) : big;
            float bx = r.Right - 12 - total, cy = w.W >= 3 ? r.Y + r.Height / 2 : r.Y + 32 + big / 2;
            for (int j = 0; j < nb; j++)
            {
                int a = nb == 1 ? 1 : j;
                float bs = a == 1 ? big : sm;
                float px = nb == 1 ? bx : bx + (j == 0 ? 0 : j == 1 ? sm + gap : sm + gap + big + gap);
                string icon = a == 0 ? "\uE892" : a == 1 ? playIcon : "\uE893";
                if (Press(Ui.Id("wcm", id * 3 + a), new RectangleF(px, cy - bs / 2, bs, bs), icon, null, a == 1, has, a == 1 ? 20 : 18, true))
                    Act(ids[a]);
            }
            right = bx - 10;
        }
        float tw = right - x;
        if (tw < 40) return;
        // the caption over the text column (across the card when the play button sits under it)
        CaptionRow(r, x, w.W >= 3 || tall ? right : r.Right - pad, null, Bento.Cap, CapMedia);
        if (!has)
        {
            // one wording on every size (and opened): "Nothing playing" does not fit a 2x1 at 17 px
            TextAt(Ellipsize(NotPlaying, tw, SmallText, FontWeight.SemiBold), x, r.Y + 56, tw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        if (tall)
        {
            // a long title takes two lines before it is cut
            float ts = 21, ty = r.Y + 58;
            var (l1, l2) = TwoLines(title ?? "", tw, ts, FontWeight.SemiBold);
            TextAt(l1, x, ty, tw + 2, Bento.Text, ts, FontWeight.SemiBold);
            if (l2.Length > 0) { ty += 26; TextAt(l2, x, ty, tw + 2, Bento.Text, ts, FontWeight.SemiBold); }
            TextAt(Ellipsize(artist ?? "", tw, SmallText, FontWeight.SemiBold), x, ty + 26, tw + 2, Sub, SmallText, FontWeight.SemiBold);
            return;
        }
        TextAt(Ellipsize(title ?? "", tw, SmallText, FontWeight.SemiBold), x, r.Y + 54, tw + 2, Bento.Text, SmallText, FontWeight.SemiBold);
        TextAt(Ellipsize(artist ?? "", tw, SmallText, FontWeight.SemiBold), x, r.Y + 78, tw + 2, Sub, SmallText, FontWeight.SemiBold);
    }

    // The system volume: the level big and as a line (read-only on the card - a tap opens the slider); - / + steps of 5 % on a
    // 2x1, the mute disc in front of them on a 3x1 (no slider on a card). Muted: the card turns red, its value a crossed speaker
    // with the level it comes back to grey beside it.
    void VolumeOrbit(RectangleF r, WatchWidget w, int id)
    {
        float lvl = Volume.Level;
        bool muted = Volume.Muted, ok = lvl >= 0, mutedNow = ok && muted;
        string pct = ok ? VolumePct(lvl) : "–";
        uint vc = ok ? Bento.Text : Bento.Faint, bar = muted ? Bento.Faint : Bento.Accent;
        float pad = PadX(r), iw = r.Width - 2 * pad;
        if (w.W == 1)
        {
            if (!mutedNow) { Small(r, CapVolume, pct, null, ok ? lvl : -1, bar, vc, Ui.Id("wvg", id)); return; }
            Caption(r, iw, CapVolume);
            MutedValue(r.X + pad, r.Y + SBase, iw, pct);
            Meter(new RectangleF(r.X + pad, r.Bottom - 16, iw, 4), lvl, bar, Ui.Id("wvg", id));
            return;
        }
        Caption(r, iw, CapVolume);
        const float bs = 44, gap = 6;
        float by = r.Y + 31;
        // the discs from the right: +, -, and on a 3x1 the mute disc a little apart in front of them
        float bx = r.Right - pad - bs;
        if (Press(Ui.Id("wvp", id), new RectangleF(bx, by, bs, bs), "\uE710", null, false, ok, 18, true)) Volume.Step(0.05f);
        bx -= bs + gap;
        if (Press(Ui.Id("wvm", id), new RectangleF(bx, by, bs, bs), "\uE738", null, false, ok, 18, true)) Volume.Step(-0.05f);
        if (w.W >= 3)
        {
            bx -= bs + 12;
            if (Press(Ui.Id("wvs", id), new RectangleF(bx, by, bs, bs), muted ? MuteGlyph : "\uE767", null, muted, ok, 20, true, accentFill: false,
                    fill: Bento.Bad))
                Volume.ToggleMute();
        }
        float vy = by + bs / 2 + Hero * DigitH / 2, room = bx - 10 - (r.X + pad);
        if (mutedNow) MutedValue(r.X + pad, vy, room, pct);
        else ValueUnit(r.X + pad, vy, room, pct, null, Hero, 20, vc);
        Meter(new RectangleF(r.X + pad, r.Bottom - 15, iw, 4), ok ? lvl : -1, bar, Ui.Id("wvg", id));
    }

    // "50%": the volume as the watch writes it (the card and its opened view)
    static string VolumePct(float lvl) => Pct(ShownPct(lvl));

    // a muted volume's value: the crossed speaker as big as a value, red, and the level it comes back to grey after it (when it fits)
    void MutedValue(float x, float by, float maxW, string pct)
    {
        float gs = Hero;
        _p.Icon(MuteGlyph, new RectangleF(x - 3, by - Hero * DigitH / 2 - gs / 2 - 1, gs + 6, gs + 2), Bento.Bad, gs);
        float tx = x + gs + 6;
        if (tx + _p.MeasureWidth(pct, SmallText, FontWeight.SemiBold) <= x + maxW) TextAt(pct, tx, by, maxW - (tx - x) + 2, Sub, SmallText, FontWeight.SemiBold);
    }

    void StatOrbit(RectangleF r, WatchWidget w, WatchWidgets.Kind k, PanelData d, int id)
    {
        if (k.Id == "session") return;
        if (w.W == 1) { StatS(r, k.Id, d, Ui.Id("wsg", id)); return; }
        var (frac, value, unit, caption, lv) = Reading(k.Id, d);
        bool none = value == "–";
        Wide(r, WideCaption(k.Id), caption, value, !none && unit is "fps" or "ms" or "GB" ? unit : null, frac, Bento.Level(lv, RoleColor(k.Id)),
            none ? Bento.Faint : Bento.Level(lv, Bento.Text), Ui.Id("wsg", id), lv: lv);
    }

    // "86 fps · 9,3 ms · 4%" and the worst of their levels (each number read once)
    static (string text, StatLevel level) FrameNote(VrStats v)
    {
        float fps = v.AppFps, ms = v.GpuMs, rp = v.ReprojPct, hz = v.DisplayHz;
        if (fps < 0) return ("waiting for frames", StatLevel.Ok);
        var lv = (StatLevel)Math.Max((int)FpsLevel(fps, hz), Math.Max((int)GpuLevel(ms, 1000f / Math.Max(1, hz)), (int)ReprojLevel(rp)));
        return ($"{fps:0} fps · {ms:0.0} ms · {Math.Max(0, rp):0}%", lv);
    }

    // the opened view's graph: its numbers as a line of text over the bars (amber / red with the worst of them, like the card)
    void GraphPlain(VrStats v, RectangleF r, uint tint)
    {
        var (label, lv) = FrameNote(v);
        _p.Text(Ellipsize(label, r.Width - 32, SmallText, FontWeight.SemiBold), new RectangleF(r.X + 16, r.Y + 6, r.Width - 32, 24),
            Bento.Level(lv, Bento.Text), SmallText, FontWeight.SemiBold);
        GraphBars(v, new RectangleF(r.X + 14, r.Y + 34, r.Width - 28, r.Height - 44), tint);
    }

    void GraphBars(VrStats v, RectangleF g, uint tint)
    {
        float budget = 1000f / Math.Max(1, v.DisplayHz), top = budget * 2;
        int n = v.FrameCount;
        float step = g.Width / VrStats.FrameHistory;
        // the look's colours: on a filled card the bars in its text colour (fainter, a flagged one full); a flag in the text
        // colour where the bars are the flag's hue (amber over budget on amber bars would vanish)
        ref readonly var L = ref Bento.Cur;
        uint bar = L.GraphBar != 0 ? L.GraphBar : tint;
        for (int i = 0; i < n; i++)
        {
            int idx = (v.FrameHead - n + i + VrStats.FrameHistory) % VrStats.FrameHistory;
            float ms = v.FrameMs[idx];
            byte flag = v.FrameFlag[idx];
            float h = Math.Clamp(ms / top, 0.03f, 1) * g.Height;
            uint col = flag == 2 ? L.FlagBad : flag == 1 || ms > budget ? L.FlagWarn : bar;
            float x = g.Right - (n - i) * step;
            _p.Fill(new RectangleF(x, g.Bottom - h, MathF.Max(1, step - 0.4f), h), col);
        }
        float by = g.Bottom - g.Height * (budget / top);
        // the budget: white at 40 % (dark on a light card)
        _p.Line(g.X, by, g.Right, by, Bento.Text == Bento.White ? 0x66FFFFFFu : Theme.WithAlpha(Bento.Text, 0.4f), 1.5f);
    }

    // ================= Windows 95 =================

    const uint Navy95c = 0xFF000080;

    void Custom95(PanelData d)
    {
        if (SkinCustom(d)) return;                // a style's own custom face (ScreenHooks.cs)
        int count = WatchPages.Count(C), cur = CurPage;
        var list = Layout;
        var grid = WatchPages.Grid(C, cur);
        _grid = grid;
        _big = false;
        SampleSources();
        if (C.Background != "cards") Theme.RetroSkin.FillDesktop(_p, new RectangleF(0, 0, TW95, TH95));     // the desktop
        var now = Now;
        _ui.Inert = _open != null || EditFolderOn(list);
        Widgets95(list, grid, d, now);
        _ui.Inert = false;
        if (count > 1) Taskbar95(count, cur);
        FolderEditView95(d, list);                          // the folder the editor works in, open over the page (WristFolder95.cs)
        DrawDetail95(d, list);
    }

    // a page's widgets in the 95 family - a folder's too, in its window (WristFolder95.cs)
    void Widgets95(List<WatchWidget> list, GridSpec grid, PanelData d, DateTime now)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            var k = WatchWidgets.Find(w.Type);
            if (k == null) continue;
            int id = Wid(w);
            var r = WatchWidgets.Rect(w, true, grid);
            CardTap(id, w, k, r);
            if (k.Id == "folder") { Folder95Widget(w, k, r, d, id); continue; }     // each style's folder (WristFolder95.cs)
            if (SkinWidget(w, k, r, d, id)) continue;        // a style's own widget (ScreenHooks.cs)
            if (k.Button) { Button95Widget(id, w, k, r, d); continue; }
            // a little 95 window: with a caption when it is tall enough, a plain raised panel when not (the style's skin)
            var c = Theme.RetroSkin.WidgetFrame(_p, r, w.H >= 2 ? k.Title95 : null);
            switch (k.Id)
            {
                case "time" when w.W == 1: Time95Small(c, now); break;
                case "time": Time95(c, now, w.H >= 2); break;
                case "date" when w.W == 1: Calendar95(c, w, now); break;       // the modern-only 1x1: the calendar's day
                case "date":
                    // a wide one gets the 10-point font
                    string ds = FitDate95(now, c.Width - 6);
                    float dsz = w.W >= 4 && _ui.Width95(ds, false, 13) <= c.Width - 10 ? 13 : Ui.F95;
                    _ui.Text95(ds, c, Blk95, false, TextAlignment.Center, size: dsz);
                    break;
                case "calendar": Calendar95(c, w, now); break;
                case "analog":
                    Dial95(now, MathF.Floor(c.X + c.Width / 2), MathF.Floor(c.Y + c.Height / 2), MathF.Floor(MathF.Min(c.Width, c.Height) / 2) - 5);
                    break;
                case "timer": Timer95(c, w, id); break;
                case "stopwatch": Stopwatch95(c, w, id); break;
                case "battery": Battery95(c, d, w); break;
                case "media": Media95Widget(c, d, w, id); break;
                case "dock": Dock95Widget(r, c, d, w, id); break;
                case "room": Room95Widget(r, c, d, w, id); break;
                case "volume": Volume95(c, w, id); break;
                case "graph": Graph95(d.Vr, c); break;
                case "minesweeper": Mines95Widget(c, w); break;
                case "mic": Mic95(r, c, w, id); break;
                case "notifications": Notes95(c, d); break;
                case "buttons": Group95(c, w, d, id); break;
                case "mixer": Mixer95(r, c, w, id); break;
                case "photo": Photo95(r, c, w, d, id); break;
                case "weather": Weather95(c, d, w); break;
                case "friends": Friends95(c, w, d); break;
                case "recap": Recap95(c, w, d); break;
                case "light": Light95(r, c, w, d, id); break;          // (WristLights95.cs)
                case "rgb": Rgb95(r, c, w, d, id); break;
                case "memory": Photo95(r, c, w, d, id); break;
                case "temps": Temps95(c, d, w); break;
                case "avatar": Avatar95(r, c, w, d, id); break;          // (WristAvatar95.cs)
                case "perf": Status95(d, new RectangleF(c.X, MathF.Floor(c.Y + (c.Height - 24) / 2), c.Width, 24)); break;
                case "session":
                    string sv = SessionText(d);
                    Value95(c, sv == "–" ? "--" : sv, w.W >= 2 ? "time in VR" : "in VR", Blk95);
                    break;
                default:
                    var (frac, value, unit, _, tint) = StatInfo(k.Id, d);
                    uint color = tint == Theme.Warn ? 0xFF806000u : tint == Theme.Bad ? 0xFF800000u : Blk95;
                    if (value == "–") value = "--";
                    string cap95 = k.Id switch { "fps" => "fps", "reproj" => "reproj.", "gpu" => "GPU ms", "vram" => "VRAM", "cpu" => "CPU", _ => "RAM" };
                    if (w.W >= 2 && (k.Id is "cpu" or "ram" or "vram") && frac >= 0)
                    {
                        // a System Monitor bar
                        _ui.Text95(Fit($"{Lang.T(cap95)}: {value}{(unit == "GB" ? " GB" : "")}", c.Width - 8), new RectangleF(c.X + 4, c.Y + 4, c.Width - 8, 13), color);
                        _ui.Progress95(new RectangleF(c.X + 4, c.Y + 22, c.Width - 8, 14), frac, Navy95c);
                    }
                    else Value95(c, value, cap95, color);
                    break;
            }
        }
    }

    void Value95(RectangleF c, string value, string caption, uint color)
    {
        float vy = MathF.Floor(c.Y + (c.Height - 28) / 2);
        _ui.Text95(Fit(value, c.Width - 2, 13, true), new RectangleF(c.X, vy, c.Width, 15), color, true, TextAlignment.Center, size: 13);
        _ui.Text95(Fit(caption, c.Width - 2), new RectangleF(c.X, vy + 15, c.Width, 13), Blk95, false, TextAlignment.Center);
    }

    void Time95(RectangleF c, DateTime now, bool tall)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        string time = Clk(now, h24 ? "H:mm:ss" : "h:mm:ss", CultureInfo.CurrentCulture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        bool withDate = tall && c.Height >= 44;
        float avail = c.Width - 8 - (ampm != null ? _ui.Width95(ampm) + 4 : 0);
        float size = MathF.Floor(MathF.Min(c.Height * (withDate ? 0.62f : 0.82f), 100 * avail / _ui.Width95(time, false, 100)));
        size = Math.Max(11, size);
        var tr = withDate ? new RectangleF(c.X, c.Y, c.Width, c.Height - 15) : c;
        float th = MathF.Ceiling(size * 1.2f);
        if (withDate && c.Height - th - 15 > 12)
            tr = new RectangleF(c.X, MathF.Floor(c.Y + (c.Height - th - 15) / 2), c.Width, th);
        BigTime95(time, ampm, tr, size, true);
        if (!withDate) return;
        _ui.Text95(FitDate95(now, c.Width - 6), new RectangleF(c.X, MathF.Min(c.Bottom - 15, tr.Bottom + 1), c.Width, 13), Blk95, false, TextAlignment.Center);
    }

    // a 1x1 time (modern only: the 95 face draws it when the face was made in the modern style): a small window's value, the
    // hours and minutes, with AM / PM or the day under them
    void Time95Small(RectangleF c, DateTime now)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        Value95(c, Clk(now, h24 ? "H:mm" : "h:mm", culture), h24 ? Clk(now, "ddd d", culture) : now.Hour < 12 ? "AM" : "PM", Blk95);
    }

    // the long date when it fits, else without the year, then the short date (found once a day for each width, look and language)
    (DateTime day, float w, CultureInfo c, int ver, string s) _fitDate;

    string FitDate95(DateTime now, float maxW)
    {
        var culture = CultureInfo.CurrentCulture;
        int ver = Theme.Version * 1000 + Lang.Version;
        if (_fitDate.s != null && _fitDate.day == now.Date && _fitDate.w == maxW && ReferenceEquals(_fitDate.c, culture) && _fitDate.ver == ver)
            return _fitDate.s;
        var f = culture.DateTimeFormat;
        string fit = null;
        foreach (string pattern in new[] { f.LongDatePattern, "dddd, " + f.MonthDayPattern, f.MonthDayPattern })
        {
            string s = now.ToString(pattern, culture);
            if (_ui.Width95(s) <= maxW) { fit = s; break; }
        }
        fit ??= Fit(now.ToString(f.ShortDatePattern, culture), maxW);
        _fitDate = (now.Date, maxW, culture, ver, fit);
        return fit;
    }

    // Date/Time Properties' calendar: a white month grid, today in navy
    void Calendar95(RectangleF c, WatchWidget w, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        if (w.H >= 2 && c.Width < 140)
        {
            // too small for a month grid: a desk calendar's page - the month on top, the day big
            var page = new RectangleF(c.X + 2, c.Y + 2, c.Width - 4, c.Height - 4);
            _p.Fill(page, Wht95);
            _p.Bevel(page, Painter.Edge.Sunken);
            var head = new RectangleF(page.X + 2, page.Y + 2, page.Width - 4, 15);
            _p.Fill(head, 0xFF800000);
            _ui.Text95(Fit(Clk(now, "MMMM", culture), head.Width - 4, Ui.F95, true), head, Wht95, true, TextAlignment.Center);
            _ui.Text95(now.Day.ToString(), new RectangleF(page.X, head.Bottom + 1, page.Width, page.Bottom - head.Bottom - 19), Blk95, true,
                TextAlignment.Center, size: MathF.Floor(MathF.Min(36, (page.Bottom - head.Bottom - 19) * 0.8f)));
            _ui.Text95(Fit(Clk(now, "dddd", culture), page.Width - 4), new RectangleF(page.X, page.Bottom - 19, page.Width, 13), Blk95, false,
                TextAlignment.Center);
            return;
        }
        if (w.H < 2)
        {
            string wd = Clk(now, w.W >= 2 ? "dddd" : "ddd", culture);
            string dd = w.W >= 2 ? Clk(now, culture.DateTimeFormat.MonthDayPattern, culture) : now.Day.ToString();
            float vy = MathF.Floor(c.Y + (c.Height - 31) / 2);
            _ui.Text95(Fit(wd, c.Width - 4), new RectangleF(c.X, vy, c.Width, 13), Blk95, false, TextAlignment.Center);
            _ui.Text95(Fit(dd, c.Width - 4, 16, true), new RectangleF(c.X, vy + 13, c.Width, 18), Blk95, true, TextAlignment.Center, size: 16);
            return;
        }
        var box = new RectangleF(c.X + 2, c.Y + 2, c.Width - 4, c.Height - 4);
        _p.Fill(box, Wht95);
        _p.Bevel(box, Painter.Edge.Sunken);
        var g = RectangleF.Inflate(box, -3, -3);
        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        float cw = g.Width / 7, rh = MathF.Floor(g.Height / 7);
        for (int col = 0; col < 7; col++)
        {
            string n = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((first + col) % 7));
            _ui.Text95(n.Substring(0, 1).ToUpper(culture), new RectangleF(g.X + col * cw, g.Y, cw, rh), Blk95, true, TextAlignment.Center);
        }
        var day1 = new DateTime(now.Year, now.Month, 1);
        int offset = ((int)day1.DayOfWeek - first + 7) % 7, days = DateTime.DaysInMonth(now.Year, now.Month);
        for (int dd = 1; dd <= days; dd++)
        {
            int cell = offset + dd - 1;
            var cr = new RectangleF(MathF.Floor(g.X + cell % 7 * cw), g.Y + (cell / 7 + 1) * rh, MathF.Ceiling(cw), rh);
            bool today = dd == now.Day;
            if (today) _p.Fill(cr, Navy95c);
            _ui.Text95(dd.ToString(), cr, today ? Wht95 : Blk95, false, TextAlignment.Center);
        }
    }

    void Timer95(RectangleF c, WatchWidget w, int id)
    {
        double left = TimerLeft(Clock.Now);
        string txt = _timer == TimerState.Done ? "Done!" : Clock2(left);
        if (w.H >= 2)
        {
            bool idle = _timer == TimerState.Idle;
            _ui.Text95(txt, new RectangleF(c.X + 4, c.Y + 3, c.Width - 36, 18), _timer == TimerState.Done ? 0xFF800000 : Blk95, true, TextAlignment.Leading,
                size: 16);
            if (_ui.GlyphButton95(Ui.Id("wtp", id), new RectangleF(c.Right - 30, c.Y + 3, 26, 18), _timer == TimerState.Running ? PauseGlyph : PlayGlyph, true))
                TimerTap();
            float by = c.Bottom - 23;
            float frac = idle ? 1 : (float)(left / Math.Max(1, Math.Max(_timerTotal, left)));
            _ui.Progress95(new RectangleF(c.X + 4, MathF.Floor(c.Y + 21 + (by - c.Y - 21 - 14) / 2), c.Width - 8, 14), frac, Navy95c);
            float rw = _ui.Width95("Reset") + 10, sw = MathF.Floor((c.Width - 8 - rw - 6) / 2);
            if (_ui.Button95(Ui.Id("wtm", id), new RectangleF(c.X + 4, by, sw, 20), "-1")) TimerAdjust(-1);
            if (_ui.Button95(Ui.Id("wtr", id), new RectangleF(c.X + 7 + sw, by, c.Width - 14 - 2 * sw, 20), "Reset", !idle)) TimerReset();
            if (_ui.Button95(Ui.Id("wtq", id), new RectangleF(c.Right - 4 - sw, by, sw, 20), "+1")) TimerAdjust(1);
            return;
        }
        Value95(c, txt, _timer switch { TimerState.Running => "running", TimerState.Paused => "paused", TimerState.Done => "done", _ => "timer" }, Blk95);
    }

    void Stopwatch95(RectangleF c, WatchWidget w, int id)
    {
        double t = StopwatchTime(Clock.Now);
        string txt = StopwatchText(t, w.W >= 2);
        if (w.W >= 2)
        {
            _ui.Text95(txt, new RectangleF(c.X + 4, c.Y + 3, c.Width - 8, 18), Blk95, true, TextAlignment.Leading, size: 16);
            float bw = MathF.Floor((c.Width - 10) / 2), by = c.Bottom - 22;
            if (_ui.Button95(Ui.Id("wsp", id), new RectangleF(c.X + 2, by, bw, 19), _swRunning ? "Stop" : "Start", true, true)) StopwatchTap();
            if (_ui.Button95(Ui.Id("wsr", id), new RectangleF(c.X + 6 + bw, by, bw, 19), "Reset", t > 0 && !_swRunning)) StopwatchReset();
            return;
        }
        Value95(c, txt, _swRunning ? "running" : _ui.Width95("stopwatch") <= c.Width - 2 ? "stopwatch" : "watch", Blk95);
    }

    // Volume Control: a slider and Mute
    void Volume95(RectangleF c, WatchWidget w, int id)
    {
        float lvl = Volume.Level;
        bool ok = lvl >= 0;
        if (w.W == 1)
        {
            Value95(c, ok ? (Volume.Muted ? "Mute" : Pct(ShownPct(lvl))) : "--", "volume", Blk95);      // (the "0" format's rounding, made once)
            return;
        }
        float v = ok ? lvl : 0;
        if (_ui.Track95(Ui.Id("wvt", id), new RectangleF(c.X + 2, c.Y, c.Width - 4, 28), ref v, 0, 1, ok && !Volume.Muted, 6)) Volume.Set(v);
        bool mute = Volume.Muted;
        if (_ui.Check95(Ui.Id("wvk", id), c.X + 4, c.Bottom - 16, "Mute", ref mute, ok)) Volume.ToggleMute();
        _ui.Text95(ok ? Pct(ShownPct(lvl)) : "--", new RectangleF(c.Right - 36, c.Bottom - 17, 32, 13), Blk95, false, TextAlignment.Trailing);
    }

    void Battery95(RectangleF c, PanelData d, WatchWidget w)
    {
        var bats = d.Vr.Batteries;
        if (bats.Count == 0)
        {
            _ui.Text95(Fit(d.VrConnected ? "No battery info" : "SteamVR is off", c.Width - 4), c, Gry95, false, TextAlignment.Center);
            return;
        }
        if (w.W == 1 && w.H == 1)
        {
            var low = bats[0];
            foreach (var b in bats) if (b.pct < low.pct) low = b;
            float y = MathF.Floor(c.Y + (c.Height - 28) / 2);
            _ui.Text95(Bat(low.label), new RectangleF(c.X, y, c.Width, 13), Blk95, false, TextAlignment.Center);
            _ui.Text95(Pct(ShownPct(low.pct)), new RectangleF(c.X, y + 14, c.Width, 15), low.pct > 0.2f ? Blk95 : 0xFF800000, true,
                TextAlignment.Center, size: 13);
            return;
        }
        var pick = PickBatteries(bats, Math.Min(4, (int)(c.Height / 13)));
        float y0 = MathF.Floor(c.Y + (c.Height - pick.Count * 13) / 2);
        for (int i = 0; i < pick.Count; i++)
        {
            var b = bats[pick[i]];
            float y = y0 + i * 13;
            _ui.Text95(Bat(b.label), new RectangleF(c.X + 2, y, 34, 12));
            var bar = new RectangleF(c.X + 36, y + 1, c.Width - 36 - 32, 10);
            bool low = b.pct <= 0.2f && !b.charging;
            if (bar.Width >= 12) _ui.Progress95(bar, b.pct, low ? 0xFF800000 : Theme.W95Navy);
            _ui.Text95(Pct(ShownPct(b.pct)), new RectangleF(c.Right - 32, y, 30, 12), low ? 0xFF800000 : Blk95, true, TextAlignment.Trailing);
        }
    }

    // the dock in a little 95 window: the key faces shrunk (nearest), where it is beside them; a click opens it
    void Dock95Widget(RectangleF r, RectangleF c, PanelData d, WatchWidget w, int id)
    {
        if (_ui.Clickable(Ui.Id("wdk", id), r, out _, out _)) OpenDetail(w);
        if (!DockShows(d))
        {
            // still connecting: busy, the hourglass where the dock's picture is
            var ic = d.Dock?.State == HexpadLink.LinkState.Connecting ? W95Icons.Id.Hourglass : W95Icons.Id.Dock;
            W95Icons.Draw(_p, ic, MathF.Floor(c.X + (c.Width - 16) / 2), MathF.Floor(c.Y + (c.Height - 30) / 2));
            string st = DockState(d);
            if (st.Length == 0 || _ui.Width95(st) > c.Width - 4) st = "Hexpad";       // a 1x1 says just what it is
            _ui.Text95(Fit(st, c.Width - 4), new RectangleF(c.X, MathF.Floor(c.Y + (c.Height - 30) / 2) + 17, c.Width, 13),
                Blk95, false, TextAlignment.Center);
            return;
        }
        var s = d.Dock;
        if (w.W >= 2 && w.H == 1)
        {
            float gh = MathF.Min(c.Height - 6, 30), gw = MathF.Floor((gh - 2) / 2) * 3 + 4;
            var g = DockGrid(new RectangleF(c.X + 3, c.Y + 3, gw, gh), d, 2, 0, true);
            float tx = g.Right + 5, tw = c.Right - 3 - tx;
            _ui.Text95(Fit(DockWhere(s), tw), new RectangleF(tx, c.Y + c.Height / 2 - 14, tw, 13));
            _ui.Text95(Fit(s.Ambient ? "Idle" : s.InFolder ? "Folder" : PageOf95(s.Page + 1, s.Pages), tw), new RectangleF(tx, c.Y + c.Height / 2 + 1, tw, 13), Gry95);
            return;
        }
        DockGrid(RectangleF.Inflate(c, -3, -3), d, 2, 0, true);
    }

    // "2 of 3" (the dock's page), made again only for other numbers
    string _pageOf;
    (int page, int pages, int lang) _pageOfFor = (-1, -1, -1);

    string PageOf95(int page, int pages)
    {
        if (_pageOf == null || (page, pages, Lang.Version) != _pageOfFor) { _pageOfFor = (page, pages, Lang.Version); _pageOf = Lang.F("{0} of {1}", page, pages); }
        return _pageOf;
    }

    // the CD Player's line, "title - artist", made again only when the song changes
    string _cdLine, _cdTitle, _cdArtist;

    string CdLine95(string title, string artist)
    {
        if (string.IsNullOrEmpty(artist)) return title;
        if (_cdLine == null || !ReferenceEquals(title, _cdTitle) || !ReferenceEquals(artist, _cdArtist)) { _cdTitle = title; _cdArtist = artist; _cdLine = $"{title} - {artist}"; }
        return _cdLine;
    }

    // the CD Player: green on black, |<<  >  >>|, and the cover when the window is tall
    void Media95Widget(RectangleF c, PanelData d, WatchWidget w, int id)
    {
        var (has, title, artist, playing) = MediaNow(d);
        float x = c.X;
        if (w.H >= 2 && c.Height >= 48)
        {
            var box = new RectangleF(c.X + 1, c.Y + 1, 36, 36);
            var inner = RectangleF.Inflate(box, -2, -2);
            _p.Fill(inner, Wht95);
            if (_art != null) _p.Image(_art, inner);
            else W95Icons.Draw(_p, W95Icons.Id.Note, inner.X + 8, inner.Y + 8);
            _p.Bevel(box, Painter.Edge.Sunken);
            x = box.Right + 4;
        }
        var lcd = new RectangleF(x, c.Y + 1, c.Right - x - 1, 18);
        _p.Fill(lcd, Blk95);
        _p.Bevel(lcd, Painter.Edge.Sunken);
        string line = has ? CdLine95(title, artist) : "Nothing playing";
        _ui.Text95(Fit(line, lcd.Width - 8), new RectangleF(lcd.X + 4, lcd.Y + 2, lcd.Width - 6, 14), has ? 0xFF00FF00 : 0xFF00A000);
        var ids = MediaIds;
        var glyphs = MediaGlyphs(playing);
        float by = lcd.Bottom + 3;
        if (by + 18 > c.Bottom + 1) return;
        int nb = lcd.Width >= 86 ? 3 : 1;
        float bx = MathF.Floor(lcd.X + (lcd.Width - (nb * 28 - 2)) / 2);
        for (int j = 0; j < nb; j++)
        {
            int a = nb == 1 ? 1 : j;
            if (_ui.GlyphButton95(Ui.Id("wcm", id * 3 + a), new RectangleF(bx + j * 28, by, 26, 18), glyphs[a], has)) Act(ids[a]);
        }
        if (w.H >= 2 && c.Width >= 150 && c.Bottom - (by + 24) >= 30)
            Volume95Row(new RectangleF(c.X + 4, by + 24, c.Width - 8, 30), Ui.Id("wcv", id));
    }

    void Button95Widget(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d)
    {
        bool click;
        if (k.Id == "layout")
            click = _ui.Button95(Ui.Id("wcb", id), r, Ui.Esc95(Fit(d.LayoutName, r.Width - 8)), true, false, false);
        else
        {
            var icon = k.Id == "desktops" && !d.DesktopsVisible ? W95Icons.Id.ComputerOff : k.Icon95;
            click = _ui.Button95(Ui.Id("wcb", id), r, w.W >= 2 ? Ui.Esc95(Fit(k.Name, r.Width - 30)) : null, true, false, IsOn(k.Id, d), icon);
        }
        if (click) Act(k.Id);
    }
}
