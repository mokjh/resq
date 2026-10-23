#!/bin/bash
# Build the Quest APK (Android, IL2CPP ARM64, Operation_XR) in the open RESQ Editor.
#   tools/unity/build_apk.sh [output=build/resq-xr.apk]
# Refuses to start below 13 GB free: an IL2CPP build grows the disk by ~2.4 GB and the project floor is 10 GB.
# Debug keystore: for SpatialSim and development devices only, never for a store channel upload.
set -e
ROOT=/Users/dev/Desktop/Resq
UC="$ROOT/tools/unity/uc.py"
OUT=${1:-build/resq-xr.apk}
FREE_GB=$(df -g / | awk 'NR==2{print $4}')
if [ "$FREE_GB" -lt 13 ]; then echo "BUILD: refused, ${FREE_GB} GB free (need >= 13 GB: 10 GB floor + ~2.4 GB build growth)"; exit 2; fi
python3 "$UC" editor_stop > /dev/null 2>&1 || true
python3 "$UC" build --target Android --outputPath "$OUT" --confirm true > /dev/null
for i in $(seq 1 360); do
  sleep 10
  s=$(python3 "$UC" build_status 2>/dev/null)
  echo "$s" | grep -q '"status": "completed"' && break
done
echo "$s" | python3 -c '
import json, sys
raw = sys.stdin.read()
d = json.loads(raw[raw.index("{"):], strict=False)
r = d.get("report") or d
summary = r.get("summary", r)
res = summary.get("result") or r.get("result")
print("BUILD:", res, "errors:", summary.get("totalErrors"), "size:", summary.get("totalSize"), "time:", summary.get("totalTime"))
sys.exit(0 if str(res).lower() in ("succeeded", "success") else 1)
'
ls -la "$ROOT/$OUT"
