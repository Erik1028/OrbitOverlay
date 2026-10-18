# Theater mode optimized - perf-theater (on perf-base 41bb8e5 / main 526060f)

Erik 10-18: "it must be optimized, get the maximum out of it" - the theater pieces (area A), against `docs/perf/baseline.md`. Measured the same way: `ORBIT_SNAP_FONTS=0 ORBIT_SNAP_ONLY=theaterperf OrbitOverlay.exe --snapshot <dir>` (`ORBIT_THEATERPERF_PANELS=0`), the best of 3 rounds (a range in brackets where the rounds differed by more than 5 %), the worst frame in parentheses. Before = the measure agent's 3 rounds on perf-base; after = 3 rounds on this branch. Every picture of citylife, pulse, mediabar, ambilight, backdrops and cyberui is pixel-identical to the base build's (the only differing pixels: the watch's clock minutes in cyberui), every "want True" check green (122 lines a round).

## What changed, win by win (a commit each)

1. **Theater.Update allocates nothing** (A6). `Update` takes `List<ITheaterScreen>` (through `IReadOnlyCollection` each foreach boxed the list's enumerator, 40 B); the backdrop thread's closure moved into `BackdropJob` (the compiler made it on entry every frame, 32 B). Theater.Update 80 / 80 / 112 B a frame -> 0, theater on or off - the installed app's "desktops 46-67 B/loop".
2. **Ambilight written at a cadence** (A1). `Ambilight.Step` says when the glow is composed, uploaded and handed over: every frame only while a colour moves 2 levels or more a frame (the first frames after a cut), every third frame in the tail of the easing (a texel moves under 2 levels a frame there), once more at rest so it ends exactly on the colours. The easing itself is untouched: the same colours on the same frames. Harness (a hard cut twice a second, the worst case): 90 -> 65.5 writes/s; a film (small changes) gets 30/s. AmbilightTest: a new "cadence" check and `ambilight_cadence.png` (the eased colour and the written stair: never more than 2.5 levels apart).
3. **The cars' and the ring's alpha in visible steps** (A2 + A8). The cars' overlay alpha in 1/128 steps, the horizon ring's in 1/256 (each under a level of their brightest texel); VrOverlay spares the rest. The cars' place is still sent every frame (45 Hz would be judder). SetAlpha 154.8 -> 57.6/s at rest; the ring with music 0.91 -> 0.38 calls a frame.
4. **The textures made ready while theater is off** (A4, `TheaterWarm.cs`). With VR connected and theater off, Theater.Update makes the style's pieces one a frame: in Cyber the wall, the band, the horizon's textures, the cars', the band under the lamps; elsewhere the backdrop. The first Cyber theater frame drew all of it: 23 ms. `DrawSkyline`, `LampsEnd`, `LampsRead` get `AggressiveOptimization` (run once a session they ran as tier-0 code: the band 12 ms, the lamps' list 4.1, the band under the lamps 10 - where the same code warm takes 2.9 / 0.7 / 0.8; not on `DrawCity` (D2D's calls) nor `SparkPixels` (3x slower that way: tier-1 folds its static readonly table)). The overlays are still made when first shown (SteamVR's overlay count).
5. **The backdrop's buffer kept, its upload sliced** (A5). The thread paints into one 8 MB buffer kept for the Theater's life (a new one each draw was a large-object allocation, a gen2 GC); the picture goes into the texture a quarter a frame after a frame that makes the texture (8 MB in one UpdateSubresource was a 2.5-3.4 ms frame). Modern coming in: the backdrop section's worst 2.54 -> 0.36 ms.

## At rest (20 s), a frame

| what | Cyber Full before -> after | Cyber Reduced before -> after | Modern Full before -> after |
|---|---|---|---|
| frame ms (work + GPU flush) | 0.1536 (0.986) -> 0.1464 (1.071) | 0.1306 (0.594) -> 0.1217 (0.443) | 0.1241 (0.443) -> 0.1134 (0.488) |
| Theater.Update ms | 0.0596 (0.887) -> 0.0518 (0.947) | 0.0474 (0.218) -> 0.0372 (0.361) | 0.0447 (0.237) -> 0.0353 (0.309) |
| - ambilight | 0.0506 (0.660) -> 0.0431 (0.941) | 0.0448 (0.216) -> 0.0354 (0.360) | 0.0402 (0.302) -> 0.0327 (0.390) |
| - city life | 0.0041 (0.840) -> 0.0043 (0.898) | 0 -> 0 | 0 -> 0 |
| - horizon | 0.0014 -> 0.0013 | 0.0003 -> 0.0003 | 0.0002 -> 0.0001 |
| - the rest (fade, places) | 0.0029 -> 0.0023 | 0.0010 -> 0.0005 | 0.0018 -> 0.0013 |
| the bar (Update) ms | 0.0065 -> 0.0069 | 0.0035 -> 0.0036 | 0.0004 -> 0.0005 |
| bytes a frame (Theater.Update) | 85 (80) -> 5 (0) | 82 (80) -> 2 (0) | 112 (112) -> 0 (0) |
| SteamVR calls a frame (worst) | 5.65 (9) -> 4.29 (9) | 1.69 (3) -> 1.42 (3) | 1.67 (2) -> 1.40 (2) |
| SetTexture/s | 156.9 -> 132.4 | 151.8 -> 127.4 | 150 -> 125.6 |
| SetAlpha/s | 154.8 -> 57.6 | 0 -> 0 | 0 -> 0 |
| SetTransform/s (the cars) | 196.2 -> 196.2 | 0 | 0 |
| texture MB/s without the screen | 46.2 -> 45.4 | 4.26 -> 3.43 | 3.06 -> 2.23 |
| band sends/s | 5 -> 5 | 0 | 0 |
| ambilight glow writes/s | 90 -> 65.5 | 90 -> 65.6 | 90 -> 65.6 |

## Coming in (3 s)

| what | Cyber Full before -> after | Cyber Reduced before -> after | Modern Full before -> after |
|---|---|---|---|
| frame ms avg (worst) | 0.372 (23.0) -> 0.232 (1.35) | 0.193 (11.2) -> 0.142 (1.88) | 0.143 (4.07) -> 0.119 (2.85) |
| Theater.Update worst | 22.4 -> 0.85 | 10.6 -> 0.99 | 2.68 -> 1.28 |
| - wall + city / backdrop worst | 18.3 -> 0.003 | 8.2 -> 0.003 | 2.63 -> 0.31 |
| - city life worst | 10.5 -> 0.06 | 0 | 0 |
| - horizon worst | 2.29 -> 0.012 | 2.34 -> 0.003 | 0 |
| - ambilight worst (its shape, 0.6-0.8 ms, still drawn in frame 1) | 1.63 -> 0.85 | 0.98 -> 0.98 | 1.12 -> 0.76 |
| bytes a frame avg (worst) | 13913 (3.14 MB) -> 2990 (799 KB) | 11744 (2.34 MB) -> 2990 (799 KB) | 3124 (807 KB) -> 2976 (799 KB) |
| SteamVR calls a frame (worst) | 8.86 (30) -> 8.24 (31) | 2.37 (29) -> 2.16 (29) | 3.40 (17) -> 3.14 (17) |

The 799 KB left is the Ambilight's shape (its weights), once per theater start - it needs the screen's aspect.

## Before theater (0.5 s: the pieces made ready, one a frame) - new

| | Cyber Full | Cyber Reduced | Modern Full |
|---|---|---|---|
| frame ms avg (worst) | 0.41 (6.1) | 0.34 (6.6) | 0.15 (2.0) |
| the frames over 0.5 ms | the wall 6.4, the band 3.3, the horizon's textures 2.5, the cars' 0.6, the band under the lamps 0.8 | 6.2 / 3.6 / 3.1 | the backdrop's texture 2.4, its four slices under 0.5 each |

Going out (1 s): bytes a frame 58 / 52 / 72 -> 0; the rest within noise.

## Looked at and left as it is

- **A3, the band handed to SteamVR 5x a second** (8 MB each, 40 MB/s): every tick changes an antenna's beat or a star's twinkle, and a texture changed under SteamVR's nose is not seen without SetOverlayTexture (whether vrclient copies or shares a D3D11 texture needs a look in VR). A 2.5 Hz beat would change the look. Left.
- **A7, the bar's marquee** at 26 steps/s with 1 px steps: 2 px at 13 Hz would be a coarser scroll. Left (0.0065 ms a frame; the long title's case 5-8 ms/s).
- **A8, the meter's COM call a frame** (`IAudioMeterInformation.GetPeakValue`): not measured here (the tools' sound is made up); the watch's microphone meter asks the same way. Left at a frame.
- **The cars' SetAbsolute every frame** (196/s): they move; every second frame would be 45 Hz judder.
- **A9, CyberScreenFx**: fine as it was.

## What to look at in VR

- The Ambilight's tail after a hard cut (the 30 Hz part of the cadence) on a film with cuts - it should look exactly as before; if a step ever shows, `Ambilight.WriteStep` (2 levels) is the knob.
- The cars' fade-in and the drone's breathing (1/128 alpha steps), the ring's breathing (1/256).
- The moment VR connects in Cyber: five slightly longer frames (6 / 3 / 2.5 / 0.6 / 0.8 ms) while the city is made; a style change to Cyber in VR does the same once.
