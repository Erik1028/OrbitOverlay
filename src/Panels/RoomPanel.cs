using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Valve.VR;
using Vortice.DirectWrite;

namespace Orbit;

// Who is in the VRChat instance with you: starred people and friends first, then everyone else (newest first), and who
// left lately. A star beside each name marks someone to be told about when they come and go. A floating panel like the
// dock: grab it, the stick scrolls the list. Data: RoomWatcher (VRChat's log; friends from VRCX when it is installed).
// Opened from Settings; the watch's room widget opens the watch's own view of the same list instead (WristRoom.cs).
public sealed partial class RoomPanel : IPointerTarget, IDisposable
{
    public const int TW = 800, TH = 480, W95 = TW / 2, H95 = TH / 2;

    readonly Painter _p;
    readonly Ui _ui;
    readonly ConfigStore _cfg;
    readonly PointerState _ptr = new();
    public readonly Surface Surf;
    Surface _canvas95;
    public VrOverlay Ov { get; private set; }
    VrSession _vr;
    float _vis, _scroll, _twoStartWidth;
    bool _dirty = true;
    double _nextDraw;
    int _seen = -1;
    Hand? _owner;
    RoomWatcher.Snapshot _s = new();

    RoomConfig C => _cfg.Data.Room;
    public bool Shown => C.Enabled && C.Visible;

    public RoomPanel(Gpu g, Painter p, ConfigStore cfg)
    {
        _p = p; _cfg = cfg;
        _ui = new Ui(p);
        Surf = new Surface(g, TW, TH);
    }

    public void CreateOverlay(VrSession vr)
    {
        _vr = vr;
        Ov = new VrOverlay("erik.orbit.room", "OrbitOverlay Room");
        Ov.SetSortOrder(136);
        Ov.SetFlag(VROverlayFlags.IsPremultiplied, true);
        Ov.SetMouseScale(TW, TH);
        _vis = 0;
        _dirty = true;
    }

    public void DestroyOverlay() { _ptr.Leave(); Ov?.Dispose(); Ov = null; _vr = null; _vis = 0; }

    public void Toggle()
    {
        if (C.Visible) { C.Visible = false; _cfg.MarkDirty(); return; }
        C.Visible = true;
        _cfg.MarkDirty();
        _dirty = true;
        if (C.Place.M == null) BringHere();
    }

    // 0.65 m ahead, a little below the eyes, tilted back slightly
    public void BringHere()
    {
        if (_vr == null || !_vr.HmdValid) return;
        var local = Matrix4x4.CreateRotationX(-14 * MathF.PI / 180) * Matrix4x4.CreateTranslation(0.25f, -0.12f, -0.65f);
        C.Place.Attach = "world";
        C.Place.M = MathUtil.ToArray(local * MathUtil.PlacementFrame(_vr.Hmd, _vr.OnFace, "room"));
        _cfg.MarkDirty();
    }

    public void Update(VrSession vr, double now, float dt, RoomWatcher.Snapshot s)
    {
        if (Ov == null) return;
        _vis = Motion.Step(_vis, Shown, dt, 0.18f);                      // Look > Motion off: at once
        if (_vis <= 0 || C.Place.M == null || !Placement.Apply(C.Place, vr, Ov))
        {
            if (_vis <= 0) _ptr.Leave();
            Ov.Hide();
            return;
        }
        float e = 1 - MathF.Pow(1 - _vis, 3);
        Ov.SetAlpha(e);
        Ov.SetWidth(Math.Clamp(C.Width, 0.2f, 1.2f) * (0.92f + 0.08f * e));
        if (s.Version != _seen) { _seen = s.Version; _s = s; _dirty = true; _orderDirty = true; }
        // a style's opening / closing shows on every frame of the fade
        if (_dirty || now >= _nextDraw || _ui.Animating || (_vis < 1 && Theme.RetroSkin.AnimatesFade))
        {
            Draw(DateTime.Now);
            Ov.SetTexture(Surf.Tex);
            _dirty = _ui.Interacting || _ui.Changed;
            _nextDraw = Math.Min(now + 15, AnimWake.Take());           // "joined 3 min ago" moves on by itself; a style's animation: sooner
        }
        Ov.Show();
    }

