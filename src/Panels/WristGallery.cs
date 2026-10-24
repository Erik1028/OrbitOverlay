using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Orbit;

// The opened photo view's gallery and Delete (Erik 10-24: "több funkció a fotó widgetben, törlés, fotógaléria, mint vrgallery a
// csuklómon"). Every photo view (the latest photo's, On this day's) has two more buttons: Gallery - a grid of every VRChat photo
// (Sys/PhotoAlbum.cs), newest first under each day's heading, the stick scrolling it; a tap on a photo opens it big in the same view
// (its arrows and the stick then step through all of them, Gallery back to the grid) - and Delete: a first tap asks ("Delete?",
// red, 4 s), a second moves the photo into the Recycle Bin (the app: PhotoBin, never deleted for good), the view stepping to the
// one before it. Modern and Cyber: the two over the photo's top right, the grid in the card; the retro styles: in the viewer's own
// toolbar, the grid in the style's list box with its scroll bar (its font, its selection); the Terminal: "> ls", the files listed.
// At rest nothing is made: the grid's layout is kept per snapshot and width, the days' headings per language and day, the cells'
// bitmaps (a few dozen) by their pixels.
public sealed partial class WristPanel
{
    const string GridGlyph = "", DeleteGlyph = "", HeartGlyph = "\uEB52", HeartLine = "\uEB51";
    const string GalleryWord = "Gallery", DeleteAsk = "Delete?", Moved = "Moved to the Recycle Bin";
    const float DelArmLen = 4, DelNoteLen = 3.5f;

    // the app: move this photo into the Recycle Bin (on a thread), then done(null) - or done(why not) - from that thread; null in the
    // tools: a second tap is only noted (LastDeletedPhoto) and taken as done
    public Action<string, Action<string>> OnDeletePhoto;
    public string LastDeletedPhoto { get; private set; }

    readonly PhotoSet _album = new()
    {
        Album = true, Cap = new[] { GalleryWord, GalleryWord }, CapHud = new[] { GalleryWord, GalleryWord }, None = "No photos yet",
        Hint = "Photos you take in VRChat show up here.", Tag = GalleryWord, TitleXp = "My Pictures", TitleMac = "Scrapbook",
    };

    bool _gal, _galGrid;                   // the album browsed (its set drawn by the opened view); the grid (else one of its photos big)
    bool _galPicked;                       // a photo of the grid was opened (Gallery from the grid goes back to it, else out)
    float _galScroll;
    string _galAnchor;                     // the photo the grid scrolls to when it opens (the one that was big)
    int _galCellsSeen = -1;
    string _delArmed;                      // the photo whose Delete asked
    double _delArmAt;
    volatile string _delBusy;              // being moved
    volatile bool _delDone;
    volatile string _delWhy;
    string _delNote;                       // what the last Delete came to, for a moment
    double _delNoteAt;
    bool _delNoteBad;

    // the gallery is open (the app keeps the album listed meanwhile: OrbitApp.Photos.cs)
    public bool GalleryOn => _gal && _open != null && !_closing && (DetailIs("photo") || DetailIs("memory"));
    public bool GalleryGridShown => GalleryOn && _galGrid;          // --snapshot's hooks
    public string GalleryNote => _delNote;
    public float GalleryScroll => _galScroll;
    public string LastGalleryPick { get; private set; }
    public string GalleryOpenShown => _album.OpenShown;          // (PhotoOpenShown outside a frame is the latest photo's set)
    public bool DeleteArmedShown => _delArmed != null && Clock.Now - _delArmAt <= DelArmLen;

    // the opened view's set: the album's while the gallery is open, else its widget's
    PhotoSet OpenSet => _gal ? _album : DetailIs("memory") ? _memory : _latest;
    SetScope UseOpenSet() => new(this, OpenSet);

    void GalleryReset()
    {
        _gal = _galGrid = _galPicked = false;
        _galFav = false;
        _galWorld = _galPerson = _galSpan = null;
        _galPeople = null;
        _galShow = _galZoom = _zoomTool = false;
        _galAnchor = null;
        _delArmed = null;
        FromNewest(_album);
    }

    // Gallery: the grid, scrolled to the photo shown; from the grid back: the album's photo it opened, else the view it came from
    void GalleryToggle(PhotoWatcher.Photo shown)
    {
        _delArmed = null;
        _dirty = true;
        if (_galShow) ShowStop();                   // (a retro viewer's Gallery while the slideshow plays: it ends)
        if (_gal && _galGrid)
        {
            if (_galPicked) _galGrid = false;
            else _gal = false;
            _galGrid = false;
            return;
        }
        if (!_gal) _galPicked = false;
        _galAnchor = shown?.Path;
        _gal = _galGrid = true;
    }

    // a photo of the grid tapped: big, in the album's set
    void GalleryPick(int index, string path)
    {
        LastGalleryPick = path;
        _album.Index = index;
        _album.Path = path;
        _album.Acc = 0;
        _galGrid = false;
        _galPicked = true;
        _delArmed = null;
        _dirty = true;
    }

    // the redraws the gallery asks for: the album listed again, a cell's picture made, Delete's answer, its ask or note run out
    bool GalleryChanged(PanelData d)
    {
        bool ch = false;
        if (_delDone) { _delDone = false; DeleteFinished(); ch = true; }
        if (GalleryOn)
        {
            if (!ReferenceEquals(GalView(d.Album), _album.Snap)) ch = true;
            int cv = (d.Album?.Source as IPhotoCells)?.CellVersion ?? 0;
            if (cv != _galCellsSeen) { _galCellsSeen = cv; ch |= _galGrid; }
        }
        double now = Clock.Now;
        if (_galShow && (_open == null || _closing || !(DetailIs("photo") || DetailIs("memory")) || (_gal && _galGrid))) _galShow = false;     // (closed while it played, or the grid over it: it ends)
        if (_galShow && now - _galShowAt >= SlideSeconds) ch = true;           // the slideshow's next slide
        if (!ReferenceEquals(Hi, _hiSeen)) { _hiSeen = Hi; ch |= _galZoom; }   // the zoom's sharp copy came
        if (_sendDone || (_sendArmed != null && now - _sendArmAt > DelArmLen)) ch = true;
        if (_delArmed != null && now - _delArmAt > DelArmLen) { _delArmed = null; ch = true; }
        if (_delNote != null && now - _delNoteAt > DelNoteLen) { _delNote = null; ch = true; }
        return ch;
    }

    // ---- the filters (Erik 10-24: "szűrni világra vagy emberre", VR Gallery's favourites): what the grid and the big view go through -
    // a snapshot of the album's matching photos, made when the album or a filter changes (the album's thread fills in the world, the
    // people and the favourites from VR Gallery's library) ----

    bool _galFav;
    string _galWorld, _galPerson;
    string _galSpan;                        // a visit of the recap's Today (WristToday.cs): its world's name, the photos taken from - to
    DateTime _galFrom, _galTo;
    PhotoWatcher.Snapshot _gvIn, _gvOut;
    (bool, string, string, long) _gvKey;
    PhotoWatcher.Photo _galPeople;          // whose people the picker shows (null: closed)

    bool GalFiltered => _galFav || _galWorld != null || _galPerson != null || _galSpan != null;
    string GalFilterName => _galWorld ?? _galPerson ?? _galSpan;
    public string GalleryFilterShown => GalFilterName ?? (_galFav ? "favourites" : null);      // --snapshot
    public bool GalleryPeopleShown => _galPeople != null;
    public void GalleryNoteForTest() => _delNote = null;

    PhotoWatcher.Snapshot GalView(PhotoWatcher.Snapshot all)
    {
        if (all == null || !GalFiltered) return all;
        var key = (_galFav, _galWorld, _galPerson, _galSpan != null ? _galFrom.Ticks ^ _galTo.Ticks : 0);
        if (ReferenceEquals(all, _gvIn) && key == _gvKey) return _gvOut;
        var s = new PhotoWatcher.Snapshot { Version = all.Version, Ready = all.Ready, FolderFound = all.FolderFound, Folder = all.Folder, Source = all.Source };
        foreach (var p in all.Recent)
        {
            if (_galFav && !p.Favorite) continue;
            if (_galWorld != null && p.World != _galWorld) continue;
            if (_galPerson != null && !HasPerson(p, _galPerson)) continue;
            if (_galSpan != null && (p.Taken < _galFrom || p.Taken > _galTo)) continue;
            s.Recent.Add(p);
        }
        (_gvIn, _gvKey, _gvOut) = (all, key, s);
        return s;
    }

