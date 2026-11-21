// Spawns parts far away (identity turn) and lists each one's mesh box and holes (local), then removes them.
var ids = UnityEngine.PlayerPrefs.GetString("t_partholes", "SNSR-Rotation-V5,ANGL-2x2-4,PLTE-2-5,OMNI-V2-2.75in,SNSR-Inertial-V5").Split(',');
var sb = new System.Text.StringBuilder();
var at = new Vector3(500f, 50f, 500f);
foreach (var id in ids) {
    var p = Protobot.PartsManager.GeneratePart(id, at, Quaternion.identity);
    if (p == null) { sb.AppendLine(id + ": none"); continue; }
    UnityEngine.Physics.SyncTransforms();
    var mf = p.GetComponentInChildren<MeshFilter>();
    var b = mf != null ? mf.sharedMesh.bounds : new Bounds();
    var rs = p.GetComponentsInChildren<Renderer>(); var wb = rs[0].bounds; foreach (var r in rs) wb.Encapsulate(r.bounds);
    sb.AppendLine(id + " box " + (wb.min - at).ToString("F3") + ".." + (wb.max - at).ToString("F3"));
    foreach (var h in p.GetComponentsInChildren<Protobot.HoleCollider>(true))
        sb.AppendLine("   " + h.holeType + " " + (h.transform.position - at).ToString("F3") + " ax " + h.transform.forward.ToString("F2") + " sz " + h.transform.lossyScale.ToString("F3"));
    UnityEngine.Object.Destroy(p);
}
return sb.ToString();
