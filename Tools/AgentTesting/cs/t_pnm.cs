var sb = new System.Text.StringBuilder();
foreach (var path in new[] { "NewRes.fbx", "25mmPiston.fbx", "25mm Stroke Pneumatic Cylinder.fbx", "50mmPiston.fbx", "75mmPiston.fbx", "25mmextended.fbx" }) {
  var full = "Assets/ProtobotRebuilt/Models/pnmatics/" + path;
  sb.Append("== " + path + "\n");
  foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(full)) {
    if (o is Mesh m) sb.Append("  mesh " + m.name + " subs " + m.subMeshCount + " verts " + m.vertexCount + " uv " + (m.uv != null && m.uv.Length > 0) + " bounds " + m.bounds.size.ToString("F3") + "\n");
    if (o is Material mat) sb.Append("  mat " + mat.name + " shader " + mat.shader.name + " tex " + (mat.mainTexture != null ? mat.mainTexture.name : "-") + " col " + mat.color + "\n");
    if (o is Texture t) sb.Append("  tex " + t.name + "\n");
    if (o is MeshRenderer r) sb.Append("  renderer " + r.name + " mats " + string.Join(",", System.Linq.Enumerable.Select(r.sharedMaterials, x => x == null ? "null" : x.name + (x.mainTexture != null ? "[" + x.mainTexture.name + "]" : ""))) + "\n");
  }
}
// where are the original's V5 cylinders used?
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ProtobotRebuilt/Resources/Part Prefabs/3Pneumatics" })) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
  var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
  foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) sb.Append(p.Substring(p.LastIndexOf('/') + 1) + " / " + mf.name + " mesh " + (mf.sharedMesh ? mf.sharedMesh.name + " @" + UnityEditor.AssetDatabase.GetAssetPath(mf.sharedMesh) : "none") + " mats " + string.Join(",", System.Linq.Enumerable.Select(mf.GetComponent<MeshRenderer>()?.sharedMaterials ?? new Material[0], x => x == null ? "null" : x.name + (x.mainTexture != null ? "[" + x.mainTexture.name + "]" : ""))) + "\n");
}
return sb.ToString();
