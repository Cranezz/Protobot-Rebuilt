UnityEditor.AssetDatabase.ImportAsset("Assets/Protobot/Cloud", UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ImportRecursive);
var sb = new System.Text.StringBuilder();
foreach (var path in new[] { "Assets/Protobot/Cloud/GetStats.js", "Assets/Protobot/Cloud/skills-override.lb" }) {
  var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
  sb.Append(path + " -> " + (o != null ? o.GetType().FullName : "null"));
  if (o != null) {
    var model = o.GetType().GetProperty("Model");
    if (model != null) { var m = model.GetValue(o); sb.Append(" model " + (m != null ? m.GetType().Name : "null")); if (m != null) { var n = m.GetType().GetProperty("Name"); if (n != null) sb.Append(" name '" + n.GetValue(m) + "'"); } }
  }
  sb.AppendLine();
}
return sb.ToString();
