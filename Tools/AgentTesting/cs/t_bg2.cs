var games = Protobot.Game.GameDefinition.All();
Protobot.Game.SkillsGame.Open(games[0], false);
return "game " + Protobot.Game.SkillsGame.Active;
