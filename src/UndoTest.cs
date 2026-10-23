using System.Collections.Generic;
using System.IO;

namespace Orbit;

// --snapshot with ORBIT_SNAP_ONLY=undo: the watch editor's Undo / Redo (Ui/SettingsView.Undo.cs) - two changes in the editor, then Undo,
// Undo, Redo by its buttons, a page turned (not a step), Ctrl+Z, a new change after an undo (Redo gone); the editor with both buttons
// live pictured in the modern look, the 95 family and Cyber (undo_editor*.png).
static partial class Tools
{
    static int UndoOnly(SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg, WristPanel wrist, PanelData data, string dir)
    {
        int failed0 = WantsFailed;
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Pages, wcfg.Page, cfg.Data.Appearance);
        void Look(string style) { cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true }; Theme.Apply(cfg.Data.Appearance); }
        void Frame()
        {
            wrist.Draw(data);
            if (Theme.Classic) { view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal); return; }
            ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End();
        }
        void Click(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); Frame(); ptr.Press(); Frame(); ptr.Release(); Frame(); ptr.Leave(); Frame(); }
        string L() => SettingsView.LayoutTextOf(wcfg);

        Look("modern");
        wcfg.Face = "custom";
        wcfg.Widgets = WatchWidgets.Seed(wcfg, "full");
        wcfg.Pages = new List<WatchPage> { new() { Grid = "6x4", Widgets = WatchWidgets.PagePreset("vrchat", GridSpec.G64) } };
        wcfg.Page = 0;
        view.Tab = SettingsView.TabWrist;
        view.EditingWatch = false;
        Frame();                                    // the Wrist tab without the editor: the steps start from here
        view.EditingWatch = true;
        ui.ResetAnimations();
        Frame();
        string s0 = L();
        wcfg.Widgets.RemoveAt(0); Frame();          // 1: a widget gone
        string s1 = L();
        wcfg.Widgets[0].Style = "solid"; Frame();   // 2: a widget's look
        string s2 = L();
        Want($"undo: two changes -> Undo {view.CanUndo} (want True), Redo {view.CanRedo} (want False)", view.CanUndo && !view.CanRedo);
        const float rx = 724, by = 574 + 22;
        Click(rx + 25, by);                         // Undo
        bool u1 = L() == s1;
        surf.SavePng(gpu, Path.Combine(dir, "undo_editor.png"));
        Click(rx + 25, by);                         // Undo
        bool u2 = L() == s0;
        Click(rx + 56 + 25, by);                    // Redo
        bool r1 = L() == s1;
        Want($"undo: the buttons - Undo back a step {u1}, again to the start {u2}, Redo forward one {r1} (want True, True, True)", u1 && u2 && r1);
        wrist.GoToPage(1); Frame(); wrist.GoToPage(0); Frame();     // pages turned on the watch: not a step
        var askFile = view.AskFile;
        view.AskFile ??= (_, _, _) => { };          // as in the desktop window (keys are typed there, not in VR)
        view.TypeKey((char)26);                     // Ctrl+Z
        view.AskFile = askFile;
        Frame();
        bool k1 = L() == s0;
        Want($"undo: a page turned is not a step - Ctrl+Z after it back at the start {k1} (want True)", k1);
        view.Redo(); Frame();                        // at s1 again, s2 still to redo
        wcfg.Widgets[1].Style = "outline"; Frame();  // a new change after an undo: the redo is gone
        Want($"undo: a new change after an undo - Redo {view.CanRedo} (want False), Undo {view.CanUndo} (want True)", !view.CanRedo && view.CanUndo);
        _ = s2;

        // the other looks, both buttons live (one step undone)
        foreach (var (style, tag) in new[] { ("classic", "_95"), (CyberHud.Id, "_cyber") })
        {
            Look(style);
            ui.ResetAnimations();
            Frame();
            view.Undo(); Frame();
            surf.SavePng(gpu, Path.Combine(dir, $"undo_editor{tag}.png"));
            view.Redo(); Frame();
        }

        (wcfg.Face, wcfg.Widgets, wcfg.Pages, wcfg.Page, cfg.Data.Appearance) = keep;
        Theme.Apply(cfg.Data.Appearance);
        view.EditingWatch = false;
        Log.Info($"undo: {WantsFailed - failed0} check(s) failed");
        return WantsFailed > failed0 ? 1 : 0;
    }
}
