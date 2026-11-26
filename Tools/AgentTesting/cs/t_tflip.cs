// (game open) Temp/tflip.txt: "start" starts the run; "N STEPS" pushes Toggle 0 at a corner, square to its axis, with N
// newtons for STEPS physics steps (live, so its detent runs); "read" = its trace (turn from the start, lift) and color.
var cmd = System.IO.File.ReadAllText("Temp/tflip.txt").Trim();
var g = Protobot.Game.SkillsGame.Current; if (g == null) return "no game";
if (cmd == "start") { g.StartRun(); return "state " + g.state; }
var t = g.Toggles[0];
var near = "";
foreach (var c in Physics.OverlapBox(t.GetComponentInChildren<Collider>().bounds.center, t.GetComponentInChildren<Collider>().bounds.extents + Vector3.one * 0.05f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
  if (c.attachedRigidbody != t.body) near += " " + c.name + "/L" + c.gameObject.layer;
if (cmd == "read") return "near:" + near + " robot " + t.TouchedByRobot + " pivot " + t.pivotLocal + " turned " + t.Turned.ToString("F1") + " color " + t.SetColor + " seated " + t.Seated + " spin " + t.body.angularVelocity.magnitude.ToString("F2") + " trace" + t.TestTrace;
var parts = cmd.Split(' '); var newtons = float.Parse(parts[0]); var steps = int.Parse(parts[1]);
var axis = t.transform.TransformDirection(t.axisLocal).normalized;
var corner = -t.transform.TransformDirection(t.faces[1].normal).normalized * t.cornerRadius;
var push = Vector3.Cross(axis, corner).normalized;
t.TestPush(push * newtons * 39.37f, t.transform.InverseTransformPoint(t.body.worldCenterOfMass + corner), steps);
return "R " + t.cornerRadius.ToString("F2") + " hold " + (t.HoldTorque / 1550f).ToString("F3") + " N·m; pushing " + newtons + " N for " + steps + " steps from " + t.Turned.ToString("F0");
