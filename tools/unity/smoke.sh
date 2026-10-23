#!/bin/bash
# PlayMode smoke gate (Assets/Resq/Tests/PlayMode/OperationSmokeTest.cs), in the open RESQ Editor:
#   each world's M1: boot, board + art checks, render budget at step 300, win strategy A through real pointer presses;
#   Command path: W1-M1 under Command rules through the head-dwell pointer only;
#   XR placement: table choice and fit in MRUK's 40 sample rooms (XrPlacementTest).
# Exit 0 = pass, 1 = fail, 2 = could not run. Result JSON: tools/.cache/smoke_last.json. Evidence: automated (Editor PlayMode).
ROOT=/Users/dev/Desktop/Resq
UC="$ROOT/tools/unity/uc.py"
python3 "$UC" editor_stop > /dev/null 2>&1
# import changed files first: a background Editor doesn't auto-refresh, and a bare recompile would test stale code
python3 "$UC" eval --code 'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport); return "refreshed";' > /dev/null 2>&1
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
# PlayMode entry reloads the domain, so the run is asynchronous: start it, then wait for the results file that
# Assets/Resq/Editor/Testing/TestResultWriter.cs writes when the run finishes. Nothing talks to the Editor bridge while
# tests run: a bridge request that times out mid-test logs an error, and the Test Framework fails that test for it.
RES="$ROOT/tools/.cache/test_results.json"
mkdir -p "$ROOT/tools/.cache"
rm -f "$RES"
python3 "$UC" run_tests --mode playmode --filter Resq.PlayMode.Tests --filter_type assembly --async_tests true --timeout 1800 > /dev/null 2>&1
for i in $(seq 1 720); do
  grep -q '"status": "completed"' "$RES" 2>/dev/null && break
  sleep 5
done
out=$(cat "$RES" 2>/dev/null)
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
rs = d.get("results", [])
for r in rs:
    print("  %s  %s  %.1fs" % (r.get("status"), r.get("name"), r.get("duration", 0)))
    if r.get("status") != "Passed":
        print("   " + (r.get("message") or "").strip().replace("\n", "\n   ")[:1500])
total = len(rs); failed = sum(1 for r in rs if r.get("status") == "Failed")
if total == 0: print("SMOKE: no tests ran"); sys.exit(2)
if failed > 0: print("SMOKE: FAIL (%d/%d)" % (failed, total)); sys.exit(1)
print("SMOKE: PASS (%d/%d)" % (d.get("passed", 0), total)); sys.exit(0)
'
