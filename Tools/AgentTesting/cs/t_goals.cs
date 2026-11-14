var s = "";
foreach (var id in new[] { "OVGL", "OVSG", "OVTG", "OVAG", "OVPN", "OVCP", "OVLD", "OVTO" }) {
  var t = Protobot.PartsManager.GetPartType(id);
  foreach (var pd in t.GetComponentsInChildren<Protobot.PartData>(true)) {
    var mf = pd.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
    s += id + " " + pd.name + ": tris " + mf.sharedMesh.triangles.Length / 3 + " size " + mf.sharedMesh.bounds.size.ToString("F2") + " asset " + UnityEditor.AssetDatabase.GetAssetPath(mf.sharedMesh) + "\n";
  }
}
return s;
