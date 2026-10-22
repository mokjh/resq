# 08 — Codex production automation and bounded autonomy

## 1. What “automated” means here

The objective is to minimize repetitive instructions and manual tool operation. It is not permission to invent success, bypass security prompts, spend without limits, submit legal declarations, or claim a headset experience was tested by reading code.

Codex should work from a persistent task graph and evidence files. A task is complete only when its required artifacts and acceptance tests exist. Unknowns become explicit blockers. Unblocked work continues independently.

OpenAI documents non-interactive Codex execution and repository instructions through AGENTS.md. Use the installed CLI's current flags and supported configuration; do not assume an old example remains valid. [S14, S15]

## 2. Startup discovery

Produce `evidence/preflight.json` once, containing:

- Actual repository path, platform, Git state, available storage, and existing files.
- Installed Unity executable/version, Android modules, license readiness, existing project and package state.
- Existing Unity MCP bridge, running Editor state, successful harmless probe, or explicit absence.
- Local Blender executable/version and a successful background geometry/export test.
- Higgsfield available tools, project policy, downloadable formats, model/credit availability, approved spending cap.
- Mobbin MCP availability and OAuth/subscription state; never mark reference research complete without results.
- Vercel identity/team/project/permissions, only if accessible and deployment is in scope.
- Android debug bridge availability, connected authorized Quest devices, actual OS/device model, capture capability.
- Node/package-manager/FFmpeg/browser-test availability.
- Required outstanding human/account gates, consolidated into one report.

Do not assume the user's desktop paths from unrelated projects. Do not install a new Unity MCP bridge over an existing one. Do not print secrets. An installed Unity application does not prove that Android modules, a license, or headset trust are ready.

## 3. Permissions and spending

Read `automation/autonomy_policy.json` before external actions. Default to local repository writes and tests; paid generation is disabled until an explicit budget is configured. A subscription does not imply unlimited paid credits.

Use four action classes:

**Local reversible:** code, docs, procedural assets, tests, previews, build artifacts, scoped commits. Proceed within the repository and authorized working directories.

**Authorized external reversible:** provider generation within a verified cap, preview deployment to an identified project, private asset upload. Proceed only when the provider, destination, data exposure, and budget are preauthorized.

**External/public:** production deployment, public media upload, changing DNS, changing release channels. Require the specific standing or one-time authorization recorded in policy; never infer it from a broad request for a specification.

**Entrant/hardware:** signing into accounts, legal attestations, physical play/comfort assessment, domain purchase, final Devpost entry. Report these gates; do not bypass them.

If an action is blocked, write a precise blocker with the smallest prerequisite, continue all independent tasks, and summarize once at the next milestone. Do not repeatedly ask the same question.

## 4. Orchestrator model

Task states: `pending`, `ready`, `running`, `blocked`, `failed`, `passed`, `skipped_with_reason`. A task has dependencies, scope, inputs, expected outputs, acceptance commands, evidence requirements, retry limit, and a designated write area.

The orchestrator selects a ready task, gives a narrow worker the relevant specification, runs validation independently, and marks completion only from validation results. A worker's natural-language claim is not a test result.

Workers:

- Producer: scope and dependency management, no authority to weaken release gates silently.
- Gameplay: pure simulation and content compilation.
- XR: interaction, placement, accessibility, lifecycle.
- Art: Higgsfield/Blender assets, provenance, export and integration.
- QA: tests, replays, performance evidence, regression triage.
- Web: Mobbin research, site implementation, browser/a11y/performance checks.
- Capture/editor: real footage selection, timeline, captions, exports.

Workers may run in parallel only on isolated files/worktrees. Unity project settings, `.unity` scenes, prefab serialization, shared manifests, and `.blend` sources each have one writer at a time. Integration is sequential with compile/test checks.

## 5. Dependency order

**A — Foundation:** repository safety, capability discovery, rule snapshot, toolchain probe, style and interaction test.

**B — Game loop:** deterministic water, command validation, graph routes, residents, tools, objectives, score, one complete W1M1 blockout.

**C — XR product:** placement/fallback, hand-only menus, one-handed path, tracking recovery, pause/save/resume, controller and Command adapters.

**D — Finished first chapter:** art lock, complete shared kit, W1M2–M5 authored and reference-tested, audio, debrief, aftercare, challenge modifiers.

**E — Qualification:** actual Quest tests, thermal/performance runs, accessibility/room matrix, content and asset audits, release candidate freeze.

