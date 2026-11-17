#!/usr/bin/env python3
"""Trailer footage, frame-stepped (needs `RESQ_CAPTURE=1 bash tools/xr/sim_editor.sh` in Play mode).

For each shot: the mission is loaded and its validated strategy A plays through the normal command path (autopilot) up to
a tick where the hazard is in play. The board is hidden for a moment so the XR Operator can grab a composited left-eye
still of the Simulator room. Then ResqCapture (game/Assets/Resq/Editor/Capture) renders every frame from the exact left
eye over that still, with the simulation stepped 1/60 s per frame, so the clip plays at true game speed.
Output: video/raw/<shot>.mp4 + <shot>.json (edit.py sidecar) + cap/<shot>.jsonl (frame log, sounds). Evidence: simulator.
    python3 tools/xr/capture.py [shot_id ...]"""
import json, os, shutil, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hands_test import ev, call, save_images, BOOT, log, ROOT

SEATED = [-0.1392, 0.0, 0.0, 0.9903]   # 16° down: the whole board, centred, with the room around it
HEAD = [0.12, 1.68, 0.08]              # floor space, chosen from framing stills on 17 Nov
SPEED = 4                              # game time per real second in the clip (disclosed on screen: "4x speed")
CMD = os.path.join(ROOT, "tools", ".cache", "cap", "cmd.json")
CAP = os.path.join(ROOT, "video", "raw", "cap")
LOG = os.path.join(ROOT, "tools", ".cache", "editor.log")
# id, mission, operation tick to fast-forward to, seconds of game time to film
SHOTS = [("c_w1m1_flood", "W1-M1", 100, 14), ("c_w1m3_skiff", "W1-M3", 100, 12), ("c_w2m1_fire", "W2-M1", 100, 12),
         ("c_w3m1_snow", "W3-M1", 100, 12), ("c_w4m1_tide", "W4-M1", 100, 12), ("c_w5m1_lava", "W5-M1", 100, 12)]

def commit(): return subprocess.run(["git", "-C", ROOT, "rev-parse", "--short", "HEAD"], capture_output=True, text=True).stdout.strip()

def prepare(mission, tick):
    return ev(BOOT + f'b.Flow.Menu.Hide(); b.Interaction.MenuOnly = false; b.Driver.LoadRun("{mission}", "wide", Resq.Sim.RuleProfile.Standard, false); b.Driver.StartAutopilot("A"); '
              f'while (b.Driver.Sim.S.C.O < {tick} && !b.Driver.Sim.IsOver) b.Driver.Advance(1); b.Driver.SetPaused(true); return "tick " + b.Driver.Sim.S.C.O + " over " + b.Driver.Sim.IsOver;')

def room_still(path):
    ev(BOOT + 'b.BoardRoot.gameObject.SetActive(false); return "hidden";'); time.sleep(1.0)
    n = save_images(call("openxr_capture_composited_image", {"eye": "left", "reason": "room background for footage"}), path)
    ev(BOOT + 'b.BoardRoot.gameObject.SetActive(true); return "shown";'); time.sleep(0.5)
    return n

def film(sid, frames, bg):
    mark = os.path.getsize(LOG) if os.path.exists(LOG) else 0
    os.makedirs(os.path.dirname(CMD), exist_ok=True)
    ev(BOOT + f'Resq.Game.SimDriver.FixedFrameStep = {SPEED}f / 60f; b.Driver.SetPaused(false); return "running";')
    with open(CMD, "w") as f: json.dump({"name": sid, "frames": frames, "eye": True, "bg": bg}, f)
    t0 = time.time()
    while time.time() - t0 < 1800:
        time.sleep(3)
        with open(LOG, errors="ignore") as f:
            f.seek(mark); tail = f.read()
        for line in tail.splitlines():
            if line.startswith(f"[ResqCap] done {sid} "): return line, time.time() - t0
    raise RuntimeError(f"{sid}: no done line in 30 min")

def main():
    only = set(sys.argv[1:]); raw = os.path.join(ROOT, "video", "raw"); os.makedirs(CAP, exist_ok=True)
    call("openxr_set_head_pose", {"position": HEAD, "orientation": SEATED, "base_space": "local_floor", "duration_seconds": 0.3, "reason": "seat the head at the board"})
    for h, x in (("left", -0.34), ("right", 0.34)):
        call("openxr_set_hand_pose", {"hand": h, "wrist_position": [x * 2.6, 0.6, 0.6], "base_space": "local_floor", "duration_seconds": 0.3, "reason": "hands out of shot"})
    for sid, mission, tick, seconds in SHOTS:
        if only and sid not in only: continue
        log(f"{sid}: {prepare(mission, tick)}"); time.sleep(1.5)
        bg = os.path.join(CAP, sid + "_room.png")
        if room_still(bg) < 1: raise RuntimeError("no room still")
        line, took = film(sid, seconds * 60, bg)
        shutil.move(os.path.join(CAP, sid + ".mp4"), os.path.join(raw, sid + ".mp4"))
        with open(os.path.join(raw, sid + ".json"), "w") as f:
            json.dump({"kind": "xrsim", "input": "autopilot", "speed": SPEED, "mission": mission, "build": commit(), "captured": time.strftime("%Y-%m-%d"),
                       "note": "Meta XR Simulator: frame-stepped game render from the exact left eye over a composited still of the Simulator room; strategy A through the command path"}, f, indent=1)
        log(f"{sid}: {line} ({took:.0f} s)")
    ev(BOOT + 'Resq.Game.SimDriver.FixedFrameStep = 1f / 60f; b.Driver.SetPaused(true); return "paused";')

if __name__ == "__main__":
    main()
