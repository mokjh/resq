# Gameplay video and competition package

## 1. Editorial objective

Make the viewer understand the problem, watch a deliberate player action, and see the rescue it causes. The game—not an AI cinematic, presenter, or technology logo reel—is the evidence.

Target deliverable: **160 seconds (2:40)**, landscape 16:9, 1920×1080, H.264 video, AAC audio, English captions. Export at a cadence supported by the recorded footage; target 60 fps only when appropriate source footage exists. Do not synthesize/interpolate frames to imply hardware performance. A 60 fps export does not demonstrate that the game ran at 72 Hz.

The official competition asks for a public YouTube or Vimeo video under three minutes showing device or emulator footage. Its presentation guidance expressly emphasizes honest gameplay. Final submission requirements must be rechecked by the entrant. [S01]

## 2. Capture prerequisites

No final trailer may be assembled from invented footage. Required inputs are a qualified build, legal/approved media, stable playable reference runs, an identifiable capture source, and a capture manifest. The current pack contains none of those clips.

Each source clip records: filename, SHA-256, build/commit/content hash, source device or emulator, world/mission/mode, capture resolution and frame cadence, session/run identifier, start/end simulation tick where available, whether it contains an actual human interaction or a replay, and privacy/rights review. Preserve original clips separately from edits.

Use real headset capture to prove hands, passthrough, physical reach, and room adaptation. Deterministic in-engine replay may capture cleaner explanation shots, but must be identified internally and labeled when presented as emulator/replay footage. Never describe a recorded automation sequence as a human usability test.

Capture in a controlled private room with no identifiable non-entrants, screens containing private information, branded props, or incidental bystanders. Do not upload raw room footage to a third-party service without proper permission. Generated hands must never replace tracking failure in a purported gameplay clip.

## 3. Locked storyboard — 160 seconds

| Time | Picture | Meaning and copy | Evidence source |
|---|---|---|---|
| 0–8 s | Flood approaching a bus; an actual hand drops a barrier; water turns | “One small move.” Short diegetic audio hook | Quest capture, W1-M1 or M3 |
| 8–22 s | Residents reach safety; title over the real scene | RESQ / Small worlds. Big saves. | Same build, real outcome |
| 22–43 s | Bridge preview, valid anchors, unfold, crossing | Show read → place → consequence without rapid cuts | W1-M1 human play |
| 43–64 s | Pump outlet forecast; compare safe/unsafe outlet; choose safe one | “Every route changes the risk.” | W1-M2 validated runs |
| 64–85 s | Load a skiff, redirect it, unload people at a safe dock | “Plan the rescue. Keep the way out open.” | W1-M3 run |
| 85–105 s | Two actual supported table setups; compact layout and virtual fallback | Seated, hands-first; real room affects safe layout | Device captures of both cases |
| 105–124 s | One-hand interaction and short Command-mode sequence | Show optional head-dwell only if fully verified | Qualified access paths |
| 124–142 s | W1 finale combines tools; meaningful score/debrief; next mission | “Learn the world. Find a better way.” | W1-M5 and real save/progression |
| 142–154 s | Tactile details, spatial sound cue, rescued town response | Explain offline game and complete first chapter, if true | Actual build shots |
| 154–160 s | Quiet final rescue payoff and clean title card | RESQ / Small worlds. Big saves. | Original in-game background/title |

Do not show the other four worlds as playable during a World-1-only submission. A planned feature belongs in the written future plan, not a misleading rapid montage. If the one-hand or Command path is not qualified, remove that claim and replace the segment with another verified gameplay moment. Never fabricate its evidence to preserve the edit.

## 4. Narration draft

This is an editorial draft, not recorded speech. Fit the actual reading to measured footage and leave room for game sound. Do not force every sentence into the final version.

> One small move can change everything.
>
> In Resq, a little world needs a way out—and you are the rescue plan.
>
> Read the rising water. Place a bridge. Redirect the danger before it reaches the people waiting below.
>
> But solving one problem can create another. Pump into the wrong basin, and the safe route becomes the next emergency. Keep your power on high ground. Watch the forecast. Then commit to your plan.
>
> Your boats have limited space. Your equipment has a real cost. Decide who needs the first trip, where they can board, and how you will keep the return journey open.
>
> Everything is designed around a seated player. The world fits a safe space in front of you, with hand interactions close by and clear ways to reach farther objects. When a suitable table is not available, a virtual one keeps the game playable.
>
> You can complete the experience with one hand. In Command mode, head-directed selection and deliberate steps offer another way to think through a rescue.
>
> Across Alder Valley, five operations build from a first crossing to a rescue that brings every tool together. Different plans reveal different consequences. The debrief shows what happened—and what you could try next.
>
> No cloud character is inventing the rules. No cinematic is pretending to be a game. This is a miniature world that responds to your decisions.
>
> Resq. Small worlds. Big saves.

