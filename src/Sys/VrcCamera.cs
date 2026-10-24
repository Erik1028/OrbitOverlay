namespace Orbit;

// The watch's camera button (Erik 10-24: a shutter on the watch for a VRChat photo, if VRChat lets OSC do it). It does, since
// VRChat 2025.3.3 (docs.vrchat.com/docs/vrchat-202533, "OSC endpoints for interacting with the camera"): /usercamera/Capture takes
// a photo. An action, so it follows VRChat's rules for buttons (wiki.vrchat.com/wiki/OSC): true is "pressed", then false - VRChat
// takes the next press only after a false. To VRChat's OSC port (General's, as the chatbox's lines go), 127.0.0.1.
// It takes the photo with the camera that is open in VRChat. Nothing opens it: VRChat has no OSC action for that (Close only; a
// Mode sent while the camera is closed does nothing - VRChat's feature request 1707 asks for one), so nothing else is sent - not
// its mode, not its zoom, nothing of its settings. The photo lands in VRChat's Pictures folder and the latest-photo widget shows it
// by itself (Sys/PhotoWatcher.cs).
public sealed class VrcCamera
{
    public const string Id = "camera";                       // the watch widget (WatchWidgets.Kinds)
    public const string Capture = "/usercamera/Capture";
    public const double Release = 0.2;                       // the false after the true: a few of VRChat's frames later, even at 20 fps

    readonly OscSender _osc;
    int _port;
    double _releaseAt = -1;                                  // < 0: nothing pressed

    public int Shots { get; private set; }                   // taps that went out (--snapshot's camera check)

    public VrcCamera(OscSender osc) { _osc = osc; }

    // a tap: the press now, its release in Release seconds. A second tap before that is left out - the press is still down, and a
    // false then a true in one of VRChat's frames could be read as no press at all.
    public void Shutter(double now, int port)
    {
        if (_releaseAt >= 0) return;
        _port = port;
        _osc.SendMessage(port, Capture, true);
        _releaseAt = now + Release;
        Shots++;
        Log.Info($"camera: the shutter to VRChat ({Capture} on port {port})");
    }

    // every frame (one compare when nothing is pressed): the release when it is due
    public void Update(double now)
    {
        if (_releaseAt < 0 || now < _releaseAt) return;
        _releaseAt = -1;
        _osc.SendMessage(_port, Capture, false);
    }
}
