// (Play mode, the demo robot loaded by t_loaddemo.cs) GPS Sensor: puts one on top of the robot at the back, facing
// back, then compares its readings with the truth: in the build (the field round the build's origin), in a skills game
// (the game's field frame), with a block in front of its camera (quality falls), and prints Insert devices' lines.
if (!UnityEngine.Application.isPlaying) return "not playing";
var sb = new System.Text.StringBuilder();
foreach (var old in UnityEngine.Object.FindObjectsByType<Transform>(UnityEngine.FindObjectsSortMode.None).Where(t => t != null && t.name == "GpsTest").ToList()) UnityEngine.Object.Destroy(old.gameObject);
var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None);
var bounds = new Bounds(Vector3.zero, Vector3.zero);
var first = true;
foreach (var p in parts) foreach (var r in p.GetComponentsInChildren<Renderer>()) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
sb.AppendLine("robot bounds " + bounds.center.ToString("F1") + " size " + bounds.size.ToString("F1"));
var gpsPart = UnityEngine.GameObject.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Select(s => s.gameObject).FirstOrDefault(g => Protobot.Devices.V5Brain.KindOf(g) == Protobot.Devices.DeviceKind.Gps);
if (gpsPart == null) {
    gpsPart = Protobot.PartsManager.GeneratePart("VXSENSORS-GPS Sensor", new Vector3(bounds.center.x, bounds.max.y + 2f, bounds.min.z - 1f), Quaternion.identity);
    // turn it so its camera faces -Z (the back, if the robot's front is +Z)
    var lk = Protobot.Devices.SensorFrame.Look(gpsPart);
    gpsPart.transform.rotation = Quaternion.FromToRotation(gpsPart.transform.TransformDirection(lk.dir), Vector3.back) * gpsPart.transform.rotation;
}
UnityEngine.Physics.SyncTransforms();
Protobot.Devices.V5Brain.Refresh(true);
var gps = Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.GpsSensor>().FirstOrDefault();
if (gps == null) return sb + "no GpsSensor (devices: " + string.Join(", ", Protobot.Devices.V5Brain.Devices.Select(d => d.kind + "@" + d.port)) + ")";
System.Action<string> Read = label => {
    for (var i = 0; i < 5; i++) gps.Sample(0.02f);
    var lens = gps.Lens();
    var truth = Protobot.Devices.GpsSensor.ToField(lens.origin);
    var th = Protobot.Devices.GpsSensor.HeadingOf(Protobot.Devices.GpsSensor.DirToField(lens.look));
    sb.AppendLine(label + ": reads x " + gps.X.ToString("F2") + " y " + gps.Y.ToString("F2") + " h " + gps.Heading.ToString("F1") + " q " + gps.Quality + " wall " + gps.WallDistance.ToString("F1") + " seeing " + gps.Seeing
        + " | truth x " + truth.x.ToString("F2") + " y " + truth.z.ToString("F2") + " h " + th.ToString("F1"));
};
sb.AppendLine("port " + gps.port + ", look " + gps.Lens().look.ToString("F2"));
Read("build");
var gl = typeof(Protobot.RobotCode.DeviceConfig).GetMethod("GpsLines", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
System.Func<string, string> Lines = lang => (string)gl.Invoke(null, new object[] { gps, new System.Collections.Generic.HashSet<string>(), lang, null });
sb.AppendLine("py:   " + Lines("py"));
sb.AppendLine("vex:  " + Lines("vex"));
sb.AppendLine("pros: " + Lines("pros"));
var def = Resources.LoadAll<Protobot.Game.GameDefinition>("Games")[0];
if (Protobot.Game.SkillsGame.Current == null) Protobot.Game.SkillsGame.Open(def, true);
UnityEngine.Physics.SyncTransforms();
var game = Protobot.Game.SkillsGame.Current;
sb.AppendLine("game start x " + game.x + " y " + game.y + " heading " + game.heading);
Read("game");
// turn it to face the robot's front (across the field)
gpsPart.transform.rotation = Quaternion.AngleAxis(180f, Vector3.up) * gpsPart.transform.rotation;
UnityEngine.Physics.SyncTransforms();
Read("game, facing front");
for (var i = 0; i < 3; i++) gps.Sample(0.02f);
// a block 10" in front of its camera
var lens2 = gps.Lens();
var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
block.name = "GpsTest";
block.transform.position = lens2.origin + lens2.look * 10f;
block.transform.localScale = new Vector3(30f, 30f, 2f);
block.transform.rotation = Quaternion.LookRotation(lens2.look);
UnityEngine.Physics.SyncTransforms();
for (var i = 0; i < 10; i++) gps.Sample(0.1f);
Read("blocked 1 s");
for (var i = 0; i < 30; i++) gps.Sample(0.1f);
Read("blocked 4 s");
UnityEngine.Object.Destroy(block);
UnityEngine.Physics.SyncTransforms();
return sb.ToString();
