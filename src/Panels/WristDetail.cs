using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// A widget of the custom face, opened: tap a widget's card and it grows over the others (modern: a springy
// zoom over a dimmed watch; 95: the window "zoom" outline, then a 95 dialog) with more to see and to control.
// A tap beside it (modern), its X or OK (95) closes it again. The widget's own small buttons still act directly.
public sealed partial class WristPanel
{
    WatchWidget _open;                  // the widget shown big (null = none)
    bool _closing;
    double _openStart = -10;
    int _calOffset;                     // months browsed away from today in the opened calendar
    readonly List<double> _laps = new();
    const string OpenKey = "wopen";
    public bool LayoutTest;             // --snapshot: no dimming, no clip (whatever sticks out of the card shows)

    // which opened view a widget has (null = it has none: the buttons just act)
    static string DetailOf(string type) => type switch
    {
        "media" => "media",
        "battery" => "battery",
        "time" or "date" or "analog" or "session" => "clock",
        "calendar" => "calendar",
        "timer" => "timer",
        "stopwatch" => "stopwatch",
        "perf" or "graph" or "fps" or "reproj" or "gpu" or "vram" => "perf",
        "cpu" or "ram" => "system",
        "volume" => "volume",
        "minesweeper" => "mines",
        "room" => "room",
        "dock" => "dock",
        "mixer" => "mixer",
        "notifications" => "notifications",
        "photo" => "photo",
        "weather" => "weather",
        "folder" => "folder",
        "friends" => "friends",
        "recap" => "recap",
        "light" => "light",
        "rgb" => "rgb",
        "memory" => "memory",
        "temps" => "temps",
        "avatar" => "avatar",
        "mixtape" => "mixtape",
        _ => null,
    };

    bool DetailIs(string kind) => _open != null && DetailOf(_open.Type) == kind;

    void OpenDetail(WatchWidget w)
    {
        _folder = FolderUnder(w);           // a widget of the open folder: the folder stays under its view (WristFolder.cs)
        _open = w;
        _closing = false;
        _openStart = Clock.Now;
        _calOffset = 0;
        _roomScroll = 0;
        NotesFromTop();
        PhotoFromNewest();
        SocialFromTop();
        DockLetGo();
        _ui.SpringFrom(OpenKey, 0);
        _dirty = true;
    }

    void CloseDetail()
    {
        // modern: it shrinks back into its card; the retro styles: every window zooms back into its widget as it opened - the 1995
        // zoom outline the other way round, or the style's own (RetroSkin.WindowZoom; LiveZoom: 7's window fading out) (Motion off:
        // gone at once)
        DockLetGo();
        if (Theme.Classic && Motion.Any) { _closing = true; _openStart = Clock.Now; }
        else if (Theme.Classic) _open = null;
        else _closing = true;
        _dirty = true;
    }

    // --snapshot: show the first widget of this type opened, at rest
    public bool OpenForTest(string type)
    {
        var w = WatchPages.Widgets(C, CurPage)?.Find(x => x?.Type == type);
        if (w == null || DetailOf(type) == null) return false;        // e.g. the dock: a tap opens its own panel
        _open = w;
        _closing = false;
        _openStart = -10;
        _roomScroll = 0;
        NotesFromTop();
        PhotoFromNewest();
        SocialFromTop();
        _ui.SpringFrom(OpenKey, 1);
        return true;
    }

    public void CloseForTest() { _open = null; _closing = false; }

    public bool IsOpen => _open != null && !_closing;

    // the card under a widget opens it; registered before the widget's own controls, so those win
    void CardTap(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r)
    {
        if (k.Button || DetailOf(k.Id) == null) return;
        if (_ui.Clickable(Ui.Id("wco", id), r, out _, out _)) OpenDetail(w);
    }

    static RectangleF Lerp(RectangleF a, RectangleF b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);

    // ================= Modern =================

