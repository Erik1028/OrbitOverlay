using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Orbit;

// A window pulled out of a monitor (Erik 10-18: "you grab a window's title bar on the monitor, pull it out, and it becomes a floating
// panel"). The laser drags a window by its title bar on a monitor panel and the beam leaves the monitor into the air (Laser.PopAngle past
// its edge): the drag ends in Windows, the window goes back to where it was on the desk (maximized again if it was), and it is mirrored
// as a window panel of its own - as big as it was on the monitor, at the beam a little nearer than the monitor, facing you, in the hand
// that pulled it until the trigger lets go. Windows > "Pull windows off a monitor" turns it off.
public sealed partial class OrbitApp
{
    const int MaxPopWindows = 6;            // (the Windows tab's limit)
    Func<IPointerTarget, Hand, Vector3, Vector3, float, IPointerTarget> _popOut;

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Native.RECT r);
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_ASYNCWINDOWPOS = 0x4000;
    const int SW_MAXIMIZE = 3;

    IntPtr _restoreHwnd;
    Native.RECT _restoreRect;
    bool _restoreMax;
    double _restoreAt = -1;

    IPointerTarget PopOutWindow(IPointerTarget pressed, Hand hand, Vector3 o, Vector3 d, float dist)
    {
        if (!C.General.PopOut || pressed is not DesktopPanel dp || !_vr.Connected) return null;
        if (!dp.MovingWindow(out var hwnd, out var from, out bool wasMax)) return null;
        if (C.Windows.Count >= MaxPopWindows || IsMirrored(hwnd)) return null;
        WindowInfo info;
        try { info = WindowList.Enumerate().FirstOrDefault(w => w.Hwnd == hwnd); }
        catch (Exception e) { Log.Warn("pull out: " + e.Message); return null; }
        if (info == null) return null;          // (not a window of the Alt-Tab list: a tool window, the desktop)
        // its width on the monitor: the window's pixels now (a maximized one restored by the drag) at the monitor's metres per pixel
        int px = GetWindowRect(hwnd, out var nowRect) ? nowRect.Right - nowRect.Left : from.Right - from.Left;
        // the drag ends in Windows: the cursor first to the monitor's middle (let go at a screen edge, Windows would snap the window to half
        // the screen, at the top maximize it), then the button let go - in this order on the input thread (Sys/InputQueue.cs)
        Native.SetCursorPos(dp.Mon.X + dp.Mon.W / 2, dp.Mon.Y + dp.Mon.H / 2);
        dp.EndDrag();
        _restoreHwnd = hwnd;                    // and back where it was, once its move loop is over (RestoreDueWindow)
        _restoreRect = from;
        _restoreMax = wasMax;
        _restoreAt = Clock.Now + 0.15;
        var wc = AddWindow(info);
        var panel = _windowPanels.FirstOrDefault(p => p.Cfg == wc);
        if (panel == null) return null;
        wc.Width = Math.Clamp(px * (dp.Cfg.Width / Math.Max(1, dp.Mon.W)), 0.25f, 1.6f);
        // at the beam, a little nearer than the monitor, upright and facing you
        var pos = o + d * Math.Clamp(dist * 0.8f, 0.45f, 1.5f);
        // out of a monitor that lies (Erik 10-24: placed parallel to rolled eyes, or overhead lying back): facing you with the monitor's
        // own up, parallel to it - upright it came out 90° to the eyes beside it, overhead edge-on. Upright and reclined rows as ever.
        if (_vr.HmdValid && Placement.TryWorld(dp.Cfg.Place, _vr, out var mw) && MonitorLies(mw) &&
            MathUtil.FacingWithUp(pos, _vr.Hmd.Translation, MathUtil.Up(mw), out var along))
            Placement.SetWorld(wc.Place, _vr, along);
        else
        {
            Vector3 z = (_vr.HmdValid ? _vr.Hmd.Translation - pos : -d) * new Vector3(1, 0, 1);
            z = z.LengthSquared() > 1e-6f ? Vector3.Normalize(z) : Vector3.UnitZ;
            var x = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z));
            Placement.SetWorld(wc.Place, _vr, MathUtil.Basis(x, Vector3.Cross(z, x), z, pos));
        }
        Log.Info($"pulled out of {dp.Mon.Name}: '{info.Title}' ({info.Process}), {wc.Width:0.00} m");
        return panel;
    }

    // a monitor that lies: its top edge more than LieRollDeg off level, or its up past GazeFrame's world-up range (under cos 69.5°:
    // overhead, underfoot). A row GazeFrame placed stays under both: a side monitor of a reclined row tips its top edge by
    // sin(round) * sin(recline), past 40° only from 48° round at 60° back.
    static bool MonitorLies(in Matrix4x4 m)
    {
        Vector3 r = MathUtil.Right(m), u = MathUtil.Up(m);
        if (r.LengthSquared() < 1e-6f || u.LengthSquared() < 1e-6f) return false;
        return MathF.Abs(Vector3.Normalize(r).Y) > MathF.Sin(MathUtil.LieRollDeg * MathF.PI / 180) || Vector3.Normalize(u).Y < 0.35f;
    }

    // the pulled-out window back at its place on the desk (asked of Windows, never waited for: a busy app cannot hold the loop)
    void RestoreDueWindow(double now)
    {
        if (_restoreAt < 0 || now < _restoreAt) return;
        _restoreAt = -1;
        var h = _restoreHwnd;
        _restoreHwnd = IntPtr.Zero;
        if (h == IntPtr.Zero) return;
        SetWindowPos(h, IntPtr.Zero, _restoreRect.Left, _restoreRect.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
        if (_restoreMax) ShowWindowAsync(h, SW_MAXIMIZE);
    }
}
