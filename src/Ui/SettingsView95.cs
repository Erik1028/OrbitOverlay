using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Orbit;

// The settings in the Windows 95 style: an "OrbitOverlay Properties" property sheet at real 1995 pixel size
// (750 x 480, enlarged with whole pixels by the host). Tabs, group boxes, "Label:  [control]" rows,
// drop-down lists, access keys, OK / Close / Apply. Same settings and actions as the modern screen.
public sealed partial class SettingsView
{
    public const int W95W = 750, W95H = 480;
    public Action OnClose95;      // the desktop window's own "close" (the VR panel uses OnClose)
    public Action OnMinimize;     // the desktop window's minimise (the Cyber header draws it: no Windows frame there)
    // the VR panel's fade, for a style's opening / closing animation: when it came into view, how far in (0..1), going away.
    // The desktop window leaves them be (always there).
    public double ShownAt = -10;
    public float Vis = 1;
    public bool Closing;

    const uint K95 = 0xFF000000, W95 = 0xFFFFFFFF, Grey95 = 0xFF808080;
    const float RowH = 21;        // a drop-down list / text box row

    Surface _canvas95;

    // Draws the sheet at 1995 size, then blows it up k times into 'target' at (ox, oy).
    public void Render95(Ui ui, PointerState ptr, ID2D1Image target, int k, float ox, float oy, uint backdrop)
    {
        var p = ui.P;
        _canvas95 ??= new Surface(p.Gpu, W95W, W95H);
        ui.Begin(ptr, k, ox, oy);
        ui.Canvas95 = new SizeF(W95W, W95H);
        p.BeginPixel(_canvas95.Bitmap, Theme.W95Face);
        Draw95(ui);
        ui.Popups95();
        p.End();
        p.Begin(target, backdrop);
        p.ImageNearest(_canvas95.Bitmap, new RectangleF(ox, oy, W95W * k, W95H * k));
        p.End();
        ui.End();
    }

    public void Dispose95() { _canvas95?.Dispose(); _canvas95 = null; _cyKeep?.Dispose(); _cyKeep = null; }

    public static int H95() => W95H;

    // something runs in the background (the updater): the 95 window's cursor is the arrow with the hourglass then (SettingsWindow)
    public bool Busy95 => _h.Updates?.Busy == true;

    public void Draw95(Ui ui)
    {
        if (SkinSettings(ui)) { if (ui.Changed) _h.Cfg.MarkDirty(); return; }      // a style's own settings screen (ScreenHooks.cs)
        var p = ui.P;
        // "What's new" over the sheet (after an update until OK, or from General): the sheet is only a picture
        var wn = WhatsNewShown;
        bool modal = wn != null && wn.Count > 0;
        ui.Inert = modal;
        var all = new RectangleF(0, 0, W95W, W95H);
        p.Fill(all, Theme.W95Face);
        p.Bevel(all, Painter.Edge.Window);
        var close = OnClose ?? OnClose95;
        ui.Caption95(new RectangleF(3, 3, W95W - 6, 18), "OrbitOverlay Properties");
        if (ui.CaptionHelp95("p95.help", W95W - 5 - 16 - 2 - 16, 5)) Tab = TabGeneral;   // "What's this?" -> About
        if (ui.CaptionClose95("p95.close", W95W - 5 - 16, 5, close != null)) close?.Invoke();

        // the page first, then the tabs over its top edge
        var page = new RectangleF(6, 46, W95W - 12, 394);
        p.Fill(page, Theme.W95Face);
        p.Bevel(page, Painter.Edge.Page);
        int t = Tab;
        if (ui.Tabs95("p95.tabs", 8, 28, Tabs, ref t)) Tab = t;

        switch (Tab)
        {
            case TabDesktops: Desktops95(ui); break;
            case TabWindows: Windows95(ui); break;
            case TabLayouts: Layouts95(ui); break;
            case TabWrist: Wrist95(ui); break;
            case TabKeyboard: Keyboard95(ui); break;
            case TabLook: Look95(ui); break;
            case TabVrchat: Vrchat95(ui); break;
            default: General95(ui); break;
        }

        // bottom row: quick actions on the left, OK / Close / Apply on the right. Everything applies at once
        // (like a 95 sheet after PSM_CANCELTOCLOSE, so "Close", not a Cancel that could not undo);
        // Apply lights up until the change is saved to disk.
        const float by = 448;
        string kbL = C.Keyboard.Visible ? "Hide &keyboard" : "Show &keyboard", dkL = C.General.DesktopsVisible ? "Hide &desktops" : "Show &desktops";
        float w1 = Btn95(ui, "Re&center", 75), w2 = Btn95(ui, kbL, 95), w3 = Btn95(ui, dkL, 95);
        if (ui.Button95("p95.rc", new RectangleF(9, by, w1, 23), "Re&center", _h.VrConnected)) _h.Recenter();
        if (ui.Button95("p95.kb", new RectangleF(15 + w1, by, w2, 23), kbL))
            _h.ToggleKeyboard();
        if (ui.Button95("p95.desk", new RectangleF(21 + w1 + w2, by, w3, 23), dkL))
            _h.ToggleDesktops();
        float x = W95W - 9 - 75;
        if (ui.Button95("p95.apply", new RectangleF(x, by, 75, 23), "&Apply", _h.Cfg.Dirty)) _h.Cfg.SaveIfDue(force: true);
        x -= 6 + 75;
        if (ui.Button95("p95.cl", new RectangleF(x, by, 75, 23), "Close", close != null)) close?.Invoke();
        x -= 6 + 75;
        if (ui.Button95("p95.ok", new RectangleF(x, by, 75, 23), "OK", true, isDefault: true))
        {
            _h.Cfg.SaveIfDue(force: true);
            close?.Invoke();
        }
        if (modal)
        {
            ui.Inert = false;
            if (WhatsNewView.Draw95(ui, new RectangleF(155, 66, 440, 340), wn, WhatsNewSubShown)) CloseWhatsNew();
        }
        if (ui.Changed) _h.Cfg.MarkDirty();
    }

    // a bottom-row button as wide as its words need, never narrower than its 1995 size (the English ones keep their places)
    static float Btn95(Ui ui, string label, float min, float icon = 0) =>
        Lang.English ? min : MathF.Max(min, MathF.Ceiling(ui.LabelWidth95(label)) + 16 + icon);

    // ---- rows ----

    void Lbl(Ui ui, float x, float y, string text, bool enabled = true) => ui.Label95(text, new RectangleF(x, y, 200, RowH), enabled);

    bool ComboRow(Ui ui, string id, float x, float y, float cx, float cw, string label, string[] items, ref int index, bool enabled = true)
    {
        Lbl(ui, x, y, label, enabled);
        return ui.Combo95(id, new RectangleF(cx, y, cw, RowH), items, ref index, enabled);
    }

