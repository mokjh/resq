# RESQ status — 5 Nov 2026

**Phase:** P1–P3 software build on the Mac; physical Quest tests stay last (P5, 12–15 Nov). Plan: `BUILD_PLAN.md`. History of earlier milestones: `git log` and `DECISIONS.md`.

## Gates (latest results)
- **Sim (automated, headless):** 68/68 (`python3 tools/simtest/simtest.py sim`), re-run 5 Nov.
- **Mission matrix (automated, headless):** 25/25 missions × 18 configs = 450/450; strategies A and B win 3★ everywhere; re-run 5 Nov.
- **Challenges (automated, headless):** 30 challenges × 2 layouts × 2 plans = 120/120 win (`python3 tools/simtest/simtest.py lab challenge all`); re-run 5 Nov.
- **Input matrix (automated, Editor): not yet run.** All 25 missions through hand ray, one-hand mirrored and head dwell (`GATE=matrix.sh tools/unity/gate_when_free.sh`, about 30 to 45 min). The first attempt on 5 Nov started no tests: the script left out `include_explicit`. Fixed, and it now fails within 5 minutes if no run starts.
- **PlayMode (automated, Editor):** 20/20 on 5 Nov (`bash tools/unity/smoke.sh`, or `tools/unity/gate_when_free.sh` to wait for a free machine), now including hand shadows and FoV:
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
- **Quest APK:** `build/resq-xr.apk` built 5 Nov (110 MB, commit 5c593ef, SHA-256 starts e05dfab8). Manifest checked: min SDK 32, target 34, `quest3|quest3s`, VR intent, not debuggable, `excludeFromRecents`, hand tracking required, no INTERNET. **Debug-signed, and never run on a headset:** it predates this week's features. The Competition channel needs a release-signed build (key exists, build needs 21 GB free). SpatialSim can't run immersive apps (see DECISIONS).

## Not done yet (in plan order)
1. **Your review of the audio:** listen to `game/Assets/Resq/Content/Resources/ResqAudio/` or play a mission in the Editor.
2. **Website:** "The Planning Table" is live at https://resq-woad.vercel.app (redeployed 30 Oct with your OK). Scrolling the hero plays the real First Crossing simulation, valleys assemble block by block, the footer has a shelf of valleys, and the grid background is gone. Checked live on phone and desktop.
3. **Trailer:** Simulator-only rough cut in `video/exports/` (72 s, QC pass, choppy, not final). Full board kept in `video/edl-final-board.json`; device clips in P5.
4. **Devpost:** draft in `docs/submission/devpost-draft.md` (498/500 words) for your review; `[P5]`, `[G]` and `[you]` markers remain.
5. **MR polish:**
   - **room weather, real-hand cut-through, captions, foveation: written 5 Nov.** Weather seen in Editor captures only, and those predate the rain retune. `RealHandsView` and `RoomMoodXr` compile but have never run, not even in the Simulator;
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
7. **XR flow check: done 5 Nov (Meta XR Simulator, Quest 3 profile).** Passed: boot, welcome, virtual table, move-board controls, anchor saved on Done, anchor restored on relaunch to the exact moved pose, table found in an MRUK sample room (0.63×0.60 m -> compact), Virtual table reset, hand-loss pause (both hands off -> U16 -> Resume), hand-shadow feeder on real joints. Then, with the XR Operator layer (`tools/xr/`, no sudo), `hands_test.py` passed a hands-only journey with real Interaction SDK pinches: Hub, Briefing, run, tool card, socket, Start. Real pinch feel and real rooms remain for P5.
   - **Found and fixed:** a chosen table behind the player left them with only their hands in view and no clue. `BoardFinderView` now shows "Your valley is behind you / this way" with an arrow (lazy-follow, no input). Seen in the Simulator at its first size; the final 1.6× scale is covered by `BoardFinderTest`.
   - Also fixed: tables beyond 1.6 m are out of reach, so the board uses the virtual table (the Simulator's nearest fitting table was 5 m away); "Emergency Bridge" is "Bridge" on its card.
   - **Limits:** `metavr xrsim record` is not supported on macOS. `tools/xr/shoot.py` films composited frames instead (2 to 6 fps captured, so choppy); device clips replace them in P5. 36 fps in the Simulator with the Editor on this Mac says nothing about the headset; measure in P5.

## Waiting on the user
- **Submission checklist: `docs/submission/READY.md`** (what's ready, what needs the owner, the order). Nothing is submitted or uploaded; the user said not to for now.
- **Entry route (blocks channel work):** RESQ can only go in beside Waz World as a declared team: Mohamed Tarek submits as Team Representative from his own accounts and his own Start membership, and Amir is a listed non-Representative member. Otherwise, one game only. The user set this aside on 5 Nov; don't raise it until they do. Steps in `READY.md`.
- RESQ's Meta team and the "RESQ" app, once the route is decided. `metavr` is signed in as Amir.
- **Memory:** 8 GB RAM with heavy swap. The Editor crashed once (Meta ISDK telemetry plugin, 24 Oct 00:08) while other projects' Unity runs and apps were open.
- **Disk (blocking all Unity work):** 0.2 to 3 GB free on 5 Nov, 07:00 to 07:15. The Trash holds 21 GB: 15 `chrome_code_sign_clone` folders of 1.4 GB each, most dated 06:04 to 06:18. RESQ is 4.6 GB. Unity work needs ≥ 10 GB free, the signed build ≥ 21 GB, the plan ~40 GB by 11 Nov. RESQ's Editor exited at 07:04 with no crash report (cause unknown); open it again once there is room.
- **Backup (no git remote):** full repo on the phone at `/sdcard/Download/RESQ-archive/` (29 Oct 00:00, commit e9c6471): git bundle + LFS objects tar + restore note. Restore was tested: 2,734/2,734 tracked files identical. Refresh it after big milestones.
- **Credits:** Meshy 600/600 (cap reached); Higgsfield 33/150 (balance 8.9).
