// (Play mode, the 6-motor drive loaded by load_6md.cs) puts a GPS Sensor on top of the robot facing its front and runs
// the "GPS: drive to points" example in the sandbox (no game: the field is the 12' square round the build's origin).
// The example prints "at x y" at each point; the true robot middle (GPS frame) is added. Read with t_marks.cs.
var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None);
var gpsPart = parts.Select(s => s.gameObject).FirstOrDefault(g => Protobot.Devices.V5Brain.KindOf(g) == Protobot.Devices.DeviceKind.Gps);
if (gpsPart == null) {
    var bounds = new Bounds(Vector3.zero, Vector3.zero);
    var first = true;
    foreach (var p in parts) foreach (var r in p.GetComponentsInChildren<Renderer>()) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
    gpsPart = Protobot.PartsManager.GeneratePart("VXSENSORS-GPS Sensor", new Vector3(bounds.center.x, bounds.max.y + 1.5f, bounds.center.z), Quaternion.identity);
    var lk = Protobot.Devices.SensorFrame.Look(gpsPart);
    gpsPart.transform.rotation = Quaternion.FromToRotation(gpsPart.transform.TransformDirection(lk.dir), Vector3.forward) * gpsPart.transform.rotation;
    UnityEngine.Physics.SyncTransforms();
    // fasten it: a part that's just placed isn't screwed to anything; give it the robot's biggest body by parenting in the sim
}
Protobot.Devices.V5Brain.Refresh(true);
var example = Protobot.RobotCode.Examples.All.First(e => e.name.StartsWith("GPS"));
var code = Protobot.RobotCode.Examples.Fill(example.code);
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var gps = Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.GpsSensor>().First();
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => {
    if (s.StartsWith("at")) {
        var center = Protobot.Devices.GpsSensor.ToField(Protobot.Devices.Pose.Point(gpsPart, Vector3.zero));
        s = s.TrimEnd() + "  (gps itself " + gps.X.ToString("0.0") + ", " + gps.Y.ToString("0.0") + " q " + gps.Quality + ")\n";
    }
    log.Append(s);
    UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString());
};
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line);
var cfg = code.Substring(0, Mathf.Min(code.Length, 1400));
return "started " + ok + " " + err + "\n" + string.Join("\n", cfg.Split('\n').Where(l => l.Contains("Gps(") || l.Contains("MotorGroup(")));
