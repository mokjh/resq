# Devpost entry: draft for the user's review

Status: a draft written 28 Oct 2026, updated 15 Nov, from what is verified in the Editor and Meta XR Simulator. Nobody has played RESQ on a headset, so the text claims no device test.

- **Markers:** `[you]` marks what only the owner can provide, `[rep]` what only the Representative can.
- **Counts:** `python3 tools/web/count_words.py docs/submission/devpost-draft.md` checks the tagline and story limits.
- **Submission:** whoever submits makes the attestations. On the declared-team route (`READY.md`), that is Osama Mohamed as Team Representative, from his own account. Keep his email and account ids out of this file.

## Form fields

As the live form shows them, 5 Nov. `[rep]` marks what only the Representative can provide.

- **Project name:** RESQ
- **Track / division:** Gaming, New
- **Tagline (≤ 140 characters):** A hands-first rescue strategy game on your real table: read the flood, fire or lava, place one tool, and bring every resident home.
- **Target launch date:** `[you]` Suggestion: Spring 2027.
- **Video:** `[you]` the public YouTube link, after the user approves the cut.
- **Build path:** Unity
- **Competition channel URL:** the invite URL `[you]`.
- **Submitter Type:** Team of Individuals
- **Country of residence:** `[rep]`
- **Email tied to the developer organization or Start membership:** `[rep]`
- **Teammates (names, emails, roles):** Osama Mohamed, Team Representative, `[rep: his real role]`. Amir Lotfy, `[you: email]`, design and development. List only what each person really did.
- **Team name:** `[you]` Shown as the creator if the entry wins.
- **Meta capabilities used:** hand tracking, passthrough, scene understanding (MRUK), spatial anchors, head-gaze dwell. Don't list the ISDK gaze interactor or eye tracking: product code has neither (checked 5 Nov).
- **Hand interactions:** from "Hands first" in the story below.
- **Attestations, by the Representative:** Start member; every teammate is of age, from an eligible jurisdiction and not a sponsor or government employee; every teammate has read and agrees to the Official Rules and Devpost's terms.

## Story (≤ 500 words)

<!-- story:start -->
**Inspiration**

We wanted a seated mixed reality game with real agency in your hands: not a passive diorama, not a workout. A miniature rescue fit: you see the whole problem at once, and one careful move visibly changes what happens to the people inside it.

**What it does**

RESQ places a small valley on a table in front of you. A flood, a forest fire, an avalanche slope, a rising tide or a lava flow is coming, and residents need a way out. You pinch tools from a tray, such as a barrier, bridge, pump, skiff, firebreak, brace or coolant, and place them on legal spots. Before you commit, a forecast runs the real simulation ahead and shows which cells stay dry and which newly flood.

There are five worlds and 25 missions, each with two very different winning plans, three rule presets and 30 named challenges. A debrief explains the cause and effect behind every run, and in aftercare the valley recovers.

**Hands first, and more than one way to play**

Everything works with hands: poke and pinch for the tray, ray and pinch for distant spots. One-hand play mirrors the controls for either hand. Command mode turns it into a turn-based puzzle: a head-gaze dwell selects, and +1 s / +5 s step controls advance time, so the whole game also works without fine finger movement. Text scales to 150 %, with high-contrast and reduced-effects modes.

**Your room matters**

You start on a virtual table. Choose "Use my table" and RESQ uses the Mixed Reality Utility Kit to find a real table that fits. A long table gets the wide layout and a small one the compact layout, which changes the mission. Rain falls from your real ceiling and the room dims with the storm. Your valley is saved as a spatial anchor, so it is still there next time, and your hands cast soft shadows on it, so you can judge height before you touch.

**How we built it**

Unity 6 with URP, the Meta XR Core and Interaction SDKs (v207) and MRUK, built and tested in Meta XR Simulator with simulated hands. The rules run in a deterministic, integer-only simulation, so physics or frame rate never decide an outcome. A headless validator proves that doing nothing fails and that both reference plans win on both layouts in every preset: 450 of 450 runs pass, plus 120 of 120 challenge runs. Terrain, water and nature come from scripted Blender kits on one palette. Landmarks, vehicles and residents were generated with Meshy from concept images made with Higgsfield, and the residents were rigged and animated with Meshy. Music and sound are synthesised from code.

**Challenges**

An honest, fast forecast: it runs the real simulation ahead on a worker thread instead of drawing a guess. Five rule sets in one engine, with presentation never touching outcomes.

**What's next**

Playtests on Quest 3 and 3S, more challenge variants, localisation, and a full release.
<!-- story:end -->

## Judge start guide (for the entry's "how to test" field)

1. Install from the invite URL on Quest 3 or 3S and sit down. A table helps but isn't needed.
2. On the welcome card, choose comfort settings or keep the defaults. The board starts on a virtual table in front of you, with no permission prompts. Raise, lower or move it, then press Done.
3. Optional: "Use my table" asks to read your room and moves the board onto a real table that fits.
4. Play "First Crossing" (Alder Valley 1). It is guided, and the first rescue takes about a minute.
5. Every mission is open from the start. Settings → Open play switches back to the campaign order.
6. Settings → Command mode gives hands-free play (head dwell, step controls).
7. Menu on the run controls pauses at any time. RESQ is fully offline and needs no account.

Known limitations: built and tested in Meta XR Simulator and the Unity Editor; not yet played on a headset.
