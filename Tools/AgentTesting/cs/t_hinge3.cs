// What touches each tracking wheel's body: every collider of the wheel's body against every collider nearby (overlap
// depth, whether the pair is ignored), and the floor
var sb = Protobot.Motion.Sandbox.Instance;
var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var bodyOf = (System.Collections.IDictionary)typeof(Protobot.Motion.Sandbox).GetField("bodyOf", F).GetValue(sb);
var outp = "";
foreach (var o in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(o => o.id.Contains("2.75"))) {
    var body = bodyOf[o.gameObject];
    var rb = (Rigidbody)body.GetType().GetField("rb").GetValue(body);
    var mine = rb.GetComponentsInChildren<Collider>();
    outp += o.name + ": " + mine.Length + " colliders (" + string.Join(", ", mine.Select(c => c.name).Distinct().Take(6)) + ")\n";
    foreach (var c in mine) {
        var hits = Physics.OverlapBox(c.bounds.center, c.bounds.extents + Vector3.one * 0.05f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits) {
            if (h.attachedRigidbody == rb) continue;
            Vector3 dir; float dist;
            var pen = Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, h, h.transform.position, h.transform.rotation, out dir, out dist);
            if (!pen) continue;
            outp += "   " + c.name + " x " + h.name + " (" + (h.attachedRigidbody != null ? h.attachedRigidbody.name + " " + h.attachedRigidbody.mass.ToString("0.000") : "static") + ", layer " + h.gameObject.layer + "): " + dist.ToString("0.000") + "\" along " + dir.ToString("F2") + (Physics.GetIgnoreCollision(c, h) ? " IGNORED" : "") + "\n";
        }
    }
}
return outp;
