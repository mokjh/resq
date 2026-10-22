# Production roadmap and scope control

## 1. Two products, one coherent codebase

The commercial design contains five worlds and 25 operations. The competition release is a complete first chapter, not an attempt to manufacture all five worlds at equal quality before the deadline. Shared systems and asset conventions prepare later worlds without exposing unfinished scenes in the public build.

Do not read the content catalogue as permission to build every feature in parallel. The single most important dependency is a tactile, strategic, replayable first rescue. If that does not work, creating more worlds multiplies the problem.

## 2. Proposed checkpoints

Dates below are scope-control checkpoints relative to the verified competition deadline, not delivery guarantees or estimates of autonomous runtime. Revise them after inspecting the actual project, skills, device access, and available capacity. Missed gates cause cuts, not false completion reports.

| Checkpoint | Suggested internal date | Acceptance |
|---|---|---|
| Foundation | 18 October 2026 | Real toolchain probe; save/content/command foundations; test harness |
| First rescue | 24 October | One mission's flood, navigation, bridge/barrier/skiff loop is genuinely playable |
| First chapter | 30 October | Five authored W1 missions integrate the shared systems; complete UI/save path |
| Feature lock | 6 November | No new mechanics; only corrections, usability, and performance work |
| Candidate qualification | 11 November | Hardware/comfort/content evidence; release claims reconciled |
| Capture and website candidate | 14 November | Actual gameplay footage; tested preview; accurate draft submission |
| Internal freeze | 17 November | Immutable signed APK, final video, judge access, entrant review |
| Official deadline | 18 November, 22:00 Cairo | Entrant completes submission before cutoff; no last-minute assumption |

The official cutoff is 12:00 PST / 20:00 UTC on the deadline date. The earlier chat's 23:00 Cairo conversion was wrong. [S01]

## 3. Scope-cut order

First remove decorative assets, optional idle animations, nonessential cinematics, extra challenge variants, voiceover, and supplementary website pages. Next simplify scenery density and art variations while retaining visual identity. Do not remove reliable save/resume, error recovery, readable warnings, or controller-free navigation to preserve decoration.

If the five-mission chapter cannot be qualified, release the three strongest complete missions with a truthful declared scope. Rework progression and ending so the chapter is still coherent. Do not leave five mission buttons with two dead destinations. This is contingency, not the initial product target.

Command mode is an additional access path, but the competition's end-to-end hands-first path is not optional. Retain one-handed design wherever feasible and never claim qualification without completing its test. Later-world systems stay out of the build if not needed by released content.

## 4. Long-term production sequence

After competition feedback, build Emberwood first because it introduces a different hazard while reusing roads, rescue transport, forecasts, and power. Frostline adds vertical evacuation and structural rules. Tidemark changes navigation through scheduled tide windows. Cinder Reach combines proven systems with limited, authored lava mechanics.

Each new world follows the same loop: mechanic prototype → first mission → reference replays → one-hand/device tests → world art kit → full mission set → playtest tuning → release qualification. Do not treat reskinning a flood map as a new world. No announced release date should precede a realistic capacity assessment.

## 5. Risk register

| Risk | Impact | Early signal | Mitigation / cut |
|---|---|---|---|
| Weak core strategy | Critical | Players solve by random placement or cannot name a trade-off | Tune one mission before content expansion |
| Hand-tracking friction | Critical | Accidental drops; fatigue; unreachable UI | Big targets, snap preview, far selection, one-hand paths |
| Unbounded simulation | High | Frame spikes with hazards/people | Integer grid/graphs, hard content budgets, profiled worst-case |
| Generated-asset inconsistency | High | Materials/silhouettes look unrelated | Lock look-development kit; batch audit; reject weak assets |
| Asset rights uncertainty | Critical | Missing source/license/provenance | Quarantine; replace with original authored geometry |
| Toolchain incompatibility | High | SDK/rig conflicts or build failures | Actual probe; one provider; exact version lock |
| No real headset evidence | Critical | Only Editor demos exist near capture gate | Front-load device access; mark unsupported claims blocked |
| Unsolvable mission variation | High | Alternate layout breaks evacuation | Fixed tested layouts; recorded successful reference runs |
| Over-scoped website/video | Medium | Marketing work consumes gameplay time | Minimum truthful site and actual-footage edit |
| Provider cost/retry runaway | High | Repeated paid generations without improvement | Budget caps, two repair attempts, idempotent reconciliation |
| Save corruption | Critical | Lost progress or inconsistent resumed people | Atomic writes, backup, schema migration, restoration tests |
| False automation completion | Critical | Tasks green without evidence | Structured result contracts; validate artifacts and tests |
| Entry/rules misunderstanding | Critical | Assumed eligibility or bot submission | Entrant verification; organizer clarification; manual final entry |
| Name conflict | Medium–high | Existing game/store/trademark collision | Provisional name; display-name-independent identifiers; clearance |

## 6. Decision log and scope freeze

Record decisions with date, reason, alternative rejected, dependency, and evidence. A changed gameplay rule requires updated specs, content validation, reference replays, and trailer/site claim review. Do not let different agents create contradictory interpretations.

Keep `DECISIONS.md`, `BLOCKERS.md`, `STATUS.md`, and `evidence/` in the implementation repository. Persist resume notes after each bounded work unit so a new Codex session can continue from actual state rather than a conversational claim.

A release candidate is a named immutable artifact set, not the newest Editor state. Bug fixes create a new candidate with new hashes and requalification; they must never silently mutate an already submitted competition build after the deadline.
