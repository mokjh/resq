# RESQ status — 23 Oct 2026, 15:30 (day 1 of 26)

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
- **Art kit on the board (23 Oct):** `ActorKit` (Resources) swaps residents (adult/child/elder by a deterministic mix; step-free residents in the wheelchair), skiffs and bridges (length-fitted to their gap) to the art kit, with group-colour bases. Decorative cottages sit beside each group's start node on free flat ground, with no colliders, so picks are never blocked. Wheelchair LOD0 is now 1,200 triangles (was 1,614); the art validator passes every check. Contact sheet for the look review: `docs/look-review-contact-sheet-20261023.png`.
- **Forecast preview:** with a tool armed over a legal socket, two isolated 300-tick forecasts (with/without) ghost cells kept dry (green), newly flooded (red) and still flooded (cyan), plus a rescue summary; 20.8 ms on the Mac (Quest needs the worker thread).
- **Guided W1-M1:** only the bridge at first; bus delivered at 42.8 s; flood clock frozen until "Got it", then barrier/skiff unlock (Editor-verified).
- **RESQ Lit shader + art import:** toon ramp, vertex AO, SH ambient, rim, atlas glint. The importer applies the kit contract (LODGroup, `_COL` colliders, one shared material). The validator writes `art/IMPORT_REPORT.md`; all sizes and triangle counts match Blender, and the wheelchair LOD0 (1,614) fails the 1,200 budget as expected. Look-dev scene: `docs/progress-lookdev-unity-20261023.png`.
- **Input:** mouse, hand pinch/poke and head-dwell adapters, all on one command path.
- **Art plan:** world-class pipeline adopted from the Waz techniques; see the `BUILD_PLAN.md` art section.

## Look 3 toward the concept art (23 Oct, Editor only)
- **HUD:** glass objectives card, resource chips, glass tool cards, hint bar, hologram placement ghost, dashed cyan routes, Atkinson Hyperlegible.
- **Terrain:** one natural heightfield (cell centres exact, rounded irregular cliffs), `Resq/Terrain` shader (grass speckle, rock striations), wooden diorama frame.
- **Hero assets on the boards (paid, logged):** 16 textured Meshy models (480 Meshy + 28.5 Higgsfield credits). Prepared by `tools/blender/hero_prep.py`: bases cut, scaled to board mm, pivots grounded, boat logo painted out. Each world's houses are the residents' homes. Church, lighthouse and lookout stand on high ground toward the back. The bus, rescue boat and bulldozer replace the greybox vehicles; the tanker and snowcat are parked by the depot. All 80 hero import checks pass.
- **Nature kits on the boards:** all 130 Blender assets pass every check; the six that failed (W3 marker pole and snow fence, W4 crate stack, W5 shrubs a/b and olive a) are fixed in `tools/blender/nature.py`. 115 non-backdrop assets are in Unity (100 kit entries). Scatter follows the natural terrain height.
- **Residents (paid, logged):** three rigged Meshy figures (woman, man, elder; 105 Meshy + 4.5 Higgsfield credits, user-approved). Prepared by `tools/blender/resident_prep.py`: 32–35 mm, 1,192 triangles, 1K texture, a 1 s looping walk; the standing pose is the walk's feet-together frame. Children are the woman rig at 0.72 scale with a 1.28× head. Wheelchair residents are the woman rig posed seated, in the kit chair. A well-mixed hash varies each group.
- **Crowds:** rescue crew stand at every evacuation landing (3 per landing, hi-vis orange jackets, white helmets, no group base). Rescued residents gather around them. Waiting groups stand in front of their own homes.
- **Golden hour and backdrops:** each world has its own low warm sun from the player's front-left, plus a matching trilight and ambient probe (`WorldLighting`). The Blender backdrop tiles ring the board: three along the far side and two down each side, flush with the player's edge and 80% height at the sides, with an earth plinth to the table. Frostline's ground is now bright snow (rock only on outcrops); Cinder Reach uses warm basalt browns.
- **Art validator:** all checks pass, 0 fail (`art/IMPORT_REPORT.md`, automated). Contact sheets v2–v4: `docs/look-review-contact-sheet-v{2,3,4}-20261023.png` (Editor captures, not device).
- **Render budget (automated, Editor):** all 25 missions on both layouts are within the Quest budgets: 87–144 draw calls (≤150) and 138–290k triangles (≤300k). Worst cases are W5-M5 (draws) and W1-M5 (triangles). Before the fixes: 175–264 draws and up to 649k triangles. Report: `docs/perf-budget-20261023.md`; tool: `tools/unity/perf.sh`. Garbage per frame needs a device development build to measure (the Editor alone makes ~14.8 KB).
- **Quest APK (automated build, not run):** `build/resq-xr.apk`, 107 MB, built 23 Oct from `Operation_XR` (IL2CPP ARM64, release, debug keystore). Manifest checked with aapt:
  - Identity: `com.playresq.resq` 0.1.0 (code 1), minSdk 32 / targetSdk 34, Horizon OS SDK 60/207.
  - Permissions: hand tracking, anchors, scene, boundary visibility; no INTERNET.
  - Quest features: passthrough, `quest3|quest3s`, VR launch category, focus-aware; not debuggable.

  Not yet run on SpatialSim or a device. During the build, free disk fell to ~2.7 GB: the Android cache grew ~8.6 GB, and the watchdog's process match missed. Both are fixed in `tools/unity/build_apk.sh` (19 GB guard, kills only the RESQ Editor's build children, clears intermediates afterwards).
