# Play-tests the Settings window: each section, switching, the old Open()s, then screenshots.
cd "$(dirname "${BASH_SOURCE[0]}")" || exit 1
. ./lib.sh
s() { echo "$1" > ../../Temp/settings.txt; u cs/t_settings.cs; }
play
step "settings"
s "home off"
s "close"
for i in 0 1 2 3; do s "open $i"; sleep 1; s "state"; s "shot settings_$i"; sleep 2; done
s "click Preferences"; sleep 1; s "state"
s "close"; sleep 1
s "old"; sleep 1; s "state"
s "close"; sleep 1; s "state"
grep -a "NullReference\|MissingReference\|Exception" "$LOG" | tail -5
