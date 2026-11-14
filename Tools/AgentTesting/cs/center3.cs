// Per hole of one part: local position, axis, and the real opening's centre offset (in the part's own axes).
var sb = new System.Text.StringBuilder();
var probe = new GameObject("Hole Probe"); var mc = probe.AddComponent<MeshCollider>(); probe.layer = 2;
var part = Protobot.PartsManager.GeneratePart("PARTID", new Vector3(500f, 500f, 500f), Quaternion.identity);
var mf = part.GetComponent<MeshFilter>();
probe.transform.SetPositionAndRotation(part.transform.position, part.transform.rotation); probe.transform.localScale = part.transform.lossyScale;
mc.sharedMesh = mf.sharedMesh;
var holes = part.GetComponentsInChildren<Protobot.HoleCollider>(true);
for (var i = 0; i < holes.Length && i < MAXN; i++) {
  var t = holes[i].transform; var f = t.forward; var r = t.right; var u = t.up;
  float half = 0.15f, step = 0.002f; Vector3 c = Vector3.zero; int cnt = 0;
  for (var a = -half; a <= half; a += step)
    for (var b = -half; b <= half; b += step) {
      var o = t.position + r * a + u * b - f * 0.5f;
      RaycastHit hit;
      if (mc.Raycast(new Ray(o, f), out hit, 1.0f)) continue;
      c += r * a + u * b; cnt++;
    }
  if (cnt == 0) { sb.Append(i + " no opening\n"); continue; }
  c /= cnt;
  var local = part.transform.InverseTransformDirection(c);
  sb.Append(i + " at " + part.transform.InverseTransformPoint(t.position).ToString("F3") + " axis " + part.transform.InverseTransformDirection(f).ToString("F1") + " opening off " + local.ToString("F4") + "\n");
}
UnityEngine.Object.DestroyImmediate(part); UnityEngine.Object.DestroyImmediate(probe);
return sb.ToString();
