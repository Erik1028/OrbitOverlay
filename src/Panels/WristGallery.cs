using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
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
    const string GridGlyph = "", DeleteGlyph = "";
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
    SetScope UseOpenSet() => new(this, _gal ? _album : DetailIs("memory") ? _memory : _latest);

    void GalleryReset()
    {
        _gal = _galGrid = _galPicked = false;
        _galAnchor = null;
        _delArmed = null;
        FromNewest(_album);
    }

    // Gallery: the grid, scrolled to the photo shown; from the grid back: the album's photo it opened, else the view it came from
    void GalleryToggle(PhotoWatcher.Photo shown)
    {
        _delArmed = null;
        _dirty = true;
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
            if (!ReferenceEquals(d.Album, _album.Snap)) ch = true;
            int cv = (d.Album?.Source as IPhotoCells)?.CellVersion ?? 0;
            if (cv != _galCellsSeen) { _galCellsSeen = cv; ch |= _galGrid; }
        }
        double now = Clock.Now;
        if (_delArmed != null && now - _delArmAt > DelArmLen) { _delArmed = null; ch = true; }
        if (_delNote != null && now - _delNoteAt > DelNoteLen) { _delNote = null; ch = true; }
        return ch;
    }

    // ---- Delete ----

    bool DeleteArmed(PhotoWatcher.Photo p) => p != null && _delArmed == p.Path && Clock.Now - _delArmAt <= DelArmLen;
    bool DeleteBusy => _delBusy != null;

    void DeleteTap(PhotoWatcher.Photo p)
    {
        if (p == null || _delBusy != null) return;
        _dirty = true;
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

    // ---- the grid's layout: each day a heading and its rows; kept per snapshot and size ----

    sealed class GalDay { public DateTime Day; public int First, Count; public float Y, End; }

    sealed class GalLayout
    {
        public PhotoWatcher.Snapshot Snap;
        public int Cols;
        public float CellW, CellH, HeadH, Gap, Height;
        public readonly List<GalDay> Days = new();
    }

    readonly GalLayout _galL = new();

    GalLayout GalLayoutOf(PhotoWatcher.Snapshot s, int cols, float cw, float ch, float headH, float gap)
    {
        var L = _galL;
        if (ReferenceEquals(L.Snap, s) && L.Cols == cols && L.CellW == cw && L.CellH == ch && L.HeadH == headH && L.Gap == gap) return L;
        (L.Snap, L.Cols, L.CellW, L.CellH, L.HeadH, L.Gap) = (s, cols, cw, ch, headH, gap);
        L.Days.Clear();
        float y = 0;
        var list = s?.Recent;
        for (int i = 0; list != null && i < list.Count;)
        {
            var day = PhotoWatcher.DayOf(list[i].Taken);
            int j = i + 1;
            while (j < list.Count && PhotoWatcher.DayOf(list[j].Taken) == day) j++;
            int rows = (j - i + cols - 1) / cols;
            float end = y + headH + rows * (ch + gap);
            L.Days.Add(new GalDay { Day = day, First = i, Count = j - i, Y = y, End = end });
            y = end;
            i = j;
        }
        L.Height = y;
        return L;
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

    // ---- the cells' bitmaps: a few dozen kept by their pixels, the least recently drawn let go ----

    readonly uint[][] _gbPx = new uint[48][];
    readonly ID2D1Bitmap[] _gbBmp = new ID2D1Bitmap[48];
    readonly long[] _gbUsed = new long[48];
    long _gbTick;

    // a grid cell's picture: its own thumbnail when it has one (the big view's, the made-up photos'), else the album's cell
    ID2D1Bitmap GalBitmap(PhotoWatcher.Photo p, IPhotoCells cells, out float w, out float h)
    {
        uint[] px = p.Thumb;
        int pw = p.TW, ph = p.TH;
        if (px == null && cells != null) (px, pw, ph) = cells.Cell(p.Path);
        (w, h) = (pw, ph);
        if (px == null) return null;
        int free = 0;
        for (int i = 0; i < _gbPx.Length; i++)
        {
            if (ReferenceEquals(_gbPx[i], px)) { _gbUsed[i] = ++_gbTick; return _gbBmp[i]; }
            if (_gbUsed[i] < _gbUsed[free]) free = i;
        }
        _gbBmp[free]?.Dispose();
        _gbPx[free] = px;
        _gbBmp[free] = _p.CreateBitmap(px, pw, ph);
        _gbUsed[free] = ++_gbTick;
        return _gbBmp[free];
    }

    void GalleryDispose()
    {
        for (int i = 0; i < _gbBmp.Length; i++) { _gbBmp[i]?.Dispose(); _gbBmp[i] = null; _gbPx[i] = null; _gbUsed[i] = 0; }
        for (int i = 0; i < _album.Bmps.Length; i++) { _album.Bmps[i].bmp?.Dispose(); _album.Bmps[i] = default; }
    }

    // ---- the grid itself (every style but the Terminal): the stick scrolls it, the visible days' headings and cells drawn ----
    // look: 0 modern, 1 Cyber, 2 a retro style (its list box, scroll bar, font and selection; v is in the retro canvas)

    const string CellId = "wgc";

    void GalGrid(RectangleF v, int look)
    {
        var s = _ph;
        var cells = s?.Source as IPhotoCells;
        bool retro = look == 2;
        var sk = Theme.RetroSkin;
        if (retro) sk.ListBox(_p, v);
        var inner = retro ? RectangleF.Inflate(v, -2, -2) : v;
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
        int cols = retro ? 3 : 4;
        float bar = retro ? 16 : 10, gap = retro ? 4 : 10, headH = retro ? 17 : 38;
        float gw = inner.Width - bar - (retro ? gap : 0);
        float cw = MathF.Floor((gw - (cols - 1) * gap) / cols), ch = MathF.Floor(cw * 9 / 16);
        var L = GalLayoutOf(s, cols, cw, ch, headH, gap);
        float viewH = inner.Height - (retro ? 2 * gap : 0), top0 = inner.Y + (retro ? gap : 0), left = inner.X + (retro ? gap : 0);
        float max = MathF.Max(0, L.Height - viewH);
        float step = (ch + gap) / 3;
        if (_galAnchor != null)
        {
            for (int i = 0; i < n; i++)
                if (s.Recent[i].Path == _galAnchor) { _galScroll = GalRowY(L, i) - (viewH - ch) / 2; break; }
            _galAnchor = null;
        }
        if (_ui.Wheel != 0 && !_ui.Inert) { _galScroll -= _ui.Wheel * step; _dirty = true; }
        _galScroll = Math.Clamp(_galScroll, 0, max);
        if (retro && max > 0)
        {
            // the style's own scroll bar: its arrows a row each, the thumb where the grid is
            var sb = new RectangleF(inner.Right - 16, inner.Y, 16, inner.Height);
            var up = new RectangleF(sb.X, sb.Y, 16, 16);
            var dn = new RectangleF(sb.X, sb.Bottom - 16, 16, 16);
            if (_ui.Clickable("wgup", up, out _, out bool uh) && !_ui.Inert) _galScroll = MathF.Max(0, _galScroll - (ch + gap));
            if (_ui.Clickable("wgdn", dn, out _, out bool dh) && !_ui.Inert) _galScroll = MathF.Min(max, _galScroll + (ch + gap));
            float track = sb.Height - 32, th = MathF.Max(8, track * viewH / L.Height), ty = sb.Y + 16 + (track - th) * _galScroll / max;
            sk.ScrollBar(_p, sb, up, dn, new RectangleF(sb.X, MathF.Round(ty), 16, MathF.Round(th)), uh, dh);
        }
        float scroll = retro ? MathF.Round(_galScroll) : _galScroll;
        var clip = new RectangleF(inner.X, top0 - (retro ? 0 : 2), gw + (retro ? gap : 0), viewH + (retro ? 0 : 4));
        _p.PushClip(clip);
        int slot = 0;
        foreach (var day in L.Days)
        {
            if (day.Y - scroll > viewH) break;
            if (day.End - scroll < 0) continue;
            float hy = top0 + day.Y - scroll;
            if (hy + headH > top0 && hy < top0 + viewH) GalHeading(GalHead(day.Day, day.Count), left, hy, gw, headH, look);
            for (int k = 0; k < day.Count; k++)
            {
                float cy = hy + headH + k / cols * (ch + gap);
                if (cy > top0 + viewH) break;
                if (cy + ch < top0) continue;
                int i = day.First + k;
                var r = new RectangleF(left + k % cols * (cw + gap), cy, cw, ch);
                var hit = RectangleF.Intersect(r, clip);
                string id = Ui.Id(CellId, slot++ % 64);
                bool hover = false, tap = false;
                if (hit.Height > 4) tap = _ui.Clickable(id, hit, out hover, out _) && !_ui.Inert;
                GalCell(r, s.Recent[i], cells, look, hover && !_ui.Inert, _galPicked && i == _album.Index && _album.Path == s.Recent[i].Path);
                if (tap) GalleryPick(i, s.Recent[i].Path);
            }
        }
        _p.PopClip();
        if (!retro && max > 0)
        {
            float bh = MathF.Max(24, viewH * viewH / L.Height), by = top0 + (viewH - bh) * (_galScroll / max);
            _p.Fill(new RectangleF(inner.Right - 4, by, 4, bh), look == 1 ? Theme.WithAlpha(CyC, 0.55f) : Theme.WithAlpha(Bento.Text, 0.3f), 2);
        }
    }

    void GalHeading(string text, float x, float y, float w, float h, int look)
    {
        if (look == 2) { _ui.Text95(PhotoCut(text, w, Ui.F95, FontWeight.Bold, null, 1), new RectangleF(x, y + 1, w, 13), Blk95, true); return; }
        if (look == 1)
        {
            string t = Upper(text);
            CyMono(PhotoCut(t, w, CyNote, FontWeight.Bold, CyberHud.Mono), x, y + h - 12, w, CyY, CyNote, FontWeight.Bold);
            float tw = MathF.Min(w, CyMonoW(PhotoCut(t, w, CyNote, FontWeight.Bold, CyberHud.Mono), CyNote, FontWeight.Bold));
            if (x + tw + 12 < x + w) _p.Fill(new RectangleF(x + tw + 10, y + h - 17, w - tw - 10, 1), CyberHud.Edge);
            return;
        }
        TextAt(PhotoCut(text, w, SmallText), x, y + h - 10, w + 2, Bento.Text, SmallText, FontWeight.SemiBold);
    }

    void GalCell(RectangleF r, PhotoWatcher.Photo p, IPhotoCells cells, int look, bool hover, bool current)
    {
        var bmp = GalBitmap(p, cells, out float w, out float h);
        if (look == 2)
        {
            // the thumbnail view's: the picture inside its square-cornered frame, the style's selection round the one under the pointer
            if (hover || current) Theme.RetroSkin.ListSelection(_p, RectangleF.Inflate(r, 2, 2));
            var f = RectangleF.Inflate(r, -1, -1);
            if (bmp != null) _p.ImagePart(bmp, f, PhotoCover(w, h, f));
            else _p.Fill(f, 0xFFD4D0C8);
            _p.Stroke(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 0xFF808080, 1);
            return;
        }
        if (look == 1)
        {
            if (bmp != null) _p.ImagePart(bmp, r, PhotoCover(w, h, r));
            else _p.Fill(r, CyberHud.Card2);
            _p.Stroke(RectangleF.Inflate(r, 0.5f, 0.5f), hover || current ? CyC : CyberHud.CyanDim, hover ? 1.5f : 1);
            if (hover) PhotoCorners(RectangleF.Inflate(r, 3, 3), CyY, 9);
            return;
        }
        _p.PushRoundedClip(r, 10);
        if (bmp != null) _p.ImagePart(bmp, r, PhotoCover(w, h, r));
        else _p.Fill(r, Bento.Tile);
        if (hover) _p.Fill(r, 0x22FFFFFF);
        _p.PopRoundedClip();
        if (hover || current) _p.Stroke(RectangleF.Inflate(r, 1, 1), current && !hover ? Theme.WithAlpha(Bento.Accent, 0.7f) : Bento.Accent, 2.5f, 11);
    }

    // ---- modern / Cyber: the grid in the card (its heading DrawDetail's: "Gallery", the count after it, Gallery lit left of the X) ----

    void GalleryModern(RectangleF E)
    {
        PhotoOpenShown = null;
        var s = _ph;
        var back = new RectangleF(E.Right - 22 - 46 - 12 - 46, E.Y + 14, 46, 46);
        if (s is { Ready: true, Recent.Count: > 0 })
        {
            float nx = E.X + 24 + _p.MeasureWidth(Lang.T(GalleryWord), 24, FontWeight.SemiBold) + 12, room = back.X - 12 - nx;
            if (room > 40) TextAt(PhotoCut(GalCountText(s.Recent.Count), room, 24), nx, E.Y + 45, room + 2, Sub, 24, FontWeight.SemiBold);
        }
        if (Press("wphall", back, GridGlyph, null, true, true, 18, true)) GalleryToggle(null);
        GalGrid(new RectangleF(E.X + 24, E.Y + 76, E.Width - 48, E.Height - 76 - 18), 0);
        DeleteNoteModern(E);
    }

    void GalleryCyber(RectangleF E)
    {
        PhotoOpenShown = null;
        var s = _ph;
        var back = new RectangleF(E.Right - 22 - 46 - 12 - 46, E.Y + 14, 46, 46);
        if (s is { Ready: true, Recent.Count: > 0 })
        {
            string cnt = Upper(GalCountText(s.Recent.Count));
            float x = E.X + 24 + CyberHud.TagWidth(_p, Upper(GalleryWord), 18) + 14, room = back.X - 14 - x;
            if (CyMonoW(cnt) <= room) CyMono(cnt, x, E.Y + 23 + (26 + CyberHud.CapH(_p, CyNote)) / 2, room, CyC);
        }
        if (Press("wphall", back, GridGlyph, null, true, true, 18, true)) GalleryToggle(null);
        GalGrid(new RectangleF(E.X + 24, E.Y + 76, E.Width - 48, E.Height - 76 - 18), 1);
        DeleteNoteModern(E);
    }

    // ---- modern / Cyber: the buttons over the photo's top right, Delete's note at the card's foot ----

    void PhotoActions(RectangleF box, PhotoWatcher.Photo p)
    {
        if (p == null) return;
        bool armed = DeleteArmed(p), busy = _delBusy == p.Path;
        float bs = 46, y = box.Y + 10;
        float dw = armed || busy ? MathF.Ceiling(MathF.Max(bs, 18 + 8 + _p.MeasureWidth(Lang.T(busy ? "Deleting..." : DeleteAsk), 17, FontWeight.SemiBold) + 30)) : bs;
        var del = new RectangleF(box.Right - 10 - dw, y, dw, bs);
        var all = new RectangleF(del.X - 10 - bs, y, bs, bs);
        if (all.X < box.X + 4) { all.X = box.X + 4; all.Y = y + bs + 8; }
        if (Press("wphall", all, GridGlyph, null, false, true, 18, true)) GalleryToggle(p);
        if (Press("wphdel", del, DeleteGlyph, armed || busy ? (busy ? "Deleting..." : DeleteAsk) : null, armed, !busy, 18, !(armed || busy), accentFill: false,
                fill: Bento.Bad))
            DeleteTap(p);
    }

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

    // the status line's words while Delete asks or answers (null: the view's own)
    string DeleteStatus(PhotoWatcher.Photo p) =>
        DeleteBusy ? "Deleting..." : DeleteArmed(p) ? "Tap Delete again to move it to the Recycle Bin" : _delNote;

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
        TCaps(PhotoCut(GalTitle("> ls {0}"), c.Width - cw - 10, Ui.F95, FontWeight.Normal, null, 2), c.X, c.Y + 2, TerminalSkin.MID);
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
        var L = GalLayoutOf(s, 1, area.Width - 16, rowH - 1, rowH, 1);
        float max = MathF.Max(0, L.Height - area.Height);
        if (_galAnchor != null)
        {
            for (int i = 0; i < n; i++)
                if (s.Recent[i].Path == _galAnchor) { _galScroll = GalRowY(L, i) - area.Height / 2; break; }
            _galAnchor = null;
        }
        if (_ui.Wheel != 0 && !_ui.Inert) { _galScroll -= _ui.Wheel * rowH; _dirty = true; }
        _galScroll = MathF.Round(Math.Clamp(_galScroll, 0, max) / rowH) * rowH;
        if (max > 0)
        {
            var sb = new RectangleF(area.Right - 12, area.Y, 12, area.Height);
            var up = new RectangleF(sb.X, sb.Y, 12, 12);
            var dn = new RectangleF(sb.X, sb.Bottom - 12, 12, 12);
            if (_ui.Clickable("wgup", up, out _, out bool uh) && !_ui.Inert) _galScroll = MathF.Max(0, _galScroll - 3 * rowH);
            if (_ui.Clickable("wgdn", dn, out _, out bool dh) && !_ui.Inert) _galScroll = MathF.Min(max, _galScroll + 3 * rowH);
            float track = sb.Height - 24, th = MathF.Max(6, track * area.Height / L.Height), ty = sb.Y + 12 + (track - th) * _galScroll / max;
            Theme.RetroSkin.ScrollBar(_p, sb, up, dn, new RectangleF(sb.X, MathF.Round(ty), 12, MathF.Round(th)), uh, dh);
        }
        float w = area.Width - 16;
        _p.PushClip(new RectangleF(area.X, area.Y, w + 2, area.Height));
        int slot = 0;
        foreach (var day in L.Days)
        {
            float hy = area.Y + day.Y - _galScroll;
            if (hy > area.Bottom) break;
            if (area.Y + day.End - _galScroll < area.Y) continue;
            if (hy + rowH > area.Y) TCaps(PhotoCut(TName("# " , GalHead(day.Day, day.Count)), w, Ui.F95, FontWeight.Normal, null, 2), area.X, hy + 2, TerminalSkin.HOT);
            for (int k = 0; k < day.Count; k++)
            {
                float y = hy + rowH + k * rowH;
                if (y > area.Bottom) break;
                if (y + rowH < area.Y) continue;
                int i = day.First + k;
                var p = s.Recent[i];
                var row = new RectangleF(area.X, y, w, rowH);
                bool tap = _ui.Clickable(Ui.Id(CellId, slot++ % 64), RectangleF.Intersect(row, area), out bool hover, out _) && !_ui.Inert;
                bool cur = _galPicked && i == _album.Index && _album.Path == p.Path;
                if (hover || cur) TerminalSkin.Lit(_p, row);
                TCaps(PhotoCut(TName("", p.Name), w - 4, Ui.F95, FontWeight.Normal, null, 2), area.X + 2, y + 2, hover || cur ? TerminalSkin.BG : TerminalSkin.INK);
                if (tap) GalleryPick(i, p.Path);
            }
        }
        _p.PopClip();
    }
}
