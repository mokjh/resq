> **Historical planning draft — retained for reference.**
> The implementation source of truth is [BUILD_PLAN.md](BUILD_PLAN.md), refined by [the specification freeze](specification/00_FREEZE_INDEX.md).
> Execute the 229-task root graph in `automation/tasks.json`; read progress from `STATUS.md` and `automation/task_state.json`.
> The earlier draft below is preserved verbatim after this notice. Its original-pack precedence, 38-task graph and starting-state observations are superseded. See [the reconciliation](docs/planning/PLAN_RECONCILIATION.md).

# RESQ — Complete Game and Production Build Plan

## 1. Summary and confirmed starting point

Build the complete **25-mission, five-world RESQ game**, its production tools, editable assets, tested Quest builds, authentic gameplay video, marketing website, and release package.

Your decisions are locked:

- Preserve all five Alder Valley missions and all 25 full-game missions. Quality and full scope take priority over the competition deadline.
- Ship English first, with localization-ready text and layouts.
- Qualify both Quest 3 and Quest 3S using physical headsets and human testing.
- Keep the project at `/Users/dev/Desktop/Resq`; free internal storage before heavy production work.

I reviewed the supplied prompt and complete production pack: all 13 specifications, mission/equipment data, schemas, automation files, diagrams, validation evidence, and combined Markdown/HTML editions. The combined documents match their modular sources. The pack validator and all **nine validator tests pass**.

The [production specification](/Users/dev/Desktop/Resq/Resq_Production_Pack/RESQ_MASTER_SPEC.md) remains the product contract. The [38-task graph](/Users/dev/Desktop/Resq/Resq_Production_Pack/automation/tasks.json) supplies traceable milestones, with the dependency corrections below.

| Area | Verified starting point |
|---|---|
| Project | Specification only; no Git repository, Unity game, finished assets, APK, footage, or website |
| Machine | M1, 8 GiB RAM; last check showed approximately **3.5 GiB free** |
| Unity | **6000.3.24f1** installed; Android Build Support missing; RESQ compilation/license readiness unproven |
| Other tools | Blender **5.2.1**, Node **22.13.1**, Python **3.14.7**, FFmpeg **9.0.1**, Git LFS, and Playwright available |
| Existing work | Another project owns active Unity/MCP processes; unrelated Claude Blender helpers also exist |
| Integrations | Mobbin returns actual references; Higgsfield read access works with **402.95 credits**; Vercel team accessible, with no RESQ project |
| Devices | You confirmed access to both targets; connection, trust, OS versions, and capture remain to be verified |

