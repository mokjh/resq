# RESQ full implementation plan — frozen 1.1.0

The complete deliverable is five worlds and 25 missions, with tested Unity/Quest builds, editable audited art/audio, working production tools, real gameplay media, a truthful marketing site and release materials. Start from `specification/00_FREEZE_INDEX.md`. The original `Resq_Production_Pack/` remains unchanged and readable. This plan resolves design choices; actual implementation acceptance is recorded separately in STATUS.md. Controlled amendment 3 (16 October 2026) moves every real Meta device test to the final qualification phase after local development of all 25 missions.

## Decisions and source of truth

The user chose all five Alder missions and the full 25 mission scope over the competition deadline, English first, both Quest 3/3S physical test targets and internal storage at the current project path. Scope cuts require a new explicit user decision. No three-mission contingency is active.

Root AGENTS governs implementation. Specification precedence and change control are in 01_SCOPE_AND_DECISIONS. Runtime arithmetic/phase ordering is 02; exact typed interfaces/save/replay are03; mission authoring is 04; inventory/UX/hardware/human acceptance is 05; tools/media/site/release operations is 06; exact evidence, bootstrap and acceptance lifecycle is 07. Machine JSON and the task graph carry stable IDs for these contracts. Any disagreement fails validation and must be reconciled before execution.

## Production milestones and exit checks

The execution order below separates local development from final physical qualification. F05 remains a late foundation acceptance task: its absence does not require waiting for physical devices before developing the game. Current acceptance and blockers live in `STATUS.md` and `automation/task_state.json`; the next-step roadmap is `docs/planning/execution-roadmap-20261020.md`.

| Milestone | Tasks | Exit contract |
|---|---|---|
| Foundation | F01–F08 | Preserved baseline, repository/status, real runner, licensed host compilation, locally built Android probe and resolved candidate lock, Blender calibration, current rules and actual Mobbin reference lock. Host logic work is independent of blocked device/publication gates. |
| Rescue engine | C01–C09 | Compiled typed content; exact clocks/commands/RNG/water/routes/stock/power/objectives/score; complete saves/replays and forecast isolation; numeric/order fixtures and seeded invariant fuzzing. |
| XR shell | X01–X06 | Safe two-class placement/fallback; near/ray and one-hand inputs; all U01–U24 states; independent continue/recovery flows; complete Command and controller adapters. |
| First complete rescue | W1-M1 AUTHOR/REPLAY/INTEGRATE, W1-TUTORIAL-LOGIC | Genuine playable blockout with bridge/barrier/boats, two successful strategies, tutorial, saves, pause, causal debrief and actual host input/recovery checks. Physical hands-first proof remains a late W1-VERTICAL requirement. |
| Look and first chapter | ART-LOOKDEV, ART-SHARED, W1 kit/audio/onboarding, W1-M1…M5 POLISH | Coherent inspected cottage/bridge/skiff/resident set with actual inspected Unity renders; complete audited kit and audio; five locally integrated missions; late device/human look approval remains required; progression, aftercare and modes. |
| Emberwood | W2 MECHANIC/FIRST/KIT/AUDIO/ONBOARD; all five local mission stages through POLISH; late qualification/freeze | Fire/wind/wetness/finite-water/crawler strategy, first mission before kit expansion, five locally developed missions and world recovery/aftercare; qualification follows in the final phase. |
| Frostline | Corresponding W3 tasks | Structure/runout/vertical transfer, finite station capacity and meltwater-power coupling; five locally developed missions; late physical qualification remains required. |
| Tidemark | Corresponding W4 tasks | Tide/current windows, relay/power topology and multi-leg transport; five locally developed missions; late physical qualification remains required. |
| Cinder Reach | Corresponding W5 tasks | Lava/cooling/finite coolant/shelter tradeoffs, combined finale; five locally developed missions; late physical qualification remains required. |
| Final physical and human qualification | F05, device look/vertical/first checks; all mission QUALIFY and world REGRESSION/PERFORMANCE/DEVICE/HUMAN/FREEZE tasks | After all 25 local POLISH results and complete required input/tutorial paths, qualify both physical models and human gates. W1 retains 60 base+40 challenge reference configurations. Qualified world freezes proceed W1→W5. |
| Delivery | D01–D12 plus D03-REVIEW | Real capture/edit, tested local site, accurate draft/guide, then separately authorized deployments/public video/channel and entrant submission. Capture and release claims follow late qualification. Local capture-tool/EDL preparation may run after W1 local POLISH; external actions do not block game development. |
| Commercial candidate | FULL01, FULL-PERFORMANCE/DEVICE/HUMAN/QUALIFY, FULL02–FULL05, FULL-VIDEO/VIDEO-REVIEW/WEB/WEB-PREVIEW | Current final APK requalified across all 25 missions, ten soaks,40 profiles and 210 physical mission runs; full human gates; reviewed commercial decisions; genuine full-game media/site; immutable signed local bundle; separately authorized publication. |

