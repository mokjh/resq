# Probe instrumentation preparation

This is source preparation for F04/F05 in the unchanged five-world, 25-mission
scope. It establishes no Editor import, Android build, Quest behavior or F05
acceptance. The authoritative frozen checks and the supplemental record protocol
remain in `automation/checks.json`, `tools/foundation_probe.py` and
`docs/foundation/xr-setup.md`. No package, SDK, scene or rig is added here.

## Recorder and integration boundary

`game/Assets/Resq/Runtime/Probe/ProbeRecorder.cs` is pure C# in `Resq.Probe`,
with no engine or SDK references. Construction requires an explicit candidate
SHA-256 digest and pseudonymous session/device aliases. It does not discover or
authenticate those identities. The digest must come from the actual future
candidate inventory; never substitute a source-only or guessed digest.

An injected `IProbeClock` supplies UTC and nonnegative monotonic milliseconds.
`SystemProbeClock` uses UTC and a session-owned Stopwatch; neither drives rescue
simulation. Successful callbacks are recorded with contiguous sequence numbers
starting at zero. Duplicate/unknown IDs, decreasing UTC or monotonic time, and
fallback/placement before their prerequisites throw without adding an event or
consuming a sequence number. Equal event timestamps are valid. Close requires a
positive UTC session interval and forbids subsequent events. Snapshot collections
are immutable copies.

The exact allowed IDs are:

- `passthrough_visible`
- `hands_near_button`
- `hands_ray_button`
- `one_hand_left_button`
- `one_hand_right_button`
- `room_permission_denied`
- `virtual_fallback`
- `placement_confirmed`
- `pause_resume`
- `suspend_resume`

Only the denial → fallback → placement subset has a required relative order.
Other callbacks may arrive in any order. `RecordSuccessfulCallback` must be called
only after the real behavior-specific callback has established its software
condition. Permission request initiation is not denial; a resume notification
alone is not restored usable interaction. The future integration must verify
each event's complete meaning in the observation table in `xr-setup.md` before
calling it. There are no timers, lifecycle hooks, button simulations, platform
tests or SDK success assumptions that generate events automatically.

`Runtime/Probe/Unity/ProbeLoggingBridge.cs` is a separate engine-facing assembly.
It is an explicit class, not an automatically running scene component. Construct
it with actual candidate/session aliases, then invoke `RecordSuccessfulCallback`
from verified future callbacks. It writes a full JSON snapshot with the console
prefix `RESQ_UNQUALIFIED_PROBE `. `LogCurrentSnapshot` can retain an interrupted
session; `CloseAndLog` records a completed interval even if required events are
missing. Rejection exceptions propagate to the caller: retain the underlying
failure logs rather than labeling a rejected callback successful.

No real callback wiring is claimed. Once supported SDK versions are installed,
wire actual handlers and inspect them in the owned licensed Editor. Editor,
simulator, phone, Link and standalone sessions all receive the same unqualified
label from this source; the bridge never upgrades any environment to Quest.

## Packet fields and evidence limits

The snapshot retains the supplemental runtime fields exactly:
`candidate_sha256`, `session_id`, `device_alias`, and `events`, with each event
containing only `event_id`, `result`, `sequence`, `monotonic_ms`, `observed_utc`.
An event's `result: passed` means the caller explicitly reported its successful
software callback. It is not an observer-supplied `observed_pass` record.

Additional top-level instrumentation fields are `schema_version: 1`,
`record_type: resq-xr-probe-runtime`,
`evidence_kind: unqualified_runtime_instrumentation`, `accepted_task: false`,
`started_utc`, nullable `ended_utc`, `session_closed`, `protocol_complete`, and
`missing_event_ids`. `protocol_complete` means only that a closed session has
all ten recorded callbacks. An open or incomplete session remains explicitly
incomplete; the recorder never fills missing events. A rejected callback can be
retried after the caller resolves its cause, but no record of that rejected
callback is inserted into the normalized successful-event list. Retain raw
failure logs separately.

Console output is a stream of cumulative snapshots, not one supplemental JSON
file. Preserve the raw log; extract a single actual session's final snapshot
without merging runs or adding events, strip only its known prefix, and retain
that JSON as the runtime_log artifact. Incomplete sessions cannot satisfy the
supplemental validator's required event count. Preserve their failure evidence.

There are no model/OS, physical-device classification, authorization, consent,
capture, human observation, observed behavior or installed-build fields generated
by this source. These must come from independently reviewed actual records.
An alias or candidate argument is an input, not proof of its authenticity. Both
physical headset sessions, actual F04 result and immutable candidate/toolchain
binding remain necessary. Supplemental validation and this logger never accept
F04/F05 or replace the root verifier.

