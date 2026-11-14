var g = Protobot.Game.SkillsGame.Current;
g.SetSkipAutonomous(true);
g.StartRun();
g.countdown = 0.2f;
UnityEditor.EditorApplication.isPaused = false;
return "started " + g.state;
