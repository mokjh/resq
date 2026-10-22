# 04 — Spatial UX, interaction, and accessibility

## 1. Spatial layout

Default board is approximately 0.75 m wide × 0.50 m deep, placed comfortably in front of a seated player. Scale and height are user-adjustable within tested limits. These are initial layout values, not universal ergonomic guarantees.

The board should sit below eye level, not require a sustained downward neck bend, and never force leaning toward the floor. Essential UI stays in a central, narrow field of view. A compact mode moves controls closer and uses remote selection for far objects. Test differing seated heights and postures rather than assuming one chair.

Physical reach and visual reach are different. A board may be visible beyond comfortable arm reach; hand rays, large target proxies, and a near-field inspection panel make distant actions accessible. Every required action works within the competition's approximate two-foot stationary interaction envelope. [S01]

The equipment tray is fixed in a comfortable near zone, not permanently attached to a raised wrist. Left/right preference mirrors the tray and persistent menu control. No mission requires two hands at once.

## 2. Placement and meaningful mixed reality

Startup offers **Use a real surface** and **Use a virtual rescue table**. Request scene access only when the real-surface route needs it. Never require raw passthrough camera capture. MRUK supplies scene/room information and fallback support; use the current supported integration rather than guessed APIs. [S07]

When a valid table is detected, fit one of a small set of validated board layouts to its safe footprint. Surface shape selects compact/wide layout classes and reachable docking edges. A real table edge becomes the visual sea outlet and a receiving dock can be anchored at its safe near edge. The player chooses a reachable arrangement before play starts.

This is purposeful spatial design: the usable surface determines the arrangement of staging and receiving points. It is not permission to generate arbitrary untested puzzle topology from a room scan. The class and chosen dock configuration enter the run's layout signature and score partition.

Reject tiny, sloping, occluded, unstable, or too-distant surfaces. Offer an explicit fallback without an error loop. The virtual table preserves the game's mechanics but uses a fixed tested layout. Explain the placement mode honestly; do not claim all room-dependent functionality remains identical in fallback.

During Active, the board's logical shape is locked. Repositioning pauses simulation, removes held previews, changes only presentation transforms, checks clearance, and resumes after confirmation. Losing an anchor pauses and offers relocalize or fallback. Never let the board drift while residents continue failing.

Do not put hazards over the player's body, ask them to reach through furniture, decorate the entire room with misleading interactive targets, or make an actual wall necessary to complete a mission.

## 3. Interaction grammar

| Intent | Hand-first implementation | Head/dwell implementation |
|---|---|---|
| Select equipment | Pinch or poke a large tool card | Target card, dwell, confirm |
| Place equipment | Ghost follows near grab or hand ray; release on legal socket | Select legal socket, then explicit Place |
| Rotate equipment | Large rotate handle or 90-degree step buttons | Select Rotate Left/Right buttons |
| Cancel | Return to tray or visible Cancel | Persistent Cancel target |
| Route a vehicle | Select vehicle → pickup → destination → confirm | Same discrete sequence |
| Inspect risk | Hover/select hazard icon | Dwell into inspection, not activation |
| Pause | Fixed menu target; platform interruption also pauses | Persistent Pause target |
| Transform board | Grab dedicated rim handle in paused placement | Nudge/rotate/scale controls |
| Resume | Explicit Resume; no accidental pinch continuation | Explicit Resume with countdown |

Hand interaction states: idle, hover, selected, preview, valid preview, invalid preview, committed, canceled, and tracking-lost. Every state has visual and sound feedback where helpful. An invalid action displays a specific cause: “Needs dry power,” “Dock is full,” “Route closes at high tide,” or “Not enough supplies.”

Default intent stabilization target: roughly 100–150 ms for hover changes, tuned on-device. Snap volumes should be larger than visible miniature handles. Grabbing uses generous targets, but ambiguous overlaps open a short selection choice rather than guessing.

No hand tracking haptics are assumed. Use contact animation, a short click, object settling, and a subtle highlight to communicate commitment. Do not add screen shake.

## 4. Tracking interruption rules

If the currently interacting hand is lost, freeze the interaction immediately. A temporary loss does not release or commit a held object. After a brief grace interval, cancel the preview and pause the simulation with a calm message. If both hands are absent in Standard, pause; if the player intentionally selected head-only mode, absence of hands is normal.

When tracking returns, require explicit confirmation before an in-progress command resumes. Test the difference between an intentional pinch release and a missing-hand pose. Do not infer release from a null tracking frame.

## 5. Hands-free scope

Head-directed targeting is not eye tracking. Quest 3/3S design must not depend on eye position. The center indicator is unobtrusive, appears only when useful, and supports target snapping without moving the camera.

