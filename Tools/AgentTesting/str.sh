# String tests: source lib.sh, then s3 "command" runs t_str3.cs with that command (see its header).
. ./lib.sh
s3() { echo "$1" > ../../Temp/str3.txt; u cs/t_str3.cs ${2:-180}; }