    // ---------------- input: one hand at a time, the stick scrolls ----------------

    public VrOverlay Overlay => Ov;
    public bool Interactive => Ov != null && Ov.Visible && Shown && _vis > 0.8f;
    public bool AcceptsFrom(Hand h) => true;
    public Vector2 TextureSize => new(TW, TH);

    public void PointerMove(Hand h, Vector2 px)
    {
        _owner ??= h;
        if (_owner != h) return;
        _ptr.Move(px.X, px.Y);
        if (_ui.NeedsRedrawForMove(_ptr, Theme.Classic ? 2 : 1)) _dirty = true;
    }

    public void PointerLeave(Hand h)
    {
        if (_owner != h) return;
        _owner = null;
        _ptr.Leave();
        _dirty = true;
    }

    public void PointerButton(Hand h, PtrButton b, bool down, Vector2 px)
    {
        if (b != PtrButton.Primary) return;
        if (down)
        {
            if (_owner != h && _ptr.Down) return;
            _owner = h;
            _ptr.Move(px.X, px.Y);
            _ptr.Press();
        }
        else
        {
            if (_owner != h) return;
            _ptr.Move(px.X, px.Y);
            _ptr.Release();
        }
        _dirty = true;
    }

    public void PointerScroll(Hand h, float notches, Vector2 px)
    {
        if (_owner != h) return;
        _ptr.Wheel += notches;
        _dirty = true;
    }

    public bool Grabbable => true;
    public Matrix4x4 WorldTransform =>
        _vr != null && Placement.TryWorld(C.Place, _vr, out var w) ? w : MathUtil.FromArray(C.Place.M);
    public void GrabBegin(bool detach) { }
    public void GrabMove(Matrix4x4 world) { if (_vr != null) { Placement.SetWorld(C.Place, _vr, world); _cfg.MarkDirty(); } }
    public void GrabStickX(float v, float dt) { C.Width = Math.Clamp(C.Width * (1 + v * 0.9f * dt), 0.2f, 1.2f); _cfg.MarkDirty(); }
    public void GrabTwoHandBegin() => _twoStartWidth = C.Width;
    public void GrabTwoHandScale(float r) { C.Width = Math.Clamp(_twoStartWidth * r, 0.2f, 1.2f); _cfg.MarkDirty(); }
    public bool GrabSnapPreview => false;
    public void GrabEnd() { }

    // ---------------- what to show ----------------

    bool Starred(RoomWatcher.Person p) => C.Starred.Contains(p.Key);

    void ToggleStar(RoomWatcher.Person p)
    {
        if (!C.Starred.Remove(p.Key)) C.Starred.Add(p.Key);
        _cfg.MarkDirty();
        _dirty = true;
        _orderDirty = true;
    }

    // starred first, then friends, then everyone else; newest first inside each - the one list kept, put in order again only when
    // the room or a star changes (a new list sorted on every draw before)
    readonly List<RoomWatcher.Person> _ordered = new();
    bool _orderDirty = true;

    int _orderStars;

    List<RoomWatcher.Person> Ordered()
    {
        var h = new HashCode();                 // the stars as they are (the watch's room view sets them too: WristRoom.cs)
        foreach (var k in C.Starred) h.Add(k);
        int stars = h.ToHashCode();
        if (!_orderDirty && stars == _orderStars) return _ordered;
        _orderDirty = false;
        _orderStars = stars;
        _ordered.Clear();
        _ordered.AddRange(_s.Here);
        Order(_ordered, C.Starred);
        _when.Clear();
        return _ordered;
    }

    // how long ago someone came or left, as the rows write it, made again only when the minute it says moves on (a string per row
    // per draw before, all of them the same)
    readonly Dictionary<(RoomWatcher.Person p, bool gone, int form), (int min, int lang, string s)> _when = new();

    string When(RoomWatcher.Person p, DateTime now, bool gone, int form)
    {
        int min = (int)Math.Floor((now - (gone ? p.Left : p.Joined)).TotalMinutes);
        var key = (p, gone, form);
        if (_when.TryGetValue(key, out var e) && e.min == min && e.lang == Lang.Version) return e.s;
        if (_when.Count > 256) _when.Clear();
        string s = gone ? LeftText(p.Left, now, form) : Ago(p.Joined, now);
        _when[key] = (min, Lang.Version, s);
        return s;
    }

