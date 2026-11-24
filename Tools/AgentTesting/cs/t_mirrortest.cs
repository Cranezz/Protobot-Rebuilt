// (Play, after t_tdrive build) Hides the right side (tag rside), then lists what the left inner rail's group holds
// (what Mirror copies): counts by id prefix.
var tags = Protobot.Builds.DemoPlayer.Target;
foreach (var kv in tags) if (kv.Key != null && kv.Value == "rside") kv.Key.SetActive(false);
var rail = tags.First(kv => kv.Key != null && kv.Value == "rail1").Key;
var group = Protobot.ConnectedObjExtensions.GetConnectedObjects(rail, true, true).Where(o => o != null && o.GetComponent<Protobot.SavedObject>() != null && o.activeInHierarchy).Distinct().ToList();
var left = tags.Where(kv => kv.Key != null && kv.Value != "rside" && !kv.Value.StartsWith("bar")).Select(kv => kv.Key).ToList();
var missing = left.Where(p => !group.Contains(p)).Select(p => p.GetComponent<Protobot.SavedObject>().id + "(" + tags[p] + ")").GroupBy(x => x).Select(g => g.Key + " x" + g.Count());
var byId = group.Select(o => o.GetComponent<Protobot.SavedObject>().id.Split('-')[0]).GroupBy(x => x).Select(g => g.Key + " " + g.Count());
return "group " + group.Count + " of left " + left.Count + ": " + string.Join(", ", byId) + "\nmissing: " + string.Join(", ", missing);
