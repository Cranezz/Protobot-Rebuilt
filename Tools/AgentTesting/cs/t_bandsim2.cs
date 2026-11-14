var sb = new System.Text.StringBuilder();
if (!Protobot.Motion.Sandbox.Running) { Protobot.Motion.Sandbox.Begin(); return "started " + Protobot.Motion.Sandbox.Running; }
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None))
  if (s0.id != null && s0.id.StartsWith("SNDF")) sb.Append(s0.id + " at " + Protobot.Motion.Sandbox.SimPoint(s0.gameObject, s0.transform.position).ToString("F2") + "\n");
foreach (var band in Protobot.Motion.ElasticBand.All) sb.Append("band: " + band.Describe() + "\n");
return sb.ToString();
