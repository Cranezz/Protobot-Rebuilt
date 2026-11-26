var t = Protobot.Game.SkillsGame.Current.Toggles[0]; var p = GameObject.Find("Test Pusher"); if (p == null) return "no box";
var bar = t.GetComponentInChildren<Collider>(); var box = p.GetComponent<Collider>();
var pen = Physics.ComputePenetration(box, box.transform.position, box.transform.rotation, bar, bar.transform.position, bar.transform.rotation, out var dir, out var dist);
var mc = bar as MeshCollider;
return "ignored " + Physics.GetIgnoreCollision(box, bar) + " pen " + pen + " " + dist.ToString("F2") + " convex " + (mc != null && mc.convex) + " verts " + (mc != null && mc.sharedMesh != null ? mc.sharedMesh.vertexCount : -1) + " box " + (p.transform.position - t.transform.TransformPoint(t.pivotLocal)).ToString("F2") + " barScale " + bar.transform.lossyScale + " meshBounds " + (mc != null ? mc.sharedMesh.bounds.size.ToString() : "");
