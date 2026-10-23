#!/bin/bash
# Build the Quest APK (Android, IL2CPP ARM64, Operation_XR) in the open RESQ Editor.
#   tools/unity/build_apk.sh [output=build/resq-xr.apk]
# Disk: a build from a warm Editor cache grew game/Library/Bee by ~8.6 GB at peak (23 Oct). The script refuses to
# start below MIN_FREE_GB (default 19 = 10 GB floor + ~9 GB), watches free space during the build and stops the
# build's own processes (children of the RESQ Editor only) below WATCH_MIN_GB (default 5), and afterwards deletes
# the regenerable Android intermediates (CLEAN=0 keeps them for a faster rebuild).
# Debug keystore: for SpatialSim and development devices only, never for a store channel upload.
set -e
ROOT=/Users/dev/Desktop/Resq
UC="$ROOT/tools/unity/uc.py"
OUT=${1:-build/resq-xr.apk}
case "$OUT" in /*) ;; *) OUT="$ROOT/$OUT" ;; esac   # Unity resolves relative paths against the project, not the repo
MIN_FREE_GB=${MIN_FREE_GB:-19}; WATCH_MIN_GB=${WATCH_MIN_GB:-5}; CLEAN=${CLEAN:-1}
FREE_GB=$(df -g / | awk 'NR==2{print $4}')
if [ "$FREE_GB" -lt "$MIN_FREE_GB" ]; then echo "BUILD: refused, ${FREE_GB} GB free (need >= ${MIN_FREE_GB} GB)"; exit 2; fi
EDITOR_PID=$(pgrep -f "Unity.app/Contents/MacOS/Unity -projectPath $ROOT/game" | head -1)
[ -n "$EDITOR_PID" ] || { echo "BUILD: the RESQ Editor is not running"; exit 2; }

descendants() { local kids; kids=$(pgrep -P "$1"); for k in $kids; do echo "$k"; descendants "$k"; done; }
mkdir -p "$ROOT/tools/.cache" "$(dirname "$OUT")"
WATCHLOG="$ROOT/tools/.cache/build_watch.log"; : > "$WATCHLOG"
(
  while true; do
    free_kb=$(df -k / | awk 'NR==2{print $4}')
    echo "$(date +%T) $((free_kb / 1048576)) GB" >> "$WATCHLOG"
    if [ "$free_kb" -lt $((WATCH_MIN_GB * 1048576)) ]; then
      echo "$(date +%T) LOW DISK: stopping the RESQ build's child processes" >> "$WATCHLOG"
      for p in $(descendants "$EDITOR_PID"); do
        case "$(ps -o comm= -p "$p" 2>/dev/null)" in *bee_backend*|*il2cpp*|*clang*|*ld*|*java*|*dotnet*) kill "$p" 2>/dev/null ;; esac
      done
      break
    fi
    sleep 3
  done
) &
WATCH_PID=$!
trap 'kill $WATCH_PID 2>/dev/null' EXIT

python3 "$UC" editor_stop > /dev/null 2>&1 || true
python3 "$UC" build --target Android --outputPath "$OUT" --confirm true > /dev/null
s=""
for i in $(seq 1 360); do
  sleep 10
  s=$(python3 "$UC" build_status 2>/dev/null)
  echo "$s" | grep -q "Succeeded\|Failed\|Cancelled\|\"completed\"" && break
done
echo "BUILD: $(echo "$s" | head -c 200)"
echo "BUILD: lowest free disk during the build: $(awk '{print $2}' "$WATCHLOG" | sort -n | head -1) GB"
ls -la "$OUT" 2>/dev/null || { echo "BUILD: no APK at $OUT"; exit 1; }
rm -rf "$ROOT/game/build/RESQ_BurstDebugInformation_DoNotShip" "$(dirname "$OUT")"/*_BurstDebugInformation_DoNotShip 2>/dev/null
if [ "$CLEAN" = "1" ]; then
  rm -rf "$ROOT/game/Library/Bee/Android" "$ROOT/game/Library/Bee/artifacts/Android" "$ROOT"/game/Library/Bee/artifacts/objcopy_*
  echo "BUILD: Android intermediates cleared; free now $(df -h / | awk 'NR==2{print $4}')"
fi
