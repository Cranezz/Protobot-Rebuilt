var v = Protobot.Game.ReplayViewer.Instance;
if (v == null) return "no viewer";
var t = typeof(Protobot.Game.ReplayViewer);
var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
t.GetMethod("Seek", f).Invoke(v, new object[] { 9f });
var shown = 0; foreach (Transform c in v.transform.root) {}
var objs = (UnityEngine.Transform[])t.GetField("objects", f).GetValue(v);
foreach (var o in objs) if (o != null && o.gameObject.activeSelf) shown++;
var meshes = 0; foreach (var o in objs) if (o != null) meshes += o.childCount;
return "ready " + t.GetField("ready", f).GetValue(v) + ", shown " + shown + " of " + objs.Length + ", meshes " + meshes;
