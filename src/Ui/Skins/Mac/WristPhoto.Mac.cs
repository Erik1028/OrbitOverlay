using System;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// The latest photo on the Mac (WristPhoto.cs has the rest): the widget a little Scrapbook window - the photo in its white well, how
// long ago and the world in Chicago under it (the shared 95 widget in the Platinum skin); opened, a Platinum document window
// with the photo's name in its striped title bar, the photo whole in a white well, where and when under it, and Platinum buttons:
// the arrows, Open in VR Gallery, OK.
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
        // the buttons along the bottom: the arrows, Gallery and Delete, which one, Open in VR Gallery, OK
        float by = win.Bottom - 8 - 23;
        int n = PhotoCount;
        if (_ui.GlyphButton95("wph95p", new RectangleF(c.X, by, 28, 23), PhPrev95, !grid && _phIndex < n - 1)) PhotoStep(1);
        if (_ui.GlyphButton95("wph95n", new RectangleF(c.X + 32, by, 28, 23), PhNext95, !grid && _phIndex > 0)) PhotoStep(-1);
        ActionButtons95(c.X + 64, by, 28, 23, p);
        float cx = c.X + 64 + 57 + 6;
        var ok = new RectangleF(win.Right - 8 - 60, by, 60, 23);
        string cnt = grid ? (_ph is { Recent.Count: > 0 } a ? GalCountText(a.Recent.Count) : null) : p != null ? words.Count : null;
        float room = ok.X - 8 - (cx + (cnt != null ? MathF.Min(_ui.Width95(cnt), 60) + 6 : 0));
        string gl = _ui.Width95(OpenGallery) + 20 <= room ? OpenGallery : Lang.Short(OpenGallery) ?? "VR Gallery";
        bool glyph = _ui.Width95(gl) + 20 > room;            // (not even the short name: the button's picture alone)
        float gw = glyph ? 28 : _ui.Width95(gl) + 20;
        var gal = new RectangleF(ok.X - 8 - gw, by, gw, 23);
        if (cnt != null) _ui.Text95(PhotoCut(cnt, gal.X - 6 - cx, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(cx, by + 5, gal.X - 6 - cx, 13), MacBlack);
        if (glyph ? _ui.GlyphButton95("wphgal", gal, OpenWin95, p != null) : _ui.Button95("wphgal", gal, gl, p != null)) PhotoOpen(p);
        if (_ui.Button95("wd95ok", ok, "OK", true, true)) { CloseDetail(); return; }
        // the photo whole in its white well; where and when over the buttons (Delete asking or answering: its words there)
        var well = new RectangleF(c.X, c.Y, c.Width, c.Height - 18);
        float iy = well.Bottom + 3;
        string ds = DeleteStatus(p);
        if (ds != null) _ui.Text95(PhotoCut(Lang.T(ds), c.Width, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(c.X, iy, c.Width, 13), MacBlack, _delNoteBad || DeleteArmed(p));
        if (grid) { GalGrid(well, 2); return; }
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
        if (bmp != null) _p.ImagePart(bmp, pic, new RectangleF(0, 0, p.TW, p.TH));
        else _p.Fill(pic, 0xFFDDDDDD);
        _p.Stroke(new RectangleF(pic.X - 0.5f, pic.Y - 0.5f, pic.Width + 1, pic.Height + 1), MacBlack, 1);
        PhotoFlash95(pic);
        if (ds != null) return;
        float ww = MathF.Min(_ui.Width95(words.When), 100);
        _ui.Text95(PhotoCut(words.When, 100, Ui.F95, FontWeight.Normal, null, 1), new RectangleF(c.Right - ww - 1, iy, ww + 2, 13), Gry95, false, TextAlignment.Trailing);
        float lw = c.Width - ww - 10;
        string where = p.World != null ? PhotoCut(p.World, lw, Ui.F95, FontWeight.Bold, null, 1) : null;
        float wl = where != null ? _ui.Width95(where, true) : 0;
        if (where != null) _ui.Text95(where, new RectangleF(c.X, iy, wl + 2, 13), MacBlack, true);
        float pr = lw - (where != null ? wl + 10 : 0);
        string people = pr > 30 ? PhotoPeople(p, pr, Ui.F95, FontWeight.Normal, 1) : null;
        if (people != null) _ui.Text95(people, new RectangleF(c.X + (where != null ? wl + 10 : 0), iy, pr, 13), MacBlack);
    }
}
