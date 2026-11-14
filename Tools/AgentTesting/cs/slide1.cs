var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "VXSLIDEASSEMBLYP-Outer Slide Member", "VXSLIDEASSEMBLYP-Inner Slide Member" }) {
  var p = Protobot.PartsManager.GeneratePart(id, new Vector3(500,500,500), Quaternion.identity); var m = p.GetComponent<MeshFilter>().sharedMesh;
  var g = new GameObject("probe"); var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = m;
  float x = 3f; var b = m.bounds; float yc = b.center.y;
  sb.Append(id + "\n");
  // z profile: for y across, first hit from +z and from -z
  for (float y = b.min.y - 0.02f; y <= b.max.y + 0.02f; y += 0.05f) {
    var top = mc.Raycast(new Ray(new Vector3(x, y, 2f), Vector3.back), out var h1, 5f) ? h1.point.z : 99f;
    var bot = mc.Raycast(new Ray(new Vector3(x, y, -2f), Vector3.forward), out var h2, 5f) ? h2.point.z : 99f;
    sb.Append("  y " + y.ToString("F2") + " from+z " + top.ToString("F3") + " from-z " + bot.ToString("F3") + "\n");
  }
  UnityEngine.Object.DestroyImmediate(g); UnityEngine.Object.DestroyImmediate(p);
}
return sb.ToString();
