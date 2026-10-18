using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Vortice.DirectWrite;

namespace Orbit;

public interface IOrbitHost
{
    ConfigStore Cfg { get; }
    bool VrConnected { get; }
    string VrStatus { get; }
    string InputStatus { get; }
    string GpuName { get; }
    IReadOnlyList<MonitorInfo> MonitorList { get; }
    PanelData Data { get; }
    WristPanel Wrist { get; }
    KeyboardPanel Keyboard { get; }
    void Recenter();
    void ResetDesktop(DesktopConfig d);
    void ResetWrist();
    void ResetKeyboard();
    void SetDesktopAttach(DesktopConfig d, string attach);
    void SetKeyboardAttach(string attach);
    void SetDesktopEnabled(DesktopConfig d, bool on);
    void SetAutoStart(bool on);
    string AutoStartStatus { get; }
    void ToggleKeyboard();
    void ToggleDesktops();
    void OpenConfigFolder();
    int GroupSize(DesktopConfig d);
    void JoinAllDesktops();
    void SeparateDesktops();
    void FitCurve(DesktopConfig d);
    void DesktopEdited(DesktopConfig d);
    void AppearanceChanged();
    bool IsElevated { get; }
    void SetRunAsAdmin(bool on);
    void RestartElevated();
    string NotificationStatus { get; }
    string StatusLinePreview { get; }      // what the chatbox status line would say now
    string RoomStatus { get; }             // VRChat log / VRCX found?
    string EyeStatus { get; }              // Virtual Desktop's eye tracking: found, tracking?
    void ToggleRoom();
    void TheaterEdited();          // theater settings changed in the window: a screen that plays follows at once
    IReadOnlyList<WhatsNew.Entry> WhatsNewList { get; }    // after an update until OK: what changed (empty = nothing)
    string WhatsNewSub { get; }
    void DismissWhatsNew();
    void TestNotification();
    Updater Updates { get; }
    void SwitchLayout(string name);
    void ForgetLayout(string name);
    IReadOnlyList<WindowInfo> OpenWindows();
    bool IsMirrored(IntPtr hwnd);
    WindowConfig AddWindow(WindowInfo w);
    void RemoveWindow(WindowConfig w);
    string WindowStatus(WindowConfig w);
    void ResetWindow(WindowConfig w);
    void SetWindowAttach(WindowConfig w, string attach);
    void SetWindowEnabled(WindowConfig w, bool on);
    // 1.14: the SteamVR app running now (Layouts > Per game offers it); a card in VR (an in-VR export says where the file went)
    (string key, string name) RunningApp => (null, null);
    void Notify(string title, string text) { }
}

// The settings screen. Same code draws the desktop window and the SteamVR dashboard tab.
// Logical canvas: 1000 x 640.
public sealed partial class SettingsView
{
    public const float W = 1000, H = 640;
    static readonly string[] Tabs = { "Desktops", "Windows", "Layouts", "Wrist", "Keyboard", "Look", "VRChat", "General" };
    static readonly string[] TabIcons = { Theme.IcoMonitor, Theme.IcoWindow, Theme.IcoLayouts, Theme.IcoWatch, Theme.IcoKeyboard, Theme.IcoColor, "\uE8BD", Theme.IcoSettings };
    public const int TabDesktops = 0, TabWindows = 1, TabLayouts = 2, TabWrist = 3, TabKeyboard = 4, TabLook = 5, TabVrchat = 6, TabGeneral = 7;
    public static int TabCount => Tabs.Length;
    static readonly string[] AttachIds = { "world", "left", "right", "head" };
    static readonly string[] AttachNames = { "World", "Left hand", "Right hand", "Head" };

    readonly IOrbitHost _h;
    public int Tab;
    string _selected;    // desktop id

    public Action OnClose;   // set when shown as the in-VR panel: draws a close button

    public SettingsView(IOrbitHost host) { _h = host; }

    public bool ShowsWristPreview => Tab == TabWrist || (Tab == TabLook && Theme.Cyber);     // Cyber's Look shows the watch
    public bool ShowsKeyboardPreview => Tab == TabKeyboard;

    Config C => _h.Cfg.Data;

    // "What's new": after an update (until OK), or opened from General
    bool _whatsNewManual;
    float _wnScroll;
    IReadOnlyList<WhatsNew.Entry> WhatsNewShown => _whatsNewManual ? WhatsNew.Latest(4) : _h.WhatsNewList;
    string WhatsNewSubShown => _whatsNewManual ? Lang.F("OrbitOverlay {0} and the versions before it", Updater.CurrentText) : WhatsNewSub(_h.WhatsNewSub);

    // "Updated to 1.13.1 from 1.8.2" (OrbitApp says it in English) in the language picked
    static string WhatsNewSub(string s)
    {
        if (Lang.English || string.IsNullOrEmpty(s) || !s.StartsWith("Updated to ")) return s;
        int from = s.IndexOf(" from ", StringComparison.Ordinal);
        return from < 0 ? s : Lang.F("Updated to {0} from {1}", s[11..from], s[(from + 6)..]);
    }

    // a mirrored window's state (WindowPanel / OrbitApp say it in English) in the language picked
    static string WinStatus(string st)
    {
        if (Lang.English || string.IsNullOrEmpty(st)) return st;
        if (st.StartsWith("live · ", StringComparison.Ordinal)) return Lang.F("live · {0}", st[7..]);
        if (st.StartsWith("can't capture it: ", StringComparison.Ordinal)) return Lang.F("can't capture it: {0}", st[18..]);
        return Lang.T(st);
    }

    void CloseWhatsNew()
    {
        if (_whatsNewManual) _whatsNewManual = false;
        else _h.DismissWhatsNew();
        _wnScroll = 0;
    }

    // ---- drawn again only when something shows differently ----
    // The VR panel and the desktop window drew the page twice a second whatever it showed, for the values that change by themselves
    // (the link, an update's progress, the clock in the status line's preview): 0.8 ms and a 5.8 MB texture to SteamVR each time.
    // Now they ask this on that beat: a hash of everything the open page shows that moves without a click - and draw only when it
    // differs from the last frame's (Erik 10-18: "it must be optimized, get the maximum out of it"). A page with a live preview (the
    // watch, the keyboard) or a list read from Windows (the windows to mirror) is never the same twice: those keep the old beat.
    // What a click changes is the panel's own business (its dirty flag), what a style animates is AnimWake's.
    int _liveTick;

    public int LiveSig()
    {
        var h = new HashCode();
        h.Add(Theme.Version); h.Add(Lang.Version); h.Add(Tab); h.Add(_h.Cfg.Changes); h.Add(_h.Cfg.Dirty);
        h.Add(_h.VrConnected); h.Add(_h.VrStatus); h.Add(_h.InputStatus);
        // the header's buttons and the layout in use: flipped from the watch or by the app itself (the keyboard hiding on its own)
        h.Add(C.Keyboard.Visible); h.Add(C.General.DesktopsVisible); h.Add(C.ActiveLayout);
        h.Add(WhatsNewShown?.Count ?? 0);                   // the host's card comes by itself (an update)
        if (Theme.Cyber) { h.Add(CyberLiveLine()); h.Add(CyberVrTime()); }
        if (ShowsWristPreview || ShowsKeyboardPreview || Tab == TabWindows) h.Add(++_liveTick);
        switch (Tab)
        {
            case TabGeneral:
                h.Add(_h.AutoStartStatus); h.Add(_h.IsElevated); h.Add(_h.NotificationStatus); h.Add(_h.GpuName);
                foreach (var m in _h.MonitorList) h.Add(m.Name);
                var (us, umsg, uprog, ulatest) = _h.Updates.Snapshot();
                h.Add(us); h.Add(umsg); h.Add(uprog); h.Add(ulatest); h.Add(_h.Updates.Busy);
                break;
            case TabDesktops:
                // where and how big too: the page draws the map and "2560×1440 · primary" from them, and a monitor whose mode changes
                // (a game switching the desktop's resolution) keeps its Id
                foreach (var m in _h.MonitorList)
                {
                    h.Add(m.Id); h.Add(m.X); h.Add(m.Y); h.Add(m.W); h.Add(m.H); h.Add(m.Primary);
                    h.Add(m.Name); h.Add(m.Device); h.Add(m.Virtual);
                }
                break;
            case TabLayouts:
                h.Add(_h.RunningApp.key);                   // Per game offers the app running now (RuleChoices): it starts and stops by itself
                break;
            case TabVrchat:
                h.Add(_h.StatusLinePreview); h.Add(_h.RoomStatus);
                break;
        }
        return h.ToHashCode();
    }

    public void Draw(Ui ui)
    {
        var p = ui.P;
        var wn = WhatsNewShown;
        bool modal = wn != null && wn.Count > 0;
        if (Tab != TabLook || modal) _fontsOpen = _stylesOpen = false;
        ui.Inert = modal || _fontsOpen || _stylesOpen;       // an open font / style list: the page under it is only a picture
        // Cyber, the VR panel coming / going: its HUD reveal (Gfx/CyberReveal.cs); the console at rest kept as a picture (SettingsCyber.cs)
        if (Theme.Cyber) CyberKeep(ui);
        var cyRev = Theme.Cyber && OnClose != null ? CyberReveal.Begin(p, new RectangleF(0, 0, W, H), ShownAt, Vis, Closing) : default;
        if (Theme.Cyber)
        {
            // the HUD console's frame, cards and grid (SettingsCyber.cs), the page's label
            if (_cyKept) p.Image(_cyKeep.Bitmap, new RectangleF(0, 0, W, H));
            else CyberGround(ui);
            CyberPageLabel(ui);
            ui.HudRight = CyPage.Right - 8;
        }
        else p.Fill(new RectangleF(0, 0, W, H), Theme.Bg);
        DrawHeader(ui);
        if (Theme.Classic) DrawNavClassic(ui);
        else if (Theme.Cyber) DrawNavCyber(ui);
        else DrawNav(ui);

        // a tab picked in the list fades in, rising a few px (DrawNav starts it; Motion off: at once)
        float tin = ui.Anim(TabIn, 1, 18);
        bool cyScan = Theme.Cyber && CyberScanBegin(ui);     // Cyber (Full): a scan line reveals it instead
        bool fading = tin < 0.999f && !(Theme.Cyber && CyberFx.Full);
        if (fading) { p.PushFade(tin); p.PushOffset(0, 10 * (1 - tin)); }
        switch (Tab)
        {
            case TabDesktops: DrawDesktops(ui); break;
            case TabWindows: DrawWindows(ui); break;
            case TabLayouts: DrawLayouts(ui); break;
            case TabWrist: DrawWrist(ui); break;
            case TabKeyboard: DrawKeyboard(ui); break;
            case TabLook: DrawLook(ui); break;
            case TabVrchat: DrawVrchat(ui); break;
            default: DrawGeneral(ui); break;
        }
        if (fading) { p.PopScale(); p.PopFade(); }
        if (cyScan) CyberScanEnd(ui);
        if (_fontsOpen) { ui.Inert = false; DrawFontList(ui); }
        if (_stylesOpen) { ui.Inert = false; DrawStyleList(ui); }
        // What's new over the page: the dark veil and the sheet fade in, the sheet growing from 96 %
        float wa = ui.Anim("wn#in", modal ? 1 : 0, 16);
        if (modal)
        {
            ui.Inert = false;
            p.Fill(new RectangleF(0, 0, W, H), Theme.WithAlpha(0x99000000, wa));
            bool grow = wa < 0.999f;
            if (grow) { p.PushFade(wa); p.PushScale(0.96f + 0.04f * wa, 500, 320); }
            if (WhatsNewView.DrawModern(ui, new RectangleF(160, 40, 680, 560), wn, WhatsNewSubShown, ref _wnScroll, Theme.Panel)) CloseWhatsNew();
            if (grow) { p.PopScale(); p.PopFade(); }
        }
        if (cyRev.Kind != 0) CyberReveal.End(p, cyRev);
        if (ui.Changed) _h.Cfg.MarkDirty();
    }

