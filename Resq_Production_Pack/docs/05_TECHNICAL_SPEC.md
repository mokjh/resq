# 05 — Technical architecture and engineering specification

## 1. Stack decision and version discipline

Choose Unity 6.3 LTS as the initial candidate, with URP, Android ARM64/IL2CPP, and a supported Meta XR Core / Interaction SDK / MRUK combination. Unity documents 6.3 as an LTS branch; Meta's current ISDK requirements identify supported Unity baselines and integration paths. That does not prove every newest package combination works together. [S05, S06]

Codex must inspect the installed editor, Android modules, JDK/SDK/NDK, graphics APIs, and actual package documentation. Run a minimal hand-tracked passthrough build on a headset before locking production versions. Save exact editor patch and dependency versions in `ProjectVersion.txt`, `manifest.json`, `packages-lock.json`, and `toolchain.lock.json`.

Use one supported XR provider path with one authoritative origin/rig. Do not combine two camera rigs, duplicate input stacks, or competing hand skeleton providers. Confirm the chosen Core/ISDK/MRUK integration together in the probe. Optional Unity XR packages must match that choice rather than being installed speculatively.

Prefer Unity's supported world-space UI approach proven with the installed interaction SDK. Start with uGUI/TextMeshPro if it passes the probe; do not migrate UI technology late in production for novelty. Use Addressables only for local scene/asset lifetime management where it reduces complexity; no remote catalog is required.

## 2. Repository shape

```text
resq/
  AGENTS.md
  docs/                       # this specification and implementation decisions
  game/                       # actual Unity project
    Assets/Resq/
      Runtime/Core/
      Runtime/Simulation/
      Runtime/Input/
      Runtime/XR/
      Runtime/Presentation/
      Runtime/UI/
      Runtime/Persistence/
      Content/
      Art/
      Audio/
      Scenes/
      Editor/
      Tests/EditMode/
      Tests/PlayMode/
    Packages/
    ProjectSettings/
  art/source/                 # editable .blend sources, outside Unity auto-import
  art/exports/                # audited FBX/GLB/textures; manifests
  content/                    # source mission briefs / typed authored definitions
  web/                        # Next.js marketing site
  automation/                 # bounded task runner and permissions config
  tools/                      # validation, import, content, capture, release tools
  captures/raw/               # immutable source footage + manifests
  captures/edits/              # project timelines and rendered derivatives
  evidence/                   # tests, device captures, reports
  releases/                   # immutable release manifests; large binaries external
```

Use Git LFS or a documented equivalent for large binary sources. Preserve `.meta` files. Exclude `Library`, `Temp`, `Obj`, secrets, device identities, room scans, signing keys, and raw private recordings from public version control. Do not delete the user's existing assets or history.

## 3. Assembly boundaries

**Resq.Core:** IDs, integer units, typed commands/events, deterministic random generator, clock interfaces, validation results. No UnityEngine dependency.

**Resq.Simulation:** hazard graphs, transport, power, objectives, scoring, checkpoints, forecast cloning. Depends only on Core and immutable compiled definitions.

**Resq.Presentation:** prefab pooling, animation, water/heat rendering, audio routing, interpolation. Reads simulation state/events and cannot decide rescue outcomes.

**Resq.Input:** hand, controller, and head/dwell adapters translating intent into the same commands. No direct mutation of entities.

**Resq.XR:** permission flow, scene loading, placement, anchors, tracking state, lifecycle events, fallback table.

**Resq.UI:** flow/state machine, binding, readable menus, accessibility settings, localized copy.

**Resq.Persistence:** versioned save envelope, migrations, integrity checks, atomic write/backup, recovery UI integration.

**Resq.Editor:** content compilation, scene/prefab factories, validators, batch builds, visual regression scenes, capture automation.

Use explicit interfaces and constructor composition for pure logic. Avoid reflection-heavy frameworks, a global service locator, or requiring an enterprise dependency injection container for this scale.

