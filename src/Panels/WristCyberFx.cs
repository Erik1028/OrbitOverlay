using System;
using System.Collections.Generic;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// The custom face's motion in the Cyber style (the shapes: CyberFx in src/Gfx/CyberFx.cs). What moves, when, for how long, and at
// which Look > Motion level (Full: all of it; Reduced: only the short transitions marked so; Off: nothing - the settled picture):
//  - the boot (Full) when the watch comes into view (Intro): the olive frame draws in from its corners (0-0.32 s); then card by
//    card from the top-left, 36 ms apart: yellow brackets lock on, its outline draws in from the corners (a cyan pen at the ends),
//    a cyan scan line reveals it top to bottom, its tag wipes in and types its letters, the note types in, values count up,
//    segments fill block by block, the dial's hands sweep to the time, the frame bars rise left to right, the time card's digits
//    glitch and settle, its hazard stripes slide in. About 1.05 s, every frame.
//  - the glance (Full; Erik 10-19: "a wristwatch cyberpunk animacioi nem mukodnek ha ranezek"): the watch back in view within 20 s
//    of the last look - no boot (its scan hides the cards: 10-18 "very responsive"), the face is there at once, but the HUD answers
//    the look: card by card (20 ms apart) yellow brackets lock on and a cyan line runs down it over what it shows, the time card's
//    digits glitch once, the loops wake. About 0.45 s.
//  - the minute glitch (Full): the yellow time card on a new minute - the red / cyan split jitters wider, slices of the digits shift
//    sideways - 0.22 s in six 37 ms steps (a frame each), then the static offset again.
//  - the page swipe (Full): a cyan scan line with its trail on the seam between the two pages (the slide's own frames).
//  - the unfold (Reduced): an opened view - yellow brackets lead the card from its tile to its place and fade as it lands (and
//    lead it back on closing); its tag types in (Full).
//  - a tap (the bracket snap: Reduced; the flash: Full): a button's corners snap in from 10 px out, a light flash fades - 0.26 s.
//  - on / off (Reduced): a button tile's cyan fill wipes in from the left, out to the left - ~0.25 s eased.
//  - a value's update (Full): a reading that changed flickers - a red and a cyan ghost beside it, 2 steps of 70 ms (a frame
//    each); at most once per value every 6 s (a frame rate changes twice a second: a flicker every time would be noise).
//  - a note's update (Full): the grey note at a card's top-right types in again when it says something new (0.1-0.5 s).
//  - the inbox (Full): a new notification - the INBOX tag and "N NEW" blink three times (2.5 Hz, 1.2 s), the newest row types in.
//  - loops, only while one is on the page in view (Full; 10 frames a second): a warning (a red value or bar: a low battery, a low
//    frame rate) pulses softly (100-60 %, 1.8 s); the live microphone's dot breathes (2.4 s). They run for 8 s after the watch
//    comes into view, a page is shown, a warning appears or the watch is tapped - then they rest (the full red, the dot lit): a
//    glance sees them alive, a watch left in view (Always visible) does not draw 10 frames a second for hours.
//  - the hazard stripes (Full) crawl a quarter of their pitch with every second - drawn in the frame the time card's seconds ask
//    for anyway (no frame of their own).
//  - a scan sweep (Full): every 7-12 s a thin cyan line sweeps down one card (the frame graph every other time), 0.7 s at 30
//    frames a second.
// Frames: WristPanel.Update draws while Animating (the boot, a tap, an unfold, a wipe, a note typing) and by the earliest frame a
// scheduled effect needs (a glitch or flicker step, a blink, a sweep's next frame or its start, the loops' tick): CyWant asks
// AnimWake for it, as every style's animation does. With nothing of that on the page nothing is asked: Cyber adds no frame to the
// watch's own (the seconds, the readings).
// The effects that start by themselves run only in the headset (the overlay showing) or in a tool that asks (CyberLive): the
// settings' preview and every --snapshot picture are the settled face.
public sealed partial class WristPanel
{
    public bool CyberLive;                          // --perfbench / --animshot: run the effects as on the headset

    bool CyLiveFx => CyberFx.Full && (CyberLive || Ov?.Visible == true);

