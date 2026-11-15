// Game open, a run started: piece sizes, goal tops, toggle heights (field frame).
var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder();
var f = g.field;
foreach (var goal in f.goals) sb.Append(goal.name + " (" + goal.position.x.ToString("F1") + "," + goal.position.y.ToString("F1") + ") top " + goal.top.ToString("F2") + " " + goal.quadrant + "; ");
sb.AppendLine();
if (g.Pins.Count > 0) {
    var p = g.Pins[0];
    var b = new Bounds(p.transform.position, Vector3.zero);
    foreach (var r in p.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
    sb.AppendLine("pin halfLength " + p.halfLength.ToString("F2") + " bounds " + b.size.ToString("F2") + " at height " + f.HeightOf(p.transform.position).ToString("F2") + " mass " + p.body.mass);
}
if (g.Cups.Count > 0) {
    var c = g.Cups[0];
    var b = new Bounds(c.transform.position, Vector3.zero);
    foreach (var r in c.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
    sb.AppendLine("cup halfLength " + c.halfLength.ToString("F2") + " bounds " + b.size.ToString("F2") + " mass " + c.body.mass);
}
foreach (var t in g.Toggles) {
    var b = new Bounds(t.transform.position, Vector3.zero);
    foreach (var r in t.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
    var c = f.ToField(b.center);
    sb.Append("toggle at (" + c.x.ToString("F1") + "," + c.y.ToString("F1") + ") h " + f.HeightOf(b.center).ToString("F2") + " size " + b.size.ToString("F1") + "; ");
}
var lying = 0; foreach (var p in g.Pins) if (Vector3.Dot(p.transform.TransformDirection(p.halves[0].direction), Vector3.up) < 0.5f) lying++;
sb.AppendLine("\npins " + g.Pins.Count + " (lying " + lying + "), cups " + g.Cups.Count + ", robot x " + g.x.ToString("F1") + " y " + g.y.ToString("F1"));
return sb.ToString();
