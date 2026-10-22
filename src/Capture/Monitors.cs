using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Orbit;

public sealed class MonitorInfo
{
    public IntPtr Handle;
    public string Device;        // \\.\DISPLAY1
    public string Id;            // stable monitor device path when available
    public string Name;          // "AOC 24G4X"
    public int X, Y, W, H;       // desktop rect in physical pixels
    public bool Primary;
    public bool Virtual;         // indirect / virtual display (Virtual Desktop, Meta, ...)
    public override string ToString() => $"{Device} '{Name}' {W}x{H} @({X},{Y}){(Virtual ? " virtual" : "")}";
}

public static class Monitors
{
    public static List<MonitorInfo> Enumerate()
    {
        var names = CcdNames();
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr d) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(h, ref mi))
            {
                var m = new MonitorInfo
                {
                    Handle = h, Device = mi.szDevice,
                    X = mi.rcMonitor.Left, Y = mi.rcMonitor.Top,
                    W = mi.rcMonitor.Right - mi.rcMonitor.Left, H = mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                    Primary = (mi.dwFlags & 1) != 0,
                };
                if (names.TryGetValue(mi.szDevice, out var n))
                {
                    m.Name = string.IsNullOrWhiteSpace(n.name) ? null : n.name;
                    m.Id = string.IsNullOrWhiteSpace(n.path) ? null : n.path;
                    m.Virtual = n.tech == 16 || n.tech == 17;   // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED / _VIRTUAL
                }
                // the Virtual Display Driver's (Sys/VirtualMonitors.cs; its EDID says "VDD by MTT", MTT1337)
                if (m.Id != null && m.Id.Contains("MTT1337", StringComparison.OrdinalIgnoreCase)) m.Name = "Virtual monitor";
                m.Name ??= m.Device.Replace(@"\\.\", "");
                m.Id ??= m.Device;
                list.Add(m);
            }
            return true;
        }, IntPtr.Zero);
        return list.OrderBy(m => m.X).ThenBy(m => m.Y).ToList();
    }

    // GDI device name -> (friendly name, device path, output technology) via the CCD API.
    static Dictionary<string, (string name, string path, uint tech)> CcdNames()
    {
        var map = new Dictionary<string, (string, string, uint)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            const uint QDC_ONLY_ACTIVE_PATHS = 2;
            if (Native.GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return map;
            var paths = new Native.DISPLAYCONFIG_PATH_INFO[np];
            var modes = new Native.DISPLAYCONFIG_MODE_INFO[nm];
            if (Native.QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return map;
            for (int i = 0; i < np; i++)
            {
                var p = paths[i];
                var src = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                src.header.type = 1;
                src.header.size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                src.header.adapter = p.srcAdapter;
                src.header.id = p.srcId;
                if (Native.DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                var tgt = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                tgt.header.type = 2;
                tgt.header.size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                tgt.header.adapter = p.tgtAdapter;
                tgt.header.id = p.tgtId;
                if (Native.DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;
                map[src.viewGdiDeviceName] = (tgt.monitorFriendlyDeviceName, tgt.monitorDevicePath, p.outputTechnology);
            }
        }
        catch (Exception e) { Log.Warn("CCD names: " + e.Message); }
        return map;
    }
}