    // the loops (a warning's pulse, the microphone's dot) run until then (8 s after the watch came into view, a page, a warning, a tap)
    double _cyLoopsUntil;
    int _cyPage = -1;
    bool _cyWarnNow, _cyWarnPrev;
    bool CyLoops => CyLiveFx && _cyT < _cyLoopsUntil;
    float CyLoopAmp => CyberFx.Clamp01((float)(_cyLoopsUntil - _cyT));          // the last second: they ease to rest
    void CyWake() => _cyLoopsUntil = Clock.Now + 8;

    // ---- the frame's bookkeeping: the flags of the frame being drawn (Ui.BeginStamp says which) ----
    long _cyStamp = -1;
    bool _cyBusy;                                   // an effect needs every frame
    double _cyDue = double.MaxValue;                // the next frame a scheduled effect needs
    double _cyT;                                    // the frame's time (Clock.Now)
    uint _cyRed = CyberHud.Red;                     // the warnings' red in this frame (pulsing)

    void CyFrame()
    {
        long s = _ui.BeginStamp;
        if (s == _cyStamp) return;
        _cyStamp = s;
        _cyBusy = false;
        _cyDue = double.MaxValue;
        _cyT = Clock.Now;
        _cyWarnPrev = _cyWarnNow;
        _cyWarnNow = false;
        if (CurPage != _cyPage) { _cyPage = CurPage; CyWake(); }
        if (CyLoops)
        {
            float ph = 0.5f + 0.5f * MathF.Cos((float)(_cyT % 1.8 / 1.8) * MathF.Tau);
            _cyRed = Theme.Mix(CyberHud.Red, CyberHud.Card, 0.4f * (1 - ph) * CyLoopAmp);
        }
        else _cyRed = CyberHud.Red;
        CyScanFrame();
    }

    void CyWant(double t) { if (t < _cyDue) _cyDue = t; AnimWake.At(t); }      // the panel takes it right after drawing (AnimWake.cs)
    void CyWantLoop() { if (CyLoops) CyWant(_cyT + 0.1); }          // the loops' tick: 10 frames a second

    // Animating / --perfbench: this frame's wishes (none once another frame - another style, another face - was drawn)
    bool CyberAnimating => _cyBusy && _cyStamp == _ui.BeginStamp && Theme.Cyber;

    // --perfbench: when Cyber asks for the next frame (MaxValue: never)
    public double CyberNextFrame => _cyStamp == _ui.BeginStamp && Theme.Cyber ? _cyDue : double.MaxValue;
    public bool CyberMoving => CyberAnimating;

    // the warnings' red (a value, a bar): pulsing - and the loop's tick asked for
    uint CyWarn()
    {
        if (!_cyWarnNow) { _cyWarnNow = true; if (!_cyWarnPrev) CyWake(); }       // a warning appeared: the loops run again
        CyWantLoop();
        return _cyRed;
    }

    // ================= the boot =================

    double _cyBootAt = -100;
    const double CyBootLen = 1.05;                  // (in the boot's own time: it runs CyBootSpeed times as fast as the clock)
    const double CyBootSpeed = 1.5;                 // the whole boot in 0.7 s, not 1.05 - the same moves, sooner (Erik 10-18: "very responsive")
    double CyBootT => (_cyT - _cyBootAt) * CyBootSpeed;
    const float CyCardLen = 0.62f;                  // a card's own part of it
    const float CyEdgeAt = 0.32f;                   // the card's outline is its own again (the scan has passed)

    // Intro (the watch came into view): the boot; Reduced / Off - the face as it is
    void CyberBoot()
    {
        CyberSettleFx();
        _cyBootAt = CyberFx.Full ? Clock.Now : -100;
        _cyScanNext = Clock.Now + CyBootLen + 4;
        _cyLoopsUntil = Clock.Now + CyBootLen + 8;
        _dirty = true;
    }

    // ================= the glance =================

    double _cyGlanceAt = -10;
    float _cyG = 99;                                // seconds into the card's glance (99: none)
    const float CyGlanceLen = 0.34f;                // a card's own part of it

