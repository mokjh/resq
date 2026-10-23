#!/bin/bash
# Per-mission render/frame budget screen in the open Editor (play mode must be running: tools/unity/replay.sh).
#   tools/unity/perf.sh [layout] [mission ...]     default: wide, all 25 missions
# Loads each mission at operation step 300 of strategy A, frames the seated view, averages 120 frames with
# PerfProbe and prints one line per mission. Evidence kind: automated, Editor (mono view, Editor overhead included).
ROOT=/Users/dev/Desktop/Resq
UC="$ROOT/tools/unity/uc.py"
L=${1:-wide}; shift
MISSIONS=${@:-"W1-M1 W1-M2 W1-M3 W1-M4 W1-M5 W2-M1 W2-M2 W2-M3 W2-M4 W2-M5 W3-M1 W3-M2 W3-M3 W3-M4 W3-M5 W4-M1 W4-M2 W4-M3 W4-M4 W4-M5 W5-M1 W5-M2 W5-M3 W5-M4 W5-M5"}
python3 "$UC" editor_focus > /dev/null
# measure the shipping HUD: without the development mission picker
python3 "$UC" eval --code "Resq.Game.OperationPanel.DevControlsOverride = false; return \"ok\";" > /dev/null
for M in $MISSIONS; do
  python3 "$UC" eval --code "var b = UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); var d = b.Driver; d.LoadRun(\"$M\", \"$L\", Resq.Sim.RuleProfile.Standard, false); d.StartAutopilot(\"A\"); d.Advance(300); d.SetPaused(false); var cam = UnityEngine.Camera.main; var o = cam.GetComponent<Resq.Game.DesktopOrbit>(); if (o != null) { o.enabled = true; o.Pitch = 52; o.Distance = 0.72f; o.Yaw = 0; cam.fieldOfView = 60f; } var old = b.GetComponent<Resq.Game.PerfProbe>(); if (old != null) UnityEngine.Object.Destroy(old); return \"ok\";" > /dev/null
  sleep 1
  python3 "$UC" eval --code "var b = UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); b.gameObject.AddComponent<Resq.Game.PerfProbe>(); return \"ok\";" > /dev/null
  r=""
  for i in $(seq 1 40); do
    sleep 1
    r=$(python3 "$UC" eval --code "return Resq.Game.PerfProbe.Done ? Resq.Game.PerfProbe.LastReport : \"\";" 2>/dev/null | tail -1 | tr -d '"')
    [ -n "$r" ] && [ "$r" != "null" ] && break
  done
  echo "$M $L $r"
done
python3 "$UC" eval --code "Resq.Game.OperationPanel.DevControlsOverride = null; var b = UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); var p = b.GetComponent<Resq.Game.PerfProbe>(); if (p != null) UnityEngine.Object.Destroy(p); var d = b.Driver; d.SetPaused(true); return \"ok\";" > /dev/null
