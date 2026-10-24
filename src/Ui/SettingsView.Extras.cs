using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Vortice.DirectWrite;

namespace Orbit;

// 1.14's settings, the Modern (and Cyber) pages: General > Alerts, VRChat > Controllers > B / Y twice (and the controls' summary,
// moved here from General to make room), Layouts > Per game (in place of the profile on the right), the watch editor's Share panel
// (export / import a page as a file, copy / paste it as text). The 95 sheet's are in SettingsView95.Extras.cs; the logic they share is
// here too.
public sealed partial class SettingsView
{
    // ================= General > Alerts =================

    static readonly int[] BatteryPcts = { 10, 15, 25 }, FpsPcts = { 70, 80, 90 }, HotCs = { 80, 85, 90 };
    static readonly string[] BatteryPctNames = { "10%", "15%", "25%" }, FpsPctNames = { "70%", "80%", "90%" }, HotCNames = { "80°", "85°", "90°" };
    static readonly AlertsConfig AlertDefaults = new();         // never changed: C.General.Alerts null reads as this

    AlertsConfig AlertsNow => C.General.Alerts ?? AlertDefaults;

    // a change: made on a copy, kept only when it is not the defaults - back at the defaults nothing is written
    void SetAlerts(Action<AlertsConfig> change)
    {
        var cur = C.General.Alerts;
        var a = cur == null ? new AlertsConfig()
            : new AlertsConfig { Battery = cur.Battery, BatteryPct = cur.BatteryPct, Fps = cur.Fps, FpsPct = cur.FpsPct, Hot = cur.Hot, HotC = cur.HotC };
        change(a);
        C.General.Alerts = a.IsDefault ? null : a;
        _h.Cfg.MarkDirty();
    }

    // the option nearest to a value (a hand-edited 12 % shows as 10 %; it is only written when one is clicked)
    static int Nearest(int[] options, int v)
    {
        int best = 0;
        for (int i = 1; i < options.Length; i++) if (Math.Abs(options[i] - v) < Math.Abs(options[best] - v)) best = i;
        return best;
    }

    // (1.15: the GPU / CPU hot row took the place of the two lines under the rows; what an alert is went up beside the title)
    void DrawAlerts(Ui ui, float rx, float y)
    {
        var a = AlertsNow;
        ui.Section(new RectangleF(rx, y, 360, 20), "Alerts");
        ui.P.Text("a card and a buzz", new RectangleF(rx + 150, y, 216, 20), Theme.Cyber ? CyberHud.Grey : Theme.Dim, 12, Theme.Cyber ? FontWeight.SemiBold : FontWeight.Normal,
            TextAlignment.Trailing, ParagraphAlignment.Center, Theme.Cyber ? CyberHud.Mono : null);
        bool b = a.Battery;
        if (ui.Toggle("g.abat", new RectangleF(rx - 10, y + 22, 232, 46), "Low battery", ref b, "controllers and trackers"))
            SetAlert(0, b ? 1 : 0);
        int bi = Nearest(BatteryPcts, a.BatteryPct);
        if (ui.Segmented("g.abatp", new RectangleF(rx + 226, y + 28, 140, 34), BatteryPctNames, ref bi, a.Battery))
            SetAlert(1, BatteryPcts[bi]);
        b = a.Fps;
        if (ui.Toggle("g.afps", new RectangleF(rx - 10, y + 70, 232, 46), "Low frame rate", ref b, "of the headset's rate, 10 s"))
            SetAlert(2, b ? 1 : 0);
        int fi = Nearest(FpsPcts, a.FpsPct);
        if (ui.Segmented("g.afpsp", new RectangleF(rx + 226, y + 76, 140, 34), FpsPctNames, ref fi, a.Fps))
            SetAlert(3, FpsPcts[fi]);
        b = a.Hot;
        if (ui.Toggle("g.ahot", new RectangleF(rx - 10, y + 118, 232, 46), "GPU / CPU hot", ref b, "at or over it for 30 s"))
            SetAlert(4, b ? 1 : 0);
        int hi = Nearest(HotCs, a.HotLimit);
        if (ui.Segmented("g.ahotc", new RectangleF(rx + 226, y + 124, 140, 34), HotCNames, ref hi, a.Hot))
            SetAlert(5, HotCs[hi]);
    }