Every mission retains its dedicated AUTHOR, REPLAY, INTEGRATE, POLISH and QUALIFY task. Local first-mission INTEGRATE precedes each later world kit and M2–M5 expansion; all five prior-world POLISH results precede the next mechanic. F05 waits for every one of the 25 POLISH results, complete host Command/controller paths and the W1 tutorial. Only then run the physical probe, retained W1-VERTICAL/W2–W5-FIRST checks, device look review, all mission QUALIFY/cumulative REGRESSION, performance, room/input and human sessions. Qualified world freezes proceed in order W1→W5, followed by exact full-candidate requalification, qualified capture and release. Every prior world remains in regression. No release contains unqualified content.

## Task execution contract

`automation/tasks.json` contains 229 immutable task definitions and `automation/checks.json` contains 231 acceptance checks with 942 required assertions. `specification/content/acceptance_cases.json` defines35,784 individual required case records across task runs, including repeated/cumulative regression; these are planned cases, not executions. The487-row production inventory defines every required asset/audio entry. `automation/task_state.json` starts every game/build task pending and later binds status to an exact result path/hash. Each task declares dependencies, inputs, write areas, single-writer locks, expected artifacts, check IDs, gates, original T01–T38 traceability, repair bound and stop condition.

F01 implements its own standalone `python3 tools/bootstrap_doctor.py` before F02 implements `python3 tools/resq.py`. Commands in checks.json are exact argv arrays with per-run/attempt report templates. They are requirements for future tools and subsystem tasks. A task begins when dependencies passed, required gates have authentic evidence and write/heavy locks are free. An independent verifier binds exact prerequisite/gate heads, parsed native individual cases, immutable output copies and derived coverage. Gate discovery does not waive authorization.

Resolve priorities by graph order, then task ID: foundation/host core → W1-M1/XR → local style/W1→W5 through all 25 POLISH → final F05/device-look/vertical/first/mission/world qualification → full candidate qualification → qualified capture/release. Web/reference work and edit planning may run independently, with game work retaining heavy-process priority. There is no scheduled/background-work promise; each active session resumes persisted state and ends with a handoff.

F02 implements process dispatch, lock ownership, actual exit-code/log capture, timeouts, native XML/JSON/build-report parsing and atomic result/state writes. Unimplemented backend, absent license/device/provider or missing output exits blocked/nonzero. No success-returning placeholder. Content compiler/replays and source data use the same C# code in tests and gameplay; Python does not implement a competing rescue simulator.

## Scope and configuration acceptance

W1 missions: First Crossing; The Lower Road; Two Banks; Power Before Water; The Long Way Home.

W2: A Line in the Trees; Last Bus from Camp; Water Is a Route; When the Wind Turns; The Green Corridor.

W3: Hold the Station; Above the Snowline; The Quiet Slope; Cable and Current; Dawn at Frostline.

W4: Between Two Tides; Keep the Light; The Floating Market; Storm Switch; Harbor of Everyone.

W5: A Path Through Heat; Borrowed Safety; The Cooling Works; Three Ways Out; Every Little Life.

