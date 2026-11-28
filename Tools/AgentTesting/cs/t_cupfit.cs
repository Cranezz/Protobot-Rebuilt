// (Play, a game running) How well each Cup's colliders cover its drawn shape: renderer bounds vs collider bounds, and
// how many of the drawn mesh's vertices lie outside every collider by more than 0.05".
var g = Protobot.Game.SkillsGame.Current;
if (g == null) return "no game";
var sb = new System.Text.StringBuilder();
var n = 0;
foreach (var cup in g.Cups) {
    if (n++ >= 2) break;
    var rs = cup.GetComponentsInChildren<MeshRenderer>();
    var cs = cup.GetComponentsInChildren<Collider>();
    var rb = rs[0].bounds; foreach (var r in rs) rb.Encapsulate(r.bounds);
    var cb = cs[0].bounds; foreach (var c in cs) cb.Encapsulate(c.bounds);
    sb.AppendLine("cup " + cup.name + " renderers " + rs.Length + " colliders " + cs.Length + " layer " + cs[0].gameObject.layer);
    sb.AppendLine("  drawn size " + rb.size.ToString("F2") + " center " + rb.center.ToString("F2"));
    sb.AppendLine("  shape size " + cb.size.ToString("F2") + " center " + cb.center.ToString("F2"));
    foreach (var r in rs) sb.AppendLine("  renderer " + r.name + " scale " + r.transform.lossyScale.ToString("F3"));
    var holders = new System.Collections.Generic.HashSet<Transform>(); foreach (var c in cs) holders.Add(c.transform.parent);
    foreach (var h in holders) sb.AppendLine("  holder " + h.name + " scale " + h.lossyScale.ToString("F3"));
    int outside = 0, total = 0, hollow = 0; float worst = 0f;
    var axis = cup.transform.TransformDirection(cup.opaqueDirection).normalized;
    foreach (var r in rs) {
        var mf = r.GetComponent<MeshFilter>(); if (mf == null || !mf.sharedMesh.isReadable) { sb.AppendLine("  (mesh not readable)"); continue; }
        var vs = mf.sharedMesh.vertices;
        for (var i = 0; i < vs.Length; i += 3) {
            var w = r.transform.TransformPoint(vs[i]); total++;
            var inside = false;
            foreach (var c in cs) if ((c.ClosestPoint(w) - w).sqrMagnitude < 1e-6f) { inside = true; break; }
            if (inside) continue;
            var off = w - cup.transform.position; var radial = off - Vector3.Dot(off, axis) * axis;
            if (radial.magnitude < 1e-3f) continue;
            var ray = new Ray(w, -radial.normalized);
            var best = 1e9f;
            foreach (var c in cs) if (c.Raycast(ray, out var hit, 1f)) best = Mathf.Min(best, hit.distance);
            if (best > 1e8f) { hollow++; continue; }   // a vertex of the inside (the hollow): nothing between it and the axis
            if (best > 0.03f) { outside++; worst = Mathf.Max(worst, best); }
        }
    }
    sb.AppendLine("  outside vertices more than 0.03\" outside the shape: " + outside + " of " + total + " (hollow " + hollow + "), worst " + worst.ToString("F2") + "\"");
}
return sb.ToString();