    int FriendsHere()
    {
        int n = 0;
        foreach (var p in _s.Here) if (p.Friend) n++;      // (FindAll made a list a draw)
        return n;
    }

    // (the watch's opened room view lists them the same way: WristRoom.cs)
    internal static void Order(List<RoomWatcher.Person> l, List<string> starred)
    {
        int Rank(RoomWatcher.Person p) => starred.Contains(p.Key) ? 0 : p.Friend ? 1 : 2;
        l.Sort((a, b) => Rank(a) != Rank(b) ? Rank(a).CompareTo(Rank(b)) : b.Joined.CompareTo(a.Joined));
    }

    internal static string Ago(DateTime t, DateTime now)
    {
        var d = now - t;
        if (d.TotalMinutes < 1) return Lang.T("just now");
        if (d.TotalHours < 1) return Lang.F("{0} min", (int)d.TotalMinutes);
        return Lang.F("{0} h {1:00} min", (int)d.TotalHours, d.Minutes);
    }

    // "5 min ago", "1 h 05 min ago", "just now"
    internal static string AgoText(DateTime t, DateTime now)
    {
        var d = now - t;
        if (d.TotalMinutes < 1) return Lang.T("just now");
        if (d.TotalHours < 1) return Lang.F("{0} min ago", (int)d.TotalMinutes);
        return Lang.F("{0} h {1:00} min ago", (int)d.TotalHours, d.Minutes);
    }

    // someone who left: "left 5 min" (a list's last column, form 0), "left 5 min ago" (1; 2: "left just now" in the first minute) -
    // English as each place always wrote it; another language its own way
    internal static string LeftText(DateTime t, DateTime now, int form)
    {
        if (Lang.English) return form == 2 ? "left " + AgoText(t, now) : "left " + Ago(t, now) + (form == 1 ? " ago" : "");
        return now - t < TimeSpan.FromMinutes(1) ? Lang.T("left just now") : Lang.F("left {0}", AgoText(t, now));
    }

    internal static string PeopleText(int n) => n == 1 ? Lang.T("1 person") : Lang.F("{0} people", n);
    internal static string FriendsText(int f) => f == 1 ? Lang.T("1 friend") : Lang.F("{0} friends", f);

    string Empty() => EmptyText(_s);

    internal static string EmptyText(RoomWatcher.Snapshot s) =>
        !s.LogFound ? "No VRChat log found" : !s.InRoom ? "Not in a world" : s.Arriving ? "Arriving..." : "Nobody else is here";

    public void DrawFor(RoomWatcher.Snapshot s, DateTime now) { if (!ReferenceEquals(_s, s)) _orderDirty = true; _s = s; Draw(now); }

    public void Draw(DateTime now)
    {
        if (Theme.Classic) Draw95(now);
        else DrawModern(now);
    }

    // ---- modern ----
    const float Pad = 20, RowH = 46;

