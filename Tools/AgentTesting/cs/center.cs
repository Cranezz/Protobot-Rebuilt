// For each part id: every few holes, a grid of rays along the hole's axis finds the real opening (rays that pass
// through the mesh); prints the opening's centre offset from the collider (in the collider's right/up) and its size.
var sb = new System.Text.StringBuilder();
var probe = new GameObject("Hole Probe"); var mc = probe.AddComponent<MeshCollider>(); probe.layer = 2;
foreach (var id in new[] { "CCHL-1x2-35", "CCHL-1x3-35", "CCHL-1x5-35", "UCHL-2x2-35", "ANGL-1x1-35", "ANGL-2x2-35" }) {
  GameObject part = null; try { part = Protobot.PartsManager.GeneratePart(id, new Vector3(500f, 500f, 500f), Quaternion.identity); } catch { }
  if (part == null) { sb.Append(id + ": no part\n"); continue; }
  var mf = part.GetComponent<MeshFilter>();
  probe.transform.SetPositionAndRotation(part.transform.position, part.transform.rotation); probe.transform.localScale = part.transform.lossyScale;
  mc.sharedMesh = null; mc.sharedMesh = mf.sharedMesh;
  var holes = part.GetComponentsInChildren<Protobot.HoleCollider>(true);
  Vector2 sum = Vector2.zero; float worst = 0f; int n = 0; string ex = ""; float sizeErr = 0f;
  for (var i = 0; i < holes.Length; i += 5) {
    var t = holes[i].transform; var f = t.forward; var r = t.right; var u = t.up;
    float half = 0.17f, step = 0.004f; Vector2 c = Vector2.zero; int cnt = 0; float minR = 9, maxR = -9, minU = 9, maxU = -9;
    for (var a = -half; a <= half; a += step)
      for (var b = -half; b <= half; b += step) {
        var o = t.position + r * a + u * b - f * 0.5f;
        RaycastHit hit;
        if (mc.Raycast(new Ray(o, f), out hit, 1.0f)) continue;
        c += new Vector2(a, b); cnt++;
        minR = Mathf.Min(minR, a); maxR = Mathf.Max(maxR, a); minU = Mathf.Min(minU, b); maxU = Mathf.Max(maxU, b);
      }
    if (cnt < 10) continue;
    c /= cnt; n++; sum += c;
    if (c.magnitude > worst) { worst = c.magnitude; ex = "hole " + i + " offset (" + c.x.ToString("F4") + "," + c.y.ToString("F4") + ") open " + (maxR - minR + step).ToString("F3") + "x" + (maxU - minU + step).ToString("F3") + " collider " + t.lossyScale.x.ToString("F3") + "x" + t.lossyScale.y.ToString("F3"); }
  }
  sb.Append(id + ": " + n + " holes, mean offset (" + (n > 0 ? (sum.x / n).ToString("F4") + "," + (sum.y / n).ToString("F4") : "-") + ") worst " + worst.ToString("F4") + " " + ex + "\n");
  UnityEngine.Object.DestroyImmediate(part);
}
UnityEngine.Object.DestroyImmediate(probe);
return sb.ToString();
