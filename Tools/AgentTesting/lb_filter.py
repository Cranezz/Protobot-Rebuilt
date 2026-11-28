"""Copies the name filter's lists from NameFilter.cs into Assets/Protobot/Cloud/DiscordLeaderboard.js and Events.js (between its
/*ANYWHERE*/, /*WORDS*/ and /*FINE*/ markers, or replacing the lists there), so Discord shows names as the app does.
Run it after changing NameFilter.cs, then publish the script (cs/t_cc_publish.cs with t_cc_names = DiscordLeaderboard)."""
import os
import re

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
cs = open(os.path.join(ROOT, "Assets/ProtobotRebuilt/Scripts/Online/NameFilter.cs"), encoding="utf-8").read()


def arr(name):
    m = re.search(name + r"\s*=\s*\{(.*?)\};", cs, re.S)
    return re.findall(r'"([^"]*)"', m.group(1))


for script in ("DiscordLeaderboard", "Events"):   # V2 (build 88): Events.js filters event names too
  js_path = os.path.join(ROOT, "Assets/Protobot/Cloud/%s.js" % script)
  js = open(js_path, encoding="utf-8").read()
  for js_name, cs_name, marker in (("ANYWHERE_ROT", "AnywhereRot", "ANYWHERE"), ("WORD_ROT", "WordRot", "WORDS"), ("FINE", "Fine", "FINE")):
    items = ", ".join('"%s"' % x for x in arr(cs_name))
    js, n = re.subn(r"const %s = \[[^\]]*\];" % js_name, "const %s = [/*%s*/%s];" % (js_name, marker, items), js)
    assert n == 1, js_name
open(js_path, "w", encoding="utf-8", newline="\n").write(js)
print("lists copied:", ", ".join("%s %d" % (c, len(arr(c))) for c in ("AnywhereRot", "WordRot", "Fine")))
