var g = Protobot.PartsManager.GetPartType("BRAN").GetComponent<Protobot.PartGenerator>();
var o = g.Generate(new Vector3(0f, 50f, 0f), Quaternion.identity);
var mesh = o.GetComponent<MeshFilter>().sharedMesh;
var v = mesh.vertices; var tris = new System.Collections.Generic.List<int>(mesh.triangles);
var sb = new System.Text.StringBuilder();
var nf = System.IO.File.ReadAllText("Temp/normal.txt").Trim().Split(' ');
var normals = new[] { new Vector3(float.Parse(nf[0]), float.Parse(nf[1]), float.Parse(nf[2])).normalized };
foreach (var n in normals) {
  var turn = Quaternion.FromToRotation(n, Vector3.forward);
  var back = Quaternion.Inverse(turn);
  var verts = new System.Collections.Generic.List<Vector3>();
  foreach (var p in v) verts.Add(turn * p * 0.0254f);
  var found = Protobot.Geometry.HoleScanner.Scan(verts, tris);
  sb.Append("normal " + n.ToString("F2") + ":\n");
  foreach (var h in found) {
    var s = h.size / 0.0254f; var s2 = h.size2 / 0.0254f;
    if (s < 0.08f || Vector3.Dot(back * h.axis, n) > -0.9f) continue;
    sb.Append("   " + (h.through ? "through" : "blind") + (h.square ? " sq " : " rd ") + s.ToString("F3") + (s2 > 0 ? " x " + s2.ToString("F3") : "") + " d " + (h.depth / 0.0254f).ToString("F2") + " at " + (back * h.entry / 0.0254f).ToString("F2") + " side " + (back * h.side).ToString("F2") + "\n");
  }
}
UnityEngine.Object.Destroy(o);
return sb.ToString();
