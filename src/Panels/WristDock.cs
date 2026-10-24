using System;
using System.Drawing;
using System.Numerics;
using Vortice.DirectWrite;
using Slide = Orbit.DockInput.Slide;

namespace Orbit;

// The Hexpad widget opened: the stream dock on the watch. A tap on the dock card grows it into this view (the Minesweeper way,
// WristDetail), its X or a tap beside it shrinks it back. The six keys big (Hexpad's sharp 136 px faces when it sends them), a
// round button under each column, the three knobs as sliders with their push buttons, the page or folder, and why nothing shows
// when Hexpad is not there. What the laser does here is DockInput's, the floating Hexpad panel's own code, through the app's one
// HexpadLink (a DockInput of the watch's own: a key held here is not one held on the panel). Modern: Bento's tokens; Windows 95:
// the panel's 95 window made to fit the watch (the keys 1:1 in picture boxes, a trackbar group per knob).
public sealed partial class WristPanel
{
    DockInput _dockIn;
    // the watch's dock controls (the app gives it the link; --snapshot's has none: what would be sent is only written down)
    public DockInput DockIn { get => _dockIn ??= new DockInput(null, _cfg); set => _dockIn = value; }
    bool _dockReady;                        // the opened dock is at rest (not growing or shrinking): the laser works it
    RectangleF _dockClose95;
    static readonly HexpadLink.Snapshot DockNone = new();
    static readonly string[] DockKeys = { "key1", "key2", "key3", "key4", "key5", "key6" }, DockBtns = { "btn7", "btn8", "btn9" };
    static readonly string[] DockKnobs = { "enc1", "enc0", "enc2" };        // the big knob first, then the small left and right ones

    bool DockLive => _open != null && !_closing && _dockReady && DetailIs("dock");

    // the watch shows a dock now: up, with a dock widget on its page (or sliding in), opened, or a folder open (one may be in it) - the
    // link decodes Hexpad's faces only then (HexpadLink.Showing)
    public bool DockInView => Alpha > 0.02f && C.Face == "custom" && (HasWidget("dock") || DetailIs("dock") || DetailIs("folder"));

    // the link's latest snapshot in, the timers (a stick's final value, a held value running out)
    DockInput DockSync(PanelData d)
    {
        var i = DockIn;
        var s = d.Dock ?? DockNone;
        if (!ReferenceEquals(s, i.Snap)) i.Snap = s;
        i.Timers(Clock.Now);
        if (i.TakeDirty()) _dirty = true;
        i.BeginLayout();
        return i;
    }

    // the view goes (closed, the watch looked away from): a held key comes up, a question is dropped
    void DockLetGo()
    {
        if (_dockIn == null) return;
        _dockIn.ReleasePress();
        _dockIn.Confirm = null;
        _dockIn.Hover = null;
        _dockReady = false;
    }

    static bool DockShown(DockInput i, PanelData d) =>
        i.Live || i.Snap.State == HexpadLink.LinkState.ScreenOff && d.DockFaces?.Any == true;

    // ================= Modern =================

    const float DkT = 104, DkG = 10, DkTop = 76, DkRow = 92, DkRowGap = 14;

