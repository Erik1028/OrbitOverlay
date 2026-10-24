using System;

namespace Orbit;

// The temperatures' side of the app (Sys/Temps.cs, Panels/WristTemps.cs): the sensors are read (every 2 s, on their own thread) only
// while a temperatures widget is on a watch page and the watch can be seen, or while General > Alerts > GPU / CPU hot is on and SteamVR
// runs - else the thread sleeps and nothing is opened. The alert itself: Sys/Alerts.cs (a card and a buzz in the watch hand).
public sealed partial class OrbitApp
{
    readonly TempSensors _temps = new();

    void UpdateTemps(double now)
    {
        var a = C.General.Alerts ?? AlertDefaults;
        bool widget = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "temps") && (VrAfter(6) || now - _photoPreview < 3);
        _temps.Active = widget || (a.Hot && VrAfter(6));
        _data.Temps = _temps.Active ? _temps.Report : null;
    }

    // from UpdateAlerts (OrbitApp.Extras.cs), every frame: a reading of the last 6 s with the headset on counts
    void UpdateHotAlert(double now, bool awake, AlertsConfig a)
    {
        var t = _data.Temps;
        bool valid = a.Hot && awake && t != null && now - t.At < 6;
        int which = _alerts.Hot(now, valid, t?.Gpu ?? float.NaN, t?.Cpu ?? float.NaN, a.HotLimit);
        if (which == 0) return;
        float c = which == 1 ? t.Gpu : t.Cpu;
        _notifyIn.Incoming.Enqueue(Alerts.HotCard(which == 1, c, a.HotLimit));
        AlertBuzz(WatchHand);
        Log.Info($"alert: {(which == 1 ? "GPU" : "CPU")} at {c:0} °C, at or over {a.HotLimit} °C for {Alerts.HotFor:0} s");
    }
}
