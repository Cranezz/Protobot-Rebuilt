var g = Protobot.PartsManager.GetPartType("BRAN").GetComponent<Protobot.PartGenerator>();
var o = g.Generate(new Vector3(0f, 50f, 0f), Quaternion.identity);
var mesh = o.GetComponent<MeshFilter>().sharedMesh;
var v = mesh.vertices; var t = mesh.triangles;
var bins = new System.Collections.Generic.Dictionary<string, float>();
for (int i = 0; i < t.Length; i += 3) {
  var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
  var n = Vector3.Cross(b - a, c - a); var area = n.magnitude / 2f; if (area < 1e-6f) continue; n.Normalize();
  var key = (Mathf.Round(n.x * 20f) / 20f).ToString("F2") + "," + (Mathf.Round(n.y * 20f) / 20f).ToString("F2") + "," + (Mathf.Round(n.z * 20f) / 20f).ToString("F2");
  float s; bins.TryGetValue(key, out s); bins[key] = s + area;
}
var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>(bins);
list.Sort((x, y) => y.Value.CompareTo(x.Value));
var sb = new System.Text.StringBuilder("tris " + t.Length / 3 + " sub " + mesh.subMeshCount + "\n");
for (int i = 0; i < 20 && i < list.Count; i++) sb.Append(list[i].Key + " " + list[i].Value.ToString("F3") + "\n");
UnityEngine.Object.Destroy(o);
return sb.ToString();
