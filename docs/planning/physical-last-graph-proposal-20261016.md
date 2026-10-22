# Physical-last task graph proposal — 16 October 2026

Planning proposal only. The coordinator owns the scheduling amendment, state migration and freeze seal. This specialist wrote only this file; it did not edit contracts/state, install anything, launch tests/builds/Editors/devices or accept a task. The original production pack remains untouched.

## Decision and scope

Move every real Meta/Quest test, VR-dependent human observation, performance run and real gameplay capture after all 25 missions have completed host/content/art development. Preserve all 229 task IDs, 231 checks, 942 assertions and 35,784 required case records. No new task/check/case is necessary: F05 can serve as the dependency barrier. All five worlds, five missions each, every hardware/human/acceptance threshold and existing repair bound remain mandatory.

The overridden requirements are scheduling requirements: physical W1 vertical/first-world gates and device look review previously preceded art/content expansion, and a qualified previous-world release previously preceded the next mechanic. They move to final qualification. Their actual evidence criteria stay intact. Host work remains provisional until the late physical/human gates pass.

## Complete device-dependent inventory

Explicit physical-device tasks (20): F05, W1-VERTICAL, W1-PERFORMANCE, W1-DEVICE, W2-FIRST, W2-PERFORMANCE, W2-DEVICE, W3-FIRST, W3-PERFORMANCE, W3-DEVICE, W4-FIRST, W4-PERFORMANCE, W4-DEVICE, W5-FIRST, W5-PERFORMANCE, W5-DEVICE, D02, FULL-PERFORMANCE, FULL-DEVICE, FULL03.

Mixed host/Quest replay tasks (31), despite their automated classification: all 25 Wn-Mm-QUALIFY, all five Wn-REGRESSION and FULL01. The catalogue requires host/quest3/quest3s repeated canonical-state runs; each currently has quest_devices. They must remain late. Per-mission Quest cases total 140 for each W1 mission and 124 for each later-world mission; cumulative Quest cases are 700/1320/1940/2560/3180 for W1–W5 regression and 3180 for FULL01. These counts exclude their host/assertion records and are not the distinct 210 full-mission hardware matrix.

VR-human tasks: W1-HUMAN through W5-HUMAN and FULL-HUMAN. ART-LOOKDEV-HUMAN is also device-dependent because its first assertion requires actual four-asset device views; its current absence of quest_devices is an admission gap. Add quest_devices to that task. The amendment must not treat desktop contact sheets alone as completion.

Other device-dependent operations remain late by closure: Wn-FREEZE and FULL-QUALIFY bind current physical/human evidence; release packaging requires exact signed-APK clean-install/launch/save/resume checks on both devices; D10 judge-channel validation includes real clean install; FULL02 includes current platform/offline-entitlement checks; FULL04 packaging and FULL05 actual clean-install/publication verification inherit FULL-QUALIFY. D03-REVIEW/FULL-VIDEO-REVIEW review genuine late footage. These are not new physical passes that can run during development. D12 is an entrant action, not a VR playtest, and remains separately authorized.

F03/F04/F06, X01–X06, all AUTHOR/REPLAY/INTEGRATE/POLISH, C01–C09 and W2–W5-MECHANIC retain actual licensed host/Editor/import/build/source evidence. They have no physical acceptance substitution: host-injected tracking/anchor/hand commands prove only the host assertions. F04 remains a real local Android IL2CPP build, without installation on a headset.

## Exact dependency replacements

The following are complete replacement depends_on arrays, not additions. Unlisted arrays stay byte-for-byte equivalent. Required gates/checks/cases stay unchanged except adding quest_devices to ART-LOOKDEV-HUMAN.

