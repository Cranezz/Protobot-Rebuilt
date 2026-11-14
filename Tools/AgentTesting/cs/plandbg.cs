var sb = new System.Text.StringBuilder();
var t = typeof(Protobot.Motion.VexHoleFixes);
var cache = (System.Collections.IDictionary)t.GetField("cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
var fixType = t.GetNestedType("Fix", System.Reflection.BindingFlags.NonPublic);
foreach (var id in new[] { "VXPNEUMATICS-Cylinder Mount", "PNMT-50mm-Normal" }) {
  if (!cache.Contains(id)) { sb.Append(id + " not planned\n"); continue; }
  var fix = cache[id];
  var add = (System.Collections.IList)fixType.GetField("add").GetValue(fix); var rem = (System.Collections.IList)fixType.GetField("remove").GetValue(fix);
  sb.Append(id + " remove " + rem.Count + " add " + add.Count + "\n");
  foreach (Protobot.Custom.CustomHole h in add) sb.Append("  " + h.center.ToString("F3") + " size " + h.size + " sq " + h.square + "\n");
}
sb.Append("handles PNMT " + Protobot.Motion.VexHoleFixes.Handles("PNMT-50mm"));
return sb.ToString();
