// (Play) What the placement ghost snaps to now: each displacement's answer, the hovered object, the ghost's turn.
var p = Protobot.Placement.Instance;
if (p == null) return "no placement";
var sb = new System.Text.StringBuilder();
sb.AppendLine("placing " + p.placing + " id " + p.PlacingId + " rot " + p.transform.eulerAngles + " fwd " + p.transform.forward + " pos " + p.transform.position);
var f = typeof(Protobot.Placement).GetField("placePositions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var data = typeof(Protobot.Placement).GetField("currentPlacementData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(p) as Protobot.PlacementData;
foreach (Protobot.PlaceDisplacement d in (System.Collections.IList)f.GetValue(p)) {
    bool ok = false; string why = "";
    try { ok = d.TryGetDisplacement(data, out var disp); if (ok) why = " -> " + disp.translation.Position + " rot " + disp.rotation.Orientation.eulerAngles; } catch (System.Exception e) { why = " EXC " + e.Message; }
    sb.AppendLine("  " + d.GetType().Name + " " + ok + why);
    if (ok) break;
}
var mc = UnityEngine.Object.FindAnyObjectByType<Protobot.MouseCast>();
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.MouseCast>(FindObjectsSortMode.None)) sb.AppendLine("  mousecast " + m.name + " hits " + (m.hit.collider ? m.hit.collider.name + " / " + m.hit.collider.transform.root.name : "nothing"));
return sb.ToString();
