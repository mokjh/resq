#!/bin/bash
# Release-signed Quest APK, built in batch mode once the machine is free:
#   tools/unity/release_apk.sh [output=build/resq-release.apk]
# Waits for no other project's Unity job and enough free disk (QUIET seconds, up to MAX_WAIT) and needs RESQ's own Editor closed. Key:
# ~/keys/resq.keystore, password in the login Keychain (service resq-keystore), never in the project or git.
# NO_KEY=1 builds debug-signed. Stops the build below WATCH_MIN_GB of disk. Restores the settings a build rewrites and
# clears Android intermediates afterwards. Then runs tools/unity/check_apk.sh. Uploading is a separate, confirmed step.
ROOT=/Users/dev/Desktop/Resq
OUT=${1:-$ROOT/build/resq-release.apk}
# MIN_FREE_GB: a build from cleared intermediates peaked at about 11 GB (5 Nov), plus the project's 10 GB floor.
QUIET=${QUIET:-120}; MAX_WAIT=${MAX_WAIT:-21600}; MIN_FREE_GB=${MIN_FREE_GB:-21}; WATCH_MIN_GB=${WATCH_MIN_GB:-5}
UNITY=/Applications/Unity/Unity-6000.3.24f1/Unity.app/Contents/MacOS/Unity
LOG="$ROOT/tools/.cache/release_build.log"
others() { pgrep -f "Unity.app/Contents/MacOS/Unity -" | while read -r p; do ps -o command= -p "$p"; done | grep -c . ; }

if pgrep -f "projectPath $ROOT/game" > /dev/null; then echo "BUILD: close RESQ's Editor first (batch mode needs the project)"; exit 2; fi
if [ "${NO_KEY:-0}" != "1" ]; then
  [ -f "$HOME/keys/resq.keystore" ] || { echo "BUILD: no keystore at ~/keys/resq.keystore"; exit 2; }
  RESQ_KEYSTORE_PASS="$(security find-generic-password -s resq-keystore -w 2>/dev/null)" || { echo "BUILD: no resq-keystore item in the Keychain"; exit 2; }
  export RESQ_KEYSTORE="$HOME/keys/resq.keystore" RESQ_KEYSTORE_PASS
fi

quiet=0; waited=0
while [ "$waited" -lt "$MAX_WAIT" ]; do
  if [ "$(others)" -eq 0 ] && [ "$(df -g / | awk 'NR==2{print $4}')" -ge "$MIN_FREE_GB" ]; then quiet=$((quiet + 30)); else quiet=0; fi
  [ "$quiet" -ge "$QUIET" ] && break
  sleep 30; waited=$((waited + 30))
done
[ "$quiet" -ge "$QUIET" ] || { echo "BUILD: gave up after ${waited}s: other Unity jobs $(others), $(df -g / | awk 'NR==2{print $4}') GB free (need ${MIN_FREE_GB})"; exit 3; }

mkdir -p "$(dirname "$OUT")" "$ROOT/tools/.cache"; rm -f "$OUT"
export RESQ_APK_OUT="$OUT"
"$UNITY" -batchmode -nographics -projectPath "$ROOT/game" -buildTarget Android -executeMethod Resq.EditorTools.ReleaseBuild.Build -logFile "$LOG" &
BUILD_PID=$!
low=999
while kill -0 "$BUILD_PID" 2>/dev/null; do
  f=$(df -g / | awk 'NR==2{print $4}'); [ "$f" -lt "$low" ] && low=$f
  if [ "$f" -lt "$WATCH_MIN_GB" ]; then echo "BUILD: stopping, ${f} GB free"; kill "$BUILD_PID"; break; fi
  sleep 5
done
wait "$BUILD_PID"; code=$?
unset RESQ_KEYSTORE_PASS
grep "\[RESQ build\]" "$LOG" | cut -c1-200
echo "BUILD: exit $code, lowest free disk ${low} GB"

cd "$ROOT" && git checkout -- game/ProjectSettings/ProjectSettings.asset 2>/dev/null
rm -rf "$ROOT/game/Library/Bee/Android" "$ROOT/game/Library/Bee/artifacts/Android" "$ROOT"/game/Library/Bee/artifacts/objcopy_* "$(dirname "$OUT")"/*_BurstDebugInformation_DoNotShip "$ROOT"/game/*_BurstDebugInformation_DoNotShip 2>/dev/null
[ "$code" -eq 0 ] && [ -f "$OUT" ] || { echo "BUILD: FAILED, see $LOG"; exit 1; }
ls -la "$OUT" | cut -c1-120
shasum -a 256 "$OUT"
bash "$ROOT/tools/unity/check_apk.sh" "$OUT"
