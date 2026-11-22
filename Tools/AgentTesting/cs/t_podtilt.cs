// (Play, sandbox running, odometry robot) How each tracking wheel sits now vs as built: its axle's tilt out of the
// chassis' level plane (camber, degrees), the axle's turn in that plane, and the wheel's middle against the frame.
var frame = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(s => s.id == "CCHL-1x2-23").OrderBy(s => s.transform.position.z).Skip(1).First().gameObject;
var rotors = Protobot.Motion.GearTrain.Current();
var sb = "";
foreach (var w in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(o => o.id.Contains("2.75"))) {
    var rotor = rotors.FirstOrDefault(r => r.parts.Contains(w.gameObject));
    if (rotor == null) { sb += w.name + ": no rotor\n"; continue; }
    var axle = rotor.axis.normalized;
    var mid = w.GetComponent<MeshFilter>() != null ? w.transform.TransformPoint(w.GetComponent<MeshFilter>().sharedMesh.bounds.center) : w.transform.position;
    // in the frame's own space, as built and now
    var builtAxle = frame.transform.InverseTransformDirection(axle);
    var builtMid = frame.transform.InverseTransformPoint(mid);
    var simFrameUp = Protobot.Motion.Sandbox.SimDirection(frame, Vector3.up);
    var nowAxleW = Protobot.Motion.Sandbox.SimDirection(w.gameObject, axle);
    var nowMidW = Protobot.Motion.Sandbox.SimPoint(w.gameObject, mid);
    var fr = Matrix4x4.TRS(Protobot.Motion.Sandbox.SimPoint(frame, frame.transform.position), Quaternion.LookRotation(Protobot.Motion.Sandbox.SimDirection(frame, frame.transform.forward), Protobot.Motion.Sandbox.SimDirection(frame, frame.transform.up)), Vector3.one).inverse;
    var nowAxle = fr.MultiplyVector(nowAxleW);
    var nowMid = fr.MultiplyPoint3x4(nowMidW);
    var arm = rotor.bearings.Select(x => x.part).FirstOrDefault(x => x != null && !rotor.parts.Contains(x));
    var armAxle = arm != null ? fr.MultiplyVector(Protobot.Motion.Sandbox.SimDirection(arm, axle)) : Vector3.zero;
    var armMid = arm != null ? fr.MultiplyPoint3x4(Protobot.Motion.Sandbox.SimPoint(arm, mid)) - builtMid : Vector3.zero;
    System.Func<Vector3, Vector3, float> Camber = (a, upLocal) => 90f - Vector3.Angle(a, upLocal);
    var upL = frame.transform.InverseTransformDirection(Vector3.up);
    foreach (var bp in rotor.bearings.Select(x => x.part).Where(x => x != null).Distinct())
        sb += "   bearing " + bp.name + " camber " + (90f - Vector3.Angle(fr.MultiplyVector(Protobot.Motion.Sandbox.SimDirection(bp, axle)), frame.transform.InverseTransformDirection(Vector3.up))).ToString("0.00") + "\n";
    sb += w.name + ": camber built " + Camber(builtAxle, upL).ToString("0.00") + " now " + Camber(nowAxle, upL).ToString("0.00")
        + "  axle turn " + Vector3.SignedAngle(Vector3.ProjectOnPlane(builtAxle, upL), Vector3.ProjectOnPlane(nowAxle, upL), upL).ToString("0.00")
        + "  middle moved " + (nowMid - builtMid).ToString("F3") + "  arm (" + (arm != null ? arm.name : "-") + ") camber " + (arm != null ? (90f - Vector3.Angle(armAxle, upL)).ToString("0.00") : "-") + " its hole moved " + armMid.ToString("F3") + "  frame up vs world " + Vector3.Angle(simFrameUp, Vector3.up).ToString("0.00") + "\n";
}
return sb;