```json
{
  "F05": [
    "F04",
    "W1-M1-POLISH",
    "W1-M2-POLISH",
    "W1-M3-POLISH",
    "W1-M4-POLISH",
    "W1-M5-POLISH",
    "W2-M1-POLISH",
    "W2-M2-POLISH",
    "W2-M3-POLISH",
    "W2-M4-POLISH",
    "W2-M5-POLISH",
    "W3-M1-POLISH",
    "W3-M2-POLISH",
    "W3-M3-POLISH",
    "W3-M4-POLISH",
    "W3-M5-POLISH",
    "W4-M1-POLISH",
    "W4-M2-POLISH",
    "W4-M3-POLISH",
    "W4-M4-POLISH",
    "W4-M5-POLISH",
    "W5-M1-POLISH",
    "W5-M2-POLISH",
    "W5-M3-POLISH",
    "W5-M4-POLISH",
    "W5-M5-POLISH",
    "X05",
    "X06",
    "W1-TUTORIAL-LOGIC"
  ],
  "W1-M2-AUTHOR": [
    "W1-M1-INTEGRATE",
    "W1-TUTORIAL-LOGIC"
  ],
  "W1-M3-AUTHOR": [
    "W1-M1-INTEGRATE",
    "W1-TUTORIAL-LOGIC"
  ],
  "W1-M4-AUTHOR": [
    "W1-M1-INTEGRATE",
    "W1-TUTORIAL-LOGIC"
  ],
  "W1-M5-AUTHOR": [
    "W1-M1-INTEGRATE",
    "W1-TUTORIAL-LOGIC"
  ],
  "ART-LOOKDEV": [
    "W1-M1-INTEGRATE",
    "W1-TUTORIAL-LOGIC",
    "F06"
  ],
  "ART-SHARED": [
    "ART-LOOKDEV"
  ],
  "ART-HF-BRIEF": [
    "ART-LOOKDEV"
  ],
  "ART-LOOKDEV-HUMAN": [
    "ART-LOOKDEV",
    "F05"
  ],
  "W2-MECHANIC": [
    "W1-M1-POLISH",
    "W1-M2-POLISH",
    "W1-M3-POLISH",
    "W1-M4-POLISH",
    "W1-M5-POLISH",
    "C09"
  ],
  "W2-M2-AUTHOR": [
    "W2-M1-INTEGRATE"
  ],
  "W2-M3-AUTHOR": [
    "W2-M1-INTEGRATE"
  ],
  "W2-M4-AUTHOR": [
    "W2-M1-INTEGRATE"
  ],
  "W2-M5-AUTHOR": [
    "W2-M1-INTEGRATE"
  ],
  "W2-KIT": [
    "W2-M1-INTEGRATE",
    "ART-SHARED"
  ],
  "W2-AUDIO": [
    "W2-M1-INTEGRATE",
    "ART-SHARED"
  ],
  "W3-MECHANIC": [
    "W2-M1-POLISH",
    "W2-M2-POLISH",
    "W2-M3-POLISH",
    "W2-M4-POLISH",
    "W2-M5-POLISH",
    "C09"
  ],
  "W3-M2-AUTHOR": [
    "W3-M1-INTEGRATE"
  ],
  "W3-M3-AUTHOR": [
    "W3-M1-INTEGRATE"
  ],
  "W3-M4-AUTHOR": [
    "W3-M1-INTEGRATE"
  ],
  "W3-M5-AUTHOR": [
    "W3-M1-INTEGRATE"
  ],
  "W3-KIT": [
    "W3-M1-INTEGRATE",
    "ART-SHARED"
  ],
  "W3-AUDIO": [
    "W3-M1-INTEGRATE",
    "ART-SHARED"
  ],
  "W4-MECHANIC": [
    "W3-M1-POLISH",
    "W3-M2-POLISH",
    "W3-M3-POLISH",
    "W3-M4-POLISH",
    "W3-M5-POLISH",
    "C09"
  ],
  "W4-M2-AUTHOR": [
    "W4-M1-INTEGRATE"
  ],
  "W4-M3-AUTHOR": [
    "W4-M1-INTEGRATE"
  ],
  "W4-M4-AUTHOR": [
    "W4-M1-INTEGRATE"
  ],
  "W4-M5-AUTHOR": [
    "W4-M1-INTEGRATE"
  ],
  "W4-KIT": [
    "W4-M1-INTEGRATE",
    "ART-SHARED"
  ],
  "W4-AUDIO": [
    "W4-M1-INTEGRATE",
    "ART-SHARED"
  ],
  "W5-MECHANIC": [
    "W4-M1-POLISH",
    "W4-M2-POLISH",
    "W4-M3-POLISH",
    "W4-M4-POLISH",
    "W4-M5-POLISH",
    "C09"
  ],
  "W5-M2-AUTHOR": [
    "W5-M1-INTEGRATE"
  ],
  "W5-M3-AUTHOR": [
    "W5-M1-INTEGRATE"
  ],
  "W5-M4-AUTHOR": [
    "W5-M1-INTEGRATE"
  ],
  "W5-M5-AUTHOR": [
    "W5-M1-INTEGRATE"
  ],
  "W5-KIT": [
    "W5-M1-INTEGRATE",
    "ART-SHARED"
  ],
  "W5-AUDIO": [
    "W5-M1-INTEGRATE",
    "ART-SHARED"
  ],
  "W1-M1-QUALIFY": [
    "W1-M1-POLISH",
    "X05",
    "X06",
    "W1-VERTICAL",
    "ART-LOOKDEV-HUMAN"
  ],
  "W1-M2-QUALIFY": [
    "W1-M2-POLISH",
    "X05",
    "X06",
    "W1-VERTICAL",
    "ART-LOOKDEV-HUMAN"
  ],
  "W1-M3-QUALIFY": [
    "W1-M3-POLISH",
    "X05",
    "X06",
    "W1-VERTICAL",
    "ART-LOOKDEV-HUMAN"
  ],
  "W1-M4-QUALIFY": [
    "W1-M4-POLISH",
    "X05",
    "X06",
    "W1-VERTICAL",
    "ART-LOOKDEV-HUMAN"
  ],
  "W1-M5-QUALIFY": [
    "W1-M5-POLISH",
    "X05",
    "X06",
    "W1-VERTICAL",
    "ART-LOOKDEV-HUMAN"
  ],
  "W2-M1-QUALIFY": [
    "W2-M1-POLISH",
    "X05",
    "X06",
    "W2-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W2-M2-QUALIFY": [
    "W2-M2-POLISH",
    "X05",
    "X06",
    "W2-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W2-M3-QUALIFY": [
    "W2-M3-POLISH",
    "X05",
    "X06",
    "W2-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W2-M4-QUALIFY": [
    "W2-M4-POLISH",
    "X05",
    "X06",
    "W2-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W2-M5-QUALIFY": [
    "W2-M5-POLISH",
    "X05",
    "X06",
    "W2-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W3-M1-QUALIFY": [
    "W3-M1-POLISH",
    "X05",
    "X06",
    "W3-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W3-M2-QUALIFY": [
    "W3-M2-POLISH",
    "X05",
    "X06",
    "W3-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W3-M3-QUALIFY": [
    "W3-M3-POLISH",
    "X05",
    "X06",
    "W3-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W3-M4-QUALIFY": [
    "W3-M4-POLISH",
    "X05",
    "X06",
    "W3-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W3-M5-QUALIFY": [
    "W3-M5-POLISH",
    "X05",
    "X06",
    "W3-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W4-M1-QUALIFY": [
    "W4-M1-POLISH",
    "X05",
    "X06",
    "W4-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W4-M2-QUALIFY": [
    "W4-M2-POLISH",
    "X05",
    "X06",
    "W4-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W4-M3-QUALIFY": [
    "W4-M3-POLISH",
    "X05",
    "X06",
    "W4-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W4-M4-QUALIFY": [
    "W4-M4-POLISH",
    "X05",
    "X06",
    "W4-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W4-M5-QUALIFY": [
    "W4-M5-POLISH",
    "X05",
    "X06",
    "W4-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W5-M1-QUALIFY": [
    "W5-M1-POLISH",
    "X05",
    "X06",
    "W5-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W5-M2-QUALIFY": [
    "W5-M2-POLISH",
    "X05",
    "X06",
    "W5-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W5-M3-QUALIFY": [
    "W5-M3-POLISH",
    "X05",
    "X06",
    "W5-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W5-M4-QUALIFY": [
    "W5-M4-POLISH",
    "X05",
    "X06",
    "W5-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W5-M5-QUALIFY": [
    "W5-M5-POLISH",
    "X05",
    "X06",
    "W5-FIRST",
    "ART-LOOKDEV-HUMAN"
  ],
  "W2-REGRESSION": [
    "W2-M1-QUALIFY",
    "W2-M2-QUALIFY",
    "W2-M3-QUALIFY",
    "W2-M4-QUALIFY",
    "W2-M5-QUALIFY",
    "W1-FREEZE"
  ],
  "W3-REGRESSION": [
    "W3-M1-QUALIFY",
    "W3-M2-QUALIFY",
    "W3-M3-QUALIFY",
    "W3-M4-QUALIFY",
    "W3-M5-QUALIFY",
    "W2-FREEZE"
  ],
  "W4-REGRESSION": [
    "W4-M1-QUALIFY",
    "W4-M2-QUALIFY",
    "W4-M3-QUALIFY",
    "W4-M4-QUALIFY",
    "W4-M5-QUALIFY",
    "W3-FREEZE"
  ],
  "W5-REGRESSION": [
    "W5-M1-QUALIFY",
    "W5-M2-QUALIFY",
    "W5-M3-QUALIFY",
    "W5-M4-QUALIFY",
    "W5-M5-QUALIFY",
    "W4-FREEZE"
  ],
  "D01": [
    "W1-M1-POLISH",
    "W1-M2-POLISH",
    "W1-M3-POLISH",
    "W1-M4-POLISH",
    "W1-M5-POLISH",
    "F02"
  ],
  "D02": [
    "D01",
    "W1-FREEZE"
  ]
}
```

