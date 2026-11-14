var sb = new System.Text.StringBuilder();
foreach (var t in Protobot.PartsManager.partTypes) {
  if (t == null) continue;
  var g = t.GetComponent<Protobot.PartGenerator>(); if (g == null) continue;
  System.Collections.Generic.List<string> o1 = null; try { o1 = g.UsesParams ? g.GetParam1Options() : null; } catch { }
  sb.Append("== " + t.id + " (" + t.name + ")" + (o1 != null ? ": " + string.Join(" | ", o1) : "") + "\n");
  if (g.UsesTwoParams && o1 != null && o1.Count > 0 && !g.param2.custom) {
    var keep = g.param1.value;
    foreach (var p in o1) { g.param1.value = p; try { var o2 = g.GetParam2Options(); if (o2 != null && o2.Count > 0) sb.Append("   " + p + " -> " + string.Join(" | ", o2) + "\n"); } catch { } }
    g.param1.value = keep;
  }
}
System.IO.File.WriteAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/part_names.txt", sb.ToString());
return "lines " + sb.ToString().Split('\n').Length;
