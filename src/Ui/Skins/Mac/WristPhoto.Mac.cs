using System;
using System.Drawing;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// The latest photo on the Mac (WristPhoto.cs has the rest): the widget a little Scrapbook window - the photo in its white well, how
// long ago and the world in Chicago under it (the shared 95 widget in the Platinum skin); opened, a Platinum document window
// with the photo's name in its striped title bar, the photo whole in a white well, where and when (and which one) under it, and
// Platinum buttons: the arrows, the gallery and Delete, the zoom (a magnifier), the slideshow (a projection screen), Send (an
// envelope), Open in VR Gallery, OK.
public sealed partial class WristPanel
{
    void PhotoWidgetMac(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        using var set = UseSet(w.Type);
        var c = Mac.WidgetFrame(_p, r, w.H >= 2 ? _set.TitleMac : null);
        Photo95(r, c, w, d, id);
    }

    void PhotoMacDetail(RectangleF win, PanelData d)
    {
        PhotoMacBody(win, d);
        GalPeople95(win);                           // a photo's people to pick from (WristGallery.cs)
    }

    void PhotoMacBody(RectangleF win, PanelData d)
    {
        using var set = UseOpenSet();
        PhotoFrame(d);
        var m = Mac;
        bool grid = _gal && _galGrid;                // the gallery's grid: the folder's window of picture icons (WristGallery.cs)
        var p = grid ? null : PhotoShown();
        var words = Words(_phwOpen, p, DateTime.Now);
        PhotoOpenShown = p?.Path;
        if (!grid) PhotoEmptyShown = p == null;
        // (the name cut here to what the title bar shows: kept, not cut again in every frame)
        m.MacWindow(_p, win, grid ? PhotoCut(GalTitle(null), win.Width - 6 - 70 - 2, Ui.F95, FontWeight.Bold, null, 1)
            : p == null ? "PictureViewer" : PhotoCut(words.Title, win.Width - 6 - 70 - 2, Ui.F95, FontWeight.Bold, null, 1));
        _mCloseBtn = new RectangleF(MacSkin.CloseX(win), MacSkin.BoxY(win), 16, 14);
        if (_ui.CaptionClose95("wd95x", MacSkin.CloseX(win), MacSkin.BoxY(win), true, 6)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 8, win.Y + 28, win.Width - 16, win.Height - 28 - 36);
        // the buttons along the bottom: the arrows, Gallery and Delete, the zoom (the magnifier, in while zoomed: PictureViewer's Normal /
        // Double Size) and the slideshow (a projection screen, in while it plays - Erik 10-24, "mehet mind"), Send (an envelope: VR
        // Gallery's newest only), Open in VR Gallery, OK; which one in the line over them (the grid: Days, Gallery, Favourites, how many)
        float by = win.Bottom - 8 - 23;
        int n = PhotoCount;
        const float bw = 26;
        if (grid)
        {
            if (_ui.Button95("wgdays", new RectangleF(c.X, by, 55, 23), "Days", true, false, C.GalleryDays)) GalSetDays(!C.GalleryDays);
        }
        else
        {
            if (_ui.GlyphButton95("wph95p", new RectangleF(c.X, by, bw, 23), PhPrev95, _phIndex < n - 1)) PhotoStep(1);
            if (_ui.GlyphButton95("wph95n", new RectangleF(c.X + 29, by, bw, 23), PhNext95, _phIndex > 0)) PhotoStep(-1);
        }
        ActionButtons95(c.X + 59, by, bw, 23, p);
        float cx = c.X + 59 + 2 * bw + 1 + 6;
        if (!grid)
        {
            if (ToggleGlyph95("wphzoom", new RectangleF(c.X + 116, by, bw, 23), Lens95, p != null, _galZoom))
            {
                if (_galZoom) ZoomOut(); else ZoomIn(p, new Vector2(0.5f, 0.5f));
            }
            bool show = _galShow;
            if (ToggleGlyph95(show ? "wphstop" : "wphplay", new RectangleF(c.X + 116 + bw + 1, by, bw, 23), Screen95, show || _phIndex < n - 1, show))
            {
                if (show) ShowStop(); else ShowStart();
            }
            cx = c.X + 116 + 2 * bw + 1 + 6;
            if (CanSend(p))
            {
                SendButton95(new RectangleF(c.X + 116 + 2 * bw + 5, by, bw, 23), p);
                cx += bw + 4;
            }
        }
        var ok = new RectangleF(win.Right - 8 - 60, by, 60, 23);
        string cnt = grid && _ph is { Recent.Count: > 0 } a ? GalCountText(a.Recent.Count) : null;
        float room = ok.X - 6 - (cx + (cnt != null ? MathF.Min(_ui.Width95(cnt), 60) + 6 : 0));
        string gl = _ui.Width95(OpenGallery) + 20 <= room ? OpenGallery : Lang.Short(OpenGallery) ?? "VR Gallery";
        bool glyph = _ui.Width95(gl) + 20 > room;            // (not even the short name: the button's picture alone)
        float gw = glyph ? bw : _ui.Width95(gl) + 20;
        var gal = new RectangleF(ok.X - 6 - gw, by, gw, 23);
        if (cnt != null) _ui.Text95(PhotoCut(cnt, gal.X - 6 - cx, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(cx, by + 5, gal.X - 6 - cx, 13), MacBlack);
        if (glyph ? _ui.GlyphButton95("wphgal", gal, OpenWin95, p != null) : _ui.Button95("wphgal", gal, gl, p != null)) PhotoOpen(p);
        if (_ui.Button95("wd95ok", ok, "OK", true, true)) { CloseDetail(); return; }
        // the photo whole in its white well; where and when over the buttons (Delete asking or answering: its words there)
        var well = new RectangleF(c.X, c.Y, c.Width, c.Height - 18);
        float iy = well.Bottom + 3;
        string ds = DeleteStatus(p);
        if (ds != null) _ui.Text95(PhotoCut(Lang.T(ds), c.Width, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(c.X, iy, c.Width, 13), MacBlack, _delNoteBad || DeleteArmed(p));
        if (grid)
        {
            GalGrid(well, 2);
            if (ds == null && GalFilterText() is { } ft)
            {
                string fc = PhotoCut(ft, c.Width, Ui.F95, FontWeight.Normal, null, 1);
                var fr = new RectangleF(c.X, iy, MathF.Min(c.Width, _ui.Width95(fc) + 2), 14);
                _ui.Text95(fc, fr, MacBlack);
                if (GalTapLine("wgclear", fr, MacBlack)) GalFilter(_galFav, null, null, null);
            }
            return;
        }
        m.Well(_p, well);
        var inner = RectangleF.Inflate(well, -5, -5);
        if (p == null)
        {
            _ui.Text95(PhotoCut(NoPhotos, inner.Width, Ui.F95, FontWeight.Normal, null, 1), inner, Gry95, false, TextAlignment.Center);
            return;
        }
        var (aw, ah) = PhotoAspect(p);
        var pic = PhotoFit(aw, ah, inner, true);
        var bmp = PhotoBitmap(p);
        PhotoImage95(pic, p, bmp, 0xFFDDDDDD, false);       // (whole or zoomed: WristGallery.cs)
        _p.Stroke(new RectangleF(pic.X - 0.5f, pic.Y - 0.5f, pic.Width + 1, pic.Height + 1), MacBlack, 1);
        PhotoFlash95(pic);
        if (ds != null) return;
        // when at the right, which one before it; the world and the people in what is left
        float ww = MathF.Min(_ui.Width95(words.When), 100);
        _ui.Text95(PhotoCut(words.When, 100, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(c.Right - ww - 1, iy, ww + 2, 13), Gry95, false, TextAlignment.Trailing);
        float cw = MathF.Min(_ui.Width95(words.Count), 60), cr = c.Right - ww - 14;
        _ui.Text95(PhotoCut(words.Count, 60, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(cr - cw - 1, iy, cw + 2, 13), Gry95, false, TextAlignment.Trailing);
        float lw = c.Width - ww - 14 - cw - 10, wm = p.Players.Length > 0 && lw - 76 >= 40 ? lw - 76 : lw;     // (a long world leaves the people a little)
        string where = p.World != null ? PhotoCut(p.World, wm, Ui.F95, FontWeight.Bold, null, 1) : null;
        float wl = where != null ? _ui.Width95(where, true) : 0;
        if (where != null) _ui.Text95(where, new RectangleF(c.X, iy, wl + 2, 13), MacBlack, true);
        GalTapWorld("wphworld", new RectangleF(c.X, iy, wl + 2, 14), p, MacBlack);
        float pr = lw - (where != null ? wl + 10 : 0);
        string people = pr > 30 ? PhotoPeople(p, pr, Ui.F95, FontWeight.Normal, 1) : null;
        if (people != null)
        {
            _ui.Text95(people, new RectangleF(c.X + (where != null ? wl + 10 : 0), iy, pr, 13), MacBlack);
            GalTapPeople("wphpeople", new RectangleF(c.X + (where != null ? wl + 10 : 0), iy, MathF.Min(pr, _ui.Width95(people) + 2), 14), p, MacBlack);
        }
    }
}
