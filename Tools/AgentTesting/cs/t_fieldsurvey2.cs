var sb = new System.Text.StringBuilder();
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Protobot/Field/Override/Override Field.prefab");
var root = prefab.transform.childCount == 1 ? prefab.transform.GetChild(0) : prefab.transform;
var groups = new System.Collections.Generic.Dictionary<string, (int n, long tris, Vector3 at, string mats)>();
foreach (Transform c in root) {
  var key = System.Text.RegularExpressions.Regex.Replace(c.name, @"\.\d+$", "");
  if (key.StartsWith("276-9250-80x")) continue;
  long tris = 0; var mats = new System.Collections.Generic.HashSet<string>();
  foreach (var mf in c.GetComponentsInChildren<MeshFilter>(true)) { tris += mf.sharedMesh.triangles.Length / 3; foreach (var m in mf.GetComponent<MeshRenderer>().sharedMaterials) mats.Add(m.name); }
  if (groups.TryGetValue(key, out var g)) groups[key] = (g.n + 1, g.tris, g.at, g.mats);
  else groups[key] = (1, tris, c.position, string.Join(",", mats));
}
foreach (var kv in groups) sb.Append(kv.Key + " x" + kv.Value.n + " tris " + kv.Value.tris + " at " + kv.Value.at.ToString("F1") + " mats " + kv.Value.mats + "\n");
return "root " + root.name + " children " + root.childCount + "\n" + sb;
