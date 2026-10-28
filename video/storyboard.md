# RESQ trailer: storyboard and capture list

Target: 160 s, 1920×1080, H.264 + AAC, English captions as SRT/VTT plus a captioned copy. Competition rule: the footage must come from a headset or the Meta XR Simulator, with no Editor footage and no AI video. The pipeline refuses any other kind of source.

This adapts the locked W1-only board in `Resq_Production_Pack/docs/11_VIDEO_AND_DEVPOST_SPEC.md` §3 to five worlds.

- **Ship gate:** a world appears only if it passes its ship gate (`BUILD_PLAN.md`). Otherwise its shot is dropped and the time goes to W1.
- **Device-only shots:** rows marked **P5** need a Quest. If a claim isn't verified on a device by 15 Nov, the shot and its caption are cut; they are never faked.

## Pipeline

1. Put each clip in `video/raw/<id>.mp4` (git-ignored; room footage stays local). Beside it, add `<id>.json`:
   ```json
   {"kind": "xrsim", "input": "human", "mission": "W1-M1", "build": "<commit>", "captured": "2026-11-06"}
   ```
   - `kind`: `quest` | `xrsim`
   - `input`: `human` | `replay` | `autopilot`
2. Record the clips:
   - **Simulator:** `metavr xrsim record start` / `stop`, from a dev build in the XR Simulator.
   - **Device:** the Quest camera app at 1080p landscape, then `metavr files pull`.
3. Set the in/out points in `video/edl.json`.
4. Run `uv run --with pillow python3 tools/video/edit.py build`. It writes:
   - `video/exports/resq-trailer.mp4`, the master without captions;
   - `resq-trailer-captioned.mp4`;
   - `.srt` and `.vtt` caption files;
   - `.decisions.json`, the record of every source's SHA-256, kind and input.

   It then runs the gates: under 178 s, 1920×1080, audio present, −16 LUFS ±1, true peak ≤ −1 dBTP, black runs reported, and first and last frames saved for review.
5. Review the whole exported file by eye and ear. The user approves before any YouTube upload.

`edit.py selftest DIR` runs the whole chain on test patterns stamped "PIPELINE TEST - NOT FOOTAGE".

## Board (160 s)

| Time | Clip id | Picture | Caption | Source |
|---|---|---|---|---|
| 0–8 | `q_w1m1_barrier` | A real hand drops a flood barrier on a real table; the water turns | One small move. | **P5** Quest, human |
| 8–14 | card | RESQ / Small worlds. Big saves. | — | title card |
| 14–34 | `x_w1m1_forecast` | Arm the bridge; the forecast shows kept-dry and newly flooded cells; place; the bus crosses | Read the water. Place the plan. Watch it work. | XR Sim, human |
| 34–48 | `x_w1m3_skiff` | Skiff loads residents, crosses, unloads at the safe dock | Every trip counts. | XR Sim, replay |
| 48–60 | `x_w2m1_fire` | Emberwood: a firebreak holds as the wind turns | Fire follows the wind. | XR Sim, replay (W2 gate) |
| 60–72 | `x_w3m1_snow` | Frostline: a brace holds; the runout misses the road | Snow remembers every load. | XR Sim, replay (W3 gate) |
| 72–84 | `x_w4m1_tide` | Tidemark: the tide rises; a relay keeps the lights on | The tide keeps its own clock. | XR Sim, replay (W4 gate) |
| 84–96 | `x_w5m1_lava` | Cinder Reach: coolant crusts a lava front; residents reach the shelter | Cool the flow. Open the way. | XR Sim, replay (W5 gate) |
| 96–114 | `q_room_tables` | The board finds a real table; compact layout on a small one; virtual table with no table | It finds your table. No table? It brings one. | **P5** Quest, human |
| 114–128 | `q_onehand_command` | One-hand play; then Command mode with head dwell and step controls | Two hands, one hand, or a steady look. | **P5** Quest, human |
| 128–144 | `x_debrief` | Results, the causal debrief, the aftercare postcard | See why it worked. Then find a better way. | XR Sim, human |
| 144–152 | `q_anchor_return` | Next session: the valley is still on the table, recovering | Your valley waits where you left it. | **P5** Quest, human |
| 152–160 | card | RESQ / Small worlds. Big saves. / Meta Quest 3 and 3S | — | title card |

**Sound:** the clips keep their game sound. The bed is the game's own music (`music_w1_resolve.wav`), ducked under game audio. There is no narration unless the user records one.

## Capture checklist

- **Every clip:** dev build from a known commit with dev controls hidden.
  - No personal items, screens or bystanders in room shots.
  - `input` says honestly whether a person played or a replay drove it.
- **XR Simulator:** record the compositor output at 1080p. A replay drives the input-free shots; W1 forecast/placement and the debrief are played by hand with simulated hands.
- **P5 device (12–15 Nov):** record two table setups plus the no-table case, one-hand play, Command dwell, and anchor return the next session. Record Quest 3 and 3S.