    // the 95 skin: the tabs as a white list box, the chosen one in navy
    void DrawNavClassic(Ui ui)
    {
        var p = ui.P;
        var box = new RectangleF(14, 80, 184, Tabs.Length * 40 + 8);
        p.Fill(box, Theme.W95Window);
        p.Bevel(box, Painter.Edge.Sunken);
        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = new RectangleF(box.X + 4, box.Y + 4 + i * 40, box.Width - 8, 40);
            if (ui.Hover(r) && ui.Pressed) Tab = i;
            bool on = Tab == i;
            if (on) p.Fill(r, Theme.W95Navy);
            uint fg = on ? 0xFFFFFFFF : Theme.W95Dark;
            p.Icon(TabIcons[i], new RectangleF(r.X + 10, r.Y, 24, r.Height), fg, 17);
            p.Text(Tabs[i], new RectangleF(r.X + 44, r.Y, r.Width - 48, r.Height), fg, 15);
        }
        p.Fill(new RectangleF(212, 76, p.Px, H - 92), Theme.W95Shadow);
        p.Fill(new RectangleF(212 + p.Px, 76, p.Px, H - 92), Theme.W95Hi);
    }

    const string TabIn = "tab#in";

    void DrawNav(Ui ui)
    {
        var p = ui.P;
        // nav: the selection pill slides to the chosen tab
        float ny = ui.Anim("nav", Tab, 14);
        var pill = new RectangleF(16, 84 + ny * 50, 180, 42);
        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = new RectangleF(16, 84 + i * 50, 180, 42);
            float hv = ui.Anim(Ui.Id("nav/", i), ui.Hover(r) && Tab != i ? 1 : 0, 18);
            if (hv > 0) p.Fill(r, Theme.WithAlpha(Theme.Panel, hv), 10);
            if (ui.Hover(r) && ui.Pressed && Tab != i) { Tab = i; ui.AnimFrom(TabIn, 0); }      // the page fades in (Draw)
        }
        p.Fill(pill, Theme.Panel3, 10);
        p.Fill(new RectangleF(pill.X, pill.Y + 10, 4, pill.Height - 20), Theme.Accent, 2);
        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = new RectangleF(16, 84 + i * 50, 180, 42);
            float on = Math.Clamp(1 - MathF.Abs(ny - i), 0, 1);
            p.Icon(TabIcons[i], new RectangleF(r.X + 14, r.Y, 24, r.Height), Theme.Mix(Theme.Dim, Theme.Accent, on), 18);
            p.Text(Tabs[i], new RectangleF(r.X + 50, r.Y, r.Width - 50, r.Height), Theme.Mix(Theme.Dim, Theme.Text, on), 16,
                on > 0.5f ? FontWeight.SemiBold : FontWeight.Normal);
        }
        p.Line(212, 76, 212, H - 16, Theme.Border, 1);
    }

    void DrawHeader(Ui ui)
    {
        var p = ui.P;
        if (Theme.Classic) { DrawHeaderClassic(ui); return; }
        if (Theme.Cyber) { DrawHeaderCyber(ui); return; }
        p.Fill(new RectangleF(0, 0, W, 64), Theme.Panel);
        p.Line(0, 64, W, 64, Theme.Border, 1);
        bool winBtns = OnClose == null && OnClose95 != null;      // the desktop window: no Windows frame, its own minimise / close
        if (winBtns) p.Stroke(new RectangleF(0.5f, 0.5f, W - 1, H - 1), 0x33FFFFFF, 1, 8);     // its edge, round like Windows 11's
        p.Logo(new RectangleF(22, 15, 34, 34));
        p.Text("OrbitOverlay", new RectangleF(64, 0, 180, 64), Theme.Text, 22, FontWeight.SemiBold);
        bool on = _h.VrConnected;
        float px = 64 + p.MeasureWidth("OrbitOverlay", 22, FontWeight.SemiBold) + 16;       // the pill right after the name
        var pill = new RectangleF(px, 19, MathF.Min(Math.Max(190, ui.P.MeasureWidth(_h.VrStatus, 13) + 36), (winBtns ? 404 : 500) - px), 26);
        p.Fill(pill, on ? 0x2234D399u : 0x22FFFFFFu, 13);
        p.Circle(pill.X + 14, pill.Y + 13, 4.5f, on ? Theme.Good : Theme.Faint);
        p.Text(_h.VrStatus, new RectangleF(pill.X + 26, pill.Y, pill.Width - 30, pill.Height), on ? Theme.Text : Theme.Dim, 13);

        float x = W - 16;
        if (OnClose != null)
        {
            x -= 40;
            if (ui.Button("hdr.close", new RectangleF(x, 14, 40, 36), null, "", false, true, 14))
                OnClose();
            x -= 8;
        }
        else if (winBtns)
        {
            x -= 40;
            if (ui.Button("hdr.wclose", new RectangleF(x, 14, 40, 36), null, "\uE8BB", false, true, 12)) OnClose95();
            x -= 48;
            if (ui.Button("hdr.wmin", new RectangleF(x, 14, 40, 36), null, "\uE921", false, true, 12)) OnMinimize?.Invoke();
            x -= 8;
        }
        x -= 132;
        if (ui.Button("hdr.desk", new RectangleF(x, 14, 132, 36), "Desktops",
                C.General.DesktopsVisible ? Theme.IcoShow : Theme.IcoHide, C.General.DesktopsVisible, true, 14))
            _h.ToggleDesktops();
        x -= 140;
        if (ui.Button("hdr.kb", new RectangleF(x, 14, 132, 36), "Keyboard", Theme.IcoKeyboard, C.Keyboard.Visible, true, 14))
            _h.ToggleKeyboard();
        x -= 140;
        if (ui.Button("hdr.rc", new RectangleF(x, 14, 132, 36), "Recenter", Theme.IcoRecenter, false, _h.VrConnected, 14))
            _h.Recenter();
    }

    // the 95 skin: in VR a navy caption with the close box (the desktop window has the real one),
    // then a toolbar - the SteamVR state in a sunken status field and the three buttons
    void DrawHeaderClassic(Ui ui)
    {
        var p = ui.P;
        bool caption = OnClose != null;
        float by = 18;
        if (caption)
        {
            var cap = new RectangleF(4, 4, W - 8, 24);
            p.Caption(cap, "OrbitOverlay Settings", 13);
            var close = new RectangleF(cap.Right - 22, cap.Y + 3, 19, 18);
            if (ui.Button("hdr.close", close, null, "\uE8BB", false, true, 7)) OnClose();
            by = 33;
        }
        const float bh = 28;
        bool on = _h.VrConnected;
        var st = new RectangleF(16, by, Math.Max(190, ui.P.MeasureWidth(_h.VrStatus, 13) + 36), bh);
        p.Bevel(st, Painter.Edge.Sunken);
        p.Fill(new RectangleF(st.X + 10, st.Y + 10, 8, 8), on ? 0xFF00A000 : Theme.W95Shadow);
        p.Text(_h.VrStatus, new RectangleF(st.X + 26, st.Y, st.Width - 30, st.Height), Theme.W95Dark, 13);
        float x = W - 16 - 132;
        if (ui.Button("hdr.desk", new RectangleF(x, by, 132, bh), "Desktops",
                C.General.DesktopsVisible ? Theme.IcoShow : Theme.IcoHide, C.General.DesktopsVisible, true, 14))
            _h.ToggleDesktops();
        x -= 140;
        if (ui.Button("hdr.kb", new RectangleF(x, by, 132, bh), "Keyboard", Theme.IcoKeyboard, C.Keyboard.Visible, true, 14))
            _h.ToggleKeyboard();
        x -= 140;
        if (ui.Button("hdr.rc", new RectangleF(x, by, 132, bh), "Recenter", Theme.IcoRecenter, false, _h.VrConnected, 14))
            _h.Recenter();
        p.EtchedLine(4, W - 4, 68);
    }

    // ---------------- Desktops ----------------
    void DrawDesktops(Ui ui)
    {
        var p = ui.P;
        var mons = _h.MonitorList;
        ui.Section(new RectangleF(232, 80, 400, 22), "Monitors");
        // (10-18: the map 26 px lower than it was, the gaze focus switch and its slider under Arrangement; the row hint moved left)
        var map = new RectangleF(232, 104, 752, 92);
        p.Fill(map, Theme.Panel, 12);
        if (mons.Count == 0) { p.Text("No monitors found", map, Theme.Dim, 15, FontWeight.Normal, TextAlignment.Center); return; }
        if (_selected == null || !mons.Any(m => m.Id == _selected)) _selected = mons.FirstOrDefault(m => !m.Virtual)?.Id ?? mons[0].Id;

        int minX = mons.Min(m => m.X), minY = mons.Min(m => m.Y), maxX = mons.Max(m => m.X + m.W), maxY = mons.Max(m => m.Y + m.H);
        float s = Math.Min((map.Width - 40) / (maxX - minX), (map.Height - 24) / (maxY - minY));
        float ox = map.X + (map.Width - (maxX - minX) * s) / 2, oy = map.Y + (map.Height - (maxY - minY) * s) / 2;
        int n = 1;
        foreach (var m in mons)
        {
            var dc = C.Desktops.FirstOrDefault(d => d.Id == m.Id);
            var r = new RectangleF(ox + (m.X - minX) * s + 3, oy + (m.Y - minY) * s + 3, m.W * s - 6, m.H * s - 6);
            bool sel = m.Id == _selected;
            bool en = dc?.Enabled ?? false;
            bool hover = ui.Hover(r);
            uint fill = en ? Theme.Mix(Theme.Panel2, Theme.Accent, hover ? 0.28f : 0.2f) : (hover ? Theme.Panel3 : Theme.Panel2);
            p.Fill(r, fill, 8);
            p.Stroke(r, sel ? Theme.Accent : Theme.Border, sel ? 2.5f : 1, 8);
            p.Text(n.ToString(), new RectangleF(r.X, r.Y + 6, r.Width, r.Height * 0.5f), en ? Theme.Text : Theme.Dim, 22, FontWeight.SemiBold, TextAlignment.Center);
            p.Text(m.Virtual ? Lang.F("{0} (virtual)", m.Name) : m.Name, new RectangleF(r.X + 4, r.Y + r.Height * 0.5f, r.Width - 8, r.Height * 0.4f),
                Theme.Dim, 12, FontWeight.Normal, TextAlignment.Center);
            if (dc != null && dc.Group != null && _h.GroupSize(dc) > 1)
                p.Icon(Theme.IcoLink, new RectangleF(r.Right - 26, r.Y + 4, 22, 22), Theme.Accent, 13);
            if (hover && ui.Pressed) _selected = m.Id;
            n++;
        }

        var mon = mons.First(m => m.Id == _selected);
        var cfg = C.Desktops.FirstOrDefault(d => d.Id == mon.Id);
        if (cfg == null) return;
        int gs = _h.GroupSize(cfg);
        p.Text($"{mon.Name}", new RectangleF(232, 206, 420, 30), Theme.Text, 19, FontWeight.SemiBold);
        p.Text($"{mon.W}×{mon.H}  ·  {mon.Device.Replace(@"\\.\", "")}{(mon.Primary ? "  ·  " + Lang.T("primary") : "")}{(gs > 1 ? "  ·  " + Lang.F("joined row of {0}", gs) : "")}",
            new RectangleF(232, 234, 460, 22), Theme.Dim, 13);
        bool en2 = cfg.Enabled;
        if (ui.Toggle("d.en", new RectangleF(714, 208, 270, 46), "Show in VR", ref en2))
            _h.SetDesktopEnabled(cfg, en2);

        float lx = 222, rx = 608, cw = 376;
        float y = 266;
        float v = cfg.Width;
        if (ui.Slider("d.w", new RectangleF(lx, y, cw, 58), "Size", ref v, 0.3f, 3f, f => Lang.F("{0:0.00} m wide", f), cfg.Enabled))
        {
            cfg.Width = v;
            _h.DesktopEdited(cfg);
        }
        v = cfg.Opacity;
        if (ui.Slider("d.o", new RectangleF(lx, y + 62, cw, 58), "Opacity", ref v, 0.2f, 1f, f => $"{f * 100:0}%", cfg.Enabled)) cfg.Opacity = v;
        v = cfg.Curvature;
        float width = cfg.Width;
        if (ui.Slider("d.c", new RectangleF(lx, y + 124, cw, 58), "Curvature", ref v, 0f, 0.5f,
                f => f < 0.005f ? "flat" : Lang.F("bend radius {0:0.00} m", width / (2 * MathF.PI * f)), cfg.Enabled))
        {
            cfg.Curvature = v < 0.005f ? 0 : v;
            _h.DesktopEdited(cfg);
        }

        ui.Label(new RectangleF(rx + 10, y, cw, 22), "Attached to");
        int ai = Math.Max(0, Array.IndexOf(AttachIds, cfg.Place.Attach ?? "world"));
        if (ui.Segmented("d.att", new RectangleF(rx + 10, y + 24, cw - 20, 38), AttachNames, ref ai, cfg.Enabled))
            _h.SetDesktopAttach(cfg, AttachIds[ai]);
        bool mouse = cfg.Mouse;
        if (ui.Toggle("d.m", new RectangleF(rx, y + 70, cw, 52), "Mouse control", ref mouse, "laser moves the real cursor, trigger clicks", cfg.Enabled))
            cfg.Mouse = mouse;
        bool live = _h.VrConnected && cfg.Enabled;
        if (ui.Button("d.reset", new RectangleF(rx + 10, y + 134, 168, 38), "Reset position", Theme.IcoRecenter, false, live, 14))
            _h.ResetDesktop(cfg);
        if (ui.Button("d.fit", new RectangleF(rx + 188, y + 134, 178, 38), "Curve around me", null, false, live && (cfg.Place.Attach ?? "world") == "world", 14))
            _h.FitCurve(cfg);

        p.Line(232, 472, 984, 472, Theme.Border, 1);
        float fps = C.General.CaptureFps;
        bool smart = C.General.SmartCapture;
        if (ui.Slider("g.fps", new RectangleF(lx, 480, cw, 58), smart ? "Capture rate (smart is on)" : "Capture rate", ref fps, 15, 90,
                f => $"{Math.Round(f):0} fps", !smart))
            C.General.CaptureFps = (int)Math.Round(fps);
        if (ui.Toggle("g.smart", new RectangleF(lx, 540, cw, 54), "Smart frame rate", ref smart,
                "where you look or point: headset rate · edge 15 · behind 4 fps"))
            C.General.SmartCapture = smart;
        ui.Label(new RectangleF(lx + 10, 598, cw - 10, 34),
            "Drop a monitor next to another one and it snaps into the row. Hold B / Y while grabbing to pull one out.",
            Theme.Dim, 12, FontWeight.Normal, true);
        ui.Section(new RectangleF(rx + 10, 480, cw, 20), "Arrangement");
        if (ui.Button("d.join", new RectangleF(rx + 10, 502, 236, 36), "Join into one curved screen", Theme.IcoLink, true, _h.VrConnected, 14))
            _h.JoinAllDesktops();
        if (ui.Button("d.sep", new RectangleF(rx + 256, 502, 110, 36), "Separate", null, false, true, 14))
            _h.SeparateDesktops();
        DrawFocus(ui, rx, cw, 542);
    }

    // gaze focus (Panels/GazeFocus.cs, Erik 10-18): the monitor you look at full size, the others of its row smaller beside it
    void DrawFocus(Ui ui, float rx, float cw, float y)
    {
        bool on = C.Focus?.Enabled ?? false;
        if (ui.Toggle("d.focus", new RectangleF(rx, y, cw, 46), "Focus the monitor you look at", ref on, "look at one and the others shrink beside it"))
            (C.Focus ??= new FocusConfig()).Enabled = on;
        float v = C.Focus?.Others ?? GazeFocus.OthersDefault;
        if (ui.Slider("d.others", new RectangleF(rx, y + 48, cw - 156, 44), "Others", ref v, GazeFocus.OthersMin, GazeFocus.OthersMax, f => $"{f * 100:0}%", on))
            (C.Focus ??= new FocusConfig()).Others = v;
        // a laser on a monitor focuses it too - off: only the look (Erik 10-18: with eye tracking the controllers need not)
        bool las = C.Focus?.Laser ?? true;
        if (ui.Toggle("d.flaser", new RectangleF(rx + cw - 150, y + 48, 150, 44), "Laser too", ref las, null, on)) (C.Focus ??= new FocusConfig()).Laser = las;
    }

    // ---------------- Wrist ----------------
    // ---------------- Layouts ----------------
    string _selLayout;

    string LayoutSummary(LayoutProfile l)
    {
        int mons, wins;
        bool night;
        // only monitors that are connected and windows still mirrored count
        bool Monitor(string id) => _h.MonitorList.Any(m => m.Id == id);
        bool Window(string id) => C.Windows.Any(w => w.Id == id);
        if (l.Name == C.ActiveLayout)           // the one in use = what is around you right now
        {
            mons = C.Desktops.Count(d => d.Enabled && Monitor(d.Id));
            wins = C.Windows.Count(w => w.Enabled);
            night = C.Appearance.NightMode;
        }
        else if (l.Saved == null) return "not used yet";
        else
        {
            mons = l.Panels.Count(s => s.Kind == "desktop" && s.Enabled && Monitor(s.Id));
            wins = l.Panels.Count(s => s.Kind == "window" && s.Enabled && Window(s.Id));
            night = l.NightMode;
        }
        var parts = new List<string> { mons == 1 ? Lang.T("1 monitor") : Lang.F("{0} monitors", mons) };
        if (wins > 0) parts.Add(wins == 1 ? Lang.T("1 window") : Lang.F("{0} windows", wins));
        if (night) parts.Add(Lang.T("night mode"));
        return string.Join("  ·  ", parts);
    }

    void DrawLayouts(Ui ui)
    {
        var p = ui.P;
        var list = C.Layouts;
        float lx = 232, lw = 352;
        if (_selLayout == null || !list.Any(l => l.Name == _selLayout)) _selLayout = C.ActiveLayout;
        ui.Section(new RectangleF(lx, 80, lw, 22), "Layout profiles");
        float y = 106;
        foreach (var l in list)
        {
            var r = new RectangleF(lx, y, lw, 72);
            bool active = l.Name == C.ActiveLayout;
            if (ui.Card(Ui.Id("lay.", l.Name), r, l.Name == _selLayout, out _)) _selLayout = l.Name;
            p.Text(l.Name, new RectangleF(r.X + 18, r.Y + 8, 200, 28), Theme.Text, 17, FontWeight.SemiBold);
            if (active)
            {
                var pill = new RectangleF(r.Right - 88, r.Y + 12, 74, 22);
                p.Fill(pill, Theme.Mix(Theme.Panel2, Theme.Accent, 0.25f), 11);
                p.Text("IN USE", pill, Theme.Accent, 11, FontWeight.SemiBold, TextAlignment.Center);
            }
            p.Text(LayoutSummary(l), new RectangleF(r.X + 18, r.Y + 38, lw - 30, 24), Theme.Dim, 12);
            y += 82;
        }
        ui.Label(new RectangleF(lx, y + 4, lw, 120),
            "Each profile remembers where your monitors, windows and keyboard are, their size and curve, and night mode. " +
            "Whatever you change is kept in the profile you are in when you switch away.",
            Theme.Dim, 13, FontWeight.Normal, true);

        float wx = 602, cw = 382;
        if (_rulesOpen) { DrawRules(ui, wx, cw); DrawTheater(ui, lx, wx, cw); return; }      // Per game, in place of the profile (Extras)
        var sel = list.First(l => l.Name == _selLayout);
        bool isActive = sel.Name == C.ActiveLayout;
        p.Text(sel.Name, new RectangleF(wx + 10, 78, cw - 10, 30), Theme.Text, 20, FontWeight.SemiBold);
        p.Text(isActive ? "in use now" : sel.Saved is { } t ? Lang.F("last used {0:MMM d, HH:mm}", t) : "not used yet",
            new RectangleF(wx + 10, 106, cw - 10, 20), Theme.Dim, 13);
        if (ui.Button("lay.use", new RectangleF(wx + 10, 140, cw - 10, 46), isActive ? "In use" : Lang.F("Switch to {0}", Lang.T(sel.Name)),
                isActive ? Theme.IcoCheck : Theme.IcoLayouts, !isActive, !isActive, 15))
            _h.SwitchLayout(sel.Name);
        bool onWrist = sel.OnWrist;
        if (ui.Toggle("lay.wrist", new RectangleF(wx, 198, cw, 54), "On the watch", ref onWrist, "its button cycles through these profiles"))
            sel.OnWrist = onWrist;
        if (ui.Button("lay.forget", new RectangleF(wx + 10, 264, 210, 38), "Forget this layout", Theme.IcoDelete, false,
                sel.Saved != null && !isActive, 14))
            _h.ForgetLayout(sel.Name);
        ui.Label(new RectangleF(wx + 10, 318, cw - 10, 150), isActive
                ? "You are using this one now: move, resize and bend things as you like - the profile keeps it when you switch to another one."
                : sel.Saved == null
                ? "The first time you switch to it, everything comes to where you are looking - lying in bed, that is above you - and is remembered from then on."
                : "Switching puts every panel back where it was in this profile. Recenter still brings the whole arrangement in front of you.",
            Theme.Dim, 13, FontWeight.Normal, true);
        PerGameButton(ui, wx, cw, 394);
        DrawTheater(ui, lx, wx, cw);
    }

    // theater mode's settings (the same ones as on the bar under a screen in VR), the bar itself on / off, the Ambilight and Auto
    // theater (Erik 10-18)
    void DrawTheater(Ui ui, float lx, float wx, float cw)
    {
        var p = ui.P;
        var t = C.Theater;
        p.Line(lx, 440, 984, 440, Theme.Border, 1);
        ui.Section(new RectangleF(lx, 450, 352, 22), "Theater mode");
        // three switches on the left (a little closer together to fit), the two sliders and Auto theater on the right
        bool b = t.Bar;
        if (ui.Toggle("th.bar", new RectangleF(lx - 10, 472, 362, 48), "Bar under the monitors", ref b,
                "point at a monitor's bottom: Theater + settings")) t.Bar = b;
        b = t.Curved;
        if (ui.Toggle("th.curve", new RectangleF(lx - 10, 521, 362, 48), "Curved", ref b, "bent around you, else flat")) { t.Curved = b; _h.TheaterEdited(); }
        b = t.Ambilight;
        if (ui.Toggle("th.amb", new RectangleF(lx - 10, 570, 362, 48), "Ambilight", ref b, "a soft glow round the screen in its colours")) { t.Ambilight = b; _h.TheaterEdited(); }
        b = t.Auto;                     // under the sliders (the left column is full)
        if (ui.Toggle("th.auto", new RectangleF(wx, 592, cw, 46), "Auto theater", ref b, "starts when a video goes full screen")) t.Auto = b;
        float v = t.Dim;
        if (ui.Slider("th.dim", new RectangleF(wx, 470, cw, 58), "Dim the world", ref v, 0, 0.9f, f => f < 0.005f ? "off" : $"{f * 100:0}%"))
        { t.Dim = v; _h.TheaterEdited(); }
        v = t.Width;
        if (ui.Slider("th.size", new RectangleF(wx, 532, cw, 58), "Screen size", ref v, Theater.MinWidth, Theater.MaxWidth, f => Lang.F("{0:0.0} m wide", f)))
        { t.Width = v; _h.TheaterEdited(); }
    }

    // ---------------- Windows ----------------
    const int MaxWindows = 6;
    string _selWin;          // WindowConfig id
    public bool Picking;     // right side lists the open windows
    IReadOnlyList<WindowInfo> _pickOpen;
    int _pickPage;

    void DrawWindows(Ui ui)
    {
        var p = ui.P;
        var list = C.Windows;
        float lx = 232, lw = 352;
        if (_selWin != null && !list.Any(w => w.Id == _selWin)) _selWin = null;
        if (_selWin == null && list.Count > 0) _selWin = list[0].Id;

        ui.Section(new RectangleF(lx, 80, lw, 22), "Mirrored windows");
        float y = 106;
        foreach (var wc in list)
        {
            var r = new RectangleF(lx, y, lw, 52);
            if (ui.Card(Ui.Id("win.", wc.Id), r, wc.Id == _selWin && !Picking, out _)) { _selWin = wc.Id; Picking = false; }
            string st = _h.WindowStatus(wc);
            bool live = st.StartsWith("live");
            st = WinStatus(st);
            p.Circle(r.X + 20, r.Y + 26, 5, live ? Theme.Good : wc.Enabled ? Theme.Warn : Theme.Faint);
            p.Text(Fit(p, string.IsNullOrEmpty(wc.Title) ? wc.Process : wc.Title, lw - 50, 15, FontWeight.SemiBold),
                new RectangleF(r.X + 36, r.Y + 5, lw - 44, 24), wc.Enabled ? Theme.Text : Theme.Dim, 15, FontWeight.SemiBold);
            p.Text(Fit(p, $"{wc.Process}  ·  {st}", lw - 50, 12), new RectangleF(r.X + 36, r.Y + 28, lw - 44, 18), Theme.Dim, 12);
            y += 60;
        }
        if (ui.Button("win.add", new RectangleF(lx, y + 2, 196, 40), "Add a window", Theme.IcoAdd, Picking || list.Count == 0, list.Count < MaxWindows, 14))
        {
            Picking = true;
            _pickPage = 0;
        }
        bool pop = C.General.PopOut;      // a window dragged off a monitor into the air becomes one of these (OrbitApp.PopOut.cs)
        if (ui.Toggle("win.pop", new RectangleF(lx, y + 50, lw, 54), "Pull windows off a monitor", ref pop, "drag one by its title bar out into the air"))
            C.General.PopOut = pop;
        ui.Label(new RectangleF(lx, y + 112, lw, 80), list.Count == 0
                ? "Mirror one app window instead of a whole monitor: a browser, Discord, a video player... It follows the window when it moves or resizes on your PC."
                : "Clicking a mirrored window brings it to the front on your PC, so the click lands in it. Grip moves it, the stick bends it, two hands resize it.",
            Theme.Dim, 13, FontWeight.Normal, true);

        if (Picking || list.Count == 0) DrawWindowPicker(ui, list.Count > 0);
        else DrawWindowSettings(ui, list.First(w => w.Id == _selWin));
    }

    void DrawWindowPicker(Ui ui, bool canCancel)
    {
        var p = ui.P;
        float rx = 612, rw = 372;
        // while pressing / releasing, the list that was drawn is used (it refreshes every second, and a
        // shifted list must not turn a click into a different window)
        var open = _pickOpen != null && (ui.Pressed || ui.Down || ui.Released || ui.Interacting) ? _pickOpen : (_pickOpen = _h.OpenWindows());
        const int per = 8;
        int pages = Math.Max(1, (open.Count + per - 1) / per);
        _pickPage = Math.Clamp(_pickPage, 0, pages - 1);
        ui.Section(new RectangleF(rx, 80, rw, 22), "Pick a window to mirror");
        if (open.Count == 0)
            p.Text("No windows open", new RectangleF(rx, 106, rw, 48), Theme.Dim, 14);
        for (int i = 0; i < per; i++)
        {
            int idx = _pickPage * per + i;
            if (idx >= open.Count) break;
            var w = open[idx];
            var r = new RectangleF(rx, 106 + i * 54, rw, 48);
            bool mirrored = _h.IsMirrored(w.Hwnd);
            if (ui.Card(Ui.Id("pick.", (int)(long)w.Hwnd), r, false, out _) && !mirrored && C.Windows.Count < MaxWindows)
            {
                _selWin = _h.AddWindow(w).Id;
                Picking = false;
            }
            p.Text(Fit(p, w.Title, rw - (mirrored ? 60 : 28), 14, FontWeight.SemiBold), new RectangleF(r.X + 14, r.Y + 4, rw - 28, 22),
                mirrored ? Theme.Dim : Theme.Text, 14, FontWeight.SemiBold);
            p.Text(mirrored ? w.Process + "  ·  " + Lang.T("already mirrored") : w.Process, new RectangleF(r.X + 14, r.Y + 26, rw - 28, 17), Theme.Dim, 12);
            if (mirrored) p.Icon(Theme.IcoCheck, new RectangleF(r.Right - 38, r.Y, 26, r.Height), Theme.Accent, 15);
        }
        float by = 106 + per * 54 + 2;
        if (pages > 1)
        {
            if (ui.Button("pick.prev", new RectangleF(rx, by, 44, 38), null, Theme.IcoLeft, false, _pickPage > 0, 13)) _pickPage--;
            p.Text($"{_pickPage + 1} / {pages}", new RectangleF(rx + 48, by, 64, 38), Theme.Dim, 13, FontWeight.Normal, TextAlignment.Center);
            if (ui.Button("pick.next", new RectangleF(rx + 116, by, 44, 38), null, Theme.IcoRight, false, _pickPage < pages - 1, 13)) _pickPage++;
        }
        if (canCancel && ui.Button("pick.cancel", new RectangleF(rx + rw - 110, by, 110, 38), "Cancel", null, false, true, 14))
            Picking = false;
    }

    void DrawWindowSettings(Ui ui, WindowConfig wc)
    {
        var p = ui.P;
        float wx = 602, cw = 382;
        p.Text(Fit(p, string.IsNullOrEmpty(wc.Title) ? wc.Process : wc.Title, cw - 10, 18, FontWeight.SemiBold),
            new RectangleF(wx + 10, 78, cw - 10, 28), Theme.Text, 18, FontWeight.SemiBold);
        p.Text(Fit(p, $"{wc.Process}  ·  {WinStatus(_h.WindowStatus(wc))}", cw - 10, 13), new RectangleF(wx + 10, 104, cw - 10, 20), Theme.Dim, 13);
        bool en = wc.Enabled;
        if (ui.Toggle("wn.en", new RectangleF(wx, 132, cw, 44), "Show in VR", ref en)) _h.SetWindowEnabled(wc, en);
        float v = wc.Width;
        if (ui.Slider("wn.w", new RectangleF(wx, 180, cw, 58), "Size", ref v, 0.15f, 3f, f => Lang.F("{0:0.00} m wide", f), wc.Enabled)) wc.Width = v;
        v = wc.Opacity;
        if (ui.Slider("wn.o", new RectangleF(wx, 240, cw, 58), "Opacity", ref v, 0.2f, 1f, f => $"{f * 100:0}%", wc.Enabled)) wc.Opacity = v;
        v = wc.Curvature;
        float width = wc.Width;
        if (ui.Slider("wn.c", new RectangleF(wx, 300, cw, 58), "Curvature", ref v, 0f, 0.5f,
                f => f < 0.005f ? "flat" : Lang.F("bend radius {0:0.00} m", width / (2 * MathF.PI * f)), wc.Enabled))
            wc.Curvature = v < 0.005f ? 0 : v;
        ui.Label(new RectangleF(wx + 10, 364, cw, 22), "Attached to");
        int ai = Math.Max(0, Array.IndexOf(AttachIds, wc.Place.Attach ?? "world"));
        if (ui.Segmented("wn.att", new RectangleF(wx + 10, 388, cw - 10, 38), AttachNames, ref ai, wc.Enabled))
            _h.SetWindowAttach(wc, AttachIds[ai]);
        bool mouse = wc.Mouse;
        if (ui.Toggle("wn.m", new RectangleF(wx, 434, cw, 52), "Mouse control", ref mouse, "laser moves the real cursor, trigger clicks", wc.Enabled))
            wc.Mouse = mouse;
        if (ui.Button("wn.reset", new RectangleF(wx + 10, 498, 168, 38), "Reset position", Theme.IcoRecenter, false, _h.VrConnected && wc.Enabled, 14))
            _h.ResetWindow(wc);
        if (ui.Button("wn.rm", new RectangleF(wx + 188, 498, cw - 188, 38), "Stop mirroring", Theme.IcoDelete, false, true, 14))
        {
            _h.RemoveWindow(wc);
            _selWin = null;
        }
    }

    // long window titles: cut with an ellipsis to fit
    readonly Dictionary<(string, float, float, FontWeight), string> _fit = new();
    int _fitLook = -1;

    string Fit(Painter p, string s, float maxW, float size, FontWeight w = FontWeight.Normal)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = Lang.T(s);                     // cut in the language picked
        if (_fitLook != Theme.Version) { _fit.Clear(); _fitLook = Theme.Version; }     // another font measures differently
        var key = (s, maxW, size, w);
        if (_fit.TryGetValue(key, out var r)) return r;
        if (p.MeasureWidth(s, size, w) <= maxW) r = s;
        else
        {
            int lo = 0, hi = s.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (p.MeasureWidth(s[..mid].TrimEnd() + "…", size, w) <= maxW) lo = mid; else hi = mid - 1;
            }
            if (lo > 0 && char.IsHighSurrogate(s[lo - 1])) lo--;           // don't split an emoji
            r = s[..lo].TrimEnd() + "…";
        }
        if (_fit.Count > 400) _fit.Clear();
        _fit[key] = r;
        return r;
    }

    void DrawWrist(Ui ui)
    {
        if (EditingWatch && C.Wrist.Face == "custom") { DrawWristEditor(ui); return; }
        var p = ui.P;
        var w = C.Wrist;
        bool custom = w.Face == "custom";
        float lx = 222, cw = 376, rx = 602;
        bool b = w.Enabled;
        if (ui.Toggle("w.en", new RectangleF(lx, 84, cw, 50), "Wrist watch", ref b)) w.Enabled = b;
        ui.Label(new RectangleF(lx + 10, 138, cw, 22), "Hand");
        int hi = w.Hand == "right" ? 1 : 0;
        if (ui.Segmented("w.hand", new RectangleF(lx + 10, 160, cw - 20, 36), HandNames, ref hi, w.Enabled))
        {
            w.Hand = hi == 1 ? "right" : "left";
            w.Offset = null;
        }
        b = w.AlwaysVisible;
        if (ui.Toggle("w.always", new RectangleF(lx, 204, cw, 52), "Always visible", ref b, "otherwise it fades in when you look at it", w.Enabled)) w.AlwaysVisible = b;
        float v = w.Width;
        if (ui.Slider("w.size", new RectangleF(lx, 262, cw, 58), "Size", ref v, 0.07f, 0.25f, f => $"{f * 100:0} cm", w.Enabled)) w.Width = v;
        b = w.ShowPerformance;
        if (ui.Toggle("w.perf", new RectangleF(lx, 326, cw, 46), "Frame timing & GPU", ref b, null, w.Enabled && !custom)) w.ShowPerformance = b;
        b = w.ShowBatteries;
        if (ui.Toggle("w.bat", new RectangleF(lx, 374, cw, 46), "Batteries", ref b, null, w.Enabled && !custom)) w.ShowBatteries = b;
        b = w.ShowMedia;
        if (ui.Toggle("w.media", new RectangleF(lx, 422, cw, 46), "Now playing", ref b, null, w.Enabled && !custom)) w.ShowMedia = b;
        b = w.Locked;
        if (ui.Toggle("w.lock", new RectangleF(lx, 470, cw, 54), "Lock position", ref b, "the grip won't move or resize the watch", w.Enabled))
            w.Locked = b;
        if (ui.Button("w.reset", new RectangleF(lx + 10, 534, 170, 38), "Reset position", Theme.IcoRecenter, false, w.Enabled, 14))
            _h.ResetWrist();
        // the Windows volume in VR (Sys/AudioMixer.cs): on = auto (Virtual Desktop's device), off = the apps keep their own; "on" (every
        // device that sets its own volume) stays a config value, shown as on here
        b = C.General.VolumeToApps != "off";
        if (ui.Toggle("w.vol", new RectangleF(lx, 578, cw, 52), "Windows volume in VR", ref b, "Virtual Desktop: the apps follow the Windows slider"))
            C.General.VolumeToApps = b ? null : "off";

        var prev = WristPreview(p, 92);
        var faces = FaceIds;
        ui.Label(new RectangleF(rx + 10, prev.Bottom + 16, cw, 22), "Watch face");
        int fi = Math.Max(0, Array.IndexOf(faces, w.Face ?? "full"));
        if (ui.Segmented("w.face", new RectangleF(rx + 10, prev.Bottom + 38, cw - 20, 36), FaceNames, ref fi, w.Enabled))
            ChooseFace(faces[fi]);
        b = C.Appearance.Clock24h;
        if (ui.Toggle("w.24h", new RectangleF(rx, prev.Bottom + 82, cw, 44), "24-hour clock", ref b, null, w.Enabled)) C.Appearance.Clock24h = b;
        WeatherRows(ui, rx + 10, prev.Bottom + 180, cw - 20);       // the weather widget's place and units (SettingsView.Weather.cs)
        if (custom)
        {
            if (_h.Wrist.Animating) ui.Animating = true;      // keep the springy preview moving
            // the custom face: its own widgets, set up in the layout editor
            if (ui.Button("w.edit", new RectangleF(rx + 10, prev.Bottom + 132, cw - 20, 38), "Edit layout", Theme.IcoLayouts, true, w.Enabled, 14))
                EditingWatch = true;
            return;
        }
        ui.Label(new RectangleF(rx + 10, prev.Bottom + 132, 120, 36), "Performance");
        int pg = w.PerfGraph ? 1 : 0;
        if (ui.Segmented("w.graph", new RectangleF(rx + 130, prev.Bottom + 132, cw - 140, 36), PerfNames, ref pg, w.Enabled && w.ShowPerformance))
            w.PerfGraph = pg == 1;
    }

    RectangleF WristPreview(Painter p, float y)
    {
        var prev = new RectangleF(612, y, 372, 372f * WristPanel.TH / WristPanel.TW);
        if (Theme.Cyber) CyberPreviewFrame(p, RectangleF.Inflate(prev, 10, 10));
        else p.Fill(RectangleF.Inflate(prev, 10, 10), Theme.Panel, 18);
        p.Image(_h.Wrist.Surf.Bitmap, prev);
        return prev;
    }

    // ---------------- Keyboard ----------------
    void DrawKeyboard(Ui ui)
    {
        var p = ui.P;
        var k = C.Keyboard;
        float lx = 222, rx = 608, cw = 376;
        bool b = k.Visible;
        if (ui.Toggle("k.vis", new RectangleF(lx, 84, cw, 50), "Show keyboard", ref b)) _h.ToggleKeyboard();
        float v = k.Width;
        if (ui.Slider("k.size", new RectangleF(lx, 144, cw, 58), "Size", ref v, 0.3f, 1.2f, f => Lang.F("{0:0} cm wide", f * 100))) k.Width = v;
        ui.Label(new RectangleF(rx + 10, 86, cw, 22), "Attached to");
        int ai = Math.Max(0, Array.IndexOf(AttachIds, k.Place.Attach ?? "world"));
        if (ai == 3) ai = 0;
        if (ui.Segmented("k.att", new RectangleF(rx + 10, 110, cw - 20, 38), AttachNames.Take(3).ToArray(), ref ai))
            _h.SetKeyboardAttach(AttachIds[ai]);
        if (ui.Button("k.reset", new RectangleF(rx + 10, 162, 170, 38), "Reset position", Theme.IcoRecenter, false, _h.VrConnected, 14))
            _h.ResetKeyboard();

        b = k.Sounds;
        if (ui.Toggle("k.snd", new RectangleF(lx, 210, cw, 50), "Key sounds", ref b)) k.Sounds = b;
        int si = k.SoundStyle == "muted" ? 1 : 0;
        if (ui.Segmented("k.style", new RectangleF(rx + 10, 216, 190, 38), SoundNames(), ref si, k.Sounds))
        {
            k.SoundStyle = si == 1 ? "muted" : "click";
            _h.Keyboard.PlayClick(false);
        }
        if (ui.Button("k.test", new RectangleF(rx + 210, 216, 156, 38), "Test sound", null, false, k.Sounds, 14))
            _h.Keyboard.PlayClick(false);
        v = k.SoundVolume;
        if (ui.Slider("k.vol", new RectangleF(lx, 264, cw, 58), "Volume", ref v, 0.05f, 1f, f => $"{f * 100:0}%", k.Sounds)) k.SoundVolume = v;
        if (k.Sounds && ui.Released && ui.Hover(new RectangleF(lx, 264, cw, 58))) _h.Keyboard.PlayClick(false);
        b = k.AutoShow;
        if (ui.Toggle("k.auto", new RectangleF(rx, 266, cw, 54), "Pop up for text fields", ref b, "when a click in VR lands in one; hides again after"))
            k.AutoShow = b;

        float pw = 540, ph = pw * KeyboardPanel.TH / KeyboardPanel.TW;      // leaves room for the line below
        var prev = new RectangleF(232 + (752 - pw) / 2, 336, pw, ph);
        if (Theme.Cyber) CyberPreviewFrame(p, RectangleF.Inflate(prev, 8, 8));
        else p.Fill(RectangleF.Inflate(prev, 8, 8), Theme.Panel, 16);
        p.Image(_h.Keyboard.Surf.Bitmap, prev);
        ui.Label(new RectangleF(232, prev.Bottom + 12, 752, 36),
            "Keys follow the active Windows layout. Shift / Ctrl / Alt / AltGr / Win: once = next key, twice = locked. " +
            "Typing goes to the focused window.", Theme.Dim, 12, FontWeight.Normal, true);
    }

    // ---------------- Look ----------------
    void DrawLook(Ui ui)
    {
        if (Theme.Cyber) { DrawLookCyber(ui); return; }      // only what Cyber uses (SettingsCyber.cs)
        var p = ui.P;
        var a = C.Appearance;
        float lx = 232, rx = 612;
        bool changed = false;

        // left: accent (presets + any hue), panels
        ui.Section(new RectangleF(lx, 84, 360, 20), "Accent colour");
        for (int i = 0; i < Theme.AccentPresets.Length; i++)
        {
            uint c = Theme.AccentPresets[i];
            if (ui.Swatch(Ui.Id("acc", i), lx + 18 + (i % 5) * 46, 126 + (i / 5) * 42, 15, c, (a.Accent & 0xFFFFFF) == (c & 0xFFFFFF)))
            { a.Accent = c; changed = true; }
        }
        float hue = Theme.Hue(a.Accent);
        if (ui.Slider("acc.hue", new RectangleF(lx - 10, 190, 376, 58), "Custom colour", ref hue, 0, 359.9f, f => $"{f:0}°"))
        { a.Accent = Theme.Hsv(hue, 0.72f, 1f); changed = true; }
        // a rainbow under the slider track
        for (int i = 0; i < 60; i++)
            p.Fill(new RectangleF(lx + i * (356 / 60f), 248, 356 / 60f + 0.5f, 4), Theme.Hsv(i * 6, 0.72f, 1f));

        ui.Section(new RectangleF(lx, 266, 360, 20), "Panel colour");
        for (int i = 0; i < Theme.PanelPresets.Length; i++)
        {
            uint c = Theme.PanelPresets[i];
            float cx = lx + 18 + i * 58;
            if (ui.Swatch(Ui.Id("pan", i), cx, 306, 15, Theme.Mix(c, 0xFFFFFFFF, 0.06f), (a.PanelColor & 0xFFFFFF) == (c & 0xFFFFFF)))
            { a.PanelColor = c; changed = true; }
            p.Text(Theme.PanelNames[i], new RectangleF(cx - 29, 326, 58, 18), Theme.Dim, 11, FontWeight.Normal, TextAlignment.Center);
        }
        float op = a.PanelOpacity;
        if (ui.Slider("pan.op", new RectangleF(lx - 10, 350, 376, 58), "Panel opacity", ref op, 0.4f, 1f, f => $"{f * 100:0}%"))
        { a.PanelOpacity = op; changed = true; }
        if (ui.Button("look.reset", new RectangleF(lx, 430, 170, 38), "Default look", Theme.IcoRecenter, false, true, 14))
        {
            C.Appearance = a = new AppearanceConfig
            {
                LaserThickness = a.LaserThickness, LaserAlways = a.LaserAlways, Clock24h = a.Clock24h,
                NightMode = a.NightMode, NightBrightness = a.NightBrightness, Style = a.Style, Font = a.Font, Motion = a.Motion,
                LookVersion = 1,     // the reset IS the Bento look: never the 1.10 migration again (1.13.2 put pink over a green accent)
            };
            changed = true;
        }
        // the style: Modern or a retro one, its name in a field; the list opens over the page (DrawStyleList)
        ui.Section(new RectangleF(lx, 488, 360, 20), "Style");
        {
            var field = StyleField;
            bool click = ui.Clickable("look.style", field, out bool hover, out _);
            float hv = ui.Anim("look.style#h", hover || _stylesOpen ? 1 : 0, 18);
            p.Fill(field, Theme.Mix(Theme.Panel, 0xFFFFFFFF, 0.05f * hv), 9);
            p.Stroke(field, _stylesOpen ? Theme.Accent : Theme.Mix(Theme.Border, Theme.Accent, 0.5f * hv), 1, 9);
            StyleChip(p, new RectangleF(field.X + 12, field.Y + 9, 28, 20), StyleMenu.IndexOf(a.Style));
            p.Text(StyleMenu.Names[StyleMenu.IndexOf(a.Style)], new RectangleF(field.X + 50, field.Y, field.Width - 88, field.Height), Theme.Text, 16);
            p.Icon(_stylesOpen ? Theme.IcoUp : Theme.IcoDown, new RectangleF(field.Right - 36, field.Y, 24, field.Height), Theme.Dim, 14);
            if (click) _stylesOpen = true;
        }
        if (Theme.Classic)
            ui.Label(new RectangleF(lx, 556, 356, 40), Lang.F("{0} has its own colours; the ones above are for Modern.", Lang.T(Theme.RetroSkin.Name)),
                Theme.Dim, 12, FontWeight.Normal, true);
        else if (Theme.Cyber)
            ui.Label(new RectangleF(lx, 556, 356, 40), Lang.F("{0} has its own colours and fonts; the ones above (and Font) are for Modern.", CyberHud.Name),
                Theme.Dim, 12, FontWeight.Normal, true);
        else
        {
            // the modern style's typeface: the chosen one's name in it; the list opens over the page (DrawFontList)
            ui.Section(new RectangleF(lx, 566, 360, 20), "Font");
            var field = FontField;
            bool click = ui.Clickable("look.font", field, out bool hover, out _);
            float hv = ui.Anim("look.font#h", hover || _fontsOpen ? 1 : 0, 18);
            p.Fill(field, Theme.Mix(Theme.Panel, 0xFFFFFFFF, 0.05f * hv), 9);
            p.Stroke(field, _fontsOpen ? Theme.Accent : Theme.Mix(Theme.Border, Theme.Accent, 0.5f * hv), 1, 9);
            p.Text(Theme.UserFont ?? Theme.DefaultFontName, new RectangleF(field.X + 14, field.Y, field.Width - 52, field.Height), Theme.Text, 16);
            p.Icon(_fontsOpen ? Theme.IcoUp : Theme.IcoDown, new RectangleF(field.Right - 36, field.Y, 24, field.Height), Theme.Dim, 14);
            if (click) _fontsOpen = true;
        }

        // right: one-click themes, laser
        ui.Section(new RectangleF(rx, 84, 360, 20), "Themes");
        for (int i = 0; i < Theme.ThemePresets.Length; i++)
        {
            var (name, acc, pan) = Theme.ThemePresets[i];
            var r = new RectangleF(rx + (i % 3) * 124, 108 + (i / 3) * 50, 116, 42);
            bool sel = (a.Accent & 0xFFFFFF) == (acc & 0xFFFFFF) && (a.PanelColor & 0xFFFFFF) == (pan & 0xFFFFFF);
            if (ui.Card(Ui.Id("theme", i), r, sel, out _)) { a.Accent = acc; a.PanelColor = pan; changed = true; }
            p.Circle(r.X + 20, r.Y + 21, 10, Theme.Mix(pan, 0xFFFFFFFF, 0.12f));
            p.Circle(r.X + 32, r.Y + 21, 10, acc);
            p.Text(name, new RectangleF(r.X + 48, r.Y, r.Width - 52, r.Height), Theme.Text, 14, FontWeight.SemiBold);
        }

        ui.Section(new RectangleF(rx, 220, 360, 20), "Laser");
        if (ui.Swatch("las.acc", rx + 18, 260, 14, Theme.Classic ? Theme.ClassicLaser : Theme.Accent, a.LaserColor == 0, "A")) { a.LaserColor = 0; changed = true; }
        for (int i = 0; i < Theme.AccentPresets.Length - 1; i++)
        {
            uint c = Theme.AccentPresets[i];
            if (ui.Swatch(Ui.Id("las", i), rx + 18 + (i + 1) * 36, 260, 11, c, a.LaserColor != 0 && (a.LaserColor & 0xFFFFFF) == (c & 0xFFFFFF)))
            { a.LaserColor = c; changed = true; }
        }
        float th = a.LaserThickness;
        if (ui.Slider("las.th", new RectangleF(rx - 10, 290, 376, 58), "Thickness", ref th, 1.5f, 8f, f => $"{f:0.0} mm"))
        { a.LaserThickness = MathF.Round(th * 2) / 2; changed = true; }
        bool always = a.LaserAlways;
        if (ui.Toggle("las.always", new RectangleF(rx - 10, 356, 376, 54), "Always show the beam", ref always,
                "otherwise only while pointing at a panel"))
        { a.LaserAlways = always; changed = true; }
        ui.Section(new RectangleF(rx, 428, 360, 20), "Night mode");
        bool night = a.NightMode;
        if (ui.Toggle("night", new RectangleF(rx - 10, 450, 376, 54), "Night mode", ref night, "dims every panel in VR - also on the watch (moon)"))
            a.NightMode = night;
        float nb = a.NightBrightness;
        if (ui.Slider("night.b", new RectangleF(rx - 10, 512, 376, 54), "Night brightness", ref nb, 0.15f, 0.9f, f => $"{f * 100:0}%"))
            a.NightBrightness = nb;
        // how much things move (Gfx/Motion.cs), what it means beside the title; written only when not Full, it applies at once
        ui.Section(new RectangleF(rx, 566, 360, 20), "Motion");
        int mi = MotionIndex(a.Motion);
        p.Text(MotionHints[mi], new RectangleF(rx + 96, 566, 260, 20), Theme.Dim, 12, FontWeight.Normal, TextAlignment.Trailing);
        if (ui.Segmented("look.motion", new RectangleF(rx, 588, 356, 38), MotionNames, ref mi))
        {
            a.Motion = Motion.Id((MotionLevel)mi);
            changed = true;
        }
        if (changed) _h.AppearanceChanged();
    }

    // Look > Motion: Full / Reduced / Off (AppearanceConfig.Motion: null / "reduced" / "off")
    static readonly string[] MotionNames = { "Full", "Reduced", "Off" };
    static readonly string[] MotionHints = { "every animation of the style", "short transitions only, no loops", "nothing moves, changes at once" };
    static int MotionIndex(string m) => m == "reduced" ? 1 : m == "off" ? 2 : 0;

    // ---- Look > Style: Modern and the retro styles, each with a little window in its colours; a pick applies at once ----
    bool _stylesOpen;
    static readonly RectangleF StyleField = new(232, 512, 356, 38);

    // the list over the page, above its field
    static RectangleF StyleList => Theme.Cyber ? CyStyleList : new(StyleField.X, StyleField.Y - 6 - StyleMenu.Ids.Length * FontRow - 12,
        StyleField.Width, StyleMenu.Ids.Length * FontRow + 12);

    // a style's little window: its face, its title bar, a selection (Modern: the panel and the accent)
    static void StyleChip(Painter p, RectangleF r, int i)
    {
        if (StyleMenu.Ids[Math.Clamp(i, 0, StyleMenu.Ids.Length - 1)] == CyberHud.Id) { CyberHud.Chip(p, r); return; }
        uint face, cap, sel;
        if (i <= 0) { face = Theme.Panel3; cap = Theme.Accent; sel = Theme.Accent; }
        else { var sk = Skins.All[i - 1]; face = sk.Face; cap = sk.ActiveCaption; sel = sk.Highlight; }
        p.Fill(r, face, 3);
        p.Fill(new RectangleF(r.X, r.Y, r.Width, 6), cap, 3);
        p.Fill(new RectangleF(r.X, r.Y + 3, r.Width, 3), cap);
        p.Fill(new RectangleF(r.X + 4, r.Y + 10, r.Width - 8, 4), sel, 1);
        p.Stroke(r, 0x33FFFFFF, 1, 3);
    }

    void DrawStyleList(Ui ui)
    {
        var p = ui.P;
        var a = C.Appearance;
        var box = StyleList;
        if (ui.Pressed && !box.Contains(ui.Mouse.X, ui.Mouse.Y))
        {
            _stylesOpen = false;         // a click beside it only closes it (the page under it was a picture this frame)
            return;
        }
        ui.Hover(box);
        if (Theme.Cyber) CyberListBox(p, box);
        else
        {
            p.Fill(new RectangleF(box.X - 2, box.Y + 4, box.Width + 4, box.Height + 4), 0x55000000, 14);
            p.Fill(box, Theme.Panel2, 12);
            p.Stroke(box, Theme.Border, 1, 12);
        }
        int sel = StyleMenu.IndexOf(a.Style);
        for (int i = 0; i < StyleMenu.Ids.Length; i++)
        {
            var row = new RectangleF(box.X + 6, box.Y + 6 + i * FontRow, box.Width - 12, FontRow);
            bool click = ui.Clickable(Ui.Id("look.style/", i), row, out bool hover, out _);
            bool on = i == sel;
            if (Theme.Cyber) CyberListRow(p, row, on, hover);
            else
            {
                if (on || hover) p.Fill(row, on ? Theme.Panel3 : Theme.WithAlpha(0x14FFFFFF, 1), 8);
                if (on) p.Fill(new RectangleF(row.X, row.Y + 10, 4, row.Height - 20), Theme.Accent, 2);
            }
            StyleChip(p, new RectangleF(row.X + 14, row.Y + 10, 28, 20), i);
            p.Text(StyleMenu.Names[i], new RectangleF(row.X + 54, row.Y, row.Width - 100, row.Height), Theme.Text, 17);
            if (on) p.Icon(Theme.IcoCheck, new RectangleF(row.Right - 34, row.Y, 24, row.Height), Theme.Accent, 14);
            if (click)
            {
                _stylesOpen = false;
                if (StyleMenu.Ids[i] != (a.Style ?? "modern"))
                {
                    a.Style = StyleMenu.Ids[i];
                    ui.Changed = true;
                    _h.AppearanceChanged();
                }
            }
        }
    }

    // ---- Look > Font: the list of typefaces, each name drawn in itself with a clock beside it; a pick applies at once ----
    bool _fontsOpen;
    static readonly RectangleF FontField = new(232, 588, 356, 38);
    const float FontRow = 40;

    // the list over the page, above its field and its title: null (the default) first, then the ones installed here
    RectangleF FontList => new(FontField.X, 562 - (Theme.FontsInstalled.Length + 1) * FontRow - 12,
        FontField.Width, (Theme.FontsInstalled.Length + 1) * FontRow + 12);

    void DrawFontList(Ui ui)
    {
        var p = ui.P;
        var a = C.Appearance;
        var box = FontList;
        if (ui.Pressed && !box.Contains(ui.Mouse.X, ui.Mouse.Y))
        {
            _fontsOpen = false;          // a click beside it only closes it (the page under it was a picture this frame)
            return;
        }
        ui.Hover(box);
        if (Theme.Cyber) CyberListBox(p, box);
        else
        {
            p.Fill(new RectangleF(box.X - 2, box.Y + 4, box.Width + 4, box.Height + 4), 0x55000000, 14);
            p.Fill(box, Theme.Panel2, 12);
            p.Stroke(box, Theme.Border, 1, 12);
        }
        string sel = Theme.UserFont;
        for (int i = 0; i <= Theme.FontsInstalled.Length; i++)
        {
            string font = i == 0 ? null : Theme.FontsInstalled[i - 1];
            string fam = font ?? Theme.UiFont;
            var row = new RectangleF(box.X + 6, box.Y + 6 + i * FontRow, box.Width - 12, FontRow);
            bool click = ui.Clickable(Ui.Id("look.font/", i), row, out bool hover, out _);
            bool on = font == sel;
            if (Theme.Cyber) CyberListRow(p, row, on, hover);
            else
            {
                if (on || hover) p.Fill(row, on ? Theme.Panel3 : Theme.WithAlpha(0x14FFFFFF, 1), 8);
                if (on) p.Fill(new RectangleF(row.X, row.Y + 10, 4, row.Height - 20), Theme.Accent, 2);
            }
            string name = font ?? Theme.DefaultFontName;
            p.Text(name, new RectangleF(row.X + 14, row.Y, row.Width - 120, row.Height), Theme.Text, 17, FontWeight.Normal,
                TextAlignment.Leading, ParagraphAlignment.Center, fam);
            if (font == null)
            {
                float nw = MathF.Min(p.MeasureWidth(name, 17, FontWeight.Normal, fam), row.Width - 120);
                p.Text("default", new RectangleF(row.X + 14 + nw + 8, row.Y, 70, row.Height), Theme.Dim, 12);
            }
            p.Text("12:45", new RectangleF(row.Right - 112, row.Y, 70, row.Height), Theme.Dim, 17, FontWeight.SemiBold,
                TextAlignment.Trailing, ParagraphAlignment.Center, fam);
            if (on) p.Icon(Theme.IcoCheck, new RectangleF(row.Right - 34, row.Y, 24, row.Height), Theme.Accent, 14);
            if (click)
            {
                _fontsOpen = false;
                if (font != a.Font)
                {
                    a.Font = font;
                    ui.Changed = true;
                    _h.AppearanceChanged();
                }
            }
        }
    }

    // ---------------- General ----------------
    // VRChat: the chatbox status line, who is in the room, and keeping clicks from the game
    void DrawVrchat(Ui ui)
    {
        var p = ui.P;
        var cb = C.Chatbox;
        var rm = C.Room;
        var g = C.General;
        float lx = 222, cw = 376, rx = 602, rw = 382;
        ui.Section(new RectangleF(lx + 10, 84, cw, 22), "Chatbox status line");
        bool b = cb.StatusLine;
        if (ui.Toggle("vc.status", new RectangleF(lx, 108, cw, 54), "Status line", ref b, "over your head in VRChat (needs OSC on in VRChat)"))
            cb.StatusLine = b;
        b = cb.ShowMedia;
        if (ui.Toggle("vc.media", new RectangleF(lx, 164, cw, 44), "Now playing", ref b)) cb.ShowMedia = b;
        b = cb.ShowTime;
        if (ui.Toggle("vc.time", new RectangleF(lx, 210, cw, 44), "Clock", ref b)) cb.ShowTime = b;
        b = cb.ShowVrTime;
        if (ui.Toggle("vc.vrtime", new RectangleF(lx, 256, cw, 44), "Time in VR", ref b)) cb.ShowVrTime = b;
        ui.Label(new RectangleF(lx + 10, 306, cw - 20, 20), cb.StatusLine ? "It says now:" : "It would say:");
        string prev = _h.StatusLinePreview;
        var box = new RectangleF(lx + 10, 330, cw - 20, 64);
        p.Fill(box, Theme.Panel, 10);
        p.Text(string.IsNullOrEmpty(prev) ? "nothing - the chatbox stays empty" : prev, RectangleF.Inflate(box, -12, -6),
            string.IsNullOrEmpty(prev) ? Theme.Dim : Theme.Text, 14, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, null, true);
        ui.Section(new RectangleF(lx + 10, 412, cw, 22), "Controllers");
        b = g.BlockGameInput;
        if (ui.Toggle("vc.block", new RectangleF(lx, 436, cw, 54), "Keep clicks from the game", ref b, "while a laser is on an OrbitOverlay panel"))
            g.BlockGameInput = b;
        DrawGesture(ui, lx, cw, 496);     // B / Y twice (SettingsView.Extras.cs)
        // the eye cursor (Vr/Laser.cs, eye tracking): off / a press where you don't look turns the beam there / the beam follows your eyes
        int eci = Math.Max(0, Array.IndexOf(EyeCursorIds, g.EyeCursor ?? "follow"));
        ui.Label(new RectangleF(lx + 10, 594, 128, 40), "Eye cursor", g.EyeTracking ? Theme.Text : Theme.Dim, 15);
        if (ui.Segmented("vc.eyec", new RectangleF(lx + 140, 596, cw - 150, 38), EyeCursorNames, ref eci, g.EyeTracking)) g.EyeCursor = EyeCursorIds[eci];
        DrawControlsHelp(ui, rx + 10, 392);
        // eye tracking (Sys/EyeGaze.cs), under the controls: the look from Virtual Desktop's - gaze focus, smart capture, the watch's
        // glance and the dim go by it; its state on the line under the two switches
        var ge = C.General;
        ui.Section(new RectangleF(rx + 10, 544, rw, 20), "Eye tracking");
        bool eb = ge.EyeTracking;
        if (ui.Toggle("vc.eyes", new RectangleF(rx, 566, rw / 2 - 4, 44), "Use eye tracking", ref eb)) ge.EyeTracking = eb;
        eb = ge.DimUnlooked;
        if (ui.Toggle("vc.dim", new RectangleF(rx + rw / 2 + 4, 566, rw / 2 - 4, 44), "Dim the rest", ref eb, null, ge.EyeTracking)) ge.DimUnlooked = eb;
        ui.P.Text(_h.EyeStatus, new RectangleF(rx + 10, 612, rw - 10, 20), Theme.Dim, 12);

        ui.Section(new RectangleF(rx + 10, 84, rw, 22), "Who is in the room");
        b = rm.Enabled;
        if (ui.Toggle("vc.room", new RectangleF(rx, 108, rw, 54), "Follow VRChat's log", ref b, _h.RoomStatus)) rm.Enabled = b;
        b = rm.NotifyFriends;
        if (ui.Toggle("vc.nf", new RectangleF(rx, 164, rw, 54), "Tell me when friends come and go", ref b, "friends from VRCX", rm.Enabled))
            rm.NotifyFriends = b;
        b = rm.NotifyStarred;
        if (ui.Toggle("vc.ns", new RectangleF(rx, 220, rw, 54), "... and starred people", ref b, "star them in the Room panel", rm.Enabled))
            rm.NotifyStarred = b;
        b = rm.NotifyEveryone;
        if (ui.Toggle("vc.ne", new RectangleF(rx, 276, rw / 2 - 4, 44), "... and everyone", ref b, null, rm.Enabled)) rm.NotifyEveryone = b;
        b = rm.BuzzFriends;
        if (ui.Toggle("vc.buzz", new RectangleF(rx + rw / 2 + 4, 276, rw / 2 - 4, 44), "Buzz for friends", ref b, null, rm.Enabled && rm.NotifyFriends))
            rm.BuzzFriends = b;
        if (ui.Button("vc.open", new RectangleF(rx + 10, 336, 220, 38), rm.Visible ? "Hide the Room panel" : "Show the Room panel", "\uE716", false,
                _h.VrConnected && rm.Enabled, 14))
            _h.ToggleRoom();
    }

    // General > Language: English (null in the config, nothing written) or another one; every panel is drawn again in it
    static readonly string[] LanguageNames = Lang.Names();

    // the choices' words, made once (an array a frame each before; the painter translates them where they are drawn)
    static readonly string[] EyeCursorIds = { "off", "click", "follow" }, EyeCursorNames = { "Off", "On click", "Follow" },
        EyeCursor95 = { "Off", "On a click", "Follows your eyes" };
    static readonly string[] LaserHands = { "Both hands", "Left", "Right" }, SmoothingNames = { "Off", "Low", "Med", "High" },
        HandNames = { "Left", "Right" }, FaceNames = { "Full", "Analog", "Minimal", "Time", "Custom" }, PerfNames = { "Numbers", "Graph" };
    string[] _soundNames;
    int _soundLang = -1;
    // "Muted" has two meanings: the keyboard's line of the table is picked here (Lang.C), again when the language changes
    string[] SoundNames()
    {
        if (_soundLang != Lang.Version) { _soundLang = Lang.Version; _soundNames = new[] { "Click", Lang.C("keyboard", "Muted") }; }
        return _soundNames;
    }

    static readonly string[] FaceIds = { "full", "analog", "minimal", "time", "custom" },
        FaceNames95 = { "Full", "Analog", "Minimal", "Time only", "Custom" }, LaserHands95 = { "Both hands", "Left hand", "Right hand" },
        SmoothingNames95 = { "Off", "Low", "Medium", "High" };

    Lang.Memo<int> _oscHint, _oscHint95;
    Lang.Memo<string> _version95;
    Lang.Memo<double> _nang95;
    string _monitorNames;
    IReadOnlyList<MonitorInfo> _monitorNamesOf;
    int _monitorNamesCount = -1;

    // "P24FBA-RAGL, 24G4, ..." again only when the list of monitors is another one
    string MonitorNames()
    {
        var l = _h.MonitorList;
        if (!ReferenceEquals(l, _monitorNamesOf) || l.Count != _monitorNamesCount)
        {
            _monitorNamesOf = l;
            _monitorNamesCount = l.Count;
            _monitorNames = string.Join(", ", l.Select(m => m.Name));
        }
        return _monitorNames;
    }

    void SetLanguage(Ui ui, int i)
    {
        string code = Lang.Languages[Math.Clamp(i, 0, Lang.Languages.Length - 1)].Code;
        if (code == Lang.Code) return;
        C.General.Language = code;
        Lang.Set(code);
        ui.Changed = true;
        _h.AppearanceChanged();
    }

    void DrawGeneral(Ui ui)
    {
        var p = ui.P;
        var g = C.General;
        float lx = 222, cw = 376;
        ui.Label(new RectangleF(lx + 10, 84, cw, 22), "Laser on");
        int li = g.LaserHand == "left" ? 1 : g.LaserHand == "right" ? 2 : 0;
        if (ui.Segmented("g.laser", new RectangleF(lx + 10, 108, cw - 20, 38), LaserHands, ref li))
            g.LaserHand = li == 1 ? "left" : li == 2 ? "right" : "both";
        ui.Label(new RectangleF(lx + 10, 152, 120, 38), "Smoothing");
        int sm = Math.Clamp(g.LaserSmoothing, 0, 3);
        if (ui.Segmented("g.smooth", new RectangleF(lx + 110, 152, cw - 120, 38), SmoothingNames, ref sm))
            g.LaserSmoothing = sm;
        bool b = g.Haptics;
        if (ui.Toggle("g.hap", new RectangleF(lx, 196, cw, 44), "Haptic ticks", ref b)) g.Haptics = b;
        b = g.QuickToggle;
        if (ui.Toggle("g.quick", new RectangleF(lx, 242, cw, 54), "Double-tap both grips", ref b, "hides / shows the monitors and the keyboard")) g.QuickToggle = b;
        b = g.Osc;
        if (ui.Toggle("g.osc", new RectangleF(lx, 298, cw, 54), "VRChat avatar parameters (OSC)", ref b,
                _oscHint.F("XSOverlay-style, for DXOverlay · 127.0.0.1:{0}", g.OscPort))) g.Osc = b;
        b = g.AutoStart;
        if (ui.Toggle("g.auto", new RectangleF(lx, 354, cw, 54), "Start with SteamVR", ref b,
                b ? (_h.VrConnected ? _h.AutoStartStatus : "applied the next time SteamVR runs") : "start OrbitOverlay yourself"))
            _h.SetAutoStart(b);
        b = g.RunAsAdmin;
        string adminHint = _h.IsElevated ? "running as admin: works in admin windows too"
            : g.RunAsAdmin ? "not elevated right now - restart to apply" : "admin windows (e.g. Task Manager) ignore the VR mouse";
        if (ui.Toggle("g.admin", new RectangleF(lx, 410, cw, 54), "Run as administrator", ref b, adminHint))
            _h.SetRunAsAdmin(b);
        b = g.PauseWhenOff;
        if (ui.Toggle("g.pause", new RectangleF(lx, 466, cw, 46), "Pause when the headset is off", ref b)) g.PauseWhenOff = b;
        float by = 520;
        bool elevate = g.RunAsAdmin && !_h.IsElevated;
        // not elevated yet: its restart button beside the folder one (the language takes the row under them)
        if (elevate && ui.Button("g.elevate", new RectangleF(lx + 10, by, 178, 38), "Restart as admin", Theme.IcoRecenter, true, true, 14))
            _h.RestartElevated();
        // the folder (only its icon while the restart button is there too), then What's new (it sat on Check for updates)
        if (ui.Button("g.folder", elevate ? new RectangleF(lx + 196, by, 40, 38) : new RectangleF(lx + 10, by, 200, 38), elevate ? null : "Open config folder",
                Theme.IcoFolder, false, true, 14))
            _h.OpenConfigFolder();
        if (ui.Button("g.whatsnew", elevate ? new RectangleF(lx + 244, by, 122, 38) : new RectangleF(lx + 220, by, 146, 38), "What's new", "\uE946", false, true, 14))
        { _whatsNewManual = true; _wnScroll = 0; }
        // the language of every screen, each choice in its own words: it applies at once
        ui.Label(new RectangleF(lx + 10, 572, 100, 38), "Language");
        int lg = Lang.Index(Lang.Code);
        if (ui.Segmented("g.lang", new RectangleF(lx + 110, 572, cw - 120, 38), LanguageNames, ref lg)) SetLanguage(ui, lg);

        float rx = 618, y = 88;
        void Row(string k, string v)
        {
            if (Theme.Cyber) { CyberRow(p, k, v, rx, y); y += 26; return; }     // a monospace readout (SettingsCyber.cs)
            p.Text(k, new RectangleF(rx, y, 100, 26), Theme.Dim, 13);
            p.Text(v, new RectangleF(rx + 100, y, 266, 26), Theme.Text, 13);
            y += 26;
        }
        Row("SteamVR", _h.VrStatus);
        Row("Input", _h.InputStatus);
        Row("GPU", _h.GpuName);
        Row("Monitors", MonitorNames());
        Row("Config", "%LOCALAPPDATA%\\Orbit");
        // version + updates: one button that checks, then offers the update
        var (us, umsg, uprog, ulatest) = _h.Updates.Snapshot();
        if (Theme.Cyber) CyberRow(p, "Version", Updater.CurrentText, rx, y);
        else
        {
            p.Text("Version", new RectangleF(rx, y, 100, 26), Theme.Dim, 13);
            p.Text(Updater.CurrentText, new RectangleF(rx + 100, y, 70, 26), Theme.Text, 13);
        }
        bool avail = us == Updater.State.Available;
        string ulabel = us switch
        {
            Updater.State.Checking => "Checking...",
            Updater.State.Downloading => $"{uprog * 100:0}%",
            Updater.State.Starting => "Installing...",
            Updater.State.Available => Lang.F("Update to {0}", ulatest),
            _ => "Check for updates",
        };
        if (ui.Button("g.upd", new RectangleF(rx + 176, y - 3, 190, 32), ulabel, avail ? Theme.IcoDownload : Theme.IcoRecenter, avail,
                !_h.Updates.Busy, 13))
        {
            if (avail) _h.Updates.Apply();
            else _h.Updates.Check();
        }
        y += 30;
        if (!string.IsNullOrEmpty(umsg))
            p.Text(umsg, new RectangleF(rx + 100, y, 266, 18), us == Updater.State.Failed ? Theme.Warn : Theme.Dim, 12);

        ui.Section(new RectangleF(rx, 268, 360, 20), "Notifications");
        b = g.Notifications;
        if (ui.Toggle("g.notif", new RectangleF(rx - 10, 290, 376, 54), "Show notifications", ref b,
                g.Notifications ? _h.NotificationStatus : "XSOverlay format (VRCX, Discord plugins...)")) g.Notifications = b;
        b = g.NotificationSound;
        if (ui.Toggle("g.nsnd", new RectangleF(rx - 10, 346, 250, 44), "Chime", ref b, null, g.Notifications)) g.NotificationSound = b;
        if (ui.Button("g.ntest", new RectangleF(rx + 252, 351, 114, 34), "Test", "\uEA8F", false, g.Notifications && _h.VrConnected, 13))
            _h.TestNotification();
        float na = g.NotificationAngle;
        if (ui.Slider("g.nang", new RectangleF(rx - 10, 392, 376, 58), "Height", ref na, 5, 35,
                f => Lang.F("{0:0}° below eye level", Math.Round(f)), g.Notifications))
            g.NotificationAngle = MathF.Round(na);
        DrawAlerts(ui, rx, 460);          // low battery / frame rate (SettingsView.Extras.cs; the controls' summary is on the VRChat tab now)
    }
}
