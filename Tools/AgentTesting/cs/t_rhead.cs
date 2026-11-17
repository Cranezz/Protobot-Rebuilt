// Each robot's turn since the start (its first tagged part's forward, in the simulation vs the build).
var sb = new System.Text.StringBuilder();
foreach (var robot in new[] { 0, 1 }) {
  var t = UnityEngine.Object.FindObjectsByType<Protobot.Online.NetPart>(UnityEngine.FindObjectsSortMode.None).Where(n => n.robot == robot).Where(n => n.GetComponent<Protobot.SavedObject>() != null && n.GetComponent<Protobot.SavedObject>().id.StartsWith("CC")).OrderBy(n => n.index).First();
  var now = Protobot.Motion.Sandbox.SimDirection(t.gameObject, t.transform.forward); now.y = 0;
  var was = t.transform.forward; was.y = 0;
  sb.Append("robot " + robot + " turned " + Vector3.SignedAngle(was, now, Vector3.up).ToString("0") + "° | ");
}
return sb.ToString();
