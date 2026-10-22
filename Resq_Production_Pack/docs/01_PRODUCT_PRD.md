# 01 — Product requirements document

## 1. Product definition

**Resq is a tactile rescue-strategy game where a living miniature world appears in front of your chair, and your interventions change whether its inhabitants reach safety.**

You are an operator at a fictional rescue organization, not an all-powerful giant. You may deploy equipment, redirect environmental hazards, open routes, allocate power, and dispatch vehicles. You cannot pick up people, erase hazards, or move arbitrary buildings. The player's freedom comes from combining understandable systems.

The emotional arc is: notice a small problem, make a useful intervention, see lives improve, encounter a complication, improvise, and watch the town recover. The tone is warm, ingenious, and occasionally tense. It is not a disaster-suffering simulator.

### The signature moment

A river is about to cut off a village bus. The player pinches a flood barrier from the equipment tray and places it at a marked embankment. A translucent forecast shows where the diverted water will go. On release, the barrier locks with a satisfying mechanical sound. Water redirects, a new route becomes dry, the bus moves, and tiny passengers wave as they arrive safely. One action visibly changes several systems.

The complete interaction must still be understandable without narration, a cutscene, generative dialogue, or explanatory slides.

## 2. Audience and use cases

Primary design audience: teenagers and adults who enjoy spatial puzzles, approachable strategy, miniature worlds, and short sessions. Target positioning is 13+ subject to the actual platform rating process, not a claimed rating. The game should avoid graphic injury, realistic distress audio, gambling, public chat, and manipulative retention mechanics.

A player might have ten minutes after work, limited room to move, a preference for seated play, or friends watching a cast of the headset. Another may need one-handed controls or a low-speed command interface. The primary experience must not require standing, crouching, floor interaction, large reach, eye tracking, or constant arm elevation.

### Jobs to be done

- “Give me a complete, satisfying challenge without rearranging my room.”
- “Let me understand what went wrong and improve my plan.”
- “Make me care about a place without asking me to maintain it every day.”
- “Let me use VR's depth and hands for something more interesting than pressing floating buttons.”

## 3. Design pillars and anti-slop rules

**Readable causality:** hazards announce themselves; previews explain effects; outcomes follow rules. A failure must be explainable in one sentence.

**Tactile purpose:** each gesture represents a game action. No decorative gestures, arbitrary hand spells, or fiddly interactions that are easier as buttons without adding value.

**Rescue first:** points, scenery, and unlocks support helping inhabitants. The most valuable behavior must not be allowing damage to farm rewards.

**Authored variety:** worlds change the problem structure, not just lighting or textures. Every mission has a central decision and at least two validated approaches where specified.

**Bounded spectacle:** art direction, sound, timing, and animation create richness within mobile performance budgets.

Do not add an AI chat companion, generated missions at runtime, random filler islands, currency shops, a crafting tree, multiplayer, open-world walking, dozens of currencies, or a fake social leaderboard. A tool is not a feature simply because it can be integrated.

## 4. Scope and releases

### Competition build: a finished chapter, not a menu of promises

World 1, **Alder Valley**, contains five playable missions. Mission 1 teaches the controls through a real rescue. The build includes two weather/layout modifiers, three difficulty/input presets, coherent scoring, a results screen, saved progress, pause/resume, a return visit to the rescued town, help, credits, and reliable placement recovery.

Other worlds are excluded from the playable competition menu. A roadmap can exist on the public site only when clearly labeled planned. No inactive buttons, “coming soon” gameplay destinations, or cinematic footage presented as implemented content.

If scope threatens quality, reduce the competition chapter to its three strongest finished missions and update every content claim. This is a contingency, not permission to ship five broken missions. Do not silently reduce scope; record the decision and actual release manifest.

### Full release: five worlds, 25 missions

1. Alder Valley: water routing and ground evacuation.
2. Emberwood: firebreaks, moisture, wind, and transport staging.
3. Frostline: unstable snow, structural supports, and cable rescue.
4. Tidemark: timed tides, electrical networks, and multi-leg transport.
5. Cinder Reach: slower volcanic hazards, cooling, shelter, and combined-system mastery.

Full-release implementation proceeds only after the first chapter meets its interaction, fun, and performance gates. Data schemas support all worlds from the beginning; meshes, mechanics, and missions do not all need to be built at once.

### Explicit non-goals

No PC dependency for players. No mandatory internet or account. No generative AI during gameplay. No cloud simulation. No payments inside the competition APK. No physically accurate fluid/structural simulator. No roomscale locomotion. No online multiplayer, public user-generated content, or cloud leaderboards at launch. No promise of compatibility with unreleased hardware without qualification.

## 5. Modes

**Story Operations:** standard 6–9 minute missions with authored threats, pause, forecasting, optional objectives, and chapter progression.

**Command Mode:** the same rescue systems with discrete selection, explicit confirmations, and pause/advance controls. Designed for head-directed/dwell input or slower hand input. Scores are recorded separately because pacing differs. Entire missions must remain completable without speaking.

