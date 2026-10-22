# RESQ production acceptance

Specification freeze: **resq-spec-freeze-1.1.0**. Rules identity: **resq-rules-1**. This is a production contract, not evidence that an asset, application, test, headset session, or review already exists. It supplements the unchanged production pack, especially docs 04, 05, 07 and 09. The runtime equations and mission definitions in the other frozen specifications remain authoritative for simulation outcomes.

The full target is **five worlds and all 25 missions**. Alder Valley is the first production gate, followed by Emberwood, Frostline, Tidemark and Cinder Reach. Passing the first world does not complete the game. English is the first release language. A competition opportunity cannot silently reduce this target.

## 1. Evidence and completion

Each required criterion below becomes an identified check in the future task registry. Four evidence dimensions remain separate:

| Dimension | What qualifies | What it cannot establish |
|---|---|---|
| Automated | Executed validation, tests, replay comparison, import/render audit or media analysis with real outputs | Human comfort, preference, enjoyment or aesthetic approval |
| Device | Observed execution of the identified APK on an identified Quest model/OS, with measurements or recordings | A human's understanding, comfort or voluntary choice |
| Human observed | A consenting person's recorded actions or observations tied to a session and build | Population-wide accessibility, safety, market demand or universal comfort |
| Human signoff | A named human reviewer explicitly accepts the stated artifact hashes and criteria | Acceptance of a later modified artifact |

AI image review is recorded as **machine review**, with the inspected image hashes and findings. It may fail an asset or request correction. It may not produce human approval. A device operator may also be a human reviewer, but the two roles and observations must be recorded separately. No evidence is inferred from an installed tool, a worker's summary, an empty report or a generated screenshot.

A required result is passed only when all expected check IDs are present, all required booleans are true, actual command outcomes agree, and every referenced artifact exists and hashes to the reported identity. The detailed result contract is in 06_RELEASE_AND_OPERATIONS.md. Missing evidence is not verified or blocked, never a successful skip.

Acceptance is incremental by the registry's frozen milestone scope. The full catalogue contains future records, but a W1 gate cannot require W2–W5 mechanics or their assets. An art-shared report records scope_world_max and qualifies only currently introduced tool families, clips, controllers, icons and SFX, plus the shared foundation used by that scope. Future-tool records remain planned until their mechanic gate passes and are accepted with that world's art integration. A narrower earlier report cannot satisfy a later cumulative scope or the full 487-record closure. The same rule scopes mission/device tests to the current world's five missions; full acceptance requires the complete matrices below.

Future commands such as the following are specifications for a harness to implement; this freeze does not claim tools/resq.py exists:

~~~text
python3 tools/resq.py check art-shared --report evidence/art/shared.json
python3 tools/resq.py check xr-input --report evidence/xr/input.json
python3 tools/resq.py check device-W1 --report evidence/device/W1.json
~~~

## 2. Frozen art direction and physical conventions

The visual direction is a coherent, tactile, hand-built miniature rescue world: readable rooflines, broad material changes, grounded supports, restrained weathering and purposeful state changes. Photorealistic disaster imagery, graphic injury, real rescue-service marks, sponsor logos, stereotyped residents and visibly mismatched downloaded kits fail acceptance.

The interaction hierarchy is equipment first, residents and evacuation paths second, hazards third, architecture fourth, decoration last. Rescue orange stays consistent across worlds. A hazard may use a warm world color, but its shape, pattern, label and edge treatment must remain distinct from interactive equipment.

### 2.1 Dimensions and coordinates

| Item | Frozen initial contract |
|---|---|
| Wide board exterior | 0.72 m wide × 0.54 m deep |
| Compact board exterior | 0.60 m × 0.45 m; uniform 5/6 visual scale of the wide board |
| Wide simulation region | 0.64 m × 0.48 m; 32 × 24 cells at 20 mm visual pitch |
| Logical cell | 1 m × 1 m, represented as 1,000 mm × 1,000 mm in logical coordinates |
| Scale | 1:50 at wide size. Compact changes presentation scale, not rules, capacities, navigation distance or clock speed |
| Layout identity | Wide/compact and the chosen validated near-side docking permutation enter the layout signature |
| Surface clearance | At least 30 mm outside each board edge: minimum clear footprint 0.78 × 0.60 m wide, 0.66 × 0.51 m compact |
| Rim | The remaining visual margin is not a secret interactive strip or additional simulation region |

Blender sources use meters, Z up and a declared forward axis of -Y. Unity prefabs use meters, Y up and +Z forward. An axis-colored calibration object and a measured 100 mm reference bar must prove the export conversion. Do not apply an assumed rotation or hidden import-scale correction. Applied mesh transforms are unit scale and zero rotation; intentional child animation transforms are documented.

The four look-development assets have the following wide-layout dimensions. Bounding-box tolerance is ±1% after export/import; the compact instance scales uniformly by 5/6.

| Asset | Width × depth × height / defining measure | Pivot and functional detail |
|---|---|---|
| Cottage | 80 × 60 × 70 mm | Ground-center root; door marker on the navigation-facing wall |
| Deployable bridge | 120 × 40 × 20 mm in deployed state | Explicit near/far support markers; animation cannot move logical sockets |
| Skiff | 80 × 35 × 25 mm | Waterline-center root; manifest-defined boarding and seating markers |
| Standing resident | 34 mm height, 12 mm shoulder width | Ground-contact root; feet do not penetrate the navigation surface |

