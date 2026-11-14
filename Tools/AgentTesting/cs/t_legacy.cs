var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var sb = new System.Text.StringBuilder();
var x = 0f;
foreach (var id in new[] { "PNMT-Legacy Double Acting-Normal", "PNMT-Legacy Single Acting-Normal", "PNMT-50mm-Normal" }) {
  var o = Protobot.PartsManager.GeneratePart(id, new Vector3(x, 2f, 0f), Quaternion.identity);
  x += 3f;
  var c = o.GetComponent<Protobot.Motion.PneumaticCylinder>();
  sb.Append(id + " cyl " + (c != null && c.enabled) + " stroke " + (c != null ? c.Stroke.ToString("F2") : "-") + " tip " + (c != null ? c.RodTip.ToString("F2") : "-") + " dir " + (c != null ? c.ExtendDirection.ToString("F2") : "-") + " single " + (c != null && c.SingleActing) + "\n");
  foreach (var h in o.GetComponentsInChildren<Protobot.HoleCollider>(true)) sb.Append("   hole " + h.holeType + " " + h.transform.lossyScale.ToString("F3") + " at " + h.transform.position.ToString("F2") + "\n");
  o.name = "T_" + id;
}
Protobot.PivotCamera.Main.SetTransform(new Vector3(3f, 1f, 0f), new Vector3(25f, 200f, 0f), -9f);
return sb.ToString();
