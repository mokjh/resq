# C08 next-approach review — 20 October 2026

**SOURCE/EVIDENCE REVIEW ONLY. NOT IMPLEMENTED, NOT EXECUTED, NOT A NEW AUTHORIZATION.** The smallest justified next step is one separately reviewed attribution diagnostic of the retained final candidate. The evidence does not yet select a defensible next optimization. C08 remains failed and C09 unaccepted; the exhausted round remains closed.

## Retained boundary and evidence

The current candidate is `c08-resumable-executor-20261018-repair03`, sealed in [candidate.json](/Users/dev/Desktop/Resq/evidence/runtime-forecast-refactor-20261018/repairs/03-final-bounded-work/candidate.json), SHA256 `b31c7babefea3a640945d689c02a6f1dbb040ffa9ecc9de51a79869ffe2e77b9`. [native07-review.json](/Users/dev/Desktop/Resq/evidence/runtime-forecast-refactor-20261018/native07-review.json), SHA256 `f9760e762603d4148ea47c5245d4ee4a4222f9ee777963f81cfa93d5682544a8`, records three passed forecast cases and one timing failure after 1,157.781954667007 seconds, exit1, without timeout/resource stop. Standard/Relaxed authoritative and actual ReplayRunner equality passed. Ordering and fresh affected C01–C07 regressions did not execute; the registered case sidecar was absent.

Cumulative counters remain **6 repairs / 12 executions / 8 raw failures**; the closed round remains **3 repairs / 7 executions / 5 raw failures / 4 chargeable code failures**. Preserve attempt03's resource correction and all failed sources/history. `tools/task_execution.py:175` currently requires `4 < 4`, which is false. A different diagnostic name cannot admit another execution through that exhausted round.

Below, witness names are exact files under [attempt07 forecast-replay-recordings](/Users/dev/Desktop/Resq/evidence/runs/C08/resumable-20261018/attempt-07/checks/check-c08/forecast-replay-recordings). Capacity rows use `<name>.timing.json`; small first rows use `standard.result.json` / `relaxed.result.json` → `whole_public_calls`, and their warm rows use `<name>.timing.json`.

| Witness | Overruns above1,000µs | Maximum whole call µs | Overruns with Gen0 delta |
| --- | ---: | ---: | ---: |
| capacity.standard.first |188|18,396|55|
| capacity.standard.warm |144|18,267|55|
| capacity.relaxed.first |152|27,033|34|
| capacity.relaxed.warm |155|25,506|34|
| standard first replay |116|44,515|58|
| standard.warm |111|7,890|58|
| relaxed first replay |210|12,243|106|
| relaxed.warm |211|8,092|105|

Cold capacity Standard `Begin` was1,297µs; first `AdvanceFrame`+`Poll` was3,763µs, labelled `forecast.allocation_counter_probe`. These are separate unresolved violations. Capacity first/warm fixtures each retain4,096 water/navigation nodes,16,384 hydraulic links/navigation edges,256 residents,32 vehicles,510 prepared pumps,512 power nodes,128 objectives and2,048 events. They are synthetic admitted fixtures, not global maxima or authored missions. All11 candidate/capacity/timing/replay file identities referenced by the review were reread and matched their recorded SHA256 values during this review.

## What is known, and what is still attribution

The prior [resumable-executor proposal](/Users/dev/Desktop/Resq/evidence/runtime-forecast-20261017/milestone/resumable-executor-proposal.md) already required shared private transactions, yielded owner work, canonical restoration/publication, whole-call measurements and immutable first/warm fixtures. The final [root source review](/Users/dev/Desktop/Resq/evidence/runtime-forecast-refactor-20261018/repairs/03-final-bounded-work/root-review.md) records yielded construction/restoration, indexes, immutable JSON chunks, yielded projection and scratch reuse. Repeating “make it resumable” or “reduce allocations” would repeat that approach without identifying the remaining blocker.

Concrete source facts remain:

- [ForecastService.cs:115](/Users/dev/Desktop/Resq/game/Assets/Resq/Runtime/Simulation/Forecast/ForecastService.cs:115) executes up to64 yielded units per call and checks its200µs scheduling target between units. It cannot preempt one unit, and guards, completion/disposal and diagnostic publication also belong to the whole call. `Begin` at63–81 and the first allocation probe at169–175 remain measured public work.
- [ResumableWork.cs:36](/Users/dev/Desktop/Resq/game/Assets/Resq/Content/ResumableWork.cs:36) assigns `Stage` after iterator work returns. [RuntimeForecastTests.cs:35](/Users/dev/Desktop/Resq/game/Assets/Resq/Editor/RuntimeForecast/Tests/RuntimeForecastTests.cs:35) samples it after `AdvanceFrame`+`Poll`. A last-yield label is not an exclusive stack or phase duration. Standard replay's44,515µs outer maximum closely accompanies its44,509µs longest work-unit measurement, but both retain a post-yield label; they do not identify an exclusive source cost.
- Contiguous allocation remains atomic at [ResumableWork.cs:97](/Users/dev/Desktop/Resq/game/Assets/Resq/Content/ResumableWork.cs:97),196 and217;64-item JSON blocks still grow their block-reference List at160. Path prefix arrays at [TransportSystem.cs:459](/Users/dev/Desktop/Resq/game/Assets/Resq/Runtime/Simulation/Rescue/TransportSystem.cs:459), row mapping at1384–1386, private fork copies at [OperationClock.cs:664](/Users/dev/Desktop/Resq/game/Assets/Resq/Runtime/Simulation/OperationClock.cs:664) and full canonical snapshots at728 remain inspection targets. Their existence does not prove which caused an observed spike.

