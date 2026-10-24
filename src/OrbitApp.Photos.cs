using System;
using System.Threading;

namespace Orbit;

// The latest photo widget's side of the app (Sys/PhotoWatcher.cs, Panels/WristPhoto.cs): VRChat's photos are followed only while a
// latest photo widget is on a watch page and the watch can be seen (SteamVR runs, or the settings show the watch's preview) - else
// the watcher is off, its thread asleep, no thumbnail kept. A new photo is noted in Orbit's own notifications (the notifications
// widget's list, no card): "Photo saved · <world>" once VRCX's metadata says the world, or after 6 s without it. A tap on Open in VR
// Gallery opens it there (PhotoGallery: VR Gallery, else Windows' viewer). The photo view's gallery lists every photo (PhotoAlbum) only
// while it is open and the watch can be seen; its Delete moves a photo into the Recycle Bin (PhotoBin) on a thread of its own, then
// the watcher, On this day and the album drop it at once. Nothing here allocates at rest.
public sealed partial class OrbitApp
{
    readonly PhotoWatcher _photos = new();
    readonly PhotoAlbum _album = new();
    PhotoWatcher.Snapshot _photoSnap;
    string _photoNoted, _photoPending;                    // the newest photo noted / waiting for its world
    double _photoPendingAt, _photoPreview = -10;

    void UpdatePhotos(double now)
    {
        bool want = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "photo") && (VrAfter(3) || now - _photoPreview < 3);
        if (want || _memories.Active) UpdateVrg(now);
        _photos.Active = want;
        if (!want) { _data.Photos = null; _photoPending = null; return; }
        var s = _photos.Get();
        _data.Photos = s;
        if (!ReferenceEquals(s, _photoSnap))
        {
            _photoSnap = s;
            if (s.Recent.Count > 0 && s.Recent[0] is { Fresh: true } p && p.Path != _photoNoted && p.Path != _photoPending)
            {
                _photoPending = p.Path;
                _photoPendingAt = now;
            }
        }
        if (_photoPending == null) return;
        PhotoWatcher.Photo shot = null;
        foreach (var x in s.Recent) if (x.Path == _photoPending) { shot = x; break; }
        if (shot == null) { _photoPending = null; return; }
        if (shot.World == null && now - _photoPendingAt < 6) return;
        _notes.Add("VRChat", shot.World != null ? Lang.F("Photo saved · {0}", shot.World) : Lang.T("Photo saved"), "", shot.Taken);
        _photoNoted = _photoPending;
        _photoPending = null;
    }

    void UpdateAlbum(double now)
    {
        bool want = _wrist != null && _wrist.GalleryOn && (VrAfter(3) || now - _photoPreview < 3);
        _album.Active = want;
        _data.Album = want ? _album.Get() : null;
    }

    // the watch's Delete (its second tap): into the Recycle Bin - checked and moved by PhotoBin, never deleted for good - then every
    // list of photos drops it; done(null), or done(why not), from that thread
    void DeletePhoto(string path, Action<string> done)
    {
        var t = new Thread(() =>
        {
            string why;
            try { why = PhotoBin.Recycle(path, PhotoWatcher.PictureFolder()); }
            catch (Exception e) { Log.Warn("photo bin: " + e.Message); why = "Windows did not move it"; }
            if (why == null)
            {
                _photos.Recheck();
                _memories.Drop(path);
                _album.Rescan();
            }
            else Log.Info("photo bin: not moved - " + why);
            done(why);
        }) { IsBackground = true, Name = "photo bin" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    // VR Gallery's settings (read only, every 20 s while a photo widget is in use): whether the photo view may offer Send, how long a slide stays
    double _vrgNext;
    void UpdateVrg(double now)
    {
        if (now < _vrgNext) return;
        _vrgNext = now + 20;
        System.Threading.Tasks.Task.Run(() =>
        {
            var s = VrGalleryData.ReadSettings();
            _wrist.SendReady = s != null && s.Api && s.Webhook && !string.IsNullOrEmpty(s.Token);
            _wrist.SlideSeconds = s?.SlideSeconds ?? 4;
        });
    }

    // the photo view's Send (its second tap): VR Gallery sends its newest photo to its Discord webhook - done(null) or done(why), from that thread
    void SendNewestPhoto(Action<string> done)
    {
        new Thread(() =>
        {
            string why;
            try { why = VrGalleryData.SendNewest(VrGalleryData.ReadSettings()); }
            catch (Exception e) { Log.Warn("vr gallery: " + e.Message); why = "VR Gallery did not send it"; }
            done(why);
        }) { IsBackground = true, Name = "vr gallery send" }.Start();
    }

    // the photo view's zoom: the photo at 1600 px (the shell's big thumbnail - an STA thread -, else the file), then the panel's Hi
    void WantHiPhoto(string path)
    {
        var t = new Thread(() =>
        {
            try
            {
                var (px, w, h) = PhotoAlbum.Picture(path, 1600, 1000);
                if (px != null) _wrist.Hi = new WristPanel.HiPicture { Path = path, Px = px, W = w, H = h };
            }
            catch (Exception e) { Log.Info("photo zoom: " + e.Message); }
        }) { IsBackground = true, Name = "photo zoom" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }
}
