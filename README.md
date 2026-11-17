# RESQ

**Small worlds. Big saves.** A hands-first mixed reality rescue strategy game for Meta Quest 3 and 3S.

A small valley sits on a table in front of you. A flood, a forest fire, an avalanche slope, a rising tide or a lava flow is coming, and residents need a way out. Pinch a tool from the tray, place it on a legal spot, and check the forecast before you commit: the real simulation runs ahead and shows what stays dry and what floods.

- **Content:** five worlds and 25 missions. Each mission has two winning plans, three rule presets (Standard, Relaxed, Command) and named challenges, 30 in all.
- **Input:** hands only: poke, pinch, and ray-and-pinch. One-hand play mirrors the controls. Command mode works with head-gaze dwell alone.
- **Room:** starts on a virtual table with no permission prompt. "Use my table" finds a real table with the Mixed Reality Utility Kit. Spatial anchors bring your valley back next session.
- **Simulation:** deterministic and integer-only, so physics and frame rate never decide an outcome.

Entered in the Meta VR Start Developer Competition 2026, Gaming track, New Experience. Website: https://resq-game.vercel.app

## Team
- **Osama Mohamed:** Team Representative. Submission, Meta developer account and the Competition release channel.
- **Amir Lotfy:** game design and development: simulation, missions, mixed reality, art and audio pipeline.

## Build
- **Unity:** 6000.3.24f1 with the Android module (SDK, NDK r27c, JDK 17). URP, IL2CPP, ARM64.
- **Meta XR:** Core, Interaction and MR Utility Kit SDKs 207.0.1, resolved from the scoped registry in `game/Packages/manifest.json`.
- **Open:** `game/`, scene `Assets/Resq/Scenes/Operation_XR.unity`.
- **Release build:** `tools/unity/release_apk.sh`. It needs your own keystore through `RESQ_KEYSTORE` and `RESQ_KEYSTORE_PASS`. No key is in this repository.
- **Without a headset:** play in the Editor or in Meta XR Simulator (`tools/xr/sim_editor.sh`).

Some scripts under `tools/` were written for one development machine and use absolute paths. Adjust `ROOT` in them before use.

## Tests
- `python3 tools/simtest/simtest.py sim`: simulation tests, headless.
- `python3 tools/simtest/simtest.py lab validate W1-M1,…,W5-M5`: every mission, both layouts, three presets, both plans (450 runs).
- `python3 tools/simtest/simtest.py lab challenge all`: the 30 challenges, 120 runs.
- `bash tools/unity/smoke.sh`: the PlayMode gate in the open Editor. `tools/unity/matrix.sh` runs all 25 missions through hand, one-hand and head-gaze input.

## Layout
| Path | What |
|---|---|
| `game/` | Unity project; product code in `game/Assets/Resq/` |
| `content/` | Generated mission layouts and replays |
| `specification/` | Rules, mission contracts, layout recipes, challenges |
| `tools/` | Layout compiler, validator, build, test, capture and trailer scripts |
| `web/app/` | Website |
| `video/` | Trailer edit list, voice-over and music |
| `docs/` | Submission material, spend log, reviews |

## Credits and tools
- **Assets:** landmarks, vehicles and residents were generated with Meshy from concept images made with Higgsfield, and the residents were rigged and animated with Meshy. Every spend and its licence terms are in `docs/spend-log.md`.
- **Made in code:** terrain and nature come from scripted Blender kits. In-game music and sound are synthesised from code (`tools/audio/synth.py`).
- **Trailer:** voice-over and music made with Google AI Studio. All gameplay captured in Meta XR Simulator.
- **Font:** Atkinson Hyperlegible, SIL Open Font License (`web/app/fonts/OFL.txt`).

© 2026 the RESQ team. All rights reserved.
