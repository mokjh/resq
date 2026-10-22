# RESQ release and operations

Specification freeze: **resq-spec-freeze-1.1.0**. Rules identity: **resq-rules-1**. This document defines future implementation and delivery operations. It is not an execution log, spending approval, deployment approval or claim that the planned harness exists.

The executable evidence contracts, bootstrap exception, immutable run paths, gate records and release-qualification closure are defined in [07_EVIDENCE_AND_EXECUTION.md](07_EVIDENCE_AND_EXECUTION.md). A schema or validator that has not yet implemented those semantics cannot substitute weaker acceptance.

The unchanged production pack remains source material. This freeze makes **all five worlds and all 25 missions** the primary product target. Alder Valley is developed locally first so later worlds inherit tested host systems. All 25 missions reach local POLISH before the first real Meta test; world physical qualification then proceeds W1→W5 in the final phase. Dates, competition eligibility and publishing opportunities cannot silently turn a first chapter into completion of the full game.

## 1. Workstream outcomes stay separate

| Outcome | Required evidence | Independent constraints |
|---|---|---|
| Game implemented | All 25 missions, systems, UI, art/audio and persistence exist in actual source/builds | Does not establish device or human qualification |
| Game fully qualified | All required automated, shared/world art, XR, device, performance and human gates in 05_PRODUCTION_ACCEPTANCE.md pass for one identified candidate | Website deployment, domain purchase and public video upload are not game-completion blockers |
| Local delivery ready | Qualified game bundle, truthful local website build, approved local media exports, press kit and release instructions | External permissions may remain pending |
| Website locally complete | All required routes, real media, truthful claims, accessibility/browser tests and reference-lock review pass | A local production build is not a deployment |
| Website deployed | Actual identified preview/production URL, deployment identity and deployed tests | Custom domain may remain pending |
| Video locally complete | Approved 160-second export, provenance, captions, media analysis and full human playback review | A local file is not a public video |
| Video public | Authorized publication and unauthenticated playback verification | Does not imply a Devpost submission |
| Game distributed | Actual signed artifact uploaded through an authorized release channel and clean install verified from that channel | Not inferred from a successful local APK build |
| Competition submitted | Entrant completes current eligibility/legal checks and the final submission | Never performed by an automated entrant bot |

Overall status must enumerate these outcomes. “Blocked on domain” cannot obscure a qualified game; “game complete” cannot imply a deployed site or public trailer. When the user has authorized the full delivery scope, blocked publishing work remains explicitly pending rather than being silently removed.

Future check IDs are art-shared, art-W1…art-W5, xr-placement, xr-input, xr-command, xr-states, device-W1…device-W5, human-W1…human-W5, performance-W1…performance-W5, web-local, web-preview, web-production, video-local, video-public, release-competition and release-full. The root task registry binds these checks to exact prerequisites.

The command-facing check names may map to registry IDs prefixed with check-, for example art-shared → check-art-shared. The wrapper records both identities and rejects an ambiguous mapping. Art reports carry their current introduction-world scope; planned future-tool records do not block W1 and cannot be misreported as accepted.

The planned invocation contract after F02 is:

~~~text
python3 tools/resq.py check <check-id> --report <path>
~~~

F01 is the explicit bootstrap exception: it implements and invokes `python3 tools/bootstrap_doctor.py --report <path>` before `tools/resq.py` exists. Its initial preflight/state/status/blocker snapshots are immutable. Check report destinations expand the registry template `evidence/checks/<task-or-suite>/{run_id}/{attempt_id}.json`; placeholders are replaced with validated IDs before execution, and an existing run report is never overwritten. See 07 for result/history paths and dependency bindings.

Examples in this specification are commands to implement and later run. At freeze time, the supplied pack validator is a specification-consistency tool; it is not this acceptance harness. Command availability is a real prerequisite: every task invoking `tools/resq.py` follows F02, including otherwise independent research tasks.

## 2. Toolchain probe before version lock

The observed planning environment contains Unity 6000.3.24f1, Blender 5.2.1, Node 22.13.1, Python 3.14.7, FFmpeg 9.0.1 and metavr 1.8.0.17.10. Those observations are dated preflight evidence, not a guarantee of continued readiness. The Unity install lacked Android Build Support. A separate Waz Unity project and its MCP processes were active. An entitlement file existed, but RESQ had no licensed build proof.

Before production tooling is locked:

1. F01's standalone doctor records the actual repository, current handoff or its absence, dirty/untracked state, tool executables, available storage, running Unity ownership and active integrations. Preserve unrelated projects and processes. Its observed-toolchain snapshot is an inventory of facts and unresolved items, not a compatibility pin.
2. Verify the installed Editor patch and its Android modules. Resolve the Editor-supported SDK, NDK and OpenJDK versions from actual installation metadata and current authoritative documentation. Do not substitute the external Android SDK/NDKs merely because their directories exist.
3. Read current Unity/Meta requirements and actual package metadata for URP, Unity OpenXR, Meta XR Core, Interaction SDK and MRUK. Choose one provider and one authoritative rig/origin. Resolve actual package versions together; do not guess “latest” pins or combine duplicate hand/input stacks.
4. Discover the installed Unity CLI/MCP bridge before proposing additions. Bind RESQ explicitly and run a harmless hierarchy/console read only on its own Editor. Waz connectivity does not prove RESQ connectivity. Do not probe or seize another project's Editor.
5. F03 uses an actual licensed Editor for the host compilation/pure-state smoke check after current primary-source compatibility review. F04 then builds a minimal RESQ Android/XR probe that exercises passthrough, hands, a world-space button, one-hand selection, room permission denial, virtual fallback, placement and pause/recovery. A license-readiness gate precedes the compilation attempt; F03 supplies the actual compilation proof, so neither depends on its own future result.
6. After all 25 missions have actual local POLISH results, complete host Command/controller paths and tutorial, install and run the retained exact F04 probe APK on both user-accessible Quest 3 and Quest 3S with appropriate device setup/trust. Record model, OS, package/build identity and measurements. Merely listing a headset is not an interaction test.
7. During local development, preserve the exact candidate Editor/package resolution and its hashes from F04 in ProjectVersion.txt, Packages/manifest.json, Packages/packages-lock.json and toolchain.lock.json; qualify that same immutable version set only through the final-phase F05 physical probe. The local candidate lock is reproducible, not physically accepted; no lock-byte rewrite is allowed to manufacture acceptance. Record actual SDK/NDK/JDK, Blender/export preset, Node/package manager, FFmpeg and browser-test versions. Acceptance is a separate evidence record, not an edit that changes the lock hash after the probe.
8. Repeat affected checks whenever a locked dependency changes. Never overwrite an accepted lock to make an unrelated newer package appear tested.

Current Meta documentation search supported the installed Unity version baseline, but did not establish a tested Core/ISDK/MRUK combination. That remains the probe's purpose. A source reading, installed license file, successful package download or empty Android build cannot satisfy it.

This is a frozen acceptance milestone, not an unspecified design decision: no guessed package pin is promoted before F01/F03 discovery, primary-source review and the required host/XR compatibility probes. Host-only logic can progress after its own qualified toolchain smoke while physical XR acceptance remains explicitly pending.

Use one installed, verified package manager consistently. The observed pnpm shim attempted a Corepack download and exited unsuccessfully; it must not be described as ready. Resolve it through the normal authorized provisioning path or lock a working alternative. Do not repeatedly bootstrap an unavailable manager.

The Blender probe uses a reviewed local script with untrusted auto-execution disabled: create a calibration mesh, export it, import it into Unity, verify axes/bounds/material references and render it. Save source before disconnecting a dedicated background Blender process. A Blender image does not replace a Unity render.

## 3. Resources and ownership

A single coordinator owns the active milestone. Independent specialists receive exact files, acceptance checks and a stop condition. There is one writer for Unity scenes/settings/prefabs, shared content manifests, every .blend source, each release manifest and each remote generation project.

Only **one heavy build, test suite, analyzer, emulator, Blender render/export batch or video encode runs at a time across the team**. Lightweight documentation and independent reads may continue. A task-owned process ledger records PID, parent PID, working directory, command purpose, start time and expected completion. End unneeded owned processes gracefully after collecting results. Never stop unrelated Unity, Claude, Blender or other work.

### 3.1 Disk gate

The initial preflight reached approximately 3.5 GiB free on a 99%-full internal Data volume; the user intends to free internal space. Re-measure before a heavy action. This observation is not a standing value and authorizes no cleanup.

The following are project operating reserves, not vendor minimum-system requirements:

| Operation | Required free space before start |
|---|---|
| F03-only lean host import/test, exact resource profile | 2 GiB hard reserve +64 MiB positive-growth cap +64 MiB cancellation buffer; <=8 MiB assets and only exact TestFramework/NUnit plus built-in modules |
| F04/F06 scoped local stages and C01 native content test | `local_operation_profiles.json`:2GiB hard reserve + explicit stage growth budget + cancellation buffer; actual tool/cache reuse, per-stage remeasurement and owned cancellation |
| Other Unity module provisioning, new project import or Android build | max(40 GiB, estimated peak new/intermediate bytes + 10 GiB reserve) |
| Blender/art processing batch | max(15 GiB, 3 × estimated new output bytes + 10 GiB reserve) |
| Website dependency/build/browser batch | max(10 GiB, estimated peak new/intermediate bytes + 5 GiB reserve) |
| Capture/video work | max(20 GiB, 2 × expected raw capture bytes + 3 × expected export bytes + 10 GiB reserve) |

An unknown estimate is resolved before launching the heavy operation; it is not treated as zero. Existing reusable inputs do not count as new output, but temporary copies and intermediate encodes do. Record the volume containing each output and verify that volume's capacity, not merely the repository volume.

F03 uses its separate host_working_storage gate and automation/resource_profiles.json. The same four real native assertions remain required in the actual game project. Template render/assets/configuration are preserved reversibly before host configuration and restored/configured for F04; no final-game scope is removed. Cold host setup measurements support the enforced bound, not Android/render estimates. F03 must stop its owned process at2GiBfree or the growth cap, with no success inferred.