    // back in view within 20 s (WristPanel.Update): the face as it is, the HUD answering the look; Reduced / Off - nothing
    void CyberGlance()
    {
        if (!CyberFx.Full) return;
        _cyGlanceAt = _cyGlitchAt = Clock.Now;
        CyWake();
        _dirty = true;
    }

    // CyCardBegin, not booting: the card's part of a glance (its brackets and line come in CyCardEnd)
    void CyGlanceBegin(WatchWidget w)
    {
        _cyG = 99;
        double gt = _cyT - _cyGlanceAt;
        if (gt < 0 || gt > 0.8 || !CyLiveFx) return;
        _cyG = (float)(gt - (w.X + w.Y * 1.25f) * 0.02);
        if (_cyG < CyGlanceLen) _cyBusy = true;
    }

    void CyGlanceOverlay(RectangleF r, float u)
    {
        if (u <= 0) return;
        // the brackets in from 9 px out (0-0.08 s), gone by 0.34 s; the line down the card over its content (0.04-0.28 s)
        float lk = CyberFx.EaseOut(CyberFx.Ramp(u, 0, 0.08)), fade = 1 - CyberFx.Ramp(u, 0.2, 0.14);
        if (fade > 0) CyberFx.Corners(_p, r, Theme.WithAlpha(CyberHud.Yellow, lk * fade), 9, 2, 2 + 9 * (1 - lk));
        float s = CyberFx.EaseInOut(CyberFx.Ramp(u, 0.04, 0.24));
        if (s > 0 && s < 1) CyberFx.ScanH(_p, r.X + 1, r.Right - 1, r.Y + 2 + (r.Height - 4) * s, CyberHud.Cyan, 0.85f);
    }

    // --snapshot: a glance now (the face shown again)
    public void CyberGlanceForTest() => CyberGlance();

    // Settle(): nothing in motion
    void CyberSettleFx()
    {
        _cyBootAt = -100;
        _cyGlanceAt = -10;
        _cyGlitchAt = _cyTapAt = _cyInboxAt = -10;
        _cyMinute = -1;
        _cyTapKey = null;
        _cyScanId = -1;
        _cyScanNext = double.MaxValue;
        _cyVals.Clear();
        _cyNoteStates.Clear();
        _cyU = 99;
        _cyCur = -1;
    }

    int _cyCur = -1;                                // the widget being drawn (-1: none - an opened view)
    float _cyU = 99;                                // seconds into its boot (99: none)
    bool _cyClip;
    // its outline as the boot draws it in (CyberCard sets it; a button's: its tile)
    RectangleF _cyEdgeR;
    uint _cyEdgeC;
    float _cyEdgeTL, _cyEdgeBR;

    static float CyScanK(float u) => CyberFx.EaseInOut(CyberFx.Ramp(u, 0.08, 0.24));

    // DrawPage, before a card: its boot (the scan's clip over its fill and content)
    void CyCardBegin(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r)
    {
        CyFrame();
        _cyCur = id;
        _cyU = 99;
        _cyEdgeR = r;
        _cyEdgeC = k.Button ? CyberHud.CyanDim : CyberHud.Edge;
        _cyEdgeTL = k.Button ? CyberHud.CardTL + 2 : CyberHud.CardTL;
        _cyEdgeBR = CyberHud.CardBR;
        CyScanCount(id, k);
        double bt = CyBootT;
        if (bt >= CyBootLen) { CyGlanceBegin(w); return; }
        _cyG = 99;
        _cyBusy = true;
        _cyU = (float)(bt - (0.03 + (w.X + w.Y * 1.25f) * 0.036));
        float s = CyScanK(_cyU);
        if (s >= 1) return;
        _p.PushClip(new RectangleF(r.X - 3, r.Y - 3, r.Width + 6, s <= 0 ? 0 : (r.Height + 6) * s));
        _cyClip = true;
    }

    // DrawPage, after it: the boot's lines over it, a scan sweep, done
    void CyCardEnd(int id, RectangleF r)
    {
        if (_cyClip) { _p.PopClip(); _cyClip = false; }
        if (_cyU < CyCardLen) CyBootOverlay(r, _cyU);
        else if (_cyG < CyGlanceLen) CyGlanceOverlay(r, _cyG);
        else if (id == _cyScanId && _cyU >= 99) CySweep(r);
        _cyU = 99;
        _cyG = 99;
        _cyCur = -1;
    }

