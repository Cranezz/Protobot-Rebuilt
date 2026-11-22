// Each tracking wheel's free hinge: which body it joins, that body's mass, the joint's anchor and the wheel's lean now
var sb = Protobot.Motion.Sandbox.Instance;
if (sb == null) return "no sandbox";
var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var bodyOf = (System.Collections.IDictionary)typeof(Protobot.Motion.Sandbox).GetField("bodyOf", F).GetValue(sb);
var free = typeof(Protobot.Motion.Sandbox).GetMethod("FreeHinge", F);
var outp = "";
foreach (var o in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(o => o.id.Contains("2.75"))) {
    var body = bodyOf[o.gameObject];
    var rb = (Rigidbody)body.GetType().GetField("rb").GetValue(body);
    var res = free.Invoke(sb, new object[] { rb });
    var joint = (ConfigurableJoint)res.GetType().GetField("Item1").GetValue(res);
    var owner = (bool)res.GetType().GetField("Item2").GetValue(res);
    var other = joint == null ? null : (owner ? joint.connectedBody : joint.GetComponent<Rigidbody>());
    var names = "";
    foreach (System.Collections.DictionaryEntry e in bodyOf) { var b2 = e.Value; var r2 = (Rigidbody)b2.GetType().GetField("rb").GetValue(b2); if (r2 == other) names += ((GameObject)e.Key).name + ","; }
    var joints = rb.GetComponents<ConfigurableJoint>().Length;
    outp += o.name + ": wheel mass " + rb.mass.ToString("0.000") + ", " + joints + " joints on it, free hinge to " + (other == null ? "none" : other.name + " mass " + other.mass.ToString("0.000") + " [" + names + "]") + " owner " + owner + "\n";
}
return outp;