- **XR wiring (simulator, macOS):** see `docs/xr-sim-verification-20261023.md`.
  - Table placement (MRUK; wide, compact or virtual table) is verified in 40 sample rooms (automated) and in the Meta XR Simulator.
  - The spatial anchor saves and restores.
  - Interaction SDK hands are tracked as pointers.
  - Head-gaze dwell fires through the real XR head pose.
  - Three XR bugs were found and fixed: the start race, the rig collider swallowing rays, and the restored virtual table missing.
  - Not yet checked (device, P5): real rooms and the permission prompt, pinch presses, relocalisation.
- **PlayMode smoke gate (automated, Editor):** `bash tools/unity/smoke.sh` runs 6 cases; it passed 4 of 4 full runs.
  - For each world's M1: boot, board and art checks, render budget at step 300 (≤150 draw calls, ≤300k triangles, worst frame), then strategy A won at 3 stars. Every step is played through real pointer presses: place, vehicle route, walk route, enable/disable, cool.
  - Command path: W1-M1 under Command rules, driven only by the head-dwell pointer with real 0.8 s dwells.
  - Mutations caught: full-detail scatter (over budget), a 0.1 s dwell (fired too early), and the old pickup rule (W3-M1 lost), plus the four earlier ones.
  - Real bugs it found: the winch pickup (the UI sent cable vehicles to ground pickups; W3-M1 couldn't be won from the UI) and stale collider picks (also fixed in the dwell pointer).
- **Remaining gaps:** golden-hour lighting is untested on Quest (cost and how it looks over passthrough); backdrop rings are procedural (the Blender backdrops aren't used yet); the W3 snow reads grey and W5 reads dark.

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
3. XR wiring: MRUK placement, anchors, ISDK gaze in the operation scene.
4. Hub/briefing/debrief UX; per-world art kits.

## Waiting on the user
- Meta VR Start membership and Devpost registration.
- The "RESQ" app in the Meta VR Developer Dashboard, then `metavr auth login`.
- **Disk (23 Oct, 22:10):** 11 GB free after two cleanup passes (caches, stale temp and build output, unused SDK/tool versions). SpatialSim v207.0.0 is installed (8.7 GB). Its run is blocked: it also needs the Android Emulator package (~1 GB) plus run space, so `tools/ssim/smoke.sh` needs ≥ 15 GB free. A rebuild needs ≥ 19 GB, and the plan needs ~40 GB by 11 Nov. The rest is project data, so it is your call: Codex worktrees 6.7 GB (all three have uncommitted changes), Egytric `.next-*` build copies ~5 GB (referenced by its docs), the older `Waz Vr/game/Library` 2.9 GB, an orphaned Chrome Canary profile 1.5 GB, inactive `node_modules`; a restart also releases swap.
- **Credits:** spent so far: Meshy 585 of the 600 cap; Higgsfield 33 of 150, with a balance of 8.9. Top up Higgsfield if you want more concepts.
