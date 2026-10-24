using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Valve.VR;
using Vortice.DirectWrite;

namespace Orbit;

// The eye calibration's board (Sys/EyeCalib.cs; Settings > VRChat > Calibrate..., Erik 10-24: "az eye tracking kurzor pontosságán még
// lehetne javítani"): 0.7 m wide, 1 m ahead of the eyes, the five points' places on it, what to do, how far it got. The dot is an overlay
// of its own over it, moved and sized every frame (the board is drawn again only when what it says changes - Run.Version). The trigger
// (either hand, wherever it points) takes the point the dot is on, B / Y cancels; while it shows the lasers and the eye cursor see the
// board alone (Aim), so no click reaches a panel nearer than it, a monitor behind or beside it, and neither hand reaches the game
// (OrbitApp.BlockGameInput); no beam is drawn on it (HidesLaser). The end: what came of it, until the trigger or ShowEnd. Its textures and
// overlays exist only while it shows. Every style: the modern card, Cyber's console, and in the retro ones a window of the era (the
// Mac's Platinum one) with the targets in a white well - the way XP's "Calibrate the pen" screen had them.
public sealed class EyeCalibPanel : IPointerTarget, IDisposable
{
    public const int TW = 960, TH = 960, W95 = TW / 2, H95 = TH / 2, DotTex = 128, DotCanvas95 = 32;
    public const float BoardW = 0.70f, DotW = 0.05f;              // metres (the dot: 2.9 deg at 1 m)
    public const double ShowEnd = 4;                              // seconds the end shows (the trigger closes it sooner)
    const float PxPerM = TW / BoardW;
    const uint K95 = 0xFF000000;

    readonly Gpu _g;
    readonly Painter _p;
    readonly Ui _ui;
    readonly PointerState _ptr = new();
    Surface _surf, _canvas95, _dotSurf, _dotCanvas;
    VrOverlay _ov, _dotOv;
    public readonly EyeCalibRun Run = new();
    bool _on, _kept, _drawnLost;
    float _vis;
    int _drawn = -1, _look = -1, _dotLook = -1;
    double _startAt, _endAt = -1, _wake = double.PositiveInfinity;
    static readonly Vector2[] _diamond = new Vector2[4];
    readonly IPointerTarget[] _alone;               // what the lasers aim at while it shows (Aim): the board alone

    public Action<EyeCalibConfig> OnDone;           // a calibration that came out right: OrbitApp keeps it in the config
    public bool Showing => _on;
    public Surface Surf => _surf;                   // (--snapshot)

    public EyeCalibPanel(Gpu g, Painter p)
    {
        _g = g;
        _p = p;
        _ui = new Ui(p);
        _alone = new IPointerTarget[] { this };
    }

    // the settings' Calibrate...: the board in front of the head now; false without VR. Already showing: the run goes on as it is (10-24
    // review: a second Calibrate... started it over at point 1). (VrOverlay.Fake: the made-up SteamVR of the --snapshot input check)
    public bool Start(VrSession vr, double now)
    {
        if (_on) return true;
        if (vr == null || !(vr.Connected || VrOverlay.Fake) || !vr.HmdValid) return false;
        Run.Start(vr.Hmd, now);
        _on = true;
        _kept = false;
        _startAt = now;
        _endAt = -1;
        _drawn = -1;
        if (_ov == null)
        {
            _ov = new VrOverlay("erik.orbit.eyecalib", "OrbitOverlay eye calibration");
            _ov.SetSortOrder(175);                  // over the screens and panels, under the notifications and the lasers
            _ov.SetFlag(VROverlayFlags.IsPremultiplied, true);
            _ov.SetMouseScale(TW, TH);
            _dotOv = new VrOverlay("erik.orbit.eyecalib.dot", "OrbitOverlay eye calibration dot");
            _dotOv.SetSortOrder(176);
            _dotOv.SetFlag(VROverlayFlags.IsPremultiplied, true);
            _vis = 0;
        }
        return true;
    }

