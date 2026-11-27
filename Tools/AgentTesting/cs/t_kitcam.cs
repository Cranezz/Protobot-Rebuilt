var sb = new System.Text.StringBuilder();
var rotors = Protobot.Motion.GearTrain.Rotors();
Protobot.Motion.Rotor best = null; var bd = 1e9f;
foreach (var r in rotors) { var d = (r.shaft.GetComponent<UnityEngine.Renderer>().bounds.center - new UnityEngine.Vector3(22f, 20f, 0f)).magnitude; if (d < bd) { bd = d; best = r; } }
var sys = Protobot.Motion.GearTrain.SystemOf(best, rotors);
var sl = sys.sliders[0];
sb.AppendLine("lateral " + sl.lateral.ToString("0.000") + " campoint " + sl.camPoint.ToString("0.00"));
var prof = sl.cam.radius; var mn = 9f; var mx = 0f; foreach (var x in prof) if (x > 0) { mn = UnityEngine.Mathf.Min(mn, x); mx = UnityEngine.Mathf.Max(mx, x); }
sb.AppendLine("profile r " + mn.ToString("0.00") + ".." + mx.ToString("0.00"));
string why; var run = Protobot.Motion.MotorRunner.Begin(sys, null, out why);
var y0 = sl.part.transform.position.y;
for (var a = 0; a < 360; a += 30) { sb.Append(a + ":" + (sl.part.transform.position.y - y0).ToString("0.00") + " reach " + Protobot.Motion.GearKit.CamReach(sl).ToString("0.00") + " | "); Protobot.Motion.MotorRunner.Apply(run, 30f); }
Protobot.Motion.MotorRunner.End(run);
return sb.ToString();
