# RESQ decision ledger

Newest first. Decisions before 22 Oct 2026 are archived verbatim in `docs/archive/process-2026-10/DECISIONS.md`.

## 22 Oct 2026 (late) — art pipeline and credit caps (user-approved)

| Decision | Result |
|---|---|
| Art pipeline | Adopt the Waz-proven routing, re-authored in RESQ (no copied code): scripted Blender kits for buildings and simple props; Meshy-t2 for organic props; Tripo (via Higgsfield) or Meshy-7.1 for landmarks and vehicles; Higgsfield sheet, then Meshy, then a shared rig plus library clips for residents. RESQ Lit toon shader. |
| Credit caps | Raised to **Meshy 600 / Higgsfield 150** for characters, landmarks and swatches. Every spend is logged in `docs/spend-log.md`. Current Higgsfield balance (110) is below the cap; a top-up is the user's call. |

## 22 Oct 2026 — studio reset and competition plan (user-approved)

| Decision | Result |
|---|---|
| Competition entry | Meta VR Start Developer Competition 2026: Gaming track, New Experience division. Submit Tue 17 Nov; the deadline is Wed 18 Nov, 22:00 Cairo. |
| Scope | The user chose to attempt all 25 missions in the entry. Per-world ship gate: a world is playable in the entry only if all five of its missions pass validation, a Simulator playthrough and a device smoke test. Otherwise it is shown honestly as "in development". Alder Valley's five missions are never cut. |
| Process | Studio mode replaces the 229-task evidence/admission regime. Gates are real tests, deterministic replays, performance budgets, playtests and user reviews. Old records are archived, not deleted. |
| Device testing | Last. Physical Quest 3/3S work happens only in phase P5 (12–15 Nov); everything before that runs in the Editor and the Meta XR Simulator (SpatialSim 207.0.0 rejects immersive apps, see 23 Oct). |
| Simulation | New lean "Sim v2": struct-of-arrays, integer, allocation-free, deterministic, covering every world's rules. The old engine is kept unchanged as a golden-value oracle in `reference/legacy-sim/`. |
| Content | Mission factory: recipe plus contract feed a layout compiler, then a headless validator (no input must fail; strategies A and B must win on both layouts and all three presets). |
| Art | Original scripted-Blender kits on one palette atlas. Paid generation capped at Meshy ≤ 300 and Higgsfield ≤ 60 credits, each spend logged with its license. |
| Machine | RESQ gets exclusive use of Unity on this Mac until 18 Nov (the user pauses Waz). Reclaimed the throwaway bootstrap Unity `Library/` cache in `evidence/` (5.7 GB). |
| Git | Local commits on `main` at each working step. No push without the user's OK. |
| External writes | Store uploads, deploys, YouTube and domain are confirmed with the user at the time. |

## 23 Oct 2026 — rule readings for the World 2 and World 3 modules (spec 02 §5, §7, §8)

| Decision | Result |
|---|---|
| Fire on non-fuel cells | Every cell takes heat, so roads and water next to a fire latch closed. A legend char with no `fuel` defaults to 1000 (spec §7), so World 2 maps must author `fuel: 0` for roads and water. |
| Collapse warnings | A published warning of any event that acts on a support (`set_stability_loss`, `set_support_load`, `tremor`, `warn_support`) counts as the relevant warning and keeps its stamp. Only an emergent warning (effective ≤ 300) is cleared when stability rises above 400. |
| `warn_support` target | Resolves to `supports` in the map's roles, not infrastructure. |
| Winch duty | Duty counts powered travelling operation ticks. Departure needs duty < 100. A winch at its limit rests empty at a safe endpoint before anyone boards; 30 rest ticks reset duty to 0. |
| Cable edges | Open while a winch is placed, unbroken and its support stands. Power governs movement (brake, then a one-time emergency return), not edge admission. |
| Runout | Segments start at the release (or bound collapse) tick; successors start exactly `delay` ticks after their predecessor. Nodes are unsafe while a segment settles; `debris_reopenable` edges reopen after `settle`; other edges stay closed. |

## 23 Oct 2026 — rule readings for the World 4 module (spec 02 §4, §5, §9)

