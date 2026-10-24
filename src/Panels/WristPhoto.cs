using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Orbit;

// The latest photo widget (Erik 10-17: a photo taken in VRChat pops up on the watch): the newest of VRChat's screenshots (PhotoWatcher,
// Sys/PhotoWatcher.cs: the folder followed, a thumbnail each, the world and the people from VRCX's metadata). Sizes: 1 x 1 just the
// picture; 2 x 1 and 2 x 2 the picture with how long ago and the world (when known); on a 4 x 3 page S, M and L. A tap grows it into
// the photo big (its aspect kept), when it was taken, the world and the people; the stick or the arrows step through that day's
// photos (the newest 20; a thumbnail is made when the view steps to it); Open in VR Gallery - Erik's photo album app, started as it
// is (it cannot be handed a photo), else Windows' viewer opens the photo (PhotoGallery; only ever on a tap). A new photo pops the
// widget: a short scale and a camera flash (Look > Motion Full only); the app notes it in Orbit's notifications (OrbitApp.Photos.cs).
// Modern: the photo is the card (rounded), the words over a dark fade at its bottom. Cyber: a HUD frame - the photo inset in the card,
// a corner bracket at each corner, the yellow CAPTURE tag, a monospace timestamp. The retro styles: each its own picture viewer -
// 95 Imaging (here), XP's Windows Picture and Fax Viewer, 7's Windows Photo Viewer, the Mac's PictureViewer in Platinum, the
// Terminal's photo in coloured characters (src/Ui/Skins/<style>/WristPhoto.<style>.cs).
// The thumbnails become bitmaps once (a few kept); the words are made when the photo, the minute or the language changes: a frame at
// rest makes nothing.
// On this day (WristMemory.cs) is drawn by these same views on its own set of photos: each set has its own snapshot, opened photo,
// words and bitmaps (PhotoSet), and a view picks the set of the widget it draws first (UseSet). The gallery (WristGallery.cs) is a
// third set, every photo: the opened view draws it while the gallery is open (UseOpenSet), its grid or one of its photos big.
public sealed partial class WristPanel
{
    public const string PhotoKindIcon = "\uE722";          // Segoe's camera: the widget in the editor's palette
    const string GalleryGlyph = "\uE8A7";                  // open in a new window
    const string PhPrev = "\uE76B", PhNext = "\uE76C";              // the arrows: the one before, the one after
    const string OpenGallery = "Open in VR Gallery";
    const float PopLen = 0.6f;                             // s: the new photo's scale and flash

    // the app: open this photo (PhotoGallery.Open); null in the tools - a tap is only noted (LastOpenedPhoto)
    public Action<string> OnOpenPhoto;
    public string LastOpenedPhoto { get; private set; }

    // A set of photos the views draw: its snapshot, the opened view's place in it, its words and bitmaps, its texts
    sealed class PhotoSet
    {
        public bool Memory;                         // On this day's (else the latest photo's)
        public bool Album;                          // the gallery's: every photo (WristGallery.cs)
        public PhotoWatcher.Snapshot Snap;          // the photos of this frame
        public long Stamp = -1;
        public string NewestPath;                   // the newest seen: another one pops the widget
        public double PopAt = -10;
        public int Index;                           // the opened view's photo (0: the newest)
        public string Path;                         // ... by its file: a photo coming in does not move the view off it
        public float Acc;
        public double ScrollAt = -10;
        public readonly (uint[] px, ID2D1Bitmap bmp)[] Bmps = new (uint[], ID2D1Bitmap)[4];
        public int BmpNext;
        public readonly PhotoWords New = new(), Open = new();
        public string WidgetShown, OpenShown;       // --snapshot's hooks
        public bool EmptyShown;
        public string[] Cap, CapHud;                // the card's caption (modern / Cyber), when it has no photo
        public string None, Hint, Tag, TitleXp, TitleMac;
    }

    readonly PhotoSet _latest = new()
    {
        Cap = new[] { "Latest photo", "Photo" }, CapHud = new[] { "Capture", "Cap" }, None = "No photos yet", Hint = "Photos you take in VRChat show up here.",
        Tag = "Capture", TitleXp = "My Pictures", TitleMac = "Scrapbook",
    };
    PhotoSet _setNow;
    PhotoSet _set => _setNow ?? _latest;

    // the set a widget of that type draws (a struct: nothing allocated); disposed, the one before is back
    readonly struct SetScope : IDisposable
    {
        readonly WristPanel _w;
        readonly PhotoSet _was;
        public SetScope(WristPanel w, PhotoSet s) { _w = w; _was = w._setNow; w._setNow = s; }
        public void Dispose() => _w._setNow = _was;
    }

    SetScope UseSet(bool memory) => new(this, memory ? _memory : _latest);
    SetScope UseSet(string type) => UseSet(type == "memory");

    // the set's state under the names the views had before there were two sets
    PhotoWatcher.Snapshot _ph { get => _set.Snap; set => _set.Snap = value; }
    long _phStamp { get => _set.Stamp; set => _set.Stamp = value; }
    string _phNewest { get => _set.NewestPath; set => _set.NewestPath = value; }
    double _phPopAt { get => _set.PopAt; set => _set.PopAt = value; }
    int _phIndex { get => _set.Index; set => _set.Index = value; }
    string _phPath { get => _set.Path; set => _set.Path = value; }
    float _phAcc { get => _set.Acc; set => _set.Acc = value; }
    double _phScrollAt { get => _set.ScrollAt; set => _set.ScrollAt = value; }
    (uint[] px, ID2D1Bitmap bmp)[] _phBmps => _set.Bmps;
    int _phBmpNext { get => _set.BmpNext; set => _set.BmpNext = value; }
    string[] CapPhoto => _set.Cap;
    string[] CapCapture => _set.CapHud;
    string NoPhotos => _set.None;
    string PhotosHint => _set.Hint;

    PhotoWatcher.Photo Newest => _ph is { Recent.Count: > 0 } s ? s.Recent[0] : null;
    int PhotoCount => _ph?.Recent.Count ?? 0;

    // the snapshot changed (Update draws again) - the latest photo's or On this day's
    bool PhotoChanged(PanelData d) => GalleryChanged(d) | (C.Face == "custom" && ((!ReferenceEquals(d.Photos, _latest.Snap) && (HasWidget("photo") || DetailIs("photo"))) ||
        (!ReferenceEquals(d.Memories, _memory.Snap) && (HasWidget("memory") || DetailIs("memory")))));

    PhotoWatcher.Snapshot SetData(PanelData d) => _set.Album ? d.Album : _set.Memory ? d.Memories : d.Photos;

    // once a frame, before the first photo is drawn: the snapshot taken, a new photo popped, the opened view kept on its photo
    void PhotoFrame(PanelData d)
    {
        if (_delDone) { _delDone = false; DeleteFinished(); }      // Delete's answer, before the view picks its photo (WristGallery.cs)
        DeleteTick();
        long s = _ui.BeginStamp;
        if (s == _phStamp) return;
        _phStamp = s;
        var snap = SetData(d);
        if (!ReferenceEquals(snap, _ph)) PhotoTake(snap);
        if (Clock.Now - _phPopAt < PopLen) _ui.Animating = true;
    }

