UnityEditor.AssetDatabase.Refresh();
System.Type.GetType("Protobot.EditorTools.FieldImporter, Assembly-CSharp-Editor").GetMethod("Import").Invoke(null, null);
var sb = new System.Text.StringBuilder();
foreach (var path in new[] { "Assets/Protobot/Field/Override/Override Field.prefab", "Assets/Protobot/Field/Override/Override Field (light).prefab" }) {
  var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
  var mats = new System.Collections.Generic.HashSet<string>();
  long tris = 0;
  foreach (var r in go.GetComponentsInChildren<UnityEngine.MeshRenderer>()) { foreach (var m in r.sharedMaterials) mats.Add(m == null ? "null" : m.name); var mf = r.GetComponent<UnityEngine.MeshFilter>(); if (mf.sharedMesh != null) for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3; }
  sb.Append(path + ": " + tris + " tris, materials " + string.Join(",", mats) + "\n");
}
return sb.ToString();
