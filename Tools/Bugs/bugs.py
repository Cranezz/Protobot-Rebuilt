"""Protobot's bug inbox: bug reports (the app's Report a bug -> #app-bug-reports, and posts in the #bugs forum) become
GitHub issues with a priority, and fixed issues are closed with the build that fixed them.

Private data never goes to GitHub: reports (their text, logs, builds, screenshots, who sent them) are saved only on this
computer in C:/Dev/ProtobotRebuiltV2/Bug Reports. The public issue is a summary written for it (what goes wrong, where,
which version), never the player's name, id, own words or files.

  python -I Tools/Bugs/bugs.py fetch          new reports since the last fetch -> Bug Reports/Inbox/<folder> (prints them)
  python -I Tools/Bugs/bugs.py labels         makes sure the labels exist on GitHub
  python -I Tools/Bugs/bugs.py issue FOLDER --priority P0|P1|P2|P3 --title "..." --body-file FILE [--idea]
                                              opens the issue, links it to the report, tells the forum thread
  python -I Tools/Bugs/bugs.py same FOLDER ISSUE --body-file FILE
                                              a report of a known issue: adds a comment to it and links it
  python -I Tools/Bugs/bugs.py skip FOLDER --why "..."      not a bug (spam, a question): marked done, no issue
  python -I Tools/Bugs/bugs.py list           open bugs and ideas, most urgent first (P0 oldest first)
  python -I Tools/Bugs/bugs.py fixed ISSUE --build N [--note "..."]
                                              closes it as fixed in build N and tells the forum threads that reported it

Priorities (labels):
  P0  blocking: a crash, lost or broken builds, can't open/save, can't finish a robot, can't play or go online
  P1  major: a tool or feature doesn't work right and there's no easy way round it
  P2  minor: annoying or looks wrong, with a way round it
  P3  can wait: polish, ideas and feature requests (with --idea it's labelled "idea", not "bug")
"""
import datetime
import io
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "Discord"))
import discord_api as D  # noqa: E402

REPO = "Cranezz/Protobot-Rebuilt"
ROOT = "C:/Dev/ProtobotRebuiltV2/Bug Reports"
INBOX = os.path.join(ROOT, "Inbox")
STATE = os.path.join(ROOT, "state.json")

PRIORITIES = {
    "P0": ("priority: P0 blocking", "b60205", "Crashes, lost builds, can't save/open, can't finish a robot or play: fixed first"),
    "P1": ("priority: P1 major", "d93f0b", "A tool or feature doesn't work right and there's no easy way round it"),
    "P2": ("priority: P2 minor", "fbca04", "Annoying or looks wrong, with a way round it"),
    "P3": ("priority: P3 can wait", "c2e0c6", "Polish, ideas and feature requests"),
}
OTHER_LABELS = {
    "bug": ("d73a4a", "Something doesn't work"),
    "idea": ("a2eeef", "A new feature or an improvement"),
    "from app": ("ededed", "Reported with Report a bug in the app"),
    "from Discord": ("5865f2", "Reported in the #bugs forum"),
}


# ------------------------------------------------------------------ state

def load_state():
    if os.path.exists(STATE):
        return json.load(open(STATE, encoding="utf-8"))
    return {"app_after": None, "threads": {}, "reports": {}, "issues": {}}


def save_state(s):
    os.makedirs(ROOT, exist_ok=True)
    tmp = STATE + ".tmp"
    json.dump(s, open(tmp, "w", encoding="utf-8"), indent=1)
    os.replace(tmp, STATE)


def server():
    return json.load(open(D.STATE_FILE, encoding="utf-8"))


# ------------------------------------------------------------------ GitHub

def gh_token():
    out = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n", capture_output=True, text=True)
    for line in out.stdout.splitlines():
        if line.startswith("password="):
            return line[len("password="):]
    sys.exit("No GitHub sign-in in git's credential manager (the token is never printed).")


def gh(method, path, body=None):
    req = urllib.request.Request("https://api.github.com" + path, method=method,
                                 data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Authorization": "Bearer " + gh_token(), "Accept": "application/vnd.github+json",
                                          "User-Agent": "protobot-bugs", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req) as r:
            data = r.read()
            return json.loads(data) if data else None
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"GitHub {method} {path}: {e.code} {e.read().decode(errors='replace')[:300]}")


def ensure_labels():
    have = {l["name"] for l in gh("GET", f"/repos/{REPO}/labels?per_page=100")}
    wanted = {name: (color, about) for name, color, about in PRIORITIES.values()}
    wanted.update(OTHER_LABELS)
    for name, (color, about) in wanted.items():
        if name not in have:
            gh("POST", f"/repos/{REPO}/labels", {"name": name, "color": color, "description": about})
            print("label made:", name)


# ------------------------------------------------------------------ Discord -> Inbox

