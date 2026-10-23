# RESQ status — 24 Oct 2026 (day 3 of 26)

**Phase:** P1–P3 software build on the Mac; physical Quest tests stay last (P5, 12–15 Nov). Plan: `BUILD_PLAN.md`. History of earlier milestones: `git log` and `DECISIONS.md`.

## Gates (latest results)
- **Sim (automated, headless):** 68/68 (`python3 tools/simtest/simtest.py sim`).
- **Mission matrix (automated, headless):** 25/25 missions × 18 configs = 450/450; strategies A and B win 3★ everywhere.
- **PlayMode (automated, Editor):** 11/11 (`bash tools/unity/smoke.sh`):
  - each world's M1 won through real pointer presses, with the render budget checked;
  - the Command path won through head dwell;
  - table placement in 40 MRUK sample rooms;
  - the game flow (4 tests).
- **Render budget (automated, Editor):** all 25 missions within 150 draws / 300k tris (`docs/perf-budget-20261023.md`).
- **Art validator:** 0 FAIL.

## What works (Editor / Meta XR Simulator; not device-tested)
- All 25 missions, five world rule sets, forecast preview, guided W1-M1, hazard overlays.
- Hero assets, nature kits, rigged residents and golden-hour lighting.
- **XR (simulator):** MRUK table placement (wide/compact/virtual), spatial anchors, Interaction SDK hands, head-gaze dwell.
- **Game flow (new 24 Oct):** these screens float above the board as a glass card that faces the player, all through the same input layer as play:
  - boot and save validation;
  - first welcome with comfort choices;
  - placement calibration (XR);
  - hub, mission select (world tabs, stars, unlocks), briefing with the kit;
  - pause with Menu always on the run controls;
  - results, causal debrief with one lesson, aftercare with a postcard;
  - settings, help, credits/privacy;
  - tracking and anchor recovery;
  - save recovery (backup offered, nothing erased).
  - Progress and settings persist through atomic saves with a backup.
  - Each world's M1 is always open; Open play unlocks all.
- **Comfort settings:** text 100/125/150 % (menus), high contrast, reduced effects (no flicker or pulsing), left-hand mirroring of run controls, dwell time 0.5–1.5 s, hands-free mode.
- **Quest APK:** `build/resq-xr.apk` (23 Oct, before the game flow); manifest checked. SpatialSim can't run immersive apps (see DECISIONS).

## Not done yet (in plan order)
1. **Settings polish:** text scale on the HUD and tray, high contrast on the HUD, captions (no speech yet).
2. **Challenges:** 30 challenges in sim and validator; unlocks in the mission screen.
3. **Audio:** music, ambience, SFX and the rescue chime.
4. **Forecast and autopilot:** forecast on a worker thread (Quest budget); dev autopilot for P5; store packaging (icon, splash, `excludeFromRecents`).
5. **Website, trailer and Devpost:** the trailer is cut from XR Simulator recordings.
6. **MR polish and resident clips:**
   - room-aware storm and hand shadows;
   - resident clips beyond walk;
   - aftercare valley-repair visuals.
7. **XR flow check in the simulator:** move board, virtual table, anchor saved on Done, hand-loss pause. The code is written but not yet run in the simulator.

## Waiting on the user
- Meta VR Start membership and Devpost registration; the "RESQ" app in the Meta dashboard, then `metavr auth login`.
- **Memory:** 8 GB RAM with heavy swap. The Editor crashed once (Meta ISDK telemetry plugin, 24 Oct 00:08) while other projects' Unity runs and apps were open.
- **Disk:** 7.5 GB free at last check; a rebuild needs ≥ 19 GB, and the plan needs ~40 GB by 11 Nov.
- **Credits:** Meshy 585/600; Higgsfield 33/150 (balance 8.9).
