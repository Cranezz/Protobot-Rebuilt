// Frames brain port Temp/frame.txt ("11", "21", "A") from outside it.
var key = System.IO.File.ReadAllText("Temp/frame.txt").Trim();
GameObject brain = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s0.id == "BRAN") brain = s0.gameObject;
var port = Protobot.Motion.WirePorts.Of(brain).Find(p => p.label == "Port " + key);
var cam = Protobot.PivotCamera.Main;
var o = port.Out;
var yaw = Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg;
cam.SetTransform(port.Position, new Vector3(20f, yaw, 0f), -2.2f);
return "port " + port.label + " at " + port.Position.ToString("F2") + " out " + o.ToString("F2") + " yaw " + yaw;
