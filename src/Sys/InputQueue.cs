using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Orbit;

// What OrbitOverlay sends to Windows - the laser's cursor, mouse buttons and the wheel, the keyboard's keys, bringing a window to the
// front - goes out on a thread of its own, in the order it was asked (Erik 10-18, the spike line: SetCursorPos and SendInput held the
// main loop 4-10 ms now and then - Windows waits for other apps' low-level mouse hooks before it returns). The main loop only queues;
// nothing it does reads back what the input changed. Off (the tools, the installer): the call is made at once, as before.
// Flush: everything queued has gone out (closing - a button let go must reach Windows before OrbitOverlay ends).
static class InputQueue
{
    public enum Kind : byte { Cursor, Mouse, Scan, Vk, Unicode, Ctrl, Front }

    public struct Item
    {
        public Kind Kind;
        public int A, B;            // Cursor: x, y; Mouse: flags, data
        public ushort Code;         // Scan / Vk / Ctrl: the code
        public bool Ext, Up;        // Scan: extended, up; Vk: up
        public string Text;         // Unicode: the text
        public IntPtr Hwnd;         // Front: the window
    }

    public static bool On;          // the app sets it at start
    static readonly ConcurrentQueue<Item> _q = new();
    static readonly AutoResetEvent _wake = new(false);
    static Thread _thread;
    static readonly object _start = new();
    static int _queued;

    // queued (true) - or Off: the caller makes the call itself (false)
    public static bool Post(in Item it)
    {
        if (!On) return false;
        if (_thread == null)
            lock (_start)
                if (_thread == null)
                {
                    _thread = new Thread(Run) { IsBackground = true, Name = "OrbitOverlay input", Priority = ThreadPriority.AboveNormal };
                    _thread.Start();
                }
        Interlocked.Increment(ref _queued);
        _q.Enqueue(it);
        _wake.Set();
        return true;
    }

    static void Run()
    {
        while (true)
        {
            _wake.WaitOne();
            while (_q.TryDequeue(out var it))
            {
                try { Native.Send(it); }
                catch (Exception e) { Log.Warn("input: " + e.Message); }
                Interlocked.Decrement(ref _queued);
            }
        }
    }

    // everything queued has gone out (or ms passed)
    public static void Flush(int ms = 500)
    {
        if (_thread == null) return;
        long until = Environment.TickCount64 + ms;
        while (Volatile.Read(ref _queued) > 0 && Environment.TickCount64 < until) Thread.Sleep(1);
    }
}