    void CyBootOverlay(RectangleF r, float u)
    {
        if (u <= 0) return;
        // yellow brackets lock on (in from 9 px out, 0-0.1 s), gone by 0.45 s
        float lk = CyberFx.EaseOut(CyberFx.Ramp(u, 0, 0.1)), fade = 1 - CyberFx.Ramp(u, 0.3, 0.15);
        if (fade > 0) CyberFx.Corners(_p, r, Theme.WithAlpha(CyberHud.Yellow, lk * fade), 9, 2, 2 + 9 * (1 - lk));
        // the outline from the corners (0.02-0.2 s), a cyan pen at its ends; its own once the scan has passed
        if (u < CyEdgeAt)
            CyberFx.OutlineIn(_p, _cyEdgeR, _cyEdgeC, 1.25f, _cyEdgeTL, 0, _cyEdgeBR, 0, CyberFx.EaseOut(CyberFx.Ramp(u, 0.02, 0.18)), CyberHud.Cyan);
        float s = CyScanK(u);
        if (s > 0 && s < 1) CyberFx.ScanH(_p, r.X + 1, r.Right - 1, r.Y - 3 + (r.Height + 6) * s, CyberHud.Cyan, 1);
    }

    // while booting: the card's own outline waits (the boot draws it in)
    bool CyEdgeShown => _cyU >= CyEdgeAt;

    // the boot's progress of a part of the card: 0..1 over [from, from + len] of its time (1: not booting)
    float CyIn(float from, float len) => _cyU >= 99 ? 1 : CyberFx.Ramp(_cyU, from, len);

    // the page: the olive frame drawn in from its corners, the page's corners locked on in yellow
    void CyberPageBoot(RectangleF all)
    {
        float bt = (float)CyBootT;
        _p.FillCut(all, Theme.OverlayBg, 18, 18, 18, 18);
        CyberFx.OutlineIn(_p, RectangleF.Inflate(all, -0.5f, -0.5f), CyberHud.Frame, 2, 17, 17, 17, 17, CyberFx.EaseOut(CyberFx.Ramp(bt, 0, 0.32)), CyberHud.Yellow);
        float lk = CyberFx.EaseOut(CyberFx.Ramp(bt, 0, 0.12)), fade = 1 - CyberFx.Ramp(bt, 0.45, 0.25);
        if (fade > 0) CyberFx.Corners(_p, RectangleF.Inflate(all, -7, -7), Theme.WithAlpha(CyberHud.Yellow, lk * fade), 22, 3, -12 * (1 - lk));
        _cyBusy = true;
    }

    bool CyPageBooting { get { CyFrame(); return CyBootT < 0.7; } }

    // ================= the minute glitch =================

    int _cyMinute = -1;
    double _cyGlitchAt = -10, _cyMinuteAt = -10;
    const double CyGlitchLen = 0.22, CyGlitchStep = 0.037;

    // the time card's glitch now: its strength (0: the static picture) and the step's seed
    (float g, int seed) CyTimeGlitch(DateTime now)
    {
        if (_cyU < 99)
        {
            // the boot: hard at first, settling by 0.6 s - a new jitter every 40 ms
            float g0 = 1 - CyberFx.EaseOut(CyberFx.Ramp(_cyU, 0.18, 0.42));
            return (g0 < 0.02f ? 0 : g0, (int)(MathF.Max(0, _cyU) / 0.04f) + 911);
        }
        int m = now.Hour * 60 + now.Minute;
        if (m != _cyMinute)
        {
            // a new minute on a card in view (not one coming back into view after minutes on another page)
            if (_cyMinute >= 0 && _cyT - _cyMinuteAt < 3 && TestNow == null && CyLiveFx) _cyGlitchAt = _cyT;
            _cyMinute = m;
        }
        _cyMinuteAt = _cyT;
        double t = _cyT - _cyGlitchAt;
        if (t >= CyGlitchLen || t < 0 || !CyLiveFx) return (0, 0);
        int step = (int)(t / CyGlitchStep);
        CyWant(_cyGlitchAt + (step + 1) * CyGlitchStep);
        return (MathF.Pow(1 - (float)(t / CyGlitchLen), 0.6f), step * 7 + m * 131);
    }

