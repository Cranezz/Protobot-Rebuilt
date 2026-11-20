// (Play mode) AI Vision Sensor: there's no AI Vision model in the parts yet, so this puts the simulated device on a
// V5 Vision Sensor part (port 20) on top of the demo robot's front, looking forward, with a red block in view; makes a
// color from the block (as the card's click does), takes pictures in the build and in a skills game (the field's pins
// and cups for the AI model), and runs Python, VEXcode C++ and PROS programs against it. Needs t_loaddemo.cs first.
if (!UnityEngine.Application.isPlaying) return "not playing";
var sb = new System.Text.StringBuilder();
foreach (var old in UnityEngine.Object.FindObjectsByType<Transform>(UnityEngine.FindObjectsSortMode.None).Where(t => t != null && t.name == "AiTest").ToList()) UnityEngine.Object.Destroy(old.gameObject);
var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None);
var bounds = new Bounds(Vector3.zero, Vector3.zero);
var first = true;
foreach (var p in parts) foreach (var r in p.GetComponentsInChildren<Renderer>()) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
var game = Protobot.Game.SkillsGame.Current;
var front = game != null ? game.front : Vector3.forward;
var at = bounds.center + front * (Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(front.x), 0f, Mathf.Abs(front.z))) - 1f);
at.y = bounds.max.y + 2f;
var cam = Protobot.PartsManager.GeneratePart("SNSR-Vision-V5", at, Quaternion.identity);
cam.name = "AiTest";
var lk = Protobot.Devices.SensorFrame.Look(cam);
cam.transform.rotation = Quaternion.FromToRotation(cam.transform.TransformDirection(lk.dir), (front - Vector3.up * 0.25f).normalized) * cam.transform.rotation;
UnityEngine.Physics.SyncTransforms();
var sensor = new Protobot.Devices.AiVisionSensor(cam);
sensor.port = 20;
var list = (System.Collections.Generic.List<Protobot.Devices.V5Device>)typeof(Protobot.Devices.V5Brain).GetField("devices", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
list.RemoveAll(d => d is Protobot.Devices.AiVisionSensor || d.port == 20 || d.part == cam);
list.Add(sensor);
var lens = sensor.Lens();
// a red block 24" out, a bit to the right
var right = Vector3.Cross(Vector3.up, lens.look).normalized;
var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
block.name = "AiTest";
UnityEngine.Object.Destroy(block.GetComponent<Collider>());
block.transform.position = lens.origin + lens.look * 24f + right * 5f;
block.transform.localScale = Vector3.one * 4f;
block.GetComponent<Renderer>().material.color = new Color(0.6f, 0.08f, 0.08f);
sensor.RenderNow();
// the block's pixel
var cv = (Camera)typeof(Protobot.Devices.VisionSensor).Assembly.GetType("Protobot.Devices.VisionCamera").GetMethod("Get").Invoke(null, null);
var vp = cv.WorldToViewportPoint(block.transform.position);
var px = new Vector2Int(Mathf.RoundToInt(vp.x * 320f), Mathf.RoundToInt((1f - vp.y) * 240f));
sb.AppendLine("block at px " + px + " pixel " + sensor.pixels[px.y * 320 + px.x]);
var red = Protobot.Devices.AiVisionSensor.Make(sensor.pixels, new RectInt(px.x - 4, px.y - 4, 9, 9), 1, "RED");
sb.AppendLine("color " + (red.HasValue ? red.Value.Token : "none"));
var saved = new Protobot.Devices.AiColor?[7];
saved[0] = red;
Protobot.Devices.AiVisionSensor.Save(cam, saved);
sensor.RenderNow();
foreach (var o in sensor.objects) sb.AppendLine("  " + o.kind + " " + o.id + " at " + o.x + "," + o.y + " " + o.width + "x" + o.height + (o.kind == Protobot.Devices.AiKind.Object ? " score " + o.score : ""));
if (game == null) {
    var def = Resources.LoadAll<Protobot.Game.GameDefinition>("Games")[0];
    Protobot.Game.SkillsGame.Open(def, true);
    UnityEngine.Physics.SyncTransforms();
    sb.AppendLine("opened a game: run again to see the field from the robot");
    return sb.ToString();
}
sb.AppendLine("pieces known " + Protobot.Devices.AiVisionSensor.GamePieces().Count());
// programs
var py = "from vex import *\n" +
    "RED = Colordesc(1, " + red.Value.r + ", " + red.Value.g + ", " + red.Value.b + ", 10, 0.2)\n" +
    "ai = AiVision(Ports.PORT20, RED, AiVision.ALL_AIOBJS)\n" +
    "reds = ai.take_snapshot(RED)\n" +
    "print('py red', len(reds), reds[0].centerX if reds else -1, reds[0].width if reds else -1)\n" +
    "objs = ai.take_snapshot(AiVision.ALL_AIOBJS)\n" +
    "print('py ai', len(objs), [(o.id, o.classname, o.score) for o in objs][:4])\n" +
    "print('py tags', len(ai.take_snapshot(AiVision.ALL_TAGS)))\n";
var cpp = "#include \"vex.h\"\nusing namespace vex;\n" +
    "aivision::colordesc RED = aivision::colordesc(1, " + red.Value.r + ", " + red.Value.g + ", " + red.Value.b + ", 10, 0.2);\n" +
    "aivision AI = aivision(PORT20, RED, aivision::ALL_AIOBJS);\n" +
    "int main() {\n  int n = AI.takeSnapshot(RED);\n" +
    "  printf(\"cpp red %d %d %d\\n\", n, AI.objects[0].centerX, AI.objectCount);\n" +
    "  int m = AI.takeSnapshot(aivision::ALL_AIOBJS);\n  printf(\"cpp ai %d id %d\\n\", m, AI.largestObject.id);\n  return 0;\n}\n";
var pros = "#include \"main.h\"\n" +
    "pros::AIVision ai(20);\n" +
    "void initialize() {\n" +
    "  ai.set_color(pros::AIVision::Color(1, " + red.Value.r + ", " + red.Value.g + ", " + red.Value.b + ", 10, 0.2));\n" +
    "  ai.enable_detection_types(pros::AivisionModeType::colors | pros::AivisionModeType::objects);\n" +
    "  pros::delay(50);\n" +
    "  int n = ai.get_object_count();\n" +
    "  printf(\"pros count %d\\n\", n);\n" +
    "  for (int i = 0; i < n && i < 4; i++) {\n" +
    "    pros::AIVision::Object o = ai.get_object(i);\n" +
    "    if (pros::AIVision::is_type(o, pros::AivisionDetectType::color)) printf(\"pros color %d x %d w %d\\n\", o.id, o.object.color.xoffset, o.object.color.width);\n" +
    "    else printf(\"pros element %d score %d\\n\", o.id, o.object.element.score);\n" +
    "  }\n}\n";
var output = new System.Text.StringBuilder();
System.Action<string> listen = s => output.Append(s);
Protobot.RobotCode.RobotProgram.Printed += listen;
foreach (var program in new[] { py, cpp, pros }) {
    string err; int line;
    if (!Protobot.RobotCode.RobotProgram.Start(program, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line)) { output.AppendLine("ERROR " + err + " line " + line); continue; }
    list.RemoveAll(d => d is Protobot.Devices.AiVisionSensor || d.port == 20 || d.part == cam);
    list.Add(sensor);   // (starting a program finds the devices again: the stand-in sensor goes back on port 20)
    for (var i = 0; i < 200 && Protobot.RobotCode.RobotProgram.Current != null && Protobot.RobotCode.RobotProgram.Current.Status == "Running"; i++) Protobot.RobotCode.RobotProgram.Step(0.01f);
    output.AppendLine("[" + (Protobot.RobotCode.RobotProgram.Current != null ? Protobot.RobotCode.RobotProgram.Current.Status : "none") + "]");
    Protobot.RobotCode.RobotProgram.Stop(null);
}
Protobot.RobotCode.RobotProgram.Printed -= listen;
var cfg = typeof(Protobot.RobotCode.DeviceConfig).GetMethod("AiVisionLines", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
foreach (var lang in new[] { "py", "vex", "pros" }) sb.AppendLine(lang + ": " + cfg.Invoke(null, new object[] { sensor, new System.Collections.Generic.HashSet<string>(), lang, null }));
return sb.ToString() + output;