    // a new reading (OrbitApp, each fresh one): the tracker's own look, the eyes' place in the head, the head of the reading's time
    public void Feed(Vector3 tracker, Vector3 origin, in Matrix4x4 head, double now)
    {
        if (_on) Run.Feed(tracker, origin, head, now);
    }

    // a frame (after the lasers): the buttons, the run's steps, the board and the dot
    public void Update(VrSession vr, float dt, double now)
    {
        if (_ov == null) return;
        if (_on)
        {
            Run.Tick(now);
            if (now - _startAt > 0.3)                       // (not the press that opened it)
                for (int h = 0; h < 2 && _on; h++)
                {
                    ref var hs = ref vr.Hands[h];
                    if (hs.SecondaryPressed && Run.Running) { Run.Cancel(); _on = false; }
                    else if (hs.ClickPressed) { if (Run.Ended) _on = false; else Run.Mark(now); }
                }
            if (Run.Ended && _endAt < 0) _endAt = now;
            if (Run.State == EyeCalibRun.Phase.Done && !_kept) { _kept = true; OnDone?.Invoke(Run.Result); }
            if (Run.State == EyeCalibRun.Phase.Cancelled || (_endAt >= 0 && now - _endAt > ShowEnd)) _on = false;
        }
        _vis = Motion.Step(_vis, _on, dt, 0.22f);          // Look > Motion off: at once
        if (_vis <= 0 && !_on) { Release(); return; }
        float e = 1 - MathF.Pow(1 - _vis, 3);
        var board = Run.Board;
        _ov.SetAbsolute(board);
        _ov.SetWidth(BoardW * (0.94f + 0.06f * e));
        _ov.SetAlpha(e);
        if (_drawn != Run.Version || _look != Theme.Version || Lost(now) != _drawnLost || Clock.Now >= _wake
            || (_vis < 1 && Theme.Classic && Theme.RetroSkin.AnimatesFade))
        {
            Draw(now, false);
            _ov.SetTexture(_surf.Tex);
        }
        _ov.Show();
        var at = Run.Dot(now, out float size);
        if (size > 0 && _on)
        {
            if (_dotLook != Theme.Version) DrawDotTex();
            _dotOv.SetAbsolute(Matrix4x4.CreateTranslation(at.X, at.Y, 0.002f) * board);
            _dotOv.SetWidth(DotW * size);
            _dotOv.SetAlpha(e);
            _dotOv.Show();
        }
        else _dotOv.Hide();
    }

    // VR gone: everything let go at once
    public void Stop()
    {
        Run.Cancel();
        Release();
    }

    void Release()
    {
        _ov?.Dispose(); _ov = null;
        _dotOv?.Dispose(); _dotOv = null;
        _surf?.Dispose(); _surf = null;
        _canvas95?.Dispose(); _canvas95 = null;
        _dotSurf?.Dispose(); _dotSurf = null;
        _dotCanvas?.Dispose(); _dotCanvas = null;
        _on = false;
        _vis = 0;
        _drawn = _look = _dotLook = -1;
    }

    public void Dispose() => Release();

    // no reading for a second while it runs: the eyes are not tracked (the board says so)
    bool Lost(double now) => Run.Running && now - Run.LastReading > 1;

    // --snapshot: the board at a made-up moment (the dot drawn on it, as the headset shows the two together)
    public void Picture(int index, EyeCalibRun.Phase state, EyeCalibConfig result = null, string why = null)
    {
        Run.Picture(Matrix4x4.Identity, index, state, 0, result, why);
        Draw(0, true);
    }

    // ---------------- drawing ----------------

