using System;
using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using T = Orbit.TerminalSkin;

namespace Orbit;

// The latest photo on the terminal (WristPhoto.cs has the rest): the photo as coloured characters - each 4 x 6 cell of the picture a
// character of a small 3 x 5 matrix (the tube's 132-column font) chosen by how bright the cell is ( .:-+=%*#@ and the solid block),
// in the cell's own colour at full strength on the black glass; made once per photo and size as a bitmap (the Terminal's colour map
// turns every brush green, it leaves bitmaps alone). A new photo types in, a row at a time behind a cursor. The widget: its box with
// PHOTO on the top line, the art filling it, how long ago and the world under it; opened, "> view <file>", the art whole, when and
// where, the people, and F-key buttons: PREV, NEXT, GALLERY - and OK.
public sealed partial class WristPanel
{
    const int ArtCW = 4, ArtCH = 6;

    // the characters, darkest first: how many of the 15 dots each lights, and the dots
    static readonly (int ink, string[] rows)[] ArtRamp =
    {
        (0, new[] { "...", "...", "...", "...", "..." }),
        (1, new[] { "...", "...", "...", "...", ".#." }),       // .
        (2, new[] { "...", ".#.", "...", ".#.", "..." }),       // :
        (3, new[] { "...", "...", "###", "...", "..." }),       // -
        (5, new[] { "...", ".#.", "###", ".#.", "..." }),       // +
        (6, new[] { "...", "###", "...", "###", "..." }),       // =
        (7, new[] { "#.#", "..#", ".#.", "#..", "#.#" }),       // %
        (9, new[] { "#.#", ".#.", "###", ".#.", "#.#" }),       // *
        (11, new[] { "#.#", "###", "#.#", "###", "#.#" }),      // #
        (12, new[] { "###", "#.#", "###", "#..", "###" }),      // @
        (15, new[] { "###", "###", "###", "###", "###" }),      // the block
    };

    readonly (uint[] src, int cols, int rows, bool cover, ID2D1Bitmap bmp)[] _tArt = new (uint[], int, int, bool, ID2D1Bitmap)[6];
    int _tArtNext;

    void TArtDispose()
    {
        for (int i = 0; i < _tArt.Length; i++) { _tArt[i].bmp?.Dispose(); _tArt[i] = default; }
    }

