YOUR TASK (fix, step 2 of 3): fix GitHub issue #{issue} of Cranezz/Protobot-Rebuilt ({title}) and nothing else.

1. Read the issue (`python -I Tools/Bugs/bugs.py list`; the issue text through the GitHub API with the token from
   `git credential fill`, never printed) and its report folder(s) in C:/Dev/ProtobotRebuiltV2/Bug Reports/Inbox (meta.json
   "issue": {issue}; the unpacked report's session.log, logs, build.pbb).
2. Reproduce it first: a cs script in Tools/AgentTesting (or the report's build.pbb loaded in Play mode), so you know
   the cause. Then fix it in the original's style (`// V2:` comments in the original's files, American spelling, inches;
   CLAUDE.md's conventions), compile with lib.sh, and play-test the exact case from the report: it must be gone and
   nothing near it broken.
3. If it's fixed: add a test script if you wrote one, revert the font atlases, and commit (git add only the files you
   changed; a one-line message saying what was wrong and what changed, "#{issue}" in it; no co-author line). Don't push,
   don't bump the build, don't release: step 3 does that. Leave Unity out of Play mode.
4. If you can't reproduce or fix it safely: undo your changes (git checkout the files you touched), comment on the issue
   with what you found (no personal details), and answer fixed = false.

Answer with the JSON the run asks for: "fixed" (true only if it's fixed, committed and play-tested), "summary" (one
user-facing line for the release notes, e.g. "Opening a build while simulating stops the simulation first"), and "note"
(what you found, or why not fixed).
