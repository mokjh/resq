#!/usr/bin/env python3
"""Hands-only journey in the Meta XR Simulator (needs tools/xr/sim_editor.sh in Play mode): a synthetic right hand aims its
pointer ray at each target in a closed loop and presses with a real pinch through the Interaction SDK. Nothing calls a
button's action or Submit. Evidence: simulator. Frames go to tools/.cache/xrsim/journey/.
    python3 tools/xr/hands_test.py"""
import json, math, os, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xrop import call, save_images

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
UC = os.path.join(ROOT, "tools", "unity", "uc.py")
OUT = os.path.join(ROOT, "tools", ".cache", "xrsim", "journey"); os.makedirs(OUT, exist_ok=True)
FAILS, shot_no = [], [0]

def log(s): print(f"[{time.strftime('%H:%M:%S')}] {s}", flush=True)

def ev(code, retries=6):
    for _ in range(retries):
        r = subprocess.run(["python3", UC, "eval", "--code", code], capture_output=True, text=True, timeout=60)
        line = r.stdout.strip().splitlines()[-1] if r.stdout.strip() else ""
        if line and "ERRORS" not in line: return json.loads(line) if line[0] in '"[{' else line
        time.sleep(1.5)
    raise RuntimeError("eval failed: " + (r.stdout + r.stderr)[-300:])

BOOT = "var b = UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); "
def state(): return ev(BOOT + 'var f = b.Flow; return f.State + " " + (f.Menu.Current != null ? f.Menu.Current.Id : "-");')

def target(label=None, name=None):
    pick = f'x.Label == "{label}"' if label else f'x.name == "{name}"'
    s = ev('var p = System.Linq.Enumerable.FirstOrDefault(UnityEngine.Object.FindObjectsByType<Resq.Game.Pickable>(UnityEngine.FindObjectsInactive.Exclude, UnityEngine.FindObjectsSortMode.None), x => ' + pick +
           ' && x.GetComponent<UnityEngine.Collider>() != null); if (p == null) return "none"; var c = p.GetComponent<UnityEngine.Collider>().bounds.center; return c.x.ToString("F4") + "," + c.y.ToString("F4") + "," + c.z.ToString("F4");')
    return None if s == "none" else [float(v) for v in s.split(",")]

# the right hand's pointer as the game itself reads it, and what it is hovering
def pointer():
    s = ev(BOOT + 'var fi = typeof(Resq.Game.BoardInteraction).GetField("_sources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); var list = (System.Collections.IEnumerable)fi.GetValue(b.Interaction); string o = "none"; '
           'foreach (var src in list) { var p = src as Resq.XR.IsdkHandPointer; if (p == null || p.Kind != Resq.Game.PointerKind.HandRight) continue; var r = p.LastRay; o = r.origin.x.ToString("F4") + "," + r.origin.y.ToString("F4") + "," + r.origin.z.ToString("F4") + "," + r.direction.x.ToString("F4") + "," + r.direction.y.ToString("F4") + "," + r.direction.z.ToString("F4") + "," + p.Tracked; } '
           'return o + "|" + (b.Interaction.Hover != null ? b.Interaction.Hover.name + "/" + b.Interaction.Hover.Label : "none");')
    ray, hover = s.split("|")
    if ray == "none": raise RuntimeError("no right hand pointer")
    v = ray.split(",")
    return [float(x) for x in v[:3]], [float(x) for x in v[3:6]], v[6] == "True", hover

def unit(v): n = math.sqrt(sum(x * x for x in v)) or 1; return [x / n for x in v]
def oxr(v): return [v[0], v[1], -v[2]]               # Unity <-> OpenXR, with the rig at the origin (checked at start)
def qmul(a, b):
    x1, y1, z1, w1 = a; x2, y2, z2, w2 = b
    return [w1*x2 + x1*w2 + y1*z2 - z1*y2, w1*y2 - x1*z2 + y1*w2 + z1*x2, w1*z2 + x1*y2 - y1*x2 + z1*w2, w1*w2 - x1*x2 - y1*y2 - z1*z2]
def qbetween(a, b):
    c = [a[1]*b[2] - a[2]*b[1], a[2]*b[0] - a[0]*b[2], a[0]*b[1] - a[1]*b[0]]; d = sum(x*y for x, y in zip(a, b))
    return unit4(c + [1 + d])