    // the hazard stripes' crawl: a quarter pitch a second (the seconds' frames); the boot slides them in from the left
    float CyStripes(float pitch)
    {
        if (_cyU < 99) return -(1 - CyberFx.EaseOut(CyberFx.Ramp(_cyU, 0.1, 0.45))) * pitch * 5;
        return CyLiveFx ? Now.Second % 4 * pitch / 4 : 0;
    }

    // ================= values, notes =================

    sealed class CyVal { public string Last; public double At = -10, Next = -10, Seen; }
    readonly Dictionary<string, CyVal> _cyVals = new();

    // a value that changed flickers: 1, 0.4 (70 ms each), then 0 - once per value every 6 s at most
    float CyFlick(string key, string v)
    {
        if (key == null) return 0;
        if (!_cyVals.TryGetValue(key, out var s)) { _cyVals[key] = new CyVal { Last = v, Seen = _cyT }; return 0; }
        if (!string.Equals(s.Last, v, StringComparison.Ordinal))
        {
            s.Last = v;
            // (not one that was out of view - another page - and comes back changed)
            if (CyLiveFx && _cyU >= 99 && _cyT >= s.Next && _cyT - s.Seen < 1.5) { s.At = _cyT; s.Next = _cyT + 6; }
        }
        s.Seen = _cyT;
        double t = _cyT - s.At;
        if (t >= 0.14 || t < 0 || !CyLiveFx) return 0;
        CyWant(s.At + (t < 0.07 ? 0.07 : 0.14));
        return t < 0.07 ? 1 : 0.4f;
    }

    sealed class CyNoteSt { public string Last; public double At = -10, Seen; }
    readonly Dictionary<int, CyNoteSt> _cyNoteStates = new();

    // how much of a card's note shows: typed in by the boot, and again when it says something new
    float CyNoteIn(string n)
    {
        float k = CyIn(0.3f, 0.02f * n.Length + 0.04f);
        if (_cyCur < 0) return k;
        if (!_cyNoteStates.TryGetValue(_cyCur, out var s)) { _cyNoteStates[_cyCur] = new CyNoteSt { Last = n, Seen = _cyT }; return k; }
        if (!string.Equals(s.Last, n, StringComparison.Ordinal))
        {
            s.Last = n;
            if (CyLiveFx && _cyU >= 99 && _cyT - s.Seen < 1.5) s.At = _cyT;
        }
        s.Seen = _cyT;
        float t = (float)(_cyT - s.At), len = 0.02f * n.Length + 0.06f;
        if (t >= 0 && t < len && CyLiveFx) { _cyBusy = true; k = MathF.Min(k, t / len); }
        return k;
    }

    // monospace text ending at 'right', its first k typed (a block cursor after them)
    void CyTyped(string s, float right, float by, uint c, float k, float size = CyNote)
    {
        float tw = CyMonoW(s, size), x0 = right - tw, cw = tw / Math.Max(1, s.Length);
        int n = Math.Clamp((int)(k * s.Length), 0, s.Length);
        if (n > 0)
        {
            _p.PushClip(new RectangleF(x0 - 1, by - 30, n * cw + 1, 40));
            CyberHud.MonoAt(_p, s, x0, by, tw + 2, c, size, FontWeight.SemiBold);
            _p.PopClip();
        }
        if (n < s.Length)
        {
            float ch = CyberHud.CapH(_p, size);
            _p.Fill(new RectangleF(x0 + n * cw + 0.5f, by - ch, MathF.Max(2, cw - 1.5f), ch), Theme.WithAlpha(c, 0.9f));
        }
    }

    // ================= the inbox =================

    double _cyInboxAt = -10;
    DateTime _cyInboxNewest;

