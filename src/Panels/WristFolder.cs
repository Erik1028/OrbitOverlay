using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The folder widget (WatchFolders) in Modern and Cyber. Closed: its name as the caption, its widgets' icons on a frosted card (the
// Bento look; the 2x2 and the 4 x 3 L show a few of their values too, where they are at hand); Cyber: a DIR tag over a bracketed grid.
// A tap grows it over the watch - a sheet as big as the page, its name and X in the page's first row, its widgets under them on the
// page's own cells, drawn by their own code: live, their buttons act, one with an opened view opens it from here (the folder stays
// open under it) and its close comes back to the folder; the folder's X closes it. The settings' editor shows the folder it works in
// the same way, open over its page (EditFolder: a picture). The 95 family: WristFolder95.cs.
public sealed partial class WristPanel
{
    WatchWidget _folder;                // the folder open under the widget opened from it (null: none)
    WatchWidget _folderSeen;            // the folder drawn open last: a widget new in it since pops in (the editor added it)
    public WatchWidget EditFolder;      // the settings' editor works in this folder: shown open over its page, a picture (null: none)

    // ================= open, back =================

    // OpenDetail: w is one of the open folder's widgets - the folder stays open under w's view
    WatchWidget FolderUnder(WatchWidget w) =>
        _open != null && !_closing && w != _open && WatchFolders.Is(_open) && WatchFolders.Kids(_open).Contains(w) ? _open : null;

    // a view opened from a folder has closed: the folder again, open as it was (null: nothing - back to the face)
    WatchWidget FolderBack()
    {
        var f = _folder;
        _folder = null;
        if (f == null) return null;
        _ui.SpringFrom(OpenKey, 1);
        _openStart = -10;
        _dirty = true;
        return f;
    }

    // DrawDetail(95) with a widget opened from a folder: the folder under it, at rest and only a picture; the widget grows from its
    // place on the folder's grid. The list it is looked for in: the folder's widgets.
    List<WatchWidget> FolderBehind(PanelData d, List<WatchWidget> list)
    {
        if (!FolderHolds(list)) return list;
        bool inert = _ui.Inert;
        _ui.Inert = true;
        FolderSheet(d, _folder, 1);
        _ui.Inert = inert;
        _grid = GridSpec.Folder;
        return _folder.Children;
    }

    // _folder is on this page and holds the opened widget (else it is forgotten)
    bool FolderHolds(List<WatchWidget> list)
    {
        if (_folder == null) return false;
        if (list.Contains(_folder) && _folder.Children != null && _folder.Children.Contains(_open)) return true;
        _folder = null;
        return false;
    }

    // the editor's folder is on this page: open over it, the page under it only a picture
    bool EditFolderOn(List<WatchWidget> list) => EditFolder != null && list.Contains(EditFolder);

    void FolderEditView(PanelData d, List<WatchWidget> list)
    {
        if (!EditFolderOn(list)) return;
        _open = _folder = null;
        _closing = false;
        bool inert = _ui.Inert;
        _ui.Inert = true;
        FolderSheet(d, EditFolder, 1);
        _ui.Inert = inert;
    }

    // the folder opened (DrawDetail's spring: 0 = its tile, 1 = open); on its way it takes no taps
    void FolderView(PanelData d, WatchWidgets.Kind k, float e)
    {
        bool inert = _ui.Inert;
        if (_closing || MathF.Abs(e - 1) > 0.02f) _ui.Inert = true;
        FolderSheet(d, _open, e);
        _ui.Inert = inert;
    }

