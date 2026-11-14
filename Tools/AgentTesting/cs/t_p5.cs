var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder();
g.SetTurn(0f); g.SetSlide(24f);
var tc = g.ToolCorners(); float tmin = 999, tmax = -999; foreach (var c in tc) { tmin = UnityEngine.Mathf.Min(tmin, c.y); tmax = UnityEngine.Mathf.Max(tmax, c.y); }
sb.Append("tool y " + tmin.ToString("0.00") + ".." + tmax.ToString("0.00") + "\n");
g.SetSlide(-10f);
float rmax = -999, rmin = 999; foreach (var c in g.Corners()) { rmax = UnityEngine.Mathf.Max(rmax, c.y); rmin = UnityEngine.Mathf.Min(rmin, c.y); }
sb.Append("slid toward tool: slide " + g.Slide.ToString("0.000") + " robot y " + rmin.ToString("0.00") + ".." + rmax.ToString("0.00") + " hits " + g.HitsTool() + "\n");
g.SetTurn(45f);
sb.Append("turned 45: slide " + g.Slide.ToString("0.00") + " hits " + g.HitsTool() + "\n");
g.SetTurn(0f);
return sb.ToString();
