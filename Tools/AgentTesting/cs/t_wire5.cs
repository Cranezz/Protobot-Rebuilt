// (Play, no game) A Brain and a V5 device of each kind, a Smart Cable from brain ports 1-6 into each device's socket
// (as the routing tool places them), the camera on the devices. Screenshot after.
if (Protobot.Game.SkillsGame.Current != null) Protobot.Game.SkillsGame.Current.Exit();
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var sb = new System.Text.StringBuilder();
var brain = Protobot.PartsManager.GetPartType("BRAN").GetComponent<Protobot.PartGenerator>().Generate(new Vector3(0f, 1f, 0f), Quaternion.identity);
var ids = new[] { "VXSENSORS-GPS Sensor", "VXSENSORS-AI Vision Sensor", "VXSENSORS-Vision Sensor", "VXSENSORS-Inertial Sensor", "VXSENSORS-Distance Sensor", "MOTR-5.5W" };
var devices = new System.Collections.Generic.List<GameObject>();
for (var i = 0; i < ids.Length; i++)
    devices.Add(Protobot.PartsManager.GeneratePart(ids[i], new Vector3(-5f + 2f * i, 1f, 5f), Quaternion.identity));
Physics.SyncTransforms();
var bp = Protobot.Motion.WirePorts.Of(brain);
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var place = typeof(Protobot.Motion.TubeTool).GetMethod("Place", flags);
for (var i = 0; i < devices.Count; i++) {
    var dp = Protobot.Motion.WirePorts.Of(devices[i]);
    if (dp.Count == 0) { sb.AppendLine(ids[i] + ": no socket"); continue; }
    var from = bp.Find(p => p.number == i + 1 && p.kind == Protobot.Motion.WireKind.Smart);
    Protobot.Motion.TubeTool.BeginWire(Protobot.Motion.WireKind.Smart, from);
    var tool = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.TubeTool>();
    var to = dp[0];
    place.Invoke(tool, new object[] { new Protobot.Motion.TubeTarget { valid = true, kind = Protobot.Motion.TubeNode.Port, part = to.part, point = to.Position, dir = to.Out, depth = Protobot.Motion.Wire.PortDepth(Protobot.Motion.WireKind.Smart), what = to.label } });
    sb.AppendLine(ids[i] + ": socket at " + to.Position.ToString("F2") + " out " + to.Out.ToString("F1"));
}
sb.AppendLine("wires " + Protobot.Motion.Wire.Wires.Count);
Protobot.PivotCamera.Main.SetTransform(new Vector3(0f, 1f, 4f), new Vector3(35f, 200f, 0f), -14f);
return sb.ToString();
