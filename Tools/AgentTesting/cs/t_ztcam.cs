var tube = UnityEngine.Object.FindAnyObjectByType<Protobot.Motion.PneumaticTube>();
var p = tube.nodes[1].Position(tube.transform);
Protobot.PivotCamera.Main.SetTransform(p, new UnityEngine.Vector3(10f, 75f, 0f), -1.2f);
return "cam";
