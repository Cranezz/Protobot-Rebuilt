// Points QuickPlace's test mouse at a brain port (Temp/frame.txt) / the motor and reports the wiring hint's port.
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
var qp = typeof(Protobot.Tools.QuickPlace);
GameObject brain = null, motor = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) { if (s0.id == "BRAN") brain = s0.gameObject; if (s0.id.StartsWith("MOTR")) motor = s0.gameObject; }
var sb = new System.Text.StringBuilder();
foreach (var key in new[] { "12", "B", "21", "motor", "11" }) {
  Protobot.Motion.WirePort port = key == "motor" ? Protobot.Motion.WirePorts.Of(motor)[0] : Protobot.Motion.WirePorts.Of(brain).Find(p => p.label == "Port " + key);
  var o = port.Out; var yaw = Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg;
  var screen = Camera.main.WorldToScreenPoint(port.Position + port.Out * 0.05f);
  qp.GetField("TestMouse", flags).SetValue(null, (Vector2?)new Vector2(screen.x, screen.y));
  var hover = (Protobot.Motion.WirePort)qp.GetMethod("HoverWirePort", flags).Invoke(null, null);
  sb.Append(key + " -> " + (hover != null ? (hover.label == "" ? "spot" : hover.label) + " " + hover.kind : "none") + "\n");
}
qp.GetField("TestMouse", flags).SetValue(null, null);
return sb.ToString();
