#!/bin/bash
# look.sh X Y Z PITCH YAW DISTANCE NAME: aims the camera (Play mode) and saves Assets/Screenshots/NAME.png.
cd "$(dirname "${BASH_SOURCE[0]}")"
printf 'UnityEngine.PlayerPrefs.SetString("t_look", "%s,%s,%s,%s,%s,%s"); UnityEngine.PlayerPrefs.SetString("t_look_name", "%s"); return "ok";' "$1" "$2" "$3" "$4" "$5" "$6" "$7" > cs/_look_set.cs
timeout 60 python -I umcp.py cs cs/_look_set.cs >/dev/null 2>&1
timeout 120 python -I umcp.py cs cs/t_lookat.cs 2>&1 | head -c 200; echo
