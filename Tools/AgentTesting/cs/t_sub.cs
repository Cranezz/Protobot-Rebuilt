var sb = new System.Text.StringBuilder();
var t = Protobot.PartsManager.GetPartType("VXPNEUMATICS");
foreach (var pd in t.GetComponentsInChildren<Protobot.PartData>(true)) {
  var n = pd.name; if (!(n.Contains("Straight Pneumatic") || n.Contains("Elbow") || n.Contains("Air Flow") || n.Contains("Tee") || n.Contains("Hand Valve") || n.Contains("Plug") || n.Contains("Solenoid   Double"))) continue;
  var mesh = pd.GetComponent<MeshFilter>().sharedMesh; var r = pd.GetComponent<MeshRenderer>();
  sb.Append(n + " bounds " + mesh.bounds.min.ToString("F3") + " " + mesh.bounds.max.ToString("F3") + "\n");
  var v = mesh.vertices;
  for (int s = 0; s < mesh.subMeshCount; s++) {
    var tri = mesh.GetTriangles(s); var b = new Bounds(v[tri[0]], Vector3.zero); foreach (var i in tri) b.Encapsulate(v[i]);
    sb.Append("  sub " + s + " " + r.sharedMaterials[s].name + " min " + b.min.ToString("F3") + " max " + b.max.ToString("F3") + " tris " + tri.Length / 3 + "\n");
  }
}
return sb.ToString();