The user-directed local stages in controlled amendment5 use their2GiB hard reserve and exact growth/buffer budgets, with process-private cache/temp/Gradle paths and no global Android preference changes. Unknown complete peaks are not zero: these are cancellable attempt budgets. Every original native/art/Android acceptance criterion remains. Their profile does not cover other tasks, provisioning new SDK/NDK/JDK modules, physical testing, video or final production workloads.

If any other running owned operation reaches less than 5 GiB free, repeated ENOSPC, or sustained critical memory pressure, checkpoint where possible and stop only that operation through its supported cancellation path. Preserve logs and usable artifacts. Do not repeatedly rerun unchanged failing commands, perform broad cleanup, change global resource limits or delete caches belonging to active work. Small authorized specification/status writes and read-only investigation can continue when they safely fit.

Eight GiB of RAM makes sequential heavy work mandatory on the observed Mac. Device performance results are still measured on headsets; Mac resource scarcity cannot justify weakening device targets or replacing them with estimates.

## 4. Evidence contract and task completion

The pack's task_result schema alone is insufficient: it can accept a passed result containing unsuccessful test entries. The future harness must enforce the stronger semantics below without modifying or misrepresenting the original pack.

### 4.1 Identity envelope

Every executed check records:

- schema version, run ID, attempt ID, task ID, check ID and exact specification/rules versions;
- UTC start/end timestamps, actor/tool identity and evidence dimension;
- repository identity, actual Git HEAD when present, dirty state, and a canonical source-manifest SHA-256 covering the source actually tested, including required untracked work;
- content bundle/hash, toolchain-lock path/hash, build ID and APK hash when relevant;
- exact command as an argument array, working directory, exit code, stdout/stderr evidence paths and their hashes, with secrets redacted before storage;
- required criterion IDs, expected count, observed count, individual status and boolean result;
- artifact roles, canonical paths, byte lengths, media/content types and SHA-256 values;
- device model/OS and pseudonymous session identity for device evidence;
- human reviewer/participant pseudonym, consent reference, actual observed session and explicit artifact acceptance for human evidence;
- limitations, remaining blockers, actual provider costs/units when any, and the next ready task IDs.

If Git has not been initialized or a commit is not authorized, record Git HEAD as null and use the complete canonical source snapshot identity. Do not fabricate a commit or commit unrelated work. A candidate eventually needs an immutable source snapshot; a real authorized commit plus the actual uncommitted manifest, or a verified immutable source archive, can identify it.

The source manifest uses sorted relative paths and real file hashes. Secrets, dependency caches, private recordings and unrelated files are excluded by an explicit scope list. Required untracked source is included. The manifest must not include its own changing hash in a recursive identity calculation.

### 4.2 Pass predicate

A task can transition to passed only when **all** conditions are true:

1. Task/check IDs and versions match the coordinator's registry; a worker cannot replace the expected check set with a smaller list.
2. Every required check appears exactly once in the accepted run set. No required result is missing, duplicated, blocked, failed, not_run, null or skipped.
3. Every required criterion boolean is true. Unknown metrics cannot be coerced to true.
4. Each command-based test actually ran and returned exit code 0; parsed native test/build reports agree. A shell exit alone cannot conceal zero tests, a skipped suite or a failed Unity BuildReport.
5. Test counts meet the registry's expected counts, including per-mission, preset, layout, device and human coverage where required.
6. Every required artifact is an existing readable nonempty regular file of the expected type. Resolve symlinks and paths; reject traversal or a target outside authorized repository/build/media roots.
7. The verifier recomputes file hashes and sizes. Required report references resolve to actual evidence, not directories, templates, future filenames, screenshots of logs or expiring provider URLs.
8. The current task's check identities agree with its result. Every prerequisite binds the exact accepted task result and hash; historical implementation prerequisites retain their original identities. Candidate requalification tasks must additionally match the candidate APK, content, rules and toolchain exactly, as specified in 07. An earlier development result cannot masquerade as current-candidate qualification.
9. Device checks contain actual hardware execution evidence. Human checks contain real observations or explicit human signoff; an AI-generated approval cannot satisfy them.
10. Costs and provider-operation IDs reconcile; uncertain paid submissions are unresolved, not successful.
11. Required blockers are empty and severity limits pass.
12. An independent integration verification accepts the result and updates durable status. A worker's prose is not the verifier.

A task with failed optional checks cannot simply call them optional after execution. Optionality is fixed in the registry beforehand and may not remove a full-product requirement. A partial result may retain valid component evidence while the task remains blocked/failed.

Human review does not require a shell exit code. It requires a real review record identifying the human role, time, inspected artifact hashes, each criterion and the actual decision. An agent may accurately transcribe provided human observations and identify itself as transcriber; it must not invent participation or approval.

### 4.3 Evidence retention and invalidation

Reports and release candidates are append-only by run/candidate identity. Fixes produce new reports and hashes; they do not edit prior failures into passes. Before completing a task, recheck inputs/outputs for changes during the run.