    void PhotoTake(PhotoWatcher.Snapshot s)
    {
        bool onNewest = _phIndex == 0;
        _ph = s;
        var n = Newest;
        if (n != null && n.Path != _phNewest && n.Fresh && (DateTime.Now - n.Seen).TotalSeconds < 60 && Motion.Full && !Motion.Still) PhotoPop();
        _phNewest = n?.Path;
        // the opened view: on the newest it follows a new one; on another it stays on that one
        int count = PhotoCount;
        if (count == 0) { _phIndex = 0; _phPath = null; return; }
        int at = onNewest ? 0 : -1;
        if (at < 0 && _phPath != null)
            for (int i = 0; i < count; i++) if (s.Recent[i].Path == _phPath) { at = i; break; }
        _phIndex = at >= 0 ? at : Math.Clamp(_phIndex, 0, count - 1);
        _phPath = s.Recent[_phIndex].Path;
    }

    // a widget opened: on the newest (OpenDetail) - in both sets
    void PhotoFromNewest()
    {
        FromNewest(_latest);
        FromNewest(_memory);
        GalleryReset();
    }

    static void FromNewest(PhotoSet s)
    {
        s.Index = 0;
        s.Path = s.Snap is { Recent.Count: > 0 } x ? x.Recent[0].Path : null;
        s.Acc = 0;
    }

    // a new photo: the widget pops (the modern cards spring from a little smaller, past their size and back) and flashes
    void PhotoPop()
    {
        _phPopAt = Clock.Now;
        _dirty = true;
        if (Theme.Classic) return;
        var list = Layout;
        if (list == null) return;
        foreach (var w in list)
            if (w?.Type == "photo") _ui.SpringFrom(Ui.Id("was", Wid(w)), 0.9f);
    }

    // 0..1 while the pop plays (1: it just came), 0 at rest
    float PhotoPopK()
    {
        float t = (float)(Clock.Now - _phPopAt);
        if (t < 0 || t >= PopLen) return 0;
        _ui.Animating = true;
        return 1 - t / PopLen;
    }

    // the thumbnail as a bitmap (a few kept, by their pixels: a photo whose metadata came later keeps its bitmap); the newest's is
    // never the one let go (the face draws it in the same frame as the opened view's)
    ID2D1Bitmap PhotoBitmap(PhotoWatcher.Photo p)
    {
        if (p?.Thumb == null) return null;
        for (int i = 0; i < _phBmps.Length; i++) if (ReferenceEquals(_phBmps[i].px, p.Thumb)) return _phBmps[i].bmp;
        int k = _phBmpNext;
        if (_phBmps[k].px != null && ReferenceEquals(_phBmps[k].px, Newest?.Thumb)) k = (k + 1) % _phBmps.Length;
        _phBmpNext = (k + 1) % _phBmps.Length;
        _phBmps[k].bmp?.Dispose();
        _phBmps[k] = (p.Thumb, _p.CreateBitmap(p.Thumb, p.TW, p.TH));
        return _phBmps[k].bmp;
    }

    void PhotoDispose()
    {
        foreach (var b in new[] { _latest.Bmps, _memory.Bmps })
            for (int i = 0; i < b.Length; i++) { b[i].bmp?.Dispose(); b[i] = default; }
        GalleryDispose();
        TArtDispose();
    }

    // the part of a w x h picture that fills dst (cut at the sides or the top and bottom), and the picture whole inside box
    static RectangleF PhotoCover(float w, float h, RectangleF dst)
    {
        float s = MathF.Max(dst.Width / w, dst.Height / h), cw = dst.Width / s, ch = dst.Height / s;
        return new RectangleF((w - cw) / 2, (h - ch) / 2, cw, ch);
    }

    static RectangleF PhotoFit(float w, float h, RectangleF box, bool whole = false)
    {
        float s = MathF.Min(box.Width / w, box.Height / h), fw = w * s, fh = h * s;
        float x = box.X + (box.Width - fw) / 2, y = box.Y + (box.Height - fh) / 2;
        return whole ? new RectangleF(MathF.Round(x), MathF.Round(y), MathF.Round(fw), MathF.Round(fh)) : new RectangleF(x, y, fw, fh);
    }

    // the photo's shape before its thumbnail is there: from its name (VRChat writes the size in it), else 16:9
    static (float w, float h) PhotoAspect(PhotoWatcher.Photo p) =>
        p.TW > 0 ? (p.TW, p.TH) : p.Width > 0 && p.Height > 0 ? (p.Width, p.Height) : (16, 9);

    // ---- the words: made when the photo, the language or the clock changes; how long ago when its minute (hour, day) does ----

    sealed class PhotoWords
    {
        public PhotoWatcher.Photo P;
        public int Lang = -1;
        public bool H24;
        public DateTime Today;
        public string Time, Day, When, Stamp, Title, Count, CountHud;      // "14:20", "Today", "14:20 · Today", "2026-10-17 14:20:05", the file's name
        public string Caption, CaptionOf;           // a window's title made of the name (its pattern)
        public int AgoKey = int.MinValue, CountKey = -1;
        public string Ago, AgoShort;                // (AgoShort: On this day's "1 yr ago", where "1 year ago" does not fit)
    }

    readonly PhotoWords _phwNew = new(), _phwOpen = new();