The sentences about five operations, one-hand completion, Command mode, and offline behavior are conditional on release evidence. The final script must be generated from verified facts, not published verbatim before the features exist. Avoid the defensive “no cinematic” line if it weakens the actual edit; a truthful demonstration normally communicates this without explanation.

## 5. Automated production pipeline

Implement `CaptureDirector` and a replay/spectator camera with a separate capture mode. It must not change the scoring rules or create footage of content absent from the build. Preflight disk space and permissions. Capture logs must distinguish real input, replay, simulator, and platform recording.

Codex then checks clip metadata with a media probe, selects recorded ranges according to the storyboard, trims them without obscuring key causal sequences, composes original text overlays, adds licensed music or actual game stems, ducks music under speech, and exports a review candidate with FFmpeg or another verified installed editor. The editing backend may be local; Higgsfield is optional for permitted supporting production tasks, not a required dependency or a replacement for real gameplay.

Preserve an edit-decision JSON containing source IDs, in/out times, overlay text, narration segments, transitions, audio levels, and export settings. Use hard cuts and a few short fades. Do not fill every moment with captions, zooms, or fast transitions. Keep at least two clearly readable action-to-result sequences with natural pacing.

Voice generation is optional and requires an authorized service, budget, and license. Do not clone a real person's voice without explicit permission. A caption-led video with game audio is an acceptable internal fallback if a suitable voice pipeline is unavailable. Final public playback still needs human review for intelligibility and framing.

## 6. Media quality gates

Probe the final file and verify duration <180 seconds, expected dimensions, readable cadence, valid audio/video streams, and no black/missing frames. Inspect actual first, last, and transition frames. Review the complete exported file, not only the editor timeline.

Use project audio targets around −16 LUFS integrated for the web edit and no higher than −1 dBTP peak, then listen on ordinary speakers/headphones; these are chosen mix targets, not a platform submission rule. Keep meaningful game audio audible. Time captions to actual speech; supply a separate SRT/VTT and a readable captioned variant. Preserve an uncaptioned master for future localization.

A claimed public video link must be opened without the uploader's authenticated session. It must not be private, blocked, or still processing. Posting requires explicit authorized publishing scope. Final Devpost entry remains an entrant action.

## 7. Devpost narrative template

**Title:** Resq — Little Worlds, Big Saves.

**One-line description:** A seated, hands-first rescue strategy game where redirecting a miniature world's hazards creates a way home.

**Inspiration:** Describe the actual desire for seated play with meaningful agency: not a passive diorama, and not a game demanding physical exercise. Explain why rescue consequences work at miniature scale. Do not invent a personal disaster story or testimonials.

**What it does:** List only the actual released chapter, playable modes, tested hardware, and demonstrated interactions from the release manifest. Identify planned worlds separately.

**How it was built:** Describe actual Unity/Meta packages, the deterministic simulation, reusable data-driven mission system, real hand/placement integration, Blender work, and any Higgsfield-generated assets actually used. Maintain authorship/provenance disclosures as applicable. Do not claim every feature used AI or imply vendor sponsorship.

**Challenges:** Use genuine development evidence: a routing bug, occluded hand interaction, power/flood coupling, or on-device optimization with before/after measurements. Do not invent struggles or benchmark improvements.

**What we learned:** State observed playtest lessons, including changes made from them. If human testing was limited, say so.

**Next:** Planned Emberwood, Frostline, Tidemark, and Cinder Reach; deeper challenge variants and localization only if still intended. Proposed full-release window: first half of 2027, subject to validated production capacity and store approval. The entrant must set a credible actual target date before submitting; this pack does not promise a launch.

## 8. Judge access and handoff

The submission package should contain a concise start guide: supported tested headset, actual release-channel access path, input requirements, permission behavior, tutorial reset, offline behavior, a recommended first operation, how to pause/recenter, optional modes, and known limitations. Keep credentials and individual invitation links out of the public marketing repository.

Maintain a frozen build and free judge access through the required period. Preserve source history proving the development timeline. Confirm the current rules about originality, resource rights, third-party branding, and automated entry language with the organizer where interpretation is necessary. Final registration/submission, eligibility statements, and legal attestations must be completed by the entrant; this plan does not authorize a bot to enter. [S01]
