# RESQ evidence and execution contracts

Freeze ID: **resq-spec-freeze-1.1.0**. Rules ID: **resq-rules-1**. This document fixes the implementation semantics for bootstrap, task execution, evidence and qualification. It describes required future behavior; it records no successful game, device, human, provider or release execution.

Read this with 05_PRODUCTION_ACCEPTANCE.md, 06_RELEASE_AND_OPERATIONS.md and the current task/check/gate registries. The authoring scripts generate contracts. The planning validator checks those contracts and provided evidence. Neither executes the future game harness. A missing adapter, native case, schema rule or verifier check remains implementation work and cannot be replaced with a success stub.

## 1. Bootstrap without a circular dependency

F01 has no task prerequisites. It implements `tools/bootstrap_doctor.py` as a self-contained Python standard-library program; it must not import `tools/resq.py`, invoke F02, require Unity project creation, download dependencies or need a previously qualified toolchain. Its registered command is:

~~~text
python3 tools/bootstrap_doctor.py --report evidence/checks/f01/<run_id>/<attempt_id>.json
~~~

The doctor reads the actual workspace and supplied baseline, then creates immutable initial snapshots before F02 exists:

| Output | Meaning |
|---|---|
| `evidence/foundation/initial-preflight.json` | Actual cwd, baseline hashes, Git state or absence, tool discovery, storage, relevant process ownership and unresolved readiness |
| `evidence/foundation/initial-state.json` | The initial task-state snapshot, including the actual pending implementation state |
| `evidence/foundation/initial-status.md` | What exists and which evidence dimensions remain unexecuted |
| `evidence/foundation/initial-blockers.md` | Consolidated observed constraints, their scope and independent work that remains possible |

Create the initial snapshots once with exclusive creation. If they already exist, preserve and hash them; do not replace an earlier snapshot with new observations. A resumed F01 attempt records fresh observations under its new run/attempt path and identifies the original snapshots. Initial task-state evidence is captured before any execution-state transition; never reset real progress to manufacture an all-pending snapshot.

The initial doctor checks actual baseline hashes and records native per-case outcomes for its own assertions. It preserves existing files, current dirty/untracked work and unrelated Editor/Blender/Claude processes. A missing handoff, absent Git repository, unavailable tool or unopened headset is recorded accurately. Discovery itself does not activate a license, install modules, start an unrelated Editor, trust a device, delete files or grant publication permission.

F01's evidence identity uses actual immutable input snapshots. Its source manifest includes the implemented doctor and its exact scoped inputs. Its content manifest points to the frozen authored content payload inspected by the doctor; it does not claim compiled game content. Its toolchain artifact contains observed executable paths/versions, unavailable components and `status: observed_unqualified`. The required `toolchain_hash` identifies those bytes; the identity envelope's `toolchain_lock` reference does not make an observed snapshot a qualified version pin. `build_sha256` is null. F02 must preserve this distinction when importing bootstrap evidence.

F01's own result is independently validated with the standalone evidence verifier after the doctor finishes. F02 may then implement the main runner. Every task whose argv invokes `tools/resq.py`, including F07/F08 research, has F02 as a prerequisite or ancestor. The bootstrap command is the sole exception; no task calls tooling that is scheduled to exist only after that task passes.

## 2. Toolchain uncertainty has a named acceptance milestone

F01 records observed facts and current primary-source references. F03 resolves a compatible host Editor/test setup and proves actual licensed compilation and a real pure-state test. F04 resolves the Android/XR package set from installed metadata and current Unity/Meta documentation, produces a real ARM64 IL2CPP build and preserves its exact resolution. F05 tests that same retained F04 candidate on Quest 3 and Quest 3S only after all 25 POLISH results, complete host X05/X06 and W1 tutorial. F04 records a resolved local lock; physical compatibility remains unqualified until F05. No guessed pin, license file alone or empty APK closes those milestones.

The pre-start `host_license` gate concerns authentic license readiness for the selected Editor. F03 supplies compilation proof; the gate cannot require F03's own future result. Similarly, `human_testers` establishes consent and participant availability before observation, and `entrant_final_submission` establishes entrant-only readiness before the entrant supplies the actual D12 receipt. A prerequisite gate must never require the output of the task it admits.

