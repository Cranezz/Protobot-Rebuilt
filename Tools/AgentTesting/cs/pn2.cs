var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "VXPNEUMATICS-Fitting for Cylinders", "VXPNEUMATICS-Fitting for Reservoir", "VXPNEUMATICS-Fitting for Valves" }) {
  var part = Protobot.PartsManager.GeneratePart(id, new Vector3(500f, 500f, 500f), Quaternion.identity);
  var mesh = part.GetComponent<MeshFilter>().sharedMesh; var b = mesh.bounds;
  var probe = new GameObject("p"); var mc = probe.AddComponent<MeshCollider>(); mc.sharedMesh = mesh;
  sb.Append(id + "\n");
  // outer radius by rays from outside toward the axis, every 0.02" along z
  for (float z = b.min.z + 0.01f; z < b.max.z; z += 0.02f) {
    float outer = 0f; float inner = 0f;
    for (int a = 0; a < 8; a++) { var d = new Vector3(Mathf.Cos(a * 0.785f), Mathf.Sin(a * 0.785f), 0f); var o = new Vector3(0, 0, z) + d * 1f;
      if (mc.Raycast(new Ray(o, -d), out var hit, 1f)) outer = Mathf.Max(outer, 1f - hit.distance); }
    // bore: from the axis outward (backfaces)
    Physics.queriesHitBackfaces = true; var c = new Vector3(0, 0, z); var bore = mc.Raycast(new Ray(c, Vector3.right), out var bh, 1f) ? bh.distance : -1f; Physics.queriesHitBackfaces = false;
    sb.Append("  z " + z.ToString("F2") + " r " + outer.ToString("F3") + " bore " + bore.ToString("F3") + "\n");
  }
  UnityEngine.Object.DestroyImmediate(probe); UnityEngine.Object.DestroyImmediate(part);
}
return sb.ToString();
