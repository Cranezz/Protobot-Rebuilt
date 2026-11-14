// A tube wrapping a part's corner (at the origin) from its top face down its side: how close it comes to the corner.
var R = Protobot.Motion.PneumaticTube.Radius;
var sb = new System.Text.StringBuilder();
foreach (var radius in new[] { 0.4f, 0.6f, 2f }) {
  var route = new System.Collections.Generic.List<(Vector3, Vector3, int, float)> {
    (new Vector3(-3f, R, 0f), Vector3.zero, 0, 0f),
    (Vector3.zero, Vector3.up, 3, 0f),
    (new Vector3(R, -3f, 0f), Vector3.zero, 0, 0f) };
  var line = Protobot.Motion.PneumaticTube.BuildLine(route, radius);
  var near = float.MaxValue;
  for (int i = 0; i + 1 < line.Count; i++) {
    var a = line[i]; var b = line[i + 1]; var ab = b - a;
    var t = Mathf.Clamp01(Vector3.Dot(-a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
    near = Mathf.Min(near, (a + ab * t).magnitude);
  }
  sb.Append("bend " + radius + ": closest to the corner " + near.ToString("F3") + " (tube radius " + R.ToString("F3") + ")\n");
}
return sb.ToString();
