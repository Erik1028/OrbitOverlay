using System;
using System.Collections.Generic;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// The avatar toggles widget's settings (Sys/AvatarToggles.cs): which of the avatar's buttons the widget shows - kept per avatar
// (Config.Avatars.Shown, by its id), so every avatar has its own; one never picked shows the filter's first ones. The avatar is the one
// worn in VRChat now (the watch's link: VRChat has to run, the avatar's OSC config has to be there). In the watch editor, when an avatar
// widget is selected: Modern / Cyber a chip per button under the watch in the palette's place (lit: on the watch; a click adds it at the
// end or takes it off), Default goes back to the filter's; the 95 family a line in the Selected widget box, its Choose... a dialog with
// the buttons as a check list.
public sealed partial class SettingsView
{
    int _avChipPage;
    readonly List<int> _avFound = new();
    string _avSearchWas = "";
    bool _wav95;                                // the 95 editor's Avatar Toggles dialog is open
    int _wav95Sel = -1;
    readonly List<int> _avPickTmp = new();
    const int AvChips = 16;                     // a page of chips: 4 x 4

    AvatarState AvatarNow => _h.Data?.Avatar?.State;

    // --snapshot: the 95 dialog open / shut; a chip clicked
    internal void AvatarDialogForTest(bool open) { _wav95 = open; _wshare = false; }
    internal void PickForTest(Ui ui, AvatarConfigFile a, int bi) => TogglePick(ui, a, bi);

    // the names picked for this avatar (null: none - the filter's first ones show)
    List<string> AvatarPicks(AvatarConfigFile a) =>
        a != null && C.Avatars?.Shown != null && C.Avatars.Shown.TryGetValue(a.Id, out var l) ? l : null;

    // a button on / off the watch: the first pick starts from what the widget showed (so one click changes one button)
    void TogglePick(Ui ui, AvatarConfigFile a, int bi)
    {
        var cfg = C.Avatars ??= new AvatarsConfig();
        cfg.Shown ??= new Dictionary<string, List<string>>();
        if (!cfg.Shown.TryGetValue(a.Id, out var list) || list == null)      // (null: as never picked - ConfigStore.Load drops those too)
        {
            list = new List<string>();
            AvatarFilter.Shown(a, null, _avPickTmp);
            foreach (int i in _avPickTmp) list.Add(a.Buttons[i].Name);
            cfg.Shown[a.Id] = list;
        }
        string name = a.Buttons[bi].Name;
        if (!list.Remove(name)) list.Add(name);
        cfg.Edits++;
        ui.Changed = true;
    }

    // back to the filter's first ones
    void ResetPicks(Ui ui, AvatarConfigFile a)
    {
        if (C.Avatars?.Shown == null || !C.Avatars.Shown.Remove(a.Id)) return;
        C.Avatars.Edits++;
        ui.Changed = true;
    }

    // why there is nothing to pick from (null: the avatar's buttons are there)
    string AvatarWhy(AvatarState s) => s == null ? "Start VRChat and wear the avatar to pick its toggles." : s.Status switch
    {
        AvatarStatus.NoVrchat => "VRChat isn't running: start it and wear the avatar to pick its toggles.",
        AvatarStatus.NoConfig => "No OSC config for the avatar worn: turn on OSC in VRChat's Action Menu.",
        AvatarStatus.Ready when s.Avatar.Buttons.Length == 0 => "This avatar has no toggles.",
        AvatarStatus.Ready => null,
        _ => "Looking for VRChat...",
    };

    // ---- Modern / Cyber: under the watch, in the palette's place (x, y: its top-left; 486 x 190) ----

    void AvatarPicker(Ui ui, float x, float y)
    {
        var p = ui.P;
        var s = AvatarNow;
        string why = AvatarWhy(s);
        if (why != null)
        {
            ui.Label(new RectangleF(x, y, 486, 60), why, Theme.Dim, 13, FontWeight.Normal, true);
            return;
        }
        var a = s.Avatar;
        var picks = AvatarPicks(a);
        int shown = AvatarFilter.Shown(a, picks, _avPickTmp);
        // an avatar with hundreds: letters typed in the desktop window (the palette's search, TypeKey) narrow the chips to the names
        // that hold them
        _avFound.Clear();
        foreach (int bi in a.Order)
            if (_wsearch.Length == 0 || a.Buttons[bi].Label.Contains(_wsearch, StringComparison.CurrentCultureIgnoreCase)) _avFound.Add(bi);
        int n = _avFound.Count;
        string head = _wsearch.Length > 0 ? Lang.F("Toggles with \"{0}\": {1}", _wsearch, n)
            : Lang.F("{0}: {1} of {2} on the watch", a.Name, shown, a.Order.Length) + (InVr ? "" : Lang.T(" · type to search"));
        ui.Label(new RectangleF(x, y, 486 - 112, 18), Cut(p, head, 486 - 116, 13), _wsearch.Length > 0 ? Theme.Text : Theme.Dim, 13);
        if (ui.Button("we.avdefault", new RectangleF(x + 486 - 104, y - 4, 104, 24), "Default", null, false, picks != null, 12)) ResetPicks(ui, a);
        int pages = (n + AvChips - 1) / AvChips;
        if (_wsearch != _avSearchWas) { _avSearchWas = _wsearch; _avChipPage = 0; }
        _avChipPage = Math.Clamp(_avChipPage, 0, Math.Max(0, pages - 1));
        for (int k = 0; k < AvChips; k++)
        {
            int i = _avChipPage * AvChips + k;
            if (i >= n) break;
            int bi = _avFound[i];
            bool on = _avPickTmp.Contains(bi);
            var r = new RectangleF(x + k % 4 * 122, y + 24 + k / 4 * 35, 116, 30);
            if (ui.Button(Ui.Id("wavp", k), r, Cut(p, a.Buttons[bi].Label, on ? 116 - 40 : 116 - 16, 12), on ? Theme.IcoCheck : null, on, true, 12))
                TogglePick(ui, a, bi);
        }
        if (pages <= 1) return;
        float py = y + 24 + 4 * 35 + 2;
        if (ui.Button("we.avprev", new RectangleF(x, py, 40, 24), null, "", false, _avChipPage > 0, 11)) _avChipPage--;
        p.Text(Lang.F("{0} / {1}", _avChipPage + 1, pages), new RectangleF(x + 46, py, 60, 24), Theme.Dim, 12, FontWeight.SemiBold, TextAlignment.Center,
            ParagraphAlignment.Center);
        if (ui.Button("we.avnext", new RectangleF(x + 112, py, 40, 24), null, "", false, _avChipPage < pages - 1, 11)) _avChipPage++;
    }

