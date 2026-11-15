var sb = new System.Text.StringBuilder();
var ids = new System.Collections.Generic.List<string>();
foreach (var n in Protobot.Interface.PartsCatalog.Nodes)
    foreach (var l in n.Leaves()) {
        if (l.typeId == null) continue;
        var id = l.typeId + (l.p1 != null ? "-" + l.p1 : "") + (l.p2 != null ? "-" + l.p2 : "");
        if (l.name.Contains("Brain") || l.name.Contains("Battery") || l.name.ToLower().Contains("battery clip")) ids.Add(id + "|" + l.name);
    }
foreach (var e in ids) {
    var id = e.Split('|')[0];
    var go = Protobot.PartsManager.GeneratePart(id, new Vector3(0f, -500f, 0f), Quaternion.identity);
    if (go == null) { sb.AppendLine(e + " null"); continue; }
    var m = go.GetComponent<MeshFilter>().sharedMesh.bounds;
    var pn = go.GetComponent<Parts_List.PartName>();
    sb.AppendLine(e + " mesh c " + m.center.ToString("F2") + " size " + m.size.ToString("F2") + " " + (pn != null ? pn.weightInGrams.ToString("F0") + " g" : ""));
    var k = 0;
    foreach (var h in go.GetComponentsInChildren<Protobot.HoleCollider>(true)) {
        if (k++ >= 8) break;
        sb.AppendLine("  " + go.transform.InverseTransformPoint(h.transform.position).ToString("F2") + " ax " + go.transform.InverseTransformDirection(h.transform.forward).ToString("F1") + " " + h.holeType + " d " + h.transform.lossyScale.z.ToString("F2"));
    }
    UnityEngine.Object.DestroyImmediate(go);
}
return sb.ToString();