Change count: 68 existing dependency arrays. The explicit 25 POLISH IDs in F05 are deliberate: depending on W5 alone would not guarantee that every earlier mission finished development. X05/X06 guarantee complete host Command/controller paths before any real-device operation; W1 tutorial is explicit.

W1-VERTICAL and W2–W5-FIRST keep their current depends_on arrays, mode, checks and physical evidence. Their F05 ancestor now places them at the end. Each mission QUALIFY explicitly depends on its physical first-slice and ART-LOOKDEV-HUMAN; each later world REGRESSION explicitly depends on the prior world FREEZE. This restores all release-evidence closure removed from development ordering. FULL01 can keep [W5-FREEZE] because W5-FREEZE once again has every earlier qualified world in its ancestry.

D01 can complete capture tooling/EDL preparation after host W1 POLISH. D02 explicitly gains W1-FREEZE so moving the planner does not admit unqualified capture. D04/D05 acceptance still waits on real media; optional local site scaffolding cannot be accepted or advertised as qualified gameplay before then.

## What the local boundary actually proves

Every mission already has meaningful host validation before its POLISH pass: AUTHOR validates both typed layouts/topology; REPLAY executes the actual host simulator twice with two strategies, three presets, challenges and negative/recovery cases; INTEGRATE executes the real Operation scene, command path, pause/resume/fallback and prohibits grabbing residents/occupied vehicles; POLISH executes integrated asset/audio/UI checks against the actual Unity scene. C09 and each mechanic retain 1000 seeds × at least 1000 attempted commands, exact invariants and golden tests. X01–X06 retain complete host input/UI/recovery checks. This is substantive local evidence, not a green placeholder.

