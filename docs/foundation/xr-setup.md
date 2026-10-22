# Android/XR preparation — F04/F05 remain pending

This is an implementation aid for the full five-world, 25-mission plan. It is
neither an accepted Android build nor a physical probe. The immutable task graph,
canonical cases and root evidence verifier remain authoritative. This preparation
does not waive F03, storage, setup authorization, device trust or capture consent.

## Actual discovery

The final timestamped inventory is
`evidence/toolchain/preparation/20261015-221659Z/environment-audit-final.json`.
The initial inventory is retained alongside it. Both hash the auditor and
inspected metadata; the exact corresponding auditor source bytes are also retained.
At those observations:

- Editor `6000.3.24f1` exists at
  `/Applications/Unity/Unity-6000.3.24f1/Unity.app`.
- Its **app Contents** `PlaybackEngines` contains `MacStandaloneSupport`;
  `AndroidPlayer` is absent at that searched path. The later, wider inspection
  below found a sibling AndroidPlayer directory. The initial auditor's flag is
  path-scoped and must not be treated as proof of global Android-module absence.
- Actual Editor metadata contains URP `17.3.0`, OpenXR `1.16.1`, Unity OpenXR:
  Meta `2.3.2`, XR Hands `1.7.3`, XR Management `4.6.1` and Input System `1.20.0`.
  These are available metadata, **not chosen or compatible pins**. No inspected
  Editor package metadata supplies Meta XR Core/Interaction/MRUK.
- The three explicitly searched cache roots are recorded individually. Their
  absence does not establish absence in every directory or another project.
- RESQ ProjectVersion and package manifest now exist, while `packages-lock.json`
  and `automation/toolchain.lock.json` were absent. Parallel host authoring may
  subsequently change those files; this inventory retains its original hashes.
- Repository and system temporary volume each had approximately 6.00 GiB free
  initially and 5.90 GiB in the final observation.
  The required Unity reserve is `max(40 GiB, estimated_peak + 10 GiB)`. This
  observation is not a gate update or a storage reservation.

Rerun safely using a **new** report destination:

```text
python3 -B tools/foundation_probe.py audit --report evidence/toolchain/preparation/<new-UTC-run>/environment-audit.json
```

The auditor reads bounded local package JSON metadata, including metadata members
inside bundled archives without extracting them. It does not resolve/download
packages, launch an Editor, read licensing secrets, contact devices, connect to
bridges or change task state. Existing report destinations are never replaced.
An audit failure is nonzero; missing modules remain explicit observations.

## Primary-source review and candidate choice

The exact metavr argv, UTC retrieval interval, exit code, stdout/stderr paths and
hashes are retained in
`evidence/toolchain/preparation/20261015-221659Z/primary-source-capture.json`.
The current Unity build path was verified with `metavr docs search` before using
Quest-specific guidance. Search returned `/vr/` aliases which fetch rejected;
the canonical `/horizon/documentation/` addresses succeeded. No MCP installation
or connected-device query was used.