Before a host/XR candidate is accepted, preserve the exact documentation URLs, retrieval dates, relevant compatibility statements, installed module metadata, resolution logs and smoke-test artifacts. The checked package set is recorded in ProjectVersion.txt, manifest.json, packages-lock.json and the candidate toolchain lock. A later acceptance record binds the lock hash; it does not rewrite the lock into a different identity. Blender/export, web, media and browser tooling are probed and pinned by their own actual execution evidence.

These are acceptance milestones with fixed outputs and checks, not unspecified design choices. Host-only logic can progress after its host prerequisite while device qualification is pending. The first mission's REPLAY task runs the real host simulator twice. Later mission QUALIFY, cumulative world REGRESSION and FULL01 use the specified host, Quest 3 and Quest 3S targets and repeated state hashes. A host replay cannot be relabeled as Android or physical interaction proof. All mission QUALIFY, cumulative REGRESSION and FULL01 tasks remain downstream of F05, including their mixed host/Quest case matrices. No required case is deleted or made optional. Local development uses actual AUTHOR/REPLAY/INTEGRATE/POLISH evidence; it does not accept a partial QUALIFY.

Before each next world, retain the exact five POLISH heads and source/content/assets/toolchain in an immutable local-development snapshot, explicitly unqualified. Retain genuine first-mission blockout candidates and original look-development sources/exports/renders for the unchanged late vertical/first/look checks. Physical/human results still bind actual tested identities. World FREEZE remains a qualified signed release at the end, never a name for a local checkpoint. Late failure creates affected rework and reruns under the existing bounds; it does not lower acceptance criteria.

## 3. Run paths and immutable history

The registry's report path is a template, not a filename containing literal braces. Expand only `{run_id}` and `{attempt_id}` from a validated execution context. Each ID matches `[a-z0-9][a-z0-9_-]{0,63}`; path traversal, separators, unknown placeholders and an existing output destination fail before execution. The check command and reported argv must equal the registry command after this exact substitution.

| Record | Required path convention |
|---|---|
| Check report | `evidence/checks/<task-or-suite>/<run_id>/<attempt_id>.json`, from its registry template |
| Task result | `evidence/results/<task_id>/<run_id>/<attempt_id>.json` |
| Logs/native/case artifacts | `evidence/runs/<task_id>/<run_id>/<attempt_id>/...` |
| Gate observation | `evidence/gates/<gate_id>/<observation_id>/gate.json` |
| History event | `automation/history/<task_id>/<run_id>/<attempt_id>-<sequence>.json` |
| Mutable projection | `automation/task_state.json` |

Allocate a run and attempt before launch, write to a temporary file in the same authorized volume, flush it and finalize by a no-overwrite atomic operation. A check report is finalized once. Running/progress events use separate history files; they do not repeatedly mutate the final report. Crash recovery preserves temporary logs and records an interrupted/blocked attempt rather than manufacturing a completed run.

An attempt retains exact argv, cwd, actor, UTC start/end, return code, redacted stdout/stderr, native reports, case records, source/content/toolchain identity and referenced artifacts. Empty stdout/stderr files are allowed when a command produced no output; required deliverables and proof artifacts must be nonempty. A rerun never changes a prior failed report into passed. Human review records and provider-operation records follow the same append-only rule.

Accepted task history stores the task ID, run/attempt IDs, event sequence, prior event hash when present, accepted result path/hash, status, reason and verifier identity. `task_state.json` is only the current projection: update it atomically after durable history and independent verification. Recover the projection from history if interrupted; a green projection without a matching accepted result is invalid.

Registered `expected_artifacts` identify the logical deliverables. Each task result contains `output_bindings`, an array of `{expected_path, artifact_path}` objects. The expected-path set equals the registry's expected artifact set exactly, with no duplicate or missing role. Each artifact path resolves to the hashed immutable copy under `evidence/runs/<task_id>/<run_id>/<attempt_id>/artifacts/`, and that copy appears in the result's artifact records. Preserve the original relative path and hash in the source/output manifests. A verifier resolves the binding and rehashes the stored bytes; it cannot validate an old result by assuming today's edited workspace is the original snapshot. This binding must be implemented in the schema/verifier before mutable artifacts can support accepted historical results. Keep required untracked source; exclude credentials, caches and unrelated work. Do not include a manifest's own changing hash in its file list.

