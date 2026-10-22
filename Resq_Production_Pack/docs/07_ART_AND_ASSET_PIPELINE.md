# 07 — Art bible, asset inventory, and Higgsfield → Blender → Unity pipeline

## 1. Visual identity

The direction is **a lovingly built, tactile miniature rescue world**, not photorealistic disaster footage, low-effort geometric blocks, or a toy-shop collection of mismatched downloaded assets.

Shapes have deliberate proportions, readable bevels, convincing contact, and modest material variation. Rooflines and terrain silhouettes carry character. Small people are expressive through posture and timing rather than detailed faces that will not be visible in a headset.

The board resembles a finely crafted physical model with cutaway geology at its edge. In MR it contacts the real surface cleanly. Rivers, bridges, docks, and hazards are readable from the normal seated viewpoint, not only in a cinematic screenshot.

### Visual hierarchy

Rescue equipment is the highest-contrast interactive layer. Residents and evacuation paths are second. Hazards are clear but do not overwhelm targets. Architecture supports navigation. Tiny decorative details are last and may disappear at lower quality settings.

Reserve strong rescue orange `#FF6B35` for equipment, important actions, and brand accents. Use deep evergreen `#132B2A` for interface panels and wordmark backgrounds; warm paper `#F3EBDD` for mission cards; pale mineral `#D9E4DB` for secondary surfaces. Warning and blocked states have distinct icons/patterns, not only hue.

World palettes are specified in the world bible. Do not recolor the shared rescue kit per biome; its consistent appearance teaches interactivity.

## 2. Shape and material language

Buildings: asymmetrical but coherent roof assemblies, slightly exaggerated eaves, clean window recesses, broad material changes, and believable supports. Doors align to navigation nodes. No floating chimneys, decorative stairs that imply an unusable route, or windows accidentally textured over doors.

Equipment: compact industrial forms with broad grips, visible hinges, labeled operating states, and a single recognizable silhouette. A pump shows intake and outlet; a generator shows power state; a boat visibly has seats/capacity.

Residents: stylized original proportions, readable color grouping, two or three clothing silhouettes, inclusive variation without stereotypes. Idle, walk, wait, board, seated, wave, and celebrate animations. Avoid jumping celebration during danger or cheerful idle behavior while a warning claims panic.

Materials: matte painted metal, wood, stone, cloth, and water. Roughness and value vary by material. Do not apply the same glossy plastic finish to everything. Keep procedural Blender shaders as editable sources but bake/export compatible material data for Unity.

Effects: purposeful water edges, a short placement sparkle, clear current arrows, heat shimmer only where affordable, limited smoke, snow paths aligned to runout geometry. No decorative glow around every object.

## 3. Asset budgets: starting targets

| Asset class | LOD0 target | Textures/materials | Requirements |
|---|---:|---|---|
| Shared cottage | 1,000–2,500 triangles | Shared atlas; 1–2 materials | Door/navigation alignment; separate damaged/recovered state where needed |
| Landmark | 4,000–8,000 triangles | ≤2 materials, typically 1k/2k atlas | Recognizable silhouette; strong lower LOD |
| Rescue tool | 800–2,500 triangles | Shared equipment atlas; 1–2 materials | Correct pivot, grab proxy, state indicators |
| Skiff/crawler | 1,500–3,500 triangles | Shared kit atlas | Seats/boarding points; no collision traps |
| Resident | 600–1,200 triangles | Shared atlas | Low bone count; simple clips; clean scale |
| Tree/rock | 150–800 triangles | Vertex color or atlas | Instancing-compatible; compact silhouette |
| Terrain tile | Budget by visible world total | Shared terrain material | No holes, grounded edges, logical hazard overlay alignment |

Budgets are not arbitrary quality scores. An asset may exceed one target with a measured justification while the complete scene remains within device budgets. Automatic decimation must not destroy silhouette, UVs, hands, seats, or supports.

Default textures: 512–1024 pixels for shared small assets; 2048 only for justified atlases/landmarks. Use explicit compression/import settings, mipmaps, and color-space handling. Do not ship 4k/8k source textures merely because generation produced them.

## 4. Shared asset inventory

**Core equipment:** six Alder tools plus firebreak marker, sprinkler, crawler, brace, winch, relay, cooler, and shelter for later worlds.

**Shared infrastructure:** depot, receiving dock, assembly pad, road pieces, bridge sockets, power sockets/cables, streetlights, signs, fences, terrain edges, wave/current markers, vehicle path indicators.

**People:** at least three base bodies/clothing silhouettes, six restrained color variants, one mobility variation supported by route rules, and seven shared animation clips. Visual variety does not require dozens of unique rigs.

**UI:** custom vector wordmark, a loop/path Q symbol, tool icons, state icons, three star states, mission cards, pause controls, route arrows, capacity indicators, caption panels, and progress postcards. No real rescue-service marks or sponsor logos embedded as scenery.

## 5. World kits

Alder: cedar cottage variants, orchard rows, stone viaduct, reservoir spillway, bus, water gate, terraced ground, water pump house, tiny boathouse, road markers, rock walls, cedar/pine variants, lantern recovery props.

Emberwood: forest trees with unburned/burning/cooled presentation states, campsite, lookout, sawmill, tank, hose segments, firebreak ground strips, refill station, fence/gate variants, replanted saplings.

Frostline: alpine houses, cable pylons/cabins, station, observatory, brace sockets, snow drifts, avalanche channels, rock shelves, shelter roof variants, icicles used sparingly.

Tidemark: plaster houses, market stalls, lighthouse, raised docks, causeways, buoy markers, boatyard crane, relay pylons, tide ruler, floating pontoons.