    public void Draw(double now, bool withDot)
    {
        _look = Theme.Version;
        _drawn = Run.Version;
        _drawnLost = Lost(now);
        _surf ??= new Surface(_g, TW, TH);
        if (Theme.Classic)
        {
            _canvas95 ??= new Surface(_g, W95, H95);
            _ui.Begin(_ptr, 2);
            _ui.Canvas95 = new SizeF(W95, H95);
            _p.BeginPixel(_canvas95.Bitmap, 0x00000000);
            Draw95(now, withDot);
            _p.End();
            _p.Begin(_surf.Bitmap, 0x00000000);
            _p.ImageNearest(_canvas95.Bitmap, new RectangleF(0, 0, TW, TH));
            _p.End();
            _ui.End();
        }
        else
        {
            _ui.Begin(_ptr, 1);
            _p.Begin(_surf.Bitmap, 0x00000000);
            if (Theme.Cyber) DrawCyber(now, withDot);
            else DrawModern(now, withDot);
            _p.End();
            _ui.End();
        }
        _wake = AnimWake.Take();
    }

    // what the board says under its title (English: the painter puts it in the language picked)
    string Say(double now) => Run.State switch
    {
        EyeCalibRun.Phase.Done => Lang.F("The eye cursor was {0:0.0}° off, now {1:0.0}°.", Run.Result.Before, Run.Result.After),
        EyeCalibRun.Phase.Failed => Run.Why,
        _ => Lost(now) ? "Your eyes are not tracked now" : "Look at the dot. It moves on once your look rests on it - or press the trigger.",
    };

    // a point's place in the board's pixels (scale 1: the modern texture, 0.5: the retro canvas)
    static PointF Px(int i, float scale)
    {
        var l = EyeCalibRun.Local(i);
        return new PointF((TW / 2f + l.X * PxPerM) * scale, (TH / 2f - l.Y * PxPerM) * scale);
    }

    // a taken point's two spots at the end (metres on the board, from the point's place): where the tracker put the look, and where it
    // is with the calibration - on the board at their true size (1 deg is 24 px of the modern texture)
    (Vector2 before, Vector2 after) Spots(int i)
    {
        var q = Run.Taken[i];
        var c = Run.Result;
        const float k = EyeCalibRun.Distance * MathF.PI / 180;
        float ex = q.Mx + q.Rx, ey = q.My + q.Ry;                                      // the dot, in the head (degrees)
        float ax = c.Xx * q.Mx + c.Xy * q.My + c.Yaw, ay = c.Yx * q.Mx + c.Yy * q.My + c.Pitch;
        return (new Vector2(-q.Rx, -q.Ry) * k, new Vector2(ax - ex, ay - ey) * k);
    }

    static void Cross(Painter p, PointF c, float arm, uint col, float w = 2)
    {
        p.Fill(new RectangleF(c.X - arm, c.Y - w / 2, 2 * arm, w), col);
        p.Fill(new RectangleF(c.X - w / 2, c.Y - arm, w, 2 * arm), col);
    }

