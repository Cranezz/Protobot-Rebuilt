# (game) Stops Play mode, compiles (skip both with "play"), enters Play, loads the Override demo robot and opens a Skills game.
# Usage: bash odom/setup.sh   (from Tools/AgentTesting)
L="$LOCALAPPDATA/Unity/Editor/Editor.log"
if [ "$1" != "play" ]; then
python -I umcp.py cs cs/t_playstop.cs >/dev/null 2>&1
for i in $(seq 1 30); do sleep 3; python -I umcp.py cs cs/t_playstate.cs 2>&1 | grep -q "playing False" && break; done
n=$(grep -a -c "Domain Reload Profiling" "$L")
python -I umcp.py cs cs/t_refresh.cs >/dev/null 2>&1
for i in $(seq 1 40); do sleep 5; m=$(grep -a -c "Domain Reload Profiling" "$L"); [ "$m" -gt "$n" ] && break; s=$(grep -a -n "Requested through public api\|Reloading assemblies" "$L" | tail -1 | cut -d: -f1); tail -n +$s "$L" | grep -a -q "error CS" && break; done
s=$(grep -a -n "Requested through public api\|Reloading assemblies" "$L" | tail -1 | cut -d: -f1)
if tail -n +$s "$L" | grep -a -q "error CS"; then tail -n +$s "$L" | grep -a "error CS" | sort -u | head; exit 1; fi
echo "compiled"
fi
python -I umcp.py cs cs/t_playunfocused.cs >/dev/null 2>&1
sleep 8
for k in 1 2 3; do
  python -I umcp.py cs cs/play.cs >/dev/null 2>&1
  ok=0; for i in $(seq 1 30); do sleep 5; python -I umcp.py cs cs/t_playstate.cs 2>&1 | grep -q "playing True" && { ok=1; break; }; done
  [ $ok = 1 ] && break
done
sleep 5
python -I umcp.py cs cs/t_loaddemo.cs | grep -o '"result":"[^"]*"'
echo 30 > ../../Temp/steps.txt; python -I umcp.py cs cs/t_step.cs >/dev/null
echo Skills > ../../Temp/gamemode.txt
python -I umcp.py cs cs/t_gameopen.cs | grep -o '"result":"[^"]*"'
