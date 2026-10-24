using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Orbit;

// What allocates (Erik 10-24: 5-10 MB a second on a thread other than the main loop's, and the private bytes up 500 MB in 30 s): the
// runtime's own GCAllocationTick event - one about every 100 KB allocated, with the type of the object that crossed the mark - summed
// by type and by thread (its name as Windows keeps it: .NET writes a named thread's name there). The perf log's "alloc:" line every
// 10 s, only when the window allocated more than 1 MB a second (a calm window says nothing). ~50 events a second at 5 MB/s: nothing.
public sealed class AllocWatch : EventListener
{
    readonly ConcurrentDictionary<string, long> _byType = new();
    readonly ConcurrentDictionary<int, long> _byThread = new();
    readonly ConcurrentDictionary<int, string> _names = new();
    int _amountAt = -1, _typeAt = -1;

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);     // GC
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal) || e.Payload == null) return;
        if (_amountAt < 0 && e.PayloadNames != null)
        {
            _amountAt = e.PayloadNames.IndexOf("AllocationAmount64");
            if (_amountAt < 0) _amountAt = e.PayloadNames.IndexOf("AllocationAmount");
            _typeAt = e.PayloadNames.IndexOf("TypeName");
        }
        if (_amountAt < 0 || _amountAt >= e.Payload.Count) return;
        long amount = Convert.ToInt64(e.Payload[_amountAt]);
        string type = _typeAt >= 0 && _typeAt < e.Payload.Count ? e.Payload[_typeAt] as string ?? "?" : "?";
        _byType.AddOrUpdate(type, amount, (_, v) => v + amount);
        _byThread.AddOrUpdate((int)e.OSThreadId, amount, (_, v) => v + amount);
    }

    // the window's line (null: under 1 MB a second), and a fresh window
    public string TakeLine(double secs)
    {
        var types = _byType.ToArray();
        var threads = _byThread.ToArray();
        _byType.Clear();
        _byThread.Clear();
        long total = types.Sum(t => t.Value);
        if (secs <= 0 || total / secs < 1 << 20) return null;
        var sb = new StringBuilder($"alloc: {total / secs / 1048576:0.0} MB/s - types:");
        foreach (var t in types.OrderByDescending(t => t.Value).Take(6)) sb.Append($" {t.Key} {t.Value / secs / 1024:0} KB/s;");
        sb.Append(" threads:");
        foreach (var t in threads.OrderByDescending(t => t.Value).Take(4)) sb.Append($" '{Name(t.Key)}' ({t.Key}) {t.Value / secs / 1024:0} KB/s;");
        return sb.ToString();
    }

    string Name(int tid) => _names.GetOrAdd(tid, id =>
    {
        IntPtr h = OpenThread(0x0800, false, (uint)id);        // THREAD_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return "?";
        try
        {
            if (GetThreadDescription(h, out IntPtr p) < 0 || p == IntPtr.Zero) return "?";
            try { string s = Marshal.PtrToStringUni(p); return string.IsNullOrEmpty(s) ? "(no name)" : s; }
            finally { LocalFree(p); }
        }
        finally { CloseHandle(h); }
    });

    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern int GetThreadDescription(IntPtr thread, out IntPtr description);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
}
