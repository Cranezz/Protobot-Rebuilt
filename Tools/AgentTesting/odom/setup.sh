# Compiles (skip with "play"), enters Play, loads the 6-motor drive and builds the odometry pods + bands. Every step is
# checked (lib.sh): a stall stops it with "STALLED: ..." instead of waiting. Usage: bash odom/setup.sh [play]
. ./lib.sh
[ "$1" != "play" ] && compile
play
step "load the 6-motor drive"
u cs/load_6md.cs 180
sleep 6
step "build the odometry pods"
u cs/t_odom_build.cs 180
sleep 4
u cs/t_odom_finish.cs 180
echo "$R" | grep -q "pulls" || fail "the pods' bands weren't made: $R"
step "robot ready"
