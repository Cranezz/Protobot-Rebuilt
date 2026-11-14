#!/bin/bash
# Recompiles, plays, makes the test tube, starts the simulation, pulls the elbow fitting 3" away; screenshot.
cd "C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting"
T=../../Temp
python -I umcp.py manage_editor '{"action":"stop"}' > /dev/null; sleep 4
python -I umcp.py cs cs/t_compile.cs > /dev/null; sleep 45; bash errs.sh | tail -5
python -I umcp.py cs cs/play.cs > /dev/null; sleep 12
python -I umcp.py cs cs/t_tube1.cs > /dev/null; sleep 2
python -I umcp.py cs cs/pause.cs > /dev/null; sleep 1
python -I umcp.py cs cs/t_tube2.cs > /dev/null
python -I umcp.py cs cs/t_sbon.cs > /dev/null
echo "${CAM:-5 0 1 35 -20 -9}" > $T/cam.txt; python -I umcp.py cs cs/t_cam.cs > /dev/null; sleep 2
echo info > $T/rope.txt; python -I umcp.py cs cs/t_rope.cs | python -c "import sys,json; print(json.load(sys.stdin)['data']['result'])"
echo "push ${PUSH:-3 0 0}" > $T/rope.txt; python -I umcp.py cs cs/t_rope.cs > /dev/null; sleep 1
echo info > $T/rope.txt; python -I umcp.py cs cs/t_rope.cs | python -c "import sys,json; print(json.load(sys.stdin)['data']['result'])"
python -I umcp.py cs cs/t_shot.cs > /dev/null; sleep 3; bash errs.sh | tail -4
