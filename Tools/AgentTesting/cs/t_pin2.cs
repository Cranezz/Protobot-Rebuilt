var g = Protobot.Game.SkillsGame.Current; var ft = g.field.transform;
var rbs0 = new System.Collections.Generic.List<UnityEngine.Rigidbody>(); var rbs1 = new System.Collections.Generic.HashSet<UnityEngine.Rigidbody>();
foreach (var body in Protobot.Motion.Sandbox.Instance.Bodies()) { var tag = body.nodes[0].GetComponentInParent<Protobot.Online.NetPart>(); if (tag == null) continue; if (tag.robot == 0) rbs0.Add(body.rb); else rbs1.Add(body.rb); }
var c1 = UnityEngine.Vector3.zero; foreach (var r in rbs1) c1 += r.worldCenterOfMass; c1 /= rbs1.Count;
var c0 = UnityEngine.Vector3.zero; foreach (var r in rbs0) c0 += r.worldCenterOfMass; c0 /= rbs0.Count;
var dir = UnityEngine.Vector3.ProjectOnPlane(c1 - c0, UnityEngine.Vector3.up).normalized;
var steps = 0; var touching = false;
for (; steps < 120 && !touching; steps++) {
  foreach (var rb in rbs0) { rb.position += dir * 0.25f; rb.transform.position += dir * 0.25f; }
  UnityEngine.Physics.SyncTransforms();
  foreach (var rb in rbs0) { foreach (var c in rb.GetComponentsInChildren<UnityEngine.Collider>()) { var hits = UnityEngine.Physics.OverlapBox(c.bounds.center, c.bounds.extents, UnityEngine.Quaternion.identity, 1 << 16); foreach (var h in hits) if (h.attachedRigidbody != null && rbs1.Contains(h.attachedRigidbody)) { UnityEngine.Vector3 d; float dist; if (UnityEngine.Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, h, h.transform.position, h.transform.rotation, out d, out dist)) touching = true; } if (touching) break; } if (touching) break; }
}
return "moved " + (steps * 0.25f) + " in, touching " + touching;
