using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Orbit;

static class Native
{
    // ---- input injection ----
    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020,
        MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000,
        MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004, KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", EntryPoint = "SetCursorPos")] static extern bool SetCursorPosRaw(int x, int y);
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] static extern bool SetForegroundWindowRaw(IntPtr hwnd);

    // What goes to Windows goes out on the input thread while the app runs (Sys/InputQueue.cs: SetCursorPos / SendInput held the main
    // loop 4-10 ms now and then), in the order asked; the tools make the call at once.
    public static void SetCursorPos(int x, int y)
    {
        if (!InputQueue.Post(new InputQueue.Item { Kind = InputQueue.Kind.Cursor, A = x, B = y })) SetCursorPosRaw(x, y);
    }

    // the window to the front (after an empty mouse event: WindowList.BringToFront), a warning when Windows says no
    public static void Front(IntPtr h)
    {
        if (!InputQueue.Post(new InputQueue.Item { Kind = InputQueue.Kind.Front, Hwnd = h })) FrontNow(h);
    }

    static void FrontNow(IntPtr h)
    {
        if (!SetForegroundWindowRaw(h)) Log.Warn($"could not bring '{WindowList.Text(h)}' to the front");
    }

    // the input thread: one queued call
    public static void Send(in InputQueue.Item it)
    {
        switch (it.Kind)
        {
            case InputQueue.Kind.Cursor: SetCursorPosRaw(it.A, it.B); break;
            case InputQueue.Kind.Mouse: SendMouseNow(unchecked((uint)it.A), it.B); break;
            case InputQueue.Kind.Scan: SendScanNow(it.Code, it.Ext, it.Up); break;
            case InputQueue.Kind.Vk: SendVkNow(it.Code, it.Up); break;
            case InputQueue.Kind.Unicode: SendUnicodeNow(it.Text); break;
            case InputQueue.Kind.Ctrl: SendCtrlComboNow(it.Code); break;
            case InputQueue.Kind.Front: FrontNow(it.Hwnd); break;
        }
    }
    [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hWnd, IntPtr id);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);

    public static void SendMouse(uint flags, int data = 0)
    {
        if (!InputQueue.Post(new InputQueue.Item { Kind = InputQueue.Kind.Mouse, A = unchecked((int)flags), B = data })) SendMouseNow(flags, data);
    }

    static void SendMouseNow(uint flags, int data)
    {
        var inp = new INPUT[1];
        inp[0].type = INPUT_MOUSE;
        inp[0].mi.dwFlags = flags;
        inp[0].mi.mouseData = unchecked((uint)data);
        SendInput(1, inp, Marshal.SizeOf<INPUT>());
    }

    public static void SendScan(ushort scan, bool extended, bool up)
    {
        if (!InputQueue.Post(new InputQueue.Item { Kind = InputQueue.Kind.Scan, Code = scan, Ext = extended, Up = up })) SendScanNow(scan, extended, up);
    }

    static void SendScanNow(ushort scan, bool extended, bool up)
    {
        var inp = new INPUT[1];
        inp[0].type = INPUT_KEYBOARD;
        inp[0].ki.wScan = scan;
        inp[0].ki.dwFlags = KEYEVENTF_SCANCODE | (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0);
        SendInput(1, inp, Marshal.SizeOf<INPUT>());
    }

    // types characters directly (no keyboard layout involved) - used by the built-in English layout
    public static void SendUnicode(string text)
    {
        if (!InputQueue.Post(new InputQueue.Item { Kind = InputQueue.Kind.Unicode, Text = text })) SendUnicodeNow(text);
    }

    static void SendUnicodeNow(string text)
    {
        var inp = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                ref var ki = ref inp[i * 2 + j];
                ki.type = INPUT_KEYBOARD;
                ki.ki.wScan = text[i];
                ki.ki.dwFlags = KEYEVENTF_UNICODE | (j == 1 ? KEYEVENTF_KEYUP : 0);
            }
        }
        SendInput((uint)inp.Length, inp, Marshal.SizeOf<INPUT>());
    }

    public static void SendVk(ushort vk, bool up)
    {
        if (!InputQueue.Post(new InputQueue.Item { Kind = InputQueue.Kind.Vk, Code = vk, Up = up })) SendVkNow(vk, up);
    }

    static void SendVkNow(ushort vk, bool up)
    {
        var inp = new INPUT[1];
        inp[0].type = INPUT_KEYBOARD;
        inp[0].ki.wVk = vk;
        inp[0].ki.wScan = (ushort)MapVirtualKeyEx(vk, 0, IntPtr.Zero);
        inp[0].ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        SendInput(1, inp, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int n, IntPtr[] list);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

    public static IntPtr[] InstalledLayouts()
    {
        int n = GetKeyboardLayoutList(0, null);
        var list = new IntPtr[Math.Max(n, 0)];
        if (n > 0) GetKeyboardLayoutList(n, list);
        return list;
    }

    public static void SendCtrlCombo(ushort vk)
    {
        if (!InputQueue.Post(new InputQueue.Item { Kind = InputQueue.Kind.Ctrl, Code = vk })) SendCtrlComboNow(vk);
    }

    static void SendCtrlComboNow(ushort vk)
    {
        var inp = new INPUT[4];
        void K(int i, ushort v, bool up)
        {
            inp[i].type = INPUT_KEYBOARD;
            inp[i].ki.wVk = v;
            inp[i].ki.wScan = (ushort)MapVirtualKeyEx(v, 0, IntPtr.Zero);
            inp[i].ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        }
        K(0, 0x11, false); K(1, vk, false); K(2, vk, true); K(3, 0x11, true);
        SendInput(4, inp, Marshal.SizeOf<INPUT>());
    }

    // ---- keyboard layout ----
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr hkl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buf, int cch, uint flags, IntPtr hkl);

    public static IntPtr ForegroundLayout()
    {
        uint tid = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        return GetKeyboardLayout(tid);
    }

    // ---- monitors ----
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public delegate bool MonitorEnumProc(IntPtr hmon, IntPtr hdc, ref RECT r, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hmon, ref MONITORINFOEX mi);

    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public LUID srcAdapter; public uint srcId, srcModeIdx, srcStatus;
        public LUID tgtAdapter; public uint tgtId, tgtModeIdx, outputTechnology, rotation, scaling, refreshNum, refreshDen, scanLine;
        public int targetAvailable; public uint tgtStatus;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType, id; public LUID adapter;
        public fixed byte union[48];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type, size; public LUID adapter; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags, outputTechnology;
        public ushort edidManufactureId, edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint numModes, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topology);
    [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME info);
    [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME info);

    // ---- window / message loop ----
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint ms, uint wakeMask, uint flags);
    public const uint QS_ALLINPUT = 0x04FF, MWMO_INPUTAVAILABLE = 0x0004;
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

    // ---- WinRT interop for Windows.Graphics.Capture ----
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    public static extern int WindowsCreateString(string src, int length, out IntPtr hstring);
    [DllImport("combase.dll")] public static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll")] public static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("d3d11.dll")] public static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    // ---- PDH (GPU counters, same source Task Manager uses) ----
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhOpenQueryW(string src, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufSize, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] public static extern uint PdhCloseQuery(IntPtr query);
    public const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000, PDH_MORE_DATA = 0x800007D2;
}