Task results carry the exact prerequisite-result references and required gate-evidence references. The verifier resolves those files, hashes, task/gate IDs and passed statuses recursively against the acyclic registry. Mutable task-state pointers alone do not prove a dependency. Every attempt, failure, block and accepted result remains in append-only history; only the current-state projection may be replaced atomically. Ordinary code work permits at most three repairs after the initial attempt; art permits at most two. A new run ID does not reset either bound.

A code/content/asset/toolchain change invalidates its direct checks and all dependent claims. The impact list names what may be reused and why. A render-only change cannot reuse an older image approval; a simulation change cannot reuse older outcome replays; an APK change cannot silently inherit a complete hardware matrix.

Public evidence is a sanitized derivative. Keep device serials, raw room scans, account IDs, private invitation links, signing material and raw private recordings out of public artifacts. A hash does not grant permission to publish its source file.

## 5. Production and delivery task sequence

The root dependency graph remains authoritative. This table fixes the responsibilities and evidence required at production boundaries.

| Work package | Prerequisites | Outputs and exit evidence |
|---|---|---|
| Local foundation | Safe repository, storage, owned Editor and permitted provisioning | Actual resolved candidate lock, licensed host/Android build, harmless RESQ bridge read, calibration export/import and explicit unqualified device status |
| Local shared look development | Host first mission/tutorial, calibration and original/licensed sources | Four-asset Unity test board, 24-cell sheet and machine findings; device captures/human signoff deferred but still mandatory |
| Core game | Frozen runtime/content contracts | Real command/simulation/save/input implementations, compiled content and executed tests |
| W1 | Shared gates and its complete mechanic | Five finished missions, owned inventory, references, device/performance/human gates and aftercare |
| Local W2 → W3 → W4 → W5 | All five prior-world POLISH heads and next-world host mechanic/first-mission integration | Each world develops five missions, host references, kit/audio/access/aftercare; retained immutable local snapshots are unqualified |
| Final physical qualification | All 25 POLISH heads, F04 candidate, host X05/X06 and tutorial | F05 on both models; retained first slices and look device/human review; all mission/cumulative Quest cases; world performance/device/human/freezes in order; then full-candidate qualification |
| Full candidate | All 25 missions and shared gates pass | Signed build candidate, exact source/content/toolchain identities, zero open P0/P1/P2, complete evidence index |
| Capture | Qualified candidate, rights/privacy clearance and known shot purposes | Actual raw clips, capture manifest, source hashes and technical media probes |
| Local video | Approved clips, EDL, licensed audio/fonts | 160-second masters, sidecar captions, media report and full-export human review |
| Website research | Actual available Mobbin access | Inspected screenshot/detail records and an original reference lock |
| Local website | Reference lock, truthful release data, real cleared media | All routes, production build, browser/a11y/media tests and visual review |
| Press/support | Qualified facts and cleared assets | Factsheet, logos, ten world screenshots, credits, troubleshooting and judge/install instructions where applicable |
| Local release packaging | Qualified game and required local delivery artifacts | Immutable release bundle, recomputed hashes, valid evidence closure and clean install instructions |
| Preview / production / public video / channel upload | Ready local artifact plus matching external authorization | Real provider identities/URLs, deployed or public playback checks and access records |
| Optional competition entry | Current rules/eligibility, explicit entrant decision and a qualified candidate containing all five W1 missions | Frozen entry draft and access package; entrant completes submission |

No final marketing dependency forces mass asset generation before mechanics work. Website data models, truthful draft copy, shot lists, EDL templates and tests can progress while gameplay is being built. Final media cannot be invented to fill missing capture inputs.

## 6. Asset and audio operations

Use the 487-record inventory in 05_PRODUCTION_ACCEPTANCE.md and its exact transcription in `specification/content/production_inventory.json`. Every family records owner world/shared scope, style-lock version, source author/provider, prompt/reference provenance where applicable, rights evidence, source/export hashes, exact dimensions, material roles, triangle/LOD data, rig/animation data, Unity import settings and render evidence.

Original procedural Blender work is a valid production path. Higgsfield assistance is used only through actually discovered tools with verified downloadable formats, destinations, terms and allowed costs. An account subscription or visible credit balance is not spending authorization.

Remote generation must have:

- an explicit provider/destination authorization and cap in the active policy;
- an operation/request ID and expected cost before submission when available;
- one active mutation per remote project and one local .blend writer;
- preserved raw committed outputs and source/license evidence;
- reconciliation of uncertain/timed-out operations before any repeat;
- no more than two asset repairs after the initial attempt before a blocker or reviewed original rebuild;
- no unknown-rights material admitted into the production manifest.

A concept image is not a mesh. A generated GLB is not a finished prefab. An editable source without real Unity renders does not pass art acceptance. Unknown-rights assets are quarantined and excluded from builds/captures.

Audio follows the same provenance rules. Required English information is captioned. Paid voice generation and voice cloning are outside the frozen inventory. A missing optional narrator cannot block the 92 required game-audio records or a caption-led trailer.

## 7. Capture provenance

Capture only from a candidate that has qualified the pictured behavior. Full-release footage may show all five worlds only when their checks passed. A separately chosen competition candidate contains all five W1 missions and may show only that candidate's own shipped content as playable. There is no three-mission fallback.

