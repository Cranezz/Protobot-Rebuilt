var g = Protobot.Game.SkillsGame.Current;
var before = g.Cups.Count;
g.DropMatchLoad("Cup");
return "cups before " + before + ", left " + g.matchLoads["Cup"];
