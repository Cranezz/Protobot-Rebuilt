var sb = new System.Text.StringBuilder();
var t = Protobot.PartsManager.GetPartType("NUT");
foreach (var pd in t.GetComponentsInChildren<Protobot.PartData>(true)) { sb.Append("NUT child " + pd.name + " p1 " + pd.param1Value + " p2 " + pd.param2Value + " comps " + string.Join(",", System.Linq.Enumerable.Select(pd.GetComponents<Component>(), c => c.GetType().Name)) + " holes " + pd.GetComponentsInChildren<Protobot.HoleCollider>(true).Length + " size " + pd.GetComponent<MeshFilter>().sharedMesh.bounds.size.ToString("F3") + "\n"); }
var v = Protobot.PartsManager.GetPartType("VXHARDWARE");
foreach (var pd in v.GetComponentsInChildren<Protobot.PartData>(true)) if (pd.name.Contains("Nut")) sb.Append("VX " + pd.name + " size " + pd.GetComponent<MeshFilter>().sharedMesh.bounds.size.ToString("F3") + " center " + pd.GetComponent<MeshFilter>().sharedMesh.bounds.center.ToString("F3") + " holes " + pd.GetComponentsInChildren<Protobot.HoleCollider>(true).Length + "\n");
foreach (var h in t.GetComponentsInChildren<Protobot.HoleCollider>(true)) { sb.Append("nut hole " + h.transform.parent.name + " " + h.holeType + " " + h.transform.localPosition.ToString("F3") + " " + h.transform.localScale.ToString("F3") + "\n"); }
return sb.ToString();
