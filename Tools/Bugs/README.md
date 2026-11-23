# Bug inbox and bug-fix routine

Bug reports reach Discord two ways: the app's **Report a bug** (Cloud Code `SendReport.js` posts the report zip to the
staff channel #app-bug-reports) and posts in the public **#bugs** forum. `bugs.py` turns them into GitHub issues with a
priority, and a scheduled task ("Protobot bug fixes", daily) works through them: it files new reports, fixes the most
urgent bugs, releases a bug-fix build and closes the issues.

Proto Bot reads the channels (it needs the **Message Content** intent: Discord Developer Portal > Proto Bot > Bot >
Privileged Gateway Intents). The GitHub token is git's own sign-in (`git credential fill`), never printed.

## The watcher (fixes start as soon as a report arrives)
`watch.py` runs in the background (started at Windows logon by "Protobot bug watcher.lnk" in the Startup folder; log in
Bug Reports/watch.log). Every minute it asks Discord, without using Claude, whether anything new is in #app-bug-reports
or the #bugs forum; when there is, it waits 2 minutes (more may come), then runs Claude Code (`claude -p`, unattended,
bypassPermissions) in stages, each on the cheapest model that does it well (prompts in `prompts/`, `rules.md` first):
1. **triage** (haiku, medium): files the reports and plans the fixes, choosing a model and effort per bug: haiku/low for
   a one-place fix the stack points at, sonnet/medium for an ordinary bug, sonnet/high when it must be reproduced in
   Unity, opus/high for physics, multiplayer, performance, robot code or an unclear cause, opus/xhigh for an unclear P0;
2. **fix** (one run per bug, at most 5, with the chosen model): reproduce, fix, play-test, commit (no push); a fix a
   cheaper model couldn't do gets one more try on opus/high;
3. **release** (sonnet, low), when anything was fixed: notes, build, push, GitHub release, one-line #releases post,
   `bugs.py fixed`.
One run at a time, at most 6 a day, a failed run waits 30 minutes; each run's stages, answers and cost in
Bug Reports/runs/. Needs the `claude` command signed in once (`claude`, then /login). `--status` and `--once` check by
hand.

## Privacy (the repository is public)
- Reports stay on this computer: `C:/Dev/ProtobotRebuiltV2/Bug Reports/Inbox/<date> <app|discord> <id>/` (message.txt,
  meta.json, the zip unpacked: report.md, session.log, build.pbb, screenshot.jpg...). Never commit them.
- An issue is a summary you write: what goes wrong, how to make it happen, which version and part of the app. Never the
  player's name, id, Discord name, their own words copied, their files, screenshots or builds. Players under 13 use the
  app too: nothing personal goes public.

## Priorities
| | Label | When |
|---|---|---|
| P0 | priority: P0 blocking | a crash or freeze, lost or broken builds, can't open/save, can't finish a robot, can't play a game or go online |
| P1 | priority: P1 major | a tool or feature doesn't work right and there's no easy way round it |
| P2 | priority: P2 minor | annoying or looks wrong, with a way round it |
| P3 | priority: P3 can wait | polish, ideas and feature requests (`--idea`: labelled "idea", not "bug") |

## The routine (each run)
1. `python -I Tools/Bugs/bugs.py fetch`. For each NEW folder: read message.txt and the unpacked report (report.md,
   session.log: the actions before the bug, errors with stacks). Then:
   - a bug already filed (`bugs.py list`, and the issue texts): `bugs.py same FOLDER ISSUE --body-file F` (F: one or two
     sentences of what's new in this report, no personal details);
   - a new bug or idea: write the issue body to a temp file (what happens, steps, expected, version, area of the app;
     log lines with errors are fine once any user/computer names are taken out), choose the priority from the table,
     `bugs.py issue FOLDER --priority Pn --title "..." --body-file F [--idea]`;
   - not a bug (spam, a question, a test): `bugs.py skip FOLDER --why "..."`.
   UPDATED folders: read what was added; comment on the linked issue if it changes anything.
2. `bugs.py list`. Fix bugs in order: every P0, then P1, then P2, oldest first, as many as fit in the run (at most about
   five; stop before a fix that would be rushed). Never ideas or P3: those wait for the owner.
3. For each fix: reproduce it first (a cs script in `Tools/AgentTesting`, or the report's build.pbb with
   `Tools > Protobot > Open Bug Report…`-style loading), fix it in the original's style (`// V2:` comments in its files,
   American spelling, inches), and play-test the exact case from the report. A bug that can't be reproduced or fixed
   safely: comment on the issue with what was found and leave it open.
4. If anything was fixed: bump the build (`cs/t_bump.cs`), add `Release/Release v2.0 Beta 1 (build N).md` (short, user-facing:
   "Bug fixes:" bullets, one per issue, no personal details), build and release as in CLAUDE.md / AGENT_HANDOFF.md
   ("Build and release routine"): `bash Tools/AgentTesting/release_build.sh`, delete the
   BurstDebugInformation folder, copy the notes into the build folder as "Release notes.md", `vpk pack` (version
   2.0.0-beta.1.N), `vpk upload github --tag v2.0.0-beta.1.N --releaseName "v2.0 Beta 1 (build N)"`, then a one-line
   post in #releases: `python -I Tools/Discord/post_release.py N --short` (the owner's rule: bug fixes get one line, the
   full notes are on GitHub; no ping). Revert the font atlases, commit (no co-author line), push
   `git push origin v2-classic-protobot:main`, then `bugs.py fixed ISSUE --build N` for each fixed issue (closes it and
   tells the forum thread).
5. Never: post in Discord channels other than the bug threads the script answers in and the one-line #releases post, change the server, publish a
   feature release, push the backup/* branches, bring the Unity window to the front (the owner is using the computer),
   or delete reports.
