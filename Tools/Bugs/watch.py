"""The bug watcher: runs in the background on this computer and starts a Claude Code fix run as soon as a bug report
arrives, so nobody has to.

Every minute it asks Discord (through Proto Bot, a few cheap REST calls, no Claude usage) whether #app-bug-reports or the
#bugs forum has something newer than what bugs.py has filed. When it does, it waits WAIT seconds (reports often come
in twos and threes), then runs

three kinds of Claude Code run in the project folder (prompts in Tools/Bugs/prompts, each with rules.md first):
  1. triage (haiku, medium): files the new reports (bugs.py) and plans the fixes: for each open P0-P2 bug the cheapest
     model and effort that will fix it (haiku/low for a one-line fix ... opus/xhigh for an unclear crash);
  2. a fix run per bug with that model and effort (one more try on opus/high when a cheaper model couldn't);
  3. release (sonnet, low) when anything was fixed: the bug-fix build, GitHub, the one-line #releases post, closing.
One run at a time; what each did (and cost) goes to C:/Dev/ProtobotRebuiltV2/Bug Reports/runs/<time>.log.

A release that fails (the fixes stay committed) is kept in pending_release.json and tried again every 30 minutes,
up to RELEASE_TRIES times.

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
PENDING = os.path.join(ROOT, "pending_release.json")   # fixes committed but not released yet
RELEASE_TRIES = 3
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


# ------------------------------------------------------------------ the runs: triage, fixes, release

SCHEMAS = {
    "triage": {"type": "object", "properties": {
        "plan": {"type": "array", "items": {"type": "object", "properties": {
            "issue": {"type": "integer"}, "title": {"type": "string"}, "priority": {"type": "string"},
            "model": {"type": "string", "enum": ["haiku", "sonnet", "opus"]},
            "effort": {"type": "string", "enum": ["low", "medium", "high", "xhigh"]}, "why": {"type": "string"}},
            "required": ["issue", "model", "effort"]}},
        "filed": {"type": "array", "items": {"type": "string"}}, "note": {"type": "string"}}, "required": ["plan"]},
    "fix": {"type": "object", "properties": {"fixed": {"type": "boolean"}, "summary": {"type": "string"},
                                             "note": {"type": "string"}}, "required": ["fixed"]},
    "release": {"type": "object", "properties": {"released": {"type": "boolean"}, "build": {"type": "integer"},
                                                 "note": {"type": "string"}}, "required": ["released"]},
}
TRIAGE = ("haiku", "medium")    # reading and filing reports: the cheapest model
RELEASE = ("sonnet", "low")     # building and publishing: steps written down
ESCALATE = ("opus", "high")     # a fix a cheaper model couldn't do gets one more try
NL = chr(10)


def prompt(name, **fill):
    text = open(os.path.join(HERE, "prompts", "rules.md"), encoding="utf-8").read() + NL + NL
    text += open(os.path.join(HERE, "prompts", name + ".md"), encoding="utf-8").read()
    for k, v in fill.items():
        text = text.replace("{" + k + "}", str(v))
    return text


def claude(stage, text, model, effort, out):
    """One Claude Code run in the project folder; its structured answer (a dict) or None."""
    out.write(NL + f"## {stage}: {model}, effort {effort}" + NL)
    out.flush()
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    try:
        p = subprocess.run([CLAUDE, "-p", text, "--model", model, "--effort", effort, "--permission-mode", "bypassPermissions",
                            "--output-format", "json", "--json-schema", json.dumps(SCHEMAS[stage.split()[0]])],
                           cwd=PROJECT, capture_output=True, text=True, encoding="utf-8", errors="replace",
                           stdin=subprocess.DEVNULL, timeout=RUN_LIMIT, creationflags=flags)
    except subprocess.TimeoutExpired:
        out.write("timed out" + NL)
        return None
    answer = None
    try:
        whole = json.loads(p.stdout)
        answer = whole.get("structured_output")
        if answer is None and isinstance(whole.get("result"), str):
            r = whole["result"]
            answer = json.loads(r[r.index("{"):r.rindex("}") + 1])
        out.write(f"cost ${whole.get('total_cost_usd', 0):.2f}, {whole.get('num_turns', '?')} turns" + NL)
    except Exception:
        out.write("couldn't read the answer: " + (p.stdout or "")[-1500:] + (p.stderr or "")[-800:] + NL)
    out.write(json.dumps(answer, indent=1) + NL)
    out.flush()
    return answer


def fix_run(reason):
    os.makedirs(RUNS, exist_ok=True)
    name = datetime.datetime.now().strftime("%Y-%m-%d %H%M%S") + ".log"
    log(f"starting ({reason}) -> runs/{name}")
    with open(os.path.join(RUNS, name), "w", encoding="utf-8") as out:
        out.write(f"# Bug run, {reason}" + NL)
        plan = claude("triage", prompt("triage"), *TRIAGE, out)
        if plan is None:
            log("triage failed")
            return False
        for line in plan.get("filed", []):
            log("filed: " + line)
        fixes = []
        for item in plan.get("plan", [])[:5]:
            n, title = item["issue"], item.get("title", "")
            model, effort = item.get("model", "sonnet"), item.get("effort", "medium")
            log(f"fixing #{n} with {model} ({effort}): {item.get('why', '')}")
            result = claude("fix #%d" % n, prompt("fix", issue=n, title=title), model, effort, out)
            if (result is None or not result.get("fixed")) and (model, effort) != ESCALATE and model != "opus":
                log(f"#{n} not fixed by {model}: trying {ESCALATE[0]} ({ESCALATE[1]})")
                result = claude("fix #%d" % n, prompt("fix", issue=n, title=title), *ESCALATE, out)
            if result and result.get("fixed"):
                fixes.append((n, result.get("summary") or title))
                log(f"#{n} fixed")
            else:
                log(f"#{n} left open: " + ((result or {}).get("note") or "no answer")[:200])
        if fixes:
            old = pending_release()
            fixes = old.get("fixes", []) + [[n, s] for n, s in fixes if n not in [f[0] for f in old.get("fixes", [])]]
            json.dump({"fixes": fixes, "tries": 0}, open(PENDING, "w", encoding="utf-8"))
            release(out)
        if plan.get("note"):
            log("note: " + plan["note"][:300])
    log("run finished")
    return True


def pending_release():
    try:
        return json.load(open(PENDING, encoding="utf-8")) if os.path.exists(PENDING) else {}
    except Exception:
        return {}


def release(out):
    """Releases the fixes in pending_release.json; keeps them there (for a retry) when it fails. True when released."""
    p = pending_release()
    if not p.get("fixes"):
        return True
    p["tries"] = p.get("tries", 0) + 1
    json.dump(p, open(PENDING, "w", encoding="utf-8"))
    lines = NL.join(f"- #{n}: {s}" for n, s in p["fixes"])
    done = claude("release", prompt("release", fixes=lines), *RELEASE, out)
    if done and done.get("released"):
        os.remove(PENDING)
        log(f"release: build {done.get('build')}")
        return True
    log(f"release FAILED (try {p['tries']} of {RELEASE_TRIES}): " + ((done or {}).get("note") or "no answer")[:300])
    if p["tries"] >= RELEASE_TRIES:
        log("giving up on this release: the fixes stay committed; release them by hand (Tools/Bugs/README.md step 4)")
        os.replace(PENDING, PENDING.replace(".json", " (gave up).json"))
    return False


def retry_release():
    """A release that failed before is tried again (after the quiet time), in a run of its own."""
    if not pending_release().get("fixes") or runs_today() >= MAX_RUNS_PER_DAY or not signed_in():
        return None
    os.makedirs(RUNS, exist_ok=True)
    name = datetime.datetime.now().strftime("%Y-%m-%d %H%M%S") + ".log"
    log(f"retrying the release -> runs/{name}")
    with open(os.path.join(RUNS, name), "w", encoding="utf-8") as out:
        out.write("# Release retry" + NL)
        return release(out)


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
        print("new:", found or "nothing", "| runs today:", runs_today(), "| claude signed in:", signed_in(),
              "| waiting to release:", [f[0] for f in pending_release().get("fixes", [])] or "nothing")
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
                    elif pending_release().get("fixes"):
                        quiet_until = time.time() + 1800   # its release failed: retried after a while
                elif retry_release() is False:
                    quiet_until = time.time() + 1800
            time.sleep(POLL)
    finally:
        try:
            os.remove(LOCK)
        except OSError:
            pass


if __name__ == "__main__":
    main()