    void DockDetail(RectangleF E, PanelData d)
    {
        var I = DockSync(d);
        var s = I.Snap;
        _dockReady = !_ui.Inert;
        double now = Clock.Now;
        // the name; where on the dock (page or folder), idle
        _p.Text("Hexpad", new RectangleF(E.X + 24, E.Y + 16, 160, 40), Bento.Text, 24, FontWeight.SemiBold);
        float hx = E.X + 24 + _p.MeasureWidth("Hexpad", 24, FontWeight.SemiBold) + 14, hr = E.Right - 22 - 46 - 12;
        if (I.Live || s.State == HexpadLink.LinkState.ScreenOff)
        {
            void Chip(string t, uint fill, uint ink)
            {
                float w = MathF.Min(_p.MeasureWidth(t, SmallText, FontWeight.SemiBold) + 28, hr - hx);
                if (w < 40) return;
                _p.Fill(new RectangleF(hx, E.Y + 20, w, 32), fill, 16);
                _p.Text(Ellipsize(t, w - 26, SmallText, FontWeight.SemiBold), new RectangleF(hx + 14, E.Y + 20, w - 24, 32), ink, SmallText, FontWeight.SemiBold);
                hx += w + 10;
            }
            Chip(I.Where(), Bento.Tile, Sub);
            if (s.Ambient) Chip("idle · point to wake", Theme.WithAlpha(Bento.Accent, 0.18f), Bento.Accent);
        }
        if (!DockShown(I, d))
        {
            string line = d.Dock == null ? "Hexpad is not connected" : I.StateLine();
            string hint = s.State switch
            {
                HexpadLink.LinkState.NotRunning => "start it, and the dock shows up here",
                HexpadLink.LinkState.ScreenOff => "hold its middle round button to turn it on",
                _ => "",
            };
            _p.Text(Ellipsize(line, E.Width - 48, 24, FontWeight.SemiBold), new RectangleF(E.X, E.Y + 160, E.Width, 40), Bento.Text, 24, FontWeight.SemiBold,
                TextAlignment.Center);
            if (hint.Length > 0)
                _p.Text(hint, new RectangleF(E.X, E.Y + 204, E.Width, 28), Sub, SmallText, FontWeight.Normal, TextAlignment.Center);
            return;
        }
        // the six keys: the sharp faces when Hexpad sends them, else the device's
        float x0 = E.X + 24, y0 = E.Y + DkTop;
        for (int i = 0; i < HexpadLink.Keys; i++)
        {
            string id = DockKeys[i];
            var r = new RectangleF(x0 + i % 3 * (DkT + DkG), y0 + i / 3 * (DkT + DkG), DkT, DkT);
            I.Hit[id] = r;
            bool down = I.Pressed == id, flash = I.Flash(id, now);
            var rr = down ? RectangleF.Inflate(r, -4, -4) : r;
            _p.Fill(rr, 0xFF000000, 14);
            var bmp = d.DockFaces?.Best(i);
            if (bmp != null && !s.Dark)
            {
                _p.PushRoundedClip(rr, 14);
                _p.Image(bmp, rr);
                _p.PopRoundedClip();
            }
            if (I.Ctl(id) is { Danger: true }) _p.Circle(rr.Right - 12, rr.Y + 12, 5, Bento.Bad);
            if (I.Hover == id || flash) _p.Stroke(RectangleF.Inflate(rr, 3, 3), flash ? Bento.Accent : Theme.WithAlpha(Bento.Accent, 0.85f), 3, 17);
        }
        // a round button under each column, its action under it
        float by = y0 + 2 * DkT + DkG + 30;
        for (int col = 0; col < 3; col++)
        {
            string id = DockBtns[col];
            float cx = x0 + col * (DkT + DkG) + DkT / 2;
            var r = new RectangleF(cx - 22, by - 22, 44, 44);
            I.Hit[id] = RectangleF.Inflate(r, 10, 6);
            bool down = I.Pressed == id, flash = I.Flash(id, now);
            _p.Circle(cx, by, down ? 19 : 22, down || flash ? Bento.AccentFill : Bento.Tile);
            if (I.Hover == id) _p.Ring(cx, by, 25, Bento.Accent, 2.5f);
            string label = I.Ctl(id)?.Label ?? "";
            TextAt(Ellipsize(label, DkT + 8, SmallText, FontWeight.SemiBold), cx - (DkT + 8) / 2, by + 46, DkT + 8, Sub, SmallText, FontWeight.SemiBold,
                TextAlignment.Center);
        }
        // the knobs as sliders on the right
        float kx = x0 + 3 * DkT + 2 * DkG + 20, kw = E.Right - 24 - kx;
        for (int k = 0; k < DockKnobs.Length; k++) DockKnob(I, DockKnobs[k], new RectangleF(kx, y0 + k * (DkRow + DkRowGap), kw, DkRow), now);
        if (I.Confirm != null) DockConfirm(I, E);
    }

