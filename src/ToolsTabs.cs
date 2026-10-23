using System.IO;

namespace Orbit;

// --snapshot with ORBIT_SNAP_ONLY=tabs: every settings tab once in the modern look (tabs<n>.png) - in seconds, for a look at where
// something new fits (the whole --snapshot has them too, among everything else)
static partial class Tools
{
    static int TabsOnly(SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, WristPanel wrist, KeyboardPanel kb, PanelData data, string dir)
    {
        for (int t = 0; t < SettingsView.TabCount; t++)
        {
            view.Tab = t;
            ui.ResetAnimations();
            if (view.ShowsWristPreview) wrist.Draw(data);
            if (view.ShowsKeyboardPreview) kb.Draw();
            ui.Begin(ptr, 1.5f);
            painter.Begin(surf.Bitmap, Theme.Bg, 1.5f);
            view.Draw(ui);
            painter.End();
            ui.End();
            surf.SavePng(gpu, Path.Combine(dir, $"tabs{t}.png"));
        }
        return 0;
    }
}
