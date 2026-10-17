using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Orbit;

// --snapshot's 'overlaps' lines (Erik, 10-16: "What's new" sat on "Check for updates" and on the Notifications title): every settings
// tab in every style drawn once, each control's hit area noted (Ui.HitSpy), and two that cross reported - the layout lines only see a
// text leaving its own box. One inside another (a row and its switch) is not a crossing. ORBIT_SNAP_ONLY=overlaps runs this alone.
static partial class Tools
{
    static int OverlapRun(WristPanel wrist, KeyboardPanel kb, SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter,
        ConfigStore cfg, PanelData data, Gpu gpu, string dir)
    {
        var keep = cfg.Data.Appearance;
        int tab0 = view.Tab, total = 0;
        var hits = new List<(string id, RectangleF r)>();
        ui.HitSpy = (id, r) => hits.Add((id, r));
        static bool Inside(RectangleF a, RectangleF b) =>
            a.Left <= b.Left + 0.5f && a.Top <= b.Top + 0.5f && a.Right >= b.Right - 0.5f && a.Bottom >= b.Bottom - 0.5f;

        void Style(string name, AppearanceConfig a, Action draw)
        {
            cfg.Data.Appearance = a;
            Theme.Apply(a);
            var found = new List<string>();
            for (int t = 0; t < SettingsView.TabCount; t++)
            {
                view.Tab = t;
                ui.ResetAnimations();
                if (view.ShowsWristPreview) wrist.Draw(data);
                if (view.ShowsKeyboardPreview) kb.Draw();
                hits.Clear();
                draw();
                // one entry per control (where it was drawn last), then every pair that crosses
                var list = hits.GroupBy(h => h.id).Select(g => g.Last()).Where(h => h.r.Width > 0 && h.r.Height > 0).ToList();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var x = RectangleF.Intersect(list[i].r, list[j].r);
                        if (x.Width < 1.5f || x.Height < 1.5f || Inside(list[i].r, list[j].r) || Inside(list[j].r, list[i].r)) continue;
                        found.Add($"tab {t} '{list[i].id}' x '{list[j].id}' ({x.Width:0} x {x.Height:0})");
                    }
            }
            total += found.Count;
            Log.Info($"overlaps {name}: {found.Count} control(s) crossing another (want 0){(found.Count > 0 ? ": " + string.Join("; ", found.Take(16)) : "")}");
        }

        void Modern() { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
        Style("modern", new AppearanceConfig { Clock24h = true }, Modern);
        foreach (var sk in Skins.All)
            Style(sk.Tag, new AppearanceConfig { Style = sk.Id, Clock24h = true }, () => view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal));
        Style("cyber", new AppearanceConfig { Style = CyberHud.Id, Clock24h = true }, Modern);
        // Modern and Cyber as the desktop window draws them: no Windows frame, their own minimise / close in the header
        var close0 = view.OnClose95; var min0 = view.OnMinimize;
        view.OnClose95 = () => { }; view.OnMinimize = () => { };
        foreach (var (name, style) in new[] { ("modern", (string)null), ("cyber", CyberHud.Id) })
        {
            Style(name + " window", new AppearanceConfig { Style = style, Clock24h = true }, Modern);
            view.Tab = SettingsView.TabGeneral;
            ui.ResetAnimations();
            Modern();
            surf.SavePng(gpu, System.IO.Path.Combine(dir, $"settings_window_{name}.png"));
            // the watch editor in the desktop window: the palette's categories and a search typed in ("vo": Volume, Volume mixer)
            var ask0 = view.AskFile;
            view.AskFile = (_, _, _) => { };
            view.Tab = SettingsView.TabWrist;
            string face0 = cfg.Data.Wrist.Face;
            cfg.Data.Wrist.Face = "custom";                     // the editor is the custom face's
            view.EditingWatch = true;
            foreach (char ch in "vo") view.TypeKey(ch);
            ui.ResetAnimations();
            Modern();
            surf.SavePng(gpu, System.IO.Path.Combine(dir, $"settings_window_search_{name}.png"));
            view.TypeKey((char)27);
            // a light widget selected: its Bulb row (the address, Go direct / Use Lumos)
            view.SelectKindForTest("light");
            ui.ResetAnimations();
            Modern();
            surf.SavePng(gpu, System.IO.Path.Combine(dir, $"settings_window_bulb_{name}.png"));
            view.SelectKindForTest(null);
            view.EditingWatch = false;
            cfg.Data.Wrist.Face = face0;
            view.AskFile = ask0;
        }
        view.OnClose95 = close0; view.OnMinimize = min0;
        System.Globalization.CultureInfo.CurrentCulture = culture;
        ui.HitSpy = null;
        cfg.Data.Appearance = keep;
        Theme.Apply(keep);
        view.Tab = tab0;
        return total;
    }
}
