namespace Orbit;

// The avatar toggles' side of the app (Sys/AvatarToggles.cs, Panels/WristAvatar*.cs): VRChat is asked about the avatar worn only while
// an avatar widget is on a watch page and the watch can be seen (SteamVR runs, or the settings show the watch - the editor's picks need
// the avatar's buttons too) - else its thread sleeps. A tap goes out through an OSC sender of its own (VRChat's port may be another
// than the one the XSOverlay-style parameters use: one sender for both would forget what it sent each time the port changed).
public sealed partial class OrbitApp
{
    readonly AvatarLink _avatar = new();
    readonly OscSender _avatarOsc = new();

    void UpdateAvatar(double now)
    {
        bool want = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "avatar") && (_vr.Connected || now - _photoPreview < 3);
        _avatar.Send ??= (address, value) => _avatarOsc.SendMessage(_avatar.SendPort, address, value);
        _avatar.OscPort = C.General.OscPort;
        _avatar.Active = want;
        _avatar.Watched = want && (_wrist?.Alpha > 0.02f || now - _photoPreview < 3);
        if (want) _avatar.Flush(now);                // a slider's last value
        _data.Avatar = want ? _avatar : null;
    }

    void AvatarDispose()
    {
        _avatar.Dispose();
        _avatarOsc.Dispose();
    }
}