- [Meta Unity setup](https://developers.meta.com/horizon/documentation/unity/unity-project-setup/)
  (page updated 2026-10-07) specifies Android Build Support, OpenJDK and Android
  SDK & NDK Tools, and recommends the Unity OpenXR provider.
- [Meta Unity/OpenXR compatibility](https://developers.meta.com/horizon/documentation/unity/unity-and-openxr-compatibility/)
  (updated 2026-10-05) distinguishes a Unity XR Origin/Essentials route from
  features using Core's origin/manager, including MRUK and Core passthrough.
- [Interaction SDK requirements](https://developers.meta.com/horizon/documentation/unity/unity-isdk-packages-and-requirements/)
  (updated 2026-10-07) distinguishes the Unity XR Essentials and Core integration
  packages and lists both target headset models. Its legacy dependency wording
  still requires reconciliation against the actual candidate package metadata.
- [MRUK getting started](https://developers.meta.com/horizon/documentation/unity/unity-mr-utility-kit-gs/)
  (updated 2025-06-11) identifies `com.meta.xr.mrutilitykit` and a Core rig route.
  Its `latest`/example version and required-scene tutorial are source examples;
  they are not RESQ pins or permission-denial behavior. RESQ must retain a usable
  virtual fallback when scene permission is denied.

The proposed route to investigate is one Unity OpenXR loader and a Core-owned
origin for Core/MRUK features, with one authoritative interaction data path.
This is a proposal, not a serialized rig or tested package combination. Before
installation, compare exact Core/Interaction/MRUK package.json dependencies,
registry provenance, Unity compatibility, release notes and license terms with
the actual Editor and current Unity package documentation. Reconcile discrepancies
explicitly. Never copy documentation's `latest`, choose a cached package merely
because it is available, add both origin types, or stack competing input providers.

## Bounded setup request

The current machine-readable draft is
`evidence/toolchain/preparation/20261015-222738Z-registry-resolution/setup-request-final.json`.
The initial unpriced/version-unresolved draft at
`evidence/toolchain/preparation/20261015-221659Z/setup-request.json` remains unchanged
as historical preparation. Neither has authorization effect or requests approval
through this document.

The prospective setup is limited to supported Android Build Support/OpenJDK/SDK
and NDK provisioning for the installed 6000.3.24f1 Editor, plus the exact package
candidate below. The original app-Contents destination assumption is superseded:
Unity's release API publishes `{UNITY_PATH}/PlaybackEngines/AndroidPlayer` and its
subpaths. The installer must resolve that placeholder and disclose download/temp
and registration-cache locations before action; do not force a manual path or
copy/symlink the existing sibling module. Record actual installed SDK/NDK/JDK
metadata after supported setup. Maximum cash spend remains zero.

New package installation is a separately bounded candidate step: exact package
IDs, versions, dependency graph, provenance, published integrity and compressed
sizes are now recorded below; license authorization, staging destinations and
credible unpacked/import peak estimates remain unresolved.
Only RESQ's `game/Packages`, `game/ProjectSettings`, its own package cache and a
new candidate `automation/toolchain.lock.json` are prospective project targets.
No package pins, lockfile or SDK versions are invented here. No editor replacement,
global configuration change, Waz/Claude bridge redirect, provider resource,
account creation, App ID or paid entitlement is included.

Module/package work requires the explicit `unity_setup_authorization` gate and a
fresh successful `working_storage` gate with a credible peak estimate. F03 must
pass before the registered F04 check. Discover existing bridge capabilities
before any proposal to add one; a bridge belonging to another project must be
left alone. RESQ must have its own verified binding before its harmless console
and hierarchy reads can be recorded. The current audit makes no bridge claim.

## Published candidate and actual partial Android files

The immutable follow-up directory is
`evidence/toolchain/preparation/20261015-222738Z-registry-resolution/`.
It retains official registry responses, supported CLI help/release discovery,
HEAD responses, selected package metadata, source captures and a concrete setup
request. No archive body, package import or installer was executed.

The [official Meta registry](https://npm.developer.oculus.com) exposes this
internally matching family. The captured source is the full registry JSON for
each package; integrity strings are retained exactly, without claiming their
archive bytes were downloaded or independently hashed:

| Package candidate | Version | Published archive bytes |
|---|---:|---:|
| com.meta.xr.sdk.core | 207.0.1 | 199,576,002 |
| com.meta.xr.sdk.interaction | 207.0.1 | 111,629,611 |
| com.meta.xr.sdk.interaction.ovr | 207.0.1 | 13,911,501 |
| com.meta.xr.mrutilitykit | 207.0.1 | 9,805,998 |

Core integration explicitly depends on Core and Essentials 207.0.1; MRUK also
depends on Core 207.0.1. Total Meta compressed payload is **334,923,112 bytes**.
All four publish SHA-512 SRI and SHA-1 values. Exact URLs, values, metadata hashes
and successful HEAD sizes are in the request and `archive-head-capture.json`.
These metadata versions are candidates, not pins in the project or a qualified lock.

The candidate uses actual installed Editor minima/builtins/bundles: URP 17.3.0,
OpenXR 1.16.1, XR Hands 1.7.3, Input System 1.20.0, XR Management 4.6.1, Core Utils
2.6.0, AI Navigation 2.0.14 and Newtonsoft JSON 3.2.2. The full closure has **42
packages and 76 satisfied minimum-version edges**, with no missing metadata node.
`candidate-dependency-set.json` retains every edge; `package-metadata/` retains
the corresponding actual manifests. All 14 Unity bundled archive candidates are
also present at the official Unity registry, with published SHA-1, exact URLs and
HEAD sizes matching the existing local archives' sizes.

The old Meta TextMeshPro 3.0.6/uGUI 1.0.0 requests are not silently discarded.
The installed Editor policy requires TextMeshPro 5.0.0 and uGUI 2.0.0; its actual
TextMeshPro package is a **shim** whose dependency is uGUI 2.0.0. Those manifests
are captured. Unity documents [dependency version overrides and resolution](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-dependencies.html).
This supplies a metadata-consistent proposal; only the real supported UPM result,
lock and compilation can establish that the proposed combination works. No vendor
package manifest is patched to force agreement.

`unity editors info 6000.3.24f1 --architecture arm64 --format json` identified
release changeset **4e7b9b5b6244**. The [official Unity Release API](https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.3.24f1&limit=1)
provided all 12 Android module/container/child records, exact URLs, destinations,
license references, published sizes and available integrity. Summed published
payload is **3,682,430,207 download bytes** and **10,457,003,689 installed bytes**;
this is the full payload envelope before reuse or overlap is evaluated. The
166-byte SDK container is not the SDK's actual total.

| Published child | Exact version |
|---|---|
| OpenJDK | 17.0.18+8 |
| NDK | r27c |
| CMake | 3.22.1 |
| SDK build/platform tools | 36.0.0 |
| SDK platforms | 34, 35, 36, 37 |
| SDK command-line tools | 16.0 |

Every module HEAD Content-Length matches the corresponding API size. The API
publishes an Android-support MD5 integrity and command-line-tools SHA-384; missing
digests remain empty. The captured Google repository supplies five additional
exact-filename checksums, retained without inventing an algorithm absent from
that XML attribute. ETags and hash-looking URL fragments are not substituted for
published cryptographic digests. The API calls the NDK payload ZIP although its
URL and HEAD content type are DMG; preserve this discrepancy for the supported
installer rather than guessing an unpack command.

There are real pre-existing Android platform files at
`/Applications/Unity/Unity-6000.3.24f1/PlaybackEngines/AndroidPlayer`, beside the app.
The IL2CPP release libunity/libmain files have ELF AArch64, x86-64 and ARMv7 headers
and embed **6000.3.24f1_4e7b9b5b6244**, matching the release revision. Their exact
hashes are in `sibling-android-binary-identities.json`; the managed extension DLL
hash is retained separately. **SDK, NDK and OpenJDK directories are absent at that
sibling root.** The support binaries' presence/version does not establish Editor
recognition or a complete Android toolchain.

The supported CLI module list returned an empty success array. Its no-EULA
`--dry-run` exited **6**: no modules found; module installation is supported only
for Hub-installed editors. The installed CLI also exposes the supported
`unity editors module refresh` command for a located editor. Its help was captured;
**refresh was not run** because it mutates registration metadata. A separately
authorized setup step must disclose that write destination, refresh through the
supported command, then recheck the explicit-architecture module list and dry run.
If still unsupported, retain the exact blocker. No automatic reinstall, metadata
injection, relocation or global configuration change is included.

## Host-template availability and loader evidence limits

`host-package-availability.json` snapshots the current template manifest and
distinguishes exact requested versions from locally available Editor candidates.
Seven direct dependencies are absent from BuiltInPackages but newer matching
Editor-policy archives do exist locally: Navigation 2.0.14, Collab Proxy 2.13.6,
Rider 3.0.40, Visual Studio 2.0.26, Input System 1.20.0, Timeline 1.8.13 and Visual
Scripting 1.9.12. None of the template's older exact archive versions is present.
URP, Test Framework and uGUI are built in at 17.3.0, 1.6.0 and 2.0.0 respectively.

The locally available Editor-minimum host candidate has 32 packages and 13 bundled
archives: approximately **0.457 GiB compressed and 0.876 GiB of regular-file tar
payload**, before imported assets, compiled intermediates, caches and temporary
copies. Thus this inventory cannot support a **512 MiB peak** claim for importing
the complete current template. It does not guarantee the unchanged manifest will
resolve offline or that the Editor will avoid external package-body requests.
A separately chosen minimal host package set needs its own bounded estimate and
actual supported import evidence; this preparation changes no project dependency.

Read-only Unity reference-source/API findings and hashes are retained in
`exact-version-module-source-capture.json` and
`exact-version-module-manager-capture.json`. The official **6000.3.24f1** reference
tag resolves to public Git revision **343494d1a517cc6c4c0439769ad0818c8b18ae1c**.
Its C# ModuleManager registers
loaded `IPlatformSupportModule` implementations and delegates native module
loading to Editor APIs. Its BuildPipeline bindings likewise delegate supported
target checks and playback-directory resolution to native code. Exact-version
reference source is not a runtime observation of local module discovery. The supported
[BuildPipeline.GetPlaybackEngineDirectory](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/BuildPipeline.GetPlaybackEngineDirectory.html)
API provides the actual resolved path when the owned Editor can safely run;
`BuildPipeline.IsBuildTargetSupported` supplies a separate supported-target check.
The public playback-path overload with `assertUnsupportedPlatforms: false` can
record an unsupported target without asserting. Neither API was called here.
Native discovery's exact local search-path result remains
unproven; do not infer recognition from directory placement, a CLI registration
list or reference-source reading.

## F04 build evidence to produce after admission

Use the registered command, substituting a new valid run/attempt destination:

```text
python3 -B tools/resq.py game build --profile xr-probe --report evidence/checks/f04/<run_id>/<attempt_id>.json
```

The real probe scene must exercise the events below and emit a runtime identity
header and event stream. Preserve actual Editor logs/process exit, successful
native BuildReport, ARM64 IL2CPP APK, ProjectVersion, package manifest/resolution
lock, supported Android tool metadata and exact rig/provider inspection. Retain
an immutable tested source/content snapshot including required untracked files.
Record one candidate lock hash before physical tests. A later acceptance record
binds it; acceptance must not rewrite the candidate lock bytes.

Map actual outputs to `check-f04-a01` (native build/APK), `a02` (exact toolchain
hashes), `a03` (one origin/provider), `a04` (no duplicate/redirected bridge).
Provide every frozen case and native proof artifact through the actual registered
adapter. The inventory and source reading above execute none of these cases.

## F05 actual observations and supplemental validator

No device commands are supplied for an unknown Android device. At the later
authorized setup step, use supported discovery, verify model and owner/trust,
then select each explicit physical headset. Discover the installed metavr command
syntax before install/run/capture. Only user-authorized Quest 3 and Quest 3S
sessions may fill the two slots. Editor, simulator and Link execution cannot fill
either physical standalone-APK slot.

For each model retain a private device identity record, pseudonymous device and
session aliases, OS build, installed package/version/APK identity, authorization,
capture consent, actual runtime logs and reviewable captures. Avoid raw serials,
room scans, signing material or other private identifiers in public derivatives.
Human observations must be supplied by the real observer; the agent can transcribe
them but cannot originate them. This probe does not establish comfort, performance,
mission playability or public gameplay footage qualification.

The supplemental record contract lives in
`evidence/toolchain/preparation/20261015-221659Z/probe-record-contract.json`.
It intentionally contains **no populated sessions or observations**. Future actual
records have this structure, defined by `tools/foundation_probe.py`:

1. Top level: schema_version `1`, record_type `resq-xr-probe-observations`,
   evidence_kind `physical_device`, exact freeze/rules IDs, identity,
   candidate_sha256, f04_result artifact, and exactly two sessions.
2. Identity contains hash-bound source_manifest, content_manifest, toolchain_lock,
   project_version, package_manifest, package_lock, apk and build_report artifacts;
   also nonempty build_id (native build GUID), package_id and version_code strings.
   `candidate_sha256` is SHA-256 of Python's UTF-8
   `json.dumps(identity, sort_keys=True, separators=(",", ":"))`. All sessions
   and runtime logs bind that same digest. Nested source/content manifest files
   use the root `byte_length` format and must still exist with their recorded hashes.
3. Every supplemental artifact is a relative repository path, exact `sha256` and
   positive integer `size_bytes`. Existing root task records continue using their
   own `byte_length` schema. Symlink/path escapes, missing files and changed bytes
   fail. The APK must contain an ARM64 ELF IL2CPP payload and native BuildReport
   identity must match. The F04 result must report passed and bind the exact
   source/content/toolchain/APK identities; authoritative root verification of that
   prerequisite remains separately required.
4. Each session: model `quest3` or `quest3s`, evidence_kind `physical_device`, runtime
   `standalone_apk`, candidate_sha256, distinct session_id/device_alias, os_build,
   observer_alias, real UTC started_utc/ended_utc and artifacts named
   device_authorization, consent, capture, runtime_log and device_identity.
   The identity JSON carries model `Quest 3`/`Quest 3S`, os_build, device_alias,
   evidence_kind, installed_package_id, installed_version_code and installed_apk_sha256.
5. The runtime_log JSON contains candidate_sha256, session_id, device_alias and
   events. Every event records event_id, result `passed`, distinct nonnegative
   integer sequence, monotonic_ms and UTC observed_utc. Additional verbose raw
   logging can be a separate artifact; the normalized event packet has each
   required event exactly once. Do not manufacture events from intended actions.
6. Session observations contains each event_id exactly once with result
   `observed_pass`, a specific observer-supplied observed_behavior, observed_utc and
   integer capture_offset_ms. Event/observation times must fall within the session;
   capture offsets must fit its interval. Runtime monotonic time cannot reverse.

| Required event | Actual behavior the observer must record |
|---|---|
| passthrough_visible | Real passthrough appears with the probe content and expected occlusion behavior recorded. |
| hands_near_button | Tracked hand near interaction activates the world-space button once. |
| hands_ray_button | Tracked hand ray selection activates the button once. |
| one_hand_left_button | Left hand alone completes selection; no second hand or controller is needed. |
| one_hand_right_button | Right hand alone completes selection; no second hand or controller is needed. |
| room_permission_denied | Observer actually denies the room/scene permission; denial is retained in runtime evidence. |
| virtual_fallback | A usable virtual board appears after the denial without requiring the permission. |
| placement_confirmed | Observer safely places/confirms the fallback board and can select afterward. |
| pause_resume | Explicit pause stops command progression; resume restores usable interaction. |
| suspend_resume | Actual standalone application suspend/recovery restores usable placement and interaction without unintended command execution. |

The denial event must precede fallback, which precedes placement confirmation.
Revisit actual payload fields as the real instrumentation is implemented; any
contract extension must retain these observations and all frozen criteria.

Run the supplemental structural check against an **actual** future record:

```text
python3 -B tools/foundation_probe.py validate-probe evidence/toolchain/device/<run_id>/probe.json
```

Missing/partial/simulator/mismatched records exit nonzero. A structurally complete
record returns `structural_validation_only`, `accepted_task: false`. It cannot
authenticate a human observation, establish consent scope or accept a task.
Do not copy unit-test fixture data to evidence directories. Tests create temporary,
explicitly synthetic artifacts solely to test the validator.

The actual F05 command remains:

```text
python3 -B tools/resq.py device qualify --scope xr-probe --models quest3,quest3s --report evidence/checks/f05/<run_id>/<attempt_id>.json
```

Map both-model observations and identities to the exact four `check-f05` assertion
cases and their physical proof index. The registered device backend/case emitter
must exist and execute; this preparation does not supply or emulate it. Run
`tools/verify_task_result.py task` on the actual result, independently review the
underlying physical evidence and only then use the coordinator's acceptance path.
Neither the setup draft nor this protocol marks F04/F05 passed.

## Verification of this preparation

`python3 -B tools/test_foundation_probe.py` executed 23 passing isolated Python
tests. These exercise missing/duplicate models, simulator/Link/Editor substitution,
candidate and installed-build mismatch, changed artifacts, absent observations,
runtime order, source snapshot loss, missing consent, path/symlink escapes and
no-overwrite reports. The successful synthetic fixture only tests record parsing;
it never becomes physical evidence. Exact stdout/stderr and exit are retained in
the preparation directory's `validator-tests.json`.

The CLI was also actually invoked against `probe-record-contract.json`. It exited
1 with `physical-device evidence required`, recorded in `protocol-rejection.json`.
This confirms the unexecuted protocol cannot masquerade as a physical probe. No
Editor, headset, emulator, build or provider operation was launched by this work.

## 16 October continuation — approved setup, deferred physical tests

The user explicitly approved the prepared $0 setup request. Current action authorization and passed permission gate are under evidence/authorization/unity-setup-20261016; this supersedes earlier pending-approval descriptions, not their historical evidence. No install occurred. Latest8.47GiBfree remains below the unchanged40GiBUnityfloor; credible peak and supported exact destinations are still required. Current supportedCLI reports active UnityPersonal, not a paid-license requirement.

Controlledamendment3 moves every realMeta test, including F05/vertical/first/look/Questreplay/performance/VRhuman, after all25actualPOLISHheads and completehostCommand/controller/tutorial. F04 remains a real local build/resolvedcandidate with physicalqualificationpending; F05 later tests its exactpreservedAPK/lock. See BUILD_PLAN.md and specification/amendments/20261016-physical-tests-last.md. CorrectedF05adapter suite now31passing synthetic tests; no physical evidence. Actual SDK rig/feature wiring and F04 native case emission/build remain implementation work.
