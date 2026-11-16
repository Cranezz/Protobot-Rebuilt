// Perf test: Skills on the FULL field (worst case) with whatever build is loaded.
UnityEditor.EditorApplication.isPaused = false;
var def = Resources.LoadAll<Protobot.Game.GameDefinition>("Games")[0];
Protobot.Game.SkillsGame.Open(def, false, Protobot.Game.SkillsGame.Mode.Skills);
return "opened";