    // one alert setting changed: 0 battery on, 1 its %, 2 frame rate on, 3 its %, 4 GPU / CPU hot on, 5 its °C (a method of its own: the lambdas holding the values
    // made their holder on every frame DrawAlerts drew, clicked or not)
    void SetAlert(int what, int v) => SetAlerts(x =>
    {
        switch (what)
        {
            case 0: x.Battery = v != 0; break;
            case 1: x.BatteryPct = v; break;
            case 2: x.Fps = v != 0; break;
            case 3: x.FpsPct = v; break;
            case 4: x.Hot = v != 0; break;
            default: x.HotC = v == AlertsConfig.HotDefault ? null : v; break;
        }
    });

    // ================= VRChat > Controllers: B / Y twice =================

    static readonly string[] DoubleNames = { "Off", "Keyboard", "Watch" }, DoubleIds = { "off", "keyboard", "watch" };

    static int DoubleIndex(string setting) => Math.Max(0, Array.IndexOf(DoubleIds, DoublePress.Mode(setting)));

    void DrawGesture(Ui ui, float lx, float cw, float y)
    {
        var g = C.General;
        int di = DoubleIndex(g.DoublePress);
        ui.Label(new RectangleF(lx + 10, y, 110, 38), "B / Y twice", Theme.Text, 15);
        if (ui.Segmented("vc.dbl", new RectangleF(lx + 120, y, cw - 130, 38), DoubleNames, ref di))
        {
            g.DoublePress = DoublePress.Store(DoubleIds[di]);
            ui.Changed = true;
        }
        string hint = di == 0 ? "Off: B / Y does nothing more on the panels."
            : !g.BlockGameInput ? "Needs \"Keep clicks from the game\" (above): only then can VRChat never get the presses."
            : Lang.T(di == 1 ? "Shows / hides the keyboard" : "Pins the watch (always visible) / lets it go") +
              Lang.T(" - pressed quickly with the laser on an OrbitOverlay panel. Not on a monitor: B / Y is its right click.");
        ui.Label(new RectangleF(lx + 10, y + 44, cw - 20, 52), hint, !g.BlockGameInput && di > 0 ? Theme.Warn : Theme.Dim, 12, FontWeight.Normal, true);
    }

    // the controllers in a few lines (General had them before the alerts came)
    static readonly string[] ControlsLines =
    {
        "Trigger: click  ·  B / Y: right click  ·  stick: scroll",
        "Grip: grab & move  ·  stick ↕ distance, ↔ bend",
        "2nd hand's grip: resize  ·  B/Y + grip: leave row",
        "Both grips twice: hide / show monitors + keyboard",
        "B / Y twice on a panel: the keyboard (or the watch)",
    };

    void DrawControlsHelp(Ui ui, float x, float y)
    {
        var p = ui.P;
        ui.Section(new RectangleF(x, y, 360, 20), "Controls");
        y += 22;                    // (22 px a line, was 24: the eye calibration's row fits under the eye tracking, 10-24)
        foreach (var l in ControlsLines) { p.Text(l, new RectangleF(x, y, 366, 22), Theme.Dim, 13); y += 22; }
    }

    // ================= Layouts > Per game =================

    bool _rulesOpen;                 // the right side shows the rules (modern) / the 95 dialog is open
    string _rulesFor;                // the profile selected when they opened: picking another one closes them
    int _ruleSel;                    // the 95 dialog's selected rule
    public const int MaxRules = 4;

    List<GameRule> Rules => C.GameRules ??= new List<GameRule>();

