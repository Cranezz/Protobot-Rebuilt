// Temp/apply.txt: "bands n", "kind KEY", "undo". Reports the band.
var f = System.IO.File.ReadAllText("Temp/apply.txt").Trim().Split(' ');
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
Protobot.Motion.ElasticBand band = null;
foreach (var b in Protobot.Motion.ElasticBand.All) if (b.gameObject.layer != 12) band = b;
var ed = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.BandEditor>();
var t = typeof(Protobot.Motion.BandEditor);
t.GetField("band", flags).SetValue(ed, band);
var posts = new System.Collections.Generic.List<Protobot.Motion.BandPost>();
foreach (var p in band.posts) posts.Add(p.Copy());
var apply = t.GetMethod("Apply", flags);
if (f[0] == "bands") apply.Invoke(ed, new object[] { posts, null, int.Parse(f[1]), -1 });
if (f[0] == "kind") apply.Invoke(ed, new object[] { posts, Protobot.Motion.ElasticKind.Of(f[1]), -1, -1 });
if (f[0] == "undo") Protobot.StateSystems.StateSystem.instance.Undo();
var r = band.GetComponent<Renderer>();
return f[0] + ": " + band.kind.key + " id " + band.GetComponent<Protobot.SavedObject>().id + " x" + band.bands + " posts " + band.posts.Count + " color " + (r != null ? r.sharedMaterial.color.ToString("F2") : "") + " | " + band.Describe();