    // The sheet: from the folder's tile (e = 0) to the whole watch (1), its content growing with it - the name row, then the widgets on
    // the folder's grid (the page's cells), drawn the way the page draws them
    void FolderSheet(PanelData d, WatchWidget f, float e)
    {
        var page = _grid;
        var r = Lerp(WatchWidgets.Rect(f, false, page), new RectangleF(0, 0, TW, TH), e);
        float ec = Math.Clamp(e, 0, 1), rad = CardRadius + (Bento.PageRadius - CardRadius) * ec;
        bool cyber = Theme.Cyber;
        if (!LayoutTest) _p.Fill(new RectangleF(0, 0, TW, TH), Theme.WithAlpha(0xFF000000, 0.62f * ec), Bento.PageRadius);     // the page dims under it
        if (cyber) CyberHud.PanelFrame(_p, r, Theme.OverlayBg, 6 + 12 * ec);
        else
        {
            // frosted: the page's colour with a light over its top, its outline
            _p.FillGradient(r, Theme.Mix(Theme.OverlayBg, 0xFFFFFFFF, 0.03f), Theme.OverlayBg, rad, 90, 0.55f);
            _p.Stroke(RectangleF.Inflate(r, -1, -1), Bento.PageLine, Bento.PageEdge, rad - 1);
        }
        _ui.Clickable("wfsheet", r, out _, out _);        // the sheet takes the laser: a tap on it is not one on the page under it
        float ca = Math.Clamp(e * 3, 0, 1);
        if (ca <= 0.01f) return;
        float gs = MathF.Min(r.Width / TW, r.Height / TH);
        bool zoom = MathF.Abs(gs - 1) > 0.002f, clip = zoom && !LayoutTest;
        if (clip) _p.PushClip(r);
        if (ca < 0.999f) _p.PushFade(ca);
        if (zoom)
        {
            // about the point that puts the watch's centre on the sheet's (as DrawDetail's growing views)
            float rx = r.X + r.Width / 2, ry = r.Y + r.Height / 2;
            _p.PushScale(gs, (rx - gs * TW / 2f) / (1 - gs), (ry - gs * TH / 2f) / (1 - gs));
        }
        var kids = WatchFolders.Kids(f);
        int n = Known(kids);
        FolderHead(f, n, cyber);
        if (n == 0) FolderEmpty(cyber);
        else
        {
            DrawPage(kids, GridSpec.Folder, 0, d, false, Volume.Level >= 0 && Volume.Muted, Now, 10, _folderSeen == f);
            _big = false;
        }
        _grid = page;
        _folderSeen = f;
        if (zoom) _p.PopScale();
        if (ca < 0.999f) _p.PopFade();
        if (clip) _p.PopClip();
    }

    // the name row: the folder's mark, its name big and how many widgets; its X at the right (Cyber: the DIR tag, the name in the HUD's
    // condensed bold)
    void FolderHead(WatchWidget f, int n, bool cyber)
    {
        var row = WatchWidgets.CellRect(0, 0, WatchWidgets.Cols, 1, false);
        const float Xs = 52;
        var xr = new RectangleF(row.Right - 14 - Xs, row.Y + (row.Height - Xs) / 2, Xs, Xs);
        if (Press("wfclose", xr, "\uE711", null, false, !_closing, 20, true)) CloseDetail();
        string name = WatchFolders.Name(f);
        if (cyber)
        {
            float tw = CyberHud.Tag(_p, row.X + 14, row.Y + 16, "DIR");
            CyMono(FolderCount(n), row.X + 14 + tw + 10, row.Y + 16 + 14, xr.X - 16 - (row.X + 24 + tw), CyG);
            float cw = xr.X - 16 - (row.X + 14);
            float cs = MathF.Min(40, MathF.Max(24, FitSize(name, cw, 40, FontWeight.Bold, Theme.Display)));
            TextAt(FCut(name, cw, cs, FontWeight.Bold, Theme.Display), row.X + 14, row.Bottom - 16, cw + 2, CyW, cs, FontWeight.Bold, TextAlignment.Leading,
                Theme.Display);
            return;
        }
        const float Ts = 56;
        var tile = new RectangleF(row.X + 14, row.Y + (row.Height - Ts) / 2, Ts, Ts);
        _p.Fill(tile, Bento.AccentFill, CardRadius);
        _p.Icon(WatchFolders.Icon, tile, Bento.OnAccent, 28);
        float x = tile.Right + 18, w = xr.X - 16 - x;
        float ns = MathF.Max(24, FitSize(name, w, 32, FontWeight.SemiBold, null));
        TextAt(FCut(name, w, ns, FontWeight.SemiBold), x, row.Y + 51, w + 2, Bento.Text, ns, FontWeight.SemiBold);
        TextAt(FolderCount(n), x, row.Y + 79, w + 2, Sub, SmallText, FontWeight.SemiBold);
    }