    PhotoWords Words(PhotoWords w, PhotoWatcher.Photo p, DateTime now)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        if (!ReferenceEquals(w.P, p) || w.Lang != Lang.Version || w.H24 != h24 || w.Today != now.Date)
        {
            (w.P, w.Lang, w.H24, w.Today, w.AgoKey, w.CountKey, w.CaptionOf) = (p, Lang.Version, h24, now.Date, int.MinValue, -1, null);
            if (p == null) return w;
            var culture = CultureInfo.CurrentCulture;
            w.Time = ShortClock(p.Taken);
            int days = (int)(now.Date - p.Taken.Date).TotalDays;
            w.Day = p.Years > 0 ? MemoryDate(p.Taken, culture)            // On this day: the date with its year (WristMemory.cs)
                : days == 0 ? Lang.T("Today") : days == 1 ? Lang.T("Yesterday") : Capital(p.Taken.ToString(culture.DateTimeFormat.MonthDayPattern, culture));
            w.When = w.Time + " · " + w.Day;
            if (p.Years > 0) (w.Ago, w.AgoShort) = (MemoryAgo(p, culture, false), MemoryAgo(p, culture, true));      // "1 year ago · Oct 16"
            w.Stamp = p.Taken.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            w.Title = p.Name;
        }
        if (p == null) return w;
        var span = now - p.Taken;
        int key = span.TotalMinutes < 1 ? -1 : span.TotalMinutes < 60 ? (int)span.TotalMinutes : span.TotalHours < 24 ? 1000 + (int)span.TotalHours
            : 100000 + (int)(now.Date - p.Taken.Date).TotalDays;
        if (key != w.AgoKey && p.Years == 0)
        {
            w.AgoKey = key;
            w.Ago = key < 0 ? Lang.T("Just now") : key < 1000 ? Lang.F("{0} min ago", key) : key < 100000 ? Lang.F("{0} h ago", key - 1000)
                : key - 100000 <= 1 ? Lang.T("Yesterday") : Lang.F("{0} days ago", key - 100000);
        }
        int ck = _phIndex * 1000 + PhotoCount;
        if (ck != w.CountKey)
        {
            w.CountKey = ck;
            w.Count = Lang.F("{0} of {1}", PhotoCount - _phIndex, PhotoCount);
            w.CountHud = $"{PhotoCount - _phIndex:00}/{PhotoCount:00}";
        }
        return w;
    }

    // a window's title made of the photo's name ("Imaging - VRChat_....png"): made once per photo and pattern
    static string PhotoCaption(PhotoWords w, string pattern)
    {
        if (w.P == null) return "";
        if (!ReferenceEquals(w.CaptionOf, pattern)) { w.CaptionOf = pattern; w.Caption = Lang.F(pattern, w.Title); }
        return w.Caption;
    }

    // a small count as text (0..99 made once)
    static readonly string[] PhotoNums = MakeNums();
    static string[] MakeNums()
    {
        var a = new string[100];
        for (int i = 0; i < a.Length; i++) a[i] = i.ToString(CultureInfo.InvariantCulture);
        return a;
    }
    static string PhotoNum(int n) => n >= 0 && n < PhotoNums.Length ? PhotoNums[n] : n.ToString(CultureInfo.InvariantCulture);

    // a text over two lines of w (as many words as fit on the first, the rest cut on the second), kept like PhotoCut
    readonly Dictionary<(string s, float w, float size, string fam), (string, string)> _phTwo = new();
    (string, string) PhotoTwo(string s, float w, float size, FontWeight wt, string fam)
    {
        if (string.IsNullOrEmpty(s)) return ("", "");
        if (_phCutLang != Lang.Version || _phTwo.Count > 32) { _phTwo.Clear(); _phCut.Clear(); _phPeople.Clear(); _phCutLang = Lang.Version; }
        var key = (s, w, size, fam);
        if (_phTwo.TryGetValue(key, out var r)) return r;
        string t = Lang.T(s);
        if (_p.MeasureWidth(t, size, wt, fam) <= w) r = (t, "");
        else
        {
            int cut = -1;
            for (int i = t.IndexOf(' '); i > 0; i = t.IndexOf(' ', i + 1))
            {
                if (_p.MeasureWidth(t[..i], size, wt, fam) > w) break;
                cut = i;
            }
            r = cut < 0 ? (Ellipsize(t, w, size, wt, fam), "") : (t[..cut], Ellipsize(t[(cut + 1)..].TrimStart(), w, size, wt, fam));
        }
        _phTwo[key] = r;
        return r;
    }

    // a text cut to fit (an ellipsis), kept for its width: a frame at rest cuts nothing again. mode: 0 the modern painter's text,
    // 1 the 95 font, 2 the Terminal's
    readonly Dictionary<(string s, float w, float size, FontWeight wt, string fam, int mode), string> _phCut = new();
    int _phCutLang = -1;

    string PhotoCut(string s, float w, float size, FontWeight wt = FontWeight.SemiBold, string fam = null, int mode = 0)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (_phCutLang != Lang.Version || _phCut.Count > 96) { _phCut.Clear(); _phPeople.Clear(); _phTwo.Clear(); _phCutLang = Lang.Version; }
        var key = (s, w, size, wt, fam, mode);
        if (_phCut.TryGetValue(key, out var r)) return r;
        r = mode switch
        {
            1 => Fit(s, w, size, wt == FontWeight.Bold),
            2 => TFit(s, w),
            _ => Ellipsize(s, w, size, wt, fam),
        };
        _phCut[key] = r;
        return r;
    }

    // the people as they fit w: "Mira, Tobi, Sam" - or the first ones and how many more ("Mira, Tobi +3"); kept like PhotoCut
    readonly Dictionary<(PhotoWatcher.Photo p, float w, float size, int mode, bool with), string> _phPeople = new();

    string PhotoPeople(PhotoWatcher.Photo p, float w, float size, FontWeight wt = FontWeight.Normal, int mode = 0, bool with = true)
    {
        if (p == null || p.Players.Length == 0) return null;
        if (_phCutLang != Lang.Version || _phPeople.Count > 48) { _phPeople.Clear(); _phCut.Clear(); _phTwo.Clear(); _phCutLang = Lang.Version; }
        var key = (p, w, size, mode, with);
        if (_phPeople.TryGetValue(key, out var r)) return r;
        float Wd(string s) => mode switch { 1 => _ui.Width95(s, wt == FontWeight.Bold, size), 2 => TerminalSkin.W(s), _ => _p.MeasureWidth(s, size, wt) };
        string[] names = p.Players;
        string Line(int k)
        {
            string s = string.Join(", ", names, 0, k) + (k < names.Length ? $" +{names.Length - k}" : "");
            if (with) s = Lang.F("with {0}", s);
            return mode == 2 ? s.ToUpperInvariant() : s;
        }
        r = null;
        for (int k = names.Length; k >= 1 && r == null; k--)
        {
            string s = Line(k);
            if (Wd(s) <= w) r = s;
        }
        r ??= PhotoCut(Line(1), w, size, wt, null, mode);
        _phPeople[key] = r;
        return r;
    }

    // the stick in the opened view: a step a notch or so from rest, then one every three (~4 a second at full tilt); up the newer.
    // The gallery's grid: it scrolls (WristGallery.cs)
    bool PhotoScroll(float n)
    {
        if (!(DetailIs("photo") || DetailIs("memory")) || _closing) return false;
        if (_gal && _galGrid) { _ptr.Wheel += n; _dirty = true; return true; }
        using var set = UseOpenSet();
        double now = Clock.Now;
        if (now - _phScrollAt > 0.35) _phAcc = 2 * MathF.Sign(n);
        _phScrollAt = now;
        _phAcc += n;
        if (MathF.Abs(_phAcc) >= 3)
        {
            int up = _phAcc > 0 ? 1 : -1;
            _phAcc -= 3 * up;
            PhotoStep(-up);
        }
        return true;
    }

    // dir +1: the one before (older), -1: the one after (newer)
    void PhotoStep(int dir)
    {
        int n = PhotoCount;
        if (n == 0) return;
        int i = Math.Clamp(_phIndex + dir, 0, n - 1);
        if (i == _phIndex) return;
        _phIndex = i;
        _phPath = _ph.Recent[i].Path;
        _dirty = true;
    }

    // the opened view's photo: its thumbnail asked for (or, it has one, the next older one's: stepping on shows it at once)
    PhotoWatcher.Photo PhotoShown()
    {
        int n = PhotoCount;
        if (n == 0) return null;
        _phIndex = Math.Clamp(_phIndex, 0, n - 1);
        var p = _ph.Recent[_phIndex];
        if (_ph.Source != null)
        {
            if (p.Thumb == null) _ph.Source.Want(p.Path);
            else if (_phIndex + 1 < n && _ph.Recent[_phIndex + 1].Thumb == null) _ph.Source.Want(_ph.Recent[_phIndex + 1].Path);
        }
        return p;
    }

    void PhotoOpen(PhotoWatcher.Photo p)
    {
        if (p == null) return;
        LastOpenedPhoto = p.Path;
        OnOpenPhoto?.Invoke(p.Path);
    }

    // --snapshot's hooks: the photo the widget shows (its file), the opened view's photo, the empty state drawn (the latest photo's;
    // On this day's: Memory*, WristMemory.cs)
    public string PhotoWidgetShown { get => _set.WidgetShown; private set => _set.WidgetShown = value; }
    public string PhotoOpenShown { get => _set.OpenShown; private set => _set.OpenShown = value; }
    public bool PhotoEmptyShown { get => _set.EmptyShown; private set => _set.EmptyShown = value; }
    public int PhotoOpenIndex => _phIndex;
    public bool PhotoPopping => Clock.Now - _phPopAt < PopLen;

    // ================= Modern =================

    // the photo is the card: drawn as its background (the layout check's card too), the dark fade the words sit on
    bool PhotoCard(WatchWidgets.Kind k, PanelData d)
    {
        if (k.Id is not ("photo" or "memory") || Theme.Cyber) return false;
        using var set = UseSet(k.Id);
        PhotoFrame(d);
        return PhotoBitmap(Newest) != null;
    }

    void PhotoBack(RectangleF r, WatchWidget w, PanelData d)
    {
        using var set = UseSet(w.Type);
        var p = Newest;
        var bmp = PhotoBitmap(p);
        _p.PushRoundedClip(r, CardRadius);
        _p.ImagePart(bmp, r, PhotoCover(p.TW, p.TH, r));
        if (w.W > 1 || w.H > 1) _p.FillGradient(r, 0x00000000, 0x14000000, 0xC0000000, 0, 90);      // the words' fade
        _p.PopRoundedClip();
        _p.Stroke(RectangleF.Inflate(r, -0.6f, -0.6f), 0x1AFFFFFF, Bento.EdgeWidth, CardRadius - 0.6f);
        (_halo, _haloCard) = (0, RectangleF.Empty);
    }

    void PhotoOrbit(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        using var set = UseSet(w.Type);
        PhotoFrame(d);
        var p = Newest;
        float pad = PadX(r), x = r.X + pad, iw = r.Width - 2 * pad;
        if (PhotoBitmap(p) == null) { PhotoEmptyCard(r, w, p == null && _ph is { Ready: true }); return; }
        PhotoWidgetShown = p.Path;
        PhotoEmptyShown = false;
        if (w.W > 1 || w.H > 1)
        {
            var words = Words(_phwNew, p, Now);
            float by = r.Bottom - 14;
            bool tall = r.Height >= 180 && p.Players.Length > 0;
            if (tall)
            {
                TextAt(PhotoPeople(p, iw, SmallText), x, by, iw + 2, 0xD9FFFFFF, SmallText, FontWeight.Normal);
                by -= 22;
            }
            TextAt(words.Ago, x, by, iw + 2, 0xD9FFFFFF, SmallText, FontWeight.SemiBold);
            if (p.World != null) TextAt(PhotoCut(p.World, iw, 20), x, by - 24, iw + 2, 0xFFFFFFFF, 20, FontWeight.SemiBold);
        }
        else if (p.Years > 0) MemoryBadge(r, Words(_phwNew, p, Now));        // On this day's 1x1: how many years ago (WristMemory.cs)
        PhotoFlash(r, CardRadius);
    }

    // the camera flash of a new photo: a white veil fading, a ring of the accent round the card
    void PhotoFlash(RectangleF r, float radius)
    {
        float k = PhotoPopK();
        if (k <= 0) return;
        _p.Fill(r, Theme.WithAlpha(0xFFFFFFFF, 0.6f * k * k), radius);
        _p.Stroke(RectangleF.Inflate(r, 1.5f, 1.5f), Theme.WithAlpha(Theme.Cyber ? CyC : Bento.Accent, k), 3, radius + 1.5f);
    }

    // no photo (yet): the card's caption, the camera, "No photos yet" (once the folder was looked at; while it is, the camera alone)
    void PhotoEmptyCard(RectangleF r, WatchWidget w, bool none)
    {
        PhotoWidgetShown = null;
        PhotoEmptyShown = none;
        float pad = PadX(r), iw = r.Width - 2 * pad;
        bool s1 = w.W == 1 && w.H == 1;
        uint ink = Theme.Cyber ? CyberHud.Faint : Bento.Faint;
        if (s1) { _p.Icon(PhotoKindIcon, new RectangleF(r.X, r.Y + 6, r.Width, r.Height - 6), ink, 30); return; }
        if (Theme.Cyber) CyberCaption(r.X + pad, r, iw, CapCapture);
        else Caption(r, iw, CapPhoto);
        float gs = r.Height >= 150 ? 40 : 28;
        float gy = r.Height >= 150 ? r.Y + r.Height / 2 - gs : r.Y + 32;
        _p.Icon(PhotoKindIcon, new RectangleF(r.X, gy, r.Width, gs + 8), ink, gs);
        if (!none) return;
        float by = r.Height >= 150 ? gy + gs + 34 : r.Bottom - 14;
        if (Theme.Cyber) CyMono(PhotoCut(Upper(NoPhotos), iw, CyNote, FontWeight.SemiBold, CyberHud.Mono), r.X + pad, by, iw + 2, CyG, CyNote,
            FontWeight.SemiBold, TextAlignment.Center);
        else TextAt(PhotoCut(NoPhotos, iw, SmallText), r.X + pad, by, iw + 2, Sub, SmallText, FontWeight.SemiBold, TextAlignment.Center);
    }

    // Open in VR Gallery: a tile with its label, left of the X (DrawDetail's, 46 px at the top right) - Notes' Clear's place
    RectangleF PhotoGalleryRect(RectangleF E)
    {
        float lw = _p.MeasureWidth(Lang.T(OpenGallery), SmallText, FontWeight.SemiBold);
        float w = MathF.Ceiling(18 + 8 + lw + 34);
        return new RectangleF(E.Right - 22 - 46 - 12 - w, E.Y + 14, w, 46);
    }

    // E: the opened card. Its heading (the widget's name) and the X are DrawDetail's: "12 of 20" after the name, Open in VR Gallery
    // left of the X; the photo as big as it fits, the words over its foot, an arrow at each side
    void PhotoDetail(RectangleF E, PanelData d)
    {
        using var set = UseOpenSet();
        PhotoFrame(d);
        if (Theme.Cyber) { CyberPhotoDetail(E, d); return; }
        if (_gal && _galGrid) { GalleryModern(E); return; }         // the gallery's grid (WristGallery.cs)
        var p = PhotoShown();
        var now = Now;
        var words = Words(_phwOpen, p, now);
        PhotoOpenShown = p?.Path;
        if (p != null)
        {
            float nx = E.X + 24 + _p.MeasureWidth(Lang.T(CapPhoto[0]), 24, FontWeight.SemiBold) + 12;
            TextAt(words.Count, nx, E.Y + 45, 110, Sub, 24, FontWeight.SemiBold);
        }
        if (Press("wphgal", PhotoGalleryRect(E), GalleryGlyph, OpenGallery, false, p != null, 18)) PhotoOpen(p);
        var c = new RectangleF(E.X + 24, E.Y + 74, E.Width - 48, E.Height - 96);
        if (p == null) { PhotoNone(c); return; }
        var (aw, ah) = PhotoAspect(p);
        var box = PhotoFit(aw, ah, c);
        // a photo standing up: at the left (after its arrow), its words beside it on the card
        bool side = box.Width < c.Width * 0.62f;
        if (side)
        {
            box = PhotoFit(aw, ah, new RectangleF(c.X + 64, c.Y, c.Width - 128 - 220, c.Height));
            box.X = c.X + 64;
        }
        var bmp = PhotoBitmap(p);
        _p.PushRoundedClip(box, 16);
        if (bmp != null) _p.ImagePart(bmp, box, new RectangleF(0, 0, p.TW, p.TH));
        else { _p.Fill(box, Bento.Tile); _p.Icon(PhotoKindIcon, box, Bento.Faint, 40); }
        if (!side) _p.FillGradient(box, 0x00000000, 0x00000000, 0xC0000000, 0, 90);
        _p.PopRoundedClip();
        PhotoFlash(box, 16);
        if (side) PhotoWordsBeside(p, words, new RectangleF(box.Right + 24, c.Y, c.Right - 64 - 12 - box.Right - 24, c.Height));
        else
        {
            // the words over its foot: the world, when (and how long ago), the people
            float x = box.X + 18, tw = box.Width - 36, by = box.Bottom - 16;
            string people = PhotoPeople(p, tw, SmallText);
            if (people != null) { TextAt(people, x, by, tw + 2, 0xD9FFFFFF, SmallText, FontWeight.Normal); by -= 24; }
            float agw = _p.MeasureWidth(words.Ago, SmallText, FontWeight.Normal);
            TextAt(PhotoCut(words.When, tw - agw - 20, SmallText), x, by, tw + 2, 0xFFFFFFFF, SmallText, FontWeight.SemiBold);
            TextAt(words.Ago, box.Right - 18 - agw, by, agw + 4, 0xD9FFFFFF, SmallText, FontWeight.Normal);
            if (p.World != null) TextAt(PhotoCut(p.World, tw, 24), x, by - 30, tw + 2, 0xFFFFFFFF, 24, FontWeight.SemiBold);
        }
        // the arrows at its sides (the card's, beside a photo standing up): the one before (older), the one after (newer) - only where
        // there is one that way
        float ay = c.Y + c.Height / 2 - 26;
        int n = PhotoCount;
        var pr = side ? new RectangleF(c.X, ay, 52, 52) : new RectangleF(box.X + 12, ay, 52, 52);
        var nr = side ? new RectangleF(c.Right - 52, ay, 52, 52) : new RectangleF(box.Right - 64, ay, 52, 52);
        if (_phIndex < n - 1 && Press("wphprev", pr, PhPrev, null, false, true, 20, true)) PhotoStep(1);
        if (_phIndex > 0 && Press("wphnext", nr, PhNext, null, false, true, 20, true)) PhotoStep(-1);
        PhotoActions(box, p);                       // Gallery, Delete at its top right (WristGallery.cs)
        DeleteNoteModern(E);
    }

    // beside a photo standing up: the world (two lines), when, how long ago, the people
    void PhotoWordsBeside(PhotoWatcher.Photo p, PhotoWords words, RectangleF col)
    {
        float w = col.Width, x = col.X;
        bool world = p.World != null;
        string people = PhotoPeople(p, w, SmallText);
        float h = (world ? 68 : 0) + 24 + 24 + (people != null ? 30 : 0);
        float y = col.Y + MathF.Max(0, (col.Height - h) / 2) + 22;
        if (world)
        {
            var (l1, l2) = PhotoTwo(p.World, w, 22, FontWeight.SemiBold, null);
            TextAt(l1, x, y, w + 2, Bento.Text, 22, FontWeight.SemiBold);
            TextAt(l2, x, y + 28, w + 2, Bento.Text, 22, FontWeight.SemiBold);
            y += 68;
        }
        TextAt(PhotoCut(words.When, w, SmallText), x, y, w + 2, Bento.Text, SmallText, FontWeight.SemiBold);
        TextAt(PhotoCut(words.Ago, w, SmallText, FontWeight.Normal), x, y + 24, w + 2, Sub, SmallText, FontWeight.Normal);
        if (people != null) TextAt(people, x, y + 54, w + 2, Sub, SmallText, FontWeight.Normal);
    }

    // nothing yet: the camera, the words, where they come from
    void PhotoNone(RectangleF c)
    {
        PhotoEmptyShown = true;
        PhotoOpenShown = null;
        float my = c.Y + (c.Height - 124) / 2;
        _p.Icon(PhotoKindIcon, new RectangleF(c.X, my, c.Width, 52), Bento.Faint, 40);
        _p.Text(PhotoCut(NoPhotos, c.Width, 22), new RectangleF(c.X, my + 58, c.Width, 34), Bento.Text, 22, FontWeight.SemiBold, TextAlignment.Center);
        _p.Text(PhotoCut(PhotosHint, c.Width, SmallText, FontWeight.Normal), new RectangleF(c.X, my + 94, c.Width, 28), Sub, SmallText, FontWeight.Normal,
            TextAlignment.Center);
    }

    // ================= Cyber =================

    // four L-shaped corners just inside r
    void PhotoCorners(RectangleF r, uint c, float arm, float w = 2)
    {
        _p.Fill(new RectangleF(r.X, r.Y, arm, w), c); _p.Fill(new RectangleF(r.X, r.Y, w, arm), c);
        _p.Fill(new RectangleF(r.Right - arm, r.Y, arm, w), c); _p.Fill(new RectangleF(r.Right - w, r.Y, w, arm), c);
        _p.Fill(new RectangleF(r.X, r.Bottom - w, arm, w), c); _p.Fill(new RectangleF(r.X, r.Bottom - arm, w, arm), c);
        _p.Fill(new RectangleF(r.Right - arm, r.Bottom - w, arm, w), c); _p.Fill(new RectangleF(r.Right - w, r.Bottom - arm, w, arm), c);
    }

    // The card's frame round the photo (8 px in: the card's edge band stays its own), a yellow bracket at each corner; two cells and up
    // the CAPTURE tag over its top-left, and at its foot a dark strip: the time in cyan monospace, how long ago grey, the world over it
    // in the condensed capitals
    void CyberPhoto(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        using var set = UseSet(w.Type);
        PhotoFrame(d);
        var p = Newest;
        var bmp = PhotoBitmap(p);
        if (bmp == null) { PhotoEmptyCard(r, w, p == null && _ph is { Ready: true }); return; }
        PhotoWidgetShown = p.Path;
        PhotoEmptyShown = false;
        var f = RectangleF.Inflate(r, -8, -8);
        _p.ImagePart(bmp, f, PhotoCover(p.TW, p.TH, f));
        bool s1 = w.W == 1 && w.H == 1;
        if (!s1)
        {
            var words = Words(_phwNew, p, Now);
            float sh = p.World != null && f.Height >= 120 ? 44 : 24;
            var strip = new RectangleF(f.X, f.Bottom - sh, f.Width, sh);
            _p.Fill(strip, 0xD90B0A0C);
            _p.Fill(new RectangleF(strip.X, strip.Y, strip.Width, 1), CyberHud.Edge);
            float x = f.X + 8, right = f.Right - 8, by = f.Bottom - 8;
            string ago = Upper(words.Ago);
            float aw = CyMonoW(ago);
            float tw = CyMonoW(words.Time);
            CyMono(words.Time, x, by, tw + 2, CyC, CyNote, FontWeight.Bold);
            if (x + tw + 12 + aw <= right) CyMono(ago, right, by, aw + 2, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
            if (sh > 24)
                TextAt(PhotoCut(Upper(p.World), right - x, 17, FontWeight.Bold, Theme.Display), x, by - 20, right - x + 2, CyW, 17, FontWeight.Bold,
                    TextAlignment.Leading, Theme.Display);
            CyberCaption(r.X + PadX(r), r, r.Width - 2 * PadX(r), CapCapture);
        }
        else if (p.Years > 0) CyMemoryBadge(f, Words(_phwNew, p, Now));      // On this day's 1x1 (WristMemory.cs)
        float k = PhotoPopK();
        PhotoCorners(f, k > 0 ? Theme.Mix(CyY, CyC, k) : CyY, s1 ? 9 : 12);
        if (k > 0) _p.Fill(f, Theme.WithAlpha(0xFFFFFFFF, 0.5f * k * k));
    }

    // The opened capture: the CAPTURE tag is DrawDetail's (it types in), "12/20" after it in cyan, Open in VR Gallery left of the X;
    // the photo whole in its bracketed frame on the left, the stepping arrows and the timestamp under it; plates on the right - TIME,
    // WORLD, PEOPLE
    void CyberPhotoDetail(RectangleF E, PanelData d)
    {
        CyFrame();
        if (_gal && _galGrid) { GalleryCyber(E); return; }
        var p = PhotoShown();
        var now = Now;
        var words = Words(_phwOpen, p, now);
        PhotoOpenShown = p?.Path;
        float tagW = CyberHud.TagWidth(_p, Upper(_set.Tag), 18);
        var gal = PhotoGalleryRect(E);
        if (p != null)
        {
            string cnt = words.CountHud;
            float room = gal.X - 14 - (E.X + 24 + tagW + 14);
            if (CyMonoW(cnt) <= room) CyMono(cnt, E.X + 24 + tagW + 14, E.Y + 23 + (26 + CyberHud.CapH(_p, CyNote)) / 2, room, CyC);
        }
        if (Press("wphgal", gal, GalleryGlyph, OpenGallery, false, p != null, 18)) PhotoOpen(p);
        var c = new RectangleF(E.X + 24, E.Y + 74, E.Width - 48, E.Height - 96);
        if (p == null)
        {
            PhotoEmptyShown = true;
            var pl = new RectangleF(c.X, c.Y + (c.Height - 120) / 2, c.Width, 120);
            CyberHud.CardShape(_p, pl, CyberHud.Card2, CyberHud.Line);
            PhotoCorners(RectangleF.Inflate(pl, -6, -6), CyY, 12);
            CyMono(PhotoCut(Upper(NoPhotos), pl.Width - 40, 18, FontWeight.Bold, CyberHud.Mono), pl.X + 20, pl.Y + 56, pl.Width - 38, CyG, 18, FontWeight.Bold,
                TextAlignment.Center);
            CyMono(PhotoCut(Upper(PhotosHint), pl.Width - 40, CyNote, FontWeight.SemiBold, CyberHud.Mono), pl.X + 20, pl.Y + 84, pl.Width - 38, CyberHud.Faint,
                CyNote, FontWeight.SemiBold, TextAlignment.Center);
            return;
        }
        // the plates' column, the photo's area beside it
        const float colW = 156, gap = 18;
        var col = new RectangleF(c.Right - colW, c.Y, colW, c.Height);
        var area = new RectangleF(c.X + 8, c.Y + 8, c.Width - colW - gap - 16, c.Height - 62 - 16);
        var (aw, ah) = PhotoAspect(p);
        var box = PhotoFit(aw, ah, area);
        var bmp = PhotoBitmap(p);
        if (bmp != null) _p.ImagePart(bmp, box, new RectangleF(0, 0, p.TW, p.TH));
        else { _p.Fill(box, CyberHud.Card2); _p.Icon(PhotoKindIcon, box, CyberHud.Faint, 36); }
        _p.Stroke(RectangleF.Inflate(box, 0.6f, 0.6f), CyberHud.CyanDim, 1.25f);
        PhotoCorners(RectangleF.Inflate(box, 6, 6), CyY, 14);
        // under it: the arrows, the timestamp, how long ago
        float uy = c.Bottom - 46;
        int n = PhotoCount;
        if (Press("wphprev", new RectangleF(c.X, uy, 46, 46), PhPrev, null, false, _phIndex < n - 1, 18)) PhotoStep(1);
        if (Press("wphnext", new RectangleF(c.X + 56, uy, 46, 46), PhNext, null, false, _phIndex > 0, 18)) PhotoStep(-1);
        float sx = c.X + 56 + 46 + 16, sr = col.X - gap, by = uy + 30;
        string ago = Upper(words.Ago);
        float sw = CyMonoW(words.Stamp), agw = CyMonoW(ago);
        if (sx + sw <= sr) CyMono(words.Stamp, sx, by, sw + 2, CyC, CyNote, FontWeight.Bold);
        if (sx + sw + 14 + agw <= sr) CyMono(ago, sr, by, agw + 2, CyG, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
        // the plates
        float ph1 = 86, ph2 = 92;
        var t = new RectangleF(col.X, col.Y, col.Width, ph1);
        CyPlate(t, "Time", Upper(words.Day), CyG);
        CyValue(t.X + 14, t.Bottom - 16, words.Time, CyFit(words.Time, t.Width - 28, 34), CyW);
        var wp = new RectangleF(col.X, t.Bottom + 10, col.Width, ph2);
        CyPlate(wp, Lang.C("photo", "World"), null, 0);          // (VRChat's world: not the Desktops tab's "world", the space around you)
        if (p.World != null)
        {
            var (l1, l2) = PhotoTwo(Upper(p.World), wp.Width - 28, 18, FontWeight.Bold, Theme.Display);
            TextAt(l1, wp.X + 14, wp.Y + 52, wp.Width - 26, CyW, 18, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
            TextAt(l2, wp.X + 14, wp.Y + 74, wp.Width - 26, CyW, 18, FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        }
        else CyMono(PhotoCut(Upper("Not known"), wp.Width - 28, CyNote, FontWeight.SemiBold, CyberHud.Mono), wp.X + 14, wp.Y + 56, wp.Width - 26, CyberHud.Faint);
        var pp = new RectangleF(col.X, wp.Bottom + 10, col.Width, c.Bottom - wp.Bottom - 10);
        CyPlate(pp, "People", p.Players.Length > 0 ? PhotoNum(p.Players.Length) : null, CyC);
        float ly = pp.Y + 50, lh = 20;
        int rows = Math.Max(0, (int)((pp.Bottom - 10 - (ly - 14)) / lh));
        if (p.Players.Length == 0)
            CyMono(PhotoCut(Upper("Not known"), pp.Width - 28, CyNote, FontWeight.SemiBold, CyberHud.Mono), pp.X + 14, ly, pp.Width - 26, CyberHud.Faint);
        for (int i = 0; i < Math.Min(rows, p.Players.Length); i++)
        {
            bool more = i == rows - 1 && p.Players.Length > rows;
            string s = more ? PhotoMore(p.Players.Length - i) : PhotoCut(p.Players[i], pp.Width - 28, CyNote, FontWeight.SemiBold, CyberHud.Mono);
            CyMono(s, pp.X + 14, ly + i * lh, pp.Width - 26, more ? CyG : CyW);
        }
        float k = PhotoPopK();
        if (k > 0) _p.Fill(box, Theme.WithAlpha(0xFFFFFFFF, 0.5f * k * k));
        PhotoActions(box, p);
        DeleteNoteModern(E);
    }

    // "+3 MORE", made once per count and language
    readonly Dictionary<int, string> _phMore = new();
    int _phMoreLang = -1;
    string PhotoMore(int n)
    {
        if (_phMoreLang != Lang.Version) { _phMore.Clear(); _phMoreLang = Lang.Version; }
        if (!_phMore.TryGetValue(n, out var s)) _phMore[n] = s = Lang.F("+{0} more", n).ToUpper(CultureInfo.CurrentCulture);
        return s;
    }

    // ================= Windows 95 (and the retro styles' shared parts) =================

    // the picture in a sunken box (the style's own well), the photo filling it
    void PicBox95(RectangleF box, PhotoWatcher.Photo p, ID2D1Bitmap bmp, uint back = 0xFF808080)
    {
        var inner = RectangleF.Inflate(box, -2, -2);
        _p.Fill(inner, back);
        if (bmp != null) _p.ImagePart(bmp, inner, PhotoCover(p.TW, p.TH, inner));
        PhotoFlash95(inner);
        _p.Bevel(box, Painter.Edge.Sunken);
    }

    // a new photo, in a retro style: the picture flashes white and back in three steps (1995 had no fades)
    void PhotoFlash95(RectangleF r)
    {
        float k = PhotoPopK();
        if (k <= 0) return;
        _p.Fill(r, Theme.WithAlpha(0xFFFFFFFF, MathF.Ceiling(k * 3) / 3 * 0.7f));
    }

    // The widget in a little 95 window (Imaging when it has a title): one cell the picture alone; one row the picture beside how long
    // ago and the world; taller the picture over them
    void Photo95(RectangleF r, RectangleF c, WatchWidget w, PanelData d, int id)
    {
        using var set = UseSet(w.Type);
        PhotoFrame(d);
        var p = Newest;
        var bmp = PhotoBitmap(p);
        if (bmp == null)
        {
            PhotoWidgetShown = null;
            PhotoEmptyShown = p == null && _ph is { Ready: true };
            // one cell: an empty picture box with the picture icon; bigger, the words
            if (c.Width < 60 || c.Height < 26)
            {
                var box = RectangleF.Inflate(c, -1, -1);
                PicBox95(box, null, null);
                W95Icons.Draw(_p, W95Icons.Id.Palette, MathF.Floor(box.X + (box.Width - 16) / 2), MathF.Floor(box.Y + (box.Height - 16) / 2));
            }
            else if (PhotoEmptyShown) _ui.Text95(PhotoCut("(no photos)", c.Width - 4, Ui.F95, FontWeight.Normal, null, 1), c, Gry95, false, TextAlignment.Center);
            return;
        }
        PhotoWidgetShown = p.Path;
        PhotoEmptyShown = false;
        var words = Words(_phwNew, p, DateTime.Now);
        if (w.W == 1 && w.H == 1 || c.Height < 26 || c.Width < 40) { PicBox95(RectangleF.Inflate(c, -1, -1), p, bmp); return; }
        if (c.Height < 56)
        {
            var box = new RectangleF(c.X + 1, c.Y + 1, MathF.Min(MathF.Floor((c.Height - 2) * 4 / 3), MathF.Floor(c.Width * 0.45f)), c.Height - 2);
            PicBox95(box, p, bmp);
            float tx = box.Right + 5, tw = c.Right - 2 - tx, ty = MathF.Floor(c.Y + (c.Height - 28) / 2);
            string ago = _ui.Width95(words.Ago, true) <= tw ? words.Ago : p.Years > 0 ? words.AgoShort : Age(p.Taken, DateTime.Now);      // "3 min ago", narrow "3 m"
            _ui.Text95(PhotoCut(ago, tw, Ui.F95, FontWeight.Bold, null, 1), new RectangleF(tx, ty, tw, 13), Blk95, true);
            _ui.Text95(PhotoCut(p.World ?? words.When, tw, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(tx, ty + 14, tw, 13), Gry95);
            return;
        }
        var pic = new RectangleF(c.X + 1, c.Y + 1, c.Width - 2, c.Height - 2 - 15);
        PicBox95(pic, p, bmp);
        float y = c.Bottom - 14, aw = MathF.Min(_ui.Width95(words.Ago, true), c.Width - 4);
        _ui.Text95(PhotoCut(words.Ago, c.Width - 4, Ui.F95, FontWeight.Bold, null, 1), new RectangleF(c.X + 2, y, c.Width - 4, 13), Blk95, true);
        if (p.World != null && c.Width - aw - 14 > 24)
            _ui.Text95(PhotoCut(p.World, c.Width - aw - 14, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(c.X + aw + 10, y, c.Width - aw - 12, 13), Gry95);
    }

    // ---- 95's Imaging: a menu bar, a toolbar (the arrows, which one, Open in VR Gallery, Close), the photo on Imaging's grey, a status bar ----

    static readonly string[] MenuImaging = { "File", "Edit", "View", "Page", "Zoom", "Help" };
    readonly Dictionary<string, float> _menuKeys = new();
    int _menuKeysLang = -1, _menuKeysTheme = -1;

    // how wide a menu word's first letter is (its access key's underline), measured once per word, language and look
    float MenuKey(string m)
    {
        if (_menuKeysLang != Lang.Version || _menuKeysTheme != Theme.Version) { _menuKeys.Clear(); _menuKeysLang = Lang.Version; _menuKeysTheme = Theme.Version; }
        if (!_menuKeys.TryGetValue(m, out float w)) _menuKeys[m] = w = _ui.Width95(Lang.T(m)[..1]);
        return w;
    }

    void Photo95Detail(RectangleF win, PanelData d, WatchWidgets.Kind k)
    {
        using var set = UseOpenSet();
        PhotoFrame(d);
        if (Theme.Retro == RetroStyle.XP) { PhotoDetailXp(win, d); return; }        // Windows Picture and Fax Viewer (Skins/Xp)
        if (Theme.Retro == RetroStyle.Terminal) { TPhotoDetail(win, d); return; }   // in characters (Skins/Terminal)
        bool grid = _gal && _galGrid;                // the gallery's grid: Imaging's thumbnail view (WristGallery.cs)
        var p = grid ? null : PhotoShown();
        var words = Words(_phwOpen, p, DateTime.Now);
        PhotoOpenShown = p?.Path;
        if (!grid) PhotoEmptyShown = p == null;
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        string title = grid ? PhotoCut(GalTitle("Imaging - {0}"), win.Width - 6 - 44, Ui.F95, FontWeight.Bold, null, 1)
            : p == null ? k.Title95 : PhotoCut(PhotoCaption(words, "Imaging - {0}"), win.Width - 6 - 44, Ui.F95, FontWeight.Bold, null, 1);
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), title);
        if (_ui.CaptionClose95("wd95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        // the menu bar (the window's own words: not menus that open here)
        float mx = win.X + 6, my = win.Y + 23;
        foreach (var m in MenuImaging)
        {
            float mw = _ui.Width95(m);
            _ui.Text95(m, new RectangleF(mx, my, mw + 2, 15), Blk95);
            _p.Fill(new RectangleF(mx, my + 12, MenuKey(m), 1), Blk95);       // its access key, underlined
            mx += mw + 12;
        }
        // the toolbar: the arrows, Gallery and Delete, which one; Open in VR Gallery (its short name where the whole does not fit), Close
        float ty = win.Y + 40;
        _p.EtchedLine(win.X + 3, win.Right - 3, ty - 2);
        int n = PhotoCount;
        if (_ui.GlyphButton95("wph95p", new RectangleF(win.X + 6, ty + 1, 26, 22), PhPrev95, !grid && _phIndex < n - 1)) PhotoStep(1);
        if (_ui.GlyphButton95("wph95n", new RectangleF(win.X + 33, ty + 1, 26, 22), PhNext95, !grid && _phIndex > 0)) PhotoStep(-1);
        ActionButtons95(win.X + 63, ty + 1, 26, 22, p);
        float cx = win.X + 63 + 53 + 6;
        var close = new RectangleF(win.Right - 6 - 56, ty + 1, 56, 22);
        string cnt = grid ? (_ph is { Recent.Count: > 0 } a ? GalCountText(a.Recent.Count) : null) : p != null ? words.Count : null;
        float room = close.X - 4 - (cx + (cnt != null ? MathF.Min(_ui.Width95(cnt), 60) + 6 : 0));
        string gl = _ui.Width95(OpenGallery) + 16 <= room ? OpenGallery : Lang.Short(OpenGallery) ?? "VR Gallery";
        bool glyph = _ui.Width95(gl) + 16 > room;            // (not even the short name: the button's picture alone)
        float gw = glyph ? 26 : _ui.Width95(gl) + 16;
        var gal = new RectangleF(close.X - 4 - gw, ty + 1, gw, 22);
        if (cnt != null) _ui.Text95(PhotoCut(cnt, gal.X - 6 - cx, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(cx, ty + 5, gal.X - 6 - cx, 13), Blk95);
        if (glyph ? _ui.GlyphButton95("wphgal", gal, OpenWin95, p != null) : _ui.Button95("wphgal", gal, gl, p != null)) PhotoOpen(p);
        if (_ui.Button95("wd95ok", close, "Close")) { CloseDetail(); return; }
        // the picture, whole, on Imaging's grey (the grid: its thumbnails)
        var st = new RectangleF(win.X + 3, win.Bottom - 3 - 18, win.Width - 6, 18);
        var view = new RectangleF(win.X + 4, ty + 27, win.Width - 8, st.Y - 2 - (ty + 27));
        string ds = DeleteStatus(p);
        if (grid)
        {
            GalGrid(view, 2);
            _ui.Pane95(st, PhotoCut(Lang.T(ds ?? "Tap a photo to open it"), st.Width - 8, Ui.F95, FontWeight.Normal, null, 1));
            return;
        }
        _p.Fill(view, 0xFF808080);
        _p.Bevel(view, Painter.Edge.Sunken);
        var inner = RectangleF.Inflate(view, -6, -6);
        if (p == null) _ui.Text95(PhotoCut(NoPhotos, inner.Width, Ui.F95, FontWeight.Normal, null, 1), inner, Wht95, false, TextAlignment.Center);
        else
        {
            var (aw, ah) = PhotoAspect(p);
            var pic = PhotoFit(aw, ah, inner, true);
            var bmp = PhotoBitmap(p);
            _p.Fill(new RectangleF(pic.X + 2, pic.Y + 2, pic.Width, pic.Height), Blk95);      // a page's shadow
            if (bmp != null) _p.ImagePart(bmp, pic, new RectangleF(0, 0, p.TW, p.TH));
            else _p.Fill(pic, Wht95);
            PhotoFlash95(pic);
        }
        // the status bar: when, the world, the people (Delete asking or answering: its words)
        if (ds != null) { _ui.Pane95(st, PhotoCut(Lang.T(ds), st.Width - 8, Ui.F95, FontWeight.Normal, null, 1)); return; }
        if (p == null) { _ui.Pane95(st, ""); return; }
        float w1 = MathF.Ceiling(MathF.Min(_ui.Width95(words.When) + 10, st.Width * 0.34f)), w2 = MathF.Floor((st.Width - w1 - 4) * 0.5f);
        _ui.Pane95(new RectangleF(st.X, st.Y, w1, st.Height), PhotoCut(words.When, w1 - 8, Ui.F95, FontWeight.Normal, null, 1));
        _ui.Pane95(new RectangleF(st.X + w1 + 2, st.Y, w2, st.Height), PhotoCut(p.World ?? Lang.T("World not known"), w2 - 8, Ui.F95, FontWeight.Normal, null, 1));
        float x3 = st.X + w1 + 2 + w2 + 2;
        _ui.Pane95(new RectangleF(x3, st.Y, st.Right - x3, st.Height), PhotoPeople(p, st.Right - x3 - 8, Ui.F95, FontWeight.Normal, 1, false) ?? "");
    }

    static readonly string[] PhPrev95 = { "...k", "..kk", ".kkk", "kkkk", ".kkk", "..kk", "...k" };
    static readonly string[] PhNext95 = { "k...", "kk..", "kkk.", "kkkk", "kkk.", "kk..", "k..." };
}
