// Play mode: (1) spacers longer than wide snap as round parts (AxleOf), (2) Ctrl+wheel families for a few parts.
var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "VXSPACERS-1/2\" Nylon Spacer (5/16\" OD)", "VXSPACERS-7/8\" Nylon Spacer (5/16\" OD)", "VXSPACERS-7/8\" Nylon Spacer (3/8\" OD)", "VXSPACERS-1.00\" Nylon Spacer (3/8\" OD)", "VXSPACERS-1/4\" Nylon Spacer (3/8\" OD)" }) {
  var p = Protobot.PartsManager.GeneratePart(id, new UnityEngine.Vector3(0f, -50f, 0f), UnityEngine.Quaternion.identity);
  if (p == null) { sb.AppendLine(id + ": not made"); continue; }
  Protobot.AxlePlaceDisplacement.Axle axle;
  var ok = Protobot.AxlePlaceDisplacement.AxleOf(p, out axle);
  sb.AppendLine(id + ": round " + ok + " axis " + axle.axis.ToString("0.0") + " length " + (axle.high - axle.low).ToString("0.000"));
  UnityEngine.Object.Destroy(p);
}
var fam = typeof(Protobot.Interface.PartsBrowser).GetMethod("FamilyOf", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
foreach (var name in new[] { "2\" Flex Wheel, 40A", "1/4\" Nylon Spacer (5/16\" OD)", "36T Bevel Gear", "Normal" }) {
  Protobot.Interface.CatalogNode leaf = null;
  foreach (var root in Protobot.Interface.PartsCatalog.Nodes) foreach (var l in root.Leaves()) if (leaf == null && l.p1 != null && l.p1.StartsWith(name)) leaf = l;
  if (leaf == null) { sb.AppendLine(name + ": no leaf"); continue; }
  var list = (System.Collections.Generic.List<Protobot.Interface.CatalogNode>)fam.Invoke(null, new object[] { leaf });
  sb.AppendLine(name + " -> " + list.Count + ": " + string.Join(", ", System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(list, n => n.name), 12)));
}
return sb.ToString();