    // Every opened view draws in the cards' colours: the accent for the frame numbers, the timer and the volume, purple for the
    // CPU, blue for memory, Bento.Track under every bar and slider, Ok / Warn / Bad from the same levels as the cards (the value's
    // text too); small headings are the cards' UPPERCASE 15 px captions, other text 17 px and up.
    void DrawDetail(PanelData d, List<WatchWidget> list)
    {
        if (_open == null) return;
        list = FolderBehind(d, list);       // opened from a folder: the folder open under it, its widgets the list (WristFolder.cs)
        var k = list.Contains(_open) ? WatchWidgets.Find(_open.Type) : null;
        string kind = k == null ? null : DetailOf(k.Id);
        if (kind == null) { _open = null; return; }
        float e = _ui.Spring(OpenKey, _closing ? 0 : 1, 240, 24);
        if (_closing && e < 0.03f) { _open = FolderBack(); _closing = false; return; }
        if (kind == "folder") { FolderView(d, k, e); return; }          // the folder open: its widgets live (WristFolder.cs)
        float ec = Math.Clamp(e, 0, 1);
        var all = new RectangleF(0, 0, TW, TH);
        if (!LayoutTest) _p.Fill(all, Theme.WithAlpha(0xFF000000, 0.62f * ec), Bento.PageRadius);
        if (_ui.Clickable("wscrim", all, out _, out _) && !_closing) CloseDetail();        // a tap beside it
        var target = WatchWidgets.CellRect(0, 0, WatchWidgets.Cols, WatchWidgets.Rows, false);
        var r = Lerp(WatchWidgets.Rect(_open, false, _grid), target, e);
        _p.Fill(r, Bento.Card, CardRadius);
        _p.Stroke(RectangleF.Inflate(r, -0.6f, -0.6f), Bento.Edge, Bento.EdgeWidth, CardRadius - 0.6f);
        if (Theme.Cyber) CyberUnfold(WatchWidgets.Rect(_open, false, _grid), target, e);     // brackets leading it (WristCyberFx.cs)
        _ui.Clickable("wcard", r, out _, out _);           // the card itself is not "beside"
        // Minesweeper grows out of its tile: its game is in the card from the start, scaled with it (and back on closing) - the
        // same widget getting bigger; playable once it is big
        bool grow = kind is "mines" or "room" or "dock";           // the room too: its card grows into the list of who is there
        float ca = grow ? Math.Clamp(e * 3, 0, 1) : Math.Clamp((e - 0.5f) / 0.45f, 0, 1);
        if (ca <= 0.01f) return;
        _halo = Bento.Card;                    // the opened card is flat
        _haloCard = RectangleF.Empty;
        // a widget with a colour of its own keeps it as the accent of its opened view (the card stays the default one)
        bool own = _open.Color.HasValue && !Theme.Cyber;
        if (own) Bento.Use(LookOf(0, _open.Color.Value | 0xFF000000, 0, true, false, false));
        if (!LayoutTest) _p.PushClip(r);
        if (ca < 0.999f) _p.PushFade(ca);
        // the content of a view that does not grow with its card fades in after the card and rises the last few px into place
        bool rise = !grow && ca < 0.999f;
        if (rise) _p.PushOffset(0, 14 * (1 - ca) * (1 - ca));
        float gs = grow ? MathF.Min(r.Width / target.Width, r.Height / target.Height) : 1;
        bool zoom = MathF.Abs(gs - 1) > 0.002f, inert = _ui.Inert;
        if (zoom)
        {
            // the scale that puts the target's centre on the card's: about the point c with c + gs (target centre - c) = card centre
            float tx = target.X + target.Width / 2, ty = target.Y + target.Height / 2, rx = r.X + r.Width / 2, ry = r.Y + r.Height / 2;
            _p.PushScale(gs, (rx - gs * tx) / (1 - gs), (ry - gs * ty) / (1 - gs));
        }
        if (grow && (_closing || MathF.Abs(e - 1) > 0.02f)) _ui.Inert = true;
        var E = target;
        if (kind is not ("mines" or "room" or "dock" or "mixtape") && Theme.Cyber)
            // Cyber: a yellow tag (it types in: WristCyberFx.cs) - the clock's CLOCK (its time card has a TIME tag of its own), the
            // notifications' INCOMING
            CyberDetailTag(E, Upper(kind switch { "clock" => "Clock", "notifications" => "Incoming", "photo" => "Capture", _ => k.Name }));
        else if (kind is not ("mines" or "room" or "dock" or "weather" or "mixtape"))      // (Mixtape: its name and its tabs - WristMixtape.cs)     // Minesweeper's board takes the left: its name is over its own column; the room: its world; the weather: its place
            _p.Text(Ellipsize(k.Name, E.Width - 120, 24, FontWeight.SemiBold), new RectangleF(E.X + 24, E.Y + 16, E.Width - 120, 40), Bento.Text, 24,
                FontWeight.SemiBold);
        if (Press("wclose", new RectangleF(E.Right - 22 - 46, E.Y + 14, 46, 46), "\uE711", null, false, !_closing, 18, true))
            CloseDetail();
        var c = new RectangleF(E.X + 24, E.Y + 74, E.Width - 48, E.Height - 96);
        var now = Now;
        switch (kind)
        {
            case "media": MediaDetail(c, d); break;
            case "battery": BatteryDetail(c, d); break;
            case "clock": ClockDetail(c, d, now); break;
            case "calendar": CalendarDetail(c, now); break;
            case "timer": TimerDetail(c); break;
            case "stopwatch": StopwatchDetail(c); break;
            case "perf": PerfDetail(c, d); break;
            case "system": SystemDetail(c, d); break;
            case "volume": VolumeDetail(c); break;
            case "mixer": MixerDetail(c); break;
            case "mines": MinesDetail(E); break;
            case "room": RoomDetail(E, d); break;
            case "dock": DockDetail(E, d); break;
            case "notifications": NotesDetail(E, d); break;
            case "photo": PhotoDetail(E, d); break;
            case "weather": WeatherDetail(E, d); break;
            case "friends": FriendsDetail(E, d); break;
            case "recap": RecapDetail(E, d); break;
            case "light": LightDetail(c, d); break;
            case "rgb": RgbDetail(c, d); break;
            case "memory": PhotoDetail(E, d); break;
            case "temps": TempsDetail(c, d); break;
            case "avatar": AvatarDetail(E, d); break;         // every button of the avatar (WristAvatar.cs)
            case "mixtape": MixtapeDetail(E, d); break;       // the whole player (WristMixtape.cs)
        }
        _ui.Inert = inert;
        if (zoom) _p.PopScale();
        if (rise) _p.PopScale();
        if (ca < 0.999f) _p.PopFade();
        if (!LayoutTest) _p.PopClip();
        if (own) Bento.Reset();
    }

    // a bar with a knob: drag along it (or tap) to set 0..1; its empty part the cards' track
    void Slider(string id, RectangleF bar, float value, uint col, bool enabled, Action<float> set)
    {
        _ui.Clickable(id, RectangleF.Inflate(bar, 8, 20), out bool hover, out bool held);
        if (held && enabled) set(Math.Clamp((_ui.Mouse.X - bar.X) / bar.Width, 0, 1));
        float v = Math.Clamp(value, 0, 1), h = bar.Height;
        if (Theme.Cyber) { CySlider(bar, v, col, enabled, hover || held); return; }      // segments and a mark (WristCyberDetail.cs)
        _p.Fill(bar, Bento.Track, h / 2);
        if (!enabled) return;
        float x = bar.X + bar.Width * v;
        _p.Fill(new RectangleF(bar.X, bar.Y, MathF.Max(h, x - bar.X), h), col, h / 2);
        _p.Circle(x, bar.Y + h / 2, h * 1.5f, _halo);
        _p.Circle(x, bar.Y + h / 2, h, col);
    }

    void MediaDetail(RectangleF c, PanelData d)
    {
        if (Theme.Cyber) { CyberMediaDetail(c, d); return; }      // the HUD's own (WristCyberDetail.cs)
        var (has, title, artist, playing) = MediaNow(d);
        Volume.Sample(Clock.Now);
        float s = MathF.Min(c.Height, c.Width * 0.44f);
        var cover = new RectangleF(c.X, c.Y + (c.Height - s) / 2, s, s);
        _p.Fill(cover, Bento.Tile, 20);
        if (_art != null)
        {
            _p.PushRoundedClip(cover, 20);
            _p.Image(_art, cover);
            _p.PopRoundedClip();
        }
        else _p.Icon("\uE8D6", cover, Sub, s * 0.36f);
        float x = cover.Right + 30, w = c.Right - x;
        if (has && _p.MeasureWidth(title, 30, FontWeight.SemiBold) <= w)
        {
            _p.Text(title, new RectangleF(x, c.Y + 6, w, 42), Bento.Text, 30, FontWeight.SemiBold);
            _p.Text(Ellipsize(artist ?? "", w, 21, FontWeight.Normal), new RectangleF(x, c.Y + 50, w, 32), Sub, 21);
        }
        else if (has)
        {
            // a long title takes two lines (a little smaller) before it is cut
            var (l1, l2) = TwoLines(title, w, 25, FontWeight.SemiBold);
            _p.Text(l1, new RectangleF(x, c.Y, w, 34), Bento.Text, 25, FontWeight.SemiBold);
            _p.Text(l2, new RectangleF(x, c.Y + 32, w, 34), Bento.Text, 25, FontWeight.SemiBold);
            _p.Text(Ellipsize(artist ?? "", w, 19, FontWeight.Normal), new RectangleF(x, c.Y + 68, w, 28), Sub, 19);
        }
        else _p.Text(NotPlaying, new RectangleF(x, c.Y + 6, w, 42), Sub, 26);
        float gap = 14, big = MathF.Min(84, (w - 2 * gap) * 0.4f), small = MathF.Min(64, (w - 2 * gap - big) / 2);
        float by = c.Y + 116, bx = x;
        if (Press("wdm0", new RectangleF(bx, by + (big - small) / 2, small, small), "\uE892", null, false, has, 24, true)) OnAction?.Invoke(MediaIds[0]);
        if (Press("wdm1", new RectangleF(bx + small + gap, by, big, big), playing ? PauseFill : PlayFill, null, true, has, 32, true))
            OnAction?.Invoke(MediaIds[1]);
        if (Press("wdm2", new RectangleF(bx + small + gap + big + gap, by + (big - small) / 2, small, small), "\uE893", null, false, has, 24, true))
            OnAction?.Invoke(MediaIds[2]);
        // the system volume under it (muted: the speaker red, like the card's mute disc)
        float lvl = Volume.Level;
        bool ok = lvl >= 0, muted = Volume.Muted;
        float vy = c.Bottom - 56;
        if (Press("wdmv", new RectangleF(x, vy, 52, 52), muted ? MuteGlyph : "\uE767", null, muted, ok, 20, true, accentFill: false, fill: Bento.Bad))
            Volume.ToggleMute();
        var bar = new RectangleF(x + 72, vy + 19, w - 72 - 22, 14);
        _p.Text(ok ? (muted ? "Muted" : Lang.F("Volume {0}", VolumePct(lvl))) : "No output device", new RectangleF(bar.X, vy - 26, bar.Width, 26),
            ok && muted ? Bento.Bad : Sub, SmallText, FontWeight.SemiBold);
        Slider("wdmb", bar, lvl, muted ? Bento.Faint : Bento.Accent, ok, Volume.Set);
    }

