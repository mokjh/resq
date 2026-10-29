#!/bin/bash
# Website object shots: each world's valley mid-run as a cut-out object, plus a close detail around its residents.
# Needs the RESQ Editor open; plays each mission's validated strategy A through the normal command path, then
# Resq.EditorTools.ObjectShot renders black/white pairs (HUD, tray, menu card and labels hidden) and
# tools/web/cutout.py turns them into web/app/media/obj_*.webp (transparent) and det_*.webp.
# Evidence kind: Editor render of the running game (labelled on the site).   ONLY="w2 det_w2" limits the run.
set -e
trap 'echo "capture stopped at line $LINENO: $BASH_COMMAND"' ERR
ROOT=/Users/dev/Desktop/Resq
UC="python3 $ROOT/tools/unity/uc.py"
ev() { $UC eval --timeout 60 --code "$1" | tail -1; }
want() { [ -z "$ONLY" ] || [[ " $ONLY " == *" $1 "* ]]; }
frames() {  # wait until the game has drawn 30 more frames so the views show the staged state
  $UC editor_focus > /dev/null 2>&1
  local f0 f1 i; f0=$(ev 'return UnityEngine.Time.frameCount.ToString();')
  for i in $(seq 1 60); do
    sleep 1; f1=$(ev 'return UnityEngine.Time.frameCount.ToString();')
    [ "$f1" -ge $((f0 + 30)) ] 2>/dev/null && return 0
    $UC editor_focus > /dev/null 2>&1
  done
  echo "  (frames did not advance: $f0 -> $f1)"; return 1
}
stage() {  # mission tick
  ev "Resq.Game.OperationPanel.DevControlsOverride=false; var d=UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>().Driver; d.LoadRun(\"$1\",\"wide\",Resq.Sim.RuleProfile.Standard,false); d.StartAutopilot(\"A\"); for(int i=0;i<$2&&!d.Sim.IsOver;i++) d.Advance(1); return d.Sim.S.C.O.ToString();" > /dev/null
  frames
}
board() {  # name yaw pitch
  ev "return Resq.EditorTools.ObjectShot.Board(\"$1\",2400,1600,${2}f,${3}f);" > /dev/null; echo "  $1"
}
detail() {  # name yaw pitch distance: around the first resident still standing on the board
  ev "UnityEngine.Transform pick=null; foreach(var t in UnityEngine.GameObject.Find(\"Actors\").GetComponentsInChildren<UnityEngine.Transform>()) if(t.name.StartsWith(\"Resident_\")&&t.gameObject.activeInHierarchy){pick=t;break;} if(pick==null) return \"none\"; return Resq.EditorTools.ObjectShot.Detail(\"$1\",1200,1500,pick.position+new UnityEngine.Vector3(0,0.012f,0),${2}f,${3}f,${4}f);" > /dev/null; echo "  $1"
}

if [ "$(ev 'return UnityEditor.EditorApplication.isPlaying.ToString();')" != "True" ]; then
  ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Resq/Scenes/Operation_Desktop.unity"); return "ok";' > /dev/null || true
  for i in 1 2 3 4 5 6; do   # editor_play can report an error while the bridge reloads: retry until confirmed
    $UC editor_play > /dev/null 2>&1 || true; sleep 10
    [ "$(ev 'return UnityEditor.EditorApplication.isPlaying.ToString();' 2>/dev/null)" = "True" ] && break
  done
fi
frames

echo "renders:"
for spec in "w1 W1-M1 700" "w2 W2-M1 900" "w3 W3-M1 900" "w4 W4-M1 900" "w5 W5-M1 900"; do
  set -- $spec
  if want "$1" || want "det_$1" || { [ "$1" = w1 ] && want hero; }; then
    stage "$2" "$3"
    want "$1" && board "obj_$1" -30 34
    [ "$1" = w1 ] && want hero && board "obj_hero" -38 24
    want "det_$1" && detail "det_$1" -20 30 0.16
  fi
done

cd "$ROOT"
objs=$(ls tools/.cache/objects/obj_*_b.png 2>/dev/null | xargs -n1 basename | sed 's/_b.png//' | grep -v _test || true)
dets=$(ls tools/.cache/objects/det_*_b.png 2>/dev/null | xargs -n1 basename | sed 's/_b.png//' || true)
uv run -q --with pillow --with numpy python3 tools/web/cutout.py $objs --width 1800
uv run -q --with pillow --with numpy python3 tools/web/cutout.py --opaque $dets --width 1000
echo done
