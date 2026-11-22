# Runs odom/py_turnpivot.py (robot built, Play running) and solves the point the robot turns about from each turn.
. ./lib.sh
run_program odom/py_turnpivot.py 1200
echo "$M" > status_pivot.txt
python -I odom/pivot_solve.py status_pivot.txt
step "done pivot"
