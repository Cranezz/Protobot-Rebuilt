// (game run on) How far a Toggle's bar could rise in its brackets: in the bar's frame, the lowest bracket point above
// the bar's top within its cross-section, near its ends.
var g = Protobot.Game.SkillsGame.Current; if (g == null) return "no game";
var sb = new System.Text.StringBuilder();
foreach (var t in g.Toggles) {
    var tr = t.transform;
    var bar = t.GetComponentsInChildren<MeshFilter>().First(f => f.name == "Bar");
    var pts = bar.sharedMesh.vertices.Select(v => tr.InverseTransformPoint(bar.transform.TransformPoint(v))).ToList();
    var ax = t.axisLocal;
    float aMin = pts.Min(p => Vector3.Dot(p, ax)), aMax = pts.Max(p => Vector3.Dot(p, ax));
    var side = Vector3.Cross(Vector3.up, ax);
    float sMin = pts.Min(p => Vector3.Dot(p, side)), sMax = pts.Max(p => Vector3.Dot(p, side));
    float top = pts.Max(p => p.y), bottom = pts.Min(p => p.y);
    var best = float.MaxValue; var bestBelow = float.MaxValue;
    foreach (var r in g.field.ToggleRenderers()) {
        if (r == null || r.name.StartsWith("276-9250-023")) continue;
        if (!r.bounds.Intersects(new Bounds(tr.position, Vector3.one * 30f))) continue;
        var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
        foreach (var v in mf.sharedMesh.vertices) {
            var p = tr.InverseTransformPoint(r.transform.TransformPoint(v));
            var a = Vector3.Dot(p, ax); var s = Vector3.Dot(p, side);
            if (a < aMin - 1.5f || a > aMax + 1.5f || s < sMin + 0.1f || s > sMax - 0.1f) continue;
            if (p.y > top + 0.01f) best = Mathf.Min(best, p.y - top);
            if (p.y < bottom - 0.01f) bestBelow = Mathf.Min(bestBelow, bottom - p.y);
        }
    }
    sb.AppendLine(t.quadrant + " bar " + (aMax - aMin).ToString("F2") + " long, " + (top - bottom).ToString("F2") + " tall; room above " + best.ToString("F2") + ", gap below " + bestBelow.ToString("F2"));
}
return sb.ToString();
