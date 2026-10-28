#!/bin/bash
# Real captures for the website (web/app/media), from the running game in the open RESQ Editor.
# Each shot loads a mission, plays its validated strategy A through the normal command path to a chosen tick,
# frames the desktop camera, and captures the Game view. Evidence kind: Editor capture (labelled as such on the site).
set -e
ROOT=/Users/dev/Desktop/Resq
UC="python3 $ROOT/tools/unity/uc.py"
MEDIA=$ROOT/web/app/media
mkdir -p "$MEDIA"
ev() { $UC eval --code "$1" | tail -1; }
focus() {  # bring the Editor forward and wait until the game has really rendered 30 new frames (HUD and views
           # refresh in LateUpdate; a camera capture alone doesn't tick the player loop)
  $UC editor_focus > /dev/null 2>&1
  local f0 f1 i
  f0=$(ev 'return UnityEngine.Time.frameCount.ToString();')
  for i in $(seq 1 60); do
    sleep 1
    f1=$(ev 'return UnityEngine.Time.frameCount.ToString();')
    [ "$f1" -ge $((f0 + 30)) ] 2>/dev/null && return 0
    $UC editor_focus > /dev/null 2>&1
  done
  echo "  (frames did not advance: $f0 -> $f1)"; return 1
}
shot() {  # name width height
  bash $ROOT/tools/unity/shot.sh "web_$1" ${2:-1920} ${3:-1080} > /dev/null
  ffmpeg -loglevel error -y -i "$ROOT/tools/.cache/shots/web_$1.png" -q:v 3 "$MEDIA/$1.jpg"
  echo "  $1.jpg"
}
want() { [ -z "$ONLY" ] || [[ " $ONLY " == *" $1 "* ]]; }   # ONLY="w3 act" recaptures just those shots
# mission, tick, pitch, distance, yaw
stage() {
  ev "var b=UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); var d=b.Driver; d.LoadRun(\"$1\",\"wide\",Resq.Sim.RuleProfile.Standard,false); d.StartAutopilot(\"A\"); for(int i=0;i<$2&&!d.Sim.IsOver;i++) d.Advance(1); var o=UnityEngine.Camera.main.GetComponent<Resq.Game.DesktopOrbit>(); o.Target=b.BoardRoot.position; o.Pitch=$3f; o.Distance=$4f; o.Yaw=$5f; return d.Sim.S.C.O.ToString();" > /dev/null
  focus
}

$UC editor_stop > /dev/null 2>&1 || true
ev 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Resq/Scenes/Operation_Desktop.unity"); return "ok";' > /dev/null
$UC editor_play > /dev/null; sleep 15; focus
# release builds have no development picker: keep it out of public images (checked, not assumed)
[ "$(ev 'Resq.Game.OperationPanel.DevControlsOverride = false; return Resq.Game.OperationPanel.DevControlsOverride.ToString();')" = "False" ] || { echo "could not hide dev controls"; exit 1; }

echo "captures:"
want hero && { stage W1-M1 700 34 0.64 0;  shot hero; }
want w1 && { stage W1-M1 300 40 0.70 0;  shot w1; }
want w2 && { stage W2-M1 900 40 0.70 0;  shot w2; }
want w3 && { stage W3-M1 900 40 0.70 0;  shot w3; }
want w4 && { stage W4-M1 900 40 0.70 0;  shot w4; }
want w5 && { stage W5-M1 900 40 0.70 0;  shot w5; }
want act && { stage W1-M1 150 32 0.56 -6; shot act; }
# plan: just before W1-M1's first surge (no orders yet), arm a barrier and show the forecast for the spot that
# changes the most cells: the real simulation run ahead, not a mock-up
if want plan; then
ev 'var b=UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); var d=b.Driver; d.LoadRun("W1-M1","wide",Resq.Sim.RuleProfile.Standard,false); var o=UnityEngine.Camera.main.GetComponent<Resq.Game.DesktopOrbit>(); o.Target=b.BoardRoot.position; o.Pitch=46f; o.Distance=0.62f; o.Yaw=0f; b.Interaction.DebugArm(Resq.Sim.ToolKind.Barrier); return "armed";' > /dev/null
focus   # the board shows its sockets a frame after a tool is armed
ev 'var b=UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); var i=b.Interaction; var f=b.Board.Forecast; Resq.Game.Pickable best=null; int most=-1; foreach(var p in UnityEngine.Object.FindObjectsByType<Resq.Game.Pickable>(UnityEngine.FindObjectsSortMode.None)) if(p.Kind==Resq.Game.PickKind.Socket && i.Evaluate(p)==Resq.Sim.Reject.OK){ f.Preview(p.Index, Resq.Sim.ToolKind.Barrier, true); if(f.ChangedCells>most){most=f.ChangedCells; best=p;} } i.DebugHover=best; f.Preview(best.Index, Resq.Sim.ToolKind.Barrier, true); return f.Summary + " changed=" + f.ChangedCells;'
focus; shot plan
ev 'var b=UnityEngine.Object.FindFirstObjectByType<Resq.Game.GameBootstrap>(); b.Interaction.DebugHover=null; b.Interaction.Cancel(); return "ok";' > /dev/null
fi
# rescue: finish a run; the game flow shows the results card
if want rescue; then
stage W1-M1 20000 26 0.62 0
sleep 4; focus; shot rescue
fi
# menu: the mission list for one world
if want menu; then
ev 'var f=Resq.Game.GameFlow.Instance; f.ShowHub(); foreach (var p in UnityEngine.Object.FindObjectsByType<Resq.Game.Pickable>(UnityEngine.FindObjectsSortMode.None)) if (p.Kind==Resq.Game.PickKind.Button && p.Label=="Missions") { p.OnPress(); break; } return f.State.ToString();' > /dev/null
sleep 2; focus; shot menu
fi
$UC editor_stop > /dev/null 2>&1 || true
ffmpeg -loglevel error -y -i "$ROOT/game/Assets/Resq/Content/Brand/app_icon.png" -vf scale=256:256 "$MEDIA/icon.png"
echo done
