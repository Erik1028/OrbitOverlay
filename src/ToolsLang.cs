using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Orbit;

// The tools in another language (General > Language): --snapshot / --faceshot ... --lang hu (or ORBIT_SNAP_LANG=hu) draws every
// screen in it, with that language's dates and numbers where the English pictures use en-US. The snapshot then checks the table
// (L.Check), writes lang_missing.txt - every text it drew that the table has no line for, most drawn first - and its layout lines
// say which language they measured ("layout modern hu: 0 problem(s)").
static partial class Tools
{
    static string LangTag => Lang.English ? "" : " " + Lang.Code;

    // what the docs pictures are drawn in: en-US in English, the language's own culture in another one
    public static System.Globalization.CultureInfo SnapCulture => Lang.English ? new System.Globalization.CultureInfo("en-US") : Lang.Culture;

    public static int Snapshot(string dir, string onlyStyle = null)
    {
        // ORBIT_SNAP_NOW=2026-09-30T23:47:58: every watch's clock held at that moment, so the pictures of two builds can be held
        // against each other pixel by pixel (the clock's digits differed otherwise)
        if (DateTime.TryParse(Environment.GetEnvironmentVariable("ORBIT_SNAP_NOW"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out var fixedNow))
            WristPanel.TestNowDefault = fixedNow;
        Dictionary<string, int> missing = null;
        if (!Lang.English)
        {
            var bad = Lang.Check(Lang.Code);
            foreach (var b in bad) Log.Warn($"lang {Lang.Code}: {b}");
            Log.Info($"lang {Lang.Code}: {bad.Count} problem(s) in its table (want 0)");
            missing = new Dictionary<string, int>(StringComparer.Ordinal);
            // not missing: the changelog's own words (What's new stays English), a text in the language's letters already (a date),
            // a text cut short with an ellipsis (its whole was looked up)
            var log = new System.Text.StringBuilder();
            foreach (var e in WhatsNew.All) { log.Append(e.Title).Append('\n'); foreach (var pt in e.Points) log.Append(pt).Append('\n'); }
            string changelog = log.ToString();
            Lang.Miss = s =>
            {
                if (!s.Any(char.IsLetter) || s.Any(ch => ch > 0x7F && char.IsLetter(ch)) || s.EndsWith('\u2026') || s.EndsWith("...")) return;
                if (s.Length > 3 && changelog.Contains(s, StringComparison.Ordinal)) return;
                missing[s] = missing.TryGetValue(s, out int n) ? n + 1 : 1;
            };
        }
        bool fit = Environment.GetEnvironmentVariable("ORBIT_SNAP_FIT") == "1";
        if (fit) FitStart();
        try { return SnapshotRun(dir, onlyStyle); }
        finally
        {
            Lang.Miss = null;
            if (fit) FitEnd(dir);
            if (missing != null)
            {
                Directory.CreateDirectory(Path.GetFullPath(dir));
                File.WriteAllLines(Path.Combine(Path.GetFullPath(dir), "lang_missing.txt"),
                    missing.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => $"{kv.Value}\t{kv.Key.Replace("\n", "\\n")}"));
                Log.Info($"lang {Lang.Code}: {missing.Count} text(s) drawn without a translation (lang_missing.txt)");
            }
        }
    }

    // ORBIT_SNAP_FIT=1: every text drawn measured against its box (wrapped: its height), the ones that do not fit listed per
    // picture in fit.txt - the same run in English and in another language shows what the translation made too long
    static List<string> _fitPending;
    static SortedDictionary<string, List<string>> _fitFound;
    static int _fitTexts;

    static void FitStart()
    {
        _fitPending = new List<string>();
        _fitFound = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        _fitTexts = 0;
        Painter.FitAll = (p, s, r, size, w, fam, wrap, h, v, tr) =>
        {
            if (fam == Theme.IconFont || string.IsNullOrEmpty(s) || !s.Any(char.IsLetterOrDigit) || p.NoText) return;
            _fitTexts++;
            float over;
            if (wrap) { over = p.MeasureHeight(s, size, w, r.Width, fam) - r.Height; if (over <= 1.5f) return; }
            else { over = p.MeasureTracked(s, size, w, tr, fam) - r.Width; if (over <= 1f) return; }
            _fitPending.Add($"{(wrap ? "tall" : "wide")} +{over:0.#} px [{r.X:0},{r.Y:0} {r.Width:0}x{r.Height:0}] {s.Replace("\n", "\\n")}");
        };
        Surface.Saved = path =>
        {
            if (_fitPending.Count == 0) return;
            string name = Path.GetFileName(path);
            if (!_fitFound.TryGetValue(name, out var l)) _fitFound[name] = l = new List<string>();
            foreach (var x in _fitPending) if (!l.Contains(x)) l.Add(x);
            _fitPending.Clear();
        };
    }

    static void FitEnd(string dir)
    {
        Painter.FitAll = null;
        Surface.Saved = null;
        int n = _fitFound.Values.Sum(l => l.Count);
        File.WriteAllLines(Path.Combine(Path.GetFullPath(dir), "fit.txt"), _fitFound.SelectMany(kv => kv.Value.Select(x => kv.Key + "\t" + x)));
        Log.Info($"text fit{LangTag}: {n} text(s) bigger than their box in {_fitFound.Count} picture(s), {_fitTexts} drawn (fit.txt)");
    }
}