| Decision | Result |
|---|---|
| Sea boundary | A map `tides` entry names its boundary grid cells. Sea head = `datum` + level. Each boundary cell exchanges with the sea through a passive link (sill = cell bed, default conductance 2,000,000). Inflow is added independently; outflow competes with the cell's other outward requests. Per-controller `SeaIn`/`SeaOut` ledgers keep volume conservation exact. |
| Controllers | One per boundary: an event ramp (`set_tide_segment`) or a fixed knot track (optional loop period). The compiler rejects event ramps on a track controller and segment indexes outside 0–3. |
| High Tide Start | `MissionDef.TideStartIndex` sets the initial level with a degenerate ramp at S=0; events keep their authored ticks. |
| Relay | A power link object `{from, to, relay}` binds a relay socket. The link carries power while the relay is placed, enabled, intact and connected. It drops above 50 mm and reconnects by itself at ≤ 40 mm. Relays start enabled on placement. Every relay socket must bind a link. |
| `warn_support` in World 4 | When its role binds infrastructure rather than a support, the operation is advisory only. |
| Dock queue metric | Longest run of consecutive operation ticks a vehicle waits because its target dock's only berth is taken. |
| Berths (fixed 23 Oct) | A skiff reserves its destination berth at departure and keeps its origin berth until arrival. A normal arrival frees the origin; a turned-back skiff frees the destination. Two skiffs can no longer share a one-berth dock. |

## 23 Oct 2026 — rule readings for the World 5 module (spec 02 §5, §10)

| Decision | Result |
|---|---|
| Lava schedule | The `advance_lava` warning publishes every downstream arrival (earliest path, `LPlanned`). Activation schedules the same arrivals; each segment receives its first arrival once. The compiler rejects lava events with under 100 ticks of warning, and successor delays under 50. |
| Lava hazard | A crossing edge is open only while its segments are Dormant or Crusted. A node bound to a segment (or standing on its cells) is unsafe while that segment's heat is ≥ 600: Hot, Cooling or Reheating. |
| Coolers | Activation is the existing `CoolSegment` command on a placed cooler. It needs an enabled socket, a Hot segment and a charge. A refused activation keeps the charge. `RefillTool` moves one charge from the socket's bound coolant store only when the cooler is empty. |
| Coolant works | An infrastructure entry with `producer {store, feedstock, ticks}`. It runs while that infrastructure is operational (power and safety from stage 4). Feedstock is reserved at job start; a job starts only below store capacity. `resource_remaining` on a coolant store counts charges. |
| Shelters | A placed shelter on a `shelter` node (capacity 1–6, never a refuge). Each hazard pulse it consumes protection when occupied and its node is unsafe or its hazard region is hot. That interval's protection masks resident exposure and shelter damage. `enable_shelter_hazard` makes the shelter node itself unsafe. |
| Arrival damage | A Hot arrival (or a return to Hot) breaks equipment on the segment's cells, except coolers bound to that segment and shelters with protection left. Spent coolant is never a loss. |

## 23 Oct 2026 — engine gaps closed for World 2 (reported by the W2 mission helper)

| Decision | Result |
|---|---|
| Pumps and stores | A pump `intake`/`outlet` may name a finite store, so tank ↔ pond ↔ cell pumping works (spec 02 §4). This previously crashed the compiler. |
| Initial fire | `fire.burning` cells start Burning at S=0, and a legend `heat` sets initial heat. These are compiled initial state, not tick-zero events (spec 02 §2). W2-M4's contract has only wind events, so its fire must already be burning. |

## 23 Oct 2026 — engine gaps from the mission helpers, fixed

| Decision | Result |
|---|---|
| Relay hand-off | A vehicle keeps waiting at its pickup while group members ride another vehicle towards that pickup. A resident dropped at a non-refuge node waits there (`RStay`) until boarded or given a new group order. Members still walking keep the group's order (previously the whole group's order was cleared). |
| Cable sampling | A cable edge without an explicit `sample` checks only its two end stations, not the ground beneath it. |
| Closed-dock wait | Spec 03: cumulative ticks a loaded, stationary vehicle waits while its chosen unload destination is closed. Berth waits no longer count. |
| Dock queue | Also counts waiting at the dock for receiving capacity. |
| Finished skiffs | A skiff whose order is done moors aside: it frees its berth but stays at the node. It must reclaim a free berth before loading or unloading there again. A loaded boat is never blocked by an idle one. |
| Hard-closure admission | Spec 02 §6: no traveller enters an edge if a known hard closure lands on it before the predicted arrival. Known closures are a warned `set_edge_enabled(0)`, a published lava arrival, a running crust's expiry, and a warned or started runout segment. Predicted depth/heat changes are not "known"; transit recovery handles them. |
| Moored members (follow-up) | A resident moored at a transfer node is awaiting a new order. It no longer counts as heading to, or still to board at, another vehicle's pickup, so a loaded boat departs instead of waiting for it. |

