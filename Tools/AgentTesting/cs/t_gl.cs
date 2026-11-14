var s = ""; foreach (var n in Protobot.Interface.PartsCatalog.Nodes) foreach (var l in n.Leaves()) if (l.name != null && l.name.Contains("T ") && (l.typeId == "GEAR" || l.typeId.Contains("GEAR"))) s += l.typeId + "|" + l.p1 + "|" + l.p2 + "; ";
return s;
