// Physics steps vs. each shaft sensor's samples since the last call (resets both).
var f = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var steps = typeof(Protobot.Motion.Sandbox).GetField("steps", f);
var sb = new System.Text.StringBuilder("steps " + steps.GetValue(null));
steps.SetValue(null, 0);
var sf = typeof(Protobot.Devices.ShaftSensor).GetField("samples", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
foreach (var d in Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.ShaftSensor>()) { sb.Append(" | " + Protobot.Motion.Controls.GivenName(d.part) + " " + sf.GetValue(d)); sf.SetValue(d, 0); }
return sb.ToString();
