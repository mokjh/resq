# Controlled amendment 5 — scoped F04/F06 and C01 work

Authority: direct user16October2026 to reassess F04/F06 like F03, reuse installed
Android tools, continue C01 when safe, preserve25missions and other projects.

The40GiB floor is an operating reserve, not a measured minimum. Existing native
AndroidPlayer, SDK with NDKr27c and JDK17 are reused without reinstalling them or
changing their bytes. Exact tool versions still require actual build validation.
AndroidExternalToolsSettings setters affect global preferences; do not persist
those changes. Prefer process-scoped environment with actual native readback.

Explicit stages in automation/local_operation_profiles.json have a2GiB hard
reserve, enforced positive file growth cap and cancellation buffer. Admission
is reserve+cap+buffer on output/temp volumes. Each stage stops only its owned
process group if free space or growth crosses its bound. Private UPM/temp and
Gradle outputs stay under RESQ. A short owned TMPDIR is necessary for Bee Unix
socket path limits. Existing caches/source are retained, never charged as new
installation; new copies and import/build intermediates are charged.

Runtime-content admission2.25GiB; calibration measure and Android tool readback2.125GiB; render2.375GiB;
render package preparation3.75GiB; XR package preparation/initial Android probe
attempt4.25GiB. These are bounded attempt budgets, **not proven full peaks**.
Known local archive expansion1,010,485,442B and existing built-in444,816,396B
sources inform staged setup; Meta expansion/import/IL2CPP/Gradle remain unknown.
An exceeded budget is a blocker with preserved evidence, never a passed check.

F04/F06 alone replace generic working_storage readiness with
local_working_storage. The gate observes smallest-stage readiness; each actual
operation separately enforces its larger profile. C01 retains its original
dependency graph and receives native runtime-content operation admission.
Other tasks and the original general reserves remain unchanged. F03's profile,
four native checks and accepted evidence are unchanged.

Preparation occurs in the retained RESQ bootstrap project when appropriate;
F04's final APK must still build the actual game project. Package API resolution,
real rig/provider/features, four native F04 assertions, exact APK/candidate
binding, native F06 units/axes/pivot/render/inspection/preservation, C01's three
native assertions and all25mission authoring/solvability checks remain required.
Physical/device/human tests stay at the final stage. No asset repair reset.

Archive prior seal at specification/archive/freeze_manifest-1.1.0-amendment-4.json.
Prior exact changed inputs are retained in
evidence/resource-reassessment-20261016/amendment-inputs. Reseal only explicit
reviewed changed/new files; preserve task history, original41packfiles and scope.