Other cottages remain within 60–100 mm footprint width and 55–85 mm height; landmarks within 160 mm height; rescue tools within their declared socket bounds; crawler within 100 × 45 × 45 mm. A footprint must be validated against its actual content sockets before acceptance. Decorative collision cannot block a route that the simulation declares open.

All roots use stable asset IDs. Meshes, renderers, moving parts, sockets, LODs and colliders have readable deterministic names. Sources, export settings, generated exports and prefab references are retained. Unity does not depend on automatic .blend import.

### 2.2 Material and palette lock

These are authored material targets, measured under the fixed look-development lighting, not a guarantee of appearance on every real surface.

| Material role | Roughness | Metallic | Additional limits |
|---|---:|---:|---|
| Painted metal | 0.65–0.85 | 0–0.15 | Bare-metal trim may use 0.8 metallic on ≤5% of a tool's visible surface |
| Wood | 0.70–0.90 | 0 | Broad grain/value changes; no noisy photographic microdetail |
| Stone | 0.80–1.00 | 0 | Soft edge wear; structural contact remains readable |
| Cloth | 0.85–1.00 | 0 | No glossy resident clothing |
| Rubber | 0.80–1.00 | 0 | Separate value from metal and water |
| Water | 0.35–0.50 | 0 | No screen-space refraction, planar reflections or stacked transparent waterfalls |
| Signal/emissive | Authored per state | 0 | No bloom dependency; every state remains legible with emission disabled |
| Ground/soil | 0.80–1.00 | 0 | Hazard overlays must align to logical cells/edges |

Where a Unity shader uses smoothness, the material pipeline explicitly converts smoothness = 1 − roughness and verifies a calibration strip. Base color uses sRGB; normal and packed occlusion/roughness/metallic data use linear sampling. Color-space, compression and mipmap settings are stored, not left to inconsistent imports.

Brand colors are rescue orange #FF6B35, evergreen #132B2A, paper #F3EBDD and mineral #D9E4DB. The following five world palette records preserve the authored identity:

| World | Palette roles |
|---|---|
| W1 Alder Valley | Foliage #355D47; water #438B9B; roof #C56646; stone #D8CCAF; rescue #FF6B35 |
| W2 Emberwood | Foliage #254D42; bark #6B4937; ember #E98A43; smoke #B4AEA1; sky #D1DDD4 |
| W3 Frostline | Snow #E8F1EF; rock #677D8C; pine #355458; roof #C54C43; signal #F4B64B |
| W4 Tidemark | Sea #2B7F88; plaster #EEE0C5; roof #BB694B; algae #587868; night #273847 |
| W5 Cinder Reach | Basalt #3B4248; lava #EF7145; steam #D3DDDA; orchard #5C7651; sea #356C7A |

The shared rescue kit never changes its semantic color by biome. Warning/safe/blocked states also use triangle/check/lock-cross shapes, labels and route patterns.

## 3. Exact production inventory

An inventory record is a **deliverable family**, not a file count. One family includes its editable source, exports, required LODs, colliders, textures/references, state variants and evidence. A re-export or recolor does not create another family. Different archetypes listed below do. Technical test scenes, .meta files and generated reports are not additional art inventory families.

The frozen inventory contains **487 records**: 395 visual/UI records and 92 audio records. This is a production target; existing completed count is not asserted here.

| Record class | Count | Ownership / stable IDs |
|---|---:|---|
| Equipment prefab families | 14 | ART-EQ-{equipment-id} |
| Shared infrastructure prefab families | 24 | ART-SH-INF-01…24 |
| Resident model/rig families | 4 | ART-RES-01…04 |
| World environment prefab families | 125 | ART-W1-K01…25 through ART-W5-K01…25 |
| Board layout assemblies | 10 | ART-W{1…5}-BOARD-WIDE and -COMPACT |
| Animation clips | 36 | 14 resident clips and 22 equipment clips below |
| Animator/state controllers | 16 | One for each of 14 equipment families; biped and mobility-resident controllers |
| Shared material-role records | 8 | MAT-SH-painted-metal, wood, stone, cloth, rubber, water, signal, ground |
| World palette records | 5 | PAL-W1…W5 |
| Resident outfit palette records | 6 | PAL-RES-navy, slate, cream, terracotta, olive, plum |
| 3D texture atlas sets | 8 | TEX-equipment, residents, infrastructure, W1, W2, W3, W4, W5 |
| UI atlas | 1 | TEX-UI |
| Licensed font family bundle | 1 | FONT-UI; regular and bold weights, font assets and license |
| UI/brand graphics | 78 | Detailed below |
| UI state view prefabs | 24 | UI-U01…U24 |
| Reusable UI components | 10 | UI-C01…C10 |
| VFX families | 25 | 10 shared and 3 per world |
| Audio families | 92 | Detailed in section 4 |
| **Total** | **487** | Duplicate IDs or unlisted release assets fail inventory reconciliation |

Every ID resolves to exactly one catalogue record. A future record may be planned without fabricated source/export paths; a record required by the current gate must resolve to actual accepted files. Source file sharing is allowed when explicit. An additional shipping archetype requires a versioned inventory change and updated counts; an unneeded listed asset cannot silently disappear from a full-scope completion claim.

### 3.1 Shared kit

All equipment retains the same model across worlds. Introduction ownership is:

