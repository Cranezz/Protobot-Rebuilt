var launch = UnityEngine.GameObject.Find("Launch Canvas");
if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen") { b.onClick.Invoke(); break; }
Protobot.PartsManager.DestroyLoadedObjects();
var names = new[] { "0.29\" Shoulder Screw (0.09\" shoulder)", "3/8\" Shoulder Screw (0.12\" shoulder)", "1/2\" Shoulder Screw (0.04\" shoulder)", "1/2\" Shoulder Screw (0.18\" shoulder)", "1.00\" Shoulder Screw (0.12\" shoulder)", "1.50\" Shoulder Screw (0.18\" shoulder)", "2.00\" Shoulder Screw (0.04\" shoulder)", "2.50\" Shoulder Screw (0.04\" shoulder)" };
var sb = new System.Text.StringBuilder();
for (int i = 0; i < names.Length; i++) {
  var go = Protobot.PartsManager.GeneratePart("VXHARDWARE-" + names[i], new UnityEngine.Vector3(i * 0.6f, 3f, 0f), UnityEngine.Quaternion.Euler(-90f, 0f, 0f));
  var r = go.GetComponent<UnityEngine.MeshRenderer>(); var m = go.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
  var screw = go.GetComponent<Protobot.Screw>();
  sb.Append(names[i].Substring(0, 5) + ": sub " + m.subMeshCount + " mats " + string.Join("+", System.Linq.Enumerable.Select(r.sharedMaterials, x => x.name)) + " screw " + (screw != null) + " | ");
}
Protobot.PivotCamera.Main.SetTransform(new UnityEngine.Vector3(2.1f, 2.5f, 0f), new UnityEngine.Vector3(15f, 0f, 0f), -6f);
return sb.ToString();
