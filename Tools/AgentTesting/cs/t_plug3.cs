var sb = new System.Text.StringBuilder();
foreach (var n in new[] { "PLUG", "ELBOW" }) { var o = GameObject.Find(n); var s = o.GetComponent<Protobot.Screw>();
  sb.Append(n + " screw " + (s != null) + " found " + (s != null && s.holeDetector != null ? s.holeDetector.TargetHoleFound.ToString() : "-") + " neighbours " + string.Join(",", System.Linq.Enumerable.Select(Protobot.GroupManager.Neighbours(o), x => x.name)) + "\n"); }
return sb.ToString();
