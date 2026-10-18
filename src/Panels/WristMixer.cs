using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Orbit;

// The volume mixer widget (1.14): the apps playing on the default output device (AudioMixer), each with its own volume - a slider per
// app (drag it with the laser, or the stick over it), a tap on an app's icon mutes it -, the active and the loudest first (the rows
// keep their order while the laser is on them). Sizes: 2 x 1 (the top two side by side), 2 x 2 and 3 x 2 (lists); on a 4 x 3 page M
// and L. A tap beside the controls opens it: every app, the device's own volume above them.
// Modern in the Bento look (a widget's own look too), Cyber in its forms (the tag, segments, a cyan mark); the retro styles through
// the 95 code - rows of 95 trackbars in a little window, and opened the Windows 95 Volume Control: a column per app with a standing
// trackbar and Mute - drawn by each style's skin (a standing trackbar is the skin's own, transposed: its light stays top-left).
public sealed partial class WristPanel
{
    AudioMixer _mixer;

    // a made-up mixer until the app hands it the real one: the tools never touch a real device
    public AudioMixer Mixer => _mixer ??= MixerFake.Standard();

    // the app: the default output device for the mixer and for the watch's volume
    public void UseRealAudio(AudioMixer m)
    {
        _mixer = m;
        Volume.Fake = false;
        Volume.Engine = m;
        Volume.Background = Mic.Background = true;     // their Core Audio calls on workers of their own, not in the watch's frame
    }

    // the tools: another made-up set
    public void UseMixer(AudioMixer m) => _mixer = m;

    public const string MixKindIcon = "\uE9E9";            // Segoe's equalizer: the widget in the editor's palette
    const string SpeakerGlyph = "\uE767";
    static readonly string[] CapMixer = { "Volume mixer", "Mixer" };
    const string NoApps = "No apps playing";
    const float KnobR = 7;                                   // the slider's knob (the bar is inset by it: it never leaves the card)

    readonly List<MixerApp> _mixShown = new();               // the rows as drawn (their order kept while the laser is on them)
    readonly List<MixerApp> _mixSrc = new();                 // the mixer's apps this frame (a copy: its worker changes the list)
    bool _mixHold, _mixHoldNext;
    int _mixVersion = -1;
    float _mixAcc;                                           // the stick's notches, whole ones passed on
    int _mixTop;                                             // the opened view's first row (the stick scrolls it)
    bool _mixWheelUsed, _mixWasOpen;
    readonly List<RectangleF> _mixCards = new();             // the mixer cards of the last frame (texture px): the stick over them
    readonly Dictionary<MixerApp, (ID2D1Bitmap big, ID2D1Bitmap small, int ver)> _mixIcons = new();
    // the tools: where each app's slider (level 0 and level 1) and its mute are, in texture px, in the last frame drawn
    readonly Dictionary<string, (Vector2 at0, Vector2 at1, Vector2 mute)> _mixHits = new();

    public IReadOnlyList<MixerApp> MixerShown => _mixShown;
    public bool MixerHit(string name, out Vector2 at0, out Vector2 at1, out Vector2 mute)
    {
        bool ok = _mixHits.TryGetValue(name, out var h);
        (at0, at1, mute) = h;
        return ok;
    }

    bool MixerOn => C.Face == "custom" && (HasWidget("mixer") || DetailIs("mixer"));

    // the list changed (a level, an app came or went): the watch draws again (WristPanel.Update)
    bool MixerChanged() => MixerOn && Mixer.Version != _mixVersion;

    // each frame a mixer is on the watch (SampleSources): the list wanted, the rows in their order
    void MixerFrame(double now)
    {
        var m = Mixer;
        m.Want(now);
        _mixVersion = m.Version;
        bool open = DetailIs("mixer");
        if (open && !_mixWasOpen) _mixTop = 0;           // opened: from its first row
        _mixWasOpen = open;
        _mixHold = _mixHoldNext;
        _mixHoldNext = false;
        _mixCards.Clear();
        _mixHits.Clear();
        _mixWheelUsed = false;
        if (!m.TryApps(_mixSrc)) return;                 // (the mixer's worker is reading the device: the rows of the last frame)
        var src = _mixSrc;
        if (!_mixHold)
        {
            _mixShown.Clear();
            _mixShown.AddRange(src);
        }
        else
        {
            // on hold: the ones that went are gone, a new one comes at the end
            for (int i = _mixShown.Count - 1; i >= 0; i--) if (!src.Contains(_mixShown[i])) _mixShown.RemoveAt(i);
            foreach (var a in src) if (!_mixShown.Contains(a)) _mixShown.Add(a);
        }
        if (_mixIcons.Count > src.Count + 8)
            foreach (var a in new List<MixerApp>(_mixIcons.Keys))
                if (!src.Contains(a)) { var e = _mixIcons[a]; e.big?.Dispose(); e.small?.Dispose(); _mixIcons.Remove(a); }
    }

