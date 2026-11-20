# run runs/$1.cpp for $2 seconds (period $3: a or d)
printf 'UnityEngine.PlayerPrefs.SetString("t_hdr_prog", @"C:/Users/kille/AppData/Local/Temp/claude/5b0c4ec0a185a9e3/8d21650f-8a3d-4b32-911f-851c0b975844/scratchpad/runs/%s.cpp"); UnityEngine.PlayerPrefs.SetString("t_hdr_period", "%s"); UnityEngine.PlayerPrefs.SetFloat("t_run_limit", %s); return "ok";' "$1" "${3:-a}" "${4:-15}" > cs/t_runix.cs
python -I umcp.py cs cs/t_runix.cs >/dev/null
python -I umcp.py cs cs/t_runfile.cs | grep -o '"result":"[^"]*"'
sleep "$2"
python -I umcp.py cs cs/t_proj3.cs | python -I -c "import sys,json; d=json.loads(sys.stdin.read()); print(d['data']['result'] if d.get('success') else d)"
