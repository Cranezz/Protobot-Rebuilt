# Cascade lift tests: source lib.sh, then L "command" runs t_lift.cs with that command (see its header).
. ./lib.sh
L() { echo "$1" > ../../Temp/lift.txt; u cs/t_lift.cs ${2:-180}; }
