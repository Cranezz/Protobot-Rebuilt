"""Publishes the tutorial videos (Video/tutorials/NN-*/Protobot tutorial N - *.mp4):

    python -I Tools/Video/publish_tutorials.py github     the GitHub release "tutorials" (a pre-release, never "latest", so
                                                          the app's updater, which skips pre-releases, never sees it): the
                                                          1080p videos as assets and a list of them as its text
    python -I Tools/Video/publish_tutorials.py discord    one post per video in the Discord #tutorials forum, the 720p copy
                                                          attached (under 10 MB) and a link to the full-quality one

Reruns update instead of adding: the GitHub release and its assets are replaced; the Discord posts (their ids kept in
server_state.json as "video_N") get their text edited. The GitHub token comes from `git credential fill` and the bot
token from the private Discord folder; neither is printed."""
import json
import os
import subprocess
import sys
import time

import requests

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "Discord"))
import discord_api as D  # noqa: E402

ROOT = "C:/Dev/ProtobotRebuiltV2/Video/tutorials"
REPO = "Cranezz/Protobot-Rebuilt"
TAG = "tutorials"

# (number, folder, title, what it shows, the #tutorials forum tag)
VIDEOS = [
    (1, "01-getting-started", "Getting started", "Find your way around: the home menu, adding parts, moving the camera, fastening parts with C, undo and saving.", "Start here"),
    (2, "02-drivetrain", "Building a drivetrain", "Build a six-motor drivetrain on 3.25\" omni wheels: screws with F, wheels and gears, copy and paste (Ctrl+C / Ctrl+V), then Mirror for the other side.", "Building"),
    (3, "03-saw-drill-bend", "Saw, drill and bend", "Cut parts to length, drill holes (across and into high strength shafts, too) and bend plates.", "Tools"),
    (4, "04-measure-mirror", "Measure and mirror", "Measure between holes, corners, edges and faces, and mirror parts to the other side of your robot.", "Tools"),
    (5, "05-polycarbonate", "Polycarbonate", "Sketch plastic corner pieces right on the robot, make them in opal white, and mirror them to every corner.", "Building"),
    (6, "06-gears-chains", "Gears, sprockets and chains", "Gear ratios, and making a chain with the Chain tool.", "Motion"),
    (7, "07-motors-devices", "Motors and devices", "Set up motors in the Devices window: ports, keys, reversed, brake or hold, and live readings.", "Motion"),
    (8, "08-simulate", "Simulate and drive", "Drive your robot with real physics: motor torque, gear ratios, wheel grip and weight.", "Simulation"),
    (9, "09-play-game", "Play a game", "Robot Skills and match practice on the V5RC Override field, scored by the game's rules, with replays.", "Game"),
    (10, "10-robot-code", "Robot code", "Run Python, Blocks or C++ (PROS, LemLib, EZ-Template) on your simulated robot.", "Robot code"),
    (11, "11-multiplayer", "Multiplayer", "Host or join online matches with invite codes, voice chat with your alliance, and the Skills leaderboard.", "Online"),
    (12, "12-tips", "Tips and tricks", "Chamfered gear teeth, slip gears, rubber bands round posts, and license plates with your team number.", "Building"),
]


def video(n, folder, title, small=False):
    name = f"Protobot tutorial {n} - {title}" + (" (720p)" if small else "") + ".mp4"
    path = os.path.join(ROOT, folder, name)
    if not os.path.exists(path):
        sys.exit("missing: " + path)
    return path


def asset_name(n, title):
    slug = "-".join(w for w in "".join(c if c.isalnum() else " " for c in title).split())
    return f"Protobot-tutorial-{n:02d}-{slug}.mp4"


def asset_url(n, title):
    return f"https://github.com/{REPO}/releases/download/{TAG}/{asset_name(n, title)}"


# ------------------------------------------------------------------ GitHub

def github_token():
    out = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n",
                         capture_output=True, text=True).stdout
    for line in out.splitlines():
        if line.startswith("password="):
            return line[len("password="):]
    sys.exit("no GitHub credential from git (it isn't printed)")


