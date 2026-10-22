# F04 resolved build identity binding

Implementation preparation only. `tools/build_candidate.py` is invoked by the Unity build adapter after a successful owned Editor process/native BuildReport and a complete native F04 case packet. It never builds, installs, chooses packages, emits missing success cases, accepts a task or operates a device. Its temporary unit fixtures are synthetic and cannot qualify F04.

## Actual inputs and retained identity

The adapter requires the exact owned F04 run/attempt directory. It reparses the real build-report.json, checks Android/native GUID, rehashes the exact resq.apk and inspects its ARM64-only IL2CPP ELF payload. A case packet must contain all four frozen native assertion IDs, passing statuses and exact dimensions, with a build_binding containing the actual build GUID, APK hash, native report hash and candidate-lock hash. A report alone cannot substitute for the missing assertions.

The helper retains the pre-build executor identity separately, then snapshots actual post-resolution source, ProjectVersion, manifest, packages-lock and generated metadata. It preserves the identity dictionary alias used by the runner. The identity toolchain_lock points to an exact-byte immutable copy of automation/toolchain.lock.json; toolchain_hash is that actual file digest, replacing the generic Python/time observation. build_sha256 is the native APK digest. Authored content identity remains unchanged and explicitly authored/uncompiled; this minimal probe does not claim compiled mission content.

Before and after copying, it rehashes admitted source bytes and checks the inventory. Only creation/change of packages-lock and newly generated Assets .meta files are allowed during the native operation. Changes to existing code, assets or settings, removed inputs, new unexpected source files, symlink files/directories, changed content or lock/report/APK bytes fail without mutating the caller's identity. Intended native setup writes must be completed and captured before build admission; unexpected normalization is a precise blocker to investigate, never a silent exception. No previous snapshot is overwritten.

## Local candidate lock format

This is the implementation format for the future native F04 inspector, not proof that it has run:

- schema_version:1 and status:resolved_local_candidate. Observed-only and physically-qualified labels cannot pass this binding step.
- inputs: exactly game/ProjectSettings/ProjectVersion.txt, game/Packages/manifest.json and game/Packages/packages-lock.json, each pointing to an immutable copy under the owned F04 run/attempt resolved-inputs/<original-path>, with sha256 and byte_length matching the actual current project file at binding time. The lock must never reference mutable project files or caches as preserved inputs.
- editor: version matching ProjectVersion and the native BuildReport unity_version; executable metadata contains its canonical absolute path, sha256 and positive byte_length and must match the exact Editor executable captured by the adapter before launch.
- packages: exactly every dependency in the actual packages-lock.json. Each entry records version and a retained package manifest artifact (workspace-relative path/hash/size); manifest name/version must match the resolution. Package metadata must already be preserved as <owned-run>/package-metadata/<package-name>.json before lock creation; a Library/PackageCache or other mutable path is rejected. Every direct manifest dependency must appear in that resolution.
- android_tools: exactly sdk, ndk and jdk. Each records a nonempty observed version and canonical executable path/hash/positive size. The binder rehashes those installed bytes; it does not copy or execute external installations.

The native inspector must discover supported paths and obtain genuine tool version/process observations, package manifests and compatibility evidence. Hash consistency alone does not establish that a tool was executed, a version is supported or SDK features are correctly wired. Existing four F04 assertions remain required. No SDK setup, real module recognition, rig/provider inspection, genuine native case emitter or Android build is supplied by this Python helper.

## Output and limitations

resolved-candidate/bindings.json identifies the actual APK/native report/GUID, retained project/package/lock files, executor identity and allowed resolution changes. Every native case observation references these same bindings. The toolchain lock is reproducible local evidence and remains physically unqualified. F05 must later test this exact accepted F04 candidate; all physical testing remains after the 25 POLISH results.

The helper does not extract/verify APK package_id or version_code, prove runtime callbacks, sign/trust/install anything, authenticate human reviews, or qualify any headset. Those real outputs remain required in their respective stages. A malformed/missing native packet blocks binding; no success stub is created. Snapshot copies retain the 5 GiB emergency reserve and existing Unity operation preflight remains unchanged.

## Latest verification boundary

The initial focused22tests passed, then the F02 integration run passed159tests including24build-binding tests. Review subsequently found that the lock Editor executable also needed to match the actual launched executable, and project/package metadata references needed immutable owned-run paths. Those fixes, three focused regressions and three resource-cancellation regressions are now in source. The next actual runner attempt was stopped by its own5GiBreserve guard before any native report existed; its result/process/stdout/stderr remain under evidence/{results,runs}/F02/build-binding-20261016/attempt-02. The earlier159-test result is historical and unaccepted. Latest source has only AST parsing proof, not passing tests.

The doctor now checks the5GiBreserve plus48MiBsmall-suite margin before launch, monitors project/temp volumes, and treats resource-guard cancellation as a named block. That classification correction is source-only until rerun; the original failed attempt and its consumed counter remain unchanged. At safe capacity rerun the complete registered F02 suite, independently verify/accept and refresh F07/F08 prerequisites. Never mark the source fixes or F04 native build accepted from historical results.

Package manifest/version binding does not prove full package archive/tree integrity or native SDK compatibility. The actual a02 inspector must still record genuine package payload hashes and provenance required by F04; this helper cannot manufacture those observations.