| First world | Equipment IDs | Count |
|---|---|---:|
| W1 | barrier, bridge, pump, skiff, beacon, generator | 6 |
| W2 | firebreak, sprinkler, crawler | 3 |
| W3 | brace, winch | 2 |
| W4 | relay | 1 |
| W5 | cooler, shelter | 2 |

The cumulative equipment acceptance counts are fixed as follows. These are subsets of the full inventory, not extra records. World-specific palettes, atlases, cards, postcards, emblems and audio likewise become required with their owning world.

| Through world | Tool families/controllers/icons | Equipment clips | Equipment SFX |
|---|---:|---:|---:|
| W1 | 6 each | 11 | 11 |
| W2 | 9 each | 15 | 15 |
| W3 | 11 each | 17 | 17 |
| W4 | 12 each | 17 | 18 |
| W5 | 14 each | 22 | 20 |

Shared infrastructure IDs correspond **in order** to these names:

| IDs | Exact archetypes | Count |
|---|---|---:|
| ART-SH-INF-01…06 | depot; receiving-dock-low; receiving-dock-high; assembly-pad; road-straight; road-turn | 6 |
| ART-SH-INF-07…12 | road-junction-T; road-cross; road-ramp; bridge-socket; power-socket; power-cable-straight | 6 |
| ART-SH-INF-13…18 | power-cable-elbow; streetlight; sign-route; sign-capacity; fence-straight; fence-corner | 6 |
| ART-SH-INF-19…24 | gate; board-rim-straight; board-rim-corner; dock-connector; relief-crate; orientation-marker | 6 |

Resident families are ART-RES-01 short-coat, ART-RES-02 coveralls, ART-RES-03 long-jacket and ART-RES-04 seated mobility resident with its mobility aid. The six outfit palettes apply across silhouettes; colors do not encode ethnicity, rescue priority or ability. Mobility routes, boarding poses and vehicle capacity must agree with the actual rules; the visual variation cannot conceal an impossible route or force an unmodeled transfer.

Resident clips are idle, move, wait, board, seated, wave and celebrate for the shared biped rig, plus seven corresponding mobility-rig clips: **14 clips**. Celebration is suppressed during an active threat. Equipment clips are bridge-fold/unfold (2), skiff-idle/travel/dock (3), crawler-idle/travel/dock (3), pump-start/run/stop (3), generator-start/run/stop (3), sprinkler-sweep (1), winch-lower/lift (2), cooler-start/run/stop (3), shelter-deploy/retract (2): **22 clips**. Other equipment communicates state through its controller, material/VFX and explicitly authored moving parts without an additional clip family.

### 3.2 World kits

Each row below enumerates five IDs in order. Each world owns **25 environment families, two board assemblies, one palette, one atlas set, three VFX families, four music stems, two ambience loops and four world-specific SFX**. Shared tools, residents and infrastructure are referenced, not duplicated.

| World / IDs | Exact archetypes |
|---|---|
| W1 K01…05 | cedar-cottage-A; cedar-cottage-B; cedar-cottage-C; orchard-tree; orchard-row-base |
| W1 K06…10 | viaduct-span; viaduct-pier; reservoir-wall; spillway-gate; rescue-bus |
| W1 K11…15 | pump-house; boathouse; road-marker; retaining-wall-straight; retaining-wall-corner |
| W1 K16…20 | cedar-A; cedar-B; pine-A; pine-B; valley-rock-A |
| W1 K21…25 | valley-rock-B; terrace-ledge; medical-station; lantern-cluster; repaired-square-props |
| W2 K01…05 | forest-tree-A; forest-tree-B; forest-tree-C; stump; fallen-trunk |
| W2 K06…10 | campsite-tent; campsite-table; lookout; sawmill; hillside-water-tank |
| W2 K11…15 | hose-straight; hose-elbow; firebreak-strip; refill-station; forest-gate |
| W2 K16…20 | forest-fence; timber-stack; ranger-cabin; forest-rock; cleared-ground-patch |
| W2 K21…25 | burned-tree-shell; cooled-tree-shell; sapling-A; sapling-B; replanting-props |
| W3 K01…05 | alpine-house-A; alpine-house-B; alpine-house-C; cable-pylon; cable-cabin |
| W3 K06…10 | cable-station; observatory; brace-socket; snow-drift-A; snow-drift-B |
| W3 K11…15 | avalanche-channel; rock-shelf; shelter-roof-A; shelter-roof-B; icicle-cluster |
| W3 K16…20 | snow-pine-A; snow-pine-B; avalanche-fence; funicular-car; funicular-track |
| W3 K21…25 | weather-mast; alpine-rock; snow-path; supply-platform; restored-station-props |
| W4 K01…05 | plaster-house-A; plaster-house-B; plaster-house-C; market-stall-A; market-stall-B |
| W4 K06…10 | lighthouse; raised-dock; causeway-straight; causeway-turn; buoy-marker |
| W4 K11…15 | boatyard-crane; relay-pylon; tide-ruler; floating-pontoon; harbor-wall |
| W4 K16…20 | pier-stair; rope-post; fish-crate; sail-shed; coastal-rock |
| W4 K21…25 | sea-grass; market-awning; transfer-landing; high-causeway-support; reopened-market-props |
| W5 K01…05 | basalt-terrace-A; basalt-terrace-B; cooling-plant; thermal-pipe-straight; thermal-pipe-elbow |
| W5 K06…10 | research-building-A; research-building-B; orchard-remnant-tree; ferry-terminal; coolant-tank |
| W5 K11…15 | hardened-channel-crossing; basalt-rock-A; basalt-rock-B; heat-vent; thermal-gate |
| W5 K16…20 | research-platform; waterfront-landing; geothermal-mast; orchard-wall; coolant-crate |
| W5 K21…25 | ferry-vessel; channel-retaining-wall; hardened-crust-piece; evacuation-sign; departure-props |

