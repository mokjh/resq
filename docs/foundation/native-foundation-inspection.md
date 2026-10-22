# Native foundation inventory

`FoundationInspection.Inspect` is an explicit Editor-only entry point. It reads
the real registered package inventory and native Android module support through
[PackageInfo.GetAllRegisteredPackages](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PackageManager.PackageInfo.GetAllRegisteredPackages.html)
and [BuildPipeline.IsBuildTargetSupported](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/BuildPipeline.IsBuildTargetSupported.html).
No startup hook, package install, settings write, rig creation or build is added.

It requires the exact `RESQ/game` batch project and a fresh output under
`evidence/foundation/project-inspection/`. The admitted coordinator must hold
the existing heavy-job and unity-project locks and capture process/log evidence.
Normal operation storage admission still applies. Example execute-method args:

```text
-executeMethod Resq.Editor.Automation.FoundationInspection.Inspect
-resqFoundationInspection /Users/dev/Desktop/Resq/evidence/foundation/project-inspection/native-attempt-01/inventory.json
```

The entry exits synchronously; the coordinator supplies the exact Editor,
projectPath, batch/log arguments and a new evidence destination. It records
Editor version, active build target, actual native module-support boolean,
project/version/manifest/resolution hashes and each loaded package's source,
version, manifest and actual payload file hashes. It rejects symlink inputs and
destinations, missing resolution, empty/duplicate packages, file changes while
hashing, changed inventories and changed project resolution. These are observed
file identities; they are not a transactional snapshot or package compatibility
qualification. No archive provenance, SDK/NDK/JDK execution, XR rig/provider
inspection or F04 assertion packet is inferred from them.

Actual installed-reference C# compilation passed under
`evidence/foundation/project-inspection/source-20261016/repair-01/verification.json`.
The preceding compiler error and its namespace qualification repair are retained.
No native inventory report exists yet. The new source awaits Unity-generated
metadata; no GUID was invented. F03/F04 acceptance still requires actual owned
Editor execution and the complete frozen checks.
