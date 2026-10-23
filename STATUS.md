# RESQ status — 22 Oct 2026, 23:50 (day 0 of 26)

**Phase:** P0 complete; P1 (vertical slice) well under way. Plan: `BUILD_PLAN.md`.

## Done
- **Studio reset:** old process docs archived with checksums; commits are on local `main`.
- **Unity project:** URP, OpenXR, Meta XR 207.0.1 (Core, Interaction, MRUK), Quest settings (ARM64/IL2CPP, SDK 32/34, Vulkan, ASTC); Meta Setup Tool clean.
- **First APK built:** `build/resq-smoke.apk`, 75 MB. INTERNET permission is now stripped (takes effect on the next build).
- **Sim v2 engine:** water, power, equipment, transport, objectives, outcome. All 18 spec golden cases pass.
- **World 2 fire module:** heat, fuel, wetness, wind, ignition warnings, firebreak, sprinkler from a finite tank, roads closed by heat. 9 tests.
- **World 3 module:** structure stability (loss carry, braces, warning-gated collapse, cancel), avalanche runout DAG with debris reopening, powered winch (duty cycle, brake, emergency return), dwell and edge-transfer metrics. 11 tests.
- **World 4 module:** tide controllers (event ramps, fixed tracks, High Tide Start), sea in/out exchange with an exact ledger, current-multiplier validation, relay power links with flood hysteresis, dock-queue metric. 9 tests.
- **World 5 module:** authored lava DAG with published arrivals, cooler cycle (50/200/50), finite coolant works, temporary shelters with exact protection accounting. 8 tests (RL01–RL06 and more); All five worlds' rules now run headlessly.
- **Dock berths:** reserved at departure (origin held until arrival). 61/61 sim tests pass.
- **Frostline (W3) greybox complete:** M1–M5 each 18/18 (90/90), A and B 3★ everywhere; real compact layouts via `class_overrides`; mistake runs fail (W3-M3 Relaxed excepted).
- **Tidemark (W4) greybox complete:** M1–M5 each 18/18 (90/90), A and B 3★ everywhere; mistakes fail in all profiles.
- **Cinder Reach (W5) greybox complete:** M1–M5 each 18/18 (90/90), A and B 3★ everywhere; mistake runs fail (W5-M1 Relaxed excepted).
- **Emberwood (W2) greybox complete:** M1–M5 each 18/18 (90/90); M3/M4 use the new store pumps and fires burning at S=0. W2-M3 plan A has one Relaxed-only step (t=6000, after the Standard end).
- **All 25 missions validated headlessly: 450/450 runs, A and B 3★ everywhere.**
- **Alder Valley (W1) greybox complete:** M1–M5 each pass 18/18 (90/90). Each has strategies A and B (3★ on every layout and preset) plus a documented mistake run that fails.
- **SimLab:** missions validate headlessly in seconds.
- **W1-M1 First Crossing:** 18/18 validation runs pass. No-input fails; strategies A and B win 3★ on both layouts and all 3 presets.
- **Unity game layer:** First Crossing plays end-to-end in Unity on the real engine — board, water, actors, tool tray, goals and routing.
- **Unity, all worlds (23 Oct, Editor only):** all 25 missions synced and loadable from a mission picker (layout and rules toggles). Per-world terrain palettes. Hazard overlay for fire, lava, avalanche debris and wetness; stability labels; tide gauge; cable lines; crawler and winch cabins; meshes for every W2–W5 tool. Tapping placed equipment cools, refills or switches it. Relay stops and staging nodes are pickable. Captures: `docs/progress-worlds-unity-20261023.png`.
- **Forecast preview:** with a tool armed over a legal socket, two isolated 300-tick forecasts (with/without) ghost cells kept dry (green), newly flooded (red) and still flooded (cyan), plus a rescue summary; 20.8 ms on the Mac (Quest needs the worker thread).
- **Guided W1-M1:** only the bridge at first; bus delivered at 42.8 s; flood clock frozen until "Got it", then barrier/skiff unlock (Editor-verified).
- **RESQ Lit shader + art import:** toon ramp, vertex AO, SH ambient, rim, atlas glint. The importer applies the kit contract (LODGroup, `_COL` colliders, one shared material). The validator writes `art/IMPORT_REPORT.md`; all sizes and triangle counts match Blender, and the wheelchair LOD0 (1,614) fails the 1,200 budget as expected. Look-dev scene: `docs/progress-lookdev-unity-20261023.png`.
- **Input:** mouse, hand pinch/poke and head-dwell adapters, all on one command path.
- **Art plan:** world-class pipeline adopted from the Waz techniques; see the `BUILD_PLAN.md` art section.

## In progress (helpers)
- Blender look-dev quartet: cottage, bridge, skiff, resident.

## Engine gaps fixed 23 Oct (see DECISIONS)
Relay hand-off, cable sampling, closed-dock wait, finished skiffs mooring aside, known-closure admission, moored members. W4-M3 and W5-M2/M3/M4 retuned.
- **Full rerun:** 25/25 missions 18/18 (450/450), A and B 3★ in all 300 runs. Mistakes fail in all six configs except Relaxed in W1-M3, W2-M2, W3-M3, W5-M1 and W5-M4: strategy times are operation ticks and Relaxed halves hazard speed. Decide later whether mistakes should be scored per profile.
- **Contract note:** W4-M3 has no tide or water events, so its critical harbor objective can't be threatened.

## Known W1 gaps
- Compact layouts are still the wide graph; the recipe's compact dock swap needs `class_overrides`.
- The W1-M3 mistake run is decided differently in Relaxed: strategy times are operation ticks.
- Thin margins: W1-M5 plan B channel about 1080 of 1200 mm; W1-M4 clinic pad 56 mm against the 100 mm limit.

## Next
0. W2–W5 mission content through SimLab (helpers per world).
1. Forecast on a worker thread (Quest budget); fire/lava forecast layers.
2. Swap greybox actors for the art kit (ActorKit) and fix the wheelchair budget.
3. XR wiring: MRUK placement, anchors, ISDK gaze in the operation scene.
4. Hub/briefing/debrief UX; per-world art kits.

## Waiting on the user
- Meta VR Start membership and Devpost registration.
- The "RESQ" app in the Meta VR Developer Dashboard, then `metavr auth login`.
- **Disk:** 7.8 GB free (other projects' temp files grew). Headless work is fine; Unity/Blender work needs ≥ 10 GB, and the plan needs ~40 GB by 11 Nov.
- **Credits:** caps raised to Meshy 600 / Higgsfield 150. Higgsfield balance is 110, so top up if you want the full 150.
