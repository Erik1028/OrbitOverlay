using System.IO;
using System.Linq;

namespace Orbit;

// --snapshot with ORBIT_SNAP_ONLY=virtual: Settings > Desktops with the virtual monitors' card (Sys/VirtualMonitors.cs) in each of its
// states - no admin, no driver, a job running, on, none, a failure - in the modern look, the 95 styles and Cyber
// (virtual_<state><tag>.png). Nothing is looked at or run: the VirtualMonitors are Fake, their state set by hand.
static partial class Tools
{
    static int VirtualOnly(FakeHost host, SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        var keep = (host.Virtual, cfg.Data.General.VirtualMonitors, cfg.Data.Appearance);
        static VirtualMonitors V(VirtualMonitors.Kind k, bool admin = true) => new() { Fake = true, Admin = admin, State = k };
        var states = new (string name, VirtualMonitors vm, int count)[]
        {
            ("admin", V(VirtualMonitors.Kind.NoDriver, admin: false), 0),
            ("nodriver", V(VirtualMonitors.Kind.NoDriver), 0),
            ("busy", Busy(V(VirtualMonitors.Kind.NoDriver), "Windows asks about the driver..."), 1),
            ("on", On(V(VirtualMonitors.Kind.On), 2), 2),
            ("none", V(VirtualMonitors.Kind.Off), 0),
            ("note", Noted(V(VirtualMonitors.Kind.Off), "pnputil /enable-device failed (5)"), 2),
        };
        int n = 0;
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            bool retro = Skins.All.Any(s => s.Id == style);
            foreach (var (name, vm, count) in states)
            {
                host.Virtual = vm;
                cfg.Data.General.VirtualMonitors = count;
                view.Tab = SettingsView.TabDesktops;
                ui.ResetAnimations();
                if (retro) view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
                else { ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
                surf.SavePng(gpu, Path.Combine(dir, $"virtual_{name}{tag}.png"));
                n++;
            }
        }
        (host.Virtual, cfg.Data.General.VirtualMonitors, cfg.Data.Appearance) = keep;
        Theme.Apply(cfg.Data.Appearance);
        System.Globalization.CultureInfo.CurrentCulture = culture;
        Log.Info($"virtual monitors: {n} pictures in {dir}");
        return 0;

        static VirtualMonitors Busy(VirtualMonitors v, string doing) { v.Busy = true; v.Doing = doing; return v; }
        static VirtualMonitors On(VirtualMonitors v, int count) { v.Count = count; return v; }
        static VirtualMonitors Noted(VirtualMonitors v, string note) { v.Note = note; return v; }
    }
}
