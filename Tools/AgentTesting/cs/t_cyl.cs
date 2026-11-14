var sb = new System.Text.StringBuilder();
var fam = Resources.Load<GameObject>("Part Prefabs/VEX/Pneumatics");
foreach (var d in fam.GetComponentsInChildren<Protobot.PartData>(true)) {
  if (!d.param1Value.Contains("Piston") && !d.param1Value.Contains("Reservoir")) continue;
  var mr = d.GetComponent<MeshRenderer>(); var mf = d.GetComponent<MeshFilter>();
  sb.Append(d.param1Value + " sub " + mf.sharedMesh.subMeshCount + " size " + mf.sharedMesh.bounds.size.ToString("F2") + " mats " + string.Join(",", System.Linq.Enumerable.Select(mr.sharedMaterials, m => m != null ? m.name + (m.mainTexture != null ? "[tex " + m.mainTexture.name + "]" : "") : "null")) + "\n");
}
var pn = Resources.Load<GameObject>("Part Prefabs/4Motion/Pneumatic Cylinder") ?? Resources.Load<GameObject>("Part Prefabs/Pneumatics/Cylinder");
sb.Append("pnmt type " + (Protobot.PartsManager.GetPartType("PNMT") != null ? Protobot.PartsManager.GetPartType("PNMT").name : "null") + "\n");
var gen = Protobot.PartsManager.GetPartType("PNMT").GetComponent<Protobot.PartGenerator>();
sb.Append("gen " + gen.GetType().Name + " p1 " + string.Join("|", gen.GetParam1Options()) + " p2 " + string.Join("|", gen.GetParam2Options()) + "\n");
return sb.ToString();