W4 pier-stair is allowed only where its displayed connection is traversable under the graph rules; otherwise omit its placement, not its audit. A common shelter is ART-EQ-shelter, so W5 does not count a redundant shelter model. Neutral/threatened/recovered states use the listed shells, replacement parts, material parameters and recovery props; they are not three duplicated worlds.

Shared VFX IDs VFX-SH-01…10 are placement-ghost, placement-lock, invalid-target, route-flow, current-arrow, power-pulse, warning-marker, rescue-arrival, capacity-highlight and anchor-status. World VFX are:

| IDs | Families |
|---|---|
| VFX-W1-01…03 | flood-surface; outlet-flow; recovery-lantern |
| VFX-W2-01…03 | flame-front; bounded-smoke; wet-ground |
| VFX-W3-01…03 | snow-runout; settling-snow; frost-state |
| VFX-W4-01…03 | tide-surface; pier-foam; lighthouse-beam |
| VFX-W5-01…03 | lava-front; cooling-steam; cooling-crust |

VFX may display the simulation result but cannot modify it. Reduced effects removes decoration while retaining hazard extent, warning lead time and route status.

### 3.3 UI, textures and fonts

The **78 graphic families** are:

- BRAND-wordmark and BRAND-Q: 2.
- ICON-TOOL-{14 equipment IDs}: 14.
- ICON-STATE-safe, risk, blocked, power, water, capacity, supplies, time, pause, play, inspect, repair, save, tracking, anchor, route: 16.
- STAR-empty, earned, mastery: 3.
- EMBLEM-W1…W5: 5.
- CARD-W1-M1…CARD-W5-M5: 25, derived from the corresponding finished mission imagery.
- POSTCARD-W1…W5: 5, depicting the actual aftercare state.
- GLYPH-forward, left, right, fork, stop, pickup, dropoff, return: 8.

The 24 views implement U01–U24 from the pack without dropping recovery/help/privacy states. UI-C01…10 are card-button, toggle, stepped-slider, caption-panel, toast, confirmation-modal, capacity-meter, forecast-timeline, selection-choice and loading-progress. Their states are shared, not copied into divergent implementations.

Three shared atlas sets use 1024 × 1024 images; five world atlas sets may use 2048 × 2048 only for their shared kit. Each set contains base color, normal and packed ORM: **24 3D texture images** inside eight families. The UI atlas is 2048 × 2048 maximum. Mission cards/postcards remain separate measured output assets. No 4K/8K production texture ships. Unused maps are removed only through a documented inventory revision, not hidden imports. Mipmaps and platform compression must be validated on both target headsets.

FONT-UI contains one licensed sans-serif family, regular and bold, with readable numeric distinctions and required English glyphs. The family is selected during look development and recorded with its actual license/hash; this freeze does not invent a licensed font. Missing glyphs, clipped ascenders/descenders or substituted fallback fonts fail the UI audit.

## 4. Exact audio inventory and acceptance

There are **92 audio families and zero required voiceover assets**. Optional future narration or dialogue needs a separate inventory/provenance change. Caption-led English can complete the game and trailer.

| Group | IDs / contents | Count |
|---|---|---:|
| World music | MUS-W{1…5}-{calm, threat, peak, recovery}; four phase-aligned stems per world | 20 |
| Shared music | MUS-hub; MUS-credits | 2 |
| World ambience | AMB-W{1…5}-{base, hazard}; two loops per world | 10 |
| Shared SFX | SFX-SH-01…20 listed below | 20 |
| Equipment SFX | SFX-EQ-01…20 listed below | 20 |
| World SFX | Four per world, listed below | 20 |
| **Total** | 22 music + 10 ambience + 60 SFX | **92** |

SFX-SH-01…20, in order: select; hover; placement-lock; cancel; blocked; rotate; route-commit; pause; resume; warning-soft; warning-urgent; boarding; arrival; star-award; mission-pass; mission-fail; save-ok; save-recovery; chapter-recover; back.

SFX-EQ-01…20, in order: pump-start; pump-run; pump-stop; generator-start; generator-run; generator-stop; skiff-idle; skiff-travel; crawler-idle; crawler-travel; bridge-unfold; barrier-lock; beacon-set; firebreak-cut; sprinkler-run; brace-lock; winch-run; relay-connect; cooler-run; shelter-deploy.

| World IDs | Four owned SFX |
|---|---|
| SFX-W1-01…04 | water-rise; water-divert; gate-open; bus-safe |
| SFX-W2-01…04 | ignition; firebreak-hold; refill; regrowth |
| SFX-W3-01…04 | snow-warning; brace-strain; avalanche-release; cable-arrival |
| SFX-W4-01…04 | tide-turn; buoy-note; relay-outage; lighthouse-restore |
| SFX-W5-01…04 | heat-advance; cooling-crust; coolant-empty; ferry-depart |