Cinder: basalt terrain, cooling plant, thermal pipes, research buildings, orchard remnants, ferry terminal, coolant equipment, temporary shelter, hardened channel crossings, stylized steam.

Each kit has neutral, threatened, and recovered variants where gameplay needs them. Do not generate three entire duplicate worlds for those states; use controlled material/prop variations and authored replacement parts.

## 6. Look-development gate

Before mass generation, finish one cottage, one bridge, one skiff, and one resident in a small Unity test board. Verify them under MR lighting, virtual-table lighting, normal seated view, close inspection, low-quality mode, and capture camera.

Compare scale, material roughness, shadow density, silhouette, and color hierarchy. Render a fixed contact sheet in Unity. If the set does not look like one game, repair the style source before generating fifty more assets.

Codex may compare images and identify problems, but do not label an aesthetic gate “human approved” without an actual review. Maintain machine-review and human-review states separately.

## 7. Tool responsibilities

**Higgsfield:** concept exploration, reference sheets, controlled 3D scene/asset work where its actual tools support the required outputs, and optional marketing graphic development. Current 3D Jutsu documentation includes GLB export; connected schemas also expose editable Blender-source retrieval. [S11, S12]

**Blender:** editable source of truth, procedural original geometry where appropriate, cleanup, retopology/decimation with checks, pivots, transforms, rigging, animation cleanup, UVs, baking, colliders, LODs, and standardized exports.

**Unity:** final in-game material look, prefab integration, physics proxies, interaction targets, audio/VFX states, profiling, screenshots, and real gameplay capture.

A generated image is not a 3D mesh. A GLB is not automatically a production-ready game asset. A Blender render is not proof of the Unity result.

## 8. Automated pipeline

1. Read the asset brief and style lock. Resolve an existing source asset before creating a duplicate.
2. Discover actual Higgsfield/Blender tools and supported formats. Do not invent model IDs, parameters, project IDs, or endpoints.
3. Create or reuse an authorized destination and record its real project/revision IDs. Respect operation locks and reconcile uncertain paid requests before retrying.
4. Generate the bounded concept/asset request within the owner's approved credit budget. Record prompt, references, provider, license evidence, output hash, and revision.
5. Retrieve actual committed GLB and editable source where available. Preserve the source. Never treat an expiring URL as the long-term artifact.
6. Open in a safe Blender process. Disable untrusted auto-execution. Inspect object count, bounds, units, transforms, materials, textures, topology, rig, clips, and missing dependencies.
7. Normalize scale/orientation using a tested coordinate conversion. Blender and Unity axes differ; verify one calibration asset rather than applying memorized rotations everywhere.
8. Create appropriate colliders and LODs, optimize materials, bake compatible textures, check clipping and normals, and export a versioned asset.
9. Import through Unity Editor tooling with fixed settings. Create prefabs and explicit references. Never rely on a local `.blend` auto-import dependency in CI.
10. Render a Unity contact sheet and run asset audits. Reject or repair failures; never silently swap in a generic primitive as a finished asset.
11. Add the approved asset to the manifest and capture a before/after diff. Invalidate dependent scenes only when needed.

Permit at most one active mutation per remote 3D project and one writer per local `.blend` file. Keep stable paid request IDs for retries with identical inputs. If a provider call times out, check its status; do not repeatedly spend credits creating replacement jobs.

## 9. Asset rejection checklist

Reject an asset for missing rights/provenance, corrupted files, unbounded scale, missing textures, broken UVs, wrong axes/pivot, impossible support/contact, unreadable silhouette, mismatched style, excessive material count, dangerous colliders, hidden high-poly contents, rig errors, or out-of-budget on-device cost.

A repair loop is bounded: up to two automatic repairs after the initial attempt. Further work becomes a recorded blocker or an original Blender rebuild, not endless regeneration.

## 10. Prompt templates for Codex's asset worker

### World concept sheet

“Create a production concept sheet for RESQ, an original seated mixed-reality miniature rescue strategy game. World: {world_name}. Mechanical focus: {mechanic}. Show a coherent modular environment from a seated three-quarter tabletop view plus clear component studies. Use the locked palette and matte handcrafted materials. Preserve readable rescue routes, broad silhouettes, and a plausible buildable kit. No people in danger shown graphically, no brands, no UI text, no photorealistic disaster, no imitation of an identifiable game or artist. This is a concept reference, not gameplay footage.”

### Single game asset

“Create one original {asset_name} for the locked RESQ miniature-world kit. Target physical dimensions: {dimensions}; operating parts: {parts}; required pivot: {pivot}; visible interaction affordance: {affordance}. Match the approved reference set, material roles, proportions, and rescue-orange accent discipline. Keep separate moving parts named clearly. Avoid decorative geometry that exceeds the supplied budget. Deliver editable geometry when supported, not just an image. Report what format and structure were actually produced.”

### Blender cleanup task

“Inspect this specific imported asset before editing. Preserve a source copy. Normalize units, transform orientation through the project calibration convention, correct normals and topology, retain the locked silhouette, create documented LODs and simple colliders, bake supported materials, and export using the project preset. Run the asset validator and produce a Unity-rendered contact sheet. Report actual triangle/material/texture statistics and every unresolved issue.”

## 11. Audio asset direction

Music: restrained, warm, original instrumental score with calm/problem/resolve stems. Avoid constant heroic orchestration or childish nursery sound. Loops need clean transitions and appropriate licensing.

Effects: distinct placement lock, cancellation, blocked target, boat engine, boarding, arrival, warning, water diversion, pump start/stop, power restoration, firebreak, winch motion, cooling, and chapter recovery. Critical signals remain distinguishable at low volume.

Voice is optional. An English caption-first competition build can be complete without spoken dialogue. Do not hold the entire production hostage to a TTS integration, and do not clone a real person's voice without permission.
