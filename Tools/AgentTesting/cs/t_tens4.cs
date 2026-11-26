// (Play) the swing motor's system
var R = Protobot.EditorTools.MotionTestRig.TensionerParts;
var m = R["swing motor"].GetComponent<Protobot.Motion.MotorDrive>();
var sys = Protobot.Motion.GearTrain.SystemOf(m, Protobot.Motion.GearTrain.Rotors());
var sb = new System.Text.StringBuilder();
sb.AppendLine("time " + Time.time + " frame " + Time.frameCount + " paused " + UnityEditor.EditorApplication.isPaused);
if (sys == null) return sb + "no system";
sb.AppendLine("jammed " + sys.jammed + " " + sys.jamReason + " rotors " + sys.rotors.Count + " chains " + sys.chains.Count);
foreach (var r in sys.rotors) sb.AppendLine(" rotor " + r.shaft.name + " parts " + string.Join(", ", r.parts.Select(p => p.name)) + " motors " + r.motors.Count);
sb.AppendLine("level " + m.Level + " cmd " + m.Command + " manual " + m.Manual);
return sb.ToString();
