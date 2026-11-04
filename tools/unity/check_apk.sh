#!/bin/bash
# Store-rule checks on a RESQ APK. tools/unity/check_apk.sh <apk> [--debug-ok]
# Exit 0 = PASS. Evidence: automated, static; it says nothing about how the build runs on a headset.
APK="$1"; DEBUG_OK="$2"
BT="$(ls -d "$HOME/Library/Android/sdk/build-tools/"* | sort -V | tail -1)"
fail=0; ok() { echo "ok   $1"; }; bad() { echo "FAIL $1"; fail=1; }
B="$("$BT/aapt2" dump badging "$APK" 2>/dev/null)"; X="$("$BT/aapt2" dump xmltree "$APK" --file AndroidManifest.xml 2>/dev/null)"
L="$(unzip -l "$APK")"

echo "$B" | grep -q "name='com.playresq.resq'" && ok "package com.playresq.resq" || bad "package name"
echo "$B" | grep -q "minSdkVersion:'32'" && ok "minSdk 32" || bad "minSdk 32"
echo "$B" | grep -q "targetSdkVersion:'34'" && ok "targetSdk 34" || bad "targetSdk 34"
echo "$B" | grep -q "application-label:'RESQ'" && ok "label RESQ" || bad "label"
[ "$(echo "$B" | grep '^native-code')" = "native-code: 'arm64-v8a'" ] && ok "arm64-v8a only" || bad "ABIs not arm64-only"
ABI32="$(echo "$L" | grep -E '(armeabi|x86)[^/]*/[^ ]*\.so$' | awk '{print $4}')"
[ -z "$ABI32" ] && ok "no 32-bit libraries" || { bad "32-bit libraries in the APK:"; echo "$ABI32" | head -5; }
echo "$X" | grep -q 'com.oculus.intent.category.VR' && ok "VR launch category" || bad "VR launch category"
echo "$X" | grep -q 'quest3|quest3s' && ok "supportedDevices quest3|quest3s" || bad "supportedDevices"
echo "$X" | grep -q 'excludeFromRecents.*=true' && ok "excludeFromRecents" || bad "excludeFromRecents"
echo "$X" | grep -q 'debuggable.*=true' && bad "debuggable" || ok "not debuggable"
echo "$X" | grep -A1 '"oculus.software.handtracking"' | grep -q 'required.*=true' && ok "hand tracking required" || bad "hand tracking not required"
echo "$X" | grep -q 'android.hardware.vr.headtracking' && ok "head tracking declared" || bad "head tracking"
echo "$X" | grep -q '"android.permission.INTERNET"' && bad "INTERNET permission (RESQ is offline)" || ok "no INTERNET permission"
echo "$L" | grep -qi 'tapedeck' && bad "TapeDeck layer in the APK" || ok "no TapeDeck layer"
SZ=$(($(stat -f%z "$APK") / 1048576)); [ "$SZ" -le 1024 ] && ok "size ${SZ} MB (limit 1 GB)" || bad "size ${SZ} MB"

S="$("$BT/apksigner" verify --verbose --print-certs "$APK" 2>&1)"
echo "$S" | grep -qi 'scheme v[23]): true' && ok "signature scheme v2+" || bad "signature scheme"
if echo "$S" | grep -q 'CN=Android Debug'; then
  if [ "$DEBUG_OK" = "--debug-ok" ]; then echo "note debug-signed (sideload only)"; else bad "debug-signed (a channel upload needs the release key)"; fi
else ok "release-signed: $(echo "$S" | grep -m1 'certificate DN' | sed 's/.*DN: //')"; fi

echo "permissions:"; echo "$X" | grep -A1 'E: uses-permission' | grep -oE '"[a-z.A-Z_]+\.[A-Z_]+"' | sort -u | sed 's/^/     /'
[ "$fail" -eq 0 ] && { echo "CHECK: PASS"; exit 0; } || { echo "CHECK: FAIL"; exit 1; }
