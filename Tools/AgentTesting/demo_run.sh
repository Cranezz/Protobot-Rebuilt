# demo_run.sh SCRIPT FOLDER [compile]: (re)compiles if asked, enters Play, plays a DemoPlayer script while recording
# frames to FOLDER, follows it (stalls when the status stops moving), then makes FOLDER/../NAME_sheet.jpg (6 frames).
cd "$(dirname "${BASH_SOURCE[0]}")" || exit 1
. ./lib.sh
script=$1; folder=$2
[ "$3" = "compile" ] && compile
# a fresh Play session each time (a run leaves parts, tools and placing behind)
state
if [ "$P" = "True" ]; then u cs/t_playstop.cs >/dev/null; wait_play False 120; fi
play
# PRE="cs/x.cs[:Temp file:contents]" sets a build up before the demo (e.g. tutorial 2's hidden target)
if [ -n "$PRE" ]; then
  pf=${PRE%%:*}; rest=${PRE#*:}
  if [ "$rest" != "$PRE" ]; then echo "${rest#*:}" > "../../Temp/${rest%%:*}"; fi
  u "$pf"; sleep 4
fi
step "demo $script"
echo "run $script | $folder" > ../../Temp/demo.txt
u cs/t_demo.cs
last=""; same=0
while :; do
  sleep 4
  echo status > ../../Temp/demo.txt; call cs/t_demo.cs || fail "status: $R"
  case "$R" in done*|FAILED*|idle*) echo "$R"; break;; esac
  if [ "$R" = "$last" ]; then same=$((same+1)); else same=0; last=$R; fi
  [ $same -ge 30 ] && stall "the demo hasn't moved for 120s: $R"
done
python -I - "$folder" <<'PY'
import sys, glob, os
from PIL import Image
folder = sys.argv[1]
fs = sorted(glob.glob(os.path.join(folder, '*.jpg'))); n = len(fs)
if n:
    pick = [fs[int(k * (n - 1) / 8)] for k in range(9)]
    sheet = Image.new('RGB', (1920, 1080))
    for k, f in enumerate(pick): sheet.paste(Image.open(f).resize((640, 360)), ((k % 3) * 640, (k // 3) * 360))
    sheet.save(os.path.normpath(folder) + '_sheet.jpg', quality=85)
print(n, 'frames')
PY