Mission QUALIFY and cumulative world REGRESSION stay unaccepted until final Quest runs. Additional host-only development regression may be run as explicitly labeled native preparation, but cannot pass those tasks with missing Quest records. No required case is removed, split, waived, marked skipped or silently moved into a smaller matrix.

Before moving from a finished world to the next, retain an immutable local development snapshot containing source/content/assets/toolchain identity and the exact five POLISH heads; label it local-development / unqualified. Store the original W1-M1 and later Wn-M1 blockout sources/content/scene/art identities and executable candidates needed by the unchanged late vertical/first-mission blockout checks. Those late checks must test retained genuine blockout candidates, not claim that a final-art build was the earlier blockout. Capture the actual four look-development assets/source/export/Unity renders immutably so late human/device review binds the intended originals. This can be an artifact under existing task write areas, with no new acceptance task/check.

Wn-FREEZE remains exclusively the signed, physically/human-qualified release freeze at the end. Never rename local snapshots as Wn-FREEZE passes or use them as completed-world/public-claim evidence. Preserve local checkpoints byte-for-byte; a failed late look/comfort/performance test requires a new development candidate and affected reruns. The explicit user decision accepts later discovery/rework risk, not lower quality.

## F04/F05 package candidate lock

F04 must preserve a real locally compiled Android/XR probe APK and exact ProjectVersion, manifest, packages-lock, SDK/NDK/JDK and toolchain.lock bytes. That is a resolved local candidate lock; it remains physically unqualified throughout all 25-mission development. The F05 assertion “exact package lock accepted only after probe” means physical compatibility acceptance, not permission to use recorded package versions in local builds. Document this distinction in 06 section 2 and 07 section 2. A later F05 acceptance adds an evidence record; it must not rewrite the F04 candidate lock and thereby change its hash.

