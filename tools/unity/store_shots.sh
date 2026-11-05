#!/bin/bash
# Store screenshots at 2560x1440: each world's first mission mid-run, rendered in the Editor (evidence: Editor render, to be
# replaced by device captures in P5). Waits for no other Unity job, opens RESQ's Editor, closes it afterwards.
ROOT=/Users/dev/Desktop/Resq; cd "$ROOT" || exit 1; UC=tools/unity/uc.py
UNITY=/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents/MacOS/Unity
OUT=docs/submission/store; mkdir -p "$OUT"
q=0; w=0; while [ "$w" -lt 14400 ]; do if [ "$(pgrep -x Unity | wc -l | tr -d ' ')" = "0" ]; then q=$((q+15)); else q=0; fi; [ "$q" -ge 45 ] && break; sleep 15; w=$((w+15)); done
[ "$q" -ge 45 ] || { echo "gave up waiting"; exit 3; }
nohup "$UNITY" -projectPath "$ROOT/game" -logFile "$ROOT/tools/.cache/editor.log" > /dev/null 2>&1 &
sleep 30; for i in $(seq 1 100); do python3 $UC editor_focus > /dev/null 2>&1; python3 $UC eval --code 'return "alive";' 2>&1 | tail -1 | grep -q alive && break; sleep 5; done
python3 $UC eval --code 'if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().path.EndsWith("Operation_Desktop.unity")) UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Resq/Scenes/Operation_Desktop.unity"); return "scene";' > /dev/null 2>&1; sleep 8
for i in 1 2 3 4 5 6; do python3 $UC editor_play > /dev/null 2>&1; sleep 6; python3 $UC editor_status 2>/dev/null | grep -q '"playMode": "playing"' && break; done; sleep 12
n=1
for spec in W1-M1:1500:Alder_Valley W2-M1:900:Emberwood W3-M1:1200:Frostline W4-M1:1200:Tidemark W5-M1:900:Cinder_Reach; do
  IFS=: read -r m t nm <<< "$spec"
  python3 $UC editor_focus > /dev/null 2>&1
  python3 $UC eval --code "var b = UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); b.Flow.Menu.Hide(); b.Interaction.MenuOnly = false; b.Driver.LoadRun(\"$m\", \"wide\", Resq.Sim.RuleProfile.Standard, false); b.Driver.StartAutopilot(\"A\"); while (b.Driver.Sim.S.C.O < $t && !b.Driver.Sim.IsOver) b.Driver.Advance(1); b.Driver.SetPaused(true); var cam = UnityEngine.Camera.main; var o = cam.GetComponent<Resq.Game.DesktopOrbit>(); if (o != null) o.enabled = false; var fwd = b.BoardRoot.forward; cam.transform.position = b.BoardRoot.position - fwd * 0.62f + UnityEngine.Vector3.up * 0.40f; cam.transform.LookAt(b.BoardRoot.position + UnityEngine.Vector3.up * 0.03f); cam.fieldOfView = 52f; return \"$m \" + b.Driver.Sim.S.C.O;" 2>&1 | tail -1
  python3 $UC editor_focus > /dev/null 2>&1; sleep 4
  bash tools/unity/shot.sh "store_${n}" 2560 1440 > /dev/null 2>&1 && cp "tools/.cache/shots/store_${n}.png" "$OUT/screenshot_${n}_${nm}.png"
  n=$((n+1))
done
python3 $UC editor_stop > /dev/null 2>&1; sleep 3
E=$(pgrep -f "Contents/MacOS/Unity -projectPath $ROOT/game" | head -1); [ -n "$E" ] && kill -TERM "$E"
sleep 10; ls -la "$OUT" | cut -c30-110