def github():
    s = requests.Session()
    s.headers.update({"Authorization": "token " + github_token(), "Accept": "application/vnd.github+json",
                      "User-Agent": "protobot-tutorials"})
    api = f"https://api.github.com/repos/{REPO}"
    body = "\n".join([
        "Short videos that show how to use Protobot Rebuilt V2, from your first build to robot code and online play.",
        "They're also in the Protobot Discord's #tutorials channel. Click a video below to watch it in your browser or download it.",
        "",
        *[f"{n}. **[{title}]({asset_url(n, title)})**: {what}" for n, _, title, what, _ in VIDEOS],
        "",
        "This page holds videos only: the app's downloads are on the latest release.",
    ])
    r = s.get(f"{api}/releases/tags/{TAG}")
    meta = {"tag_name": TAG, "name": "Tutorial videos", "body": body, "prerelease": True, "make_latest": "false"}
    if r.status_code == 200:
        release = r.json()
        r = s.patch(f"{api}/releases/{release['id']}", json=meta)
    else:
        r = s.post(f"{api}/releases", json={**meta, "target_commitish": "main"})
    if r.status_code >= 300:
        sys.exit(f"release: {r.status_code} {r.text[:300]}")
    release = r.json()
    have = {a["name"]: a for a in release.get("assets", [])}
    upload = release["upload_url"].split("{")[0]
    for n, folder, title, _, _ in VIDEOS:
        name = asset_name(n, title)
        path = video(n, folder, title)
        if name in have:
            if have[name]["size"] == os.path.getsize(path):
                print("kept", name)
                continue
            s.delete(f"{api}/releases/assets/{have[name]['id']}")
        with open(path, "rb") as f:
            r = s.post(upload, params={"name": name}, data=f, headers={"Content-Type": "video/mp4"}, timeout=600)
        if r.status_code >= 300:
            sys.exit(f"upload {name}: {r.status_code} {r.text[:300]}")
        print("uploaded", name, round(os.path.getsize(path) / 1e6, 1), "MB")
    print("release:", release["html_url"])


# ------------------------------------------------------------------ Discord

def discord():
    state = D.load_state()
    forum_id = state["channels"]["tutorials"]
    forum = D.call("GET", f"/channels/{forum_id}")
    tags = {t["name"]: t["id"] for t in forum.get("available_tags", [])}
    threads = state.setdefault("threads", {})
    # Posted last-first, so the forum lists them 1 to 12 from the top (newest posts show first).
    for n, folder, title, what, tag in reversed(VIDEOS):
        key = f"video_{n}"
        name = f"🎬 Video {n}: {title}"
        content = f"**{what}**\n\nFull quality (1080p): {asset_url(n, title)}\nEvery video: https://github.com/{REPO}/releases/tag/{TAG}"
        applied = [tags[tag]] if tag in tags else []
        tid = threads.get(key)
        thread = D.call("GET", f"/channels/{tid}", ok404=True) if tid else None
        if thread:
            D.call("PATCH", f"/channels/{tid}/messages/{tid}", {"content": content, "allowed_mentions": {"parse": []}})
            D.call("PATCH", f"/channels/{tid}", {"name": name, "applied_tags": applied})
            print("edited", name)
        else:
            path = video(n, folder, title, small=True)
            if os.path.getsize(path) > 10 * 1024 * 1024:
                sys.exit(f"{path} is over Discord's 10 MB upload limit")
            body = {"name": name, "applied_tags": applied, "auto_archive_duration": 10080,
                    "message": {"content": content, "allowed_mentions": {"parse": []}, "attachments": [{"id": 0}]}}
            thread = D.call_with_file("POST", f"/channels/{forum_id}/threads", body, path, asset_name(n, title))
            threads[key] = thread["id"]
            D.save_state(state)
            print("posted", name)
        time.sleep(1.0)


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else ""
    if what == "github":
        github()
    elif what == "discord":
        discord()
    else:
        sys.exit(__doc__)
