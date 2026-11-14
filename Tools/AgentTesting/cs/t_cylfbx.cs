var sb = new System.Text.StringBuilder();
foreach (var path in UnityEditor.AssetDatabase.FindAssets("Piston t:Model")) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(path);
  var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
  foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) {
    if (!mr.name.EndsWith("LOD0")) continue;
    var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
    sb.Append(p.Replace("Assets/Protobot/Parts/Models/", "") + " " + mr.name + " sub " + mesh.subMeshCount + ": ");
    for (int i = 0; i < mesh.subMeshCount; i++) {
      var m = i < mr.sharedMaterials.Length ? mr.sharedMaterials[i] : null;
      var b = mesh.GetSubMesh(i).bounds;
      sb.Append("[" + (m != null ? m.name + " " + ColorUtility.ToHtmlStringRGB(m.color) : "null") + " tris " + mesh.GetSubMesh(i).indexCount / 3 + " c " + (b.center * 39.37f).ToString("F2") + " s " + (b.size * 39.37f).ToString("F2") + "] ");
    }
    sb.Append("\n");
  }
}
foreach (var path in UnityEditor.AssetDatabase.FindAssets("Reservoir t:Model")) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(path);
  var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
  foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) if (mr.name.EndsWith("LOD0")) sb.Append(p + " " + string.Join(",", System.Linq.Enumerable.Select(mr.sharedMaterials, m => m.name + " " + ColorUtility.ToHtmlStringRGB(m.color))) + "\n");
}
return sb.ToString();