Each raw clip manifest entry contains:

- stable clip ID, actual immutable path, byte length and SHA-256;
- source candidate/build/APK, source manifest, content and toolchain hashes;
- device model/OS or clearly identified simulator/render target;
- mission, layout, preset, input mode, session/run ID and simulation tick range where available;
- actual human interaction, deterministic replay, simulator or capture-camera classification;
- original resolution, frame cadence, audio format and capture settings;
- privacy/rights review, identifiable-room/bystander status and permitted publication scope;
- shot purpose, editorial in/out eligibility and technical defects.

Retain raw clips separately from derivatives. Do not overwrite originals when trimming, stabilizing, captioning or encoding. Never use generated hands, invented rescues, repaired tracking footage or AI cinematic worlds as evidence of gameplay. A replay may explain mechanics, but it is not a human usability observation. Headset footage proves headset presentation only for its actual build and device.

Use a controlled room and consenting participants. Do not upload raw room video to a provider without the specific permitted exposure. Framing/cropping may protect privacy, but must not conceal the interaction or imply a different supported reach. Capture-camera shots are not presented as the player's head camera.

If raw footage is absent, deliver the storyboard/EDL plan and a capture blocker. The media task remains unfinished.

## 8. Locked W1 video and separate full-game edition: 160 seconds

Each final edition is **160.000 seconds, 16:9, 1920 × 1080, SDR Rec.709, H.264 video and AAC stereo audio**. The first-chapter/competition edition retains the pack's full five-mission W1 storyboard below. It is a separate immutable artifact; later-world footage never replaces its shots or changes the submitted master.

| Time | Required real picture | Intended evidence |
|---|---|---|
| 0–8 | W1 threat, actual hand intervention, visible redirection | Immediate player action and consequence |
| 8–22 | Residents reach safety; title over the real W1 scene | RESQ / Small worlds. Big saves. |
| 22–43 | W1-M1 bridge preview, legal anchors, unfolding and crossing | Read → place → consequence without obscuring the action |
| 43–64 | W1-M2 pump forecast, unsafe/safe outlet comparison and deliberate safe choice | Every route changes the risk |
| 64–85 | W1-M3 skiff loading, route decision and safe unloading | Plan the rescue; keep the way out open |
| 85–105 | Two actual table setups, compact class and virtual fallback | Real room/placement behavior and seated play |
| 105–124 | One-hand interaction and a qualified Command sequence | Actual supported access paths, not generated hands |
| 124–142 | W1-M5 combined tools, result/debrief and actual progression | Find a better plan; show the full five-mission chapter |
| 142–154 | Tactile W1 detail, real sound cue, rescued-town response | Complete first chapter and offline behavior only as verified |
| 154–160 | Quiet final rescue payoff and original title card | RESQ / Small worlds. Big saves. |

A **separate full-game edition** can be assembled only after all five worlds are qualified, with a new EDL, source manifest and export hashes. Its explicit 160-second plan contains **ten segments**:

| Segment | Time | Required real picture |
|---|---|---|
| 1 | 0–8 | W1 hook |
| 2 | 8–20 | Title/rescue |
| 3 | 20–40 | W1 flood decision |
| 4 | 40–60 | W2 fire/wind decision |
| 5 | 60–80 | W3 brace/vertical rescue |
| 6 | 80–100 | W4 tide/power decision |
| 7 | 100–120 | W5 cooling/temporary-refuge decision |
| 8 | 120–136 | Verified seated/access/fallback paths |
| 9 | 136–150 | Real full progression/debrief/aftercare |
| 10 | 150–160 | Final payoff/title |

This later edit never inserts future footage into the W1 candidate. Local/public status is tracked independently for each edition; absence of the later edition does not alter the W1 master or falsely complete full-game delivery.

At least two action-to-result sequences retain ≥10 continuous seconds of readable causal footage. A sped-up segment is visibly labeled and cannot prove timing/performance. Planned worlds, concept art and undeployed features cannot appear as playable content.

The EDL stores clip IDs and hashes, source in/out positions, timeline start/end, overlays, narration/caption intervals, audio gain/ducking, transitions, crop/scale decisions and export settings. Every segment fits the frozen 160-second timeline and resolves to cleared inputs.

### 8.1 Export and validation contract

| Property | Target |
|---|---|
| Frame rate | CFR 60 only when real source cadence supports it; otherwise CFR 30. No synthesized/interpolated frames |
| Exact frame count | 9,600 frames at 60 fps or 4,800 at 30 fps |
| Duration tolerance | At most one output-frame deviation from 160 seconds; always <180 seconds for a competition derivative |
| Codec | H.264 High, yuv420p; level 4.2 at 60 fps / 4.1 at 30 fps |
| Encode target | CRF 18, medium preset, maximum video rate 20 Mb/s, 40 Mb buffer; change only through a recorded quality decision |
| GOP | Maximum 2 seconds; 120 or 60 frames according to cadence |
| Audio | AAC, 48 kHz stereo, 192 kb/s |
| Loudness | −16 LUFS integrated ±1 LU; true peak ≤−1 dBTP, measured from final export |
| Container | MP4 with fast-start metadata; one intended video stream and one audio stream |
| Color | SDR Rec.709; no accidental HDR tone mapping or crushed caption backing |
| Captions | English SRT and VTT plus a captioned export; retain uncaptioned master |
| Caption layout | ≤2 lines, ≤42 characters per line, ≤17 characters/second, safe area ≥5% from each edge |
| Caption timing | Spoken captions within 100 ms of onset and 200 ms of end; readable explanatory captions remain ≥2 seconds |
| Caption size | At least 42 px equivalent at 1080p, strong backing and no obstruction of the causal action |
| Black/frozen frames | Zero unexplained black segment ≥100 ms or freeze ≥500 ms; intentional fades/stills declared per EDL and inspected |
| Visual review | Inspect first/last frames and every cut/fade; a human watches the entire exported file with ordinary headphones/speakers |

