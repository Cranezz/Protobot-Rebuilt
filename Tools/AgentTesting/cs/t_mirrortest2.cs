// (Play, after t_tdrive build + t_mirrortest) Mirrors the left side through x = 0 (as the tool on the centre line) and
// compares the copies with the hidden right side: how many land on a right-side part of the same id (within 0.05").
var tags = Protobot.Builds.DemoPlayer.Target;
var rail = tags.First(kv => kv.Key != null && kv.Value == "rail1").Key;
var bars = tags.Where(kv => kv.Key != null && kv.Value.StartsWith("bar")).Select(kv => kv.Key).ToList();
foreach (var b in bars) b.SetActive(false);
var group = Protobot.ConnectedObjExtensions.GetConnectedObjects(rail, true, true).Where(o => o != null && o.GetComponent<Protobot.SavedObject>() != null && o.activeInHierarchy).Distinct().ToList();
var targets = Protobot.Tools.MirrorTool.WithShafts(group);
var before = new HashSet<Protobot.SavedObject>(UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None));
Protobot.Tools.MirrorTool.Mirror(targets, Vector3.right, Vector3.zero, 0f);
var made = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).Where(s => !before.Contains(s)).ToList();
var right = tags.Where(kv => kv.Key != null && kv.Value == "rside").Select(kv => kv.Key.GetComponent<Protobot.SavedObject>()).ToList();
int hit = 0; var off = new List<string>();
foreach (var m in made) {
    var c = m.GetComponent<Renderer>() ? m.GetComponent<Renderer>().bounds.center : m.transform.position;
    var twin = right.FirstOrDefault(r => r.id == m.id && (r.transform.position - m.transform.position).magnitude < 0.05f);
    if (twin != null) hit++; else off.Add(m.id + " at " + m.transform.position);
}
var unmatched = right.Count(r => !made.Any(m => m.id == r.id && (r.transform.position - m.transform.position).magnitude < 0.05f));
return "targets " + targets.Count + ", made " + made.Count + ", on a right-side twin " + hit + ", right side parts with no copy " + unmatched + "\n" + string.Join("\n", off.Take(15));