    // one knob (the panel's SliderRow on the watch): its name and value, the push as a pill, a wide track for the laser
    void DockKnob(DockInput I, string id, RectangleF card, double now)
    {
        var c = I.Ctl(id);
        var t = I.TypeOf(id);
        bool off = t == Slide.Abs && I.Off(id), on = t != Slide.None && !off;
        bool dragging = I.Drag == id, flash = I.Flash(id, now);
        bool hovS = on && (dragging || I.Hover == DockInput.S(id)), hovP = I.Hover == id;
        I.Cards[id] = card;
        _p.Fill(card, Theme.WithAlpha(Bento.Text, 0.06f), 16);
        if (flash) _p.Stroke(RectangleF.Inflate(card, -1, -1), Bento.Accent, 2, 15);
        string push = DockInput.PushLabel(c);
        float right = card.Right - 12;
        if (push.Length > 0)
        {
            float bw = Math.Clamp(_p.MeasureWidth(push, 15, FontWeight.SemiBold) + 30, 64, 100);
            var b = new RectangleF(card.Right - 10 - bw, card.Y + 10, bw, 34);
            I.Hit[id] = RectangleF.Inflate(b, 4, 4);
            bool down = I.Pressed == id;
            var bb = down ? RectangleF.Inflate(b, -2, -2) : b;
            _p.Fill(bb, down ? Bento.AccentFill : hovP ? Bento.TileHover : Bento.Tile, bb.Height / 2);
            if (hovP && !down) _p.Stroke(RectangleF.Inflate(b, 2, 2), Bento.Accent, 2, b.Height / 2 + 2);
            float dr = c.Danger ? 10 : 0, pw = b.Width - 20 - dr, ps = MathF.Max(13, FitSize(push, pw - 2, 15, FontWeight.SemiBold, null));
            _p.Text(Ellipsize(push, pw, ps, FontWeight.SemiBold), new RectangleF(b.X + 10, b.Y, pw, b.Height), down ? Bento.OnAccent : Bento.Text, ps,
                FontWeight.SemiBold, TextAlignment.Center);
            if (c.Danger) _p.Circle(b.Right - 13, b.Y + b.Height / 2, 4, Bento.Bad);
            right = b.X - 8;
        }
        string name = DockInput.KnobName(id, c), value = I.ValueText(id, t, now);
        if (t == Slide.None) value = push.Length > 0 ? "push only" : "";
        float nx = card.X + 14;
        float vw = value.Length > 0 ? _p.MeasureWidth(value, 16, FontWeight.SemiBold) + 8 : 0;
        float nmax = MathF.Max(30, right - nx - vw);
        float ns = MathF.Max(15, FitSize(name, nmax, 18, FontWeight.SemiBold, null));
        name = Ellipsize(name, nmax, ns, FontWeight.SemiBold);
        float nw = MathF.Min(_p.MeasureWidth(name, ns, FontWeight.SemiBold), nmax);
        _p.Text(name, new RectangleF(nx, card.Y + 10, nmax + 2, 34), Bento.Text, ns, FontWeight.SemiBold);
        if (value.Length > 0)
        {
            uint vc = t == Slide.None || off ? Bento.Faint : dragging && t == Slide.Jog ? Bento.Accent : value == "Muted" ? Bento.WarnInk : Sub;
            _p.Text(value, new RectangleF(nx + nw + 8, card.Y + 10, MathF.Max(0, right - nx - nw - 6), 34), vc, 16, FontWeight.SemiBold);
        }
        // the track: 12 px, a 24 px thumb; the lower part of the card takes the laser
        float cy = card.Y + 66;
        var tr = new RectangleF(card.X + 24, cy - 6, card.Width - 48, 12);
        I.Track[id] = tr;
        if (on) I.Hit[DockInput.S(id)] = new RectangleF(card.X, card.Y + 46, card.Width, card.Height - 46 + 5);
        float hl = _ui.Anim(Ui.Id("wdkh", id), hovS ? 1 : 0, 18);
        if (!on)
        {
            _p.Fill(tr, Theme.WithAlpha(Bento.Track, 0.55f), 6);
            return;
        }
        _p.Fill(tr, Bento.Track, 6);
        if (t == Slide.Abs)
        {
            if (I.AbsValue(id, now) is not float v)
            {
                _p.Ring(tr.X + tr.Width / 2, cy, 10, Bento.Faint, 2);     // Hexpad does not know it yet
                return;
            }
            float kx = tr.X + tr.Width * v;
            bool muted = I.Snap.Values.TryGetValue(id, out var x) && x.Muted && !dragging && !I.HoldUntil.ContainsKey(id);
            if (kx - tr.X > 1) _p.Fill(new RectangleF(tr.X, tr.Y, MathF.Max(12, kx - tr.X + 6), tr.Height), muted ? Bento.Faint : Bento.Accent, 6);
            DockThumb(kx, cy, hl, dragging);
            return;
        }
        // jog: detent marks either side of the middle; the thumb follows the pull and springs back
        float mid = tr.X + tr.Width / 2, det = I.Detent(id), half = tr.Width / 2;
        for (float k = det; k < half - 6; k += det)
        {
            _p.Circle(mid - k, cy, 2, Theme.WithAlpha(Bento.Text, 0.3f));
            _p.Circle(mid + k, cy, 2, Theme.WithAlpha(Bento.Text, 0.3f));
        }
        float off2 = _ui.Anim(Ui.Id("wdkj", id), dragging ? Math.Clamp(I.JogOff, -half, half) : 0, dragging ? 1000 : 14);
        if (MathF.Abs(off2) > 1)
        {
            float a = MathF.Min(mid, mid + off2), w = MathF.Abs(off2);
            _p.Fill(new RectangleF(a - 6, tr.Y, w + 12, tr.Height), Bento.Accent, 6);
        }
        DockThumb(mid + off2, cy, hl, dragging);
    }

