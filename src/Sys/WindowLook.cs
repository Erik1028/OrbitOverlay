using System;
using System.Runtime.InteropServices;

namespace Orbit;

// The auto theater's look at a window (Sys/AutoTheater.cs): where it is, whether it has a caption, whether it shows, and its program -
// a handful of cheap calls, a few times a second while the setting is on and SteamVR runs. Read only: nothing is moved or focused.
// The program's name is asked only for a window not seen lately (kept per window and process - a window keeps its process for life,
// and a reused process id comes with another window), so a look at the same windows allocates nothing.
static class WindowLook
{
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out WindowList.RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, char[] name, ref int size);

    const int GWL_STYLE = -16;
    const long WS_CAPTION = 0x00C00000;     // WS_BORDER | WS_DLGFRAME: a title bar
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // the last few windows' programs (the one in front and the one watched; a few more for alt-tab)
    static readonly nint[] _hwnd = new nint[6];
    static readonly uint[] _pid = new uint[6];
    static readonly string[] _name = new string[6];
    static int _next;
    static readonly char[] _buf = new char[1024];

    // the window as it is now; Hwnd 0 when there is none (closed)
    public static void Look(IntPtr h, ref AutoTheater.Win w)
    {
        w = default;
        if (h == IntPtr.Zero || !WindowList.IsWindow(h)) return;
        w.Hwnd = h;
        w.Shown = IsWindowVisible(h) && !WindowList.IsIconic(h) && !WindowList.Cloaked(h);
        if (GetWindowRect(h, out var r)) { w.L = r.Left; w.T = r.Top; w.R = r.Right; w.B = r.Bottom; }
        w.Framed = ((long)GetWindowLongPtr(h, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
        w.Process = ProgramOf(h);
    }

    static string ProgramOf(IntPtr h)
    {
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (pid == 0) return null;
        for (int i = 0; i < _hwnd.Length; i++)
            if (_hwnd[i] == h && _pid[i] == pid) return _name[i];
        string name = Stem(pid);
        // a Store app's window (Movies & TV, Media Player) belongs to ApplicationFrameHost: the app is the process of the window inside it
        // (not there yet while it opens, or while minimised: not kept then, asked again next time)
        bool frame = string.Equals(name, "applicationframehost", StringComparison.Ordinal);
        if (frame)
        {
            var inner = FindWindowEx(h, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
            uint ipid = 0;
            if (inner != IntPtr.Zero) Native.GetWindowThreadProcessId(inner, out ipid);
            if (ipid == 0 || ipid == pid) return name;
            name = Stem(ipid);
        }
        if (name == null) return null;              // can't be asked (elevated): asked again next time, nothing kept
        _hwnd[_next] = h;
        _pid[_next] = pid;
        _name[_next] = name;
        _next = (_next + 1) % _hwnd.Length;
        return name;
    }

    // "C:\Program Files\VideoLAN\VLC\vlc.exe" -> "vlc" (null when the process can't be asked: an elevated one from a normal Orbit)
    static string Stem(uint pid)
    {
        IntPtr ph = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (ph == IntPtr.Zero) return null;
        try
        {
            int n = _buf.Length;
            if (!QueryFullProcessImageName(ph, 0, _buf, ref n) || n <= 0) return null;
            var path = new ReadOnlySpan<char>(_buf, 0, n);
            int slash = path.LastIndexOf('\\');
            var file = slash >= 0 ? path[(slash + 1)..] : path;
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file = file[..^4];
            return file.ToString().ToLowerInvariant();
        }
        finally { CloseHandle(ph); }
    }
}
