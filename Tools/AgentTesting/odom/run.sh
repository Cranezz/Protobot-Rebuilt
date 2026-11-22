# Runs a program file (robot built: odom/setup.sh) and prints its marks with the truth as they come; stops at once with
# "STALLED: ..." when the program or Unity stops moving. Usage: bash odom/run.sh odom/x.py [max seconds]
. ./lib.sh
run_program "$1" "${2:-1800}"
step "done $1"
