using System;
using System.Collections.Generic;

namespace Orbit;

// SlimeVR's side of the app (Sys/SlimeVrLink.cs, Panels/WristSlimeVr.cs): its server is asked about the trackers only while a SlimeVR
// widget is on a watch page and the watch can be seen (SteamVR runs and the watch shows, or the settings show the watch) - kept 20 s after
// the watch went away, so a glance off and back does not connect again (SlimeVR logs every connection) - or while its alerts are on and
// SteamVR runs; else nothing is open. A reset goes out from the watch's second tap (OnSlimeReset, set in the constructor).
// The alerts (Erik 10-24; Sys/SlimeAlerts.cs, VRChat > SlimeVR alerts - never switched: on while a SlimeVR widget is on a page): the link
// stays open whenever SteamVR runs (its one small update a second), and while the headset is on a tracker's low battery, a tracker lost
// (and back) come as cards with a buzz in the watch's hand. Lying down (the head lies ~3 s, MathUtil.IsLying) with SlimeVR connected: the
// widget puts its Yaw reset first (PanelData.SlimeLying) - and with the alerts on, a card offers it once per lying-down, its button asking
// first like the widget's. Nothing is ever reset by itself: only the watch's or the card's second tap sends one.
public sealed partial class OrbitApp
{
    readonly SlimeVrLink _slime = new();
    readonly SlimeAlerts _slimeAlerts = new();
    readonly List<SlimeAlerts.Note> _slimeNotes = new();
    Func<bool> _slimeYaw;                        // the card's second tap (made once)
    double _slimeSeen = -100;

    void UpdateSlimeVr(double now)
    {
        bool want = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "slimevr");
        if (want && ((VrAfter(10) && _wrist?.Alpha > 0.02f) || now - _photoPreview < 3)) _slimeSeen = now;
        bool alerts = (C.SlimeVr?.Alerts ?? want) && VrAfter(10);          // (SlimeAlerts.On, its widget test already made)
        _slime.Active = (want && now - _slimeSeen < 20) || alerts;
        var s = _slime.Get();
        _data.SlimeVr = want ? s : null;
        // lying down a while with SlimeVR's trackers there: the widget's Yaw reset first; with the alerts on, a card once per lying-down
        bool connected = s.State == SlimeVrLink.State.Ok && s.Dialect != SolarXr.Dialect.Unknown && s.Connected > 0;
        bool lying = _slimeAlerts.Lie(now, _vr.Connected && _vr.HmdValid && MathUtil.IsLying(_vr.Hmd, _vr.OnFace), connected, out bool offer);
        _data.SlimeLying = lying && connected;
        if (!alerts) { _slimeAlerts.Forget(); return; }
        if (!_awake) return;                         // (the cards are for your eyes: with the headset off nothing moves on)
        if (offer)
        {
            _notifyIn.Incoming.Enqueue(SlimeAlerts.YawCard(_slimeYaw ??= () => _slime.Reset(SlimeVrLink.ResetKind.Yaw)));
            SlimeBuzz(false);
            Log.Info("slimevr: lying down - the Yaw reset offered on a card");
        }
        _slimeNotes.Clear();
        _slimeAlerts.Trackers(now, s, _slimeNotes);
        for (int i = 0; i < _slimeNotes.Count; i++)
        {
            var n = _slimeNotes[i];
            _notifyIn.Incoming.Enqueue(SlimeAlerts.Card(n));
            if (n.Kind != SlimeAlerts.Kind.Back) SlimeBuzz(twice: n.Kind == SlimeAlerts.Kind.Lost);
            Log.Info($"slimevr alert: {n.Name} {(n.Kind == SlimeAlerts.Kind.Low ? $"battery {n.Battery} % (under {SlimeAlerts.LowPct} %)" : n.Kind == SlimeAlerts.Kind.Lost ? $"lost (status {n.Status})" : "back")}");
        }
    }

    // the watch's hand buzzes like it does for a friend - twice for a tracker lost (the second pulse a moment later: UpdateRoom)
    void SlimeBuzz(bool twice)
    {
        if (!_vr.Connected || !C.General.Haptics) return;
        _vr.Haptic(WatchHand, 0.7f, 0.05f);
        if (twice) _buzzAgainAt = Clock.Now + 0.16;
    }
}
