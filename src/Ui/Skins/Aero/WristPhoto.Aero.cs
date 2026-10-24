using System;
using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Orbit;

// The latest photo in Windows 7 (WristPhoto.cs has the rest). The widget: the Slide Show gadget - the photo in its thin black frame
// with a soft shadow, and (two cells and up) the gadget's dark glass strip along its foot with how long ago and the world. Opened:
// Windows Photo Viewer - the glass window, the dark blue command bar (File, Print, Send - its E-mail -, Burn, and its Open menu: Open
// in VR Gallery), the photo whole on the viewer's light ground, when and where under it, and the dark blue control bar: the magnifier
// (the zoom) and the gallery at its left, the round Previous / Next buttons round the big blue Play slide show, which one it is, Delete.
public sealed partial class WristPanel
{
    const uint ViewerBar1 = 0xFF2C4F7E, ViewerBar2 = 0xFF0F2848, ViewerGround = 0xFFF4F7FC;
    const string SendCmd = "Send";
    static readonly string[] ViewerMenu = { "File", "Print", SendCmd, "Burn" };
    static readonly string[] DropAero = { "kkkkk", ".kkk.", "..k.." };
    static readonly string[] PlayBigAero = { "kk.....", "kkkk...", "kkkkkk.", "kkkkkkk", "kkkkkkk", "kkkkkk.", "kkkk...", "kk....." };
    static readonly string[] PauseBigAero = { "kk..kk", "kk..kk", "kk..kk", "kk..kk", "kk..kk", "kk..kk", "kk..kk", "kk..kk" };

    void PhotoTileAero(RectangleF r, WatchWidget w, PanelData d, int id)
    {
        using var set = UseSet(w.Type);
        PhotoFrame(d);
        var p = Newest;
        var bmp = PhotoBitmap(p);
        if (bmp == null)
        {
            PhotoWidgetShown = null;
            PhotoEmptyShown = p == null && _ph is { Ready: true };
            AeroSkin.Tile(_p, r);
            W95Icons.Draw(_p, W95Icons.Id.Palette, MathF.Floor(r.X + (r.Width - 16) / 2), MathF.Floor(r.Y + (r.Height - 16) / 2) - (PhotoEmptyShown && r.Height >= 40 && r.Width >= 70 ? 6 : 0));
            if (PhotoEmptyShown && r.Height >= 40 && r.Width >= 70)
                Ae.GlowLight(_p, PhotoCut("No pictures", r.Width - 6, 11, FontWeight.Normal, Ae.Font), new RectangleF(r.X + 3, r.Y + r.Height / 2 + 6, r.Width - 6, 16), 11);
            return;
        }
        PhotoWidgetShown = p.Path;
        PhotoEmptyShown = false;
        // the gadget: its shadow, the black frame (rounded), the photo a pixel inside it
        _p.Smooth(true);
        _p.FillRound(new RectangleF(r.X + 1, r.Y + 2, r.Width - 1, r.Height - 2), Theme.WithAlpha(0xFF000000, 0.25f), 4);
        _p.FillRound(new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 2), 0xFF101418, 4);
        _p.Smooth(false);
        var f = new RectangleF(r.X + 2, r.Y + 2, r.Width - 5, r.Height - 6);
        _p.ImagePart(bmp, f, PhotoCover(p.TW, p.TH, f));
        _p.Fill(new RectangleF(f.X, f.Y, f.Width, 1), Theme.WithAlpha(0xFFFFFFFF, 0.35f));
        PhotoFlash95(f);
        if (w.W == 1 && w.H == 1) return;
        // the gadget's strip: dark glass along the foot, the words in white
        var words = Words(_phwNew, p, DateTime.Now);
        var strip = new RectangleF(f.X, f.Bottom - 16, f.Width, 16);
        _p.GradientRaw(strip, Theme.WithAlpha(0xFF000000, 0.45f), Theme.WithAlpha(0xFF000000, 0.75f));
        _p.Fill(new RectangleF(strip.X, strip.Y, strip.Width, 1), Theme.WithAlpha(0xFFFFFFFF, 0.25f));
        float aw = MathF.Min(WAero(words.Ago, 11), strip.Width - 8);
        _p.Text(PhotoCut(words.Ago, strip.Width - 8, 11, FontWeight.Normal, Ae.Font), new RectangleF(strip.X + 4, strip.Y, aw + 4, strip.Height), 0xFFFFFFFF, 11,
            FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
        float room = strip.Width - aw - 16;
        if (p.World != null && room > 24)
            _p.Text(PhotoCut(p.World, room, 11, FontWeight.Normal, Ae.Font), new RectangleF(strip.Right - 4 - room, strip.Y, room, strip.Height), 0xFFD6E4F5, 11,
                FontWeight.Normal, TextAlignment.Trailing, ParagraphAlignment.Center, Ae.Font);
    }

