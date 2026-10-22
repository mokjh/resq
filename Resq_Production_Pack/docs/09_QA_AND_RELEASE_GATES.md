# QA, playtesting, and release gates

## 1. Evidence, not a completion claim

Every accepted task identifies the tested commit, content hash, toolchain lock, test command, exit status, artifact paths, and limitations. A screenshot of an Editor scene is not proof of a playable APK. Passing desktop simulation tests is not proof of hand tracking, Quest performance, comfort, or enjoyable pacing. The current pack has none of that device evidence.

Use four separate statuses: `automated_pass`, `device_pass`, `human_observed_pass`, and `not_verified`. Do not collapse them into a single green check. An inaccessible provider is `blocked`, not a skipped success. Release claims are assembled only from the evidence manifest.

## 2. Testing layers

| Layer | Required coverage | Evidence |
|---|---|---|
| Content validation | Unique IDs, valid references, budgets, bounds, solvable graph candidates, objective predicates, event order, spawn counts | Machine-readable validator report |
| Pure simulation | Conservation, schedules, resources, reservations, terminal states, deterministic checksums | C# test report and replay hashes |
| Unity EditMode | Serialization, imports, prefab references, scene build list, asset limits, content compiler | Test XML and audit JSON |
| Unity PlayMode | Mission lifecycle, views, UI commands, saves, transitions, pooling, interrupted interactions | Test XML and scoped recordings |
| Simulator | Hand interaction paths, MR permission fallback, UX states | Labeled emulator recording |
| Physical Quest | Tracking, comfort, performance, thermal stability, installation, suspend/resume | Build/device/OS-specific capture and observations |
| Web | Responsive routes, keyboard, media, metadata, broken links, deployed behavior | Browser report, screenshots, deployment ID |
| Video | Honest provenance, readable edit, audio/caption sync, duration, public playback | Capture manifest and media validation report |

## 3. Simulation tests that must exist

**Water:** zero inflow keeps a static basin stable; outflow never exceeds available volume; closed barriers do not leak through disabled edges; opening a gate redistributes volume; pumps move exactly their permitted amount; power loss stops pumping; negative volume cannot occur; conservation equals old volume plus sources minus sinks. Stress with high gradients and full cells. Render meshes cannot influence these results.

**Navigation:** residents never enter an impassable edge; an already occupied edge resolves using the documented crossing policy; boats reserve seats atomically; an occupied skiff cannot be picked up; two groups cannot reserve the same final seats; an invalid dock blocks boarding with a clear reason; safe arrival counts each resident once; path rerouting is stable under equal costs.

**Resources:** an invalid preview consumes nothing; a accepted deployment consumes supplies once; retries with the same command ID are idempotent; dismantling cannot generate infinite supplies; generator capacity and consumers cannot create negative remaining power; disconnected networks do not share power.

**Objectives:** threshold equality is tested; mandatory and optional predicates remain separate; a player cannot obtain a rescue star merely by saving infrastructure; an already rewarded outcome cannot award again after restart; assisted and unassisted records remain separate; unlocked content persists without duplicate rewards.

**State and time:** pause applies immediately even when simulation ticks are stopped; suspended applications do not advance hazards by elapsed wall-clock time; save/resume recreates queues, reservations, hazards, RNG state, pending events, and equipment; a replay yields identical canonical state hashes on every supported tested architecture.

**Later-world additions:** fire consumes fuel and respects wetness; wind changes cannot retroactively ignite old cells; braces apply to their own structural groups; winch boarding and unloading conserve people; tides change legal routes at defined ticks; cooling consumes its own resources and cannot permanently neutralize unlimited lava without a designed cost.

## 4. Mission authoring gate

Each mission needs two recorded successful reference command sequences, not just the two prose strategies in `missions.json`. They should use meaningfully different decisions. Record complete states at decisive ticks and confirm the expected score/objectives. Validate both default supported layout classes independently.

Also record a no-input run. For normal missions, inaction must not satisfy the core goal. The outcome may vary by mission, but failure must be understandable and recoverable. Use a deliberately bad route or equipment placement to test the debrief explanation. The game should report the actual cause, not a random tutorial sentence.

Run a seeded command fuzzer with legal and illegal commands against the simulation. Its purpose is safety and invariants, not proving that a level is fun or universally solvable. Do not describe fuzzer completion as human playtesting.

## 5. Hands-first qualification

Perform a complete cold-start-to-exit run without pairing controllers: launch, onboarding, table setup, menu, briefing, equipment selection, gameplay, pause, settings, results, next mission, save/resume, help, and exit. Platform-level setup remains subject to the actual headset's capabilities.

