var g = Protobot.Game.SkillsGame.Current; var ft = g.field.transform;
var ch = (GameObject)System.AppDomain.CurrentDomain.GetData("spinPart");
var now = Protobot.Motion.Sandbox.SimDirection(ch, ch.transform.forward); now.y = 0; var was = ch.transform.forward; was.y = 0;
Vector3 c = Vector3.zero, s = Vector3.zero; int n = 0;
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) { c += ft.InverseTransformPoint(Protobot.Motion.Sandbox.SimPoint(so.gameObject, so.transform.position)); s += ft.InverseTransformPoint(so.transform.position); n++; }
c /= n; s /= n;
return "state " + g.state + " time " + g.timeLeft.ToString("0") + " | start (" + s.x.ToString("0.0") + ", " + s.z.ToString("0.0") + ") now (" + c.x.ToString("0.0") + ", " + c.z.ToString("0.0") + ") turned " + Vector3.SignedAngle(was, now, Vector3.up).ToString("0") + "°";
