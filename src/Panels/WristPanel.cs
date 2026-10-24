using System;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using Valve.VR;
using Vortice.DirectWrite;

namespace Orbit;

// What the panels show; filled by OrbitApp (or with fake numbers by --snapshot).
public sealed class PanelData
{
    public VrStats Vr = new();
    public GpuStats Gpu;
    public float VramTotalGB;
    public bool VrConnected;
    public bool DesktopsVisible = true, KeyboardVisible, NightMode;
    public string LayoutName = "Desk";
    public float FakeGpuLoad = -1, FakeVram = -1;
    public DateTime? VrSince;            // when SteamVR connected (the "time in VR" widget)
    public MediaInfo Media;
    public HexpadLink.Snapshot Dock;     // Hexpad's stream dock (null = not looked for)
    public HexpadFaces DockFaces;
    public RoomWatcher.Snapshot Room;    // who is in the VRChat instance (null = not followed)
    public NotificationHistory Notes;    // the last notifications shown (the notifications widget)
    public PhotoWatcher.Snapshot Photos; // VRChat's newest photos (the latest photo widget; null = not followed)
    public PhotoWatcher.Snapshot Memories;   // this day's photos of earlier years (On this day; null = not looked for)
    public PhotoWatcher.Snapshot Album;      // every photo, while the photo view's gallery is open (null = not listed; WristGallery.cs)
    public TempReport Temps;             // the temperatures (the Temperatures widget, the hot alert; null = not read)
    public WeatherReport Weather;        // the weather widget's report (Sys/Weather.cs; null = none asked for)
    public FriendsWatcher.Snapshot Friends;  // VRCX's friends online (the friends widget; null = not followed)
    public SessionRecap Recap;           // the last session's recap (the recap widget; null = none yet)
    public LumosLink Lamp;               // the room light through Lumos (Sys/RoomLights.cs; null = none)
    public PrismaLink Rgb;               // the PC's RGB through Prisma
    public AvatarLink Avatar;            // the VRChat avatar's toggles (Sys/AvatarToggles.cs; null = none followed)
    public (double pos, double dur)? FakeTimeline;     // --snapshot: where in the track (null: the media session's)
    public uint[] FakeArt;               // --snapshot: a cover with FakeMedia (MediaInfo.ArtSize square, premultiplied)
    public bool StatusLine;              // the chatbox status line is on
    public bool Theater;                 // theater mode plays
    public (bool has, string title, string artist, bool playing)? FakeMedia;
    public float GpuLoad => FakeGpuLoad >= 0 ? FakeGpuLoad : Gpu?.Load3D ?? -1;
    public float VramUsed => FakeVram >= 0 ? FakeVram : Gpu?.VramUsedGB ?? -1;
}

// Watch on the wrist: clock, frame timing, GPU, batteries and four quick buttons.
// Fades in when the wrist is turned towards the face; the other hand's laser can click it or grab it.
public sealed partial class WristPanel : IPointerTarget, IDisposable
{
    public const int TW = 640, TH = 440;
    readonly Painter _p;
    Ui _ui;                              // (not readonly: the warm-up draws with a Ui of its own - WristWarm.cs)
    readonly ConfigStore _cfg;
    readonly PointerState _ptr = new();
    public readonly Surface Surf;
    public VrOverlay Ov { get; private set; }
    public Action<string> OnAction;
    int _introWant;                      // the watch came into view: 2 its boot, 1 the quick one (Cyber) - started once it is really there
    double _introWantAt;
    bool _eyeOn;                         // eye tracking: your eyes are on it (EyeOn / EyeOff degrees, a hysteresis)
    const float EyeOn = 15, EyeOff = 28;
    double _shownAt = -1e9;              // the last frame the watch showed (its intro: not on a glance back)
    public float Alpha;
    bool _dirty = true, _grabbing;
    int _themeVersion = -1;
    double _nextDraw, _lastDraw;
    int _dockVersion = -1;
    VrSession _vr;

    WristConfig C => _cfg.Data.Wrist;

    public WristPanel(Gpu g, Painter p, ConfigStore cfg)
    {
        _p = p; _cfg = cfg;
        _ui = new Ui(p) { Hud = false };      // Cyber: the watch keeps its own designs (WristCyber.cs), not the settings' HUD controls
        Surf = new Surface(g, TW, TH);
    }

    public void CreateOverlay(VrSession vr)
    {
        _vr = vr;
        Ov = new VrOverlay("erik.orbit.wrist", "OrbitOverlay Wrist");
        Ov.SetFlag(VROverlayFlags.IsPremultiplied, true);
        Ov.SetSortOrder(150);
        Ov.SetMouseScale(TW, TH);
        Alpha = 0;
        _dirty = true;
    }

