using System;
using System.Collections.Generic;
using System.Linq;

namespace Orbit;

// ORBIT_SNAP_ONLY=lights: the bulb with VR (Sys/LightAuto.cs) against a made-up bulb - on when SteamVR starts (once it answers, not
// when Orbit restarts into a running SteamVR, not with the switch off), theater mode dimming it and its brightness back (not when it was
// changed meanwhile), the cover's colour (only with its switch, once a song, not for a grey cover, not twice in 2 s), the cover's hue.
static class LightAutoChecks
{
    sealed class FakeLamp : ILampControl
    {
        public LampState State { get; set; } = new() { Status = LinkStatus.Ready, On = false, Brightness = 70 };
        public readonly List<string> Sent = new();
        public void SetPower(bool on) { Sent.Add($"power {on}"); Change(s => s.On = on); }
        public void SetBrightness(int v) { Sent.Add($"brightness {v}"); Change(s => s.Brightness = v); }
        public void SetColor(int h, int sat) { Sent.Add($"colour {h} {sat}"); Change(s => { s.Hue = h; s.Sat = sat; s.Temp = 0; }); }
        public void Change(Action<LampState> c) { var s = State.Copy(); c(s); State = s; }
        public string All => string.Join(", ", Sent);
    }

    static uint[] Cover(uint argb, int size = 32) => Enumerable.Repeat(argb, size * size).ToArray();

    public static void Run()
    {
        static (string, uint[]) None() => (null, null);
        var cfg = new LightsConfig { Bulb = "10.0.0.98" };

        // SteamVR starts: on once the bulb answers
        var lamp = new FakeLamp();
        lamp.Change(s => s.Status = LinkStatus.Looking);
        var a = new LightAuto();
        a.Tick(0, cfg, lamp, false, () => true, false, None);
        a.Tick(1, cfg, lamp, true, () => true, false, None);
        bool waited = lamp.Sent.Count == 0;
        lamp.Change(s => s.Status = LinkStatus.Ready);
        a.Tick(2, cfg, lamp, true, () => true, false, None);
        a.Tick(3, cfg, lamp, true, () => true, false, None);
        string on = lamp.All;
        // Orbit restarted into a running SteamVR; the switch off: nothing
        var lamp2 = new FakeLamp();
        var b = new LightAuto();
        b.Tick(0, cfg, lamp2, false, () => false, false, None);
        b.Tick(1, cfg, lamp2, true, () => false, false, None);
        var off = new LightsConfig { Bulb = "10.0.0.98", OnWithVr = false };
        var c = new LightAuto();
        c.Tick(0, off, lamp2, false, () => true, false, None);
        c.Tick(1, off, lamp2, true, () => true, false, None);
        string nothing = lamp2.All;
        Tools.Want($"lights auto: SteamVR started - waited for the bulb {waited}, then '{on}' (want power True, once); Orbit restarted into a running SteamVR / " +
                   $"the switch off: '{nothing}' (want nothing)", waited && on == "power True" && nothing == "");

        // theater mode: 70 % -> 10 % -> 70 %; changed meanwhile: left as it is
        var lamp3 = new FakeLamp();
        lamp3.Change(s => s.On = true);
        var d = new LightAuto();
        d.Tick(0, cfg, lamp3, true, () => false, false, None);
        d.Tick(1, cfg, lamp3, true, () => false, true, None);
        d.Tick(2, cfg, lamp3, true, () => false, true, None);
        d.Tick(3, cfg, lamp3, true, () => false, false, None);
        string theater = lamp3.All;
        lamp3.Sent.Clear();
        d.Tick(4, cfg, lamp3, true, () => false, true, None);
        lamp3.SetBrightness(35);                 // the user, during the film
        lamp3.Sent.Clear();
        d.Tick(5, cfg, lamp3, true, () => false, false, None);
        string changed = lamp3.All;
        Tools.Want($"lights auto: theater mode '{theater}' (want brightness 10, brightness 70); changed to 35 % during it, after: '{changed}' (want nothing)",
            theater == "brightness 10, brightness 70" && changed == "");

        // the cover's colour: only with its switch; a red cover once; the same again / a grey one / within 2 s: nothing
        var lamp4 = new FakeLamp();
        lamp4.Change(s => s.On = true);
        var e = new LightAuto();
        e.Tick(0, cfg, lamp4, true, () => false, false, () => ("s:red", Cover(0xFFE01010)));
        string noSwitch = lamp4.All;
        var cc = new LightsConfig { Bulb = "10.0.0.98", CoverColor = true };
        e.Tick(10, cc, lamp4, true, () => false, false, () => ("s:red", Cover(0xFFE01010)));
        e.Tick(11, cc, lamp4, true, () => false, false, () => ("s:red", Cover(0xFFE01010)));
        string red = lamp4.All;
        lamp4.Sent.Clear();
        e.Tick(11.5, cc, lamp4, true, () => false, false, () => ("s:blue", Cover(0xFF1030E0)));
        string soon = lamp4.All;
        e.Tick(20, cc, lamp4, true, () => false, false, () => ("s:grey", Cover(0xFF808080)));
        string grey = lamp4.All;
        var hb = LightAuto.CoverHue(Cover(0xFF1030E0));
        Tools.Want($"lights auto: the cover's colour without its switch '{noSwitch}' (want nothing), with it a red cover '{red}' (want one colour, hue 0-15), " +
                   $"a blue one 0.5 s later '{soon}' (want nothing yet), a grey one '{grey}' (want nothing); a blue cover's hue {hb?.h} {hb?.s} % (want 225-240, 50-100)",
            noSwitch == "" && red.StartsWith("colour 7 ") && !red.Contains(',') && soon == "" && grey == "" && hb is { h: >= 225 and <= 240, s: >= 50 });
    }
}
