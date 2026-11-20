# stop play, compile, wait, report errors
python -I umcp.py cs cs/t_playstop.cs >/dev/null; sleep 4; python -I umcp.py cs cs/t_compile.cs >/dev/null; sleep 25
for i in $(seq 1 30); do r=$(python -I umcp.py cs cs/t_mon0.cs 2>&1); echo "$r" | grep -q "compiling False" && break; sleep 4; done
bash errs.sh | head -30