Default dwell target: 800 ms, adjustable approximately 500–1500 ms. Destructive/reset actions use a separate confirmation. Reading a label cannot spend resources. Important inspection panels have non-activating regions.

Command mode converts dragging into select-source/select-destination and complex positioning into legal sockets plus orientation steps. It may be slower, so the game allows complete pause and advance controls. Speech is unnecessary. Platform-level login/setup may still require the platform's own supported input; do not advertise control over OS behavior.

## 6. Screen/state inventory

| ID | State | Main content | Primary exit and recovery |
|---|---|---|---|
| U01 | Boot | Small wordmark, loading feedback, saved-data validation | Hub; recover save if needed |
| U02 | First welcome | One-sentence premise, comfort mode, Begin | Placement |
| U03 | Permission explanation | Why surface data is useful; no raw image collection | OS permission or virtual table |
| U04 | Surface selection | Eligible surfaces, clearance, scale preview | Confirm or fallback |
| U05 | Placement calibration | Height, distance, scale, handedness | Confirm or reset |
| U06 | Rescue hub | Continue, missions, settings | Mission or resumed run |
| U07 | World/mission selection | Only implemented released missions, progress, objectives | Briefing |
| U08 | Briefing | Who needs help, expected threat, mission goals | Loadout/back |
| U09 | Loadout | Legal tools, budget, fixed default kit, reset | Planning |
| U10 | Planning | Forecast, queued actions, safe routes | Start/resume |
| U11 | Active HUD | Rescue count, supplies, next threat, pause | Inspect/equipment/outcome |
| U12 | Equipment detail | Cost, power/stock, purpose, valid sockets | Place/cancel |
| U13 | Route planner | Pickup, destination, capacity, expected closure | Confirm/cancel |
| U14 | Warning inspection | Cause, time window, one actionable option | Return/act |
| U15 | Pause | Resume, restart, comfort, return to hub | Safe transition |
| U16 | Tracking recovery | Tracking status, cancel preview, resume | Reacquire/fallback |
| U17 | Anchor recovery | Relocalize or virtual table | Recalibrate while paused |
| U18 | Results | Rescued/total, stars, score, objectives | Debrief/replay/next |
| U19 | Causal debrief | Event timeline, one specific lesson | Replay/aftercare |
| U20 | Aftercare | Repaired world, postcard, image capture | Hub |
| U21 | Settings | Audio, text, input, comfort, effects, reset | Persist/back |
| U22 | Help | Interactive gesture practice and tool rules | Resume/hub |
| U23 | Credits/privacy | Real credits, tool/asset attribution, local data information | Back |
| U24 | Save recovery | Valid backup choices; no forced erase | Recover/start new with consent |

Every state supports Back or an equivalent safe exit. No screen traps. Recenter and pause are never hidden behind a tutorial requirement.

## 7. Information design

Keep four persistent HUD elements: people rescued, supplies, next significant threat, and pause. Show power, water, capacity, and tool detail contextually. Avoid multiple floating charts surrounding the player.

Use broad opaque or near-opaque panels with strong contrast over unpredictable passthrough. Important text should be readable at normal viewing distance; define sizes by visual angle and test on Quest 3S, not merely desktop pixels. Body target begins around 0.6–0.8 degrees cap height, adjustable through text scaling. Do not treat these initial values as accessibility certification.

Color reinforces meaning but icons, patterns, labels, and route shapes carry the same information. Good/risky/blocked states use a check, warning triangle, and lock/cross. Resident counts remain legible in bright environments and muted effects mode.

## 8. Audio and comfort settings

Separate music, effects, warnings, and speech volumes. All spoken information has captions. Caption size and background opacity are adjustable. A subtitle label identifies the speaking role without a portrait dominating the board.

Offer reduced particle effects, disabled flashes, no camera shake, dimmer highlights, simplified backgrounds, left/right hand preference, one-hand operation, large targets, slower gameplay, and head/dwell controls. Disabling decorative effects must not hide hazards.

No smooth virtual locomotion, forced camera rotation, vehicle-camera possession, or unskippable cinematic that moves the viewpoint. Cinematic capture cameras are for marketing tools, not the player's gameplay camera.

## 9. Copy examples

Welcome: “A little world needs a steady hand.”

Permission explanation: “Use your room's surfaces to place the rescue board. You can play on a virtual table instead.”

First objective: “Get the bus to high ground.”

Tracking pause: “Hands out of view. Your rescue is paused.”

Blocked action: “This pump needs one unit of power.”

Aftercare: “Everyone you rescued is home. Take a look around.”

Failure: “Backup crews took over. See what changed, then try another plan.”
