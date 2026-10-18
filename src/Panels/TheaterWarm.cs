using System;

namespace Orbit;

// Theater mode's textures made ready while theater is off (Erik 10-18: "it must be optimized"). The first Cyber theater's first frame
// drew the city wall, the skyline band (its lamps listed) and the horizon's ring and sparks - 18-23 ms in one frame, two stalled
// loop frames (the lasers and panels froze; the compositor went on) - and the band under the lamps 10 ms more a few frames later;
// the Modern hall came up a second after the dim (its thread started with theater). Now, with VR connected and theater off,
// Theater.Update makes the style's pieces on idle frames, one piece a frame (a few ms each, at VR connect or a style change - nobody is
// watching a theater then): in Cyber the wall (in eighths), the band, the floor's three (in quarters), the horizon's two textures, the
// cars' three, then the band under the lamps (LifeBase's own frames and its thread); in every other style its backdrop
// (TheaterBackdrop.cs: on its thread, uploaded when it comes). The overlays themselves are still made when first shown (SteamVR's
// overlay count is precious: none sits there for a theater never started). The rule "only the style in use has a texture" stays:
// Cyber's pieces go when a backdrop is made, the backdrop when the city is. Everything ready: a few flag tests a frame. The tools
// without VR (no overlay) never warm, so their numbers are their own.
public sealed partial class Theater
{
    // from Update, with theater off and no screen on its way out
    void WarmTheater()
    {
        if (_dim == null || _p == null) return;
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
}
