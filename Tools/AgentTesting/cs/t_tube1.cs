if (Protobot.Motion.Sandbox.Running) Protobot.Motion.Sandbox.Stop();
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var vx = Protobot.PartsManager.GetPartType("VXPNEUMATICS").GetComponent<Protobot.PartGenerator>();
vx.param1.value = "Straight Pneumatic Fitting";
var f1 = vx.Generate(new Vector3(0f, 0.5f, 0f), Quaternion.Euler(-90f, 0f, 0f));
vx.param1.value = "Elbow Fitting";
var f2 = vx.Generate(new Vector3(8f, 0.5f, 0f), Quaternion.Euler(-90f, 0f, 0f));
var ch = Protobot.PartsManager.GetPartType("CCHL").GetComponent<Protobot.PartGenerator>();
ch.param1.value = ch.GetParam1Options()[0];
var chan = ch.Generate(new Vector3(4f, 0f, 2f), Quaternion.identity);
Protobot.PivotCamera.Main.SetTransform(new Vector3(4f, 1f, 1f), new Vector3(50f, 0f, 0f), -14f);
var sb = new System.Text.StringBuilder();
foreach (var f in new[] { f1, f2 }) {
  var port = Protobot.Motion.TubeTool.PortNear(f, f.transform.position);
  sb.Append(f.GetComponent<Protobot.SavedObject>().id + " port " + (port != null ? port.transform.position.ToString("F2") : "none") + "\n");
}
return sb.ToString();
