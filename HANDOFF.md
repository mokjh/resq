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
- **PlayMode smoke:** `bash tools/unity/smoke.sh` runs in the open RESQ Editor (it focuses the Editor so the Game view renders) and exits 0 on a pass. For each world's M1 it checks the board and art, asserts the render budget at step 300, and wins strategy A through real pointer presses. A Command-path case wins W1-M1 through the head-dwell pointer. Any logged error fails it. The last result is in `tools/.cache/smoke_last.json`; perf lines are in the Editor log as `[RESQ smoke]`.
- **XR in the simulator:** `metavr xrsim app ensure-running`, then `metavr xrsim env set --id LivingRoom`, then the Unity menu `Window/Meta/Meta XR Simulator/Activate`. Open `Operation_XR` and play; `[RESQ XR]` lines in the Editor log report placement and anchors. The macOS simulator has no room data; for the table path, set `RESQ_DEV_ROOM` to an MRUK sample-room prefab path in the Editor process. Deactivate afterwards and run `metavr xrsim app quit`.
- **Render budget, all missions:** `bash tools/unity/perf.sh wide|compact` (play mode running).

## Current step
Look 3 (concept art direction) and P1 slice work. See `STATUS.md`.
