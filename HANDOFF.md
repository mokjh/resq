# RESQ handoff

Resume by reading `AGENTS.md`, `BUILD_PLAN.md` and `STATUS.md`. Older handoffs are archived in `docs/archive/process-2026-10/HANDOFF.md` and are historical only.

## Environment
- **Mac:** M1, 8 GB RAM. Keep at least 10 GB of disk free.
- **Unity:** 6000.3.24f1 at `/Applications/Unity/Unity-6000.3.24f1/Unity.app`, with Android tools already set in Unity preferences:
  - SDK: `~/Library/Android/sdk`
  - NDK: r27c (27.2.12479018)
  - JDK: Homebrew openjdk@17
- **Unity CLI and MCP:** `unity` (Homebrew, 1.0.0-beta.6). The RESQ MCP is `unity mcp --project-path /Users/dev/Desktop/Resq/game`. Never touch Waz's MCP (`--project-path …/Waz/game`).
- **Meta tooling:**
  - `metavr` CLI: docs, `xrsim`, `ssim`, `store dist upload`, `perf`, `skills`.
  - XR Simulator 207.1 at `/Applications/MetaXRSimulator.app`.
  - Meta agent skills in `~/.agents/skills/hz-*`.
- **Other tools:** Blender 5.2.1 (`/opt/homebrew/bin/blender`), ffmpeg, Node 22, Python 3.14.

## Where things are
- **Design:** `specification/`, including `content/*.json` (mission contracts, layout recipes, equipment, challenges, inventory) and `02_RUNTIME_RULES.md`. Also `Resq_Production_Pack/docs/`.
- **Product code:** `game/Assets/Resq/`. The legacy simulation moves to `reference/legacy-sim/` in P0.
- **Archived process history:** `docs/archive/process-2026-10/` and `evidence/` (frozen, not tracked in git).

## Gates (run before committing board, art, input or sim changes)
- **Sim tests (headless):** `python3 tools/simtest/simtest.py sim`.
- **Mission matrix:** `python3 tools/simtest/simtest.py lab validate|run MISSION ...`.
- **Art import:** the Editor menu `RESQ/Art/Validate Art` writes `art/IMPORT_REPORT.md`; a pass means 0 FAIL rows.
- **PlayMode smoke:** `bash tools/unity/smoke.sh` runs in the open RESQ Editor and exits 0 on a pass. It boots `Operation_Desktop`, checks the board and art, then wins W1-M1 with strategy A using real pointer presses through `BoardInteraction`. Any logged error fails it. The last result is in `tools/.cache/smoke_last.json`.

## Current step
Look 3 (concept art direction) and P1 slice work. See `STATUS.md`.
