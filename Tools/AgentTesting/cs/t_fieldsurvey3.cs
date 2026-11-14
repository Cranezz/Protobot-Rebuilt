var sb = new System.Text.StringBuilder();
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Protobot/Field/Override/Override Field.prefab");
var root = prefab.transform.GetChild(0);
foreach (Transform c in root) {
  if (!(c.name.Contains("840") || c.name.Contains("830") || c.name.Contains("81x") || c.name.Contains("120"))) continue;
  var b = new Bounds(); bool any = false;
  foreach (var mf in c.GetComponentsInChildren<MeshFilter>(true)) { var wb = mf.GetComponent<MeshRenderer>().bounds; if (!any) { b = wb; any = true; } else b.Encapsulate(wb); }
  if (c.name.Contains("81x") && !c.name.EndsWith("81x")) continue;
  sb.Append(c.name + " rot " + c.rotation.eulerAngles.ToString("F0") + " bounds size " + b.size.ToString("F2") + " center " + b.center.ToString("F1") + "\n");
}
foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (mf.name.Contains("9250-023") || mf.name.Contains("9250-02")) { sb.Append("mesh " + mf.name + " parent " + mf.transform.parent.name + " size " + mf.GetComponent<MeshRenderer>().bounds.size.ToString("F2") + "\n"); if (sb.Length > 8000) break; }
return sb.ToString();
