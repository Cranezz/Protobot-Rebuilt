// Play mode, after t_goalmeasure.cs: empties FeaturePicker's edge cache, turns the Measure tool on, points at the goal
// and times the frames (the tool's first look at the goal finds its edges again).
var sb = new System.Text.StringBuilder();
var rail = Protobot.Interface.ToolRail.Instance;
var tools = (System.Collections.IEnumerable)rail.GetType().GetField("tools", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).GetValue(rail);
foreach (var t in tools) sb.Append(t.GetType().GetField("name").GetValue(t) + " ");
sb.AppendLine();
var playtest = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
var frames = playtest.GetMethod("Frames");
Protobot.Interface.ToolRail.Select("Measure");
frames.Invoke(null, new object[] { 3 });
var measure = UnityEngine.Object.FindAnyObjectByType<Protobot.Tools.MeasureTool>();
sb.AppendLine("tool " + Protobot.Interface.ToolRail.Current + ", MeasureTool active " + (measure != null && measure.isActiveAndEnabled));
var cache = (System.Collections.IDictionary)typeof(Protobot.Interface.FeaturePicker).GetField("edgeCache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
cache.Clear();
UnityEngine.GameObject goal = null;
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) if (so.name.StartsWith("Override Goal")) goal = so.gameObject;
var r = goal.GetComponent<UnityEngine.Renderer>();
playtest.GetMethod("PointAt", new[] { typeof(UnityEngine.Vector3) }).Invoke(null, new object[] { r.bounds.center });
var sw = System.Diagnostics.Stopwatch.StartNew();
frames.Invoke(null, new object[] { 1 });
sb.AppendLine("first frame " + sw.ElapsedMilliseconds + " ms, edge cache " + cache.Count);
sw.Restart();
frames.Invoke(null, new object[] { 10 });
sb.AppendLine("next 10 frames " + sw.ElapsedMilliseconds + " ms");
return sb.ToString();
