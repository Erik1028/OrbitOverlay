using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Orbit;

// Paces the overlay loop on its own clock at the headset's refresh rate (a high-resolution waitable timer).
//
// WaitFrameSync, which paced it before 1.9.1, returns once per GAME frame: measured on Erik's PC with VRChat, 63-83 per
// second while the compositor presented 75-85 frames and the display runs at 90 Hz. Every frame the game dropped (and
// the compositor reprojected) the overlays' content skipped too - the cursor, a grabbed panel, mirrored video, the
// watch's motion - and an update waited ~10 ms at the top of the frame before it was composited. On its own grid the
// loop runs 90 times a second whatever the game does, and an update waits half a frame on average.
public sealed class FramePacer : IDisposable
{
    const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2, TIMER_ALL_ACCESS = 0x1F0003;
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    IntPtr _timer;
    double _next;

    public FramePacer()
    {
        // high resolution (~0.5 ms) since Windows 10 1803; without it: 1 ms sleeps and a short spin
        _timer = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    }

    public bool HighResolution => _timer != IntPtr.Zero;
    public double LastLate;                     // how late the last Wait returned after its slot (s; the perf log's "show:" line)

    // Waits for the next slot on a grid of 1/hz. A loop that ran late starts at once (one catch-up, no burst);
    // one that fell a whole frame behind (a hitch) starts a new grid from now.
    public void Wait(double hz)
    {
        double period = 1.0 / Math.Clamp(hz, 10, 240);
        double now = Clock.Now;
        _next += period;
        if (_next < now - period || _next > now + 2 * period) _next = now + period;
        double left = _next - now;
        if (left <= 0) { LastLate = -left; return; }
        if (_timer != IntPtr.Zero)
        {
            long due = -(long)(left * 1e7);          // relative, in 100 ns units
            if (SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                WaitForSingleObject(_timer, 1000);
                LastLate = Clock.Now - _next;
                return;
            }
        }
        while (_next - Clock.Now > 0.0015) Thread.Sleep(1);      // timeBeginPeriod(1) is on while the loop runs
        while (Clock.Now < _next) Thread.SpinWait(50);
        LastLate = Clock.Now - _next;
    }

    public void Dispose()
    {
        if (_timer != IntPtr.Zero) CloseHandle(_timer);
        _timer = IntPtr.Zero;
    }
}