    // the photo as cols x rows characters (cover: cut to the cells' shape, else the whole picture squeezed into them)
    ID2D1Bitmap TArt(PhotoWatcher.Photo p, int cols, int rows, bool cover)
    {
        if (p?.Thumb == null || cols < 1 || rows < 1) return null;
        for (int i = 0; i < _tArt.Length; i++)
        {
            var a = _tArt[i];
            if (ReferenceEquals(a.src, p.Thumb) && a.cols == cols && a.rows == rows && a.cover == cover) return a.bmp;
        }
        int W = cols * ArtCW, H = rows * ArtCH, tw = p.TW, th = p.TH;
        var px = new uint[W * H];
        Array.Fill(px, T.BG | 0xFF000000);
        var src = cover ? PhotoCover(tw, th, new RectangleF(0, 0, W, H)) : new RectangleF(0, 0, tw, th);
        float sw = src.Width / cols, sh = src.Height / rows;
        var thumb = p.Thumb;
        // each cell's colour (3 x 3 samples across it) and how bright it is; the darkest and brightest of them stretched to the
        // characters' whole range (a dim photo still uses the blocks, a pale one the dots)
        var cells = new (float r, float g, float b, float lum)[cols * rows];
        float lo = 1, hi = 0;
        for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                float r = 0, g = 0, b = 0;
                for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++)
                    {
                        int x = Math.Clamp((int)(src.X + (cx + (i + 0.5f) / 3) * sw), 0, tw - 1), y = Math.Clamp((int)(src.Y + (cy + (j + 0.5f) / 3) * sh), 0, th - 1);
                        uint c = thumb[y * tw + x];
                        r += (c >> 16) & 255; g += (c >> 8) & 255; b += c & 255;
                    }
                r /= 9; g /= 9; b /= 9;
                float lum = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
                cells[cy * cols + cx] = (r, g, b, lum);
                lo = MathF.Min(lo, lum);
                hi = MathF.Max(hi, lum);
            }
        float span = MathF.Max(0.08f, hi - lo);
        for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                var (r, g, b, lum) = cells[cy * cols + cx];
                int want = (int)MathF.Round(Math.Clamp((lum - lo) / span, 0, 1) * 15), k = 0;
                for (int q = 1; q < ArtRamp.Length; q++)
                    if (Math.Abs(ArtRamp[q].ink - want) < Math.Abs(ArtRamp[k].ink - want)) k = q;
                if (k == 0) continue;
                // its hue at full strength (a grey one: the phosphor's white)
                float m = MathF.Max(r, MathF.Max(g, b)), s = m > 0 ? 255 / m : 0;
                uint ink = 0xFF000000 | ((uint)MathF.Min(255, r * s) << 16) | ((uint)MathF.Min(255, g * s) << 8) | (uint)MathF.Min(255, b * s);
                var glyph = ArtRamp[k].rows;
                for (int gy = 0; gy < 5; gy++)
                    for (int gx = 0; gx < 3; gx++)
                        if (glyph[gy][gx] == '#') px[(cy * ArtCH + gy) * W + cx * ArtCW + gx] = ink;
            }
        int slot = _tArtNext;
        _tArtNext = (slot + 1) % _tArt.Length;
        _tArt[slot].bmp?.Dispose();
        _tArt[slot] = (p.Thumb, cols, rows, cover, _p.CreateBitmap(px, W, H));
        return _tArt[slot].bmp;
    }

    // the art at x, y; a new photo: typed in, a row at a time, the cursor where the next one goes
    void TArtAt(ID2D1Bitmap art, float x, float y, int cols, int rows)
    {
        var dst = new RectangleF(x, y, cols * ArtCW, rows * ArtCH);
        float k = PhotoPopK();
        if (k <= 0) { _p.ImageNearest(art, dst); return; }
        int shown = (int)MathF.Floor((1 - k) * rows);
        _p.PushClip(new RectangleF(dst.X, dst.Y, dst.Width, shown * ArtCH));
        _p.ImageNearest(art, dst);
        _p.PopClip();
        if (shown < rows) _p.Fill(new RectangleF(dst.X + (cols * ArtCW - 4) * (1 - k), dst.Y + shown * ArtCH, 3, 5), T.HOT);
    }

    void TPhotoWidget(RectangleF r, RectangleF c, WatchWidget w, PanelData d, int id)
    {
        using var set = UseSet(w.Type);
        PhotoFrame(d);
        var p = Newest;
        if (p?.Thumb == null)
        {
            PhotoWidgetShown = null;
            PhotoEmptyShown = p == null && _ph is { Ready: true };
            if (PhotoEmptyShown) TCapsC(PhotoCut(Upper(c.Width < 60 ? "none" : "No photos"), c.Width - 4, Ui.F95, FontWeight.Normal, null, 2), c, MathF.Floor(c.Y + (c.Height - 7) / 2), T.DIM);
            return;
        }
        PhotoWidgetShown = p.Path;
        PhotoEmptyShown = false;
        bool words = !(w.W == 1 && w.H == 1) && c.Height >= 30;
        var area = new RectangleF(c.X + 1, c.Y + 1, c.Width - 2, c.Height - 2 - (words ? 10 : 0));
        int cols = (int)(area.Width / ArtCW), rows = (int)(area.Height / ArtCH);
        var art = TArt(p, cols, rows, true);
        if (art != null) TArtAt(art, MathF.Floor(area.X + (area.Width - cols * ArtCW) / 2), MathF.Floor(area.Y + (area.Height - rows * ArtCH) / 2), cols, rows);
        if (!words) return;
        var wd = Words(_phwNew, p, DateTime.Now);
        string ago = PhotoCut(Upper(wd.Ago), c.Width - 4, Ui.F95, FontWeight.Normal, null, 2);
        float aw = T.W(ago), y = c.Bottom - 8;
        TCaps(ago, c.X + 1, y, T.INK);
        if (p.World != null && c.Width - aw - 12 > 24)
        {
            string wn = PhotoCut(Upper(p.World), c.Width - aw - 12, Ui.F95, FontWeight.Normal, null, 2);
            TCapsR(wn, c.Right - 1, y, T.MID);
        }
    }

    // ---- opened: the viewer ----

    void TPhotoDetail(RectangleF win, PanelData d)
    {
        bool grid = _gal && _galGrid;                // the gallery: "> ls", the files by day (WristGallery.cs)
        var p = grid ? null : PhotoShown();
        var words = Words(_phwOpen, p, DateTime.Now);
        PhotoOpenShown = p?.Path;
        if (!grid) PhotoEmptyShown = p == null;
        _p.Fill(win, T.BG);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), "PHOTO VIEWER");       // (in capitals already: the tube's title is not made again in every frame)
        if (_ui.CaptionClose95("wd95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        if (_ui.Button95("wd95ok", new RectangleF(win.Right - 8 - 75, win.Bottom - 8 - 23, 75, 23), "OK", true, true)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 8, win.Y + 27, win.Width - 16, win.Height - 27 - 36);
        // the keys beside OK: the arrows, ALL (the gallery), DEL (asking: SURE?), GALLERY (VR Gallery)
        int n = PhotoCount;
        float ky = c.Bottom + 5, kw = MathF.Floor((c.Width - 75 - 2 * 26 - 4 * 4) / 3);
        RectangleF Key(int i) => new(c.X + 2 * 30 + i * (kw + 4), ky, kw, 23);
        if (TGlyphKey("wph95p", new RectangleF(c.X, ky, 26, 23), PhPrev95, !grid && _phIndex < n - 1)) PhotoStep(1);
        if (TGlyphKey("wph95n", new RectangleF(c.X + 30, ky, 26, 23), PhNext95, !grid && _phIndex > 0)) PhotoStep(-1);
        if (TKey("wphall", Key(0), "[F3]", grid, true, "ALL")) GalleryToggle(p);
        bool asking = DeleteArmed(p);
        if (TKey("wphdel", Key(1), "[F4]", asking, p != null && !DeleteBusy, asking ? "SURE?" : "DEL")) DeleteTap(p);
        if (TKey("wphgal", Key(2), "[F5]", false, p != null, "GALLERY")) PhotoOpen(p);
        string ds = DeleteStatus(p);
        if (grid)
        {
            TGallery(new RectangleF(c.X, c.Y, c.Width, c.Height - (ds != null ? 11 : 0)));
            if (ds != null) TCaps(PhotoCut(Upper(ds), c.Width, Ui.F95, FontWeight.Normal, null, 2), c.X, c.Bottom - 9, T.HOT);
            return;
        }
        if (p == null)
        {
            TCaps("> view", c.X, c.Y + 2, T.MID);
            TCaps(PhotoCut(Upper(NoPhotos), c.Width, Ui.F95, FontWeight.Normal, null, 2), c.X, c.Y + 16, T.DIM);
            TCaps("> _", c.X, c.Y + 30, T.INK);
            return;
        }
        // the command, which one at the right
        string count = words.Count;
        float cw = T.W(count);
        TCapsR(count, c.Right, c.Y + 2, T.DIM);
        TCaps(PhotoCut(PhotoCaption(words, "> view {0}"), c.Width - cw - 10, Ui.F95, FontWeight.Normal, null, 2), c.X, c.Y + 2, T.MID);
        // the art, whole: as many cells as fit with the picture's shape (a cell is 4 x 6)
        var area = new RectangleF(c.X, c.Y + 13, c.Width, c.Height - 13 - 24);
        var (aw, ah) = PhotoAspect(p);
        int rows = (int)(area.Height / ArtCH), cols = (int)MathF.Round(rows * ArtCH * aw / ah / ArtCW);
        if (cols * ArtCW > area.Width) { cols = (int)(area.Width / ArtCW); rows = Math.Max(1, (int)MathF.Round(cols * ArtCW * ah / aw / ArtCH)); }
        var art = TArt(p, cols, rows, false);
        float ax = MathF.Floor(area.X + (area.Width - cols * ArtCW) / 2), ay = MathF.Floor(area.Y + (area.Height - rows * ArtCH) / 2);
        if (art != null) TArtAt(art, ax, ay, cols, rows);
        else TCapsC("LOADING...", area, MathF.Floor(area.Y + (area.Height - 7) / 2), T.DIM);
        // when and where; the people
        float y1 = c.Bottom - 20, y2 = c.Bottom - 9;
        string when = PhotoCut(Upper(words.When), c.Width / 2, Ui.F95, FontWeight.Normal, null, 2);
        float ww = T.W(when);
        TCaps(when, c.X, y1, T.INK);
        if (p.World != null) TCaps(PhotoCut(Upper(p.World), c.Width - ww - 12, Ui.F95, FontWeight.Normal, null, 2), c.X + ww + 12, y1, T.HOT);
        if (ds != null) { TCaps(PhotoCut(Upper(ds), c.Width, Ui.F95, FontWeight.Normal, null, 2), c.X, y2, T.HOT); return; }
        string people = PhotoPeople(p, c.Width, Ui.F95, FontWeight.Normal, 2);
        TCaps(people ?? PhotoCut(Upper("Nobody known"), c.Width, Ui.F95, FontWeight.Normal, null, 2), c.X, y2, people != null ? T.MID : T.DIM);
    }
}