    void OpenRules(bool on)
    {
        _rulesOpen = on;
        _rulesFor = _selLayout;
        _ruleSel = 0;
        if (C.GameRules is { Count: 0 }) C.GameRules = null;        // none left: nothing written
    }

    string RulesSummary()
    {
        var r = C.GameRules;
        if (r == null || r.Count == 0) return Lang.T("none");
        string first = $"{Lang.T(SceneApp.Label(r[0]))}: {RuleWhat(r[0])}";
        return r.Count == 1 ? first : Lang.F("{0} + {1} more", first, r.Count - 1);
    }

    string RuleWhat(GameRule r)
    {
        var parts = new List<string>();
        if (r.Layout != null) parts.Add(Lang.T(r.Layout));
        if (r.Page is int pg) parts.Add(PageLabel(pg));
        return parts.Count == 0 ? Lang.T("nothing") : string.Join(", ", parts);
    }

    string PageLabel(int pg) => pg < WatchPages.Count(C.Wrist) ? WatchPages.Label(C.Wrist, pg) : Lang.F("Page {0}", pg + 1);

    // the rules that can still be added: the app running now, VRChat, any other app
    List<(string key, string name)> RuleChoices()
    {
        var res = new List<(string, string)>();
        bool Has(string k) => C.GameRules?.Any(r => r.App == k) == true;
        var (rk, rn) = _h.RunningApp;
        if (!string.IsNullOrEmpty(rk) && !Has(rk)) res.Add((rk, rn ?? rk));
        if (!Has(GameRuleEngine.VrchatKey) && rk != GameRuleEngine.VrchatKey) res.Add((GameRuleEngine.VrchatKey, "VRChat"));
        if (!Has(GameRuleEngine.AnyOther)) res.Add((GameRuleEngine.AnyOther, null));
        return res;
    }

    void AddRule(string key, string name)
    {
        if (Rules.Count >= MaxRules || Rules.Any(r => r.App == key)) return;
        // the any-other-app rule stays last (an app's own rule wins anyway)
        var rule = new GameRule { App = key, Name = name, Layout = C.ActiveLayout };
        int at = Rules.FindIndex(r => r.App == GameRuleEngine.AnyOther);
        if (key == GameRuleEngine.AnyOther || at < 0) Rules.Add(rule); else Rules.Insert(at, rule);
        _ruleSel = Rules.IndexOf(rule);
        _h.Cfg.MarkDirty();
    }

    void RemoveRule(GameRule r)
    {
        C.GameRules?.Remove(r);
        if (C.GameRules is { Count: 0 }) C.GameRules = null;
        _ruleSel = Math.Max(0, _ruleSel - 1);
        _h.Cfg.MarkDirty();
    }

    // the layout / page a rule switches to, one step on (null = keep)
    void NextLayout(GameRule r)
    {
        var names = C.Layouts.Select(l => l.Name).ToList();
        int i = r.Layout == null ? -1 : names.IndexOf(r.Layout);
        r.Layout = i + 1 < names.Count ? names[i + 1] : null;
        _h.Cfg.MarkDirty();
    }

    void NextPage(GameRule r)
    {
        int n = WatchPages.Count(C.Wrist), i = r.Page ?? -1;
        r.Page = i + 1 < n ? i + 1 : null;
        _h.Cfg.MarkDirty();
    }

    // under the profile: what Per game does now, a click opens it
    void PerGameButton(Ui ui, float wx, float cw, float y)
    {
        string label = C.GameRules is { Count: > 0 } ? Lang.F("Per game: {0}", RulesSummary()) : "Per game: switch when an app starts";
        if (ui.Button("lay.games", new RectangleF(wx + 10, y, cw - 10, 38), Fit(ui.P, label, cw - 70, 14, FontWeight.SemiBold), "\uE7FC", false, true, 14))
            OpenRules(true);
    }

