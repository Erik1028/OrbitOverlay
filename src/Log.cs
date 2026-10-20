using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Orbit;

// The log: a line is stamped where it is written and queued; a thread of its own writes the queue to the file (and the console)
// every 100 ms, at once for a warning or an error, and everything left as the process ends (ProcessExit; Flush). It was written
// and flushed on the caller's thread - on the main loop that was a file write per line, and the perf log's 5-6 lines every 10 s
// held the overlay up for 0.3-40 ms, and three times for 2.8-6.5 SECONDS while the disk was busy (Erik 10-20, the spike lines).
static class Log
{
    static StreamWriter _w;
    static readonly object _lock = new();
    static readonly ConcurrentQueue<string> _q = new();
    static readonly AutoResetEvent _wake = new(false);
    static Thread _t;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit");

    public static void Init(string name)
    {
        Directory.CreateDirectory(Dir);
        string path = Path.Combine(Dir, name);
        try
        {
            // keep the previous run's log next to the new one
            if (File.Exists(path))
                File.Copy(path, Path.ChangeExtension(path, ".prev.log"), true);
            _w = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
        }
        catch { _w = null; }
        if (_t != null) return;
        _t = new Thread(Run) { IsBackground = true, Name = "OrbitOverlay log", Priority = ThreadPriority.BelowNormal };
        _t.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Flush();
    }

    static void Write(string level, string msg, bool now = false)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {level} {msg}";
        if (_t == null) { Put(line); return; }             // (no log thread yet - before Init: as it comes)
        _q.Enqueue(line);
        if (now) _wake.Set();
    }

    static void Put(string line)
    {
        lock (_lock)
        {
            try { _w?.WriteLine(line); } catch { }
        }
        try { Console.WriteLine(line); } catch { }
    }

    static void Run()
    {
        while (true)
        {
            _wake.WaitOne(100);
            Flush();
        }
    }

    // everything queued, written and on the disk (the log thread; the end of the process)
    public static void Flush()
    {
        lock (_lock)
        {
            while (_q.TryDequeue(out var line))
            {
                try { _w?.WriteLine(line); } catch { }
                try { Console.WriteLine(line); } catch { }
            }
            try { _w?.Flush(); } catch { }
        }
    }

    public static void Info(string msg) => Write("INF", msg);
    public static void Warn(string msg) => Write("WRN", msg, true);
    public static void Error(string msg) => Write("ERR", msg, true);
}