Base configurations require 300 successful references;30 named challenge definitions add120. Physical full-mission qualification has 210 minimum runs, plus per-world/per-model thermal/performance and recovery matrices. Human criteria include first meaningful rescue median<90 seconds after placement, uncoached completion/understanding/voluntary continuation and observed comfort. Exact thresholds/inventory counts are in 05. Qualification cannot infer device/human evidence from automated tests.

Initial targets are native 72 Hz, CPU p95≤10 ms, GPU p95≤11 ms, simulation≤1.5 ms,0 B steady hot-path allocation, bounded actors/effects/materials and clean memory after repeated transitions. Every world runs30 minutes on both models. Final candidates have zero P0/P1/P2 defects; reviewed P3 limitations are explicit. Numerical tuning and creative art remain subject to observed correction through versioned change control, not weakened checks.

## Dependencies, budget and publication

The selected Mac has 8GiBRAM and little free disk space; remeasure capacity before each heavy operation. The user will free internal space. Apply 06 section 3.1 general operation floors and the exact controlled-amendment exceptions below. F03 uses the frozen bounded host profile (2 GiB reserve +64 MiB growth cap +64 MiB overshoot); named later local/runtime operations use `automation/local_operation_profiles.json` and `automation/runtime_operation_profiles.json` only within their existing scopes. Operations without an applicable exception retain Unity max(40 GiB,peak+10 GiB), art max(15 GiB,3×new+10 GiB), web max(10 GiB,peak+5 GiB), and capture max(20 GiB,2×raw+3×export+10 GiB). A profile grants neither repair authorization nor future capacity. Preserve unrelated active work. One team heavy operation and one writer per serialized Unity/shared-manifest/Blender resource at a time.

Unity Android modules and package compatibility, license, actual headset trust, capture consent, human observations and provider/account scopes have named gates. Exact locally resolved package versions are preserved from F04 for reproducibility; they remain physically unqualified until the late F05 probe. A minimal probe never qualifies the final game. Paid generation remains0approved; ART-HF-BRIEF prepares a concrete request and ART-HF-GENERATE is optional/gated. Original Blender sources can complete art gates while generation is blocked. Default external approvals remain disabled. Never infer budget from credits or create provider/project/domain resources without the relevant scope.

The site is independently static Next.js/TypeScript with npm lockfile, actual Mobbin research, release-derived typed content, real media and the specified routes. No backend/account/waitlist is included. The game runs offline independently of it. The160 second W1 video uses real qualified footage and the original storyboard. A full-game edit after W5 uses real qualified worlds, preserving the W1 master.

Prepare local site/video/build artifacts and validations before requesting final publishing approval. Vercel preview precedes production; domain linking requires ownership+DNSscope. Publicvideo/channel uploads and final entrant actions are separate. A blocked publication gate does not make a qualified local build fail or complete the unperformed external deliverable.

The18 November 2026, 22:00 Cairo competition deadline is a conditional opportunity, not permission to cut content. Recheck it and current rules at release. Rebaseline local development estimates after actual host W1-M1 results; physical readiness and a release date remain unproven until the final device phase. No unverified commercial date, price, name clearance, performance, awards or market acceptance is promised.

## Validation available in this freeze

Run `python3 -B tools/validate_spec_freeze.py --report evidence/specification-freeze/validation.json` and `python3 -B tools/test_spec_freeze.py`. The root evidence verifier validates provided real result/asset/release records and rejects missing/failed/stale evidence. The original pack validator and its nine tests continue unchanged. These checks qualify the specification and evidence machinery; game compilation/replays/headset/human/site/video remain future execution work.

Initial bootstrap sequence (historical once accepted): F01 records preflight and establishes the local repository/status; F02 implements the actual runner; F03 proves licensed host compilation under its gates. Resumed work starts from current accepted results, `STATUS.md`, `HANDOFF.md` and the dependency graph. Continue all independently ready authorized work without repeating prerequisite questions.

