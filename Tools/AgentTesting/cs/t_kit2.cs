// Play mode, after t_kit1.cs: what GearTrain finds at each rig, then one turn of the first shaft (MotorRunner).
var sb = new System.Text.StringBuilder();
var rotors = Protobot.Motion.GearTrain.Rotors();
var points = new[] { ("bevel", new UnityEngine.Vector3(1.2f, 20f, 0f)), ("worm", new UnityEngine.Vector3(5f, 20f, 0f)), ("rack", new UnityEngine.Vector3(10f, 20.3f, 0f)), ("lead", new UnityEngine.Vector3(15f, 20f, 0f)), ("cam", new UnityEngine.Vector3(22f, 20f, 0f)) };
var turns = UnityEngine.PlayerPrefs.GetFloat("t_kit_turn", 360f);
foreach (var pt in points) { var name = pt.Item1; var at = pt.Item2;
  Protobot.Motion.Rotor best = null; var bd = 1e9f;
  foreach (var r in rotors) { if (r.shaft == null) continue; var d = (r.shaft.GetComponent<UnityEngine.Renderer>().bounds.center - at).magnitude; if (d < bd) { bd = d; best = r; } }
  if (best == null || bd > 1.5f) { sb.AppendLine(name + ": no rotor near (" + bd + ")"); continue; }
  var sys = Protobot.Motion.GearTrain.SystemOf(best, rotors);
  sb.Append(name + ": parts " + string.Join(",", System.Linq.Enumerable.Select(best.parts, p => p.name.Replace("(Clone)", ""))) + " | rotors " + sys.rotors.Count + " jammed " + sys.jammed + " " + sys.jamReason + " backdriven " + sys.backdriven + " blocked " + sys.blocked);
  foreach (var r in sys.rotors) sb.Append(" [" + r.Name + " " + sys.ratio[r].ToString("0.0000") + "]");
  foreach (var sl in sys.sliders) sb.Append(" slider " + sl.kind + " ipr " + sl.inchesPerRadian.ToString("0.0000") + " travel " + sl.low.ToString("0.00") + ".." + sl.high.ToString("0.00") + " parts " + sl.parts.Count + " axis " + sl.axis.ToString("0.0"));
  sb.AppendLine();
  string why;
  var run = Protobot.Motion.MotorRunner.Begin(sys, null, out why);
  if (run == null) { sb.AppendLine("  can't turn: " + why); continue; }
  var before = new System.Collections.Generic.List<UnityEngine.Vector3>();
  foreach (var sl in sys.sliders) before.Add(sl.part.transform.position);
  var done = 0f; for (var i = 0; i < 36; i++) done += Protobot.Motion.MotorRunner.Apply(run, turns / 36f);
  sb.Append("  turned " + done.ToString("0.0"));
  for (var i = 0; i < sys.sliders.Count; i++) sb.Append(" | " + sys.sliders[i].kind + " moved " + (sys.sliders[i].part.transform.position - before[i]).ToString("0.000"));
  foreach (var r in sys.rotors) if (r != best) sb.Append(" | " + r.Name + " now " + r.shaft.transform.eulerAngles.ToString("0"));
  sb.AppendLine();
  Protobot.Motion.MotorRunner.End(run);
}
System.IO.File.WriteAllText("Temp/kit1.txt", sb.ToString());
return sb.ToString();
