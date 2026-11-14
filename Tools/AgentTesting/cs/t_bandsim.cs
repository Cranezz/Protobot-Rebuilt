var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
Protobot.Motion.ElasticTool.Stop();
Protobot.PartsManager.DestroyLoadedObjects();
var g = Protobot.PartsManager.GetPartType("SNDF").GetComponent<Protobot.PartGenerator>();
g.param1.value = "2.00in";
var a = g.Generate(new Vector3(0f, 1f, 0f), Quaternion.Euler(-90f, 0f, 0f));
var b = g.Generate(new Vector3(6f, 1f, 0f), Quaternion.Euler(-90f, 0f, 0f));
var list = new System.Collections.Generic.List<Protobot.Motion.BandPost>();
foreach (var part in new[] { a, b }) {
  Vector3 origin, axis;
  Protobot.Motion.ElasticTool.PostLine(part, out origin, out axis);
  var ol = part.transform.InverseTransformPoint(origin + axis * 0.6f);
  var al = part.transform.InverseTransformDirection(axis).normalized;
  list.Add(new Protobot.Motion.BandPost { part = part, local = ol, axisLocal = al, radius = 0.144f, what = "test" });
}
var bt = Protobot.PartsManager.GetPartType("BAND").GetComponent<Protobot.PartGenerator>();
bt.param1.value = "V64";
var obj = bt.Generate(list[0].Center, Quaternion.identity);
var band = obj.GetComponent<Protobot.Motion.ElasticBand>();
band.Set(list, 2, 2, 0f, Vector3.up);
System.IO.File.WriteAllText("Temp/bandsim.txt", band.Describe());
Protobot.PivotCamera.Main.SetTransform(new Vector3(3f, 1f, 0f), new Vector3(35f, 0f, 0f), -12f);
return band.Describe();
