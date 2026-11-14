var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "OVPN", "OVCP", "OVGL", "OVLD", "OVTO", "OVFL" }) {
  var type = Protobot.PartsManager.GetPartType(id);
  if (type == null) { sb.Append(id + " missing\n"); continue; }
  var gen = type.GetComponent<Protobot.PartGenerator>();
  sb.Append(id + " " + type.name + " gen " + gen.GetType().Name + "\n");
  foreach (var d in type.GetComponentsInChildren<Protobot.PartData>(true)) {
    var mf = d.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
    long tris = 0; for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += mf.sharedMesh.GetIndexCount(i) / 3;
    sb.Append("   " + d.param1Value + " mesh " + mf.sharedMesh.name + " tris " + tris + " size " + Vector3.Scale(mf.sharedMesh.bounds.size, d.transform.lossyScale).ToString("F2") + " scale " + d.transform.lossyScale.ToString("F3") + " mats " + string.Join(",", System.Linq.Enumerable.Select(d.GetComponent<MeshRenderer>().sharedMaterials, m => m != null ? m.name : "null")) + " holes " + d.GetComponentsInChildren<Protobot.HoleCollider>(true).Length + "\n");
  }
}
return sb.ToString();
