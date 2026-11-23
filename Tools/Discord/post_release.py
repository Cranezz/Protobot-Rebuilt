"""Announces a big Protobot release in #releases: a short post (the headlines, not the whole notes) that pings Update
Pings and is published to servers following the channel.

    python -I Tools/Discord/post_release.py 70 [--no-ping] [--video FILE.mp4]
    python -I Tools/Discord/post_release.py 73 --short      a bug-fix build: one line, no ping, the notes on GitHub

Big releases (new features, a new beta) get the headlines post with an Update Pings mention; bug-fix builds get a
one-line message (the owner's rule: the full notes are on the GitHub release), so the channel stays short. Reads the build's notes from Release/ ("Release v2.0
Beta 1 (build 70).md", or "Release 1.9.N.md" from before). Running it again for the same build edits the post."""
import glob
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from discord_api import call, call_with_file, load_state, save_state  # noqa: E402
import server_content as C  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MAX_POINTS = 6


def find_notes(build):
    for path in glob.glob(os.path.join(ROOT, "Release", "Release *.md")):
        name = os.path.splitext(os.path.basename(path))[0]
        m = re.search(r"\(build (\d+)\)$|\.(\d+)$", name)
        if m and int(m.group(1) or m.group(2)) == build:
            return path, name[len("Release "):]
    sys.exit(f"No release notes for build {build} in Release/")


def tag_for(label, build):
    m = re.match(r"v(\d+)\.(\d+) Beta (\d+) \(build \d+\)", label)
    return f"v{m.group(1)}.{m.group(2)}.0-beta.{m.group(3)}.{build}" if m else f"v{label}"


def headlines(text):
    """The notes' bold headings ("- **Players under 13:** ...")."""
    text = re.sub(r"\n  +", " ", text.replace("\r", ""))
    points = []
    for line in text.split("\n"):
        m = re.match(r"-\s+\*\*(.+?)\*\*:?\s*(.*)", line.strip())
        if not m:
            continue
        head, rest = m.group(1).rstrip(":"), m.group(2)
        # Just the headline, plus its first sentence when that's short ("New name for versions: this is v2.0 Beta 1").
        first = re.split(r"(?<=[.!?])\s", rest, maxsplit=1)[0].strip().rstrip(".")
        short = first and len(first) <= 50 and not first.startswith("(")
        points.append(f"• **{head}**" + (f": {first}" if short else ""))
    return points


def main():
    args = [a for i, a in enumerate(sys.argv[1:], 1) if not a.startswith("--") and sys.argv[i - 1] != "--video"]
    if not args or not args[0].isdigit():
        sys.exit(__doc__)
    build = int(args[0])
    ping = "--no-ping" not in sys.argv
    path, label = find_notes(build)
    text = open(path, encoding="utf-8").read()
    lines = text.replace("\r", "").split("\n")
    summary = next((l.strip("* ").strip() for l in lines[1:] if l.strip() and not l.lstrip().startswith("-")), "")
    points = headlines(text)
    more = len(points) - MAX_POINTS
    body = (summary + "\n\n" if summary else "") + "\n".join(points[:MAX_POINTS])
    if more > 0:
        body += f"\n…and {more} more."
    url = f"{C.REPO}/releases/tag/{tag_for(label, build)}"
    if "--short" in sys.argv:
        # One line: what it is (the notes' first bold line, "Bug fixes."), and where the full notes are.
        what = summary.rstrip(".") if summary and len(summary) < 80 else "Bug fixes"
        ping = False
        short_text = f"🛠️ **Protobot {label}**: {what}. It updates itself; full notes: <{url}>"
    embed = {
        "title": f"🆕 Protobot {label}",
        "url": url,
        "color": C.BLUE,
        "description": body[:4000],
        "fields": [{"name": "Get it", "value": f"Protobot updates itself (Help > Check for updates). [Full notes and download]({url})", "inline": False}],
    }
    state = load_state()
    channel = state["channels"]["releases"]
    role = state["roles"]["ping_updates"]
    message = {"content": (f"<@&{role}> " if ping else "") + f"**Protobot {label}** is out!", "embeds": [embed],
               "allowed_mentions": {"parse": [], "roles": [role] if ping else []}}
    if "--short" in sys.argv:
        message = {"content": short_text, "embeds": [], "allowed_mentions": {"parse": []}}
    posted = state.setdefault("releases", {})
    mid = posted.get(str(build))
    if mid and call("GET", f"/channels/{channel}/messages/{mid}", ok404=True):
        call("PATCH", f"/channels/{channel}/messages/{mid}", message)
        print("edited the post for", label)
        return
    video = next((sys.argv[i + 1] for i, a in enumerate(sys.argv) if a == "--video" and i + 1 < len(sys.argv)), None)
    if video:
        if os.path.getsize(video) > 10_000_000:
            sys.exit("The video is over Discord's 10 MB limit for this server: use the 720p copy")
        m = call_with_file("POST", f"/channels/{channel}/messages", message, video, "Protobot " + label + " trailer.mp4")
    else:
        m = call("POST", f"/channels/{channel}/messages", message)
    posted[str(build)] = m["id"]
    save_state(state)
    try:
        call("POST", f"/channels/{channel}/messages/{m['id']}/crosspost")
        print("posted and published", label, "in #releases")
    except RuntimeError as e:
        print("posted", label, "(not published to followers:", str(e)[:120] + ")")


if __name__ == "__main__":
    main()