## 23 Oct 2026 — art direction: miniature realism (user concepts)

| Decision | Result |
|---|---|
| Target look | The user's four concept frames (Alder Valley ×2, Tidemark, Emberwood) are the style target: model-railway miniature realism at golden hour, dense life, textured hero buildings and vehicles, whitewater, lit windows, glass HUD chips, cyan holographic placement ghosts, contextual "Pinch to place · Rotate with wrist" tips. They are concept art (AI-generated), never presented as gameplay footage. |
| Quest-achievable path | Textured terrain with natural rounded cliffs over the unchanged sim grid; textured hero assets (paid generation within caps); scattered vegetation, crowds and props via instancing; whitewater driven by the simulated flow; per-world golden-hour lighting with emissive windows and cheap particles; the concept HUD rebuilt in-engine. Not reproduced: photographic depth of field, volumetric smoke, ray-traced reflections. |
| Status | Look 2.0 step 1 (procedural terrain, scatter, backdrop ring, foam) is the foundation; the vertex-colour faceted placeholders are interim. |

## 23 Oct 2026 — Hero and nature kits on the boards
- **Hero prep:** Meshy models keep their own 1K textures and use the `Resq/Hero` shader, not the vertex-colour kit. The shader has the same toon light model, with baked shading softened by `_Flatten`. Cut heights per asset are in `tools/blender/hero_prep.py` and were set from survey renders. Scale: the longest side matches the target; tall landmarks may stretch up to 1.3× in Z.
- **Orientation:** verified in an Editor lineup, buildings face −Z (yaw 180) and vehicles point −X (yaw 90).
- **Placement:** homes use the world's house landmarks, never the generic cottage when the world has its own. Scenic landmarks (church, lookout, lighthouse) take the highest free ground toward the back, never the rim. Parked vehicles go next to the depot. Presentation only: no colliders, and the scatter is masked under every footprint.
- **Nature:** an asset that fails any Blender check stays out of the Unity kit until it's fixed. Backdrops aren't used yet.