    public void DestroyOverlay() { Ov?.Dispose(); Ov = null; _vr = null; }

    // Default spot: on the back of the forearm just behind the controller, text along the arm.
    // Rows are the overlay's X/Y/Z axes in controller space (see MathUtil for the convention).
    public static Matrix4x4 DefaultOffset(string hand) => hand == "right"
        ? MathUtil.Basis(new(0, 0, 1), new(0, -1, 0), new(1, 0, 0), new(0.04f, -0.01f, 0.13f))
        : MathUtil.Basis(new(0, 0, -1), new(0, -1, 0), new(-1, 0, 0), new(-0.04f, -0.01f, 0.13f));

    Matrix4x4 Offset => C.Offset != null ? MathUtil.FromArray(C.Offset) : DefaultOffset(C.Hand);
    uint HandIndex(VrSession vr) => C.Hand == "right" ? vr.RightIndex : vr.LeftIndex;

    public void Update(VrSession vr, double now, float dt, PanelData d)
    {
        if (Ov == null) return;
        TickTimers(now);
        uint idx = HandIndex(vr);
        if (!C.Enabled || idx == VrSession.InvalidIndex || !vr.DeviceValid[idx])
        {
            Ov.Hide();
            Alpha = 0;
            return;
        }
        var rel = Offset;
        Ov.SetRelative(idx, rel);
        Ov.SetWidth(C.Width);

        float target = 1;
        if (!C.AlwaysVisible && !_grabbing && vr.HmdValid)
        {
            var world = rel * vr.Device[idx];
            Vector3 n = Vector3.Normalize(MathUtil.Back(world));
            Vector3 toEye = Vector3.Normalize(vr.Hmd.Translation - world.Translation);
            float facing = Vector3.Dot(n, toEye);
            float look;
            if (vr.EyesValid)
            {
                // eye tracking (Erik 10-18: "only when I look at it"): your eyes alone, on it - shown from 15 deg off it, gone past 28 (a look
                // at its edge does not make it blink); a blink is held by EyeGaze, the head counts only when the eyes are not tracked
                float ang = MathF.Acos(Math.Clamp(Vector3.Dot(vr.Gaze, -toEye), -1f, 1f)) * 180 / MathF.PI;
                if (ang < EyeOn) _eyeOn = true;
                else if (ang > EyeOff) _eyeOn = false;
                look = _eyeOn ? 1 : 0;
            }
            else
            {
                _eyeOn = false;
                look = MathUtil.Smoothstep(0.55f, 0.8f, Vector3.Dot(Vector3.Normalize(MathUtil.Forward(vr.Hmd)), -toEye));
            }
            target = MathUtil.Smoothstep(0.45f, 0.75f, facing) * look;
        }
        if (_dismissReq) { _dismissReq = false; _dismissed = true; _dismissUntil = now + (C.AlwaysVisible ? 8 : 30); }
        if (_dismissed)
        {
            if (now >= _dismissUntil || (!C.AlwaysVisible && target < 0.05f)) _dismissed = false;
            else target = 0;
        }
        Alpha = Motion.Any ? Alpha + (target - Alpha) * Math.Min(1, dt * 10) : target;      // Motion off: there at once
        // looking away keeps an opened widget open: a look back finds it as it was (Erik 10-24: "a wristwatch megjegyezhetné hogy melyik
        // widget van nyitva akkor is ha elfordítom"); its X, a tap beside it or another page closes it
        if (Alpha < 0.02f) { Ov.Hide(); _ptr.Leave(); SwipeCancel(); DockLetGo(); return; }
        Ov.SetAlpha(Math.Min(1, Alpha));
        if (!Ov.Visible)
        {
            _dirty = true;
            // the widgets cascade in (Cyber: every face boots) - not again on a glance back within 20 s: the watch is simply there
            // (a 1.2 s cascade on every raise made a look at the time slow; Erik 10-18: "very responsive")
            // Cyber, back within 20 s: the boot again, quicker (Erik 10-19: "ha elnézek és vissza utána nem [animálódik], csak ha sok ideig
            // nem nézek rá"; WristCyberFx.cs)
            // (a widget left open: no cascade or boot under it - the watch is back as it was left)
            if ((C.Face == "custom" || Theme.Cyber) && !Theme.Classic && _open == null)
            {
                // (gone under 0.4 s: the eyes slipped off its edge a moment - it simply stays)
                double away = now - _shownAt;
                int want = away > 20 ? 2 : Theme.Cyber && away > 0.4 ? 1 : 0;
                if (want > 0 && _introWant == 0) IntroHold();           // the face waits for its boot (nothing of it before)
                _introWant = Math.Max(_introWant, want);                  // (a boot still held from a look cut short stays)
                _introWantAt = now;
                if (Laser.Trace) Log.Info($"watch: in view - {(_introWant == 2 ? "boot" : _introWant == 1 ? "quick boot" : "as it was")} (away {Math.Min(away, 999):0.0} s, motion {Motion.Level})");
            }
        }
        _shownAt = now;
        Ov.Show();
        // the boot / the glance once the watch is really there - Alpha 0.55, or 0.35 s after it came: started at 2 % it ran while the
        // wrist was still turning up and was over before the watch could be seen (Erik 10-19: "még mindig nem animálva jelenik meg az
        // overlay ha ránézek a csuklómra")
        if (_introWant != 0 && (Alpha >= 0.55f || now - _introWantAt > 0.35))
        {
            if (_introWant == 2) Intro(); else CyberBoot(quick: true);
            if (Laser.Trace) Log.Info($"watch: {(_introWant == 2 ? "boot" : "quick boot")} started at {Alpha * 100:0} % shown, {(now - _introWantAt) * 1000:0} ms after it came");
            _introWant = 0;
        }
        if (_themeVersion != Theme.Version) { _themeVersion = Theme.Version; _dirty = true; }
        // Hexpad's keys / knobs changed (its animations, a knob turned): drawn as they come (up to its 20 a second), not on the
        // next timer tick - that was 2-10 frames a second (Erik: "a hexpad elég kevés fps-el mozog")
        int dv = d.Dock?.Version ?? -1;
        if (dv != _dockVersion)
        {
            _dockVersion = dv;
            if (C.Face == "custom" && (HasWidget("dock") || _open?.Type == "dock")) _dirty = true;
        }
        if (MixerChanged()) _dirty = true;           // the mixer's apps: a level moved, an app came or went (WristMixer.cs)
        if (MixtapeChanged()) _dirty = true;         // Mixtape: what plays, the queue, a cover come, the lyrics (WristMixtape.cs)
        if (PhotoChanged(d)) _dirty = true;          // a new photo, its metadata, a thumbnail made (WristPhoto.cs)
        if (LightsChanged(d)) _dirty = true;         // the light / the RGB answered or were switched (WristLights.cs)
        if (AvatarChanged(d)) _dirty = true;         // VRChat's values, a tap shown, other buttons picked (WristAvatar.cs)
        // motion is drawn at the headset's rate (it was 45 fps; laid-out text is kept now, a frame is ~1.5 ms)
        if (_dirty || now >= _nextDraw || Animating)
        {
            _dirty = false;          // before Draw: a tap inside Draw asks for the next frame itself
            Spikes.Mark(Spikes.Watch);
            Draw(d);
            Spikes.Mark(Spikes.WatchDraw);
            Ov.SetTexture(Surf.Tex);
            Spikes.Mark(Spikes.WatchSend);
            _lastDraw = now;
            bool fast = C.Face == "custom" ? FramesShown || FastWidgets : WantsFrames;
            _nextDraw = now + (fast ? 0.1 : 0.5);
            if (Theme.Classic)      // the 95 clock shows seconds: draw again just after the next one starts
                _nextDraw = Math.Min(_nextDraw, now + 1.02 - DateTime.Now.Millisecond / 1000.0);
            // so do the modern time widget (seconds in every size) and the analog clocks (a second hand, the seconds satellite)
            else if (C.Face == "custom" && (HasWidget("time") || HasWidget("analog")))
                _nextDraw = Math.Min(_nextDraw, now + 1.02 - DateTime.Now.Millisecond / 1000.0);
            // the time in VR (its card, the clock's header) moves on a whole minute after SteamVR came on: just after that too
            if (!Theme.Classic && C.Face == "custom" && d.VrSince is { } since && (HasWidget("session") || HasWidget("time")))
                _nextDraw = Math.Min(_nextDraw, now + 60.02 - (DateTime.Now - since).TotalSeconds % 60);
            // an animation wants its next frame by a time (AnimWake.cs): a style's (a blink, a sweep, a fade; Cyber's scheduled effects),
            // the charging bolt's breath
            _nextDraw = Math.Min(_nextDraw, AnimWake.Take());
        }
    }