def stamp(iso):
    return datetime.datetime.fromisoformat(iso.replace("Z", "+00:00")).astimezone().strftime("%Y-%m-%d %H%M")


def download(url):
    req = urllib.request.Request(url, headers={"User-Agent": "DiscordBot (protobot-bugs, 1.0)"})
    with urllib.request.urlopen(req) as r:
        return r.read()


def save_report(source, key, when, text, attachments, extra):
    folder = os.path.join(INBOX, f"{stamp(when)} {source} {key}")
    os.makedirs(folder, exist_ok=True)
    files = []
    for a in attachments:
        name = os.path.basename(a["filename"])
        data = download(a["url"])
        open(os.path.join(folder, name), "wb").write(data)
        files.append(name)
        if name.lower().endswith(".zip"):
            try:
                zipfile.ZipFile(io.BytesIO(data)).extractall(os.path.join(folder, os.path.splitext(name)[0]))
            except zipfile.BadZipFile:
                pass
    meta = {"source": source, "key": key, "when": when, "files": files, "status": "new", **extra}
    open(os.path.join(folder, "message.txt"), "w", encoding="utf-8").write(text)
    json.dump(meta, open(os.path.join(folder, "meta.json"), "w", encoding="utf-8"), indent=1)
    return folder


def fetch():
    st, s = load_state(), server()
    me = D.call("GET", "/users/@me")["id"]
    new, updated = [], []
    # The app's reports (one message each, with the report zip).
    channel = s["channels"]["app_bugs"]
    after = st.get("app_after")
    while True:
        page = D.call("GET", f"/channels/{channel}/messages?limit=100" + (f"&after={after}" if after else ""))
        if not page:
            break
        for m in sorted(page, key=lambda x: int(x["id"])):
            after = m["id"]
            if m["author"].get("id") == me:
                continue
            folder = save_report("app", m["id"], m["timestamp"], m.get("content") or "", m.get("attachments", []),
                                 {"channel": channel, "message": m["id"]})
            st["reports"][m["id"]] = folder
            new.append(folder)
        if len(page) < 100:
            break
    st["app_after"] = after
    # The #bugs forum: a post is a report; later messages in it are added to it.
    forum = s["channels"]["bugs"]
    threads = [t for t in D.call("GET", f"/guilds/{s['guild']}/threads/active")["threads"] if t.get("parent_id") == forum]
    threads += D.call("GET", f"/channels/{forum}/threads/archived/public?limit=50").get("threads", [])
    for t in threads:
        if t.get("owner_id") == me:
            continue   # the bot's own pinned guide
        tid = t["id"]
        seen = st["threads"].get(tid)
        messages = D.call("GET", f"/channels/{tid}/messages?limit=100" + (f"&after={seen}" if seen else ""))
        messages = sorted([m for m in messages if m["author"].get("id") != me], key=lambda x: int(x["id"]))
        if not messages:
            continue
        text = "\n\n".join(f"[{m['timestamp'][:16]}] {m.get('content') or ''}" for m in messages)
        attachments = [a for m in messages for a in m.get("attachments", [])]
        if seen is None:
            folder = save_report("discord", tid, t.get("thread_metadata", {}).get("create_timestamp") or messages[0]["timestamp"],
                                 f"Title: {t.get('name', '')}\n\n{text}", attachments, {"thread": tid, "title": t.get("name", "")})
            st["reports"][tid] = folder
            new.append(folder)
        else:
            folder = st["reports"].get(tid)
            if folder and os.path.isdir(folder):
                with open(os.path.join(folder, "message.txt"), "a", encoding="utf-8") as f:
                    f.write("\n\n--- later ---\n" + text)
                for a in attachments:
                    open(os.path.join(folder, os.path.basename(a["filename"])), "wb").write(download(a["url"]))
                updated.append(folder)
        st["threads"][tid] = messages[-1]["id"]
    save_state(st)
    for f in new:
        print("NEW", f)
    for f in updated:
        print("UPDATED", f)
    if not new and not updated:
        print("no new reports")


# ------------------------------------------------------------------ issues

def meta_of(folder):
    return json.load(open(os.path.join(folder, "meta.json"), encoding="utf-8"))


def set_meta(folder, **changes):
    m = meta_of(folder)
    m.update(changes)
    json.dump(m, open(os.path.join(folder, "meta.json"), "w", encoding="utf-8"), indent=1)


def tell_thread(meta, text):
    if meta.get("source") == "discord" and meta.get("thread"):
        try:
            D.call("POST", f"/channels/{meta['thread']}/messages", {"content": text, "allowed_mentions": {"parse": []}})
        except Exception as e:  # a closed/locked thread: the issue still stands
            print("couldn't post in the thread:", str(e)[:150])
    elif meta.get("source") == "app" and meta.get("message"):
        try:
            D.call("PUT", f"/channels/{meta['channel']}/messages/{meta['message']}/reactions/%E2%9C%85/@me")
        except Exception as e:
            print("couldn't react to the report:", str(e)[:150])


