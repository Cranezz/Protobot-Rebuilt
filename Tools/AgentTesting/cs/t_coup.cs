var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.Screw>(FindObjectsSortMode.None)) {
  var mf = s.GetComponent<MeshFilter>(); if (mf == null) continue;
  var b = mf.sharedMesh.bounds;
  var so = s.GetComponent<Protobot.SavedObject>();
  sb.Append((so != null ? so.id : s.name) + " pos " + s.transform.position.ToString("F3") + " fwd " + s.transform.forward.ToString("F2") + " up " + s.transform.up.ToString("F2") + " bounds c " + b.center.ToString("F3") + " size " + b.size.ToString("F3") + " len " + s.ScrewLength.ToString("F3") + " stud " + Protobot.Screw.Studs.Contains(mf.sharedMesh) + "\n");
}
return sb.ToString();
