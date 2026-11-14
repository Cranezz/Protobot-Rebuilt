var def = Resources.LoadAll<Protobot.Game.GameDefinition>("Games")[0];
Protobot.Game.SkillsGame.Open(def, true);
return "open " + (Protobot.Game.SkillsGame.Current != null) + " " + def.name;