def link(st, number, folder):
    st["issues"].setdefault(str(number), [])
    if folder not in st["issues"][str(number)]:
        st["issues"][str(number)].append(folder)


def issue(folder, priority, title, body_file, idea):
    if priority not in PRIORITIES:
        sys.exit("priority must be P0, P1, P2 or P3")
    meta = meta_of(folder)
    body = open(body_file, encoding="utf-8").read().strip()
    body += f"\n\n---\nPriority **{priority}**. Reported {'in the app' if meta['source'] == 'app' else 'on Discord'} on {meta['when'][:10]}" \
            " (the report itself is kept privately)."
    labels = [PRIORITIES[priority][0], "idea" if idea else "bug", "from app" if meta["source"] == "app" else "from Discord"]
    ensure_labels()
    made = gh("POST", f"/repos/{REPO}/issues", {"title": title, "body": body, "labels": labels})
    st = load_state()
    link(st, made["number"], folder)
    save_state(st)
    set_meta(folder, status="issue", issue=made["number"], priority=priority)
    words = "idea" if idea else "bug"
    tell_thread(meta, f"Thanks! This {words} is tracked as {made['html_url']} (priority {priority}: {PRIORITIES[priority][0].split(' ', 2)[2]}). "
                      "You'll hear here when it's fixed.")
    print("issue", made["number"], made["html_url"])


def same(folder, number, body_file):
    meta = meta_of(folder)
    body = open(body_file, encoding="utf-8").read().strip()
    gh("POST", f"/repos/{REPO}/issues/{number}/comments", {"body": f"Reported again ({meta['when'][:10]}, {'app' if meta['source'] == 'app' else 'Discord'}): {body}"})
    st = load_state()
    link(st, number, folder)
    save_state(st)
    found = gh("GET", f"/repos/{REPO}/issues/{number}")
    set_meta(folder, status="issue", issue=int(number))
    tell_thread(meta, f"Thanks! This is already tracked as {found['html_url']}. You'll hear here when it's fixed.")
    print("linked to issue", number)


def skip(folder, why):
    set_meta(folder, status="skipped", why=why)
    print("skipped", folder)


def rank(i):
    names = [l["name"] for l in i.get("labels", [])]
    for p, (name, _, _) in PRIORITIES.items():
        if name in names:
            return p
    return "P?"


def list_open():
    issues = []
    page = 1
    while True:
        got = gh("GET", f"/repos/{REPO}/issues?state=open&per_page=100&page={page}")
        issues += [i for i in got if "pull_request" not in i]
        if len(got) < 100:
            break
        page += 1
    issues = [i for i in issues if any(l["name"] in ("bug", "idea") for l in i["labels"])]
    issues.sort(key=lambda i: (rank(i), i["created_at"]))
    for i in issues:
        kind = "idea" if any(l["name"] == "idea" for l in i["labels"]) else "bug"
        print(f"#{i['number']:<4} {rank(i)} {kind:4} {i['created_at'][:10]}  {i['title']}")
    if not issues:
        print("no open bugs or ideas")


def fixed(number, build, note):
    text = f"Fixed in build {build}." + (f" {note}" if note else "") + " Protobot updates itself (Help > Check for updates)."
    gh("POST", f"/repos/{REPO}/issues/{number}/comments", {"body": text})
    gh("PATCH", f"/repos/{REPO}/issues/{number}", {"state": "closed", "state_reason": "completed"})
    st = load_state()
    for folder in st["issues"].get(str(number), []):
        if os.path.isdir(folder):
            meta = meta_of(folder)
            set_meta(folder, status="fixed", fixed_in=build)
            if meta.get("source") == "discord":
                tell_thread(meta, f"Fixed in build {build}! Protobot updates itself (Help > Check for updates). Thanks for reporting it.")
    print("closed", number)


def main():
    a = sys.argv[1:]
    if not a:
        sys.exit(__doc__)
    opt = lambda name, default=None: a[a.index(name) + 1] if name in a else default
    cmd = a[0]
    if cmd == "fetch":
        fetch()
    elif cmd == "labels":
        ensure_labels()
    elif cmd == "issue":
        issue(a[1], opt("--priority"), opt("--title"), opt("--body-file"), "--idea" in a)
    elif cmd == "same":
        same(a[1], a[2], opt("--body-file"))
    elif cmd == "skip":
        skip(a[1], opt("--why", ""))
    elif cmd == "list":
        list_open()
    elif cmd == "fixed":
        fixed(a[1], opt("--build"), opt("--note", ""))
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
