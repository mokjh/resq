#!/bin/bash
# Place the camera explicitly around a board cell (orbit disabled) and capture:
#   tools/unity/closeup.sh <x> <y> <distance_m> <pitch_deg> <yaw_deg> <name> [fov]
set -e
ROOT=/Users/dev/Desktop/Resq
FOV=${7:-32}
python3 $ROOT/tools/unity/uc.py eval --code "var b = UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); var d = b.Driver.Def; var t = b.Driver.transform.TransformPoint(Resq.Game.BoardSpace.CellCenter(d, d.CellIndex($1, $2))); var cam = UnityEngine.Camera.main; var o = cam.GetComponent<Resq.Game.DesktopOrbit>(); if (o != null) o.enabled = false; cam.fieldOfView = ${FOV}f; cam.transform.position = t + UnityEngine.Quaternion.Euler(${4}f, ${5}f, 0) * new UnityEngine.Vector3(0, 0, -${3}f); cam.transform.LookAt(t); return t.ToString(\"F3\");" | tail -1
python3 $ROOT/tools/unity/uc.py editor_focus > /dev/null
sleep 3
bash $ROOT/tools/unity/shot.sh "$6" 1600 1000
