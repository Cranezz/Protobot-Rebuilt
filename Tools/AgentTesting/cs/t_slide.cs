// Sideways slide of the 6-motor drive (all omnis): an arc at full speed, a spin in place, an S of swings.
// Roller resistance from PlayerPrefs "t_slide_rr" (0 = leave it). Results in PlayerPrefs "t_slide" (read with t_slide_read.cs).
var rr = UnityEngine.PlayerPrefs.GetFloat("t_slide_rr", 0f);
var field = typeof(Protobot.Motion.Sandbox).GetField("RollerResistance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
if (rr > 0f && field != null) field.SetValue(null, rr);
var code = @"from vex import *
L = MotorGroup(Motor(Ports.PORT4, GearSetting.RATIO_6_1, False), Motor(Ports.PORT5, GearSetting.RATIO_6_1, False), Motor(Ports.PORT6, GearSetting.RATIO_6_1, False))
R = MotorGroup(Motor(Ports.PORT1, GearSetting.RATIO_6_1, True), Motor(Ports.PORT2, GearSetting.RATIO_6_1, True), Motor(Ports.PORT3, GearSetting.RATIO_6_1, True))
def logger():
    while 1:
        print('s')
        wait(10, MSEC)
Thread(logger)
def go(l, r, t, name):
    print('phase ' + name)
    L.spin(FORWARD, 12 * l / 100, VOLT)
    R.spin(FORWARD, 12 * r / 100, VOLT)
    wait(t, SECONDS)
go(100, 100, 1.0, 'straight')
go(100, 30, 1.5, 'arc')
go(0, 0, 1.0, 'stop1')
go(100, -100, 1.2, 'spin')
go(0, 0, 1.0, 'stop2')
go(100, 0, 0.8, 'swingL')
go(0, 100, 0.8, 'swingR')
go(0, 0, 1.0, 'stop3')
print('phase end')
";
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var log = new System.Text.StringBuilder();
log.AppendLine("roller resistance " + (field != null ? field.GetValue(null) : "?") + "  mass " + Protobot.Motion.Sandbox.Instance.Bodies().Sum(b => b.rb.mass).ToString("0.00") + " kg");
Vector3 fwdLocal = Vector3.zero;
string phase = "";
float last = 0f, slip = 0f, maxAngle = 0f, maxLat = 0f;
Vector3 phaseStart = rb.position;
System.Action<string> flush = null;
flush = name => {
    var d = rb.position - phaseStart; d.y = 0f;
    if (phase != "") log.AppendLine(phase.PadRight(9) + " moved " + d.magnitude.ToString("0.0") + "\"  sideways slip " + slip.ToString("0.0") + "\"  max slip angle " + maxAngle.ToString("0") + "°  max sideways " + maxLat.ToString("0.0") + " in/s  speed now " + rb.linearVelocity.magnitude.ToString("0.0"));
    phase = name; slip = 0f; maxAngle = 0f; maxLat = 0f; phaseStart = rb.position;
};
System.Action<string> handler = null;
handler = s => {
    if (rb == null) return;
    var t = (float)Protobot.RobotCode.RobotProgram.Current.Time;
    var dt = t - last; last = t;
    s = s.Trim();
    if (s.StartsWith("phase")) {
        if (phase == "straight" && fwdLocal == Vector3.zero) {
            var v0 = rb.linearVelocity; v0.y = 0f;
            fwdLocal = rb.transform.InverseTransformDirection(v0.normalized);
        }
        flush(s.Substring(6));
        if (s == "phase end") { Protobot.RobotCode.RobotProgram.Printed -= handler; Protobot.RobotCode.RobotProgram.Stop("done"); }
        UnityEngine.PlayerPrefs.SetString("t_slide", log.ToString());
        return;
    }
    if (fwdLocal == Vector3.zero) return;
    var fwd = rb.transform.TransformDirection(fwdLocal); fwd.y = 0f; fwd.Normalize();
    var side = Vector3.Cross(Vector3.up, fwd);
    var com = rb.worldCenterOfMass;
    var v = rb.GetPointVelocity(com); v.y = 0f;
    var lat = Vector3.Dot(v, side);
    slip += Mathf.Abs(lat) * (float)dt;
    maxLat = Mathf.Max(maxLat, Mathf.Abs(lat));
    if (v.magnitude > 10f) maxAngle = Mathf.Max(maxAngle, Vector3.Angle(v, fwd * Mathf.Sign(Vector3.Dot(v, fwd) + 1e-6f)));
};
UnityEngine.PlayerPrefs.SetString("t_slide", "running");
Protobot.RobotCode.RobotProgram.Printed += handler;
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Program, 30, out err, out line);
return "started " + ok + " " + err;