    // Watch faces: "full" (everything), "analog" (dial instead of digits), "minimal" (big time +
    // buttons), "time" (only the time on a small card, nothing to press).
    public void Draw(PanelData d)
    {
        _rolling = false;
        if (Theme.Classic) { Draw95(d); return; }
        _ui.Begin(_ptr, 1);
        SwipeGuard();
        _p.Begin(Surf.Bitmap, 0x00000000, TestScale);
        string face = C.Face ?? "full";
        var now = Now;                        // (the clock; a tool's fixed moment)
        if (face == "custom")
        {
            DrawCustom(d);
            long e0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _p.End();                                   // (D2D draws the batch here: waits when the GPU is behind)
            Spikes.Piece("watch: D2D flush", e0);
            _ui.End();
            return;
        }
        if (Theme.Cyber) { DrawCyberFace(face, d); _p.End(); _ui.End(); return; }      // its own HUD for each preset (WristCyberFaces.cs)
        if (face == "time") { DrawTimeOnly(now); _p.End(); _ui.End(); return; }

        var all = new RectangleF(0, 0, TW, TH);
        if (Theme.Cyber) CyberHud.PanelFrame(_p, all, Theme.OverlayBg);      // the olive frame
        else _p.Fill(all, Theme.OverlayBg, 30);
        if (Theme.Classic) _p.Bevel(all, Painter.Edge.Window);            // a 95 window
        else if (!Theme.Cyber) _p.Stroke(new RectangleF(1, 1, TW - 2, TH - 2), 0x40FFFFFF, 2, 29);

        if (face == "minimal")
        {
            BigTime(now, new RectangleF(0, 34, TW, 170), 150, TextAlignment.Center);
            _p.Text(DateText(now), new RectangleF(0, 206, TW, 40), Theme.Dim, 26, FontWeight.Normal, TextAlignment.Center);
            Buttons(d);
            _p.End(); _ui.End();
            return;
        }

        if (face == "analog")
        {
            Dial(now, 104, 92, 76);
            BigTime(now, new RectangleF(204, 30, 200, 70), 52, TextAlignment.Leading);
            _p.Text(DateText(now), new RectangleF(206, 98, 190, 60), Theme.Dim, 19, FontWeight.Normal,
                TextAlignment.Leading, ParagraphAlignment.Near, null, true);
        }
        else
        {
            BigTime(now, new RectangleF(30, 6, 340, 126), 108, TextAlignment.Leading);
            _p.Text(DateText(now), new RectangleF(34, 124, 340, 34), Theme.Dim, 22);
        }

        // batteries, top right
        if (C.ShowBatteries)
        {
            float y = 22;
            int shown = 0;
            foreach (var b in d.Vr.Batteries)
            {
                if (shown++ >= 4) break;
                DrawBattery(new RectangleF(400, y, 214, 30), Bat(b.label), b.pct, b.charging);
                y += 36;
            }
            if (shown == 0)
                _p.Text(d.VrConnected ? "no battery info" : "SteamVR offline", new RectangleF(400, 22, 214, 30), Theme.Faint, 16,
                    FontWeight.Normal, TextAlignment.Trailing);
        }

        // performance strip: numbers or the frame-time graph
        if (Theme.Classic) _p.EtchedLine(28, TW - 28, 171);
        else _p.Line(28, 172, TW - 28, 172, 0x26FFFFFF, 1.5f);
        if (C.ShowPerformance)
        {
            if (C.PerfGraph) Graph(d.Vr, new RectangleF(28, 180, TW - 56, 76));
            else Numbers(d);
        }

        if (C.ShowMedia) DrawMedia(d);
        Buttons(d);
        _p.End();
        _ui.End();
    }

