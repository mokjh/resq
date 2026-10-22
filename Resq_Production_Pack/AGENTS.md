# RESQ repository instructions

## Purpose
Build the game specified in this pack: a seated, hands-first miniature rescue strategy game. Keep the full five-world design, but implement and qualify Alder Valley before expanding. This pack is a specification, not a Unity project or existing game. Do not report its content as implemented.

## Read first
1. `docs/00_DECISIONS_AND_SOURCES.md`
2. `docs/01_PRODUCT_PRD.md`
3. `docs/02_GAMEPLAY_SYSTEMS.md`
4. `docs/08_CODEX_AUTOMATION_PLAN.md`
5. `automation/autonomy_policy.json` and `automation/tasks.json`
Then read only the additional specifications relevant to the active task. Gameplay workers need docs03/05/06; XR workers docs04/09; art workers doc07; web workers doc10; capture workers doc11. Keep a durable summary when context changes.

## First action
Inspect the actual selected working folder, Git state, existing Unity project, installed tools, and available integrations. Preserve all pre-existing work. Do not assume this pack's `/mnt/data` path exists on the user's computer. Create a capability/preflight report and aggregate blockers once. Run `python3 tools/validate_pack.py` before adopting or editing authored content.

## Non-negotiable gameplay
- Core loop: inspect risk → plan → place/route → observe → rescue → understand consequences.
- No picking up residents, occupied rescue vehicles, arbitrary buildings, or hazards to bypass strategy.
- The simulation is deterministic, integer/graph based and offline. Presentation physics never decide mission results.
- Default hands-first path and one-hand-completable navigation. Head-dwell Command mode is additional, not a substitute for hands.
- No runtime LLM, generated mission prose as executable logic, multiplayer, mandatory account, gacha, or punitive daily chores in competition scope.
- All worlds need different meaningful mechanics. Do not reskin the same mission 25 times.

## Production discipline
Follow task dependencies. Implement small vertical slices with actual compilation and tests. Check results independently of a worker's summary. Statuses must distinguish specification, implemented, automated-tested, device-tested, human-observed, and released.

Do not change acceptance criteria to make an implementation pass. Do not disable tests, suppress errors, or fill missing outputs with synthetic reports. Three failed repair attempts create a blocker; continue independent work. Full-game expansion must use a separate development branch/artifacts after a competition candidate is frozen; never mutate the submitted candidate.

One writer owns Unity serialized scenes/settings/prefabs, shared manifests and each Blender source at a time. Use supported Editor operations or editor scripts rather than fragile guessed YAML. Respect Unity's project lock. Never open the same project for simultaneous interactive and batch writes. Detect existing MCP before proposing additions; do not install one without approval.

## Art
Use Higgsfield only through actually discovered tools with confirmed formats, destinations, licenses and cost limits. Exported models require Blender inspection/cleanup and Unity integration. Editable sources, textures, material roles, pivots, colliders, LODs, provenance and real render checks are mandatory. Do not equate an attractive concept image with a finished mesh or a technically exported mesh with game-ready quality.

Default paid-generation allowance is zero until the user records an explicit limit. Reconcile uncertain operation IDs instead of submitting duplicates. Preserve raw outputs and license evidence. Quarantine unknown-rights assets.

## Website and video
Actual Mobbin research precedes website composition. Record a dominant reference lock and create original layouts with real game media. Never claim a failed tool call was research.

The trailer must show real qualified gameplay. No generated cinematic can stand in for unbuilt worlds or interaction proof. If footage is absent, create the storyboard/EDL and a blocker, not a fake final trailer.

Public claims come from a release manifest with evidence, not from planned features. Vercel scope/project and domain ownership must be verified; `playresq.com` is an intended domain only. Keep secrets out of code, logs, artifacts, screenshots and tool payloads.

## Autonomy boundaries
Proceed on reversible local work within the actual repository. External writes, spending, publishing, DNS and release-channel actions require the scoped authorization in `automation/autonomy_policy.json`. Never purchase a domain, accept legal terms for the entrant, fabricate human playtests, or submit Devpost with a bot. Consolidate human prerequisites; do not repeatedly request already provided information.

## Per-task handoff
Write a structured result matching `schemas/task_result.schema.json`: real artifact paths/hashes, executed test evidence, costs, limitations, blockers and next ready tasks. Update `STATUS.md`, `DECISIONS.md`, `BLOCKERS.md`, and task state with actual results. Commit only scoped changes if authorized; never perform destructive Git cleanup.

## Completion
A specification can pass pack validation. A game can pass only actual game tests. A device claim requires the actual device. A deployed site requires a real verified deployment. A published trailer requires accessible real media. Never substitute the former for the latter.