Each source is 48 kHz, 24-bit PCM WAV, mono for positional SFX and stereo for music/ambience. Provider/original-author provenance and rights apply to every audio hash. World stems share tempo, bar count and loop boundary within that world: 16 bars in 4/4 at W1 80, W2 88, W3 72, W4 76 and W5 84 BPM; sample count is rounded once with ≤1-sample boundary error. Hub is 16 bars at 80 BPM; credits 16 bars at 60 BPM. Ambience loops are 60 seconds. Runtime compression/streaming settings are pinned after the actual Unity import probe.

Acceptance requires zero corrupt/truncated files, zero unintended clipping, no audible click at ten repeated loop joins, phase-aligned state crossfades, and distinct warning/confirmation/cancellation cues. Automatic sample checks flag discontinuities; a human listens to the complete loop/crossfade test and ordinary gameplay at low and normal volume. Passing a waveform check cannot certify intelligibility.

The four mixer groups are music, world effects, warnings and speech/captions. Speech remains available as a setting but has no required voiced content. Maximum concurrent voices are 24: up to 2 music stems, 2 ambience loops, 16 world/SFX voices, 2 warnings and 2 UI sounds. Warn on budget overflow; do not silently lose the only critical warning. Critical events also have persistent visual/caption equivalents. Initial gameplay mix target is −18 LUFS integrated ±2 LU over the fixed reference run, true peak ≤−1 dBTP. This is a project mixing target, not a hearing-safety certification.

## 5. Look-development, asset and render gates

Under the 16 October physical-tests-last decision, local production follows the cottage, bridge, skiff and resident test-board set with real calibration, Unity renders and machine inspection (ART-LOOKDEV). Device views and human visual signoff remain pending; local assets are not a physically/human-qualified art direction. Preserve exact sources/exports/renders and every first-mission blockout candidate for later review. The complete look gate below remains mandatory in the final phase (ART-LOOKDEV-HUMAN) before any mission/world qualification:

1. A calibration/export/import report proving scale, axes, transforms, pivots and measured bounds.
2. Unity-rendered contact sheets from the exact imported assets. Each fixed 512 × 512 cell uses one of six views: seated-wide, seated-compact, close inspection, reverse three-quarter, reduced-quality and capture camera. Four assets × six views = 24 cells; sheet dimensions 3072 × 2048.
3. A group-scene review on Quest 3 and Quest 3S in both real-surface and virtual-table modes, including normal seated and close inspection positions. This is **eight device view captures**, separate from the automated sheet.
4. Machine review with defects and inspected hashes.
5. Explicit human visual signoff for silhouette coherence, proportion, material separation, ground contact, color hierarchy and legibility. Record each criterion, not a single unexplained approval.
6. No P0/P1/P2 issue in the look-development set.

By full-game acceptance, the six-view sheet is generated for every one of the 177 prefab/board families. Each earlier gate requires only the families in its declared cumulative scope. Every required neutral/threatened/recovered state also receives a perspective render in its gameplay context; unchanged geometry may legitimately share a source, but state evidence cannot be omitted. Every world receives wide and compact whole-board sheets for neutral, worst threat and aftercare: six world-state images.

Automated asset checks require:

- Every record required by the current gate and every required file exists with matching SHA-256; source/export/prefab relationships resolve. Future records retain planned status until their owning mechanic/art gate.
- Missing textures/materials, pink shaders, null prefab references, invalid vertex data, wrong bounds, negative scales, unintended hidden meshes, unsupported source dependencies and invalid rigs: **zero**.
- Colliders match intended interaction/navigation proxies; no detailed mesh collider for a resident or moving tool. Occupied vehicles, residents, arbitrary buildings and hazards cannot gain forbidden grab behavior.
- Each visible rigid asset above 800 LOD0 triangles has three authored LODs; LOD1 ≤60% and LOD2 ≤30% of LOD0. Smaller props may use two levels. A deliberate exception requires a revised approved budget and measured device evidence; the checker cannot ignore it silently.
- Biped rig ≤24 bones; mobility rig ≤28; ≤4 skin weights per vertex. All 14 resident clips pass foot/contact, seat, boarding and bounding-box checks.
- One shared atlas/material approach per role; ≤2 visible materials on a cottage, landmark, tool or vehicle, and one on a resident. No unbounded per-instance material creation.
- Ground contacts differ from the intended support surface by ≤1 mm in the fixed render scene. Boat waterline is tested against the declared waterline, not ground.
- UVs have no unintended out-of-range coordinates, missing islands or overlaps for baked unique surfaces. Intentional mirrored/stacked islands are declared. Lightmap UVs, if used, require zero overlapping islands.
- Lower LODs preserve doors, bridge supports, occupied seats, tool direction and warning silhouette.

The fixed-render regression gate uses the same render backend, toolchain, camera, seed, lighting and time. Pixel mean absolute error must be ≤2/255 per color channel and the proportion of pixels differing by >10/255 must be ≤0.5% against the approved baseline. A changed platform/backend is a new baseline review, not an excuse to pass a comparison. These checks detect change; they do not score beauty. Intended visual changes require human review and a versioned baseline before rerun.

## 6. Geometry, rendering, audio and memory budgets

Budgets apply to **every shipped mission**, including worst threat, maximum legal actors, menus over active play, reduced effects, repeated transitions and aftercare.

