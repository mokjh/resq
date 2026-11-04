#!/bin/bash
# RESQ's Editor in Play mode on Operation_XR against the Meta XR Simulator, with the XR Operator layer (MCP on :8720) for
# scripted hands, head pose and composited frames. No sudo: only this Editor sees the Simulator runtime.
# Evidence from these sessions is "simulator". Optional: RESQ_DEV_ROOM=<MRUK sample room prefab>. Blocks until the Editor exits.
ROOT=/Users/dev/Desktop/Resq
UNITY=/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents/MacOS/Unity
OP="$(ls -d "$HOME/Library/Application Support/metavr/tools/meta-xr-operator/"*/macos | tail -1)"
export PATH="$HOME/.metavr/bin:$PATH"
metavr xrsim app ensure-running > /dev/null 2>&1
export XR_RUNTIME_JSON=/Applications/MetaXRSimulator.app/Contents/Resources/MetaXRSimulator/meta_openxr_simulator.json
export XR_API_LAYER_PATH="$OP" XR_ENABLE_API_LAYERS=XR_APILAYER_METAX_operator
exec "$UNITY" -projectPath "$ROOT/game" -executeMethod Resq.EditorTools.SimSession.Play -logFile "$ROOT/tools/.cache/editor.log"
