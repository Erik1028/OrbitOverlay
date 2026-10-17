using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using Valve.VR;

namespace Orbit;

// --ovdump <seconds>: what SteamVR has for the running OrbitOverlay's panels - visible, where (and how far from the
// headset), width, bend, alpha, sort order, texture size - logged whenever it changes. Read-only: it creates no
// overlay and changes nothing.
static class OverlayDump
{
    public static int Run(double seconds)
    {
        var err = EVRInitError.None;
        OpenVR.Init(ref err, EVRApplicationType.VRApplication_Overlay);
        if (err != EVRInitError.None) { Log.Error("ovdump: " + err); return 2; }
        try
        {
            var keys = new List<string> { "erik.orbit.desktop.0", "erik.orbit.desktop.1", "erik.orbit.desktop.2", "erik.orbit.desktop.3",
                "erik.orbit.theater.dim", "erik.orbit.theater.bar", "erik.orbit.theater.glow", "erik.orbit.kb" };
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Log.Dir, "config.json")));
                if (doc.RootElement.TryGetProperty("Windows", out var ws))
                    foreach (var w in ws.EnumerateArray())
                        if (w.TryGetProperty("Id", out var id)) keys.Add("erik.orbit.window." + id.GetString());
            }
            catch (Exception e) { Log.Warn("ovdump: config: " + e.Message); }
            var ov = OpenVR.Overlay;
            var poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            var last = new Dictionary<string, string>();
            double t0 = Clock.Now;
            Log.Info($"ovdump: watching {keys.Count} overlays for {seconds:0} s");
            while (Clock.Now - t0 < seconds)
            {
                OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);
                var hmd = MathUtil.FromHmd(poses[0].mDeviceToAbsoluteTracking).Translation;
                foreach (var k in keys)
                {
                    ulong h = 0;
                    if (ov.FindOverlay(k, ref h) != EVROverlayError.None) { Note(last, k, "not found"); continue; }
                    bool vis = ov.IsOverlayVisible(h);
                    float a = 0, w = 0, c = 0;
                    uint so = 0, tw = 0, th = 0;
                    ov.GetOverlayAlpha(h, ref a);
                    ov.GetOverlayWidthInMeters(h, ref w);
                    ov.GetOverlayCurvature(h, ref c);
                    ov.GetOverlaySortOrder(h, ref so);
                    ov.GetOverlayTextureSize(h, ref tw, ref th);
                    var tt = VROverlayTransformType.VROverlayTransform_Absolute;
                    ov.GetOverlayTransformType(h, ref tt);
                    string where;
                    if (tt == VROverlayTransformType.VROverlayTransform_Absolute)
                    {
                        var origin = ETrackingUniverseOrigin.TrackingUniverseStanding;
                        var m = new HmdMatrix34_t();
                        ov.GetOverlayTransformAbsolute(h, ref origin, ref m);
                        var p = MathUtil.FromHmd(m).Translation;
                        where = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"abs {origin} ({p.X:0.00},{p.Y:0.00},{p.Z:0.00}) {Vector3.Distance(p, hmd):0.00} m from head");
                    }
                    else if (tt == VROverlayTransformType.VROverlayTransform_TrackedDeviceRelative)
                    {
                        uint dev = 0;
                        var m = new HmdMatrix34_t();
                        ov.GetOverlayTransformTrackedDeviceRelative(h, ref dev, ref m);
                        var p = MathUtil.FromHmd(m).Translation;
                        where = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"device {dev} ({p.X:0.00},{p.Y:0.00},{p.Z:0.00})");
                    }
                    else where = tt.ToString();
                    // positions move with the head: round them so only real changes are logged
                    Note(last, k, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(vis ? "VISIBLE" : "hidden ")} a {a:0.00} w {w:0.00} c {c:0.000} sort {so} tex {tw}x{th} {where}"));
                }
                Thread.Sleep(250);
            }
            return 0;
        }
        finally { OpenVR.Shutdown(); }
    }

    static void Note(Dictionary<string, string> last, string key, string state)
    {
        // log a change of visibility / size / alpha at once, a move only when it is more than ~10 cm
        if (last.TryGetValue(key, out var prev) && Same(prev, state)) return;
        last[key] = state;
        Log.Info($"ovdump: {key,-28} {state}");
    }

    static bool Same(string a, string b)
    {
        static string Head(string s) { int i = s.IndexOf(" abs ", StringComparison.Ordinal); return i < 0 ? s : s[..i]; }
        if (Head(a) != Head(b)) return false;
        var na = System.Text.RegularExpressions.Regex.Matches(a, @"-?\d+\.\d+");
        var nb = System.Text.RegularExpressions.Regex.Matches(b, @"-?\d+\.\d+");
        if (na.Count != nb.Count) return false;
        for (int i = 0; i < na.Count; i++)
            if (MathF.Abs(float.Parse(na[i].Value, System.Globalization.CultureInfo.InvariantCulture) -
                          float.Parse(nb[i].Value, System.Globalization.CultureInfo.InvariantCulture)) > 0.15f) return false;
        return true;
    }
}
