var part = Protobot.PartsManager.GeneratePart("GSET-Pack-Pivot", new Vector3(500f, 500f, 500f), Quaternion.identity);
var sb = new System.Text.StringBuilder();
foreach (var h in part.GetComponentsInChildren<Protobot.HoleCollider>()) {
  var o = h.holeData != null ? h.holeData.outline : null; if (o == null) continue;
  sb.Append("outline pts " + o.Length + " centre " + part.transform.InverseTransformPoint(h.transform.position).ToString("F3") + " right " + part.transform.InverseTransformDirection(h.transform.right).ToString("F1") + " up " + part.transform.InverseTransformDirection(h.transform.up).ToString("F1") + "\n");
  var c = part.transform.InverseTransformPoint(h.transform.position);
  var right = part.transform.InverseTransformDirection(h.transform.right); var up = part.transform.InverseTransformDirection(h.transform.up);
  System.Func<Vector2, bool> inside = p => { bool r = false; for (int i = 0, j = o.Length - 1; i < o.Length; j = i++) if ((o[i].y > p.y) != (o[j].y > p.y) && p.x < (o[j].x - o[i].x) * (p.y - o[i].y) / (o[j].y - o[i].y) + o[i].x) r = !r; return r; };
  foreach (var rad in new[] { 0.85f, 0.93f, 1.0f, 1.07f, 1.15f }) {
    sb.Append(" r " + rad + ": ");
    for (int a = 90; a <= 180; a += 15) { var pt = new Vector3(0.314f, -0.314f, 0f) + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad), 0f) * rad; var d = pt - c; sb.Append(inside(new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up))) ? "#" : "."); }
    sb.Append("\n");
  }
}
UnityEngine.Object.DestroyImmediate(part);
return sb.ToString();
