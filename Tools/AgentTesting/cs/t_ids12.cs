var g = Protobot.PartsManager.GetPartType("VXMISCELLANEOUS").GetComponent<Protobot.PartGenerator>();
return string.Join(" | ", g.GetParam1Options().Where(o => o.Contains("License")));
