// The sim bodies of the tracking wheels and their sensors: mass, and whether Roll straightens them (axleIn)
var sb = Protobot.Motion.Sandbox.Instance;
var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var bodyOf = (System.Collections.IDictionary)typeof(Protobot.Motion.Sandbox).GetField("bodyOf", F).GetValue(sb);
var axleIn = (System.Collections.IDictionary)typeof(Protobot.Motion.Sandbox).GetField("axleIn", F).GetValue(sb);
var outp = "axleIn " + axleIn.Count + "\n";
foreach (var o in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(o => o.id.Contains("2.75") || o.name.Contains("Rotation"))) {
    if (!bodyOf.Contains(o.gameObject)) { outp += o.name + ": no body\n"; continue; }
    var body = bodyOf[o.gameObject];
    var rb = (Rigidbody)body.GetType().GetField("rb").GetValue(body);
    outp += o.name + " (" + o.id + "): body " + rb.name + " mass " + rb.mass.ToString("0.000") + " straightened " + axleIn.Contains(rb) + "\n";
}
return outp;
