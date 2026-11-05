#!/bin/bash
# Run the PlayMode gate (smoke.sh) once the machine is free: no other project's Unity process for QUIET seconds
# and at least MIN_FREE_MB of disk. Waits up to MAX_WAIT seconds. Launches RESQ's Editor if it isn't open, guards
# disk while the gate runs (stops RESQ's Editor below STOP_MB), and writes the outcome to tools/.cache/gate_when_free.txt.
# Never touches other projects' processes.
ROOT=/Users/dev/Desktop/Resq
UC="$ROOT/tools/unity/uc.py"
OUT="$ROOT/tools/.cache/gate_when_free.txt"
QUIET=${QUIET:-180}; MIN_FREE_MB=${MIN_FREE_MB:-10000}; STOP_MB=${STOP_MB:-6500}; MAX_WAIT=${MAX_WAIT:-10800}
UNITY=/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents/MacOS/Unity
free_mb() { df -m / | tail -1 | awk '{print $4}'; }
others() { pgrep -x Unity | while read -r p; do ps -o command= -p "$p" | grep -v "Desktop/Resq/game"; done | grep -c . ; }   # by process name: -f would also match shells whose command text mentions Unity
echo "waiting since $(date +%H:%M:%S) (quiet ${QUIET}s, disk >= ${MIN_FREE_MB} MB)" > "$OUT"
quiet=0; waited=0
while [ "$waited" -lt "$MAX_WAIT" ]; do
  if [ "$(others)" -eq 0 ] && [ "$(free_mb)" -ge "$MIN_FREE_MB" ]; then quiet=$((quiet + 30)); else quiet=0; fi
  [ "$quiet" -ge "$QUIET" ] && break
  sleep 30; waited=$((waited + 30))
done
if [ "$quiet" -lt "$QUIET" ]; then echo "gave up at $(date +%H:%M:%S): other Unity jobs=$(others), free=$(free_mb) MB" >> "$OUT"; exit 2; fi
echo "machine free at $(date +%H:%M:%S), free=$(free_mb) MB" >> "$OUT"
if ! pgrep -f "projectPath $ROOT/game" > /dev/null; then
  nohup "$UNITY" -projectPath "$ROOT/game" -logFile "$ROOT/tools/.cache/editor.log" > /dev/null 2>&1 &
  sleep 30
  for i in $(seq 1 100); do python3 "$UC" editor_status 2>/dev/null | grep -q '"status": "ready"' && break; sleep 5; done
fi
( while pgrep -f "projectPath $ROOT/game" > /dev/null; do
    f=$(free_mb); if [ "$f" -lt "$STOP_MB" ]; then pkill -f "tools/unity/smoke.sh"; pkill -f "projectPath $ROOT/game"; echo "disk guard stopped the Editor at $f MB" >> "$OUT"; break; fi
    sleep 10; done ) &
GUARD=$!
bash "$ROOT/tools/unity/smoke.sh" >> "$OUT" 2>&1; code=$?
kill "$GUARD" 2>/dev/null
echo "gate exit $code at $(date +%H:%M:%S); other Unity jobs during run end: $(others)" >> "$OUT"
exit $code
