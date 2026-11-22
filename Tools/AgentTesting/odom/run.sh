# Runs a program file through t_odom_run.cs with Unity in front and prints the marks. Usage: bash odom/run.sh odom/x.py
echo "$1" > ../../Temp/odomprog.txt
python -I umcp.py cs cs/t_odom_prog.cs >/dev/null
python -I umcp.py cs cs/t_odom_run.cs | grep -o '"result":"[^"]*"\|errors[^]]*'
for i in $(seq 1 ${2:-60}); do sleep 3; r=$(python -I umcp.py cs cs/t_marks.cs | grep -o '"result":"[^"]*"'); echo "$r" | grep -q "Finished\|Error\|Stopped\|rror" && break; done
echo "$r" | sed 's/\n/\n/g'