| Class or metric | Frozen initial acceptance target |
|---|---|
| Cottage LOD0 | 1,000–2,500 triangles |
| Landmark LOD0 | 4,000–8,000 triangles |
| Rescue tool LOD0 | 800–2,500 triangles |
| Skiff/crawler LOD0 | 1,500–3,500 triangles |
| Resident LOD0 | 600–1,200 triangles |
| Tree/rock LOD0 | 150–800 triangles |
| World-visible triangles | ≤300,000 |
| Draw calls/batches | ≤150 in the measured game camera |
| Dynamic resident views | ≤60; logical groups may contain more people |
| Active gameplay vehicles | ≤8 |
| Realtime shadow-casting lights | ≤1 |
| Native display refresh | 72 Hz baseline; no performance claim based on reprojection |
| Application CPU p95 | ≤10 ms |
| Application GPU p95 | ≤11 ms; CPU and GPU times are not added |
| Simulation tick p95 | ≤1.5 ms at the actual 10 Hz operation scheduler |
| Steady managed allocation | 0 B/frame over the fixed 60-second active-play sample, excluding declared loading intervals |
| Missed application frames | ≤1.0% over the measured steady interval; zero unexplained consecutive missed-frame segment ≥0.5 seconds |
| Application memory | ≤1,536 MiB measured with the documented device metric |
| Return-to-hub growth | After ten load/play/unload cycles and normal 10-second settling, ≤max(32 MiB, 2% of initial settled baseline), with no monotonically increasing retained-object population |
| Full Android artifact | APK ≤1 GiB as a project budget; current distribution limits are verified separately |
| Audio voices | ≤24 under the allocation in section 4 |

No volumetric fog, runtime GI, cinematic depth of field, motion blur, camera shake, screen-space refraction or gameplay camera animation is required or enabled by default. Reducing decoration is allowed; reducing simulation truth, mandatory content or warning visibility to pass a budget is not.

Every world has a 30-minute thermal/mission-cycle soak on **each** target model: ten soaks across the full game. Additionally capture five-minute worst-case runs for each world/model/layout in both normal and reduced-effects presentation: 40 measured runs. Record build hash, OS, refresh, resolution/render scale, battery/power state, thermal state, profiling overhead and frame-time distributions. Unsupported metrics are not fabricated; their required checks remain blocked until a valid measurement path exists.

## 7. Frozen initial UX values and human validation bounds

These are initial product settings and test bounds. They are not certified ergonomic limits. A human comfort finding can require narrowing or revising them through a versioned decision and rerunning dependent acceptance.

| Setting | Default | Allowed values / behavior |
|---|---|---|
| Play preset | Standard | Standard, Relaxed, Command; no arbitrary time-scale slider |
| Operation clock | 10 Hz | Movement/boarding run on operation ticks |
| Scenario clock | Standard/Command one pulse per operation tick | Relaxed one pulse per two operation ticks, accumulator starts at zero; hazards/resources/events/deadline use scenario time |
| Pause/advance | Paused until explicit start/resume | Advance 1 or 5 operation seconds = 10 or 50 operation ticks. Relaxed advances 0.5 or 2.5 scenario seconds; UI states both clearly |
| Tutorial guidance | First-play guided | Scenario freezes only at declared tutorial gates while operation movement may continue. Unguided replay starts from the same content/seed and scenario zero with a different guidance comparison flag |
| Board class | Wide if eligible; compact offered | Two validated classes only, no continuous scale slider |
| Board height | Seated eye height minus 300 mm | Calibration range eye minus 250–450 mm; choose a safe eligible surface or fallback |
| Board center distance | 550 mm horizontal distance from seated head projection | 450–750 mm; distant targets use rays/near inspection, never require leaning |
| Near tray/control zone | 350 mm from calibrated neutral hand position | 250–450 mm; left/right mirroring; ray alternative |
| Required physical hand excursion | Comfortable calibrated near zone | No action requires >450 mm excursion from neutral hand position; within the approximate 610 mm stationary interaction envelope |
| Placement adjustment | While paused only | 10 mm translation/height steps, 5-degree rotation steps; class choice handles scale |
| Surface slope | Horizontal target | Reject >5 degrees, inadequate clearance, unstable or occluded usable footprint |
| Surface stability before confirm | 1 second | Pose variation ≤5 mm translation and ≤1 degree rotation over the observed stable interval |
| Anchor recovery | Explicit reconfirmation | Lost tracking or unconfirmed jump >15 mm / >2 degrees freezes presentation and pauses; no silent moving board during simulation |
| Hover stabilization | 120 ms | Fixed initial value; changes require device retest |
| Active-hand tracking loss | Gesture latch freezes on first lost-tracking input sample | No pending gesture can commit during grace. Full simulation pauses within 300 ms; cancel preview, retain inventory, require explicit resume |
| Hands absent in Command | Expected | Does not pause solely because no hands are tracked |
| Dwell | 800 ms | 500–1500 ms in 100 ms steps; visible progress; leaving target cancels |
| Destructive command | Separate confirmation | Restart/reset/erase require an additional distinct action; reading cannot activate |
| Text size | 100% | 100%, 125%, 150%; base body cap height target 0.7 degrees, never below 0.6 degrees at normal panel distance |
| Target proxies | Large default | 36 mm near target minimum or 2.5-degree angular target for ray/dwell, whichever is larger; optional 150% target mode |
| Captions | On | 100/125/150% size; background opacity default 90%, selectable 70/80/90/100%; no spoken-only critical information |
| Hand preference | Right, editable before play | Right/left; full right-only and left-only completion paths |
| Music / effects / warning / speech volume | 50 / 80 / 100 / 80 | 0–100 in steps of 5; muting cannot remove visual warnings |
| Reduced effects | Off | On preserves all hazard boundaries, timing, route and capacity information |
| Flashes / camera shake | Off / absent | No essential information encoded by flashing; camera shake is not implemented |
| Highlight intensity | 70% | 40–100% in steps of 10, constrained by contrast acceptance |
| Locomotion | Seated stationary | No smooth locomotion, forced rotation, vehicle-camera possession or compulsory standing |