**Relaxed Operations:** slower hazard progression, earlier warnings, unlimited planning, and checkpoint retry. No leaderboard comparison with Standard. Relaxed play earns normal story access.

**Challenge Operations:** fixed authored modifiers such as fewer supplies or an earlier surge; unlocked after completing the base mission. Not procedurally generated difficulty without validation.

**Aftercare:** revisit a rescued board with hazards disabled. Watch repairs, inspect rescue postcards and small character moments, rotate the board, and take an in-engine image. No chores, passive income, or punishment for absence.

Controller input is an alternate adapter for the same commands, never an exclusive feature path.

## 6. Session and progression

Cold start goes directly to the last safe hub state. First launch asks for the minimum spatial permissions at the point of use and offers a fallback. A returning player sees Continue Operation, Choose Mission, and Settings. First launch replaces Continue with Begin Rescue.

First session establishes safe placement and a tested layout before briefing. Mission order: brief → choose loadout → reconfirm the board if needed → inspect forecast → start → rescue and stabilize → outcome → debrief → optional replay/next/aftercare. Relocating later must preserve or explicitly reselect a compatible tested layout; it must not silently change the active mission.

Unlock the next mission by meeting the mission's minimum rescue target and mandatory objectives. Do not require three stars to advance. Completing a world unlocks the next world in builds where that world is genuinely installed and released.

Tools unlock through authored introductions. A fixed default loadout makes every mission solvable without grinding. Players may later select permitted alternate tools within the supply budget. No permanent stat upgrade should invalidate early mission balance.

### Rewards

Stars, rescue postcards, chapter restoration details, tool skins with no competitive advantage, and developer-authored challenge modifiers. Each unique person can count toward a mission result only once. Replaying a mission never duplicates a one-time reward transaction.

## 7. Functional requirements

| ID | Requirement | Priority | Acceptance evidence |
|---|---|---|---|
| P-001 | End-to-end hand-only flow | Required | First launch to mission completion, restart, settings, exit without controllers |
| P-002 | Fully seated operation | Required | All mandatory actions reachable through near or ray interaction |
| P-003 | Real strategic rescue loop | Required | At least two different successful plans in W1M2 and W1M5 |
| P-004 | Forecast previews | Required | Water destination, path validity, and supply cost visible before commit |
| P-005 | Reliable pause/resume | Required | Every runtime subsystem freezes; save resumes without extra damage |
| P-006 | Local progress | Required | Abrupt termination cannot corrupt the last valid save |
| P-007 | Placement fallback | Required | No surface permission or room scan still permits a complete mission |
| P-008 | Hand tracking recovery | Required | Missing hands never drop/activate a destructive command |
| P-009 | Narrative through behavior | Required | Rescue acknowledgments and recovery moments are present in game |
| P-010 | Distinct world systems | Full release | Each new world introduces a mechanically consequential rule |
| P-011 | Complete Command mode | Competition target; required before claiming hands-free support | Head-directed flow covers every gameplay and menu action |
| P-012 | No network dependency | Required | All competition missions complete in airplane mode after install |
| P-013 | Honest marketing | Required | Every playable-world and feature claim matches release_manifest.json |
| P-014 | Versioned content | Required | Replays and saves identify build, mission, and rules versions |

## 8. Product acceptance and research

A prototype passes the **interaction gate** when a new player can deploy equipment, cancel a mistake, and understand a safe/unsafe placement without coaching. Target median completion of the first meaningful rescue: below 90 seconds after board placement.

A prototype passes the **gameplay gate** when players can explain their important decision, can identify a different strategy for a replay, and voluntarily try another operation. Proposed internal signal: at least half of an initial ten-person playtest choose another attempt when free to leave. This is a decision heuristic, not an industry benchmark or statistical proof.

A release passes the **quality gate** only with verified device evidence. Automated bots can prove rules and some interface transitions; they cannot certify emotional appeal, hand comfort, nausea, audio clarity, or perceived polish.

Collect qualitative stop reasons separately: did not understand, could not select, tired, bored, unfair outcome, too tense, or session naturally completed. Do not reduce all feedback to “retention.”

## 9. Commercial and lifecycle assumptions

Proposed business model: premium game plus substantial later world expansions. Final price, release date, store art, rating, privacy statement, and localization claims require release approval. No advertising, subscriptions, loot boxes, paid rescue advantages, or forced daily streaks.

English is the competition baseline. Architect text and layout for localization, including future Arabic, but do not claim Arabic shipping until fonts, shaping, bidirectional text, line breaks, and all screens are tested. Content IDs and commands remain language-independent.

## 10. Definition of a complete product

A polished toy that people do not want to replay is not complete. A strategic prototype with unreliable controls is not complete. A beautiful trailer for unimplemented scenes is not complete.

Completion means the installed game delivers the promised chapter independently, teaches itself, tolerates real-world interruptions, preserves progress, and leaves the player wanting to make a better rescue plan.
