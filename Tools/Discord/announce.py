"""Posts an announcement in #announcements from a text file (Tools/Discord/announcements/NAME.md), mentioning
Announcement Pings where the text says {ping} and naming channels with {#key} (server_state.json's channel keys).
Published to servers following the channel. Running it again edits the post (its id is kept as messages["announce_NAME"]).

    python -I Tools/Discord/announce.py mac"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from discord_api import call, load_state, save_state  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
NO_EMBEDS = 1 << 2   # links stay links (no GitHub card under the post)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    name = sys.argv[1]
    path = os.path.join(HERE, "announcements", name + ".md")
    text = open(path, encoding="utf-8").read().strip()
    state = load_state()
    channels = state["channels"]
    role = state["roles"]["ping_announce"]
    text = text.replace("{ping}", f"<@&{role}>")
    text = re.sub(r"\{#(\w+)\}", lambda m: f"<#{channels[m.group(1)]}>", text)
    if len(text) > 2000:
        sys.exit(f"{len(text)} characters: Discord allows 2000")
    channel = channels["announcements"]
    messages = state.setdefault("messages", {})
    key = "announce_" + name
    mid = messages.get(key)
    if mid and call("GET", f"/channels/{channel}/messages/{mid}", ok404=True):
        call("PATCH", f"/channels/{channel}/messages/{mid}", {"content": text, "flags": NO_EMBEDS, "allowed_mentions": {"parse": []}})
        print("edited the announcement")
        return
    m = call("POST", f"/channels/{channel}/messages", {"content": text, "flags": NO_EMBEDS, "allowed_mentions": {"parse": [], "roles": [role]}})
    messages[key] = m["id"]
    save_state(state)
    try:
        call("POST", f"/channels/{channel}/messages/{m['id']}/crosspost")
    except RuntimeError as e:
        print("not published to following servers:", str(e)[:200])
    print("announced")


if __name__ == "__main__":
    main()