    void MixDispose()
    {
        foreach (var e in _mixIcons.Values) { e.big?.Dispose(); e.small?.Dispose(); }
        _mixIcons.Clear();
    }

    // the stick: over a mixer card or in the opened mixer, whole notches go to the frame (the slider under the laser takes them,
    // in the opened view else the list scrolls)
    bool MixerScroll(Vector2 px, float n)
    {
        if (!MixerOn) return false;                  // (the cards of a page swiped away are not looked at)
        bool open = DetailIs("mixer") && !_closing;
        if (!open)
        {
            if (_open != null || C.Face != "custom") return false;
            bool over = false;
            foreach (var r in _mixCards) if (r.Contains(px.X, px.Y)) { over = true; break; }
            if (!over) return false;
        }
        _mixAcc += n;
        int k = (int)_mixAcc;
        if (k == 0) return true;
        _mixAcc -= k;
        _ptr.Wheel += k;
        _dirty = true;
        return true;
    }

    // the app's icon (null: none - its initial is drawn)
    ID2D1Bitmap MixIcon(MixerApp a, bool small)
    {
        if (a.Icon32 == null && a.Icon16 == null) return null;
        if (!_mixIcons.TryGetValue(a, out var e) || e.ver != a.IconVersion)
        {
            e.big?.Dispose();
            e.small?.Dispose();
            e = (a.Icon32 != null ? _p.CreateBitmap(a.Icon32, 32, 32) : null, a.Icon16 != null ? _p.CreateBitmap(a.Icon16, 16, 16) : null, a.IconVersion);
            _mixIcons[a] = e;
        }
        return small ? e.small : e.big;
    }

    static string MixPct(MixerApp a) => Pct(ShownPct(a.Level));

    // "Muted" in red - on a coloured card (a widget's own solid / two-tone look) in its text colour: red on amber would not read
    static uint MutedInk => Bento.CapBadge ? Bento.Text : Bento.BadInk;

    // ================= Modern (and Cyber) =================

    void MixerOrbit(RectangleF r, WatchWidget w, int id)
    {
        _mixCards.Add(r);
        if (_ui.OverNow(r)) _mixHoldNext = true;
        var apps = _mixShown;
        float pad = PadX(r);
        if (r.Height < 120) { MixColumns(r, id); return; }
        float top = r.Y + 34, bottom = r.Bottom - 8;
        int slots = Math.Max(1, (int)((bottom - top) / 44));
        float rh = (bottom - top) / slots;
        CaptionRow(r, apps.Count > slots ? $"+{apps.Count - slots}" : null, CapMixer);
        if (apps.Count == 0)
        {
            MixEmpty(r.X + pad, r.Y + MathF.Min(70, r.Height * 0.45f), r.Width - 2 * pad);
            return;
        }
        for (int i = 0; i < Math.Min(slots, apps.Count); i++)
            MixRow(new RectangleF(r.X + pad, top + i * rh, r.Width - 2 * pad, rh), apps[i], Ui.Id("wmx", id * 16 + i), false);
    }

