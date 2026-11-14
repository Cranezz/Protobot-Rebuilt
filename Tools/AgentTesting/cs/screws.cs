var type = Protobot.PartsManager.GetPartType("SCRW"); var gen = type.GetComponent<Protobot.PartGenerator>();
var sb = new System.Text.StringBuilder(gen.GetType().Name + "\n");
foreach (var o in gen.GetParam1Options()) {
  var p = Protobot.PartsManager.GeneratePart("SCRW-" + o, Vector3.zero, Quaternion.identity);
  var mesh = p.GetComponent<MeshFilter>().sharedMesh;
  // The head's hex socket: among vertices near the head's top face, the angle (about the screw axis) of the farthest one.
  var b = mesh.bounds; float top = float.MinValue; foreach (var v in mesh.vertices) top = Mathf.Max(top, v.z);
  float best = 0f; float ang = 0f;
  foreach (var v in mesh.vertices) if (top - v.z < 0.01f) { var r = new Vector2(v.x, v.y).magnitude; if (r < 0.06f && r > best) { best = r; ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg; } }
  sb.Append(o + ": mesh " + mesh.name + " rot " + p.transform.rotation.eulerAngles.ToString("F0") + " childRot " + (p.transform.childCount > 0 ? p.transform.GetChild(0).localEulerAngles.ToString("F0") : "-") + " socket corner at " + ang.ToString("F0") + "° r " + best.ToString("F3") + " bounds " + b.size.ToString("F2") + "\n");
  UnityEngine.Object.DestroyImmediate(p);
}
return sb.ToString();