def unit4(q): n = math.sqrt(sum(x*x for x in q)) or 1; return [x/n for x in q]

WRIST = [0.18, 1.25, -0.22]                          # OpenXR: a relaxed right hand, elbow bent, in front of the seated player
wrist_q = [0.0, 0.0, 0.0, 1.0]

def set_hand(dur=0.25):
    r = call("openxr_set_hand_pose", {"hand": "right", "wrist_position": WRIST, "wrist_orientation": wrist_q, "base_space": "local_floor", "duration_seconds": dur, "reason": "aim the hand ray"})
    if r.get("isError"): raise RuntimeError(str(r)[:300])
    time.sleep(dur + 0.25)

def aim(t, tries=14):
    global wrist_q
    for i in range(tries):
        o, d, tracked, hover = pointer()
        want = unit([t[k] - o[k] for k in range(3)])
        err = math.degrees(math.acos(max(-1.0, min(1.0, sum(x*y for x, y in zip(d, want))))))
        if err < 0.7: return err, hover
        wrist_q = unit4(qmul(qbetween(oxr(d), oxr(want)), wrist_q))
        set_hand()
    return err, hover

def frame(tag):
    shot_no[0] += 1
    p = os.path.join(OUT, f"{shot_no[0]:02d}_{tag}.png")
    save_images(call("openxr_capture_composited_image", {"eye": "left", "reason": tag}), p)

def press(what, label=None, name=None, expect=None):
    t = target(label, name)
    if t is None: FAILS.append(f"{what}: target not on screen ({state()})"); log(FAILS[-1]); return False
    err, hover = aim(t)
    log(f"{what}: aimed to {err:.1f}°, hovering {hover}")
    call("openxr_hand_gesture", {"hand": "right", "gesture": "open", "reason": "ready"}); time.sleep(0.3)
    frame(what.replace(" ", "_") + "_aim")
    r = call("openxr_hand_gesture", {"hand": "right", "gesture": "pinch", "auto_release": True, "hold_duration": 0.35, "reason": "press " + what})
    if r.get("isError"): FAILS.append(f"{what}: pinch refused {str(r)[:200]}"); log(FAILS[-1]); return False
    time.sleep(1.4)
    now = state(); log(f"{what}: now {now}")
    if expect and expect not in now: FAILS.append(f"{what}: expected {expect}, got {now}"); log(FAILS[-1]); return False
    return True

def main():
    log("head " + json.loads(call("openxr_get_head_pose", {"base_space": "local_floor"})["content"][0]["text"])["pose"]["position"].__str__())
    call("openxr_hand_gesture", {"hand": "right", "gesture": "aim", "reason": "start"}); set_hand(0.4)
    log("start: " + state()); frame("start")
    steps = [("Begin", "Begin", None, "Placement"), ("Done", "Done", None, "Hub"), ("Continue", None, None, "Briefing"), ("Skip the guide", "Skip the guide", None, "Operation")]
    now = state()
    if "Welcome" not in now: steps = steps[1:]
    if "Placement" not in now and "Welcome" not in now: steps = steps[1:]
    for what, label, name, expect in steps:
        if what == "Continue":
            lab = "Continue" if target("Continue") else "Start"
            press(lab, label=lab, expect=expect)
        else:
            press(what, label=label, expect=expect)
        if FAILS: break
    if not FAILS:
        time.sleep(1.5); frame("operation")
        mission = ev(BOOT + 'return b.Driver.MissionId + " paused " + b.Driver.Paused;'); log("mission " + mission)
        press("Barrier card", label="Flood Barrier")
        log("armed: " + ev(BOOT + 'return b.Interaction.Mode + " " + b.Interaction.ArmedTool;'))
        press("Barrier spot 3", name="Socket_barrier:03")
        time.sleep(0.6)
        placed = ev(BOOT + 'var S = b.Driver.Sim; return "supplies " + S.RemainingSupplies + " mode " + b.Interaction.Mode;'); log(placed); frame("barrier_placed")
        press("Start", label="Start")
        time.sleep(4); r = ev(BOOT + 'return "tick " + b.Driver.Sim.S.C.O + " paused " + b.Driver.Paused + " supplies " + b.Driver.Sim.RemainingSupplies;'); log(r); frame("running")
    print("RESULT:", "PASS" if not FAILS else "FAIL " + "; ".join(FAILS))
    sys.exit(0 if not FAILS else 1)


if __name__ == "__main__":
    main()