    void DrawRules(Ui ui, float wx, float cw)
    {
        var p = ui.P;
        if (_selLayout != _rulesFor) { OpenRules(false); return; }      // another profile picked on the left
        p.Text("Per game", new RectangleF(wx + 10, 78, cw - 110, 30), Theme.Text, 20, FontWeight.SemiBold);
        p.Text("switches once when a SteamVR app starts", new RectangleF(wx + 10, 106, cw - 110, 20), Theme.Dim, 13);
        if (ui.Button("rules.done", new RectangleF(wx + cw - 96, 80, 96, 34), "Done", Theme.IcoCheck, true, true, 14)) { OpenRules(false); return; }
        var rules = C.GameRules ?? new List<GameRule>();
        float y = 134;
        GameRule gone = null;
        for (int i = 0; i < rules.Count && i < MaxRules; i++)
        {
            var r = rules[i];
            var card = new RectangleF(wx + 10, y, cw - 10, 60);
            p.Fill(card, Theme.Panel, 10);
            p.Text(Fit(p, SceneApp.Label(r), cw - 70, 15, FontWeight.SemiBold), new RectangleF(card.X + 12, y + 2, cw - 70, 24), Theme.Text, 15, FontWeight.SemiBold);
            if (ui.Button(Ui.Id("rule.rm.", i), new RectangleF(card.Right - 34, y + 4, 30, 24), null, Theme.IcoDelete, false, true, 11)) gone = r;
            float bw = (card.Width - 30) / 2;
            if (ui.Button(Ui.Id("rule.lay.", i), new RectangleF(card.X + 10, y + 27, bw, 28), Fit(p, Lang.F("Layout: {0}", Lang.T(r.Layout ?? "keep")), bw - 12, 13, FontWeight.SemiBold),
                    null, r.Layout != null, true, 13))
                NextLayout(r);
            if (ui.Button(Ui.Id("rule.pg.", i), new RectangleF(card.X + 20 + bw, y + 27, bw, 28),
                    Fit(p, Lang.F("Watch: {0}", r.Page is int pg ? PageLabel(pg) : Lang.T("keep")), bw - 12, 13, FontWeight.SemiBold), null, r.Page != null, C.Wrist.Face == "custom", 13))
                NextPage(r);
            y += 66;
        }
        if (gone != null) RemoveRule(gone);
        if (rules.Count == 0)
        {
            ui.Label(new RectangleF(wx + 10, y, cw - 10, 64), "No rules yet. Add one: the layout in use now is its first pick; click its buttons to change what it " +
                     "switches to (\"keep\" changes nothing).", Theme.Dim, 13, FontWeight.Normal, true);
            y += 68;
        }
        // add: the app running now, VRChat, any other app - side by side
        var choices = RuleChoices();
        int room = MaxRules - rules.Count;
        if (room > 0 && choices.Count > 0)
        {
            int n = Math.Min(choices.Count, 3);
            float aw = (cw - 10 - (n - 1) * 6) / n, ay = y;
            for (int i = 0; i < n; i++)
            {
                var (key, name) = choices[i];
                string label = key == GameRuleEngine.AnyOther ? "Other apps" : name;
                if (ui.Button(Ui.Id("rule.add.", i), new RectangleF(wx + 10 + i * (aw + 6), ay, aw, 34), Fit(p, label, aw - 36, 13, FontWeight.SemiBold), Theme.IcoAdd, false, true, 13))
                    AddRule(key, name);
            }
        }
    }

    // ================= the watch editor: Share =================

    bool _wshare;                    // the right side shows the share panel (modern) / the 95 dialog is open
    string _shareMsg;                // what the last export / import did
    bool _shareBad;
    List<FileInfo> _shareFiles;      // the in-VR import's list (read when the panel opens, after an export)
    double _shareFilesAt = -10;
    int _shareSel = -1;
    const float ShareW = 40;

