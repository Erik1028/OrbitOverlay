using System;

namespace Orbit;

// What drives the bulb (LumosLink - straight or through Lumos; a made-up one in the checks)
public interface ILampControl
{
    LampState State { get; }
    void SetPower(bool on);
    void SetBrightness(int v);
    void SetColor(int h, int sat);
}

// The bulb with VR (Erik 10-19: "1, 7 mehet" - Lumos's "Lights on with SteamVR", and more), each part a switch of its own in the light's
// opened view (Config.Lights):
//  - OnWithVr: SteamVR just started - the bulb on, once it answers (within a minute). Orbit started into a SteamVR already running (an
//    update, a restart) does nothing: the bulb may have been switched off on purpose.
//  - TheaterDim: theater mode starts with the bulb on and brighter than 10 % - dimmed to 10 %; it ends - its brightness back, unless it
//    was changed meanwhile (not 10 % any more) or switched off.
//  - CoverColor: a new song with a cover, the bulb on - its colour the cover's main one (a grey cover leaves it), at most every 2 s (a
//    skip through songs is not a flicker of the bulb).
public sealed class LightAuto
{
    public const int TheaterPct = 10;
    bool _vrWas, _theaterWas;
    double _onUntil = -1, _coverAt = -10;
    int _dimFrom = -1;
    string _coverKey;
    public string Last;                  // what it did last (the log, the checks)

    // each frame: SteamVR connected now, whether it only just started (asked once, on connecting), theater mode, what plays (a key per
    // cover and its pixels - premultiplied BGRA, a square; null: none)
    public void Tick(double now, LightsConfig cfg, ILampControl lamp, bool vr, Func<bool> steamVrFresh, bool theater, Func<(string key, uint[] px)> cover)
    {
        if (cfg == null || lamp == null) { _vrWas = vr; _theaterWas = theater; return; }
        var s = lamp.State;
        if (vr && !_vrWas && cfg.OnWithVr && steamVrFresh()) _onUntil = now + 60;
        _vrWas = vr;
        if (_onUntil > 0)
        {
            if (now > _onUntil || !cfg.OnWithVr) _onUntil = -1;
            else if (s.Ready)
            {
                _onUntil = -1;
                if (!s.On) Did(() => lamp.SetPower(true), "on: SteamVR started");
            }
        }
        if (theater != _theaterWas)
        {
            _theaterWas = theater;
            if (theater && cfg.TheaterDim && s.Ready && s.On && s.Brightness > TheaterPct)
            {
                _dimFrom = s.Brightness;
                Did(() => lamp.SetBrightness(TheaterPct), $"dimmed to {TheaterPct} % for theater mode (from {_dimFrom} %)");
            }
            else if (!theater && _dimFrom > 0)
            {
                int back = _dimFrom;
                _dimFrom = -1;
                if (s.Ready && s.On && s.Brightness == TheaterPct) Did(() => lamp.SetBrightness(back), $"back to {back} % after theater mode");
            }
        }
        if (!cfg.CoverColor || !vr || !s.Ready || !s.On) return;
        var (key, px) = cover();
        if (key == null || px == null || key == _coverKey || now - _coverAt < 2) return;
        _coverKey = key;
        _coverAt = now;
        if (CoverHue(px) is { } c) Did(() => lamp.SetColor(c.h, c.s), $"the cover's colour (hue {c.h}, {c.s} %)");
    }

    void Did(Action a, string what)
    {
        a();
        Last = what;
        Log.Info("lights: " + what);
    }

    // The cover's main colour: a hue histogram (24 steps of 15 degrees) of its coloured pixels, each weighted by how coloured and bright
    // it is; the step with the most, and the saturation there (at least 50 %: a bulb's pale colour reads as white). Null: a grey cover
    // (under 3 % of it coloured).
    public static (int h, int s)? CoverHue(uint[] px)
    {
        if (px == null || px.Length == 0) return null;
        Span<float> bins = stackalloc float[24], sats = stackalloc float[24];
        float total = 0;
        int n = 0;
        for (int i = 0; i < px.Length; i += 2)
        {
            uint c = px[i];
            float a = (c >> 24) / 255f;
            if (a < 0.5f) continue;
            n++;
            float r = ((c >> 16) & 255) / 255f / a, g = ((c >> 8) & 255) / 255f / a, b = (c & 255) / 255f / a;
            float mx = MathF.Max(r, MathF.Max(g, b)), mn = MathF.Min(r, MathF.Min(g, b)), d = mx - mn;
            if (mx < 0.15f || d < 0.12f) continue;
            float h = mx == r ? 60 * (((g - b) / d + 6) % 6) : mx == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
            float sat = Math.Clamp(d / mx, 0, 1), w = sat * mx;
            int k = (int)(h / 15) % 24;
            bins[k] += w;
            sats[k] += sat * w;
            total += w;
        }
        if (n == 0 || total < n * 0.03f) return null;
        int best = 0;
        for (int k = 1; k < 24; k++) if (bins[k] > bins[best]) best = k;
        return ((best * 15 + 7) % 360, Math.Clamp((int)MathF.Round(sats[best] / bins[best] * 100), 50, 100));
    }
}
