// Game open with the robot: a jig (a 2x1x2x10 C-channel standing on its side) as the alignment tool, moved 6" along the
// wall from the robot, then Line up: the robot's side facing it slides against it. Then a saved group (two of the
// robot's parts) loads as the tool; the test group file is deleted after.
var g = Protobot.Game.SkillsGame.Current;
if (g == null) return "no game";
var sb = new System.Text.StringBuilder();
string channel = null;
foreach (var n in Protobot.Interface.PartsCatalog.Nodes)
    foreach (var l in n.Leaves())
        if (channel == null && l.typeId != null && l.typeId.StartsWith("CCHL")) channel = l.typeId + (l.p1 != null ? "-" + l.p1 : "") + (l.p2 != null ? "-" + l.p2 : "");
var jig = new Protobot.Builds.ObjectData { partId = channel, rColor = 0.7f, gColor = 0.7f, bColor = 0.7f };
g.LoadToolParts(new[] { jig }, "Jig test");
sb.AppendLine("jig " + channel + ": tool " + (g.tool != null) + " at " + g.toolX.ToString("F2") + ", " + g.toolY.ToString("F2") + " heading " + g.toolHeading.ToString("F0") + ", hits " + g.HitsTool());
// 6" farther along the wall.
var t = g.WallTangent;
var away = Vector2.Dot(new Vector2(g.toolX - g.x, g.toolY - g.y), t) >= 0f ? 1f : -1f;
g.SetTool(g.toolX + t.x * 6f * away, g.toolY + t.y * 6f * away, g.toolHeading);
var slideBefore = g.Slide;
// The robot's side facing the tool and the tool's side facing the robot, at mid height.
var fieldT = g.field.transform;
var dirWorld = fieldT.TransformDirection(new Vector3(t.x * away, 0f, t.y * away));
var robotPoint = g.field.ToWorld(new Vector2(g.x, g.y), 3f);
var ra = Physics.RaycastAll(robotPoint + dirWorld * 40f, -dirWorld, 40f);
var toolPoint = g.field.ToWorld(new Vector2(g.toolX, g.toolY), 1f);
// Faces: the robot's from its footprint corners, the tool's from its corners (exact for boxes).
var fl = typeof(Protobot.Game.SkillsGame).GetMethod("Corners", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var rc = (Vector2[])fl.Invoke(g, null);
var tc = g.ToolCorners();
float rMax = float.MinValue, tMin = float.MaxValue;
var t2 = t * away;
foreach (var c in rc) rMax = Mathf.Max(rMax, Vector2.Dot(c, t2));
foreach (var c in tc) tMin = Mathf.Min(tMin, Vector2.Dot(c, t2));
sb.AppendLine("gap before " + (tMin - rMax).ToString("F3") + "\"");
var rp = g.field.ToWorld(new Vector2(g.x, g.y) + t2 * (rMax - Vector2.Dot(new Vector2(g.x, g.y), t2)), 2f);
var tp = g.field.ToWorld(new Vector2(g.toolX, g.toolY) + t2 * (tMin - Vector2.Dot(new Vector2(g.toolX, g.toolY), t2)), 0.5f);
var msg = g.LineUp(rp, dirWorld, tp, -dirWorld);
rc = (Vector2[])fl.Invoke(g, null);
rMax = float.MinValue;
foreach (var c in rc) rMax = Mathf.Max(rMax, Vector2.Dot(c, t2));
sb.AppendLine("line up: " + (msg ?? "done") + "; slid " + (g.Slide - slideBefore).ToString("F3") + "\", gap after " + (tMin - rMax).ToString("F3") + "\", hits " + g.HitsTool());
// A saved group as the tool.
var parts = new System.Collections.Generic.List<GameObject>();
foreach (var p in Protobot.PartsManager.FindLoadedObjects()) if (parts.Count < 2 && p.activeInHierarchy) parts.Add(p);
Protobot.Builds.SavedGroups.Save("zz agent jig test", parts[0], parts);
g.LoadToolGroup("zz agent jig test");
sb.AppendLine("group tool: " + (g.tool != null ? g.toolName + ", " + g.tool.GetComponentsInChildren<Renderer>().Length + " meshes" : "none"));
Protobot.Interface.PartsCatalog.Invalidate();
return sb.ToString();
