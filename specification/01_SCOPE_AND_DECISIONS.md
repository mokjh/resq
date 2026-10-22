# Binding scope and decisions

## Scope

RESQ is a premium seated miniature rescue strategy game: inspect → forecast → place/route → observe → rescue → understand consequences. Five authored worlds and 25 missions remain required. Every world includes its own mechanically consequential system, five missions, onboarding, final kit/audio, aftercare, reference solutions, recovery and physical qualification.

The first local deliverable is the complete W1-M1 gameplay slice. Develop all five Alder Valley missions, then W2→W5, completing host AUTHOR/REPLAY/INTEGRATE/POLISH for all 25 before any real Meta device testing. Preserve immutable local development snapshots; they are unqualified. In the final phase qualify the probe, retained first slices, look, missions and worlds, then freeze physically/human-qualified W1→W5 candidates before full-game qualification and release. The user chose full scope over the 18 November competition date; the original three-mission contingency is superseded. No mission or required input/recovery feature can be removed to meet the date.

English game/site/captions ship first. All user-facing game strings use stable keys, no concatenated sentence fragments, and text expansion tests at 150%; no Arabic translation or RTL release is claimed. Quest 3 and Quest 3S are required physical test targets. User-confirmed access does not establish connection/trust or successful tests.

Ship Story, Relaxed, complete Command mode, authored Challenges and Aftercare. Controllers are equivalent alternate adapters. No mandatory game account, online simulation, telemetry, multiplayer, runtime LLM, cloud generation, gacha, chores, permanent stat upgrades, or speculative store backend. The premium model uses the platform purchase flow at commercial distribution; no in-game payment SDK is planned. Final price and legal/store attestations are owner gates with concrete release tasks.

## Contract corrections

| ID | Frozen resolution | Superseded ambiguity |
|---|---|---|
| D01 | Root implementation areas and task state; original pack immutable | Original validator forces all tasks pending |
| D02 | `game/Assets/Resq/Runtime/{Core,Simulation,Input,XR,Presentation,UI,Persistence}` and `art/exports` are canonical | Original graph path variants |
| D03 | Core can compile/test on host before Android/device lock; all local integration may proceed while physical qualification remains pending until the final phase | T04 unnecessarily blocked by headset T03 |
| D04 | Guided first-play tutorial precedes the hazard epoch; unguided replay supports A/B on identical rules/layout | Bridge-first tutorial versus boat-first reference B |
| D05 | Separate new-run, continue, interruption and assisted rollback paths | Fallback routed through new briefing/loadout |
| D06 | Seats, movement slots, dock berths and receiving capacity are separate integers | Bridge “2 agents” versus bus with four passengers |
| D07 | Typed temporal objectives plus explicit critical/mastery IDs per mission | End-state and “never” conflation, redundant mastery |
| D08 | Difficulty/pacing profile and input adapter are separate; partition includes both | Three mixed preset names with controller ambiguity |
| D09 | Two W1 challenge definitions per mission, one chapter-specific challenge per later mission; no arbitrary combinations | Modifier ideas mistaken for shipped variants |
| D10 | Passed/qualified schemas require real successful evidence, identities and hashes | Nonempty results permitted false success |
| D11 | Full scope wins over calendar; complete Command mode required | Optional path and competition cut ambiguity |
| D12 | A qualified game can be frozen while website/domain/publication gates remain pending | Marketing dependency delays game completion |
| D13 | User decision 16 October: all 25 missions complete local development before any real Meta device tests; physical/human/look/Quest replay criteria retained for final qualification | Physical tests blocking local world/art expansion |

## Application and asset architecture

Bootstrap owns platform lifetime and service composition; Hub owns selection/settings; one Operation scene loads immutable content and the active world kit. Construct pure logic with explicit dependencies. Avoid global service locators, reflection frameworks and enterprise DI. Use local direct asset references and explicit lifetime handles; Addressables is excluded initially, added only through a documented measured loading problem and versioned change.

The installed Unity `6000.3.24f1` is the first candidate. Verify live package documentation, resolve one OpenXR/Core/ISDK/MRUK combination and preserve the actual minimal Android build and resolved candidate package hashes before local production. Physical compatibility acceptance occurs only through the late same-candidate F05 test; until then the reproducible lock is explicitly unqualified. A mismatch becomes a blocker; choose the closest supported installed/available LTS patch only through a recorded compatibility decision, never an unreviewed broad upgrade. URP, ARM64/IL2CPP, one rig and world-space uGUI/TextMeshPro are fixed. Vulkan is the first graphics candidate; if the real probe fails a supported feature, requalify OpenGL ES3 as a documented device/toolchain profile rather than enabling both blindly.

Pure simulation does not depend on UnityEngine, XR or presentation. Editor scripts create serialized Unity resources. Blender `.blend` files are source; audited FBX/textures are the normal Unity import contract, GLB/raw generator output is preserved. No `.blend` auto-import dependency in builds.

## Change control

Frozen does not mean unchangeable balance. A change request records observed failing evidence, exact proposed value/behavior, affected rules/content/assets/checks and required reruns. User product decisions cannot be reversed by an agent. Within unchanged scope, authoring/tuning corrections are authorized local work and need no repetitive permission. A change creates a new mission revision or rules version as appropriate, regenerates canonical hashes and invalidates stale qualification/replay/media claims. Acceptance criteria may not be weakened to conceal failure.

The implementer does not choose an unlisted mode, mission, provider or mechanic. Use the supplied defaults and task recipes; remaining discovery gates gather facts. The artifact that closes each gate is specified in the task graph.