The competition deadline remains **18 November 2026, 22:00 Cairo**. Enter only if the complete chapter passes its gates; otherwise continue the full build without reducing scope. [Official rules](https://start-developer-competition-26.devpost.com/rules)

## 2. Foundation, architecture, and contract corrections

### Repository and executable production tools

Preserve `Resq_Production_Pack` as the original specification baseline. Create the implementation repository around it, with separate areas for the Unity game, runtime content, Blender sources/exports, website, automation, captures, evidence, and immutable releases.

Use Git LFS for large editable sources. Preserve Unity `.meta` files. Keep caches, signing keys, credentials, private room recordings, and generated build outputs outside ordinary source control.

Create actual working commands:

- `resq doctor`
- `resq content validate` and `resq content replay`
- `resq art audit`
- `resq game test` and `resq game build`
- `resq device qualify`
- `resq capture` and `resq video assemble`
- `resq web check`
- `resq release package`

Use a small Python command runner that invokes real Unity Editor tooling, Blender scripts, FFmpeg, and website checks. Pure simulation tests initially run through Unity EditMode, avoiding a separate .NET dependency.

Maintain execution state separately from the supplied task graph: its validator intentionally requires every specification task to remain pending. Accepted tasks must have existing artifacts, matching hashes, successful required checks, and matching build/content identities. Changed inputs invalidate dependent evidence.

### Toolchain qualification

Retain installed Unity **6000.3.24f1** as the candidate. After storage is available:

1. Provision that Editor’s matched Android Build Support, SDK, NDK, and JDK.
2. Resolve one supported OpenXR/Core/Interaction SDK/MRUK combination.
3. Use one XR origin, one hand-input provider, URP, and world-space uGUI/TextMeshPro.
4. Build a minimal Android ARM64/IL2CPP application demonstrating hands, passthrough, placement, fallback, and suspension.
5. Install and exercise it on both target headsets.
6. Lock exact Editor, package, Android-tool, and rendering versions from the successful probe.

Current Meta documentation supports this integration direction, but an installed Editor or documentation search does not establish compatibility. [Meta Unity setup guidance](https://developers.meta.com/vr/documentation/unity/unity-project-setup)

Inspect the existing Unity integration before adding anything. Never redirect the other project’s bridge or install a duplicate speculatively. If RESQ has no usable bridge, supported Editor scripts and batch operations remain the implementation path.

Split toolchain work into host compilation and Android/XR qualification so missing headset integration does not unnecessarily block pure simulation development.

### Runtime architecture

Keep these boundaries explicit:

- **Core and Simulation:** Unity-independent IDs, integer units, commands, deterministic clock, hazards, routes, residents, equipment, power, objectives, and forecasting.
- **Input and XR:** hand/controller/head-dwell adapters, permissions, placement, tracking, anchors, and platform lifecycle.
- **Presentation and UI:** pooled models, interpolation, animations, audio, accessible menus, and feedback.
- **Persistence:** atomic versioned saves, migrations, backups, progression, and reward deduplication.
- **Editor tooling:** content compilation, prefab/scene factories, audits, tests, builds, and capture.

Use Bootstrap, Hub, and one reusable Operation scene. Load only the active world’s assets. No gameplay backend, mandatory application account, runtime LLM, remote content dependency, or payment SDK is required.

### Resolve the identified specification gaps

Before W1-M1 production:

- **Compile briefs into runtime content.** Define versioned layouts, navigation/hazard graphs, sockets, infrastructure, spawns, equipment stock, events, objectives, presets, challenges, and reference commands. Prose never becomes executable mission logic.
- **Freeze deterministic ordering.** Process commands in stable sequence, revalidate queued actions at execution, apply scheduled events, allocate power, advance hazards, update route validity and transport, then evaluate objectives/outcome. Define boundary-tick precedence and integer rounding in golden tests.
- **Make water allocation exact.** Use integer heads, bounded conductance, simultaneous transfers, and shared source-volume allocation with stable remainder ordering. Pumps transfer water; sources and sinks are separately accounted.
- **Separate guidance from replay.** First play teaches bridge→barrier→skiff. Unguided replay permits both authored strategies. Persist tutorial progress and the hazard-clock activation point; test both reference strategies on the same unguided configuration.
- **Define transport capacity explicitly.** Separate passenger seats from edge occupancy. A bridge has two movement slots; pedestrians use one and the fixed bus uses both. Passengers consume bus seats, not additional bridge slots. Reserve vehicle and receiving capacity atomically.
- **Specify interruption behavior.** New operation, saved continuation, tracking recovery, and assisted checkpoint rollback are separate paths. Relocation/fallback restores the active attempt without returning through a fresh loadout.
- **Support temporal objectives.** Distinguish end-state, never-exposed, maintained-during-transfer, and completed-before-event conditions. Conservation and capacity invariants remain mandatory regardless of stars.
- **Separate input from pacing.** Standard, Relaxed, and Command rules use shared commands. Relaxed starts with half-speed hazard/scenario progression and unchanged transport speed; Command provides pause, advance 1 second, advance 5 seconds, and Run. Store all clock state.
- **Strengthen evidence contracts.** A task cannot pass with required failed/not-run checks. An asset cannot qualify without source hashes, rights, import evidence, and required audits. Release manifests link exact code, rules, content, assets, APK, device evidence, and media.

Record these interpretations in the implementation decision log before dependent content is authored.

## 3. Complete implementation sequence

### Milestone 0 — Safe foundation

Create the repository structure, implementation instructions, preflight report, consolidated blockers, task runner, evidence schemas, and durable status/handoff files.

Measure installation, cache, build, and capture storage needs before heavy commands. Reserve working capacity beyond each operation’s estimated output. Approximately **40–60 GiB free** is an initial planning allowance, to be replaced by measured requirements.

**Exit:** original files preserved; real tool statuses recorded; lightweight validation works; installation and build prerequisites are explicit.

### Milestone 1 — Toolchain and spatial interaction probe

Complete the Android/XR probe and Blender calibration/export probe. Verify one original calibration asset through Blender export and Unity import.

Exercise both tested board classes—compact and wide—with real-surface selection and virtual-table fallback. Confirm seated height/distance adjustment, far selection, dominant-hand preference, tracking interruption, and explicit resume.

**Exit:** reproducible Android build, successful physical probe on both targets, inspected Unity render, and exact toolchain lock.

### Milestone 2 — Deterministic rescue engine

Implement:

- Fixed 10 Hz simulation and immediate application-level pause.
- Water, barriers, gates, pumps, supply stock, dry power networks, and priorities.
- Ground/water navigation, resident states, the fixed bus, skiffs, reservations, boarding, unloading, and safe rerouting.
- Validated commands, deduplication, forecasts, objectives, score, rewards, and replay hashes.
- Full snapshots, atomic saves, backups, migrations, and recovery.

Forecasts clone authoritative state and cannot mutate the live run. Presentation physics never decide rescue outcomes.

**Exit:** real executable tests establish conservation, determinism, command atomicity, transport correctness, and save/replay equivalence.

### Milestone 3 — Complete W1-M1 vertical slice

Build **First Crossing** with intentionally simple original geometry:

- Both layouts and the complete bridge/barrier/skiff rescue.
- Boot, placement, briefing, planning, gameplay, pause, outcome, restart, and continuation.
- Near and far hand interaction, one-handed completion, cancel/preview feedback, and tracking recovery.
- State-driven tutorial, real warnings, causal debrief, and save/resume.
- Two meaningfully different successful command replays, plus no-input and deliberate-mistake runs.

Bring tutorial logic forward from T17 so the first slice is complete before art expansion. Finish its visual/audio treatment later.

**Exit:** the rescue is understandable and replayable on physical hardware; initial human observations support the interaction direction. Measure the first-rescue target of under 90 seconds after placement.

### Milestone 4 — Final art pipeline and shared kit

Complete the look-development set: **cottage, bridge, skiff, resident**.

Inspect it inside Unity under MR and virtual-table lighting, seated and close views, reduced quality, and capture framing. Lock silhouettes, proportions, material roles, shadows, palette, and interaction hierarchy.

Then produce:

- Shared equipment and infrastructure.
- Alder Valley environment/landmark kit.
- Three resident silhouettes, restrained variants, mobility representation, and seven shared animation actions.
- Original icons, wordmark, mission cards, captions, routes, and feedback.
- Licensed/original music stems and event-driven sound effects.

Keep editable Blender sources. Preserve generated GLB/raw outputs when used; export audited meshes, animations, and textures through a calibrated Unity pipeline. Validate pivots, dimensions, material count, colliders, LODs, textures, rigs, and provenance.

Higgsfield generation proceeds only after a concrete asset batch has a verified cost and explicit spending authorization. Original Blender work can continue independently.

**Exit:** coherent inspected Unity contact sheets, passing asset audits, editable sources, and traceable rights records.

### Milestone 5 — Complete Alder Valley

Finish:

1. First Crossing.
2. The Lower Road.
3. Two Banks.
4. Power Before Water.
5. The Long Way Home.

Integrate all 24 specified application states, complete Command mode, controller alternatives, accessibility settings, progression, results, causal debriefs, help, credits/privacy, postcards, and aftercare.

Implement the first two challenge modifiers as individually authored configurations: **Early Surge** and **Alternate Dock**. Do not enable arbitrary modifier combinations.

Every mission retains its distinct decision, two strategies, failure explanation, tutorial needs, and recovery behavior.

**Exit:** five complete missions with final art/audio, both layouts, all required modes, validated variants, and no unfinished destinations in navigation.

### Milestone 6 — Qualify and freeze the first chapter

Run the full automated, physical-device, performance, and human test matrices. Profile throughout development; this milestone repeats qualification against the final candidate.

Resolve every P0/P1 issue. Freeze the signed APK, source/content/toolchain hashes, asset manifest, evidence bundle, known limitations, and truthful release manifest.

If the competition deadline remains feasible, prepare that exact candidate for its distribution channel. Otherwise retain the qualified chapter milestone and continue the full game.

**Exit:** a reproducible, independently evidenced first-chapter candidate.

### Milestone 7 — Authentic media, website, and competition package

Mobbin research may proceed earlier as independent work. Before website composition, inspect the three required reference categories and record one dominant reference plus at most two supporting patterns. The access probe completed here is not a finished reference lock.

After qualified gameplay exists:

- Capture real human hand/room demonstrations and deterministic replay shots with distinct provenance.
- Preserve raw footage and a capture manifest.
- Assemble the specified **160-second** edit using the supplied storyboard.
- Deliver an editable EDL, captioned version, clean master, SRT/VTT, and measured media validation.
- Build the Next.js/TypeScript site using npm and a pinned lockfile.
- Deliver homepage, how-to-play, worlds/details, accessibility, press, support, privacy, optional judge information, and proper not-found handling.
- Derive claims, availability, hardware support, and media from the release manifest.
- Omit waitlist collection until a real provider is authorized and implemented.

Prepare the accurate Devpost draft and judge guide. Verify a Vercel preview after scoped authorization, then production and domain linking after their separate prerequisites. Public video upload and release-channel distribution likewise require their specific authorization.

**Exit:** genuine media, tested site, verified authorized deployments/public playback, and an accurate entrant-ready submission packet.

### Milestone 8 — Build the remaining four worlds

Preserve the frozen chapter candidate. Continue on a separate development/artifact track.

Each world follows the same sequence: new mechanic → first mission → two strategies → device/input qualification → world kit → remaining four missions → onboarding/audio/aftercare → complete qualification.

| World | New systems | Five missions |
|---|---|---|
| **Emberwood** | Fuel, heat, wetness, forecast wind, finite water, firebreaks, sprinklers, crawlers | A Line in the Trees; Last Bus from Camp; Water Is a Route; When the Wind Turns; The Green Corridor |
| **Frostline** | Stability, structure-specific braces, warned runout, winches, duty cycles, vertical staging | Hold the Station; Above the Snowline; The Quiet Slope; Cable and Current; Dawn at Frostline |
| **Tidemark** | Tide/current schedules, dock windows, dry relay networks, multi-leg transport | Between Two Tides; Keep the Light; The Floating Market; Storm Switch; Harbor of Everyone |
| **Cinder Reach** | Authored lava channels, finite coolant, cooling/reheating, coolant production, temporary shelters | A Path Through Heat; Borrowed Safety; The Cooling Works; Three Ways Out; Every Little Life |

Each world gets its own recognizable environment, landmark, sound identity, recovery transformation, and meaningful mechanics. Completed earlier worlds remain in regression coverage.

**Exit:** all 25 authored missions qualify with coherent campaign progression and complete recovery/accessibility paths.

### Milestone 9 — Full commercial release

Qualify the complete campaign, clean installations, upgrades from the first chapter, save migration, world transitions, offline play, final credits, and full asset rights.

Complete name clearance, current store requirements, pricing/commercial decisions, store materials, support information, and privacy disclosures. Update the website and gameplay media from the full-release manifest.

**Exit:** tested full-game project and signed build, audited source assets, production tools, evidence archive, accurate store/release materials, and verified authorized publication.

## 4. Test plan and acceptance criteria

| Layer | Required proof |
|---|---|
| Content | Valid IDs, graphs, sockets, capacities, budgets, typed events/objectives, supported configurations, and reference solutions |
| Simulation | Water/person/resource conservation; stable ordering; duplicate-command safety; forecast isolation; objective boundary ticks; deterministic replay |
| Persistence | Interrupted writes, truncation, disk failure, backup recovery, future schemas, migration, suspended runs, and reward idempotency |
| Unity | EditMode and PlayMode results; imports, prefab references, UI transitions, interrupted interactions, pooling, loading cancellation, and unload cleanup |
| Missions | Two distinct successful strategies for every supported base configuration; no-input failure; deliberate mistake; tracking interruption; saved/resumed equivalence |
| Physical XR | Full controller-free journey; right/left hand; seated reach; occlusion; lighting; denied permission; poor surfaces; recentering; anchor recovery; Command mode |
| Performance | Both headsets, busiest released content, repeated mission/world transitions, and 30-minute thermal runs |
| Human play | At least ten consenting adult participants; first rescue, coaching needs, forecast comprehension, strategic understanding, voluntary replay, and comfort |
| Website/media | Responsive/keyboard/reduced-motion behavior, real links, media failures, content truth, deployed behavior, full-video review, provenance, and caption/audio checks |

The base replay matrix is at least **300 successful executions**: 25 missions × two layouts × three rule presets × two strategies. Add challenge configurations, controller/input equivalence checks, and failure/recovery cases.

Initial performance targets remain:

- Sustained native **72 Hz** on both headsets.
- p95 CPU ≤10 ms; GPU ≤11 ms.
- Simulation tick p95 ≤1.5 ms.
- No recurring managed allocations in steady gameplay hot paths.
- Stable memory after repeated world loads/unloads.

These are acceptance targets until measured. Simulator, automated, device, human, and released evidence remain separate.

## 5. Execution boundaries and delivery discipline

- Free internal capacity before Android provisioning, Unity imports/builds, asset batches, or capture. No unrelated cleanup is included.
- Run one RESQ heavy operation at a time; preserve the existing Waz and Claude work.
- Give any specialist a bounded independent assignment. Keep one writer for Unity serialized assets, shared manifests, and each Blender source.
- Stop repeated code repair after three failed attempts; allow at most two automatic asset repairs after the initial attempt. Record the blocker and continue independent work.
- Keep paid generation disabled until a priced request is authorized. An account balance is not a spending allowance.
- Keep publishing, provider uploads, DNS, and release-channel changes behind their recorded scopes. Domain ownership remains unverified.
- Entrant eligibility, legal statements, physical comfort observations, and final Devpost submission remain human actions.
- Rebaseline calendar checkpoints after the Android probe and first playable mission establish actual throughput. Do not promise a full-release date from the current specification-only state.
- End each milestone with actual artifact paths/hashes, executed checks, inspected renders, device/human evidence, costs, blockers, and a self-contained handoff.

The first implementation milestone is the preserved repository, executable evidence framework, consolidated preflight, and toolchain preparation. Full completion requires all 25 qualified missions and their supporting production and release artifacts.