**F — Delivery:** actual gameplay captures, trailer edit, screenshot/press kit, website built from truthful release data, preview/production deployment under policy, competition submission packet.

**G — Full-game expansion:** W2→W5 sequentially after the first chapter passes. Implement a world's new mechanic, its tutorial, then its remaining missions. Do not generate all world art before the mechanics work.

## 6. Autonomous coding loop

For each task: inspect current state → make a small implementation → compile → run relevant tests → inspect logs/images → fix root cause → rerun → record evidence → commit a scoped change.

If three attempted repairs fail for the same issue, stop that repair loop, preserve the last working state, create a blocker, and continue an independent task. Do not repeatedly reinstall dependencies, alter test expectations, disable validation, or replace real behavior with mocks to pass.

Never call a code path “tested” merely because it compiles. Never report a test suite as passing if it was skipped due to licensing, device absence, missing service, or a command failure. Evidence status must distinguish pass, fail, blocked, and not run.

## 7. Project-specific automation tools Codex must implement

`resq doctor`: validates toolchain and capabilities; emits machine-readable status.

`resq content validate`: checks IDs, budgets, references, graph connectivity, safe capacities, event timing, objective schema, release scope, and required reference solutions.

`resq content replay`: runs a specific command replay with expected outcomes and state hashes.

`resq art audit`: validates asset manifest, units, topology stats, materials, missing textures, rigs, colliders, LODs, and Unity import results.

`resq game test`: runs EditMode, PlayMode, and smoke tests with actual results, not stub reports.

`resq game build`: creates the Android artifact in a locked build workspace; reads the Unity BuildReport; fails on errors or missing output.

`resq device qualify`: collects only what a connected authorized device can actually provide; marks manual comfort checks separately.

`resq capture`: runs approved in-engine replays and capture shots, preserves raw footage and provenance.

`resq video assemble`: builds a deterministic edit from approved raw clips, mixes audio, renders captions, and validates the export.

`resq web check`: validates content truthfulness, links, browser flows, accessibility, performance, and secret exposure.

`resq release package`: bundles exact artifacts/hashes and blocks if critical evidence is missing.

These are commands to implement in the user's project, not commands this specification claims already exist. The included `tools/validate_pack.py` and its self-test suite are the executable validation tools supplied with this planning pack.

## 8. Editor and render constraints

Use an actual licensed Unity environment. Interactive Editor and batch execution cannot write the same project simultaneously. Respect project locks and use explicit clean build copies when necessary. [S09]

For graphics captures, provide a graphics-capable runtime. A successful headless compile is not a rendered-image validation. Blender's background scripting can automate asset processing, but run safe reviewed scripts with explicit failure exit behavior. [S10]

If a device is unavailable, build simulator evidence labeled as such, continue logic/art/site work, and leave physical qualification blocked. Do not create a fake device-performance CSV.

## 9. Video automation rules

The capture worker selects clips from `captures/raw/` using a manifest of source build, device or simulator, timestamp, content hash, shot purpose, and consent/privacy review state.

Higgsfield may assist with editing or non-gameplay graphics if its discovered capabilities support that task. It must not generate gameplay that never happened, replace real hands with fabricated footage as proof, or transform the graphics beyond the shipped build's appearance. The judging brief explicitly favors honest gameplay. [S01]

If the game is not ready to capture, produce the edit decision list, narration script, and a blocker—not a falsely completed trailer.

## 10. Website automation rules

Mobbin research precedes page implementation. Retrieve real references from the user's connection, record what each teaches, choose a dominant direction, and implement original layouts/assets rather than copying screens. A failed research call is a failed call, not evidence of a design study. [S13]

Build a preview, test it, verify real feature claims, then deploy only under the authorized project/environment policy. Vercel project IDs, scope, deployment URLs, and domain status must come from actual tooling. [S16, S17]

The purchase of playresq.com remains a user action. After ownership, domain linking/DNS verification can be automated within explicit permission. Before then, retain a real preview URL and mark the custom domain pending.

## 11. Honest production status

At every milestone report: implemented artifact paths; tests actually run; unresolved failures; screenshots/videos actually inspected; real device evidence; costs consumed; blocked human/account prerequisites; next ready tasks.

Do not write “AAA,” “award-winning,” “fully autonomous,” “100% bug-free,” “perfect performance,” or “all worlds complete” as an evidence substitute.