    // the desktop window's file dialogs (SettingsWindow sets it): save?, a suggested name, then the path (null: cancelled). Not set in
    // VR (the panel, the dashboard): the faces folder in Documents instead
    public Action<bool, string, Action<string>> AskFile;
    public bool InVr => AskFile == null;

    void ShareButton(Ui ui, float x, float y, float h)
    {
        if (ui.Button("we.share", new RectangleF(x, y, ShareW, h), null, "\uE72D", _wshare, true, 13))
            OpenShare(!_wshare);
    }

    void OpenShare(bool on)
    {
        _wshare = on;
        _wadd = false;
        _wselW = null;
        _shareMsg = null;
        _shareSel = -1;
        _shareFilesAt = -10;
    }

    void Say(string msg, bool bad = false) { _shareMsg = msg; _shareBad = bad; }

    List<FileInfo> ShareFiles()
    {
        if (_shareFiles == null || Clock.Now - _shareFilesAt > 3) { _shareFiles = FaceShare.List(); _shareFilesAt = Clock.Now; }
        return _shareFiles;
    }

    // ---- what the buttons do (both styles) ----

    void ExportPage()
    {
        var w = C.Wrist;
        int page = EditPage;
        if (WatchPages.Widgets(w, page) == null) WatchLayout();          // page 1 never set up: as the watch shows it
        if (AskFile != null)
        {
            string suggested = FaceShare.FileName(WatchPages.Name(w, page));
            AskFile(true, suggested, path =>
            {
                if (path == null) return;
                try { FaceShare.SaveTo(path, w, Math.Min(page, WatchPages.Count(w) - 1)); Say(Lang.F("Saved: {0}", Path.GetFileName(path))); }
                catch (Exception e) { Say(Lang.F("It could not be saved: {0}", e.Message), true); }
            });
            return;
        }
        try
        {
            string path = FaceShare.SaveToFolder(w, page, DateTime.Now);
            Say(Lang.F("Saved to {0}", FaceShare.Short(path)));
            _h.Notify("Watch page exported", FaceShare.Short(path));
            _shareFilesAt = -10;
        }
        catch (Exception e) { Say(Lang.F("It could not be saved: {0}", e.Message), true); }
    }

    void CopyPage()
    {
        var w = C.Wrist;
        if (WatchPages.Widgets(w, EditPage) == null) WatchLayout();
        string text = FaceShare.ClipboardText(w, EditPage);
        bool code = text.StartsWith(FaceShare.CodePrefix);
        try
        {
            FaceShare.SetClipboard(text);
            Say(Lang.F(code ? "Copied as a short code ({0} characters): paste it where you share it - Discord, a note."
                : "Copied ({0} characters): paste it where you share it - Discord, a note.", text.Length));
        }
        catch (Exception e) { Say(Lang.F("The clipboard is busy: {0}", e.Message), true); }
    }

    // a page from text: added after the others and shown; the editor says what was left out
    void ImportText(string text, string from)
    {
        var page = FaceShare.Parse(text, out string err, out int dropped);
        if (page == null) { Say(err, true); return; }
        if (!FaceShare.Add(C.Wrist, page, out err)) { Say(err, true); return; }
        _h.Cfg.MarkDirty();
        int at = WatchPages.Count(C.Wrist) - 1;
        bool keep = _wshare;
        ShowPage(at);
        _wshare = keep;
        Say((page.Name != null ? Lang.F("Added from {0} as page {1} ({2})", Lang.T(from), at + 1, Lang.T(page.Name)) : Lang.F("Added from {0} as page {1}", Lang.T(from), at + 1)) +
            (dropped > 0 ? Lang.F(dropped == 1 ? "; {0} widget this version cannot show left out." : "; {0} widgets this version cannot show left out.", dropped) : "."));
    }

