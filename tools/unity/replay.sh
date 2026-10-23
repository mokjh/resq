#!/bin/bash
# Stop play, let any pending import/compile finish for real, then enter play mode on the operation scene.
ROOT=/Users/dev/Desktop/Resq
UC="python3 $ROOT/tools/unity/uc.py"
$UC editor_stop > /dev/null 2>&1
$UC recompile > /dev/null 2>&1
sleep 6
for i in $(seq 1 60); do
  s=$($UC editor_status 2>/dev/null)
  echo "$s" | grep -q '"compiling": false' && echo "$s" | grep -q '"domainReloadInProgress": false' && break
  sleep 3
done
sleep 3
$UC console_status 2>/dev/null | grep -q '"compilationFailed": true' && { echo "COMPILE FAILED"; exit 1; }
$UC editor_play > /dev/null
for i in $(seq 1 30); do sleep 2; $UC editor_status 2>/dev/null | grep -q '"playMode": "playing"' && break; done
sleep 5
echo playing