FFmpeg and ffprobe availability/version are verified at execution. Use a reproducible local encoding script and retain its exact argument arrays, EDL hash and measured results. A planned command is not a rendered file. The final encode itself is probed and listened to; timeline preview approval is insufficient.

For each produced edition, delivery families are the uncaptioned master, captioned master, SRT, VTT, EDL, source capture manifest, final technical report, human review and one real-game poster image. Optional narration must come from an authorized original/licensed voice; otherwise the edit remains caption-led with real game sound.

video-local passes only when D03's technical assembly and the distinct D03-REVIEW human signoff both pass on the exact W1 exports. The full-game equivalent requires FULL-VIDEO and FULL-VIDEO-REVIEW. The automation can validate the review record; it cannot watch on behalf of the human. video-public additionally requires matching publication authorization, a real provider URL/ID and successful unauthenticated playback of the published result. Confirm processing has finished and captions/audio are available. A 60 fps export does not claim native 72 Hz gameplay performance.

## 9. Website reference lock and exact route scope

The website's initial brief is a miniature rescue field guide using the game's evergreen, warm paper and rescue-orange hierarchy. It is **not a locked composition or font decision** until real Mobbin research is inspected.

Research three needs: game/entertainment launch with strong real media, readable editorial information hierarchy, and accessible support/feature explanation. Retrieve actual screenshots/details. Record reference IDs/URLs, capture dates, local permitted evidence/hash, observed traits, adaptations and rejected traits. A search title, inaccessible screen or failed call cannot count.

Choose one dominant composition reference and at most two narrow supporting patterns. The reference lock contains:

- actual inspected references and their permitted research use;
- original section hierarchy, annotated desktop/mobile layouts and media slots;
- exact color/text/background pairings and measured contrast;
- licensed font family/weights and files, scale and line-length rules;
- spacing, content width, radii/borders, focus and reduced-motion behavior;
- source-game media mapping and a list of excluded generic patterns.

The initial brief uses an 8 px spacing base and a content maximum within 1200–1360 px. Final values are selected from the inspected direction and recorded. Research screens are not licensed marketing assets. Do not copy distinctive logos, illustration, text or an entire layout.

### 9.1 Routes

There are **13 required page outcomes**, including the not-found outcome, and one optional competition page:

| Route / outcome | Required truthful content |
|---|---|
| / | Real gameplay hero, read/plan/act/rescue story and a working available CTA |
| /how-to-play | Core loop, actual input instructions, pause/advance semantics, failure recovery |
| /worlds | Five-world catalogue with explicit implementation/qualification/release status |
| /worlds/alder-valley | W1's five real missions, flood strategy, qualified images/media |
| /worlds/emberwood | W2's five missions, fire/wind/wetness decisions |
| /worlds/frostline | W3's five missions, structures and vertical logistics |
| /worlds/tidemark | W4's five missions, tide windows and dry power |
| /worlds/cinder-reach | W5's five missions, cooling and temporary refuge |
| /accessibility | Tested inputs, seated bounds, text/captions, dwell settings, limitations and fallback |
| /press | Approved assets, factual description, current factsheet, actual credits/contact |
| /support | Setup, trust/permission fallback, saves/recovery and real support destination |
| /privacy | Implemented local data and website service behavior, not a generic policy |
| Not-found /404 outcome | Real HTTP not-found handling, usable navigation and no broken loop |
| /competition — optional | Frozen candidate/access instructions and current declared scope; no private invitations or secrets |

The five world slugs are stable. Public data derives from the release manifest, with planned, implemented, qualified and released kept distinct. Before qualification, unfinished worlds are clearly labeled plans or omitted from public navigation; they are never sold as playable. The finished full-product site represents all five qualified worlds and 25 missions.

No accounts, waitlist, email collection, store checkout, subscriptions, database, CMS, tracking analytics or third-party behavioral cookies are part of this freeze. These require separate product scope and authorization. There is no fake form or optimistic “success” message. If a real support contact or owner identity is missing, record the prerequisite rather than inventing an address or company.

### 9.2 Media and press inventory

The finished website/press set contains:

