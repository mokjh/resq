# Resq — decisions, assumptions, and verified constraints

Version: 1.0 • Prepared 15 October 2026 • Owner: Project creator

## Status of this delivery

This is a production specification and Codex handoff pack, not a built or tested game. It contains original product/design decisions, machine-readable mission briefs, a logical ERD, validation tooling, acceptance criteria, and a production workflow. It does not contain a playable APK, finished meshes, recorded gameplay, a published video, or a deployed website. Those deliverables must be generated and verified in the user's local Codex/Unity environment.

Numbers labeled targets are initial design or engineering budgets, not measured performance or market benchmarks. Mission events, quantities, and challenge tuning are initial authored values requiring playtesting. Machine-readable mission briefs are NOT finished scene geometry or compiled runtime levels.

## Decisions to preserve

| Decision | Specification |
|---|---|
| Working name | Resq; display wordmark RESQ; provisional subtitle “Little Worlds, Big Saves” |
| Tagline | Small worlds. Big saves. |
| Domain | playresq.com, intended future domain; ownership and availability unverified |
| Product | Seated, hands-first miniature rescue strategy game for standalone Meta Quest |
| Competition | Meta VR Start Developer Competition 2026; intended Gaming / New Experience division |
| Primary targets | Quest 3 and Quest 3S; test both before claiming support |
| Default play | Hand tracking, single-hand-completable; optional controllers |
| Additional accessibility | Head-directed selection and dwell; distinct Command mode; no eye tracking assumed |
| Presentation | Mixed reality with room-aware table placement; virtual-table fallback |
| Full-game content | Five worlds, five authored missions per world: 25 missions |
| Competition slice | World 1, all five missions, integrated tutorial, replay variations, results, save/resume |
| Runtime | Offline, deterministic game rules; no LLM dependency, mandatory login, or cloud generation |
| Build | Unity 6.3 LTS candidate + URP + a verified compatible Meta SDK combination |
| Art | Higgsfield-assisted concept/3D work; Blender as editable source and optimization stage |
| Site | Next.js/TypeScript, deployed to Vercel; Mobbin research in the user's authenticated Codex environment |
| Commercial model | Proposed premium purchase after competition; final pricing not approved |
| Submission video | 2:40 target, actual game footage; no fabricated gameplay |

## Name assessment

“Resq” is short, relates to rescue, and fits the concept. It is not a clean-sheet name: a historical video game is documented as ResQ [S18], and a designer documents a board game with that name [S19]. This is a preliminary collision check, not trademark clearance. A subtitle may improve search specificity but does not establish legal availability. Keep all game identifiers independent of the display name. Do not purchase a domain or assert ownership on the user's behalf. Before commercial launch, verify relevant game/store names and obtain appropriate name clearance in intended markets.

## Competition constraints that change implementation

The official rules require a hands-first experience and support a seated, short-session design. A Unity submission uses an APK in a Meta developer release channel called `Competition`. A public YouTube or Vimeo demo should be under three minutes and show device or emulator footage. Freeze the submission at the deadline and preserve judging access. English is required directly or through translation/subtitles. Verify eligibility, Start membership, permitted resources, and asset rights before submission. The rules warn against bot/automated entries; do not automate final entry submission or attestations. Seek organizer clarification before interpreting ambiguous originality/automation language. [S01–S04]

Deadline: **18 November 2026, 12:00 Pacific Standard Time = 20:00 UTC = 22:00 Cairo**. The earlier conversational conversion to 23:00 Cairo was incorrect. Conversion uses timezone rules for the deadline date, not today's UTC offset. [S01]

Competition judging assigns equal weight to innovation, experience design, technical implementation, and polish. Its published floor of 60 fps is not this project's production target: Meta's Quest optimization guidance gives 72 fps as its VRC minimum. Build toward sustained native 72 fps, then qualify 90 fps independently. Recheck current requirements before the final build. [S01, S08]

## Automation boundaries

Automate routine local implementation, scene assembly, asset optimization, tests, builds, website checks, and video assembly. Do not equate agent completion with evidence of correctness.

Human/account gates remain: hardware access and safe physical setup, developer mode and USB trust, Unity licensing, provider OAuth, approved spend limits, actual comfort/playability observations, footage permission, domain purchase/ownership, release approval, and final competition submission. Aggregate these gates into one startup report; continue unrelated work rather than repeatedly interrupting the owner.

No system here guarantees a win, award, commercial success, platform acceptance, or studio-equivalent quality without review.

## Tool verification notes

Higgsfield's current 3D Jutsu documentation describes editable scenes and GLB export. Connected tool definitions additionally expose committed Blender-source and GLB retrieval. That is useful for production, but does not establish Quest-ready topology, materials, licensing, rigging, or performance. Every asset still passes Blender and Unity validation. [S11, S12]

Mobbin documents an official MCP endpoint and Codex compatibility. The research connection was not usable inside this conversation: plugin discovery returned no Mobbin connector, and the alternative Refero connection reported an inactive subscription. Consequently, this pack does NOT claim that any Mobbin screens were inspected. Codex must perform and record the real reference study before implementing the site. [S13]

## Sources

Public sources were checked during preparation. Recheck live platform/competition rules before release. Tool schemas describe only the environment in which they are exposed; discover them again in Codex.

- **S01:** Competition official rules — https://start-developer-competition-26.devpost.com/rules
- **S02:** Competition FAQ — https://start-developer-competition-26.devpost.com/details/faqs
- **S03:** Competition overview — https://start-developer-competition-26.devpost.com/
- **S04:** Meta competition announcement — https://developers.meta.com/blog/meta-connect-2026-vr-start-developer-competition/
- **S05:** Unity 6 release support — https://unity.com/releases/unity-6/support
- **S06:** Meta Interaction SDK requirements — https://developers.meta.com/vr/documentation/unity/unity-isdk-packages-and-requirements/
- **S07:** Official MRUK sample overview — https://developers.meta.com/vr/documentation/unity/unity-sample-mruk-basic/
- **S08:** Meta optimization workflow — https://developers.meta.com/vr/documentation/unity/po-perf-opt-mobile/
- **S09:** Unity 6.3 command-line reference — https://docs.unity.com/en-us/engine/6000.3/manual/unity-editor/command-line-arguments/editor
- **S10:** Blender command-line reference — https://docs.blender.org/manual/en/3.0/advanced/command_line/arguments.html (flag reference; inspect installed-version documentation before use)
- **S11:** Higgsfield 3D Jutsu — https://higgsfield.ai/blog/higgsfield-3d-jutsu
- **S12:** Higgsfield MCP — https://higgsfield.ai/mcp
- **S13:** Mobbin MCP — https://mobbin.com/mcp and https://github.com/mobbin/mobbin-mcp-server
- **S14:** Codex non-interactive mode — https://learn.chatgpt.com/docs/non-interactive-mode
- **S15:** Codex AGENTS.md guidance — https://learn.chatgpt.com/docs/agent-configuration/agents-md
- **S16:** Vercel deployments — https://vercel.com/docs/deployments
- **S17:** Vercel domain documentation — https://vercel.com/docs/domains
- **S18:** Historical ResQ documentation — https://www.segaretro.org/ResQ
- **S19:** Designer's ResQ board-game page — https://www.aishwaryanarvekar.com/resq

All subsequent gameplay, numerical tuning, art choices, mission scenarios, and architecture choices are proposed Resq specifications unless explicitly attributed to a source.
