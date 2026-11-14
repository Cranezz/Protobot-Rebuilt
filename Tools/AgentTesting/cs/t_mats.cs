var sb = new System.Text.StringBuilder();
var t = Protobot.PartsManager.GetPartType("VXPNEUMATICS");
foreach (var pd in t.GetComponentsInChildren<Protobot.PartData>(true)) {
  var r = pd.GetComponent<MeshRenderer>(); if (r == null) continue;
  sb.Append(pd.name + ": ");
  foreach (var m in r.sharedMaterials) if (m != null) sb.Append(m.name + " #" + ColorUtility.ToHtmlStringRGB(m.color) + " a" + m.color.a.ToString("F2") + "; ");
  sb.Append("\n");
}
return sb.ToString();
