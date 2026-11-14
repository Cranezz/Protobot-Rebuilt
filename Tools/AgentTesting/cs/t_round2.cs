var sb = new System.Text.StringBuilder();
foreach (var b in Protobot.Motion.ElasticBand.All) sb.Append("after: band " + b.kind.key + " x" + b.bands + " " + b.wraps + " wraps, posts " + b.posts.Count + " parts " + string.Join(",", System.Linq.Enumerable.Select(b.posts, p => p.part != null ? p.part.GetComponent<Protobot.SavedObject>().id : "none")) + " : " + b.Describe() + "\n");
foreach (var t in Protobot.Motion.PneumaticTube.All) sb.Append("after: tube " + t.nodes.Count + " " + t.Length.ToString("F3") + " parts " + string.Join(",", System.Linq.Enumerable.Select(t.nodes, n => n.part != null ? n.part.GetComponent<Protobot.SavedObject>().id : "none")) + "\n");
return sb.ToString();