    void DockThumb(float x, float y, float hl, bool held)
    {
        if (hl > 0.01f) _p.Circle(x, y, 12 + 8 * hl, Theme.WithAlpha(Bento.Accent, 0.22f * hl));
        _p.Circle(x, y, held ? 13 : 12, Bento.Text);
    }

    // what could end the VR session asks first (the panel's question)
    void DockConfirm(DockInput I, RectangleF E)
    {
        string label = I.ConfirmLabel();
        _p.Fill(E, Theme.WithAlpha(0xFF000000, 0.55f), CardRadius);
        var card = new RectangleF(E.X + E.Width / 2 - 220, E.Y + E.Height / 2 - 96, 440, 192);
        _p.Fill(card, Bento.Card, 22);
        _p.Stroke(RectangleF.Inflate(card, -0.6f, -0.6f), Theme.Border, 1.2f, 22);
        string q = Lang.F("Run “{0}”?", label);
        float qs = MathF.Max(18, FitSize(q, card.Width - 48, 24, FontWeight.SemiBold, null));
        _p.Text(Ellipsize(q, card.Width - 48, qs, FontWeight.SemiBold), new RectangleF(card.X + 24, card.Y + 20, card.Width - 48, 40), Bento.Text, qs,
            FontWeight.SemiBold);
        _p.Text("It could end your VR session, so it asks first.", new RectangleF(card.X + 24, card.Y + 62, card.Width - 48, 28), Sub, SmallText);
        if (Press("wdkno", new RectangleF(card.X + 24, card.Bottom - 70, 188, 48), null, "Cancel", false)) I.ConfirmNo();
        if (Press("wdkyes", new RectangleF(card.Right - 212, card.Bottom - 70, 188, 48), null, "Do it", true)) I.ConfirmYes();
    }

    // ================= Windows 95 =================

    static RectangleF Tex2(RectangleF r) => new(r.X * 2, r.Y * 2, r.Width * 2, r.Height * 2);

