// (Play, the odometry robot built: t_odom_build + t_odom_finish) Runs a robot program from a file (PlayerPrefs
// "t_odom_prog", default odom/py_lemlib.py) with {vert} {horiz} {imu} replaced by the tracking sensors' and the
// Inertial's ports, in the sandbox from a standstill. Each printed line starting with "mark" gets the truth added:
// where the build's origin (the tracking center the offsets are measured from) really is, in the start frame
// (x right, y forward, inches) and the frame's real heading (clockwise). Read the marks with t_marks.cs.
Protobot.Devices.V5Brain.Refresh(true);
System.Func<string, int> Port = name => { var d = Protobot.Devices.V5Brain.Devices.FirstOrDefault(x => Protobot.Motion.Controls.GivenName(x.part) == name); return d != null ? d.port : 0; };
var file = UnityEngine.PlayerPrefs.GetString("t_odom_prog", "odom/py_lemlib.py");
var text = System.IO.File.ReadAllText("Tools/AgentTesting/" + file).Replace("{vert}", Port("Vertical tracking").ToString()).Replace("{horiz}", Port("Horizontal tracking").ToString())
    .Replace("{imu}", Port("Inertial").ToString());
// the wheels' offsets from the build's origin (vertical: its x, left negative; horizontal: its z, back negative)
System.Func<string, Vector3> WheelAt = name => {
    var d0 = Protobot.Devices.V5Brain.Devices.FirstOrDefault(x => Protobot.Motion.Controls.GivenName(x.part) == name);
    if (d0 == null) return Vector3.zero;
    var s0 = d0.part;
    return UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(o => o.id == "OMNI-V2-2.75in")
        .Select(o => o.transform.position).OrderBy(p => (p - s0.transform.position).sqrMagnitude).First(); };
var vw = WheelAt("Vertical tracking"); var hw = WheelAt("Horizontal tracking");
// {voffc}/{hoffc}: from the turning center (the drive wheels' middle, as Insert devices measures), and the truth there too
var driveWheels = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(o => o.id.StartsWith("OMNI") && !o.id.Contains("2.75")).Select(o => o.transform.position).ToList();
var turnCenter = Vector3.zero; foreach (var w0 in driveWheels) turnCenter += w0; if (driveWheels.Count > 0) turnCenter /= driveWheels.Count;
var fromCenter = text.Contains("{voffc}") || text.StartsWith("# truth: center");
var inv0 = System.Globalization.CultureInfo.InvariantCulture;
text = text.Replace("{voffc}", (vw.x - turnCenter.x).ToString("0.###", inv0)).Replace("{hoffc}", (hw.z - turnCenter.z).ToString("0.###", inv0));
var inv = System.Globalization.CultureInfo.InvariantCulture;
text = text.Replace("{voffabs}", Mathf.Abs(vw.x).ToString("0.###", inv)).Replace("{hoffabs}", Mathf.Abs(hw.z).ToString("0.###", inv))
    .Replace("{voff}", vw.x.ToString("0.###", inv)).Replace("{hoff}", hw.z.ToString("0.###", inv));
var lefts = Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.MotorDevice>().Where(m => Protobot.Motion.Controls.Side(m.part) == "L").Select(m => (Protobot.Motion.Controls.Reversed(m.part) ? -m.port : m.port).ToString());
var rights = Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.MotorDevice>().Where(m => Protobot.Motion.Controls.Side(m.part) == "R").Select(m => (Protobot.Motion.Controls.Reversed(m.part) ? -m.port : m.port).ToString());
text = text.Replace("{left_ports}", "{" + string.Join(", ", lefts) + "}").Replace("{right_ports}", "{" + string.Join(", ", rights) + "}");
if (text.Contains("{cppconfig}")) text = text.Replace("{cppconfig}", Protobot.RobotCode.DeviceConfig.GenerateCpp(true));
var code = text.Contains("{config}") ? text.Replace("{config}", Protobot.RobotCode.DeviceConfig.Generate()) : text;
var period = text.StartsWith("// period: autonomous") ? Protobot.RobotCode.RobotProgram.Period.Autonomous : Protobot.RobotCode.RobotProgram.Period.Program;
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var frame = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(s => s.id == "CCHL-1x2-23").OrderBy(s => s.transform.position.z).Skip(1).First().gameObject;
var origin = fromCenter ? new Vector3(turnCenter.x, frame.transform.position.y, turnCenter.z) : new Vector3(0f, frame.transform.position.y, 0f);
var local = frame.transform.InverseTransformPoint(origin);
var fwdLocal = frame.transform.InverseTransformDirection(Vector3.forward);
var start = Protobot.Devices.Pose.Point(frame, local);
var startFwd = Protobot.Devices.Pose.Dir(frame, fwdLocal);
var startYaw = Mathf.Atan2(startFwd.x, startFwd.z) * Mathf.Rad2Deg;
var marks = new System.Collections.Generic.List<string>();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => {
    if (!s.StartsWith("mark")) { if (s.Contains("rror") || s.Contains("arning")) { marks.Add(s.Trim()); UnityEngine.PlayerPrefs.SetString("t_code_marks", string.Join("\n", marks)); } return; }
    var p = Protobot.Devices.Pose.Point(frame, local);
    var f = Protobot.Devices.Pose.Dir(frame, fwdLocal);
    var d = Quaternion.Euler(0f, -startYaw, 0f) * (p - start);
    var yaw = Mathf.DeltaAngle(startYaw, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
    marks.Add(s.Trim() + "   TRUE x=" + d.x.ToString("0.00") + " y=" + d.z.ToString("0.00") + " h=" + yaw.ToString("0.00") + " t=" + (Protobot.RobotCode.RobotProgram.Current != null ? Protobot.RobotCode.RobotProgram.Current.Time.ToString("0.0") : ""));
    UnityEngine.PlayerPrefs.SetString("t_code_marks", string.Join("\n", marks));
};
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, period, 0, out err, out line);
return "started " + ok + " " + err + " line " + line + "  ports v " + Port("Vertical tracking") + " h " + Port("Horizontal tracking") + " imu " + Port("Inertial") + " offsets v " + vw.x.ToString("0.00") + " h " + hw.z.ToString("0.00");
