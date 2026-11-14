var g = Protobot.Game.SkillsGame.Current;
if (g == null) return "no game";
g.EndRun();
return "ending (" + g.state + ")";
