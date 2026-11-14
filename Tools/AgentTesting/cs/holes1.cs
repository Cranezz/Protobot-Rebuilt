// For each part type: generate it, cast rays just beside each hole (on the metal) along the hole axis and compare
// the part's surfaces with the hole collider's two faces.
var sb = new System.Text.StringBuilder();
var only = "CCHL,UCHL,ANGL,RAIL";
var probe = new GameObject("Hole Probe"); var mc = probe.AddComponent<MeshCollider>();
probe.layer = 2;
foreach (var type in Protobot.PartsManager.partTypes) {
  if (type == null) continue;
  if (only != "ALL" && !("," + only + ",").Contains("," + type.id + ",")) continue;
  var gen = type.GetComponent<Protobot.PartGenerator>(); if (gen == null) continue;
  string id; try { id = gen.GetId(); } catch { continue; }
  GameObject part = null; try { part = Protobot.PartsManager.GeneratePart(id, new Vector3(500f, 500f, 500f), Quaternion.identity); } catch { }
  if (part == null) continue;
  var mf = part.GetComponent<MeshFilter>();
  if (mf == null || mf.sharedMesh == null) { UnityEngine.Object.DestroyImmediate(part); continue; }
  probe.transform.SetPositionAndRotation(part.transform.position, part.transform.rotation); probe.transform.localScale = part.transform.lossyScale;
  mc.sharedMesh = null; mc.sharedMesh = mf.sharedMesh;
  float worst = 0f, sum = 0f; int n = 0, off = 0; string example = "";
  foreach (var h in part.GetComponentsInChildren<Protobot.HoleCollider>(true)) {
    var t = h.transform; var f = t.forward; var depth = t.lossyScale.z;
    var side = t.up * (Mathf.Max(t.lossyScale.x, t.lossyScale.y) / 2f + 0.035f);
    foreach (var s in new[] { 1f, -1f }) {
      var face = t.position + f * s * depth / 2f;
      var from = face + f * s * 0.6f + side; RaycastHit hit;
      if (!mc.Raycast(new Ray(from, -f * s), out hit, 1.2f)) continue;
      var err = Vector3.Dot(hit.point - (face + side), f * s);  // + : the surface is outside the collider face
      n++;
      sum += err; if (Mathf.Abs(err) > 0.002f) { off++; if (Mathf.Abs(err) > Mathf.Abs(worst)) { worst = err; example = t.name; } }
    }
  }
  if (off > 0 || only != "ALL") sb.Append(type.id + " (" + id + "): " + n + " faces, " + off + " off >0.002, mean " + (n > 0 ? sum / n : 0f).ToString("F4") + " worst " + worst.ToString("F4") + " " + example + "\n");
  UnityEngine.Object.DestroyImmediate(part);
}
UnityEngine.Object.DestroyImmediate(probe);
return sb.Length == 0 ? "all flush" : sb.ToString();