## 23 Oct 2026 — Residents and crowds
- **Residents:** three Meshy-rigged figures (woman, man, elder) replace the vertex-colour figures (user-approved spend: 105 Meshy + 4.5 Higgsfield). Children are made from the woman rig (0.72× with a 1.28× head) rather than a fourth paid model. Wheelchair residents are the woman rig posed seated (a one-frame clip) in the original kit chair, so step-free residents keep the same style as everyone else.
- **Animation:** one looping walk per figure. Standing residents hold the walk's feet-together frame, which costs no extra idle clip. Each resident starts at its own phase so groups don't march in step. Presentation only: the simulation moves residents and never reads animation.
- **Crowds:** for readability, the only people at a home are residents who need rescuing, so no ambient townsfolk stand there. Landings get presentation-only rescue crew instead. They wear hi-vis orange jackets (a recoloured copy of the man's texture) and white helmets, have no group base and stand clear of the arrival paths. Rescued residents gather around them.
- **Import gates added:** a rigged resident must be one skinned mesh of at most 1,200 triangles, 30–36 mm tall, with a looping walk clip and its texture. These checks caught a 1.9 m figure (object-level scale keys in the Meshy action) and a 250-frame bake before either reached the board.

## 23 Oct 2026 — Golden hour, backdrops, W3/W5 palettes
- **Lighting:** one preset per world, applied on every board build relative to the board's own yaw, so the look holds wherever the diorama sits in the room. The sun comes from the player's front-left at 24–30° elevation, so the faces and facades toward the player are lit. The SH ambient probe is rebuilt from the trilight, because the RESQ shaders read SampleSH. Emberwood and Cinder Reach were toned down after a first pass read too orange.
- **Backdrops:** Blender tiles replace the procedural ring wherever a world has them (the procedural ring stays as the fallback). Imported tiles face −Z (verified in the Editor). The player's side stays open. Side rows are squeezed lengthwise to end exactly at the player's edge; squeezing both tiles the same way keeps their edges matched. Backdrops cast no shadows, to save the shadow pass on Quest.
- **Palettes:** the Frostline "#" filler (code 3) is snow-covered ground, not bare rock. Cinder Reach basalt goes from near-black (0x3B3836) to warm grey-brown (0x574A42), with warmer strata.

## 23 Oct 2026 — PlayMode smoke gate
- **Shape:** one Editor PlayMode test (`Assets/Resq/Tests/PlayMode/OperationSmokeTest.cs`), run by `tools/unity/smoke.sh` through the open Editor's bridge. PlayMode entry reloads the domain, so the run is started asynchronously and polled. Input is a scripted `IPointerSource` that casts real rays at pickable targets: hover, press, release. Nothing calls `Submit` directly and `DebugHover` isn't used. Time runs on the game's command-mode `Advance`, so strategy tick timing is exact. Thirty real-time frames mid-run exercise the live visuals. Unity's default fails the test on any logged error or exception.
- **Bug found:** with `autoSyncTransforms` off (the project default), pointer picks between fixed steps tested last frame's collider positions for handles that move in LateUpdate (vehicles, groups). The test failed 3 of 9 runs on a just-placed skiff. `BoardInteraction.Pick` now calls `Physics.SyncTransforms()` first. Since then: 6 of 6 passes.
- **Scope:** W1-M1, wide layout, Standard rules, desktop pointer path. The hand, one-hand and head-dwell adapters share `BoardInteraction`, but nothing exercises them yet.

## 23 Oct 2026 — Render budget trims
- **Scatter:** LOD1 draws the instances and LOD2 casts their shadows. World 1 woods are thinned slightly: broadleaf density 0.45→0.32, conifer 0.30→0.36, shrubs 0.30→0.24.
- **Terrain:** a coarse heightfield proxy casts the shadows; the full-detail terrain only receives them.
- **Crowds:** standing figures (residents, seated riders, crew) are a baked pose drawn instanced, so their skinned renderers and Animators run only while walking. `BakeMesh(useScale: false)` gives world-size vertices relative to the renderer, so they draw with position and rotation only (verified 23 Oct).
- **Shadows:** skinned figures, water, overlays, markers, routes and the ghost don't cast. The coloured group base is the figure's ground anchor.
- **HUD:** `GlassBatcher` merges the static glass on each root into one mesh; moving bubbles are merged per frame without allocations. Chips and tool cards each use one text object. Text refreshes at 5 Hz. The dev picker shows only in the Editor and development builds (`OperationPanel.DevControlsOverride` overrides it).
- **Measurement:** `perf.sh` measures the shipping HUD at strategy-A step 300 from the seated view. Editor numbers are a budget screen; device numbers come in P5.

## 23 Oct 2026 — Smoke gate: every world's M1, the Command path, the render budget
- **Strategy player:** the test plays each mission's validated strategy A through pointer gestures. The player acts at t−1 (when the replay submits); steps sharing a tick are all performed, one step applies them, then each is checked. The Start/Pause control is exercised first, and the run then restarts, so dwell time can't shift strategy ticks.
- **Command path:** the game's own `HeadDwellPointer` fed by a test head transform. A selection that fires in under 0.75 s fails the test ("inspection must never activate").
- **Budget check:** worst frame over 60 rendered frames at step 300, shipping HUD, seated view; ≤150 draw calls and ≤300k triangles. The test fails if the Game view didn't render every frame, rather than passing on empty counters.
- **Game fix: vehicle pickup.** `BoardInteraction.PickupNodeFor(group, vehicle)` uses the authored pickup when the vehicle can reach it. Otherwise it uses the nearest node that the vehicle reaches and the group can walk to (a winch serves only its cable stations). Before this, W3-M1's winch plan was impossible from the UI.
- **Game fix: dwell picks.** `HeadDwellPointer` syncs physics transforms before its cast, matching `BoardInteraction.Pick`.

## 23 Oct 2026 — XR wiring
- **Assembly split:** XR lives in `Resq.XR` (Meta Core, Interaction SDK, MRUK); `Resq.Game` has no Meta SDK dependency. GameBootstrap exposes `XrPlacement`, `XrPointers` and `BeginRun(layout)`.
- **Placement order:** saved anchor first, then the nearest fitting MRUK table, then a virtual table. Fit order: wide facing, wide turned, compact facing, compact turned (footprint includes the frame, plus a 2 cm margin). The scene uses device data only; a fallback prefab room would put the board on a table that isn't in the player's room. MRUK sample rooms load only through an Editor-only dev hook.
- **Anchor:** one `OVRSpatialAnchor` per valley, saved with its layout and surface type. The board is parented to it. A stale anchor is forgotten and placement starts afresh.
- **Input:** our own `IPointerSource` adapters over the Interaction SDK's `IHand`, not ISDK interactables, so every adapter feeds the one validated command path. Head-gaze dwell runs in the Command profile; gaze + pinch is an accessibility option.
- **Picking:** casts take the nearest pickable hit and ignore non-game colliders (rig, room). `GameBootstrap` waits for OpenXR before choosing between desktop and headset.

## 23 Oct 2026 — First XR APK, build disk budget
- `evidence/` (2.0 GB, untracked, process-era records) was deleted with the user's approval to make room for the build.
- **Build disk budget:** an IL2CPP ARM64 build grows `game/Library/Bee` by ~8.6 GB at peak, even from a warm Editor cache. `build_apk.sh` therefore:
  - needs ≥ 19 GB free to start;
  - watches the disk and stops only the RESQ Editor's build child processes below 5 GB;
  - deletes the regenerable Android intermediates afterwards. A rebuild is slower but leaves the disk as it found it.
- **Output path:** passed as an absolute path; Unity resolves relative output paths against the project folder (`game/`).
- **SpatialSim is not a RESQ test target (23 Oct):** the emulator renders 2D panels only and rejects immersive (OpenXR) launches, so it can't exercise placement, anchors or the permission prompt. It is kept only as an install/manifest check. `tools/ssim/smoke.sh` now exits 3 ("inconclusive") when the launch is rejected or no `[RESQ XR]` lines appear, rather than reporting "0 fatal lines" as a pass. Those paths stay covered by the Meta XR Simulator, the 40-room automated test and P5 on device.
- **Game flow (24 Oct):** U01–U24 are built as one data-driven menu card (`MenuPanel` + `GameFlow`), not 24 prefabs. Every control is a `Pickable` button, so menus use the same input layer as play (hands, either hand alone, head dwell, mouse). While a menu is open, `BoardInteraction.MenuOnly` lets only buttons respond. Briefing text comes from the world bible via `tools/content/briefings.py` → `content/briefings.json`; nothing is hand-copied. The save is a local JSON with atomic writes and a last-good backup. A damaged save is never overwritten without the player's choice (U24). Each world's M1 is always unlocked so judges can try every world; later missions unlock in order; Open play unlocks all. With the flow, the XR spatial anchor is saved when the player confirms the calibrated pose, not straight after placement. Atkinson Hyperlegible has no ★/✓ glyphs, so stars and objective results are written in words.
- **Challenges (24 Oct):** all 30 named challenges are implemented as transforms of a mission's map and contract (`Sim/Runtime/Content/Challenges.cs`), shared by the game and SimLab. `lab challenge all` checks 30 × 2 layouts × 2 plans = 120/120 winning reference configurations under Standard rules. Three plans needed challenge versions: W4-M5 High Tide Start B (the third skiff fetches the low market), W5-M5 Light Kit B (generator on the basin platform, no barrier), and the ten W1 Alternate Dock copies (pickup at the new landing). In-game, challenges unlock after the base mission is won, use Standard rules, are scored under their own id, and keep the challenge through restarts.
  - *Spec gap, W2-M3 Crosswind:* W2-M3 has no authored wind, so "reverse the wind" would change nothing. On a calm map, Crosswind brings a steady east wind (code 2) with the first ignition. All four directions were tested and both reference plans win with each.
  - *Spec deviation, Alternate Dock:* the spec puts the third landing one cell east of the high-landing anchor, which the group can't reach on the authored W1 maps. `tools/content/alt_docks.py` adds it beside the group's own dock instead. It has the same capacity and dock depths, a ground leg +20 ticks, water approaches +100 ticks and a 20-tick link between the two berths. The base matrix stays 450/450.
- **PlayMode gate harness (24 Oct):** `smoke.sh` refreshes assets before compiling; a background Editor otherwise tested stale code once. It no longer polls the Editor bridge during a run: `Editor/Testing/TestResultWriter.cs` writes `tools/.cache/test_results.json` and the script waits on that file. A bridge request that timed out mid-test made the bridge log an error, which the Test Framework counted against whichever test was running. No test was weakened.
- **Audio (24 Oct):** all 44 sounds are synthesised by `tools/audio/synth.py` with a fixed seed: no samples, no generated-audio services, so they're licence-clean and cost no credits. That covers 24 effects, 5 world ambiences, and calm/problem/resolve music stems per world at 80 BPM in 24 s seamless loops. Each world has its own instrument family, following the bible's sound notes. `AudioDirector` maps sim events and input to sounds. The problem stem fades in while a warning is fresh, a hazard is due within 10 s or residents await backup. Warning, blocked and backup cues use the Warnings volume, so they stay audible when music and effects are lowered. Import is mono Vorbis; music and ambience stream. How it sounds is a human review item, not a test result.
- **Forecast worker (24 Oct):** the forecaster's main-thread `Prepare` (state copy plus preview submit) is split from `Advance`, which steps on a `Task`. The view paints only when the job finishes and the same socket is still hovered. A PlayMode test checks that the worker and synchronous results match and that the live run's hash is untouched.
- **Autopilot and brand (24 Oct):** in dev builds, an `autopilot.flag` file in the app's data folder plays every reference strategy in real time and logs outcomes and frame timings for P5. The icon and VR splash are drawn by `tools/brand/icon.py` in Atkinson Hyperlegible. The system splash shows over contextual passthrough.

## 24–28 Oct 2026 — Forecast horizon, website, trailer pipeline, Devpost draft
- **Forecast horizon 60 s (was 30 s):** in W1-M1 the first surge lands after 30 s, so a pre-start preview showed no difference at all. The preview now runs 600 ticks ahead on the worker thread. Changed cells are drawn as full-cell tiles at 0.8 alpha: green for kept dry, red for newly flooded. The site copy says "the next minute". PlayMode stays 15/15.
- **Stale toast:** `OperationPanel.Rebuild` clears the last warning, so a hazard toast from one mission ("Lava advances") never shows over the next.
- **Website media are Editor captures, and say so:** every caption reads "Development build, captured in the Unity Editor … Not headset footage." Shots come from the running game with strategy A played through the normal command path, and dev controls are hidden and checked. `capture.sh` waits for ≥ 30 real frames before each shot; a run without that left one mission's HUD on every image. `ONLY="w3 act"` recaptures single shots.
- **Trailer pipeline (`tools/video/edit.py`):**
  - Sources are `video/raw/<id>.mp4` (git-ignored), each with a sidecar recording kind (quest/xrsim), input (human/replay/autopilot), mission, build and date.
  - The build refuses any other kind, following the competition's device-or-simulator rule.
  - This ffmpeg has no drawtext/subtitles filters, so title cards and burned-in captions are Pillow PNGs overlaid. Caption stills are looped, because a single-frame input stopped compositing after the first overlay.
  - Loudness is two-pass loudnorm to −16 LUFS / −1 dBTP. The music bed is the game's own stem, ducked under game sound by sidechain.
  - QC gates: < 178 s, size, audio, loudness, black runs, and first/last frames for review.
  - `selftest` runs the whole chain on stamped test patterns and checks that the kind gate refuses them.
- **Storyboard:** five worlds, one shot each, each subject to its ship gate. Shots that need a device (real table, one hand, Command, anchor return) are cut if not verified in P5.
- **Devpost draft:** `docs/submission/devpost-draft.md`; story 498/500 words. Unverified claims carry `[P5]`, `[G]` or `[you]` markers to settle before submission.
- **RAM and disk etiquette (28 Oct):** while another project runs an Android IL2CPP build (swap near full, < 4 GB disk free), the RESQ Editor stays closed and non-Unity work continues.
- **Resident life clips (28 Oct):** the user approved the last 15 Meshy credits (cap 600 reached) for five preset actions on the man's rig: Idle, Alert, Wave for Help, Cheer, Chair Sit.
  - **Prep:** `resident_prep.py` bakes them into each resident's own FBX beside the walk, one NLA strip per clip. Bone rotations transfer by name across the three Meshy rigs. Object channels and Hips translation are dropped, so each figure keeps its own proportions and the simulation keeps owning position.
  - **Controllers:** the importer names the takes; each controller gets a state per clip, with Walk the default.
  - **Draw budget:** standing residents stay on the shared baked pose (instanced). Only one figure per node moves:
    - where a group waits in danger, it waves for help;
    - at a safe landing, it cheers.

    Figures without the clip (seated riders, older kits) stay still. Checked in the Editor: two wavers at W1-M1's start, two cheering after the win, non-leaders unchanged. PlayMode 15/15, including each world's M1 render budget.
  - Sit is baked but not used yet, since riders aboard vehicles are hidden.
- **HUD hint:** a run paused after its first tick says "Paused — press Start to continue"; "Plan first" is only shown before the clock has moved.
- **Website tap targets:** list links (support/privacy contents, the world list) are 48 px rows. All eight pages fit 375 px with no horizontal scroll.