## 4. Retries, ownership and admission

Admission requires the exact prerequisite results and gate records, permitted write areas, current operation storage and free single-writer/heavy locks. Register task-owned PIDs, parents, cwd and purpose. One heavy operation runs across the team. Respect the exact Unity project lock and one writer per serialized resource, shared manifest, .blend file and remote generation project. A passed old storage snapshot does not reserve current capacity.

Code/general repair permits an initial attempt plus at most **three repairs**: at most four execution attempts for that unresolved defect. Asset creation or repair permits an initial attempt plus at most **two repairs**: at most three attempts for that asset family. The art-specific bound takes precedence over a generic task bound. Human tasks have zero automatic repair attempts; an agent may correct transcription/validation errors but cannot rerun a person or invent approval.

Each repair records the failed attempt, root cause, changed inputs and remaining allowance. A new run ID, renamed file, provider retry or restarted agent does not reset the bound. A separately reviewed original rebuild is a recorded change of approach, not an unlimited hidden retry. Unknown paid-operation outcomes are reconciled using the original operation ID before any new submission; retries still require remaining scope and budget. A gate block before launch records no executed acceptance case and consumes no repair attempt. Preserve failures and continue independent ready work when a bound or gate blocks one branch.

Use the operation thresholds in 06 section 3.1: Unity `max(40 GiB, peak new/intermediate + 10 GiB)`; art `max(15 GiB, 3 × new output + 10 GiB)`; web `max(10 GiB, peak new/intermediate + 5 GiB)`; capture/video `max(20 GiB, 2 × raw + 3 × exports + 10 GiB)`. Measure every output volume immediately before the operation, including temporary files. Unknown estimates fail admission. Below 5 GiB free during execution, repeated ENOSPC or sustained critical memory pressure triggers supported cancellation/checkpoint of the owned operation only.

## 5. Exact dependency and gate bindings

Every task result contains `prerequisite_results` and `gate_results`, even when either is empty. Their wire shapes are:

~~~json
{
  "prerequisite_results": [
    {"task_id": "F01", "result_path": "evidence/results/F01/<run>/<attempt>.json", "result_sha256": "<64 lowercase hex digits>"}
  ],
  "gate_results": [
    {"gate_id": "working_storage", "evidence_path": "evidence/gates/working_storage/<observation>/gate.json", "evidence_sha256": "<64 lowercase hex digits>"}
  ]
}
~~~

These are shape templates, not evidence. On a passed result:

1. The prerequisite task-ID set equals the task's direct `depends_on` set exactly, with no duplicate, missing, extra, self or unknown ID. F01's set is empty.
2. Each referenced result is an existing permitted file with its recomputed SHA-256, the matching internal `task_id` and internal `status: passed`. At acceptance its path/hash also equals that prerequisite's current accepted `task_state` result path/hash. Its checks, artifacts and own dependency closure independently validate. A `status` assertion in the caller or task-state projection cannot replace the dereferenced status.
3. The gate-ID set equals `required_gates` exactly. For a newly admitted operation, each gate file/hash equals the registry's current evidence head; internal gate ID, registered type, passed status, criteria, evidence and current action scope validate. Already accepted prerequisite history retains its immutable gate observation and action-time authorization, without requiring today's volatile head. Empty/mismatched references fail.
4. Resolve recursively in DAG order with cycle detection and a visited-result cache. Cache only a tuple of path, hash and exact validation inputs; invalidate it if any bytes or registry identity change. A dependency path cannot point to the caller or another result that depends on the caller.
5. Bind the task/check/gate/case-registry hashes used for execution through its source/input manifest. Recheck those and output hashes at acceptance to catch a change during execution.

Dependencies represent historical inputs, so F03 need not have the future final APK hash. The current task's check reports must all match its result identity. Exact final-candidate equality is additionally enforced only for the requalification tasks identified in section 9; changing an unrelated later documentation file does not turn a historical implementation task into a false current-candidate test.

Replacing a prerequisite task result requires dependent acceptance to be checked against that new result head. Refreshing a volatile gate applies to newly admitted operations and does not invalidate already accepted prerequisite history. Prior result files and observations remain immutable; explicit candidate requalification still governs changed game builds/content.

