namespace Orbit;

// The watch's camera button (Erik 10-24: a shutter on the watch for a VRChat photo, if VRChat lets OSC do it). It does, since
// VRChat 2025.3.3 (docs.vrchat.com/docs/vrchat-202533, "OSC endpoints for interacting with the camera"): /usercamera/Capture takes
// a photo. An action, so it follows VRChat's rules for buttons (wiki.vrchat.com/wiki/OSC): true is "pressed", then false - VRChat
// takes the next press only after a false. To VRChat's OSC port (General's, as the chatbox's lines go), 127.0.0.1.
// It takes the photo with the camera that is open in VRChat. Nothing opens it: VRChat has no OSC action for that (Close only; a
// Mode sent while the camera is closed does nothing - VRChat's feature request 1707 asks for one), so nothing else is sent - not
// its mode, not its zoom, nothing of its settings. The photo lands in VRChat's Pictures folder and the latest-photo widget shows it
// by itself (Sys/PhotoWatcher.cs).
// Whether the camera is open (Erik 10-24: the widget should know it): /usercamera/Mode is read / write (VRChat 2025.3.3's notes:
// "All of the new endpoints have read/write access"; wiki.vrchat.com/wiki/OSC: an int, 0 Off, 1 Photo, 2 Stream, 3 Emoji,
// 4 Multilayer, 5 Print, 6 Drone). VRChat sends the camera's values to its OSC output as they change (Pose and Zoom were seen coming
// there: github.com/azumag/streamcaster pull 55), but that port is Hexpad's (9001, never bound here); its OSCQuery service answers
// them too, as they are now - seen 10-24 with the camera closed:
// {"FULL_PATH":"/usercamera/Mode","ACCESS":3,"TYPE":"i","VALUE":[0]}. So the avatar link's thread asks the service for it, beside
// the avatar's values (AvatarLink.CameraMode, Sys/AvatarToggles.cs). Read only: Mode is never sent. Not known (no OSCQuery yet, OSC
// off, a VRChat before 2025.3.3): the button works as it always did. Closed: a tap sends nothing (the watch greys the button and
// says where to open it, Panels/WristCamera.cs).
public sealed class VrcCamera
{
    public const string Id = "camera";                       // the watch widget (WatchWidgets.Kinds)
    public const string Capture = "/usercamera/Capture";
    public const string ModePath = "/usercamera/Mode";
    public const int Unknown = -1, Off = 0;                  // Mode: not known (nothing read yet), closed; 1.. a mode of the open camera
    public const double Release = 0.2;                       // the false after the true: a few of VRChat's frames later, even at 20 fps

    readonly OscSender _osc;
    int _port;
    double _releaseAt = -1;                                  // < 0: nothing pressed

    public int Shots { get; private set; }                   // taps that went out (--snapshot's camera check)
    public int Refused { get; private set; }                 // taps while the camera was closed: nothing sent

    // the camera's mode as VRChat's OSCQuery said it last (Unknown: no link, or nothing read yet)
    public AvatarLink Link;
    public int Mode => Link?.CameraMode ?? Unknown;
    public bool Closed => Mode == Off;

    public VrcCamera(OscSender osc) { _osc = osc; }

    // the open camera's mode in words (drawn through the language's table); a mode a later VRChat added: just "Camera"
    public static string ModeName(int mode) => mode switch
    {
        1 => "Photo", 2 => "Stream", 3 => "Emoji", 4 => "Multilayer", 5 => "Print", 6 => "Drone", _ => "Camera",
    };

    // a tap: the press now, its release in Release seconds. A second tap before that is left out - the press is still down, and a
    // false then a true in one of VRChat's frames could be read as no press at all. The camera closed: nothing at all (VRChat would
    // drop it - and Mode, the one way to open it, does nothing while it is closed); the link asks VRChat again now, in case it was
    // opened since the last read.
    public void Shutter(double now, int port)
    {
        if (Closed)
        {
            Refused++;
            Link?.CameraNow();
            Log.Info("camera: VRChat's camera is closed - nothing sent");
            return;
        }
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
