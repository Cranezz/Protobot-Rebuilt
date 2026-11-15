var g = Protobot.Game.SkillsGame.Current; g.BackToSetup(); g.SetPreload(false); Protobot.Motion.Sandbox.TestSticks = () => (Vector2.zero, Vector2.zero); g.StartRun(); return "restarted " + g.state;
