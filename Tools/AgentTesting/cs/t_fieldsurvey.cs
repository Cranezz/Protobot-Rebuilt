var sb = new System.Text.StringBuilder();
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Protobot/Field/Override/Override Field.prefab");
int count = 0;
var seen = new System.Collections.Generic.Dictionary<string, int>();
foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true)) {
  count++;
  var path = mf.name; var t = mf.transform.parent; int depth = 0;
  while (t != null && t != prefab.transform && depth < 3) { path = t.name + "/" + path; t = t.parent; depth++; }
  var key = System.Text.RegularExpressions.Regex.Replace(path, @"\d+$", "");
  var low = path.ToLowerInvariant();
  if (!(low.Contains("goal") || low.Contains("load") || low.Contains("toggle") || low.Contains("9250") || low.Contains("pin") || low.Contains("cup"))) continue;
  if (seen.ContainsKey(key)) { seen[key]++; continue; }
  seen[key] = 1;
  sb.Append(path + " tris " + (mf.sharedMesh.triangles.Length / 3) + " mats " + string.Join(",", System.Linq.Enumerable.Select(mf.GetComponent<MeshRenderer>().sharedMaterials, m => m.name)) + " at " + mf.transform.position.ToString("F1") + "\n");
  if (sb.Length > 12000) break;
}
return "meshes " + count + "\n" + sb;
