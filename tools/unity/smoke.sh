#!/bin/bash
# PlayMode smoke gate (Assets/Resq/Tests/PlayMode/OperationSmokeTest.cs), in the open RESQ Editor:
#   each world's M1: boot, board + art checks, render budget at step 300, win strategy A through real pointer presses;
#   Command path: W1-M1 under Command rules through the head-dwell pointer only.
# Exit 0 = pass, 1 = fail, 2 = could not run. Result JSON: tools/.cache/smoke_last.json. Evidence: automated (Editor PlayMode).
ROOT=/Users/dev/Desktop/Resq
UC="$ROOT/tools/unity/uc.py"
python3 "$UC" editor_stop > /dev/null 2>&1
python3 "$UC" recompile > /dev/null 2>&1
sleep 6
for i in $(seq 1 80); do
  s=$(python3 "$UC" editor_status 2>/dev/null)
  echo "$s" | grep -q '"compiling": false' && echo "$s" | grep -q '"domainReloadInProgress": false' && echo "$s" | grep -q '"status": "ready"' && break
  sleep 3
done
python3 "$UC" console_status 2>/dev/null | grep -q '"compilationFailed": true' && { echo "SMOKE: COMPILE FAILED"; exit 2; }
# the perf check reads render counters: the Game view must repaint every frame, so the Editor needs focus
python3 "$UC" editor_focus > /dev/null 2>&1
# PlayMode entry reloads the domain, so the run is asynchronous: start it, then poll test_status
python3 "$UC" run_tests --mode playmode --filter OperationSmokeTest --async_tests true --timeout 1800 > /dev/null 2>&1
sleep 10
mkdir -p "$ROOT/tools/.cache"
for i in $(seq 1 360); do
  out=$(python3 "$UC" test_status 2>/dev/null)
  echo "$out" | grep -q '"status": "completed"' && break
  sleep 5
done
echo "$out" > "$ROOT/tools/.cache/smoke_last.json"
echo "$out" | python3 -c '
import json, sys
raw = sys.stdin.read()
try:
    d = json.loads(raw[raw.index("{"):])
except Exception:
    print("SMOKE: no result\n" + raw[-1500:]); sys.exit(2)
if d.get("status") != "completed":
    print("SMOKE: did not complete (" + str(d.get("status")) + ")"); sys.exit(2)
s = d.get("summary", {})
for r in d.get("results", []):
    print("  %s  %s  %.1fs" % (r.get("Status"), r.get("FullName"), r.get("Duration", 0)))
    if r.get("Status") != "Passed":
        print("   " + (r.get("Message") or "").strip().replace("\n", "\n   "))
        print("   " + (r.get("Output") or "").strip()[:1500])
if s.get("total", 0) == 0: print("SMOKE: no tests ran"); sys.exit(2)
if s.get("failed", 0) > 0: print("SMOKE: FAIL (%d/%d)" % (s["failed"], s["total"])); sys.exit(1)
print("SMOKE: PASS (%d/%d)" % (s.get("passed", 0), s["total"])); sys.exit(0)
'