    // the modern card: the title with its eye, what to do, the points' places (a cross to come, a ticked one taken), a pill a point; at
    // the end a tick (or a warning) with what came of it and, kept, each point's two spots - the tracker's look ringed in grey on a line
    // from the point, the calibrated one in the accent
    void DrawModern(double now, bool withDot)
    {
        var p = _p;
        var all = new RectangleF(0, 0, TW, TH);
        p.Fill(all, Theme.OverlayBg, 28);
        p.Stroke(RectangleF.Inflate(all, -1, -1), 0x40FFFFFF, 2, 27);
        int n = EyeCalibRun.Count;
        bool ended = Run.Ended, ok = Run.State == EyeCalibRun.Phase.Done;
        if (ended)
        {
            uint c = ok ? Theme.Accent : Theme.Bad;
            p.Circle(60, 52, 23, Theme.WithAlpha(c, 0.18f));
            p.Icon(ok ? Theme.IcoCheck : "", new RectangleF(38, 30, 44, 44), c, 22);
        }
        else p.Icon(Theme.IcoShow, new RectangleF(40, 32, 40, 40), Theme.Accent, 30);
        p.Text(ended ? ok ? "Calibrated" : "Not calibrated" : "Eye calibration", new RectangleF(92, 28, TW - 132, 44), Theme.Text, 28, FontWeight.SemiBold);
        p.Text(Say(now), new RectangleF(92, 74, TW - 132, 28), Lost(now) ? Theme.Text : Theme.Dim, 17);
        if (!ended) p.Text("Keep your head still - move only your eyes.", new RectangleF(92, 102, TW - 132, 24), Theme.Faint, 15);
        for (int i = 0; i < n; i++)
        {
            var c = Px(i, 1);
            if (ok)
            {
                Cross(p, c, 11, Theme.Faint);
                var (b, a) = Spots(i);
                float bx = c.X + b.X * PxPerM, by = c.Y - b.Y * PxPerM;
                p.Line(c.X, c.Y, bx, by, Theme.WithAlpha(Theme.Dim, 0.55f), 1.5f);
                p.Ring(bx, by, 6, Theme.Dim, 2);
                p.Circle(c.X + a.X * PxPerM, c.Y - a.Y * PxPerM, 6.5f, Theme.Accent);
            }
            else if (!ended && i == Run.Index) continue;
            else if (!ended && i < Run.Index)
            {
                p.Circle(c.X, c.Y, 11, Theme.Accent);
                p.Icon(Theme.IcoCheck, new RectangleF(c.X - 11, c.Y - 11, 22, 22), Theme.OnAccent, 12);
            }
            else Cross(p, c, 11, Theme.Faint);
        }
        if (ended)
        {
            if (ok)
            {
                string t1 = Lang.T("without the calibration"), t2 = Lang.T("with it");
                float w1 = p.MeasureWidth(t1, 15), w2 = p.MeasureWidth(t2, 15), x = (TW - (20 + w1 + 34 + 20 + w2)) / 2;
                p.Ring(x + 7, 862, 6, Theme.Dim, 2);
                p.Text(t1, new RectangleF(x + 20, 848, w1 + 4, 28), Theme.Dim, 15);
                x += 20 + w1 + 34;
                p.Circle(x + 7, 862, 6.5f, Theme.Accent);
                p.Text(t2, new RectangleF(x + 20, 848, w2 + 4, 28), Theme.Dim, 15);
            }
            p.Text("Press the trigger to close", new RectangleF(40, 898, TW - 80, 26), Theme.Dim, 15, FontWeight.Normal, TextAlignment.Center);
            return;
        }
        const float pw = 40, gap = 10;
        float x0 = (TW - (n * pw + (n - 1) * gap)) / 2;
        for (int i = 0; i < n; i++)
            p.Fill(new RectangleF(x0 + i * (pw + gap), 846, pw, 8), i < Run.Index ? Theme.Accent : i == Run.Index ? Theme.WithAlpha(Theme.Accent, 0.45f) : Theme.Panel3, 4);
        p.Text(Lang.F("Point {0} of {1}", Run.Index + 1, n), new RectangleF(40, 864, TW - 80, 30), Theme.Text, 17, FontWeight.SemiBold, TextAlignment.Center);
        p.Text("Trigger: this point · B / Y: cancel", new RectangleF(40, 898, TW - 80, 26), Theme.Dim, 15, FontWeight.Normal, TextAlignment.Center);
        if (withDot)
        {
            var at = Run.Dot(now, out float s);
            DotModern(p, TW / 2f + at.X * PxPerM, TH / 2f - at.Y * PxPerM, DotW * PxPerM * s);
        }
    }