    // Every device as a row: its name and bolt the way the cards write them, the percentage big (amber / red when low), its line.
    // One device: a gauge.
    void BatteryDetail(RectangleF c, PanelData d)
    {
        var bats = d.Vr.Batteries;
        if (bats.Count == 0)
        {
            _p.Text(d.VrConnected ? "No battery information from SteamVR" : "SteamVR is not running", c, Sub, 20, FontWeight.Normal, TextAlignment.Center);
            return;
        }
        int n = Math.Min(bats.Count, 8);
        if (n == 1)
        {
            var b = bats[0];
            int shown = ShownPct(b.pct);
            var lv = BatteryLevel(shown);
            Gauge(c, b.pct, Bento.Level(lv, Bento.Ok), Ui.Id("wdb", 0), Pct(shown), Upper(Bat(b.label)), Bento.Level(lv, Bento.Text),
                unitIcon: b.charging ? Bolt : null);
            return;
        }
        // a row per device (two columns from five on): the name, the percentage big, the line under them
        int cols = n <= 4 ? 1 : 2, rows = (n + cols - 1) / cols;
        const float gapX = 36;
        float cw = (c.Width - (cols - 1) * gapX) / cols, ch = MathF.Min(96, c.Height / rows);
        float y0 = c.Y + (c.Height - rows * ch) / 2;
        float ts = BatteryRowSize(new SizeF(cw, ch), bats, n);
        for (int i = 0; i < n; i++)
            BatteryRow(new RectangleF(c.X + i % cols * (cw + gapX), y0 + i / cols * ch, cw, ch), bats[i], Ui.Id("wdb", i), ts);
    }

    void ClockDetail(RectangleF c, PanelData d, DateTime now)
    {
        if (Theme.Cyber) { CyberClockDetail(c, d, now); return; }      // the HUD's own (WristCyberDetail.cs)
        AnalogOrbit(new RectangleF(c.X - 8, c.Y - 8, c.Height + 16, c.Height + 16), now, Bento.Accent);
        float x = c.X + c.Height + 30, w = c.Right - x;
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        bool timing = _timer is TimerState.Running or TimerState.Paused;
        float blockH = d.VrSince != null ? (timing ? 296 : 230) : timing ? 230 : 152;
        float top = c.Y + MathF.Max(0, (c.Height - blockH) / 2);
        string t = Clk(now, h24 ? "HH:mm:ss" : "h:mm:ss", culture);
        float size = FitSize(t, w - (h24 ? 0 : 50), 66, FontWeight.SemiBold, Theme.Display);
        RollText("wdct", t, new RectangleF(x, top, w, 84), Bento.Text, size, FontWeight.SemiBold, Theme.Display, TextAlignment.Leading);
        if (!h24)
            _p.Text(now.Hour < 12 ? "AM" : "PM", new RectangleF(x + _p.MeasureWidth(t, size, FontWeight.SemiBold, Theme.Display) + 8, top + 44, 60, 30), Sub, 20,
                FontWeight.SemiBold);
        Words(now);
        // the face draws its text unclipped: every line here is cut to the column
        // a long date ("Október 15., csütörtök") gets smaller to fit whole, down to the 17 px floor, before anything is cut
        float ds = MathF.Max(SmallText, FitSize(_dateLine, w, 22, FontWeight.SemiBold, null));
        _p.Text(Ellipsize(_dateLine, w, ds, FontWeight.SemiBold), new RectangleF(x, top + 92, w, 32), Bento.Text, ds, FontWeight.SemiBold);
        _p.Text(Ellipsize(Lang.F("Week {0} · day {1} of {2}", ISOWeek.GetWeekOfYear(now), now.DayOfYear, DateTime.IsLeapYear(now.Year) ? 366 : 365), w, SmallText,
            FontWeight.Normal), new RectangleF(x, top + 126, w, 26), Sub, SmallText);
        float y = top + 176;
        if (d.VrSince is { } since)
        {
            CapText("IN VR", x, y + 16, w, Sub);
            _p.Text(Ellipsize($"{SessionText(d)}  ·  {Lang.F("since {0}", ShortClock(since))}", w, 22, FontWeight.SemiBold), new RectangleF(x, y + 22, w, 32),
                Bento.Text, 22, FontWeight.SemiBold);
            y += 66;
        }
        if (timing)
        {
            CapText("TIMER", x, y + 16, w, Sub);
            _p.Text(Ellipsize(Lang.F(_timer == TimerState.Paused ? "{0} left (paused)" : "{0} left", Clock2(TimerLeft(Clock.Now))), w, 22, FontWeight.SemiBold),
                new RectangleF(x, y + 22, w, 32), Bento.Accent, 22, FontWeight.SemiBold);
        }
    }

