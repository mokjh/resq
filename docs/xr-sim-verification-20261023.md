# XR wiring — verification record, 23 Oct 2026

Evidence kinds are labelled on every line. Nothing here was run on a Quest; device checks belong to P5.

## What was built
- **`Resq.XR` assembly** (`game/Assets/Resq/XR/`). The core game has no Meta SDK dependency; XR plugs in through `GameBootstrap.XrPlacement` and `XrPointers`.
- **`TablePlacement`:**
  1. Restore the saved spatial anchor; the board returns where it was, on the same surface (table or virtual table).
  2. Otherwise use the nearest MRUK `TABLE` whose top holds the board, in this order: wide facing the player, wide turned 90°, compact facing, compact turned. The board sits on the top, square to it, with its forward pointing away from the player.
  3. Otherwise build a virtual table at seated height.

  A fresh placement is saved as an `OVRSpatialAnchor`, and the board is parented to it.
- **`XrPointers`:**
  - Interaction SDK `IHand` pointers per hand: pointer-pose ray, index pinch with hysteresis, fingertip poke.
  - Head-gaze dwell in the Command profile.
  - Optional head-gaze + pinch.
  - Falls back to `OVRHand`.
- **XR scene** (`tools/unity/BuildOperationXRScene.cs`): MRUK with device scene data loaded on startup, and the scene permission requested at startup.

## Verified
| Check | Evidence | Result |
|---|---|---|
| Placement in all 40 MRUK sample rooms (LivingRoom, Bedroom, Office, Other): the board's footprint lies on the chosen table top, at its height, square, facing away from the player; when no table is chosen, none could hold the compact board | automated, Editor PlayMode (`XrPlacementTest`) | pass: 27 wide, 4 compact, 9 virtual table |
| Unity attaches to the XR Simulator; XR detected; both Interaction SDK hands become pointer sources | simulator (macOS, LivingRoom env) | pass |
| Simulated hands reach the pointers: tracked, valid pointer rays, fingertip poke points | simulator | pass |
| No room data → virtual table; spatial anchor created and saved | simulator | pass (`ce87c8f7-…`) |
| Next session restores the anchor at the same pose | simulator | pass |
| Restored board comes back on a real table, with no stray virtual table | simulator | pass |
| Table path end to end with an MRUK sample room loaded into the live session (Editor-only `RESQ_DEV_ROOM` hook; the macOS simulator gives MRUK no rooms): 1.81 × 0.80 m table → wide, yaw 91°, anchored; restored next session | simulator + MRUK sample room | pass |
| Head-gaze dwell on the Start control through the real XR head pose: not fired at 0.55 s, fired by 2.7 s, run unpaused | simulator | pass |
| The desktop smoke gate is still green after the input changes | automated, Editor PlayMode | 6/6 pass |

## Bugs found and fixed
- **XR start race:** `GameBootstrap` checked `XRSettings.isDeviceActive` before OpenXR had started, so placement and hand input were skipped. Startup now waits up to 3 s for the device when a camera rig is present.
- **Rig collider swallowing rays:** the Interaction rig's `PlayerController` capsule was the first hit on head and hand rays, so the dwell never targeted anything. Pointer casts now take the nearest **pickable** hit (`BoardInteraction.CastPickable`, non-allocating).
- **Restored virtual table missing:** a restored anchor on a virtual-table placement came back floating. The surface type is now saved and the table rebuilt.

## Not verified yet (needs a headset, P5)
- Real Space Setup rooms and the scene permission prompt or denial: the macOS simulator provides no room data.
- Hand **pinch** presses: the simulator CLI can't script a pinch, so only tracking and rays were checked. The input → command layer behind them is covered by the smoke gate.
- Gaze + pinch, controller input, and anchor relocalisation after the user moves or re-centres.
- Passthrough look, hand occlusion and shadows, frame rate.
