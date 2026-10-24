using System;

namespace Orbit;

// SlimeVR's side of the app (Sys/SlimeVrLink.cs, Panels/WristSlimeVr.cs): its server is asked about the trackers only while a SlimeVR
// widget is on a watch page and the watch can be seen (SteamVR runs and the watch shows, or the settings show the watch) - kept 20 s after
// the watch went away, so a glance off and back does not connect again (SlimeVR logs every connection) - else nothing is open. A reset
// goes out from the watch's second tap (OnSlimeReset, set in the constructor).
public sealed partial class OrbitApp
{
    readonly SlimeVrLink _slime = new();
    double _slimeSeen = -100;

    void UpdateSlimeVr(double now)
    {
        bool want = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "slimevr");
        if (want && ((VrAfter(10) && _wrist?.Alpha > 0.02f) || now - _photoPreview < 3)) _slimeSeen = now;
        _slime.Active = want && now - _slimeSeen < 20;
        _data.SlimeVr = want ? _slime.Get() : null;
    }
}