    // ---- the 95 family: a line in the editor's Selected widget box, and its dialog ----

    void Avatar95Row(Ui ui, float x, float y)
    {
        var s = AvatarNow;
        Lbl(ui, x, y, "Toggles:");
        string what;
        if (AvatarWhy(s) is { } why) what = s?.Status == AvatarStatus.NoVrchat ? Lang.T("VRChat isn't running") : why;
        else what = Lang.F("{0} of {1} shown", AvatarFilter.Shown(s.Avatar, AvatarPicks(s.Avatar), _avPickTmp), s.Avatar.Order.Length);
        ui.Edit95(new RectangleF(x + 56, y, 116, RowH), Fit95(ui, what, 110));
        if (ui.Button95("we95.avpick", new RectangleF(647, y - 1, 75, 23), "C&hoose...", s?.Ready == true)) { _wav95 = true; _wshare = false; _wav95Sel = -1; }
    }

    // Avatar Toggles: the avatar's buttons as a check list (checked: on the watch), Default and OK
    void AvatarDialog95(Ui ui, List<WatchWidget> list)
    {
        if (!_wav95) return;
        if (SelWidget(list)?.Type != "avatar") { _wav95 = false; return; }
        var s = AvatarNow;
        var c = Window95(ui, new RectangleF(200, 56, 350, 372), "Avatar Toggles");
        float x = c.X + 10, cw = c.Width - 20, y = c.Y + 8;
        W95Icons.Draw(ui.P, W95Icons.Id.People, x, y);
        if (AvatarWhy(s) is { } why || s?.Avatar == null)
        {
            ui.Text95(AvatarWhy(s) ?? "", new RectangleF(x + 22, y + 2, cw - 24, 60), K95, false, TextAlignment.Leading, true, true);
            if (ui.Button95("we95.avok", new RectangleF(c.Right - 10 - 75, c.Bottom - 31, 75, 23), "OK", true, true)) _wav95 = false;
            return;
        }
        var a = s.Avatar;
        var picks = AvatarPicks(a);
        int shown = AvatarFilter.Shown(a, picks, _avPickTmp), n = a.Order.Length;
        ui.Text95(Fit95(ui, a.Name, cw - 24), new RectangleF(x + 22, y + 2, cw - 24, 15), K95, true);
        ui.Text95(Fit95(ui, Lang.F("Check the ones the watch shows ({0} of {1}).", shown, n), cw), new RectangleF(x, y + 24, cw, 13), K95);
        var lr = new RectangleF(x, y + 42, cw, c.Bottom - 40 - (y + 42));
        int hit = ui.List95("we95.avlist", lr, n, _wav95Sel, (i, r, on) =>
        {
            int bi = a.Order[i];
            Theme.RetroSkin.CheckBox(ui.P, r.X + 2, r.Y + 1, "", _avPickTmp.Contains(bi), true, on && Theme.Retro == RetroStyle.Terminal, false);      // (lit on the Terminal's lit bar)
            var b = a.Buttons[bi];
            string kind = b.Kind switch { AvatarKind.Toggle => "", AvatarKind.Stepper => Lang.T("number"), _ => Lang.T("slider") };
            float kw = kind.Length > 0 ? ui.Width95(kind) + 8 : 0;
            ui.Text95(Fit95(ui, b.Label, r.Width - 24 - kw), new RectangleF(r.X + 20, r.Y + 1, r.Width - 22 - kw, 13), on ? W95 : K95);
            if (kw > 0) ui.Text95(kind, new RectangleF(r.Right - kw, r.Y + 1, kw - 2, 13), on ? W95 : Grey95, false, TextAlignment.Trailing);
        });
        if (hit >= 0) { _wav95Sel = hit; TogglePick(ui, a, a.Order[hit]); }
        if (ui.Button95("we95.avdefault", new RectangleF(x, c.Bottom - 31, 75, 23), "&Default", picks != null)) ResetPicks(ui, a);
        if (ui.Button95("we95.avok", new RectangleF(c.Right - 10 - 75, c.Bottom - 31, 75, 23), "OK", true, true)) _wav95 = false;
    }
}
