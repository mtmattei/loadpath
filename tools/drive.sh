#!/usr/bin/env bash
# Interactive headless session: starts the app under Xvfb, then runs a script of steps.
# Steps (one per arg):  "shot <path>"  |  "sleep <s>"  |  any other string is passed to xdotool as arguments.
# Env vars before the script via ENV="K=V K2=V2".
set -u
export DISPLAY=:99
if ! pgrep -x Xvfb >/dev/null; then
  Xvfb :99 -screen 0 1600x1000x24 -nolisten tcp >/tmp/claude-0/xvfb.log 2>&1 &
  sleep 1.5
fi
DIR="$(cd "$(dirname "$0")" && pwd)"
EXE="$DIR/../Loadpath/bin/Debug/net10.0-desktop/Loadpath"
pkill -x Loadpath 2>/dev/null; sleep 0.5
env APP_NO_HOTDESIGN=1 ${ENV:-} "$EXE" >/tmp/claude-0/app.log 2>&1 &
APP=$!
sleep "${STARTUP:-7}"
# Focus the window so keys land in it.
WID=$(xdotool search --onlyvisible --class Loadpath 2>/dev/null | head -1)
[ -z "$WID" ] && WID=$(xdotool search --onlyvisible --name Loadpath 2>/dev/null | head -1)
[ -n "$WID" ] && xdotool windowactivate --sync "$WID" 2>/dev/null; xdotool windowfocus "$WID" 2>/dev/null
echo "window $WID geometry: $(xdotool getwindowgeometry $WID 2>/dev/null | tr '\n' ' ')"
for step in "$@"; do
  case "$step" in
    shot\ *) sleep 0.4; import -display :99 -window root "${step#shot }"; echo "shot ${step#shot }";;
    sleep\ *) sleep "${step#sleep }";;
    *) xdotool $step; sleep 0.25;;
  esac
done
kill $APP 2>/dev/null
grep -iE "exception|unhandled" /tmp/claude-0/app.log | head -5