    static bool HasPerson(PhotoWatcher.Photo p, string name)
    {
        foreach (var n in p.Players) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // a filter set (from the grid's heading, a photo's world or person): the grid, the photo it came from (or the one at the top) kept in view
    void GalFilter(bool fav, string world, string person, string keep)
    {
        if (world != null || person != null) _galSpan = null;        // (a visit's span stays under Favourites; a world or a person replaces it)
        (_galFav, _galWorld, _galPerson) = (fav, world, person);
        if (keep != null) _galAnchor = keep; else _galAnchorTop = _galTopPhoto;
        _gal = _galGrid = true;
        _galPicked = false;
        _galPeople = null;
        _delArmed = null;
        if (_galShow) ShowStop();                   // (the grid over the slideshow: it ends - it went on under it, a redraw every frame)
        _dirty = true;
    }

    // the filter's chip / status line tapped: every photo again (Favourites stays as it is)
    void GalClearFilter()
    {
        _galSpan = null;
        GalFilter(_galFav, null, null, null);
    }

    // Today's visit tapped (WristToday.cs; the photo view open already): the gallery with the photos taken there and then
    void GalleryOpenSpan(string name, DateTime from, DateTime to)
    {
        (_galSpan, _galFrom, _galTo) = (string.IsNullOrEmpty(name) ? "?" : name, from, to);
        _galFav = false;
        _galWorld = _galPerson = null;
        _gal = _galGrid = true;
        _galPicked = false;
        _galAnchor = _galAnchorTop = null;
        _galScroll = 0;
        _dirty = true;
    }

    // a photo's world, tapped: its photos
    void GalTapWorld(string id, RectangleF r, PhotoWatcher.Photo p, uint line)
    {
        if (p?.World == null) return;
        if (GalTapLine(id, r, line)) GalFilter(_galFav, p.World, null, p.Path);
    }

    // its people, tapped: one - their photos; more - the picker
    void GalTapPeople(string id, RectangleF r, PhotoWatcher.Photo p, uint line)
    {
        if (p == null || p.Players.Length == 0) return;
        if (!GalTapLine(id, r, line)) return;
        if (p.Players.Length == 1) GalFilter(_galFav, null, p.Players[0], p.Path);
        else GalPeopleOpen(p);
    }

    // the picker opened over the view: the slideshow ends (its photo's people are picked from)
    void GalPeopleOpen(PhotoWatcher.Photo p)
    {
        _galPeople = p;
        if (_galShow) ShowStop();
        _dirty = true;
    }

    // a line of text that is a link: underlined under the pointer
    bool GalTapLine(string id, RectangleF r, uint line)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out _) && !_ui.Inert;
        if (hover && !_ui.Inert) _p.Fill(new RectangleF(r.X, r.Bottom - 2, r.Width, Theme.Classic ? 1 : 1.5f), line);
        return click;
    }

    // modern / Cyber: the people as chips over the card - a tap shows their photos, a tap beside closes it
    void GalPeopleSheet(RectangleF E)
    {
        var p = _galPeople;
        if (p == null) return;
        if (!(DetailIs("photo") || DetailIs("memory")) || _closing || p.Players.Length == 0) { _galPeople = null; return; }
        bool cyber = Theme.Cyber;
        if (_ui.Clickable("wpscrim", E, out _, out _) && !_ui.Inert) { _galPeople = null; _dirty = true; return; }
        _p.Fill(E, 0xA6000000, CardRadius);
        int n = Math.Min(p.Players.Length, 16);
        float pw = E.Width - 96, inner = pw - 44, chH = 44, gap = 10;
        // the chips' rows (measured each frame: a dozen names at most, the painter keeps their layouts)
        int rows = 1;
        float rx = 0;
        for (int i = 0; i < n; i++)
        {
            float cw = MathF.Min(inner, _p.MeasureWidth(p.Players[i], 17, FontWeight.SemiBold) + 34);
            if (rx > 0 && rx + cw > inner) { rows++; rx = 0; }
            rx += cw + gap;
        }
        float ph = 22 + 34 + rows * (chH + gap) - gap + 22;
        var panel = new RectangleF(E.X + 48, E.Y + MathF.Max(70, (E.Height - ph) / 2), pw, ph);
        if (cyber) CyberHud.CardShape(_p, panel, CyberHud.Card2, CyC);
        else
        {
            _p.Fill(panel, Bento.Card, CardRadius);
            _p.Stroke(RectangleF.Inflate(panel, -0.6f, -0.6f), Bento.Edge, Bento.EdgeWidth, CardRadius - 0.6f);
        }
        _ui.Clickable("wppanel", panel, out _, out _);          // (a tap on the sheet itself does not close it)
        string title = Lang.T("Show the photos with");
        if (cyber) CyMono(Upper(title), panel.X + 22, panel.Y + 38, inner, CyY, CyNote, FontWeight.Bold);
        else TextAt(PhotoCut(title, inner, 20), panel.X + 22, panel.Y + 40, inner + 2, Bento.Text, 20, FontWeight.SemiBold);
        float x = panel.X + 22, y = panel.Y + 22 + 34;
        for (int i = 0; i < n; i++)
        {
            float cw = MathF.Min(inner, _p.MeasureWidth(p.Players[i], 17, FontWeight.SemiBold) + 34);
            if (x > panel.X + 22 && x + cw > panel.X + 22 + inner) { x = panel.X + 22; y += chH + gap; }
            if (Press(Ui.Id("wpp", i), new RectangleF(x, y, cw, chH), null, p.Players[i], false, true, 18)) { GalFilter(_galFav, null, p.Players[i], p.Path); return; }
            x += cw + gap;
        }
    }

    // a retro style: the people in a little window of the style - its list, Cancel
    Action<int, RectangleF, bool> _personRow;
    void GalPeople95(RectangleF win)
    {
        var p = _galPeople;
        if (p == null) return;
        if (!(DetailIs("photo") || DetailIs("memory")) || _closing || p.Players.Length == 0) { _galPeople = null; return; }
        _ui.Clickable("wpscrim", win, out _, out _);             // (the viewer under it takes no taps meanwhile)
        int n = p.Players.Length, rows = Math.Min(n, 6);
        float w = MathF.Min(win.Width - 40, 190), h = 3 + 18 + 6 + rows * 16 + 4 + 8 + 23 + 8;
        var dlg = new RectangleF(MathF.Floor(win.X + (win.Width - w) / 2), MathF.Floor(win.Y + (win.Height - h) / 2), w, h);
        _p.Fill(dlg, Theme.W95Face);
        _p.Bevel(dlg, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(dlg.X + 3, dlg.Y + 3, dlg.Width - 6, 18), Theme.Retro == RetroStyle.Terminal ? "PEOPLE" : "People");
        if (_ui.CaptionClose95("wpx", dlg.Right - 5 - 16, dlg.Y + 5, true, 6)) { _galPeople = null; _dirty = true; return; }
        var lr = new RectangleF(dlg.X + 8, dlg.Y + 27, dlg.Width - 16, rows * 16 + 4);
        int hit = _ui.List95("wplist", lr, n, -1, _personRow ??= PersonRow95);
        if (_ui.Button95("wpcancel", new RectangleF(dlg.Right - 8 - 75, dlg.Bottom - 8 - 23, 75, 23), "Cancel")) { _galPeople = null; _dirty = true; return; }
        if (hit >= 0 && hit < n) GalFilter(_galFav, null, p.Players[hit], p.Path);
    }

    void PersonRow95(int i, RectangleF r, bool sel)
    {
        var p = _galPeople;
        if (p == null || i < 0 || i >= p.Players.Length) return;
        _ui.Text95(PhotoCut(p.Players[i], r.Width - 6, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(r.X + 3, r.Y + 1, r.Width - 6, 14), sel ? Wht95 : Blk95);
    }

    // what the filter shows, for a retro status line ("Showing: Quiet Meadow - tap to show all"); null: no world / person filter
    string _galFiltText, _galFiltOf;
    int _galFiltLang = -1;
    string GalFilterText()
    {
        string f = GalFilterName;
        if (f == null) return null;
        if (!ReferenceEquals(f, _galFiltOf) || _galFiltLang != Lang.Version) { (_galFiltOf, _galFiltLang) = (f, Lang.Version); _galFiltText = Lang.F("Showing: {0} - tap to show all", f); }
        return _galFiltText;
    }

    // the grid's status line (a retro style): the filter, tapped - all again; else Delete's words or the hint
    void GalStatus95(RectangleF st, string ds)
    {
        string f = GalFilterText();
        if (f != null)
        {
            bool click = _ui.Clickable("wgclear", st, out _, out _) && !_ui.Inert;
            _ui.Pane95(st, PhotoCut(f, st.Width - 8, Ui.F95, FontWeight.Normal, null, 1));
            if (click) GalClearFilter();
            return;
        }
        _ui.Pane95(st, PhotoCut(Lang.T(ds ?? "Tap a photo to open it"), st.Width - 8, Ui.F95, FontWeight.Normal, null, 1));
    }

    static readonly string[] Heart95 = { ".kk.kk.", "kkkkkkk", "kkkkkkk", ".kkkkk.", "..kkk..", "...k...", "......." };

    // a retro toolbar's Favourites toggle: held in while on, the heart red
    bool HeartButton95(string id, RectangleF r)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held) && !_ui.Inert;
        Theme.RetroSkin.ButtonFace(_p, r, false, held || _galFav, false, hover);
        float o = held || _galFav ? 1 : 0;
        _p.Pixels(Heart95, MathF.Floor(r.X + (r.Width - 7) / 2) + o, MathF.Floor(r.Y + (r.Height - 6) / 2) + o, _galFav ? 0xFFC00000 : Blk95);
        if (click) GalFilter(!_galFav, _galWorld, _galPerson, null);
        return click;
    }

    // ---- Delete ----

    bool DeleteArmed(PhotoWatcher.Photo p) => p != null && _delArmed == p.Path && Clock.Now - _delArmAt <= DelArmLen;
    bool DeleteBusy => _delBusy != null;

    void DeleteTap(PhotoWatcher.Photo p)
    {
        if (p == null || _delBusy != null) return;
        _dirty = true;
        if (_galShow) ShowStop();                   // (a retro viewer's Delete while the slideshow plays: it ends on that photo)
        if (!DeleteArmed(p)) { _delArmed = p.Path; _delArmAt = Clock.Now; _delNote = null; return; }
        _delArmed = null;
        _delBusy = p.Path;
        LastDeletedPhoto = p.Path;
        if (OnDeletePhoto == null) { _delWhy = null; _delDone = true; return; }
        OnDeletePhoto(p.Path, why => { _delWhy = why; _delDone = true; });
    }

    // Delete's answer (on the frame's thread): moved - every view on it steps to the one before (else after) it, the snapshots drop it
    // soon after; not - why, for a moment
    void DeleteFinished()
    {
        string path = _delBusy, why = _delWhy;
        _delBusy = null;
        _delNoteBad = why != null;
        _delNote = why ?? Moved;
        _delNoteAt = Clock.Now;
        if (why != null || path == null) return;
        foreach (var s in new[] { _latest, _memory, _album })
        {
            if (s.Path != path || s.Snap == null) continue;
            int n = s.Snap.Recent.Count;
            int i = s.Index + 1 < n ? s.Index + 1 : s.Index - 1;
            if (i < 0 || i >= n) { s.Path = null; continue; }
            s.Index = i;
            s.Path = s.Snap.Recent[i].Path;
        }
    }

    // ---- the grid's layout: All - one sheet of photos, square, almost no gap (VR Gallery's All, Erik 10-24: "mutathatná az all listát
    // folyamatosan mint a vrgallery"); Days - each day a heading and its rows. The months' first rows for the rail. Kept per snapshot and size ----

    sealed class GalDay { public DateTime Day; public int First, Count; public float Y, End; public string Worlds; }

    sealed class GalLayout
    {
        public PhotoWatcher.Snapshot Snap;
        public int Cols;
        public bool ByDay;
        public float CellW, CellH, HeadH, Gap, Height;
        public readonly List<GalDay> Days = new();              // (All: one, the whole sheet, no heading)
        public readonly List<(float y, int year, int month)> Months = new();      // the newest month first
    }

    readonly GalLayout _galL = new();

    GalLayout GalLayoutOf(PhotoWatcher.Snapshot s, int cols, float cw, float ch, float headH, float gap, bool byDay)
    {
        var L = _galL;
        if (ReferenceEquals(L.Snap, s) && L.Cols == cols && L.CellW == cw && L.CellH == ch && L.HeadH == headH && L.Gap == gap && L.ByDay == byDay)
            return L;
        (L.Snap, L.Cols, L.CellW, L.CellH, L.HeadH, L.Gap, L.ByDay) = (s, cols, cw, ch, headH, gap, byDay);
        L.Days.Clear();
        L.Months.Clear();
        float y = 0;
        var list = s?.Recent;
        int n = list?.Count ?? 0;
        if (!byDay && n > 0)
        {
            int rows = (n + cols - 1) / cols;
            L.Days.Add(new GalDay { Day = PhotoWatcher.DayOf(list[0].Taken), First = 0, Count = n, Y = 0, End = rows * (ch + gap) });
            y = rows * (ch + gap);
        }
        for (int i = 0; byDay && i < n;)
        {
            var day = PhotoWatcher.DayOf(list[i].Taken);
            int j = i + 1;
            while (j < n && PhotoWatcher.DayOf(list[j].Taken) == day) j++;
            int rows = (j - i + cols - 1) / cols;
            float end = y + headH + rows * (ch + gap);
            L.Days.Add(new GalDay { Day = day, First = i, Count = j - i, Y = y, End = end, Worlds = WorldsOf(list, i, j) });
            y = end;
            i = j;
        }
        L.Height = y;
        int ym = -1;
        for (int i = 0; i < n; i++)
        {
            var d = PhotoWatcher.DayOf(list[i].Taken);
            int k = d.Year * 12 + d.Month;
            if (k == ym) continue;
            ym = k;
            float my = GalRowY(L, i);
            if (byDay) foreach (var g in L.Days) if (g.First == i) { my = g.Y; break; }
            L.Months.Add((my, d.Year, d.Month));
        }
        return L;
    }

    // a day's worlds for its heading ("Quiet Meadow, Aurora Overlook +2"; null: none known) - made with the layout
    static string WorldsOf(List<PhotoWatcher.Photo> list, int i, int j)
    {
        List<string> w = null;
        for (int k = i; k < j; k++)
            if (list[k].World is { } x && (w == null || !w.Contains(x))) (w ??= new List<string>()).Add(x);
        if (w == null) return null;
        return w.Count <= 2 ? string.Join(", ", w) : w[0] + ", " + w[1] + " +" + (w.Count - 2).ToString(CultureInfo.InvariantCulture);
    }

    // where photo i's cell is in the layout (y of its row)
    static float GalRowY(GalLayout L, int i)
    {
        foreach (var d in L.Days)
            if (i < d.First + d.Count) return d.Y + L.HeadH + (i - d.First) / L.Cols * (L.CellH + L.Gap);
        return 0;
    }

    // a day's heading: "Today · 12", "Yesterday · 3", "October 17 · 8", with its year when not this one; made once per day and language
    readonly Dictionary<(DateTime, int), string> _galHeads = new();
    int _galHeadsLang = -1;
    DateTime _galHeadsToday;

    string GalHead(DateTime day, int count)
    {
        var today = PhotoWatcher.DayOf(DateTime.Now);
        if (_galHeadsLang != Lang.Version || _galHeadsToday != today || _galHeads.Count > 400)
        {
            _galHeads.Clear();
            (_galHeadsLang, _galHeadsToday) = (Lang.Version, today);
        }
        if (_galHeads.TryGetValue((day, count), out var s)) return s;
        var culture = CultureInfo.CurrentCulture;
        int ago = (int)(today - day).TotalDays;
        string when = ago == 0 ? Lang.T("Today") : ago == 1 ? Lang.T("Yesterday")
            : day.Year == today.Year ? Capital(day.ToString(culture.DateTimeFormat.MonthDayPattern, culture)) : MemoryDate(day, culture);
        _galHeads[(day, count)] = s = when + " · " + count.ToString(culture);
        return s;
    }

    // "1,903 photos": made when the count or the language changes
    string _galCount;
    int _galCountN = -1, _galCountLang = -1;

    string GalCountText(int n)
    {
        if (n != _galCountN || _galCountLang != Lang.Version)
        {
            (_galCountN, _galCountLang) = (n, Lang.Version);
            _galCount = n == 1 ? Lang.T("1 photo") : Lang.F("{0} photos", n.ToString("N0", CultureInfo.CurrentCulture));
        }
        return _galCount;
    }

    // the titles made of the folder's name ("Imaging - VRChat"; null: the name alone), kept per pattern, folder and language
    string _galTitle, _galTitleOf, _galTitleFolder;
    int _galTitleLang = -1;

    string GalTitle(string pattern)
    {
        string f = GalFolderName(_ph);
        if (!ReferenceEquals(pattern, _galTitleOf) || !ReferenceEquals(f, _galTitleFolder) || _galTitleLang != Lang.Version || _galTitle == null)
        {
            (_galTitleOf, _galTitleFolder, _galTitleLang) = (pattern, f, Lang.Version);
            _galTitle = pattern == null ? f : Lang.F(pattern, f);
        }
        return _galTitle;
    }

    string _galFolder, _galFolderOf;
    string GalFolderName(PhotoWatcher.Snapshot s)
    {
        string f = s?.Folder ?? "";
        if (!ReferenceEquals(f, _galFolderOf)) { _galFolderOf = f; _galFolder = string.IsNullOrEmpty(f) ? "VRChat" : Path.GetFileName(f.TrimEnd('\\', '/')); }
        return _galFolder;
    }

    // ---- the cells' bitmaps: a few dozen kept by their pixels, the least recently drawn let go; each with when it came (its fade-in) ----

    readonly uint[][] _gbPx = new uint[64][];
    readonly ID2D1Bitmap[] _gbBmp = new ID2D1Bitmap[64];
    readonly long[] _gbUsed = new long[64];
    readonly double[] _gbBorn = new double[64];
    long _gbTick;

    // a grid cell's picture: its own thumbnail when it has one (the big view's, the made-up photos'), else the album's cell
    ID2D1Bitmap GalBitmap(PhotoWatcher.Photo p, IPhotoCells cells, out float w, out float h, out double born)
    {
        uint[] px = p.Thumb;
        int pw = p.TW, ph = p.TH;
        if (px == null && cells != null) (px, pw, ph) = cells.Cell(p.Path);
        (w, h, born) = (pw, ph, 0);
        if (px == null) return null;
        int free = 0;
        for (int i = 0; i < _gbPx.Length; i++)
        {
            if (ReferenceEquals(_gbPx[i], px)) { _gbUsed[i] = ++_gbTick; born = _gbBorn[i]; return _gbBmp[i]; }
            if (_gbUsed[i] < _gbUsed[free]) free = i;
        }
        _gbBmp[free]?.Dispose();
        _gbPx[free] = px;
        _gbBmp[free] = _p.CreateBitmap(px, pw, ph);
        _gbUsed[free] = ++_gbTick;
        born = _gbBorn[free] = Clock.Now;
        return _gbBmp[free];
    }

    void GalleryDispose()
    {
        for (int i = 0; i < _gbBmp.Length; i++) { _gbBmp[i]?.Dispose(); _gbBmp[i] = null; _gbPx[i] = null; _gbUsed[i] = 0; }
        for (int i = 0; i < _album.Bmps.Length; i++) { _album.Bmps[i].bmp?.Dispose(); _album.Bmps[i] = default; }
    }

    // ---- the grid itself (every style but the Terminal). The sheet is one control: a tap opens the photo under it, a drag scrolls it
    // (the laser takes hold of the sheet; let go it glides on - modern, Cyber), the stick scrolls it; at its right the rail - the years
    // and months, dragged to jump (a retro style: its own scroll bar, its thumb dragged). Days: the day's heading stays at the top while
    // its photos scroll under it. look: 0 modern, 1 Cyber, 2 a retro style (its list box, scroll bar, font and selection; v in the
    // retro canvas) ----

    bool _galHeld, _galDrag, _galRailHeld;
    Vector2 _galPressAt;
    float _galPressScroll, _galVel, _galLastY;
    double _galLastAt, _galTickAt;
    readonly RectangleF[] _galCells = new RectangleF[128];     // the cells drawn this frame (a tap's; --snapshot's GalleryCellPoint)
    readonly int[] _galCellIdx = new int[128];
    int _galCellN;

    // the view: All (one sheet) or Days (C.GalleryDays: kept in the config, as VR Gallery keeps its level)
    void GalSetDays(bool days)
    {
        if (C.GalleryDays == days) return;
        // the photo at the top stays at the top
        _galAnchorTop = _galTopPhoto;
        C.GalleryDays = days;
        _cfg.MarkDirty();
        _dirty = true;
    }
    string _galAnchorTop, _galTopPhoto;

    void GalGrid(RectangleF v, int look)
    {
        var s = _ph;
        var cells = s?.Source as IPhotoCells;
        bool retro = look == 2, days = C.GalleryDays;
        var sk = Theme.RetroSkin;
        if (retro) sk.ListBox(_p, v);
        var inner = retro ? RectangleF.Inflate(v, -2, -2) : v;
        _galCellN = 0;
        int n = s?.Recent.Count ?? 0;
        if (s == null || !s.Ready || n == 0)
        {
            string t = s == null || !s.Ready ? "Loading..." : NoPhotos;
            if (retro) _ui.Text95(PhotoCut(t, inner.Width - 8, Ui.F95, FontWeight.Normal, null, 1), inner, Gry95, false, TextAlignment.Center);
            else if (look == 1) CyMono(PhotoCut(Upper(t), inner.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), inner.X, inner.Y + inner.Height / 2, inner.Width,
                CyG, CyNote, FontWeight.SemiBold, TextAlignment.Center);
            else _p.Text(PhotoCut(t, inner.Width, 22), inner, Sub, 22, FontWeight.SemiBold, TextAlignment.Center);
            PhotoEmptyShown = s != null && s.Ready;
            return;
        }
        PhotoEmptyShown = false;
        int cols = retro ? (days ? 3 : 5) : (days ? 4 : 5);
        float gap = retro ? (days ? 4 : 2) : (days ? 10 : 4), headH = days ? (retro ? 17 : 38) : 0;
        float railW = retro ? 16 : 40, pad = retro ? gap : 0;
        float gw = inner.Width - railW - 2 * pad - (retro ? 0 : 4);
        float cw = MathF.Floor((gw - (cols - 1) * gap) / cols), ch = days ? MathF.Floor(cw * 9 / 16) : cw;
        gw = cols * cw + (cols - 1) * gap;
        var L = GalLayoutOf(s, cols, cw, ch, headH, gap, days);
        float viewH = inner.Height - 2 * pad, top0 = inner.Y + pad, left = inner.X + pad;
        float max = MathF.Max(0, L.Height - viewH);
        double now = Clock.Now;
        float fdt = (float)Math.Clamp(now - _galTickAt, 0, 0.1);
        _galTickAt = now;
        // where it opens: the photo that was big in the middle; Days / All switched: the photo at the top stays there
        if (_galAnchor != null || _galAnchorTop != null)
        {
            string a = _galAnchor ?? _galAnchorTop;
            for (int i = 0; i < n; i++)
                if (s.Recent[i].Path == a) { _galScroll = GalRowY(L, i) - (_galAnchor != null ? (viewH - ch) / 2 : (days ? headH : 0)); break; }
            _galAnchor = _galAnchorTop = null;
            _galVel = 0;
        }
        // the stick
        if (_ui.Wheel != 0 && !_ui.Inert) { _galScroll -= _ui.Wheel * (ch + gap) / 3; _galVel = 0; _dirty = true; }
        // the sheet: held - a drag past a few px scrolls it with the pointer; a release without one is the tap (below, on the cell under it)
        var sheet = new RectangleF(left, top0, gw, viewH);
        bool tap = GalSheet(sheet, retro, now, out bool over, out bool held);
        var m = _ui.Mouse;
        // let go: it glides on, slowing (1995 did not)
        if (!held && !retro && MathF.Abs(_galVel) > 25)
        {
            _galScroll += _galVel * fdt;
            _galVel *= MathF.Exp(-3.2f * fdt);
            if (_galScroll <= 0 || _galScroll >= max) _galVel = 0;
            _ui.Animating = true;
        }
        else if (!held) _galVel = 0;
        // the rail / the scroll bar
        if (max > 0)
        {
            if (retro) GalScrollBar95(new RectangleF(inner.Right - 16, inner.Y, 16, inner.Height), L, viewH, max, ch + gap);
            else GalRail(new RectangleF(inner.Right - railW, top0, railW, viewH), L, viewH, max, look);
        }
        else _galRailHeld = false;
        _galScroll = Math.Clamp(_galScroll, 0, max);
        float scroll = retro ? MathF.Round(_galScroll) : _galScroll;
        var clip = retro ? new RectangleF(inner.X, top0, inner.Width - 16, viewH) : new RectangleF(left - 3, top0 - 3, gw + 6, viewH + 6);
        _p.PushClip(clip);
        bool pressing = held && !_galDrag;
        _galTopPhoto = null;
        foreach (var day in L.Days)
        {
            if (day.Y - scroll > viewH) break;
            if (day.End - scroll < 0) continue;
            float hy = top0 + day.Y - scroll;
            if (days && hy + headH > top0 && hy < top0 + viewH) GalHeading(GalHead(day.Day, day.Count), day.Worlds, left, hy, gw, headH, look);
            int k0 = Math.Max(0, (int)((scroll - day.Y - headH) / (ch + gap)) * cols);         // (the rows above the view skipped whole)
            for (int k = k0; k < day.Count; k++)
            {
                float cy = hy + headH + k / cols * (ch + gap);
                if (cy > top0 + viewH) break;
                if (cy + ch < top0) continue;
                int i = day.First + k;
                var r = new RectangleF(left + k % cols * (cw + gap), cy, cw, ch);
                if (_galTopPhoto == null && cy >= top0 - ch / 2) _galTopPhoto = s.Recent[i].Path;
                if (_galCellN < _galCells.Length) { _galCells[_galCellN] = r; _galCellIdx[_galCellN] = i; _galCellN++; }
                bool hover = over && !_galDrag && !_ui.Inert && r.Contains(m.X, m.Y);
                GalCell(r, s.Recent[i], cells, look, hover, pressing && r.Contains(_galPressAt.X, _galPressAt.Y),
                    _galPicked && i == _album.Index && _album.Path == s.Recent[i].Path, days ? (retro ? 0 : 10) : (retro ? 0 : 5));
            }
        }
        // Days: the heading of the day at the top stays there, pushed up by the next one
        if (days)
            for (int d = 0; d < L.Days.Count; d++)
            {
                var day = L.Days[d];
                if (day.Y >= scroll || day.End <= scroll) continue;
                float next = d + 1 < L.Days.Count ? L.Days[d + 1].Y - scroll : float.MaxValue;
                float hy = top0 + MathF.Min(0, next - headH);
                var band = new RectangleF(clip.X, hy - (retro ? 0 : 3), clip.Width, headH + (retro ? 0 : 3));
                _p.Fill(band, retro ? sk.Window : Bento.Card);
                GalHeading(GalHead(day.Day, day.Count), day.Worlds, left, hy, gw, headH, look);
                break;
            }
        _p.PopClip();
        // a tap: the photo under it
        if (tap)
            for (int c = 0; c < _galCellN; c++)
                if (_galCells[c].Contains(_galPressAt.X, _galPressAt.Y) && RectangleF.Intersect(_galCells[c], clip).Contains(_galPressAt.X, _galPressAt.Y))
                {
                    int i = _galCellIdx[c];
                    GalleryPick(i, s.Recent[i].Path);
                    break;
                }
    }

    // The sheet's one control: held, a move past a few px is a drag - the sheet follows the pointer (its speed kept for the throw);
    // released without one: the tap (true), on whatever is under the press point
    bool GalSheet(RectangleF sheet, bool retro, double now, out bool over, out bool held)
    {
        bool tap = _ui.Clickable("wgrid", sheet, out over, out held) && !_ui.Inert;
        var m = _ui.Mouse;
        if (held && !_galHeld)
            (_galHeld, _galDrag, _galPressAt, _galPressScroll, _galVel, _galLastY, _galLastAt) = (true, false, m, _galScroll, 0, m.Y, now);
        else if (held)
        {
            if (!_galDrag && MathF.Abs(m.Y - _galPressAt.Y) > (retro ? 4 : 9)) { _galDrag = true; _galPressAt.Y = m.Y; _galPressScroll = _galScroll; }
            if (_galDrag)
            {
                float dt = (float)Math.Max(0.004, now - _galLastAt);
                _galVel = _galVel * 0.5f - (m.Y - _galLastY) / dt * 0.5f;
                _galScroll = _galPressScroll - (m.Y - _galPressAt.Y);
                (_galLastY, _galLastAt) = (m.Y, now);
                _dirty = true;
            }
        }
        if (!held && _galHeld)
        {
            _galHeld = false;
            if (!_galDrag || retro || now - _galLastAt > 0.08) _galVel = 0;        // (held still before the release: no throw)
        }
        return tap && !_galDrag;
    }

    // the rail (modern, Cyber): the months as ticks, each year's label where it starts, where the view is as a bar; held: the view goes
    // where the pointer is on it, its month in a bubble beside
    void GalRail(RectangleF rail, GalLayout L, float viewH, float max, int look)
    {
        bool cyber = look == 1;
        _ui.Clickable("wgrail", rail, out bool hover, out bool held);
        held &= !_ui.Inert;
        _galRailHeld = held;
        float top = rail.Y + 6, h = rail.Height - 12;
        float Y(float contentY) => top + h * Math.Clamp(contentY / L.Height, 0, 1);
        if (held)
        {
            float f = Math.Clamp((_ui.Mouse.Y - top) / h, 0, 1);
            _galScroll = f * L.Height - viewH / 2;
            _galVel = 0;
            _dirty = true;
        }
        uint ink = cyber ? CyG : Sub, faint = cyber ? CyberHud.Edge : Theme.WithAlpha(Bento.Text, 0.18f);
        float x = rail.Right - 4;
        // the view's bar
        float sc = Math.Clamp(_galScroll, 0, max);
        float b0 = Y(sc), b1 = Y(sc + viewH);
        _p.Fill(new RectangleF(rail.Right - 5, top, 2, h), faint, 1);
        _p.Fill(new RectangleF(rail.Right - 6, b0, 4, MathF.Max(10, b1 - b0)), cyber ? CyC : held || hover ? Bento.Accent : Theme.WithAlpha(Bento.Text, 0.45f), 2);
        // the months' ticks, each year's label (where there is room for one)
        float lastLabel = float.MinValue;
        int lastYear = -1;
        foreach (var (my, year, month) in L.Months)
        {
            float ty = Y(my);
            _p.Fill(new RectangleF(x - 9, ty, 4, 1), faint);
            if (year == lastYear) continue;
            lastYear = year;
            if (ty - lastLabel < 16 || ty > top + h - 4) continue;
            lastLabel = ty;
            string ys = GalYear(year);
            if (cyber) CyMono(ys, rail.Right - 12, ty + 5, rail.Width - 12, ink, CyNote - 2, FontWeight.SemiBold, TextAlignment.Trailing);
            else TextAt(ys, rail.X, ty + 5, rail.Width - 12, ink, 13, FontWeight.SemiBold, TextAlignment.Trailing);
        }
        if (!held) return;
        // the bubble: the month the view is at
        int at = 0;
        float mid = sc + viewH / 2;
        for (int i = 0; i < L.Months.Count; i++) if (L.Months[i].y <= mid) at = i;
        if (L.Months.Count == 0) return;
        string t = GalMonth(L.Months[at].year, L.Months[at].month);
        float tw = cyber ? CyMonoW(t) : _p.MeasureWidth(t, SmallText, FontWeight.SemiBold);
        float by = Math.Clamp(_ui.Mouse.Y - 19, rail.Y, rail.Bottom - 38);
        var bub = new RectangleF(rail.X - tw - 34, by, tw + 26, 38);
        if (cyber)
        {
            CyberHud.CardShape(_p, bub, CyberHud.Card2, CyC);
            CyMono(t, bub.X + 13, bub.Y + 24, tw + 4, CyC, CyNote, FontWeight.Bold);
        }
        else
        {
            _p.Fill(bub, 0xF0101418, 12);
            _p.Stroke(RectangleF.Inflate(bub, -0.5f, -0.5f), Bento.Accent, 1.5f, 11.5f);
            TextAt(t, bub.X + 13, bub.Y + 26, tw + 4, 0xFFFFFFFF, SmallText, FontWeight.SemiBold);
        }
    }

    // a retro style's own scroll bar: its arrows a row each, the track dragged (or tapped) puts the thumb there
    void GalScrollBar95(RectangleF sb, GalLayout L, float viewH, float max, float row)
    {
        var up = new RectangleF(sb.X, sb.Y, sb.Width, 16);
        var dn = new RectangleF(sb.X, sb.Bottom - 16, sb.Width, 16);
        var trk = new RectangleF(sb.X, sb.Y + 16, sb.Width, sb.Height - 32);
        if (_ui.Clickable("wgup", up, out _, out bool uh) && !_ui.Inert) _galScroll = MathF.Max(0, _galScroll - row);
        if (_ui.Clickable("wgdn", dn, out _, out bool dh) && !_ui.Inert) _galScroll = MathF.Min(max, _galScroll + row);
        float th = MathF.Max(8, trk.Height * viewH / L.Height);
        _ui.Clickable("wgtrk", trk, out _, out bool tk);
        if (tk && !_ui.Inert)
        {
            _galScroll = Math.Clamp((_ui.Mouse.Y - trk.Y - th / 2) / MathF.Max(1, trk.Height - th), 0, 1) * max;
            _dirty = true;
        }
        float ty = trk.Y + (trk.Height - th) * Math.Clamp(_galScroll, 0, max) / max;
        Theme.RetroSkin.ScrollBar(_p, sb, up, dn, new RectangleF(sb.X, MathF.Round(ty), sb.Width, MathF.Round(th)), uh, dh);
    }

    // a year as text, a month as "2025 Oct" in the language's way: made once each
    readonly Dictionary<int, string> _galYears = new(), _galMonths = new();
    int _galMonthsLang = -1;
    string GalYear(int y)
    {
        if (!_galYears.TryGetValue(y, out var s)) _galYears[y] = s = y.ToString(CultureInfo.InvariantCulture);
        return s;
    }
    string GalMonth(int y, int m)
    {
        if (_galMonthsLang != Lang.Version) { _galMonths.Clear(); _galMonthsLang = Lang.Version; }
        if (!_galMonths.TryGetValue(y * 12 + m, out var s))
        {
            var c = CultureInfo.CurrentCulture;
            _galMonths[y * 12 + m] = s = Capital(new DateTime(y, m, 1).ToString(c.DateTimeFormat.YearMonthPattern, c));
        }
        return s;
    }

    // a day's heading, its worlds after it (VR Gallery's library: Sys/VrGalleryData.cs) in the style's quieter ink
    void GalHeading(string text, string worlds, float x, float y, float w, float h, int look)
    {
        if (look == 2)
        {
            string t = PhotoCut(text, w, Ui.F95, FontWeight.Bold, null, 1);
            _ui.Text95(t, new RectangleF(x, y + 1, w, 13), Blk95, true);
            float tw2 = _ui.Width95(t, true) + 8;
            if (worlds != null && w - tw2 > 30) _ui.Text95(PhotoCut(worlds, w - tw2, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(x + tw2, y + 1, w - tw2, 13), Gry95);
            return;
        }
        if (look == 1)
        {
            string t = PhotoCut(Upper(text), w, CyNote, FontWeight.Bold, CyberHud.Mono);
            CyMono(t, x, y + h - 12, w, CyY, CyNote, FontWeight.Bold);
            float tw = MathF.Min(w, CyMonoW(t, CyNote, FontWeight.Bold));
            if (worlds != null && w - tw - 14 > 40)
            {
                string ws = PhotoCut(Upper(worlds), w - tw - 14, CyNote, FontWeight.SemiBold, CyberHud.Mono);
                CyMono(ws, x + tw + 14, y + h - 12, w - tw - 14, CyG, CyNote, FontWeight.SemiBold);
                tw += 14 + CyMonoW(ws, CyNote, FontWeight.SemiBold);
            }
            if (x + tw + 12 < x + w) _p.Fill(new RectangleF(x + tw + 10, y + h - 17, w - tw - 10, 1), CyberHud.Edge);
            return;
        }
        string m = PhotoCut(text, w, SmallText);
        TextAt(m, x, y + h - 10, w + 2, Bento.Text, SmallText, FontWeight.SemiBold);
        float mw = _p.MeasureWidth(m, SmallText, FontWeight.SemiBold) + 12;
        if (worlds != null && w - mw > 40) TextAt(PhotoCut(worlds, w - mw, SmallText, FontWeight.Normal), x + mw, y + h - 10, w - mw + 2, Sub, SmallText, FontWeight.Normal);
    }

    // a favourite's heart on its tile (VR Gallery's favourites); modern / Cyber: the world under the pointer along its foot
    void GalTileMarks(RectangleF r, PhotoWatcher.Photo p, int look, bool hover)
    {
        if (look == 2)
        {
            if (p.Favorite) _p.Pixels(Heart95, r.Right - 10, r.Y + 3, 0xFFC00000);
            return;
        }
        if (hover && p.World != null && r.Height >= 56)
        {
            var strip = new RectangleF(r.X, r.Bottom - 26, r.Width, 26);
            _p.FillGradient(strip, 0x00000000, 0x80000000, 0xD9000000, 0, 90);
            if (look == 1) CyMono(PhotoCut(Upper(p.World), r.Width - 12, CyNote - 2, FontWeight.SemiBold, CyberHud.Mono), r.X + 6, r.Bottom - 8, r.Width - 10, CyW,
                CyNote - 2, FontWeight.SemiBold);
            else TextAt(PhotoCut(p.World, r.Width - 12, 13, FontWeight.SemiBold), r.X + 6, r.Bottom - 8, r.Width - 10, 0xFFFFFFFF, 13, FontWeight.SemiBold);
        }
        if (!p.Favorite) return;
        float cx = r.Right - 13, cy = r.Y + 13;
        _p.Circle(cx, cy, 10, 0x8C000000);
        _p.Icon(HeartGlyph, new RectangleF(cx - 10, cy - 9.5f, 20, 20), look == 1 ? CyY : 0xFFFF5D93, 12);
    }

    void GalCell(RectangleF r, PhotoWatcher.Photo p, IPhotoCells cells, int look, bool hover, bool pressed, bool current, float radius)
    {
        var bmp = GalBitmap(p, cells, out float w, out float h, out double born);
        if (look == 2)
        {
            // the thumbnail view's: the picture inside its square-cornered frame, the style's selection round the one under the pointer
            if (hover || current || pressed) Theme.RetroSkin.ListSelection(_p, RectangleF.Inflate(r, 2, 2));
            var f = RectangleF.Inflate(r, -1, -1);
            if (bmp != null) _p.ImagePart(bmp, f, PhotoCover(w, h, f));
            else _p.Fill(f, 0xFFD4D0C8);
            _p.Stroke(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 0xFF808080, 1);
            GalTileMarks(r, p, 2, hover);
            return;
        }
        // a picture that just came fades in (a fifth of a second)
        float fade = bmp == null ? 0 : (float)Math.Clamp((Clock.Now - born) / 0.2, 0, 1);
        if (fade < 1 && bmp != null) _ui.Animating = true;
        if (pressed) r = RectangleF.Inflate(r, -r.Width * 0.03f, -r.Height * 0.03f);
        if (look == 1)
        {
            _p.Fill(r, CyberHud.Card2);
            if (bmp != null)
            {
                if (fade < 1) _p.PushFade(fade);
                _p.ImagePart(bmp, r, PhotoCover(w, h, r));
                if (fade < 1) _p.PopFade();
            }
            GalTileMarks(r, p, 1, hover);
            _p.Stroke(RectangleF.Inflate(r, 0.5f, 0.5f), hover || current ? CyC : CyberHud.CyanDim, hover ? 1.5f : 1);
            if (hover) PhotoCorners(RectangleF.Inflate(r, 3, 3), CyY, 9);
            return;
        }
        _p.PushRoundedClip(r, radius);
        _p.Fill(r, Bento.Tile);
        if (bmp != null)
        {
            if (fade < 1) _p.PushFade(fade);
            _p.ImagePart(bmp, r, PhotoCover(w, h, r));
            if (fade < 1) _p.PopFade();
        }
        if (hover) _p.Fill(r, 0x26FFFFFF);
        GalTileMarks(r, p, 0, hover);
        _p.PopRoundedClip();
        if (hover || current) _p.Stroke(RectangleF.Inflate(r, 1, 1), current && !hover ? Theme.WithAlpha(Bento.Accent, 0.7f) : Bento.Accent, 2.5f, radius + 1);
    }

    // --snapshot: where the grid's cell 'slot' (the slot-th drawn, top left first) is, in the panel's pixels
    public Vector2? GalleryCellPoint(PanelData d, int slot)
    {
        Draw(d);
        if (slot < 0 || slot >= _galCellN) return null;
        var r = _galCells[slot];
        float k = Theme.Classic ? 2 : 1;
        return new Vector2((r.X + r.Width / 2) * k, (r.Y + r.Height / 2) * k);
    }
    public bool GalleryDaysShown => C.GalleryDays;

    // ---- modern / Cyber: the grid in the card (its heading DrawDetail's: "Gallery", the count after it; All / Days, Gallery lit left of the X) ----

    // All | Days, right-aligned to 'right' at y: returns its left edge
    float GalViewChips(float right, float y)
    {
        bool days = C.GalleryDays;
        float wa = MathF.Ceiling(_p.MeasureWidth(Lang.T("All"), 17, FontWeight.SemiBold) + 30);
        float wd = MathF.Ceiling(_p.MeasureWidth(Lang.T("Days"), 17, FontWeight.SemiBold) + 30);
        var rd = new RectangleF(right - wd, y, wd, 40);
        var ra = new RectangleF(rd.X - 6 - wa, y, wa, 40);
        if (Press("wgall", ra, null, "All", !days, true, 18)) GalSetDays(false);
        if (Press("wgdays", rd, null, "Days", days, true, 18)) GalSetDays(true);
        var rf = new RectangleF(ra.X - 6 - 46, y - 3, 46, 46);
        if (Press("wgfav", rf, _galFav ? HeartGlyph : HeartLine, null, _galFav, true, 18, true)) GalFilter(!_galFav, _galWorld, _galPerson, null);
        return rf.X;
    }

    // the world / person shown (in the count's place): a chip, its X shows all again; false: none
    bool GalFilterChip(float x, float right, float y)
    {
        string f = GalFilterName;
        if (f == null || right - x < 60) return f != null;
        string icon = _galWorld != null ? "\uE909" : _galPerson != null ? "\uE77B" : "\uE787";      // the globe / a person / a calendar (Today's visit)
        float w = MathF.Min(right - x, MathF.Ceiling(_p.MeasureWidth(f, 17, FontWeight.SemiBold) + 18 + 8 + 30 + 22));
        var r = new RectangleF(x, y, w, 40);
        if (Press("wgclear", r, icon, f, true, true, 16)) GalClearFilter();
        return true;
    }

    void GalleryModern(RectangleF E)
    {
        PhotoOpenShown = null;
        var s = _ph;
        var back = new RectangleF(E.Right - 22 - 46 - 12 - 46, E.Y + 14, 46, 46);
        float chips = GalViewChips(back.X - 14, E.Y + 17);
        float nx = E.X + 24 + _p.MeasureWidth(Lang.T(GalleryWord), 24, FontWeight.SemiBold) + 12, room = chips - 12 - nx;
        if (!GalFilterChip(nx, chips - 12, E.Y + 17) && s is { Ready: true, Recent.Count: > 0 } && room > 40)
            TextAt(PhotoCut(GalCountText(s.Recent.Count), room, 20), nx, E.Y + 44, room + 2, Sub, 20, FontWeight.SemiBold);
        if (Press("wphall", back, GridGlyph, null, true, true, 18, true)) GalleryToggle(null);
        GalGrid(new RectangleF(E.X + 20, E.Y + 74, E.Width - 34, E.Height - 74 - 14), 0);
        DeleteNoteModern(E);
    }

    void GalleryCyber(RectangleF E)
    {
        PhotoOpenShown = null;
        var s = _ph;
        var back = new RectangleF(E.Right - 22 - 46 - 12 - 46, E.Y + 14, 46, 46);
        float chips = GalViewChips(back.X - 14, E.Y + 17);
        float cx0 = E.X + 24 + CyberHud.TagWidth(_p, Upper(GalleryWord), 18) + 14;
        if (!GalFilterChip(cx0, chips - 12, E.Y + 17) && s is { Ready: true, Recent.Count: > 0 })
        {
            string cnt = Upper(GalCountText(s.Recent.Count));
            float room = chips - 14 - cx0;
            if (CyMonoW(cnt) <= room) CyMono(cnt, cx0, E.Y + 23 + (26 + CyberHud.CapH(_p, CyNote)) / 2, room, CyC);
        }
        if (Press("wphall", back, GridGlyph, null, true, true, 18, true)) GalleryToggle(null);
        GalGrid(new RectangleF(E.X + 20, E.Y + 74, E.Width - 34, E.Height - 74 - 14), 1);
        DeleteNoteModern(E);
    }

    // ---- modern / Cyber: the buttons over the photo's top right - the slideshow, Send (VR Gallery's newest only), Gallery, Delete; while
    // the slideshow plays, its Stop alone. A row too narrow for them (a photo standing up) goes on below ----

    const string ShowGlyph = "", StopGlyph = "", SendGlyph = "";
    const string SendAsk = "Send?", Sent = "Sent to Discord";

    void PhotoActions(RectangleF box, PhotoWatcher.Photo p)
    {
        if (p == null) return;
        float bs = 46, y = box.Y + 10, right = box.Right - 10;
        if (_galShow)
        {
            if (Press("wphstop", new RectangleF(right - bs, y, bs, bs), StopGlyph, null, true, true, 18, true)) ShowStop();
            return;
        }
        bool armed = DeleteArmed(p), busy = _delBusy == p.Path;
        float dw = armed || busy ? MathF.Ceiling(MathF.Max(bs, 18 + 8 + _p.MeasureWidth(Lang.T(busy ? "Deleting..." : DeleteAsk), 17, FontWeight.SemiBold) + 30)) : bs;
        bool send = CanSend(p), sArmed = send && SendArmed(p), sBusy = send && _sendBusy;
        float sw = sArmed || sBusy ? MathF.Ceiling(MathF.Max(bs, 18 + 8 + _p.MeasureWidth(Lang.T(sBusy ? "Sending..." : SendAsk), 17, FontWeight.SemiBold) + 30)) : bs;
        // right to left: Delete, Gallery, Send, the slideshow
        float x = right;
        RectangleF Next(float w)
        {
            if (x - w < box.X + 4) { x = right; y += bs + 8; }
            var r = new RectangleF(x - w, y, w, bs);
            x -= w + 10;
            return r;
        }
        var del = Next(dw);
        var all = Next(bs);
        var snd = send ? Next(sw) : RectangleF.Empty;
        var play = PhotoCount > 1 ? Next(bs) : RectangleF.Empty;
        if (Press("wphdel", del, DeleteGlyph, armed || busy ? (busy ? "Deleting..." : DeleteAsk) : null, armed, !busy, 18, !(armed || busy), accentFill: false,
                fill: Bento.Bad))
            DeleteTap(p);
        if (Press("wphall", all, GridGlyph, null, false, true, 18, true)) GalleryToggle(p);
        if (send) _sendSeen = true;
        if (send && Press("wphsend", snd, SendGlyph, sArmed || sBusy ? (sBusy ? "Sending..." : SendAsk) : null, sArmed, !sBusy, 18, !(sArmed || sBusy)))
            SendTap(p);
        if (!play.IsEmpty && Press("wphplay", play, ShowGlyph, null, false, true, 18, true)) ShowStart();
    }

    // ---- Send to Discord: VR Gallery sends its newest photo to the webhook set up in it (its local API; it cannot be told another) -
    // so the button is there on the newest photo of them all only, once it is 10 s old (VR Gallery has listed it by then); a first tap
    // asks ("Send?"), the second sends (the app: Sys/VrGalleryData.cs, on a thread) ----

    public Action<Action<string>> OnSendNewest;      // the app; null in the tools: a second tap is only noted (LastSent) and taken as done
    public volatile bool SendReady;                  // the app: VR Gallery's API on, with a token and a webhook (read every 20 s)
    public string LastSent { get; private set; }
    string _newestAll;                               // the newest photo of them all (the latest photo's watcher, else the album)
    string _sendArmed;
    double _sendArmAt;
    volatile bool _sendBusy, _sendDone;
    volatile string _sendWhy;

    bool CanSend(PhotoWatcher.Photo p) => SendReady && p != null && p.Path == _newestAll && (DateTime.Now - p.Taken).TotalSeconds > 10;
    bool SendArmed(PhotoWatcher.Photo p) => p != null && _sendArmed == p.Path && Clock.Now - _sendArmAt <= DelArmLen;
    public bool SendShown(PanelData d) { _sendSeen = false; Draw(d); return _sendSeen; }       // --snapshot: was Send drawn
    bool _sendSeen;

    void SendTap(PhotoWatcher.Photo p)
    {
        if (p == null || _sendBusy) return;
        _dirty = true;
        if (_galShow) ShowStop();
        if (!SendArmed(p)) { _sendArmed = p.Path; _sendArmAt = Clock.Now; _delNote = null; return; }
        _sendArmed = null;
        _sendBusy = true;
        LastSent = p.Path;
        if (OnSendNewest == null) { _sendWhy = null; _sendDone = true; return; }
        OnSendNewest(why => { _sendWhy = why; _sendDone = true; });
    }

    // Send's answer and the newest photo's path, a frame (PhotoFrame: on the frame's thread)
    void SendFrame(PanelData d)
    {
        var all = d.Photos is { Recent.Count: > 0 } ps ? ps : d.Album;
        _newestAll = all is { Recent.Count: > 0 } a ? a.Recent[0].Path : null;
        if (_sendArmed != null && Clock.Now - _sendArmAt > DelArmLen) _sendArmed = null;
        if (!_sendDone) return;
        _sendDone = false;
        _sendBusy = false;
        _delNoteBad = _sendWhy != null;
        _delNote = _sendWhy ?? Sent;
        _delNoteAt = Clock.Now;
        _dirty = true;
    }

    // ---- the slideshow (VR Gallery's Play): from the photo shown, the next older one every SlideSeconds (VR Gallery's own setting),
    // each fading in; Stop, a tap on the photo, an arrow or the stick end it, as the last photo does and the grid or the people's picker
    // opened over it (a world's or a person's photos). The retro viewers have theirs (Erik
    // 10-24, "mehet mind"): XP's Start Slide Show, 7's big blue Play (Pause while it plays), the Mac's projection screen - held in while
    // it plays, a slide cutting in; 95 Imaging had none, the Terminal has no key for one (its keys are full) ----

    public volatile int SlideSeconds = 4;            // the app: VR Gallery's slideshow_secs
    bool _galShow;
    double _galShowAt, _galSlideAt = -10;
    public bool GalleryShowing => _galShow;

    void ShowStart() { (_galShow, _galShowAt, _galSlideAt, _galZoom) = (true, Clock.Now, Clock.Now, false); _delArmed = _sendArmed = null; _dirty = true; }
    void ShowStop() { _galShow = false; _dirty = true; }

    // in the opened view's set (PhotoFrame's, once a frame - so every style's viewer has it): the next slide when it is due; not under
    // the gallery's grid
    void ShowTick()
    {
        if (!_galShow || (_gal && _galGrid)) return;
        double now = Clock.Now;
        if (now - _galShowAt < SlideSeconds) return;
        if (_phIndex >= PhotoCount - 1) { ShowStop(); return; }
        int i = _phIndex + 1;
        _phIndex = i;
        _phPath = _ph.Recent[i].Path;
        (_galShowAt, _galSlideAt) = (now, now);
        _dirty = true;
    }

    // ---- the zoom: a tap on the photo - 2.2 x there, from a sharp copy of it (the app reads it at 1600 px: the shell's big thumbnail,
    // else the file; the watch's 640 px one until it comes); a drag moves round it; a tap again - whole. The photo's controls stay over
    // it; its words and arrows give way meanwhile (modern, Cyber). The retro viewers zoom their own way, the photo zoomed in its frame
    // and a drag moving round it all the same: 95 Imaging's Zoom menu word a switch (on: a tap on the photo zooms in there, again
    // whole), XP's Zoom In and Zoom Out, 7's "Change the display size" magnifier and the Mac's (in while zoomed) ----

    public sealed class HiPicture { public string Path; public uint[] Px; public int W, H; }
    public Action<string> OnWantHi;                  // the app: the photo at 1600 px, on a thread; then Hi
    public volatile HiPicture Hi;
    HiPicture _hiSeen;
    ID2D1Bitmap _hiBmp;
    uint[] _hiBmpPx;
    bool _galZoom, _zoomHeld, _zoomDrag;
    bool _zoomTool;                                  // 95 Imaging's Zoom switched on: a tap on the photo zooms
    Vector2 _zoomAt = new(0.5f, 0.5f), _zoomPress, _zoomFrom;
    string _zoomPath;
    const float ZoomK = 2.2f;
    public bool GalleryZoomShown => _galZoom;
    public bool ZoomToolShown => _zoomTool;          // --snapshot

    // the photo in its box: whole, faded in as a slide comes, or zoomed; a tap / drag on it (registered before the controls over it)
    void PhotoImage(RectangleF box, PhotoWatcher.Photo p, ID2D1Bitmap bmp, uint fill)
    {
        PhotoZoomInput(box, p, "wphzoom", true);
        if (bmp == null) { _p.Fill(box, fill); _p.Icon(PhotoKindIcon, box, Theme.Cyber ? CyberHud.Faint : Bento.Faint, 40); return; }
        float fade = _galShow ? (float)Math.Clamp((Clock.Now - _galSlideAt) / 0.35, 0, 1) : 1;
        if (fade < 1) { _ui.Animating = true; _p.Fill(box, fill); _p.PushFade(fade); }
        PhotoZoomDraw(box, p, bmp);
        if (fade < 1) _p.PopFade();
    }

    // a retro viewer's photo where it shows whole (pic): whole or zoomed, a slide cutting in (the era's viewers did not fade); tapZooms:
    // a tap on it zooms in there and out again (95's Zoom on), else its buttons do (ZoomIn, ZoomOut) and a tap only ends the slideshow
    void PhotoImage95(RectangleF pic, PhotoWatcher.Photo p, ID2D1Bitmap bmp, uint empty, bool tapZooms)
    {
        PhotoZoomInput(pic, p, tapZooms ? "wphzoom" : "wphpic", tapZooms);
        if (bmp != null) PhotoZoomDraw(pic, p, bmp);
        else _p.Fill(pic, empty);
    }

    // the tap and the drag on the photo: held and moved while zoomed - round it; a tap - the slideshow ends, else (tapZooms) zoomed in
    // there, or whole again
    void PhotoZoomInput(RectangleF box, PhotoWatcher.Photo p, string id, bool tapZooms)
    {
        if (_zoomPath != p.Path)
        {
            if (_galZoom) _dirty = true;            // (a retro viewer's zoom buttons were drawn zoomed this frame: once more)
            (_galZoom, _zoomPath) = (false, p.Path);
        }
        bool tap = _ui.Clickable(id, box, out _, out bool held) && !_ui.Inert;
        var m = _ui.Mouse;
        if (held && !_zoomHeld) (_zoomHeld, _zoomDrag, _zoomPress, _zoomFrom) = (true, false, m, _zoomAt);
        else if (held && _galZoom)
        {
            if (!_zoomDrag && Vector2.Distance(m, _zoomPress) > (Theme.Classic ? 4 : 8)) _zoomDrag = true;      // (a retro canvas: half the px)
            if (_zoomDrag)
            {
                var dd = (m - _zoomPress) / new Vector2(box.Width * ZoomK, box.Height * ZoomK);
                _zoomAt = Vector2.Clamp(_zoomFrom - dd, new Vector2(0.5f / ZoomK), new Vector2(1 - 0.5f / ZoomK));
                _dirty = true;
            }
        }
        if (!held) _zoomHeld = false;
        if (!tap || _zoomDrag) return;
        if (_galShow) ShowStop();
        else if (!tapZooms) return;
        else if (_galZoom) _galZoom = false;
        else ZoomIn(p, new Vector2((m.X - box.X) / box.Width, (m.Y - box.Y) / box.Height));
        _dirty = true;
    }

    // zoomed in at 'at' (0..1 across the photo), its sharp copy asked for; ZoomOut: whole again
    void ZoomIn(PhotoWatcher.Photo p, Vector2 at)
    {
        if (p == null) return;
        if (_galShow) ShowStop();
        (_galZoom, _zoomPath) = (true, p.Path);
        _zoomAt = Vector2.Clamp(at, new Vector2(0.5f / ZoomK), new Vector2(1 - 0.5f / ZoomK));
        if (Hi?.Path != p.Path) OnWantHi?.Invoke(p.Path);
        _dirty = true;
    }

    void ZoomOut() { _galZoom = false; _dirty = true; }

    // 95's Zoom switched on or off (off: the photo whole again)
    void ZoomTool(bool on) { _zoomTool = on; if (!on) _galZoom = false; _dirty = true; }

    // the photo whole, or the part zoomed in on - from the sharp copy when it is this photo's, else the watch's own
    void PhotoZoomDraw(RectangleF box, PhotoWatcher.Photo p, ID2D1Bitmap bmp)
    {
        if (!_galZoom) { _p.ImagePart(bmp, box, new RectangleF(0, 0, p.TW, p.TH)); return; }
        var hi = Hi;
        float W = p.TW, H = p.TH;
        var b = bmp;
        if (hi != null && hi.Path == p.Path && hi.Px != null)
        {
            if (!ReferenceEquals(_hiBmpPx, hi.Px)) { _hiBmp?.Dispose(); _hiBmp = _p.CreateBitmap(hi.Px, hi.W, hi.H); _hiBmpPx = hi.Px; }
            (b, W, H) = (_hiBmp, hi.W, hi.H);
        }
        float sw = W / ZoomK, sh = H / ZoomK;
        _p.ImagePart(b, box, new RectangleF(Math.Clamp(_zoomAt.X * W - sw / 2, 0, W - sw), Math.Clamp(_zoomAt.Y * H - sh / 2, 0, H - sh), sw, sh));
    }

    void ZoomDispose() { _hiBmp?.Dispose(); _hiBmp = null; _hiBmpPx = null; }

    void DeleteNoteModern(RectangleF E)
    {
        if (_delNote == null) return;
        string t = PhotoCut(Lang.T(_delNote), E.Width - 120, SmallText);
        float w = _p.MeasureWidth(t, SmallText, FontWeight.SemiBold) + 36;
        var r = new RectangleF(E.X + (E.Width - w) / 2, E.Bottom - 54, w, 38);
        if (Theme.Cyber)
        {
            CyberHud.CardShape(_p, r, CyberHud.Card2, _delNoteBad ? CyR : CyC);
            CyMono(Upper(t), r.X + 18, r.Y + 25, r.Width - 30, _delNoteBad ? CyR : CyC, CyNote, FontWeight.Bold);
            return;
        }
        _p.Fill(r, _delNoteBad ? Bento.Bad : 0xE6101418, 19);
        TextAt(t, r.X + 18, r.Y + 26, r.Width - 30, 0xFFFFFFFF, SmallText, FontWeight.SemiBold);
    }

    // ---- the retro styles: the viewer's toolbar buttons (95 / Mac / XP: glyph buttons; 7: its control bar's) ----

    static readonly string[] Grid95 = { "kkk.kkk", "kkk.kkk", "kkk.kkk", ".......", "kkk.kkk", "kkk.kkk", "kkk.kkk" };
    static readonly string[] Del95 = { "kk...kk", ".kk.kk.", "..kkk..", "..kkk..", ".kk.kk.", "kk...kk", "......." };
    // Open in VR Gallery where not even its short name fits: a little window (the toolbars' "open" picture)
    static readonly string[] OpenWin95 = { "kkkkkkk", "kkkkkkk", "k.....k", "k.....k", "k.....k", "k.....k", "kkkkkkk" };
    // Send: an envelope (the era's picture for mail going out); the zoom: a magnifier - XP's Zoom In / Zoom Out with a + / - in its lens;
    // the slideshow: a projection screen on its stand (XP's Start Slide Show). The *Fill maps: what XP colours in under them
    static readonly string[] Mail95 = { "kkkkkkkkk", "kk.....kk", "k.k...k.k", "k..k.k..k", "k...k...k", "k.......k", "kkkkkkkkk" };
    static readonly string[] MailFill = { ".........", "..kkkkk..", ".k.kkk.k.", ".kk.k.kk.", ".kkk.kkk.", ".kkkkkkk." };
    static readonly string[] Lens95 = { "..kkk.....", ".k...k....", "k.....k...", "k.....k...", "k.....k...", ".k...k....", "..kkk.kk..", ".......kk.", "........kk" };
    static readonly string[] LensIn95 = { "..kkk.....", ".k...k....", "k..k..k...", "k.kkk.k...", "k..k..k...", ".k...k....", "..kkk.kk..", ".......kk.", "........kk" };
    static readonly string[] LensOut95 = { "..kkk.....", ".k...k....", "k.....k...", "k.kkk.k...", "k.....k...", ".k...k....", "..kkk.kk..", ".......kk.", "........kk" };
    static readonly string[] LensFill = { "..........", "..kkk.....", ".kkkkk....", ".kkkkk....", ".kkkkk....", "..kkk....." };
    static readonly string[] Screen95 = { "kkkkkkkkkkk", ".k.......k.", ".k.......k.", ".k.......k.", ".k.......k.", ".kkkkkkkkk.", ".....k.....", "....k.k....", "...k...k..." };
    static readonly string[] ScreenFill = { "...........", "..kkkkkkk..", "..kkkkkkk..", "..kkkkkkk..", "..kkkkkkk.." };

    // a retro push button with its picture that stays in while on (the Mac's magnifier while zoomed, its slideshow while it plays)
    bool ToggleGlyph95(string id, RectangleF r, string[] glyph, bool enabled, bool on)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held) && enabled;
        Theme.RetroSkin.GlyphButton(_p, r, glyph, enabled, (held || on) && enabled, hover);
        return click;
    }

    // a 95 / Mac toolbar's Send (VR Gallery's newest only): the envelope; asking - held down, the envelope in the selection's navy (the
    // status line says to tap again); sending - greyed
    void SendButton95(RectangleF r, PhotoWatcher.Photo p)
    {
        _sendSeen = true;
        if (SendArmed(p))
        {
            bool click = _ui.Clickable("wphsend", r, out _, out _) && !_sendBusy;
            Theme.RetroSkin.ButtonFace(_p, r, false, true, false, false);
            _p.Pixels(Mail95, MathF.Floor(r.X + (r.Width - 9) / 2) + 1, MathF.Floor(r.Y + (r.Height - 7) / 2) + 1, 0xFF000080);
            if (click) SendTap(p);
        }
        else if (_ui.GlyphButton95("wphsend", r, Mail95, !_sendBusy)) SendTap(p);
    }

    // the ask and the note run out (drawn: the frame's own check, as Update's - GalleryChanged - may not have run)
    void DeleteTick()
    {
        double now = Clock.Now;
        if (_delArmed != null && now - _delArmAt > DelArmLen) _delArmed = null;
        if (_delNote != null && now - _delNoteAt > DelNoteLen) _delNote = null;
    }

    // the pair at x (two buttons w wide), y: Gallery, then Delete (asking: pressed in, the status line says why)
    void ActionButtons95(float x, float y, float w, float h, PhotoWatcher.Photo p)
    {
        if (_ui.GlyphButton95("wphall", new RectangleF(x, y, w, h), Grid95, true)) GalleryToggle(p);
        bool can = p != null && !(_gal && _galGrid) && !DeleteBusy;
        var dr = new RectangleF(x + w + 1, y, w, h);
        if (_gal && _galGrid) { HeartButton95("wgfav", dr); return; }        // (the grid: Favourites, VR Gallery's)
        if (DeleteArmed(p))
        {
            // asked: the button held down (the style's pushed face) with the glyph in red
            bool click = _ui.Clickable("wphdel", dr, out _, out _) && can;
            Theme.RetroSkin.ButtonFace(_p, dr, false, true, false, false);
            _p.Pixels(Del95, MathF.Floor(dr.X + (dr.Width - 7) / 2) + 1, MathF.Floor(dr.Y + (dr.Height - 7) / 2) + 1, 0xFFC00000);
            if (click) DeleteTap(p);
        }
        else if (_ui.GlyphButton95("wphdel", dr, Del95, can)) DeleteTap(p);
    }

    // the status line's words while Delete or Send asks or answers (null: the view's own)
    string DeleteStatus(PhotoWatcher.Photo p) =>
        DeleteBusy ? "Deleting..." : DeleteArmed(p) ? "Tap Delete again to move it to the Recycle Bin" : _sendBusy ? "Sending..."
            : SendArmed(p) ? "Tap Send again to send it to Discord" : _delNote;

    // ---- the Terminal: "> ls" - the files listed by day, the stick scrolling, a tap opening one ----

    // a line in the tube's capitals, made once (the files' names, the days' "# " headings)
    readonly Dictionary<(string, string), string> _tNames = new();
    int _tNamesLang = -1;

    string TName(string pre, string s)
    {
        if (_tNamesLang != Lang.Version || _tNames.Count > 256) { _tNames.Clear(); _tNamesLang = Lang.Version; }
        if (!_tNames.TryGetValue((pre, s), out var u)) _tNames[(pre, s)] = u = pre + s.ToUpper(CultureInfo.CurrentCulture);
        return u;
    }

    void TGallery(RectangleF c)
    {
        var s = _ph;
        int n = s?.Recent.Count ?? 0;
        string cnt = n > 0 ? Upper(GalCountText(n)) : null;
        float cw = cnt != null ? TerminalSkin.W(cnt) : 0;
        if (cnt != null) TCapsR(cnt, c.Right, c.Y + 2, TerminalSkin.DIM);
        string grep = GalFilterName;
        string cmd = grep == null ? GalTitle("> ls {0}") : TName("", GalTitle("> ls {0}") + " | grep \"" + grep + "\"");
        TCaps(PhotoCut(cmd, c.Width - cw - 10, Ui.F95, FontWeight.Normal, null, 2), c.X, c.Y + 2, grep != null ? TerminalSkin.HOT : TerminalSkin.MID);
        if (grep != null && GalTapLine("wgclear", new RectangleF(c.X, c.Y, c.Width - cw - 10, 11), TerminalSkin.MID)) GalClearFilter();
        var area = new RectangleF(c.X, c.Y + 13, c.Width, c.Height - 13);
        if (s == null || !s.Ready || n == 0)
        {
            PhotoEmptyShown = s != null && s.Ready;
            TCaps(PhotoCut(Upper(s == null || !s.Ready ? "Loading..." : NoPhotos), c.Width, Ui.F95, FontWeight.Normal, null, 2), area.X, area.Y + 2, TerminalSkin.DIM);
            return;
        }
        PhotoEmptyShown = false;
        const float rowH = 11;
        // a line a photo, a "#" line a day: as the layout's rows (one column, the heading a line)
        bool days = C.GalleryDays;
        var L = GalLayoutOf(s, 1, area.Width - 16, rowH - 1, days ? rowH : 0, 1, days);
        float max = MathF.Max(0, L.Height - area.Height);
        if (_galAnchor != null)
        {
            for (int i = 0; i < n; i++)
                if (s.Recent[i].Path == _galAnchor) { _galScroll = GalRowY(L, i) - area.Height / 2; break; }
            _galAnchor = null;
        }
        if (_ui.Wheel != 0 && !_ui.Inert) { _galScroll -= _ui.Wheel * rowH; _dirty = true; }
        var sheet = new RectangleF(area.X, area.Y, area.Width - (max > 0 ? 14 : 0), area.Height);
        bool tapped = GalSheet(sheet, true, Clock.Now, out bool over, out bool held);
        var m = _ui.Mouse;
        _galScroll = Math.Clamp(_galScroll, 0, max);
        float shown = MathF.Round(_galScroll / rowH) * rowH;           // (whole lines on the tube; the drag keeps its own fraction)
        if (max > 0)
        {
            var sb = new RectangleF(area.Right - 12, area.Y, 12, area.Height);
            var up = new RectangleF(sb.X, sb.Y, 12, 12);
            var dn = new RectangleF(sb.X, sb.Bottom - 12, 12, 12);
            if (_ui.Clickable("wgup", up, out _, out bool uh) && !_ui.Inert) _galScroll = MathF.Max(0, _galScroll - 3 * rowH);
            if (_ui.Clickable("wgdn", dn, out _, out bool dh) && !_ui.Inert) _galScroll = MathF.Min(max, _galScroll + 3 * rowH);
            float track = sb.Height - 24, th = MathF.Max(6, track * area.Height / L.Height), ty = sb.Y + 12 + (track - th) * shown / max;
            Theme.RetroSkin.ScrollBar(_p, sb, up, dn, new RectangleF(sb.X, MathF.Round(ty), 12, MathF.Round(th)), uh, dh);
        }
        float w = area.Width - 16;
        _p.PushClip(new RectangleF(area.X, area.Y, w + 2, area.Height));
        _galCellN = 0;
        foreach (var day in L.Days)
        {
            float hy = area.Y + day.Y - shown;
            if (hy > area.Bottom) break;
            if (area.Y + day.End - shown < area.Y) continue;
            if (days && hy + rowH > area.Y) TCaps(PhotoCut(TName("# " , GalHead(day.Day, day.Count)), w, Ui.F95, FontWeight.Normal, null, 2), area.X, hy + 2, TerminalSkin.HOT);
            int k0 = Math.Max(0, (int)((shown - day.Y - L.HeadH) / rowH));
            for (int k = k0; k < day.Count; k++)
            {
                float y = hy + L.HeadH + k * rowH;
                if (y > area.Bottom) break;
                if (y + rowH < area.Y) continue;
                int i = day.First + k;
                var p = s.Recent[i];
                var row = new RectangleF(area.X, y, w, rowH);
                if (_galCellN < _galCells.Length) { _galCells[_galCellN] = row; _galCellIdx[_galCellN] = i; _galCellN++; }
                bool hover = over && !_galDrag && !_ui.Inert && row.Contains(m.X, m.Y);
                bool cur = _galPicked && i == _album.Index && _album.Path == p.Path;
                if (hover || cur) TerminalSkin.Lit(_p, row);
                TCaps(PhotoCut(TName("", p.Name), w - 4, Ui.F95, FontWeight.Normal, null, 2), area.X + 2, y + 2, hover || cur ? TerminalSkin.BG : TerminalSkin.INK);
            }
        }
        _p.PopClip();
        if (tapped)
            for (int q = 0; q < _galCellN; q++)
                if (_galCells[q].Contains(_galPressAt.X, _galPressAt.Y) && area.Contains(_galPressAt.X, _galPressAt.Y))
                {
                    GalleryPick(_galCellIdx[q], s.Recent[_galCellIdx[q]].Path);
                    break;
                }
    }
}
