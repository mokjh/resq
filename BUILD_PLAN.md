# RESQ — Hackathon Master Plan: all 25 missions by Tue Nov 17

## Context — where the project actually is (verified Oct 22)

- **One week in, nothing playable.** 12 of 229 tasks "accepted" (bootstrap tooling + rules C01–C07). Zero scenes, prefabs, art, audio, site or APK. The last four days went to one-shot diagnostics and admission guards; the project's own rules deadlocked it (C08 "exhausted", F04/F06 "blocked", every Quest test scheduled after all 25 missions → no entry possible).
- **Existing simulation (~11k LOC pure C#)** encodes the rules correctly but can't run a game: every tick forks all state, builds a JSON snapshot and SHA-256-hashes it (5.3 ms/tick on a 4-node fixture on the Mac), nothing composes the systems, and the forecast reruns that path (44 ms worst call). Keep it as a rules oracle, not a foundation.
- **The design is fully specified for all 25 missions:** contracts (groups, budgets, events, objectives, two strategy briefs), a layout recipe per mission (32×24 grid anchors, routes, sockets, docks) plus exact construction rules (`specification/04_MISSION_AUTHORING.md` §2), integer rules for water, fire/wind, stability/runout, tides, lava/coolant/shelters (`specification/02_RUNTIME_RULES.md`), 30 challenges, art/UX bibles, a 487-row asset inventory.
- **Toolchain is nearly ready:** Unity 6000.3.24f1 + AndroidPlayer + Android SDK/NDK r27c/JDK 17 already wired (Waz builds Android with them); Meta XR Core/Interaction 207.0.0 in the UPM cache; Meta XR Simulator 207.1 installed; `metavr` CLI 1.8 (XR Sim control/record, SpatialSim APK emulator, store upload, perf, asset search); Unity CLI 1.0-beta.6 (`unity mcp`); Blender 5.2.1; ffmpeg; Node 22.
- **Constraints:** M1, 8 GB RAM; 12 GB free disk (5.7 GB reclaimable in `evidence/`); Meshy 1,084 credits, Higgsfield 110 credits; Vercel team "mellardoo's projects".
- **Competition (live rules, read today):** Meta VR Start Developer Competition 2026. Deadline **Wed Nov 18, 12:00 PT = 22:00 Cairo**, no changes after. Submit an **APK in a release channel named "Competition" + Invite URL**, a **public YouTube/Vimeo video < 3 min** (device or XR Simulator footage, no AI video), and the form (140-char tagline, ≤500-word story, Gaming track, New Experience division, target launch date). Hard rule: fully usable with hands end-to-end. Judging 4 × 25%: Innovation (hands, gaze, passthrough, scene understanding, persistent anchors), Experience Design (onboarding; passthrough must be purposeful; FoV-aware UI), Technical (ISDK v207+ gaze, MRUK anchoring that works in unknown rooms, ≥60 fps, bug-free), Polish (UI, art, sound, honest video). Target: **Best New Gaming Experience**; strong fits for First Five Minutes, Accessibility Forward, Reason to Come Back, Boldest Original Concept.

## Decisions

Yours (Oct 22): attempt **all 25 missions** in the entry · **studio mode** · **hybrid capped AI budget** (Meshy ≤300, Higgsfield ≤60 credits) · **RESQ gets the Mac exclusively + reclaim 5.7 GB**.

Mine, as if I were you:
1. **Gaming track, New Experience division** (project began Oct 14, inside the window).
2. **Device testing last but never blind:** build and test everything on the Mac (Unity Editor + Meta XR Simulator with synthetic rooms and simulated hands + the Editor desktop adapter). APKs build from day 2 and are checked with aapt2 manifest checks and `metavr vrc-local store-listing-check` (SpatialSim only runs 2D panel apps, so it cannot test RESQ). Physical Quest 3/3S only in P5 (Nov 12–15).
3. **Three factories make 25 missions possible:**
   - **Sim v2:** lean struct-of-arrays, integer, allocation-free, deterministic engine covering all five worlds' rules. Hash only at run end. Forecast = cheap clone + run-ahead on a worker thread. Old engine is the golden-value oracle in tests.
   - **Mission factory:** recipe + contract → layout compiler (implements §2 construction rules) → compact + wide layouts → headless validator (no-input must fail; strategies A and B must win on both layouts × 3 presets; challenges) → replay files.
   - **Board/kit factory:** layout cells + roles → procedural terrain with cutaway edge + kit placement; per-world kits from parametric Blender scripts on one palette atlas. Sim and visuals can never disagree.
4. **Breadth first, polish by priority.** All 25 greybox-validated by Nov 5, then polish in order: first-run path + W1 → each world's first mission → the rest.
5. **Per-world ship gate:** a world is playable in the entry only if all 5 missions pass validation + Simulator playthrough + device smoke. Otherwise the hub/site/trailer show it honestly as "in development". No broken content in front of judges.
6. **Judge-friendly access:** campaign unlocks progressively, plus an "Open play" setting that unlocks every mission (also an accessibility feature). The hub highlights each world's first mission.
7. **The real room matters:** MRUK environment-raycast `PlaceBox` picks the **wide or compact layout from your actual table** (gameplay differs: primary dock swaps), else virtual table. **Your valley persists on your table** (spatial anchor) and visibly recovers between sessions. The storm is room-aware (rain from your ceiling height, lightning tint on passthrough). Hands cast shadows on the diorama.
8. **Original, cohesive art** from scripted Blender (rights-clean, Quest-cheap). Meshy/Higgsfield only as accelerators within the cap. Every spend is logged with license terms.

## What ships Nov 17 (definition of done)

- 5 worlds × 5 missions (subject to the ship gate), each with compact + wide layouts, two winning strategies, Standard / Relaxed / Command presets, and its authored challenges (30 total).
- Guided W1-M1: first rescue < 90 s after placement, no wall of text. Each world's M1 teaches its mechanic by doing.
- Hub on your table: stars/score, causal debrief, aftercare (each valley rebuilds, postcards), save/resume, pause anywhere.
- Inputs: two-hand, one-hand (left or right), **gaze + pinch via ISDK v207 `GazeInteractor` fed by head gaze** (Quest 3/3S have no eye tracking), head-dwell Command path. Controllers optional.
- UI states U01–U24, including permission denied, tracking/anchor recovery and save recovery.
- Accessibility: text 100/125/150%, captions on, ≥4.5:1 contrast + icon redundancy, high-contrast mode, reduced effects, 150% targets, Relaxed pacing, Open play.
- Final art + audio for every shipped world. 72 Hz target (≥60 floor) on Quest 3 and 3S.
- Competition-channel build + invite URL, < 3 min honest trailer, Devpost text, website.

## Art & graphics pipeline — world-class, adapted from Waz (decided 22 Oct)

Proven in the user's Waz projects: 686 provenance rows, Quest port at ~113 draws / 75k tris. RESQ adopts the routing and lessons. Every RESQ script and shader is **re-authored from scratch** in this repo, because the competition's New Experience division forbids pre-existing codebases. Waz is a reference for techniques only, never copied.

**Look:** a hand-crafted miniature, made "readable toy-real".
- **Shading:** one RESQ Lit shader family (World / Character / Interactable):
  - 3-step toon ramp and soft wrap lighting;
  - shadows tinted by world palette, never black;
  - AO stored in vertex-colour alpha;
  - rim light, plus a thin pixel-sized outline **only on interactables and residents**, so the things you can act on pop.
- **Palette:** one palette per world, as data. Look-dev golden renders act as a regression gate.

| Asset class | Route | Budget | Notes |
|---|---|---|---|
| Buildings (cottages, houses, stalls…) | **Scripted modular kit** in Blender with authored LODs | 1–2.5k tris | One shared trim atlas per world, built from Higgsfield `nano_banana_2` texture swatches made seamless by script. Extra edge loops give vertex AO room to work. |
| Simple props, equipment, tools (barrier, bridge, pump, generator, beacon…) | **Scripted part templates** (lathe, bevel box, profile extrude, sweep), vertex colour | 150–2.5k tris | No credits; cleanest results in Waz tests. |
| Organic props (trees, rocks, sacks, crops) | **Meshy-t2** image→3D at 2× budget, then RESQ `clean_prop` | ≤800 tris | 15 Meshy each. |
| Hero landmarks (viaduct, lighthouse, observatory, cooling works, sawmill) and vehicles (bus, skiff, crawler, winch cabin) | `nano_banana_2` concept → **Tripo H3.1** (via Higgsfield) or **Meshy-7.1** multi-image → remesh → clean | 2–4k tris, 1024² texture | 9–30 credits each. |
| Residents (adult, child, elder, step-free wheelchair) | Higgsfield character sheet (front/side/back, one reference element per character) → Meshy multi-image → voxel remesh → decimate to ~1k → one shared Mixamo-named skeleton → Meshy library clips | 0.8–1.2k tris, 512² atlas cell | 7 clips: idle, walk, wait, board, seated, wave, celebrate. Clips stripped to the armature, donor rest pose, per-clip floor offsets. Heads are slightly enlarged (miniature charm, readable at 0.6 m). |
| Water, fire, snow, tide, lava | **Shaders and particles**, data-driven from the simulation | — | No meshes; animated by sim state. |
| 2D: UI icons, mission cards, postcards, key art | Scripted SVG for icons and type (text is never AI-generated). `nano_banana_2` for postcard/key-art illustrations. | — | Real in-game renders are used for anything that shows gameplay. |

**Headless Blender `clean_prop` (RESQ version):**
1. Import, normalise scale and pivot.
2. Voxel-remesh AI meshes before decimating, to fix holes.
3. Collapse-decimate to budget, protecting colour edges.
4. UV, then bake AO into vertex alpha.
5. LOD1 at 50%, plus a convex `_COL` collider.
6. Export FBX with `axis_forward=-Z`, `axis_up=Y`, `FBX_SCALE_UNITS`, `bake_space_transform`, sRGB colours.
7. Write a JSON report and a preview render.

**Unity side:**
- **Import:** an AssetPostprocessor turns `_LODn` meshes into a LODGroup and `_COL` meshes into colliders, assigns RESQ Lit materials, and rebuilds prefabs only when an input changes.
- **Textures:** ASTC 6×6 for world textures, 4×4 for characters.
- **Shadow budget:** turn off shadow casting on the SDK hand-rig meshes. On Quest, Waz found they tripled draw calls.

**Process gates:**
- Lock the style prompt and negative prompt plus one reference element per character *before* batch generation.
- Batch-generate, then batch-clean with one script.
- Keep a manifest row per asset in `art/manifest.csv` (source, tool, model, cost, license, hash).
- Run golden-render lookdev diffs.
- **You review full-resolution stills and video, not small contact sheets** (a Waz lesson).

**Credits (estimate for 5 worlds):**
- **Meshy ≈ 600:** residents ~170, organic props ~200, landmarks/vehicles ~230. Balance is 1,084.
- **Higgsfield ≈ 150:** swatches ~75, concepts/sheets ~40, Tripo heroes ~35. Balance is 110 Higgsfield; that's more than the current 60 cap, and the balance itself would need a top-up.
- Both exceed the 22 Oct caps (Meshy 300 / Higgsfield 60). **Raising the caps is your decision.** Until then, everything that can be scripted is scripted first.

## Phases, steps and gates

### P0 — Reset & foundation · Thu Oct 22 → Sat Oct 24
1. Studio-mode records: archive AGENTS/STATUS/HANDOFF/BLOCKERS/DECISIONS/BUILD_PLAN verbatim to `docs/archive/process-2026-10/`; write a lean AGENTS.md (gates = tests/replays/perf/playtests/your reviews; honesty rules kept; one Unity writer; repair loops capped with an alternative approach, not a stop) and one-page STATUS/HANDOFF; add the reset entry to DECISIONS. `Resq_Production_Pack/` and `specification/` remain the design reference.
2. Disk: delete `evidence/foundation/f03-bounded-20261016/bootstrap-project` (5.7 GB); keep the rest of `evidence/` untouched on disk, untracked. You free more toward ≥40 GB by Nov 12.
3. Git: Unity `.gitignore`, LFS for `.fbx/.blend/.png/.wav/.ogg`, baseline commit on local `main`.
4. Unity project: URP (Quest forward renderer, MSAA 4x, no HDR/post), OpenXR + Meta features, Meta XR Core/Interaction(+OVR)/MRUK **207.0.1** (the current latest on Meta's registry, one matched family), XR Hands, Input System, TextMeshPro, Test Framework, `com.unity.pipeline`; Android IL2CPP ARM64, Vulkan, min/target SDK per current Meta docs; Meta Project Setup Tool clean. RESQ-scoped `.mcp.json` → `unity mcp --project-path …/Resq/game` (Waz untouched).
5. Headless sim runner: compile + test pure C# with Unity's bundled Roslyn/Mono (no new installs), so helpers can test without the Editor.
6. Sim v2 skeleton: state layout, operation/scenario clocks, ordered step (events → commands → power → hazards → transport → objectives → outcome), water + transport ported with golden tests vs the old engine. The old engine (68 files, 30 asmdefs) moves out of `game/Assets` to `reference/legacy-sim/`, unchanged. Only the headless runner compiles it, as the golden-value oracle, which keeps Editor compiles fast.
7. Smoke: passthrough + hands + cube on a table in XR Simulator; APK builds headless.
**Gate M0:** sim tests green; MR hands scene in XR Sim; APK builds; baseline committed.

### P1 — Vertical slice: First Crossing + factories v1 · Sat Oct 24 → Wed Oct 28
1. Mission factory v1: layout compiler from recipe + contract; W1-M1 compact + wide; validator v1; replay format.
2. Sim v2 W1 rules complete: barrier, bridge, pump, skiff, beacon, generator, bus, power priorities, temporal objectives, stars/score, replay hash, save/resume snapshot.
3. Board factory v1: terrain from cell heights with cutaway edge, greybox kit placement, water surface from sim heads (vertex-displaced mesh, cheap shader).
4. Placement: permission explanation → `PlaceBox` (wide → compact → virtual table) → height/distance calibration → spatial anchor.
5. Hands (ISDK): tool tray (poke/pinch), ghost drag (near grab or ray) to legal sockets with red/amber + icons, rotate, release = commit; route planner; pause target; mirrored one-hand layout.
6. Forecast preview (ghost water + route risk), warnings, objectives HUD, outcome, causal debrief; guided tutorial (hazard clock frozen until the bus is safe).
7. Tests: A/B replays on both layouts × 3 presets; PlayMode smoke through the input → command layer.
8. In parallel, from Sat Oct 24 once the Sim v2 core API is frozen: helpers implement the W2–W5 rule modules as pure C# with headless tests, straight from `02_RUNTIME_RULES.md` §7–10. They need no Unity, so the Editor is never blocked.
**Gate CP-A (Wed Oct 28):** a new player places the board and wins First Crossing both ways in XR Sim; you get a recorded playthrough.

### P2 — Systems, look lock, Alder Valley greybox · Wed Oct 28 → Sun Nov 1
1. Integrate and finish the helpers' W2–W5 rule modules (started in P1): fire/heat/wetness/wind + firebreak/sprinkler/finite water/crawler; stability/braces/collapse/runout DAG/winch duty cycle; tide ramps/currents/relay power/multi-leg transport; lava segments/cooler/coolant production/shelters.
2. Hazard presentation for each world: fire/smoke cells, snow runout, tide level + current arrows, lava crust states — all cheap, data-driven shaders/particles.
3. Look lock: style sheet from the art bible; Blender pipeline (`tools/blender/`) with fixed axis/scale conventions (fixes the old F06 bug), palette atlas + baked vertex AO, LODs, proxy colliders; Unity import validator (scale, pivot, tris, materials). Look-dev quartet (cottage, bridge, skiff, resident) in Unity under MR + virtual-table lighting → contact sheet → **your review Sat Oct 31**.
4. Residents: 3 bodies + step-free variant, 7 clips (idle, walk, wait, board, seated, wave, celebrate); Meshy rig/animate allowed within the cap.
5. W1 M2–M5 through the mission factory, validated.
**Gate CP-B (Sun Nov 1):** look locked; all world mechanics pass rule tests; W1 5/5 validated.

### P3 — Campaign build-out · Sun Nov 1 → Sat Nov 7
1. Content: W2 → W3 → W4 → W5 missions through the factory (helpers per world, disjoint `content/wN/`); strategies tuned against the contracts' briefs; challenges.
2. Kits: W1 final first, then Emberwood, Frostline, Tidemark, Cinder Reach (one Blender helper at a time, RAM); equipment set (14 kinds), vehicles (bus, skiff, crawler, winch cabin), landmarks, threatened/recovered variants.
3. Meta-game + UX: hub, world/mission select, briefing, loadout, results, debrief, aftercare/postcards, challenge unlocks, Open play, settings, help, credits/privacy, save/tracking/anchor recovery.
4. Command path (head-dwell 800 ms, gaze + pinch, destructive confirm, Advance 1 s / 5 s / Run) and controller adapter via ISDK.
5. Audio (helper): per-world 4-stem music at 80 BPM + ambience + hazard SFX, shared UI/equipment SFX, rescue chime. Sources: Higgsfield Sonilo (music) / Mirelo (SFX) within the cap, CC0 libraries, and original synthesis; license log per file.
**Gate CP-C (Thu Nov 5):** 25/25 missions validated by replays on both layouts × 3 presets. **CP-C2 (Sat Nov 7):** every world playable start-to-end in XR Sim with its kit.

### P4 — Polish, MR, accessibility, performance, RC1 · Sat Nov 7 → Wed Nov 11
1. Polish order: first-run path + W1 → each world's M1 → remaining missions.
2. MR features: anchor-persistent valley with relocalize/fallback; room-aware storm; hand shadows + real-hand occlusion; residents look up at you.
3. Accessibility pass + full one-hand and gaze-only runs of every world's M1.
4. Performance in Editor/XR Sim profiling: sim ≤1.5 ms, ≤150 draw calls, ≤300k tris, 0 B/frame steady GC, per-world scene loading, ASTC, fixed foveation, fast boot scene; APK manifest/VRC checks.
5. **On-device autopilot (dev builds only):** plays every mission's reference replays for P5 perf/stability runs without a human.
6. Store readiness to Meta's release spec: minSdk 32 / targetSdk 34, `com.oculus.supportedDevices` = Quest 3 + 3S, not debuggable, VR intent category, `excludeFromRecents`, unique label, release keystore (created once, never changes), icon, splash, version code; `metavr vrc-local store-listing-check`. **RC1 uploaded (draft) to the Competition channel** to rehearse the judge path; every channel upload must already meet release packaging.
7. FoV audit (`hz-unity-device-readiness`): no head-locked HUD; must-see UI inside the central 0–30°; captions/toasts lazy-follow.
**Gate CP-D (Mon Nov 9):** all shipped worlds have final kits/audio. **Feature freeze Wed Nov 11:** RC1 in channel; site preview live; trailer rough cut.

### Track W (parallel helpers) — Website, trailer, Devpost · Mon Nov 2 → Mon Nov 16
1. Site: Next.js/TS static site in `web/app` from the existing A24-led reference lock: `/`, `/how-to-play`, `/worlds` + one page per world (shipped worlds playable, others "in development"), `/accessibility`, `/press`, `/support`, `/privacy`, 404. Real renders/footage only. Vercel preview → production; `playresq.com` once you own it.
2. Trailer ≤ 2:40, best material first, re-storyboarded from the 160 s W1 board to show only worlds that actually ship: clean gameplay via `metavr xrsim record`; first-person MR/real-hand shots in P5 via the Quest Camera app (1080p landscape, 20 Mbps, pulled with `metavr files pull`); FFmpeg/EDL edit; SRT/VTT captions; game score and sound (narration optional — your voice, or TTS within the cap).
3. Devpost copy (name, tagline, story, hands write-up, target launch date) and real screenshots, ready for your review.

### P5 — Device phase: Quest 3 + Quest 3S · Thu Nov 12 → Sun Nov 15 (the only physical phase)
1. Install RC on both. Autopilot runs all 25 missions' replays per model with perf logging (`metavr perf` / OVR Metrics); 30-min soak per model.
2. Hands-only journeys: full W1, M1 of every world, random samples; permission denied → virtual table; suspend/resume; tracking loss; anchor relocalization across sessions.
3. Rooms: 2–3 real setups (desk, dining table, coffee table, no table).
4. Playtests: you + 3–5 first-time adults — time to first rescue, understanding, 20-min comfort, voluntary replay (your observations).
5. Fix loop → RC2/RC3 (P0/P1/P2 only). Apply the per-world ship gate.
6. Real footage: MR captures of hands + passthrough on both devices; screenshots.
**Gate M5 (Sun Nov 15):** both models pass hands-only journeys; ≥72 fps typical (≥60 floor); no known P0/P1; footage captured.

### P6 — Ship · Mon Nov 16 → Tue Nov 17 (Wed Nov 18 = buffer until 22:00 Cairo)
1. Final build: replay matrix + device smoke; upload to Competition channel; Invite URL; install from the invite on a clean account to test the judge path.
2. Final trailer to YouTube (public); site production deploy.
3. Devpost form fully prepared by me; **you submit Tue Nov 17**. Tag the release; no changes after.

## Checkpoints and cut-line
- If a checkpoint slips by more than a day, simplify in this order. Never cut hands-first, W1's five missions, accessibility paths, presets/modes or honesty.
  1. Website extras and trailer complexity.
  2. Aftercare/postcard variety (one template per world).
  3. Unique landmark detail: shared kit + palette/material swaps instead.
  4. Controller adapter.
  5. Polish depth of W4–W5 missions 2–5.
  6. Last resort: a world that fails its ship gate is shown as "in development" and finishes right after Nov 18.
- Decision points with you: Sat Oct 31 (look) and Thu Nov 5 (CP-C: which worlds are on track).

## Reference material I'll lean on
Meta's official agent skills already installed in `~/.agents/skills/`:
- `hz-unity-meta-core-sdk`, `hz-unity-meta-interaction-sdk`, `hz-unity-meta-quest-ui`
- `hz-xr-simulator-control`, `hz-vr-debug`, `hz-perfetto-debug`, `hz-unity-code-review`
- `hz-unity-device-readiness`, `hz-store-submit`, `hz-immersive-designer`
- MRUK and simpleperf skills are added to the same folder in P0.

`metavr docs` is the source of truth for SDK details. The pack's art/UX/video bibles set the bar.

## Team model and daily loop
- **Me:** the only Unity writer — Sim v2 core API, integration, XR/UX, performance, builds.
- **Helpers** (disjoint write areas, acceptance tests, stop conditions), at most one Blender job at a time:
  - mechanics modules (`game/Assets/Resq/Sim/Worlds/*`)
  - content per world (`content/wN/`)
  - art per world (`art/wN/`, `tools/blender/`)
  - audio (`audio/`)
  - web (`web/app/`)
  - video (`video/`)
- **Daily:** compile + headless tests on each change → replay matrix → XR Sim playthrough recording of the day's feature → APK build → commit → 5-line STATUS. At milestones I send you a short clip or contact sheet.

## Your actions (human-only)
- **Today/tomorrow:**
  - Confirm or apply for Meta VR Start membership.
  - Join the hackathon on Devpost.
  - In the Meta VR Developer Dashboard, create the app "RESQ" (Quest) and share the App ID.
  - Run `metavr auth login` once so I can upload builds.
  - Create the release signing key with one command I prepare. Its password stays in your Keychain, and the key can never change once uploaded.
  - Pause Waz Unity work while RESQ is active.
- **Sat Oct 31:** 15-min look review. **Thu Nov 5:** 10-min scope check.
- **By Nov 11:** free disk to ≥40 GB; line up 3–5 adult playtesters and rooms with tables; enable developer mode on both headsets.
- **Nov 12–15:** ~5–8 h wearing the headsets for tests, playtests and footage.
- **Nov 17:** submit on Devpost and make the legal attestations.
- **Optional:** buy `playresq.com`.

## Approvals requested by this plan
- **Process:** studio-mode reset of process docs (originals archived verbatim).
- **Commits:** local git commits on `main` at each working step (no push).
- **Disk:** deletion of the 5.7 GB bootstrap-project copy in `evidence/`.
- **Unity:** packages + RESQ-scoped MCP config inside RESQ only. No global prefs; Waz untouched. Reuse of the existing Android SDK/NDK/JDK.
- **Tools:** install the Meta MRUK + simpleperf agent skills with `metavr skills install` (into `~/.agents/skills`); install the SpatialSim emulator (770 MB) via `metavr tools` when disk allows.
- **Paid generation:** within the cap you set, each spend logged.
- **External writes, confirmed with you at the time:** Competition-channel uploads, Vercel deploys, YouTube, domain.

## Risks and mitigations
| Risk | Mitigation |
|---|---|
| 25 missions in 26 days | Factories + headless validator; breadth first; per-world ship gate; checkpoints with you |
| 8 GB RAM (Unity + XR Sim + Blender) | Exclusive Unity; Blender headless one job at a time; close the Simulator when idle |
| Device-only bugs found late | XR Sim + desktop adapter from P1; Meta building blocks; perf budgets from day 1; autopilot for all 25 on device; RC1 by Nov 11; 1-day buffer |
| Membership/dashboard lead time | Day-1 actions |
| Art coherence across 5 worlds | One palette atlas, shared kit grammar, look locked Oct 31, import validator |
| XR Simulator has "limited support" on macOS | Editor-only desktop adapter (mouse/keys → the same input→command layer, also used by PlayMode tests) as a guaranteed fallback; the device phase catches the rest |
| Unknown judge rooms | Two-class `PlaceBox` + virtual fallback + anchor recovery; tested against MRUK sample rooms |
| APK size/memory with 5 worlds | Per-world scenes, ASTC, Vorbis; target APK < 600 MB, memory < 1.5 GB |

## Verification
- Every change: Sim v2 tests (determinism, conservation, command atomicity, golden values vs old engine, per-world rule tests).
- Daily: replay matrix (25 missions × 2 layouts × 3 presets × 2 strategies = 300 + challenges), PlayMode smoke (boot → place → win W1-M1 via scripted input), APK build.
- Milestones: XR Sim recordings and contact sheets sent to you; perf captures.
- P5: autopilot runs on both models + hands-only checklist + judge-path install via the invite URL.
- Claims on Devpost, site and trailer match the shipped build; "device-tested" only after P5.

## After Nov 18
Finish any world that missed the gate, then full-game QA, Store submission and commercial site. You pick the public target launch date for Devpost (suggestion: Spring 2027).

## Critical files
- **New:**
  - `game/Assets/Resq/{Sim,Missions,Board,XR,UI,Audio}`
  - `content/w1..w5/`
  - `tools/{layout,validate,blender,build}/`
  - `web/app/`, `video/`, `.mcp.json`
  - lean `AGENTS.md`, `STATUS.md`, `HANDOFF.md`
- **Reuse as reference:**
  - `game/Assets/Resq/Runtime/Simulation/**` (rules oracle)
  - `specification/content/{mission_contracts,layout_recipes,equipment_rules,modes_and_challenges,production_inventory}.json`
  - `specification/02_RUNTIME_RULES.md`, `specification/04_MISSION_AUTHORING.md`
  - `Resq_Production_Pack/docs/{03,04,07,11}_*.md`
  - `web/design/reference-lock.md` + fonts