    // nothing in it yet: a word where its widgets would be
    void FolderEmpty(bool cyber)
    {
        var a = WatchWidgets.CellRect(0, 0, WatchWidgets.Cols, GridSpec.Folder.Rows, false, GridSpec.Folder);
        if (cyber)
        {
            float tw = CyMonoW(Upper("Empty"), 18, FontWeight.Bold);
            CyMono(Upper("Empty"), a.X + (a.Width - tw) / 2, a.Y + a.Height / 2 - 6, tw + 4, CyG, 18, FontWeight.Bold);
            float hw = CyMonoW(Upper(EmptyHint));
            if (hw <= a.Width - 40) CyMono(Upper(EmptyHint), a.X + (a.Width - hw) / 2, a.Y + a.Height / 2 + 22, hw + 4, CyberHud.Faint);
            return;
        }
        _p.Icon(WatchFolders.Icon, new RectangleF(a.X, a.Y + a.Height / 2 - 92, a.Width, 80), Bento.Faint, 56);
        _p.Text("Empty", new RectangleF(a.X, a.Y + a.Height / 2 - 4, a.Width, 34), Sub, 22, FontWeight.SemiBold, TextAlignment.Center);
        _p.Text(EmptyHint, new RectangleF(a.X + 20, a.Y + a.Height / 2 + 32, a.Width - 40, 28), Bento.Faint, SmallText, FontWeight.Normal, TextAlignment.Center);
    }

    const string EmptyHint = "Add widgets to it in the settings";

    // four corner brackets round r, out from it by 'off' (Cyber's mark of a block that belongs together: the closed folder's icons)
    void GridBrackets(RectangleF r, float off, float arm)
    {
        var o = RectangleF.Inflate(r, off, off);
        const float W = 2;
        uint c = CyY;
        _p.Fill(new RectangleF(o.X, o.Y, arm, W), c); _p.Fill(new RectangleF(o.X, o.Y, W, arm), c);
        _p.Fill(new RectangleF(o.Right - arm, o.Y, arm, W), c); _p.Fill(new RectangleF(o.Right - W, o.Y, W, arm), c);
        _p.Fill(new RectangleF(o.X, o.Bottom - W, arm, W), c); _p.Fill(new RectangleF(o.X, o.Bottom - arm, W, arm), c);
        _p.Fill(new RectangleF(o.Right - arm, o.Bottom - W, arm, W), c); _p.Fill(new RectangleF(o.Right - W, o.Bottom - arm, W, arm), c);
    }

    // ================= closed =================

    // the card: frosted - its gradient with a light over the top, a lighter hairline (a look of its own: CardBack draws that one)
    void FolderBack(RectangleF r, bool cards)
    {
        ref readonly var L = ref Bento.Cur;
        uint f0 = cards ? Theme.WithAlpha(L.Fill0, 0.96f) : L.Fill0, f1 = cards ? Theme.WithAlpha(L.Fill1, 0.96f) : L.Fill1;
        _p.FillGradient(r, f0, f1, CardRadius, L.Angle, L.Span);
        _p.FillGradient(r, 0x1CFFFFFF, 0x00FFFFFF, CardRadius, 90, 0.6f);
        _p.Stroke(RectangleF.Inflate(r, -0.6f, -0.6f), 0x2EFFFFFF, Bento.EdgeWidth, CardRadius - 0.6f);
        (_haloF0, _haloF1, _haloAngle, _haloSpan, _haloCard) = (f0, f1, L.Angle, L.Span, r);
    }