    // ---- Windows Photo Viewer ----

    void PhotoDetailAero(RectangleF win, PanelData d)
    {
        PhotoAeroBody(win, d);
        GalPeople95(win);                           // a photo's people to pick from (WristGallery.cs)
    }

    void PhotoAeroBody(RectangleF win, PanelData d)
    {
        using var set = UseOpenSet();
        PhotoFrame(d);
        bool grid = _gal && _galGrid;                // the gallery's grid: the folder's thumbnails (WristGallery.cs)
        var p = grid ? null : PhotoShown();
        var words = Words(_phwOpen, p, DateTime.Now);
        PhotoOpenShown = p?.Path;
        if (!grid) PhotoEmptyShown = p == null;
        var cl = AeroSkin.Window7(_p, win, 24, ViewerGround);
        string title = grid ? PhotoCut(GalTitle(null), win.Width - 70, 12, FontWeight.Normal, Ae.Font)
            : p == null ? "Windows Photo Viewer" : PhotoCut(PhotoCaption(words, "{0} - Windows Photo Viewer"), win.Width - 70, 12, FontWeight.Normal, Ae.Font);
        Ae.GlowText(_p, title, new RectangleF(win.X + 9, win.Y + 3, win.Width - 70, 18), 0xFF000000, 12);
        var rc = new RectangleF(win.Right - 6 - 43, win.Y + 1, 43, 18);
        bool x = _ui.Clickable("wd95x", RectangleF.Inflate(rc, 4, 4), out bool xh, out bool xd);
        AeroSkin.CaptionBox(_p, rc, CaptionKind.Close, true, xd, xh);
        if (x) { CloseDetail(); return; }
        // the command bar: the viewer's menus (its words only) - its E-mail Send here, VR Gallery's Send to Discord (SendAero) -, then
        // its Open menu - Open in VR Gallery
        var cmd = new RectangleF(cl.X, cl.Y, cl.Width, 20);
        _p.GradientRaw(cmd, ViewerBar1, ViewerBar2);
        float mx = cmd.X + 8;
        foreach (var m in ViewerMenu)
        {
            if (m == SendCmd) { mx = SendAero(mx, cmd, grid ? null : p); continue; }
            float mw = WAero(m, 11);
            _p.Text(m, new RectangleF(mx, cmd.Y, mw + 4, cmd.Height), 0xFFE8F0FB, 11, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
            mx += mw + 14;
        }
        float gw = MathF.Min(WAero(OpenGallery, 11) + 22, cmd.Right - 4 - mx);
        var gal = new RectangleF(mx - 4, cmd.Y + 1, gw, cmd.Height - 2);
        bool go = _ui.Clickable("wphgal", gal, out bool gh, out bool gd) && p != null;
        if ((gh || gd) && p != null)
        {
            _p.Smooth(true);
            _p.FillRound(gal, Theme.WithAlpha(0xFFFFFFFF, gd ? 0.12f : 0.22f), 3);
            _p.StrokeRound(new RectangleF(gal.X + 0.5f, gal.Y + 0.5f, gal.Width - 1, gal.Height - 1), Theme.WithAlpha(0xFFFFFFFF, 0.5f), 1, 3);
            _p.Smooth(false);
        }
        uint gc = p != null ? 0xFFFFFFFF : 0xFF7C8EA8;
        _p.Text(PhotoCut(OpenGallery, gw - 18, 11, FontWeight.Normal, Ae.Font), new RectangleF(gal.X + 4, gal.Y, gw - 16, gal.Height), gc, 11, FontWeight.Normal,
            TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
        _p.Pixels(DropAero, gal.Right - 9, MathF.Floor(gal.Y + gal.Height / 2) - 1, gc);
        if (go) PhotoOpen(p);
        // the control bar, the viewer's own (Erik 10-24, "mehet mind"): at its left "Change the display size" - the magnifier, the zoom
        // (lit while zoomed, greyed over the grid) - and the gallery where its Actual size was; Previous, the big blue Play slide show
        // (Pause while it plays) and Next in the middle; which one where its rotate buttons were; Delete's red X at its right
        var bar = new RectangleF(cl.X, cl.Bottom - 30, cl.Width, 30);
        _p.GradientRaw(bar, ViewerBar1, ViewerBar2);
        _p.Fill(new RectangleF(bar.X, bar.Y, bar.Width, 1), 0xFF6F8DB5);
        int n = PhotoCount;
        float cx = MathF.Floor(bar.X + bar.Width / 2), cy = MathF.Floor(bar.Y + bar.Height / 2);
        if (FlatAero("wphzoom", new RectangleF(bar.X + 6, bar.Y + 3, 28, 24), Lens95, 0xFFE8F0FB, !grid && p != null, _galZoom))
        {
            if (_galZoom) ZoomOut(); else ZoomIn(p, new Vector2(0.5f, 0.5f));
        }
        if (OrbAero("wphall", bar.X + 53, cy, Grid95, true)) GalleryToggle(p);
        if (grid)
        {
            if (HeartAero("wgfav", new RectangleF(bar.Right - 34, bar.Y + 3, 28, 24))) GalFilter(!_galFav, _galWorld, _galPerson, null);
        }
        else if (DelAero("wphdel", new RectangleF(bar.Right - 34, bar.Y + 3, 28, 24), p != null && !DeleteBusy, DeleteArmed(p))) DeleteTap(p);
        if (grid)
        {
            float dx = bar.X + 73, dw = WAero(Lang.T("Days"), 11) + 18;
            if (TextAero("wgdays", new RectangleF(dx, bar.Y + 4, dw, bar.Height - 8), "Days", C.GalleryDays)) GalSetDays(!C.GalleryDays);
            if (GalFilterText() is { } ft)
            {
                // the filter in the count's place: tapped - all again
                float fx = dx + dw + 10, fw = bar.Right - 40 - fx;      // (between Days and the heart)
                var fr = new RectangleF(fx, bar.Y + 4, fw, bar.Height - 8);
                bool click = TextAero("wgclear", fr, PhotoCut(ft, fw - 8, 11, FontWeight.Normal, Ae.Font), true);
                if (click) GalClearFilter();
            }
            else if (_ph is { Recent.Count: > 0 } a)
                _p.Text(GalCountText(a.Recent.Count), new RectangleF(cx - 80, bar.Y, 160, bar.Height), 0xFFE8F0FB, 11, FontWeight.Normal, TextAlignment.Center,
                    ParagraphAlignment.Center, Ae.Font);
        }
        else
        {
            if (OrbAero("wph95p", cx - 36, cy, PrevAero, _phIndex < n - 1)) PhotoStep(1);
            bool show = _galShow;
            if (ShowOrbAero(show ? "wphstop" : "wphplay", cx, cy, show ? PauseBigAero : PlayBigAero, show || _phIndex < n - 1))
            {
                if (show) ShowStop(); else ShowStart();
            }
            if (OrbAero("wph95n", cx + 36, cy, NextAero, _phIndex > 0)) PhotoStep(-1);
            float x0 = cx + 36 + 13 + 6, x1 = bar.Right - 34 - 6;
            if (p != null)
                _p.Text(PhotoCut(words.Count, x1 - x0, 11, FontWeight.Normal, Ae.Font), new RectangleF(x0, bar.Y, x1 - x0, bar.Height), 0xFFE8F0FB, 11,
                    FontWeight.Normal, TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
        }
        // the photo whole on the viewer's ground; when and where under it
        var view = new RectangleF(cl.X + 8, cmd.Bottom + 6, cl.Width - 16, bar.Y - 18 - (cmd.Bottom + 6));
        string ds = DeleteStatus(p);
        if (grid)
        {
            GalGrid(new RectangleF(cl.X + 4, cmd.Bottom + 4, cl.Width - 8, bar.Y - 4 - (cmd.Bottom + 4)), 2);
            return;
        }
        if (ds != null)
            _p.Text(PhotoCut(Lang.T(ds), view.Width, 11, FontWeight.Normal, Ae.Font), new RectangleF(view.X, bar.Y - 15, view.Width, 14), _delNoteBad || DeleteArmed(p) ? 0xFFC42B1C : InkAero,
                11, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
        if (p == null)
        {
            _p.Text(PhotoCut(NoPhotos, view.Width, 12, FontWeight.Normal, Ae.Font), view, SubAero, 12, FontWeight.Normal, TextAlignment.Center,
                ParagraphAlignment.Center, Ae.Font);
            return;
        }
        var (aw, ah) = PhotoAspect(p);
        var pic = PhotoFit(aw, ah, view, true);
        _p.Fill(new RectangleF(pic.X + 1, pic.Y + 2, pic.Width, pic.Height), Theme.WithAlpha(0xFF000000, 0.2f));
        var bmp = PhotoBitmap(p);
        PhotoImage95(pic, p, bmp, 0xFFDCE6F2, false);       // (whole or zoomed: WristGallery.cs)
        PhotoFlash95(pic);
        if (ds != null) return;
        float iy = bar.Y - 15, iw = view.Width;
        float ww = MathF.Min(WAero(words.When, 11), 110);
        _p.Text(PhotoCut(words.When, 110, 11, FontWeight.Normal, Ae.Font), new RectangleF(view.Right - ww - 2, iy, ww + 4, 14), SubAero, 11, FontWeight.Normal,
            TextAlignment.Trailing, ParagraphAlignment.Center, Ae.Font);
        float lw = iw - ww - 14;
        string where = p.World;
        string people = PhotoPeople(p, lw - (where != null ? WAero(where, 11) + 12 : 0), 11, FontWeight.Normal, 0);
        if (where != null)
        {
            string wc = PhotoCut(where, lw, 11, FontWeight.Normal, Ae.Font);
            float wl = WAero(wc, 11);
            _p.Text(wc, new RectangleF(view.X, iy, wl + 4, 14), InkAero, 11, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
            GalTapWorld("wphworld", new RectangleF(view.X, iy, wl + 4, 15), p, InkAero);
            if (people != null && lw - wl - 12 > 30)
            {
                _p.Text(people, new RectangleF(view.X + wl + 12, iy, lw - wl - 12, 14), SubAero, 11, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
                GalTapPeople("wphpeople", new RectangleF(view.X + wl + 12, iy, MathF.Min(lw - wl - 12, WAero(people, 11) + 4), 15), p, SubAero);
            }
        }
        else if (people != null)
        {
            _p.Text(people, new RectangleF(view.X, iy, lw, 14), SubAero, 11, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
            GalTapPeople("wphpeople", new RectangleF(view.X, iy, MathF.Min(lw, WAero(people, 11) + 4), 15), p, SubAero);
        }
    }

    // a word on the control bar that toggles (the gallery's Days): flat, the bar's glass glow under the pointer and while on
    bool TextAero(string id, RectangleF r, string label, bool on)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held);
        if (hover || held || on)
        {
            _p.Smooth(true);
            _p.FillRound(r, Theme.WithAlpha(0xFFFFFFFF, on ? 0.26f : held ? 0.2f : 0.14f), 3);
            _p.StrokeRound(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.WithAlpha(0xFFFFFFFF, 0.5f), 1, 3);
            _p.Smooth(false);
        }
        _p.Text(label, r, 0xFFE8F0FB, 11, FontWeight.Normal, TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
        return click;
    }

    // the grid's Favourites on the control bar: flat, a heart (red while on), the glass glow under the pointer and while on
    bool HeartAero(string id, RectangleF r)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held);
        if (hover || held || _galFav)
        {
            _p.Smooth(true);
            _p.FillRound(r, Theme.WithAlpha(0xFFFFFFFF, _galFav ? 0.26f : held ? 0.2f : 0.14f), 3);
            _p.StrokeRound(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.WithAlpha(0xFFFFFFFF, 0.5f), 1, 3);
            _p.Smooth(false);
        }
        _p.Pixels(Heart95, MathF.Floor(r.X + (r.Width - 7) / 2), MathF.Floor(r.Y + (r.Height - 6) / 2), _galFav ? 0xFFF05A3C : 0xFFE8F0FB);
        return click;
    }

    // the control bar's Delete: flat, its red X; under the pointer (and asking) the bar's soft glass glow
    bool DelAero(string id, RectangleF r, bool enabled, bool asking) => FlatAero(id, r, Del95, 0xFFF05A3C, enabled, asking, asking ? 0xFFFF6040 : 0xFFFFFFFF);

    // a flat button of the control bar (Delete, the magnifier): its picture; under the pointer, held or on, the bar's soft glass glow
    bool FlatAero(string id, RectangleF r, string[] glyph, uint ink, bool enabled, bool on, uint glow = 0xFFFFFFFF)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held) && enabled;
        if ((hover || held || on) && enabled)
        {
            _p.Smooth(true);
            _p.FillRound(r, Theme.WithAlpha(glow, held || on ? 0.28f : 0.18f), 3);
            _p.StrokeRound(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.WithAlpha(0xFFFFFFFF, 0.5f), 1, 3);
            _p.Smooth(false);
        }
        _p.Pixels(glyph, MathF.Floor(r.X + (r.Width - glyph[0].Length) / 2), MathF.Floor(r.Y + (r.Height - glyph.Length) / 2), enabled ? ink : 0xFF7C8EA8);
        return click;
    }

    // the big blue Play slide show in the middle of the control bar (its Pause while the slideshow plays)
    bool ShowOrbAero(string id, float cx, float cy, string[] glyph, bool enabled)
    {
        bool click = _ui.Clickable(id, new RectangleF(cx - 15, cy - 15, 30, 30), out bool hover, out bool held) && enabled;
        AeroSkin.Orb(_p, cx, cy, 13, true, hover && enabled, held && enabled, glyph, enabled);
        return click;
    }

    // the command bar's Send - the viewer's E-mail, saying what it does here (VR Gallery sends its newest photo to its Discord webhook):
    // live on that photo only, greyed on the others; asking ("Send?") the bar's glass stays on it, its answer in the line under the
    // photo. Returns where the next word goes
    float SendAero(float mx, RectangleF cmd, PhotoWatcher.Photo p)
    {
        bool can = CanSend(p), armed = can && SendArmed(p), busy = can && _sendBusy;
        string label = busy ? "Sending..." : armed ? SendAsk : SendCmd;
        float mw = WAero(label, 11);
        if (can)
        {
            _sendSeen = true;
            var r = new RectangleF(mx - 4, cmd.Y + 1, mw + 8, cmd.Height - 2);
            bool click = _ui.Clickable("wphsend", r, out bool h, out bool d) && !busy;
            if (h || d || armed)
            {
                _p.Smooth(true);
                _p.FillRound(r, Theme.WithAlpha(0xFFFFFFFF, d ? 0.12f : armed ? 0.28f : 0.22f), 3);
                _p.StrokeRound(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.WithAlpha(0xFFFFFFFF, 0.5f), 1, 3);
                _p.Smooth(false);
            }
            if (click) SendTap(p);
        }
        _p.Text(label, new RectangleF(mx, cmd.Y, mw + 4, cmd.Height), can ? 0xFFFFFFFF : 0xFF7C8EA8, 11, FontWeight.Normal, TextAlignment.Leading,
            ParagraphAlignment.Center, Ae.Font);
        return mx + mw + 14;
    }

    // a round silver button of the viewer's control bar (Media Player's orb, small)
    bool OrbAero(string id, float cx, float cy, string[] glyph, bool enabled)
    {
        bool click = _ui.Clickable(id, new RectangleF(cx - 13, cy - 13, 26, 26), out bool hover, out bool held) && enabled;
        AeroSkin.Orb(_p, cx, cy, 10, false, hover && enabled, held && enabled, glyph, enabled);
        return click;
    }
}
