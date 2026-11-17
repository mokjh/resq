#!/usr/bin/env python3
"""Trailer shots of hands and menus (same session and camera as tools/xr/capture.py; reuses its room still).
  c_place     real Interaction SDK pinches: Flood Barrier card, aim at a spot (the forecast shows), place, Start
  c_missions  mission select across the five worlds
  c_results   a won First Crossing: results, then the debrief
  c_aftercare the valley recovering after the win
  c_settings  comfort settings (text size, one hand, hands-free)
  c_table     the board card with "Use my table" over the virtual table
Evidence: simulator. Gameplay at 1x except where the sidecar says otherwise.
    python3 tools/xr/capture_ui.py [shot ...]"""
import json, os, shutil, sys, threading, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hands_test as H
from hands_test import ev, call, BOOT, log, ROOT
from capture import CMD, CAP, LOG, HEAD, SEATED, commit

BG = os.path.join(CAP, "c_w1m1_flood_room.png")
FLOW = 'var F = typeof(Resq.Game.GameFlow); var NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; '
def flow(method, args="null"): return ev(BOOT + FLOW + f'F.GetMethod("{method}", NP).Invoke(b.Flow, {args}); return b.Flow.State + " " + (b.Flow.Menu.Current != null ? b.Flow.Menu.Current.Id : "-");')

def record(sid, frames, during=None, speed=1, note=""):
    mark = os.path.getsize(LOG)
    ev(BOOT + f'Resq.Game.SimDriver.FixedFrameStep = {speed}f / 60f; return "ok";')
    with open(CMD, "w") as f: json.dump({"name": sid, "frames": frames, "eye": True, "bg": BG}, f)
    if during: threading.Thread(target=during, daemon=True).start()
    t0 = time.time()
    while time.time() - t0 < 1800:
        time.sleep(2)
        with open(LOG, errors="ignore") as f: f.seek(mark); tail = f.read()
        done = [l for l in tail.splitlines() if l.startswith(f"[ResqCap] done {sid} ")]
        if done: break
    else: raise RuntimeError(sid + ": no done line")
    shutil.move(os.path.join(CAP, sid + ".mp4"), os.path.join(ROOT, "video", "raw", sid + ".mp4"))
    with open(os.path.join(ROOT, "video", "raw", sid + ".json"), "w") as f:
        json.dump({"kind": "xrsim", "input": "human-scripted" if sid == "c_place" else "flow", "speed": speed, "build": commit(),
                   "captured": time.strftime("%Y-%m-%d"), "note": note}, f, indent=1)
    log(f"{sid}: {done[0]} ({time.time() - t0:.0f} s)")

def start_run(mission="W1-M1"):
    # StartMission is the flow's own entry (briefing skipped); the run waits paused for planning until Start
    return ev(BOOT + FLOW + f'b.Flow.Menu.Hide(); F.GetMethod("StartMission", NP).Invoke(b.Flow, new object[] {{ "{mission}", false, null, null }}); return b.Flow.State + " paused " + b.Driver.Paused;')

def hands_place():
    H.FAILS.clear()
    call("openxr_hand_gesture", {"hand": "right", "gesture": "aim", "reason": "start"}); H.set_hand(0.4)
    H.press("Barrier card", label="Flood Barrier")
    t = H.target(name="Socket_barrier:03")
    if t: H.aim(t); time.sleep(1.5)                       # hovering the spot: the forecast shows
    H.press("Barrier spot 3", name="Socket_barrier:03"); time.sleep(0.8)
    H.press("Start", label="Start")
    log("place: " + ("ok" if not H.FAILS else "; ".join(H.FAILS)))

def main():
    only = set(sys.argv[1:]); want = lambda s: not only or s in only
    call("openxr_set_head_pose", {"position": HEAD, "orientation": SEATED, "base_space": "local_floor", "duration_seconds": 0.3, "reason": "same camera as the world shots"})
    if want("c_place"):
        log(start_run()); time.sleep(1.0)
        record("c_place", 60 * 30, during=hands_place, note="real pinches with simulated hands through the Interaction SDK (driven by script)")
        for h, x in (("left", -0.9), ("right", 0.9)):
            call("openxr_set_hand_pose", {"hand": h, "wrist_position": [x, 0.6, 0.6], "base_space": "local_floor", "duration_seconds": 0.3, "reason": "hands out of shot"})
    if want("c_missions"):
        log(flow("ShowMissions", 'new object[] { "W1" }')); time.sleep(0.5)
        def worlds():
            for w in ["W2", "W3", "W4", "W5"]:
                time.sleep(1.6); flow("ShowMissions", f'new object[] {{ "{w}" }}')
        record("c_missions", 60 * 9, during=worlds, note="mission select, worlds switched by the flow")
    if want("c_results") or want("c_aftercare"):
        log(start_run())
        log(ev(BOOT + 'b.Driver.StartAutopilot("A"); int n = 0; while (!b.Driver.Sim.IsOver && n < 20000) { b.Driver.Advance(1); n++; } b.Driver.SetPaused(false); return "over " + b.Driver.Sim.IsOver + " success " + b.Driver.Sim.S.C.Success;'))
        time.sleep(2.0); log("after win: " + ev(BOOT + 'return b.Flow.State + " " + (b.Flow.Menu.Current != null ? b.Flow.Menu.Current.Id : "-");'))
        if want("c_results"):
            record("c_results", 60 * 8, during=lambda: (time.sleep(2.5), flow("ShowDebrief")), note="results after a win by strategy A, then the debrief")
        if want("c_aftercare"):
            log(flow("ShowAftercare")); record("c_aftercare", 60 * 8, note="aftercare: the valley recovers")
    if want("c_settings"):
        log(flow("ShowSettings", 'new object[] { Resq.Game.FlowState.Hub }')); record("c_settings", 60 * 5, note="comfort settings")
    if want("c_table"):
        log(flow("ShowCalibration", 'new object[] { "On a virtual table. \\"Use my table\\" puts it on your real one" }')); record("c_table", 60 * 5, note="the board card with Use my table")
    ev(BOOT + 'Resq.Game.SimDriver.FixedFrameStep = 1f / 60f; return "ok";')

if __name__ == "__main__":
    main()
