var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
var w = Protobot.Motion.Wire.Wires.Find(x => x.kind == Protobot.Motion.WireKind.ThreeWire);
var a = w.nodes[0].Position(w.transform);
Protobot.PivotCamera.Main.SetTransform(a + new UnityEngine.Vector3(0.6f, 0.2f, 0f), new UnityEngine.Vector3(20f, 200f, 0f), -1.1f);
t.GetMethod("Frames").Invoke(null, new object[] { 40 });
return t.GetMethod("Screenshot").Invoke(null, new object[] { "w3" });
