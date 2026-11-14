var part = Protobot.PartsManager.GeneratePart("VXPNEUMATICS-Cylinder Mount", new Vector3(500f, 500f, 500f), Quaternion.identity);
var sb = new System.Text.StringBuilder();
foreach (Transform c in part.transform) sb.Append(c.name + " active " + c.gameObject.activeSelf + " hc " + (c.GetComponent<Protobot.HoleCollider>() != null) + " scale " + c.localScale.ToString("F3") + "\n");
UnityEngine.Object.DestroyImmediate(part);
var p2 = Protobot.PartsManager.GeneratePart("PNMT-50mm", new Vector3(500f, 500f, 500f), Quaternion.identity);
sb.Append("pnmt id " + p2.GetComponent<Protobot.SavedObject>().id + "\n");
UnityEngine.Object.DestroyImmediate(p2);
return sb.ToString();
