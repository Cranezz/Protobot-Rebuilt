var g = Protobot.Game.SkillsGame.Current;
var c = g.Cups[g.Cups.Count - 1];
var p = g.field.ToField(c.transform.position);
return "cups " + g.Cups.Count + ", newest at field (" + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ") height " + g.field.HeightOf(c.transform.position).ToString("F1");
