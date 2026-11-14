var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var x = -30f;
foreach (var id in new[] { "OVGL-Red Alliance", "OVGL-Short", "OVGL-Tall", "OVGL-Blue Alliance", "OVPN-Red Blue", "OVPN-Red Yellow", "OVCP", "OVLD-Red", "OVTO-Red side" }) {
  var o = Protobot.PartsManager.GeneratePart(id, new Vector3(x, 0f, 0f), Quaternion.identity);
  x += id.StartsWith("OVGL") ? 14f : 6f;
}
Protobot.PivotCamera.Main.SetTransform(new Vector3(0f, 3f, 0f), new Vector3(30f, 180f, 0f), -48f);
return "ok";