    void DrawModern(DateTime now)
    {
        _ui.Begin(_ptr, 1);
        _p.Begin(Surf.Bitmap, 0x00000000);
        var all = new RectangleF(0, 0, TW, TH);
        var cyRev = CyberReveal.Begin(_p, all, Ov != null ? Ov.ShownAt : -10, Ov != null ? _vis : 1, !Shown);     // Cyber: its reveal
        if (Theme.Cyber) CyberHud.ConsoleFrame(_p, all, Theme.OverlayBg);
        else
        {
            _p.Fill(all, Theme.OverlayBg, 30);
            _p.Stroke(new RectangleF(1, 1, TW - 2, TH - 2), 0x40FFFFFF, 2, 29);
        }
        uint sub = Theme.Mix(Theme.Text, Theme.OverlayBg, 0.42f);

        if (Theme.Cyber) CyberHeader();            // a ROOM tag, the count and the world as readouts (RoomCyber.cs)
        else
        {
        _p.Text("Room", new RectangleF(Pad + 4, 14, 100, 38), Theme.Text, 22, FontWeight.SemiBold);
        float hx = Pad + 4 + _p.MeasureWidth("Room", 22, FontWeight.SemiBold) + 14;
        int friends = FriendsHere();
        if (_s.InRoom)
        {
            string count = CountText(_s.Here.Count, friends, false);
            float cw = _p.MeasureWidth(count, 15, FontWeight.SemiBold) + 26;
            _p.Fill(new RectangleF(hx, 19, cw, 30), Theme.WithAlpha(Theme.Accent, 0.18f), 15);
            _p.Text(count, new RectangleF(hx + 13, 19, cw - 22, 30), Theme.Accent, 15, FontWeight.SemiBold);
            hx += cw + 10;
            float ww = TW - Pad - 100 - hx;
            if (ww > 60 && _s.World.Length > 0)
                _p.Text(Ellipsize(_s.World, ww, 15), new RectangleF(hx, 19, ww, 30), sub, 15);
        }
        }
        if (HeaderButton("rm.x", new RectangleF(TW - Pad - 40, 14, 40, 40), "")) Toggle();
        if (HeaderButton("rm.here", new RectangleF(TW - Pad - 90, 14, 40, 40), Theme.IcoRecenter)) BringHere();

        var list = new RectangleF(Pad, 66, TW - 2 * Pad, TH - 66 - 16);
        var people = Ordered();
        int rows = people.Count + (_s.Gone.Count > 0 ? 1 + _s.Gone.Count : 0);
        if (people.Count == 0)
        {
            _p.Text(Empty(), new RectangleF(0, list.Y + 40, TW, 40), Theme.Text, 22, FontWeight.SemiBold, TextAlignment.Center);
            if (!_s.VrcxFound && _s.LogFound)
                _p.Text("install VRCX to see your friends first", new RectangleF(0, list.Y + 82, TW, 26), sub, 15, FontWeight.Normal, TextAlignment.Center);
            if (_s.Gone.Count == 0) { if (cyRev.Kind != 0) CyberReveal.End(_p, cyRev); _p.End(); _ui.End(); return; }
        }
        // the stick scrolls, a row at a time
        float max = MathF.Max(0, rows * RowH - list.Height);
        if (_ptr.Wheel != 0) { _scroll = Math.Clamp(_scroll - _ptr.Wheel * RowH, 0, max); _ptr.Wheel = 0; }
        _scroll = Math.Clamp(_scroll, 0, max);
        float rowW = max > 0 ? list.Width - 14 : list.Width;      // room for the scroll bar
        _p.PushClip(list);
        float y = list.Y - _scroll;
        for (int i = 0; i < people.Count; i++, y += RowH)
        {
            if (y + RowH < list.Y || y > list.Bottom) continue;
            Row(people[i], new RectangleF(list.X, y, rowW, RowH - 4), now, false, sub, i);
        }
        if (_s.Gone.Count > 0)
        {
            if (y + RowH >= list.Y && y <= list.Bottom)
                _p.Text("Left", new RectangleF(list.X + 14, y + 10, 200, 28), sub, 14, FontWeight.SemiBold);
            y += RowH;
            for (int i = 0; i < _s.Gone.Count; i++, y += RowH)
            {
                if (y + RowH < list.Y || y > list.Bottom) continue;
                Row(_s.Gone[i], new RectangleF(list.X, y, rowW, RowH - 4), now, true, sub, 1000 + i);
            }
        }
        _p.PopClip();
        if (max > 0)
        {
            // a thin scroll bar on the right
            float bh = list.Height * list.Height / (rows * RowH), by = list.Y + (list.Height - bh) * (_scroll / max);
            _p.Fill(new RectangleF(list.Right - 4, by, 4, bh), Theme.WithAlpha(Theme.Text, 0.3f), 2);
        }
        if (cyRev.Kind != 0) CyberReveal.End(_p, cyRev);
        _p.End();
        _ui.End();
    }

