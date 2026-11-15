#!/bin/bash
# Waits until the Unity editor answers (after Play mode starts or a reload).
cd "C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting"
for i in $(seq 1 40); do
  if python -I umcp.py cs cs/t_count.cs 2>/dev/null | grep -q '"success":true'; then exit 0; fi
  sleep 3
done
exit 1