    // a new notification arrived (CyNotesNow): the blink
    void CyInboxSeen(DateTime newest, bool first)
    {
        if (newest == _cyInboxNewest) return;
        _cyInboxNewest = newest;
        if (!first && CyLiveFx) _cyInboxAt = _cyT;
    }

    // the blink's phase: (blinking, lit)
    (bool on, bool lit) CyInboxBlink()
    {
        double t = _cyT - _cyInboxAt;
        if (t < 0 || t >= 1.2 || !CyLiveFx) return (false, false);
        int ph = (int)(t / 0.2);
        CyWant(_cyInboxAt + (ph + 1) * 0.2);
        return (true, ph % 2 == 0);
    }

    // the newest row typing in (0..1)
    float CyInboxRow()
    {
        float k = CyberFx.Ramp(_cyT - _cyInboxAt, 0.05, 0.4);
        if (k < 1) _cyBusy = true;
        return k;
    }

    // ================= a tap =================

    string _cyTapKey;
    double _cyTapAt = -10;

    void CyTap(string key)
    {
        if (!CyberFx.Any) return;
        _cyTapKey = key;
        _cyTapAt = Clock.Now;
        CyWake();
        _dirty = true;
    }

    // over the button that was tapped: its corners snap in, a light flash fades (b: its shape now, tl / br its cuts)
    void CyTapFx(string key, RectangleF b, float tl, float br)
    {
        if (_cyTapKey == null || !string.Equals(key, _cyTapKey, StringComparison.Ordinal)) return;
        CyFrame();
        double t = _cyT - _cyTapAt;
        if (t >= 0.26 || t < 0) { _cyTapKey = null; return; }
        _cyBusy = true;
        if (Motion.Full)
        {
            float fa = 1 - (float)(t / 0.18);
            if (fa > 0) _p.FillCut(b, Theme.WithAlpha(0xFFFFFFFF, 0.26f * fa), tl, 0, br, 0);
        }
        float snap = CyberFx.EaseOut((float)(t / 0.12)), fade = 1 - CyberFx.Ramp(t, 0.16, 0.1);
        CyberFx.Corners(_p, b, Theme.WithAlpha(CyberHud.Cyan, fade), 8, 2, 2 + 10 * (1 - snap));
    }

    // a button tile's on / off: 0..1 of its fill (eased while Live and Motion allow; else at once)
    float CyOnOff(string key, bool on)
    {
        string ak = Ui.Id(key, "#on");
        return CyberFx.Any ? _ui.Anim(ak, on ? 1 : 0, 16) : _ui.AnimSnap(ak, on ? 1 : 0);
    }

    // ================= the scan sweep =================

    double _cyScanNext = double.MaxValue, _cyScanAt = -10;
    int _cyScanId = -1, _cyScanN;
    bool _cyScanGraph;                              // --perfbench / --animshot: the next one on the frame graph
    const double CyScanLen = 0.7;
    // the cards drawn in the frame before (a sweep picks one of them)
    readonly int[] _cyIds = new int[48], _cyIdsLast = new int[48];
    int _cyIdCount, _cyIdCountLast, _cyGraphId = -1, _cyGraphLast = -1;

    void CyScanCount(int id, WatchWidgets.Kind k)
    {
        if (_cyIdCount < _cyIds.Length) _cyIds[_cyIdCount++] = id;
        if (k.Id == "graph") _cyGraphId = id;
    }

    void CyScanFrame()
    {
        Array.Copy(_cyIds, _cyIdsLast, _cyIdCount);
        _cyIdCountLast = _cyIdCount;
        _cyIdCount = 0;
        _cyGraphLast = _cyGraphId;
        _cyGraphId = -1;
        if (!CyLiveFx || CyBootT < CyBootLen || _open != null || _swiping || _slideOther >= 0) { _cyScanId = -1; return; }
        if (_cyScanId >= 0)
        {
            if (_cyT - _cyScanAt < CyScanLen) { CyWant(_cyT + 1 / 30.0); return; }
            _cyScanId = -1;
            _cyScanNext = _cyT + 7 + 5 * CyberFx.Hash(_cyScanN, 3);
        }
        if (_cyScanNext == double.MaxValue) _cyScanNext = _cyT + 6;
        if (_cyT < _cyScanNext || _cyIdCountLast == 0) { CyWant(_cyScanNext); return; }
        _cyScanN++;
        bool graph = _cyGraphLast >= 0 && (_cyScanGraph || CyberFx.Hash(_cyScanN, 1) < 0.5f);
        _cyScanId = graph ? _cyGraphLast : _cyIdsLast[(int)(CyberFx.Hash(_cyScanN, 2) * _cyIdCountLast) % _cyIdCountLast];
        _cyScanGraph = false;
        _cyScanAt = _cyT;
        CyWant(_cyT + 1 / 30.0);
    }