    void ImportFile()
    {
        if (AskFile != null)
        {
            AskFile(false, null, path =>
            {
                if (path == null) return;
                string text = FaceShare.ReadFile(path, out string err);
                if (text == null) Say(err, true); else ImportText(text, Path.GetFileName(path));
            });
            return;
        }
        var files = ShareFiles();
        if (_shareSel < 0 || _shareSel >= files.Count) { Say("Pick a file in the list first.", true); return; }
        string t = FaceShare.ReadFile(files[_shareSel].FullName, out string e2);
        if (t == null) Say(e2, true); else ImportText(t, files[_shareSel].Name);
    }

    void PastePage()
    {
        string text;
        try { text = FaceShare.GetClipboard(); }
        catch (Exception e) { Say(Lang.F("The clipboard is busy: {0}", e.Message), true); return; }
        if (string.IsNullOrWhiteSpace(text)) { Say("The clipboard has no text: copy a page first.", true); return; }
        ImportText(text, "the clipboard");
    }

    // ---- the modern panel (in place of the page's settings on the right) ----

    void SharePanel(Ui ui, float rx, float rw)
    {
        var p = ui.P;
        var w = C.Wrist;
        int ep = EditPage;
        ui.Section(new RectangleF(rx, 84, rw, 22), "Share this page");
        p.Text(Fit(p, $"{WatchPages.Label(w, ep)}  ·  {WatchPages.GridId(w, ep).Replace("x", " × ")}", rw, 13), new RectangleF(rx, 106, rw, 20), Theme.Dim, 13);
        if (ui.Button("ws.export", new RectangleF(rx, 130, rw, 36), InVr ? "Save to Documents" : "Export to a file", "\uE74E", false, true, 13)) ExportPage();
        if (ui.Button("ws.copy", new RectangleF(rx, 172, rw, 36), "Copy as text", "\uE8C8", false, true, 13)) CopyPage();
        ui.Section(new RectangleF(rx, 222, rw, 22), "Add a page from");
        bool room = WatchPages.Count(w) < FaceShare.MaxPages;
        float y = 246;
        if (InVr)
        {
            // the faces folder: the newest files, one to pick, then Import
            var files = ShareFiles();
            p.Text("Documents\\OrbitOverlay\\faces", new RectangleF(rx, y, rw, 18), Theme.Dim, 12);
            y += 20;
            if (files.Count == 0) { p.Text("no files there yet", new RectangleF(rx, y, rw, 28), Theme.Faint, 13); y += 30; }
            for (int i = 0; i < files.Count && i < 4; i++)
            {
                var r = new RectangleF(rx, y, rw, 28);
                if (ui.Card(Ui.Id("ws.file.", i), r, i == _shareSel, out _)) _shareSel = i;
                p.Text(Fit(p, Path.GetFileNameWithoutExtension(files[i].Name), rw - 20, 13), new RectangleF(r.X + 10, r.Y, rw - 20, r.Height), Theme.Text, 13);
                y += 32;
            }
            if (ui.Button("ws.import", new RectangleF(rx, y + 2, rw, 36), "Import the picked file", "\uE8E5", false, room && _shareSel >= 0 && _shareSel < files.Count, 13))
                ImportFile();
            y += 44;
        }
        else
        {
            if (ui.Button("ws.import", new RectangleF(rx, y, rw, 36), "Import a file...", "\uE8E5", false, room, 13)) ImportFile();
            y += 42;
        }
        if (ui.Button("ws.paste", new RectangleF(rx, y, rw, 36), "Paste text (the clipboard)", "\uE77F", false, room, 13)) PastePage();
        y += 44;
        string msg = !room ? Lang.F("The watch has {0} pages: remove one to add another.", FaceShare.MaxPages) : _shareMsg ??
            "A page goes as a small file or as text - its widgets, grid, name and looks. Adding one never changes your other pages.";
        ui.Label(new RectangleF(rx, y, rw, Math.Max(40, 568 - y)), msg, _shareBad || !room ? Theme.Warn : _shareMsg != null ? Theme.Good : Theme.Dim, 13, FontWeight.Normal, true);
    }
}