    void MixEmpty(float x, float by, float w)
    {
        if (Theme.Cyber) CyMono(Ellipsize("NO APPS PLAYING", w, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, by, w + 2, CyG);
        else TextAt(Ellipsize(NoApps, w, SmallText, FontWeight.SemiBold), x, by, w + 2, Sub, SmallText, FontWeight.SemiBold);
    }

    // the 2 x 1: the top two side by side - the icon (a tap mutes it), the name and the level beside it, the slider under them
    void MixColumns(RectangleF r, int id)
    {
        var apps = _mixShown;
        var a = new RectangleF(r.X + 10, r.Y + 8, r.Width - 20, r.Height - 16);
        if (apps.Count == 0)
        {
            Caption(r, r.Width - 2 * PadX(r), CapMixer);
            MixEmpty(r.X + PadX(r), r.Y + MathF.Min(64, r.Height * 0.5f + 12), r.Width - 2 * PadX(r));
            return;
        }
        const float gap = 12;
        float cw = (a.Width - gap) / 2;
        for (int j = 0; j < Math.Min(2, apps.Count); j++)
        {
            var app = apps[j];
            var col = new RectangleF(a.X + j * (cw + gap), a.Y, cw, a.Height);
            string key = Ui.Id("wmx", id * 16 + j);
            const float s = 26, head = 40;
            var icon = new RectangleF(col.X, col.Y + (head - s) / 2 - 1, s, s);
            var mhit = new RectangleF(col.X - 6, col.Y, s + 12, head);
            MixMuteIcon(key, icon, mhit, app);
            float tx = icon.Right + 7, tw = col.Right - tx;
            if (Theme.Cyber)
            {
                CyMono(Ellipsize(Upper(app.Name), tw, CyNote, FontWeight.SemiBold, CyberHud.Mono), tx, col.Y + 15, tw + 2, app.Muted ? CyberHud.Faint : CyG);
                if (app.Muted) CyMono("MUTED", tx, col.Y + 35, tw + 2, CyR, CyNote, FontWeight.Bold);
                else CyValue(tx, col.Y + 36, MixPct(app), 22, CyW);
            }
            else
            {
                TextAt(Ellipsize(app.Name, tw, 15, FontWeight.SemiBold), tx, col.Y + 15, tw + 2, app.Muted ? Bento.Faint : Sub, 15, FontWeight.SemiBold);
                if (app.Muted) TextAt(Ellipsize("Muted", tw, SmallText, FontWeight.Bold), tx, col.Y + 36, tw + 2, MutedInk, SmallText, FontWeight.Bold);
                else TextAt(MixPct(app), tx, col.Y + 36, tw + 2, Bento.Text, 20, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
            }
            var bar = new RectangleF(col.X + KnobR, col.Bottom - 12, col.Width - 2 * KnobR, 5);
            MixSlider(Ui.Id(key, "s"), bar, new RectangleF(col.X - 4, col.Y + head, col.Width + 8, col.Height - head), app.Level, app.Muted,
                v => Mixer.SetApp(app, v));
            MixRecord(app, bar, icon, false);
        }
    }

    // A row of a list: the icon (a tap mutes it), the name with the level at its right, the slider under them. opened: the opened view's
    // row - bigger, a mute disc at its right
    void MixRow(RectangleF row, MixerApp app, string key, bool opened)
    {
        float h = row.Height;
        float s = opened ? 32 : MathF.Min(30, h - 14);
        var icon = new RectangleF(row.X, row.Y + (h - s) / 2, s, s);
        float tx = icon.Right + 10, right = row.Right;
        RectangleF disc = default;
        if (opened)
        {
            disc = new RectangleF(right - 42, row.Y + (h - 42) / 2, 42, 42);
            right = disc.X - 12;
            if (Press(Ui.Id(key, "x"), disc, app.Muted ? MuteGlyph : SpeakerGlyph, null, app.Muted, true, 18, true, accentFill: false, fill: Bento.Bad))
            {
                Mixer.ToggleAppMute(app);
                _dirty = true;
            }
            DrawMixIcon(icon, app, false);
        }
        else MixMuteIcon(key, icon, new RectangleF(row.X - 5, row.Y, s + 14, h), app);
        float ns = opened ? 19 : SmallText, by = row.Y + (opened ? 22 : 17 + (h - 44) * 0.25f);
        float tw = right - tx;
        if (Theme.Cyber)
        {
            string pct = app.Muted ? "MUTED" : MixPct(app);
            float pw = app.Muted ? CyMonoW(pct, CyNote, FontWeight.Bold) : CyValueW(pct, 18);
            TextAt(Ellipsize(app.Name, tw - pw - 10, ns, FontWeight.SemiBold, Theme.Display), tx, by, tw - pw - 8, app.Muted ? CyberHud.Faint : CyW, ns,
                FontWeight.SemiBold, TextAlignment.Leading, Theme.Display);
            if (app.Muted) CyMono(pct, right, by, pw + 2, CyR, CyNote, FontWeight.Bold, TextAlignment.Trailing);
            else CyValue(right - pw, by, pct, 18, CyC);
        }
        else
        {
            string pct = app.Muted ? "Muted" : MixPct(app);
            float ps = opened ? SmallText : 15;
            var pwt = app.Muted ? FontWeight.Bold : FontWeight.SemiBold;
            float pw = _p.MeasureWidth(pct, ps, pwt);
            TextAt(Ellipsize(app.Name, tw - pw - 10, ns, FontWeight.SemiBold), tx, by, tw - pw - 8, app.Muted ? Bento.Faint : Bento.Text, ns, FontWeight.SemiBold);
            TextAt(pct, right - pw - 2, by, pw + 4, app.Muted ? MutedInk : Sub, ps, pwt);
        }
        float bh = opened ? 6 : 5;
        var bar = new RectangleF(tx + KnobR, row.Bottom - (opened ? 17 : 12) - bh / 2, right - tx - 2 * KnobR, bh);
        MixSlider(Ui.Id(key, "s"), bar, new RectangleF(tx - 4, row.Y, right - tx + 8, h), app.Level, app.Muted, v => Mixer.SetApp(app, v));
        MixRecord(app, bar, opened ? disc : icon, opened);
    }

    // force: the opened view's (over the face's cards behind it)
    void MixRecord(MixerApp app, RectangleF bar, RectangleF mute, bool force)
    {
        if (app.Name == null || (!force && (_ui.Inert || _mixHits.ContainsKey(app.Name)))) return;
        float y = bar.Y + bar.Height / 2;
        _mixHits[app.Name] = (new Vector2(bar.X, y), new Vector2(bar.Right, y), new Vector2(mute.X + mute.Width / 2, mute.Y + mute.Height / 2));
    }

    // the icon as the mute button: it gives under a press; muted, it fades and a red crossed speaker sits on its corner
    void MixMuteIcon(string key, RectangleF icon, RectangleF hit, MixerApp app)
    {
        bool click = _ui.Clickable(Ui.Id(key, "m"), MinHit(hit), out bool hover, out bool held);
        if (hover) _mixHoldNext = true;
        float k = _ui.Spring(Ui.Id(key, "#ms"), held ? 0.88f : 1f, 420, 18);
        bool sc = MathF.Abs(k - 1) > 0.001f;
        if (sc) _p.PushScale(k, icon.X + icon.Width / 2, icon.Y + icon.Height / 2);
        if (hover && !Motion.Still) _p.Fill(RectangleF.Inflate(icon, 4, 4), Theme.WithAlpha(Theme.Cyber ? CyberHud.Card3 : Bento.TileHover, 0.9f), 10);
        DrawMixIcon(icon, app, app.Muted);
        if (sc) _p.PopScale();
        if (click) { Mixer.ToggleAppMute(app); _dirty = true; }
    }

    // the exe's icon (its initial on a coloured tile when it has none; the system sounds a speaker)
    void DrawMixIcon(RectangleF icon, MixerApp app, bool muted)
    {
        float op = muted ? 0.4f : 1;
        var bmp = MixIcon(app, false);
        if (bmp != null) _p.Image(bmp, icon, op);
        else if (app.System)
        {
            _p.Fill(icon, Theme.WithAlpha(Theme.Cyber ? CyberHud.Card2 : Bento.Tile, op), 8);
            _p.Icon(SpeakerGlyph, icon, Theme.WithAlpha(Theme.Cyber ? CyC : Bento.Text, op), MathF.Round(icon.Width * 0.55f));
        }
        else
        {
            _p.Fill(icon, Theme.WithAlpha(AvatarColor(app.Name), op), 8);
            _p.Text(InitialOf(app.Name), new RectangleF(icon.X, icon.Y, icon.Width, icon.Height + 1), Theme.WithAlpha(Bento.Dark, op),
                MathF.Round(icon.Width * 0.55f), FontWeight.Bold, TextAlignment.Center, ParagraphAlignment.Center);
        }
        if (!muted) return;
        // the crossed speaker on a red disc at the bottom right
        float d = MathF.Round(icon.Width * 0.62f), cx = icon.Right - d * 0.3f, cy = icon.Bottom - d * 0.3f;
        HaloDisc(cx, cy, d / 2 + 2);
        _p.Circle(cx, cy, d / 2, Theme.Cyber ? CyR : Bento.Bad);
        _p.Icon(MuteGlyph, new RectangleF(cx - d / 2, cy - d / 2, d, d), Theme.Cyber ? CyInk : 0xFFFFFFFF, MathF.Round(d * 0.58f));
    }

    // A bar with a knob: drag along it (or tap) to set 0..1, or the stick over it (2 % a notch). Cyber: segments with a cyan mark.
    void MixSlider(string id, RectangleF bar, RectangleF hit, float value, bool muted, Action<float> set)
    {
        _ui.Clickable(id, hit, out bool hover, out bool held);
        if (hover) _mixHoldNext = true;
        if (held)
        {
            _ui.DragHeld = true;
            _mixHoldNext = true;
            float nv = Math.Clamp((_ui.Mouse.X - bar.X) / bar.Width, 0, 1);
            if (MathF.Abs(nv - value) > 0.0005f) { set(nv); value = nv; _dirty = true; }
        }
        else if (hover && _ui.Wheel != 0 && !_mixWheelUsed)
        {
            _mixWheelUsed = true;
            value = Math.Clamp(value + 0.02f * _ui.Wheel, 0, 1);
            set(value);
            _dirty = true;
        }
        float v = Math.Clamp(value, 0, 1), x = bar.X + bar.Width * v, cy = bar.Y + bar.Height / 2;
        if (Theme.Cyber)
        {
            // segments lit to the level (grey when muted), the level a cyan mark standing over them
            CyberHud.Segments(_p, bar, v, muted ? CyberHud.Faint : held || hover ? CyC : CyY, CyberHud.Track);
            _p.Fill(new RectangleF(MathF.Round(x) - 1.5f, cy - 8, 3, 16), muted ? CyG : CyC);
            return;
        }
        float h = bar.Height;
        _p.Fill(bar, Bento.Track, h / 2);
        uint col = muted ? Bento.Faint : Bento.Accent;
        _p.Fill(new RectangleF(bar.X, bar.Y, MathF.Max(h, x - bar.X), h), col, h / 2);
        float kr = held ? KnobR : hover ? KnobR - 0.5f : KnobR - 1.5f;
        HaloDisc(x, cy, kr + 2);
        _p.Circle(x, cy, kr, muted ? Bento.Faint : Bento.Text);
        _p.Circle(x, cy, kr - 2.5f, col);
    }

    // ---- opened: the device's volume, then every app (two columns when more than four, the stick scrolls) ----

    void MixerDetail(RectangleF c)
    {
        var m = Mixer;
        if (_ui.OverNow(c)) _mixHoldNext = true;
        var apps = _mixShown;
        // the device: its name, the speaker disc (mute), its slider and level
        bool ok = m.Master >= 0;
        string cap = Theme.Cyber ? "DEVICE" : "OUTPUT DEVICE";
        CapText(cap, c.X, c.Y + 17, c.Width, Sub);
        float cw = Theme.Cyber ? CyMonoW(cap) : _p.MeasureTracked(cap, CapSize, FontWeight.SemiBold, CapTrack);
        string dev = string.IsNullOrEmpty(m.DeviceName) ? "No output device" : m.DeviceName;
        float dw = c.Width - cw - 20;
        if (dw > 40)
        {
            if (Theme.Cyber) CyMono(Ellipsize(Upper(dev), dw, CyNote, FontWeight.SemiBold, CyberHud.Mono), c.Right, c.Y + 17, dw, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
            else TextAt(Ellipsize(dev, dw, SmallText, FontWeight.SemiBold), c.Right - dw, c.Y + 17, dw + 2, Sub, SmallText, FontWeight.SemiBold, TextAlignment.Trailing);
        }
        float my = c.Y + 28;
        var disc = new RectangleF(c.X, my, 48, 48);
        if (Press("wmxmute", disc, m.MasterMuted ? MuteGlyph : SpeakerGlyph, null, m.MasterMuted, ok, 20, true, accentFill: false, fill: Bento.Bad))
            m.SetMaster(m.Master, !m.MasterMuted);
        string mp = !ok ? "–" : m.MasterMuted ? "Muted" : VolumePct(m.Master);
        float pw = Theme.Cyber ? CyValueW(mp, 30) : _p.MeasureWidth(mp, 26, FontWeight.SemiBold, Theme.Display);
        float pr = c.Right, bx = disc.Right + 18 + KnobR;
        var mbar = new RectangleF(bx, my + 21, pr - 86 - bx, 7);
        if (Theme.Cyber) CyValue(pr - pw, my + 35, mp, 30, m.MasterMuted ? CyR : CyW);
        else TextAt(mp, pr - pw, my + 34, pw + 4, !ok ? Bento.Faint : m.MasterMuted ? MutedInk : Bento.Text, 26, FontWeight.SemiBold, TextAlignment.Leading, Theme.Display);
        if (ok) MixSlider("wmxms", mbar, new RectangleF(disc.Right + 8, my - 4, mbar.Right + KnobR + 8 - disc.Right - 8, 56), m.Master, m.MasterMuted,
            v => m.SetMaster(v, m.MasterMuted));
        _p.Fill(new RectangleF(c.X, c.Y + 88, c.Width, 1), Theme.Cyber ? CyberHud.Line : Theme.WithAlpha(Theme.Border, 0.9f));
        // the apps
        var area = new RectangleF(c.X, c.Y + 96, c.Width, c.Bottom - c.Y - 96);
        if (apps.Count == 0)
        {
            MixEmpty(area.X, area.Y + 40, area.Width);
            return;
        }
        int per = Math.Max(1, (int)(area.Height / 56));
        int cols = apps.Count > per ? 2 : 1, rows = (apps.Count + cols - 1) / cols;
        float rh = area.Height / per, gap = 24, colW = (area.Width - (cols - 1) * gap) / cols;
        int maxTop = Math.Max(0, rows - per);
        _mixTop = Math.Clamp(_mixTop, 0, maxTop);
        for (int i = _mixTop * cols; i < Math.Min(apps.Count, (_mixTop + per) * cols); i++)
        {
            int ri = i / cols - _mixTop, ci = i % cols;
            MixRow(new RectangleF(area.X + ci * (colW + gap), area.Y + ri * rh, colW - (maxTop > 0 && ci == cols - 1 ? 10 : 0), rh), apps[i],
                Ui.Id("wmxo", i), true);
        }
        // the stick off the sliders scrolls a row a notch; a thin bar at the right says where
        if (maxTop > 0)
        {
            if (!_mixWheelUsed && _ui.Wheel != 0 && _ui.OverNow(area))
            {
                _mixTop = Math.Clamp(_mixTop - Math.Sign(_ui.Wheel), 0, maxTop);
                _mixWheelUsed = true;
                _dirty = true;
            }
            var track = new RectangleF(area.Right - 4, area.Y + 4, 4, area.Height - 8);
            _p.Fill(track, Theme.Cyber ? CyberHud.Track : Bento.Track, 2);
            float th = MathF.Max(16, track.Height * per / rows), ty = track.Y + (track.Height - th) * _mixTop / maxTop;
            _p.Fill(new RectangleF(track.X, ty, 4, th), Theme.Cyber ? CyC : Bento.Sub, 2);
        }
    }

    public int MixerTop => _mixTop;

    // ================= Windows 95 (every retro style: the skin draws the controls) =================

    static readonly string[] MuteX95 = { "kk...kk", ".kk.kk.", "..kkk..", ".kk.kk.", "kk...kk" };

    // a 16 x 16 icon on the 95 canvas: the exe's own small icon, a 1995 window when it has none, the note for the system sounds;
    // the Terminal shows none (green text only)
    void Icon95(MixerApp app, float x, float y)
    {
        var bmp = MixIcon(app, true);
        if (bmp != null) _p.ImageNearest(bmp, new RectangleF(x, y, 16, 16));
        else W95Icons.Draw(_p, app.System ? W95Icons.Id.Note : W95Icons.Id.Window, x, y);
        if (app.Muted) _p.Pixels(MuteX95, x + 9, y + 11, 0xFFFF0000);
    }

    bool Icons95 => Theme.Retro != RetroStyle.Terminal;

    // The widget: a row per app - its icon (a click mutes it), its name where there is room, a trackbar; on a narrow one the level
    // at the right (a click there opens the Volume Control)
    void Mixer95(RectangleF r, RectangleF c, WatchWidget w, int id)
    {
        _mixCards.Add(new RectangleF(r.X * 2, r.Y * 2, r.Width * 2, r.Height * 2));
        if (_ui.OverNow(r)) _mixHoldNext = true;
        var apps = _mixShown;
        if (apps.Count == 0)
        {
            _ui.Text95(Fit("(no sound)", c.Width - 4), c, Gry95, false, TextAlignment.Center);
            return;
        }
        // rows 20 px and up (40 on the texture: a laser's target)
        int rows = Math.Max(1, (int)(c.Height / 20));
        if (rows > 2 && c.Height / rows < 22) rows--;             // roomier where it can be (3 rows of 25 rather than 4 of 19.5)
        float rh = MathF.Floor(c.Height / rows), y0 = MathF.Floor(c.Y + (c.Height - rows * rh) / 2);
        bool icons = Icons95;
        bool names = c.Width >= 120 || !icons;
        float nameW = !names ? 0 : icons ? MathF.Min(52, MathF.Floor(c.Width * 0.34f)) : MathF.Min(64, MathF.Floor(c.Width * 0.5f));
        bool pct = !names;
        if (rows == 1 && apps.Count >= 2 && c.Height >= 34 && c.Width >= 80) { Mixer95Pair(c, id); return; }
        for (int i = 0; i < Math.Min(rows, apps.Count); i++)
        {
            var app = apps[i];
            string key = Ui.Id("wmx", id * 16 + i);
            var row = new RectangleF(c.X + 2, y0 + i * rh, c.Width - 4, rh);
            float x = row.X;
            if (icons)
            {
                var ih = new RectangleF(x - 1, row.Y, 20, row.Height);
                if (_ui.Clickable(Ui.Id(key, "m"), ih, out _, out _)) { Mixer.ToggleAppMute(app); _dirty = true; }
                Icon95(app, x + 1, MathF.Floor(row.Y + (rh - 16) / 2));
                x += 20;
            }
            if (names)
            {
                // without icons (the Terminal) the name is the mute button
                if (!icons && _ui.Clickable(Ui.Id(key, "m"), new RectangleF(x, row.Y, nameW, row.Height), out _, out _)) { Mixer.ToggleAppMute(app); _dirty = true; }
                _ui.Text95(Fit(app.Name, nameW - 2), new RectangleF(x, MathF.Floor(row.Y + (rh - 13) / 2), nameW, 13), app.Muted ? Gry95 : Blk95);
                x += nameW;
            }
            float pw = pct ? _ui.Width95("100%") + 3 : 0;
            float off = MathF.Max(0, MathF.Floor((rh - 21) / 2));       // the thumb in the row's middle, the hit area to its bottom
            var tr = new RectangleF(x, row.Y + off, row.Right - x - pw, rh - off);
            float v = app.Level;
            if (_ui.Track95(Ui.Id(key, "t"), tr, ref v, 0, 1, !app.Muted, 0)) { Mixer.SetApp(app, v); _dirty = true; }
            if (pct)
                _ui.Text95(app.Muted ? "--" : $"{app.Level * 100:0}%", new RectangleF(row.Right - pw, MathF.Floor(row.Y + (rh - 13) / 2), pw, 13), Blk95, false,
                    TextAlignment.Trailing);
            Record95(app, new RectangleF(tr.X + 6, tr.Y + 8, tr.Width - 12, 4), new RectangleF(row.X, row.Y, icons ? 18 : nameW, rh), false, false);
        }
    }

    // a window too low for two rows (the Terminal's 2 x 1): the top two side by side, the name over its trackbar - the name is the mute
    // button, as on the Terminal's rows (greyed when muted)
    void Mixer95Pair(RectangleF c, int id)
    {
        float cw = MathF.Floor((c.Width - 6) / 2);
        for (int j = 0; j < 2; j++)
        {
            var app = _mixShown[j];
            var col = new RectangleF(c.X + 2 + j * (cw + 2), c.Y, cw, c.Height);
            string key = Ui.Id("wmx", id * 16 + j);
            if (_ui.Clickable(Ui.Id(key, "m"), new RectangleF(col.X, col.Y, cw, MathF.Max(14, col.Height - 22)), out _, out _))
            { Mixer.ToggleAppMute(app); _dirty = true; }
            _ui.Text95(Fit(app.Name, cw - 2), new RectangleF(col.X, col.Y + 1, cw, 13), app.Muted ? Gry95 : Blk95);
            var tr = new RectangleF(col.X, col.Bottom - 22, cw, 22);
            float v = app.Level;
            if (_ui.Track95(Ui.Id(key, "t"), tr, ref v, 0, 1, !app.Muted, 0)) { Mixer.SetApp(app, v); _dirty = true; }
            Record95(app, new RectangleF(tr.X + 6, tr.Y + 8, tr.Width - 12, 4), new RectangleF(col.X, col.Y, cw, 14), false, false);
        }
    }

    void Record95(MixerApp app, RectangleF ch, RectangleF mute, bool standing, bool force)
    {
        if (app.Name == null || (!force && (_ui.Inert || _mixHits.ContainsKey(app.Name)))) return;
        // canvas -> texture (shown 2x); a standing trackbar's level 0 is at the bottom
        Vector2 a = standing ? new(ch.X + ch.Width / 2, ch.Bottom) : new(ch.X, ch.Y + ch.Height / 2);
        Vector2 b = standing ? new(ch.X + ch.Width / 2, ch.Y) : new(ch.Right, ch.Y + ch.Height / 2);
        _mixHits[app.Name] = (a * 2, b * 2, new Vector2(mute.X + mute.Width / 2, mute.Y + mute.Height / 2) * 2);
    }

    // A standing trackbar: the skin's own, drawn transposed (its light stays at the top-left; the thumb points at the ticks on the
    // right). Up is louder. Its hit area is r (28 wide); the stick over it moves it 2 % a notch.
    bool VTrack95(string id, RectangleF r, ref float value, bool enabled)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held);
        if (held) _ui.DragHeld = true;
        float chY = r.Y + 6, chH = r.Height - 12;
        bool changed = false;
        if (enabled && (held || click))
        {
            float nv = 1 - Math.Clamp((_ui.Mouse.Y - chY) / chH, 0, 1);
            if (MathF.Abs(nv - value) > 1e-5f) { value = nv; changed = true; }
        }
        else if (enabled && hover && _ui.Wheel != 0 && !_mixWheelUsed)
        {
            _mixWheelUsed = true;
            value = Math.Clamp(value + Math.Sign(_ui.Wheel) * 0.02f, 0, 1);
            changed = true;
        }
        if (hover || held) _mixHoldNext = true;
        _p.PushMatrix(new Matrix3x2(0, 1, 1, 0, 0, 0));
        Theme.RetroSkin.Trackbar(_p, new RectangleF(r.Y, r.X, r.Height, r.Width), 1 - Math.Clamp(value, 0, 1), enabled, 11, hover, held);
        _p.PopScale();
        return changed;
    }

    // A scroll bar lying down (the skin's, transposed): the arrows step a column, the track a page
    void HScroll95(string id, RectangleF r, ref int pos, int max, int page)
    {
        var left = new RectangleF(r.X, r.Y, 16, r.Height);
        var right = new RectangleF(r.Right - 16, r.Y, 16, r.Height);
        float track = r.Width - 32, tw = MathF.Max(8, MathF.Round(track * page / (max + page)));
        float tx = MathF.Round(r.X + 16 + (track - tw) * pos / Math.Max(1, max));
        var tr = new RectangleF(r.X + 16, r.Y, track, r.Height);
        bool t = _ui.Clickable(Ui.Id(id, "t"), tr, out _, out _);
        // the arrows (after the track: they win where they overlap) take a little more than they show - 40 px on the texture
        bool l = _ui.Clickable(Ui.Id(id, "l"), new RectangleF(left.X, left.Y, 20, left.Height + 6), out _, out bool lh);
        bool rr = _ui.Clickable(Ui.Id(id, "r"), new RectangleF(right.Right - 20, right.Y, 20, right.Height + 6), out _, out bool rh);
        if (t) pos = Math.Clamp(pos + (_ui.Mouse.X < tx ? -page : page), 0, max);
        if (l) pos = Math.Max(0, pos - 1);
        if (rr) pos = Math.Min(max, pos + 1);
        tx = MathF.Round(r.X + 16 + (track - tw) * pos / Math.Max(1, max));
        // transposed: x <-> y (the vertical bar's up arrow is this one's left)
        _p.PushMatrix(new Matrix3x2(0, 1, 1, 0, 0, 0));
        Theme.RetroSkin.ScrollBar(_p, new RectangleF(r.Y, r.X, r.Height, r.Width), new RectangleF(r.Y, r.X, r.Height, 16), new RectangleF(r.Y, r.Right - 16, r.Height, 16),
            new RectangleF(r.Y, tx, r.Height, tw), lh, rh);
        _p.PopScale();
    }

    // Opened: Windows 95's Volume Control - the device's volume first ("Mute all"), then a column per app, each with its icon and
    // name, "Volume:", a standing trackbar and Mute; etched lines between them; a scroll bar under them when they do not all fit
    // (the stick off the trackbars scrolls too)
    void Mixer95Detail(RectangleF c)
    {
        var m = Mixer;
        if (_ui.OverNow(c)) _mixHoldNext = true;
        var apps = _mixShown;
        bool icons = Icons95;
        const float first = 62, minCol = 50;
        int fit = Math.Max(1, (int)((c.Width - first) / minCol));
        bool bar = apps.Count > fit;
        float colsH = c.Height - (bar ? 19 : 0);
        float cw = MathF.Floor((c.Width - first) / Math.Max(1, Math.Min(apps.Count == 0 ? 1 : apps.Count, fit)));
        cw = MathF.Min(cw, 70);
        int max = Math.Max(0, apps.Count - fit);
        _mixTop = Math.Clamp(_mixTop, 0, max);
        // the device's column: its short name ("Speakers", "Headphones")
        string dev = m.DeviceName ?? "";
        int paren = dev.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0) dev = dev[..paren];
        Column95("wmx95m", new RectangleF(c.X, c.Y, first, colsH), null, dev.Length > 0 ? dev : "Volume", m.Master, m.MasterMuted, m.Master >= 0,
            "Mute all", v => m.SetMaster(v, m.MasterMuted), () => m.SetMaster(m.Master, !m.MasterMuted));
        if (apps.Count == 0)
        {
            _ui.Text95("(no sound)", new RectangleF(c.X + first + 8, c.Y + 40, c.Width - first - 16, 13), Gry95);
            return;
        }
        for (int j = 0; j < Math.Min(fit, apps.Count - _mixTop); j++)
        {
            var app = apps[_mixTop + j];
            float x = c.X + first + j * cw;
            Separator95(x, c.Y, colsH);
            Column95(Ui.Id("wmx95c", _mixTop + j), new RectangleF(x + 2, c.Y, cw - 2, colsH), app, app.Name, app.Level, app.Muted, true, "Mute",
                v => Mixer.SetApp(app, v), () => Mixer.ToggleAppMute(app));
        }
        if (bar)
        {
            int top = _mixTop;
            HScroll95("wmx95h", new RectangleF(c.X + first, c.Bottom - 16, c.Width - first, 16), ref top, max, fit);
            if (!_mixWheelUsed && _ui.Wheel != 0 && _ui.OverNow(c)) { top = Math.Clamp(top - Math.Sign(_ui.Wheel), 0, max); _mixWheelUsed = true; }
            if (top != _mixTop) { _mixTop = top; _dirty = true; }
        }
    }

