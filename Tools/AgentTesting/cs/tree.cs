var sb = new System.Text.StringBuilder();
foreach (var n in Protobot.Interface.PartsCatalog.Nodes) {
  if (n.section == "Custom") continue;
  sb.Append(n.section + " | " + n.name + " (" + n.Count + ")");
  if (n.IsGroup) { sb.Append(": "); foreach (var c in n.children) sb.Append(c.IsGroup ? "[" + c.name + " " + c.Count + "] " : c.name + "; "); }
  sb.Append("\n");
}
System.IO.File.WriteAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/catalog_tree.txt", sb.ToString());
return "ok";
