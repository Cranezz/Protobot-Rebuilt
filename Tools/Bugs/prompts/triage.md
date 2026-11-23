YOUR TASK (triage, step 1 of 3): file the new reports and plan the fixes. Don't change any code.

1. `python -I Tools/Bugs/bugs.py fetch`. If a NEW report's message.txt is empty and it has no files, Proto Bot can't
   read message content: answer with an empty plan and say so in "note".
2. For each NEW folder, read message.txt and the unpacked report (report.md, session.log: the steps before the bug, and
   errors with their stacks; bugs.log, player.log), then as Tools/Bugs/README.md says: `bugs.py same` (a bug already
   filed: check `bugs.py list` and the issues), `bugs.py issue ... --priority Pn` (a new bug or idea; write the body to
   a temp file first), or `bugs.py skip` (spam, a question, a test). UPDATED folders: comment on the linked issue if
   it adds something.
3. `bugs.py list`. Plan the fixes: every open P0, P1 and P2 bug (never ideas or P3), most urgent first, at most 5. For
   each, look briefly at the code the report points to (the stack, the tool or feature named) and choose who fixes it,
   the cheapest that will do it well:
   - "haiku", "low": wording, a label, a number or a check that is obviously wrong in one place (the stack points at it).
   - "sonnet", "medium": an ordinary bug in one or two files with a clear cause (a null, a missing guard, a layout).
   - "sonnet", "high": several files, or it has to be reproduced in Unity to be sure of the cause.
   - "opus", "high": physics/simulation, multiplayer, performance, robot code (the interpreter, C++), lost or broken
     builds, or a cause that isn't clear from the report.
   - "opus", "xhigh": a P0 crash or data loss whose cause isn't clear.

Answer with the JSON the run asks for: "plan" (issue number, priority, model, effort, one line on why), "filed" (what
became of each new report: "#12 P1 title", "skipped: test"), and "note" (anything the owner should know, or "").