    void Row(RoomWatcher.Person p, RectangleF r, DateTime now, bool gone, uint sub, int index)
    {
        if (Theme.Cyber) { CyberRow(p, r, now, gone, index); return; }      // a HUD list row (RoomCyber.cs)
        bool star = Starred(p);
        _p.Fill(r, gone ? Theme.WithAlpha(Theme.Panel3, 0.35f) : Theme.Panel3, 12);
        // the star: tap to have them announced
        var sr = new RectangleF(r.X + 4, r.Y, 44, r.Height);
        if (_ui.Clickable(Ui.Id("rm.s", index), sr, out bool hover, out _)) ToggleStar(p);
        _p.Text(star ? "★" : "☆", sr, star ? Theme.Warn : hover ? Theme.Text : sub, 22, FontWeight.Normal, TextAlignment.Center);
        float x = sr.Right + 4;
        string when = When(p, now, gone, 1);
        float ww = _p.MeasureWidth(when, 14) + 16;
        float nw = r.Right - x - ww - (p.Friend ? 76 : 8);
        _p.Text(Ellipsize(p.Name, nw, 18, FontWeight.SemiBold), new RectangleF(x, r.Y, nw, r.Height), gone ? sub : Theme.Text, 18, FontWeight.SemiBold);
        if (p.Friend)
        {
            float fx = x + MathF.Min(nw, _p.MeasureWidth(Ellipsize(p.Name, nw, 18, FontWeight.SemiBold), 18, FontWeight.SemiBold)) + 10;
            var fr = new RectangleF(fx, r.Y + (r.Height - 22) / 2, 60, 22);
            _p.Fill(fr, Theme.WithAlpha(Theme.Good, 0.2f), 11);
            _p.Text("friend", fr, Theme.Good, 12, FontWeight.SemiBold, TextAlignment.Center);
        }
        _p.Text(when, new RectangleF(r.Right - ww - 4, r.Y, ww, r.Height), sub, 14, FontWeight.Normal, TextAlignment.Trailing);
    }

    // "12 people · 2 friends" (Cyber: in capitals), made again only when the numbers or the language change
    string _countText;
    (int n, int friends, bool upper, int lang) _countFor = (-1, -1, false, -1);

    string CountText(int n, int friends, bool upper)
    {
        var key = (n, friends, upper, Lang.Version);
        if (_countText != null && key == _countFor) return _countText;
        _countFor = key;
        string count = PeopleText(n);
        if (friends > 0) count += " · " + FriendsText(friends);
        return _countText = upper ? count.ToUpperInvariant() : count;
    }

    string Ellipsize(string s, float maxW, float size, FontWeight wt = FontWeight.Normal)
    {
        s = Lang.T(s);                     // cut in the language picked
        if (string.IsNullOrEmpty(s) || _p.MeasureWidth(s, size, wt) <= maxW) return s;
        int n = s.Length;
        while (n > 1 && _p.MeasureWidth(s[..n] + "…", size, wt) > maxW) n--;
        if (n > 1 && char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n].TrimEnd() + "…";
    }

    bool HeaderButton(string id, RectangleF r, string icon)
    {
        if (Theme.Cyber) return _ui.HudButton(id, r, null, icon, false, true, 13);        // a HUD plate
        bool click = _ui.Clickable(id, r, out bool hover, out bool held);
        _p.Fill(r, held ? Theme.Accent : hover ? Theme.Mix(Theme.Panel3, 0xFFFFFFFF, 0.12f) : Theme.Panel3, r.Height / 2);
        _p.Icon(icon, r, held ? Theme.OnAccent : Theme.Text, 16);
        return click;
    }

    // ---- Windows 95: a window with a details list, the star column first ----
    const uint Blk95 = 0xFF000000, Gry95 = 0xFF808080;
    int _sel95 = -1;
    readonly List<(RoomWatcher.Person p, bool gone)> _lines95 = new();

    void Draw95(DateTime now)
    {
        if (SkinRoom(now)) return;          // a style's own panel (ScreenHooks.cs)
        _canvas95 ??= new Surface(_p.Gpu, W95, H95);
        _ui.Begin(_ptr, 2);
        _ui.Canvas95 = new SizeF(W95, H95);
        _p.BeginPixel(_canvas95.Bitmap, 0x00000000);
        var win = new RectangleF(0, 0, W95, H95);
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(3, 3, W95 - 6, 18), _s.InRoom && _s.World.Length > 0 ? Fit95(Lang.F("Room - {0}", _s.World), W95 - 40) : "Room");
        if (_ui.CaptionClose95("rm95.x", W95 - 5 - 16, 5, true, 6)) Toggle();

