# OrbitOverlay

**A wrist watch, your desktop and a keyboard - floating in SteamVR.**

OrbitOverlay (formerly Orbit) is a lightweight SteamVR overlay for Windows, in the spirit of XSOverlay and OVR Toolkit:
a watch on your wrist, your monitors (or single app windows) as curved screens around you with a
laser mouse, a full VR keyboard, and its own settings panel you can grab and move in VR.

![OrbitOverlay settings - Look tab](docs/img/settings-look.png)

> 🤖 **Made with AI.** OrbitOverlay was written primarily by an AI assistant (Anthropic's Claude, via Claude Code)
> working from a human's requests and testing in VR. It's shared in good faith - please use it with the
> same care you'd give any community software.

## Features

**Wrist watch** - turn your wrist towards you to see it.
- Watch faces: full, analog, minimal, time only, or **custom**; 12 / 24-hour clock
- **Custom face**: put widgets anywhere on a 6 x 4 grid - time, date, calendar, analog clock, timer, stopwatch,
  time in VR, batteries, now playing, volume, performance gauges, frame graph, CPU, memory and every watch button,
  most in several sizes. Arrange them in Settings > Wrist > Edit layout (drag to move, drag the corner to resize),
  start from your old face or from "Corners". Tap a widget to open it big, with more to see and to control (cover,
  controls and volume; every battery; the month; timer presets; laps; the frame graph with averages; CPU history).
  In the Modern style values are big numbers over a plain line in your accent colour, with motion: widgets cascade in when you
  look at the watch, open with a spring, digits roll; in the Windows 95 style they are little 95 windows
- Frame rate, reprojection, GPU frame time and VRAM - as numbers or a live frame-time graph
- Batteries of the headset, controllers and trackers
- Now playing (Spotify, browsers, any Windows media session) with previous / play-pause / next
- Buttons: keyboard, monitors on/off, recenter, layout profile, night mode, settings, theater mode

![Watch faces](docs/img/watch-faces.png)

![Custom watch face](docs/img/watch-widgets.png)

**Desktop in VR**
- Every monitor as its own panel; first start joins them into one curved screen
- Mirror **single app windows** instead of whole monitors (browser, Discord, video player...) -
  they follow the window on your PC, and a click brings it to the front first
- Laser mouse: trigger = click, B / Y = right click, stick = scroll; smooth, low-latency cursor
- Grip = grab and move; stick = distance / bend; both hands = resize; panels snap into rows
- Adjustable size, opacity and curvature; attach to the world, a hand or your head
- Smart frame rate: what you look at runs at the headset's refresh rate, the rest is throttled
- **Theater mode** (a watch button): the monitor or window you used last becomes one big curved screen in front
  of you, the other panels and the keyboard step aside and the world dims behind it. Grab it to move it, the stick
  or both hands to size it; tap again and everything glides back to where it was. It remembers the size.
  Point at the screen and a switcher shows up under it: one button per monitor and window to swap what plays.
  With **Auto theater** on (Settings > Layouts), a video going full screen on a monitor (a browser, VLC, mpv, MPC,
  PotPlayer, Movies & TV) starts it there by itself, and leaving full screen ends it - games never do

**Stream dock (Hexpad)** - for an Ajazz AKP03 stream dock run by the Hexpad app
- The dock as a panel you can place over the real one on your desk: its six keys exactly as they look right
  now (pages, folders, live tiles, the idle screen), its round buttons, and its three knobs as sliders
- The laser presses keys and buttons (holds and double taps work as on the dock), drags the sliders (volume,
  brightness... set directly) and pushes the knobs with the button at a slider's end; anything that could end
  your session (lock, sleep, empty the Recycle Bin) asks first
- A "Hexpad" watch widget shows the keys live; a tap opens the panel. Without Hexpad none of this shows up

**VR keyboard**
- Follows your Windows keyboard layout, with a built-in English (US) layout on the globe key
- Shortcut bar (select all, copy, paste, cut, undo, redo), sticky modifiers, key repeat, click sounds
- Pops up by itself when a click in VR lands in a text field, and goes away afterwards
- **VRChat chatbox mode**: type a line and send it to the in-game chatbox over OSC

![Keyboard](docs/img/keyboard.png)

**VRChat**
- **Who is in the room**: a panel with everyone in your instance - friends (from VRCX, if you use it) and
  people you star first - and who left lately; a card when a friend or a starred person comes or goes.
  Arriving in a world gives one card for everyone you know who is there, not one per person. A "Room" watch
  widget shows how many are there. Read from VRChat's own log; nothing is sent anywhere
- **Status line**: what you are listening to (and, if you like, the time and your time in VR) as your chatbox
  text over your head, kept up to date; switch it on from the watch or Settings > VRChat (needs OSC on in VRChat)
- **No stray clicks in the game**: while a laser points at an OrbitOverlay panel, the game doesn't get that
  hand's trigger, grip, stick or B / Y (SteamVR's overlay input priority) - switchable in Settings > VRChat

