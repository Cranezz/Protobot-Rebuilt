var g = Protobot.Game.SkillsGame.Current; var ft = g.field.transform;
var sb = new System.Text.StringBuilder("state " + g.state + " | ");
foreach (var robot in new[] { 0, 1 }) {
  Vector3 c = Vector3.zero; int n = 0;
  foreach (var t in UnityEngine.Object.FindObjectsByType<Protobot.Online.NetPart>(UnityEngine.FindObjectsSortMode.None)) if (t.robot == robot) { c += Protobot.Motion.Sandbox.SimPoint(t.gameObject, t.transform.position); n++; }
  c /= n;
  var near = g.Pins.Where(p => p != null && Vector3.Distance(p.transform.position, c) < 14f).Count() + g.Cups.Where(p => p != null && Vector3.Distance(p.transform.position, c) < 14f).Count();
  sb.Append("robot " + robot + ": " + near + " pieces within 14\" | ");
}
return sb.ToString();
