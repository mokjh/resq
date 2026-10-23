# RESQ — agent instructions (studio mode, from 22 Oct 2026)

Read `BUILD_PLAN.md` first, then `STATUS.md` and `HANDOFF.md`.

- **Design reference:** `specification/` (rules, contracts, layout recipes, challenges, inventory) and `Resq_Production_Pack/` (the original pack, kept byte-for-byte).
- **Archived history:** everything before 22 Oct 2026 lives in `docs/archive/process-2026-10/`. The frozen, untracked `evidence/` folder was deleted on 23 Oct with the user's approval to free disk. Do not resume the old task-graph/evidence-packet regime; the user replaced it on 22 Oct 2026.

## Goal

Ship RESQ to the Meta VR Start Developer Competition 2026 by **Tue 17 Nov 2026**. The hard deadline is Wed 18 Nov, 12:00 PT (22:00 Cairo).
- **Scope:** all 25 missions, subject to the per-world ship gate in `BUILD_PLAN.md`.
- **Entry:** Gaming track, New Experience division.

## Rules

- **Honesty:**
  - Never fabricate results, footage, device or human evidence, or public claims.
  - Never weaken, skip or stub a test to make it pass.
  - Label evidence as automated, simulator, device or human.
- **Real gates:**
  - simulation tests
  - the mission replay matrix
  - the PlayMode smoke test
  - performance budgets
  - playtests
  - the user's reviews
- **Device testing comes last:** physical Quest 3/3S work happens only in phase P5 (12–15 Nov). Before that, use the Editor, the Meta XR Simulator, SpatialSim and the Editor desktop input adapter.
- **Runtime:**
  - The simulation is offline, deterministic and integer-based.
  - Every input becomes a validated command.
  - Residents, occupied vehicles, buildings and hazards cannot be grabbed to bypass strategy.
  - Hands-only, one-hand and head-gaze Command paths must each complete every mission.
  - Physics, animation, wall time and presentation scale never decide outcomes.
- **Ownership:**
  - The coordinator is the only writer of the Unity project.
  - Helpers get disjoint write areas plus acceptance tests and stop conditions.
  - Run one Blender job at a time.
  - Keep at least 10 GB of disk free during Unity or Blender work.
- **Other projects:** never touch Waz or any other project, their processes, MCP bridges or global Unity preferences.
- **Git:**
  - Commit locally on `main` at each working step.
  - Do not push without the user's OK.
  - Never commit `Library/`, secrets, keystores, raw room captures or `evidence/`.
- **External writes need the user's confirmation at the time:** store/channel uploads, Vercel deploys, YouTube, domain and DNS.
- **Paid generation cap (raised by the user 22 Oct):** Meshy ≤ 600 credits, Higgsfield ≤ 150 credits. Log every spend and its license terms in `docs/spend-log.md`.
- **Repair loops:** after three failed repairs of the same problem, record the blocker in `STATUS.md`, switch to an alternative approach and keep the other work moving.
- **Docs:** keep `STATUS.md` and `HANDOFF.md` to one page each, current at every milestone. Append decisions to `DECISIONS.md`.

## Layout

| Path | Purpose |
|---|---|
| `game/` | Unity 6000.3.24f1 project; product code under `Assets/Resq/` |
| `content/` | Generated mission layouts and replays per world |
| `tools/` | Layout compiler, validator, Blender scripts, build and test scripts (older process tools remain but are unused) |
| `reference/legacy-sim/` | The previous simulation, kept unchanged as a golden-value oracle |
| `web/app/` | Marketing website |
| `video/` | Trailer sources and edit decisions |