    // Cyber: the console with its yellow tag and the point count in cyan monospace, the places as ticks (a taken one a cyan block in
    // brackets), the progress as five segments, the hints in monospace capitals; at the end the tag in cyan (red: not kept) with the
    // numbers, each point's tracker look a grey ring, the calibrated one a cyan block
    void DrawCyber(double now, bool withDot)
    {
        var p = _p;
        var all = new RectangleF(0, 0, TW, TH);
        CyberHud.ConsoleFrame(p, all, Theme.WithAlpha(CyberHud.Page, 0.94f), 22);
        int n = EyeCalibRun.Count;
        bool ended = Run.Ended, ok = Run.State == EyeCalibRun.Phase.Done;
        if (ended)
        {
            CyberHud.Tag(p, 40, 36, ok ? "CALIBRATED" : "NOT CALIBRATED", fill: ok ? CyberHud.Cyan : CyberHud.Red, ink: CyberHud.Ink, size: 15, h: 22);
            if (ok) CyberHud.MonoAt(p, Lang.F("{0:0.0}° → {1:0.0}°", Run.Result.Before, Run.Result.After), TW - 40 - 260, 54, 260, CyberHud.Cyan, 15,
                FontWeight.Bold, TextAlignment.Trailing);
        }
        else
        {
            CyberHud.Tag(p, 40, 36, "EYE CALIBRATION", size: 15, h: 22);
            CyberHud.MonoAt(p, Lang.F("POINT {0}/{1}", Run.Index + 1, n), TW - 40 - 260, 54, 260, CyberHud.Cyan, 15, FontWeight.Bold, TextAlignment.Trailing);
        }
        p.Text(Say(now), new RectangleF(40, 72, TW - 80, 28), Lost(now) || (ended && !ok) ? CyberHud.Red : CyberHud.White, 18);
        if (!ended) p.Text("Keep your head still - move only your eyes.", new RectangleF(40, 100, TW - 80, 24), CyberHud.Grey, 15);
        p.Fill(new RectangleF(40, 134, TW - 80, 1), CyberHud.Line);
        for (int i = 0; i < n; i++)
        {
            var c = Px(i, 1);
            if (ok)
            {
                var (b, a) = Spots(i);
                float bx = c.X + b.X * PxPerM, by = c.Y - b.Y * PxPerM, ax = c.X + a.X * PxPerM, ay = c.Y - a.Y * PxPerM;
                CyberHud.Brackets(p, new RectangleF(c.X - 13, c.Y - 13, 26, 26), CyberHud.Faint, 7, 0, 1.5f);
                p.Line(c.X, c.Y, bx, by, CyberHud.Faint, 1.5f);
                p.Ring(bx, by, 6, CyberHud.Grey, 2);
                p.Fill(new RectangleF(ax - 5, ay - 5, 10, 10), CyberHud.Cyan);
            }
            else if (!ended && i == Run.Index) continue;
            else if (!ended && i < Run.Index)
            {
                p.Fill(new RectangleF(c.X - 5, c.Y - 5, 10, 10), CyberHud.Cyan);
                CyberHud.Brackets(p, new RectangleF(c.X - 13, c.Y - 13, 26, 26), CyberHud.CyanDim, 7, 0, 1.5f);
            }
            else
            {
                p.Fill(new RectangleF(c.X - 12, c.Y - 1, 7, 2), CyberHud.Faint); p.Fill(new RectangleF(c.X + 5, c.Y - 1, 7, 2), CyberHud.Faint);
                p.Fill(new RectangleF(c.X - 1, c.Y - 12, 2, 7), CyberHud.Faint); p.Fill(new RectangleF(c.X - 1, c.Y + 5, 2, 7), CyberHud.Faint);
            }
        }
        p.Fill(new RectangleF(40, 826, TW - 80, 1), CyberHud.Line);
        if (ended)
        {
            if (ok)
            {
                string t1 = Lang.Upper("without the calibration"), t2 = Lang.Upper("with it");
                float w1 = CyberHud.MonoWidth(p, t1, 13), w2 = CyberHud.MonoWidth(p, t2, 13), x = (TW - (20 + w1 + 34 + 20 + w2)) / 2;
                p.Ring(x + 7, 856, 6, CyberHud.Grey, 2);
                CyberHud.MonoAt(p, t1, x + 20, 861, w1 + 4, CyberHud.Grey, 13);
                x += 20 + w1 + 34;
                p.Fill(new RectangleF(x + 2, 851, 10, 10), CyberHud.Cyan);
                CyberHud.MonoAt(p, t2, x + 20, 861, w2 + 4, CyberHud.Grey, 13);
            }
            p.Text(Lang.Upper("Press the trigger to close"), new RectangleF(40, 880, TW - 80, 30), CyberHud.Grey, 14, FontWeight.SemiBold,
                TextAlignment.Center, ParagraphAlignment.Center, CyberHud.Mono);
        }
        else
        {
            CyberHud.Segments(p, new RectangleF(TW / 2f - 150, 846, 300, 10), Run.Index / (float)n, CyberHud.Cyan, CyberHud.Track, n);
            p.Text(Lang.Upper("Trigger: this point · B / Y: cancel"), new RectangleF(40, 880, TW - 80, 30), CyberHud.Grey, 14, FontWeight.SemiBold,
                TextAlignment.Center, ParagraphAlignment.Center, CyberHud.Mono);
        }
        CyberHud.Brackets(p, all, CyberHud.Cyan, 14, 10, 2, topRight: true, bottomLeft: false);
        if (withDot && !ended)
        {
            var at = Run.Dot(now, out float s);
            DotCyber(p, TW / 2f + at.X * PxPerM, TH / 2f - at.Y * PxPerM, DotW * PxPerM * s);
        }
    }