## Shared safety checks and standalone invocation

Fourteen shared checks in `Tests/Probe/Shared/ProbeProtocolChecks.cs` cover the
allowlist, explicit identities, absence of automatic events, duplicates/unknowns,
denial order, reversed clocks and rollback, equal timestamps/sequence, incomplete
close, closed-session rejection, immutable snapshots, exact normalized JSON,
invariant formatting under Arabic culture, and invalid clock values. Separate
`Resq.Probe.Checks` and `Resq.Probe.Tests` assemblies avoid modifying the current
host EditMode assemblies. `Tests/Probe/EditMode/ProbeRecorderTests.cs` invokes
those same checks using NUnit; it has no frozen F04/F05 acceptance case IDs.

The coordinator owns compilation and the one-heavy-operation slot. The following
uses the installed Unity toolchain paths already used by `verify_host_core.py`,
without installing dependencies or launching an Editor. Choose a fresh output
directory after operation admission; retain actual argv, exit codes, logs and
source/output hashes. Paths below assume repository cwd and an existing fresh
`<output>` directory. Replace that placeholder before invoking.

```sh
probe_scripting=/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents/Resources/Scripting
"$probe_scripting/NetCoreRuntime/dotnet" "$probe_scripting/DotNetSdkRoslyn/csc.dll" \
  -nologo -noconfig -nostdlib+ -langversion:latest -target:library \
  -reference:"$probe_scripting/UnityReferenceAssemblies/unity-4.8-api/mscorlib.dll" \
  -reference:"$probe_scripting/UnityReferenceAssemblies/unity-4.8-api/System.dll" \
  -reference:"$probe_scripting/UnityReferenceAssemblies/unity-4.8-api/System.Core.dll" \
  -out:<output>/Resq.Probe.dll game/Assets/Resq/Runtime/Probe/ProbeRecorder.cs
"$probe_scripting/NetCoreRuntime/dotnet" "$probe_scripting/DotNetSdkRoslyn/csc.dll" \
  -nologo -noconfig -nostdlib+ -langversion:latest -target:exe \
  -reference:"$probe_scripting/UnityReferenceAssemblies/unity-4.8-api/mscorlib.dll" \
  -reference:"$probe_scripting/UnityReferenceAssemblies/unity-4.8-api/System.dll" \
  -reference:"$probe_scripting/UnityReferenceAssemblies/unity-4.8-api/System.Core.dll" \
  -reference:<output>/Resq.Probe.dll -out:<output>/ProbeProtocolHarness.exe \
  game/Assets/Resq/Tests/Probe/Shared/ProbeProtocolChecks.cs tools/ProbeProtocolHarness.cs
"$probe_scripting/MonoBleedingEdge/bin/mono" <output>/ProbeProtocolHarness.exe
"$probe_scripting/MonoBleedingEdge/bin/mono" <output>/ProbeProtocolHarness.exe --synthetic-sample-json
```

The default harness exits nonzero on any failed check. Its sample mode produces
an intentionally synthetic session/device alias and unqualified packet for JSON
inspection; it must never be included as physical evidence. The pure harness
does not compile the Unity bridge/NUnit adapter; their actual Editor compilation
and callback integration remain separate work.

Author's light checks: all four assembly definitions parsed as JSON; the C#
allowlist exactly matched the Python AST's ten `EVENTS`; the pure recorder has no
Unity references or physical-evidence claims. No compiler, Editor, build, native
test, device, SDK download, provider or bridge operation was run by this author.

## Coordinator's actual verification

The installed Unity Roslyn compiler built the recorder and harness, and installed Mono executed all fourteen checks successfully. Record: `evidence/foundation/source-bootstrap-20261016/probe-recorder-attempt-01/verification.json`, with actual process records, logs, hashes and individual checks. The synthetic sample remained explicitly unqualified, with accepted_task=false. The final nine-assembly type check also compiled the Unity bridge and NUnit adapter against installed references. No Editor or device ran.

Reproduce the standalone check into a fresh owned evidence directory:

```sh
python3 -B tools/verify_probe_protocol.py --output evidence/foundation/<new-run>/probe-recorder
```

This wrapper holds the one-heavy-operation lock, checks the bounded resource margin and retains actual compiler/harness outcomes. The test inputs remain synthetic; successful protocol execution establishes no physical behavior or task acceptance.
