#!/bin/bash
# Full input matrix (OperationSmokeTest, category Matrix), in the open RESQ Editor: every one of the 25 missions played to a
# 3-star win through the input layer three ways: a hand ray; one hand with mirrored controls and the largest text on the
# compact board (strategy B); head gaze only under Command rules with the real dwell. Takes about 30 to 45 minutes.
# Exit 0 = pass, 1 = fail, 2 = could not run. Result JSON: tools/.cache/matrix_last.json. Evidence: automated (Editor PlayMode).
ROOT=/Users/dev/Desktop/Resq
UC="$ROOT/tools/unity/uc.py"
python3 "$UC" editor_stop > /dev/null 2>&1
python3 "$UC" eval --code 'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport); return "refreshed";' > /dev/null 2>&1
python3 "$UC" recompile > /dev/null 2>&1
sleep 6
for i in $(seq 1 80); do
  s=$(python3 "$UC" editor_status 2>/dev/null)
  echo "$s" | grep -q '"compiling": false' && echo "$s" | grep -q '"domainReloadInProgress": false' && echo "$s" | grep -q '"status": "ready"' && break
  sleep 3
done
python3 "$UC" console_status 2>/dev/null | grep -q '"compilationFailed": true' && { echo "MATRIX: COMPILE FAILED"; exit 2; }
python3 "$UC" editor_focus > /dev/null 2>&1
RES="$ROOT/tools/.cache/test_results.json"
rm -f "$RES"
# include_explicit: all three matrix tests are [Explicit], and without it the filter silently matches nothing and no run starts.
python3 "$UC" run_tests --mode playmode --filter Matrix --filter_type category --include_explicit true --async_tests true --timeout 7200 > /dev/null 2>&1
# TestResultWriter writes "running" when the run starts, so a run that never starts fails here rather than after the long wait.
for i in $(seq 1 60); do [ -f "$RES" ] && break; sleep 5; done
[ -f "$RES" ] || { echo "MATRIX: no run started within 5 minutes (Editor closed, or the filter matched no tests)"; exit 2; }
for i in $(seq 1 1440); do
  grep -q '"status": "completed"' "$RES" 2>/dev/null && break
  sleep 5
done
cp "$RES" "$ROOT/tools/.cache/matrix_last.json" 2>/dev/null
python3 - "$RES" <<'PY'
import json, sys
try:
    raw = open(sys.argv[1]).read(); d = json.loads(raw[raw.index("{"):])
except Exception as e:
    print("MATRIX: no result", e); sys.exit(2)
rs = d.get("results", [])
for r in rs:
    if r.get("status") != "Passed":
        print("  %s  %s  %.1fs\n   %s" % (r.get("status"), r.get("name"), r.get("duration", 0), (r.get("message") or "").strip()[:700]))
failed = sum(1 for r in rs if r.get("status") == "Failed")
if not rs: print("MATRIX: no tests ran"); sys.exit(2)
print("MATRIX: %s (%d/%d passed, %.0f min)" % ("FAIL" if failed else "PASS", sum(1 for r in rs if r.get("status") == "Passed"), len(rs), sum(r.get("duration", 0) for r in rs) / 60))
sys.exit(1 if failed else 0)
PY