    // the retro styles (the half-size canvas): the era's window, the targets in a white well, how far it got under it, the hints in its
    // status bar - the skin draws every part as its style did. At the end the message box's icon and words in the well, each point's
    // tracker look a hollow square, the calibrated one a filled one, and what they mean where the progress bar was
    void Draw95(double now, bool withDot)
    {
        var p = _p;
        var ui = _ui;
        var all = new RectangleF(0, 0, W95, H95);
        if (Theme.Retro == RetroStyle.Mac) ((MacSkin)Skins.Mac).MacWindow(p, all, "Eye Calibration");
        else
        {
            p.Fill(all, Theme.W95Face);
            p.Bevel(all, Painter.Edge.Window);
            ui.Caption95(new RectangleF(3, 3, W95 - 6, 18), "Eye Calibration");
        }
        var well = new RectangleF(8, 27, W95 - 16, 402);
        p.Fill(well, Theme.W95Window);
        p.Bevel(well, Painter.Edge.Sunken);
        int n = EyeCalibRun.Count;
        bool ended = Run.Ended, ok = Run.State == EyeCalibRun.Phase.Done;
        if (ended)
        {
            W95Icons.Draw(p, W95Icons.Id.Info32, 18, 36);
            ui.Text95(ok ? "Calibration complete" : "Calibration failed", new RectangleF(60, 38, W95 - 84, 15), K95, true);
            ui.Text95(Say(now), new RectangleF(60, 56, W95 - 84, 28), K95, false, TextAlignment.Leading, true, true);
        }
        else ui.Text95(Say(now), new RectangleF(16, 33, W95 - 32, 28), K95, false, TextAlignment.Leading, true, true);
        const float s95 = PxPerM / 2;
        for (int i = 0; i < n; i++)
        {
            var c = Px(i, 0.5f);
            float x = MathF.Round(c.X), y = MathF.Round(c.Y);
            if (ok)
            {
                p.Fill(new RectangleF(x - 4, y, 9, 1), Theme.W95Shadow);
                p.Fill(new RectangleF(x, y - 4, 1, 9), Theme.W95Shadow);
                var (b, a) = Spots(i);
                float bx = MathF.Round(x + b.X * s95), by = MathF.Round(y - b.Y * s95), ax = MathF.Round(x + a.X * s95), ay = MathF.Round(y - a.Y * s95);
                p.Stroke(new RectangleF(bx - 2.5f, by - 2.5f, 6, 6), Theme.W95Shadow, 1);
                p.Fill(new RectangleF(ax - 2, ay - 2, 5, 5), Theme.W95Navy);
            }
            else if (!ended && i == Run.Index) continue;
            else if (!ended && i < Run.Index) p.Fill(new RectangleF(x - 3, y - 3, 7, 7), Theme.W95Navy);
            else
            {
                p.Fill(new RectangleF(x - 4, y, 9, 1), Theme.W95Shadow);
                p.Fill(new RectangleF(x, y - 4, 1, 9), Theme.W95Shadow);
            }
        }
        if (ended)
        {
            if (ok)
            {
                float w1 = ui.Width95("Without the calibration"), x = 12;
                p.Stroke(new RectangleF(x + 0.5f, 437.5f, 6, 6), Theme.W95Shadow, 1);
                ui.Text95("Without the calibration", new RectangleF(x + 11, 434, w1 + 4, 15), K95);
                x += 11 + w1 + 18;
                p.Fill(new RectangleF(x + 1, 438, 5, 5), Theme.W95Navy);
                ui.Text95("With it", new RectangleF(x + 11, 434, 120, 15), K95);
            }
            ui.Pane95(new RectangleF(5, 455, W95 - 10, 19), "Press the trigger to close.");
            return;
        }
        ui.Text95(Lang.F("Point {0} of {1}", Run.Index + 1, n), new RectangleF(10, 434, 96, 15), K95);
        ui.Progress95(new RectangleF(108, 434, W95 - 118, 15), Run.Index / (float)n);
        ui.Pane95(new RectangleF(5, 455, W95 - 10, 19), "Keep your head still.   Trigger: this point   B / Y: cancel");
        if (withDot)
        {
            var at = Run.Dot(now, out float s);
            Dot95(p, W95 / 2f + at.X * PxPerM / 2, H95 / 2f - at.Y * PxPerM / 2, DotW * PxPerM / 2 * s);
        }
    }

