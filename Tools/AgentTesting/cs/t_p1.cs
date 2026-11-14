var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder();
System.Action<string> rep = (label) => {
  var bad = new System.Collections.Generic.List<string>();
  foreach (var c in g.Checks()) if (!c.ok && !c.rule.StartsWith("Fits")) bad.Add(c.rule.Substring(0, System.Math.Min(28, c.rule.Length)));
  sb.Append(label + ": slide " + g.Slide.ToString("0.00") + " turn " + g.Turn.ToString("0.0") + " x " + g.x.ToString("0.00") + " y " + g.y.ToString("0.00") + " fail [" + string.Join("|", bad) + "]\n");
};
rep("open");
g.SetTurn(30f); rep("turn30");
g.SetTurn(-120f); rep("turn-120");
g.SetTurn(180f); rep("turn180");
g.SetTurn(0f); g.SetSlide(200f); rep("slide200");
g.SetSlide(-200f); rep("slide-200");
g.SetSlide(20f); rep("slide20");
// alignment tool: one of the robot's C-channels
Protobot.SavedObject ch = null;
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) if (so.id.StartsWith("CCHN") || so.id.StartsWith("C-") || so.id.Contains("CHANNEL") || so.id.StartsWith("CHN")) { ch = so; break; }
if (ch == null) { foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) { sb.Append(so.id + " "); } return sb.ToString(); }
var od = new Protobot.Builds.ObjectData(); od.partId = ch.id; 
g.LoadToolParts(new[] { od }, "test jig");
sb.Append("tool " + ch.id + " size " + g.toolSize + " at " + g.toolX.ToString("0.00") + "," + g.toolY.ToString("0.00") + " h " + g.toolHeading + "\n");
return sb.ToString();