## 4. Application composition

`Bootstrap` owns process lifetime, platform lifecycle callbacks, save loading, and service composition. `Hub` owns selection and settings. A single `Operation` scene loads the selected board/content rather than one independent copy of every system per mission. World art loads additively or through an explicit local asset lifetime handle.

On world exit: stop input, pause simulation, save when appropriate, release route previews and pooled transient effects, unload world handles, clear material instances, and verify memory returns near baseline. Never keep all five worlds resident.

### Architecture flow

```text
Hand / Controller / Head-Dwell
             ↓
       Intent Resolver
             ↓
Validated Command Queue → Simulation Tick → Typed Events
                              ↓                  ↓
                       State Snapshot      Presentation / UI / Audio
                              ↓
                    Save + Replay + Tests

MRUK / Anchors → Placement Adapter → visual board transform + locked layout class
                                       (not unrestricted simulation mutation)
```

## 5. Determinism and replay

Use a fixed tick scheduler, explicitly seeded random state, stable ID iteration, and integer authoritative quantities. Avoid dictionary iteration order as a rule. Unity PhysX, animation root motion, floating rigidbody outcomes, wall-clock time, network time, and global Unity random state must not determine success.

A run records mission version, rules version, content hash, layout signature, seed, input preset, validated commands, and periodic compact state hashes. Save actual PRNG state, not only the initial seed, when resuming a snapshot.

Replay correctness is guaranteed only within declared compatible rules/content versions and tested targets. Cross-device bit-exact behavior is a target to verify, not an assumption. Old incompatible replays remain available as metadata or an explicit unsupported-version message; do not run them against changed logic silently.

Reference replays must be generated through real commands and the real simulation. Never write a “solver” that directly sets success flags.

## 6. Runtime services and interfaces

- `ISimulationClock`: current tick, start, pause, step, resume; single owner.
- `ICommandValidator`: validate against current state, reject reasons, no mutation.
- `ISimulation`: apply validated command, advance tick, emit ordered events.
- `IForecastService`: clone state, apply preview command, simulate bounded horizon, return risk/path results.
- `IPlacementService`: safe layout choices, tracking/anchor state, board transform, fallback.
- `IInputAdapter`: intent samples plus cancellation/tracking loss.
- `ISaveStore`: read validated envelope, staged write, promote, rollback to previous valid generation.
- `IContentCatalog`: immutable definition lookup by content IDs/version.
- `IAudioDirector`: event-to-sound with concurrency and priority limits.
- `ICaptureDirector`: dev-only scripted replay and capture camera control; absent from normal control flow.

Async loading must use cancellation and lifecycle ownership. A stale load callback cannot attach a world after the player returned to the hub. Every transient asset handle has one owner and a defined disposal path.

## 7. Persistence without unnecessary backend

The competition game uses local versioned JSON aggregates under the supported persistent-data path. A logical relational ERD documents relationships, but a production SQL server is not required. Content ships read-only with the build.

Write to a new temporary generation, flush where supported, deserialize and validate, then atomically promote using the platform-supported operation and retain at least one last-known-good generation. Verify actual Android behavior. Checksum detects corruption, not cheating or authenticity. Never describe it as security encryption.

Store options, mission progress, unlocks, best results by comparison partition, reward receipts, and an optional current run snapshot. Do not store raw hand poses, microphone input, camera frames, or room geometry. Placement preferences are coarse; an anchor identifier is local and recoverable.

Save at mission phase boundaries and periodic safe intervals without blocking the render thread. Backpressure/coalesce saves so a slow disk cannot create an unbounded queue. A failed save warns once and keeps the last valid progress.

## 8. Rendering strategy and budgets

**Primary target:** sustained native 72 fps on Quest 3S and Quest 3 in worst-case shipped scenes. Meta gives a 72 fps frame budget of approximately 13.9 ms. Aim for additional headroom, not merely average fps. [S08]

Initial internal budgets, to revise from device evidence:

