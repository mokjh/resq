#!/bin/bash
# Load a mission in the playing Operation scene, run a validated strategy on autopilot for N operation steps,
# frame the board and capture: tools/unity/run_mission.sh W2-M4 A 900 [wide|compact] [shot-name]
set -e
ROOT=/Users/dev/Desktop/Resq
M=${1:-W1-M1}; S=${2:-A}; N=${3:-600}; L=${4:-wide}; NAME=${5:-$M-$S-$N}
python3 $ROOT/tools/unity/uc.py eval --code "var b = UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); var d = b.Driver; d.LoadRun(\"$M\", \"$L\", Resq.Sim.RuleProfile.Standard, false); d.StartAutopilot(\"$S\"); d.Advance($N); var cam = UnityEngine.Camera.main; var o = cam.GetComponent<Resq.Game.DesktopOrbit>(); if (o != null) { o.enabled = true; o.Pitch = 52; o.Distance = 0.72f; o.Yaw = 0; cam.fieldOfView = 60f; } var s = d.Sim.S; return \"S=\" + s.C.S + \" rescued=\" + s.C.Evacuated + \"/\" + d.Def.TotalResidents + \" backup=\" + s.C.AwaitingBackup;" | tail -1
python3 $ROOT/tools/unity/uc.py editor_focus > /dev/null   # play mode only repaints while the Editor is focused
sleep 3
bash $ROOT/tools/unity/shot.sh "$NAME" 1600 1000
