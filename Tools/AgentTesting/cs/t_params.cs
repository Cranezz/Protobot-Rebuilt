UnityEditor.AssetDatabase.ImportAsset("Assets/Protobot/Cloud", UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ImportRecursive);
var sb = new System.Text.StringBuilder();
foreach (var path in new[] { "Assets/Protobot/Cloud/SubmitSkillsRun.js", "Assets/Protobot/Cloud/RecordMatch.js" }) {
  var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
  var m = o.GetType().GetProperty("Model").GetValue(o);
  var ps = (System.Collections.IList)m.GetType().GetProperty("Parameters").GetValue(m);
  sb.Append(path + ": " + ps.Count + " params");
  foreach (var p in ps) sb.Append(" " + p.GetType().GetField("Name").GetValue(p));
  sb.AppendLine();
}
return sb.ToString();