Test right-hand only, left-hand only, hands resting, common occlusions, normal indoor lighting, fast accidental pinches, very slow release, a pinch beginning outside a target, tracking interruption during a grab, and changing dominant hand. Test one-handed access to every critical action, not only gameplay tools.

A tracking failure must never finalize placement, discard equipment, spend supplies, or continue a dangerous uncontrolled mission. If the active hand is lost during manipulation, freeze that interaction and provide a clear resume/cancel path. A genuinely hands-free Command-mode session does not pause merely because hands are absent.

Head-dwell qualification includes inspecting without activating, adjustable dwell duration, destructive confirmation, reading captions, cancelling, and completing every menu. Never advertise eye tracking based on a head ray.

## 6. Physical-space and comfort matrix

Test a usable table, no table, denied room permission, a narrow surface, cluttered surfaces, moved furniture, changed lighting, recentring, a different room, and seated positions near a wall. The game must offer virtual-table fallback without asking the player to walk around. Do not place meaningful content outside a safe reachable/viewable envelope.

Observe seated players across varied reach and posture. Record discomfort, neck/arm fatigue, missed targets, unreadable text, confusion, and voluntary stopping. Collect consent for observations; do not collect room scans or private video unnecessarily. For minors, use appropriate guardian consent and applicable research safeguards; do not recruit minors automatically.

The test team should see no required standing, no compelled leaning over hazards, no forced camera rotation, no essential content behind the player, and no control that needs sustained raised arms. An observed issue blocks an unqualified accessibility claim, even when the code is correct.

## 7. Performance targets

These are project targets, not measured results or guaranteed headset capabilities:

- Native 72 Hz baseline. Profile CPU and GPU independently; initial p95 budgets are 10 ms CPU and 11 ms GPU, leaving room within a 13.89 ms frame.
- Measure the busiest released mission, maximum allowed visible residents/tools, hazard effects, menus over gameplay, and repeated scene transitions.
- Run a 30-minute thermal/mission-loop soak on both Quest 3 and Quest 3S before claiming both devices qualified. Record resolution, refresh rate, OS, power/thermal state, profiling mode, and frame-time distribution.
- No recurrent hot-path allocations, unbounded object growth, missing resident views, or increasing memory over repeated mission reloads. A clean crash-free short run is not a leak test.
- Investigate every sustained missed-frame segment; do not hide it behind averaged FPS or a sped-up trailer.

Performance work should reduce overdraw, costly shaders, transparent layers, mesh/detail density, and unnecessary updates before changing authored rules. Device-specific render quality may vary; game outcomes may not.

## 8. Playability gate

Begin with at least ten consenting target players across younger and older adult groups. This is formative testing, not a representative market study. For the first chapter, record time to first meaningful rescue, requests for help, failed gestures, understanding of the hazard forecast, voluntary replay, and later return where observable.

Initial internal gates: most participants complete the first rescue without verbal coaching; at least half voluntarily choose another mission or attempt when free to stop; players can explain one trade-off; no recurring control or comfort failure remains unresolved. These are product decisions, not industry benchmarks or statistically validated success predictions.

A player saying the visuals are beautiful is not enough. Ask what they would do differently next run. An inability to answer can reveal that the experience is spectacle without strategy.

## 9. Release blocker classification

| Severity | Example | Decision |
|---|---|---|
| P0 | Crash/data loss; unsafe required movement; credentials leaked; APK cannot launch | Stop release and related dependent work |
| P1 | Mission softlock; unplayable one-hand path; false rescue score; critical tracking issue | Must fix or remove the affected feature from the release |
| P2 | Noticeable visual defect; unclear optional objective; intermittent noncritical UI issue | Fix before candidate where feasible; document any residual issue |
| P3 | Small polish issue without functional or communication impact | Backlog with evidence |

No P0/P1 issues may remain in the released path. Removing a feature means hiding its navigation, correcting website/video claims, and updating the scope manifest—not leaving a disabled button that promises it works.

## 10. Competition candidate checklist

Confirm eligibility separately with the entrant. Confirm the current official rules, correct division, original work history, and rights/provenance records. Install the actual signed APK from the intended distribution channel on a clean device. Verify judge access without a paid account, tutorial reset, fallback placement, and English instructions. Recheck public video visibility and duration, website links, privacy, third-party-brand restrictions, and all text claims.

Freeze an immutable artifact set with APK SHA-256, commit, content hash, package lock, release-channel reference, final video hash, and submission draft. Keep access available for the judging period and do not modify the submitted version after the deadline except through explicit organizer instructions. The entrant, not an automated browser bot, completes the final Devpost submission. [S01]
