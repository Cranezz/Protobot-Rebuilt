#!/bin/bash
# mouse.sh "w x y z down" ... : one mouse state per call, a short wait between.
cd "C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting"
for step in "$@"; do
  echo "$step" > ../../Temp/mouse.txt
  python -I umcp.py cs cs/t_mouse.cs | python -c "import sys,json; d=json.load(sys.stdin); print(d['data']['result'] if d.get('success') else str(d)[:400])"
  sleep 0.4
done
