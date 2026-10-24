using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Threading;

namespace Orbit;

// --snapshot's part for the photo view's gallery and Delete (WristGallery.cs, Sys/PhotoAlbum.cs), on made-up photos only: Gallery opens
// the grid, the stick scrolls it, a tap opens a photo big, its arrow steps through all of them, Gallery goes back to the grid and to the
// photo again; Delete asks on its first tap, on the second hands the photo to the app (a made-up hand-over: nothing is ever deleted by a
// tool) and the view steps off it, a refusal says why; the album itself on a folder of made-up PNGs; what the bin refuses; the photo
// view's Send, slideshow and zoom in every style that has them (ViewChecks; ORBIT_SNAP_ONLY=photoview runs those alone). Pictures:
// wrist_gallery_<case><tag>.png, gallery_preview.png (every style's grid and Delete asking) and gallery_actions.png (Send asking, the
// slideshow, the zoom).
static class GalleryViewTools
{
    static void Draw2(WristPanel wrist, PanelData data) { wrist.Draw(data); wrist.Draw(data); }

    static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    static bool TapId(WristPanel wrist, PanelData data, string id)
    {
        var at = wrist.ControlPoint(data, id);
        if (at is not { } a) return false;
        Tap(wrist, data, a);
        return true;
    }

    static bool TapCell(WristPanel wrist, PanelData data, int slot)
    {
        var at = wrist.GalleryCellPoint(data, slot);
        if (at is not { } a) return false;
        Tap(wrist, data, a);
        return true;
    }

