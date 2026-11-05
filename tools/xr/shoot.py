#!/usr/bin/env python3
"""Simulator footage for the trailer (needs tools/xr/sim_editor.sh in Play mode). The simulator's MP4 recorder is unsupported
on macOS, so each shot grabs composited left-eye frames through the XR Operator, stamps them with wall time and encodes a
constant-rate MP4 in video/raw/ with its sidecar. The mission's validated strategy A plays through the normal command path,
which the sidecar records honestly as input "autopilot". Real game, real simulation; no AI video (competition rule).
This Mac films only 2 to 6 frames a second, so the game runs slowed (PACE of real time) during capture and the frames are
placed at their game time, which gives smooth motion at the game's true speed. The sidecar records the pace.
    python3 tools/xr/shoot.py [shot_id ...]"""
import json, os, shutil, subprocess, sys, tempfile, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hands_test import ev, call, save_images, BOOT, log, ROOT
FPS = 30
PACE = 0.12   # game speed while filming
SEATED = [-0.2164, 0.0, 0.0, 0.9763]   # 25° down, at the board
# id, mission, operation tick to fast-forward to (hazards in play, the plan under way), seconds to film
SHOTS = [("x_w1m1_flood", "W1-M1", 1500, 16), ("x_w1m3_skiff", "W1-M3", 1200, 16), ("x_w2m1_fire", "W2-M1", 900, 14),
         ("x_w3m1_snow", "W3-M1", 1200, 14), ("x_w4m1_tide", "W4-M1", 1200, 14), ("x_w5m1_lava", "W5-M1", 900, 14)]

def commit(): return subprocess.run(["git", "-C", ROOT, "rev-parse", "--short", "HEAD"], capture_output=True, text=True).stdout.strip()

def prepare(mission, tick):
    return ev(BOOT + f'b.Flow.Menu.Hide(); b.Interaction.MenuOnly = false; b.Driver.LoadRun("{mission}", "wide", Resq.Sim.RuleProfile.Standard, false); b.Driver.StartAutopilot("A"); '
              f'while (b.Driver.Sim.S.C.O < {tick} && !b.Driver.Sim.IsOver) b.Driver.Advance(1); b.Driver.SetPaused(false); UnityEngine.Time.timeScale = {PACE}f; b.Driver.CapturePace = {PACE}f; return "tick " + b.Driver.Sim.S.C.O + " over " + b.Driver.Sim.IsOver;')

def film(out, seconds):
    tmp = tempfile.mkdtemp(prefix="resq_frames_"); stamps = []; t0 = time.time()
    while time.time() - t0 < seconds / PACE:
        p = os.path.join(tmp, f"f{len(stamps):05d}.png")
        if save_images(call("openxr_capture_composited_image", {"eye": "left", "reason": "footage"}), p): stamps.append((p, time.time()))
    if len(stamps) < 2: raise RuntimeError("no frames")
    with open(os.path.join(tmp, "list.txt"), "w") as f:
        for (p, t), (_, tn) in zip(stamps, stamps[1:] + [(None, stamps[-1][1] + 1.0 / FPS)]):
            f.write(f"file '{p}'\nduration {max((tn - t) * PACE, 1.0 / FPS):.4f}\n")
        f.write(f"file '{stamps[-1][0]}'\n")
    # 16:9 from the eye's square-ish frame, centred a little below the middle where the board sits
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", os.path.join(tmp, "list.txt"),
                    "-vf", f"crop=iw:iw*9/16:0:(ih-iw*9/16)*0.72,scale=1920:1080:flags=lanczos,fps={FPS},format=yuv420p", "-c:v", "libx264", "-crf", "18", "-an", out], check=True)
    rate = len(stamps) / (stamps[-1][1] - t0); shutil.rmtree(tmp, ignore_errors=True)
    return len(stamps), rate

def main():
    only = set(sys.argv[1:]); raw = os.path.join(ROOT, "video", "raw"); os.makedirs(raw, exist_ok=True)
    call("openxr_set_head_pose", {"orientation": SEATED, "base_space": "local_floor", "duration_seconds": 0.3, "reason": "seat the head at the board"})
    for h, x in (("left", -0.34), ("right", 0.34)):
        call("openxr_set_hand_pose", {"hand": h, "wrist_position": [x, 1.0, -0.15], "base_space": "local_floor", "duration_seconds": 0.3, "reason": "hands at rest, out of shot"})
    for sid, mission, tick, seconds in SHOTS:
        if only and sid not in only: continue
        log(f"{sid}: {prepare(mission, tick)}"); time.sleep(2.0)
        out = os.path.join(raw, sid + ".mp4")
        n, rate = film(out, seconds)
        with open(os.path.join(raw, sid + ".json"), "w") as f:
            json.dump({"kind": "xrsim", "input": "autopilot", "capture_pace": PACE, "mission": mission, "build": commit(), "captured": time.strftime("%Y-%m-%d"), "note": "composited left eye through the XR Operator; strategy A"}, f, indent=1)
        log(f"{sid}: {n} frames, {rate:.1f} fps captured -> {out}")
    ev(BOOT + 'UnityEngine.Time.timeScale = 1f; b.Driver.CapturePace = 1f; return "normal speed";')

if __name__ == "__main__":
    main()
