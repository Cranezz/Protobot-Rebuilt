// Play mode: the catalog tree under groups whose name matches PlayerPrefs "t_cat" (regex), with leaf p1s.
var sb = new System.Text.StringBuilder();
var re = new System.Text.RegularExpressions.Regex(UnityEngine.PlayerPrefs.GetString("t_cat", "Flex|Advanced|Gear"));
System.Action<Protobot.Interface.CatalogNode, int, bool> walk = null;
walk = (n, depth, show) => {
  var hit = show || re.IsMatch(n.name ?? "");
  if (hit) sb.AppendLine(new string(' ', depth * 2) + n.name + (n.IsGroup ? " [" + n.Count + "]" : " = " + n.typeId + " / " + n.p1));
  if (n.IsGroup && (hit || depth < 3)) foreach (var c in n.children) walk(c, depth + 1, hit);
};
foreach (var root in Protobot.Interface.PartsCatalog.Nodes) walk(root, 0, false);
return sb.ToString();
