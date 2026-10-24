using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The SlimeVR widget ("slimevr"; the data: Sys/SlimeVrLink.cs - the SlimeVR server's own API, read only; Erik 10-24: "slimevr widget is
// lehetne"): the full-body trackers. A 1x1 is the emptiest tracker's battery big and how many are connected; a 2x1 every tracker's
// battery as a small bar, where it is worn under it; a 2x2 the list - where, the battery, its line (the 4 x 3 page's the same, bigger).
// A tap opens all of it: each tracker's place (or its own name), battery, signal and status, and SlimeVR's three resets - Yaw, Full and
// Mounting - each asking first (a first tap: "Reset?", 4 s; the second sends it - the photo view's Delete, WristGallery.cs), then the
// server's countdown on its button ("3 s", "Done"). SlimeVR not there: "SlimeVR not running".
// Modern: Bento cards in the Batteries widget's colours (Ok, amber 30 %, red 15 %). Cyber: the SLIMEVR tag, the condensed bold, segments.
// The retro styles that draw the shared windows (95, XP, 7, the Mac): a little window with LED bars in the style's colours, the list as
// Power Meter's rows; opened, a details list (tracker, battery, signal, status) and three push buttons, what is asked or counted on the
// status line beside OK. The Terminal: `slimevr status` (WristSlimeVr.Term.cs). Nothing allocated a frame at rest: the texts are made
// when the snapshot or the language changes, cut texts kept.
public sealed partial class WristPanel
{
    public const string SlimeKindIcon = "\uE805";           // Segoe's walking figure: the widget in the editor's palette

    // the app: send this reset (0 yaw, 1 full, 2 mounting; true: it went out); null in the tools: a second tap is only noted (LastSlimeReset)
    public Func<int, bool> OnSlimeReset;
    public int LastSlimeReset { get; private set; } = -1;

    // ================= the texts =================

    sealed class SvRow
    {
        public string Name = "", NameUp = "", Short = "", Pct = "", Value = "", Signal = "", Rssi = "", Ping = "", SigLine = "", Status = "";
        public int Battery;                      // % (-1: not told)
        public float Frac, SigFrac;              // 0..1 (-1: not told)
        public StatLevel Lv, SigLv, StLv;        // the battery's, the signal's, the status' level
        public bool On, Charging;                // On: connected (not disconnected, timed out or asleep)
    }

    readonly List<SvRow> _svRows = new(), _svPool = new();
    SlimeVrLink.Snapshot _svOf;
    bool _svMade;
    int _svLang = -1, _svLow = -1;               // _svLow: the emptiest connected tracker with a battery (-1: none)
    CultureInfo _svCulture;
    // "5/6"; "5 of 6 connected"; what to say instead of a list (null: the list) and its hint; Cyber's note and head
    string _svCount = "–", _svOfN = "", _svMsg, _svHint, _svHintShort, _svCy = "", _svCyHead = "";
    // a 1x1's note: why nothing / "connected" / the count's forms ("6/6 connected", "6/6 up", "6/6")
    readonly string[] _svNote = new string[2], _svOnNote = new string[2], _svCountNote = new string[3];
    readonly Dictionary<(string, float, float, FontWeight, string), string> _svFits = new();
    static readonly string[] CapSlime = { "SlimeVR" };

    // SlimeVR's names for where a tracker is worn (its GUI's English), and the two letters a 2x1's bar has room for
    static string PartName(SlimeVrLink.Part p) => p switch
    {
        SlimeVrLink.Part.Head => "Head", SlimeVrLink.Part.Neck => "Neck", SlimeVrLink.Part.UpperChest => "Upper chest", SlimeVrLink.Part.Chest => "Chest",
        SlimeVrLink.Part.LowerChest => "Lower chest", SlimeVrLink.Part.Waist => "Waist", SlimeVrLink.Part.UpperWaist => "Upper waist",
        SlimeVrLink.Part.LowerWaist => "Lower waist", SlimeVrLink.Part.Hip => "Hip", SlimeVrLink.Part.LeftHip => "Left hip", SlimeVrLink.Part.RightHip => "Right hip",
        SlimeVrLink.Part.LeftThigh => "Left thigh", SlimeVrLink.Part.RightThigh => "Right thigh", SlimeVrLink.Part.LeftAnkle => "Left ankle",
        SlimeVrLink.Part.RightAnkle => "Right ankle", SlimeVrLink.Part.LeftFoot => "Left foot", SlimeVrLink.Part.RightFoot => "Right foot",
        SlimeVrLink.Part.LeftShoulder => "Left shoulder", SlimeVrLink.Part.RightShoulder => "Right shoulder", SlimeVrLink.Part.LeftUpperArm => "Left upper arm",
        SlimeVrLink.Part.RightUpperArm => "Right upper arm", SlimeVrLink.Part.LeftLowerArm => "Left lower arm", SlimeVrLink.Part.RightLowerArm => "Right lower arm",
        SlimeVrLink.Part.LeftHand => "Left hand", SlimeVrLink.Part.RightHand => "Right hand", SlimeVrLink.Part.Finger => "Finger", SlimeVrLink.Part.Toe => "Toe",
        _ => "Unassigned",
    };

    static string PartShort(SlimeVrLink.Part p) => p switch
    {
        SlimeVrLink.Part.Head => "HD", SlimeVrLink.Part.Neck => "NK", SlimeVrLink.Part.UpperChest => "UC", SlimeVrLink.Part.Chest => "CH",
        SlimeVrLink.Part.LowerChest => "LC", SlimeVrLink.Part.Waist => "WA", SlimeVrLink.Part.UpperWaist => "UW", SlimeVrLink.Part.LowerWaist => "LW",
        SlimeVrLink.Part.Hip => "HP", SlimeVrLink.Part.LeftHip => "LH", SlimeVrLink.Part.RightHip => "RH", SlimeVrLink.Part.LeftThigh => "LT",
        SlimeVrLink.Part.RightThigh => "RT", SlimeVrLink.Part.LeftAnkle => "LA", SlimeVrLink.Part.RightAnkle => "RA", SlimeVrLink.Part.LeftFoot => "LF",
        SlimeVrLink.Part.RightFoot => "RF", SlimeVrLink.Part.LeftShoulder => "LS", SlimeVrLink.Part.RightShoulder => "RS", SlimeVrLink.Part.LeftUpperArm => "LU",
        SlimeVrLink.Part.RightUpperArm => "RU", SlimeVrLink.Part.LeftLowerArm => "LL", SlimeVrLink.Part.RightLowerArm => "RL", SlimeVrLink.Part.LeftHand => "LN",
        SlimeVrLink.Part.RightHand => "RN", SlimeVrLink.Part.Finger => "FI", SlimeVrLink.Part.Toe => "TO",
        _ => "?",
    };

    static string StatusName(int s) => s switch
    {
        SlimeVrLink.Ok => "OK", SlimeVrLink.Disconnected => "Disconnected", SlimeVrLink.Busy => "Busy", SlimeVrLink.Error => "Error",
        SlimeVrLink.Occluded => "Occluded", SlimeVrLink.TimedOut => "Timed out", SlimeVrLink.Sleeping => "Sleeping", _ => "No status",
    };

    // a text cut to w, kept until the snapshot changes (Ellipsize makes a new string each time it cuts)
    string SvCut(string s, float w, float size, FontWeight wt, string family = null)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, size, wt, family);
        if (_svFits.TryGetValue(key, out var done)) return done;
        if (_svFits.Count > 512) _svFits.Clear();
        return _svFits[key] = Ellipsize(s, w, size, wt, family);
    }

    // the same in the 95 font (the retro styles)
    string SvFit95(string s, float w, bool bold = false, float size = Ui.F95)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, size, bold ? FontWeight.Bold : FontWeight.Normal, "95");
        if (_svFits.TryGetValue(key, out var done)) return done;
        if (_svFits.Count > 512) _svFits.Clear();
        return _svFits[key] = Fit(s, w, size, bold);
    }

    // the rows and their words, made again only when the snapshot, the language or the culture changed
    void SvNow(PanelData d)
    {
        var s = d.SlimeVr;
        var culture = CultureInfo.CurrentCulture;
        if (_svMade && ReferenceEquals(s, _svOf) && _svLang == Lang.Version && ReferenceEquals(culture, _svCulture)) return;
        (_svMade, _svOf, _svLang, _svCulture) = (true, s, Lang.Version, culture);
        _svFits.Clear();
        foreach (var r in _svRows) _svPool.Add(r);
        _svRows.Clear();
        _svLow = -1;
        var st = s?.State ?? SlimeVrLink.State.NotRunning;
        bool listed = s != null && st != SlimeVrLink.State.NotRunning && s.Dialect != SolarXr.Dialect.Unknown && s.Trackers.Count > 0;
        if (listed)
            foreach (var t in s.Trackers)
            {
                var r = _svPool.Count > 0 ? _svPool[^1] : new SvRow();
                if (_svPool.Count > 0) _svPool.RemoveAt(_svPool.Count - 1);
                r.Name = t.CustomName.Length > 0 ? t.CustomName : t.Part != SlimeVrLink.Part.None ? Lang.T(PartName(t.Part))
                    : t.DisplayName.Length > 0 ? t.DisplayName : Lang.F("Tracker {0}", t.Num);
                r.NameUp = r.Name.ToUpper(culture);
                r.Short = t.Part != SlimeVrLink.Part.None ? Lang.C("slimevr", PartShort(t.Part)) : r.NameUp.Length > 2 ? r.NameUp[..2] : r.NameUp;
                (r.On, r.Charging, r.Battery) = (t.Connected, t.Charging, t.Battery);
                r.Pct = t.Battery >= 0 ? Pct(t.Battery) : "–";
                r.Frac = t.Battery >= 0 ? t.Battery / 100f : -1;
                r.Lv = t.Battery >= 0 ? BatteryLevel(t.Battery) : StatLevel.Ok;
                r.Value = r.On ? r.Pct : Lang.T("off");
                r.Rssi = t.Rssi != int.MinValue ? t.Rssi.ToString(CultureInfo.InvariantCulture) : "-";
                r.Signal = t.Rssi != int.MinValue ? r.Rssi + " dBm" : "–";
                r.SigFrac = t.Rssi != int.MinValue ? Math.Clamp((t.Rssi + 95) / 55f, 0, 1) : -1;
                r.SigLv = r.SigFrac < 0 || r.SigFrac > 0.4f ? StatLevel.Ok : r.SigFrac > 0.2f ? StatLevel.Warn : StatLevel.Bad;
                r.Ping = t.Ping >= 0 ? Lang.F("{0} ms", t.Ping) : "";
                r.SigLine = r.Ping.Length > 0 ? r.Signal + " · " + r.Ping : r.Signal;
                r.Status = Lang.C("slimevr", StatusName(t.Status));
                r.StLv = t.Status is SlimeVrLink.Ok or SlimeVrLink.Sleeping ? StatLevel.Ok : t.Status is SlimeVrLink.Busy or SlimeVrLink.Occluded ? StatLevel.Warn : StatLevel.Bad;
                _svRows.Add(r);
            }
        for (int i = 0; i < _svRows.Count; i++)
            if (_svRows[i].On && _svRows[i].Battery >= 0 && (_svLow < 0 || _svRows[i].Battery < _svRows[_svLow].Battery)) _svLow = i;
        int n = _svRows.Count, on = listed ? s.Connected : 0;
        (_svMsg, _svHint, _svHintShort, _svNote[0], _svNote[1]) =
            !listed && (s == null || st == SlimeVrLink.State.NotRunning) ? ("SlimeVR not running", "Start SlimeVR to see your trackers", "Start SlimeVR", "not running", "off")
            : !listed && (st != SlimeVrLink.State.Ok || s.Dialect == SolarXr.Dialect.Unknown) ? ("Looking for SlimeVR", "", "", "looking", "…")
            : !listed ? ("No trackers", "Turn your trackers on", "Turn them on", "no trackers", "none")
            : (null, null, null, "connected", "online");
        (_svOnNote[0], _svOnNote[1]) = ("connected", "online");
        _svCount = listed ? on.ToString(culture) + "/" + n.ToString(culture) : "–";
        (_svCountNote[0], _svCountNote[1], _svCountNote[2]) = (Lang.F("{0} connected", _svCount), Lang.F("{0} up", _svCount), _svCount);
        _svOfN = listed ? Lang.F("{0} of {1} connected", on, n) : "";
        _svCy = listed ? Upper(_svOfN) : Upper(_svNote[0]);
        _svCyHead = listed ? _svCy + "  //  SOLARXR" : Upper(_svMsg);
        SvTermTexts(culture);                  // the Terminal's lines (WristSlimeVr.Term.cs)
    }

    // which rows fit: all of them, or the emptiest connected ones first (kept in their order). The list is the same one every call (no
    // garbage a frame): read it before the next call.
    List<int> SvPick(int k)
    {
        _svAll.Clear();
        for (int i = 0; i < _svRows.Count; i++) _svAll.Add(i);
        if (_svRows.Count <= k) return _svAll;
        _svAll.Sort(_svByCharge ??= (a, b) =>
        {
            int ka = _svRows[a].On && _svRows[a].Battery >= 0 ? _svRows[a].Battery : 999, kb = _svRows[b].On && _svRows[b].Battery >= 0 ? _svRows[b].Battery : 999;
            return ka != kb ? ka.CompareTo(kb) : a.CompareTo(b);
        });
        _svKeep.Clear();
        for (int i = 0; i < k; i++) _svKeep.Add(_svAll[i]);
        _svKeep.Sort();
        return _svKeep;
    }

    readonly List<int> _svAll = new(), _svKeep = new();
    Comparison<int> _svByCharge;               // (made once: a lambda here was made every call)

    // ================= the resets: asked first, then sent =================

    const float SvArmLen = 4;                  // s a first tap's "Reset?" stays
    const string ResetAsk = "Reset?";
    // (the retro buttons' third is shorter: "Mounting Reset" does not fit the Mac's Chicago in a third of the window)
    static readonly string[] SvResetNames = { "Yaw reset", "Full reset", "Mounting reset" }, SvResetNames95 = { "&Yaw Reset", "&Full Reset", "&Mounting" };
    static readonly string[] SvIds = { "wsvr0", "wsvr1", "wsvr2" };       // (the same in every style: the tools tap them by these)
    static readonly string[] SvIcons = { "\uE7AD", "\uE72C", "\uE77B" };  // Segoe's rotate, refresh, a person
    int _svArmed = -1, _svSent = -1;
    double _svArmAt, _svSentAt = -10;

    bool SvArmed(int k) => _svArmed == k && Clock.Now - _svArmAt <= SvArmLen;

    // --snapshot's hook: the reset asking now (-1: none)
    public int SlimeArmed => _svArmed >= 0 && Clock.Now - _svArmAt <= SvArmLen ? _svArmed : -1;

    void SvResetTap(int k)
    {
        _dirty = true;
        if (!SvArmed(k)) { _svArmed = k; _svArmAt = Clock.Now; return; }
        _svArmed = -1;
        LastSlimeReset = k;
        if (OnSlimeReset?.Invoke(k) ?? true) (_svSent, _svSentAt) = (k, Clock.Now);
    }

    void SvDisarm() => _svArmed = _svSent = -1;

    // a reset button now: asking ("Reset?"), the server counting its delay down ("3 s"), done a moment ("Done"), sent and not answered yet
    // ("Sent"), else its name
    (string label, bool asking, bool counting) SvButton(int k, PanelData d, bool retro)
    {
        if (SvArmed(k)) return (Lang.T(ResetAsk), true, false);
        var s = d.SlimeVr;
        double now = Clock.Now;
        if (s != null && s.ResetKind == k && s.ResetAt >= _svSentAt - 30)
        {
            double left = s.ResetLeftMs / 1000.0 - (now - s.ResetAt);
            if (s.ResetState == 0 && left > 0) return (SvSecs((int)Math.Ceiling(left)), false, true);
            if (s.ResetState == 1 && now - s.ResetAt < 2.5) return (Lang.T("Done"), false, false);
        }
        if (_svSent == k && now - _svSentAt < 2.5 && (s == null || s.ResetAt < _svSentAt)) return (Lang.T("Sent"), false, false);
        return (Lang.T(retro ? SvResetNames95[k] : SvResetNames[k]), false, false);
    }

    // "3 s", made once per number and language
    readonly Dictionary<int, string> _svSecs = new();
    int _svSecsLang = -1;
    string SvSecs(int n)
    {
        if (_svSecsLang != Lang.Version) { _svSecs.Clear(); _svSecsLang = Lang.Version; }
        if (!_svSecs.TryGetValue(n, out var t)) _svSecs[n] = t = Lang.F("{0} s", n);
        return t;
    }

    bool SvCan(PanelData d) => d.SlimeVr is { State: SlimeVrLink.State.Ok, Dialect: not SolarXr.Dialect.Unknown } && _svRows.Count > 0;

    // ================= Modern =================

    void SlimeOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        SvNow(d);
        int n = _svRows.Count;
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        if (w.W == 1 && w.H == 1)
        {
            Caption(r, iw, CapSlime);
            if (_svLow < 0)
            {
                // no battery to show: the count (or "–") and why under it
                ValueUnit(x, r.Y + SBase, iw, n > 0 ? _svCount : "–", null, Hero, 20, n > 0 ? Bento.Text : Bento.Faint);
                Note(x, r.Y + NoteBase, iw, n > 0 ? _svOnNote : _svNote);
                return;
            }
            // the emptiest one's percentage as big as every 1x1's value (amber / red when low; on a coloured card a badge), how many are
            // connected under it ("6/6 connected", "6/6 up", "6/6": the caption and the count do not fit one row)
            var e = _svRows[_svLow];
            float dw = e.Lv != StatLevel.Ok && Bento.Badge ? DotW : 0, vs = HeroFit(e.Pct, null, iw - dw).vs;
            float vw = ValueText(x, r.Y + SBase, e.Pct, vs, Bento.Level(e.Lv, Bento.Text), null);
            if (dw > 0) StatusDot(x + vw + 11, r.Y + SBase - vs * DigitH / 2, e.Lv);
            Note(x, r.Y + NoteBase, iw, _svCountNote);
            return;
        }
        CaptionRow(r, n > 0 ? _svCount : null, CapSlime);
        if (n == 0)
        {
            if (w.H == 1) TextAt(SvCut(_svMsg, iw, SmallText, FontWeight.SemiBold), x, r.Y + 58 + (_big ? 20 : 0), iw + 2, Sub, SmallText, FontWeight.SemiBold);
            else SvEmpty(new RectangleF(x, r.Y + 40, iw, r.Bottom - 8 - (r.Y + 40)), _big ? 19 : SmallText, true);
            return;
        }
        if (w.H == 1) { SvBars(new RectangleF(x, r.Y + 36, iw, r.Bottom - 10 - (r.Y + 36)), id); return; }
        // tall: a row each - where (a bolt in front while charging), the battery at the right, its line under them
        var area = new RectangleF(x, r.Y + 34, iw, r.Bottom - 10 - (r.Y + 34));
        const float gapX = 12;
        int maxRows = Math.Max(1, (int)(area.Height / (_big ? 34 : 26))), maxCols = Math.Max(1, (int)((area.Width + gapX) / (100 + gapX)));
        var pick = SvPick(Math.Min(maxRows * maxCols, 12));
        int m = pick.Count, cols = 1;
        while ((m + cols - 1) / cols > maxRows) cols++;
        int rows = (m + cols - 1) / cols;
        float cw2 = (area.Width - (cols - 1) * gapX) / cols, ch = MathF.Min(_big ? 52 : 30, area.Height / rows);
        for (int j = 0; j < m; j++)
            SvCell(new RectangleF(area.X + j % cols * (cw2 + gapX), area.Y + j / cols * ch, cw2, ch), _svRows[pick[j]], Ui.Id("wsvc", id * 16 + j), _big);
    }

    // every tracker's battery as a small bar (the emptiest ones when not all fit), where it is worn under it in two letters
    void SvBars(RectangleF a, int id)
    {
        float lab = _big ? 22 : 18;
        int fit = Math.Max(1, (int)(a.Width / (_big ? 30 : 24)));
        var pick = SvPick(Math.Min(fit, 12));
        int m = pick.Count;
        float pitch = a.Width / m, bw = MathF.Floor(MathF.Min(_big ? 24 : 16, pitch - 8)), bh = a.Height - lab - 4;
        if (bh < 8) return;
        for (int j = 0; j < m; j++)
        {
            var e = _svRows[pick[j]];
            float cx = a.X + pitch * (j + 0.5f);
            var bar = new RectangleF(MathF.Round(cx - bw / 2), a.Y, bw, bh);
            _p.Fill(bar, Bento.Track, MathF.Min(4, bw / 2));
            float f = _ui.Spring(Ui.Id("wsvb", id * 16 + j), e.On && e.Frac >= 0 ? e.Frac : 0, 140, 20, 0.08f);
            if (f > 0.01f)
            {
                float h = MathF.Max(bw, bh * MathF.Min(f, 1));
                _p.Fill(new RectangleF(bar.X, bar.Bottom - h, bw, h), Bento.Level(e.Lv, Bento.Ok), MathF.Min(4, bw / 2));
            }
            if (e.Charging && e.On) _p.Icon(Bolt, new RectangleF(bar.X - 4, bar.Y + 2, bw + 8, 16), BoltInk(Bento.Text), 13);
            TextAt(e.Short, cx - pitch / 2, a.Bottom - 1, pitch, e.On ? Sub : Bento.Faint, CapSize, FontWeight.SemiBold, TextAlignment.Center);
        }
    }

    // a tracker on a tall card: its place (UPPERCASE grey; a bolt in front while charging) and its battery (amber / red when low; "off"
    // when not connected) on one baseline, its line under them
    void SvCell(RectangleF c, SvRow e, string key, bool big)
    {
        float lt = big ? 6 : 4, ps = big ? 20 : CapSize;
        var bar = new RectangleF(c.X, c.Bottom - lt - 3, c.Width, lt);
        float by = bar.Y - (big ? 7 : 5);
        float pw = _p.MeasureWidth(e.Value, ps, FontWeight.Bold, Theme.Display);
        uint pc = !e.On || e.Battery < 0 ? Bento.Faint : Bento.Level(e.Lv, Bento.Text);
        bool dot = e.On && e.Battery >= 0 && e.Lv != StatLevel.Ok && Bento.Badge;
        TextAt(e.Value, c.Right - pw - 4, by, pw + 4, pc, ps, FontWeight.Bold, TextAlignment.Trailing, Theme.Display);
        if (dot) StatusDot(c.Right - pw - 4 - 10, by - ps * DigitH / 2, e.Lv);
        float nx = c.X, nr = c.Right - pw - 10 - (dot ? 22 : 0), ns = big ? 17 : CapSize;
        if (e.Charging && e.On) { _p.Icon(Bolt, new RectangleF(nx - 2, by - 16, 16, 17), BoltInk(Bento.Text), 15); nx += 14; }
        if (nr - nx > 12) TextAt(SvCut(e.NameUp, nr - nx, ns, FontWeight.SemiBold), nx, by, nr - nx + 2, e.On ? Sub : Bento.Faint, ns, FontWeight.SemiBold);
        Meter(bar, e.On ? e.Frac : -1, Bento.Level(e.Lv, Bento.Ok), key);
    }

    // nothing to list: why (SlimeVR not running, looking for it, no trackers) and what would help, in the middle of r (card: a widget's -
    // its shorter hint)
    void SvEmpty(RectangleF r, float size, bool card = false)
    {
        string why = _svMsg ?? "No trackers", h = card ? _svHintShort : _svHint;
        bool hint = !string.IsNullOrEmpty(h) && r.Height >= 70;
        float my = r.Y + (r.Height - (hint ? size + 34 : size + 6)) / 2;
        _p.Text(SvCut(why, r.Width, size, FontWeight.SemiBold), new RectangleF(r.X, my, r.Width, size + 10), Bento.Text, size, FontWeight.SemiBold, TextAlignment.Center);
        if (hint)
            _p.Text(SvCut(h, r.Width, SmallText, FontWeight.SemiBold), new RectangleF(r.X, my + size + 10, r.Width, 26), Sub, SmallText, FontWeight.SemiBold,
                TextAlignment.Center);
    }

    // Opened: how many are connected after the name; a tile per tracker in two columns - where (or its own name), the battery big, the
    // signal and the ping, the status, the battery's line; the three resets along the bottom
    void SlimeDetail(RectangleF E, PanelData d)
    {
        if (Theme.Cyber) { CyberSlimeDetail(E, d); return; }
        SvNow(d);
        int n = _svRows.Count;
        float nx = E.X + 24 + _p.MeasureWidth(Lang.T(WatchWidgets.Find("slimevr").Name), 24, FontWeight.SemiBold) + 14, nr = E.Right - 22 - 46 - 14;
        if (n > 0 && nr - nx > 40) TextAt(SvCut(_svOfN, nr - nx, 19, FontWeight.Normal), nx, E.Y + 45, nr - nx + 2, Sub, 19, FontWeight.Normal);
        var B = new RectangleF(E.X + 24, E.Bottom - 22 - 56, E.Width - 48, 56);
        var L = new RectangleF(E.X + 24, E.Y + 78, E.Width - 48, B.Y - 16 - (E.Y + 78));
        if (n == 0) SvEmpty(L, 22);
        else
        {
            const float gap = 24;
            int cols = n <= 3 ? 1 : 2, rows = Math.Min((n + cols - 1) / cols, Math.Max(1, (int)(L.Height / 56)));
            float cw = (L.Width - (cols - 1) * gap) / cols, rh = MathF.Min(84, L.Height / rows);
            int shown = Math.Min(n, rows * cols);
            if (shown < n) shown--;                    // the last place says how many more
            for (int i = 0; i < shown; i++) SvTile(new RectangleF(L.X + i % cols * (cw + gap), L.Y + i / cols * rh, cw, rh), _svRows[i], Ui.Id("wdsv", i));
            if (shown < n)
            {
                var mr = new RectangleF(L.X + shown % cols * (cw + gap), L.Y + shown / cols * rh, cw, rh);
                TextAt(SvCut(MoreText(n - shown), cw, SmallText, FontWeight.SemiBold), mr.X, mr.Y + 30, cw + 2, Sub, SmallText, FontWeight.SemiBold);
            }
        }
        SvButtons(B, d);
    }

    void SvTile(RectangleF c, SvRow e, string key)
    {
        uint ink = e.On ? Bento.Text : Bento.Faint;
        // the battery at the right (a bolt before it while charging), the place beside it
        float vs = 24, pw = _p.MeasureWidth(e.Value, vs, FontWeight.Bold, Theme.Display), right = c.Right;
        TextAt(e.Value, right - pw - 2, c.Y + 28, pw + 4, !e.On || e.Battery < 0 ? Bento.Faint : Bento.Level(e.Lv, Bento.Text), vs, FontWeight.Bold,
            TextAlignment.Leading, Theme.Display);
        right -= pw + 6;
        if (e.Charging && e.On) { _p.Icon(Bolt, new RectangleF(right - 18, c.Y + 8, 18, 24), BoltInk(Bento.Text), 16); right -= 20; }
        float nw = right - 10 - c.X;
        if (nw > 20) TextAt(SvCut(e.Name, nw, 20, FontWeight.SemiBold), c.X, c.Y + 28, nw + 2, ink, 20, FontWeight.SemiBold);
        // the signal: bars and dBm (the ping after it); the status at the right
        float sy = c.Y + 54;
        float stw = _p.MeasureWidth(e.Status, SmallText, FontWeight.SemiBold);
        uint stc = !e.On && e.StLv == StatLevel.Ok ? Sub : e.StLv == StatLevel.Ok ? Bento.Ok : Bento.Level(e.StLv, Sub);
        TextAt(e.Status, c.Right - stw - 2, sy, stw + 4, stc, SmallText, FontWeight.SemiBold);
        SvSignal(c.X, sy, e);
        float tx = c.X + 26, tw = c.Right - stw - 12 - tx;
        if (tw > 24) TextAt(SvCut(e.SigLine, tw, SmallText, FontWeight.Normal), tx, sy, tw + 2, Sub, SmallText, FontWeight.Normal);
        Meter(new RectangleF(c.X, c.Y + 64, c.Width, 5), e.On ? e.Frac : -1, Bento.Level(e.Lv, Bento.Ok), key);
    }

    // four rising bars, lit by the signal (-95 dBm none, -40 all), in its level's colour
    void SvSignal(float x, float by, SvRow e)
    {
        int lit = !e.On || e.SigFrac < 0 ? 0 : Math.Clamp((int)MathF.Ceiling(e.SigFrac * 4), 1, 4);
        uint on = Bento.Level(e.SigLv, Bento.Text);
        for (int i = 0; i < 4; i++)
        {
            float h = 5 + 3.5f * i;
            _p.Fill(new RectangleF(x + i * 5, by - h, 3.5f, h), i < lit ? on : Bento.Track, 1);
        }
    }

    // the three resets: Press - a first tap asks (amber), the second sends, the server's countdown on it (the accent)
    void SvButtons(RectangleF B, PanelData d)
    {
        bool can = SvCan(d);
        const float gap = 12;
        float bw = (B.Width - 2 * gap) / 3;
        for (int k = 0; k < 3; k++)
        {
            var (label, asking, counting) = SvButton(k, d, false);
            var rb = new RectangleF(B.X + k * (bw + gap), B.Y, bw, B.Height);
            // (cut here to what Press has beside its icon - 17 px text - and kept: Press would cut it again on every frame)
            label = SvCut(label, bw - 20 - 8 - 16 - 2, MathF.Min(18, MathF.Max(SmallText, B.Height * 0.3f)), FontWeight.SemiBold);
            if (Press(SvIds[k], rb, SvIcons[k], label, asking || counting, can, 20, false, accentFill: counting, fill: Bento.Warn))
                SvResetTap(k);
        }
    }

    // ================= Cyber =================
    // SLIMEVR on its tag, "5 OF 6 CONNECTED" the cyan note; the emptiest battery in the condensed bold with its segments; the bars as
    // stacks of blocks; a tall card a row each; opened, plates - the place in the condensed capitals, the status a chip, the battery and
    // its segments, the signal in monospace; the resets the HUD's buttons.

    void CyberSlime(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        SvNow(d);
        int n = _svRows.Count;
        float pad = PadX(r), x = r.X + pad, right = r.Right - pad, iw = right - x;
        if (w.W == 1 && w.H == 1)
        {
            CyberCaption(x, r, iw + pad - CyTagX, CapSlime);
            float barY0 = r.Bottom - (_big ? 18 : 15) - (_big ? 6 : 5), by0 = barY0 - (_big ? 13 : 9);
            var bar0 = new RectangleF(x, barY0, iw, _big ? 6 : 5);
            if (_svLow < 0)
            {
                string v = n > 0 ? _svCount : "–";
                float vw0 = CyValue(x, by0, v, MathF.Min(34, (by0 - r.Y - 33) / CyCap), n > 0 ? CyW : CyberHud.Faint);
                string why = Upper(n > 0 ? _svOnNote[1] : _svNote[1]);
                if (right - (x + vw0 + 8) >= CyMonoW(why)) CyMono(why, x + vw0 + 8, by0, right - x - vw0 - 8, CyG);
                CyberMeter(bar0, -1, CyberHud.Faint, Ui.Id("wsv", id * 16), 5);
                return;
            }
            var e = _svRows[_svLow];
            float nw = CyMonoW(_svCount);
            float vs = CyFit(e.Pct, iw - nw - 6, MathF.Min((by0 - r.Y - 33) / CyCap, _big ? 58 : 40));
            if (vs < 24) { nw = 0; vs = CyFit(e.Pct, iw, MathF.Min((by0 - r.Y - 33) / CyCap, 40)); }
            CyValue(x, by0, e.Pct, vs, e.Lv == StatLevel.Ok ? CyW : CyWarn(), Ui.Id("wsv", id * 16));
            if (nw > 0) CyMono(_svCount, right, by0, nw + 2, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
            CyberMeter(bar0, e.Frac, CyBattery(e.Frac, e.Lv), Ui.Id("wsv", id * 16), 5);
            return;
        }
        CyberCaptionRow(r, x, right, n > 0 ? _svCount : null, CyC, CapSlime);
        if (n == 0)
        {
            CyMono(SvCut(Upper(_svMsg), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Y + MathF.Min(66, r.Height * 0.5f), iw + 2, CyG);
            if (w.H >= 2 && !string.IsNullOrEmpty(_svHint))
                CyMono(SvCut(Upper(_svHint), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), x, r.Y + MathF.Min(66, r.Height * 0.5f) + 20, iw + 2, CyberHud.Faint);
            return;
        }
        if (w.H == 1)
        {
            // a stack of blocks per tracker, its two letters under it
            var a = new RectangleF(x, r.Y + 36, iw, r.Bottom - 12 - (r.Y + 36));
            int fit = Math.Max(1, (int)(a.Width / (_big ? 30 : 24)));
            var pick = SvPick(Math.Min(fit, 12));
            int m = pick.Count, blocks = _big ? 8 : 5;
            float pitch = a.Width / m, bw = MathF.Floor(MathF.Min(_big ? 22 : 16, pitch - 8)), bh = a.Height - 18;
            float gap = 2, blh = MathF.Floor((bh - (blocks - 1) * gap) / blocks);
            if (blh < 2) return;
            for (int j = 0; j < m; j++)
            {
                var e = _svRows[pick[j]];
                float cx = a.X + pitch * (j + 0.5f), bx = MathF.Round(cx - bw / 2);
                float f = _ui.Spring(Ui.Id("wsvb", id * 16 + j), e.On && e.Frac >= 0 ? e.Frac : 0, 140, 20, 0.08f) * CyIn(0.28f, 0.3f);
                int lit = f <= 0.005f ? 0 : Math.Max(1, (int)MathF.Round(f * blocks));
                uint c = e.Charging ? CyC : CyBattery(e.Frac, e.Lv);
                for (int b = 0; b < blocks; b++)
                {
                    float by = a.Y + bh - (b + 1) * blh - b * gap;
                    _p.Fill(new RectangleF(bx, by, bw, blh), b < lit ? c : CyberHud.Track);
                }
                CyMono(e.Short, cx - CyMonoW(e.Short) / 2, a.Bottom, pitch, e.On ? CyG : CyberHud.Faint);
            }
            return;
        }
        // tall: a row each
        float top = r.Y + 34, bottom = r.Bottom - 12;
        int rows = Math.Max(1, (int)((bottom - top) / (_big ? 36 : 26)));
        var pk = SvPick(Math.Min(rows, 12));
        float rh = MathF.Min(_big ? 54 : 30, (bottom - top) / pk.Count);
        for (int j = 0; j < pk.Count; j++)
        {
            var e = _svRows[pk[j]];
            var c = new RectangleF(x, top + j * rh, iw, rh);
            float barH = _big ? 6 : 4, ts = _big ? 22 : 17;
            float barY = c.Bottom - 3 - barH, by = barY - 6;
            float pw = CyValueW(e.Value, ts);
            CyValue(c.Right - pw, by, e.Value, ts, !e.On ? CyberHud.Faint : e.Lv == StatLevel.Ok ? CyW : CyWarn(), Ui.Id("wsvc", id * 16 + j));
            float nx = c.X;
            if (e.Charging && e.On) { _p.Icon(Bolt, new RectangleF(nx - 2, by - 14, 14, 15), CyC, 12); nx += 12; }
            float room = c.Right - pw - 10 - nx, ns = _big ? 19 : 16;
            if (room > 12) TextAt(SvCut(e.NameUp, room, ns, FontWeight.SemiBold, Theme.Display), nx, by, room + 2, e.On ? CyW : CyberHud.Faint, ns,
                FontWeight.SemiBold, TextAlignment.Leading, Theme.Display);
            CyberMeter(new RectangleF(c.X, barY, c.Width, barH), e.On ? e.Frac : -1, CyBattery(e.Frac, e.Lv), Ui.Id("wsvm", id * 16 + j),
                Math.Max(5, Math.Min(12, (int)(c.Width / 17))));
        }
    }

    void CyberSlimeDetail(RectangleF E, PanelData d)
    {
        SvNow(d);
        CyFrame();
        int n = _svRows.Count;
        float nx = E.X + 24 + CyberHud.TagWidth(_p, Upper(WatchWidgets.Find("slimevr").Name), 18) + 14, nr = E.Right - 22 - 46 - 14;
        if (nr - nx > 40) CyMono(SvCut(_svCyHead, nr - nx, CyNote, FontWeight.SemiBold, CyberHud.Mono), nx, E.Y + 23 + (26 + CyberHud.CapH(_p, CyNote)) / 2,
            nr - nx, n > 0 ? CyC : CyG);
        var B = new RectangleF(E.X + 24, E.Bottom - 22 - 56, E.Width - 48, 56);
        var L = new RectangleF(E.X + 24, E.Y + 74, E.Width - 48, B.Y - 14 - (E.Y + 74));
        if (n == 0)
        {
            CyMono(SvCut(Upper(_svMsg ?? "No trackers"), L.Width, 17, FontWeight.SemiBold, CyberHud.Mono), L.X, L.Y + L.Height / 2, L.Width + 2, CyG, 17);
            if (!string.IsNullOrEmpty(_svHint))
                CyMono(SvCut(Upper(_svHint), L.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), L.X, L.Y + L.Height / 2 + 24, L.Width + 2, CyberHud.Faint);
        }
        else
        {
            const float pg = 10, gap = 12;
            int cols = n <= 3 ? 1 : 2, rows = Math.Min((n + cols - 1) / cols, Math.Max(1, (int)((L.Height + pg) / (62 + pg))));
            float cw = (L.Width - (cols - 1) * gap) / cols, ph = MathF.Min(76, (L.Height + pg) / rows - pg);
            int shown = Math.Min(n, rows * cols);
            for (int i = 0; i < shown; i++)
            {
                var e = _svRows[i];
                var p = new RectangleF(L.X + i % cols * (cw + gap), L.Y + i / cols * (ph + pg), cw, ph);
                CyberHud.CardShape(_p, p, CyberHud.Card2, e.On ? CyberHud.Edge : CyberHud.Line);
                float x = p.X + 14, right = p.Right - 12;
                // the battery at the right, the place beside it; the status chip and the signal under them; the segments along the bottom
                float pw = CyValueW(e.Value, 24);
                CyValue(right - pw, p.Y + 30, e.Value, 24, !e.On ? CyberHud.Faint : e.Lv == StatLevel.Ok ? CyW : CyWarn(), Ui.Id("wdsv", i));
                float nr2 = right - pw - 10;
                if (e.Charging && e.On) { _p.Icon(Bolt, new RectangleF(nr2 - 14, p.Y + 12, 14, 18), CyC, 13); nr2 -= 18; }
                if (nr2 - x > 20) TextAt(SvCut(Upper(e.Name), nr2 - x, 19, FontWeight.SemiBold, Theme.Display), x, p.Y + 30, nr2 - x + 2, e.On ? CyW : CyberHud.Faint, 19,
                    FontWeight.SemiBold, TextAlignment.Leading, Theme.Display);
                string chip = Upper(e.Status);
                float chw = CyMonoW(chip) + 10, cy = p.Y + 38;
                uint cc = e.StLv == StatLevel.Ok ? (e.On ? CyC : CyG) : CyR;
                _p.Stroke(RectangleF.Inflate(new RectangleF(x, cy, chw, 19), -0.6f, -0.6f), cc, 1.25f);
                CyMono(chip, x + 5, cy + 14, chw, cc);
                float sx = x + chw + 10;
                if (right - sx > 30) CyMono(SvCut(Upper(e.SigLine), right - sx, CyNote, FontWeight.SemiBold, CyberHud.Mono), sx, cy + 14, right - sx, e.SigLv == StatLevel.Ok ? CyG : CyWarn());
                if (ph >= 68) CyberMeter(new RectangleF(x, p.Bottom - 10, right - x, 4), e.On ? e.Frac : -1, CyBattery(e.Frac, e.Lv), Ui.Id("wdsvm", i), 10);
            }
            if (shown < n) CyMono(SvCut(Upper(MoreText(n - shown)), L.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), L.X, B.Y - 4, L.Width, CyG);
        }
        SvButtons(B, d);                           // (Press draws the HUD's buttons in Cyber)
    }

    // ================= the retro styles (the shared 95 windows: 95, XP, 7, the Mac) =================

    // c: the window's inside (the style's frame is drawn). light: 7's glass (white words). A 1x1: the emptiest battery bold, how many are
    // connected under it; one row: an LED bar each (the style's highlight colour, red when low) with its two letters; taller: Power Meter's
    // rows - the place, a progress bar, the percentage
    void Slime95(RectangleF c, WatchWidget w, PanelData d, bool light = false)
    {
        SvNow(d);
        int n = _svRows.Count;
        uint ink = light ? 0xFFFFFFFF : Blk95, grey = light ? 0xFFD6E4F5 : Gry95, low95 = light ? 0xFFFFA48F : 0xFF800000;
        if (w.W == 1 && w.H == 1 || c.Width < 70)
        {
            float vy = MathF.Floor(c.Y + (c.Height - 28) / 2);
            var e = _svLow >= 0 ? _svRows[_svLow] : null;
            string v = e != null ? e.Pct : n > 0 ? _svCount : "--";
            _ui.Text95(SvFit95(v, c.Width - 2, true, 13), new RectangleF(c.X, vy, c.Width, 15), e != null && e.Lv != StatLevel.Ok ? low95 : ink, true, TextAlignment.Center, size: 13);
            _ui.Text95(SvFit95(e != null ? _svCount : n > 0 ? _svOnNote[1] : _svNote[1], c.Width - 2), new RectangleF(c.X, vy + 15, c.Width, 13), grey, false, TextAlignment.Center);
            return;
        }
        if (n == 0)
        {
            // why, and what would help - a narrow window: "SlimeVR" over "not running" (cut, the first would lose what it is)
            string msg = _svMsg ?? "No trackers", hint = c.Height >= 44 ? _svHint : null;
            bool whole = _ui.Width95(Lang.T(msg), true) <= c.Width - 6;
            if (!whole) (msg, hint) = ("SlimeVR", _svNote[0]);
            float my = MathF.Floor(c.Y + (c.Height - (!string.IsNullOrEmpty(hint) ? 28 : 13)) / 2);
            _ui.Text95(SvFit95(msg, c.Width - 6, true), new RectangleF(c.X + 3, my, c.Width - 6, 13), ink, true, TextAlignment.Center);
            if (!string.IsNullOrEmpty(hint))
                _ui.Text95(SvFit95(hint, c.Width - 6), new RectangleF(c.X + 3, my + 15, c.Width - 6, 13), grey, false, TextAlignment.Center);
            return;
        }
        if (c.Height < 56)
        {
            // an LED bar each - with its two letters where they have room; else the emptiest battery and the count at the left of them
            var pick = SvPick(Math.Min(12, Math.Max(1, (int)((c.Width - 4) / 7))));
            int m = pick.Count;
            bool labels = (c.Width - 4) / m >= 18;
            float left = c.X + 2, top = c.Y + 3, bh = MathF.Floor(c.Height - 6 - (labels ? 14 : 0));
            if (!labels && _svLow >= 0)
            {
                var low = _svRows[_svLow];
                float vw = MathF.Max(_ui.Width95(low.Pct, true), _ui.Width95(_svCount)) + 4, vy = MathF.Floor(c.Y + (c.Height - 28) / 2);
                _ui.Text95(low.Pct, new RectangleF(left, vy, vw, 14), low.Lv != StatLevel.Ok ? low95 : ink, true, TextAlignment.Center);
                _ui.Text95(_svCount, new RectangleF(left, vy + 15, vw, 13), grey, false, TextAlignment.Center);
                left += vw + 2;
            }
            float pitch = (c.Right - 2 - left) / m, bw = MathF.Floor(MathF.Min(12, pitch - 3));
            for (int j = 0; j < m; j++)
            {
                var e = _svRows[pick[j]];
                float cx = left + pitch * (j + 0.5f);
                Led95(new RectangleF(MathF.Floor(cx - bw / 2), top, bw, bh), e.On ? e.Frac : 0, e.Lv == StatLevel.Bad ? 0xFF800000 : Led95Ink);
                if (labels) _ui.Text95(e.Short, new RectangleF(MathF.Floor(cx - pitch / 2), top + bh + 1, MathF.Ceiling(pitch), 13), e.On ? ink : grey, false, TextAlignment.Center);
            }
            return;
        }
        // (rows of 13 px, 12 when that fits them all; the emptiest ones when not even that does)
        float rh = Math.Clamp(MathF.Floor((c.Height - 2) / n), 12, 13);
        var all = SvPick(Math.Min(12, (int)((c.Height - 2) / rh)));
        float y0 = MathF.Floor(c.Y + (c.Height - all.Count * rh) / 2);
        for (int i = 0; i < all.Count; i++)
        {
            var e = _svRows[all[i]];
            float y = y0 + i * rh;
            bool warn = e.On && e.Battery >= 0 && e.Lv == StatLevel.Bad;
            _ui.Text95(e.Short, new RectangleF(c.X + 3, y, 22, 12), e.On ? ink : grey);
            var bar = new RectangleF(c.X + 26, y + 1, c.Width - 26 - 32, 10);
            if (bar.Width >= 12) _ui.Progress95(bar, e.On && e.Frac >= 0 ? e.Frac : 0, warn ? 0xFF800000 : Theme.W95Navy);
            _ui.Text95(e.Value, new RectangleF(c.Right - 32, y, 30, 12), !e.On ? grey : warn ? low95 : ink, e.On, TextAlignment.Trailing);
        }
    }

    // the LED's lit colour: the style's progress blocks - XP's and 7's green, 95's and the Mac's selection colour
    static uint Led95Ink => Theme.Retro is RetroStyle.XP or RetroStyle.Aero ? 0xFF2FB52F : Theme.RetroSkin.Highlight;

    // an LED bar: a sunken well, lit blocks from the bottom (red when low)
    void Led95(RectangleF r, float frac, uint lit)
    {
        _p.Fill(r, Theme.RetroSkin.Window);
        _p.Bevel(r, Painter.Edge.Sunken);
        var b = RectangleF.Inflate(r, -2, -2);
        if (b.Height < 4) return;
        int blocks = Math.Max(2, (int)((b.Height + 1) / 4));
        float bh = (b.Height - (blocks - 1)) / blocks;
        int on = frac <= 0.005f ? 0 : Math.Max(1, (int)MathF.Round(Math.Clamp(frac, 0, 1) * blocks));
        for (int i = 0; i < on; i++) _p.Fill(new RectangleF(b.X, MathF.Round(b.Bottom - (i + 1) * bh - i), b.Width, MathF.Max(1, MathF.Round(bh))), lit);
    }

    // Opened, in the window the style draws (title, X, OK): c is its inside, the OK row under it. A details list under its column
    // headers - the tracker, its battery (a progress bar and the percentage), the signal, the status - then the three resets as push
    // buttons (asking: held in, "Reset?"); what is asked or counted, else how many are connected, on the status line beside OK.
    SvRow[] _svDraw95 = Array.Empty<SvRow>();
    Action<int, RectangleF, bool> _svRow95;
    float _svCol95A, _svCol95B, _svCol95C;
    const string SlimeList95 = "wsv95list";

    void Slime95Detail(RectangleF c, PanelData d)
    {
        SvNow(d);
        int n = _svRows.Count;
        float sw = c.Right - 75 - 8 - c.X;
        _ui.Text95(SvFit95(SvStatus95(d), sw), new RectangleF(c.X, c.Bottom + 10, sw, 13), SvArmed95() ? 0xFF800000 : Blk95);
        // the resets along the bottom of the inside
        bool can = SvCan(d);
        float bw = MathF.Floor((c.Width - 12) / 3), by = c.Bottom - 23;
        for (int k = 0; k < 3; k++)
        {
            var (label, asking, counting) = SvButton(k, d, true);
            if (_ui.Button95(SvIds[k], new RectangleF(c.X + k * (bw + 6), by, bw, 23), label, can, false, asking || counting)) SvResetTap(k);
        }
        var box = new RectangleF(c.X, c.Y, c.Width, by - 6 - c.Y);
        if (n == 0)
        {
            W95Icons.Draw(_p, W95Icons.Id.Info32, c.X + 12, c.Y + 18);
            _ui.Text95(SvFit95(_svMsg ?? "No trackers", c.Width - 64, true), new RectangleF(c.X + 56, c.Y + 20, c.Width - 60, 15), Blk95, true);
            if (!string.IsNullOrEmpty(_svHint)) _ui.Text95(SvFit95(_svHint, c.Width - 64), new RectangleF(c.X + 56, c.Y + 38, c.Width - 60, 13), Blk95);
            return;
        }
        float nameW = MathF.Floor(c.Width * 0.36f), batW = 74, sigW = MathF.Floor((c.Width - nameW - batW) * 0.45f);
        Head95(new RectangleF(box.X, box.Y, nameW, 16), Lang.T("Tracker"));
        Head95(new RectangleF(box.X + nameW, box.Y, batW, 16), Lang.T("Battery"));
        Head95(new RectangleF(box.X + nameW + batW, box.Y, sigW, 16), Lang.T("Signal"));
        Head95(new RectangleF(box.X + nameW + batW + sigW, box.Y, box.Width - nameW - batW - sigW, 16), Lang.T("Status"));
        if (_svDraw95.Length < n) _svDraw95 = new SvRow[Math.Max(n, 16)];
        _svRows.CopyTo(_svDraw95);
        (_svCol95A, _svCol95B, _svCol95C) = (nameW, batW, sigW);
        _svRow95 ??= SvRow95;
        // (rows of 16 px, down to 14 when that shows them all - 7's window is a little lower than 95's)
        float lh = box.Height - 16, rowH = Math.Clamp(MathF.Floor((lh - 4) / n), 14, 16);
        _ui.List95(SlimeList95, new RectangleF(box.X, box.Y + 16, box.Width, lh), n, -1, _svRow95, rowH);
    }

    // a row of the opened list (a method of its own, kept as one delegate: a lambda here was made every frame)
    void SvRow95(int i, RectangleF r, bool on)
    {
        var e = _svDraw95[i];
        uint ink = e.On ? Blk95 : Gry95;
        bool warn = e.On && e.Battery >= 0 && e.Lv == StatLevel.Bad;
        float x = r.X + 3;
        _ui.Text95(SvFit95(e.Name, _svCol95A - 6), new RectangleF(x, r.Y, _svCol95A - 4, r.Height), ink);
        x = r.X + _svCol95A;
        if (e.On && e.Frac >= 0) _ui.Progress95(new RectangleF(x + 2, r.Y + 3, 34, r.Height - 6), e.Frac, warn ? 0xFF800000 : Theme.W95Navy);
        _ui.Text95(e.Value, new RectangleF(x + 38, r.Y, _svCol95B - 40, r.Height), warn ? 0xFF800000 : ink, false, TextAlignment.Trailing);
        x += _svCol95B;
        _ui.Text95(SvFit95(e.Signal, _svCol95C - 6), new RectangleF(x + 3, r.Y, _svCol95C - 4, r.Height), ink);
        x += _svCol95C;
        float tw = MathF.Min(r.Right - x - 4, 200);
        if (tw > 8) _ui.Text95(SvFit95(e.Status, tw), new RectangleF(x + 3, r.Y, tw, r.Height), e.StLv == StatLevel.Bad ? 0xFF800000 : ink);
    }

    bool SvArmed95() => _svArmed >= 0 && Clock.Now - _svArmAt <= SvArmLen;

    // the status line: what is asked ("Click Full Reset again to reset"), counted ("Full reset in 3 s"), else how many are connected
    string SvStatus95(PanelData d)
    {
        if (_svAskLang != Lang.Version) { _svIn95.Clear(); Array.Clear(_svAsk95); _svAskLang = Lang.Version; }
        if (SvArmed95()) return _svAsk95[_svArmed] ??= Lang.F("Click {0} again to reset", Lang.T(SvResetNames[_svArmed]));
        var s = d.SlimeVr;
        if (s != null && s.ResetKind is >= 0 and <= 2 && s.ResetState == 0)
        {
            int left = (int)Math.Ceiling(s.ResetLeftMs / 1000.0 - (Clock.Now - s.ResetAt));
            if (left > 0)
            {
                if (!_svIn95.TryGetValue((s.ResetKind, left), out var t)) _svIn95[(s.ResetKind, left)] = t = Lang.F("{0} in {1} s", Lang.T(SvResetNames[s.ResetKind]), left);
                return t;
            }
        }
        return _svRows.Count > 0 ? _svOfN : Lang.T(_svMsg ?? "No trackers");
    }

    readonly string[] _svAsk95 = new string[3];
    readonly Dictionary<(int, int), string> _svIn95 = new();
    int _svAskLang = -1;

    // ---- --snapshot's hooks ----

    public int SlimeShown(PanelData d) { SvNow(d); return _svRows.Count; }
    public string SlimeRow(PanelData d, int i) { SvNow(d); return i < _svRows.Count ? $"{_svRows[i].Name} {_svRows[i].Value} {_svRows[i].Signal} {_svRows[i].Status}" : null; }
    public string SlimeLowest(PanelData d) { SvNow(d); return _svLow >= 0 ? _svRows[_svLow].Pct : "–"; }
    public string SlimeMessage(PanelData d) { SvNow(d); return _svMsg; }
}
