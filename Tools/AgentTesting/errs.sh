#!/bin/bash
# Prints this session's distinct console errors (first line of each), then clears the console.
cd "C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting"
python -I umcp.py read_console '{"action":"get","types":["error"],"count":60,"format":"plain"}' | python -c "
import sys, json
raw = sys.stdin.read()
try:
    d = json.loads(raw)['data']
except Exception:
    print(raw[:1500]); sys.exit()
seen = {}
for e in d or []:
    first = e.strip().split('\n')[0][:300]
    seen[first] = seen.get(first, 0) + 1
for k, v in seen.items(): print(v, 'x', k)
print('errors:', len(d or []))
"
python -I umcp.py read_console '{"action":"clear"}' > /dev/null