    void CalendarDetail(RectangleF c, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        var month = new DateTime(now.Year, now.Month, 1).AddMonths(_calOffset);
        string ml = Capital(month.ToString("MMMM yyyy", culture));
        float mw = _p.MeasureWidth(ml, 24, FontWeight.SemiBold);
        _p.Text(ml, new RectangleF(c.X + 56, c.Y - 4, mw + 8, 36), Bento.Text, 24, FontWeight.SemiBold);
        if (Press("wdcp", new RectangleF(c.X, c.Y - 6, 44, 44), "\uE76B", null, false, true, 18, true)) { _calOffset--; _dirty = true; }
        if (Press("wdcn", new RectangleF(c.X + 56 + mw + 12, c.Y - 6, 44, 44), "\uE76C", null, false, true, 18, true)) { _calOffset++; _dirty = true; }
        var g = new RectangleF(c.X, c.Y + 46, c.Width, c.Height - 46);
        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        int weeks = ((((int)month.DayOfWeek - first + 7) % 7) + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7;
        float cw = g.Width / 7, rh = g.Height / (weeks + 1);
        for (int col = 0; col < 7; col++)
        {
            // the weekday heads as the cards write them: UPPERCASE, grey, 15 px
            var dow = (DayOfWeek)((first + col) % 7);
            _p.Text(Upper(culture.DateTimeFormat.GetAbbreviatedDayName(dow)), new RectangleF(g.X + col * cw, g.Y, cw, rh), Sub, CapSize, FontWeight.SemiBold,
                TextAlignment.Center);
        }
        int offset = ((int)month.DayOfWeek - first + 7) % 7, days = DateTime.DaysInMonth(month.Year, month.Month);
        bool thisMonth = month.Year == now.Year && month.Month == now.Month;
        for (int dd = 1; dd <= days; dd++)
        {
            int cell = offset + dd - 1;
            var cr = new RectangleF(g.X + cell % 7 * cw, g.Y + (cell / 7 + 1) * rh, cw, rh);
            var dow = (DayOfWeek)((first + cell % 7) % 7);
            bool today = thisMonth && dd == now.Day, weekend = dow is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (today)
            {
                float d2 = MathF.Min(cw, rh) - 4;
                _p.Fill(new RectangleF(cr.X + (cw - d2) / 2, cr.Y + (rh - d2) / 2, d2, d2), Bento.AccentFill, d2 / 2);
            }
            _p.Text(DayNumber(dd), cr, today ? Bento.OnAccent : weekend ? Sub : Bento.Text, 19, today ? FontWeight.Bold : FontWeight.Normal,
                TextAlignment.Center);
        }
    }

    void TimerPreset(int minutes)
    {
        _timerTotal = minutes * 60;
        _timerEnd = Clock.Now + _timerTotal;
        _timer = TimerState.Running;
        _dirty = true;
    }

    static readonly int[] Presets = { 1, 3, 5, 10, 15, 30 };
    static readonly string[] PresetText = { "1 min", "3 min", "5 min", "10 min", "15 min", "30 min" };

    void TimerDetail(RectangleF c)
    {
        double left = TimerLeft(Clock.Now);
        float frac = _timer == TimerState.Idle ? 1 : (float)(left / Math.Max(1, Math.Max(_timerTotal, left)));
        bool done = _timer == TimerState.Done;
        uint tint = done ? Bento.Warn : Bento.Accent;
        uint vc = done ? Bento.Warn : _timer == TimerState.Paused ? Sub : Bento.Text;      // as on the card
        // the time big over its line, the state under it; the presets and the controls on the right
        float rad = c.Height / 2 - 8, cx = c.X + rad + 8;
        string txt = done ? "Done" : Clock2(left);
        string state = _timer switch { TimerState.Running => "running", TimerState.Paused => "paused", TimerState.Done => "time is up", _ => Lang.F("{0} min", _timerTotal / 60) };
        Gauge(new RectangleF(c.X, c.Y, 2 * rad + 16, c.Height), frac, tint, "wdtr", txt, state, vc, 0, "wdtx");
        // quick starts, then - / start-pause / + / reset
        float x = cx + rad + 36, w = c.Right - x;
        float bigC = MathF.Min(64, (w - 3 * 8) * 0.31f), y0 = c.Y + MathF.Max(0, (c.Height - (30 + 102 + 24 + bigC)) / 2);
        CapText("START A TIMER", x, y0 + 17, w, Sub);
        float pw = (w - 2 * 10) / 3;
        for (int i = 0; i < Presets.Length; i++)
            if (Press(Ui.Id("wdtp", i), new RectangleF(x + i % 3 * (pw + 10), y0 + 30 + i / 3 * 56, pw, 46), null, PresetText[i], false))
                TimerPreset(Presets[i]);
        // start-pause first and biggest (amber once the time is up), then reset, -1 min, +1 min - all within the column
        bool idle = _timer == TimerState.Idle;
        float big = bigC, small = MathF.Min(50, (w - 3 * 8 - big) / 3), by = y0 + 30 + 102 + 24;
        float sy = by + (big - small) / 2;
        if (Press("wdtg", new RectangleF(x, by, big, big), _timer == TimerState.Running ? PauseFill : PlayFill, null, true, true, 26, true,
                accentFill: !done, fill: Bento.Warn))
            TimerTap();
        if (Press("wdtr0", new RectangleF(x + big + 8, sy, small, small), "\uE72C", null, false, !idle, 18, true)) TimerReset();
        if (Press("wdtm", new RectangleF(x + big + small + 16, sy, small, small), "\uE738", null, false, true, 18, true)) TimerAdjust(-1);
        if (Press("wdtq", new RectangleF(x + big + 2 * small + 24, sy, small, small), "\uE710", null, false, true, 18, true)) TimerAdjust(1);
    }

    void StopwatchDetail(RectangleF c)
    {
        double now = Clock.Now, t = StopwatchTime(now);
        string txt = StopwatchText(t, true);
        // white while it runs, grey when stopped (as on the card)
        _p.Text(txt, new RectangleF(c.X, c.Y - 6, c.Width - 190, 90), _swRunning ? Bento.Text : Sub,
            FitSize(txt, c.Width - 200, 72, FontWeight.SemiBold, Theme.Display), FontWeight.SemiBold, TextAlignment.Leading, ParagraphAlignment.Center, Theme.Display);
        float bs = 72, bx = c.Right - 2 * bs - 14;
        bool lap = _swRunning;
        if (Press("wdsl", new RectangleF(bx, c.Y + 4, bs, bs), lap ? "\uE7C1" : "\uE72C", null, false, _swRunning || t > 0, 24, true))
        {
            if (lap) _laps.Add(t);
            else { StopwatchReset(); _laps.Clear(); }
        }
        if (Press("wdsp", new RectangleF(bx + bs + 14, c.Y + 4, bs, bs), _swRunning ? PauseFill : PlayFill, null, true, true, 28, true))
            StopwatchTap();
        // the laps, newest first
        float y = c.Y + 100;
        CapText("LAP", c.X, y + 17, c.Width, Sub);
        CapText("SPLIT", c.X + c.Width / 2, y + 17, c.Width / 2, Sub, TextAlignment.Trailing);
        CapText("TOTAL", c.Right, y + 17, c.Width, Sub, TextAlignment.Trailing);
        _p.Fill(new RectangleF(c.X, y + 28, c.Width, 1), Theme.WithAlpha(Theme.Border, 0.9f));
        y += 34;
        if (_laps.Count == 0)
            _p.Text(_swRunning ? "Tap the flag for a lap time" : "Laps appear here", new RectangleF(c.X, y, c.Width, c.Bottom - y), Sub, 18,
                FontWeight.Normal, TextAlignment.Center);
        int maxRows = (int)((c.Bottom - y) / 34);
        for (int i = _laps.Count - 1, row = 0; i >= 0 && row < maxRows; i--, row++)
        {
            double split = _laps[i] - (i > 0 ? _laps[i - 1] : 0);
            var rr = new RectangleF(c.X, y + row * 34, c.Width, 30);
            _p.Fill(new RectangleF(rr.X, rr.Bottom, rr.Width, 1), Theme.WithAlpha(Theme.Border, 0.8f));
            _p.Text(Lang.F("Lap {0}", i + 1), rr, Sub, 18);
            _p.Text(StopwatchText(split, true), new RectangleF(rr.X, rr.Y, rr.Width / 2, rr.Height), Bento.Text, 20, FontWeight.SemiBold, TextAlignment.Trailing);
            _p.Text(StopwatchText(_laps[i], true), rr, Sub, 18, FontWeight.Normal, TextAlignment.Trailing);
        }
    }

    // the four frame numbers as gauges in their role colours (amber / red when they need attention), the frames under them
    void PerfDetail(RectangleF c, PanelData d)
    {
        if (Theme.Cyber) { CyberPerfDetail(c, d); return; }       // the HUD's own (WristCyberDetail.cs)
        float gw = c.Width / 4;
        for (int j = 0; j < 4; j++)
        {
            string id = PerfIds[j];
            var (f, val, unit, cap, lv) = Reading(id, d);
            Gauge(new RectangleF(c.X + j * gw, c.Y - 6, gw, 128), f, Bento.Level(lv, RoleColor(id)), Ui.Id("wdp", j), val, unit,
                val == "–" ? Bento.Faint : Bento.Level(lv, Bento.Text));
            string label = id switch { "gpu" => "GPU " + cap, "vram" => "VRAM " + cap, _ => cap };
            _p.Text(Ellipsize(label, gw - 8, SmallText, FontWeight.Normal), new RectangleF(c.X + j * gw, c.Y + 118, gw, 24), Sub, SmallText, FontWeight.Normal,
                TextAlignment.Center);
        }
        var g = new RectangleF(c.X - 10, c.Y + 150, c.Width + 20, c.Height - 150);
        GraphPlain(d.Vr, g, Bento.Accent);
        // the frames behind the graph: average, worst, how many dropped
        var v = d.Vr;
        int n = v.FrameCount, dropped = 0;
        float sum = 0, worst = 0;
        for (int i = 0; i < n; i++)
        {
            int idx = (v.FrameHead - n + i + VrStats.FrameHistory) % VrStats.FrameHistory;
            float ms = v.FrameMs[idx];
            sum += ms;
            worst = MathF.Max(worst, ms);
            if (v.FrameFlag[idx] == 2) dropped++;
        }
        if (n > 0)
            _p.Text(Ellipsize(Lang.F("avg {0:0.0} ms · worst {1:0.0} ms · {2} dropped", sum / n, worst, dropped),
                    g.Width - 32 - _p.MeasureWidth(FrameNote(v).text, SmallText, FontWeight.SemiBold) - 16, SmallText, FontWeight.Normal),
                new RectangleF(g.X + 16, g.Y + 6, g.Width - 32, 24), Sub, SmallText, FontWeight.Normal, TextAlignment.Trailing);
    }

    // the CPU (purple) and the memory (blue): a gauge each with its facts beside it, the last minute of CPU load under them
    void SystemDetail(RectangleF c, PanelData d)
    {
        if (Theme.Cyber) { CyberSystemDetail(c, d); return; }     // the HUD's own (WristCyberDetail.cs)
        var (cf, cv, _, _, cl) = Reading("cpu", d);
        var (rf, rv, ru, _, rl) = Reading("ram", d);
        // the opened widget's own colour only for its own reading: the other keeps its role colour (two gauges in one
        // colour would look like one thing)
        bool ramOpen = _open?.Type == "ram";
        uint cpuC = ramOpen ? Bento.DataPurple : RoleColor("cpu"), ramC = ramOpen ? RoleColor("ram") : Bento.DataBlue;
        float half = c.Width / 2, gs = 140, tx = gs + 6, tw = half - tx - 10;
        // a heading (a card's caption), one big fact and a smaller one beside each gauge
        void Facts(float x, string head, string big, string small)
        {
            CapText(head, x, c.Y + 33, tw, Sub);
            _p.Text(big, new RectangleF(x, c.Y + 40, tw, 36), Bento.Text, MathF.Max(SmallText, FitSize(big, tw, 26, FontWeight.SemiBold, Theme.Display)),
                FontWeight.SemiBold, TextAlignment.Leading, ParagraphAlignment.Center, Theme.Display);
            _p.Text(Ellipsize(small, tw, SmallText, FontWeight.Normal), new RectangleF(x, c.Y + 78, tw, 24), Sub, SmallText);
        }
        Gauge(new RectangleF(c.X, c.Y - 6, gs, gs), cf, Bento.Level(cl, cpuC), "wdsc", cv, "", cv == "–" ? Bento.Faint : Bento.Level(cl, Bento.Text));
        // the gauge already shows the load now: beside it, the last minute's average and peak
        float avg = 0, peak = 0;
        for (int i = 0; i < Stats.CpuCount; i++)
        {
            float v = Stats.CpuHistory[(Stats.CpuHead - Stats.CpuCount + i + SystemStats.History) % SystemStats.History];
            avg += v / Stats.CpuCount;
            peak = MathF.Max(peak, v);
        }
        bool any = Stats.CpuCount > 0;
        Facts(c.X + tx, "CPU", any ? Lang.F("avg {0:0}%", avg * 100) : "–", any ? Lang.F("peak {0:0}%", peak * 100) : "");
        Gauge(new RectangleF(c.X + half, c.Y - 6, gs, gs), rf, Bento.Level(rl, ramC), "wdsr", rv, ru, rv == "–" ? Bento.Faint : Bento.Level(rl, Bento.Text));
        float total = Stats.RamTotalGB, used = Stats.RamUsedGB;
        bool ram = total > 0 && used >= 0;
        Facts(c.X + half + tx, "MEMORY", ram ? Lang.F("{0:0.0} GB free", total - used) : "–", ram ? Lang.F("of {0:0} GB", total) : "");
        // the last minute of CPU load, in the CPU's purple (amber / red where it was high, the card's thresholds)
        var g = new RectangleF(c.X, c.Y + 150, c.Width, c.Height - 150);
        CapText("CPU, LAST MINUTE", g.X, g.Y + 16, g.Width, Sub);
        var b = new RectangleF(g.X, g.Y + 28, g.Width, g.Height - 28);
        _p.Fill(new RectangleF(b.X, b.Bottom, b.Width, 1), Theme.WithAlpha(Theme.Border, 0.9f));
        float step = b.Width / SystemStats.History;
        for (int i = 0; i < Stats.CpuCount; i++)
        {
            int idx = (Stats.CpuHead - Stats.CpuCount + i + SystemStats.History) % SystemStats.History;
            float v = Stats.CpuHistory[idx], h = MathF.Max(2, v * b.Height);
            float x = b.Right - (Stats.CpuCount - i) * step;
            _p.Fill(new RectangleF(x + 1, b.Bottom - h, MathF.Max(2, step - 3), h), Bento.Level(CpuLevel(v), cpuC), 2);
        }
    }

    void VolumeDetail(RectangleF c)
    {
        Volume.Sample(Clock.Now);
        float lvl = Volume.Level;
        bool ok = lvl >= 0, muted = Volume.Muted;
        uint col = muted ? Bento.Faint : Bento.Accent;
        var left = new RectangleF(c.X, c.Y, c.Height + 16, c.Height);
        string v = ok ? (muted ? "Muted" : VolumePct(lvl)) : "–";
        _p.Text(v, new RectangleF(left.X, left.Y + left.Height * 0.12f, left.Width, left.Height * 0.42f), !ok ? Bento.Faint : muted ? Bento.Bad : Bento.Text,
            FitSize(v, left.Width - 48, 96, FontWeight.SemiBold, Theme.Display), FontWeight.SemiBold, TextAlignment.Center, ParagraphAlignment.Center, Theme.Display);
        Slider("wdvs", new RectangleF(left.X + 28, left.Y + left.Height * 0.6f, left.Width - 56, 14), lvl, col, ok, Volume.Set);
        _p.Text("drag to set", new RectangleF(left.X, left.Y + left.Height * 0.6f + 30, left.Width, 30), Sub, SmallText, FontWeight.Normal, TextAlignment.Center);
        float x = c.X + c.Height + 36, w = c.Right - x;
        CapText("SYSTEM VOLUME", x, c.Y + 29, w, Sub);
        if (Press("wdvm", new RectangleF(x, c.Y + 46, 64, 64), "\uE738", null, false, ok, 22, true)) Volume.Step(-0.05f);
        if (Press("wdvp", new RectangleF(x + 78, c.Y + 46, 64, 64), "\uE710", null, false, ok, 22, true)) Volume.Step(0.05f);
        if (Press("wdvx", new RectangleF(x, c.Bottom - 64, MathF.Min(w, 220), 56), muted ? MuteGlyph : "\uE767", muted ? "Unmute" : "Mute", muted,
                ok, 22, accentFill: false, fill: Bento.Bad)) Volume.ToggleMute();
    }

    // ================= Windows 95 =================

    // a step of the window zoom, t 0..1 from 'from' to 'to': the style's own (XP's title bar flying, the Mac's dotted zoom
    // rectangles: RetroSkin.WindowZoom), else 1995's grey outline
    void Zoom95(RectangleF from, RectangleF to, float t)
    {
        if (Theme.RetroSkin.WindowZoom(_p, from, to, t)) return;
        var z = Lerp(from, to, t);
        _p.Stroke(new RectangleF(MathF.Round(z.X) + 1, MathF.Round(z.Y) + 1, MathF.Round(z.Width) - 2, MathF.Round(z.Height) - 2), 0xFF808080, 2);
    }

    void DrawDetail95(PanelData d, List<WatchWidget> list)
    {
        if (_open == null) return;
        list = FolderBehind95(d, list);     // opened from a folder: the folder's window under it, its widgets the list (WristFolder95.cs)
        var k = list.Contains(_open) ? WatchWidgets.Find(_open.Type) : null;
        string kind = k == null ? null : DetailOf(k.Id);
        if (kind == null) { _open = null; return; }
        if (kind == "folder") { FolderWindow95(d, k); return; }         // each style's folder window (WristFolder95.cs)
        var win = WatchWidgets.CellRect(0, 0, WatchWidgets.Cols, WatchWidgets.Rows, true);
        double zs = Motion.Any ? Theme.RetroSkin.ZoomSeconds : 0;       // the style's zoom (95: 0.22 s); Motion off: none, the window at once
        double t = zs > 0 ? (Clock.Now - _openStart) / zs : 1;
        bool live = Theme.RetroSkin.LiveZoom;               // the style draws the window itself all the way (it fades / scales it)
        if (_closing)
        {
            // the zoom outline back into its widget, then it is gone
            if (t >= 1) { _open = FolderBack(); _closing = false; return; }
            _ui.Animating = true;
            if (!live) { Zoom95(win, WatchWidgets.Rect(_open, true, _grid), (float)t); return; }
        }
        else if (t < 1)
        {
            // the 1995 window zoom: an outline growing from the widget to the window (or the style's own)
            _ui.Animating = true;
            if (!live) { Zoom95(WatchWidgets.Rect(_open, true, _grid), win, (float)t); return; }
        }
        if (SkinDetail(d, k, kind, win)) return;                  // a style's own window (ScreenHooks.cs)
        if (kind == "mines") { Mines95Detail(win); return; }      // the Minesweeper window: winmine's own inside, its chrome beside it
        if (kind == "room") { Room95Detail(win, d); return; }     // the Room window: a details list and a status bar
        if (kind == "dock") { Dock95Detail(win, d); return; }     // the Hexpad window
        if (kind is "photo" or "memory") { Photo95Detail(win, d, k); return; }     // a picture viewer of its style (WristPhoto.cs)
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), k.Title95);
        if (_ui.CaptionClose95("wd95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        if (_ui.Button95("wd95ok", new RectangleF(win.Right - 8 - 75, win.Bottom - 8 - 23, 75, 23), "OK", true, true)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 8, win.Y + 27, win.Width - 16, win.Height - 27 - 36);
        var now = DateTime.Now;
        switch (kind)
        {
            case "media": Media95Detail(c, d); break;
            case "battery": Battery95Detail(c, d); break;
            case "clock": Clock95Detail(c, now); break;
            case "calendar": Calendar95Detail(c, now); break;
            case "timer": Timer95Detail(c); break;
            case "stopwatch": Stopwatch95Detail(c); break;
            case "perf":
                Graph95(d.Vr, new RectangleF(c.X, c.Y, c.Width, c.Height - 30));
                Status95(d, new RectangleF(c.X, c.Bottom - 26, c.Width, 24));
                break;
            case "system": System95Detail(c); break;
            case "volume": Volume95Detail(c); break;
            case "mixer": Mixer95Detail(c); break;
            case "notifications": Notes95Detail(c, d); break;
            case "weather": Weather95Detail(c, d); break;
            case "friends": Friends95Detail(c, d); break;
            case "recap": Recap95Detail(c, d); break;
            case "light": Light95Detail(c, d); break;
            case "rgb": Rgb95Detail(c, d); break;
            case "temps": Temps95Detail(c, d); break;
            case "avatar": Avatar95Detail(c, d); break;       // (WristAvatar95.cs)
            case "mixtape": Mixtape95Detail(c); break;
        }
    }

    void Media95Detail(RectangleF c, PanelData d)
    {
        var (has, title, artist, playing) = MediaNow(d);
        Volume.Sample(Clock.Now);
        var box = new RectangleF(c.X, c.Y, 60, 60);
        var inner = RectangleF.Inflate(box, -2, -2);
        _p.Fill(inner, Wht95);
        if (_art != null) _p.Image(_art, inner);
        else W95Icons.Draw(_p, W95Icons.Id.Note, inner.X + 20, inner.Y + 20);
        _p.Bevel(box, Painter.Edge.Sunken);
        var lcd = new RectangleF(box.Right + 6, c.Y, c.Right - box.Right - 6, 34);
        _p.Fill(lcd, Blk95);
        _p.Bevel(lcd, Painter.Edge.Sunken);
        _ui.Text95(Fit(has ? title : "Nothing playing", lcd.Width - 10), new RectangleF(lcd.X + 5, lcd.Y + 3, lcd.Width - 8, 14), has ? 0xFF00FF00 : 0xFF00A000);
        if (has) _ui.Text95(Fit(artist ?? "", lcd.Width - 10), new RectangleF(lcd.X + 5, lcd.Y + 17, lcd.Width - 8, 14), 0xFF00C000);
        string[] ids = { "media.prev", "media.play", "media.next" };
        string[][] glyphs = { PrevGlyph, playing ? PauseGlyph : PlayGlyph, NextGlyph };
        for (int j = 0; j < 3; j++)
            if (_ui.GlyphButton95(Ui.Id("wd95m", j), new RectangleF(lcd.X + j * 32, lcd.Bottom + 6, 30, 20), glyphs[j], has)) OnAction?.Invoke(ids[j]);
        Volume95Row(new RectangleF(c.X, box.Bottom + 12, c.Width, 34));
    }

    // Volume Control's row: "Volume:", a trackbar, Mute
    void Volume95Row(RectangleF r, string key = "wd95v")
    {
        float lvl = Volume.Level;
        bool ok = lvl >= 0;
        _ui.Text95("Volume:", new RectangleF(r.X, r.Y + 3, 50, 15), Blk95, false, TextAlignment.Leading, ok);
        float v = ok ? lvl : 0;
        if (_ui.Track95(key + "t", new RectangleF(r.X + 50, r.Y, r.Width - 50 - 60, 30), ref v, 0, 1, ok && !Volume.Muted, 11)) Volume.Set(v);
        bool mute = Volume.Muted;
        if (_ui.Check95(key + "k", r.Right - 54, r.Y + 3, "Mute", ref mute, ok)) Volume.ToggleMute();
    }

    void Battery95Detail(RectangleF c, PanelData d)
    {
        var bats = d.Vr.Batteries;
        if (bats.Count == 0) { _ui.Text95(d.VrConnected ? "No battery information." : "SteamVR is not running.", c, Blk95, false, TextAlignment.Center); return; }
        int n = Math.Min(bats.Count, 6);
        float pitch = MathF.Floor(Math.Clamp((c.Height - 26) / Math.Max(n, 4), 19, 26));
        float gh = n * pitch + 26, gy = MathF.Floor(c.Y + (c.Height - gh) / 2);
        var g = new RectangleF(c.X, gy, c.Width, gh);
        _ui.Group95(g, "Batteries");
        var inner = new RectangleF(g.X + 10, g.Y + 18, g.Width - 20, gh - 26);
        for (int i = 0; i < n; i++)
        {
            var b = bats[i];
            float y = inner.Y + i * pitch + MathF.Floor((pitch - 15) / 2);
            bool low = b.pct <= 0.2f && !b.charging;
            _ui.Text95(Bat(b.label), new RectangleF(inner.X, y, 50, 15));
            _ui.Progress95(new RectangleF(inner.X + 52, y + 1, inner.Width - 52 - 110, 13), b.pct, low ? 0xFF800000 : Theme.W95Navy);
            _ui.Text95($"{b.pct * 100:0}%", new RectangleF(inner.Right - 104, y, 34, 15), low ? 0xFF800000 : Blk95, true, TextAlignment.Trailing);
            string state = b.charging ? "Charging" : b.pct <= 0.2f ? Lang.C("battery", "Low") : "";
            _ui.Text95(state, new RectangleF(inner.Right - 64, y, 64, 15), b.charging ? Blk95 : 0xFF800000);
        }
    }

    // Date/Time Properties: the month on the left, the clock on the right
    void Clock95Detail(RectangleF c, DateTime now)
    {
        var left = new RectangleF(c.X, c.Y, 160, c.Height);
        _ui.Group95(left, "Date");
        Month95(new RectangleF(left.X + 8, left.Y + 18, left.Width - 16, left.Height - 26), now, now);
        var right = new RectangleF(left.Right + 8, c.Y, c.Right - left.Right - 8, c.Height);
        _ui.Group95(right, "Time");
        float rad = MathF.Floor(MathF.Min(right.Width - 20, right.Height - 56) / 2);
        Dial95(now, MathF.Floor(right.X + right.Width / 2), MathF.Floor(right.Y + 18 + rad), rad);
        string time = Clk(now, _cfg.Data.Appearance.Clock24h ? "H:mm:ss" : "h:mm:ss tt", CultureInfo.CurrentCulture);
        _ui.Edit95(new RectangleF(right.X + 10, right.Bottom - 30, right.Width - 20, 21), time);
    }

    // a month in a white box, today in navy
    void Month95(RectangleF box, DateTime month, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        var first1 = new DateTime(month.Year, month.Month, 1);
        _ui.Text95(Capital(first1.ToString("MMMM yyyy", culture)), new RectangleF(box.X, box.Y, box.Width, 15), Blk95, true, TextAlignment.Center);
        var g = new RectangleF(box.X, box.Y + 18, box.Width, box.Height - 18);
        _p.Fill(g, Wht95);
        _p.Bevel(g, Painter.Edge.Sunken);
        var gi = RectangleF.Inflate(g, -3, -3);
        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        float cw = gi.Width / 7, rh = MathF.Floor(gi.Height / 7);
        for (int col = 0; col < 7; col++)
        {
            string n = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((first + col) % 7));
            _ui.Text95(n.Substring(0, 1).ToUpper(culture), new RectangleF(gi.X + col * cw, gi.Y, cw, rh), Blk95, true, TextAlignment.Center);
        }
        int offset = ((int)first1.DayOfWeek - first + 7) % 7, days = DateTime.DaysInMonth(month.Year, month.Month);
        for (int dd = 1; dd <= days; dd++)
        {
            int cell = offset + dd - 1;
            var cr = new RectangleF(MathF.Floor(gi.X + cell % 7 * cw), gi.Y + (cell / 7 + 1) * rh, MathF.Ceiling(cw), rh);
            bool today = month.Year == now.Year && month.Month == now.Month && dd == now.Day;
            if (today) _p.Fill(cr, Navy95c);
            _ui.Text95(dd.ToString(), cr, today ? Wht95 : Blk95, false, TextAlignment.Center);
        }
    }