    void CySweep(RectangleF r)
    {
        float s = (float)((_cyT - _cyScanAt) / CyScanLen);
        if (s < 0 || s >= 1) return;
        float a = MathF.Min(1, MathF.Min(s, 1 - s) * 7) * 0.75f;
        _p.PushClip(RectangleF.Inflate(r, -1.5f, -1.5f));
        CyberFx.ScanH(_p, r.X + 2, r.Right - 2, r.Y + 3 + (r.Height - 6) * CyberFx.EaseInOut(s), CyberHud.Cyan, a);
        _p.PopClip();
    }

    // ================= the page swipe, the unfold =================

    // the seam between two sliding pages (CyberDots: pos between two pages)
    void CySeam(float pos)
    {
        if (!CyberFx.Full) return;
        float f = pos - MathF.Floor(pos);
        if (f < 0.002f || f > 0.998f) return;
        float x = (1 - f) * TW;
        int dir = pos < CurPage ? -1 : 1;                 // to a later page the seam moves left (its trail at its right)
        float a = MathF.Min(1, MathF.Min(f, 1 - f) * 10);
        CyberFx.ScanV(_p, x, 3, TH - 3, CyberHud.Cyan, a, dir);
    }

    // an opened view's card: yellow brackets lead it from its tile to its place (and back), fading as it lands
    void CyberUnfold(RectangleF from, RectangleF target, float e)
    {
        CyFrame();
        if (!CyberFx.Any) return;
        float lead = _closing ? CyberFx.Clamp01(e * 1.3f - 0.3f) : CyberFx.Clamp01(e * 1.35f);
        float a = CyberFx.Clamp01(MathF.Min(e * 6, (1 - e) * 5));
        if (a <= 0.01f) return;
        var b = Lerp(from, target, CyberFx.EaseOut(lead));
        CyberFx.Corners(_p, b, Theme.WithAlpha(CyberHud.Yellow, a), 16, 3, 3);
    }

    // the opened view's tag: types in as it opens
    void CyberDetailTag(RectangleF e, string name)
    {
        float wipe = 1, type = 1;
        if (CyberFx.Full && !_closing)
        {
            double t = Clock.Now - _openStart;
            wipe = CyberFx.EaseOut(CyberFx.Ramp(t, 0.1, 0.1));
            type = CyberFx.Ramp(t, 0.16, 0.025 * name.Length);
            if (type < 1) _cyBusy = true;
        }
        CyberHud.Tag(_p, e.X + 24, e.Y + 23, name, wipe, type, size: 18, h: 26);
    }

    // ================= --perfbench / --animshot =================

    // start an effect now: "glitch", "scan" (on the frame graph if there is one), "inbox", "tap" (the first button of the page),
    // "open:<type>" (that widget opened, unfolding)
    public void CyberTest(string what)
    {
        double now = Clock.Now;
        switch (what)
        {
            case "glitch": _cyGlitchAt = now; break;
            case "scan": _cyScanNext = now; _cyScanGraph = true; _cyScanId = -1; break;
            case "inbox": _cyInboxAt = now; break;
            case "close": if (_open != null) CloseDetail(); break;
            case "tap":
                foreach (var w in Layout)
                    if (WatchWidgets.Find(w.Type) is { Button: true }) { CyTap(Ui.Id("wcb", Wid(w))); break; }
                break;
            default:
                if (what.StartsWith("open:", StringComparison.Ordinal) && Layout.Find(x => x.Type == what[5..]) is { } ow) OpenDetail(ow);
                break;
        }
        _dirty = true;
    }
}
