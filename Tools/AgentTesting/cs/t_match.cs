var games = Protobot.Game.GameDefinition.All();
Protobot.Game.SkillsGame.Open(games[0], true, Protobot.Game.SkillsGame.Mode.Match);
var g = Protobot.Game.SkillsGame.Current;
return "game " + (g != null) + " x " + g.x + " y " + g.y + " h " + g.heading;
