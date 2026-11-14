var sb = new System.Text.StringBuilder();
var root = Resources.Load<GameObject>("Part Prefabs/5Structure/Nut");
foreach (Transform c in root.transform) {
  var mf = c.GetComponent<MeshFilter>(); if (mf == null) continue;
  var m = mf.sharedMesh;
  sb.Append(c.name + " mesh " + (m != null ? m.name + " size " + m.bounds.size.ToString("F3") + " sub " + m.subMeshCount : "none") + " mats " + string.Join(",", System.Linq.Enumerable.Select(c.GetComponent<MeshRenderer>().sharedMaterials, x => x != null ? x.name : "null")) + "\n");
}
var fbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Protobot/Parts/Models/Structure/Hardware/Nut Low Profile.fbx");
foreach (var mf in fbx.GetComponentsInChildren<MeshFilter>(true)) sb.Append("fbx " + mf.name + " size(in) " + (mf.sharedMesh.bounds.size * 39.3701f).ToString("F3") + " center " + (mf.sharedMesh.bounds.center * 39.3701f).ToString("F3") + " sub " + mf.sharedMesh.subMeshCount + " mats " + string.Join(",", System.Linq.Enumerable.Select(mf.GetComponent<MeshRenderer>().sharedMaterials, x => x != null ? x.name : "null")) + "\n");
return sb.ToString();