    // the laser held on the sheet and pulled up by dy px (the panel's), a step a frame
    static void Drag(WristPanel wrist, PanelData data, Vector2 from, float dy, bool rail = false)
    {
        wrist.PointerMove(Hand.Left, from); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, from); wrist.Draw(data);
        for (int i = 1; i <= 8; i++)
        {
            Thread.Sleep(12);
            wrist.PointerMove(Hand.Left, from + new Vector2(0, -dy * i / 8)); wrist.Draw(data);
        }
        var to = from + new Vector2(0, -dy);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, to); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    static void Settle(WristPanel wrist, PanelData data)
    {
        for (int k = 0; k < 11; k++) { Thread.Sleep(40); wrist.Draw(data); }
    }

    static void Scroll(WristPanel wrist, PanelData data, float n)
    {
        float s = Theme.Classic ? 2 : 1;
        var mid = new Vector2(WristPanel.TW / 2f, WristPanel.TH / 2f);
        wrist.PointerMove(Hand.Left, mid); wrist.Draw(data);
        wrist.PointerScroll(Hand.Left, n, mid); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    // the album: the latest photo's five (today), then made-up days - yesterday, three days ago, last month, a year ago
    static PhotoWatcher.Snapshot _album;
    static PhotoWatcher.Snapshot _albumOf;

    public static PhotoWatcher.Snapshot Album(PhotoWatcher.Snapshot latest)
    {
        if (ReferenceEquals(_albumOf, latest) && _album != null) return _album;
        var s = new PhotoWatcher.Snapshot { Version = 1, Ready = true, FolderFound = true, Folder = @"C:\made-up\VRChat" };
        s.Recent.AddRange(latest.Recent);
        var now = DateTime.Now;
        int k = 0;
        foreach (var (days, n) in new[] { (1, 7), (3, 9), (40, 6), (100, 12), (366, 4), (400, 10) })
            for (int i = 0; i < n; i++, k++)
            {
                int scene = (k * 5 + 1) % 6;
                bool tall = scene == 5;
                var (px, w, h) = PhotoFake.Pixels(scene, tall ? 180 : 320, tall ? 320 : 180);
                var taken = now.Date.AddDays(-days).AddHours(21).AddMinutes(-i * 13);
                var (world, people) = PhotoFake.Meta[k % PhotoFake.Meta.Length];
                string name = $"VRChat_{taken:yyyy-MM-dd_HH-mm-ss.fff}_{(tall ? "1080x1920" : "1920x1080")}.png";
                s.Recent.Add(new PhotoWatcher.Photo
                {
                    Path = @"C:\made-up\VRChat\" + taken.ToString("yyyy-MM") + @"\" + name, Name = name, Taken = taken, Width = tall ? 1080 : 1920,
                    Height = tall ? 1920 : 1080, World = world, Players = people, Thumb = px, TW = w, TH = h, Seen = now, Favorite = k % 4 == 1,
                });
            }
        (_album, _albumOf) = (s, latest);
        return s;
    }

    public static readonly List<(string tag, string grid, string ask)> Preview = new();

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var photos0 = data.Photos;
        var album0 = data.Album;
        var del0 = wrist.OnDeletePhoto;
        var open0 = wrist.OnOpenPhoto;
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            wrist.OnOpenPhoto = _ => { };
            Checks(wrist, data, wcfg, tag);
            FilterChecks(wrist, data, wcfg, gpu, dir, tag);
            ViewChecks(wrist, data, wcfg, gpu, dir, tag);
            Pictures(wrist, data, wcfg, gpu, dir, tag);
        }
        catch (Exception e) { Log.Error("gallery view" + tag + ": " + e); }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            wrist.OnDeletePhoto = del0;
            wrist.OnOpenPhoto = open0;
            data.Photos = photos0;
            data.Album = album0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    static void Checks(WristPanel wrist, PanelData data, WristConfig wcfg, string tag)
    {
        bool classic = Theme.Classic;
        string prevId = classic ? "wph95p" : "wphprev";
        var snap = PhotoFake.Snap(5);
        var album = Album(snap);
        data.Photos = snap;
        data.Album = album;
        string deleted = null, answer = null;
        wrist.OnDeletePhoto = (p, done) => { deleted = p; done(answer); };
        wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 0, Y = 2, W = 2, H = 1 } };
        wrist.Settle();
        wrist.OpenForTest("photo");
        Draw2(wrist, data);
        // Gallery: the grid (scrolled to the photo shown: the newest, at the top)
        bool all = TapId(wrist, data, "wphall");
        bool grid = all && wrist.GalleryGridShown && wrist.GalleryScroll == 0;
        // the stick scrolls it
        Scroll(wrist, data, -6);
        bool scrolled = wrist.GalleryScroll > 0;
        // the laser pulls the sheet up: it scrolls, nothing opens (and let go it glides on a little: modern, Cyber)
        float s1 = wrist.GalleryScroll;
        var c0 = wrist.GalleryCellPoint(data, 6);
        if (c0 is { } cp) Drag(wrist, data, cp, 120);
        bool dragged = c0 != null && wrist.GalleryScroll > s1 + (Theme.Classic ? 35 : 80) && wrist.GalleryGridShown;     // (retro: half the px)
        Settle(wrist, data);
        // Days: the headings; All again: the one sheet, the photo at the top still there
        bool daysTap = TapId(wrist, data, "wgdays");
        bool days = daysTap && wrist.GalleryDaysShown;
        Settle(wrist, data);
        bool allTap = Theme.Classic ? TapId(wrist, data, "wgdays") : TapId(wrist, data, "wgall");
        bool allAgain = allTap && !wrist.GalleryDaysShown;
        Settle(wrist, data);
        // a tap on a photo: big, in all the photos
        bool tapped = TapCell(wrist, data, 2);
        Draw2(wrist, data);
        string pick = wrist.LastGalleryPick;
        int at = pick == null ? -1 : album.Recent.FindIndex(p => p.Path == pick);
        bool picked = tapped && at >= 0 && !wrist.GalleryGridShown && wrist.GalleryOpenShown == pick;
        if (!picked) Log.Info($"gallery view{tag}: (the tap: control found {tapped}, picked {pick ?? "none"} at {at}, grid {wrist.GalleryGridShown}, shown {wrist.GalleryOpenShown ?? "none"})");
        // its arrow: the one before it, across the days
        TapId(wrist, data, prevId);
        bool older = at >= 0 && at + 1 < album.Recent.Count && wrist.GalleryOpenShown == album.Recent[at + 1].Path;
        // Gallery: the grid again; again: back to the photo
        TapId(wrist, data, "wphall");
        bool back = wrist.GalleryGridShown;
        TapId(wrist, data, "wphall");
        Draw2(wrist, data);
        bool again = !wrist.GalleryGridShown && at >= 0 && wrist.GalleryOpenShown == album.Recent[at + 1].Path;
        // Delete: the first tap asks (nothing handed over), the second hands it to the app - the view steps to the one before it
        string target = wrist.GalleryOpenShown;
        TapId(wrist, data, "wphdel");
        bool asks = wrist.DeleteArmedShown && deleted == null;
        TapId(wrist, data, "wphdel");
        Draw2(wrist, data);
        bool moved = target != null && deleted == target && wrist.GalleryNote == "Moved to the Recycle Bin" && wrist.GalleryOpenShown != target;
        // one Windows would not move: why, the view stays on it
        answer = "Windows did not move it";
        deleted = null;
        string t2 = wrist.GalleryOpenShown;
        TapId(wrist, data, "wphdel");
        TapId(wrist, data, "wphdel");
        Draw2(wrist, data);
        bool refused = t2 != null && deleted == t2 && wrist.GalleryNote == answer && wrist.GalleryOpenShown == t2;
        // closed and opened again: the latest photo's own view, not the gallery; Delete there too
        wrist.CloseForTest();
        wrist.Settle();
        wrist.OpenForTest("photo");
        Draw2(wrist, data);
        bool fresh = !wrist.GalleryOn && wrist.PhotoOpenShown == snap.Recent[0].Path;
        answer = null;
        deleted = null;
        TapId(wrist, data, "wphdel");
        TapId(wrist, data, "wphdel");
        Draw2(wrist, data);
        bool latestDel = deleted == snap.Recent[0].Path && wrist.PhotoOpenShown == snap.Recent[1].Path;
        // no album yet (it is being listed): the grid waits, nothing breaks
        data.Album = null;
        TapId(wrist, data, "wphall");
        Draw2(wrist, data);
        bool waits = wrist.GalleryGridShown && !wrist.PhotoEmptyShown;
        wrist.CloseForTest();
        Log.Info($"gallery view{tag}: Gallery opened the grid {grid} (want True), the stick scrolled it {scrolled} (want True), the laser dragged it " +
                 $"{dragged} (want True), Days {days} and All again {allAgain} (want True True), a tap opened that photo big " +
                 $"{picked} (want True), its arrow stepped to the one before it {older} (want True), Gallery went back to the grid {back} and to the photo " +
                 $"again {again} (want True True); Delete asked first {asks} (want True), then handed it over and stepped off it {moved} (want True), " +
                 $"a refusal said why and stayed {refused} (want True); opened again: the latest photo's view {fresh} (want True), its Delete too " +
                 $"{latestDel} (want True); no album yet: the grid waits {waits} (want True)");
    }

    // The filters (VR Gallery's favourites, a photo's world, its people): Favourites shows only those; a photo's world tapped - its photos;
    // its people tapped - the picker, a name in it - their photos; the filter's chip (a retro style: its status line) - all again
    static void FilterChecks(WristPanel wrist, PanelData data, WristConfig wcfg, Gpu gpu, string dir, string tag)
    {
        bool classic = Theme.Classic;
        var snap = PhotoFake.Snap(5);
        var album = Album(snap);
        data.Photos = snap;
        data.Album = album;
        wrist.OnDeletePhoto = (p, done) => done(null);
        wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 0, Y = 2, W = 2, H = 1 } };
        int favs = album.Recent.FindAll(p => p.Favorite).Count;
        wrist.Settle();
        wrist.OpenForTest("photo");
        wrist.GalleryNoteForTest();                     // (the Delete checks' "Moved to the Recycle Bin": a retro status line says it a while)
        Draw2(wrist, data);
        TapId(wrist, data, "wphall");
        // Favourites: only those
        bool favTap = TapId(wrist, data, "wgfav");
        Draw2(wrist, data);
        bool favOnly = favTap && wrist.GalleryFilterShown == "favourites";
        Settle(wrist, data);
        string favPic = Path.Combine(dir, $"wrist_gallery_fav{tag}.png");
        wrist.Surf.SavePng(gpu, favPic);
        TapCell(wrist, data, 0);
        Draw2(wrist, data);
        var first = album.Recent.Find(p => p.Path == wrist.LastGalleryPick);
        bool favPicked = first != null && first.Favorite;
        TapId(wrist, data, "wphall");
        TapId(wrist, data, "wgfav");                    // (off again)
        Draw2(wrist, data);
        bool favOff = wrist.GalleryFilterShown == null;
        // a photo with a world and people (Lantern Harbor at Dusk, five people): its world tapped - only that world's photos
        int at = album.Recent.FindIndex(p => p.World == PhotoFake.Meta[0].world && p.Players.Length > 1);
        string world = PhotoFake.Meta[0].world;
        int inWorld = album.Recent.FindAll(p => p.World == world).Count;
        // (opened through the grid: scroll it there - the cell's slot is its place among the drawn ones)
        TapCell(wrist, data, 0);
        Draw2(wrist, data);
        for (int g = 0; g < 80 && wrist.GalleryOpenShown != album.Recent[at].Path; g++)
        {
            var pn = wrist.ControlPoint(data, classic ? "wph95p" : "wphprev");
            if (pn is not { } pp) break;
            Tap(wrist, data, pp);
        }
        bool onIt = wrist.GalleryOpenShown == album.Recent[at].Path;
        Settle(wrist, data);
        bool worldTap = TapId(wrist, data, "wphworld");
        Draw2(wrist, data);
        bool worldOnly = worldTap && wrist.GalleryFilterShown == world && wrist.GalleryGridShown;
        Settle(wrist, data);
        string worldPic = Path.Combine(dir, $"wrist_gallery_world{tag}.png");
        wrist.Surf.SavePng(gpu, worldPic);
        // the filter's chip / status line: all again
        bool clearTap = TapId(wrist, data, "wgclear");
        Draw2(wrist, data);
        bool cleared = clearTap && wrist.GalleryFilterShown == null;
        // the people: the picker, a name in it
        TapCell(wrist, data, 0);
        Draw2(wrist, data);
        for (int g = 0; g < 80 && wrist.GalleryOpenShown != album.Recent[at].Path; g++)
        {
            var pn = wrist.ControlPoint(data, classic ? "wph95p" : "wphprev");
            if (pn is not { } pp) break;
            Tap(wrist, data, pp);
        }
        Settle(wrist, data);
        bool peopleTap = TapId(wrist, data, "wphpeople");
        Draw2(wrist, data);
        bool picker = peopleTap && wrist.GalleryPeopleShown;
        Settle(wrist, data);
        string pickPic = Path.Combine(dir, $"wrist_gallery_people{tag}.png");
        wrist.Surf.SavePng(gpu, pickPic);
        string person = album.Recent[at].Players[1];
        bool nameTap = TapId(wrist, data, classic ? "wplist/1" : "wpp1");
        Draw2(wrist, data);
        bool personOnly = nameTap && wrist.GalleryFilterShown == person && !wrist.GalleryPeopleShown && wrist.GalleryGridShown;
        wrist.CloseForTest();
        Log.Info($"gallery filters{tag}: Favourites showed only those {favOnly} (want True), a photo of them is one {favPicked} (want True), off again " +
                 $"{favOff} (want True); on the photo {onIt} (want True), its world tapped - only its photos {worldOnly} (want True), the chip showed all " +
                 $"again {cleared} (want True); its people tapped - the picker {picker} (want True), a name in it - their photos {personOnly} (want True)" +
                 $" ({favs} favourites, {inWorld} in that world)");
        FilterPreview.RemoveAll(x => x.tag == tag);
        FilterPreview.Add((tag, worldPic, pickPic));
    }

    public static readonly List<(string tag, string world, string people)> FilterPreview = new();

    // The photo view's Send (VR Gallery's newest only - a made-up hand-over, nothing is sent), the slideshow and the zoom, in each style
    // its way: modern / Cyber over the photo (a tap on it zooms); 95 Imaging Send and the zoom - its Zoom menu word the switch, a tap on
    // the photo then zooms (no slideshow: Imaging had none); XP's Start Slide Show and Zoom In / Zoom Out; 7's Play orb and magnifier;
    // the Mac's magnifier and slideshow. The Terminal has none of them (its keys are full)
    public static readonly List<(string tag, string send, string show, string zoom)> ActionsPreview = new();

    static void ViewChecks(WristPanel wrist, PanelData data, WristConfig wcfg, Gpu gpu, string dir, string tag)
    {
        var style = Theme.Retro;
        if (style == RetroStyle.Terminal) { Log.Info($"gallery view{tag}: the Terminal's viewer has no Send, slideshow or zoom (its keys are full)"); return; }
        bool classic = Theme.Classic, hasShow = style != RetroStyle.Win95;
        string prevId = classic ? "wph95p" : "wphprev";
        var snap = PhotoFake.Snap(5);
        data.Photos = snap;
        data.Album = Album(snap);
        int sent = 0;
        string hiAsked = null;
        var send0 = wrist.OnSendNewest;
        var hi0 = wrist.OnWantHi;
        bool ready0 = wrist.SendReady;
        int secs0 = wrist.SlideSeconds;
        try
        {
            wrist.OnSendNewest = done => { sent++; done(null); };
            wrist.OnWantHi = path => hiAsked = path;
            wrist.SendReady = true;
            wrist.SlideSeconds = 1;
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 0, Y = 2, W = 2, H = 1 } };
            wrist.Settle();
            wrist.OpenForTest("photo");
            wrist.GalleryNoteForTest();
            Draw2(wrist, data);
            // Send: on the newest, asks first, then hands it over
            bool shown = wrist.ControlPoint(data, "wphsend") != null;
            TapId(wrist, data, "wphsend");
            bool asks = sent == 0;
            Settle(wrist, data);
            string sendPic = Path.Combine(dir, $"wrist_gallery_send{tag}.png");
            wrist.Surf.SavePng(gpu, sendPic);
            TapId(wrist, data, "wphsend");
            Draw2(wrist, data);
            bool handed = sent == 1 && wrist.GalleryNote == "Sent to Discord";
            // not on an older one (VR Gallery sends its newest only; 7's command bar greys its Send there)
            TapId(wrist, data, prevId);
            Draw2(wrist, data);
            bool notOlder = wrist.ControlPoint(data, "wphsend") == null && !wrist.SendShown(data);
            // the slideshow: on to the older one each SlideSeconds (a retro viewer's too: PhotoFrame's tick), Stop ends it
            string showLine = "", showPic = null;
            if (hasShow)
            {
                string before = wrist.PhotoOpenShown;
                bool playTap = TapId(wrist, data, "wphplay");
                bool playing = playTap && wrist.GalleryShowing;
                Thread.Sleep(400);                      // (modern / Cyber: the first slide faded in; a slide steps on in a frame drawn only)
                Draw2(wrist, data);
                showPic = Path.Combine(dir, $"wrist_gallery_show{tag}.png");
                wrist.Surf.SavePng(gpu, showPic);
                Thread.Sleep(750);
                Draw2(wrist, data);
                int i0 = snap.Recent.FindIndex(x => x.Path == before);
                bool advanced = i0 >= 0 && i0 + 1 < snap.Recent.Count && wrist.PhotoOpenShown == snap.Recent[i0 + 1].Path;
                bool stopTap = TapId(wrist, data, "wphstop");
                bool stopped = stopTap && !wrist.GalleryShowing;
                showLine = $"the slideshow played {playing} (want True), stepped on {advanced} (want True), Stop ended it {stopped} (want True); ";
            }
            // the zoom - zoomed in, the sharp copy asked for; it comes, it is drawn; whole again: modern / Cyber a tap on the photo and another;
            // 95 its Zoom switched on, then the same (the photo no control before); XP Zoom In, Zoom Out; 7 and the Mac the magnifier twice
            bool unarmed = style != RetroStyle.Win95 || wrist.ControlPoint(data, "wphzoom") == null;
            string[] zoomOn = style switch { RetroStyle.Win95 => new[] { "wph95zoom", "wphzoom" }, RetroStyle.XP => new[] { "wphzin" }, _ => new[] { "wphzoom" } };
            string[] zoomOff = style == RetroStyle.XP ? new[] { "wphzout" } : new[] { "wphzoom" };
            bool onTaps = true, offTaps = true;
            foreach (var id in zoomOn) onTaps &= TapId(wrist, data, id);
            bool zoomed = onTaps && wrist.GalleryZoomShown && hiAsked == wrist.PhotoOpenShown;
            if (hiAsked != null)
            {
                var (px, w, h) = PhotoFake.Pixels(snap.Recent.Find(x => x.Path == hiAsked) is { } hp && hp.TW < hp.TH ? 5 : 1, 1280, 720);
                wrist.Hi = new WristPanel.HiPicture { Path = hiAsked, Px = px, W = w, H = h };
            }
            Settle(wrist, data);
            string zoomPic = Path.Combine(dir, $"wrist_gallery_zoom{tag}.png");
            wrist.Surf.SavePng(gpu, zoomPic);
            foreach (var id in zoomOff) offTaps &= TapId(wrist, data, id);
            bool whole = offTaps && !wrist.GalleryZoomShown;
            // a step while zoomed in (XP, 7, the Mac): the zoom's button pressed, the stick steps on, the button let go in the frame after
            // the step - the new photo is whole before the toolbar is drawn, so the button zooms it (the toolbar was drawn zoomed for a
            // frame: XP's Zoom In greyed, 7's and the Mac's magnifier in - the tap did nothing)
            string stepLine = "";
            if (style is RetroStyle.XP or RetroStyle.Aero or RetroStyle.Mac)
            {
                string zid = style == RetroStyle.XP ? "wphzin" : "wphzoom";
                bool zin = TapId(wrist, data, zid) && wrist.GalleryZoomShown;
                string from = wrist.PhotoOpenShown;
                bool stepZoom = false;
                if (zin && wrist.ControlPoint(data, zid) is { } zp)
                {
                    wrist.PointerMove(Hand.Left, zp); wrist.Draw(data);
                    wrist.PointerButton(Hand.Left, PtrButton.Primary, true, zp); wrist.Draw(data);
                    wrist.PointerScroll(Hand.Left, -1, zp);                 // (the stick, a notch: the one before)
                    wrist.PointerButton(Hand.Left, PtrButton.Primary, false, zp); wrist.Draw(data);
                    wrist.PointerLeave(Hand.Left); wrist.Draw(data);
                    stepZoom = wrist.PhotoOpenShown != from && wrist.GalleryZoomShown && hiAsked == wrist.PhotoOpenShown;
                }
                stepLine = $", after a step its button zoomed the new one {stepZoom} (want True)";
            }
            // the slideshow, then the photo's world (its link stays live while it plays): the grid of that world's photos, the slideshow
            // ended (it went on under the grid, a redraw every frame, and a photo picked there stepped on)
            string gridLine = "";
            if (hasShow)
            {
                wrist.GalleryNoteForTest();             // (Send's "Sent to Discord" gone: in XP, 7 and the Mac it holds the line the world is in)
                bool play2 = TapId(wrist, data, "wphplay") && wrist.GalleryShowing;
                bool worldTap = TapId(wrist, data, "wphworld");
                Draw2(wrist, data);
                bool ended = play2 && worldTap && wrist.GalleryGridShown && !wrist.GalleryShowing;
                gridLine = $"; a world tapped while it played - its grid, the slideshow ended {ended} (want True)";
            }
            string toolLine = "";
            if (style == RetroStyle.Win95)
            {
                bool toolOff = TapId(wrist, data, "wph95zoom") && !wrist.ZoomToolShown && wrist.ControlPoint(data, "wphzoom") == null;
                toolLine = $"; before Zoom was on the photo did not zoom {unarmed} (want True), Zoom off again neither {toolOff} (want True)";
            }
            wrist.CloseForTest();
            Log.Info($"gallery view{tag}: Send on the newest {shown} (want True), it asked first {asks} (want True), then handed it over {handed} (want True), " +
                     $"none on an older one {notOlder} (want True); {showLine}zoomed in {zoomed} (want True), whole again {whole} (want True){stepLine}{toolLine}{gridLine}");
            ActionsPreview.RemoveAll(x => x.tag == tag);
            ActionsPreview.Add((tag, sendPic, showPic, zoomPic));
        }
        finally
        {
            wrist.CloseForTest();
            wrist.OnSendNewest = send0;
            wrist.OnWantHi = hi0;
            wrist.SendReady = ready0;
            wrist.SlideSeconds = secs0;
            wrist.Hi = null;
        }
    }

    static void Pictures(WristPanel wrist, PanelData data, WristConfig wcfg, Gpu gpu, string dir, string tag)
    {
        var snap = PhotoFake.Snap(5);
        data.Photos = snap;
        data.Album = Album(snap);
        wrist.OnDeletePhoto = (p, done) => done(null);
        wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 2, Y = 0, W = 2, H = 2 } };
        string Save(string name)
        {
            string file = Path.Combine(dir, $"wrist_gallery_{name}{tag}.png");
            wrist.Surf.SavePng(gpu, file);
            return file;
        }
        wrist.Settle();
        wrist.OpenForTest("photo");
        Draw2(wrist, data);
        TapId(wrist, data, "wphall");
        wrist.Settle();
        Settle(wrist, data);
        string grid = Save("grid");
        Scroll(wrist, data, -9);
        Settle(wrist, data);
        Save("scrolled");
        TapId(wrist, data, "wgdays");
        Scroll(wrist, data, -4);
        Settle(wrist, data);
        Save("days");
        if (Theme.Classic) TapId(wrist, data, "wgdays"); else TapId(wrist, data, "wgall");
        Settle(wrist, data);
        TapCell(wrist, data, 1);
        wrist.Settle();
        Settle(wrist, data);
        Save("open");
        TapId(wrist, data, "wphdel");
        Settle(wrist, data);
        string ask = Save("delete");
        TapId(wrist, data, "wphdel");
        Draw2(wrist, data);
        Save("deleted");
        wrist.CloseForTest();
        Preview.RemoveAll(p => p.tag == tag);
        Preview.Add((tag, grid, ask));
        Log.Info($"gallery view{tag}: 6 pictures (wrist_gallery_*{tag}.png)");
    }

    // what a frame allocates with the grid at rest: nothing is the goal (the layout, the headings, the bitmaps kept)
    public static void Perf(WristPanel wrist, PanelData data, ConfigStore cfg, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var photos0 = data.Photos;
        var album0 = data.Album;
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            wcfg.Widgets = new List<WatchWidget> { new() { Type = "photo", X = 0, Y = 0, W = 2, H = 2 } };
            var snap = PhotoFake.Snap(5);
            data.Photos = snap;
            data.Album = Album(snap);
            long Frame()
            {
                for (int i = 0; i < 6; i++) wrist.Draw(data);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 30; i++) wrist.Draw(data);
                return (GC.GetAllocatedBytesForCurrentThread() - a0) / 30;
            }
            wrist.Settle();
            wrist.OpenForTest("photo");
            TapId(wrist, data, "wphall");
            long grid = Frame();
            Scroll(wrist, data, -9);
            long scrolled = Frame();
            TapCell(wrist, data, 1);
            long open = Frame();
            wrist.CloseForTest();
            Log.Info($"gallery perf{tag}: the grid at rest {grid} B a frame, scrolled {scrolled} B, a photo of it open {open} B (want 0, 0, 0)");
        }
        finally
        {
            wrist.CloseForTest();
            data.Photos = photos0;
            data.Album = album0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    // ORBIT_SNAP_ONLY=photoview: the photo view's Send, slideshow and zoom in every style (ViewChecks) and their pictures side by side
    // (gallery_actions.png) - in seconds, on made-up photos
    public static int ViewOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = Tools.SnapCulture;
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var photos0 = data.Photos;
        var album0 = data.Album;
        var open0 = wrist.OnOpenPhoto;
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            wrist.OnOpenPhoto = _ => { };
            foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
            {
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                try { ViewChecks(wrist, data, wcfg, gpu, dir, tag); }
                catch (Exception e) { Log.Error("gallery view" + tag + ": " + e); }
            }
            ActionsSheet(dir);
        }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            wrist.OnOpenPhoto = open0;
            data.Photos = photos0;
            data.Album = album0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        Log.Info("snapshot (photo view only) written to " + dir);
        return 0;
    }

    // every style's Send asking, its slideshow and its zoom, side by side: gallery_actions.png
    static void ActionsSheet(string dir)
    {
        if (ActionsPreview.Count == 0) return;
        const int W = 480, H = 330, Gap = 12, Label = 26;
        using var sheet = new Bitmap(3 * W + 4 * Gap, ActionsPreview.Count * (H + Label + Gap) + Gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 32, 34, 40));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int i = 0; i < ActionsPreview.Count; i++)
            {
                var (t, send, show, zoom) = ActionsPreview[i];
                float y = Gap + i * (H + Label + Gap);
                g.DrawString($"{(t.Length == 0 ? "modern" : t[1..])}: Send asking, the slideshow playing, the zoom", font, Brushes.White, Gap, y + 4);
                int k = 0;
                foreach (var f in new[] { send, show, zoom })
                {
                    float x = Gap + k++ * (W + Gap);
                    if (f == null) { g.DrawString("(no slideshow in this style)", font, Brushes.Gray, x + 12, y + Label + H / 2f - 8); continue; }
                    using var b = new Bitmap(f);
                    g.DrawImage(b, x, y + Label, W, H);
                }
            }
        }
        string path = Path.Combine(dir, "gallery_actions.png");
        sheet.Save(path, ImageFormat.Png);
        Log.Info($"gallery view: the styles' Send, slideshow and zoom side by side: {path}");
    }

    // every style's grid and its Delete asking, side by side: gallery_preview.png
    public static void PreviewSheet(string dir)
    {
        ActionsSheet(dir);
        if (Preview.Count == 0) return;
        const int W = 640, H = 440, Gap = 12, Label = 26;
        using var sheet = new Bitmap(2 * W + 3 * Gap, Preview.Count * (H + Label + Gap) + Gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(255, 32, 34, 40));
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            for (int i = 0; i < Preview.Count; i++)
            {
                var (t, grid, ask) = Preview[i];
                float y = Gap + i * (H + Label + Gap);
                string style = t.Length == 0 ? "modern" : t[1..];
                g.DrawString($"{style}: the gallery's grid, a photo of it with Delete asking", font, Brushes.White, Gap, y + 4);
                using (var a = new Bitmap(grid)) g.DrawImage(a, Gap, y + Label, W, H);
                using (var b = new Bitmap(ask)) g.DrawImage(b, 2 * Gap + W, y + Label, W, H);
            }
        }
        string path = Path.Combine(dir, "gallery_preview.png");
        sheet.Save(path, ImageFormat.Png);
        if (FilterPreview.Count > 0)
        {
            using var s2 = new Bitmap(2 * W + 3 * Gap, FilterPreview.Count * (H + Label + Gap) + Gap);
            using (var g = Graphics.FromImage(s2))
            {
                g.Clear(Color.FromArgb(255, 32, 34, 40));
                using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
                for (int i = 0; i < FilterPreview.Count; i++)
                {
                    var (t, w1, p1) = FilterPreview[i];
                    float y = Gap + i * (H + Label + Gap);
                    g.DrawString($"{(t.Length == 0 ? "modern" : t[1..])}: a photo's world (its photos), its people (the picker)", font, Brushes.White, Gap, y + 4);
                    using (var a = new Bitmap(w1)) g.DrawImage(a, Gap, y + Label, W, H);
                    using (var b = new Bitmap(p1)) g.DrawImage(b, 2 * Gap + W, y + Label, W, H);
                }
            }
            s2.Save(Path.Combine(dir, "gallery_filters.png"), ImageFormat.Png);
        }
        Log.Info($"gallery view: the styles' grids and Delete side by side: {path}");
    }

    // a made-up PNG with VRCX's metadata where VRCX writes it: right after IHDR, before the picture data
    static byte[] PngHead(int k, int w, int h, int meta)
    {
        var tail = PhotoFake.Png(k, w, h, meta);          // (its chunk before IEND: moved to after IHDR)
        var plain = PhotoFake.Png(k, w, h);
        int chunkLen = tail.Length - plain.Length;
        var chunk = tail.AsSpan(plain.Length - 12, chunkLen).ToArray();
        var o = new List<byte>(plain.AsSpan(0, 33).ToArray());
        o.AddRange(chunk);
        o.AddRange(plain.AsSpan(33).ToArray());
        return o.ToArray();
    }

    // The album on a folder of made-up photos (the snapshot's directory): every one listed, newest first, from the folder and its month
    // folders; a cell's picture made (the shell's thumbnail or the file decoded) within its bounds; a photo wanted big gets its picture
    // and the metadata from its head; a file gone listed again without it; switched off, the pictures let go. Then what the bin
    // refuses - checked only: nothing is ever moved by the tool.
    public static void AlbumChecks(string dir)
    {
        string root = Path.Combine(dir, "album_folder");
        using var a = new PhotoAlbum { FolderOverride = root };
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            var now = DateTime.Now;
            string Month(DateTime t) { string m = Path.Combine(root, t.ToString("yyyy-MM")); Directory.CreateDirectory(m); return m; }
            string Put(string folder, DateTime t, byte[] png)
            {
                string f = Path.Combine(folder, $"VRChat_{t:yyyy-MM-dd_HH-mm-ss.fff}_640x360.png");
                File.WriteAllBytes(f, png);
                return f;
            }
            var t0 = now.AddMinutes(-5);
            string newest = Put(Month(t0), t0, PngHead(2, 640, 360, 2));
            Put(Month(now.AddMinutes(-30)), now.AddMinutes(-30), PhotoFake.Png(0, 640, 360));
            Put(Month(now.AddDays(-1)), now.AddDays(-1), PhotoFake.Png(1, 640, 360));
            string gone = Put(Month(now.AddDays(-40)), now.AddDays(-40), PhotoFake.Png(3, 640, 360));
            Put(Month(now.AddDays(-41)), now.AddDays(-41), PhotoFake.Png(4, 640, 360));
            Put(root, now.AddYears(-2), PhotoFake.Png(0, 640, 360));          // (before the month folders: in the folder itself)
            File.WriteAllText(Path.Combine(root, "notes.txt"), "not a photo");
            a.Active = true;
            PhotoWatcher.Snapshot s = null;
            bool Wait(Func<PhotoWatcher.Snapshot, bool> ok, double seconds)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < seconds)
                {
                    s = a.Get();
                    if (ok(s)) return true;
                    Thread.Sleep(50);
                }
                return false;
            }
            bool listed = Wait(x => x.Ready && x.Recent.Count == 6, 5);
            bool order = listed && s.Recent[0].Path == newest;
            for (int i = 1; order && i < s.Recent.Count; i++) order = s.Recent[i - 1].Taken >= s.Recent[i].Taken;
            // a cell: asked, then made
            var sw0 = System.Diagnostics.Stopwatch.StartNew();
            (uint[] px, int w, int h) cell = a.Cell(newest);
            bool askedFirst = cell.px == null;
            while (cell.px == null && sw0.Elapsed.TotalSeconds < 8) { Thread.Sleep(50); cell = a.Cell(newest); }
            bool cellOk = cell.px != null && cell.w <= PhotoAlbum.CellW && cell.h <= PhotoAlbum.CellH && cell.px.Length == cell.w * cell.h;
            double cellMs = sw0.Elapsed.TotalMilliseconds;
            // the photo big: its picture and the head's metadata
            a.Want(newest);
            bool big = Wait(x => x.Recent.Count > 0 && x.Recent[0].Thumb != null, 8);
            bool meta = big && s.Recent[0].World == PhotoFake.Meta[2].world && s.Recent[0].Players.Length == PhotoFake.Meta[2].people.Length;
            // a file gone: listed again without it
            File.Delete(gone);
            a.Rescan();
            bool relisted = Wait(x => x.Recent.Count == 5 && !x.Recent.Exists(p => p.Path == gone), 5);
            // off: the pictures let go
            a.Active = false;
            bool letGo = Wait(x => !x.Recent.Exists(p => p.Thumb != null), 3) && a.Cell(newest).px == null;
            Log.Info($"photo album: listed every photo {listed} (want True), newest first {order} (want True); a cell asked for first {askedFirst} (want True), " +
                     $"made {cellOk} (want True) in {cellMs:0} ms; the photo big {big} (want True) with its world and people from its head {meta} (want True); " +
                     $"a deleted file listed again without it {relisted} (want True); switched off, the pictures let go {letGo} (want True)");
            // the bin's checks (never Recycle: a tool moves nothing)
            Directory.CreateDirectory(Path.Combine(root, "VRChat_folder.png"));
            string outside = Path.Combine(dir, "VRChat_2026-01-01_00-00-00.000_640x360.png");
            File.WriteAllBytes(outside, PhotoFake.Png(0, 64, 36));
            string c1 = PhotoBin.Check(outside, root);
            string c2 = PhotoBin.Check(Path.Combine(root, "notes.txt"), root);
            string c3 = PhotoBin.Check(Path.Combine(root, "VRChat_folder.png"), root);
            string c4 = PhotoBin.Check(gone, root);
            string c5 = PhotoBin.Check(Path.Combine(root, "..", "album_folder", "..", Path.GetFileName(outside)), root);
            string c6 = PhotoBin.Check(newest, root);
            File.Delete(outside);
            Log.Info($"photo bin: outside the folder \"{c1}\" (want Not in VRChat's photo folder), not a photo \"{c2}\" (want Not a VRChat photo), a folder " +
                     $"\"{c3}\" (want Not a plain file), gone \"{c4}\" (want The photo is not there any more), a path climbing out \"{c5}\" (want Not in " +
                     $"VRChat's photo folder); a photo of the folder \"{c6 ?? "may be moved"}\" (want may be moved, on a fixed disk with its Recycle Bin on)");
        }
        catch (Exception e) { Log.Error("photo album: " + e); }
        finally
        {
            a.Active = false;
            try { Thread.Sleep(200); if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    // --albumbench (read only: nothing is written, moved or kept): the album on VRChat's real folder - how long the listing takes, then
    // the newest 24 cells asked for as the grid asks (how many came from the shell's thumbnail cache and how fast), and one photo big
    public static int Bench()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var a = new PhotoAlbum();
        a.Active = true;
        PhotoWatcher.Snapshot s = a.Get();
        while (!s.Ready && sw.Elapsed.TotalSeconds < 20) { Thread.Sleep(20); s = a.Get(); }
        double listMs = sw.Elapsed.TotalMilliseconds;
        Log.Info($"albumbench: {s.Recent.Count} photo(s) listed in {listMs:0} ms (folder found {s.FolderFound})");
        int n = Math.Min(24, s.Recent.Count), made = 0;
        sw.Restart();
        var firstAt = new double[n];
        for (int i = 0; i < n; i++) a.Cell(s.Recent[i].Path);
        while (made < n && sw.Elapsed.TotalSeconds < 60)
        {
            made = 0;
            for (int i = 0; i < n; i++)
            {
                var c = a.Cell(s.Recent[i].Path);
                if (c.px != null) { made++; if (firstAt[i] == 0) firstAt[i] = sw.Elapsed.TotalMilliseconds; }
            }
            Thread.Sleep(10);
        }
        Array.Sort(firstAt);
        Log.Info($"albumbench: {made}/{n} cells in {sw.Elapsed.TotalMilliseconds:0} ms (first {(n > 0 ? firstAt[0] : 0):0} ms, median {(n > 0 ? firstAt[n / 2] : 0):0} ms)");
        // each way timed on its own, on the 24th-48th photos (not yet asked: no cache of this process)
        var shell = new List<double>();
        var dec = new List<double>();
        for (int i = n; i < Math.Min(n + 6, s.Recent.Count); i++)
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var th = new Thread(() => { var r = PhotoAlbum.Picture(s.Recent[i].Path, PhotoAlbum.CellW, PhotoAlbum.CellH); });
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            th.Join();
            shell.Add(t0.Elapsed.TotalMilliseconds);
            t0.Restart();
            using (var fs = new FileStream(s.Recent[i].Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
                PhotoWatcher.Decode(fs, PhotoAlbum.CellW, PhotoAlbum.CellH);
            dec.Add(t0.Elapsed.TotalMilliseconds);
        }
        string Ms(List<double> l) => l.Count == 0 ? "-" : string.Join(", ", l.ConvertAll(x => x.ToString("0")));
        Log.Info($"albumbench: a cell the album's way (shell first) {Ms(shell)} ms; the file decoded {Ms(dec)} ms");
        if (s.Recent.Count > 0)
        {
            sw.Restart();
            a.Want(s.Recent[0].Path);
            while (sw.Elapsed.TotalSeconds < 10 && a.Get().Recent[0].Thumb == null) Thread.Sleep(10);
            var p0 = a.Get().Recent[0];
            Log.Info($"albumbench: the newest big in {sw.Elapsed.TotalMilliseconds:0} ms ({p0.TW}x{p0.TH}, world known {p0.World != null}, {p0.Players.Length} people)");
        }
        a.Active = false;
        return 0;
    }
}