    // The closed folder: its name as the caption with how many at the right, its widgets' icons on small tiles (as many as fit, the last
    // a "+3" when there are more); room for bigger tiles (the 2x2, the 4 x 3 L): each with its icon and its value where it has one at
    // hand (the time, a reading, the batteries, the volume, the weather, the time in VR), else its name
    void FolderOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        var kids = WatchFolders.Kids(w);
        int n = Known(kids);
        if (Theme.Cyber) { CyberFolder(r, w, kids, n, d); return; }
        float pad = PadX(r), iw = r.Width - 2 * pad;
        string count = n > 0 ? FolderNum(n) : null;
        float nw = count == null ? 0 : _p.MeasureWidth(count, SmallText, FontWeight.SemiBold);
        float capW = nw > 0 ? iw - nw - 10 : iw;
        CapText(FCutCaps(WatchFolders.Name(w), capW), r.X + pad, r.Y + CapBase, capW, Bento.Cap);
        if (nw > 0) TextAt(count, r.Right - pad - nw, r.Y + CapBase, nw + 2, Sub, SmallText, FontWeight.SemiBold);
        var a = RectangleF.FromLTRB(r.X + pad, r.Y + 36, r.Right - pad, r.Bottom - 11);
        if (n == 0) { _p.Icon(WatchFolders.Icon, a, Bento.Faint, MathF.Round(MathF.Min(a.Height, a.Width) * 0.6f)); return; }
        if (a.Width >= 150 && a.Height >= 140) FolderTiles(a, kids, n, d, false);
        else FolderIcons(a, kids, n, false);
    }

    // the icons: two rows (one where there is no room for two), as many a row as fit, the block in the middle
    void FolderIcons(RectangleF a, List<WatchWidget> kids, int n, bool cyber)
    {
        int rows = a.Height >= 2 * 20 + 4 ? 2 : 1;
        float g = a.Height >= 80 ? 6 : 4;
        float s = MathF.Floor(MathF.Min(40, (a.Height - (rows - 1) * g) / rows));
        int cols = Math.Max(1, (int)((a.Width + g) / (s + g)));
        int cap = rows * cols, shown = n <= cap ? n : cap - 1;
        int used = Math.Min(n, cap), urows = (used + cols - 1) / cols, ucols = Math.Min(used, cols);
        float bw = ucols * (s + g) - g, bh = urows * (s + g) - g;
        float x0 = MathF.Round(a.X + (a.Width - bw) / 2), y0 = MathF.Round(a.Y + (a.Height - bh) / 2);
        if (cyber) GridBrackets(new RectangleF(x0, y0, bw, bh), 3, 7);
        int j = 0;
        foreach (var c in kids)
        {
            var k = c == null ? null : WatchWidgets.Find(c.Type);
            if (k == null) continue;
            if (j >= shown) break;
            var t = new RectangleF(x0 + j % cols * (s + g), y0 + j / cols * (s + g), s, s);
            if (cyber)
            {
                _p.FillCut(t, CyberHud.Card2, 0, 0, MathF.Round(s * 0.22f), 0);
                _p.Icon(k.Icon, t, CyC, MathF.Round(s * 0.5f));
            }
            else
            {
                _p.Fill(t, Bento.Tile, MathF.Round(s * 0.28f));
                _p.Icon(k.Icon, t, Bento.Text, MathF.Round(s * 0.5f));
            }
            j++;
        }
        if (n > cap) More(new RectangleF(x0 + j % cols * (s + g), y0 + j / cols * (s + g), s, s), n - shown, cyber);
    }

    // the bigger tiles: an icon, the widget's value (or its name) under it
    void FolderTiles(RectangleF a, List<WatchWidget> kids, int n, PanelData d, bool cyber)
    {
        const float G = 8;
        int cols = Math.Max(1, (int)((a.Width + G) / (80 + G))), rows = Math.Max(1, (int)((a.Height + G) / (70 + G)));
        float tw = (a.Width - (cols - 1) * G) / cols, th = (a.Height - (rows - 1) * G) / rows;
        int cap = rows * cols, shown = n <= cap ? n : cap - 1;
        if (cyber) GridBrackets(a, 3, 9);
        int j = 0;
        foreach (var c in kids)
        {
            var k = c == null ? null : WatchWidgets.Find(c.Type);
            if (k == null) continue;
            if (j >= shown) break;
            var t = new RectangleF(a.X + j % cols * (tw + G), a.Y + j / cols * (th + G), tw, th);
            FolderTile(t, c, k, d, cyber);
            j++;
        }
        if (n > cap) More(new RectangleF(a.X + j % cols * (tw + G), a.Y + j / cols * (th + G), tw, th), n - shown, cyber);
    }

    void FolderTile(RectangleF t, WatchWidget c, WatchWidgets.Kind k, PanelData d, bool cyber)
    {
        const float P = 10;
        float iw = t.Width - 2 * P;
        string v = FolderValue(c, d);
        if (cyber)
        {
            _p.FillCut(t, CyberHud.Card2, 0, 0, 9, 0);
            _p.Icon(k.Icon, new RectangleF(t.X + P - 3, t.Y + 7, 26, 26), CyC, 18);
            if (v != null && CyValueW(v, 22) <= iw) CyValue(t.X + P, t.Bottom - 12, v, MathF.Min(30, CyFit(v, iw, 30)), CyW);
            else CyMono(FCutMono(Upper(k.Name), iw), t.X + P, t.Bottom - 12, iw + 2, CyG, CyNote, FontWeight.Bold);
            return;
        }
        _p.Fill(t, Bento.Tile, 12);
        _p.Icon(k.Icon, new RectangleF(t.X + P - 3, t.Y + 7, 26, 26), Sub, 18);
        float vs = v == null ? 0 : FitSize(v, iw, _big ? 30 : 26, FontWeight.Bold, Theme.Display);
        if (v != null && vs >= 17) TextAt(v, t.X + P, t.Bottom - 12, iw + 2, Bento.Text, vs, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        else TextAt(FCut(k.Name, iw, SmallText, FontWeight.SemiBold), t.X + P, t.Bottom - 12, iw + 2, Bento.Text, SmallText, FontWeight.SemiBold);
    }

    // "+3": the ones that do not fit
    void More(RectangleF t, int more, bool cyber)
    {
        string s = Plus(more);
        if (cyber)
        {
            _p.StrokeCut(RectangleF.Inflate(t, -0.6f, -0.6f), CyberHud.Line, 1.25f, 0, 0, MathF.Round(MathF.Min(t.Width, t.Height) * 0.22f), 0);
            float mw = CyMonoW(s, CyNote, FontWeight.Bold);
            CyMono(s, t.X + (t.Width - mw) / 2, t.Y + t.Height / 2 + 5, mw + 4, CyG, CyNote, FontWeight.Bold);
            return;
        }
        _p.Stroke(RectangleF.Inflate(t, -0.6f, -0.6f), Bento.Track, 1.25f, MathF.Min(12, MathF.Round(t.Width * 0.28f)));
        float fs = MathF.Max(15, MathF.Min(22, MathF.Round(t.Height * 0.45f)));
        _p.Text(s, t, Sub, fs, FontWeight.SemiBold, TextAlignment.Center, ParagraphAlignment.Center);
    }

    // ---- Cyber ----

    static readonly string[] CapDir = { "DIR" };

    // the DIR tag, the folder's name in monospace beside it, the icons in a bracketed grid
    void CyberFolder(RectangleF r, WatchWidget w, List<WatchWidget> kids, int n, PanelData d)
    {
        float x0 = r.X + PadX(r), tw = CyberCaption(x0, r, r.Width - 2 * PadX(r), CapDir);
        float x = x0 + tw + 8, room = r.Right - 10 - x;
        if (room >= 24) CyMono(FCutMono(Upper(WatchFolders.Name(w)), room), x, r.Y + CyBase, room + 2, CyG);
        var a = RectangleF.FromLTRB(r.X + 14, r.Y + 36, r.Right - 14, r.Bottom - 12);
        if (n == 0)
        {
            float ew = CyMonoW(Upper("Empty"), CyNote, FontWeight.Bold);
            CyMono(Upper("Empty"), a.X + (a.Width - ew) / 2, a.Y + a.Height / 2 + 5, ew + 4, CyberHud.Faint, CyNote, FontWeight.Bold);
            return;
        }
        if (a.Width >= 150 && a.Height >= 140) FolderTiles(a, kids, n, d, true);
        else FolderIcons(a, kids, n, true);
    }

    // ================= what is shown of the widgets =================

    static int Known(List<WatchWidget> kids)
    {
        int n = 0;
        foreach (var c in kids) if (c != null && WatchWidgets.Find(c.Type) != null) n++;
        return n;
    }

    // a widget's value where it is at hand without work of its own (null: none - its name shows)
    string FolderValue(WatchWidget c, PanelData d)
    {
        switch (c.Type)
        {
            case "time":
                return Clk(Now, _cfg.Data.Appearance.Clock24h ? "HH:mm" : "h:mm", CultureInfo.CurrentCulture);
            case "fps" or "reproj" or "gpu" or "cputime" or "vram" or "cpu" or "ram":
                var v = Reading(c.Type, d).value;
                return v == "–" ? null : v;
            case "battery":
                var bats = d.Vr.Batteries;
                if (bats.Count == 0) return null;
                float low = 2;
                foreach (var b in bats) low = MathF.Min(low, b.pct);
                return Pct(ShownPct(low));
            case "volume":
                return Volume.Level < 0 || Volume.Muted ? null : VolumePct(Volume.Level);
            case "session":
                var (sv, su) = SessionValue(d);
                return sv == "–" || su != null ? null : sv;
            case "weather":
                var t = Wxt(d);
                return t.Has ? t.Temp : null;
        }
        return null;
    }

    // ---- words, made once ----

    readonly string[] _fNums = new string[24], _fCounts = new string[24], _fPlus = new string[24];
    int _fWordsLang = -1;

    void FolderWords()
    {
        if (_fWordsLang == Lang.Version) return;
        _fWordsLang = Lang.Version;
        Array.Clear(_fNums);
        Array.Clear(_fCounts);
        Array.Clear(_fPlus);
        Array.Clear(_fObj);
        Array.Clear(_fItems);
        Array.Clear(_fFiles);
    }

    // "5" (the closed folder's caption row)
    string FolderNum(int n)
    {
        FolderWords();
        if (n >= _fNums.Length) return n.ToString(CultureInfo.InvariantCulture);
        return _fNums[n] ??= n.ToString(CultureInfo.InvariantCulture);
    }

    // "5 widgets", "1 widget", "Empty" (the open folder's name row)
    string FolderCount(int n)
    {
        FolderWords();
        if (n >= _fCounts.Length) return Lang.F("{0} widgets", n);
        return _fCounts[n] ??= n == 0 ? Lang.T("Empty") : n == 1 ? Lang.T("1 widget") : Lang.F("{0} widgets", n);
    }

    string Plus(int n)
    {
        FolderWords();
        if (n >= _fPlus.Length) return "+" + n.ToString(CultureInfo.InvariantCulture);
        return _fPlus[n] ??= "+" + n.ToString(CultureInfo.InvariantCulture);
    }

    // a text cut to a width with "…", kept (a folder's name is the user's: cut once, not in every frame)
    readonly Dictionary<(string, float, float, int), string> _fCuts = new();
    int _fCutsVer = -1;

    bool CutKept(string s, float maxW, float size, int how, out string c)
    {
        int ver = Theme.Version * 1000 + Lang.Version;
        if (_fCutsVer != ver || _fCuts.Count > 128) { _fCuts.Clear(); _fCutsVer = ver; }
        return _fCuts.TryGetValue((s, maxW, size, how), out c);
    }

    string CutKeep(string s, float maxW, float size, int how, string c) => _fCuts[(s, maxW, size, how)] = c;

    string FCut(string s, float maxW, float size, FontWeight wt, string family = null)
    {
        int how = (int)wt * 4 + (family == null ? 0 : 1);
        return CutKept(s, maxW, size, how, out var c) ? c : CutKeep(s, maxW, size, how, Ellipsize(s, maxW, size, wt, family));
    }

    // UPPERCASE and letter-spaced, as a caption is drawn
    string FCutCaps(string s, float maxW)
    {
        if (CutKept(s, maxW, CapSize, 2, out var c)) return c;
        string u = Upper(s);
        if (_p.MeasureTracked(u, CapSize, FontWeight.SemiBold, CapTrack) > maxW)
        {
            int k = u.Length;
            while (k > 1 && _p.MeasureTracked(u[..k].TrimEnd() + "…", CapSize, FontWeight.SemiBold, CapTrack) > maxW) k--;
            if (k > 1 && char.IsHighSurrogate(u[k - 1])) k--;
            u = u[..k].TrimEnd() + "…";
        }
        return CutKeep(s, maxW, CapSize, 2, u);
    }

    // Cyber's monospace notes
    string FCutMono(string s, float maxW) =>
        CutKept(s, maxW, CyNote, 3, out var c) ? c : CutKeep(s, maxW, CyNote, 3, Ellipsize(s, maxW, CyNote, FontWeight.SemiBold, CyberHud.Mono));

    // ---- the ids of the widgets in folders (WidgetTotal, the pruning of _wid in DrawCustomFace) ----

    int FolderKidTotal()
    {
        int n = 0;
        for (int i = 0; i < WatchPages.Count(C); i++)
            if (WatchPages.Widgets(C, i) is { } l)
                foreach (var w in l) n += w?.Children?.Count ?? 0;
        return n;
    }

    void FolderKidsInto(HashSet<WatchWidget> live)
    {
        for (int i = 0; i < WatchPages.Count(C); i++)
            if (WatchPages.Widgets(C, i) is { } l)
                foreach (var w in l) if (w?.Children != null) live.UnionWith(w.Children);
    }

    // ================= --snapshot's hooks =================

    // the folder in view's widget opened (as a tap on it would): false when it is not there or has no view
    public bool FolderOpenForTest(WatchWidget folder)
    {
        var l = WatchPages.Widgets(C, CurPage);
        if (l == null || !l.Contains(folder)) return false;
        _folder = null;
        _open = folder;
        _closing = false;
        _openStart = -10;
        _ui.SpringFrom(OpenKey, 1);
        return true;
    }

    public WatchWidget OpenWidget => _open;              // what is open (null: nothing)
    public WatchWidget FolderUnderOpen => _folder;       // the folder under it (null: none)
    public bool Closing => _closing;
}
