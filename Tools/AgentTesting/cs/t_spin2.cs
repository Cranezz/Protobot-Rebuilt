var ch = (GameObject)System.AppDomain.CurrentDomain.GetData("spinPart");
var now = Protobot.Motion.Sandbox.SimDirection(ch, ch.transform.forward); now.y = 0; var was = ch.transform.forward; was.y = 0;
var p = Protobot.Motion.Sandbox.SimPoint(ch, ch.transform.position) - ch.transform.position; p.y = 0;
return "turned " + Vector3.SignedAngle(was, now, Vector3.up).ToString("0.0") + "°, moved " + p.magnitude.ToString("0.0") + "\"";
