# lift_cycle.sh MODE [setup commands...]: builds the lift, sets things up, runs up / down twice and prints where it ends.
. ./lift.sh
mode=$1; shift
L stop >/dev/null; L "build $mode" 300 | head -1; for i in 1 2 3; do L "step 100" >/dev/null; done; L "finish $mode 2 5.5" >/dev/null
for c in "$@"; do L "$c"; done
L sim >/dev/null; L "step 30" | head -3
up=1; [ "$mode" = string ] && up=-1
for c in 1 2; do
  L "drive $up" >/dev/null; for i in 1 2 3 4 5 6; do L "step 60" >/dev/null; done; echo "-- top $c"; L "step 10" | sed -n '1,3p;4,9p'
  L "drive $((-up))" >/dev/null; for i in 1 2 3 4 5 6 7 8; do L "step 60" >/dev/null; done; L "drive 0" >/dev/null; echo "-- bottom $c"; L "step 30" | sed -n '1,3p;4,9p'
done