The existing F05 backend/runner special binding tests the exact current accepted F04 APK/source/content/toolchain identity, retaining executor identity separately. Adding POLISH prerequisites must not cause it to bind the later full-game source identity in place of F04 or to test a rebuilt but differently hashed probe. Preserve F04 immutable artifacts as genuine early probe inputs; F05 remains a compatibility test of that APK. If the package/toolchain changes during development, obtain a new accepted F04 candidate and perform appropriate dependency invalidation/rechecks. Do not let F05 qualify a stale lock while final game uses another. The final full-game APK still requires every FULL01/FULL-PERFORMANCE/FULL-DEVICE/FULL-HUMAN case independently. A working minimal probe cannot qualify the game.

Any later rebuild/re-sign/package/source/content/art change requires impact analysis and registered current-candidate requalification. Do not rewrite accepted reports or declared hashes. Preserve F05 backend review compatibility with the unchanged check-f05 assertion/case IDs.

## Amendment and migration consequences

1. Update tools/author_task_graph.py deterministically with these arrays and the missing look-review gate; update BUILD_PLAN and relevant specification 01/05/06/07 wording that enforces physical-before-expansion sequencing. Scheduling change is explicit user authority; acceptance thresholds remain fixed.
2. Replace validator assertions that require previous FREEZE or FIRST ancestors for local expansion with host M1-INTEGRATE / prior-world all-five POLISH requirements. Add invariants: every physical_device, human_observation, quest_devices/Quest-target task and device-view look review has all 25 POLISH ancestors; no POLISH task has any such physical/human ancestor; every qualified release retains first-slice/F05/look-human/prior-qualified-world closure. Keep exact scope and catalogue equality checks.
3. Do not blindly run the current author_task_graph.py: its task-state setdefault preserves existing entries, but it rewrites mutable gates/other generated ledgers later in main. Coordinator must merge only authorized definition changes and preserve authentic state/gate heads, attempt history, accepted result paths/hashes, repair counts and resource/approval observations. No initial-state reset.
4. Schema currently carries dependency arrays but no development-vs-qualified status distinction; use explicit local snapshot terminology and evidence kind. Keep physical target cases and quest_devices gates on every mixed-runtime task. Do not loosen coverage reducers or proof identity checks.
5. Prepare controlled freeze amendment/archive/seal with a truthful migration record. Changed frozen task definitions can make existing accepted F02/F07/F08 source snapshots stale under current verifier rules. Coordinator must assess exact invalidation closure and reverify/reaccept only with genuine new checks; never rewrite historical immutable results. F01 remains historical bootstrap evidence, not approval of amended scheduling.
6. Freeze-validator and appropriate scheduling/evidence tests must pass after root applies the amendment. Planning consistency remains specification evidence, not gameplay/Quest/human completion. Keep storage/license/setup gates; this graph amendment alone does not authorize installation, cleanup, headset use, captures, recruitment, publication, paid generation or legal attestations.

## Static proposal audit

On an in-memory copy of the current 229-task graph, these replacements are acyclic. Every explicit physical task, human-observation task, current quest_devices task and ART-LOOKDEV-HUMAN has all 25 POLISH ancestors. No POLISH task has any of those ancestors. Each Wn-FREEZE has F05, its first-slice gate, ART-LOOKDEV-HUMAN and every previous Wk-FREEZE in its ancestry. This audit evaluated graph structure only; no acceptance check or test suite was executed. The unchanged catalogue retains 231 checks / 942 assertions / 35,784 cases.
