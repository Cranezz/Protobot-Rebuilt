# Drives with test sticks ($1, e.g. "0.6,0.6") for ~1.5 s, releases, then prints the pose 12 times (~4 s).
python -I umcp.py cs cs/t_sbrestart.cs >/dev/null
powershell -NoProfile -Command "\$p = Get-Process Unity | ? { \$_.MainWindowTitle -like '*ProtobotRebuiltV2*' } | select -First 1; (New-Object -ComObject WScript.Shell).AppActivate(\$p.Id) | Out-Null"
echo "$1" > ../../Temp/sticks.txt; python -I umcp.py cs cs/t_keydrive.cs >/dev/null
for i in 1 2 3; do python -I umcp.py cs cs/t_still.cs >/dev/null; done
echo "0,0" > ../../Temp/sticks.txt; python -I umcp.py cs cs/t_keydrive.cs >/dev/null
for i in $(seq 1 12); do python -I umcp.py cs cs/t_still.cs | grep -o 'pos [^|"]*'; done
