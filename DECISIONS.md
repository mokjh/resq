# RESQ decision ledger

Newest first. Decisions before 22 Oct 2026 are archived verbatim in `docs/archive/process-2026-10/DECISIONS.md`.

## 22 Oct 2026 (late) — art pipeline and credit caps (user-approved)

| Decision | Result |
|---|---|
| Art pipeline | Adopt the Waz-proven routing, re-authored in RESQ (no copied code): scripted Blender kits for buildings and simple props; Meshy-t2 for organic props; Tripo (via Higgsfield) or Meshy-7.1 for landmarks and vehicles; Higgsfield sheet, then Meshy, then a shared rig plus library clips for residents. RESQ Lit toon shader. |
| Credit caps | Raised to **Meshy 600 / Higgsfield 150** for characters, landmarks and swatches. Every spend is logged in `docs/spend-log.md`. Current Higgsfield balance (110) is below the cap; a top-up is the user's call. |

## 22 Oct 2026 — studio reset and competition plan (user-approved)

| Decision | Result |
|---|---|
| Competition entry | Meta VR Start Developer Competition 2026: Gaming track, New Experience division. Submit Tue 17 Nov; the deadline is Wed 18 Nov, 22:00 Cairo. |
| Scope | The user chose to attempt all 25 missions in the entry. Per-world ship gate: a world is playable in the entry only if all five of its missions pass validation, a Simulator playthrough and a device smoke test. Otherwise it is shown honestly as "in development". Alder Valley's five missions are never cut. |
| Process | Studio mode replaces the 229-task evidence/admission regime. Gates are real tests, deterministic replays, performance budgets, playtests and user reviews. Old records are archived, not deleted. |
| Device testing | Last. Physical Quest 3/3S work happens only in phase P5 (12–15 Nov); everything before that runs in the Editor, Meta XR Simulator and SpatialSim. |
| Simulation | New lean "Sim v2": struct-of-arrays, integer, allocation-free, deterministic, covering every world's rules. The old engine is kept unchanged as a golden-value oracle in `reference/legacy-sim/`. |
| Content | Mission factory: recipe plus contract feed a layout compiler, then a headless validator (no input must fail; strategies A and B must win on both layouts and all three presets). |
| Art | Original scripted-Blender kits on one palette atlas. Paid generation capped at Meshy ≤ 300 and Higgsfield ≤ 60 credits, each spend logged with its license. |
| Machine | RESQ gets exclusive use of Unity on this Mac until 18 Nov (the user pauses Waz). Reclaimed the throwaway bootstrap Unity `Library/` cache in `evidence/` (5.7 GB). |
| Git | Local commits on `main` at each working step. No push without the user's OK. |
| External writes | Store uploads, deploys, YouTube and domain are confirmed with the user at the time. |

## 23 Oct 2026 — rule readings for the World 2 and World 3 modules (spec 02 §5, §7, §8)

| Decision | Result |
|---|---|
| Fire on non-fuel cells | Every cell takes heat, so roads and water next to a fire latch closed. A legend char with no `fuel` defaults to 1000 (spec §7), so World 2 maps must author `fuel: 0` for roads and water. |
| Collapse warnings | A published warning of any event that acts on a support (`set_stability_loss`, `set_support_load`, `tremor`, `warn_support`) counts as the relevant warning and keeps its stamp. Only an emergent warning (effective ≤ 300) is cleared when stability rises above 400. |
| `warn_support` target | Resolves to `supports` in the map's roles, not infrastructure. |
| Winch duty | Duty counts powered travelling operation ticks. Departure needs duty < 100. A winch at its limit rests empty at a safe endpoint before anyone boards; 30 rest ticks reset duty to 0. |
| Cable edges | Open while a winch is placed, unbroken and its support stands. Power governs movement (brake, then a one-time emergency return), not edge admission. |
| Runout | Segments start at the release (or bound collapse) tick; successors start exactly `delay` ticks after their predecessor. Nodes are unsafe while a segment settles; `debris_reopenable` edges reopen after `settle`; other edges stay closed. |
