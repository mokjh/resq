# RESQ status — 22 Oct 2026 (day 0 of 26)

**Phase:** P0, reset and foundation (22–24 Oct). Plan: `BUILD_PLAN.md`.

## Done today
- Approved the plan with all 25 missions, studio mode, a capped AI budget and exclusive use of the Mac.
- Archived the old process docs verbatim with checksums in `docs/archive/process-2026-10/`. Wrote a lean `AGENTS.md`; `BUILD_PLAN.md` is now the approved plan.

## Inherited from the old regime
- About 11k lines of rules-correct simulation code (too slow for runtime; it becomes the golden oracle).
- Bootstrap tools.
- A Mobbin reference lock and fonts.
- The full design specification.
- No scenes, art, audio, site or APK.

## Next (P0)
1. Reclaim the 5.7 GB bootstrap `Library/` copy.
2. Git baseline commit.
3. Move the legacy simulation to `reference/` and build a headless C# runner.
4. Unity packages and URP/XR/Android setup.
5. MR hands smoke scene plus an APK build.
6. Sim v2 skeleton.

## Gate M0 (Sat 24 Oct)
- Simulation tests pass.
- The MR hands scene runs in the XR Simulator.
- An APK builds.
- The baseline is committed.

## Waiting on the user
- Meta VR Start membership and Devpost registration.
- The "RESQ" app in the Meta VR Developer Dashboard, then `metavr auth login`.
- Pausing Waz's Unity work while RESQ is running.
