using System;
using System.Collections.Generic;
using System.Globalization;

namespace Orbit;

// OrbitOverlay's own words in the language picked in General (GeneralConfig.Language). The code is written in English, and
// English is what shows when nothing else is picked (null in the config): drawn exactly as before, nothing is looked up.
// Another language is a table of its own file (src/Lang/Hu.cs): the English text, its translation ({0} {1}: the parts put in).
// A text the table does not have yet simply stays English. A new language = a new table, a line in Languages and in TableOf.
//
// Where the words change:
//  - the painter looks up every text it draws or measures (Painter.Text, TrackedText, MeasureWidth ...; the Terminal's own
//    TerminalSkin.Put / W): a label, a button, a caption, a list of names is translated where it is drawn - no call in the code.
//    An ALL CAPS text is found by its letters and comes back in the language's capitals (NOW PLAYING -> MOST SZÓL).
//  - text made of parts is translated where it is made: Lang.F("{0} monitors", n) - the pattern is the table's key.
//  - code that cuts or measures pieces of a text (an ellipsis, a typewriter, a 95 access key) translates it first: Lang.T(s).
//  - Windows' own controls (the tray menu) ask Lang.T themselves, again when the language changes (Changed).
// Names and data (windows, songs, people) are never keys of a table, so they stay as they are (a single letter never is: the
// keyboard's keys go through the same painter). A word with two meanings in English has a line "context|word" (Lang.C); a
// language's shorter form for where its whole text does not fit, "short|text" (Lang.Short: the cut helpers try it first).
// Dates and numbers: the language's culture on the UI thread (Magyar: hu-HU); English keeps what Windows says, as before.
public static class Lang
{
    // the choices of General > Language, each in its own words
    public static readonly (string Code, string Name)[] Languages = { (null, "English"), ("hu", "Magyar") };

    static (string En, string Tr)[] TableOf(string code) => code switch
    {
        "hu" => Hu.Table,
        _ => null,
    };

    public static string Code { get; private set; }          // null = English
    public static int Version { get; private set; }          // goes up with every change: caches of made text start over
    public static event Action Changed;                      // after a change (the tray menu relabels itself)

    static Dictionary<string, string> _t;                    // English -> the language (null: English)
    static Dictionary<string, string> _caps;                 // ENGLISH IN CAPITALS -> THE LANGUAGE IN CAPITALS
    static HashSet<string> _values;                          // the translations themselves (Miss: not missing)
    static Dictionary<(string, string), string> _ctx;        // the "context|text" lines by (context, text): C and Short
    static CultureInfo _culture, _system;

    public static bool English => _t == null;

    // day and month names, number format: the language's; English keeps the system's (as it always did)
    public static CultureInfo Culture => _culture ?? CultureInfo.CurrentCulture;

    // --snapshot: every text drawn that the table does not have (a translation itself, or a text without letters, is not one)
    public static Action<string> Miss;