## 6. Gate evidence JSON contract

`specification/schemas/gate_evidence.schema.json` and its semantic verifier must enforce this shape. The following deliberately contains placeholders and cannot be accepted as a real record:

~~~json
{
  "schema_version": 1,
  "gate_id": "working_storage",
  "status": "passed",
  "type": "machine",
  "reviewer_kind": "machine",
  "reviewer_alias": "<actual verifier/tool identity>",
  "observed_utc": "<actual UTC timestamp>",
  "criteria": {"pass_condition": true},
  "evidence": [
    {
      "path": "evidence/gates/working_storage/<observation>/storage.json",
      "sha256": "<64 lowercase hex digits>",
      "kind": "storage_measurement",
      "byte_length": 0
    }
  ],
  "authorization_path": null
}
~~~

`type` equals the gate registry's `kind`: account, machine, permission, hardware, human, external or paid. `reviewer_kind` is exactly human or machine and identifies who performed the recorded verification. Human consent, owner/legal decisions and signoff require authentic human evidence; a machine can validate its record without becoming the human decision maker.

For this freeze's single-condition gate registry, `criteria` must equal **`{"pass_condition": true}` exactly**. An empty map, additional keys, strings, null, false or omitted condition fails. Its evidence must establish every clause of the gate's recorded `pass_condition`; the boolean alone is not proof. If a later registry introduces named criteria, the versioned schema/verifier must require the exact new key set, all true, without accepting an older weaker map.

Evidence is a nonempty array of real permitted artifacts with recomputed hash, measured positive size and appropriate content. `authorization_path` is null only when the gate does not require separate action authorization. If non-null, it resolves to an actual authorization record, also included hash-bound in `evidence`; validate its authentic user-instruction reference, destination/action/exposure, artifact hashes, budget and expiry. A subscription, provider login or available credits is not that authorization. A factual gate never silently authorizes external writes.

The registry's `authorization_actions` is an exact action allowlist for each permission/external/paid gate: `unity_setup`, `preview_deployment`, `production_deployment`, `public_video_upload`, `release_channel_upload`, `commercial_publication` or `paid_generation`, as assigned. An approval for another action cannot pass the gate. DNS, purchases and other separate actions require their own explicit scope. Expired authorization records may remain in the policy's history, but only currently valid records can enable new allowances.

Gate state may point to the observation's `evidence_path` and `evidence_sha256`, but the immutable gate file remains the authority. Re-observe volatile facts such as storage, device connection, entitlement validity and provider destination before the dependent action. Preserve prior gate observations. Gates are readiness/permission records; hardware qualification, actual human playability, published playback and final entrant receipts remain separate task evidence.

Current gate heads apply to a newly admitted operation. Recursive verification of an already accepted prerequisite uses that prerequisite's immutable gate observation, including authorization validity at the recorded observation/action time. Refreshing a storage/device observation must not invalidate historical results. The verifier still requires current direct prerequisite task-result heads and explicit candidate requalification; historical gate preservation grants no permission for a new action. An expired authorization can remain valid evidence of an earlier permitted action while blocking any new use.

## 7. Native cases establish coverage

`tools/evidence_case_contracts.py` defines the dimensions and reducers used to author `specification/content/acceptance_cases.json`. The case catalogue is frozen planning data. Generating it executes zero game/device/human cases. The executor uses its declared `case_id`, kind, exact dimensions and minimum metrics; a worker cannot replace it with a shorter list or an aggregate number.

A passed check must satisfy all of these conditions:

- `case_records` contains every required case ID exactly once and no substituted case. Each row's kind/dimensions match the catalogue exactly. All required statuses are passed; failed, blocked, skipped, absent and zero-case runs fail.
- The native XML or native-summary JSON contains the corresponding executed case IDs/statuses. Native runner totals and the check's counts agree. A zero-test exit 0, screenshot of a log or hand-written aggregate cannot pass.
- Each case has an existing hash-bound evidence file containing its case ID, exact dimensions, result identity, observed metrics and applicable proof index. It is separate from the check report and cannot cite the check itself as evidence.
- Metrics come from the actual test/session/artifact and meet the case's fixed thresholds. Native state hashes, timings and outputs are retained. Their absence remains unknown; it cannot be coerced to zero or success.
- `proof_index` resolves to an appropriate record when the case requires physical, human or hosted evidence. The proof's build/device/model/session or reviewed artifact identity must match the case. A local/simulator record cannot fill a physical slot.
- The verifier recomputes every coverage value with the catalogue's reducer over validated case records and compares it with both the declared coverage and registry expectation. Counts, distinct counts, sets, minimums and minimum group counts have their defined meaning; callers cannot submit a larger number to conceal missing groups.

