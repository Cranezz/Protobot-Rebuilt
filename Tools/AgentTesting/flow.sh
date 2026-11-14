#!/bin/bash
cd "C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting"
for step in "$@"; do
  echo "$step" > ../../Temp/flow.txt
  echo "> $step"; python -I umcp.py cs cs/t_bandflow.cs | python -c "import sys,json; d=json.load(sys.stdin); print(d['data']['result'] if d.get('success') else str(d)[:500])"
  sleep 0.5
done
