# Toggle push test: opens a Skills game (Play) unless "play" is given, starts the run, then pushes Toggle 0 with each
# "N STEPS" given (Temp/tflip.txt) and reads where it ends up.  bash tflip.sh [play] "3 24" "5 24" ...
source lib.sh
if [ "$1" = "play" ]; then shift; else step "game"; timeout 900 bash odom/game_setup.sh play | tail -1; fi
echo start > ../../Temp/tflip.txt; u cs/t_tflip.cs
for i in $(seq 1 20); do echo read > ../../Temp/tflip.txt; call cs/t_tflip.cs; sleep 2; done >/dev/null   # countdown
for f in "$@"; do
  echo "$f" > ../../Temp/tflip.txt; u cs/t_tflip.cs
  last=""; for i in $(seq 1 60); do sleep 3; echo read > ../../Temp/tflip.txt; call cs/t_tflip.cs; [ "$R" = "$last" ] && break; last="$R"; done
  echo "$R"
done