    void Calendar95Detail(RectangleF c, DateTime now)
    {
        var month = new DateTime(now.Year, now.Month, 1).AddMonths(_calOffset);
        if (_ui.Button95("wd95cp", new RectangleF(c.X, c.Y, 24, 21), "<")) { _calOffset--; _dirty = true; }
        if (_ui.Button95("wd95cn", new RectangleF(c.Right - 24, c.Y, 24, 21), ">")) { _calOffset++; _dirty = true; }
        Month95(new RectangleF(c.X + 30, c.Y + 3, c.Width - 60, c.Height - 3), month, now);
    }

    void Timer95Detail(RectangleF c)
    {
        double left = TimerLeft(Clock.Now);
        string txt = _timer == TimerState.Done ? "Time is up!" : Clock2(left);
        _ui.Text95(txt, new RectangleF(c.X, c.Y, c.Width, 26), _timer == TimerState.Done ? 0xFF800000 : Blk95, true, TextAlignment.Center, size: 22);
        float frac = _timer == TimerState.Idle ? 1 : (float)(left / Math.Max(1, Math.Max(_timerTotal, left)));
        _ui.Progress95(new RectangleF(c.X, c.Y + 32, c.Width, 16), frac, Navy95c);
        _ui.Text95("Preset:", new RectangleF(c.X, c.Y + 58, 44, 15));
        int[] presets = { 1, 3, 5, 10, 15, 30 };
        for (int i = 0; i < presets.Length; i++)
            if (_ui.Button95(Ui.Id("wd95tp", i), new RectangleF(c.X + 44 + i * 41, c.Y + 54, 38, 22), Lang.F("{0}m", presets[i]))) TimerPreset(presets[i]);
        float by = c.Y + 88;
        if (_ui.Button95("wd95tg", new RectangleF(c.X, by, 70, 23), _timer == TimerState.Running ? "&Pause" : "&Start")) TimerTap();
        if (_ui.Button95("wd95tr", new RectangleF(c.X + 76, by, 60, 23), "&Reset", _timer != TimerState.Idle)) TimerReset();
        if (_ui.Button95("wd95tm", new RectangleF(c.X + 142, by, 52, 23), "-1 min")) TimerAdjust(-1);
        if (_ui.Button95("wd95tq", new RectangleF(c.X + 200, by, 52, 23), "+1 min")) TimerAdjust(1);
    }

