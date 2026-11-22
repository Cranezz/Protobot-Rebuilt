// (sandbox running, robot at rest) Which robot bodies move: horizontal speed and turn about the vertical, and the hold's counter.
var sb = Protobot.Motion.Sandbox.Instance;
if (sb == null) return "no sandbox";
var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var t = typeof(Protobot.Motion.Sandbox);
var bodies = (System.Collections.IList)t.GetField("bodies", f).GetValue(sb);
var stillFor = t.GetField("stillFor", f).GetValue(sb);
var lines = new System.Collections.Generic.List<string>();
foreach (var b in bodies) {
    var rb = (Rigidbody)b.GetType().GetField("rb").GetValue(b);
    if (rb == null) continue;
    var v = rb.linearVelocity;
    var h = new Vector2(v.x, v.z).magnitude;
    var w = rb.angularVelocity.y * Mathf.Rad2Deg;
    if (h > 0.05f || Mathf.Abs(w) > 0.1f) lines.Add(rb.mass.ToString("F3") + " kg: " + h.ToString("F3") + " in/s, yaw " + w.ToString("F2") + " deg/s, kin " + rb.isKinematic + ", sleep " + rb.IsSleeping());
}
var cmds = "";
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) if (Mathf.Abs(m.Command) > 0.0005f) cmds += m.name + " " + m.Command.ToString("F4") + "; ";
return "stillFor " + stillFor + ", bodies " + bodies.Count + ", moving: " + string.Join(" | ", lines) + " || commands: " + cmds;