Clearance and distance checks use the observed supported geometry, not invented MRUK confidence fields. If a setting combination does not fit, explain and offer the validated alternative. Do not squeeze the board through furniture or invent a smaller untested topology.

Persistent HUD is limited to rescued people, supplies, next significant threat and Pause. Power/water/capacity detail is contextual. Text contrast is ≥4.5:1 for body text and ≥3:1 for large text and essential nontext controls on actual composited backgrounds; panels must provide their own readable backing. These contrast checks do not replace on-device reading tests.

U01–U24 all have a safe exit/back equivalent, visible pause/recenter where applicable, focus recovery, long-text handling and loading/error/recovery states. Tested settings persist across restart. Every critical action has hands, right-only, left-only and Command implementations through the same validated command layer. Controller fallback must be complete for its supported route.

## 8. Per-layer test acceptance

| Layer | Required pass criteria |
|---|---|
| Content | All 25 mission IDs exactly once; typed objectives/graphs/events and legal layouts; inventory/capacity/budget references; no placeholder scripts or prose interpreted as rules; all compiled hashes reproducible |
| Pure simulation | Conservation, resource bounds, reservation/boarding idempotency, deterministic ordering, exact threshold equality, no-input failure, valid end states, world-specific mechanisms and canonical replay hashes |
| Unity EditMode | Imports, references, build list, content compiler, persistence migrations, asset budgets and all source/compiled linkage |
| Unity PlayMode | Boot-to-hub, mission lifecycle, all U01–U24 transitions, pooling, save/recovery, interruption, input adapters and no leaked world ownership |
| XR placement | Both layout classes, real/fallback placement, rejected surfaces, permission denial, recenter, moved furniture, anchor loss/recovery; checks use xr-placement |
| XR input | Hands/right/left/controller intent paths; zero lost-tracking commits or duplicate spending; xr-input |
| Command | Complete menu and gameplay sequences, inspect-without-activate, dwell bounds, cancel, confirmation, pause/advance; xr-command |
| XR states | Every U01–U24 state and safe recovery under each supported input; xr-states |
| Art | Inventory, import/render budgets, actual Unity images and human look-development/world review; art-shared and art-W1…art-W5 |
| Physical devices | The matrix below, with actual APK/device evidence; device-W1…device-W5 |
| Performance | Every budget and all required run/soak identities; performance-W1…performance-W5 |
| Human | Consent, observed usability/comfort, voluntary choice and human acceptance; human-W1…human-W5 |

The simulation suite includes at least 1,000 seeded legal/illegal command sequences per world, each with at least 1,000 attempted commands, checking invariants after every accepted command and tick. Seed and failure reproduction data are retained. Fuzzing proves neither universal solvability nor enjoyment.

For every mission, both A/B reference strategies pass in both layout classes and all three presets: **25 × 2 × 2 × 3 = 300 reference configurations**. The exact command timings may differ by preset; a reference is not made successful by mutating outcomes. Repeat each configuration on the host test runtime and Android IL2CPP ARM64, compare canonical states at decisive ticks, and run it twice per target for repeatability.

Each mission/layout/preset also has a no-input failure, one consequential mistake with correct causal debrief, tracking interruption and save/resume check. Mandatory/optional/mastery predicates and actual resident totals remain separate. A successful screenshot without these runs does not qualify a mission.

## 9. Hardware matrix

Target hardware is Quest 3 and Quest 3S. Access has been confirmed by the user; connection, trust, OS and successful operation remain evidence to collect. Device identities in public artifacts are pseudonymous.

The following is the minimum **physical full-mission** matrix; it is deliberately not described as every possible cross-product:

| Case | Coverage per world | Full runs across five worlds |
|---|---|---:|
| Baseline | All five missions × both devices × both layouts, Standard hands. Wide uses real surface; compact uses virtual table | 100 |
| Alternative input | M1 and M5 × both devices × right-only, left-only, Command. M1 wide/virtual; M5 compact/real | 60 |
| Relaxed | M5 × both devices × both layouts, real/virtual coverage balanced | 20 |
| Controller fallback | M1 × both devices; wide on one, compact on the other, reversed in the next world | 10 |
| Opposite placement mode | M5 × both devices × both layouts, using the placement mode opposite its baseline case | 20 |
| **Total** | **42 full-mission runs per world** | **210** |

A run with a changed APK/content/toolchain identity cannot complete the matrix for an older candidate without an explicit impact analysis and required reruns. Input adapters and presentation-only placement modes do not justify changing outcome expectations.

Separate from these full runs, every target device must cover:

