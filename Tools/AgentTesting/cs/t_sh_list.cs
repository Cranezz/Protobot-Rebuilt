var go = UnityEngine.Resources.Load<UnityEngine.GameObject>("Part Prefabs/VEX/Hardware");
var sb = new System.Text.StringBuilder();
foreach (UnityEngine.Transform c in go.transform) {
  if (!c.name.Contains("Shoulder")) continue;
  var mf = c.GetComponent<UnityEngine.MeshFilter>(); var mr = c.GetComponent<UnityEngine.MeshRenderer>();
  sb.Append(c.name + " | sub " + (mf && mf.sharedMesh ? mf.sharedMesh.subMeshCount : -1) + " mats " + (mr ? string.Join("+", System.Linq.Enumerable.Select(mr.sharedMaterials, m => m ? m.name : "null")) : "-") + " tris " + (mf && mf.sharedMesh ? mf.sharedMesh.triangles.Length/3 : 0) + " size " + (mf && mf.sharedMesh ? mf.sharedMesh.bounds.size.ToString("F3") : "") + "\n");
}
return sb.ToString();
