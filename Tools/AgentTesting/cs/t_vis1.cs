// (Play mode) Vision Sensor: an empty build with a V5 Vision Sensor and two blocks in front of it (red, 24" out;
// blue, to its right), lit by the scene. Makes signature 1 from the red block's box and 2 from the blue one's (as the
// Vision Utility's Set does), keeps them on the sensor, takes a picture and lists what it finds. t_vis2.cs runs
// programs against it. Test objects are named "VisTest" (destroyed on the next run).
if (!UnityEngine.Application.isPlaying) return "not playing";
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
foreach (var old in UnityEngine.Object.FindObjectsByType<Transform>(UnityEngine.FindObjectsSortMode.None).Where(t => t != null && t.name == "VisTest").ToList()) UnityEngine.Object.Destroy(old.gameObject);
Protobot.PartsManager.DestroyLoadedObjects();
var sensorPart = Protobot.PartsManager.GeneratePart("SNSR-Vision-V5", new Vector3(0f, 4f, 0f), Quaternion.identity);
UnityEngine.Physics.SyncTransforms();
var sb = new System.Text.StringBuilder();
var look = Protobot.Devices.SensorFrame.Look(sensorPart);
var origin = sensorPart.transform.TransformPoint(look.origin);
var dir = sensorPart.transform.TransformDirection(look.dir).normalized;
sb.AppendLine("lens " + origin.ToString("F2") + " looks " + dir.ToString("F2"));
System.Func<Color, Vector3, Vector3, GameObject> Block = (color, at, size) => {
    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
    go.name = "VisTest";
    UnityEngine.Object.Destroy(go.GetComponent<Collider>());
    go.transform.position = at;
    go.transform.localScale = size;
    go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    var r = go.GetComponent<Renderer>();
    r.material.color = color;
    return go;
};
var right = Vector3.Cross(Vector3.up, dir).normalized;
if (right.sqrMagnitude < 0.5f) right = Vector3.right;
Block(new Color(0.6f, 0.08f, 0.08f), origin + dir * 24f, new Vector3(4f, 4f, 4f));
Block(new Color(0.08f, 0.15f, 0.6f), origin + dir * 30f + right * 9f, new Vector3(3f, 3f, 3f));
Protobot.Devices.V5Brain.Refresh(true);
var sensor = Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.VisionSensor>().FirstOrDefault();
if (sensor == null) return sb + "no VisionSensor device (devices: " + string.Join(", ", Protobot.Devices.V5Brain.Devices.Select(d => d.kind + "@" + d.port)) + ")";
sensor.PowerOn();
sensor.RenderNow();
var lens = sensor.Lens();
sb.AppendLine("port " + sensor.port + ", picture " + (sensor.pixels != null) + ", lens up " + lens.up.ToString("F2"));
// Where each block lands in the picture.
System.Func<Vector3, Vector2Int> Px = p => {
    var d = p - lens.origin;
    var z = Vector3.Dot(d, lens.look);
    var upv = lens.up;
    var rightv = Vector3.Cross(upv, lens.look);
    var t = Mathf.Tan(23f * Mathf.Deg2Rad);
    var x = 158f + Vector3.Dot(d, rightv) / z / (t * 316f / 212f) * 158f;
    var y = 106f - Vector3.Dot(d, upv) / z / t * 106f;
    return new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
};
var redAt = Px(origin + dir * 24f);
var blueAt = Px(origin + dir * 30f + right * 9f);
sb.AppendLine("red block at px " + redAt + ", blue at " + blueAt);
var c = sensor.pixels[redAt.y * 316 + redAt.x];
int u, v; Protobot.Devices.VisionSensor.UV(c, out u, out v);
sb.AppendLine("red pixel " + c + " u " + u + " v " + v);
var red = Protobot.Devices.VisionSensor.Make(sensor.pixels, new RectInt(redAt.x - 6, redAt.y - 6, 12, 12), 1, "RED_BOX");
var blue = Protobot.Devices.VisionSensor.Make(sensor.pixels, new RectInt(blueAt.x - 4, blueAt.y - 4, 8, 8), 2, "BLUE_BOX");
sb.AppendLine("red sig " + (red.HasValue ? red.Value.Token : "none"));
sb.AppendLine("blue sig " + (blue.HasValue ? blue.Value.Token : "none"));
var sigs = new Protobot.Devices.VisionSignature?[7];
sigs[0] = red; sigs[1] = blue;
Protobot.Devices.VisionSensor.Save(sensorPart, sigs);
sensor.RenderNow();
foreach (var b in sensor.blobs) sb.AppendLine("  found SIG " + b.id + " at " + b.x + "," + b.y + " " + b.width + "x" + b.height + " area " + b.area + (b.code ? " code angle " + b.angle : ""));
UnityEngine.PlayerPrefs.SetString("t_vis_sigs", (red.HasValue ? red.Value.Token : "") + "|" + (blue.HasValue ? blue.Value.Token : ""));
return sb.ToString();
