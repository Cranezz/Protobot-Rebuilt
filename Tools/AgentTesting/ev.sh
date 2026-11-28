# Events test helper: ev "command" runs cs/t_ev.cs with it; evw waits for the background call's answer.
cd "$(dirname "${BASH_SOURCE[0]}")" || exit 1
. ./lib.sh
ev() { echo "$1" > ../../Temp/ev.txt; u cs/t_ev.cs; }
evw() { for i in $(seq 1 30); do c=$(cat ../../Temp/ev_out.txt 2>/dev/null); [ "$c" != "working" ] && { echo "$c" | cut -c1-${1:-600}; return 0; }; sleep 1; done; stall "events call"; }
