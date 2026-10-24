using System;

namespace Orbit;

// On this day's side of the app (Sys/PhotoMemories.cs, Panels/WristMemory.cs): VRChat's photos of this day in earlier years are looked
// for only while an On this day widget is on a watch page and the watch can be seen (as the latest photo's, OrbitApp.Photos.cs) - once
// a day (and at the start); the thumbnails are let go when it is switched off. Open in VR Gallery is the latest photo's (OnOpenPhoto).
public sealed partial class OrbitApp
{
    readonly PhotoMemories _memories = new();

    void UpdateMemories(double now)
    {
        bool want = C.Wrist.Face == "custom" && WatchPages.Any(C.Wrist, "memory") && (VrAfter(5) || now - _photoPreview < 3);
        _memories.Active = want;
        _data.Memories = want ? _memories.Get() : null;
    }
}
