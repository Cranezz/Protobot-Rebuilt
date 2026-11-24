// (Play, sandbox) Where the first motor's part is now in the simulation (its middle) and the sandbox time.
var m = Protobot.Motion.MotorDrive.All.FirstOrDefault(x => x != null);
var p = Protobot.Motion.Sandbox.SimPoint(m.gameObject, m.transform.position);
return "motor at " + p.ToString("F2") + " t " + Time.time.ToString("F1");
