#!/bin/bash
# SpatialSim smoke test for the Quest APK: install, permission flow, MRUK placement, spatial anchor restore.
#   tools/ssim/smoke.sh [apk=build/resq-xr.apk]
# Every result comes from the app's own "[RESQ XR]" log lines (logcat, tag Unity). Evidence kind: emulator.
# Disk (23 Oct): the SpatialSim image installs to ~8.7 GB (system-qemu.img 8 GB) under ~/Library/Application Support/metavr;
# it also needs the Android SDK "emulator" package (~1 GB). Refuses to start below 15 GB free once SpatialSim is
# installed, 24 GB before. Never touches other apps or devices.
set -u
ROOT=/Users/dev/Desktop/Resq
APK=${1:-$ROOT/build/resq-xr.apk}
PKG=com.playresq.resq
ADB=${ADB:-$HOME/Library/Android/sdk/platform-tools/adb}
FREE_GB=$(df -g / | awk 'NR==2{print $4}')
NEED=15; metavr ssim status --format json | grep -q '"installed": *true' || NEED=24
NEED=${MIN_FREE_GB:-$NEED}   # the user may override the floor (23 Oct: "don't stay stuck on disk storage limits")
if [ "$FREE_GB" -lt "$NEED" ]; then echo "SSIM: refused, ${FREE_GB} GB free (need >= ${NEED} GB)"; exit 2; fi
[ -f "$APK" ] || { echo "SSIM: no APK at $APK (run tools/unity/build_apk.sh)"; exit 2; }

metavr ssim status --format json | grep -q '"installed": *true' || metavr ssim download || exit 2
SDKM=$HOME/Library/Android/sdk/cmdline-tools/latest/bin/sdkmanager
[ -x "$HOME/Library/Android/sdk/emulator/emulator" ] || yes | "$SDKM" --install emulator > /dev/null || { echo "SSIM: Android Emulator install failed"; exit 2; }
metavr ssim status --format json | grep -q '"running": *true' || metavr ssim start || exit 2
SERIAL=""
for i in $(seq 1 60); do
  SERIAL=$(metavr ssim status --format json | python3 -c 'import json,sys; d=json.load(sys.stdin); d=d[0] if isinstance(d,list) else d; print(d.get("adb_serial") or d.get("adb serial") or "")' 2>/dev/null)
  [ -n "$SERIAL" ] && "$ADB" -s "$SERIAL" get-state 2>/dev/null | grep -q device && break
  sleep 5
done
[ -n "$SERIAL" ] || { echo "SSIM: emulator did not come up"; exit 2; }
A="$ADB -s $SERIAL"
# safety stop: if free disk falls below STOP_GB (default 2) the emulator is stopped so the Mac doesn't run out
( while metavr ssim status --format json 2>/dev/null | grep -q '"running": *true'; do
    [ "$(df -g / | awk 'NR==2{print $4}')" -lt "${STOP_GB:-2}" ] && { echo "SSIM: LOW DISK, stopping SpatialSim"; metavr ssim stop; break; }
    sleep 10; done ) &
$A wait-for-device
$A install -r -g "$APK" > /dev/null || { echo "SSIM: install failed"; exit 1; }

NOTRUN=0
run() {   # $1 label; launches, waits, prints the app's placement lines
  $A logcat -c
  $A shell monkey -p $PKG -c com.oculus.intent.category.VR 1 > /dev/null 2>&1 || $A shell am start -n $PKG/com.unity3d.player.UnityPlayerGameActivity > /dev/null
  sleep ${WAIT:-45}
  echo "--- $1"
  $A logcat -d -s Unity:V | grep -a "\[RESQ XR\]" | sed 's/^.*\[RESQ XR\]/[RESQ XR]/' | head -8
  $A logcat -d | grep -a -c "FATAL EXCEPTION\|AndroidRuntime: FATAL" | sed 's/^/fatal lines: /'
  # SpatialSim 207.0.0 has no OpenXR runtime: it refuses immersive apps, so "no fatal lines" proves nothing then
  if $A logcat -d | grep -aq "Rejecting immersive app launch"; then echo "NOT RUN: SpatialSim rejected the immersive launch"; NOTRUN=1; fi
  $A logcat -d -s Unity:V | grep -aq "\[RESQ XR\]" || { echo "NO RESQ XR LINES: placement not exercised"; NOTRUN=1; }
  $A shell am force-stop $PKG
}

# 1. scene permission granted (installed with -g), fresh data: expect a table or a virtual table, then an anchor saved
$A shell pm clear $PKG > /dev/null
$A shell pm grant $PKG com.oculus.permission.USE_SCENE 2>/dev/null
run "1 fresh, scene permission granted"
# 2. relaunch: the anchor from run 1 should restore
run "2 relaunch (anchor restore)"
# 3. scene permission denied, fresh data: MRUK gets no room -> virtual table; the app must not crash
$A shell pm clear $PKG > /dev/null
$A shell pm revoke $PKG com.oculus.permission.USE_SCENE 2>/dev/null
run "3 fresh, scene permission denied"
echo "--- permissions now:"; $A shell dumpsys package $PKG | grep -a "com.oculus.permission" | head -6
[ "$NOTRUN" = 0 ] || { echo "SSIM: inconclusive, the app did not run its XR path (evidence: emulator install + manifest only)"; exit 3; }