    // the Hexpad window: the keys 1:1 in picture boxes, the round buttons under them, a trackbar group per knob beside them;
    // the page in the caption (the watch has no room for the panel's status bar)
    void Dock95Detail(RectangleF win, PanelData d)
    {
        var I = DockSync(d);
        var s = I.Snap;
        _dockReady = true;                  // drawn only once the zoom outline is done
        double now = Clock.Now;
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        bool show = DockShown(I, d);
        string name = s.InFolder ? s.FolderName : s.PageName;
        string pages = s.InFolder ? (s.FolderPages > 1 ? $"{s.FolderPage + 1}/{s.FolderPages}" : "") : $"{s.Page + 1}/{s.Pages}";
        string title = show ? $"Hexpad - {name}{(pages.Length > 0 ? $" ({pages})" : "")}{(s.Ambient ? " - " + Lang.T("Idle") : "")}" : "Hexpad";
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), Fit(title, win.Width - 6 - 21 - 40, Ui.F95, true), W95Icons.Id.Dock);
        _dockClose95 = new RectangleF(win.Right - 5 - 16, win.Y + 5, 16, 14);
        if (_ui.CaptionClose95("wdk95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        if (!show)
        {
            // nothing to show: the classic message in one white box
            var msg = new RectangleF(win.X + 6, win.Y + 26, win.Width - 12, win.Height - 32);
            _p.Fill(msg, Wht95);
            _p.Bevel(msg, Painter.Edge.Sunken);
            W95Icons.Draw(_p, W95Icons.Id.Info32, msg.X + 14, msg.Y + 16);
            string line = d.Dock == null ? "Hexpad is not connected." : Lang.T(I.StateLine()) + ".";
            _ui.Text95(line, new RectangleF(msg.X + 54, msg.Y + 18, msg.Width - 64, msg.Height - 30), Blk95, false, TextAlignment.Leading, true, true);
            return;
        }
        // the six keys: 68 x 68 picture boxes, the dock's own pixels
        for (int i = 0; i < HexpadLink.Keys; i++)
        {
            string id = DockKeys[i];
            var box = new RectangleF(win.X + 6 + i % 3 * 76, win.Y + 26 + i / 3 * 76, 72, 72);
            I.Hit[id] = Tex2(box);
            bool down = I.Pressed == id;
            _p.Fill(box, Blk95);
            var bmp = d.DockFaces?.Bitmaps[i];
            if (bmp != null && !s.Dark) _p.ImageNearest(bmp, new RectangleF(box.X + 2 + (down ? 1 : 0), box.Y + 2 + (down ? 1 : 0), 68, 68));
            _p.Bevel(box, down ? Painter.Edge.Pressed : Painter.Edge.Sunken);
            if (I.Hover == id || I.Flash(id, now)) Dotted95(RectangleF.Inflate(box, -4, -4), I.Flash(id, now) ? 0xFFFFFF00 : Wht95);
        }
        // the round buttons under the columns
        for (int col = 0; col < 3; col++)
        {
            string id = DockBtns[col];
            var r = new RectangleF(win.X + 6 + col * 76, win.Y + 26 + 152, 72, 22);
            I.Hit[id] = Tex2(r);
            var inner = _p.ButtonFace95(r, false, I.Pressed == id || I.Flash(id, now), false);
            _ui.Text95(Fit(I.Ctl(id)?.Label ?? "", inner.Width - 4), inner, Blk95, false, TextAlignment.Center);
        }
        // the knobs: a trackbar group each, the big one first
        float gx = win.X + 6 + 3 * 72 + 2 * 4 + 4;
        for (int k = 0; k < DockKnobs.Length; k++)
            DockKnob95(I, DockKnobs[k], new RectangleF(gx, win.Y + 22 + k * 62, win.Right - 6 - gx, 60), now);
        if (I.Confirm != null) DockConfirm95(I, win);
    }

    void Dotted95(RectangleF r, uint c)
    {
        for (float x = r.X; x < r.Right; x += 2) { _p.Fill(new RectangleF(x, r.Y, 1, 1), c); _p.Fill(new RectangleF(x, r.Bottom - 1, 1, 1), c); }
        for (float y = r.Y; y < r.Bottom; y += 2) { _p.Fill(new RectangleF(r.X, y, 1, 1), c); _p.Fill(new RectangleF(r.Right - 1, y, 1, 1), c); }
    }

    // the panel's SliderGroup95, narrower: the knob's name on the group, a trackbar, under it the value and the push button
    void DockKnob95(DockInput I, string id, RectangleF g, double now)
    {
        var c = I.Ctl(id);
        var t = I.TypeOf(id);
        bool off = t == Slide.Abs && I.Off(id), on = t != Slide.None && !off, dragging = I.Drag == id;
        I.Cards[id] = Tex2(g);
        _ui.Group95(g, Fit(DockInput.KnobName(id, c), g.Width - 16));
        var bar = new RectangleF(g.X + 5, g.Y + 14, g.Width - 10, 18);
        var ch = new RectangleF(bar.X + 5, bar.Y + 7, bar.Width - 10, 4);
        I.Track[id] = Tex2(new RectangleF(ch.X, ch.Y, ch.Width - 1, ch.Height));
        if (on) I.Hit[DockInput.S(id)] = Tex2(new RectangleF(g.X + 2, g.Y + 8, g.Width - 4, 26));
        _p.Fill(ch, Wht95);
        _p.Bevel(ch, Painter.Edge.Sunken);
        float mid = ch.X + MathF.Floor((ch.Width - 1) / 2), tx = mid;
        bool known = true;
        if (t == Slide.Abs && !off)
        {
            if (I.AbsValue(id, now) is float v) tx = ch.X + MathF.Round((ch.Width - 1) * v);
            else known = false;
        }
        else if (t == Slide.Jog && dragging)
            tx = mid + Math.Clamp(MathF.Round(I.JogOff / 2), -(ch.Width - 1) / 2, (ch.Width - 1) / 2);
        var thumb = new RectangleF(tx - 5, bar.Y, 11, bar.Height);
        _p.Fill(thumb, Theme.W95Face);
        _p.Bevel(thumb, Painter.Edge.Raised);
        if (!on || !known) _p.FillChecker(RectangleF.Inflate(thumb, -2, -2), Wht95, Theme.W95Face);
        if (on && (I.Hover == DockInput.S(id) || dragging) || I.Flash(id, now)) Dotted95(new RectangleF(g.X + 3, g.Y + 12, g.Width - 6, 22), Blk95);
        float by = g.Y + 36;
        string push = DockInput.PushLabel(c);
        bool canPush = push.Length > 0;
        if (!canPush) push = "Push";
        float bw = Math.Clamp(_ui.Width95(push) + 10, 30, 40);
        var b = new RectangleF(g.Right - 5 - bw, by, bw, 20);
        if (canPush) I.Hit[id] = Tex2(RectangleF.Inflate(b, 1, 1));
        bool down = canPush && I.Pressed == id;
        var inner = _p.ButtonFace95(b, false, down, false);
        float o = down ? 1 : 0;
        _ui.Text95(Fit(push, inner.Width - 2), new RectangleF(inner.X + o, inner.Y + o, inner.Width, inner.Height), Blk95, false, TextAlignment.Center, canPush);
        string value = I.ValueText(id, t, now).Replace('−', '-');
        var vr = new RectangleF(g.X + 6, by, b.X - g.X - 8, 20);
        _ui.Text95(Fit(value, vr.Width), vr, Blk95, false, TextAlignment.Leading, on);
    }

    void DockConfirm95(DockInput I, RectangleF win)
    {
        string label = I.ConfirmLabel();
        var box = new RectangleF(MathF.Floor(win.X + win.Width / 2 - 130), MathF.Floor(win.Y + win.Height / 2 - 56), 260, 112);
        _p.Fill(box, Theme.W95Face);
        _p.Bevel(box, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(box.X + 3, box.Y + 3, box.Width - 6, 18), "Hexpad");
        W95Icons.Draw(_p, W95Icons.Id.Info32, box.X + 12, box.Y + 32);
        _ui.Text95(Fit(Lang.F("Run \"{0}\"?", label), box.Width - 64, Ui.F95, true), new RectangleF(box.X + 52, box.Y + 30, box.Width - 60, 16), Blk95, true);
        _ui.Text95("It could end your VR session.", new RectangleF(box.X + 52, box.Y + 48, box.Width - 60, 16));
        if (_ui.Button95("wdk95yes", new RectangleF(box.X + box.Width / 2 - 80, box.Bottom - 32, 75, 23), "&Yes", true, true)) I.ConfirmYes();
        if (_ui.Button95("wdk95no", new RectangleF(box.X + box.Width / 2 + 5, box.Bottom - 32, 75, 23), "&No")) I.ConfirmNo();
    }

    // ---- --snapshot's hooks: the middle of a control of the opened dock (texture px) ----

    public Vector2 DockPoint(string id) =>
        _dockIn != null && _dockIn.Hit.TryGetValue(id, out var r) ? new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2) : new Vector2(-1, -1);

    public Vector2 DockClosePoint => Theme.Classic
        ? new Vector2((_dockClose95.X + _dockClose95.Width / 2) * 2, (_dockClose95.Y + _dockClose95.Height / 2) * 2)
        : MinesClosePoint;
}
