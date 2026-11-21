// Lists the odometry pods' parts (2.75" omnis, plates, rotation sensors, angles, bands) with position and axis.
var sb = new System.Text.StringBuilder();
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) {
    if (!(so.id.StartsWith("OMNI-V2-2.75") || so.id.StartsWith("PLTE-2-6") || so.id.StartsWith("SNSR") || so.id.StartsWith("ANGL-3x3") || so.id.StartsWith("BAND") || so.id.StartsWith("SNDF-1.50") || so.id.StartsWith("SHFT-Normal-3") && Mathf.Abs(so.transform.position.x) < 4f)) continue;
    sb.AppendLine(so.id + " at " + so.transform.position.ToString("F2") + " fwd " + so.transform.forward.ToString("F2") + " up " + so.transform.up.ToString("F2"));
}
foreach (var p in Protobot.EditorTools.OdomRig.pivots) sb.AppendLine("pivot " + (p == null ? "null" : p.transform.position.ToString("F2") + " joint " + Protobot.Motion.Linkage.IsJoint(p)));
return sb.ToString();
