var g = Protobot.Game.SkillsGame.Current; var ft = g.field.transform;
var b0 = new UnityEngine.Bounds(); var b1 = new UnityEngine.Bounds(); bool f0 = true, f1 = true;
var rbs0 = new System.Collections.Generic.List<UnityEngine.Rigidbody>();
foreach (var body in Protobot.Motion.Sandbox.Instance.Bodies()) {
  var tag = body.nodes[0].GetComponentInParent<Protobot.Online.NetPart>(); if (tag == null) continue;
  foreach (var c in body.rb.GetComponentsInChildren<UnityEngine.Collider>()) {
    if (tag.robot == 0) { if (f0) { b0 = c.bounds; f0 = false; } else b0.Encapsulate(c.bounds); }
    else { if (f1) { b1 = c.bounds; f1 = false; } else b1.Encapsulate(c.bounds); }
  }
  if (tag.robot == 0) rbs0.Add(body.rb);
}
var c1 = ft.InverseTransformPoint(b1.center); var c0 = ft.InverseTransformPoint(b0.center);
var half1 = ft.InverseTransformVector(b1.extents); var half0 = ft.InverseTransformVector(b0.extents);
var side = c1.z < 0 ? 1f : -1f;   // toward the field's middle
var target = new UnityEngine.Vector3(c1.x, c0.y, c1.z + side * (UnityEngine.Mathf.Abs(half1.z) + UnityEngine.Mathf.Abs(half0.z) + 0.02f));
var delta = ft.TransformVector(target - c0);
foreach (var rb in rbs0) { rb.isKinematic = true; rb.position += delta; rb.transform.position += delta; }
UnityEngine.Physics.SyncTransforms();
return "blue at field (" + c1.x.ToString("F1") + ", " + c1.z.ToString("F1") + "), red moved beside it to (" + target.x.ToString("F1") + ", " + target.z.ToString("F1") + ")";
