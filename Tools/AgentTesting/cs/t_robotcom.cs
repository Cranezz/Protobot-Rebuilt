// The robot's center of mass from the parts' weights (build frame, inches), beside the motors' and wheels' middles
var total = 0f; var sum = Vector3.zero; var n = 0;
foreach (var o in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) {
    if (!o.gameObject.activeInHierarchy) continue;
    var pn = o.GetComponent<Parts_List.PartName>();
    var g = pn != null ? pn.weightInGrams : 0f;
    if (g <= 0f) continue;
    var mf = o.GetComponent<MeshFilter>();
    var at = mf != null && mf.sharedMesh != null ? o.transform.TransformPoint(mf.sharedMesh.bounds.center) : o.transform.position;
    sum += at * g; total += g; n++;
}
return "parts " + n + " weight " + (total / 453.6f).ToString("0.00") + " lb, center of mass " + (sum / total).ToString("F2");
