var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder();
g.SetTurn(0f); g.SetSlide(20f);
var od = new Protobot.Builds.ObjectData(); od.partId = "CCHL-1x2-25";
g.LoadToolParts(new[] { od }, "test jig");
sb.Append("tool size " + g.toolSize + " at " + g.toolX.ToString("0.00") + "," + g.toolY.ToString("0.00") + " h " + g.toolHeading + "\n");
var tc = g.ToolCorners(); float minX = 999, maxY = -999, minY = 999; foreach (var c in tc) { minX = UnityEngine.Mathf.Min(minX, c.x); minY = UnityEngine.Mathf.Min(minY, c.y); maxY = UnityEngine.Mathf.Max(maxY, c.y); }
sb.Append("tool minX " + minX.ToString("0.00") + " y " + minY.ToString("0.00") + ".." + maxY.ToString("0.00") + "\n");
var rc = g.Corners(); float rMinY = 999, rMaxY = -999; foreach (var c in rc) { rMinY = UnityEngine.Mathf.Min(rMinY, c.y); rMaxY = UnityEngine.Mathf.Max(rMaxY, c.y); }
sb.Append("robot y " + rMinY.ToString("0.00") + ".." + rMaxY.ToString("0.00") + "\n");
// move the tool 6" further along the wall, then line the robot's side face up against the tool's near face
g.PlaceTool(g.toolX, g.toolY + 6f, g.toolHeading, 1f);
var f = g.field;
var T = g.WallTangent; // +y for Left
float toolNear = 999; foreach (var c in g.ToolCorners()) toolNear = UnityEngine.Mathf.Min(toolNear, UnityEngine.Vector2.Dot(c, T));
float robotFar = -999; foreach (var c in g.Corners()) robotFar = UnityEngine.Mathf.Max(robotFar, UnityEngine.Vector2.Dot(c, T));
var rp = f.ToWorld(new UnityEngine.Vector2(g.x, g.y) + T * (robotFar - UnityEngine.Vector2.Dot(new UnityEngine.Vector2(g.x, g.y), T)), 2f);
var tp = f.ToWorld(new UnityEngine.Vector2(g.toolX, g.toolY) + T * (toolNear - UnityEngine.Vector2.Dot(new UnityEngine.Vector2(g.toolX, g.toolY), T)), 1f);
var n3 = f.transform.TransformDirection(new UnityEngine.Vector3(T.x, 0, T.y));
var msg = g.LineUp(rp, n3, tp, -n3);
robotFar = -999; foreach (var c in g.Corners()) robotFar = UnityEngine.Mathf.Max(robotFar, UnityEngine.Vector2.Dot(c, T));
sb.Append("lineup msg " + (msg ?? "ok") + " gap " + (toolNear - robotFar).ToString("0.000") + " turn " + g.Turn.ToString("0.0") + "\n");
// face mate that needs a turn: robot front face against tool side face
var fr = Protobot.Game.SkillsGame.Dir(g.heading);
var frontPt = f.ToWorld(new UnityEngine.Vector2(g.x, g.y) + fr * g.depth / 2f, 2f);
var front3 = f.transform.TransformDirection(new UnityEngine.Vector3(fr.x, 0, fr.y));
msg = g.LineUp(frontPt, front3, tp, -n3);
robotFar = -999; foreach (var c in g.Corners()) robotFar = UnityEngine.Mathf.Max(robotFar, UnityEngine.Vector2.Dot(c, T));
sb.Append("front-face mate msg " + (msg ?? "ok") + " turn " + g.Turn.ToString("0.0") + " gap " + (toolNear - robotFar).ToString("0.000") + "\n");
g.Undo(); g.Undo();
sb.Append("after 2 undos turn " + g.Turn.ToString("0.0") + " slide " + g.Slide.ToString("0.00"));
return sb.ToString();