Controlled amendment5,16October2026: user-directed F04/F06 staged storage reassessment and C01 continuation use automation/local_operation_profiles.json and fresh scoped admission. Installed Android tools are reused; no global preferences/other project changes. All25missions and every acceptance check remain. See specification/amendments/20261016-staged-local-storage.md.

Controlled amendment6,16October2026: dependency-ready C02 uses its own sealed automation/runtime_operation_profiles.json with fresh2.25GiB native admission,128MiB enforced growth cap and2GiB hard reserve. All25missions and acceptance checks remain unchanged. See specification/amendments/20261016-runtime-commands-storage.md.

Controlled amendment7,16October2026: dependency-ready C03 uses its explicit runtime-water stage in automation/runtime_operation_profiles.json, with fresh2.25GiB native admission and existing bounded cancellation. All25missions and acceptance checks remain unchanged. See specification/amendments/20261016-runtime-water-storage.md.

Controlled amendment8,16October2026: independently dependency-ready C04 receives its own runtime-transport stage (fresh2.25GiB admission,128MiBcap,2GiBreserve). Existing budgets and all25missions/checks remain. See specification/amendments/20261016-runtime-transport-storage.md.

Controlled amendment 9, 16 October 2026: boarding history uses an explicit compiled-milestone-bound internal causal-summary row in the existing MetricState wire shape. Public objective queries, rules outcomes and all 25 missions/checks remain unchanged. See specification/amendments/20261016-transport-boarding-history.md.

Controlled amendment 10, 16 October 2026: dependency-ready C05 receives its explicit runtime-equipment stage (2.25 GiB admission, 128 MiB cap, 2 GiB reserve). Runtime-state table formatting is restored without changing its fields or amendment 9 semantics. All 25 missions/checks remain. See specification/amendments/20261016-runtime-equipment-storage.md.

Controlled amendment 11, 17 October 2026: correct recent load/replay owner references from C08 to C07 against the unchanged root task graph. All scope, shapes, rules and checks remain unchanged. See specification/amendments/20261017-task-owner-references.md.

Controlled amendment12,17October2026: explicitly bounded dependency-ready C06 objectives and units suites run sequentially on the lean host with fresh2.25GiB admission per suite,128MiBcap and2GiBreserve. Both checks and all25missions remain. See specification/amendments/20261017-runtime-objectives-storage.md.

Controlled amendment13,17October2026: bind objective causal summaries and actual transport boundary observations in the existing state shape, with exact dock clocks/restore semantics. All25missions/checks remain. See specification/amendments/20261017-objective-causal-summaries.md.

Controlled amendment 14, 17 October 2026: dependency-ready C07 uses the runtime-persistence stage on the existing lean licensed host, with fresh 2.375 GiB admission, a 256 MiB enforced growth cap, 128 MiB cancellation margin and 2 GiB hard reserve. Small actual save-file fixtures stay inside monitored RESQ-owned paths; no graphics or package installation is admitted. All 25 missions/checks and prior stage budgets remain unchanged; full-game peaks remain unknown. See specification/amendments/20261017-runtime-persistence-storage.md.

Controlled amendment 15, 17 October 2026: C07 replay uses an optional separately supplied, canonically hash-bound application journal for actual initial lifecycle and external recovery callbacks. Exact lifecycle/command boundaries, real recovery execution and journal-bound evidence close the missing external-input representation before acceptance. Existing Snapshot/Command/Replay schemas, all 25 missions/checks and storage budgets remain unchanged. Native, Android and device evidence are not inferred. See specification/amendments/20261017-replay-application-journal.md.

Controlled amendment 16,17October2026: dependency-ready C08 forecast and ordering suites run sequentially on the existing lean licensed host, each freshly admitted at2.375GiB with256MiB enforced growth,128MiB cancellation margin and2GiB reserve. Both checks/allRO01–RO07 rules,25missions and prior budgets remain unchanged. No graphics/package installation or full-game peak claim. See specification/amendments/20261017-runtime-forecast-storage.md.
