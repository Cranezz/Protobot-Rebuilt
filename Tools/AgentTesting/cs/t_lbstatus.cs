var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Protobot/Cloud/skills-override.lb");
var m = o.GetType().GetProperty("Model").GetValue(o);
var sb = new System.Text.StringBuilder();
foreach (var p in m.GetType().GetProperties()) {
  object v = null; try { v = p.GetValue(m); } catch {}
  sb.Append(p.Name + "=" + v);
  if (v != null && p.Name == "Status") foreach (var q in v.GetType().GetProperties()) sb.Append(" [" + q.Name + "=" + q.GetValue(v) + "]");
  if (v != null && p.Name == "States") foreach (var s in (System.Collections.IEnumerable)v) { foreach (var q in s.GetType().GetProperties()) sb.Append(" {" + q.Name + "=" + q.GetValue(s) + "}"); }
  sb.AppendLine();
}
return sb.ToString();
