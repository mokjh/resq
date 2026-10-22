# RESQ status — 22 Oct 2026, 23:50 (day 0 of 26)

**Phase:** P0 complete; P1 (vertical slice) well under way. Plan: `BUILD_PLAN.md`.

## Done
- **Studio reset:** old process docs archived with checksums; commits are on local `main`.
- **Unity project:** URP, OpenXR, Meta XR 207.0.1 (Core, Interaction, MRUK), Quest settings (ARM64/IL2CPP, SDK 32/34, Vulkan, ASTC); Meta Setup Tool clean.
- **First APK built:** `build/resq-smoke.apk`, 75 MB. INTERNET permission is now stripped (takes effect on the next build).
- **Sim v2 engine:** water, power, equipment, transport, objectives, outcome. All 18 spec golden cases pass.
- **World 2 fire module:** heat, fuel, wetness, wind, ignition warnings, firebreak, sprinkler from a finite tank, roads closed by heat. 9 fire tests; 32/32 sim tests pass.
- **W1-M2, W1-M3:** 18/18 validation runs each.
- **SimLab:** missions validate headlessly in seconds.
- **W1-M1 First Crossing:** 18/18 validation runs pass. No-input fails; strategies A and B win 3★ on both layouts and all 3 presets.
- **Unity game layer:** First Crossing plays end-to-end in Unity on the real engine — board, water, actors, tool tray, goals and routing.
- **Input:** mouse, hand pinch/poke and head-dwell adapters, all on one command path.
- **Art plan:** world-class pipeline adopted from the Waz techniques; see the `BUILD_PLAN.md` art section.

## In progress (helpers)
- Blender look-dev quartet: cottage, bridge, skiff, resident.
- Missions W1-M4 and W1-M5 with SimLab.

## Next
0. Remaining world modules (headless): stability/runout/winch (W3), tides/relay (W4), lava/cooler/shelter (W5).
1. XR wiring: OVR rig, hands, passthrough and MRUK placement in the operation scene.
2. Forecast preview overlay.
3. Guided tutorial.
4. RESQ Lit toon shader.
5. Greybox → art kit import pipeline.

## Waiting on the user
- Meta VR Start membership and Devpost registration.
- The "RESQ" app in the Meta VR Developer Dashboard, then `metavr auth login`.
- **Disk:** 13 GB free. Please free about 25 GB more.
- **Credits:** caps raised to Meshy 600 / Higgsfield 150. Higgsfield balance is 110, so top up if you want the full 150.
