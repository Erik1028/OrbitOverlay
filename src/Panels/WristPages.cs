using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// The custom face's pages (1.12): page 1 is WristConfig.Widgets, the others WristConfig.Pages, each on its own grid (6 x 4, or the
// 4 x 3 Bento grid with fewer, bigger cards). Swiped with the laser - the face follows it and springs to its page - or a tap on the
// dots along the bottom; the 95 face has a little taskbar of page buttons there. And the widgets made with the pages: the 4 x 3
// designs of the room, now playing (its cover's colours), the frame graph (the last 30 s), batteries, CPU and memory; the
// microphone, the notifications and a group of buttons in one card.
public sealed partial class WristPanel
{
    public readonly SystemMic Mic = new();
    GridSpec _grid = GridSpec.G64;              // the grid of the page being drawn
    int _seenPage = -1, _slideOther = -1;       // the page drawn last frame; the one sliding in beside it (-1: none)
    readonly HashSet<WatchPage> _cleaned = new();

    // the page in view (one removed in the editor: the last one there is)
    int CurPage
    {
        get
        {
            int n = WatchPages.Count(C);
            if (C.Page < 0 || C.Page >= n) C.Page = Math.Clamp(C.Page, 0, n - 1);
            return C.Page;
        }
    }

    public int PageNow => CurPage;

    // a page's widgets, checked once against its own grid (a hand-edited config); page 1 never set up starts from the old face
    List<WatchWidget> PageList(int i)
    {
        if (i <= 0)
        {
            if (C.Widgets == null)
            {
                C.Widgets = WatchPages.Grid(C, 0).Is43 ? WatchWidgets.PagePreset("everyday", GridSpec.G43) : WatchWidgets.Seed(C, "full");
                _cfg.MarkDirty();
            }
            else if (!_layoutChecked) C.Widgets = WatchWidgets.Clean(C.Widgets, WatchPages.Grid(C, 0));     // a hand-edited config
            _layoutChecked = true;
            return C.Widgets;
        }
        var p = C.Pages[i - 1];
        p.Widgets ??= new List<WatchWidget>();
        if (_cleaned.Add(p)) p.Widgets = WatchWidgets.Clean(p.Widgets, WatchPages.Grid(C, i));
        return p.Widgets;
    }

    int WidgetTotal()
    {
        int n = 0;
        for (int i = 0; i < WatchPages.Count(C); i++) n += WatchPages.Widgets(C, i)?.Count ?? 0;
        n += FolderKidTotal();                      // the widgets in folders (WristFolder.cs)
        return n;
    }

    // ---- going to a page ----

    const string PageKey = "wpage";
    float _pagePosNow;

    // Shows page 'to' (remembered in the config). animate: it slides there from where it is now (a swipe let go, a dot tapped);
    // an opened widget closes - it belongs to the page it was on
    public void GoToPage(int to, bool animate = false)
    {
        int n = WatchPages.Count(C);
        to = Math.Clamp(to, 0, n - 1);
        int cur = CurPage;
        if (to == cur) return;
        float from = _seenPage == cur ? _pagePosNow : cur;
        C.Page = to;
        _cfg.MarkDirty();
        _open = null;
        _closing = false;
        _ui.SpringFrom(PageKey, animate && !Theme.Classic ? from : to);
        _dirty = true;
    }

    // where the face is: a swipe moves it with the laser (past the first / last page only a little), then it springs to its page
    float PagePosition(int count, int cur)
    {
        float pos;
        if (_swiping)
        {
            pos = cur - _sweepDx / TW;
            if (pos < 0) pos *= 0.3f;
            else if (pos > count - 1) pos = count - 1 + (pos - (count - 1)) * 0.3f;
            _ui.SpringFrom(PageKey, pos);
        }
        else pos = _ui.Spring(PageKey, cur, 190, 24);
        if (MathF.Abs(pos - cur) < 0.002f) pos = cur;
        return _pagePosNow = pos;
    }

    // ---- the swipe: trigger held, the laser across the face; past a fifth of its width it is the next / previous page ----

    Vector2 _sweepFrom;
    bool _sweepDown, _swiping, _swallow;
    float _sweepDx;
    int _sweepDraws;                               // frames drawn since the press: a slider under it has said so by then

    bool CanSwipe => C.Face == "custom" && WatchPages.Count(C) > 1 && _open == null;
    public bool Swiping => _swiping;

    void SwipeBegin(Vector2 px)
    {
        _sweepDown = CanSwipe;
        _sweepFrom = px;
        _swiping = false;
        _sweepDx = 0;
        _sweepDraws = 0;
    }

    // the press became a swipe (it went sideways more than down, and not on a slider): true while it is one
    bool SwipeMove(Vector2 px)
    {
        if (!_sweepDown) return false;
        float dx = px.X - _sweepFrom.X, dy = px.Y - _sweepFrom.Y;
        if (!_swiping)
        {
            if (_sweepDraws < 1 || MathF.Abs(dx) < 16 || MathF.Abs(dx) < MathF.Abs(dy) * 1.3f || _ui.DragHeld) return false;
            _swiping = true;
        }
        _sweepDx = dx;
        return true;
    }

    void SwipeEnd(Vector2 px)
    {
        if (_swiping)
        {
            SwipeMove(px);
            int cur = CurPage, to = cur;
            if (MathF.Abs(_sweepDx) > TW * 0.2f) to = Math.Clamp(cur + (_sweepDx < 0 ? 1 : -1), 0, WatchPages.Count(C) - 1);
            _swallow = true;            // the release that ends a swipe is not a tap
            _swiping = false;
            if (to != cur) GoToPage(to, animate: true);     // else it springs back
            _dirty = true;
        }
        _sweepDown = false;
    }

    void SwipeCancel()
    {
        if (_swiping) { _swiping = false; _swallow = true; }
        _sweepDown = false;
    }

    // right after the frame's Ui.Begin: while a swipe runs (and for the release that ends it) no widget has the press
    void SwipeGuard()
    {
        _sweepDraws++;
        if (_swiping || _swallow) { _ui.CancelPress(); _swallow = false; }
    }

    // The dots in their own strip under the cards (WatchWidgets.PageStrip): one per page, the page in view a pill in the accent (it
    // slides with the face). A tap on a dot goes to its page, on the strip's left / right half to the page before / after (the whole
    // strip takes the laser: a target that big is hittable).
    void PageDots(int count, float pos, bool cards)
    {
        const float pitch = 18, rad = 3.5f, cy = TH - WatchWidgets.PageStrip / 2;
        float span = (count - 1) * pitch, x0 = TW / 2f - span / 2;
        if (Theme.Cyber) CyberDots(count, pos, cards, x0, pitch, cy);          // short bars (WristCyber.cs)
        else
        {
            // a capsule round them, clear of the cards: dark glass with a hairline when there is no page under it
            var cap = new RectangleF(x0 - 17, cy - 9, span + 34, 18);
            if (cards) { _p.Fill(cap, 0xB3000000, 9); _p.Stroke(cap, 0x26FFFFFF, 1, 9); }
            else _p.Fill(cap, 0x14FFFFFF, 9);
            uint dim = Theme.WithAlpha(Bento.Grey, 0.75f);
            for (int i = 0; i < count; i++) _p.Circle(x0 + i * pitch, cy, rad, dim);
            // the pill stretches over to the next dot on the way - its front gets there first, then its back follows
            float pc = Math.Clamp(pos, 0, count - 1), i0 = MathF.Min(MathF.Floor(pc), count - 1), f = pc - i0;
            float a0 = x0 + (i0 + MathF.Max(0, 2 * f - 1)) * pitch, a1 = x0 + (i0 + MathF.Min(1, 2 * f)) * pitch;
            _p.Fill(new RectangleF(a0 - 9, cy - rad, a1 - a0 + 18, 2 * rad), Bento.Default.Accent, rad);
        }
        var hit = new RectangleF(x0 - 30, TH - WatchWidgets.PageStrip, span + 60, WatchWidgets.PageStrip);
        if (!_ui.Clickable("wdots", hit, out _, out _)) return;
        float mx = _ui.Mouse.X;
        int at = (int)MathF.Round((mx - x0) / pitch), cur = CurPage;
        int to = at >= 0 && at < count && MathF.Abs(mx - (x0 + at * pitch)) <= 6 ? at : cur + (mx < TW / 2f ? -1 : 1);
        GoToPage(to, true);
    }