    // a 1995 etched line standing up
    void Separator95(float x, float y, float h)
    {
        _p.Fill(new RectangleF(x, y, 1, h), Theme.W95Shadow);
        _p.Fill(new RectangleF(x + 1, y, 1, h), Theme.W95Hi);
    }

    void Column95(string key, RectangleF col, MixerApp app, string name, float level, bool muted, bool enabled, string muteText, Action<float> set,
        Action toggle)
    {
        float cx = MathF.Floor(col.X + col.Width / 2), y = col.Y + 2;
        if (Icons95)
        {
            if (app != null) Icon95(app, cx - 8, y);
            else W95Icons.Draw(_p, W95Icons.Id.Note, cx - 8, y);
            y += 18;
        }
        _ui.Text95(Fit(name ?? "", col.Width - 4), new RectangleF(col.X + 1, y, col.Width - 2, 13), enabled ? Blk95 : Gry95, false, TextAlignment.Center);
        y += 16;
        _ui.Text95("Volume:", new RectangleF(col.X + 1, y, col.Width - 2, 13), Blk95, false, TextAlignment.Center, enabled);
        y += 15;
        float trackH = col.Bottom - 20 - y;
        var tr = new RectangleF(cx - 11, y, 28, trackH);
        float v = enabled ? level : 0;
        if (VTrack95(Ui.Id(key, "t"), tr, ref v, enabled && !muted)) { set(v); _dirty = true; }
        // Mute: the check box, and the whole width under the trackbar takes the click too (a laser needs a bigger target)
        float my = col.Bottom - 16;
        if (19 + _ui.LabelWidth95(muteText) > col.Width - 2) muteText = "Mute";      // "Mute all" in a wider font
        float lw = 19 + _ui.LabelWidth95(muteText);
        float mx = MathF.Floor(col.X + MathF.Max(2, (col.Width - lw) / 2));
        bool hitAll = _ui.Clickable(Ui.Id(key, "z"), new RectangleF(col.X, my - 5, col.Width, 21), out _, out _);
        bool mu = muted;
        bool box = _ui.Check95(Ui.Id(key, "k"), mx, my, lw <= col.Width - 2 ? muteText : "", ref mu, enabled);
        if ((box || hitAll) && enabled) { toggle(); _dirty = true; }
        if (app != null) Record95(app, new RectangleF(tr.X + 8, tr.Y + 6, 4, tr.Height - 12), new RectangleF(col.X, my - 5, col.Width, 21), true, true);
    }
}