    // the config's value to a language of the list (an unknown one: English)
    public static string Normalize(string code)
    {
        foreach (var (c, _) in Languages)
            if (c != null && string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    public static int Index(string code)
    {
        code = Normalize(code);
        for (int i = 0; i < Languages.Length; i++) if (Languages[i].Code == code) return i;      // (no lambda: asked every frame)
        return 0;
    }

    public static string[] Names()
    {
        var n = new string[Languages.Length];
        for (int i = 0; i < n.Length; i++) n[i] = Languages[i].Name;
        return n;
    }

    // picks a language (null = English); the UI thread's culture follows it
    public static void Set(string code)
    {
        _system ??= CultureInfo.CurrentCulture;
        code = Normalize(code);
        if (code == Code && (code == null) == (_t == null)) { ApplyCulture(); return; }
        Dictionary<string, string> t = null, caps = null;
        Dictionary<(string, string), string> ctx = null;
        HashSet<string> values = null;
        CultureInfo culture = null;
        var table = code == null ? null : TableOf(code);
        if (table == null) code = null;
        else
        {
            try { culture = CultureInfo.GetCultureInfo(code == "hu" ? "hu-HU" : code); } catch (CultureNotFoundException) { culture = null; }
            var up = culture ?? CultureInfo.InvariantCulture;
            t = new Dictionary<string, string>(table.Length, StringComparer.Ordinal);
            values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (en, tr) in table)
                // (never a single letter: the keyboard's keys are drawn by the same painter)
                if (en.Length > 1 && !string.IsNullOrEmpty(tr) && t.TryAdd(en, tr))
                {
                    values.Add(tr);
                    if (tr.Contains('&')) values.Add(NoKey(tr));      // drawn without its access key
                }
            caps = new Dictionary<string, string>(StringComparer.Ordinal);
            ctx = new Dictionary<(string, string), string>();
            foreach (var (en, tr) in t)
            {
                if (en.IndexOf('|') is int bar and > 0) ctx[(en[..bar], en[(bar + 1)..])] = tr;
                string ue = en.ToUpperInvariant();
                if (ue == en || t.ContainsKey(ue)) continue;
                string ut = tr.ToUpper(up);
                if (caps.TryAdd(ue, ut)) values.Add(ut);
            }
        }
        _caps = caps;
        _values = values;
        _ctx = ctx;
        _culture = culture;
        _t = t;
        Code = code;
        Version++;
        ApplyCulture();
        Changed?.Invoke();
    }

    static string NoKey(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '&' && i + 1 < s.Length) i++;
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    static void ApplyCulture()
    {
        var c = _culture ?? _system;
        if (c != null && !ReferenceEquals(CultureInfo.CurrentCulture, c)) CultureInfo.CurrentCulture = c;
    }

    // s in the language (itself when the table has no such text, and always in English)
    public static string T(string s)
    {
        var t = _t;
        if (t == null || string.IsNullOrEmpty(s)) return s;
        if (t.TryGetValue(s, out var v)) return v;
        if (_caps.TryGetValue(s, out v)) return v;
        if (Miss != null && !_values.Contains(s)) Miss(s);
        return s;
    }

    // a word that means two things in English ("Muted": a keyboard's soft click, a microphone that is off), or a one-word one that a
    // name could be ("rgb|Static": a friend called Static is not the effect): the table's line "context|word" first, else the word's
    // own line. (Found by its two parts: context + "|" + s made a string on every call, and some are asked in every frame.)
    public static string C(string context, string s)
    {
        var t = _t;
        var c = _ctx;
        if (t == null || c == null || string.IsNullOrEmpty(s)) return s;
        return c.TryGetValue((context, s), out var v) ? v : T(s);
    }

    // a text made of parts: the pattern translated, then filled in (formatted like $"..." - the culture's numbers)
    public static string F(string format, object a) => string.Format(T(format), a);
    public static string F(string format, object a, object b) => string.Format(T(format), a, b);
    public static string F(string format, object a, object b, object c) => string.Format(T(format), a, b, c);
    public static string F(string format, params object[] a) => string.Format(T(format), a);

    // A text made of one part (F), kept until the part, the language or the culture changes: a frame drawn again with the same value
    // makes no new string (a format and a boxed number a frame were much of the settings' and the watch's garbage). A field of the
    // class that draws it (not readonly: it remembers); a call site of its own each.
    public struct Memo<T> where T : IEquatable<T>
    {
        string _s;
        object _how;                 // the format, or the function that made it
        T _v;
        int _lang;
        CultureInfo _culture;

        bool Same(object how, T v) =>
            _s != null && ReferenceEquals(how, _how) && _lang == Version && ReferenceEquals(CultureInfo.CurrentCulture, _culture) && _v.Equals(v);

        void Keep(object how, T v, string s) => (_s, _how, _v, _lang, _culture) = (s, how, v, Version, CultureInfo.CurrentCulture);

        public string F(string format, T v)
        {
            if (!Same(format, v)) Keep(format, v, Lang.F(format, v));
            return _s;
        }

        // made by 'make' (a static lambda: the same one every call, its text depending on v, the language and the culture alone)
        public string Of(T v, Func<T, string> make)
        {
            if (!Same(make, v)) Keep(make, v, make(v));
            return _s;
        }
    }

    // the table has a line for this text (a language's own form of something English has none for: "date|long", "short|Volume")
    public static bool Has(string key) => _t != null && key != null && _t.ContainsKey(key);

    // a shorter form the language has of this English text, for where its whole one does not fit ("short|Monitors": Magyar
    // "Monitor" on a 1x1 tile; a text cut to fit tries it before the ellipsis); null: none (English: always none). Found like C: the cut
    // helpers ask on every frame, and "short|" + english made a string each time (Magyar's garbage at rest)
    public static string Short(string english) =>
        _ctx != null && english != null && _ctx.TryGetValue(("short", english), out var v) ? v : null;

    // T, then in capitals the language's way
    // (English: ToUpperInvariant, as the code did)
    public static string Upper(string s) => string.IsNullOrEmpty(s) ? s : _t == null ? s.ToUpperInvariant() : T(s).ToUpper(Culture);

    // an access key: '&' before a letter ("&&" is a plain &, "Timing & GPU" no key either)
    static bool HasKey(string s)
    {
        for (int i = 0; i + 1 < s.Length; i++)
        {
            if (s[i] != '&') continue;
            if (s[i + 1] == '&') { i++; continue; }
            if (char.IsLetterOrDigit(s[i + 1])) return true;
        }
        return false;
    }

    // --snapshot's check of a table: every {n} of the English kept, no English text twice, nothing translated twice over (a
    // translation that is itself an English text of the table would change again when drawn), access keys (&) kept
    public static List<string> Check(string code)
    {
        var bad = new List<string>();
        var table = TableOf(code);
        if (table == null) { bad.Add("no table"); return bad; }
        var hole = new System.Text.RegularExpressions.Regex(@"\{(\d+)(:[^}]*)?\}");
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (en, tr) in table)
        {
            if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(tr)) { bad.Add($"'{en}': empty"); continue; }
            if (!seen.TryAdd(en, tr)) { bad.Add($"'{en}' twice"); continue; }
            var a = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match m in hole.Matches(en)) a.Add(m.Groups[1].Value);
            var b = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match m in hole.Matches(tr)) b.Add(m.Groups[1].Value);
            if (!a.SetEquals(b)) bad.Add($"'{en}': the parts {{{string.Join(",", a)}}} vs {{{string.Join(",", b)}}}");
            if (HasKey(en) != HasKey(tr)) bad.Add($"'{en}': an access key (&) in one of them only");
        }
        foreach (var (en, tr) in seen)
            if (tr != en && seen.TryGetValue(tr, out var again) && again != tr) bad.Add($"'{en}' -> '{tr}' -> '{again}'");
        return bad;
    }
}
