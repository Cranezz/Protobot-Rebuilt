var v = Protobot.Game.ReplayViewer.Instance;
if (v != null) v.Close();
var g = Protobot.Game.SkillsGame.Current;
return "closed; game " + (g != null ? g.state.ToString() : "none") + ", viewer " + Protobot.Game.ReplayViewer.Active;
