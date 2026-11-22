# Shared by the test scripts (source it from Tools/AgentTesting): every call to Unity has a time limit, every wait
# checks that something is actually moving, and a stall stops the script at once with "STALLED: <what>" (exit 3)
# instead of waiting out its whole time. The last step is in status.txt beside this file (with the time), so anyone
# can see where a test is.
STATUS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/status.txt"
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"

step() { echo "== $*"; echo "$(date +%T) $*" > "$STATUS"; }
note() { echo "$(date +%T)   $*" >> "$STATUS"; }
stall() { echo "STALLED: $*"; echo "$(date +%T) STALLED: $*" >> "$STATUS"; exit 3; }
fail() { echo "FAILED: $*"; echo "$(date +%T) FAILED: $*" >> "$STATUS"; exit 4; }

# call SCRIPT [seconds]: runs a cs script in Unity. R = its result (\n as real newlines). Returns 1 when the script
# itself failed (R = the error). Stalls when Unity doesn't answer three tries in a row.
call() {
  local f=$1 lim=${2:-90} raw i
  for i in 1 2 3; do
    raw=$(timeout "$lim" python -I umcp.py cs "$f" 2>&1)
    if echo "$raw" | grep -q '"success":true'; then
      R=$(echo "$raw" | python -I -c "import sys, json; d = json.loads(sys.stdin.read()); r = (d.get('data') or {}).get('result'); print('' if r is None else r)" 2>/dev/null)
      return 0
    fi
    if echo "$raw" | grep -q '"success":false'; then
      R=$(echo "$raw" | head -c 600)
      return 1
    fi
    sleep 5
  done
  stall "Unity didn't answer $f (3 tries of ${lim}s)"
}

# patient SCRIPT MAX: like call, but for steps where Unity is busy and can't answer for a while (entering Play and
# recompiling reload every script: a minute or more). Silence is fine while Unity's log keeps growing; it stalls
# when Unity is silent and its log hasn't moved for 60 s, or past MAX seconds.
patient() {
  local f=$1 max=$2 start=$SECONDS size last_size=-1 quiet_since=$SECONDS raw
  while :; do
    raw=$(timeout 30 python -I umcp.py cs "$f" 2>&1)
    if echo "$raw" | grep -q '"success":true'; then
      R=$(echo "$raw" | python -I -c "import sys, json; d = json.loads(sys.stdin.read()); r = (d.get('data') or {}).get('result'); print('' if r is None else r)" 2>/dev/null)
      return 0
    fi
    if echo "$raw" | grep -q '"success":false'; then R=$(echo "$raw" | head -c 600); return 1; fi
    size=$(stat -c %s "$LOG" 2>/dev/null || echo 0)
    if [ "$size" != "$last_size" ]; then last_size=$size; quiet_since=$SECONDS; fi
    [ $((SECONDS - quiet_since)) -ge 60 ] && stall "Unity didn't answer $f and its log hasn't moved for 60s"
    [ $((SECONDS - start)) -ge "$max" ] && stall "Unity didn't answer $f within ${max}s (its log is still moving: busy?)"
    sleep 5
  done
}

# u SCRIPT [seconds]: call and print the result; a script error stops the test.
u() { call "$@" || fail "$1: $R"; echo "$R"; }

# state: P = playing True/False, S = sandbox True/False
state() { patient cs/t_playstate.cs 300 || fail "t_playstate: $R"; P=$(echo "$R" | grep -o "playing [A-Za-z]*" | cut -d' ' -f2); S=$(echo "$R" | grep -o "sandbox [A-Za-z]*" | cut -d' ' -f2); }

# wait_play True|False SECONDS
wait_play() {
  local want=$1 max=$2 start=$SECONDS
  while :; do
    state
    [ "$P" = "$want" ] && return 0
    [ $((SECONDS - start)) -ge "$max" ] && stall "Play mode didn't become $want in ${max}s"
    sleep 4
  done
}

