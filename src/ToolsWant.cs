using System.Threading;

namespace Orbit;

// A check of the tools that can fail: its line (what was measured, with the numbers), then its verdict the way the harness reads the
// log - "... False (want True)" is a failure. Every False one is counted, and a tag (ORBIT_SNAP_ONLY) that ran them exits with 1, as
// focus / autotheater / mediabar / extras already do with their own counts: a wrapper that looks at the exit code sees it too.
static partial class Tools
{
    // the checks that came out False in this run
    public static int WantsFailed;

    public static bool Want(string line, bool ok)
    {
        if (!ok) Interlocked.Increment(ref WantsFailed);
        Log.Info($"{line} {ok} (want True)");
        return ok;
    }

    // a tag's exit code: 1 when one of the checks counted since 'from' (WantsFailed when the tag started) failed
    public static int WantsExit(int from) => WantsFailed > from ? 1 : 0;
}