Case IDs encode their sorted immutable dimensions. A filesystem-friendly case artifact name may use SHA-256 of the full ID, while the JSON retains the exact ID. Participant slots are planning slots, not people; only actual distinct consented participant aliases count. Multiple slots filled by the same person do not increase the distinct-person count.

Reference replay coverage distinguishes configurations from executions. The full campaign has 300 base successful configurations and 120 challenge configurations, each with the required target/repeat executions at final qualification. Initial mission REPLAY uses host twice; mission QUALIFY, world REGRESSION and FULL01 use host/Quest 3/Quest 3S twice and compare their canonical hashes. Negative/recovery cases remain separately required. C09 establishes the W1 fuzzer infrastructure and 1,000 seeds; each later Wn-MECHANIC supplies its world's 1,000 seeds with at least 1,000 attempted commands per seed. These tests do not replace 210 physical full-mission runs, 40 five-minute profiles, ten 30-minute thermal soaks or authentic human observations.

Host-only REPLAY remains independent of device readiness. Every task whose canonical case matrix includes a Quest runtime has `quest_devices` as a direct required gate, so Android replay qualification obtains a current observation instead of inheriting an old probe's connection state.

Web case labels resolve to the exact 13 page outcomes in 06, including `/worlds`, `/how-to-play` and the real not-found outcome. No invented `/media` route substitutes for `/worlds`. Video cases cover both clean/captioned exports and SRT/VTT, with actual duration/format measurements. Each 160-second edition has ten storyboard segments; separate human tasks review the entire exported files.

## 8. Evidence dimensions cannot be promoted

| Dimension | Required proof | What it cannot establish |
|---|---|---|
| Specification | Validated contracts, schemas, catalogue and DAG | Executed implementation or solvability |
| Automated | Real command/native cases, source identity, outputs and measured results | Human experience, physical comfort or public availability |
| Physical device | Actual Quest model/OS/session, candidate hash, observations and captures/traces | An untested model, build, input path or future release |
| Human observation | Consented real participants, observer/participant aliases, build/session, actual observations and thresholds | AI-generated participants, simulated comfort or inferred enjoyment |
| Human signoff | Real reviewer, exact inspected hashes, criteria, decision and time | A review of a later modified export or an agent's own approval |
| External verified | Actual destination/resource/URL, matching authorization and real verification evidence | Publication from a local build or a provider Ready status alone |

A human does not need to execute a shell command. A human-validation adapter may run and return exit 0 after validating supplied real review records; that exit proves the adapter ran, not that a person watched or played. The separate proof record supplies the human fact. The same rule applies to importing actual device/provider observations. Authenticity still requires those real actions; valid JSON and matching hashes alone cannot manufacture them.

D03 and FULL-VIDEO perform technical assembly/probing. D03-REVIEW and FULL-VIDEO-REVIEW carry the human acceptance. D05/FULL-WEB cover local website evidence; D06/FULL-WEB-PREVIEW and production verification cover their actual deployed destinations. Preserve these distinctions in user-facing status and release claims.

## 9. Release closure and identity

A release's `qualification_tasks` uses the same `{task_id, result_path, result_sha256}` references as task prerequisites. Its `edition` is exactly `game_only` or `delivery_bundle`. The verifier checks the internal passed status, required checks, evidence dimension and transitive dependency closure of every referenced result. Arrays of mission names, asserted device names or opaque evidence files are insufficient.

The required qualification sets are:

