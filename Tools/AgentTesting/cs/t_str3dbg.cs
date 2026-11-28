var sb = new System.Text.StringBuilder();
var inst = Protobot.Motion.Sandbox.Instance;
var f = typeof(Protobot.Motion.Sandbox).GetField("strings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var list = (System.Collections.IList)f.GetValue(inst);
foreach (var st in list) {
  var t = st.GetType();
  var line = (Protobot.Motion.StringLine)t.GetField("line").GetValue(st);
  var frame = (Rigidbody)t.GetField("frame").GetValue(st);
  var parts = (System.Collections.Generic.List<GameObject>)t.GetField("parts").GetValue(st);
  var centers = (System.Collections.Generic.List<Vector3>)t.GetField("buildCenters").GetValue(st);
  var world = t.GetField("world").GetValue(st) as System.Collections.IList;
  sb.AppendLine("frame " + (frame ? frame.GetInstanceID() + " at " + frame.position.ToString("F2") : "world"));
  for (var i = 0; i < parts.Count; i++) sb.AppendLine(" node " + i + " part " + (parts[i] ? parts[i].name + " body " + Protobot.Motion.Sandbox.BodyOf(parts[i]).GetInstanceID() : "-") + " build " + centers[i].ToString("F2") + " sim " + world[i] + " node.post.part " + (line.nodes[i].post.part ? line.nodes[i].post.part.name : "-") + " Center " + line.nodes[i].post.Center.ToString("F2"));
}
return sb.ToString();
