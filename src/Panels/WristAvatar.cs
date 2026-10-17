using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The avatar toggles widget ("avatar"; the data: Sys/AvatarToggles.cs - VRChat's OSC config of the avatar worn, its values from VRChat's
// OSCQuery): buttons for the avatar, a tap sent to VRChat over OSC. Which buttons: the ones picked for this avatar in the watch editor
// (SettingsView.Avatar.cs), else the filter's first (toggles first). A bool is a toggle (a switch in a tile, the tile in the accent when
// on), an int a stepper (‹ 3 ›: the left half of its tile down, the right half up), a float a slider (the tile fills to it; drag along it).
// A tap shows at once; what VRChat reads back corrects it (WristPanel draws again when the link's state changes).
// A 1x1 is its first button (a toggle switches with a tap on the card); 2x1 a row of tiles, bigger ones more rows (on narrow tiles a
// stepper or a slider takes two). A tap on the card's header opens all of the avatar's buttons, the stick scrolls them.
// VRChat not running, no config for the avatar worn, nothing to toggle: the widget says so (and what would help).
// Modern: Bento tiles. Cyber: cut squares, yellow when on, segments for a slider. The retro styles: WristAvatar95.cs (95, XP, 7, the
// Mac), Skins/Terminal/WristAvatar.Term.cs. Nothing allocated a frame at rest: the texts are made when the state, the picks or the
// language change; cut ones are kept.
public sealed partial class WristPanel
{
    public const string AvatarKindIcon = "";        // Segoe's contact: a person
    static readonly string[] CapAvatar = { "Avatar toggles", "Avatar" }, CapAvatarS = { "Avatar" };
    const string ChevL = "", ChevR = "";
    static readonly AvatarState NoAvatar = new() { Status = AvatarStatus.Off };

    // ---- what changed: the watch draws again when VRChat answers, a tap is shown, or the editor picked other buttons ----

    int _avSeen = -1, _avEditsSeen = -1;

    bool AvatarChanged(PanelData d)
    {
        int v = d.Avatar?.State.Version ?? -1, e = _cfg.Data.Avatars?.Edits ?? 0;
        if (v == _avSeen && e == _avEditsSeen) return false;
        (_avSeen, _avEditsSeen) = (v, e);
        return C.Face == "custom" && (HasWidget("avatar") || DetailIs("avatar"));
    }

    // ================= the texts =================

    AvatarState _avS;
    List<string> _avPick;
    int _avLang = -1, _avEdits = -1;
    readonly List<int> _avShown = new();
    // what to say instead of buttons (null: the buttons), shorter, what would help; the avatar's name; where its values are from
    string _avMsg, _avShort, _avHint, _avName = "", _avStatus = "", _avCyHead = "", _avCount = "";
    readonly Dictionary<(string, float, float, int), string> _avFits = new();
    static readonly string[] IntText = MakeInts();

    static string[] MakeInts()
    {
        var a = new string[256];
        for (int i = 0; i < a.Length; i++) a[i] = i.ToString(CultureInfo.InvariantCulture);
        return a;
    }

    static string IntOf(float v)
    {
        int n = (int)MathF.Round(v);
        return n >= 0 && n < IntText.Length ? IntText[n] : n.ToString(CultureInfo.InvariantCulture);
    }

    // the buttons' state now: made again only when the link's state, the picks or the language changed
    void AvNow(PanelData d)
    {
        var s = d.Avatar?.State ?? NoAvatar;
        var cfgA = _cfg.Data.Avatars;
        int edits = cfgA?.Edits ?? 0;
        List<string> pick = null;
        if (s.Avatar != null && cfgA?.Shown != null) cfgA.Shown.TryGetValue(s.Avatar.Id, out pick);
        if (ReferenceEquals(s, _avS) && _avLang == Lang.Version && edits == _avEdits && ReferenceEquals(pick, _avPick)) return;
        bool again = _avS == null || !ReferenceEquals(s.Avatar, _avS.Avatar) || s.Status != _avS.Status || s.Source != _avS.Source || edits != _avEdits ||
                     !ReferenceEquals(pick, _avPick) || _avLang != Lang.Version;
        (_avS, _avLang, _avEdits, _avPick) = (s, Lang.Version, edits, pick);
        if (!again) return;                   // only values moved: the words stay
        _avFits.Clear();
        AvatarFilter.Shown(s.Avatar, pick, _avShown);
        var a = s.Avatar;
        _avName = a?.Name ?? "";
        (_avMsg, _avShort, _avHint) = s.Status switch
        {
            AvatarStatus.NoVrchat => ("VRChat isn't running", "No VRChat", "Start VRChat on this PC"),
            AvatarStatus.NoConfig => ("No OSC config for this avatar", "No config", "Turn on OSC in VRChat"),
            AvatarStatus.Ready when a.Buttons.Length == 0 => ("Nothing to toggle", "None", "This avatar has no toggles"),
            AvatarStatus.Ready when _avShown.Count == 0 => ("No toggles picked", "None", "Pick them in Edit layout"),
            AvatarStatus.Ready => (null, null, null),
            _ => ("Looking for VRChat", "Looking", ""),
        };
        string from = s.Source switch
        {
            AvatarSource.Live => Lang.T("Live from VRChat"),
            AvatarSource.Log => Lang.T("From VRChat's log · no live values"),
            AvatarSource.Newest => Lang.T("The config used last · no live values"),
            _ => "",
        };
        int n = a?.Buttons.Length ?? 0;
        _avCount = n == 1 ? Lang.T("1 toggle") : Lang.F("{0} toggles", n);
        _avStatus = s.Ready ? (from.Length > 0 ? _avCount + "  ·  " + from : _avCount) : Lang.T(_avMsg ?? "");
        _avCyHead = s.Ready ? Upper(_avName) + "  //  " + Upper(s.Live ? "Live" : "No live values") : Upper(_avMsg ?? "");
    }

    bool AvReady => _avS != null && _avS.Ready && _avMsg == null;

    // a text cut to w, kept until the avatar, the picks or the language change
    string AvFit(string s, float w, float size, FontWeight wt = FontWeight.SemiBold, string fam = null)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, size, (int)wt * 31 + (fam == null ? 0 : fam.GetHashCode()));
        if (_avFits.TryGetValue(key, out var done)) return done;
        if (_avFits.Count > 512) _avFits.Clear();
        return _avFits[key] = Ellipsize(s, w, size, wt, fam);
    }

    // ================= Modern / Cyber: the card =================

    void AvatarOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        AvNow(d);
        var link = d.Avatar;
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad;
        if (w.W == 1 && w.H == 1) { AvatarSmall(r, w, d, id); return; }
        AvCaptionRow(r, AvReady ? _avName : _avShort);
        if (!AvReady)
        {
            if (w.H >= 2) { if (Theme.Cyber) CyLxNone(r, _avMsg, _avHint); else LxNone(r, AvatarKindIcon, _avMsg, _avHint); return; }
            if (Theme.Cyber) CyMono(AvFit(Upper(_avMsg), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Y + MBase - 4, iw + 2, CyR);
            else TextAt(AvFit(_avMsg, iw, SmallText), x, r.Y + MBase - 6, iw + 2, Bento.Text, SmallText, FontWeight.SemiBold);
            return;
        }
        // the tiles: as many columns as fit, rows down to the bottom; on narrow tiles a stepper or a slider takes two side by side (its
        // name and its value need the room), a toggle the next free one - so a gap a wide one left is filled. Fewer than fit: wider ones.
        // (from 40 px down: clear of a header look's band, 34 px)
        var area = new RectangleF(x, r.Y + 40, iw, r.Bottom - 10 - (r.Y + 40));
        float gap = _big ? 8 : 6, minW = _big ? 96 : 80, th = _big ? 50 : 44;
        int cols = Math.Clamp((int)((area.Width + gap) / (minW + gap)), 1, 8), rows = Math.Clamp((int)((area.Height + gap) / (th + gap)), 1, 8);
        // the rows as tall as the card lets them (up to a little more), the block in the middle of what is left
        th = MathF.Min(_big ? 60 : 50, (area.Height - (rows - 1) * gap) / rows);
        area.Y += MathF.Floor((area.Height - rows * th - (rows - 1) * gap) / 2);
        var s = _avS;
        int n = _avShown.Count;
        bool narrow = (area.Width - (cols - 1) * gap) / cols < WideTile;
        int cells = 0;
        for (int k = 0; k < n && cells < cols; k++) cells += Span(s.Avatar.Buttons[_avShown[k]], narrow, cols);
        if (cells < cols) cols = Math.Max(1, cells);
        float tw = (area.Width - (cols - 1) * gap) / cols;
        Array.Clear(_avCells, 0, cols * rows);
        for (int k = 0; k < n; k++)
        {
            int bi = _avShown[k], span = Span(s.Avatar.Buttons[bi], narrow, cols), at = -1;
            for (int c = 0; c < cols * rows && at < 0; c++)
                if (!_avCells[c] && (span == 1 || (c % cols < cols - 1 && !_avCells[c + 1]))) at = c;
            if (at < 0) continue;                 // no room for it (a smaller one after it may still fit)
            _avCells[at] = true;
            if (span == 2) _avCells[at + 1] = true;
            var t = new RectangleF(area.X + at % cols * (tw + gap), area.Y + at / cols * (th + gap), span * tw + (span - 1) * gap, th);
            AvTile(Ui.Id("wav", id * 64 + k), t, s.Avatar.Buttons[bi], s.Values[bi], bi, link);
        }
    }

    readonly bool[] _avCells = new bool[64];
    const float WideTile = 120;                 // a tile this wide has the room for a switch beside its name, a stepper's name and value

    static int Span(AvatarParam p, bool narrow, int cols) => narrow && p.Kind != AvatarKind.Toggle && cols >= 2 ? 2 : 1;

    // the caption row: the caption, the avatar's name at the right (cut here, once: the row would cut it again every frame)
    void AvCaptionRow(RectangleF r, string note)
    {
        float pad = PadX(r), iw = r.Width - 2 * pad;
        float cw = CaptionWidth(CapAvatar, iw * 0.55f);
        float room = iw - (cw > 0 ? cw + 10 : 0) - (Theme.Cyber ? 10 : 0);
        string n = string.IsNullOrEmpty(note) || room < 36 ? null
            : Theme.Cyber ? AvFit(Upper(note), room, CyNote, FontWeight.SemiBold, CyberHud.Mono) : AvFit(note, room, SmallText);
        CaptionRow(r, n, CapAvatar);
    }

    // a 1x1: its first button - the caption, a picture and the value as big as a 1x1's ("On", "3", "45%"), its name along the bottom; a
    // toggle switches with a tap anywhere on it (a stepper or a slider opens the widget)
    void AvatarSmall(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad, right = r.Right - pad;
        float ny = r.Bottom - (_big ? 14 : 12);
        if (!AvReady)
        {
            Caption(r, iw, CapAvatarS);
            if (Theme.Cyber)
            {
                CyWord(r, x, right, AvatarKindIcon, "–", CyberHud.Faint);
                CyMono(AvFit(Upper(_avShort), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, ny, iw + 2, CyR);
                return;
            }
            LxIconValue(x, right, r.Y + SBase, AvatarKindIcon, Bento.Faint, "–", Bento.Faint);
            float ns = MathF.Max(CapSize, FitSize(Lang.T(_avShort), iw, SmallText, FontWeight.SemiBold, null));      // (down to a hint's 15 px)
            TextAt(AvFit(_avShort, iw, ns), x, ny, iw + 2, Sub, ns, FontWeight.SemiBold);
            return;
        }
        var s = _avS;
        int bi = _avShown[0];
        var p = s.Avatar.Buttons[bi];
        float v = s.Values[bi];
        bool known = !float.IsNaN(v), on = known && v >= 0.5f;
        string value = !known ? "–" : p.Kind switch { AvatarKind.Toggle => on ? "On" : "Off", AvatarKind.Slider => Pct((int)MathF.Round(Math.Clamp(v, 0, 1) * 100)), _ => IntOf(v) };
        if (p.Kind == AvatarKind.Toggle)
        {
            string key = Ui.Id("wav1", id);
            if (_ui.Clickable(key, r, out _, out bool held)) { d.Avatar?.Toggle(bi); _dirty = true; if (Theme.Cyber) CyTap(key); }
            if (Theme.Cyber) CyTapFx(key, r, CyberHud.CardTL, CyberHud.CardBR);
            else if (held) _p.Fill(r, Theme.WithAlpha(Bento.Accent, 0.14f), CardRadius);
        }
        bool lit = p.Kind == AvatarKind.Toggle ? on : known;
        Caption(r, iw, CapAvatarS);
        if (Theme.Cyber)
        {
            if (on && p.Kind == AvatarKind.Toggle) CyLit(r, CyY);
            CyWord(r, x, right, AvatarKindIcon, Upper(value), !known ? CyberHud.Faint : lit ? CyY : CyG);
            CyMono(AvFit(Upper(p.Label), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, ny, iw + 2, CyC);
            return;
        }
        // (a look of its own: its text colour - the accent would not read on it)
        LxIconValue(x, right, r.Y + SBase, AvatarKindIcon, !lit ? Bento.Faint : Styled(w) ? Bento.Text : Bento.Accent, value, known ? Bento.Text : Bento.Faint);
        TextAt(AvFit(p.Label, iw, SmallText), x, ny, iw + 2, Sub, SmallText, FontWeight.SemiBold);
    }

    // ---- a button ----

    void AvTile(string key, RectangleF t, AvatarParam p, float v, int bi, AvatarLink link)
    {
        switch (p.Kind)
        {
            case AvatarKind.Toggle: AvToggle(key, t, p, v, bi, link); break;
            case AvatarKind.Slider: AvSlider(key, t, p, v, bi, link); break;
            default: AvStepper(key, t, p, v, bi, link); break;
        }
    }

    float AvRad(RectangleF t) => MathF.Min(_big ? 14 : 12, t.Height / 2);

    // a toggle: the tile in the accent when on, a switch at its right; not known (no live values): off, the switch's knob in the middle
    void AvToggle(string key, RectangleF t, AvatarParam p, float v, int bi, AvatarLink link)
    {
        bool known = !float.IsNaN(v), on = known && v >= 0.5f;
        bool click = _ui.Clickable(key, t, out bool hover, out bool held);
        if (click) { link?.Toggle(bi); _dirty = true; if (Theme.Cyber) CyTap(key); }
        float sc = _ui.Spring(Ui.Id(key, "#s"), held ? 0.94f : 1f, 420, 18);
        float a = _ui.Anim(Ui.Id(key, "#on"), on ? 1 : 0, 16);
        var b = new RectangleF(t.X + t.Width * (1 - sc) / 2, t.Y + t.Height * (1 - sc) / 2, t.Width * sc, t.Height * sc);
        float cy = t.Y + t.Height / 2, pad = t.Width < 100 ? 10 : 12;
        float pw = _big ? 32 : 28, ph = _big ? 18 : 16;
        bool pill = t.Width >= WideTile;           // a narrow tile: its fill alone says on / off (its name needs the room)
        float lw = t.Width - 2 * pad - (pill ? pw + 6 : 0);
        if (Theme.Cyber)
        {
            float cut = MathF.Round(MathF.Min(b.Width, b.Height) * 0.22f);
            _p.FillCut(b, on ? CyY : hover ? CyberHud.Card3 : CyberHud.Card2, 0, 0, cut, 0);
            if (!on) _p.StrokeCut(RectangleF.Inflate(b, -0.6f, -0.6f), hover ? CyC : CyberHud.CyanDim, 1.25f, 0, 0, MathF.Max(0, cut - 0.3f), 0);
            uint ink = on ? CyInk : known ? CyW : CyG;
            string w0 = Upper(on ? "On" : "Off"), label = Upper(p.Label);
            float ow = CyMonoW(w0, CyNote, FontWeight.Bold), all = t.Width - 2 * pad;
            // ON / OFF after its name where both fit (else its fill says it)
            bool word = _p.MeasureWidth(label, 17, FontWeight.SemiBold, Theme.Display) + ow + 8 <= all;
            float lw2 = word ? all - ow - 6 : all;
            TextAt(AvFit(label, lw2, 17, FontWeight.SemiBold, Theme.Display), t.X + pad, cy + 6, lw2 + 2, ink, 17, FontWeight.SemiBold,
                TextAlignment.Leading, Theme.Display);
            if (word) CyMono(w0, t.Right - pad, cy + 5, ow + 2, on ? CyInk : !known ? CyberHud.Faint : CyC, CyNote, FontWeight.Bold, TextAlignment.Trailing);
            CyTapFx(key, b, 0, cut);
            return;
        }
        float rad = AvRad(b);
        _p.Fill(b, Theme.Mix(hover ? Bento.TileHover : Bento.Tile, Bento.AccentFill, a), rad);
        if (a < 1 && Bento.Edge != 0) _p.Stroke(RectangleF.Inflate(b, -0.6f, -0.6f), Theme.WithAlpha(Bento.Edge, 1 - a), Bento.EdgeWidth, rad);
        uint fg = Theme.Mix(known ? Bento.Text : Sub, Bento.OnAccent, a);
        TextAt(AvFit(p.Label, lw, SmallText), t.X + pad, cy + 6, lw + 2, fg, SmallText, FontWeight.SemiBold);
        if (!pill) return;
        // the switch
        var sw = new RectangleF(t.Right - pad - pw, cy - ph / 2, pw, ph);
        _p.Fill(sw, Theme.Mix(Theme.WithAlpha(Bento.Text, 0.16f), Theme.WithAlpha(Bento.OnAccent, 0.32f), a), ph / 2);
        float kr = ph / 2 - 2.5f, kx = !known ? sw.X + pw / 2 : sw.X + ph / 2 + (pw - ph) * a;
        _p.Circle(kx, cy, kr, Theme.Mix(known ? Sub : Bento.Faint, Bento.OnAccent, a));
    }

    // a slider: the tile fills from the left to its value, the value at its right; a drag along it (or a tap) sets it
    void AvSlider(string key, RectangleF t, AvatarParam p, float v, int bi, AvatarLink link)
    {
        bool known = !float.IsNaN(v);
        float val = known ? Math.Clamp(v, 0, 1) : 0;
        _ui.Clickable(key, t, out bool hover, out bool held);
        if (held)
        {
            _ui.DragHeld = true;              // a drag here is the slider's, not a page swipe
            float nv = Math.Clamp((_ui.Mouse.X - t.X - 6) / MathF.Max(1, t.Width - 12), 0, 1);
            if (!known || MathF.Abs(nv - val) > 0.004f) { link?.Set(bi, nv); val = nv; known = true; _dirty = true; }
        }
        float cy = t.Y + t.Height / 2, pad = t.Width < 100 ? 10 : 12;
        string pct = known ? Pct((int)MathF.Round(val * 100)) : "–";
        if (Theme.Cyber)
        {
            float cut = MathF.Round(MathF.Min(t.Width, t.Height) * 0.22f);
            _p.FillCut(t, hover || held ? CyberHud.Card3 : CyberHud.Card2, 0, 0, cut, 0);
            _p.StrokeCut(RectangleF.Inflate(t, -0.6f, -0.6f), held ? CyC : CyberHud.CyanDim, 1.25f, 0, 0, MathF.Max(0, cut - 0.3f), 0);
            CyberHud.Segments(_p, new RectangleF(t.X + pad, t.Bottom - 10, t.Width - 2 * pad - cut * 0.6f, 4), val, held ? CyC : CyY, CyberHud.Track);
            float vw = CyMonoW(pct, CyNote, FontWeight.Bold);
            float lw = t.Width - 2 * pad - vw - 6;
            TextAt(AvFit(Upper(p.Label), lw, 17, FontWeight.SemiBold, Theme.Display), t.X + pad, cy + 1, lw + 2, CyW, 17, FontWeight.SemiBold,
                TextAlignment.Leading, Theme.Display);
            CyMono(pct, t.Right - pad, cy, vw + 2, known ? CyC : CyberHud.Faint, CyNote, FontWeight.Bold, TextAlignment.Trailing);
            return;
        }
        float rad = AvRad(t);
        _p.Fill(t, hover || held ? Bento.TileHover : Bento.Tile, rad);
        if (Bento.Edge != 0) _p.Stroke(RectangleF.Inflate(t, -0.6f, -0.6f), Bento.Edge, Bento.EdgeWidth, rad);
        // the part up to its value a touch toward the text colour (its name stays readable in every look), an accent line along its foot
        if (val > 0.005f)
        {
            float fw = MathF.Max(2 * rad, t.Width * val);
            _p.Fill(new RectangleF(t.X, t.Y, fw, t.Height), Theme.WithAlpha(Bento.Text, held ? 0.2f : 0.13f), rad);
            float lx = t.X + rad * 0.7f, lr = MathF.Max(lx + 3, t.X + fw - rad * 0.7f);
            _p.Fill(new RectangleF(lx, t.Bottom - 6, lr - lx, 3), Bento.Accent, 1.5f);
        }
        float pw2 = _p.MeasureWidth(pct, SmallText, FontWeight.Bold);
        float lw2 = t.Width - 2 * pad - pw2 - 6;
        TextAt(AvFit(p.Label, lw2, SmallText), t.X + pad, cy + 6, lw2 + 2, known ? Bento.Text : Sub, SmallText, FontWeight.SemiBold);
        TextAt(pct, t.Right - pad - pw2, cy + 6, pw2 + 2, known ? Bento.Text : Bento.Faint, SmallText, FontWeight.Bold);
    }

    // a stepper: ‹ its name, its value › - the tile's left half steps down, its right half up
    void AvStepper(string key, RectangleF t, AvatarParam p, float v, int bi, AvatarLink link)
    {
        bool known = !float.IsNaN(v);
        var lh = new RectangleF(t.X, t.Y, t.Width / 2, t.Height);
        var rh = new RectangleF(t.X + t.Width / 2, t.Y, t.Width / 2, t.Height);
        string kd = Ui.Id(key, "-"), ku = Ui.Id(key, "+");
        if (_ui.Clickable(kd, lh, out bool hd, out bool dn)) { link?.Step(bi, -1); _dirty = true; if (Theme.Cyber) CyTap(kd); }
        if (_ui.Clickable(ku, rh, out bool hu, out bool up)) { link?.Step(bi, +1); _dirty = true; if (Theme.Cyber) CyTap(ku); }
        float cy = t.Y + t.Height / 2, cs = 14, cw = 14;
        string val = known ? IntOf(v) : "–";
        if (Theme.Cyber)
        {
            float cut = MathF.Round(MathF.Min(t.Width, t.Height) * 0.22f);
            _p.FillCut(t, hd || hu ? CyberHud.Card3 : CyberHud.Card2, 0, 0, cut, 0);
            _p.StrokeCut(RectangleF.Inflate(t, -0.6f, -0.6f), CyberHud.CyanDim, 1.25f, 0, 0, MathF.Max(0, cut - 0.3f), 0);
            _p.Icon(ChevL, new RectangleF(t.X + 4, cy - 10, cw, 20), dn ? CyY : CyC, cs);
            _p.Icon(ChevR, new RectangleF(t.Right - 4 - cw, cy - 10, cw, 20), up ? CyY : CyC, cs);
            float vw = CyMonoW(val, CyNote, FontWeight.Bold), lx = t.X + 4 + cw + 4, lw = t.Right - 4 - cw - 6 - vw - 6 - lx;
            TextAt(AvFit(Upper(p.Label), lw, 17, FontWeight.SemiBold, Theme.Display), lx, cy + 6, lw + 2, CyW, 17, FontWeight.SemiBold, TextAlignment.Leading, Theme.Display);
            CyMono(val, t.Right - 4 - cw - 6, cy + 5, vw + 2, known ? CyY : CyberHud.Faint, CyNote, FontWeight.Bold, TextAlignment.Trailing);
            CyTapFx(kd, lh, 0, 0);
            CyTapFx(ku, rh, 0, cut);
            return;
        }
        float rad = AvRad(t);
        _p.Fill(t, hd || hu ? Bento.TileHover : Bento.Tile, rad);
        if (Bento.Edge != 0) _p.Stroke(RectangleF.Inflate(t, -0.6f, -0.6f), Bento.Edge, Bento.EdgeWidth, rad);
        if (dn) _p.Fill(lh, Theme.WithAlpha(Bento.Text, 0.08f), rad);
        if (up) _p.Fill(rh, Theme.WithAlpha(Bento.Text, 0.08f), rad);
        _p.Icon(ChevL, new RectangleF(t.X + 4, cy - 10, cw, 20), Bento.Text, cs);
        _p.Icon(ChevR, new RectangleF(t.Right - 4 - cw, cy - 10, cw, 20), Bento.Text, cs);
        float vw2 = _p.MeasureWidth(val, SmallText, FontWeight.Bold), lx2 = t.X + 4 + cw + 4, lw2 = t.Right - 4 - cw - 6 - vw2 - 6 - lx2;
        TextAt(AvFit(p.Label, lw2, SmallText), lx2, cy + 6, lw2 + 2, known ? Bento.Text : Sub, SmallText, FontWeight.SemiBold);
        TextAt(val, t.Right - 4 - cw - 6 - vw2, cy + 6, vw2 + 2, known ? Bento.Text : Bento.Faint, SmallText, FontWeight.Bold);
    }

    // ================= opened =================

    float _avScroll, _avMax;
    double _avOpenedAt = double.NaN;
    WatchWidget _avOpened;
    int _avTiles;                               // --snapshot: the buttons drawn in the opened view's last frame
    const float AvRowH = 56, AvGap = 10;
    const int AvCols = 3;

    // a newly opened one starts from the top (OpenDetail stays the shared one's)
    void AvFromTop()
    {
        if (ReferenceEquals(_avOpened, _open) && _avOpenedAt == _openStart) return;
        (_avOpened, _avOpenedAt) = (_open, _openStart);
        _avScroll = 0;
        _ui.ListScrollTo95(AvatarList95, 0);
    }

    // the title (the shared code's), the avatar's name after it; how many buttons and where their values are from under it; every
    // button of the avatar in three columns (a switch beside each name), the stick scrolls them
    void AvatarDetail(RectangleF E, PanelData d)
    {
        AvNow(d);
        AvFromTop();
        var link = d.Avatar;
        var s = _avS;
        float right = E.Right - 24;
        if (Theme.Cyber)
        {
            CyFrame();
            float cx = E.X + 24 + CyberHud.TagWidth(_p, Upper(WatchWidgets.Find("avatar").Name), 18) + 14, cr = E.Right - 22 - 46 - 14;
            if (cr - cx > 40) CyMono(AvFit(_avCyHead, cr - cx, CyNote, FontWeight.SemiBold, CyberHud.Mono), cx, E.Y + 23 + (26 + CyberHud.CapH(_p, CyNote)) / 2, cr - cx,
                s.Ready ? (s.Live ? CyC : CyY) : CyG);
        }
        else
        {
            float nx = E.X + 24 + _p.MeasureWidth(Lang.T(WatchWidgets.Find("avatar").Name), 24, FontWeight.SemiBold) + 12, nr = E.Right - 22 - 46 - 14;
            if (_avName.Length > 0 && nr - nx > 40) TextAt(AvFit(_avName, nr - nx, 24), nx, E.Y + 45, nr - nx + 2, Sub, 24, FontWeight.SemiBold);
            TextAt(AvFit(_avStatus, right - (E.X + 24), 19, FontWeight.Normal), E.X + 24, E.Y + 82, right - E.X - 22, s.Ready && !s.Live ? Bento.WarnInk : Sub, 19,
                FontWeight.Normal);
        }
        float top = Theme.Cyber ? E.Y + 74 : E.Y + 100;
        var L = new RectangleF(E.X + 24, top, E.Width - 48, E.Bottom - 40 - top);
        _avTiles = 0;
        if (!s.Ready || s.Avatar.Buttons.Length == 0)
        {
            _avMax = 0;
            AvEmpty(L);
            return;
        }
        var order = s.Avatar.Order;
        int n = order.Length, rows = (n + AvCols - 1) / AvCols;
        float content = rows * (AvRowH + AvGap) - AvGap, max = MathF.Max(0, content - L.Height);
        _avMax = max;
        if (_ui.Wheel != 0 && !_ui.Inert) { _avScroll -= _ui.Wheel * (AvRowH + AvGap); _dirty = true; }
        _avScroll = Math.Clamp(_avScroll, 0, max);
        float rw = max > 0 ? L.Width - 14 : L.Width, tw = (rw - (AvCols - 1) * AvGap) / AvCols;
        bool big = _big;
        _big = true;                            // the opened view's tiles: the 4 x 3 page's sizes
        _p.PushClip(new RectangleF(L.X - 2, L.Y, L.Width + 4, L.Height));
        for (int i = 0; i < n; i++)
        {
            float y = L.Y - _avScroll + i / AvCols * (AvRowH + AvGap);
            if (y + AvRowH < L.Y || y > L.Bottom) continue;
            int bi = order[i];
            // a tile half out of the list is only a picture (its hidden half must not take a tap)
            bool part = y < L.Y - 0.5f || y + AvRowH > L.Bottom + 0.5f, inert = _ui.Inert;
            if (part) _ui.Inert = true;
            AvTile(Ui.Id("wavd", bi), new RectangleF(L.X + i % AvCols * (tw + AvGap), y, tw, AvRowH), s.Avatar.Buttons[bi], s.Values[bi], bi, link);
            _ui.Inert = inert;
            _avTiles++;
        }
        _p.PopClip();
        _big = big;
        if (max > 0)
        {
            float bh = MathF.Max(24, L.Height * L.Height / content), by = L.Y + (L.Height - bh) * (_avScroll / max);
            _p.Fill(new RectangleF(L.Right - 4, by, 4, bh), Theme.Cyber ? CyOther : Theme.WithAlpha(Bento.Text, 0.3f), 2);
        }
        int hidden = AvBelow(n, L);
        if (hidden > 0)
        {
            string more = ScrollMore(hidden);
            if (Theme.Cyber) CyMono(AvFit(Upper(more), L.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), L.Right, E.Bottom - 20, L.Width, CyG, CyNote,
                FontWeight.SemiBold, TextAlignment.Trailing);
            else TextAt(AvFit(more, L.Width, SmallText), L.X, E.Bottom - 22, L.Width + 2, Sub, SmallText, FontWeight.SemiBold, TextAlignment.Trailing);
        }
    }

    // the buttons whose row is not wholly in view below the list
    int AvBelow(int n, RectangleF L)
    {
        int hidden = 0;
        for (int i = 0; i < n; i++)
            if (L.Y - _avScroll + i / AvCols * (AvRowH + AvGap) + AvRowH > L.Bottom + 0.5f) hidden++;
        return hidden;
    }

    // nothing to show: why, and what would help, in the middle of the list's place
    void AvEmpty(RectangleF L)
    {
        string msg = _avMsg ?? "Nothing to toggle", hint = _avHint;
        bool h = !string.IsNullOrEmpty(hint);
        float my = L.Y + (L.Height - (h ? 56 : 30)) / 2;
        if (Theme.Cyber)
        {
            CyMono(AvFit(Upper(msg), L.Width, 17, FontWeight.SemiBold, CyberHud.Mono), L.X, my + 20, L.Width + 2, CyG, 17);
            if (h) CyMono(AvFit(Upper(hint), L.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), L.X, my + 46, L.Width + 2, CyberHud.Faint);
            return;
        }
        _p.Icon(AvatarKindIcon, new RectangleF(L.X, my - 54, L.Width, 48), Bento.Faint, 36);
        _p.Text(AvFit(msg, L.Width, 22), new RectangleF(L.X, my, L.Width, 32), Bento.Text, 22, FontWeight.SemiBold, TextAlignment.Center);
        if (h) _p.Text(AvFit(hint, L.Width, SmallText), new RectangleF(L.X, my + 32, L.Width, 26), Sub, SmallText, FontWeight.SemiBold, TextAlignment.Center);
    }

    // ---- --snapshot's hooks ----

    public string AvatarSays(PanelData d) { AvNow(d); return AvReady ? "ready" : _avMsg; }
    public string AvatarShownLabels(PanelData d)
    {
        AvNow(d);
        var sb = new System.Text.StringBuilder();
        foreach (int i in _avShown) sb.Append(sb.Length > 0 ? ", " : "").Append(_avS.Avatar.Buttons[i].Label);
        return sb.ToString();
    }
    public int AvatarTilesDrawn => _avTiles;
    public float AvatarScroll => Theme.Classic ? _ui.ListTop95(AvatarList95) : _avScroll;      // modern / Cyber: px; the 95 list: its first row
    public float AvatarScrollMax => _avMax;

    // where a button of the first avatar widget bigger than 1x1 on the page is in a frame drawn now (part: a stepper's "-" / "+")
    public System.Numerics.Vector2? AvatarTilePoint(PanelData d, int i, string part = null)
    {
        var w = WatchPages.Widgets(C, CurPage)?.Find(x => x?.Type == "avatar" && !(x.W == 1 && x.H == 1));
        if (w == null) return null;
        string key = Ui.Id(!Theme.Classic ? "wav" : Theme.Retro == RetroStyle.Terminal ? "wavt" : "wav95", Wid(w) * 64 + i);
        return ControlPoint(d, part == null ? key : Ui.Id(key, part));
    }
}
