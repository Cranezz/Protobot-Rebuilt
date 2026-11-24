# Builds the Windows player (cs/build.cs) with every step checked (lib.sh): leaves Play, starts the build, and waits for
# build_result.txt while Unity's log keeps moving (Unity can't answer while it builds). Usage: bash release_build.sh
cd "$(dirname "${BASH_SOURCE[0]}")" || exit 1   # (lib.sh, umcp.py and the cs scripts are beside it)
. ./lib.sh
step "leave Play for the build"
state
if [ "$P" = "True" ]; then u cs/t_playstop.cs >/dev/null; wait_play False 120; fi
rm -f build_result.txt
step "build the player"
# (the call returns or times out while Unity builds: the build goes on either way)
timeout 60 python -I umcp.py cs cs/build.cs >/dev/null 2>&1 &
start=$SECONDS; last_size=-1; quiet_since=$SECONDS
while [ ! -f build_result.txt ]; do
  sleep 10
  size=$(stat -c %s "$LOG" 2>/dev/null || echo 0)
  if [ "$size" != "$last_size" ]; then last_size=$size; quiet_since=$SECONDS; note "building, $((SECONDS - start))s"; fi
  [ $((SECONDS - quiet_since)) -ge 180 ] && stall "the build: Unity's log hasn't moved for 180s"
  [ $((SECONDS - start)) -ge 3600 ] && stall "the build ran past an hour"
done
cat build_result.txt; echo
grep -q "^Succeeded" build_result.txt || fail "the build didn't succeed"
step "built, build number $(cat ../../Assets/ProtobotRebuilt/Resources/Version/build.txt)"
