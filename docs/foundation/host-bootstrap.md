# F03 host foundation — real partial results

The canonical build plan is BUILD_PLAN.md; plan.md preserves the earlier draft with a superseded notice. Full five-world/25-mission scope is unchanged.

## Prepared project

The installed 6000.3.24f1 Editor's URP template 17.0.14 supplied 58 source/configuration files totaling 214,024 bytes. `tools/prepare_host_project.py` copies only regular Assets/Packages/ProjectSettings sources, refuses mismatched existing files and verifies all 41 baseline hashes before/after. It excludes Library and the template's old packages-lock. No remote template, installation, import or package resolution occurred. Template dependencies are declarations, not chosen XR pins. The template manifest remains byte-for-byte unchanged.

Actual source record: `evidence/foundation/source-bootstrap-20261016/template-source.json`. Core and test asmdefs, ProjectVersion and manifest now exist. Unity will generate genuine .meta records on its eventual import; none were fabricated for authored files. Template .meta files were preserved with their exact template assets.

## Executable pure C# proof

`Resq.Core.Tick` implements only the frozen tick scalar domain 0..1,000,000, using signed 64-bit storage and immutable values. It does not implement a second clock or simulation; C02 owns clock advancement. Core asmdef has noEngineReferences=true. Shared checks exercise bounds, equality/hash, ordering and copy isolation.

The actual installed Unity Roslyn compiler built Core and the harness. Installed Unity Mono ran seven checks successfully; compiled Core has zero UnityEngine/UnityEditor references. No separate .NET dependency was installed. Actual argv/processes/stdout/stderr/source/tool/output hashes:

`evidence/foundation/source-bootstrap-20261016/csharp-attempt-01/verification.json`

Reproduce into a fresh evidence directory:

```text
python3 -B tools/verify_host_core.py --output evidence/foundation/<new-run>/host-core
```

This is real standalone C# execution, not licensed Editor execution or a game test result.

## Editor-facing implementation and actual type-check

The actual host-bootstrap NUnit category has four tests with exact frozen case_id properties: actual batch Editor compilation/version, seven isolated pure checks, Core/Simulation engine-reference exclusion and original baseline/owned project checks. The native callback writes a case packet only from the exact four successful native XML leaves; no cases are synthesized from aggregate counts. resq now maps --suite to a category filter and passes the owner's PID for project-lock verification.

Initially five assemblies type-checked with the installed compiler/reference modules and bundled template TestRunner binaries: Core, shared checks, Editor automation, EditMode tests and calibration Editor helper. Record:

`evidence/foundation/source-bootstrap-20261016/editor-typecheck-attempt-05/verification.json`

Reproduce into a fresh destination:

```text
python3 -B tools/verify_editor_sources.py --output evidence/foundation/<new-run>/editor-types
```

Initial compiler-profile failures (netstandard/legacy reference facades), actual BuildSummary int-vs-uint errors and ambiguous NUnit Property errors are retained in attempts 01–04. The successful type-check reports ordinary reflection-populated JSON field warnings. These records prove C# compilation against installed references; they do not prove Unity assembly import, project package resolution or Editor runtime behavior. Bundled template TestRunner assemblies are explicitly identified; they are not an accepted package lock.

## Evidence machinery corrections for the real project

Source snapshots now include nested tools, authored Unity assemblies, .meta, package declarations, settings and binary imported assets. Library and Python caches are excluded; linked/escaping inputs fail. Snapshot writes retain raw bytes. New isolated regressions verify the actual binary round trip and refuse linked inputs. Existing accepted snapshots/history remain immutable; fresh F02 evidence rechecks the evolved implementation.

## Narrow resource decision and remaining checks

User asked to continue F01–F08 despite storage. `resource-decision.json` in the source-bootstrap directory bounds source copying to 1 MiB and compiler/calibration probe outputs to 2 MiB, while preserving a 5 GiB emergency margin. It permits no heavy Unity import/Android build/capture, package/module install, external write, device use or unrelated cleanup. The working_storage gate is not passed by this record.

F03 remains unaccepted until the licensed Editor imports the actual project, resolves an authorized host package set and executes the four native checks under the real runner. F04 still needs matched Android modules and a proven package/build graph; F05 needs both real authorized Quest models. F06 has real Blender files and round-trip proof, but Unity import measurements/render inspection remain pending. Continue bounded source/native preparation independently; do not turn these partial records into task acceptance.

## Final foundation source check

Nine assemblies now pass the actual installed-reference type check in `evidence/foundation/source-bootstrap-20261016/editor-typecheck-complete-foundation/verification.json`: the initial five plus pure Probe, its Unity logging bridge, shared protocol checks and NUnit adapter. The calibration Editor assembly includes both native measurement and contact-sheet renderer source. Actual compiled recorder execution passed fourteen checks with synthetic inputs; see `docs/foundation/probe-instrumentation.md`. None of these results is licensed Editor execution, package resolution or F03–F06 acceptance.

The latest accepted F02 result is `evidence/results/F02/device-binding-20261016/attempt-01.json`, with 24 runner-core +24 execution-control +80 evidence tests. Replacing a prerequisite result invalidates passed dependent heads atomically while preserving history; F07/F08 were refreshed against the new head. Failed rechecks retain prior accepted projections and consume the failed-attempt budget. Frozen test fixtures reset only copied gate heads, under controlled amendment2; actual live gates and passing criteria remain intact.
