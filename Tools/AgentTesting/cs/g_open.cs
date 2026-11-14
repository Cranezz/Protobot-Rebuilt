UnityEditor.EditorApplication.isPaused = false;
var def = Resources.LoadAll<Protobot.Game.GameDefinition>("Games")[0];
Protobot.Game.SkillsGame.Open(def, true);
var g = Protobot.Game.SkillsGame.Current;
return "start pose x " + g.x + " y " + g.y + " heading " + g.heading;
