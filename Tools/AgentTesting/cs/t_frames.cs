// Local frames of building blocks: mesh bounds and holes (local pos, axis, depth, type).
var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "CCHL-1x2-5", "ANGL-2x2-5", "ANGL-1x1-5", "PLTE-3-5", "SNDF-2.00in", "FWHL-With Adapters-3.00in", "BRNG-Normal" }) {
    var go = Protobot.PartsManager.GeneratePart(id, new Vector3(0f, -500f, 0f), Quaternion.identity);
    if (go == null) { sb.AppendLine(id + ": null"); continue; }
    var m = go.GetComponent<MeshFilter>().sharedMesh.bounds;
    sb.AppendLine(id + " mesh c " + m.center.ToString("F3") + " size " + m.size.ToString("F3"));
    var n = 0;
    foreach (var h in go.GetComponentsInChildren<Protobot.HoleCollider>(true)) {
        if (n++ >= 14) { sb.AppendLine("  ..."); break; }
        var t = h.transform;
        var lp = go.transform.InverseTransformPoint(t.position);
        var lf = go.transform.InverseTransformDirection(t.forward);
        sb.AppendLine("  " + lp.ToString("F3") + " ax " + lf.ToString("F1") + " d " + t.lossyScale.z.ToString("F3") + " " + h.holeType);
    }
    UnityEngine.Object.DestroyImmediate(go);
}
return sb.ToString();