- one real hero gameplay loop with one still poster and a user-controlled Play path;
- five world clips, one per world, 8–12 seconds each, and five still posters;
- ten cleared high-resolution gameplay screenshots, two per world, with build/world/mode captions;
- wordmark and Q symbol exports in light/dark SVG and suitable PNG variants, derived from the approved brand families;
- one factsheet in structured data and a readable PDF, one credits/provenance index and one support/start guide;
- the approved 160-second trailer and caption assets;
- one versioned press archive with a manifest and hashes.

Responsive derivatives are generated from these real originals and declared in the media manifest. Do not upscale screenshots and label the output native resolution. Explanatory arrows are labeled editorial overlays when they are not in-game UI. No fake awards, review quotes, ratings, sponsor association, fabricated store availability or planned-world gameplay montage appears.

### 9.3 Local web acceptance

Use compatible pinned Next.js/TypeScript versions resolved from the actual project. Static content and small interactive components are the default. Do not add a heavy above-the-fold 3D viewer or backend dependency.

web-local requires actual type checking, linting, content/claim/link validation, production build, browser flows, accessibility automation, media validation and visual review. Test all routes at 320, 360, 768 and 1440 CSS-pixel widths, normal and 200% text scaling, keyboard-only input, reduced motion, slow/unavailable media, long headings and modal focus restoration.

Required results:

- zero broken internal links, missing required assets, console errors or unhandled browser exceptions;
- zero critical/serious accessibility findings; zero unresolved findings on essential navigation, contrast, labels, focus or controls;
- all required controls keyboard-operable, visible focus, sensible order and restored focus after modal close;
- body text contrast ≥4.5:1, large text/essential controls ≥3:1, no color-only information;
- no horizontal overflow at the declared widths; no clipped content at 200% text;
- no autoplay audio; reduced-motion mode uses real stills with explicit playback;
- responsive images/posters and deferred below-fold media;
- median of three declared lab runs for the homepage and each route template: LCP ≤2.5 s, CLS ≤0.1, and TBT ≤200 ms under the locked lab profile;
- scripted interaction latency p95 ≤200 ms under that same documented lab setup;
- actual inspected desktop/mobile screenshots compared with the reference lock.

Field INP ≤200 ms is a later real-traffic target. Synthetic TBT or scripted latency must not be reported as collected field INP. Playwright WebKit is identified as an engine test, not a physical iPhone or actual Safari session. Record the browsers/engines actually exercised; no untested-device compatibility claim is inferred.

## 10. Deployments, domain and external permissions

Drafting this specification and preparing reviewable local outputs require no additional permission. At execution, previously granted scoped authorization persists; do not ask again for the same covered action. The permission check is separate from whether a technical artifact is ready.

| Action | Required scope before execution |
|---|---|
| Existing authorized local code/docs/assets/tests | Actual repository and designated build/media paths; preserve pre-existing work |
| Installing Unity packages/modules or a new MCP bridge | Matching explicit provisioning authorization where policy requires it, after discovery and a concrete package/module plan |
| Paid generation | Provider, destination, output/data exposure, cost/credit limit and retry policy |
| Private provider upload | Named provider/destination, specified files/data class and privacy permission |
| Vercel preview | Verified team/project/repository/environment and permitted preview exposure |
| Production deployment | Named project/environment and public release authorization |
| Public video/media upload | Provider/account/destination, exact approved export and publication scope |
| Release-channel upload | Actual app/channel, signed artifact, audience/access scope and authorization |
| DNS/domain linking | Verified user ownership and exact records/project changes |
| Domain purchase, entrant legal terms, final submission | User/entrant action; not inferred from broad build instructions |
| Human research/recording | Real participant consent and any necessary activity/contact authorization |

The production pack defaults to zero paid generation and external writes disabled. A reachable connector, OAuth login, paid subscription, readable Vercel team or credit balance does not change those defaults. Record authorized scope without exposing tokens.

Make the action concrete before a final permission request: build/test the local output, identify exact destination and exposure, prepare the artifact hashes and proposed change, then ask only for the still-missing action-specific authorization. Do not interrupt already authorized reversible local work with speculative permission questions.

### 10.1 Vercel and public verification

1. Read the actual Vercel identity/team/project and repository/environment binding. Never guess IDs or create a new project as a discovery shortcut.
2. Deploy the tested preview only when its scope is authorized.
3. Record deployment ID, environment, source hash, content/release-manifest hash and returned URL.
4. Run web-preview against that deployed URL: links, media, assets, accessibility/browser flows, metadata, errors, privacy and claim checks. Authentication/protection behavior is recorded.
5. Prepare the exact production promotion/deployment. Execute only under matching authorization.
6. Run web-production against the real public destination, including an unauthenticated access check. A Ready status alone is not enough.
7. Preserve the prior release identity and documented rollback path. Do not automatically roll back unrelated newer work.

playresq.com remains an **intended domain, pending user ownership action and verification**. Do not purchase it or imply availability/ownership. After ownership, inspect the exact current project DNS instructions and apply only authorized records. Do not overwrite unrelated DNS or use a remembered IP. A verified provider URL can serve the site while the custom domain is pending.

## 11. Release bundle and claims

A release candidate is a named immutable set, not the latest Editor state. The release manifest contains:

