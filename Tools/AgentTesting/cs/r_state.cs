var field = UnityEngine.Object.FindFirstObjectByType<Protobot.Game.GameField>();
var sb = new System.Text.StringBuilder();
Rigidbody robot = null; float best = 0f;
foreach (var b in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (b.gameObject.layer == 16 && b.mass > best) { best = b.mass; robot = b; }
var rf = field.ToField(robot.worldCenterOfMass);
sb.Append("robot at " + rf.ToString("F1") + " speed " + robot.linearVelocity.magnitude.ToString("F1") + " heading fwd " + robot.transform.forward.ToString("F2") + "\n");
foreach (var b in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (b.gameObject.layer == 18) {
  var f = field.ToField(b.position);
  if ((f - rf).magnitude < 20f) sb.Append(b.name + " at " + f.ToString("F1") + " h " + b.position.y.ToString("F1") + " v " + b.linearVelocity.magnitude.ToString("F1") + "; ");
}
foreach (var t in UnityEngine.Object.FindObjectsByType<Protobot.Game.Toggle>(FindObjectsSortMode.None)) if (t.quadrant.ToString() == "Left") sb.Append("\nLeft toggle " + t.SetColor + " touched " + t.TouchedByRobot);
var intake = Protobot.Motion.MotorDrive.Live().Find(m => m.Label == "Intake");
sb.Append("\nintake command " + (intake != null ? intake.Command.ToString("F2") : "none") + " key held " + UnityEngine.InputSystem.Keyboard.current.iKey.isPressed);
return sb.ToString();
