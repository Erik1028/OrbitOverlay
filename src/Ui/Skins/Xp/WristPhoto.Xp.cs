using System;
using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Orbit;

// The latest photo in Windows XP (WristPhoto.cs has the rest). The widget: My Pictures in its Thumbnails view - a little window on
// white, the photo in its thin grey thumbnail frame, how long ago and the world under it (or beside it on one row). Opened: the
// Windows Picture and Fax Viewer - the photo whole on white, a line saying when and where under it, and the viewer's toolbar along
// the bottom: the green Previous / Next Image buttons, Open in VR Gallery (the viewer's "Open for editing"), Close.
public sealed partial class WristPanel
{
    const uint XpThumbEdge = 0xFFB9B9AE, XpViewerBar = 0xFFC5D2E6;

    // a thumbnail: white, its 1 px frame, the photo filling it a pixel in
    void ThumbXp(RectangleF box, PhotoWatcher.Photo p, ID2D1Bitmap bmp)
    {
        _p.Fill(box, 0xFFFFFFFF);
        var inner = RectangleF.Inflate(box, -2, -2);
        if (bmp != null) _p.ImagePart(bmp, inner, PhotoCover(p.TW, p.TH, inner));
        PhotoFlash95(inner);
        _p.Stroke(new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), PhotoPopK() > 0 ? 0xFF316AC5 : XpThumbEdge, 1);
    }

    void PhotoWidgetXp(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        using var set = UseSet(w.Type);
        PhotoFrame(d);
        var c = Theme.RetroSkin.WidgetFrame(_p, r, w.H >= 2 ? _set.TitleXp : null);
        _p.Fill(c, 0xFFFFFFFF);
        var p = Newest;
        var bmp = PhotoBitmap(p);
        if (bmp == null)
        {
            PhotoWidgetShown = null;
            PhotoEmptyShown = p == null && _ph is { Ready: true };
            // one cell: an empty thumbnail with the picture icon; bigger, the words
            if (c.Width < 60 || c.Height < 26)
            {
                var box = RectangleF.Inflate(c, -2, -2);
                ThumbXp(box, null, null);
                W95Icons.Draw(_p, W95Icons.Id.Palette, MathF.Floor(box.X + (box.Width - 16) / 2), MathF.Floor(box.Y + (box.Height - 16) / 2));
            }
            else if (PhotoEmptyShown)
                _ui.Text95(PhotoCut("(no pictures)", c.Width - 4, Ui.F95, FontWeight.Normal, null, 1), c, XpGrey, false, TextAlignment.Center);
            return;
        }
        PhotoWidgetShown = p.Path;
        PhotoEmptyShown = false;
        var words = Words(_phwNew, p, DateTime.Now);
        if (w.W == 1 && w.H == 1 || c.Width < 40 || c.Height < 26) { ThumbXp(RectangleF.Inflate(c, -2, -2), p, bmp); return; }
        if (c.Height < 56)
        {
            // one row: the thumbnail, its words beside it
            var box = new RectangleF(c.X + 2, c.Y + 2, MathF.Min(MathF.Floor((c.Height - 4) * 4 / 3), MathF.Floor(c.Width * 0.45f)), c.Height - 4);
            ThumbXp(box, p, bmp);
            float tx = box.Right + 5, tw = c.Right - 2 - tx, ty = MathF.Floor(c.Y + (c.Height - 28) / 2);
            string ago = _ui.Width95(words.Ago) <= tw ? words.Ago : p.Years > 0 ? words.AgoShort : Age(p.Taken, DateTime.Now);      // "3 min ago", narrow "3 m"
            _ui.Text95(PhotoCut(ago, tw, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(tx, ty, tw, 13), 0xFF000000);
            _ui.Text95(PhotoCut(p.World ?? words.When, tw, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(tx, ty + 14, tw, 13), XpGrey);
            return;
        }
        // the thumbnail with its name under it (Thumbnails view): how long ago, the world
        var tb = new RectangleF(c.X + 3, c.Y + 3, c.Width - 6, c.Height - 6 - 28);
        ThumbXp(tb, p, bmp);
        _ui.Text95(PhotoCut(words.Ago, c.Width - 4, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(c.X + 2, c.Bottom - 29, c.Width - 4, 13), 0xFF000000,
            false, TextAlignment.Center);
        _ui.Text95(PhotoCut(p.World ?? words.When, c.Width - 4, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(c.X + 2, c.Bottom - 15, c.Width - 4, 13),
            XpGrey, false, TextAlignment.Center);
    }

    // ---- Windows Picture and Fax Viewer ----

    void PhotoDetailXp(RectangleF win, PanelData d)
    {
        bool grid = _gal && _galGrid;                // the gallery's grid: My Pictures' Thumbnails view (WristGallery.cs)
        var p = grid ? null : PhotoShown();
        var words = Words(_phwOpen, p, DateTime.Now);
        PhotoOpenShown = p?.Path;
        if (!grid) PhotoEmptyShown = p == null;
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        string title = grid ? PhotoCut(GalTitle(null), win.Width - 6 - 44, Ui.F95, FontWeight.Bold, null, 1)
            : p == null ? "Windows Picture and Fax Viewer"
            : PhotoCut(PhotoCaption(words, "{0} - Windows Picture and Fax Viewer"), win.Width - 6 - 44, Ui.F95, FontWeight.Bold, null, 1);
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), title);
        if (_ui.CaptionClose95("wd95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        var body = new RectangleF(win.X + 4, win.Y + 23, win.Width - 8, win.Height - 27);
        _p.Fill(body, 0xFFFFFFFF);
        // the toolbar along the bottom: a pale band, a line over it
        var bar = new RectangleF(body.X, body.Bottom - 28, body.Width, 28);
        _p.GradientRaw(bar, 0xFFFFFFFF, 0xFFE6EDF8);
        _p.Fill(new RectangleF(bar.X, bar.Y, bar.Width, 1), XpViewerBar);
        int n = PhotoCount;
        float bx = bar.X + 6, by = bar.Y + 3;
        if (grid)
        {
            // the view: Days (held in while on - the viewer's own toggles' way)
            var dr = new RectangleF(bx, by, 50, 22);
            if (C.GalleryDays) Theme.RetroSkin.ButtonFace(_p, dr, false, true, false, false);
            if (XpTool("wgdays", dr, "Days", false, true)) GalSetDays(!C.GalleryDays);
        }
        else
        {
            if (XpRound("wph95p", new RectangleF(bx, by, 24, 22), false, _phIndex < n - 1)) PhotoStep(1);
            if (XpRound("wph95n", new RectangleF(bx + 26, by, 24, 22), true, _phIndex > 0)) PhotoStep(-1);
        }
        _p.Fill(new RectangleF(bx + 56, by + 2, 1, 18), XpViewerBar);
        // the viewer's own Delete (its red X), and the thumbnails' four squares for the gallery
        if (XpGlyphTool("wphall", new RectangleF(bx + 61, by, 24, 22), Grid95, 0xFF316AC5, true, grid)) GalleryToggle(p);
        if (grid)
        {
            if (XpGlyphTool("wgfav", new RectangleF(bx + 87, by, 24, 22), Heart95, 0xFFD8432E, true, _galFav)) GalFilter(!_galFav, _galWorld, _galPerson, null);
        }
        else if (XpGlyphTool("wphdel", new RectangleF(bx + 87, by, 24, 22), Del95, 0xFFD8432E, p != null && !DeleteBusy, DeleteArmed(p))) DeleteTap(p);
        _p.Fill(new RectangleF(bx + 117, by + 2, 1, 18), XpViewerBar);
        float cx = bx + 123;
        var close = new RectangleF(bar.Right - 6 - 56, by, 56, 22);
        // the gallery's button leaves the counter its room: its short form ("VR Gallery", as the 95 and Mac viewers say) where the
        // whole would take it (Magyar's "Megnyitás a VR Galleryben" left a cut "5")
        string cnt = grid ? (_ph is { Recent.Count: > 0 } a ? GalCountText(a.Recent.Count) : null) : p != null ? words.Count : null;
        float room = close.X - 6 - cx, need = cnt != null ? MathF.Min(_ui.Width95(cnt), 70) + 12 : 0;
        string gl = OpenGallery;
        if (_ui.Width95(gl) + 30 > room - need) gl = Lang.Short(OpenGallery) ?? "VR Gallery";
        if (_ui.Width95(gl) + 30 > room - need) gl = "";            // (not even the short name: its picture alone)
        float gw = gl.Length == 0 ? 26 : _ui.Width95(gl) + 30;
        var gal = new RectangleF(close.X - 6 - gw, by, gw, 22);
        if (cnt != null) _ui.Text95(PhotoCut(cnt, gal.X - 6 - cx, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(cx, by + 4, gal.X - 6 - cx, 13), XpGrey);
        if (XpTool("wphgal", gal, gl, true, p != null)) PhotoOpen(p);
        if (_ui.Button95("wd95ok", close, "Close")) { CloseDetail(); return; }
        var view = new RectangleF(body.X + 8, body.Y + 8, body.Width - 16, bar.Y - 18 - (body.Y + 8));
        float iy = bar.Y - 15, iw = body.Width - 16;
        string ds = DeleteStatus(p);
        if (grid)
        {
            GalGrid(new RectangleF(body.X + 4, body.Y + 4, body.Width - 8, iy - 3 - (body.Y + 4)), 2);
            if (ds != null) _ui.Text95(PhotoCut(Lang.T(ds), iw, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(body.X + 8, iy, iw, 13), 0xFFC00000);
            else if (GalFilterText() is { } ft)
            {
                // the filter, a link (XP's blue): all again
                string fc = PhotoCut(ft, iw, Ui.F95, FontWeight.Normal, null, 1);
                var fr = new RectangleF(body.X + 8, iy, MathF.Min(iw, _ui.Width95(fc) + 2), 14);
                _ui.Text95(fc, fr, 0xFF0066CC);
                if (GalTapLine("wgclear", fr, 0xFF0066CC)) GalFilter(_galFav, null, null, null);
            }
            return;
        }
        if (p == null)
        {
            _ui.Text95(PhotoCut(NoPhotos, view.Width, Ui.F95, FontWeight.Normal, null, 1), view, XpGrey, false, TextAlignment.Center);
            return;
        }
        // the photo whole, a soft shadow at its right and foot; when and where under it
        var (aw, ah) = PhotoAspect(p);
        var pic = PhotoFit(aw, ah, view, true);
        _p.Fill(new RectangleF(pic.X + 2, pic.Y + 2, pic.Width, pic.Height), 0xFFD8D8D8);
        var bmp = PhotoBitmap(p);
        if (bmp != null) _p.ImagePart(bmp, pic, new RectangleF(0, 0, p.TW, p.TH));
        else _p.Fill(pic, 0xFFF1EFE2);
        PhotoFlash95(pic);
        // Delete asking or answering: its words in the line under the photo
        if (ds != null)
        {
            _ui.Text95(PhotoCut(Lang.T(ds), iw, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(body.X + 8, iy, iw, 13), _delNoteBad || DeleteArmed(p) ? 0xFFC00000 : 0xFF000000);
            return;
        }
        // when at the right; the world and the people in what is left
        float ww = MathF.Min(_ui.Width95(words.When), 100);
        _ui.Text95(PhotoCut(words.When, 100, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(body.Right - 8 - ww - 1, iy, ww + 2, 13), XpGrey, false,
            TextAlignment.Trailing);
        float lw = iw - ww - 12;
        string where = p.World != null ? PhotoCut(p.World, lw, Ui.F95, FontWeight.Normal, null, 1) : null;
        float wl = where != null ? _ui.Width95(where) : 0, px = body.X + 8 + (where != null ? wl + 12 : 0), pr = lw - (where != null ? wl + 12 : 0);
        if (where != null) _ui.Text95(where, new RectangleF(body.X + 8, iy, wl + 2, 13), 0xFF000000);
        GalTapWorld("wphworld", new RectangleF(body.X + 8, iy, wl + 2, 14), p, 0xFF000000);
        string people = pr > 30 ? PhotoPeople(p, pr, Ui.F95, FontWeight.Normal, 1) : null;
        if (people != null)
        {
            _ui.Text95(people, new RectangleF(px, iy, pr, 13), XpGrey);
            GalTapPeople("wphpeople", new RectangleF(px, iy, MathF.Min(pr, _ui.Width95(people) + 2), 14), p, XpGrey);
        }
    }

    // a toolbar button with only its picture (Delete's red X, the thumbnails' four squares): flat, XP's hot button under the pointer;
    // down: held in (the grid shown, Delete asking)
    bool XpGlyphTool(string id, RectangleF r, string[] glyph, uint ink, bool enabled, bool down)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held) && enabled;
        if ((hover || held || down) && enabled) Theme.RetroSkin.ButtonFace(_p, r, false, held || down, false, true);
        float o = (held || down) && enabled ? 1 : 0;
        _p.Pixels(glyph, MathF.Floor(r.X + (r.Width - 7) / 2) + o, MathF.Floor(r.Y + (r.Height - 7) / 2) + o, enabled ? ink : 0xFFACA899);
        return click;
    }

    // a toolbar button of the viewer: flat; under the pointer XP's hot button, pressed its sunken one
    bool XpTool(string id, RectangleF r, string label, bool icon, bool enabled)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held) && enabled;
        if ((hover || held) && enabled) Theme.RetroSkin.ButtonFace(_p, r, false, held, false, true);
        float o = held && enabled ? 1 : 0, x = r.X + 5 + o;
        if (icon)
        {
            // the viewer's "open" picture: a little window with a blue title
            var ic = new RectangleF(x, MathF.Floor(r.Y + (r.Height - 12) / 2) + o, 14, 12);
            _p.Fill(ic, enabled ? 0xFF316AC5 : 0xFFACA899);
            _p.Fill(new RectangleF(ic.X + 1, ic.Y + 3, ic.Width - 2, ic.Height - 4), 0xFFFFFFFF);
            _p.Fill(new RectangleF(ic.X + 3, ic.Y + 5, 6, 4), enabled ? 0xFF5DBB4D : 0xFFD0D0BF);
            x += 19;
        }
        _ui.Text95(PhotoCut(label, r.Right - 3 - x, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(x, r.Y + 4 + o, r.Right - 3 - x, 13),
            enabled ? 0xFF000000 : 0xFFACA899);
        return click;
    }

    // Previous / Next Image: a green ball with a white arrow (grey when there is none that way)
    bool XpRound(string id, RectangleF r, bool next, bool enabled)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held) && enabled;
        if ((hover || held) && enabled) Theme.RetroSkin.ButtonFace(_p, r, false, held, false, true);
        float cx = MathF.Floor(r.X + r.Width / 2) + (held ? 1 : 0), cy = MathF.Floor(r.Y + r.Height / 2) + (held ? 1 : 0);
        _p.Smooth(true);
        _p.Circle(cx, cy, 8, enabled ? 0xFF2E8B22 : 0xFFB5B2A6);
        _p.Circle(cx, cy - 1, 6.6f, enabled ? 0xFF5DBB4D : 0xFFD6D3C6);
        _p.Circle(cx - 1.5f, cy - 3.5f, 2.8f, Theme.WithAlpha(0xFFFFFFFF, 0.5f));
        _p.Smooth(false);
        _p.Pixels(next ? PhNext95 : PhPrev95, cx - 2, cy - 3, 0xFFFFFFFF);
        return click;
    }
}