    void Stopwatch95Detail(RectangleF c)
    {
        double t = StopwatchTime(Clock.Now);
        _ui.Text95(StopwatchText(t, true), new RectangleF(c.X, c.Y, 140, 26), Blk95, true, TextAlignment.Leading, size: 22);
        if (_ui.Button95("wd95sp", new RectangleF(c.Right - 3 * 64, c.Y + 1, 60, 23), _swRunning ? "S&top" : "&Start")) StopwatchTap();
        if (_ui.Button95("wd95sl", new RectangleF(c.Right - 2 * 64, c.Y + 1, 60, 23), "&Lap", _swRunning)) _laps.Add(t);
        if (_ui.Button95("wd95sr", new RectangleF(c.Right - 64, c.Y + 1, 60, 23), "&Reset", !_swRunning && t > 0)) { StopwatchReset(); _laps.Clear(); }
        int n = _laps.Count;
        _ui.List95("wd95laps", new RectangleF(c.X, c.Y + 32, c.Width, c.Height - 32), n, -1, (i, r, on) =>
        {
            int li = n - 1 - i;
            double split = _laps[li] - (li > 0 ? _laps[li - 1] : 0);
            _ui.Text95(Lang.F("Lap {0}", li + 1), new RectangleF(r.X + 4, r.Y, 60, r.Height));
            _ui.Text95(StopwatchText(split, true), new RectangleF(r.X + 70, r.Y, 70, r.Height));
            _ui.Text95(StopwatchText(_laps[li], true), new RectangleF(r.Right - 80, r.Y, 76, r.Height), Gry95, false, TextAlignment.Trailing);
        });
    }

