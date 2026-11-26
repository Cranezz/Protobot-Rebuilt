// (Play, sandbox) each tensioner's limit joint
var sb = new System.Text.StringBuilder();
var inst = Protobot.Motion.Sandbox.Instance;
var f = typeof(Protobot.Motion.Sandbox).GetField("tensions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var list = (System.Collections.IList)f.GetValue(inst);
foreach (var st in list) {
    var T = st.GetType();
    System.Func<string, object> G = n => T.GetField(n).GetValue(st);
    var j = (ConfigurableJoint)G("joint"); var arm = (Rigidbody)G("arm"); var frame = (Rigidbody)G("frame");
    var a = arm.transform.TransformPoint(j.anchor); var c = frame.transform.TransformPoint(j.connectedAnchor);
    var piv = frame.transform.TransformPoint((Vector3)G("pivotInFrame"));
    sb.AppendLine("arm " + arm.name + " kin " + arm.isKinematic + " mass " + arm.mass + " frame " + frame.name + " kin " + frame.isKinematic + " connected " + (j.connectedBody == frame) + " motions " + j.xMotion + j.yMotion + j.zMotion);
    sb.AppendLine("  dist " + (a - c).magnitude.ToString("F3") + " limit " + j.linearLimit.limit.ToString("F3") + " reach " + ((float)G("reach")).ToString("F3") + " slack " + ((float)G("slack")).ToString("F3") + " P-pivot " + (a - piv).magnitude.ToString("F3") + " Q-pivot " + (c - piv).magnitude.ToString("F3"));
    foreach (var jj in arm.GetComponents<Joint>()) sb.AppendLine("  joint on arm: " + jj.GetType().Name + " to " + (jj.connectedBody ? jj.connectedBody.name : "null"));
}
return sb.ToString();
