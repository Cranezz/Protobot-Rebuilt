# enter Play mode, wait for the MCP to come back, load the 6-motor drive
python -I umcp.py cs cs/play.cs >/dev/null 2>&1; sleep 20
for i in $(seq 1 24); do r=$(python -I umcp.py cs cs/t_playstate.cs 2>&1); echo "$r" | grep -q "playing True" && break; sleep 5; done
python -I umcp.py cs cs/load_6md.cs | grep -o '"result":"[^"]*"'; sleep 8
