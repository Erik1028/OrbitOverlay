# OrbitOverlay perf baseline - main 526060f

Best of 3 rounds of `ORBIT_SNAP_FONTS=0 ORBIT_SNAP_ONLY=<tag> OrbitOverlay.exe --snapshot <dir>` (tags theaterperf, perf, cyberperf), the perf-base build, nothing else heavy running. "Best" = the lowest average (the PC's load swings run to run); a count that differed between rounds by more than 5 % shows its range. Worst (in brackets) = the slowest single frame, the lowest of the rounds' worsts: what a quiet machine does; in VR, with the game on every core, the worst is above it.

## Theater mode, a frame (theaterperf)

A made-up headset and 2560 x 1440 screen (60 new capture frames a second, a cut twice a second), media playing, made-up music, Ambilight on, the bar up (the laser on the big screen), 90 frames a simulated second, the GPU idle before each frame. CPU ms = the frame's work + the GPU flush. SteamVR calls = calls that reached the (fake) runtime; spared = a setter found nothing changed. Texture MB/s = bytes behind SetTexture (the 2560 x 1440 screen alone is 884.7 MB/s at 60 Hz).

### at rest (20 s)

| what (average a frame; worst frame in brackets) | Cyber Full | Cyber Reduced | Modern Full |
|---|---|---|---|
| frame ms | 0.1536 (0.986) | 0.1306 (0.452) | 0.1241 (0.398) |
| Theater.Update ms | 0.0596 (0.862) | 0.0474 (0.217) | 0.0447 (0.237) |
| theater: wall + city / backdrop ms | 0.0006 (0.027) | 0.0005 (0.03) | 0.0008 (0.035) |
| theater: city life ms | 0.0041 (0.826) | 0 (0.002) | 0 (0) |
| theater: ambilight ms | 0.0506 (0.66) | 0.0448 (0.216) | 0.0402 (0.235) |
| theater: horizon ms | 0.0014 (0.024) | 0.0003 (0.005) | 0.0002 (0.002) |
| theater: the rest (fade, places) ms | 0.0029 (0.862) | 0.001 (0.217) | 0.0018 (0.237) |
| the bar (Update) ms | 0.0065 (0.532) | 0.0035 (0.19) | 0.0004 (0.001) |
| the screen (place, capture hand-over) ms | 0.0004 (0.001) | 0.0002 (0) | 0.0002 (0.001) |
| the screen's Cyber fx ms | 0.0005 (0.002) | 0.0002 (0) | 0 (0) |
| bytes a frame | 85.47 (4920) | 81.7 (288) | 112 (112) |
| bytes a frame: Theater.Update | 80 | 80 | 112 |
| bytes a frame: the bar | 5.47 | 1.7 | 0 |
| bytes a frame: the screen | 0 | 0 | 0 |
| SteamVR calls a frame | 5.65 (9) | 1.69 (3) | 1.67 (2) |
| SteamVR calls spared a frame | 41.3 | 35.67 | 21.67 |
| texture MB/s | 930.94 | 889 | 887.8 |
| SetTexture/s | 156.95 | 151.85 | 150 |
| SetTransform/s | 196.2 |  |  |
| SetWidth/s | 0.1 |  |  |
| SetAlpha/s | 154.8 |  |  |
| Show/s | 0.1 |  |  |

### coming in (3 s)

| what (average a frame; worst frame in brackets) | Cyber Full | Cyber Reduced | Modern Full |
|---|---|---|---|
| frame ms | 0.3719 (23.044) | 0.1927 (11.249) | 0.1429 (3.437) |
| Theater.Update ms | 0.1885 (22.442) | 0.0837 (10.61) | 0.0553 (2.666) |
| theater: wall + city / backdrop ms | 0.0684 (18.251) | 0.0307 (8.206) | 0.0142 (2.633) |
| theater: city life ms | 0.0458 (10.495) | 0 (0) | 0 (0) |
| theater: ambilight ms | 0.0544 (1.441) | 0.0419 (0.88) | 0.0377 (1.117) |
| theater: horizon ms | 0.0109 (2.29) | 0.0089 (2.341) | 0.0001 (0) |
| theater: the rest (fade, places) ms | 0.0036 (22.442) | 0.0013 (10.61) | 0.002 (2.666) |
| the bar (Update) ms | 0.0793 (0.533) | 0.0243 (0.529) | 0.0087 (2.235) |
| the screen (place, capture hand-over) ms | 0.0006 (0.001) | 0.0002 (0) | 0.0002 (0) |
| the screen's Cyber fx ms | 0.0113 (0.114) | 0.0007 (0.134) | 0 (0) |
| bytes a frame | 13913 (3141320) | 11744 (2342648) | 3124 (807144) |
| bytes a frame: Theater.Update | 13890 | 11717 | 3108 |
| bytes a frame: the bar | 23.97 | 24.8 | 16.09 |
| bytes a frame: the screen | 0 | 2.52 | 0 |
| SteamVR calls a frame | 8.86 (30) | 2.37 (29) | 3.4 (17) |
| SteamVR calls spared a frame | 36.42 | 34.8 | 18.99 |
| texture MB/s | 987.72 | 898.91 | 890.23 |
| SetTexture/s | 179 | 142.67 | 135 |
| SetTransform/s | 190.33 | 7.67 | 40.67 |
| SetWidth/s | 109.33 | 2.33 | 40.67 |
| SetAlpha/s | 148 | 53.33 | 46.67 |
| SetCurvature/s | 107.67 | 1.67 | 40 |
| SetTextureBounds/s | 54 |  |  |
| Show/s | 3 | 2 | 1.33 |
| Hide/s | 0.67 |  |  |

### going out (1 s)

| what (average a frame; worst frame in brackets) | Cyber Full | Cyber Reduced | Modern Full |
|---|---|---|---|
| frame ms | 0.1146 (0.235) | 0.0762 (0.239) | 0.0874 (0.215) |
| Theater.Update ms | 0.0212 (0.11) | 0.0109 (0.102) | 0.0201 (0.121) |
| theater: wall + city / backdrop ms | 0.0002 (0.001) | 0.0002 (0) | 0.0002 (0.001) |
| theater: city life ms | 0.0012 (0.049) | 0 (0) | 0 (0) |
| theater: ambilight ms | 0.0177 (0.105) | 0.0086 (0.101) | 0.0172 (0.103) |
| theater: horizon ms | 0.0005 (0.001) | 0.0002 (0.001) | 0.0001 (0) |
| theater: the rest (fade, places) ms | 0.0011 (0.11) | 0.0011 (0.102) | 0.0017 (0.121) |
| the bar (Update) ms | 0.0004 (0.002) | 0.0004 (0.002) | 0.0002 (0) |
| the screen (place, capture hand-over) ms | 0.0004 (0.001) | 0.0004 (0.001) | 0.0004 (0.001) |
| the screen's Cyber fx ms | 0.0138 (0.07) | 0.0002 (0.001) | 0 (0) |
| bytes a frame | 58.22 (80) | 52.44 (80) | 71.73 (112) |
| bytes a frame: Theater.Update | 58.22 | 52.44 | 71.73 |
| bytes a frame: the bar | 0 | 0 | 0 |
| bytes a frame: the screen | 0 | 0 | 0 |
| SteamVR calls a frame | 8 (20) | 2.73 (9) | 5.07 (12) |
| SteamVR calls spared a frame | 18.19 | 14.77 | 10.24 |
| texture MB/s | 989.77 | 885.38 | 885.96 |
| SetTexture/s | 131 | 79 | 96 |
| SetTransform/s | 171 | 1 | 80 |
| SetWidth/s | 81 | 1 | 80 |
| SetAlpha/s | 243 | 142 | 115 |
| SetCurvature/s | 81 | 1 | 80 |
| SetTextureBounds/s |  | 15 |  |
| Show/s | 1 |  |  |
| Hide/s | 10 | 6 | 4 |

### At rest, the pieces' own rates

| what | Cyber Full | Cyber Reduced | Modern Full |
|---|---|---|---|
| texture MB/s without the screen | 46.2 | 4.26 | 3.06 |
| band sends/s | 5 | 0 | 0 |
| ambilight samples/s | 10 | 10 | 10 |
| ambilight glow writes/s | 90 | 90 | 90 |

### One-off costs (the first time; ms, after the JIT and fonts are warm)

| what | CPU cold | CPU warm (drawn again) | until the GPU is done (cold) |
|---|---|---|---|
| the city wall 1024 x 2048 (DrawCity) | 6.38 | 5.58 | 7.04 |
| the skyline band 4096 x 512 (DrawSkyline, its lamps recorded) | 2.46 | 2.04 | 3.04 |
| the band without its lamps (city life's base) | 0.61 | 0.62 | 1.14 |
| the horizon's ring 4096 x 64 and a spark 512 x 128 (pixels + upload) | 3.4 | 0 | 3.71 |
| the cars' textures | 0.42 | 0 | 0.61 |
| the ring's pixels alone (HorizonPulse.RingPixels) | 0.27 | 0.25 | 0.45 |
| a spark's pixels alone (HorizonPulse.SparkPixels) | 1.47 | 1.44 | 1.84 |
| the Modern backdrop (the hall, 1024 x 2048; on a thread in VR) | 31.48 | 30.35 | 31.68 |
| the Ambilight's shape (16:9: the glow's weights) | 0.7 | 0.54 | 0.99 |
| the Ambilight's sampling of a 2560 x 1440 picture | 0.26 | 0.07 | 0.51 |
| the Cyber bar with the media console (DrawFor) | 0.66 | 0.29 | 0.89 |
| the screen's Cyber frame 1024 x 604 (the lock-on, DrawForTest) | 0.25 | 0.05 | 0.61 |

## Panels at rest, in VR terms (theaterperf: Update with a fake overlay, 90 Hz on the clock, 6 s after settling)

| panel | frame ms (worst) | bytes a frame (worst) | redraws/s (SetTexture) | texture MB/s | SteamVR calls a frame (worst) | spared a frame |
|---|---|---|---|---|---|---|
| Modern dock panel | 0.076 (0.691) | 8 (704) | 1 | 1.53 | 0.01 (1) | 4 |
| Modern keyboard | 0.078 (0.195) | 0 (0) | 0 | 0 | 0 (0) | 4 |
| Modern room panel | 0.07 (0.21) | 0 (0) | 0 | 0 | 0 (0) | 4 |
| Modern settings VR panel | 0.081 (0.571) | 1 (40) | 2 | 11.5 | 0.02 (1) | 4 |
| Modern watch (full) | 0.088 (0.513) | 17 (816) | 2 | 2.25 | 0.02 (1) | 4 |
| Cyber dock panel | 0.085 (0.841) | 8 (752) | 1 | 1.53 | 0.01 (1) | 4 |
| Cyber keyboard | 0.082 (0.273) | 0 (0) | 0 | 0 | 0 (0) | 4 |
| Cyber room panel | 0.079 (0.26) | 0 (0) | 0 | 0 | 0 (0) | 4 |
| Cyber settings VR panel | 0.087 (0.555) | 1 (40) | 1.83 (1.83..2) | 10.54 (10.54..11.5) | 0.02 (1) | 4 |
| Cyber watch (full) | 0.088 (1.006) | 2 (152) | 2 | 2.25 | 0.02 (1) | 4 |

## Panels drawn (perf: a Draw a frame, the GPU idle before each; best of 3 rounds of 60)

| what | Modern ms / B a frame / layouts made | Cyber ms / B a frame / layouts made | 95 ms / B a frame / layouts made |
|---|---|---|---|
| watch at rest | 0.979 / 0 / 0 | 0.776 / 463 / 0 | 0.65 / 2432 / 0 |
| watch animating (comes into view) | 1.187 / 0 / 0 | 0.69 / 397 / 0.1 | 0.662 / 2360 / 0 |
| keyboard at rest | 0.793 / 0 / 0 | 0.121 / 0 / 0 | 0.835 / 0 / 0 |
| keyboard typing (a key let go) | 0.804 / 0 / 0 | 0.196 / 0 / 0.1 | 0.839 / 0 / 0 |
| settings window at rest | 0.756 / 96 / 0 | 0.755 / 96 / 0 | 0.766 / 280 / 0 |
| settings window, a switch hovered | 0.758 / 96 / 0 | 0.749 / 96 / 0 | 0.75 / 280 / 0 |
| settings VR panel at rest | 0.803 / 96 / 0 | 0.745 / 96 / 0 | 0.73 / 280 / 0 |
| settings VR panel, a switch hovered | 0.798 / 96 / 0 | 0.768 / 96 / 0 | 0.737 / 280 / 0 |
| theater bar (switcher) | 0.23 / 0 / 0.1 | 0.251 / 0 / 0.1 | 0.253 / 0 / 0 |
| notifications (2 cards) | 0.168 / 0 / 0 | 0.253 / 40 / 0 | 0.166 / 0 / 0 |
| room panel | 0.34 / 984 / 0.1 | 0.438 / 1360 / 0.1 | 0.308 / 1952 / 0.1 |
| dock panel | 0.461 / 968 / 0.1 | 0.524 / 752 / 0.1 | 0.315 / 856 / 0.1 |

## Settings and keyboard, Cyber against Modern (cyberperf)

| style | settings (General) at rest ms (B) | a switch under the pointer ms | a tab picked ms | keyboard at rest ms (B) | a key let go ms |
|---|---|---|---|---|---|
| Cyber | 0.923 (96) | 0.939 | 0.997 | 0.126 (0) | 0.137 |
| Modern | 0.893 (96) | 0.895 | 0.879 | 0.812 (0) | 0.787 |

## The main loop's other costs (perf, round 1 as logged)

- perf: the VR settings panel's corners cut against the clip layer, 8 tabs: 160 pixels differ (want 0), by up to 1 of 255
- perf: the retro styles' pixel text from kept layouts against DrawText, 50 pictures: 0 pixels differ (want 0)
- perf: capture P24FBA-RAGL    109 new frames 0.144 ms a poll, 71 without 0.006 ms, 0 B a poll
- perf: capture 24G4           108 new frames 0.191 ms a poll, 72 without 0.006 ms, 0 B a poll
- perf: capture P24FBA-RAGL    0 new frames 0.000 ms a poll, 180 without 0.006 ms, 0 B a poll
- perf: pacer 181 ticks, cpu 5.9 us a wait, interval median 11.13 ms p99 11.68 (target 11.11), 0 B a wait

