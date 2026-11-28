// (Play, simulating) the cascade lift (CascadeLiftRig): what the carriage and each stage 2 body touch, their joints' forces, and each band's spring.
var rig = System.Type.GetType("Protobot.EditorTools.CascadeLiftRig, Assembly-CSharp-Editor");
var P = (System.Collections.Generic.Dictionary<string, GameObject>)rig.GetField("P").GetValue(null);
var inv = System.Globalization.CultureInfo.InvariantCulture;
var sb = new System.Text.StringBuilder();
System.Func<Collider, string> Name = c => { var r = c.GetComponentInParent<MeshRenderer>(); var src = r != null ? Protobot.Motion.Sandbox.SourceOf(r) : null; return src != null ? src.name : c.transform.parent != null ? c.transform.parent.name + "/" + c.name : c.name; };
var bodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
foreach (var key in new[] { "carriage", "R stage 2", "L stage 2" }) {
  var at = Protobot.Motion.Sandbox.SimPoint(P[key], P[key].GetComponent<Renderer>().bounds.center);
  var mine = Protobot.Motion.Sandbox.BodyOf(P[key]);
  if (mine == null) { sb.AppendLine(key + ": no body"); continue; }
  foreach (var j in UnityEngine.Object.FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None)) {
    if (j.GetComponent<Rigidbody>() != mine && j.connectedBody != mine) continue;
    var other = j.GetComponent<Rigidbody>() == mine ? j.connectedBody : j.GetComponent<Rigidbody>();
    sb.AppendLine("  joint " + (j.GetComponent<Rigidbody>() == mine ? "own" : "on") + " to " + (other != null ? other.name + " m" + other.mass.ToString("F2", inv) : "world") + " x" + j.xMotion + " lim " + j.linearLimit.limit.ToString("F2", inv) + " force " + (j.currentForce.magnitude / 39.37f / 4.448f).ToString("F2", inv) + " lbf dir " + j.currentForce.normalized.ToString("F2"));
  }
  sb.AppendLine(key + " body " + mine.name + " mass " + mine.mass.ToString("F3", inv) + " v " + mine.linearVelocity.ToString("F2"));
  var cols = mine.GetComponentsInChildren<Collider>().Where(c => c.attachedRigidbody == mine && !c.isTrigger).ToList();
  foreach (var rb in bodies) {
    if (rb == mine) continue;
    foreach (var o in rb.GetComponentsInChildren<Collider>()) {
      if (o.attachedRigidbody != rb || o.isTrigger) continue;
      foreach (var c in cols) {
        if (Physics.GetIgnoreCollision(c, o)) continue;
        if (!c.bounds.Intersects(o.bounds)) continue;
        if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, o, o.transform.position, o.transform.rotation, out var dir, out var dist) || dist > 0f)
          sb.AppendLine("  " + Name(c) + " x " + Name(o) + " at " + c.bounds.center.ToString("F2") + " / " + o.bounds.center.ToString("F2") + " pen " + dist.ToString("F3", inv) + " dir " + dir.ToString("F2"));
        else {
          var p = o.ClosestPoint(c.bounds.center); var q = c.ClosestPoint(p);
          if ((p - q).magnitude < 0.03f) sb.AppendLine("  near " + c.name + " x " + o.name + " (" + rb.name + ") " + (p - q).magnitude.ToString("F3", inv));
        }
      }
    }
  }
}
foreach (var sj in UnityEngine.Object.FindObjectsByType<SpringJoint>(FindObjectsSortMode.None)) {
  var a = sj.transform.TransformPoint(sj.anchor); var b = sj.connectedBody != null ? sj.connectedBody.transform.TransformPoint(sj.connectedAnchor) : sj.connectedAnchor;
  sb.AppendLine("spring " + sj.name + " spring " + sj.spring.ToString("F1", inv) + " span " + (a - b).magnitude.ToString("F2", inv) + " force " + sj.currentForce.magnitude.ToString("F1", inv));
}
return sb.ToString();
