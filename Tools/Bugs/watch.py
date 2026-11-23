"""The bug watcher: runs in the background on this computer and starts a Claude Code fix run as soon as a bug report
arrives, so nobody has to.

Every minute it asks Discord (through Proto Bot, a few cheap REST calls, no Claude usage) whether #app-bug-reports or the
#bugs forum has something newer than what bugs.py has filed. When it does, it waits WAIT seconds (reports often come
in twos and threes), then runs

    claude -p <Tools/Bugs/fix_prompt.md> --permission-mode bypassPermissions

in the project folder, one run at a time, and writes what it did to C:/Dev/ProtobotRebuiltV2/Bug Reports/runs/<time>.log.
The run itself (fix_prompt.md -> Tools/Bugs/README.md) files the reports, fixes P0-P2 bugs and releases the fix.

Limits so it can't run away: at most MAX_RUNS_PER_DAY runs a day, and after a failed run it waits 30 minutes.

    pythonw -I Tools/Bugs/watch.py            in the background (no window)
    python  -I Tools/Bugs/watch.py --once     one check (and a run if something is new), then stop
    python  -I Tools/Bugs/watch.py --status   what it would do now, without running anything

Claude Code (the `claude` command) must be signed in once in a terminal: `claude`, then /login.
"""
import datetime
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(HERE, "..", "Discord"))
import discord_api as D  # noqa: E402

ROOT = "C:/Dev/ProtobotRebuiltV2/Bug Reports"
STATE = os.path.join(ROOT, "state.json")
RUNS = os.path.join(ROOT, "runs")
WATCH_LOG = os.path.join(ROOT, "watch.log")
LOCK = os.path.join(ROOT, "watch.lock")
CLAUDE = os.path.expanduser("~/.local/bin/claude.exe")
POLL = 60            # seconds between checks
WAIT = 120           # after a new report, before starting (more may come)
MAX_RUNS_PER_DAY = 6
RUN_LIMIT = 4 * 3600  # a run that takes longer is stopped


def log(text):
    os.makedirs(ROOT, exist_ok=True)
    line = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S") + "  " + text
    with open(WATCH_LOG, "a", encoding="utf-8") as f:
        f.write(line + "\n")
    if sys.stdout is not None:
        try:
            print(line, flush=True)
        except Exception:
            pass


def state():
    return json.load(open(STATE, encoding="utf-8")) if os.path.exists(STATE) else {"app_after": None, "threads": {}}


def pending():
    """What's in Discord that bugs.py hasn't filed yet (a short description per item), without changing anything."""
    st, s = state(), json.load(open(D.STATE_FILE, encoding="utf-8"))
    me = D.call("GET", "/users/@me")["id"]
    found = []
    after = st.get("app_after")
    page = D.call("GET", f"/channels/{s['channels']['app_bugs']}/messages?limit=50" + (f"&after={after}" if after else ""))
    found += [f"app report {m['id']}" for m in page if m["author"].get("id") != me]
    forum = s["channels"]["bugs"]
    threads = [t for t in D.call("GET", f"/guilds/{s['guild']}/threads/active")["threads"] if t.get("parent_id") == forum]
    for t in threads:
        if t.get("owner_id") == me:
            continue
        seen = st.get("threads", {}).get(t["id"])
        last = t.get("last_message_id")
        if seen is None or (last and int(last) > int(seen)):
            # (the bot's own reply is the newest message after it answers: not new)
            newest = D.call("GET", f"/channels/{t['id']}/messages?limit=1")
            if newest and newest[0]["author"].get("id") != me:
                found.append(f"forum post {t['id']} ({t.get('name', '')[:40]})")
    return found


def runs_today():
    if not os.path.isdir(RUNS):
        return 0
    today = datetime.date.today().strftime("%Y-%m-%d")
    return sum(1 for f in os.listdir(RUNS) if f.startswith(today))


def signed_in():
    try:
        out = subprocess.run([CLAUDE, "auth", "status"], capture_output=True, text=True, timeout=60)
        return '"loggedIn": true' in out.stdout
    except Exception:
        return False


def fix_run(reason):
    os.makedirs(RUNS, exist_ok=True)
    name = datetime.datetime.now().strftime("%Y-%m-%d %H%M%S") + ".log"
    path = os.path.join(RUNS, name)
    prompt = open(os.path.join(HERE, "fix_prompt.md"), encoding="utf-8").read()
    log(f"starting a fix run ({reason}) -> runs/{name}")
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    with open(path, "w", encoding="utf-8") as out:
        out.write(f"# Fix run, {reason}\n\n")
        out.flush()
        try:
            p = subprocess.run([CLAUDE, "-p", prompt, "--permission-mode", "bypassPermissions"], cwd=PROJECT,
                               stdout=out, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, timeout=RUN_LIMIT,
                               creationflags=flags)
            code = p.returncode
        except subprocess.TimeoutExpired:
            code = "timeout"
    log(f"fix run finished: {code}")
    return code == 0


def check_once():
    try:
        found = pending()
    except Exception as e:
        log("couldn't check Discord: " + str(e)[:200])
        return "error"
    if not found:
        return "nothing"
    log("new: " + "; ".join(found))
    if runs_today() >= MAX_RUNS_PER_DAY:
        log(f"already {MAX_RUNS_PER_DAY} runs today: waiting for tomorrow")
        return "limit"
    if not signed_in():
        log("Claude Code isn't signed in (run `claude` in a terminal, then /login): can't start a fix run")
        return "signin"
    return "ok" if fix_run(", ".join(found)) else "failed"


def main():
    if "--status" in sys.argv:
        found = pending()
        print("new:", found or "nothing", "| runs today:", runs_today(), "| claude signed in:", signed_in())
        return
    if "--once" in sys.argv:
        print(check_once())
        return
    # One watcher at a time (the lock holds its process id).
    if os.path.exists(LOCK):
        try:
            pid = int(open(LOCK).read().strip())
            alive = subprocess.run(["tasklist", "/FI", f"PID eq {pid}"], capture_output=True, text=True).stdout
            if str(pid) in alive:
                log(f"another watcher is running (pid {pid}): this one stops")
                return
        except Exception:
            pass
    open(LOCK, "w").write(str(os.getpid()))
    log(f"watching for bug reports (every {POLL}s)")
    quiet_until = 0.0
    try:
        while True:
            if time.time() >= quiet_until:
                try:
                    found = pending()
                except Exception as e:
                    log("couldn't check Discord: " + str(e)[:200])
                    found = []
                if found:
                    time.sleep(WAIT)   # more reports may be on their way: one run for all
                    result = check_once()
                    if result in ("failed", "signin", "error"):
                        quiet_until = time.time() + 1800
                    elif result == "limit":
                        quiet_until = time.time() + 3600
            time.sleep(POLL)
    finally:
        try:
            os.remove(LOCK)
        except OSError:
            pass


if __name__ == "__main__":
    main()
