var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var sb = new System.Text.StringBuilder();
var z = 0f;
foreach (var size in new[] { "25mm", "50mm", "75mm", "Legacy Double Acting" }) {
  var type = Protobot.PartsManager.GetPartType("PNMT");
  var gen = type.GetComponent<Protobot.PartGenerator>();
  gen.param1.value = size; gen.param2.value = "Normal";
  var o = gen.Generate(new Vector3(0f, 1f, z), Quaternion.identity);
  z += 1.2f;
  var mf = o.GetComponent<MeshFilter>();
  sb.Append(size + " body " + mf.sharedMesh.name + " mats " + string.Join(",", System.Linq.Enumerable.Select(o.GetComponent<MeshRenderer>().sharedMaterials, m => m.name.Substring(0, System.Math.Min(12, m.name.Length)))) + " size " + mf.sharedMesh.bounds.size.ToString("F2") + "\n");
}
Protobot.PivotCamera.Main.SetTransform(new Vector3(0f, 1f, 1.8f), new Vector3(35f, 200f, 0f), -7f);
return sb.ToString();
