using System;

namespace Orbit;

// Virtual monitors (Sys/VirtualMonitors.cs, Erik 10-21): the count Settings > Desktops keeps. "Only while in VR" (on by default):
// they are there only while SteamVR runs - when they go, Windows moves the windows on them back to the real screens, so nothing is
// left on a screen nobody can see. A count of 0 leaves the driver as it is (set to 0 in the settings: turned off that once).
public sealed partial class OrbitApp
{
    readonly VirtualMonitors _virtual = new();

    public VirtualMonitors Virtual => _virtual;

    int VirtualWanted => C.General.VirtualOnlyInVr && !_vr.Connected ? 0 : C.General.VirtualMonitors;

    // every 5 s, with the monitor check: a new monitor shows up in the next check (SyncMonitors makes its panel)
    void VirtualTick()
    {
        if (C.General.VirtualMonitors <= 0) return;
        if (_virtual.State == VirtualMonitors.Kind.Unknown) { _virtual.LookSoon(); return; }
        _virtual.Want(VirtualWanted);
    }

    public void SetVirtualCount(int n)
    {
        C.General.VirtualMonitors = Math.Clamp(n, 0, VirtualMonitors.Max);
        _cfg.MarkDirty();
        _virtual.Want(VirtualWanted, asked: true);
        _win.Dirty = true;
    }

    public void SetVirtualOnlyInVr(bool on)
    {
        C.General.VirtualOnlyInVr = on;
        _cfg.MarkDirty();
        if (C.General.VirtualMonitors > 0) _virtual.Want(VirtualWanted, asked: true);
        _win.Dirty = true;
    }

    public void InstallVirtual()
    {
        if (C.General.VirtualMonitors <= 0) { C.General.VirtualMonitors = 1; _cfg.MarkDirty(); }
        _virtual.Install(C.General.VirtualMonitors);
        _win.Dirty = true;
    }

    public void RemoveVirtual()
    {
        C.General.VirtualMonitors = 0;
        _cfg.MarkDirty();
        _virtual.Remove();
        _win.Dirty = true;
    }

    // Orbit closing: the monitors that are there only with it go too
    void VirtualExit()
    {
        if (C.General.VirtualMonitors > 0 && C.General.VirtualOnlyInVr) _virtual.OffNow(3000);
    }
}