        var people = Ordered();
        var lines = _lines95;              // (the one list, filled again each draw: no garbage)
        lines.Clear();
        foreach (var p in people) lines.Add((p, false));
        foreach (var p in _s.Gone) lines.Add((p, true));
        // column headers like a 1995 details view
        var head = new RectangleF(8, 26, W95 - 16, 16);
        HeaderCell(new RectangleF(head.X, head.Y, 20, 16), "");
        HeaderCell(new RectangleF(head.X + 20, head.Y, head.Width - 20 - 100, 16), "Name");
        HeaderCell(new RectangleF(head.Right - 100, head.Y, 100, 16), "Joined");
        var box = new RectangleF(8, 42, W95 - 16, 166);
        if (lines.Count == 0)
        {
            _p.Fill(box, 0xFFFFFFFF);
            _p.Bevel(box, Painter.Edge.Sunken);
            _ui.Text95(Lang.T(Empty()) + ".", new RectangleF(box.X + 8, box.Y + 6, box.Width - 16, 16));
        }
        else
        {
            int hit = _ui.List95("rm95.list", box, lines.Count, _sel95, (i, r, on) =>
            {
                var (p, gone) = lines[i];
                uint fg = on ? 0xFFFFFFFF : gone ? Gry95 : Blk95;
                _ui.Text95(Starred(p) ? "★" : "☆", new RectangleF(r.X + 2, r.Y, 16, r.Height), on ? fg : Starred(p) ? 0xFF808000 : Gry95);
                string name = p.Name + (p.Friend ? Lang.T("  (friend)") : "");
                _ui.Text95(Fit95(name, r.Width - 20 - 104), new RectangleF(r.X + 20, r.Y, r.Width - 124, r.Height), fg, p.Friend && !gone);
                string when = When(p, now, gone, 0);
                _ui.Text95(Fit95(when, 98), new RectangleF(r.Right - 100, r.Y, 98, r.Height), fg);
            });
            if (hit >= 0)
            {
                _sel95 = hit;
                if (_ui.Mouse.X < box.X + 22) ToggleStar(lines[hit].p);       // a click on the star column
            }
        }
        int friends = FriendsHere();
        _ui.Pane95(new RectangleF(4, 214, 120, 20), _s.InRoom ? PeopleText(people.Count) : "Not in a world");
        _ui.Pane95(new RectangleF(126, 214, 110, 20), FriendsText(friends));
        _ui.Pane95(new RectangleF(238, 214, W95 - 242, 20), Fit95(_s.VrcxFound ? Lang.F("VRCX: {0} friends", _s.Friends) : "VRCX not found", W95 - 256),
            _s.VrcxFound ? 0xFF008000 : 0);
        _p.End();
        _p.Begin(Surf.Bitmap, 0x00000000);
        _p.ImageNearest(_canvas95.Bitmap, new RectangleF(0, 0, TW, TH));
        _p.End();
        _ui.End();
    }

    void HeaderCell(RectangleF r, string text)
    {
        _p.Fill(r, Theme.W95Face);
        _p.Bevel(r, Painter.Edge.Raised);
        if (text.Length > 0) _ui.Text95(text, new RectangleF(r.X + 4, r.Y, r.Width - 6, r.Height));
    }

    string Fit95(string s, float w)
    {
        s = Lang.T(s);                     // cut in the language picked
        if (string.IsNullOrEmpty(s) || _ui.Width95(s) <= w) return s;
        int n = s.Length;
        while (n > 1 && _ui.Width95(s[..n] + "...") > w) n--;
        return s[..n].TrimEnd() + "...";
    }

    // no retro style on a while (OrbitApp.GpuSweep, Erik 10-24: Orbit's VRAM): its half-size canvas let go, made again by the next retro draw
    public void ReleaseStyle() { if (!Theme.Classic) { _canvas95?.Dispose(); _canvas95 = null; } }

    public void Dispose()
    {
        DestroyOverlay();
        Surf.Dispose();
        _canvas95?.Dispose();
    }
}