    string DateText(DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        if (Lang.Has("date|long")) return now.ToString(Lang.T("date|long"), culture);       // the language's own order (Magyar: "péntek, okt. 16.")
        return Clk(now, culture.DateTimeFormat.MonthDayPattern, culture) + ", " + Clk(now, "dddd", culture);
    }

    void BigTime(DateTime now, RectangleF r, float size, TextAlignment align)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        string time = Clk(now, h24 ? "HH:mm" : "h:mm", CultureInfo.CurrentCulture);
        _p.Text(time, r, Theme.Text, size, FontWeight.SemiBold, align, ParagraphAlignment.Center, Theme.Display);
        if (!h24)
        {
            float tw = _p.MeasureWidth(time, size, FontWeight.SemiBold, Theme.Display);
            float x = align == TextAlignment.Center ? r.X + (r.Width + tw) / 2 + 6 : r.X + tw + 6;
            // nothing under 15 px on the watch (the analog face's 52 px time made it 10)
            float ss = MathF.Max(15, size * 0.2f);
            _p.Text(now.Hour < 12 ? "AM" : "PM", new RectangleF(x, r.Y + r.Height * 0.55f, 70, MathF.Max(ss * 1.4f, size * 0.3f)), Theme.Dim, ss, FontWeight.SemiBold);
        }
    }

    // analog dial: ticks, hour and minute hands, accent centre
    void Dial(DateTime now, float cx, float cy, float r)
    {
        if (Theme.Classic)
        {
            _p.Circle(cx, cy, r, Theme.W95Window);
            _p.Ring(cx, cy, r, Theme.W95Dark, 2);
        }
        else
        {
            _p.Circle(cx, cy, r, Theme.Mix(Theme.Panel2, 0xFF000000, 0.15f));
            _p.Ring(cx, cy, r, Theme.WithAlpha(Theme.Accent, 0.55f), 3);
        }
        for (int i = 0; i < 60; i++)
        {
            bool hour = i % 5 == 0;
            float a = i / 60f * MathF.Tau - MathF.PI / 2;
            float r0 = r - (hour ? 14 : 7), r1 = r - 4;
            _p.Line(cx + r0 * MathF.Cos(a), cy + r0 * MathF.Sin(a), cx + r1 * MathF.Cos(a), cy + r1 * MathF.Sin(a),
                hour ? Theme.Text : Theme.Faint, hour ? 3 : 1.5f);
        }
        float m = now.Minute + now.Second / 60f;
        float h = now.Hour % 12 + m / 60f;
        void Hand(float frac, float len, float width, uint col)
        {
            float a = frac * MathF.Tau - MathF.PI / 2;
            _p.Line(cx, cy, cx + len * MathF.Cos(a), cy + len * MathF.Sin(a), col, width);
        }
        Hand(h / 12f, r * 0.5f, 7, Theme.Text);
        Hand(m / 60f, r * 0.78f, 4.5f, Theme.Text);
        _p.Circle(cx, cy, 6, Theme.Accent);
    }

    void DrawTimeOnly(DateTime now)
    {
        var card = new RectangleF(70, 130, TW - 140, 190);
        _p.Fill(card, Theme.OverlayBg, 34);
        if (Theme.Classic) _p.Bevel(card, Painter.Edge.Window);
        else _p.Stroke(RectangleF.Inflate(card, -1, -1), Theme.PanelLine, 2, 33);
        BigTime(now, new RectangleF(card.X, card.Y + 6, card.Width, 128), 112, TextAlignment.Center);
        _p.Text(DateText(now), new RectangleF(card.X, card.Y + 132, card.Width, 40), Theme.Dim, 24, FontWeight.Normal, TextAlignment.Center);
    }

    void Numbers(PanelData d)
    {
        var v = d.Vr;
        float cw = (TW - 56) / 4f;
        uint fpsCol = v.AppFps < 0 ? Theme.Text : v.AppFps < v.DisplayHz * 0.85f ? Theme.Warn : Theme.Text;
        Stat(0, cw, v.AppFps < 0 ? "–" : $"{v.AppFps:0}", Lang.F("fps / {0:0}", v.DisplayHz), fpsCol);
        uint rpCol = v.ReprojPct < 0 ? Theme.Text : v.ReprojPct > 25 ? Theme.Bad : v.ReprojPct > 8 ? Theme.Warn : Theme.Text;
        Stat(1, cw, v.ReprojPct < 0 ? "–" : $"{v.ReprojPct:0}%", "reprojected", rpCol);
        float budget = 1000f / Math.Max(1, v.DisplayHz);
        uint msCol = v.GpuMs < 0 ? Theme.Text : v.GpuMs > budget ? Theme.Bad : v.GpuMs > budget * 0.85f ? Theme.Warn : Theme.Text;
        Stat(2, cw, v.GpuMs < 0 ? "–" : $"{v.GpuMs:0.0}", "GPU ms", msCol);
        float vram = d.VramUsed;
        // no GPU %: the WDDM engine counters badly under-read AMD cards (2 % at a real 99 %)
        string vramCap = d.VramTotalGB > 0 ? Lang.F("GB VRAM / {0:0}", d.VramTotalGB) : "GB VRAM";
        uint vrCol = vram > 0 && d.VramTotalGB > 0 && vram > d.VramTotalGB * 0.92f ? Theme.Warn : Theme.Text;
        Stat(3, cw, vram < 0 ? "–" : $"{vram:0.0}", vramCap, vrCol);
    }

    // last ~5 s of frames: GPU time per frame against the frame budget line;
    // amber = over budget / reprojected, red = dropped
    void Graph(VrStats v, RectangleF r)
    {
        if (Theme.Classic) { GraphClassic(v, r); return; }
        _p.Fill(r, Theme.WithAlpha(0xFF000000, 0.25f), 10);
        float budget = 1000f / Math.Max(1, v.DisplayHz);
        float top = budget * 2;
        int n = v.FrameCount;
        float step = r.Width / VrStats.FrameHistory;
        float by = r.Bottom - 4 - (r.Height - 8) * (budget / top);
        for (int i = 0; i < n; i++)
        {
            int idx = (v.FrameHead - n + i + VrStats.FrameHistory) % VrStats.FrameHistory;
            float ms = v.FrameMs[idx];
            byte flag = v.FrameFlag[idx];
            float h = Math.Clamp(ms / top, 0.02f, 1) * (r.Height - 8);
            uint col = flag == 2 ? Theme.Bad : flag == 1 || ms > budget ? Theme.Warn : Theme.WithAlpha(Theme.Accent, 0.85f);
            float x = r.Right - (n - i) * step;
            _p.Fill(new RectangleF(x, r.Bottom - 4 - h, Math.Max(1, step - 0.3f), h), col);
        }
        _p.Line(r.X + 6, by, r.Right - 6, by, 0x80FFFFFF, 1.5f);
        string label = v.AppFps < 0 ? "waiting for frames" :
            Lang.F("{0:0} fps · {1:0.0} ms GPU · {2:0}% reprojected", v.AppFps, v.GpuMs, Math.Max(0, v.ReprojPct));
        _p.Text(label, new RectangleF(r.X + 10, r.Y + 4, r.Width - 20, 24), Theme.Text, 16, FontWeight.SemiBold);
        _p.Text($"{budget:0.0} ms", new RectangleF(r.Right - 80, by - 22, 72, 20), Theme.Dim, 15, FontWeight.Normal, TextAlignment.Trailing);
    }

    // the 95 skin's graph: a sunken black box, green grid, green bars - the performance window of the era
    void GraphClassic(VrStats v, RectangleF r)
    {
        const uint green = 0xFF00FF00, dim = 0xFF008000, warn = 0xFFFFFF00, bad = 0xFFFF0000;
        _p.Fill(r, 0xFF000000);
        _p.Bevel(r, Painter.Edge.Sunken);
        var inner = RectangleF.Inflate(r, -4, -4);
        for (float gx = inner.X + 12; gx < inner.Right; gx += 24) _p.Fill(new RectangleF(gx, inner.Y, 1, inner.Height), dim);
        for (float gy = inner.Y + 12; gy < inner.Bottom; gy += 12) _p.Fill(new RectangleF(inner.X, gy, inner.Width, 1), dim);
        float budget = 1000f / Math.Max(1, v.DisplayHz);
        float top = budget * 2;
        int n = v.FrameCount;
        float step = inner.Width / VrStats.FrameHistory;
        float by = inner.Bottom - inner.Height * (budget / top);
        for (int i = 0; i < n; i++)
        {
            int idx = (v.FrameHead - n + i + VrStats.FrameHistory) % VrStats.FrameHistory;
            float ms = v.FrameMs[idx];
            byte flag = v.FrameFlag[idx];
            float h = Math.Clamp(ms / top, 0.02f, 1) * inner.Height;
            uint col = flag == 2 ? bad : flag == 1 || ms > budget ? warn : green;
            float x = inner.Right - (n - i) * step;
            _p.Fill(new RectangleF(x, inner.Bottom - h, Math.Max(1, step - 0.3f), h), col);
        }
        _p.Fill(new RectangleF(inner.X, by, inner.Width, 1), 0xFFFFFFFF);
        string label = v.AppFps < 0 ? "waiting for frames" :
            Lang.F("{0:0} fps · {1:0.0} ms GPU · {2:0}% reprojected", v.AppFps, v.GpuMs, Math.Max(0, v.ReprojPct));
        _p.Text(label, new RectangleF(inner.X + 6, inner.Y + 2, inner.Width - 12, 22), green, 15, FontWeight.Bold);
        _p.Text($"{budget:0.0} ms", new RectangleF(inner.Right - 80, by - 20, 74, 18), green, 13, FontWeight.Normal, TextAlignment.Trailing);
    }

    // keyboard, monitors, recenter, layout profile (shows its name), night mode, settings
    void Buttons(PanelData d)
    {
        string[] ids = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
        string[] icons = { Theme.IcoKeyboard, d.DesktopsVisible ? Theme.IcoMonitor : Theme.IcoHide, Theme.IcoRecenter, null, Theme.IcoNight, Theme.IcoSettings };
        const int n = 6;
        float bw = (TW - 56 - (n - 1) * 10) / (float)n;
        for (int i = 0; i < n; i++)
        {
            var r = new RectangleF(28 + i * (bw + 10), 354, bw, 62);
            bool on = (i == 0 && d.KeyboardVisible) || (i == 1 && d.DesktopsVisible) || (i == 4 && d.NightMode);
            bool click = icons[i] == null
                ? _ui.Button(Ui.Id("w", i), r, d.LayoutName, null, false, true, d.LayoutName.Length > 5 ? 15 : 17)
                : _ui.Button(Ui.Id("w", i), r, null, icons[i], on, true, 24);
            if (click) Act(ids[i]);
        }
    }

    Vortice.Direct2D1.ID2D1Bitmap _art;
    int _artVersion = -1;

    // now playing: cover, title / artist, previous - play/pause - next
    void DrawMedia(PanelData d)
    {
        bool has; string title, artist; bool playing; uint[] art = null; int ver = -2;
        if (d.FakeMedia is { } f) (has, title, artist, playing) = f;
        else if (d.Media != null) (has, title, artist, playing, art, ver) = d.Media.Snapshot();
        else { has = false; title = artist = null; playing = false; }

        if (ver != _artVersion)
        {
            _artVersion = ver;
            _art?.Dispose();
            _art = art != null ? _p.CreateBitmap(art, MediaInfo.ArtSize, MediaInfo.ArtSize) : null;
        }
        var cover = new RectangleF(28, 270, 64, 64);
        if (_art != null) _p.Image(_art, cover);
        else
        {
            _p.Fill(cover, Theme.Panel3, 10);
            _p.Icon("\uE8D6", cover, Theme.Dim, 26);
        }
        if (Theme.Classic) _p.Bevel(RectangleF.Inflate(cover, 2, 2), Painter.Edge.Sunken);   // a 95 picture box
        float bx = TW - 28 - 3 * 58 + 4;
        var text = new RectangleF(104, 268, bx - 104 - 10, 68);
        if (has)
        {
            _p.Text(title, new RectangleF(text.X, text.Y + 4, text.Width, 34), Theme.Text, 22, FontWeight.SemiBold);
            _p.Text(artist ?? "", new RectangleF(text.X, text.Y + 36, text.Width, 28), Theme.Dim, 17);
        }
        else
            _p.Text(NotPlaying, text, Theme.Faint, 19);
        string[] ids = { "media.prev", "media.play", "media.next" };
        string[] icons = { "\uE892", playing ? "\uE769" : "\uE768", "\uE893" };
        for (int i = 0; i < 3; i++)
        {
            if (_ui.Button("m" + i, new RectangleF(bx + i * 58, 276, 52, 52), null, icons[i], false, has, 20))
                Act(ids[i]);
        }
    }

    void Stat(int i, float cw, string value, string caption, uint color)
    {
        if (Theme.Classic && color == Theme.Warn) color = 0xFF806000;      // amber that reads on the grey face
        else if (Theme.Classic && color == Theme.Bad) color = 0xFF800000;
        float x = 28 + i * cw;
        _p.Text(value, new RectangleF(x, 180, cw - 6, 52), color, 40, FontWeight.SemiBold, TextAlignment.Leading,
            ParagraphAlignment.Center, Theme.Display);
        _p.Text(caption, new RectangleF(x + 2, 230, cw, 26), Theme.Dim, 16);
    }

    // a device: its name, its bar and the percentage - in the watch's one battery rule (WristCustom.BatteryLevel: Ok above 30 %,
    // Warn 16-30 %, Bad at 15 % or less, on the number shown), so a battery has the same colour on every face
    void DrawBattery(RectangleF r, string label, float pct, bool charging)
    {
        int shown = ShownPct(pct);
        var lv = BatteryLevel(shown);
        uint col = Bento.Level(lv, Bento.Ok);
        _p.Text(label, new RectangleF(r.X, r.Y, 74, r.Height), Theme.Dim, 18, FontWeight.SemiBold, TextAlignment.Trailing);
        var bar = new RectangleF(r.X + 84, r.Y + 8, 70, r.Height - 16);
        if (Theme.Classic)
        {
            var well = RectangleF.Inflate(bar, 0, 2);
            _p.Bevel(well, Painter.Edge.Sunken);
            var inner = RectangleF.Inflate(well, -3, -3);
            uint block = pct > 0.2f ? Theme.W95Navy : 0xFF800000;
            int blocks = (int)MathF.Round(Math.Clamp(pct, 0, 1) * 8);
            float bw = (inner.Width - 7 * 2) / 8;
            for (int i = 0; i < blocks; i++) _p.Fill(new RectangleF(inner.X + i * (bw + 2), inner.Y, bw, inner.Height), block);
        }
        else
        {
            _p.Fill(bar, 0x30FFFFFF, 4);
            _p.Fill(new RectangleF(bar.X, bar.Y, bar.Width * Math.Clamp(pct, 0, 1), bar.Height), col, 4);
        }
        _p.Text((charging ? "⚡" : "") + Pct(shown), new RectangleF(bar.Right + 4, r.Y, r.Right - bar.Right - 4, r.Height),
            Bento.Level(lv, Theme.Text), 18, FontWeight.SemiBold, TextAlignment.Trailing);
    }

    // ---- IPointerTarget ----
    public VrOverlay Overlay => Ov;
    public bool Interactive => Ov != null && Ov.Visible && Alpha > 0.6f;
    public bool AcceptsFrom(Hand h) => (C.Hand == "right") != (h == Hand.Right);
    public Vector2 TextureSize => new(TW, TH);
    public void PointerMove(Hand h, Vector2 px)
    {
        _ptr.Move(px.X, px.Y);
        if (DockLive) { DockIn.Move(px); if (DockIn.TakeDirty()) _dirty = true; }      // the opened dock (WristDock.cs)
        if (SwipeMove(px)) { _dirty = true; return; }       // a swipe between the pages: the face follows the laser
        if (_ui.NeedsRedrawForMove(_ptr, Theme.Classic ? 2 : 1)) _dirty = true;     // only when it gets onto another control
    }
    public void PointerLeave(Hand h) { SwipeCancel(); _ptr.Leave(); _dockIn?.Leave(); _dirty = true; }
    public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px)
    {
        if (b != PtrButton.Primary) return;
        _ptr.Move(px.X, px.Y);
        if (down) { _ptr.Press(); SwipeBegin(px); if (DockLive) DockIn.Down(px); }
        else { SwipeEnd(px); _ptr.Release(); if (_dockIn?.Busy == true) _dockIn.Up(); }
        _dirty = true;
    }
    // the stick scrolls the opened room's and notifications' lists (nothing else on the watch scrolls); the 95 list moves 3 rows a
    // notch (a wheel's), so it gets whole notches, a third as many
    float _wheel95;
    public void PointerScroll(Hand h, float n, Vector2 px)
    {
        if (DockLive) { DockIn.Scroll(px, n); if (DockIn.TakeDirty()) _dirty = true; return; }      // the stick over a knob turns it
        if (MixerScroll(px, n)) return;            // the stick over a mixer's slider moves it, in the opened mixer off them it scrolls
        if (PhotoScroll(n)) return;                // the opened photo: the stick steps through the day's photos (WristPhoto.cs)
        if (!(DetailIs("room") || DetailIs("notifications") || DetailIs("friends") || DetailIs("recap") || DetailIs("avatar") || DetailIs("mixtape")) || _closing) return;
        if (Theme.Classic)
        {
            _wheel95 += n / 3;
            if (MathF.Abs(_wheel95) < 1) return;
            n = MathF.Truncate(_wheel95);
            _wheel95 -= n;
        }
        _ptr.Wheel += n;
        _dirty = true;
    }
    // the stick pushed sideways (Erik 10-23: "a jobb joystickkal ... lapozni, nem csak húzással"): the next / previous page, sliding as a
    // let-go swipe does; none past the first / last, none while a widget is open (it keeps the stick: the dock's knobs, the photos)
    public void PointerStep(Hand h, int dir, Vector2 px)
    {
        if (!CanSwipe || _swiping || _closing) return;
        int to = CurPage + Math.Sign(dir);
        if (to < 0 || to >= WatchPages.Count(C)) return;
        GoToPage(to, animate: true);
    }

    public bool Grabbable => !C.Locked;

    public Matrix4x4 WorldTransform =>
        _vr != null && _vr.TryDeviceWorld(HandIndex(_vr), out var dev) ? Offset * dev : Matrix4x4.Identity;

    public void GrabMove(Matrix4x4 world)
    {
        _grabbing = true;
        if (_vr != null && _vr.TryDeviceWorld(HandIndex(_vr), out var dev))
        {
            C.Offset = MathUtil.ToArray(world * MathUtil.Invert(dev));
            _cfg.MarkDirty();
        }
    }

    float _twoStartWidth;
    public void GrabBegin(bool detach) { }
    public void GrabStickX(float v, float dt) { C.Width = Math.Clamp(C.Width * (1 + v * 0.9f * dt), 0.06f, 0.3f); _cfg.MarkDirty(); }
    public void GrabTwoHandBegin() => _twoStartWidth = C.Width;
    public void GrabTwoHandScale(float r) { C.Width = Math.Clamp(_twoStartWidth * r, 0.06f, 0.3f); _cfg.MarkDirty(); }
    public bool GrabSnapPreview => false;

    public void GrabEnd() { _grabbing = false; }

    public void Dispose() { DestroyOverlay(); Surf.Dispose(); Canvas95?.Dispose(); Volume.Dispose(); Mic.Dispose(); MixDispose(); PhotoDispose(); MtDispose(); }
}