- All U01–U24 states under hands, right-only, left-only and Command; controller fallback screens too.
- Usable table, no table, denied room permission, narrow surface, cluttered surface, moved furniture, changed lighting, recenter, a different room and seated near a wall.
- Indoor illumination bands of 100–300 lux and 300–700 lux, measured or explicitly recorded as unmeasured; unmeasured lighting cannot satisfy the quantitative lighting case.
- Tracking loss during hover, grab, route confirmation, release and resume; 20 injected loss/recovery trials per phase in automated integration and at least five observed physical trials per phase/device.
- Suspend/resume, headset removal, application interruption, clean launch, reinstall preserving/clearing data as explicitly selected, corrupted-current-save recovery and versioned migration.
- At least one continuous full cold-start-to-exit hands-only session on each device without paired controllers, covering placement, settings, gameplay, results, next mission, save/resume and help.

No ADB access, simulator or desktop hand replay counts as a successful physical run without the actual device execution evidence.

## 10. Human review and playability thresholds

Human participation and signoff occur when the build is ready. There is no request for approval of unwritten work here. Recruitment, consent and any recordings remain separate authorized activities; do not automatically recruit minors or contact participants.

The initial formative cohort is at least ten consenting adults: at least five aged 18–44 and five aged 45+, with varied reach/posture and at least two people testing their preferred left-hand path. Age bands are optional self-reported categories, not identity records. Accommodations and withdrawal are available. Findings are formative, not representative market evidence.

| Human gate | Minimum threshold |
|---|---|
| W1 time to first rescue | Median strictly less than 90 seconds from confirmed board placement to the first resident entering permanent safety; include briefing/tutorial/menu time. Calculate over the full ten-person first-play cohort, treating no observed rescue as an infinite duration rather than discarding it |
| W1 first rescue | ≥8 of 10 participants make a meaningful rescue within five minutes of active play without verbal coaching |
| W1 strategy comprehension | ≥8 of 10 explain one actual cause-and-effect tradeoff after play |
| W1 voluntary continuation | ≥5 of 10 freely choose another mission/attempt when explicitly allowed to stop |
| W2–W5 novel mechanic | At least five adult participants per world; ≥4 of 5 explain and use its new mechanic without verbal coaching |
| W2–W5 voluntary continuation | ≥3 of 5 choose another mission/attempt when free to stop |
| Every mission | At least two distinct humans play it, including one first-time player of that mission; at least one uncoached completion and no unresolved required-action failure |
| Comfort session | Offer 20-minute seated sessions; stopping early is always allowed and recorded, never scored as participant failure |
| Required movement | Zero required standing, unsafe leaning, reach through furniture, essential behind-head action or sustained raised-arm requirement |
| Self-reported discomfort | Median pre/post increase ≤2 on a 0–10 scale for neck, arm and visual discomfort; no unexplained individual increase ≥4 remains unresolved |
| Recurring usability issue | The same required-action confusion/failure observed in ≥2 participants must be repaired and retested |
| Visual/audio signoff | A human reviews each world's actual device imagery, aftercare, warning readability and complete audio test; every criterion explicitly accepted |

World cohorts may reuse consenting participants; human counts and individual session coverage must remain auditable. A person may stop at any time regardless of numeric scores. A stop attributed to a required interaction/comfort defect blocks that path until corrected and retested. The scales are project research thresholds, not clinical measures or a promise of no motion sickness.

Participants are not coached to produce passing numbers, pressured to continue, or replaced in the denominator because they struggled. Record exclusions and reasons before calculation. AI cannot fill missing questionnaires or act as a human participant.

## 11. All-world completion and severity

For each W1…W5, completion requires: its 25 environment families and other owned inventory; its distinct working mechanic and tutorial; five finished missions; 60 passing reference configurations; the mission negative/interruption/save cases; its 42 device runs; performance runs and both 30-minute soaks; human gate; final audio; aftercare; clean unload; truthful release evidence. Shared art, all U01–U24 states, input and placement gates are prerequisites.

The full-game gate requires **W1-M1…W1-M5, W2-M1…W2-M5, W3-M1…W3-M5, W4-M1…W4-M5 and W5-M1…W5-M5** with no substitutions. Counted assets, source imports, planned missions and generated videos are not playable completion.

| Severity | Examples | Frozen decision |
|---|---|---|
| P0 | Crash/data loss, unsafe required movement, secret disclosure, cannot install/launch | Stop release and affected dependent work; preserve evidence |
| P1 | Softlock, incorrect rescue/score, lost-tracking commit, broken required input, corrupted progression, a required mission/feature absent | Fix and requalify; no full-game acceptance |
| P2 | Noticeable visual/audio defect, misleading optional objective, intermittent noncritical UI fault, mismatch with approved art direction | Fix before full candidate acceptance |
| P3 | Minor polish with no functional, readability, comfort or communication impact | May remain in a reviewed known-issues list with owner and scope |

Full candidate acceptance requires **zero open P0, P1 or P2 issues**. No severity downgrade may be used to hide a failed criterion. P3 items cannot contradict any required check. A separately chosen competition candidate contains **all five W1 missions** and passes all applicable W1/shared gates with zero open P0/P1/P2; the pack's three-mission contingency is not adopted. That candidate never marks the full 25-mission scope complete.

A failed gate produces a precise blocker and impact list. Three unsuccessful code repair attempts on the same issue end that repair loop; asset repair is limited to two repairs after its initial attempt. Continue independent authorized work. A new criterion or budget requires a versioned decision, updated dependent checks and truthful status, not an edited pass report.

