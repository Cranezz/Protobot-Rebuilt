// Play mode: a screw's thread lead: along one line on its surface (vertices within 4 degrees of one direction round
// the axis), the radius' crests along the axis; also the sense (right or left hand) from the crest angle's drift.
var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "VXADVANCEDGEARS-Lead Screw Segment", "MCMS-Screw Kit-Worm Gear", "VXADVANCEDGEARS-Worm Gear", "MCMS-Worm and Wheel-Worm" }) {
  var p = Protobot.PartsManager.GeneratePart(id, new UnityEngine.Vector3(0f, -100f, 0f), UnityEngine.Quaternion.identity);
  var mesh = p.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
  Protobot.HoleCollider mid = null; foreach (var h in p.GetComponentsInChildren<Protobot.HoleCollider>()) mid = h;
  var c = mid.transform.localPosition; var ax = (mid.transform.localRotation * UnityEngine.Vector3.forward).normalized;
  var u = UnityEngine.Vector3.Cross(ax, UnityEngine.Mathf.Abs(ax.y) < 0.9f ? UnityEngine.Vector3.up : UnityEngine.Vector3.right).normalized; var w = UnityEngine.Vector3.Cross(ax, u);
  foreach (var at in new[] { 0f, 90f }) {
    var crest = new System.Collections.Generic.List<float>();
    var pts = new System.Collections.Generic.List<UnityEngine.Vector2>();
    foreach (var v in mesh.vertices) { var d = v - c; var a = UnityEngine.Vector3.Dot(d, ax); var f = d - ax * a; var ang = UnityEngine.Mathf.Atan2(UnityEngine.Vector3.Dot(f, w), UnityEngine.Vector3.Dot(f, u)) * UnityEngine.Mathf.Rad2Deg; if (UnityEngine.Mathf.Abs(UnityEngine.Mathf.DeltaAngle(ang, at)) < 4f) pts.Add(new UnityEngine.Vector2(a, f.magnitude)); }
    var rMax = 0f; foreach (var q in pts) rMax = UnityEngine.Mathf.Max(rMax, q.y);
    pts.Sort((x, y) => x.x.CompareTo(y.x));
    var inCrest = false; float sum = 0f; int n = 0;
    foreach (var q in pts) { var top = q.y > rMax - 0.01f; if (top) { sum += q.x; n++; inCrest = true; } else if (inCrest) { crest.Add(sum / n); sum = 0; n = 0; inCrest = false; } }
    if (inCrest) crest.Add(sum / n);
    var gaps = new System.Text.StringBuilder(); for (var i = 1; i < crest.Count; i++) gaps.Append((crest[i] - crest[i - 1]).ToString("0.000") + " ");
    sb.AppendLine(id + " @" + at + ": rMax " + rMax.ToString("0.000") + " crests " + string.Join(" ", crest.ConvertAll(x => x.ToString("0.000"))) + " | gaps " + gaps);
  }
  UnityEngine.Object.Destroy(p);
}
return sb.ToString();