**And also**
- **Layout profiles** (Desk / Bed / Couch): each remembers where everything was - switch from the watch;
  lying in bed, the screens come above you
- **Night mode**: dims every panel, one tap on the watch
- Themes and any accent colour, panel colour and opacity, laser colour / thickness
- **Windows 95 style** (Settings > Look > Style): the settings, the watch, the keyboard and the notifications
  as real 1995 Windows - an "OrbitOverlay Properties" property sheet, a Date/Time window on your wrist, MS Sans Serif,
  3D bevels, drop-down lists - drawn at their original pixel size and enlarged without smoothing
- **Notifications** in XSOverlay's format (UDP 42069) - e.g. from VRCX
- **VRChat avatar parameters** compatible with XSOverlay (works with DXOverlay and similar avatar add-ons)
- Double-tap both grips to hide / show everything (OVR Toolkit style)
- Starts and stops with SteamVR; runs as administrator so the laser mouse also works in admin windows
- Settings as a floating panel in VR (open it from the watch) and as a desktop window (tray icon)
- **Check for updates** button (Settings > General): an installed copy updates itself from GitHub releases

![Windows 95 style](docs/img/windows95.png)

## Download

Get **`OrbitOverlaySetup.exe`** from the [latest release](https://github.com/Erik1028/OrbitOverlay/releases/latest) and run it.
It installs for your user only (no admin needed), adds a Start menu entry, and can start OrbitOverlay together
with SteamVR. Uninstall from Windows Settings > Apps.

- The installer is not code-signed, so Windows SmartScreen may warn: *More info > Run anyway*.
- The first start asks once for administrator rights (so the VR mouse can click into admin windows);
  you can say No - everything else still works.
- `OrbitOverlay Quick Start.txt` next to the installed app explains the controls. Updating from Orbit 1.6 or
  older moves the install over by itself (Orbit.exe becomes OrbitOverlay.exe, shortcuts follow, settings stay).

**Requirements:** Windows 10 (2004+) or 11, a DirectX 11 GPU, SteamVR. Overlays show while the game runs
through SteamVR (Virtual Desktop users: pick SteamVR as the runtime, not VDXR / OpenXR).

## Controls

| In VR | Does |
|---|---|
| Point at a panel | laser; on a monitor or window it moves the real cursor |
| Trigger | click |
| B / Y | right click |
| Stick up / down | scroll |
| Grip on a panel | grab and move (stick: distance / bend, other hand's grip: resize) |
| Both grips, twice quickly | hide / show monitors and keyboard |
| Look at your left wrist | the watch (point at it with the other hand) |

## Privacy

No account, no telemetry. OrbitOverlay talks to SteamVR, to VRChat over OSC on `127.0.0.1`, and listens for
notifications on `127.0.0.1:42069`. The only internet request is the GitHub release check when you press
*Check for updates*. Settings and logs stay in `%LOCALAPPDATA%\Orbit`.

## Building

.NET 8 SDK on Windows:

```
dotnet build -c Release
powershell -File build_release.ps1      # -> release\OrbitOverlaySetup.exe (+ OrbitSetup.exe for old updaters)
```

Developer switches (`--snapshot`, `--selftest`, `--raytest`, `--leaktest`, `--focustest`, ...) and the
design notes are documented (in Hungarian) in [docs/NOTES.hu.md](docs/NOTES.hu.md).

Built with [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (Direct3D 11, Direct2D,
DirectWrite, XAudio2), Valve's [OpenVR](https://github.com/ValveSoftware/openvr) and Windows.Graphics.Capture.

## License

MIT - see [LICENSE](LICENSE). The OpenVR API files in `third_party/openvr` are Valve's, under their
BSD 3-clause license. Virtual monitors (Settings > Desktops) use the
[Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (MIT, signed by SignPath
Foundation): OrbitOverlay downloads its release 25.7.23 only when you press Add, checks the file's SHA-256,
and Windows asks you about the driver's publisher before installing it.
