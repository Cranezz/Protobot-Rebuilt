var g = Protobot.Game.SkillsGame.Current;
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var pins = (System.Collections.Generic.List<Protobot.Game.Pin>)typeof(Protobot.Game.SkillsGame).GetField("pins", bf).GetValue(g);
var cups = (System.Collections.Generic.List<Protobot.Game.Cup>)typeof(Protobot.Game.SkillsGame).GetField("cups", bf).GetValue(g);
var toggles = (System.Collections.Generic.List<Protobot.Game.Toggle>)typeof(Protobot.Game.SkillsGame).GetField("toggles", bf).GetValue(g);
var s = Protobot.Game.OverrideScoring.Evaluate(g.field, pins, cups, toggles, false, true, 0, 0, true);
return "match score: red " + s.red + " blue " + s.blue;