Gen0 overlap cannot explain every overrun: capacity rows retain133/89/118/121 overruns without observed Gen0 delta. Retained zero-delta samples include18,284µs (`transport.path.compare-cost`, capacity Relaxed first) and17,999µs (`transport.validate`, Relaxed warm). This does not exclude other runtime pauses or scheduling. Allocation counters remain unavailable/null. Heap endpoints are process-wide sampled observations. Most overrun lists truncate at128, so their label frequencies are not complete distributions. No profiler stacks, collection durations, JIT trace or scheduler attribution establish exclusive causes. First/warm differences establish neither JIT causation nor impossibility.

## Proposed bounded change of approach

Prepare a **single attribution experiment**, with no algorithm change, before selecting a repair. The coordinator must first review a concrete separately scoped diagnostic packet and resolve its authorization/accounting outside the closed round. This document supplies no runnable command or bypass.

The packet should bind an immutable diagnostic copy of repair03, its fixtures and exact native argv, plus supported installed Unity capture capabilities. No profiler package/install, foreign bridge, global setting, alternate runtime or dependency-cache copy is proposed. If useful capture requires a new installation, unsupported operation or budget increase, stop and report that prerequisite.

Limit proposed instrumentation to diagnostic copies of `ForecastService.cs`, `ResumableWork.cs`, `RuntimeForecastTests.cs` and explicit allocation/row-map marker sites in `ResumableOperationSystems.cs` / `TransportSystem.cs`. Capture public-call IDs, entered/completed spans, actual work-unit counts, unit durations, guard/disposal/publication time and allocation-site sizes/callers. Use stack/call-tree and GC pause evidence only if the supported installed tooling actually supplies it; leave unavailable fields unknown. Include harness allocations between calls, which can affect later collections. Post-yield labels alone are insufficient. Instrumentation must be bounded, counted and retained inside its applicable measured boundary; diagnostic output serialization may follow measurement but cannot relocate existing simulation/required publication work. Instrumented timing is diagnostic, never a passing performance result.

One fresh process runs the existing fixture sequence once, preserving capacity Standard first as the first forecast-service use, every first/warm fixture, complete horizons, cancellation/authority checks and canonical/replay comparisons. No preliminary warm-up, smaller fixture, forced collection or GC exclusion is allowed. Do not substitute a one-unit scheduler for the candidate during this experiment: that changes the mechanism being attributed.

The coordinator alone owns execution, shared manifests and task accounting; one source owner prepares the disjoint diagnostic area. One heavy operation globally; preserve Waz/Claude and Editor/automation locks. Fresh admission must cover the exact capture output: existing C08 minimum2,550,136,832B,2GiB hard reserve,256MiB enforced growth cap and128MiB cancellation margin are ceilings to retain, not evidence that an unmeasured profiler trace fits. Seal a smaller bounded capture/output policy within them before launch; if infeasible, stop. Keep the1,800-second native timeout and1,200 completed-operation ceiling. No automatic retry after compiler, capture, timeout, resource or test failure; retain partial evidence and stop.

## Review gate and eventual acceptance

Diagnostic completion means a sealed terminal record and enough correlated spans/stacks to distinguish long atomic work, cumulative units, callback/publication cost and observed runtime pauses, with instrumentation limits disclosed. If attribution is inconclusive, the result is **inconclusive**, with no inferred code remedy. If conclusive, prepare exactly one new narrowly scoped candidate proposal naming the demonstrated cost, files, ownership and validation budget. Code preparation/execution still needs its own explicit reviewed scope; this experiment grants none.

A future product candidate must retain the1,000µs whole-public-call criterion, all first/warm witnesses, fixture dimensions, full horizons,1,200-operation ceiling,1,800-second timeout, source isolation, cancellation, deterministic rules and canonical/actual ReplayRunner equality. Passing native C08 forecast **and ordering**, emitted registered case evidence, and fresh affected C01–C07 regressions on exact candidate bytes are required before C08 acceptance. Preserve every historical result/head and append new accounting truthfully. Host synthetic attribution or passing inner spans cannot establish Android, physical Quest3/3S, human, authored-campaign or release acceptance.

Review work performed here: read retained sources/evidence and verify referenced file identities; write only this planning document. No C# source, contracts, tests, wrappers, task state or historic evidence were edited. No compile/profile/test/native process ran.
