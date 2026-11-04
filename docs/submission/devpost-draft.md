# Devpost entry: draft for the user's review

Status: a draft written 28 Oct 2026, updated 4 Nov, from what is verified in the Editor and Meta XR Simulator.

- **Markers:** `[P5]` marks a claim that holds only once the device phase (12–15 Nov) confirms it; if it doesn't, the claim is cut. `[you]` marks what only the user can provide.
- **Counts:** `python3 tools/web/count_words.py docs/submission/devpost-draft.md` checks the tagline and story limits.
- **Submission:** the user submits the form and makes the attestations.

## Form fields

- **Project name:** RESQ
- **Track / division:** Gaming, New Experience
- **Tagline (≤ 140 characters):** A hands-first rescue strategy game on your real table: read the flood, fire or lava, place one tool, and bring every resident home.
- **Target launch date:** `[you]` Suggestion: Spring 2027.
- **Video:** `[you]` the public YouTube link, after the user approves the cut.
- **Build:** the APK in the "Competition" release channel plus its invite URL `[you]`.

## Story (≤ 500 words)

<!-- story:start -->
**Inspiration**

We wanted a seated mixed reality game with real agency in your hands: not a passive diorama, not a workout. A miniature rescue fit: you see the whole problem at once, and one careful move visibly changes what happens to the people inside it.

**What it does**

RESQ places a small valley on your real table. A flood, a forest fire, an avalanche slope, a rising tide or a lava flow is coming, and residents need a way out. You pinch tools from a tray, such as a barrier, bridge, pump, skiff, firebreak, brace or coolant, and place them on legal spots. Before you commit, a forecast runs the real simulation ahead and shows which cells stay dry and which newly flood. Then start the clock and watch.

There are five worlds and 25 missions, each with two very different winning plans, three rule presets (Standard, Relaxed and Command) and 30 named challenges. A debrief explains the cause and effect behind every run, and in aftercare you watch the valley recover: the water drains, the hazards fade and everyone home cheers.

**Hands first, and more than one way to play**

Everything works with hands: poke and pinch for the tray, ray and pinch for distant spots `[P5: pinch]`. One-hand play mirrors the controls for either hand. Command mode turns it into a turn-based puzzle: a head-gaze dwell selects, and +1 s / +5 s step controls advance time, so the whole game also works without fine finger movement. Text scales to 150 %, with high-contrast and reduced-effects modes and an Open play option that unlocks every mission.

**Your room matters**

RESQ uses the Mixed Reality Utility Kit to find a table that fits. A long table gets the wide layout and a small one the compact layout, which changes the mission. With no table, it builds a virtual one. Your valley is saved as a spatial anchor, so it is still there next time, and your real hands cast soft shadows on it, so you can judge height before you touch. `[P5]`

**How we built it**

Unity 6 with URP, the Meta XR Core and Interaction SDKs (v207) and MRUK. The rules run in a deterministic, integer-only simulation, so physics or frame rate never decide an outcome. A mission factory compiles each map from a recipe and contract. A headless validator proves that doing nothing fails and that both reference plans win on both layouts in every preset: 450 of 450 runs pass, plus 120 of 120 challenge runs. Tests also keep must-see UI within 30° of a seated player's gaze. The art is original, from scripted Blender kits on one palette; the music and sound are synthesised from code.

**Challenges**

An honest, fast forecast: it runs the real simulation ahead on a worker thread instead of drawing a guess. Five rule sets in one engine, with presentation never touching outcomes. And building it all on an 8 GB laptop.

**What's next**

Playtests with first-time players `[P5]`, more challenge variants, localisation, and a full release `[you: target date]`.
<!-- story:end -->

## Judge start guide (for the entry's "how to test" field)

1. Install from the invite URL on Quest 3 or 3S `[P5: confirm both]`. Sit at a table if one is available; any clear surface or none works.
2. Allow spatial data when asked; it finds your table. If you decline, you get a virtual table.
3. On the welcome card, choose comfort settings or keep the defaults. Then confirm the board's position.
4. Play "First Crossing" (Alder Valley 1). It is guided, and the first rescue takes about a minute.
5. Settings → Open play unlocks every mission. Each world's first mission is always open.
6. Settings → Command mode gives hands-free play (head dwell, step controls).
7. Menu on the run controls pauses at any time. RESQ is fully offline and needs no account.

Known limitations: `[P5: fill from device testing]`.
