# Quest render budget check — 23 Oct 2026

**Evidence kind:** automated, Unity Editor play mode on the M1 Mac. One mono view, Editor overhead included; not device evidence. Device numbers come in P5.

**Budgets (BUILD_PLAN):** ≤150 draw calls and ≤300k triangles per frame, 72 Hz on Quest 3/3S, 0 B/frame of garbage once running.

**Method:** `tools/unity/perf.sh [wide|compact]` loads each mission at operation step 300 of strategy A, which is mid-run with groups moving. It frames the seated view (pitch 52°, 0.72 m), measures with the shipping HUD (no dev mission picker), and averages 120 frames of Unity's render counters (`PerfProbe`). Triangle counts include the shadow pass.

## Before → after

| | Draw calls | Triangles |
|---|---|---|
| First reading, W1-M1 | 175 | 649k |
| First reading, W3-M2 | 237 | 320k |
| All 25 missions, wide, final | 87–144 | 138k–290k |
| All 25 missions, compact, final | 87–144 | 138k–290k |

## What changed

- **Nature scatter:** visible instances draw the kit's LOD1 and the shadow pass uses LOD2, at the same draw count. World 1's woods have fewer broadleaf crowns (≈400 triangles each) and a few more conifers.
- **Terrain:** the visible heightfield no longer casts shadows; a coarse proxy (2 samples per cell, about a quarter of the triangles) does.
- **Residents and crew:** standing residents, seated riders, crew, group bases, chairs and helmets are drawn instanced, one draw per mesh and material. Each figure type's standing pose is baked once; walking residents switch to their animated skinned renderer.
- **Shadows:** skinned figures, water, overlays, markers, routes and the placement ghost don't cast.
- **HUD:** glass panels and icons merge into one mesh per root (card, chips and toast; sign; tool tray; controls; bubbles). Each chip and each tool card uses one text object, not two. HUD text refreshes at 5 Hz. The dev mission picker is off outside the Editor and development builds.
- **Garbage:** per-frame string-built `Find` calls are gone. In the Editor, garbage is about 15.5 KB/frame, but with every game behaviour off the Editor alone produces about 14.8 KB. The game's own share (~0.5–1 KB) needs a development build on device to measure exactly.

## Per mission (final)

| Mission | Wide draws | Wide tris | Compact draws | Compact tris | Editor main thread (wide) |
|---|---:|---:|---:|---:|---:|
| W1-M1 | 101 | 277k | 101 | 277k | 5.9 ms |
| W1-M2 | 114 | 275k | 114 | 275k | 3.7 ms |
| W1-M3 | 107 | 284k | 107 | 284k | 3.9 ms |
| W1-M4 | 116 | 286k | 116 | 286k | 3.8 ms |
| W1-M5 | 130 | 290k | 130 | 290k | 4.2 ms |
| W2-M1 | 115 | 166k | 115 | 166k | 3.9 ms |
| W2-M2 | 108 | 177k | 108 | 177k | 4.1 ms |
| W2-M3 | 107 | 161k | 107 | 161k | 4.4 ms |
| W2-M4 | 111 | 180k | 111 | 180k | 4.0 ms |
| W2-M5 | 114 | 223k | 111 | 229k | 4.0 ms |
| W3-M1 | 109 | 163k | 109 | 163k | 4.0 ms |
| W3-M2 | 112 | 170k | 112 | 170k | 3.7 ms |
| W3-M3 | 104 | 182k | 104 | 182k | 3.9 ms |
| W3-M4 | 123 | 175k | 123 | 175k | 4.6 ms |
| W3-M5 | 115 | 185k | 115 | 185k | 3.7 ms |
| W4-M1 | 87 | 141k | 87 | 141k | 3.7 ms |
| W4-M2 | 98 | 169k | 98 | 169k | 3.8 ms |
| W4-M3 | 103 | 142k | 103 | 142k | 3.8 ms |
| W4-M4 | 97 | 148k | 97 | 148k | 3.9 ms |
| W4-M5 | 100 | 156k | 97 | 161k | 3.7 ms |
| W5-M1 | 108 | 138k | 109 | 138k | 3.9 ms |
| W5-M2 | 101 | 156k | 101 | 156k | 3.5 ms |
| W5-M3 | 116 | 157k | 116 | 157k | 4.0 ms |
| W5-M4 | 121 | 181k | 121 | 180k | 3.9 ms |
| W5-M5 | 144 | 181k | 144 | 180k | 4.4 ms |

## Not covered

- **Stereo:** Quest renders two views. Single-pass multiview keeps draw calls the same but runs vertex work twice.
- **Device data:** GPU time, thermals and fixed foveation are measured in P5 with `metavr perf` / OVR Metrics.
- **Headroom:** the worst cases are W5-M5 at 144 draws, peaking with 24 residents walking at once, and W1-M5 at 290k triangles. If device frame time is tight, the next steps are a baked walk flipbook (instanced walkers) and shadow proxies for the hero buildings.