    // System Monitor: the CPU over the last minute, and the memory
    void System95Detail(RectangleF c)
    {
        var g = new RectangleF(c.X, c.Y, c.Width, c.Height - 44);
        _p.Fill(g, Blk95);
        _p.Bevel(g, Painter.Edge.Sunken);
        var gi = RectangleF.Inflate(g, -3, -3);
        for (float gy = gi.Y + 16; gy < gi.Bottom; gy += 10) _p.Fill(new RectangleF(gi.X, gy, gi.Width, 1), 0xFF008000);
        float step = gi.Width / SystemStats.History;
        for (int i = 0; i < Stats.CpuCount; i++)
        {
            int idx = (Stats.CpuHead - Stats.CpuCount + i + SystemStats.History) % SystemStats.History;
            float h = MathF.Max(1, MathF.Round(Stats.CpuHistory[idx] * gi.Height));
            _p.Fill(new RectangleF(MathF.Floor(gi.Right - (Stats.CpuCount - i) * step), gi.Bottom - h, MathF.Max(1, MathF.Ceiling(step) - 1), h), 0xFF00FF00);
        }
        string cpuLabel = Stats.Cpu >= 0 ? Lang.F("CPU usage: {0:0}%", Stats.Cpu * 100) : Lang.T("CPU usage: --");
        _p.Fill(new RectangleF(gi.X, gi.Y, _ui.Width95(cpuLabel) + 8, 15), Blk95);
        _ui.Text95(cpuLabel, new RectangleF(gi.X + 3, gi.Y + 1, 120, 13), 0xFF00FF00);
        float frac = Stats.RamTotalGB > 0 ? Stats.RamUsedGB / Stats.RamTotalGB : 0;
        _ui.Text95(Stats.RamTotalGB > 0 ? Lang.F("Memory: {0:0.0} of {1:0} GB", Stats.RamUsedGB, Stats.RamTotalGB) : "Memory: --", new RectangleF(c.X, c.Bottom - 38, c.Width, 15));
        _ui.Progress95(new RectangleF(c.X, c.Bottom - 20, c.Width, 16), frac, Navy95c);
    }

    void Volume95Detail(RectangleF c)
    {
        Volume.Sample(Clock.Now);
        float lvl = Volume.Level;
        _ui.Text95(lvl >= 0 ? (Volume.Muted ? "Muted" : $"{lvl * 100:0}%") : "No output device", new RectangleF(c.X, c.Y, c.Width, 26), Blk95, true,
            TextAlignment.Center, size: 22);
        Volume95Row(new RectangleF(c.X, c.Y + 40, c.Width, 34));
        if (_ui.Button95("wd95vm", new RectangleF(c.X, c.Y + 84, 60, 23), "-5%", lvl >= 0)) Volume.Step(-0.05f);
        if (_ui.Button95("wd95vp", new RectangleF(c.X + 66, c.Y + 84, 60, 23), "+5%", lvl >= 0)) Volume.Step(0.05f);
    }
}