# compile: leaves Play, asks Unity to refresh and waits for the scripts to reload (or prints the compile errors and
# stops). Unity must keep answering while it works.
compile() {
  step "compile"
  state
  if [ "$P" = "True" ]; then u cs/t_playstop.cs >/dev/null; wait_play False 90; fi
  local n m s start=$SECONDS
  n=$(grep -a -c "Domain Reload Profiling" "$LOG")
  u cs/t_refresh.cs >/dev/null
  while :; do
    sleep 5
    m=$(grep -a -c "Domain Reload Profiling" "$LOG")
    s=$(grep -a -n "Requested through public api\|Reloading assemblies" "$LOG" | tail -1 | cut -d: -f1)
    if [ -n "$s" ] && tail -n +"$s" "$LOG" | grep -a -q "error CS"; then
      tail -n +"$s" "$LOG" | grep -a "error CS" | sort -u | head
      fail "compile errors"
    fi
    if [ "$m" -gt "$n" ]; then break; fi
    # nothing to compile: the refresh finished without a reload
    if [ $((SECONDS - start)) -ge 30 ] && ! tail -n 200 "$LOG" | grep -a -q "Reloading assemblies\|Compiling"; then
      state; [ "$P" = "False" ] && { note "no reload needed"; break; }
    fi
    [ $((SECONDS - start)) -ge 300 ] && stall "the scripts didn't reload in 300s"
  done
  state   # (Unity answers again after the reload)
  echo "compiled"
}

# play: enters Play (with Unity in the background) and waits for it
play() {
  step "enter Play"
  state
  [ "$P" = "True" ] && return 0
  u cs/t_playunfocused.cs >/dev/null
  u cs/play.cs >/dev/null
  wait_play True 240
  sleep 3
}

# run_program FILE [max seconds]: starts a robot program (t_odom_run: the truth beside each mark) and follows it: new
# marks are printed as they come; it stops when the program ends, and stalls when the program's clock stops moving
# for 90 s (Play paused or left, Unity frozen) or Unity stops answering. M = the final marks text.
run_program() {
  local file=$1 max=${2:-1800} start=$SECONDS last_t="" last_move=$SECONDS shown=0 status t lines
  step "run $file"
  [ -d ../../Temp ] || stall "Unity's Temp folder is gone (the editor closed?)"
  echo "$file" > ../../Temp/odomprog.txt
  u cs/t_odom_prog.cs >/dev/null
  u cs/t_odom_run.cs
  echo "$R" | grep -q "started True" || fail "the program didn't start: $R"
  while :; do
    sleep 5
    call cs/t_marks.cs 30 || fail "t_marks: $R"
    M=$R
    status=$(echo "$M" | head -1)
    t=$(echo "$status" | grep -o "t=[0-9.]*" | cut -d= -f2)
    lines=$(echo "$M" | tail -n +2 | grep -c .)
    if [ "$lines" -gt "$shown" ]; then echo "$M" | tail -n +2 | tail -n +$((shown + 1)); shown=$lines; fi
    # a program that keeps threads alive (the motion module's odometry) never finishes: it prints "mark END" at its end
    if echo "$M" | grep -q "^mark END"; then note "program printed END"; return 0; fi
    case "$status" in
      Finished*|Stopped*|*rror*) note "program ended: $status"; return 0 ;;
      none*) fail "no program running (it ended or never ran): $M" ;;
    esac
    if [ "$t" != "$last_t" ]; then last_t=$t; last_move=$SECONDS; note "program t=$t, $lines marks"; fi
    if [ $((SECONDS - last_move)) -ge 90 ]; then state; stall "the program's clock stopped at t=$t for 90s (playing $P, sandbox $S)"; fi
    [ $((SECONDS - start)) -ge "$max" ] && stall "the program ran past ${max}s (t=$t)"
  done
}
