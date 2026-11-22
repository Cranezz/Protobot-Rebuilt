# Runs odom/py_points.py from a fresh robot (Play running, robot built: bash odom/setup.sh first): the wheels' lean
# 20 s in and at the end, a picture (Assets/Screenshots/shot.png), the marks. Usage: bash odom/points.sh [on|off] [program]
. ./lib.sh
[ -d ../../Temp ] || stall "Unity's Temp folder is gone (the editor closed?)"
echo "${1:-on}" > ../../Temp/straighten.txt
u cs/t_prefstraighten.cs >/dev/null
u cs/t_straighten.cs
echo "${2:-odom/py_points2.py}" > ../../Temp/odomprog.txt
step "start py_points"
u cs/t_odom_prog.cs >/dev/null
u cs/t_odom_run.cs
echo "$R" | grep -q "started True" || fail "the program didn't start: $R"
sleep 20
step "picture and lean"
u cs/t_shot.cs
u cs/t_podtilt.cs
# follow the rest (the program is already running: watch it like run_program does)
start=$SECONDS; last_t=""; last_move=$SECONDS; shown=0
while :; do
  sleep 5
  call cs/t_marks.cs 30 || fail "t_marks: $R"
  M=$R; status=$(echo "$M" | head -1); t=$(echo "$status" | grep -o "t=[0-9.]*" | cut -d= -f2)
  lines=$(echo "$M" | tail -n +2 | grep -c .)
  if [ "$lines" -gt "$shown" ]; then echo "$M" | tail -n +2 | tail -n +$((shown + 1)); shown=$lines; fi
  echo "$M" | grep -q "^mark END" && break
  case "$status" in Finished*|Stopped*|*rror*) break ;; none*) fail "no program running: $M" ;; esac
  if [ "$t" != "$last_t" ]; then last_t=$t; last_move=$SECONDS; note "program t=$t, $lines marks"; fi
  [ $((SECONDS - last_move)) -ge 90 ] && { state; stall "the program's clock stopped at t=$t (playing $P, sandbox $S)"; }
  [ $((SECONDS - start)) -ge 1800 ] && stall "the program ran past 1800s"
done
step "lean at the end"
u cs/t_podtilt.cs
step "done points"