    // the dot's own texture (its overlay sizes it): the style's target, the retro one on a 32 px canvas shown in whole pixels
    void DrawDotTex()
    {
        _dotLook = Theme.Version;
        _dotSurf ??= new Surface(_g, DotTex, DotTex);
        if (Theme.Classic)
        {
            _dotCanvas ??= new Surface(_g, DotCanvas95, DotCanvas95);
            _p.BeginPixel(_dotCanvas.Bitmap, 0x00000000);
            Dot95(_p, DotCanvas95 / 2f, DotCanvas95 / 2f, DotCanvas95);
            _p.End();
            _p.Begin(_dotSurf.Bitmap, 0x00000000);
            _p.ImageNearest(_dotCanvas.Bitmap, new RectangleF(0, 0, DotTex, DotTex));
            _p.End();
        }
        else
        {
            _p.Begin(_dotSurf.Bitmap, 0x00000000);
            if (Theme.Cyber) DotCyber(_p, DotTex / 2f, DotTex / 2f, DotTex);
            else DotModern(_p, DotTex / 2f, DotTex / 2f, DotTex);
            _p.End();
        }
        _dotOv.SetTexture(_dotSurf.Tex);
    }

    // the targets, d across at (cx, cy): modern a white disc in an accent ring with a soft halo, its dark middle the point to look at
    static void DotModern(Painter p, float cx, float cy, float d)
    {
        float r = d / 2;
        p.Circle(cx, cy, r, Theme.WithAlpha(Theme.Accent, 0.22f));
        p.Circle(cx, cy, r * 0.8f, Theme.Accent);
        p.Circle(cx, cy, r * 0.6f, 0xFFFFFFFF);
        p.Circle(cx, cy, r * 0.14f, 0xFF0B1220);
    }

