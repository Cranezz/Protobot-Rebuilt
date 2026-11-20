# play, load the 6 motor drive
python -I umcp.py cs cs/play.cs >/dev/null; sleep 15
for i in $(seq 1 20); do r=$(python -I umcp.py cs cs/t_mon0.cs 2>&1); echo "$r" | grep -q success && break; sleep 4; done
python -I umcp.py cs cs/load_6md.cs | head -c 90; echo; sleep 5