| Claim/stage | Required task closure |
|---|---|
| World Wn manifest emitted by Wn-FREEZE | Wn-REGRESSION, Wn-PERFORMANCE, Wn-DEVICE and Wn-HUMAN; the emitting FREEZE result is accepted separately |
| Five-mission competition local candidate | W1-REGRESSION, W1-PERFORMANCE, W1-DEVICE, W1-HUMAN, W1-FREEZE, D03-REVIEW and D05, including their prerequisites |
| Full `game_only` manifest emitted by FULL-QUALIFY | FULL01, FULL-PERFORMANCE, FULL-DEVICE and FULL-HUMAN; the emitting FULL-QUALIFY result is accepted separately |
| Full `delivery_bundle` | FULL01, FULL-PERFORMANCE, FULL-DEVICE, FULL-HUMAN, FULL-QUALIFY, FULL-VIDEO-REVIEW and FULL-WEB, including their prerequisites |

**Only upstream results are required inside a manifest emitted by a task.** Wn-FREEZE's own output binds its four completed REGRESSION/PERFORMANCE/DEVICE/HUMAN results; it cannot refer to its own unfinished result. FULL-QUALIFY's own output binds FULL01/FULL-PERFORMANCE/FULL-DEVICE/FULL-HUMAN and excludes its own result. The independent verifier then accepts the emitting task. A later candidate/bundle, including FULL04, binds the now-accepted FULL-QUALIFY result; a later competition candidate binds the now-accepted W1-FREEZE result. A qualified successor record is a new immutable file. No artifact contains a hash of a result that, in turn, contains that artifact's hash.

The emitter handling is derived from the declared scope/edition and actual registered task being verified, never from a worker-selected omission. It cannot remove another required task, waive failed upstream evidence or make a candidate into a submitted/released product. A completed world claim also requires its accepted Wn-FREEZE result; a completed full-game claim also requires its accepted FULL-QUALIFY result. Those results are referenced by downstream records rather than by their own output manifests. If packaging discovers a new defect, it emits a failed/blocked attempt and starts a new candidate; it does not revise accepted history.

The candidate is signed before final measurements so its APK SHA-256 is stable. Every referenced current-candidate requalification result must bind the candidate's exact APK/build SHA-256, content hash, `resq-rules-1` identity and tested toolchain hash:

- World stage: that world's REGRESSION, PERFORMANCE, DEVICE and HUMAN, plus FREEZE when referenced downstream.
- Full stage: FULL01, FULL-PERFORMANCE, FULL-DEVICE and FULL-HUMAN, plus FULL-QUALIFY when referenced downstream.
- Competition stage: the W1 qualification set for its own immutable five-mission APK.

Earlier implementation/authoring/art/tool-discovery results keep their historical source identities and remain auditable prerequisites; they are not all required to match a future APK. Media/web reviews bind their exact export/site identities and the source candidate through provenance manifests, rather than pretending their FFmpeg/Node toolchain is the game's Unity toolchain. A final current-candidate human result must explicitly reconcile any reused compatible observations against the exact candidate and identify the human decision; automatic inheritance of an old APK's complete human/device matrix is forbidden.

Changing code/content/assets/packages or re-signing/rebuilding into different APK bytes invalidates affected candidate claims and requires the registered current-candidate qualification again. An identity difference cannot be hidden by changing a declared hash. Full qualification covers all 25 missions, world qualification covers its complete five, and competition qualification covers all five W1 missions. Zero P0/P1/P2 remains mandatory; reviewed P3 limitations are explicit.

## 10. Publication and final acceptance stay separate

Game qualification and the local delivery bundle do not depend on DNS ownership, Vercel deployment, video upload, a store channel or Devpost submission. The local bundle can be qualified while those tasks remain blocked/pending. Conversely, a live website or public trailer does not qualify the game.

Public/released claims require matching publication records, exact artifact/destination authorization, actual deployed/playback/channel verification and the current release identity. Competition eligibility/legal confirmations and final submission remain entrant actions. The planned `playresq.com` domain remains pending user ownership and verification; no domain or account action is implied by this document.

A final task pass requires complete exact checks/cases, all required criteria true, nonempty actual artifacts, recomputed hashes, valid upstream/gate closure, matching candidate identities where required, no blocking issues and independent acceptance. Report remaining outcomes separately. Do not turn planned tooling, generated contract counts or a successful specification test into implementation, device, human, hosted or release acceptance.