| Metric | Initial target |
|---|---|
| p95 application CPU frame time | ≤10 ms at 72 Hz |
| p95 application GPU frame time | ≤11 ms at 72 Hz |
| Native fps | Sustain selected refresh; no reliance on reprojection to claim 72 |
| Simulation tick cost | ≤1.5 ms p95 in worst shipped operation |
| Per-frame managed allocations | 0 B during steady gameplay hot paths |
| World-visible triangles | Start below ~300k, measure actual renderer/device cost |
| Batches/draw calls | Start below ~150; profile materials and instancing |
| Dynamic visible residents | ≤60; logical counts may be larger through group representation |
| Active gameplay vehicles | ≤8 |
| Real-time shadowed lights | 0–1 depending on profile |
| Initial installed competition size | Prefer <1 GB; verify current upload limits separately |
| Initial application memory target | ≤1.5 GB measured, with clean return after mission cycling |

These are project budgets, not platform hard limits or measured results. CPU and GPU work can overlap; do not add their times naively as the frame duration. Profile real bottlenecks.

Use URP mobile-friendly shaders, baked/static lighting where useful, vertex color variation, limited shadow casting, pooled effects, modest transparent layers, and appropriate texture compression. Water should use an economical surface treatment rather than screen-space refraction, planar reflections, and multiple transparent depth layers. No volumetric fog, cinematic depth of field, motion blur, runtime GI, or high-cost post-processing by default.

A 90 Hz profile is optional and must pass its own frame-time tests. Automatic visual degradation may reduce decorative particles, shadow quality, and LOD distance, never hazard visibility or simulation rules.

## 9. Audio implementation

Use four mixer groups: music, world effects, warnings, speech/captions. Limit repeated resident acknowledgments and pump loops by voice concurrency. Prioritize actionable warnings; duck music gently, not abruptly. Directional cues complement visual indicators, never replace them.

Create stems for calm, developing threat, peak, and recovery. Crossfade based on meaningful simulation state, not the number of particles on screen. Suspend or attenuate loops appropriately when paused.

## 10. Unity/Blender automation

Use deterministic Editor tooling to create prefabs, scenes, ScriptableObjects, materials, bindings, and build profiles. Do not hand-author large Unity YAML structures when an Editor API exists. Validate every prefab reference after creation.

Unity supports batch tasks through command-line methods, but cannot open the same project concurrently in interactive and batch processes. Use a project lock and a clean CI/worktree copy when necessary. [S09]

Discover an existing Unity MCP bridge before recommending another. A Codex-side Unity skill is not an Editor bridge. Validate the actual bridge with a harmless console/hierarchy read. Without it, local batch and repository tooling can still perform many tasks; interactive scene inspection remains unverified.

Blender may run original project Python scripts in background mode for repeatable builds and exports. Disable untrusted file auto-execution, explicitly run reviewed scripts, and fail on Python errors. Do not import/download arbitrary assets from inside untrusted generated scripts. [S10]

## 11. Security, permissions, and networking

No runtime provider API keys in Unity. No unnecessary network requests or web views. No embedded generative model services. Do not upload spatial data for analytics. If optional crash reporting is later added, review data collection, identifiers, consent, retention, and disclosure first.

All automation runs with least privilege within the repository and designated build directories. Keep signing keys and deployment credentials in local/CI secret storage; never log, commit, paste into prompts, or include them in evidence zips. Builds for a trusted protected branch only may receive signing/publishing secrets.

## 12. Release artifact contract

Every release manifest records semantic version, platform build number, source commit, toolchain hash, content hash, APK hash, signing certificate fingerprint, supported devices actually tested, implemented mission IDs, evidence report paths, known limitations, and capture compatibility.

`Competition` is a distribution channel, not a special game mode with different rules. Freeze its exact APK and evidence bundle at submission. Continue future development on another branch/channel. Marketing updates must not alter the linked judged artifacts after the deadline. [S01–S03]
