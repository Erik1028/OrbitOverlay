using System;

namespace Orbit;

// Theater mode's textures made ready while theater is off (Erik 10-18: "it must be optimized"). The first Cyber theater's first frame
// drew the city wall, the skyline band (its lamps listed) and the horizon's ring and sparks - 18-23 ms in one frame, two stalled
// loop frames (the lasers and panels froze; the compositor went on) - and the band under the lamps 10 ms more a few frames later;
// the Modern hall came up a second after the dim (its thread started with theater). Now, with VR connected and theater off,
// Theater.Update makes the style's pieces on idle frames, one piece a frame (a few ms each, at VR connect or a style change - nobody is
// watching a theater then): in Cyber the wall (in eighths), the band, the floor's three (in quarters), the horizon's two textures, the
// cars' three, then the band under the lamps (LifeBase's own frames and its thread); in every other style its backdrop
// (TheaterBackdrop.cs: on its thread, uploaded when it comes); first of all, in any style, the Ambilight's shape for a 16:9 screen. The overlays themselves are still made when first shown (SteamVR's
// overlay count is precious: none sits there for a theater never started). The rule "only the style in use has a texture" stays:
// Cyber's pieces go when a backdrop is made, the backdrop when the city is. Everything ready: a few flag tests a frame. The tools
// without VR (no overlay) never warm, so their numbers are their own.
// (Erik 10-24: Orbit's VRAM - "release the GPU memory of what is not in use".) Kept warm for good they were 17 MB in Cyber (the wall, the
// band, the floor, the horizon), 8 in every other style (the backdrop), all evening for a theater that may never come. So once Orbit has
// not been used for a while (OrbitApp.GpuSweep: no laser, no watch, no theater, no full-screen video waiting for auto theater) they are
// let go - their overlays hidden and told first - and Cold keeps this from making them again the next frame. Orbit used again (every
// way into theater goes through one of those) clears it and they are warmed as above before a theater can start; one started cold
// anyway makes what it needs in its first frame, as before 10-18. The lamps' list and the backdrop's buffer (CPU memory) stay.
public sealed partial class Theater
{
    public bool Cold;                    // its textures let go while Orbit was not used (OrbitApp.GpuSweep): not warmed until it is again
    public bool Resting => !On && _screen == null && _leaving == null;      // nothing of theater shows (what WarmTheater runs with)

    // from Update, with theater off and no screen on its way out
    void WarmTheater()
    {
        if (_dim == null || _p == null || Cold) return;
        if (WarmAmbilight()) return;                                        // (the glow's shape, once a run: TheaterAmbilight.cs)
        if (!Theme.Cyber) { BackdropTextures(BackdropFor()); return; }     // (the city's textures let go; the picture drawn, then uploaded)
        if (_backTex != null) FreeBackdrop();
        if (!CityReady) { CityRows(CityWarmRows); return; }                    // (the wall: an eighth a frame)
        if (_sky == null) { _sky = new Surface(_g, BandW, BandH); DrawSkyline(); return; }
        if (FloorTextureStep(FloorWarmRows)) return;                           // (the floor's three, a quarter of one a frame: TheaterFloor.cs)
        if (_hzRingTex == null || _hzSparkTex == null) { EnsureHorizonTex(); return; }
        if (_carTex == null || _droneTex == null) { CarTextures(); return; }
        if (!CyberFx.Full) { LifeDrop(); return; }                         // (Motion turned down half way through the read-back: let go)
        if (_lifeState != 2 && _lifeState != 4) LifeBase();                 // (a frame each: the bare band and its copy, the map, the read-out)
    }

    // Orbit not used for a while, theater at rest (OrbitApp.GpuSweep): every texture warmed above let go. An overlay that was given one is
    // hidden and SteamVR told to let go of it before it is disposed (the wall gets its black texture back: it is never let go); every
    // "given once" flag goes with its texture, so the next theater gives them again. The cars' tiny textures stay (with their overlays).
    public void ReleaseTextures()
    {
        if (!Resting) return;
        Cold = true;
        if (_cityOn && _dim != null) { _dim.SetTexture(_black.Tex); _ringR = -1; }      // (as BackdropTextures: the city off the wall first)
        _cityOn = false;
        _band?.Hide(); _band?.ClearTexture();
        _city?.Dispose(); _city = null;
        _sky?.Dispose(); _sky = null;
        LifeDrop();                                                     // (a read-back half way: its thread joined, the copy unmapped, let go)
        FloorHide();
        foreach (var o in _floorOv) o?.ClearTexture();
        FloorFree();                                                    // (_floorSet false: given again)
        DisposeHorizon();                                               // (its overlays too: they are given their textures only when made)
        _back?.Hide(); _back?.ClearTexture();
        FreeBackdrop();                                                 // (_backSet None: given again)
    }

    // the style changed a while ago (OrbitApp.GpuSweep): the horizon's textures and overlays are Cyber's alone, and the style rule above
    // never let them go (only the city, the band, the floor and the read-back); hidden already (HorizonOff), made again by the next Cyber
    // theater (EnsureHorizon) or its warm-up
    public void ReleaseStyle()
    {
        if (!Theme.Cyber && !_hzShown) DisposeHorizon();
    }
}