    // label, trackbar, value - 30 px high
    bool TrackRow(Ui ui, string id, float x, float y, float cx, float tw, float vw, string label, ref float v, float min, float max,
        string value, bool enabled = true)
    {
        ui.Label95(label, new RectangleF(x, y + 3, cx - x, 15), enabled);
        bool ch = ui.Track95(id, new RectangleF(cx - 6, y, tw + 12, 30), ref v, min, max, enabled);
        ui.Text95(Fit95(ui, value, vw), new RectangleF(cx + tw + 10, y + 3, vw, 15), K95, false, TextAlignment.Leading, enabled);
        return ch;
    }

    void Hint(Ui ui, float x, float y, float w, string text, float h = 13) =>
        ui.Text95(text, new RectangleF(x, y, w, h), K95, wrap: h > 13);

    static readonly string[] Attach4 = { "World", "Left hand", "Right hand", "Head" };

    string Fit95(Ui ui, string s, float w)
    {
        // a text cut once is kept (the hints, the About rows: a few strings per letter of a long one, every frame); another look or
        // language measures again
        int ver = Theme.Version * 1000 + Lang.Version;
        if (ver != _fit95For || _fit95.Count > 128) { _fit95.Clear(); _fit95For = ver; }
        if (s != null && _fit95.TryGetValue((s, w), out var done)) return done;
        string key = s;
        s = Lang.T(s);                     // cut in the language picked
        if (string.IsNullOrEmpty(s) || ui.Width95(s) <= w) return key == null ? s : _fit95[(key, w)] = s;
        int lo = 0, hi = s.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (ui.Width95(s[..mid] + "...") <= w) lo = mid; else hi = mid - 1;
        }
        if (lo > 0 && char.IsHighSurrogate(s[lo - 1])) lo--;
        return _fit95[(key, w)] = s[..lo].TrimEnd() + "...";
    }

    readonly Dictionary<(string, float), string> _fit95 = new();
    int _fit95For = -1;

    static string Radius(float width, float curve) => curve < 0.005f ? "flat" : Lang.F("radius {0:0.00} m", width / (2 * MathF.PI * curve));

    // ---------------- Desktops (Display Properties' Settings tab) ----------------

    void Desktops95(Ui ui)
    {
        var p = ui.P;
        var mons = _h.MonitorList;
        var vm = _h.Virtual;                    // (10-21: with virtual monitors to offer, their group box at the right end)
        var box = new RectangleF(16, 56, vm == null ? 718 : 500, 100);
        p.Fill(box, Theme.W95Face);
        p.Bevel(box, Painter.Edge.Sunken);
        if (vm != null) Virtual95(ui, vm, new RectangleF(524, 50, 210, 106));
        if (mons.Count == 0) { ui.Text95("No monitors found.", new RectangleF(28, 98, 300, 13)); return; }
        if (_selected == null || !mons.Any(m => m.Id == _selected)) _selected = mons.FirstOrDefault(m => !m.Virtual)?.Id ?? mons[0].Id;

        // the arrangement: little numbered monitors, teal when they are shown in VR
        var map = RectangleF.Inflate(box, -10, -8);
        int minX = mons.Min(m => m.X), minY = mons.Min(m => m.Y), maxX = mons.Max(m => m.X + m.W), maxY = mons.Max(m => m.Y + m.H);
        float s = Math.Min(map.Width / (maxX - minX), map.Height / (maxY - minY));
        float ox = map.X + MathF.Round((map.Width - (maxX - minX) * s) / 2), oy = map.Y + MathF.Round((map.Height - (maxY - minY) * s) / 2);
        var names = new string[mons.Count];
        int sel = 0;
        for (int i = 0; i < mons.Count; i++)
        {
            var m = mons[i];
            var dc = C.Desktops.FirstOrDefault(d => d.Id == m.Id);
            var r = new RectangleF(MathF.Round(ox + (m.X - minX) * s) + 2, MathF.Round(oy + (m.Y - minY) * s) + 2,
                MathF.Round(m.W * s) - 4, MathF.Round(m.H * s) - 4);
            bool on = m.Id == _selected, en = dc?.Enabled ?? false;
            if (on) sel = i;
            p.Fill(r, Theme.W95Face);
            p.Bevel(r, Painter.Edge.Raised);
            var screen = RectangleF.Inflate(r, -4, -4);
            p.Fill(screen, en ? Theme.W95Teal : Grey95);
            p.Bevel(RectangleF.Inflate(screen, 1, 1), Painter.Edge.Sunken);
            ui.Text95((i + 1).ToString(), screen, W95, true, TextAlignment.Center, size: 16);
            if (on) p.Stroke(new RectangleF(r.X - 1.5f, r.Y - 1.5f, r.Width + 3, r.Height + 3), K95, 1);
            if (ui.Hover(r) && ui.Pressed) _selected = m.Id;
            names[i] = $"{i + 1}. {m.Name} ({m.W} x {m.H}){(m.Primary ? Lang.T(", primary") : "")}";
        }
        bool changedBefore = ui.Changed;
        // (10-23) shown again: in front of you, as SteamVR's dashboard - beside the list, which gives it room
        bool rs = C.General.RecenterOnShow;
        if (ui.Check95("d95.rcshow", 534, 168, "Re&center when shown", ref rs)) C.General.RecenterOnShow = rs;
        if (ComboRow(ui, "d95.disp", 16, 164, 70, 432, "&Display:", names, ref sel))
        {
            _selected = mons[sel].Id;
            ui.Changed = changedBefore;         // only a selection, not a setting
        }
        var mon = mons.First(m => m.Id == _selected);
        var cfg = C.Desktops.FirstOrDefault(d => d.Id == mon.Id);
        if (cfg == null) return;

        int gs = _h.GroupSize(cfg);
        ui.Group95(new RectangleF(16, 192, 350, 238), gs > 1 ? Lang.F("This monitor (in a row of {0})", gs) : "This monitor");
        bool en2 = cfg.Enabled;
        if (ui.Check95("d95.en", 28, 212, "&Show in VR", ref en2)) _h.SetDesktopEnabled(cfg, en2);
        int ai = Math.Max(0, Array.IndexOf(AttachIds, cfg.Place.Attach ?? "world")), a2 = ai;
        if (ComboRow(ui, "d95.att", 28, 234, 108, 120, "Attached &to:", Attach4, ref a2, cfg.Enabled)) _h.SetDesktopAttach(cfg, AttachIds[a2]);
        bool mouse = cfg.Mouse;
        if (ui.Check95("d95.m", 28, 264, "&Mouse control (the laser moves the cursor)", ref mouse, cfg.Enabled)) cfg.Mouse = mouse;
        float v = cfg.Width;
        if (TrackRow(ui, "d95.w", 28, 286, 108, 150, 90, "Si&ze:", ref v, 0.3f, 3f, Lang.F("{0:0.00} m wide", cfg.Width), cfg.Enabled))
        { cfg.Width = v; _h.DesktopEdited(cfg); }
        v = cfg.Opacity;
        if (TrackRow(ui, "d95.o", 28, 318, 108, 150, 90, "&Opacity:", ref v, 0.2f, 1f, $"{cfg.Opacity * 100:0}%", cfg.Enabled)) cfg.Opacity = v;
        v = cfg.Curvature;
        if (TrackRow(ui, "d95.c", 28, 350, 108, 150, 90, "C&urvature:", ref v, 0f, 0.5f, Radius(cfg.Width, cfg.Curvature), cfg.Enabled))
        { cfg.Curvature = v < 0.005f ? 0 : v; _h.DesktopEdited(cfg); }
        bool live = _h.VrConnected && cfg.Enabled;
        if (ui.Button95("d95.reset", new RectangleF(28, 396, 100, 23), "&Reset position", live)) _h.ResetDesktop(cfg);
        if (ui.Button95("d95.fit", new RectangleF(134, 396, 110, 23), "Curve &around me", live && (cfg.Place.Attach ?? "world") == "world"))
            _h.FitCurve(cfg);

        ui.Group95(new RectangleF(376, 192, 358, 238), "All monitors");
        bool smart = C.General.SmartCapture;
        ui.Check95("d95.smart", 388, 212, "Smart &frame rate", ref smart);
        C.General.SmartCapture = smart;
        Hint(ui, 407, 230, 315, "Where you look or point: headset rate. At the edge of your view 15 fps, behind you 4 fps.", 28);
        float fps = C.General.CaptureFps;
        TrackRow(ui, "d95.fps", 388, 266, 476, 150, 90, "Ca&pture rate:", ref fps, 15, 90, $"{Math.Round(fps):0} fps", !smart);
        C.General.CaptureFps = (int)Math.Round(fps);
        if (ui.Button95("d95.join", new RectangleF(388, 304, 170, 23), "&Join into one curved screen", _h.VrConnected)) _h.JoinAllDesktops();
        if (ui.Button95("d95.sep", new RectangleF(564, 304, 75, 23), "S&eparate")) _h.SeparateDesktops();
        Hint(ui, 388, 332, 334, "Drop a monitor next to another one in VR and it snaps into the row. Click a monitor above to set it up.", 28);
        // gaze focus (Panels/GazeFocus.cs, 10-18): the one you look at full size, the others of its row smaller beside it
        bool focus = C.Focus?.Enabled ?? false;
        if (ui.Check95("d95.focus", 388, 366, "Focus the monitor you &look at (the others shrink)", ref focus)) (C.Focus ??= new FocusConfig()).Enabled = focus;
        float oth = C.Focus?.Others ?? GazeFocus.OthersDefault;
        if (TrackRow(ui, "d95.others", 388, 390, 476, 150, 90, "Ot&hers:", ref oth, GazeFocus.OthersMin, GazeFocus.OthersMax, $"{oth * 100:0}%", focus))
            (C.Focus ??= new FocusConfig()).Others = oth;
        bool las = C.Focus?.Laser ?? true;                  // a laser on a monitor focuses it too (off: only the look)
        if (ui.Check95("d95.flaser", 388, 424, "A &laser on a monitor focuses it too", ref las, focus)) (C.Focus ??= new FocusConfig()).Laser = las;
    }

    // virtual monitors (Sys/VirtualMonitors.cs, 10-21): an up-down count box, or "Add..." the first time
    static readonly string[] Up95 = { "...k...", "..kkk..", ".kkkkk.", "kkkkkkk" };
    static readonly string[] Down95 = { "kkkkkkk", ".kkkkk.", "..kkk..", "...k..." };

    void Virtual95(Ui ui, VirtualMonitors vm, RectangleF g)
    {
        vm.LookSoon();
        ui.Group95(g, "Virtual monitors");
        float x = g.X + 12, y = g.Y + 22, w = g.Width - 24;
        if (!vm.Admin) { Hint(ui, x, y, w, "Orbit needs to run as administrator for these (General tab).", 41); return; }
        if (vm.Busy && vm.Doing != null || vm.State == VirtualMonitors.Kind.Unknown) { Hint(ui, x, y + 5, w, vm.Doing ?? "Looking..."); return; }
        if (vm.State == VirtualMonitors.Kind.NoDriver)
        {
            if (ui.Button95("v95.add", new RectangleF(x, y, 75, 23), "A&dd...")) _h.InstallVirtual();
            Hint(ui, x, y + 31, w, vm.Note ?? "Installs a free display driver. Windows asks once.", 41);
            return;
        }
        int n = C.General.VirtualMonitors;
        ui.Label95("Cou&nt:", new RectangleF(x, y + 5, 44, 13));
        var ed = new RectangleF(x + 46, y + 1, 30, 21);
        ui.Edit95(ed, n.ToString());
        if (ui.GlyphButton95("v95.up", new RectangleF(ed.Right, ed.Y, 16, 11), Up95, n < VirtualMonitors.Max)) _h.SetVirtualCount(n + 1);
        if (ui.GlyphButton95("v95.dn", new RectangleF(ed.Right, ed.Y + 11, 16, 10), Down95, n > 0)) _h.SetVirtualCount(n - 1);
        float sx = ed.Right + 24;
        if (n == 0)
        {
            if (ui.Button95("v95.rm", new RectangleF(sx, y, g.Right - 12 - sx, 23), "Remove dri&ver")) _h.RemoveVirtual();
        }
        else Hint(ui, sx, y + 5, g.Right - 12 - sx, vm.State == VirtualMonitors.Kind.Broken ? "A problem" : vm.State == VirtualMonitors.Kind.On
            ? Lang.F("{0} on", vm.Count) : C.General.VirtualOnlyInVr && !_h.VrConnected ? "With SteamVR" : "Off");
        bool only = C.General.VirtualOnlyInVr;
        if (ui.Check95("v95.vr", x, y + 33, "Only &while in VR", ref only)) _h.SetVirtualOnlyInVr(only);
        if (vm.Note != null) Hint(ui, x, y + 53, w, vm.Note, 28);
    }

    // ---------------- Windows ----------------

    void Windows95(Ui ui)
    {
        var p = ui.P;
        var list = C.Windows;
        if (_selWin != null && !list.Any(w => w.Id == _selWin)) _selWin = null;
        if (_selWin == null && list.Count > 0) _selWin = list[0].Id;

        ui.Group95(new RectangleF(16, 56, 350, 374), "Mirrored windows");
        int sel = list.FindIndex(w => w.Id == _selWin);
        int hit = ui.List95("w95.list", new RectangleF(28, 76, 326, 260), list.Count, Picking ? -1 : sel, (i, r, on) =>
        {
            var wc = list[i];
            W95Icons.Draw(p, W95Icons.Id.Window, r.X + 1, r.Y);
            string title = string.IsNullOrEmpty(wc.Title) ? wc.Process : wc.Title;
            ui.Text95(Fit95(ui, title, r.Width - 24), new RectangleF(r.X + 20, r.Y, r.Width - 22, r.Height), on ? W95 : wc.Enabled ? K95 : Grey95);
        });
        if (hit >= 0) { _selWin = list[hit].Id; Picking = false; _pickHwnd = IntPtr.Zero; }
        if (list.Count == 0) ui.Text95("(none)", new RectangleF(32, 80, 300, 13), Grey95);
        var cur = list.FirstOrDefault(w => w.Id == _selWin);
        if (ui.Button95("w95.add", new RectangleF(28, 344, 75, 23), "&Add...", list.Count > 0 && list.Count < MaxWindows && !Picking))
        {
            Picking = true;
            _pickHwnd = IntPtr.Zero;
        }
        if (ui.Button95("w95.rm", new RectangleF(109, 344, 75, 23), "&Remove", cur != null && !Picking))
        {
            _h.RemoveWindow(cur);
            _selWin = null;
            return;
        }
        Hint(ui, 28, 372, 326, "Mirror one app window instead of a whole monitor. Clicking it in VR brings it to the front on your PC.", 28);
        bool pop = C.General.PopOut;
        if (ui.Check95("w95.pop", 28, 406, "&Pull a window off a monitor to float it", ref pop)) C.General.PopOut = pop;

        if (Picking || list.Count == 0) { Picker95(ui, list.Count > 0); return; }
        if (cur == null) return;

        ui.Group95(new RectangleF(376, 56, 358, 374), "This window");
        W95Icons.Draw(p, W95Icons.Id.Window, 388, 76);
        ui.Text95(Fit95(ui, string.IsNullOrEmpty(cur.Title) ? cur.Process : cur.Title, 310), new RectangleF(410, 76, 312, 16), K95, true);
        ui.Text95(Fit95(ui, $"{cur.Process} - {WinStatus(_h.WindowStatus(cur))}", 310), new RectangleF(410, 92, 312, 13), Grey95);
        bool en = cur.Enabled;
        if (ui.Check95("wn95.en", 388, 118, "&Show in VR", ref en)) _h.SetWindowEnabled(cur, en);
        int ai = Math.Max(0, Array.IndexOf(AttachIds, cur.Place.Attach ?? "world")), a2 = ai;
        if (ComboRow(ui, "wn95.att", 388, 140, 468, 120, "Attached &to:", Attach4, ref a2, cur.Enabled)) _h.SetWindowAttach(cur, AttachIds[a2]);
        bool mouse = cur.Mouse;
        if (ui.Check95("wn95.m", 388, 170, "&Mouse control (the laser moves the cursor)", ref mouse, cur.Enabled)) cur.Mouse = mouse;
        float v = cur.Width;
        if (TrackRow(ui, "wn95.w", 388, 192, 468, 150, 90, "Si&ze:", ref v, 0.15f, 3f, Lang.F("{0:0.00} m wide", cur.Width), cur.Enabled)) cur.Width = v;
        v = cur.Opacity;
        if (TrackRow(ui, "wn95.o", 388, 224, 468, 150, 90, "&Opacity:", ref v, 0.2f, 1f, $"{cur.Opacity * 100:0}%", cur.Enabled)) cur.Opacity = v;
        v = cur.Curvature;
        if (TrackRow(ui, "wn95.c", 388, 256, 468, 150, 90, "C&urvature:", ref v, 0f, 0.5f, Radius(cur.Width, cur.Curvature), cur.Enabled))
            cur.Curvature = v < 0.005f ? 0 : v;
        if (ui.Button95("wn95.reset", new RectangleF(388, 300, 100, 23), "R&eset position", _h.VrConnected && cur.Enabled)) _h.ResetWindow(cur);
    }

    IntPtr _pickHwnd;      // the window chosen in the picker (by handle: the list re-reads itself every second)

    void Picker95(Ui ui, bool canCancel)
    {
        var p = ui.P;
        ui.Group95(new RectangleF(376, 56, 358, 374), "Add a window");
        var open = _pickOpen != null && (ui.Pressed || ui.Down || ui.Released || ui.Interacting) ? _pickOpen : (_pickOpen = _h.OpenWindows());
        int sel = -1;
        for (int i = 0; i < open.Count; i++) if (open[i].Hwnd == _pickHwnd) { sel = i; break; }
        Lbl(ui, 388, 70, "&Choose the window to mirror:");
        int hit = ui.List95("pk95.list", new RectangleF(388, 92, 334, 292), open.Count, sel, (i, r, on) =>
        {
            var w = open[i];
            bool mirrored = _h.IsMirrored(w.Hwnd);
            W95Icons.Draw(p, W95Icons.Id.Window, r.X + 1, r.Y);
            ui.Text95(Fit95(ui, w.Title, r.Width - 112), new RectangleF(r.X + 20, r.Y, r.Width - 110, r.Height), on ? W95 : mirrored ? Grey95 : K95);
            ui.Text95(Fit95(ui, mirrored ? "(mirrored)" : w.Process, 84), new RectangleF(r.Right - 88, r.Y, 86, r.Height), on ? W95 : Grey95);
        });
        if (hit >= 0) { sel = hit; _pickHwnd = open[hit].Hwnd; }
        bool can = sel >= 0 && !_h.IsMirrored(open[sel].Hwnd) && C.Windows.Count < MaxWindows;
        float bx = 734 - 12 - 75;
        if (canCancel && ui.Button95("pk95.cancel", new RectangleF(bx, 396, 75, 23), "Cancel"))
        {
            Picking = false;
            _pickHwnd = IntPtr.Zero;
        }
        if (canCancel) bx -= 81;
        if (ui.Button95("pk95.ok", new RectangleF(bx, 396, 75, 23), "&Mirror", can, isDefault: true))
        {
            _selWin = _h.AddWindow(open[sel]).Id;
            Picking = false;
            _pickHwnd = IntPtr.Zero;
        }
    }

    // ---------------- Layouts ----------------

    void Layouts95(Ui ui)
    {
        bool inert95 = Dialog95Begin(ui, _rulesOpen);      // the Per game dialog over the page (SettingsView95.Extras.cs)
        var list = C.Layouts;
        if (_selLayout == null || !list.Any(l => l.Name == _selLayout)) _selLayout = C.ActiveLayout;
        ui.Group95(new RectangleF(16, 56, 350, 236), "Layout profiles");
        int sel = list.FindIndex(l => l.Name == _selLayout);
        int hit = ui.List95("l95.list", new RectangleF(28, 76, 326, 4 + 6 * 18), list.Count, sel, (i, r, on) =>
        {
            var l = list[i];
            W95Icons.Draw(ui.P, W95Icons.Id.Layouts, r.X + 1, r.Y + 1);
            ui.Text95(l.Name == C.ActiveLayout ? Lang.F("{0}  (in use)", Lang.T(l.Name)) : l.Name, new RectangleF(r.X + 20, r.Y, r.Width - 22, r.Height), on ? W95 : K95);
        }, 18);
        if (hit >= 0) _selLayout = list[hit].Name;
        var s = list.First(l => l.Name == _selLayout);
        ui.Text95(LayoutSummary(s), new RectangleF(28, 192, 326, 13));
        Hint(ui, 28, 214, 326, "Each profile remembers where your monitors, windows and keyboard are, their size and curve, and night mode. " +
                               "What you change is kept in the profile you are in when you switch away.", 60);

        bool active = s.Name == C.ActiveLayout;
        ui.Group95(new RectangleF(376, 56, 358, 236), Ui.Esc95(s.Name));
        W95Icons.Draw(ui.P, W95Icons.Id.Layouts, 388, 76);
        ui.Text95(active ? "In use now." : s.Saved is { } t ? Lang.F("Last used {0:MMM d, HH:mm}.", t) : "Not used yet.", new RectangleF(410, 77, 312, 15));
        if (ui.Button95("l95.use", new RectangleF(388, 102, 150, 23), active ? "In use" : Lang.F("&Switch to {0}", Ui.Esc95(Lang.T(s.Name))), !active, isDefault: !active))
            _h.SwitchLayout(s.Name);
        bool onWrist = s.OnWrist;
        if (ui.Check95("l95.wrist", 388, 138, "On the &watch (its button cycles through these)", ref onWrist)) s.OnWrist = onWrist;
        if (ui.Button95("l95.forget", new RectangleF(388, 162, 150, 23), "&Forget this layout", s.Saved != null && !active))
            _h.ForgetLayout(s.Name);
        Hint(ui, 388, 198, 334, active
                ? "You are using this one now: move, resize and bend things as you like - the profile keeps it when you switch to another one."
                : s.Saved == null
                ? "The first time you switch to it, everything comes to where you are looking - lying in bed, that is above you."
                : "Switching puts every panel back where it was in this profile. Recenter still brings everything in front of you.", 60);
        Theater95(ui);
        PerGame95(ui, inert95);
    }

    // theater mode: the same settings as on the bar under a screen in VR, and the bar itself on / off
    void Theater95(Ui ui)
    {
        var t = C.Theater;
        ui.Group95(new RectangleF(16, 300, 718, 130), "Theater mode");
        W95Icons.Draw(ui.P, W95Icons.Id.Theater, 28, 320);
        bool b = t.Bar;
        if (ui.Check95("t95.bar", 52, 322, "Show a &bar under the monitor you point at (its lower part)", ref b)) t.Bar = b;
        Hint(ui, 70, 340, 640, "The bar has a Theater button and these settings; in theater mode it switches between monitors.", 30);
        ui.Label95("&Dim the world:", new RectangleF(28, 378, 90, 15));
        float v = t.Dim;
        if (ui.Track95("t95.dim", new RectangleF(118, 372, 150, 28), ref v, 0, 0.9f, true, 10)) { t.Dim = v; _h.TheaterEdited(); }
        ui.Text95(v < 0.005f ? "off" : $"{v * 100:0}%", new RectangleF(274, 378, 40, 15));
        ui.Label95("Screen si&ze:", new RectangleF(330, 378, 80, 15));
        v = t.Width;
        if (ui.Track95("t95.size", new RectangleF(410, 372, 150, 28), ref v, Theater.MinWidth, Theater.MaxWidth, true, 10)) { t.Width = v; _h.TheaterEdited(); }
        ui.Text95($"{v:0.0} m", new RectangleF(566, 378, 44, 15));
        b = t.Curved;
        if (ui.Check95("t95.curve", 628, 378, "&Curved", ref b)) { t.Curved = b; _h.TheaterEdited(); }
        b = t.Ambilight;
        if (ui.Check95("t95.amb", 28, 408, "A&mbilight: a soft glow round the screen in its colours", ref b)) { t.Ambilight = b; _h.TheaterEdited(); }
        b = t.Auto;                     // a video going full screen starts it by itself (10-18, Sys/AutoTheater.cs)
        if (ui.Check95("t95.auto", 380, 408, "A&uto theater when a video goes full screen", ref b)) t.Auto = b;
    }

    // ---------------- Wrist ----------------

    void Wrist95(Ui ui)
    {
        if (EditingWatch && C.Wrist.Face == "custom") { Wrist95Editor(ui); return; }
        UndoForget();
        bool inert95 = Dialog95Begin(ui, _wx95);          // the Weather dialog over the page (SettingsView.Weather.cs)
        var p = ui.P;
        var w = C.Wrist;
        bool custom = w.Face == "custom";
        ui.Group95(new RectangleF(16, 56, 350, 218), "Watch");
        bool b = w.Enabled;
        ui.Check95("w95.en", 28, 76, "&Show the watch", ref b);
        w.Enabled = b;
        int hi = w.Hand == "right" ? 1 : 0;
        if (ComboRow(ui, "w95.hand", 28, 98, 108, 100, "&Hand:", HandNames, ref hi, w.Enabled))
        { w.Hand = hi == 1 ? "right" : "left"; w.Offset = null; }
        var faces = FaceIds;
        int fi = Math.Max(0, Array.IndexOf(faces, w.Face ?? "full"));
        if (ComboRow(ui, "w95.face", 28, 126, 108, 100, "&Face:", FaceNames95, ref fi, w.Enabled))
            ChooseFace(faces[fi]);
        if (custom && ui.Button95("w95.edit", new RectangleF(214, 125, 140, 23), "&Edit layout...", w.Enabled)) EditingWatch = true;
        b = w.AlwaysVisible;
        ui.Check95("w95.always", 28, 156, "Always &visible (or only when you look at it)", ref b, w.Enabled);
        w.AlwaysVisible = b;
        float v = w.Width;
        TrackRow(ui, "w95.size", 28, 178, 108, 150, 90, "Si&ze:", ref v, 0.07f, 0.25f, $"{w.Width * 100:0} cm", w.Enabled);
        w.Width = v;
        b = w.Locked;
        ui.Check95("w95.lock", 28, 216, "&Lock position (the grip won't move it)", ref b, w.Enabled);
        w.Locked = b;
        if (ui.Button95("w95.reset", new RectangleF(28, 240, 100, 23), "&Reset position", w.Enabled)) _h.ResetWrist();
        // the Windows volume in VR (Sys/AudioMixer.cs): ticked = auto (Virtual Desktop), unticked = off
        b = C.General.VolumeToApps != "off";
        bool vb = b;
        ui.Check95("w95.vol", 140, 245, "&Windows volume in VR", ref b, true);
        if (b != vb) C.General.VolumeToApps = b ? null : "off";

        ui.Group95(new RectangleF(16, 282, 350, 148), "Show on the watch");
        b = w.ShowBatteries;
        ui.Check95("w95.bat", 28, 302, "&Batteries", ref b, w.Enabled && !custom);
        w.ShowBatteries = b;
        b = w.ShowMedia;
        ui.Check95("w95.media", 28, 322, "&Now playing", ref b, w.Enabled && !custom);
        w.ShowMedia = b;
        b = w.ShowPerformance;
        ui.Check95("w95.perf", 28, 342, "Frame &timing && GPU", ref b, w.Enabled && !custom);
        w.ShowPerformance = b;
        int pg = w.PerfGraph ? 1 : 0;
        if (ComboRow(ui, "w95.graph", 47, 362, 128, 100, "Sh&own as:", PerfNames, ref pg, w.Enabled && w.ShowPerformance && !custom))
            w.PerfGraph = pg == 1;
        b = C.Appearance.Clock24h;
        ui.Check95("w95.24h", 28, 396, "24-hour &clock", ref b, w.Enabled);
        C.Appearance.Clock24h = b;

        // the watch itself, pixel for pixel, on the 95 desktop
        ui.Group95(new RectangleF(376, 56, 358, 374), "Preview");
        var desk = new RectangleF(388, 76, 334, 256);
        Theme.RetroSkin.FillDesktop(p, desk);
        p.Bevel(desk, Painter.Edge.Sunken);
        var c95 = _h.Wrist.Canvas95;
        if (c95 != null)
            p.ImageNearest(c95.Bitmap, new RectangleF(desk.X + (desk.Width - c95.W) / 2, desk.Y + (desk.Height - c95.H) / 2, c95.W, c95.H));
        Hint(ui, 388, 342, 334, "Turn your wrist towards you to see it; point at it with the other hand to use its buttons. " +
                                "Hold the grip on it to move it.", 60);
        Weather95(ui, inert95);
    }

    // ---------------- Keyboard ----------------

    void Keyboard95(Ui ui)
    {
        var k = C.Keyboard;
        ui.Group95(new RectangleF(16, 56, 350, 190), "Keyboard");
        bool b = k.Visible;
        if (ui.Check95("k95.vis", 28, 76, "&Show the keyboard", ref b)) _h.ToggleKeyboard();
        b = k.AutoShow;
        ui.Check95("k95.auto", 28, 98, "&Pop up for text fields", ref b);
        k.AutoShow = b;
        Hint(ui, 47, 115, 300, "when a click in VR lands in one; hides again after");
        float v = k.Width;
        TrackRow(ui, "k95.size", 28, 136, 108, 150, 90, "Si&ze:", ref v, 0.3f, 1.2f, Lang.F("{0:0} cm wide", k.Width * 100));
        k.Width = v;
        int ai = Math.Max(0, Array.IndexOf(AttachIds, k.Place.Attach ?? "world"));
        if (ai == 3) ai = 0;
        int a2 = ai;
        if (ComboRow(ui, "k95.att", 28, 172, 108, 120, "Attached &to:", Attach4[..3], ref a2) && a2 != ai) _h.SetKeyboardAttach(AttachIds[a2]);
        if (ui.Button95("k95.reset", new RectangleF(28, 208, 100, 23), "&Reset position", _h.VrConnected)) _h.ResetKeyboard();

        ui.Group95(new RectangleF(376, 56, 358, 190), "Sounds");
        b = k.Sounds;
        ui.Check95("k95.snd", 388, 76, "&Key sounds", ref b);
        k.Sounds = b;
        int si = k.SoundStyle == "muted" ? 1 : 0;
        if (ComboRow(ui, "k95.style", 388, 98, 468, 100, "S&ound:", SoundNames(), ref si, k.Sounds))
        {
            k.SoundStyle = si == 1 ? "muted" : "click";
            _h.Keyboard.PlayClick(false);
        }
        v = k.SoundVolume;
        if (TrackRow(ui, "k95.vol", 388, 130, 468, 150, 90, "&Volume:", ref v, 0.05f, 1f, $"{k.SoundVolume * 100:0}%", k.Sounds))
            k.SoundVolume = v;
        if (k.Sounds && ui.Released && ui.Hover(new RectangleF(462, 130, 162, 30))) _h.Keyboard.PlayClick(false);   // hear the new volume
        if (ui.Button95("k95.test", new RectangleF(388, 172, 90, 23), "&Test sound", k.Sounds)) _h.Keyboard.PlayClick(false);

        ui.Group95(new RectangleF(16, 254, 718, 176), "Using it");
        W95Icons.Draw(ui.P, W95Icons.Id.Keyboard, 28, 274);
        ui.Text95("The keys follow the active Windows layout; the globe key switches layouts (and to a built-in English one).",
            new RectangleF(52, 274, 670, 30), K95, wrap: true);
        ui.Text95("Shift, Ctrl, Alt, AltGr and Win: press once for the next key, twice to lock.",
            new RectangleF(52, 302, 670, 30), K95, wrap: true);
        ui.Text95("Typing goes to the focused window. Chatbox mode types into the VRChat chatbox instead; Enter sends it.",
            new RectangleF(52, 330, 670, 30), K95, wrap: true);
    }

    // ---------------- Look (Display Properties' Appearance tab) ----------------

    void Look95(Ui ui)
    {
        var p = ui.P;
        var a = C.Appearance;
        bool changed = false;
        ui.Group95(new RectangleF(16, 56, 350, 150), "Style");
        int si = StyleMenu.IndexOf(a.Style);
        if (ComboRow(ui, "lk95.style", 28, 76, 108, 150, "&Style:", StyleMenu.Names, ref si))
        {
            a.Style = StyleMenu.Ids[si];
            changed = true;
        }
        string era = Theme.Retro == RetroStyle.Win95 ? "their real 1995 pixel size" : "the real pixel size of its day";
        Hint(ui, 28, 108, 326, Lang.F("{0} draws these settings, the watch, the keyboard and the notifications at {1}, enlarged without smoothing.",
                               Lang.T(Theme.RetroSkin.Name), Lang.T(era)), 60);

        ui.Group95(new RectangleF(16, 214, 350, 106), "Modern colours");
        Hint(ui, 28, 234, 326, "The accent colour, the panel colour, the themes and the opacity belong to the Modern style; switch to it " +
                               "to change them.", 46);
        if (ui.Button95("lk95.reset", new RectangleF(28, 286, 100, 23), "&Default look"))
        {
            C.Appearance = a = new AppearanceConfig
            {
                LaserThickness = a.LaserThickness, LaserAlways = a.LaserAlways, Clock24h = a.Clock24h,
                NightMode = a.NightMode, NightBrightness = a.NightBrightness, Style = a.Style, Font = a.Font, Motion = a.Motion,
                LookVersion = 1,     // the reset IS the Bento look: never the 1.10 migration again (1.13.2 put pink over a green accent)
            };
            changed = true;
        }

        // Look > Motion: option buttons, one under the other (written only when not Full; it applies at once)
        ui.Group95(new RectangleF(16, 328, 350, 102), "Motion");
        int mi = MotionIndex(a.Motion);
        for (int i = 0; i < Motion95.Length; i++)
            if (ui.Radio95(Ui.Id("lk95.motion", i), 28, 348 + i * 20, Motion95[i], mi == i) && mi != i)
            {
                a.Motion = Motion.Id((MotionLevel)i);
                changed = true;
            }
        Hint(ui, 28, 410, 326, Theme.RetroSkin == Skins.Xp ? "The window zoom, the sliding lists, the fades."
                                                            : "The window zoom, the sliding pages, the Modern style's animations.");

        ui.Group95(new RectangleF(376, 56, 358, 190), "Laser");
        Lbl(ui, 388, 76, "Colour:");
        // colour wells: "same as the style", then the presets
        var wells = new List<uint> { 0 };
        wells.AddRange(Theme.AccentPresets.Take(Theme.AccentPresets.Length - 1));
        for (int i = 0; i < wells.Count; i++)
        {
            var r = new RectangleF(468 + i * 26, 76, 20, 20);
            uint c = wells[i] == 0 ? Theme.ClassicLaser : wells[i];
            bool on = wells[i] == 0 ? a.LaserColor == 0 : a.LaserColor != 0 && (a.LaserColor & 0xFFFFFF) == (c & 0xFFFFFF);
            if (on) p.Stroke(new RectangleF(r.X - 2.5f, r.Y - 2.5f, r.Width + 5, r.Height + 5), K95, 1);
            p.Fill(r, c);
            p.Bevel(r, Painter.Edge.Sunken);
            if (ui.Hover(r) && ui.Pressed) { a.LaserColor = wells[i]; changed = true; }
        }
        float th = a.LaserThickness;
        if (TrackRow(ui, "lk95.th", 388, 108, 468, 150, 90, "&Thickness:", ref th, 1.5f, 8f, $"{a.LaserThickness:0.0} mm"))
        { a.LaserThickness = MathF.Round(th * 2) / 2; changed = true; }
        bool always = a.LaserAlways;
        ui.Check95("lk95.always", 388, 150, "&Always show the beam", ref always);
        Hint(ui, 407, 167, 315, "otherwise only while pointing at a panel");
        if (always != a.LaserAlways) { a.LaserAlways = always; changed = true; }

        ui.Group95(new RectangleF(376, 254, 358, 176), "Night mode");
        bool night = a.NightMode;
        ui.Check95("lk95.night", 388, 274, "&Night mode", ref night);
        Hint(ui, 407, 291, 315, "dims every panel in VR - the watch has a button for it too");
        if (night != a.NightMode) { a.NightMode = night; changed = true; }
        float nb = a.NightBrightness;
        if (TrackRow(ui, "lk95.nb", 388, 314, 468, 150, 90, "&Brightness:", ref nb, 0.15f, 0.9f, $"{a.NightBrightness * 100:0}%"))
        { a.NightBrightness = nb; changed = true; }
        if (changed) { ui.Changed = true; _h.AppearanceChanged(); }
    }

    static readonly string[] Motion95 = { "&Full", "&Reduced - short movements only", "&Off - everything changes at once" };

    // ---------------- General ----------------

    void Vrchat95(Ui ui)
    {
        var cb = C.Chatbox;
        var rm = C.Room;
        var g = C.General;
        ui.Group95(new RectangleF(16, 56, 350, 206), "Chatbox status line");
        bool b = cb.StatusLine;
        ui.Check95("vc95.on", 28, 76, "Send a &status line to the chatbox", ref b);
        cb.StatusLine = b;
        Hint(ui, 47, 93, 310, "over OSC - OSC must be on in VRChat");
        b = cb.ShowMedia;
        ui.Check95("vc95.m", 47, 114, "&Now playing", ref b);
        cb.ShowMedia = b;
        b = cb.ShowTime;
        ui.Check95("vc95.t", 47, 134, "C&lock", ref b);
        cb.ShowTime = b;
        b = cb.ShowVrTime;
        ui.Check95("vc95.v", 47, 154, "Time in &VR", ref b);
        cb.ShowVrTime = b;
        ui.Label95("Preview:", new RectangleF(28, 178, 80, 15));
        var box = new RectangleF(28, 196, 326, 56);
        ui.P.Fill(box, 0xFFFFFFFF);
        ui.P.Bevel(box, Painter.Edge.Sunken);
        string prev = _h.StatusLinePreview;
        ui.Text95(string.IsNullOrEmpty(prev) ? "(nothing - the chatbox stays empty)" : prev, new RectangleF(box.X + 4, box.Y + 3, box.Width - 8, box.Height - 6),
            string.IsNullOrEmpty(prev) ? 0xFF808080 : K95, false, TextAlignment.Leading, true, true);

        // eye tracking (Sys/EyeGaze.cs)
        ui.Group95(new RectangleF(376, 230, 358, 110), "Eye tracking");       // (right, under Who is in the room)
        bool eb = C.General.EyeTracking;
        if (ui.Check95("vc95.eyes", 388, 250, "Use &eye tracking (Virtual Desktop's)", ref eb)) C.General.EyeTracking = eb;
        Hint(ui, 407, 268, 316, _h.EyeStatus, 16);
        eb = C.General.DimUnlooked;
        if (ui.Check95("vc95.dim", 388, 288, "&Dim the monitors you don't look at", ref eb, C.General.EyeTracking)) C.General.DimUnlooked = eb;
        int eci = Math.Max(0, Array.IndexOf(EyeCursorIds, C.General.EyeCursor ?? "follow"));
        if (ComboRow(ui, "vc95.eyec", 388, 306, 500, 170, "Eye &cursor:", EyeCursor95, ref eci, C.General.EyeTracking)) C.General.EyeCursor = EyeCursorIds[eci];
        ui.Group95(new RectangleF(16, 270, 350, 64), "Controllers");
        b = g.BlockGameInput;
        ui.Check95("vc95.block", 28, 290, "&Keep clicks from the game", ref b);
        g.BlockGameInput = b;
        Hint(ui, 47, 307, 310, "while a laser points at an OrbitOverlay panel");

        ui.Group95(new RectangleF(376, 56, 358, 166), "Who is in the room");
        b = rm.Enabled;
        ui.Check95("vc95.room", 388, 76, "&Follow VRChat's log", ref b);
        rm.Enabled = b;
        Hint(ui, 407, 93, 320, Fit95(ui, _h.RoomStatus, 320));
        b = rm.NotifyFriends;
        ui.Check95("vc95.nf", 388, 114, "Tell me when &friends come and go", ref b, rm.Enabled);
        rm.NotifyFriends = b;
        b = rm.NotifyStarred;
        ui.Check95("vc95.ns", 388, 134, "... and s&tarred people", ref b, rm.Enabled);
        rm.NotifyStarred = b;
        b = rm.NotifyEveryone;
        ui.Check95("vc95.ne", 388, 154, "... and &everyone", ref b, rm.Enabled);
        rm.NotifyEveryone = b;
        b = rm.BuzzFriends;
        ui.Check95("vc95.buzz", 560, 154, "&Buzz for friends", ref b, rm.Enabled && rm.NotifyFriends);
        rm.BuzzFriends = b;
        string roomL = rm.Visible ? "Hide Room &panel" : "Show Room &panel";
        if (ui.Button95("vc95.open", new RectangleF(388, 186, Btn95(ui, roomL, 140), 23), roomL, _h.VrConnected && rm.Enabled))
            _h.ToggleRoom();
        Gesture95(ui);                // B / Y twice (SettingsView95.Extras.cs)
    }

    void General95(Ui ui)
    {
        var g = C.General;
        ui.Group95(new RectangleF(16, 56, 350, 122), "Laser");
        int li = g.LaserHand == "left" ? 1 : g.LaserHand == "right" ? 2 : 0;
        if (ComboRow(ui, "g95.laser", 28, 76, 108, 120, "Laser &on:", LaserHands95, ref li))
            g.LaserHand = li == 1 ? "left" : li == 2 ? "right" : "both";
        int sm = Math.Clamp(g.LaserSmoothing, 0, 3);
        if (ComboRow(ui, "g95.smooth", 28, 104, 108, 120, "S&moothing:", SmoothingNames95, ref sm))
            g.LaserSmoothing = sm;
        bool b = g.Haptics;
        ui.Check95("g95.hap", 28, 134, "&Haptic ticks", ref b);
        g.Haptics = b;
        b = g.QuickToggle;
        ui.Check95("g95.quick", 28, 154, "Double-tap both &grips: hide / show the panels", ref b);
        g.QuickToggle = b;

        ui.Group95(new RectangleF(16, 186, 350, 190), "Startup and system");
        b = g.AutoStart;
        ui.Check95("g95.auto", 28, 206, "Start with Steam&VR", ref b);
        Hint(ui, 47, 223, 300, Fit95(ui, b ? (_h.VrConnected ? _h.AutoStartStatus : "applied the next time SteamVR runs") : "start OrbitOverlay yourself", 300));
        if (b != g.AutoStart) _h.SetAutoStart(b);
        b = g.RunAsAdmin;
        ui.Check95("g95.admin", 28, 244, "Run as &administrator", ref b);
        Hint(ui, 47, 261, 300, _h.IsElevated ? "running as admin: works in admin windows too"
            : g.RunAsAdmin ? "not elevated right now - restart to apply" : "admin windows (e.g. Task Manager) ignore the VR mouse");
        if (b != g.RunAsAdmin) _h.SetRunAsAdmin(b);
        b = g.PauseWhenOff;
        ui.Check95("g95.pause", 28, 282, "&Pause when the headset is off", ref b);
        g.PauseWhenOff = b;
        b = g.Osc;
        ui.Check95("g95.osc", 28, 304, "VRChat avatar parameters (OS&C)", ref b);
        Hint(ui, 47, 321, 300, _oscHint95.F("XSOverlay-style, for DXOverlay - 127.0.0.1:{0}", g.OscPort));
        g.Osc = b;
        float bx = 28;
        if (g.RunAsAdmin && !_h.IsElevated)
        {
            float ew = Btn95(ui, "R&estart as admin", 110);
            if (ui.Button95("g95.elevate", new RectangleF(bx, 346, ew, 23), "R&estart as admin")) _h.RestartElevated();
            bx += ew + 6;
        }
        if (ui.Button95("g95.folder", new RectangleF(bx, 346, Btn95(ui, "Open con&fig folder", 130, 22), 23), "Open con&fig folder", true, false, false, W95Icons.Id.Folder))
            _h.OpenConfigFolder();

        // System Properties' General tab: the logo, the name, the version, the machine
        ui.Group95(new RectangleF(376, 56, 358, 178), "About OrbitOverlay");
        W95Icons.Draw(ui.P, W95Icons.Id.Orbit, 388, 74, 2);
        ui.Text95("OrbitOverlay", new RectangleF(430, 74, 200, 15), K95, true);
        ui.Text95(_version95.F("Version {0}", Updater.CurrentText), new RectangleF(430, 90, 200, 15));
        float ry = 116;
        void Row(string key, string val)
        {
            ui.Text95(key, new RectangleF(388, ry, 64, 13), Grey95);
            ui.Text95(Fit95(ui, val, 266), new RectangleF(454, ry, 268, 13));
            ry += 16;
        }
        Row("SteamVR:", _h.VrStatus);
        Row("Input:", _h.InputStatus);
        Row("Graphics:", _h.GpuName);
        Row("Monitors:", MonitorNames());
        var (us, umsg, uprog, ulatest) = _h.Updates.Snapshot();
        bool avail = us == Updater.State.Available;
        string ulabel = us switch
        {
            Updater.State.Checking => "Checking...",
            Updater.State.Downloading => Lang.F("Downloading {0:0}%", uprog * 100),
            Updater.State.Starting => "Installing...",
            Updater.State.Available => Lang.F("&Update to {0}", ulatest),
            _ => "Check for &updates",
        };
        // checking / downloading / installing: busy - the hourglass on the button (the desktop window's cursor turns into it too)
        if (ui.Button95("g95.upd", new RectangleF(388, 186, 130, 23), ulabel, !_h.Updates.Busy, avail, false,
                avail ? W95Icons.Id.Download : _h.Updates.Busy ? W95Icons.Id.Hourglass : null))
        {
            if (avail) _h.Updates.Apply();
            else _h.Updates.Check();
        }
        // busy: the style's marquee / progress bar there (95: the words)
        bool bar = _h.Updates.Busy && Theme.RetroSkin.BusyBar(ui.P, new RectangleF(526, 191, 196, 13), us == Updater.State.Downloading ? uprog : -1);
        if (!string.IsNullOrEmpty(umsg) && !bar)
            ui.Text95(Fit95(ui, umsg, 196), new RectangleF(524, 191, 200, 13), us == Updater.State.Failed ? 0xFF800000 : Grey95);
        if (ui.Button95("g95.whatsnew", new RectangleF(388, 212, 130, 23), "&What's New...")) { _whatsNewManual = true; }

        ui.Group95(new RectangleF(376, 242, 358, 188), "Notifications");
        b = g.Notifications;
        ui.Check95("g95.notif", 388, 262, "Show &notifications", ref b);
        Hint(ui, 407, 279, 315, Fit95(ui, g.Notifications ? _h.NotificationStatus : "XSOverlay format (VRCX, Discord plugins...)", 315));
        g.Notifications = b;
        b = g.NotificationSound;
        ui.Check95("g95.nsnd", 388, 302, "C&hime", ref b, g.Notifications);
        g.NotificationSound = b;
        if (ui.Button95("g95.ntest", new RectangleF(647, 298, 75, 23), "&Test", g.Notifications && _h.VrConnected)) _h.TestNotification();
        float na = g.NotificationAngle;
        TrackRow(ui, "g95.nang", 388, 330, 468, 150, 90, "H&eight:", ref na, 5, 35, _nang95.F("{0:0}° below eyes", Math.Round(na)), g.Notifications);
        g.NotificationAngle = MathF.Round(na);
        Alerts95(ui);                 // low battery / frame rate (SettingsView95.Extras.cs)

        // the language of every screen, each choice in its own words (Regional Settings' box): it applies at once
        ui.Group95(new RectangleF(16, 382, 350, 48), "Language");
        int lg = Lang.Index(Lang.Code);
        if (ComboRow(ui, "g95.lang", 28, 401, 108, 150, "&Language:", LanguageNames, ref lg)) SetLanguage(ui, lg);
    }
}
