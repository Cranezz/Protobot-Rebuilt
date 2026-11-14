var fieldObj = UnityEngine.GameObject.Find("Game Field");
var robot = new System.Collections.Generic.Dictionary<string, long[]>();
var field = new System.Collections.Generic.Dictionary<string, long[]>();
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsSortMode.None)) {
  if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
  var f = r.GetComponent<UnityEngine.MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
  long tris = 0; var m = f.sharedMesh; for (int s = 0; s < m.subMeshCount; s++) tris += m.GetIndexCount(s) / 3;
  bool isField = fieldObj != null && r.transform.IsChildOf(fieldObj.transform);
  string key;
  if (isField) { var n = r.name; int cut = n.IndexOfAny(new[]{'_',' ','.'}); key = cut > 0 ? n.Substring(0, cut) : n; }
  else { var so = r.GetComponentInParent<Protobot.SavedObject>(); if (so == null) continue; key = so.id; }
  var d = isField ? field : robot; long[] e; if (!d.TryGetValue(key, out e)) { e = new long[3]; d[key] = e; } e[0] += tris; e[1]++; e[2] = tris;
}
var sb = new System.Text.StringBuilder("ROBOT (part: drawn tris, copies, tris each)\n");
foreach (var kv in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(robot, k => k.Value[0]), 12)) sb.Append("  " + kv.Key + ": " + kv.Value[0] + " x" + kv.Value[1] + " (" + kv.Value[2] + ")\n");
long ft = 0; foreach (var kv in field) ft += kv.Value[0];
sb.Append("FIELD total " + ft + "\n");
foreach (var kv in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(field, k => k.Value[0]), 25)) sb.Append("  " + kv.Key + ": " + kv.Value[0] + " (" + (100.0 * kv.Value[0] / ft).ToString("0.0") + "%) x" + kv.Value[1] + " (" + kv.Value[2] + " each)\n");
return sb.ToString();
