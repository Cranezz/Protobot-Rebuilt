# run project $1 for $2 seconds (period $3: a or d) and print the result
printf 'UnityEngine.PlayerPrefs.SetInt("t_proj_index", %s); UnityEngine.PlayerPrefs.SetString("t_hdr_period", "%s"); return "ok";' "$1" "${3:-a}" > cs/t_projix.cs
python -I umcp.py cs cs/t_projix.cs >/dev/null
python -I umcp.py cs cs/t_proj2.cs | grep -o '"result":"[^"]*"'
sleep "$2"
python -I umcp.py cs cs/t_proj3.cs | python -I -c "import sys,json; d=json.loads(sys.stdin.read()); print(d['data']['result'] if d.get('success') else d)"
