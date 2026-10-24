using System;

namespace Orbit;

// The latest photo widget's side of the app (Sys/PhotoWatcher.cs, Panels/WristPhoto.cs): VRChat's photos are followed only while a
// latest photo widget is on a watch page and the watch can be seen (SteamVR runs, or the settings show the watch's preview) - else
// the watcher is off, its thread asleep, no thumbnail kept. A new photo is noted in Orbit's own notifications (the notifications
// widget's list, no card): "Photo saved · <world>" once VRCX's metadata says the world, or after 6 s without it. A tap on Open in VR
// Gallery opens it there (PhotoGallery: VR Gallery, else Windows' viewer). Nothing here allocates at rest.
public sealed partial class OrbitApp
{
    readonly PhotoWatcher _photos = new();
    PhotoWatcher.Snapshot _photoSnap;
    string _photoNoted, _photoPending;                    // the newest photo noted / waiting for its world
    double _photoPendingAt, _photoPreview = -10;

    void UpdatePhotos(double now)
    {
        bool want = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "photo") && (VrAfter(3) || now - _photoPreview < 3);
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
}
