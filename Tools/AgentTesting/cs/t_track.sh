# run runs/$1.cpp, print the pose every second for $2 seconds
bash cs/t_run.sh $1 0 ${3:-a} ${4:-15} > /dev/null
for i in $(seq 1 $2); do sleep 0.7; python -I umcp.py cs cs/t_pose.cs | grep -o '"result":"[^"]*"' | cut -d'"' -f4; done
