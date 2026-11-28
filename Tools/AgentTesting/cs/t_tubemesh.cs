// Edit mode: the Loader tube mesh (276-9250-031) in the full field: submeshes, materials, and its outline by height.
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Protobot/Field/Override/Override Field.prefab");
Transform tube = null;
foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("276-9250-031") && t.GetComponent<MeshFilter>() != null) { tube = t; break; }
var mesh = tube.GetComponent<MeshFilter>().sharedMesh;
var r = tube.GetComponent<MeshRenderer>();
var sb = new System.Text.StringBuilder(tube.name + " mesh " + mesh.name + " sub " + mesh.subMeshCount + " readable " + mesh.isReadable + " mats");
foreach (var m in r.sharedMaterials) sb.Append(" " + (m ? m.name : "null"));
sb.AppendLine();
var v = mesh.vertices;
var M = tube.localToWorldMatrix;
var rootInv = prefab.transform.worldToLocalMatrix;
var w = new Vector3[v.Length];
float lo = 1e9f, hi = -1e9f;
for (var i = 0; i < v.Length; i++) { w[i] = rootInv.MultiplyPoint3x4(M.MultiplyPoint3x4(v[i])); lo = Mathf.Min(lo, w[i].y); hi = Mathf.Max(hi, w[i].y); }
sb.AppendLine("y " + lo.ToString("F2") + " .. " + hi.ToString("F2"));
for (var y = lo; y < hi; y += 0.25f) {
    float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f; var n = 0;
    for (var i = 0; i < w.Length; i++) if (w[i].y >= y && w[i].y < y + 0.25f) { n++; x0 = Mathf.Min(x0, w[i].x); x1 = Mathf.Max(x1, w[i].x); z0 = Mathf.Min(z0, w[i].z); z1 = Mathf.Max(z1, w[i].z); }
    if (n > 0) sb.AppendLine(y.ToString("F2") + ": n " + n + " x " + x0.ToString("F2") + ".." + x1.ToString("F2") + " z " + z0.ToString("F2") + ".." + z1.ToString("F2"));
}
System.IO.File.WriteAllText("Temp/tubemesh.txt", sb.ToString());
return "ok";
