var type = Protobot.PartsManager.GetPartType("SCRW"); var g = type.GetComponent<Protobot.PartGenerator>();
return string.Join(" | ", g.GetParam1Options());
