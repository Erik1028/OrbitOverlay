using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Orbit;

public sealed record WindowInfo(IntPtr Hwnd, string Title, string Process, string Class)
{
    public override string ToString() => $"'{Title}' ({Process}, {Class})";
}

// Top-level application windows: the list for the picker, finding a mirrored window again
// after a restart (process + window class, title as tie-break), and bringing one to the front.
public static class WindowList
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lp);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lp);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    const uint GW_OWNER = 4, GA_ROOT = 2;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    const int SW_RESTORE = 9;

    static readonly HashSet<string> SkipClasses = new()
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    internal static string Text(IntPtr h)
    {
        int n = GetWindowTextLength(h);
        if (n <= 0) return "";
        var sb = new StringBuilder(n + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // program name of a window's process, asked fresh every time: a cache by pid gave the wrong name
    // once Windows reused the pid of a closed program (a cheap call, only made while listing windows)
    static string ProcessOf(IntPtr h)
    {
        Native.GetWindowThreadProcessId(h, out uint pid);
        IntPtr ph = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (ph == IntPtr.Zero) return "?";
        try
        {
            var sb = new StringBuilder(1024);
            int n = sb.Capacity;
            return QueryFullProcessImageName(ph, 0, sb, ref n) ? System.IO.Path.GetFileNameWithoutExtension(sb.ToString()) : "?";
        }
        finally { CloseHandle(ph); }
    }

    public static bool Cloaked(IntPtr h) =>
        DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int c, sizeof(int)) == 0 && c != 0;

    // what Alt+Tab would show (roughly), minus our own windows
    static bool IsAppWindow(IntPtr h, uint ownPid)
    {
        if (!IsWindowVisible(h) || Cloaked(h)) return false;
        long ex = (long)GetWindowLongPtr(h, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if ((ex & WS_EX_NOACTIVATE) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (GetWindow(h, GW_OWNER) != IntPtr.Zero && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (GetWindowTextLength(h) == 0) return false;
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (pid == ownPid) return false;
        return !SkipClasses.Contains(ClassOf(h));
    }

    public static List<WindowInfo> Enumerate()
    {
        var list = new List<WindowInfo>();
        uint own = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            try
            {
                if (IsAppWindow(h, own)) list.Add(new WindowInfo(h, Text(h), ProcessOf(h), ClassOf(h)));
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string TitleOf(IntPtr h) => Text(h);

    // The window a config was made for: same program and window class; the same title wins,
    // then the first one nobody else mirrors yet.
    public static WindowInfo Find(WindowConfig c, List<WindowInfo> open, ICollection<IntPtr> taken)
    {
        WindowInfo first = null;
        foreach (var w in open)
        {
            if (taken.Contains(w.Hwnd)) continue;
            if (!string.Equals(w.Process, c.Process, StringComparison.OrdinalIgnoreCase) || w.Class != c.Class) continue;
            if (w.Title == c.Title) return w;
            first ??= w;
        }
        return first;
    }

    // Visible window rectangle on the screen (physical pixels, without the invisible resize border) -
    // the same area Windows.Graphics.Capture delivers.
    public static bool Bounds(IntPtr h, out RECT r)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>()) == 0 && r.Right > r.Left) return true;
        r = default;
        return false;
    }

    public static bool IsForeground(IntPtr h)
    {
        var fg = Native.GetForegroundWindow();
        return fg == h || (fg != IntPtr.Zero && GetAncestor(fg, GA_ROOT) == h);
    }

    // Puts the window on top so clicks land in it and not in whatever covers it on the real desktop.
    // Windows only lets the process that sent the last input change the foreground: an empty
    // mouse event (no movement, no buttons) makes that us without side effects.
    public static void BringToFront(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        if (IsForeground(h)) return;
        Native.SendMouse(0);
        Native.Front(h);                    // (after the empty event, in order: Sys/InputQueue.cs)
    }
}