- candidate ID, semantic/app build version and UTC freeze timestamp;
- exact specification/rules/source/content/toolchain identities;
- signed APK path, SHA-256, byte length, package ID and signing-certificate fingerprint;
- the 25 target, implemented and qualified mission IDs as separate arrays;
- actual qualified device models/OS versions, input/layout/preset coverage and evidence links;
- all shared/world check reports and their hashes;
- `qualification_tasks`: exact task IDs, accepted result paths, hashes and passed statuses for the declared release scope, with candidate identity bindings defined in 07;
- asset/audio inventory identity, rights index and known-issues list;
- save/content compatibility and migration policy;
- capture compatibility and approved public claims;
- local website/media/press artifact identities and their independent statuses;
- actual deployment, public video and release-channel identities only when they exist;
- domain ownership/verification state and unresolved external prerequisites.

No null URL is replaced with an invented destination. A public compatibility statement comes only from matching qualified evidence. “Offline” must match actual runtime behavior; optional marketing/provider tools do not become runtime game dependencies.

release-full validates the complete local full-game candidate and required local delivery bundle. Its game qualification component must stand independently of deployment/domain/public-upload authorization. Publishing statuses remain separate until executed and verified. A technical error in the game cannot be waived because marketing is ready.

Full-game qualification requires FULL01, FULL-PERFORMANCE, FULL-DEVICE, FULL-HUMAN and FULL-QUALIFY. The full local delivery bundle also requires FULL-VIDEO-REVIEW and FULL-WEB, including their prerequisites. Each world requires its REGRESSION, PERFORMANCE, DEVICE, HUMAN and FREEZE tasks. The five-mission competition candidate adds D03-REVIEW and D05 to the W1 set. Entrant submission and publication remain separate. Candidate manifests are assembled before their own task result is accepted; a subsequent immutable qualified record binds the accepted results without creating a manifest/result hash cycle.

The release packaging gate recomputes every manifest hash, validates evidence closure, scans the intended distributable for secrets/private data, verifies rights, and performs a clean install/launch/save/resume check of the exact signed APK on both devices. It rejects a wrong package, missing file, stale hash, mismatched source or incomplete mission list.

All artifacts are retained in versioned release paths; large/private binaries may use an authorized external artifact store with durable local manifest records. Do not place private room captures, signing keys, account secrets or judge invitation tokens in a public repository or press ZIP.

## 12. Competition, dates and full-scope priority

The pack's October/November 2026 checkpoints and first-half-2027 aspiration are historical planning assumptions, not delivery promises or autonomous-runtime estimates. Re-verify any official cutoff and rules from authoritative sources when the entrant actually chooses a submission.

Full scope takes priority over a competition deadline. Missing a calendar checkpoint produces a revised forecast and blockers; it does not reduce five worlds to one, remove missions, relax device/human gates or justify a false completion statement.

If the entrant separately chooses a competition candidate:

- require all five W1 missions and all applicable shared/W1 gates with zero open P0/P1/P2; declare its supported hardware and accessible modes;
- keep its artifact set and release branch/channel immutable;
- continue full-game development separately without mutating the submitted candidate;
- verify current entry eligibility, division, originality, rights, presentation constraints, judging access and deadline;
- supply a truthful title/description, actual development process, observed challenges and lessons, and clearly separated future work;
- provide real public video, access/start instructions, tutorial reset, fallback, offline behavior and known limitations;
- test a clean installation from the intended channel and maintain access for the actual required judging period;
- leave registration, eligibility/legal attestations and final Devpost submission to the entrant.

release-competition cannot turn an unfinished full-game task green. A competition-specific submission failure need not block ongoing full-game implementation. Changes after a submitted freeze require actual organizer/entrant direction, a new candidate identity and appropriate requalification.

## 13. Handoff and maintenance

At each bounded milestone, update actual status, decisions, blockers, task state and a handoff with:

- cwd, repository/source identity, dirty/untracked work and exact owned files;
- completed artifacts/hashes and tests actually executed;
- device/human evidence limits, costs and unresolved approvals/prerequisites;
- active processes/Editors, ownership, storage snapshot and locks;
- failed repair attempts and preserved working state;
- next smallest ready action and dependencies.

One coordinator remains accountable. A successor receives current uncommitted/untracked work and verifies readiness once before ownership transfers. Do not create a successor merely for a short pause, lose the working tree, copy credentials/caches or create an idle chain.

Maintain a single consolidated prerequisite list. Before asking the user again, inspect existing authorization and supplied answers. Current confirmed planning decisions include access to both Quest 3 and Quest 3S, English first, full scope over the competition deadline and intended internal-space cleanup by the user. They do not prove device trust, freed capacity, a licensed build, human playtests, domain ownership or permission for external writes.

The smallest implementation-ready entry point is F01: implement the self-contained bootstrap doctor and preserve immutable initial snapshots. F02 then implements the real runner and evidence enforcement; F03 proves licensed host compilation before F04 resolves and builds the local Android/XR candidate. Local work continues through all 25 missions; F05 and every real Meta/device/VR-human test occur in the final qualification phase. Recheck storage and ownership before each heavy action. Until those tasks actually run, this freeze remains specification evidence only.