    // --snapshot: a tap on dot i
    public static Vector2 DotPoint(int count, int i) => new(TW / 2f - (count - 1) * 9 + i * 18, TH - WatchWidgets.PageStrip / 2);

    // The 95 face's pages: a little taskbar along the bottom, a button per page - the one in view pushed in, as the running program's
    // button is on the real one
    void Taskbar95(int count, int cur)
    {
        if (SkinTaskbar(count, cur)) return;      // a style's own taskbar (ScreenHooks.cs)
        float y = TH95 - WatchWidgets.Taskbar95 - 2;
        _p.Fill(new RectangleF(0, y, TW95, TH95 - y), Theme.W95Face);
        _p.Fill(new RectangleF(0, y, TW95, 1), Theme.W95Light);
        _p.Fill(new RectangleF(0, y + 1, TW95, 1), Theme.W95Hi);
        float bw = MathF.Floor(MathF.Min(104, (TW95 - 6 - (count - 1) * 3) / (float)count));
        for (int i = 0; i < count; i++)
        {
            var b = new RectangleF(3 + i * (bw + 3), y + 4, bw, TH95 - y - 6);
            string name = Ui.Esc95(Fit(WatchPages.Label(C, i), bw - 10));
            if (_ui.Button95(Ui.Id("wtb", i), b, name, true, false, i == cur)) GoToPage(i);
        }
    }

    // --snapshot: the middle of the 95 taskbar's button i (texture px)
    public static Vector2 TaskbarPoint(int count, int i)
    {
        float y = TH95 - WatchWidgets.Taskbar95 - 2;
        float bw = MathF.Floor(MathF.Min(104, (TW95 - 6 - (count - 1) * 3) / (float)count));
        return new Vector2((3 + i * (bw + 3) + bw / 2) * 2, (y + 4 + (TH95 - y - 6) / 2) * 2);
    }

    // ================= the 4 x 3 designs =================

    bool Widget43(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d)
    {
        bool s = w.W == 1 && w.H == 1;
        switch (k.Id)
        {
            case "room" when w.W >= 2: RoomM43(r, w, d, id); return true;
            case "media" when CoverCard(k, w, Styled(w)): MediaCover(r, w, d, id); return true;
            case "graph": GraphLine(r, w, d); return true;
            case "battery" when s: BatteryBars(r, d); return true;
            case "battery" when w.H == 1: BatteryRows43(r, d, id); return true;
            case "cpu" when s: CpuS43(r, d); return true;
            case "vram" or "ram" when s: MemS43(r, k.Id, d, id); return true;
        }
        return false;
    }

    // ---- people's initials (the room, the notifications) ----

