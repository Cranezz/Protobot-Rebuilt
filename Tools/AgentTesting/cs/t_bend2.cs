var sb = new System.Text.StringBuilder();
GameObject sndf = null;
foreach (var o in Protobot.PartsManager.FindLoadedObjects()) { var so = o.GetComponent<Protobot.SavedObject>(); if (so != null && so.id == "SNDF-3/4in" && o.transform.position.x > 0.5f) sndf = o; }
var saved = Protobot.Tools.PartBend.SavedBends(sndf);
foreach (var n in Protobot.GroupManager.Neighbours(sndf)) {
  var s = Protobot.Tools.PartBend.Along(sndf, n.transform.position, saved);
  var set = Protobot.Motion.Dynamics.Fastened(new[] { n }, new System.Collections.Generic.List<GameObject> { sndf }, null, out _);
  sb.Append(n.name + " along " + s.ToString("F3") + " saved " + (n.GetComponent<Protobot.SavedObject>() != null) + " fastened set: " + string.Join(",", System.Linq.Enumerable.Select(set, x => x.name)) + "\n");
}
sb.Append("length " + Protobot.Tools.PartBend.Frame(sndf, Protobot.Tools.PartBend.Straight(sndf)).length);
return sb.ToString();
