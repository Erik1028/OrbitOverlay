using System;
using System.Collections.Generic;
using System.Linq;

namespace Orbit;

// 1.14 in the main loop: B / Y twice (Vr/DoublePress.cs), the alerts (Sys/Alerts.cs), the per-game rules (Sys/GameRules.cs).
// One call a frame from Tick, after the lasers and before BlockGameInput: the gesture must see the hand's block as it was when this
// frame's buttons were read. Nothing here allocates at rest (a card is made only when one is shown).
public sealed partial class OrbitApp
{
    readonly DoublePress _double = new();
    readonly Alerts _alerts = new();
    readonly List<BatteryDevice> _alertFire = new();
    double _alertRead = -1;
    readonly SceneApp _scene = new();
    readonly GameRuleEngine _gameRules = new();
    static readonly AlertsConfig AlertDefaults = new();

    void UpdateExtras(double now, bool awake, bool dash)
    {
        QuickGesture(now, awake && !dash);
        UpdateAlerts(now, awake, dash);
        _scene.Poll(now);
        if (_gameRules.Step(now, _scene.Key, C.GameRules) is { } rule) ApplyGameRule(rule);
    }

    void ExtrasReset()
    {
        _double.Reset();
        _alerts.Reset();
        _alertRead = -1;
        _scene.Reset();
        _gameRules.Reset();
    }

    // ---- B / Y twice ----

    void QuickGesture(double now, bool on)
    {
        string mode = DoublePress.Mode(C.General.DoublePress);
        if (!on || mode == "off" || _lasers == null) { _double.Reset(); return; }
        for (int h = 0; h < 2; h++)
        {
            ref var hs = ref _vr.Hands[h];
            var t = _lasers[h].Hovered;
            // Orbit's own panels only (not a monitor / window: B / Y is their right click), and the game already kept from this hand;
            // not while the eye calibration's board shows (its B / Y cancels it - not half of a double press, 10-24 review)
            bool eligible = !_eyeCalib.Showing && DoublePress.Eligible(C.General.BlockGameInput, _vr.Suppress[h], t, hs.Grab);
            if (!_double.Update(h, hs.SecondaryPressed, eligible, now)) continue;
            if (C.General.Haptics) _vr.Haptic((Hand)h, 0.45f, 0.03f);
            if (mode == "watch")
            {
                C.Wrist.AlwaysVisible = !C.Wrist.AlwaysVisible;
                Log.Info($"B / Y twice: the watch {(C.Wrist.AlwaysVisible ? "pinned" : "let go")}");
                _cfg.MarkDirty();
                _win.Dirty = true;
            }
            else
            {
                ToggleKeyboard();
                Log.Info($"B / Y twice: keyboard {(C.Keyboard.Visible ? "up" : "down")}");
            }
        }
    }

    // ---- the alerts ----

    void UpdateAlerts(double now, bool awake, bool dash)
    {
        var a = C.General.Alerts ?? AlertDefaults;
        var v = _data.Vr;
        // the batteries: once per reading (VrStats reads them once a second), only with the headset on (the card is for your eyes)
        if (a.Battery && awake && v.ReadAt != _alertRead)
        {
            _alertRead = v.ReadAt;
            _alertFire.Clear();
            float threshold = Math.Clamp(a.BatteryPct, 1, 95) / 100f;
            _alerts.Battery(now, v.Devices, threshold, _alertFire);
            foreach (var d in _alertFire)
            {
                int trackerNo = 0;
                if (d.Kind == BatteryDevice.Tracker)
                    for (int i = 0, k = 0; i < v.Devices.Count; i++)
                        if (v.Devices[i].Kind == BatteryDevice.Tracker) { k++; if (v.Devices[i].Index == d.Index) trackerNo = k; }
                _notifyIn.Incoming.Enqueue(Alerts.BatteryCard(d, trackerNo, (int)Math.Round(threshold * 100)));
                Hand hand = d.Kind == BatteryDevice.Left ? Hand.Left : d.Kind == BatteryDevice.Right ? Hand.Right : WatchHand;
                AlertBuzz(hand);
                Log.Info($"alert: {Alerts.DeviceName(d, trackerNo)} battery {d.Pct * 100:0} % (under {threshold * 100:0} %)");
            }
        }
        // the frame rate: a game running, the headset on, no dashboard, not loading, measured in the last 1.5 s
        bool valid = a.Fps && awake && !dash && v.ScenePid != 0 && !v.AppStalled && v.AppFps >= 0 && now - v.FpsAt < 1.5 && v.DisplayHz > 1;
        float limit = v.DisplayHz * Math.Clamp(a.FpsPct, 10, 100) / 100f;
        if (_alerts.Fps(now, valid, v.AppFps < limit))
        {
            _notifyIn.Incoming.Enqueue(Alerts.FpsCard(v.AppFps, v.DisplayHz, a.FpsPct));
            AlertBuzz(WatchHand);
            Log.Info($"alert: frame rate {v.AppFps:0} fps under {limit:0} ({a.FpsPct} % of {v.DisplayHz:0} Hz) for {Alerts.LowFor:0} s");
        }
        UpdateHotAlert(now, awake, a);       // the GPU / CPU hot (OrbitApp.Temps.cs)
    }

    Hand WatchHand => C.Wrist.Hand == "right" ? Hand.Right : Hand.Left;

    void AlertBuzz(Hand h)
    {
        if (_vr.Connected && C.General.Haptics) _vr.Haptic(h, 0.8f, 0.15f);
    }

    // ---- per game ----

    void ApplyGameRule(GameRule r)
    {
        var did = new List<string>();
        if (r.Layout != null && r.Layout != C.ActiveLayout && C.Layouts.Any(l => l.Name == r.Layout))
        {
            SwitchLayout(r.Layout);
            did.Add(Lang.F("the {0} layout", Lang.T(r.Layout)));
        }
        var w = C.Wrist;
        if (r.Page is int p && w.Face == "custom" && p >= 0 && p < WatchPages.Count(w) && p != _wrist.PageNow)
        {
            _wrist.GoToPage(p, animate: true);
            did.Add(Lang.F("the {0} page", WatchPages.Label(w, p)));
        }
        string app = _scene.Name ?? SceneApp.Label(r);
        Log.Info($"per game: {app} started -> {(did.Count == 0 ? "nothing to change" : string.Join(", ", did))}");
        if (did.Count == 0) return;
        _notifyIn.Incoming.Enqueue(new Notification
        {
            Source = "OrbitOverlay", Title = Lang.F("{0} started", app), Content = Lang.F("Switched to {0}.", string.Join(Lang.T(" and "), did)), Timeout = 4, Volume = 0.4f,
        });
        _cfg.MarkDirty();
        _win.Dirty = true;
    }

    // IOrbitHost: what Layouts > Per game offers to add; a card (the in-VR export says where the file went)
    public (string key, string name) RunningApp => _vr.Connected ? (_scene.Key, _scene.Name) : (null, null);

    public void Notify(string title, string text) =>
        _notifyIn.Incoming.Enqueue(new Notification { Source = "OrbitOverlay", Title = title, Content = text, Timeout = 6 });
}
