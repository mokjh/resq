# RESQ status — 28 Oct 2026

**Phase:** P1–P3 software build on the Mac; physical Quest tests stay last (P5, 12–15 Nov). Plan: `BUILD_PLAN.md`. History of earlier milestones: `git log` and `DECISIONS.md`.

## Gates (latest results)
- **Sim (automated, headless):** 68/68 (`python3 tools/simtest/simtest.py sim`).
- **Mission matrix (automated, headless):** 25/25 missions × 18 configs = 450/450; strategies A and B win 3★ everywhere.
- **Challenges (automated, headless):** 30 challenges × 2 layouts × 2 plans = 120/120 win (`python3 tools/simtest/simtest.py lab challenge all`).
- **PlayMode (automated, Editor):** 17/17 on 31 Oct (`bash tools/unity/smoke.sh`, or `tools/unity/gate_when_free.sh` to wait for a free machine), now including hand shadows and FoV:
  - each world's M1 won through real pointer presses, with the render budget checked;
  - the Command path won through head dwell;
  - table placement in 40 MRUK sample rooms;
  - the game flow, Command step controls and challenges (7 tests);
  - audio wiring and the forecast worker thread.
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
- **Challenges in game:** unlock after a base win, chosen on the briefing, Standard rules, scored separately.
- **Command profile:** +1 s / +5 s step controls; Restart asks first.
- **Comfort settings:** text 100/125/150 % (menus, HUD, tool tray, run controls), high contrast (menus and HUD), reduced effects (no flicker or pulsing), left-hand mirroring of run controls, dwell time 0.5–1.5 s, hands-free mode.
- **Audio (synthesised, original):** 24 effects, 5 ambiences, calm/problem/resolve stems per world; mixed by game state; follows the Settings volumes. Not yet human-reviewed.
- **Forecast** on a worker thread; **dev autopilot** for P5; **app icon and system splash** applied.
- **Quest APK:** the 23 Oct build (before the game flow) is archived on the phone at `/sdcard/Download/RESQ-archive/` (SHA-256 checked) and removed from the Mac; the next `tools/unity/build_apk.sh` run makes a fresh one. SpatialSim can't run immersive apps (see DECISIONS).

## Not done yet (in plan order)
1. **Your review of the audio:** listen to `game/Assets/Resq/Content/Resources/ResqAudio/` or play a mission in the Editor.
2. **Website:** "The Planning Table" is live at https://resq-woad.vercel.app (redeployed 30 Oct with your OK). Scrolling the hero plays the real First Crossing simulation, valleys assemble block by block, the footer has a shelf of valleys, and the grid background is gone. Checked live on phone and desktop.
3. **Trailer:** pipeline ready and self-tested (`tools/video/edit.py`); storyboard and EDL in `video/`. Needs XR Simulator recordings now and device clips in P5.
4. **Devpost:** draft in `docs/submission/devpost-draft.md` (498/500 words) for your review; `[P5]`, `[G]` and `[you]` markers remain.
5. **MR polish:**
   - room-aware storm (not started);
   - hand shadows and aftercare recovery: **done** 30 Oct (Editor captures + PlayMode gate). Device check in P5. Editor captures then led to three fixes:
     - shadows were far too faint: URP blends in linear space, so 0.65 opacity darkened grass by only ~27%. Shadows now use a soft opaque core and 0.9/0.15 near/far opacity, and read clearly in the capture;
     - recovery receded to the first rendered frame, not the mission's starting water; it now uses `WInitVol` (55 wet cells vs 139 at the end of a won First Crossing);
     - one material per shadow slot.
     **Gate re-run on the fixed code: PASS 16/16 (30 Oct, 23:30; automated, Editor PlayMode)**, including HandShadowTest and the aftercare recovery asserts. While RESQ's Editor runs, pause Waz/Waz Vr sessions; their verify scripts launch batch Unity jobs. `tools/unity/gate_when_free.sh` waits for a free machine.
   Resident clips are done (28 Oct): one waver per waiting group, one cheer per safe landing.
6. **FoV-aware UI: done 31 Oct.** An audit with Meta's device-readiness skill found no controller or legacy input and no head-locked UI; everything is world-locked to the board and faces the player.
   - `FovTest` (PlayMode, in the gate) measures the live UI from the virtual-table head against Meta's 30° primary zone. Results: HUD 29.0° on all five worlds (was 31.9°), briefing card 29.1° (was 49.8° and 0.43 m away), pause card 20.8°. Reported only (reach zone): run controls 26.4°, tool tray 33.3°.
   - Changes: the menu card sits over the board's middle, 0.67 m away. The HUD spans ±0.31 m, 3.5 cm lower. Long HUD titles shrink to fit (no truncation on any of the 25 missions, checked in the Editor).
   - Found on the way: after a restart, tool cards lost their stock and cost line (stale state). Fixed, and the smoke test now checks it; it failed 6/17 without the fix.
   - Gate 17/17 (31 Oct 04:10, automated, Editor).
7. **XR flow check in the simulator:** move board, virtual table, anchor saved on Done, hand-loss pause. The code is written but not yet run in the simulator.

## Waiting on the user
- Meta VR Start membership and Devpost registration; the "RESQ" app in the Meta dashboard, then `metavr auth login`.
- **Memory:** 8 GB RAM with heavy swap. The Editor crashed once (Meta ISDK telemetry plugin, 24 Oct 00:08) while other projects' Unity runs and apps were open.
- **Disk (blocking Unity work):** 1.7 GB free at 23:10 on 28 Oct. macOS swap files reached ~9 GB on disk while Waz's Android build and PlayMode runs overlapped RESQ's Editor on 8 GB RAM. RESQ itself is 4.6 GB. The RESQ Editor needs ≥ 5 GB free to run safely, an APK rebuild ≥ 19 GB, and the plan ~40 GB by 11 Nov.
- **Backup (no git remote):** full repo on the phone at `/sdcard/Download/RESQ-archive/` (29 Oct 00:00, commit e9c6471): git bundle + LFS objects tar + restore note. Restore was tested: 2,734/2,734 tracked files identical. Refresh it after big milestones.
- **Credits:** Meshy 600/600 (cap reached); Higgsfield 33/150 (balance 8.9).
