// (Play, 6-motor drive loaded) Builds the odometry pods and the Inertial Sensor (Editor/OdomRig).
var t = System.Type.GetType("Protobot.EditorTools.OdomRig, Assembly-CSharp-Editor");
if (t == null) return "no OdomRig (not compiled yet)";
try { return (string)t.GetMethod("Build").Invoke(null, null); } catch (System.Reflection.TargetInvocationException e) { return e.InnerException.ToString(); }
