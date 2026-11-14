// Round parts: the middle hole's depth (what placing on a screw measures) against the part's real length along that
// axis (its mesh). Lists the ones that differ by more than 0.005".
var sb = new System.Text.StringBuilder();
int checkedCount = 0, bad = 0;
foreach (var type in Protobot.PartsManager.partTypes) {
  if (type == null) continue;
  if (!(type.id.StartsWith("VXSPACER") || type.id.StartsWith("SPCR") || type.id.StartsWith("VXWASHER") || type.id.StartsWith("WASH") || type.id.Contains("COLLAR") || type.id.StartsWith("COLR") || type.id.StartsWith("VXSHAFTCOLL"))) continue;
  var g = type.GetComponent<Protobot.PartGenerator>(); if (g == null) continue;
  System.Collections.Generic.List<string> o1 = null; try { o1 = g.GetParam1Options(); } catch { }
  if (o1 == null || o1.Count == 0) o1 = new System.Collections.Generic.List<string> { g.param1.value };
  foreach (var p1 in o1) {
    System.Collections.Generic.List<string> o2 = null;
    g.param1.value = p1;
    if (g.UsesTwoParams) { try { o2 = g.GetParam2Options(); } catch { } }
    if (o2 == null || o2.Count == 0) o2 = new System.Collections.Generic.List<string> { null };
    foreach (var p2 in o2) {
      var id = type.id + (g.UsesParams ? "-" + p1 : "") + (p2 != null ? "-" + p2 : "");
      GameObject part = null; try { part = Protobot.PartsManager.GeneratePart(id, new Vector3(400f, 400f, 400f), Quaternion.identity); } catch { }
      if (part == null) continue;
      var mesh = part.GetComponent<MeshFilter>().sharedMesh;
      Protobot.HoleCollider mid = null; var center = part.GetComponent<Renderer>().bounds.center;
      foreach (var h in part.GetComponentsInChildren<Protobot.HoleCollider>(true)) if (mid == null || (h.transform.position - center).sqrMagnitude < (mid.transform.position - center).sqrMagnitude) mid = h;
      if (mid == null || mesh == null) { UnityEngine.Object.DestroyImmediate(part); continue; }
      var axis = mid.transform.forward; float lo = 9f, hi = -9f;
      foreach (var v in mesh.vertices) { var w = part.transform.TransformPoint(v); var d = Vector3.Dot(w - mid.transform.position, axis); lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
      var depth = mid.transform.lossyScale.z; var hlo = -depth / 2f; var hhi = depth / 2f;
      checkedCount++;
      if (Mathf.Abs(lo - hlo) > 0.005f || Mathf.Abs(hi - hhi) > 0.005f) { bad++; if (bad <= 40) sb.Append(id + ": hole " + hlo.ToString("F3") + ".." + hhi.ToString("F3") + " mesh " + lo.ToString("F3") + ".." + hi.ToString("F3") + "\n"); }
      UnityEngine.Object.DestroyImmediate(part);
    }
  }
}
return checkedCount + " checked, " + bad + " differ\n" + sb.ToString();
