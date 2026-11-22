// (game open) Temp/ml.txt: "start" starts the run; "drop K" drops a Match Load of kind K; "where" lists the pieces
// near the red Loaders (field frame); "raise" raises/lowers the chosen Loader; "column" lists the Loader columns.
var cmd = System.IO.File.ReadAllText("Temp/ml.txt").Trim();
var g = Protobot.Game.SkillsGame.Current; if (g == null) return "no game";
if (cmd == "start") { g.StartRun(); return "state " + g.state; }
if (cmd == "raise") { g.ToggleLoader(); return "loader up " + g.LoaderUp; }
if (cmd == "column") { var col = g.field.transform.Find("Field Colliders"); var sb2 = new System.Text.StringBuilder(); foreach (Transform c in col) if (c.name == "Loader Column") sb2.AppendLine("column h " + g.field.HeightOf(c.position).ToString("F2") + " kids " + c.childCount + " at " + g.field.ToField(c.GetComponentInChildren<Collider>().bounds.center)); return sb2.ToString(); }
if (cmd.StartsWith("drop ")) { g.DropMatchLoad(cmd.Substring(5)); return "dropped, state " + g.state; }
var f = g.field;
var sb = new System.Text.StringBuilder("state " + g.state + " time " + g.timeLeft.ToString("0.0") + "\n");
foreach (var p in g.Pins.Select(x => (Component)x).Concat(g.Cups.Select(x => (Component)x))) {
    var c = f.ToField(p.transform.position);
    if (c.x > -60f) continue;
    sb.AppendLine("  " + p.GetType().Name + " at (" + c.x.ToString("F1") + ", " + c.y.ToString("F1") + ") h " + f.HeightOf(p.transform.position).ToString("F1") + " v " + p.GetComponent<Rigidbody>().linearVelocity.magnitude.ToString("F1"));
}
return sb.ToString();
