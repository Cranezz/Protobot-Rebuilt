// Rays along -z (from +z) at x = Temp/profile.txt values, y from 0.7 to -0.7: where they hit.
var g = Protobot.PartsManager.GetPartType("BRAN").GetComponent<Protobot.PartGenerator>();
var o = g.Generate(new Vector3(0f, 200f, 0f), Quaternion.identity);
var probe = new GameObject("p"); probe.transform.position = o.transform.position; probe.layer = 2;
var mc = probe.AddComponent<MeshCollider>(); mc.sharedMesh = o.GetComponent<MeshFilter>().sharedMesh;
Physics.SyncTransforms();
var sb = new System.Text.StringBuilder();
foreach (var xs in System.IO.File.ReadAllText("Temp/profile.txt").Trim().Split(' ')) {
  var x = float.Parse(xs); sb.Append("x " + x + ": ");
  for (float y = 0.7f; y > -0.71f; y -= 0.05f) {
    RaycastHit h; var from = o.transform.position + new Vector3(x, y, 5f);
    sb.Append(y.ToString("F2") + "=" + (mc.Raycast(new Ray(from, Vector3.back), out h, 20f) ? (h.point.z - o.transform.position.z).ToString("F2") : "-") + " ");
  }
  sb.Append(System.Environment.NewLine);
}
UnityEngine.Object.Destroy(probe); UnityEngine.Object.Destroy(o);
return sb.ToString();
