// Play mode: hole audit. For every part of the VEX family at PlayerPrefs "t_audit_i" (index into the VEX families),
// the openings through its model (VexHoleFixes.Openings, by rays) of screw/shaft size with no hole on them -> appended
// to Temp/audit.txt. Returns the family done and how many left.
var families = new System.Collections.Generic.List<Protobot.PartType>();
foreach (var t in UnityEngine.Resources.LoadAll<Protobot.PartType>("Part Prefabs/VEX")) families.Add(t);
families.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
var i = UnityEngine.PlayerPrefs.GetInt("t_audit_i", 0);
if (i >= families.Count) return "done";
var fam = families[i];
UnityEngine.PlayerPrefs.SetInt("t_audit_i", i + 1);
var openings = typeof(Protobot.Motion.VexHoleFixes).GetMethod("Openings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var sb = new System.Text.StringBuilder();
var gen = fam.GetComponent<Protobot.PartGenerator>();
foreach (var opt in gen.GetParam1Options()) {
  var id = fam.id + "-" + opt;
  UnityEngine.GameObject p = null;
  try { p = Protobot.PartsManager.GeneratePart(id, new UnityEngine.Vector3(0f, -200f, 0f), UnityEngine.Quaternion.identity); } catch (System.Exception) { continue; }
  if (p == null) continue;
  var mf = p.GetComponent<UnityEngine.MeshFilter>();
  if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || mf.sharedMesh.vertexCount > 200000) { UnityEngine.Object.Destroy(p); continue; }
  var probe = new UnityEngine.GameObject("audit probe"); var col = probe.AddComponent<UnityEngine.MeshCollider>(); col.sharedMesh = mf.sharedMesh;
  System.Collections.IList list = null;
  try { list = (System.Collections.IList)openings.Invoke(null, new object[] { mf.sharedMesh, col }); } catch (System.Exception e) { sb.AppendLine(id + ": error " + e.InnerException?.Message); }
  UnityEngine.Object.DestroyImmediate(probe);
  var holes = p.GetComponentsInChildren<Protobot.HoleCollider>(true);
  var missing = 0; var detail = new System.Text.StringBuilder();
  if (list != null) foreach (var o in list) {
    var ot = o.GetType();
    var size = (float)ot.GetField("size").GetValue(o); var size2 = (float)ot.GetField("size2").GetValue(o);
    var c = (UnityEngine.Vector3)ot.GetField("centre").GetValue(o); var n = (UnityEngine.Vector3)ot.GetField("normal").GetValue(o);
    var small = UnityEngine.Mathf.Min(size, size2 > 0 ? size2 : size);
    if (small < 0.12f || small > 0.5f) continue;
    var covered = false;
    foreach (var h in holes) { var hl = p.transform.InverseTransformPoint(h.transform.position); var off = hl - c; var lat = (off - n * UnityEngine.Vector3.Dot(off, n)).magnitude; if (lat < 0.08f && UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot((p.transform.InverseTransformDirection(h.transform.forward)), n)) > 0.9f) { covered = true; break; } }
    if (!covered) { missing++; if (missing <= 4) detail.Append(" [" + size.ToString("0.00") + (size2 > 0 ? "x" + size2.ToString("0.00") : "") + " at " + c.ToString("0.00") + "]"); }
  }
  if (missing > 0) sb.AppendLine(id + ": holes " + holes.Length + ", openings without a hole " + missing + detail);
  UnityEngine.Object.Destroy(p);
}
System.IO.File.AppendAllText("Temp/audit.txt", "## " + fam.id + "\n" + sb);
return fam.id + " (" + (i + 1) + "/" + families.Count + ")";
