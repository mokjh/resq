# RESQ specification freeze 1.1.0

Freeze ID: `resq-spec-freeze-1.1.0`. Rules ID: `resq-rules-1`. Date: 15 October 2026, Cairo. Status: implementation contract; no game implementation or qualification implied.

This overlay resolves the remaining planning gaps while preserving the original pack. The original pack's 41-file inventory is recorded in `specification/baseline_manifest.json`; the original content remains unchanged. The root validator verifies hashes before accepting a freeze revision.

Controlled amendment 3, 16 October 2026: the user approved the prepared $0 setup scope and moved every real Meta test to final qualification after all 25 missions reach local POLISH. See `amendments/20261016-physical-tests-last.md`. Task/check/case counts and acceptance thresholds are preserved; local and physical qualification remain distinct.

## Read order

1. `BUILD_PLAN.md`: milestones, architecture, task execution and completion.
2. `01_SCOPE_AND_DECISIONS.md`: binding product choices and corrections.
3. `02_RUNTIME_RULES.md`: exact simulation, transport, equipment, time and hazard algorithms.
4. `03_RUNTIME_CONTRACTS.md`: typed commands/content/state/save/replay contracts and numeric fixtures.
5. `04_MISSION_AUTHORING.md` plus `content/mission_contracts.json`: all 25 mission bindings and authored-layout requirements.
6. `05_PRODUCTION_ACCEPTANCE.md`: asset/audio inventory, UX, hardware, performance and human gates.
7. `06_RELEASE_AND_OPERATIONS.md`: evidence, pipeline tools, capture, site, release and permissions.
8. `07_EVIDENCE_AND_EXECUTION.md`: standalone bootstrap, immutable history, native case coverage, exact prerequisite/gate/result bindings and release closure.

Machine-readable inputs: `automation/tasks.json`, `automation/checks.json`, `automation/gates.json`, `specification/content/{mission_contracts,layout_recipes,equipment_rules,modes_and_challenges,production_inventory,acceptance_cases}.json`, and `specification/schemas/`. Mutable task state is stored separately. F01 implements its standalone bootstrap doctor; F02 implements the future `tools/resq.py` command dispatcher. Registered commands are requirements, not supplied game tooling.

## What is frozen

Full scope; architecture and public/internal interfaces; deterministic rule interpretation; temporal objectives; mission populations/budgets/tool stock; meaningful mastery; layout authoring constraints; support matrix; challenge catalogue; source/export and evidence contracts; task ownership/dependencies/outputs/checks; failure and approval boundaries.

## What must be established by implementation evidence

Exact resolved SDK versions, a working license/build, authored runtime terrain/graphs, numerical balance, successful command sequences, final creative assets, measured rendering/performance, human comfort/fun, account identity/destinations, rights clearance and approved publishing. These are named task outputs with mandatory checks, not unanswered design choices. Initial tuning is frozen as a reproducible starting point; a proven bad value is corrected by versioning the affected content and rerunning dependent checks.

## Validation now

`python3 -B tools/validate_spec_freeze.py --report evidence/specification-freeze/validation.json`

`python3 -B tools/test_spec_freeze.py`

The original pack validator and nine mutation tests also remain runnable. Freeze validation establishes contract completeness, mission coverage and DAG/check integrity. It does not establish runtime solvability, game quality, or release readiness.

Controlled amendment4,16October2026: F03 alone uses a measured lean host profile and separate host_working_storage gate. All four native assertions and all25missions remain. Other Unity/Android/render storage gates are unchanged. See amendments/20261016-bounded-f03-host.md.

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