    // Cyber: a cyan reticle - a ring, four ticks, the yellow diamond in the middle
    static void DotCyber(Painter p, float cx, float cy, float d)
    {
        float r = d / 2, w = MathF.Max(2, d * 0.05f), t = r * 0.28f;
        p.Circle(cx, cy, r * 0.62f, Theme.WithAlpha(CyberHud.Cyan, 0.14f));
        p.Ring(cx, cy, r * 0.62f, CyberHud.Cyan, MathF.Max(1.5f, d * 0.035f));
        p.Fill(new RectangleF(cx - w / 2, cy - r, w, t), CyberHud.Cyan);
        p.Fill(new RectangleF(cx - w / 2, cy + r - t, w, t), CyberHud.Cyan);
        p.Fill(new RectangleF(cx - r, cy - w / 2, t, w), CyberHud.Cyan);
        p.Fill(new RectangleF(cx + r - t, cy - w / 2, t, w), CyberHud.Cyan);
        float k = r * 0.26f;
        _diamond[0] = new Vector2(cx, cy - k); _diamond[1] = new Vector2(cx + k, cy); _diamond[2] = new Vector2(cx, cy + k); _diamond[3] = new Vector2(cx - k, cy);
        p.Polygon(_diamond, 4, CyberHud.Yellow);
        p.Circle(cx, cy, r * 0.07f, CyberHud.Ink);
    }

    // the retro styles: a bullseye in the style's selection colour on its window colour, in whole pixels
    static void Dot95(Painter p, float cx, float cy, float d)
    {
        float r = MathF.Round(d / 2);
        p.Circle(cx, cy, r, Theme.W95Navy);
        p.Circle(cx, cy, r * 0.72f, Theme.W95Window);
        p.Circle(cx, cy, r * 0.46f, Theme.W95Navy);
        p.Fill(new RectangleF(MathF.Round(cx) - 1, MathF.Round(cy) - 1, 2, 2), Theme.W95Window);
    }

    // ---------------- the lasers: while it shows they see the board alone, drawn nowhere on it ----------------
    // (10-24 review: they only stopped on it - a panel nearer than the board still won their nearest hit. The VR settings open 0.7 m
    // ahead, and Calibrate... clicked there left the hand on it: the trigger meant for the first point pressed Calibrate... as well, its
    // release started the run over at point 1 - it never got past it -, and the end's "press the trigger to close" started a new one; a
    // hand that drifted pressed Reset, Use eye tracking or a key of the keyboard; with the eye cursor on a click or off a monitor beside
    // the board took a click; the settings' beam and dot lay over the board. OrbitApp aims the lasers and the eye cursor by Aim, holds
    // both hands back from the game while it shows and draws no beam that hits nothing meanwhile.)
    public IReadOnlyList<IPointerTarget> Aim(IReadOnlyList<IPointerTarget> all) => _on ? _alone : all;

    public VrOverlay Overlay => _ov;
    public bool Interactive => _on && _ov != null && _vis > 0.5f;
    public bool AcceptsFrom(Hand h) => true;
    public Vector2 TextureSize => new(TW, TH);
    public bool HidesLaser => true;
    public void PointerMove(Hand h, Vector2 px) { }
    public void PointerLeave(Hand h) { }
    public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px) { }     // (the trigger and B / Y are read in Update)
    public void PointerScroll(Hand h, float notches, Vector2 px) { }
    public bool Grabbable => false;
    public Matrix4x4 WorldTransform => Run.Board;
    public void GrabBegin(bool detach) { }
    public void GrabMove(Matrix4x4 world) { }
    public void GrabStickX(float v, float dt) { }
    public void GrabTwoHandBegin() { }
    public void GrabTwoHandScale(float ratio) { }
    public bool GrabSnapPreview => false;
    public void GrabEnd() { }
}
