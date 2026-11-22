// (Play) Hole sizes of some pneumatics parts and whether each counts as a tubing port (PneumaticTube.IsPort): makes
// each part far away, lists its holes, deletes it.
var sb = new System.Text.StringBuilder();
foreach (var name in new[] { "Cylinder Mount", "Cylinder Rod Pivot", "Fitting for Cylinders", "Straight Pneumatic Fitting", "Elbow Fitting", "T Fitting", "Solenoid   Double Acting", "V5 Pneumatic Air Reservoir", "Manifold Assembly", "Pneumatic Hand Valve", "Piston Nut", "On Off Switch" }) {
    GameObject p = null;
    try { p = Protobot.PartsManager.GeneratePart("VXPNEUMATICS-" + name, new Vector3(0f, 5000f, 0f), Quaternion.identity); } catch (System.Exception e) { sb.AppendLine(name + ": " + e.Message); continue; }
    if (p == null) { sb.AppendLine(name + ": none"); continue; }
    sb.Append(name + ":");
    foreach (var h in p.GetComponentsInChildren<Protobot.HoleCollider>(true)) {
        if (h.transform.parent != p.transform) continue;
        var s = h.transform.lossyScale;
        sb.Append(" " + Mathf.Min(s.x, s.y).ToString("F3") + (h.holeType == Protobot.HoleCollider.HoleType.Threaded ? "T" : "") + (Protobot.Motion.PneumaticTube.IsPort(h) ? "P" : ""));
    }
    sb.AppendLine();
    UnityEngine.Object.Destroy(p);
}
return sb.ToString();
