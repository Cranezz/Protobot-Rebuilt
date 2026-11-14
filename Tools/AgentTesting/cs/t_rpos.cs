var g = Protobot.Game.SkillsGame.Current;
var ft = g.field.transform;
var sb = new System.Text.StringBuilder();
foreach (var robot in new[] { 0, 1 }) {
  UnityEngine.Vector3 now = UnityEngine.Vector3.zero, start = UnityEngine.Vector3.zero; int n = 0;
  foreach (var t in UnityEngine.Object.FindObjectsByType<Protobot.Online.NetPart>(UnityEngine.FindObjectsSortMode.None)) {
    if (t.robot != robot) continue;
    now += ft.InverseTransformPoint(Protobot.Motion.Sandbox.SimPoint(t.gameObject, t.transform.position));
    start += ft.InverseTransformPoint(t.transform.position); n++;
  }
  now /= n; start /= n;
  sb.Append("robot " + robot + " start (" + start.x.ToString("F1") + ", " + start.z.ToString("F1") + ") now (" + now.x.ToString("F1") + ", " + now.z.ToString("F1") + ") | ");
}
return sb.ToString();
