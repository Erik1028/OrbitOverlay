using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Orbit;

// --snapshot with ORBIT_SNAP_ONLY=tabs: every settings tab once in the modern look (tabs<n>.png) - in seconds, for a look at where
// something new fits (the whole --snapshot has them too, among everything else). ORBIT_SNAP_TABTIME=1: each tab drawn 20 more times in
// the modern look, Cyber and the 95 family, the draw's time (D2D's flush included) median / max - which page is slow to draw (Erik 10-24:
// the in-VR settings panel's page took 15-18 ms a draw).
static partial class Tools
{
    static int TabsOnly(SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, WristPanel wrist, KeyboardPanel kb, PanelData data, ConfigStore cfg, string dir)
    {
        void Frame()
        {
            if (view.ShowsWristPreview) wrist.Draw(data);
            if (view.ShowsKeyboardPreview) kb.Draw();
            if (Theme.Classic) { view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal); return; }
            ui.Begin(ptr, 1.5f);
            painter.Begin(surf.Bitmap, Theme.Bg, 1.5f);
            view.Draw(ui);
            painter.End();
            ui.End();
        }
        for (int t = 0; t < SettingsView.TabCount; t++)
        {
            view.Tab = t;
            ui.ResetAnimations();
            Frame();
            surf.SavePng(gpu, Path.Combine(dir, $"tabs{t}.png"));
        }
        if (Environment.GetEnvironmentVariable("ORBIT_SNAP_TABTIME") != "1") return 0;
        foreach (var style in new[] { "modern", CyberHud.Id, "classic" })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            for (int t = 0; t < SettingsView.TabCount; t++)
            {
                view.Tab = t;
                ui.ResetAnimations();
                Frame(); Frame();                              // the first draws make the page's text layouts
                var ms = new double[20];
                for (int i = 0; i < ms.Length; i++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    Frame();
                    ms[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                }
                Array.Sort(ms);
                Log.Info($"tabtime {style} tab {t}: median {ms[ms.Length / 2]:0.00} ms, max {ms[^1]:0.00} ms (wrist preview {view.ShowsWristPreview}, keyboard preview {view.ShowsKeyboardPreview})");
            }
        }
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        return 0;
    }
}
