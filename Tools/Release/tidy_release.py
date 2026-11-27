"""Makes a GitHub release easy to download from: run it after `vpk upload github` (and the Mac upload, if any).

    python -I Tools/Release/tidy_release.py N            # tidy v2.0.0-beta.1.N
    python -I Tools/Release/tidy_release.py N --dry-run  # show what it would do

What it does:
- Puts a "Download" table at the top of the release's notes: the Windows installer, the Mac zip (from this release, or
  the newest release that has one: bug-fix builds don't always rebuild the Mac app) and a line saying the other files
  are for automatic updates. Rerunning replaces the table (it sits between <!-- downloads --> markers).
- Deletes the portable Windows zip (ProtobotRebuiltV2-win-Portable.zip): nobody downloads it (the installer needs no
  admin rights and keeps itself up to date) and it doubled the list of big files. The update files stay: the app's
  updater reads releases.win.json and the .nupkg packages, and installs from before build 40 read RELEASES.

The token comes from git's credential manager (`git credential fill`) and is never printed.
"""
import json
import re
import subprocess
import sys
import urllib.request

REPO = "Cranezz/Protobot-Rebuilt"
API = "https://api.github.com/repos/" + REPO
SETUP = "ProtobotRebuiltV2-win-Setup.exe"
MAC = "ProtobotRebuiltV2-mac.zip"
PORTABLE = "ProtobotRebuiltV2-win-Portable.zip"
START, END = "<!-- downloads -->", "<!-- /downloads -->"


def token():
    out = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n",
                         capture_output=True, text=True)
    for line in out.stdout.splitlines():
        if line.startswith("password="):
            return line[len("password="):]
    sys.exit("no GitHub credential from git (it isn't printed)")


def call(method, url, tok, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": "Bearer " + tok, "Accept": "application/vnd.github+json", "User-Agent": "protobot-release"})
    with urllib.request.urlopen(req) as r:
        text = r.read()
        return json.loads(text) if text else None


def size(asset):
    return f"{asset['size'] / 1048576:.0f} MB"


def table(release, mac_asset, mac_build):
    setup = next((a for a in release["assets"] if a["name"] == SETUP), None)
    rows = ["| | Download | |", "|---|---|---|"]
    if setup:
        rows.append(f"| **Windows** | **[{SETUP}]({setup['browser_download_url']})** ({size(setup)}) | "
                    "Run it once: it installs Protobot (no admin needed) and updates it by itself from then on. "
                    "If Windows says it protected your PC, click **More info**, then **Run anyway**. |")
    if mac_asset:
        built = "" if mac_build is None else f" (from build {mac_build})"
        rows.append(f"| **Mac** (test version) | [{MAC}]({mac_asset['browser_download_url']}) ({size(mac_asset)}){built} | "
                    "Intel and Apple Silicon. Unzip it, then right-click the app and choose **Open** the first time. "
                    "Macs don't update by themselves. |")
    return "\n".join([START, "## Download", "", *rows, "",
                      "_The other files below (.nupkg, RELEASES, releases.win.json) are what Protobot's automatic updates "
                      "download. You don't need them._", END])


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    dry = "--dry-run" in sys.argv
    if not args:
        sys.exit(__doc__)
    build = int(args[0])
    tok = token()
    release = call("GET", f"{API}/releases/tags/v2.0.0-beta.1.{build}", tok)

    mac_asset, mac_build = next((a for a in release["assets"] if a["name"] == MAC), None), None
    if mac_asset is None:   # the newest release that has a Mac app
        for r in call("GET", f"{API}/releases?per_page=30", tok):
            m = next((a for a in r["assets"] if a["name"] == MAC), None)
            if m and not r["prerelease"]:
                mac_asset = m
                found = re.search(r"\.(\d+)$", r["tag_name"])
                mac_build = int(found.group(1)) if found else None
                break

    block = table(release, mac_asset, mac_build)
    body = release.get("body") or ""
    if START in body and END in body:
        body = body[:body.index(START)] + block + body[body.index(END) + len(END):]
    else:
        # After the notes' title line, so the release still opens with its name.
        lines = body.split("\n")
        at = 1 if lines and lines[0].startswith("# ") else 0
        body = "\n".join(lines[:at] + ["", block, ""] + lines[at:])

    portable = [a for a in release["assets"] if a["name"] == PORTABLE]
    print(block)
    print(("would delete: " if dry else "deleting: ") + (", ".join(a["name"] for a in portable) or "nothing"))
    if dry:
        return
    call("PATCH", f"{API}/releases/{release['id']}", tok, {"body": body})
    for a in portable:
        call("DELETE", f"{API}/releases/assets/{a['id']}", tok)
    print("done:", release["html_url"])


if __name__ == "__main__":
    main()
