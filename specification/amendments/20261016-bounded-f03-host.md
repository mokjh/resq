# Controlled amendment 4 — bounded F03 host work

Authority: direct user instruction16October2026 to reassess the40GiB F03 reserve,
use a safely bounded workflow if possible and continue Unity with all25missions.
This amends an operating reserve, not any game, native-test or release criterion.

F03 still executes in the actual `game` project with the same four native
assertions, seven pure-state checks, original41file hashes and live owned lock.
An isolated bootstrap project is package-configuration preparation only; it
cannot qualify F03. Unity created that empty project and its PackageManager
Client.AddAndRemove produced the lean manifest. Its bytes were copied intact
into game; dependency JSON was not hand edited.

The full template package/settings configuration and deferred template assets,
including their original metadata, are preserved under
`evidence/foundation/f03-bounded-20261016/full-template-before-host`.
`host-profile-migration.json` lists exact original/preserved asset paths.
Restore/configure those render inputs and the full required URP/XR package set
through supported Editor operations for F04. No authored RESQ source, original
art, mission contract or acceptance case is removed.

F03 alone uses `host_working_storage`, with exact TestFramework1.6.0/NUnit2.0.5
and built-in1.0.0modules; <=8MiBAssets;2GiBhardfree reserve;64MiBmaximum positive
file growth and64MiBcancellation buffer. Admission requires2.125GiBfree on each
output/temp volume. Native creation measured1.95MBgrowth, package setup9.53MB,
and default-extra removal4.20MB. Installed test payloads total approximately
1.5MiB. The64MiBcap is a conservative enforced envelope, not a full Android
or rendering estimate. Runtime checks stop only the owned process if either
growth or free-space limit is reached; missing/cancelled cases never pass.

The generic `working_storage` gate,40GiBother-Unity admission and5GiBother-run
emergency guard remain unchanged. No Android modules/build, rendered-art,
device/human or release acceptance is admitted by this profile. The full229
task/231check/942assertion/35,784case/25mission scope and final physical schedule
remain unchanged. F03 receives its scoped resource gate and an explicit heavy-job lock; original/native criteria
and check/case catalogue bytes are preserved.

The prior seal is archived at
`specification/archive/freeze_manifest-1.1.0-amendment-3.json`; exact prior inputs
are retained in the bounded-host evidence amendment-inputs folder. The frozen
resource profile is explicit. No broad-glob reseal or state/history reset.

The first main-project host pass uses a stricter64MiBgrowthcap based on the measured native bootstrap (<10MB per pass) and actual110,679BRESQAssets, with a separate64MiB cancellation buffer. This is a hard attempt budget, not a claim that all future imports fit. Exceeding it blocks without accepting F03. The2GiBhardreserve never decreases.
