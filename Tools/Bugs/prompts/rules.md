You are working on Protobot Rebuilt V2, a Unity 6 VEX robot builder, in C:\Dev\ProtobotRebuiltV2\ProtobotRebuiltV2
(git branch v2-classic-protobot, pushed with `git push origin v2-classic-protobot:main`). This run was started by the bug
watcher (Tools/Bugs/watch.py) because a bug report arrived. The owner asked for bugs to be handled fully automatically:
nobody is watching this run, so never stop to ask a question; anything that needs the owner goes in your final answer and
as a comment on the GitHub issue.

Read `Tools/Bugs/README.md` first. Read `CLAUDE.md` and `AGENT_HANDOFF.md` (long) only as far as your task needs.

Rules that always apply:
- The GitHub repository is public: issues and comments contain only your own summary of a bug, never a player's name,
  id, Discord name, their words copied, their files, screenshots or builds (players can be under 13). Reports stay in
  C:/Dev/ProtobotRebuiltV2/Bug Reports and are never committed.
- The owner is using the computer: never bring the Unity window to the front. Drive Unity through `Tools/AgentTesting`
  (umcp.py, lib.sh: compile/play/state with stall checks; Play runs unfocused after cs/t_playunfocused.cs; step frames
  with cs/t_step.cs). Never touch any other Unity project. If the Unity editor isn't running with this project (umcp.py
  can't reach it), start it in the background (Unity Hub's editor 6000.3.21f1 with -projectPath to this folder; never
  print its command line) and wait until umcp.py answers.
- Revert the font atlases (Assets/ProtobotRebuilt/Fonts/Roboto/*SDF.asset and Assets/TextMesh Pro/Resources/Fonts &
  Materials/LiberationSans SDF - Fallback.asset) before committing, never add a co-author line or mention Claude in
  commits, never commit CLAUDE.md or AGENT_HANDOFF.md (git-ignored) or Assets/Resources (test leftovers), never push
  backup/* branches.
- Never print secrets (the GitHub token from git credential fill, the Discord bot token, webhooks).
- Don't post in Discord except where `bugs.py` and `post_release.py --short` do.