    static int NameHash(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (char ch in s ?? "") h = h * 31 + char.ToUpperInvariant(ch);
            return h & 0x7FFFFFFF;
        }
    }

    static uint AvatarColor(string name) => Bento.Avatars[NameHash(name) % Bento.Avatars.Length];

    static readonly Dictionary<string, string> _initials = new();

    static string InitialOf(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        if (_initials.TryGetValue(name, out var s)) return s;
        if (_initials.Count > 512) _initials.Clear();
        s = "?";
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsHighSurrogate(name[i]) && i + 1 < name.Length) { s = name.Substring(i, 2); break; }
            if (char.IsLetterOrDigit(name[i])) { s = char.ToUpper(name[i], CultureInfo.CurrentCulture).ToString(); break; }
        }
        return _initials[name] = s;
    }

    // a person as a circle with the initial (they overlap: a gap of the card around each); a friend ringed in green
    void Avatar(float cx, float cy, float rad, string name, bool friend)
    {
        HaloDisc(cx, cy, rad + 2.5f);
        if (friend) _p.Circle(cx, cy, rad, Bento.Ok);
        _p.Circle(cx, cy, friend ? rad - 2.5f : rad, AvatarColor(name));
        float fs = MathF.Max(14, MathF.Round(rad * 1.05f));
        _p.Text(InitialOf(name), new RectangleF(cx - rad, cy - rad, 2 * rad, 2 * rad + 1), Bento.Dark, fs, FontWeight.Bold,
            TextAlignment.Center, ParagraphAlignment.Center);
    }

    // ---- the room (4 x 3 M): the world big, the people as initials (friends first, ringed), "Mira, Tobi + 10" ----

    static readonly string[] CapRoomWide = { "VRChat room", "Room" };
    RoomWatcher.Snapshot _room43Seen;
    bool _room43Known;
    string _roomMeta, _roomLine;
    readonly List<(string name, bool friend)> _roomOrder = new(6);

    void Room43Now(PanelData d)
    {
        if (_room43Known && ReferenceEquals(d.Room, _room43Seen)) return;
        _room43Known = true;
        _room43Seen = d.Room;
        _roomOrder.Clear();
        _roomMeta = _roomLine = null;
        var s = d.Room;
        if (s == null || !s.InRoom) return;
        int n = s.Here.Count;
        _roomMeta = n == 1 ? Lang.T("1 here") : Lang.F("{0} here", n);
        foreach (var p in s.Here) if (p.Friend && _roomOrder.Count < 6) _roomOrder.Add((p.Name, true));
        foreach (var p in s.Here) if (!p.Friend && _roomOrder.Count < 6) _roomOrder.Add((p.Name, false));
        var names = new List<string>(2);
        foreach (var (name, _) in _roomOrder) if (names.Count < 2) names.Add(name);
        int rest = n - names.Count;
        _roomLine = string.Join(", ", names) + (rest > 0 ? $" + {rest}" : "");
    }

    void RoomM43(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        bool click = _ui.Clickable(Ui.Id("wrm", id), r, out _, out bool held);
        if (held) _p.Fill(r, Theme.WithAlpha(Bento.Accent, 0.12f), CardRadius);
        if (click) OpenDetail(w);
        Room43Now(d);
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        CaptionRow(r, _roomMeta, CapRoomWide);
        if (_roomMeta == null)
        {
            string why = d.Room?.LogFound == false ? "VRChat is not running" : "Not in a world";
            TextAt(Ellipsize(why, iw, 20, FontWeight.SemiBold), x, r.Y + 64, iw + 2, Sub, 20, FontWeight.SemiBold);
            return;
        }
        bool compact = r.Height < 120;                    // a 6 x 4 one-row card (97 px)
        string world = string.IsNullOrEmpty(d.Room.World) ? "A world" : d.Room.World;
        float ws = compact ? 18 : 20;
        TextAt(Ellipsize(world, iw, ws, FontWeight.SemiBold), x, r.Y + (compact ? 50 : 62), iw + 2, Bento.Text, ws, FontWeight.SemiBold);
        float rad = compact ? 12 : 15, pitch = compact ? 18 : 23;
        float cy = r.Bottom - (compact ? 10 : 13) - rad;
        int shown = _roomOrder.Count;
        for (int i = 0; i < shown; i++) Avatar(x + rad + i * pitch, cy, rad, _roomOrder[i].name, _roomOrder[i].friend);
        float tx = x + 2 * rad + (shown - 1) * pitch + 12, room = r.Right - pad - tx;
        if (room > 40)
            TextAt(Ellipsize(_roomLine, room, SmallText, FontWeight.SemiBold), tx, cy + 6, room + 2, Sub, SmallText, FontWeight.SemiBold);
    }

    // ---- now playing on a 4 x 3 page: the card in its cover's colours ----

    const uint CoverText = 0xFFFFFFFF, CoverSub = 0xD0FFFFFF, CoverCap = 0xC0FFFFFF;
    readonly uint[] _pal = new uint[3];
    uint[] _palSrc;
    bool _palMade;
    int _palTheme = -1;

    // now playing in its cover's colours: on either grid, unless it has a look of its own
    bool CoverCard(WatchWidgets.Kind k, WatchWidget w, bool styled) => k.Id == "media" && !styled && !Theme.Cyber;

    // the card: three colours of the cover, corner to corner, darker toward the bottom (white text on it reads). A new cover: its
    // colours fade in over the old ones (and the cover over the old one: _coverFade, MediaCover)
    readonly uint[] _palOld = new uint[3];
    float _coverFade = 1;

    void CoverBack(RectangleF r, PanelData d, int id)
    {
        MediaNow(d);
        var pal = CoverPalette();
        string fk = Ui.Id("wcf", id);
        if (_artFade) _ui.SpringFrom(fk, 0);
        _coverFade = Math.Clamp(_ui.Spring(fk, 1, 170, 26), 0, 1);
        if (_coverFade < 0.999f)
        {
            _p.FillGradient(r, _palOld[0], _palOld[1], _palOld[2], CardRadius, 45);
            _p.PushFade(_coverFade);
            _p.FillGradient(r, pal[0], pal[1], pal[2], CardRadius, 45);
            _p.PopFade();
        }
        else _p.FillGradient(r, pal[0], pal[1], pal[2], CardRadius, 45);
        _p.FillGradient(r, 0x00000000, 0x4D000000, CardRadius, 90);
        _p.Stroke(RectangleF.Inflate(r, -0.6f, -0.6f), 0x1AFFFFFF, Bento.EdgeWidth, CardRadius - 0.6f);
        (_halo, _haloCard) = (0, RectangleF.Empty);
    }

    uint[] CoverPalette()
    {
        if (_palMade && ReferenceEquals(_palSrc, _artSource) && _palTheme == Theme.Version) return _pal;
        if (_palMade) Array.Copy(_pal, _palOld, 3);          // what a new cover's colours fade in over
        _palMade = true;
        _palSrc = _artSource;
        _palTheme = Theme.Version;
        if (_artSource == null)
        {
            // no cover: the accent, purple and a peach
            _pal[0] = Deep(Theme.Accent); _pal[1] = Deep(Bento.DataPurple); _pal[2] = Deep(0xFFFF9F7A);
            return _pal;
        }
        ArtPalette(_artSource, _pal);
        return _pal;
    }

    // a colour made dark enough for white text over it
    static uint Deep(uint c)
    {
        c |= 0xFF000000;
        for (int i = 0; i < 20 && Theme.Luma(c) > 0.42f; i++) c = Theme.Mix(c, 0xFF000000, 0.1f);
        return c;
    }

    // Three colours of a cover (96 x 96, premultiplied): its most common colours in 16 levels a channel, distinct from each other,
    // lightest first, each made dark enough for white text; fewer than three: darker steps of the first
    static void ArtPalette(uint[] px, uint[] outp)
    {
        var count = new int[4096];
        var sum = new long[4096 * 3];
        foreach (uint p in px)
        {
            uint a = p >> 24;
            if (a < 128) continue;
            int rr = (int)(((p >> 16) & 255) * 255 / a), gg = (int)(((p >> 8) & 255) * 255 / a), bb = (int)((p & 255) * 255 / a);
            rr = Math.Min(255, rr); gg = Math.Min(255, gg); bb = Math.Min(255, bb);
            int bin = (rr >> 4) << 8 | (gg >> 4) << 4 | (bb >> 4);
            count[bin]++;
            sum[bin * 3] += rr; sum[bin * 3 + 1] += gg; sum[bin * 3 + 2] += bb;
        }
        var picked = new List<int>(3);
        for (int round = 0; round < 2 && picked.Count < 3; round++)
            for (int k = 0; k < 64 && picked.Count < 3; k++)
            {
                int best = -1;
                for (int b = 0; b < 4096; b++)
                {
                    if (count[b] == 0 || picked.Contains(b)) continue;
                    int r0 = b >> 8, g0 = (b >> 4) & 15, b0 = b & 15;
                    // the first pass leaves out the near-greys (a cover's white border, its black text)
                    if (round == 0 && Math.Max(r0, Math.Max(g0, b0)) - Math.Min(r0, Math.Min(g0, b0)) < 3) continue;
                    bool far = true;
                    foreach (int q in picked)
                        if (Math.Abs((q >> 8) - r0) + Math.Abs(((q >> 4) & 15) - g0) + Math.Abs((q & 15) - b0) < 5) { far = false; break; }
                    if (far && (best < 0 || count[b] > count[best])) best = b;
                }
                if (best < 0) break;
                picked.Add(best);
            }
        var cols = new List<uint>(3);
        foreach (int b in picked)
        {
            long n = Math.Max(1, count[b]);
            cols.Add(0xFF000000 | (uint)(sum[b * 3] / n) << 16 | (uint)(sum[b * 3 + 1] / n) << 8 | (uint)(sum[b * 3 + 2] / n));
        }
        if (cols.Count == 0) cols.Add(0xFF3A3F55);
        cols.Sort((a, b) => Theme.Luma(b).CompareTo(Theme.Luma(a)));
        while (cols.Count < 3) cols.Add(Theme.Mix(cols[^1], 0xFF000000, 0.3f));
        for (int i = 0; i < 3; i++) outp[i] = Deep(cols[i]);
    }

    (bool ok, double pos, double dur) Timeline(PanelData d) =>
        d.FakeTimeline is { } f ? (true, f.pos, f.dur) : d.Media?.Timeline() ?? (false, 0, 0);

    // "1:42 / 4:03", made when a second changes
    int _tlPos = -1, _tlDur = -1;
    string _tlText;
    string TimelineText(double pos, double dur)
    {
        int p = (int)pos, du = (int)Math.Round(dur);
        if (p != _tlPos || du != _tlDur) { _tlPos = p; _tlDur = du; _tlText = $"{Clock2(p)} / {Clock2(du)}"; }
        return _tlText;
    }

    // a round button on the cover: the white disc (its glyph in the cover's colour), or a see-through one (previous / next)
    bool CoverDisc(string key, float cx, float cy, float dia, string glyph, uint glyphCol, bool enabled, bool white)
    {
        var b = new RectangleF(cx - dia / 2, cy - dia / 2, dia, dia);
        bool click = _ui.Clickable(key, MinHit(b, 44), out bool hover, out bool held) && enabled;
        float s = _ui.Spring(Ui.Id(key, "#s"), held && enabled ? 0.9f : 1f, 420, 18);
        float rad = dia * s / 2;
        uint fill = white ? (enabled ? 0xFFF2F3F7 : 0x80F2F3F7) : hover && enabled ? 0x4DFFFFFFu : 0x33FFFFFFu;
        _p.Circle(cx, cy, rad, fill);
        _p.Icon(glyph, new RectangleF(cx - rad, cy - rad, 2 * rad, 2 * rad), white ? glyphCol : enabled ? CoverText : CoverCap, MathF.Round(dia * 0.4f * s));
        return click;
    }

    void MediaCover(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var (has, title, artist, playing) = MediaNow(d);
        var pal = CoverPalette();
        var (tok, pos, dur) = Timeline(d);
        tok &= has;
        float pad = 14, x = r.X + pad, iw = r.Width - 2 * pad;
        string play = playing ? PauseFill : PlayFill;
        uint glyph = pal[1];
        if (w.W == 1)
        {
            // S: the cover fills the card (else its colours and a note), the play disc in the corner
            bool old = _coverFade < 0.999f && _artOld != null;
            if (_art != null || old)
            {
                _p.PushRoundedClip(r, CardRadius);
                if (old) _p.Image(_artOld, r, _art != null ? 1 : 1 - _coverFade);
                if (_art != null) _p.Image(_art, r, _coverFade);
                _p.FillGradient(r, 0x00000000, 0x80000000, 0, 90);
                _p.PopRoundedClip();
            }
            else _p.Icon("\uE8D6", new RectangleF(x - 4, r.Y + 12, 44, 44), CoverSub, 32);
            if (CoverDisc(Ui.Id("wcm", id * 3 + 1), r.Right - 12 - 22, r.Bottom - 12 - 22, 44, play, glyph, has, true)) Act(MediaIds[1]);
            float tw = r.Width - 2 * pad - 50;
            if (_art == null && tw > 40)
            {
                var (l1, l2) = TwoLines(has ? title ?? "" : NotPlaying, tw, 15, FontWeight.SemiBold);
                float by = l2.Length > 0 ? r.Bottom - 34 : r.Bottom - 16;
                TextAt(l1, x, by, tw + 2, CoverText, 15, FontWeight.SemiBold);
                if (l2.Length > 0) TextAt(l2, x, by + 19, tw + 2, CoverText, 15, FontWeight.SemiBold);
            }
            return;
        }
        bool tall = w.H >= 2, wide = w.W >= 4, compact = r.Height < 120;       // compact: a 6 x 4 one-row card (97 px)
        float tx = x, right = r.Right - pad;
        if (tall && r.Width >= r.Height * 1.5f)
        {
            // two rows and wide (6 x 4: 4x2, 6x2): the cover as tall as the card on the left; the caption, the title (two lines if it
            // must), the artist with where in the track, the line, and previous / play / next under them
            float s = r.Height - 28;
            var cover = new RectangleF(r.X + 14, r.Y + 14, s, s);
            CoverArt(cover);
            tx = cover.Right + 18;
            float tw = right - tx;
            CapText(Upper(CaptionWidth(CapMedia, tw) >= 0 && _p.MeasureTracked(Upper(CapMedia[0]), CapSize, FontWeight.SemiBold, CapTrack) <= tw ? CapMedia[0] : CapMedia[1]),
                tx, r.Y + CapBase, tw, CoverCap);
            float bcy = r.Bottom - 14 - 26;
            if (CoverDisc(Ui.Id("wcm", id * 3), tx + 20, bcy, 40, "\uE892", glyph, has, false)) Act(MediaIds[0]);
            if (CoverDisc(Ui.Id("wcm", id * 3 + 1), tx + 40 + 12 + 26, bcy, 52, play, glyph, has, true)) Act(MediaIds[1]);
            if (CoverDisc(Ui.Id("wcm", id * 3 + 2), tx + 40 + 12 + 52 + 12 + 20, bcy, 40, "\uE893", glyph, has, false)) Act(MediaIds[2]);
            float ty = r.Y + 60;
            if (!has) { TextAt(Ellipsize(NotPlaying, tw, SmallText, FontWeight.SemiBold), tx, ty, tw + 2, CoverSub, SmallText, FontWeight.SemiBold); return; }
            var (l1, l2) = TwoLines(title ?? "", tw, 20, FontWeight.SemiBold);
            TextAt(l1, tx, ty, tw + 2, CoverText, 20, FontWeight.SemiBold);
            if (l2.Length > 0) { ty += 24; TextAt(l2, tx, ty, tw + 2, CoverText, 20, FontWeight.SemiBold); }
            SubLine(tx, ty + 22, tw, artist, tok, pos, dur);
            if (tok) CoverBar(new RectangleF(tx, ty + 36, tw, 4), pos / dur);
            return;
        }
        if (wide)
        {
            // Wide: the cover on the left, previous / play / next on the right
            float s = r.Height - 28;
            var cover = new RectangleF(r.X + 14, r.Y + 14, s, s);
            CoverArt(cover);
            tx = cover.Right + 16;
            float cy = r.Y + r.Height / 2, big = compact ? 48 : 52;
            if (CoverDisc(Ui.Id("wcm", id * 3 + 2), r.Right - pad - 20, cy, 40, "\uE893", glyph, has, false)) Act(MediaIds[2]);
            if (CoverDisc(Ui.Id("wcm", id * 3 + 1), r.Right - pad - 40 - 10 - big / 2, cy, big, play, glyph, has, true)) Act(MediaIds[1]);
            if (CoverDisc(Ui.Id("wcm", id * 3), r.Right - pad - 40 - 10 - big - 10 - 20, cy, 40, "\uE892", glyph, has, false)) Act(MediaIds[0]);
            right = r.Right - pad - 40 - 10 - big - 10 - 40 - 16;
            if (compact) CoverText3(r, tx, right, r.Y + CapBase, r.Y + 52, r.Y + 72, has, title, artist, tok, pos, dur, 18, false);
            else CoverText3(r, tx, right, r.Y + CapBase, r.Y + 72, r.Y + 96, has, title, artist, tok, pos, dur, 22, false);
            if (tok) CoverBar(new RectangleF(tx, r.Bottom - (compact ? 14 : 18), right - tx, 4), pos / dur);
            return;
        }
        if (tall)
        {
            // L: the cover top-left, the play disc and previous / next beside it; the title (two lines if it must), the artist and
            // where in the track under them; the line at the bottom
            float s = MathF.Min(126, r.Height * 0.45f);
            var cover = new RectangleF(x, r.Y + 38, s, s);
            CapText(Upper(CapMedia[0]), x, r.Y + CapBase, iw, CoverCap);
            CoverArt(cover);
            float bx = cover.Right + (right - cover.Right) / 2, cy = cover.Y + s / 2;
            if (CoverDisc(Ui.Id("wcm", id * 3 + 1), bx, cy, 56, play, glyph, has, true)) Act(MediaIds[1]);
            if (CoverDisc(Ui.Id("wcm", id * 3), bx - 52, cy, 40, "\uE892", glyph, has, false)) Act(MediaIds[0]);
            if (CoverDisc(Ui.Id("wcm", id * 3 + 2), bx + 52, cy, 40, "\uE893", glyph, has, false)) Act(MediaIds[2]);
            float ty = cover.Bottom + 30;
            if (!has) { TextAt(NotPlaying, x, ty, iw + 2, CoverSub, SmallText, FontWeight.SemiBold); return; }
            var (l1, l2) = TwoLines(title ?? "", iw, 20, FontWeight.SemiBold);
            TextAt(l1, x, ty, iw + 2, CoverText, 20, FontWeight.SemiBold);
            if (l2.Length > 0) { ty += 24; TextAt(l2, x, ty, iw + 2, CoverText, 20, FontWeight.SemiBold); }
            SubLine(x, ty + 22, iw, artist, tok, pos, dur);
            if (tok) CoverBar(new RectangleF(x, r.Bottom - 18, iw, 4), pos / dur);
            return;
        }
        // M: the caption, the title and the artist with where in the track; the white play disc on the right; the line along the bottom
        float disc = compact ? 44 : 48, dcx = r.Right - pad - disc / 2, dcy = compact ? r.Y + r.Height / 2 : r.Y + 70;
        if (CoverDisc(Ui.Id("wcm", id * 3 + 1), dcx, dcy, disc, play, glyph, has, true)) Act(MediaIds[1]);
        right = dcx - disc / 2 - 12;
        if (compact) CoverText3(r, x, right, r.Y + CapBase, r.Y + 52, r.Y + 72, has, title, artist, tok, pos, dur, 18, true);
        else CoverText3(r, x, right, r.Y + CapBase, r.Y + 80, r.Y + 102, has, title, artist, tok, pos, dur, 20, true);
        if (tok) CoverBar(new RectangleF(x, r.Bottom - (compact ? 14 : 16), iw, 4), pos / dur);
    }

    // the cover on its card; a new one cross-fades over the old one (_coverFade, CoverBack)
    void CoverArt(RectangleF cover)
    {
        _p.Fill(cover, 0x26FFFFFF, 10);
        bool old = _coverFade < 0.999f && _artOld != null;
        if (_art != null || old)
        {
            _p.PushRoundedClip(cover, 10);
            if (old) _p.Image(_artOld, cover, _art != null ? 1 : 1 - _coverFade);
            if (_art != null) _p.Image(_art, cover, _coverFade);
            _p.PopRoundedClip();
        }
        if (_art == null) _p.Icon("\uE8D6", cover, Theme.WithAlpha(CoverSub, _coverFade), cover.Width * 0.36f);
    }

    // the caption, the title, the artist and the timeline on the cover's card (capFull: the caption across the card)
    void CoverText3(RectangleF r, float x, float right, float capBy, float titleBy, float subBy, bool has, string title, string artist,
        bool tok, double pos, double dur, float ts, bool capFull)
    {
        float tw = right - x;
        if (tw < 40) return;
        float cw = capFull ? r.Right - PadX(r) - x : tw;
        float c0 = CaptionWidth(CapMedia, cw);
        if (c0 >= 0) CapText(Upper(_p.MeasureTracked(Upper(CapMedia[0]), CapSize, FontWeight.SemiBold, CapTrack) <= cw ? CapMedia[0] : CapMedia[1]),
            x, capBy, c0, CoverCap);
        if (!has)
        {
            TextAt(Ellipsize(NotPlaying, tw, SmallText, FontWeight.SemiBold), x, titleBy, tw + 2, CoverSub, SmallText, FontWeight.SemiBold);
            return;
        }
        TextAt(Ellipsize(title ?? "", tw, ts, FontWeight.SemiBold), x, titleBy, tw + 2, CoverText, ts, FontWeight.SemiBold);
        SubLine(x, subBy, tw, artist, tok, pos, dur);
    }

    // "M83 · 1:42 / 4:03": the artist cut to leave the time whole
    void SubLine(float x, float by, float w, string artist, bool tok, double pos, double dur)
    {
        const float ss = 15;
        string time = tok ? TimelineText(pos, dur) : null;
        float timeW = time == null ? 0 : _p.MeasureWidth(time, ss, FontWeight.SemiBold);
        string a = artist ?? "";
        if (time == null) { TextAt(Ellipsize(a, w, ss, FontWeight.SemiBold), x, by, w + 2, CoverSub, ss, FontWeight.SemiBold); return; }
        float sep = a.Length > 0 ? _p.MeasureWidth(" · ", ss, FontWeight.SemiBold) : 0;
        float aw = w - timeW - sep;
        if (a.Length > 0 && aw >= 30)
        {
            string cut = Ellipsize(a, aw, ss, FontWeight.SemiBold);
            float cwid = _p.MeasureWidth(cut, ss, FontWeight.SemiBold);
            TextAt(cut, x, by, cwid + 2, CoverSub, ss, FontWeight.SemiBold);
            x += cwid;
            TextAt(" · ", x, by, sep + 2, CoverSub, ss, FontWeight.SemiBold);
            x += sep;
        }
        TextAt(time, x, by, timeW + 2, CoverSub, ss, FontWeight.SemiBold);
    }

    void CoverBar(RectangleF bar, double frac)
    {
        float f = (float)Math.Clamp(frac, 0, 1), h = bar.Height;
        _p.Fill(bar, 0x40FFFFFF, h / 2);
        if (f > 0) _p.Fill(new RectangleF(bar.X, bar.Y, MathF.Max(h, bar.Width * f), h), CoverText, h / 2);
    }

    // ---- batteries on a 4 x 3 page ----

    static readonly string[] MainDevices = { "HMD", "L", "R" };
    readonly List<(string label, string name, float pct, bool charging)> _bat4 = new(4);

    // HMD, left, right (those there), then the emptiest of the rest - the trackers' lowest: the four the 4 x 3 cards show
    void PickFour(List<(string label, float pct, bool charging)> bats)
    {
        _bat4.Clear();
        foreach (var m in MainDevices)
            foreach (var b in bats)
                if (b.label == m) { _bat4.Add((b.label, LongName(b.label), b.pct, b.charging)); break; }
        while (_bat4.Count < 4)
        {
            int low = -1;
            for (int i = 0; i < bats.Count; i++)
            {
                if (Array.IndexOf(MainDevices, bats[i].label) >= 0 || _bat4.Exists(x => x.label == bats[i].label || x.label == "Trk" && bats[i].label.StartsWith("Trk"))) continue;
                if (low < 0 || bats[i].pct < bats[low].pct) low = i;
            }
            if (low < 0) break;
            var b = bats[low];
            bool trk = b.label.StartsWith("Trk", StringComparison.Ordinal);
            _bat4.Add((trk ? "Trk" : b.label, trk ? "Trk min" : LongName(b.label), b.pct, b.charging));
        }
    }

    static string LongName(string label) => label switch { "L" => "Left", "R" => "Right", _ => label };

    // S: a bar per device, standing, filled from the bottom in its level's colour (a bolt in its top while charging), its name under it
    void BatteryBars(RectangleF r, PanelData d)
    {
        var bats = d.Vr.Batteries;
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        Caption(r, iw, CapBatteries);
        if (bats.Count == 0)
        {
            TextAt(Ellipsize(d.VrConnected ? "No battery info" : "SteamVR off", iw, SmallText, FontWeight.SemiBold), x, r.Y + 64, iw + 2, Sub,
                SmallText, FontWeight.SemiBold);
            return;
        }
        PickFour(bats);
        float colW = iw / 4f, bw = 14, top = r.Y + 38, bottom = r.Bottom - 31;
        for (int j = 0; j < _bat4.Count; j++)
        {
            var b = _bat4[j];
            float cx = x + colW * (j + 0.5f);
            var lv = BatteryLevel(ShownPct(b.pct));
            var bar = new RectangleF(cx - bw / 2, top, bw, bottom - top);
            _p.Fill(bar, Bento.Track, bw / 2);
            float fh = MathF.Max(bw, bar.Height * Math.Clamp(b.pct, 0, 1));
            if (b.pct > 0.005f) _p.Fill(new RectangleF(bar.X, bar.Bottom - fh, bw, fh), Bento.Level(lv, Bento.Ok), bw / 2);
            if (b.charging) _p.Icon(Bolt, new RectangleF(cx - 8, bar.Y + 1, 16, 16), BoltInk(Bento.Text), 12);
            float ls = MathF.Max(14, FitSize(Bat(b.label), colW - 2, 15, FontWeight.SemiBold, null));
            TextAt(Bat(b.label), cx - colW / 2, r.Bottom - 12, colW, ls >= 15 ? Sub : Bento.Badge ? Bento.Text : Theme.Mix(Sub, Bento.Text, 0.35f), ls,
                FontWeight.SemiBold, TextAlignment.Center);
        }
    }

    // M: four rows in two columns - the name, the percentage (amber / red when low), its line under them
    void BatteryRows43(RectangleF r, PanelData d, int id)
    {
        var bats = d.Vr.Batteries;
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        Caption(r, iw, CapBatteries);
        if (bats.Count == 0)
        {
            TextAt(Ellipsize(d.VrConnected ? "No battery info" : "SteamVR off", iw, SmallText, FontWeight.SemiBold), x, r.Y + 64, iw + 2, Sub,
                SmallText, FontWeight.SemiBold);
            return;
        }
        PickFour(bats);
        const float gapX = 18;
        float cw = (iw - gapX) / 2, top = r.Y + 36, ch = (r.Bottom - 10 - top) / 2;
        for (int j = 0; j < _bat4.Count; j++)
        {
            var b = _bat4[j];
            var c = new RectangleF(x + j % 2 * (cw + gapX), top + j / 2 * ch, cw, ch);
            int shown = ShownPct(b.pct);
            var lv = BatteryLevel(shown);
            string pct = Pct(shown);
            float by = c.Y + 21, pw = _p.MeasureWidth(pct, 20, FontWeight.Bold, Theme.Display);
            TextAt(pct, c.Right - pw - 2, by, pw + 4, Bento.Level(lv, Bento.Text), 20, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
            float nx = c.X;
            if (b.charging) { _p.Icon(Bolt, new RectangleF(nx - 2, by - 15, 16, 17), BoltInk(Bento.Text), 15); nx += 15; }
            float room = c.Right - pw - 8 - nx;
            TextAt(Ellipsize(b.name, room, 15, FontWeight.SemiBold), nx, by, room + 2, Sub, 15, FontWeight.SemiBold);
            Meter(new RectangleF(c.X, c.Y + 29, c.Width, 6), b.pct, Bento.Level(lv, Bento.Ok), Ui.Id("wbt", id * 8 + j));
        }
    }

    // ---- CPU and memory as 4 x 3 S ----

    // the load big, the last 30 seconds as bars along the bottom (purple; amber over 75 %, red over 90 %) - as tall as the window's
    // busiest second (a quarter at least), so a PC idling at 10 % still shows its ups and downs
    void CpuS43(RectangleF r, PanelData d)
    {
        var (_, value, _, _, lv) = Reading("cpu", d);
        bool none = value == "–";
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        Caption(r, iw, CapCpu, lv);
        ValueText(x, r.Y + (_big ? 78 : SBase), value, MathF.Max(24, FitSize(value, iw, Hero, FontWeight.Bold, Theme.Display)), none ? Bento.Faint : Bento.Level(lv, Bento.Text), null);
        int n = Math.Min(30, Stats.CpuCount);
        float pitch = iw / 30f, bw = MathF.Max(1.6f, pitch - (_big ? 1.4f : 0.8f)), hmax = _big ? 24 : 16, bottom = r.Bottom - (_big ? 12 : 10);
        uint role = RoleColor("cpu");
        float top = 0.25f;
        for (int i = 0; i < n; i++) top = MathF.Max(top, Stats.CpuHistory[(Stats.CpuHead - n + i + SystemStats.History) % SystemStats.History]);
        for (int i = 0; i < n; i++)
        {
            float v = Stats.CpuHistory[(Stats.CpuHead - n + i + SystemStats.History) % SystemStats.History];
            float h = MathF.Max(3, v / top * hmax);
            uint c = v > 0.9f ? Bento.Level(StatLevel.Bad, role) : v > 0.75f ? Bento.Level(StatLevel.Warn, role) : role;
            _p.Fill(new RectangleF(x + iw - (n - i) * pitch + (pitch - bw) / 2, bottom - h, bw, h), c, 1);
        }
    }

    // the amount in use big, of how much under it, the line (blue)
    void MemS43(RectangleF r, string id, PanelData d, int key)
    {
        var (frac, value, _, caption, lv) = Reading(id, d);
        bool none = value == "–";
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        Caption(r, iw, SmallCaption(id), lv);
        ValueUnit(x, r.Y + 80, iw, value, none ? null : "GB", Hero, 24, none ? Bento.Faint : Bento.Level(lv, Bento.Text));
        if (!none) TextAt(Ellipsize(caption, iw, 15, FontWeight.SemiBold), x, r.Y + 104, iw + 2, Sub, 15, FontWeight.SemiBold);
        Meter(new RectangleF(x, r.Bottom - 14, iw, 4), frac, Bento.Level(lv, RoleColor(id)), Ui.Id("wsg", key));
    }

    // ---- the frame graph on a 4 x 3 page: the last 30 s as a line ----

    static readonly string[] CapGraph30 = { "Frame time · 30 s", "Frame time", "Frames" };
    readonly Vector2[] _gp = new Vector2[VrStats.LongBuckets + 2];
    float _gBudget = -1, _gFpsV = -9, _gHz = -9, _gLowV = -9, _gRpV = -9;
    int _gBudgetLang = -1;
    string _gBudgetText, _gFps, _gFpsUnit, _gLow, _gRp;

    // The slowest frame of every tenth of a second (GPU ms) as a line with the area under it, the budget (1000 / the headset's Hz)
    // dashed, the peak a dot (white under budget, amber over, red on a dropped frame). L: under it the frame rate (of the headset's),
    // the slowest 1 % as a frame rate, and how much was reprojected.
    void GraphLine(RectangleF r, WatchWidget w, PanelData d)
    {
        var v = d.Vr;
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        float hz = v.DisplayHz, budget = 1000f / Math.Max(1, hz);
        bool tall = w.H >= 2;
        if (budget != _gBudget || _gBudgetLang != Lang.Version) { _gBudget = budget; _gBudgetLang = Lang.Version; _gBudgetText = Lang.F("{0:0.0} ms budget", budget); }
        bool coloured = Bento.Badge || Bento.CapBadge;           // a filled card or a band: amber would not read there
        CaptionRow(r, x, r.Right - pad, _gBudgetText, coloured ? Bento.Cap : Bento.WarnInk, CapGraph30);
        var g = RectangleF.FromLTRB(x, r.Y + 40, r.Right - pad, tall ? r.Bottom - 72 : r.Bottom - 12);
        const int LB = VrStats.LongBuckets;
        long now = v.LongNow;
        int n = 0, peakAt = -1;
        float peak = 0;
        byte peakFlag = 0;
        for (int k = 0; k < LB && now >= 0; k++)
        {
            long tick = now - (LB - 1) + k;
            if (tick < 0) continue;
            int idx = (int)(tick % LB);
            if (v.LongTick[idx] != tick) continue;
            float ms = v.LongMs[idx];
            _gp[n] = new Vector2(g.X + g.Width * k / (LB - 1), ms);
            if (ms > peak) { peak = ms; peakAt = n; peakFlag = v.LongFlag[idx]; }
            n++;
        }
        ref readonly var L = ref Bento.Cur;
        uint line = L.GraphBar != 0 ? Bento.Text : Bento.Accent;
        if (n < 2)
            TextAt("Waiting for frames", x, g.Y + g.Height / 2 + 6, iw + 2, Sub, SmallText, FontWeight.SemiBold);
        else
        {
            float top = MathF.Max(budget * 1.5f, peak * 1.1f);
            for (int i = 0; i < n; i++) _gp[i].Y = g.Bottom - g.Height * Math.Clamp(_gp[i].Y / top, 0, 1);
            _gp[n] = new Vector2(_gp[n - 1].X, g.Bottom);
            _gp[n + 1] = new Vector2(_gp[0].X, g.Bottom);
            _p.Polygon(_gp, n + 2, Theme.WithAlpha(line, 0.18f));
            _p.Polyline(_gp, n, line, 2.5f);
            float by = g.Bottom - g.Height * (budget / top);
            _p.DashedLine(g.X, by, g.Right, by, coloured ? Theme.WithAlpha(Bento.Text, 0.6f) : Theme.WithAlpha(Bento.WarnInk, 0.8f), 1.5f, 5, 4);
            var pk = _gp[peakAt];
            uint pc = peakFlag == 2 ? L.FlagBad : peak > budget ? L.FlagWarn : Bento.Text;
            HaloDisc(pk.X, pk.Y, 7);
            _p.Circle(pk.X, pk.Y, 4.5f, pc);
        }
        if (!tall) return;
        float fps = v.AppFps, low = v.OnePercentLow(), rp = v.ReprojPct;
        if (MathF.Round(fps) != _gFpsV || hz != _gHz)
        {
            _gFpsV = MathF.Round(fps);
            _gHz = hz;
            _gFps = fps < 0 ? "–" : $"{fps:0}";
            _gFpsUnit = $"/ {hz:0}";
        }
        if (MathF.Round(low) != _gLowV) { _gLowV = MathF.Round(low); _gLow = low < 0 ? "–" : $"{low:0}"; }
        if (MathF.Round(rp) != _gRpV) { _gRpV = MathF.Round(rp); _gRp = rp < 0 ? "–" : $"{rp:0}%"; }
        float cw = iw / 3, vb = r.Bottom - 36, lb = r.Bottom - 13;
        var fl = FpsLevel(fps, hz);
        var ll = low < 0 ? StatLevel.Ok : FpsLevel(low, hz);
        var rl = ReprojLevel(rp);
        StatCol(x, vb, lb, cw, _gFps, fps < 0 ? null : _gFpsUnit, "fps", fps < 0 ? Bento.Faint : Bento.Level(fl, Bento.Text));
        StatCol(x + cw, vb, lb, cw, _gLow, null, "1% low fps", low < 0 ? Bento.Faint : Bento.Level(ll, Bento.Text));
        StatCol(x + 2 * cw, vb, lb, cw, _gRp, null, "reprojected", rp < 0 ? Bento.Faint : Bento.Level(rl, Bento.Text));
    }

    void StatCol(float x, float vb, float lb, float cw, string value, string unit, string label, uint vc)
    {
        ValueUnit(x, vb, cw - 8, value, unit, 28, 20, vc);
        TextAt(Ellipsize(label, cw - 6, 15, FontWeight.SemiBold), x, lb, cw - 4, Sub, 15, FontWeight.SemiBold);
    }

    // ================= the new kinds =================

    // ---- the microphone: Windows' default recording device; a tap mutes it or turns it back on ----

    static readonly string[] CapMic = { "Microphone", "Mic" };
    const string MicGlyph = "\uE720", MicOffGlyph = "\uEC54";

    // Live: the microphone as big as a value, "Live" after it, the input level as a line. Muted: the danger card (DrawPage), the
    // microphone crossed out in red, "Muted". No device: faint, "No mic".
    void MicOrbit(RectangleF r, WatchWidget w, int id)
    {
        bool click = _ui.Clickable(Ui.Id("wmic", id), r, out _, out bool held);
        if (click) { Mic.ToggleMute(); _dirty = true; }
        bool has = Mic.Present, muted = has && Mic.Muted;
        if (held) _p.Fill(r, Theme.WithAlpha(muted ? Bento.Bad : Bento.Accent, 0.14f), CardRadius);
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        Caption(r, iw, CapMic);
        string word = !has ? "No mic" : muted ? "Muted" : "Live";
        uint ic = !has ? Bento.Faint : muted ? Bento.BadInk : Bento.Text, wc = !has ? Bento.Faint : muted ? Bento.Text : Sub;
        var fw = muted ? FontWeight.Bold : FontWeight.SemiBold;
        float gs = Hero, by = r.Y + SBase, mid = by - Hero * DigitH / 2;
        _p.Icon(muted ? MicOffGlyph : MicGlyph, new RectangleF(x - 4, mid - gs / 2 - 1, gs + 8, gs + 2), ic, gs);
        float tx = x + gs + 8, room = r.Right - pad - tx;
        float ws = _big || w.W >= 2 ? 20 : SmallText;
        bool below = false;
        if (_p.MeasureWidth(word, ws, fw) <= room) TextAt(word, tx, by, room + 2, wc, ws, fw);
        else { below = true; TextAt(Ellipsize(word, iw, SmallText, fw), x, r.Bottom - 12, iw + 2, wc, SmallText, fw); }
        if (!has || muted || below) return;
        // the level as it is (no spring: it moves all the time while you talk)
        var bar = new RectangleF(x, r.Bottom - (w.W >= 2 || _big ? 17 : 16), iw, w.W >= 2 ? 6 : 4);
        float f = Mic.Peak <= 0 ? 0 : MathF.Min(1, MathF.Sqrt(Mic.Peak));
        _p.Fill(bar, Bento.Track, bar.Height / 2);
        if (f > 0.01f) _p.Fill(new RectangleF(bar.X, bar.Y, MathF.Max(bar.Height, bar.Width * f), bar.Height), Bento.Ok, bar.Height / 2);
    }

    void Mic95(RectangleF r, RectangleF c, WatchWidget w, int id)
    {
        if (_ui.Clickable(Ui.Id("wmic", id), r, out _, out _)) { Mic.ToggleMute(); _dirty = true; }
        bool has = Mic.Present, muted = has && Mic.Muted;
        if (w.W >= 2 && c.Height >= 34)
        {
            bool m = muted;
            if (_ui.Check95(Ui.Id("wmk", id), c.X + 4, c.Y + 4, "Mute", ref m, has)) { Mic.ToggleMute(); _dirty = true; }
            _ui.Progress95(new RectangleF(c.X + 4, c.Bottom - 16, c.Width - 8, 12), has && !muted ? MathF.Sqrt(MathF.Max(0, Mic.Peak)) : 0, 0xFF008000);
            return;
        }
        Value95(c, !has ? "--" : muted ? "Muted" : "Live", _ui.Width95("microphone") <= c.Width - 2 ? "microphone" : "mic", muted ? 0xFF800000 : Blk95);
    }

    // ---- the notifications: the last ones shown, newest first - who (an initial), what, how long ago ----

    static readonly string[] CapNotes = { "Notifications", "Notices" };
    int _notesVersion = -2;
    readonly List<(string text, string who, DateTime at)> _noteRows = new();
    string _notesCount;

    bool _notesSlide;              // one came in: the rows slide down to make room for it (the next frame's cards)

    void NotesNow(PanelData d)
    {
        int v = d.Notes?.Version ?? -1;
        if (v == _notesVersion) return;
        _notesSlide = _notesVersion != -2 && !_settled && d.Notes != null && d.Notes.Items.Count > _noteRows.Count;
        _notesVersion = v;
        _noteRows.Clear();
        _notesCount = null;
        if (d.Notes == null) return;
        foreach (var it in d.Notes.Items)
        {
            if (_noteRows.Count >= 8) break;
            string t = it.Title?.Trim() ?? "", c = it.Content?.Trim() ?? "";
            // a room card says who in its title ("Tobi joined"); the others what in their text (the app is the initial)
            string text = it.Source == "Room" || c.Length == 0 ? (t.Length > 0 ? t : c) : c;
            string who = it.Source == "Room" || string.IsNullOrEmpty(it.Source) ? t : it.Source;
            _noteRows.Add((text, who, it.At));
        }
        int n = d.Notes.Items.Count;
        if (n > 0) _notesCount = n.ToString(CultureInfo.InvariantCulture);
    }

    static string[] AgeM, AgeH;
    static int _agesLang = -1;
    // the units in the language picked (a letter by itself is no line of the table: "age|m" is)
    static string[] MakeAges(string u, int n)
    {
        u = Lang.C("age", u);
        var a = new string[n];
        for (int i = 0; i < n; i++) a[i] = i.ToString(CultureInfo.InvariantCulture) + " " + u;
        return a;
    }

    // "now", "2 m", "3 h", "2 d"
    static string Age(DateTime at, DateTime now)
    {
        double s = (now - at).TotalSeconds;
        if (s < 60) return Lang.T("now");
        if (_agesLang != Lang.Version) { AgeM = MakeAges("m", 60); AgeH = MakeAges("h", 24); _agesLang = Lang.Version; }
        if (s < 3600) return AgeM[(int)(s / 60)];
        if (s < 86400) return AgeH[(int)(s / 3600)];
        return ((int)(s / 86400)).ToString(CultureInfo.InvariantCulture) + " " + Lang.C("age", "d");
    }

    void NotesOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        NotesNow(d);
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        CaptionRow(r, _notesCount, CapNotes);
        if (_noteRows.Count == 0)
        {
            TextAt(Ellipsize("No notifications", iw, SmallText, FontWeight.SemiBold), x, r.Y + MathF.Min(64, r.Height * 0.5f + 12), iw + 2, Sub,
                SmallText, FontWeight.SemiBold);
            return;
        }
        float top = r.Y + 34, bottom = r.Bottom - 8;
        int slots = Math.Max(1, (int)((bottom - top) / 27));
        float rh = (bottom - top) / slots;
        var now = DateTime.Now;
        const float rad = 11;
        // a new one slides in at the top, the others move down a row (the last one out at the bottom)
        string sk = Ui.Id("wnt", id);
        if (_notesSlide) _ui.SpringFrom(sk, 0);
        float e = Math.Clamp(_ui.Spring(sk, 1, 200, 26), 0, 1), dy = (e - 1) * rh;
        bool sliding = e < 0.999f;
        if (sliding) _p.PushClip(RectangleF.FromLTRB(r.X, top - 2, r.Right, bottom + 2));
        for (int i = 0; i < Math.Min(slots + (sliding ? 1 : 0), _noteRows.Count); i++)
        {
            var (text, who, at) = _noteRows[i];
            float cy = top + rh * (i + 0.5f) + dy, by = cy + 6;
            bool fresh = sliding && i == 0;
            if (fresh) _p.PushFade(e);
            _p.Circle(x + rad, cy, rad, AvatarColor(who));
            _p.Text(InitialOf(who), new RectangleF(x, cy - rad, 2 * rad, 2 * rad + 1), Bento.Dark, 14, FontWeight.Bold, TextAlignment.Center,
                ParagraphAlignment.Center);
            string age = Age(at, now);
            float aw = _p.MeasureWidth(age, 15, FontWeight.SemiBold);
            TextAt(age, r.Right - pad - aw - 2, by, aw + 4, Sub, 15, FontWeight.SemiBold);
            float tx = x + 2 * rad + 9, tw = r.Right - pad - aw - 10 - tx;
            TextAt(Ellipsize(text, tw, SmallText, FontWeight.SemiBold), tx, by, tw + 2, Bento.Text, SmallText, FontWeight.SemiBold);
            if (fresh) _p.PopFade();
        }
        if (sliding) _p.PopClip();
    }

    void Notes95(RectangleF c, PanelData d)
    {
        NotesNow(d);
        var box = new RectangleF(c.X + 1, c.Y + 1, c.Width - 2, c.Height - 2);
        _p.Fill(box, Wht95);
        _p.Bevel(box, Painter.Edge.Sunken);
        var inner = RectangleF.Inflate(box, -4, -3);
        if (_noteRows.Count == 0) { _ui.Text95(Fit("(no notifications)", inner.Width), inner, Gry95, false, TextAlignment.Center); return; }
        int rows = Math.Max(1, (int)(inner.Height / 13));
        var now = DateTime.Now;
        for (int i = 0; i < Math.Min(rows, _noteRows.Count); i++)
        {
            var (text, _, at) = _noteRows[i];
            float y = inner.Y + i * 13;
            string age = Age(at, now);
            float aw = _ui.Width95(age) + 4;
            _ui.Text95(Fit(text, inner.Width - aw - 4), new RectangleF(inner.X, y, inner.Width - aw, 13), Blk95);
            _ui.Text95(age, new RectangleF(inner.Right - aw, y, aw, 13), Gry95, false, TextAlignment.Trailing);
        }
    }

    // ---- a group of buttons in one card: each does what its own button does, on = the accent ----

    readonly List<string> _groupIds = new(6);

    void GroupIdsOf(WatchWidget w)
    {
        _groupIds.Clear();
        if (w.Items != null)
            foreach (var s in w.Items)
                if (s != null && Array.IndexOf(WatchWidgets.GroupKinds, s) >= 0 && !_groupIds.Contains(s) && _groupIds.Count < 6) _groupIds.Add(s);
        if (_groupIds.Count == 0) _groupIds.AddRange(WatchWidgets.GroupDefault);
    }

    static string GroupLabel(string id, PanelData d) => id == "layout" ? d.LayoutName : TileName(WatchWidgets.Find(id), false);

    // One row: up to three tiles side by side, the icon over the name (only icons when a name would be under 14 px); more in two rows,
    // icons only. Two rows tall: pills, the icon beside the name. Inner tiles: radius 10, 8 px in from the card, 6 apart.
    void GroupOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        GroupIdsOf(w);
        int n = _groupIds.Count;
        bool small = r.Height < 120 && w.H == 1;
        float inset = 8, gap = small ? 5 : 6;
        var a = RectangleF.Inflate(r, -inset, -inset);
        if (Bento.Cur.Band != 0) a = RectangleF.FromLTRB(a.Left, r.Top + 12, a.Right, a.Bottom);     // a header's strip along the top: clear of it
        int cols, rows, mode;              // mode 0: icon over name, 1: icon only, 2: icon beside name
        if (w.H >= 2) { cols = n <= 3 ? 1 : 2; rows = (n + cols - 1) / cols; mode = 2; }
        else if (n <= 3 || a.Width / n >= 90) { cols = n; rows = 1; mode = a.Width / n >= 150 ? 2 : 0; }
        else { cols = (n + 1) / 2; rows = 2; mode = 1; }
        float tw = (a.Width - (cols - 1) * gap) / cols, th = (a.Height - (rows - 1) * gap) / rows;
        if (mode == 0)
        {
            // the names only when every one of them fits (a row of names and bare icons looks broken); the layout's own name does
            // not count - it is shortened ("Desk…")
            if (th < 64) mode = 1;
            else
                foreach (var gid in _groupIds)
                    if (gid != "layout" && FitSize(GroupLabel(gid, d), tw - 8, 15, FontWeight.SemiBold, null) < 14) { mode = 1; break; }
        }
        else if (mode == 2)
            foreach (var gid in _groupIds)
                if (gid != "layout" && _p.MeasureWidth(GroupLabel(gid, d), 15, FontWeight.Bold) > tw - 24 - 24 - 10)
                {
                    // pills too narrow for the names: icons only, three a row
                    mode = 1;
                    cols = Math.Min(n, 3);
                    rows = (n + cols - 1) / cols;
                    tw = (a.Width - (cols - 1) * gap) / cols;
                    th = (a.Height - (rows - 1) * gap) / rows;
                    break;
                }
        for (int j = 0; j < n; j++)
        {
            var t = new RectangleF(a.X + j % cols * (tw + gap), a.Y + j / cols * (th + gap), tw, th);
            GroupTile(Ui.Id("wgb", id * 8 + j), t, _groupIds[j], d, mode);
        }
    }

    void GroupTile(string key, RectangleF t, string kid, PanelData d, int mode)
    {
        var k = WatchWidgets.Find(kid);
        if (k == null) return;
        bool on = IsOn(kid, d);
        bool click = _ui.Clickable(key, MinHit(t), out bool hover, out bool held);
        float s = _ui.Spring(Ui.Id(key, "#s"), held ? 0.92f : 1f, 420, 18);
        float a = _ui.Anim(Ui.Id(key, "#on"), on ? 1 : 0, 16);         // on / off: the fill eases over
        bool scaled = MathF.Abs(s - 1) > 0.001f;
        if (scaled) _p.PushScale(s, t.X + t.Width / 2, t.Y + t.Height / 2);
        const float rad = 10;
        _p.Fill(t, Theme.Mix(hover ? Bento.TileHover : Bento.Tile, Bento.AccentFill, a), rad);
        uint fg = Theme.Mix(Bento.Text, Bento.OnAccent, a);
        var lw = a > 0.5f ? FontWeight.Bold : FontWeight.SemiBold;
        string icon = kid == "desktops" && !d.DesktopsVisible ? Theme.IcoHide : k.Icon;
        string label = GroupLabel(kid, d);
        const float ic = 24;
        if (mode == 2)
        {
            float room = t.Width - 24 - ic - 10;
            float ls = MathF.Max(15, FitSize(label, room, SmallText, lw, null));
            string lab = Ellipsize(label, room, ls, lw);
            float lwid = _p.MeasureWidth(lab, ls, lw), total = ic + 10 + lwid, lx = t.X + (t.Width - total) / 2;
            _p.Icon(icon, new RectangleF(lx - 2, t.Y, ic + 4, t.Height), fg, ic);
            _p.Text(lab, new RectangleF(lx + ic + 10, t.Y, lwid + 4, t.Height), fg, ls, lw);
        }
        else if (mode == 0)
        {
            float ls = MathF.Max(14, FitSize(label, t.Width - 8, 15, lw, null));
            float cy = t.Y + t.Height * 0.4f;
            _p.Icon(icon, new RectangleF(t.X, cy - ic / 2 - 2, t.Width, ic + 4), fg, ic);
            TextAt(Ellipsize(label, t.Width - 8, ls, lw), t.X + 4, t.Bottom - 14, t.Width - 6, fg, ls, lw, TextAlignment.Center);
        }
        else _p.Icon(icon, t, fg, 22);
        if (scaled) _p.PopScale();
        if (click) Act(kid);
    }

    // 95: plain push buttons in a row (a toolbar), the ones that are on pushed in; a tall one in two columns with their names
    void Group95(RectangleF c, WatchWidget w, PanelData d, int id)
    {
        GroupIdsOf(w);
        int n = _groupIds.Count;
        int cols = w.H >= 2 ? Math.Min(2, n) : n, rows = (n + cols - 1) / cols;
        var a = RectangleF.Inflate(c, -2, -2);
        float bw = MathF.Floor((a.Width - (cols - 1) * 2) / cols), bh = MathF.Floor(MathF.Min(26, (a.Height - (rows - 1) * 2) / rows));
        float y0 = MathF.Floor(a.Y + (a.Height - rows * bh - (rows - 1) * 2) / 2);
        for (int j = 0; j < n; j++)
        {
            string kid = _groupIds[j];
            var k = WatchWidgets.Find(kid);
            var b = new RectangleF(a.X + j % cols * (bw + 2), y0 + j / cols * (bh + 2), bw, bh);
            var icon = kid == "desktops" && !d.DesktopsVisible ? W95Icons.Id.ComputerOff : k.Icon95;
            string label = bw >= 72 ? Ui.Esc95(Fit(GroupLabel(kid, d), bw - 30)) : null;
            if (_ui.Button95(Ui.Id("wgb", id * 8 + j), b, label, true, false, IsOn(kid, d), icon)) Act(kid);
        }
    }
}
