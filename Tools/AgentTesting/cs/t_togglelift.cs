// (game run on) Temp/tl.txt: "push" lifts the first Toggle (an upward push for a moment); "read" prints each Toggle's
// height above its rest, seated, set color.
var g = Protobot.Game.SkillsGame.Current; if (g == null) return "no game";
var cmd = System.IO.File.Exists("Temp/tl.txt") ? System.IO.File.ReadAllText("Temp/tl.txt").Trim() : "read";
if (cmd == "push") {
    var t0 = g.Toggles[0];
    t0.body.AddForce(Vector3.up * t0.body.mass * 2000f, ForceMode.VelocityChange);
    t0.body.linearVelocity = Vector3.up * 40f;
    return "pushed " + t0.quadrant;
}
var sb = new System.Text.StringBuilder();
foreach (var t in g.Toggles)
    sb.AppendLine(t.quadrant + " lift " + (t.transform.position.y - t.restHeight).ToString("F2") + " seated " + t.Seated + " color " + t.SetColor + " moved " + Vector3.ProjectOnPlane(t.transform.position - g.field.transform.position, Vector3.up).magnitude.ToString("F2"));
return sb.ToString();
