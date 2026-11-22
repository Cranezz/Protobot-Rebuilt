// The straightener's view: angle between the wheel's axle and the pod's (by axleIn), and each against the build's axle
var sb = Protobot.Motion.Sandbox.Instance;
var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var bodyOf = (System.Collections.IDictionary)typeof(Protobot.Motion.Sandbox).GetField("bodyOf", F).GetValue(sb);
var axleIn = (System.Collections.IDictionary)typeof(Protobot.Motion.Sandbox).GetField("axleIn", F).GetValue(sb);
var hingeAxle = (System.Collections.IDictionary)typeof(Protobot.Motion.Sandbox).GetField("hingeAxle", F).GetValue(sb);
var free = typeof(Protobot.Motion.Sandbox).GetMethod("FreeHinge", F);
var rotors = Protobot.Motion.GearTrain.Current();
var outp = "hingeAxle " + hingeAxle.Count + "\n";
foreach (var o in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(o => o.id.Contains("2.75"))) {
    var body = bodyOf[o.gameObject];
    var rb = (Rigidbody)body.GetType().GetField("rb").GetValue(body);
    var res = free.Invoke(sb, new object[] { rb });
    var joint = (ConfigurableJoint)res.GetType().GetField("Item1").GetValue(res);
    var pod = joint.GetComponent<Rigidbody>() == rb ? joint.connectedBody : joint.GetComponent<Rigidbody>();
    var ax = axleIn[rb];
    var inFrame = (Vector3)ax.GetType().GetField("Item1").GetValue(ax);
    var inWheel = (Vector3)ax.GetType().GetField("Item2").GetValue(ax);
    var podAxle = pod.rotation * inFrame; var wheelAxle = rb.rotation * inWheel;
    var rotor = rotors.First(r => r.parts.Contains(o.gameObject));
    var simAxle = Protobot.Motion.Sandbox.SimDirection(o.gameObject, rotor.axis);
    var podSim = Protobot.Motion.Sandbox.SimDirection(rotor.bearings.Select(b => b.part).First(p => p.name.Contains("Rotation")), rotor.axis);
    var jointAxisW = joint.transform.TransformDirection(joint.axis);
    outp += o.name + ": wheel vs pod (straightener) " + Vector3.Angle(wheelAxle, podAxle).ToString("0.00") + "  wheel(straightener) vs wheel(build) " + Mathf.Min(Vector3.Angle(wheelAxle, simAxle), Vector3.Angle(-wheelAxle, simAxle)).ToString("0.00")
        + "  pod(straightener) vs pod(build) " + Mathf.Min(Vector3.Angle(podAxle, podSim), Vector3.Angle(-podAxle, podSim)).ToString("0.00") + "  wheel vs pod (build) " + Mathf.Min(Vector3.Angle(simAxle, podSim), Vector3.Angle(-simAxle, podSim)).ToString("0.00")
        + "  joint axis vs wheel(build) " + Mathf.Min(Vector3.Angle(jointAxisW, simAxle), Vector3.Angle(-jointAxisW, simAxle)).ToString("0.00") + "\n";
}
return outp;
