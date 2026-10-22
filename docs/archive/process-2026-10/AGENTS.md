# RESQ implementation instructions

Read `BUILD_PLAN.md`, `specification/00_FREEZE_INDEX.md`, `STATUS.md`, and `HANDOFF.md` first. Then load only the frozen contract sections and task inputs needed for the next bounded step.

The original `Resq_Production_Pack/` is preserved byte-for-byte. It is a specification baseline, never implementation evidence. The freeze overlay resolves its ambiguities; user instructions take precedence. Precedence: current explicit user decisions → freeze overlay and root task graph → original modular specifications → combined editions. No silent scope cuts: five worlds, five missions each; all five Alder missions take priority over the competition date. English first. Both Quest 3 and Quest 3S require physical qualification.

Use `automation/tasks.json` for immutable task definitions and `automation/task_state.json` for mutable execution state. All build tasks start pending. Do not edit the original pack's graph, briefs, or validator to represent progress. Each accepted result must meet `specification/06_RELEASE_AND_OPERATIONS.md`, `specification/07_EVIDENCE_AND_EXECUTION.md` and the root evidence schemas. Run `python3 -B tools/validate_spec_freeze.py` after changes to frozen contracts or task definitions. This validates planning consistency only.

Preserve dirty/untracked work and unrelated Waz/Claude processes. One coordinator owns the active milestone. Delegate only concrete independent work, with disjoint write areas and acceptance/stop conditions. One writer per Unity serialized resource, shared manifest, and Blender source. One heavy build/test/import/render/emulator across the team at a time; respect Editor locks. Do not reset, clean, overwrite, or delete unrelated work. Commit only if explicitly authorized.

Runtime is offline deterministic integer/graph simulation. Input produces validated commands. Residents, occupied vehicles, buildings, and hazards cannot be grabbed to bypass strategy. Hand-only and one-hand-complete paths are required; head-dwell is a separate complete Command path. Physics, animation, wall time, and presentation scale never determine outcomes.

Use actual licensed Unity and supported Editor operations. Discover an existing MCP before proposing installation; never redirect another project's bridge. Unity package/MCP installation remains an explicit setup gate. Storage must pass the operation preflight before heavy work. A blocked device/license/provider action does not block independent pure logic, documentation, or original art work that can safely execute.

Default paid-generation and external write approvals remain disabled in `automation/autonomy_policy.json`. Prepare concrete priced/destination-scoped artifacts before approval requests. No approval is inferred from account credits or this specification-freeze request. Domain purchase, legal statements, final Devpost entry, and physical comfort observations remain human actions.

Keep editable sources, rights records, hashes, and inspected Unity renders for art. Real qualified footage is required for gameplay claims. Mobbin reference inspection precedes website composition. Public claims derive from exact release evidence. Never fabricate results, weaken criteria, disable tests, or use success stubs. Three failed code repair attempts or two asset repairs after initial creation produce a precise blocker; continue independent work.

At each milestone update STATUS, DECISIONS, BLOCKERS, HANDOFF and task evidence. Label specification/automated/device/human/released evidence separately. Context rollover uses the user's standing workflow: one successor if necessary, deterministic current-work verification, explicit ownership release, and no new scope.
