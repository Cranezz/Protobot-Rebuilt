# Play-tests "Show only this part" while sketching on a part.
cd "$(dirname "${BASH_SOURCE[0]}")" || exit 1
. ./lib.sh
k() { echo "$1" > ../../Temp/sketchonly.txt; u cs/t_sketchonly.cs; }
play
step "sketch only"
k "load C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/6 Motor Drive (3.25 omni, 360 rpm).pbb"
sleep 4
k "count"
k "enter"; sleep 2
k "shot sketch_all"; sleep 2
k "only"; sleep 1
k "shot sketch_only"; sleep 2
k "only"
k "only"
k "leave"
k "enter"; sleep 1
k "count"
k "close"; sleep 1
k "count"
echo 0 > /dev/null
