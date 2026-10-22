#!/bin/bash
# Capture the Unity Game view to tools/.cache/shots/<name>.png (the bridge only writes under Assets/).
set -e
NAME=${1:-shot}; W=${2:-1280}; H=${3:-720}
ROOT=/Users/dev/Desktop/Resq
python3 $ROOT/tools/unity/uc.py capture_game_view --source camera --width $W --height $H --save_path Temp/shots/$NAME.png > /dev/null
mkdir -p $ROOT/tools/.cache/shots
mv -f $ROOT/game/Assets/Temp/shots/$NAME.png $ROOT/tools/.cache/shots/$NAME.png
rm -rf $ROOT/game/Assets/Temp $ROOT/game/Assets/Temp.meta
echo $ROOT/tools/.cache/shots/$NAME.png
