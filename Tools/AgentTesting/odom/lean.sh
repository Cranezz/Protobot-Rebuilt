# Quick lean check: (Play running, robot built) starts the sandbox with a program that drives and stops, reads the
# tracking wheels' lean at rest, while driving and after. Every step checked (lib.sh).
. ./lib.sh
step "lean: start"
echo odom/py_leandrive.py > ../../Temp/odomprog.txt
u cs/t_odom_prog.cs >/dev/null
u cs/t_odom_run.cs
echo "$R" | grep -q "started True" || fail "didn't start: $R"
for k in 1 2 3; do sleep 12; step "lean reading $k"; u cs/t_podtilt.cs | grep -v bearing; done
u cs/t_shot.cs
step "done lean"
