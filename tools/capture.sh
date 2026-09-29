#!/usr/bin/env bash
# Headless capture: runs the desktop build under Xvfb and saves a screenshot.
# Usage: tools/capture.sh <out.png> [wait-seconds] [ENV=VALUE ...]
set -u
OUT=${1:-shot.png}; WAIT=${2:-8}
if [ $# -ge 2 ]; then shift 2; else shift $#; fi
export DISPLAY=:99
if ! pgrep -x Xvfb >/dev/null; then
  Xvfb :99 -screen 0 1440x900x24 -nolisten tcp >/tmp/claude-0/xvfb.log 2>&1 &
  sleep 1.5
fi
DIR="$(cd "$(dirname "$0")" && pwd)"
EXE="$DIR/../Loadpath/bin/Debug/net10.0-desktop/Loadpath"
pkill -x Loadpath 2>/dev/null; sleep 0.5
env APP_NO_HOTDESIGN=1 "$@" "$EXE" >/tmp/claude-0/app.log 2>&1 &
APP=$!
sleep "$WAIT"
import -display :99 -window root "$OUT"
echo "captured $OUT (app alive=$(kill -0 $APP 2>/dev/null && echo yes || echo no))"
kill $APP 2>/dev/null
