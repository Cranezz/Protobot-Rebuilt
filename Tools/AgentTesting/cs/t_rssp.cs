var sb = new System.Text.StringBuilder();
var x = 0f;
foreach (var k in Protobot.Motion.RoboSourceSpacers.All) {
  var p = Protobot.PartsManager.GeneratePart("RSSP-" + k.key, new UnityEngine.Vector3(x, 0.5f, -6f), UnityEngine.Quaternion.identity);
  x += 1.2f;
  var g = Protobot.PartsManager.GetPartType("RSSP").GetComponent<Protobot.PartGenerator>();
  g.param1.value = k.key;
  Protobot.PartData pd; var has = g.TryGetPartData(out pd);
  Protobot.AxlePlaceDisplacement.Axle axle; var round = has && Protobot.AxlePlaceDisplacement.AxleOf(pd.gameObject, out axle);
  sb.AppendLine(p.GetComponent<Protobot.SavedObject>().id + " holes " + p.GetComponentsInChildren<Protobot.HoleCollider>().Length + " grams " + p.GetComponent<Parts_List.PartName>().weightInGrams.ToString("0.00") + " template " + has + " slides on screws " + round);
}
var n = 0; foreach (var root in Protobot.Interface.PartsCatalog.Nodes) foreach (var l in root.Leaves()) if (l.typeId == "RSSP") n++;
sb.AppendLine("catalog leaves " + n);
return sb.ToString();
