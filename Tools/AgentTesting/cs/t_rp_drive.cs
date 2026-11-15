// Play mode, game open: starts a run with the drive going forward and turning (test sticks).
Protobot.Motion.Sandbox.TestSticks = () => (new UnityEngine.Vector2(0.35f, 1f), UnityEngine.Vector2.zero);
Protobot.Game.SkillsGame.Current.StartRun();
return "run " + Protobot.Game.SkillsGame.Current.state;
