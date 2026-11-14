var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var sb = new System.Text.StringBuilder();
var t = Protobot.PartsManager.GetPartType("SNDF");
var g = t.GetComponent<Protobot.PartGenerator>();
sb.Append("SNDF opts " + string.Join(",", g.GetParam1Options()) + " | " + string.Join(",", g.GetParam2Options()) + "\n");
g.param1.value = g.GetParam1Options()[System.Math.Min(4, g.GetParam1Options().Count - 1)];
var posts = new System.Collections.Generic.List<GameObject>();
foreach (var x in new[] { 0f, 4f, 2f }) {
  var o = g.Generate(new Vector3(x, 1f, x == 2f ? 3f : 0f), Quaternion.Euler(-90f, 0f, 0f));
  posts.Add(o);
  Vector3 origin, axis;
  var ok = Protobot.Motion.ElasticTool.PostLine(o, out origin, out axis);
  sb.Append(o.name + " id " + o.GetComponent<Protobot.SavedObject>().id + " line " + ok + " axis " + axis.ToString("F2") + " origin " + origin.ToString("F2") + "\n");
}
// a band round the first two (built as the tool does)
System.Func<GameObject, Protobot.Motion.BandPost> Post = part => {
  Vector3 origin, axis;
  Protobot.Motion.ElasticTool.PostLine(part, out origin, out axis);
  var tr = part.transform;
  var ol = tr.InverseTransformPoint(origin); var al = tr.InverseTransformDirection(axis).normalized;
  var r = Protobot.Motion.Strands.RadiusAt(part, ol, al, 0f);
  return new Protobot.Motion.BandPost { part = part, local = ol, axisLocal = al, radius = r, what = "test" };
};
var keys = new[] { "V64", "RS117", "LT" };
var sets = new[] { new[] { 0, 1 }, new[] { 0, 1, 2 }, new[] { 0, 2 } };
for (var k = 0; k < 3; k++) {
  var key = keys[k];
  var bt = Protobot.PartsManager.GetPartType("BAND").GetComponent<Protobot.PartGenerator>();
  bt.param1.value = key;
  var list = new System.Collections.Generic.List<Protobot.Motion.BandPost>();
  foreach (var i in sets[k]) list.Add(Post(posts[i]));
  var obj = bt.Generate(list[0].Center, Quaternion.identity);
  var band = obj.GetComponent<Protobot.Motion.ElasticBand>();
  band.Set(list, 1, 1, key == "LT" ? 6f : 0f, Vector3.up);
  sb.Append(key + " radius " + list[0].radius.ToString("F3") + " valid " + (band.Shape != null && band.Shape.Valid) + " len " + (band.Shape != null ? band.Shape.length.ToString("F2") : "-") + " normal " + (band.Shape != null ? band.Shape.normal.ToString("F2") : "") + " : " + band.Describe() + " grams " + band.Grams.ToString("F2") + " | token " + Protobot.Motion.Dynamics.Token(obj, "loop") + "\n");
}
Protobot.PivotCamera.Main.SetTransform(new Vector3(2f, 1.5f, 1.2f), new Vector3(50f, 200f, 0f), -9f);
return sb.ToString();
