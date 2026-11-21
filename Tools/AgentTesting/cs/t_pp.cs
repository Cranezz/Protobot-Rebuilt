UnityEngine.PlayerPrefs.SetString("t_partholes", "ANGL-3x3-3,PLTE-2-6,SNDF-1.50in");
var g = Protobot.PartsManager.GetPartType("SCRW").GetComponent<Protobot.PartGenerator>();
return string.Join(",", g.GetParam1Options()) + " | nut: " + string.Join(",", Protobot.PartsManager.GetPartType("NUT").GetComponent<Protobot.PartGenerator>().GetParam1Options());
