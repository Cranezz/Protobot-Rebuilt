var t = Protobot.PartsManager.GetPartType("SCRW");
var g = t.GetComponent<Protobot.PartGenerator>();
return string.Join(" | ", g.GetParam1Options()) + " || " + string.Join(" | ", g.GetParam2Options());
