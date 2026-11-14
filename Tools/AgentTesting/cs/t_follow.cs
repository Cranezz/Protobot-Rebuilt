var sb = new System.Text.StringBuilder();
GameObject chan = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s0.id != null && s0.id.StartsWith("CCHL")) chan = s0.gameObject;
var tube = Protobot.Motion.PneumaticTube.All[0];
sb.Append("tube length before move " + tube.Length.ToString("F3") + "\n");
chan.transform.position += new Vector3(0f, 0f, 1.5f);
tube.Refresh(false);
sb.Append("after moving the channel 1.5\": " + tube.Length.ToString("F3") + "\n");
// an elastic from an old build: "band:<end>|loops|rest"
var bt = Protobot.PartsManager.GetPartType("BAND").GetComponent<Protobot.PartGenerator>();
bt.param1.value = "B64";
var old = bt.Generate(new Vector3(0f, 3f, 6f), Quaternion.identity);
old.GetComponent<Protobot.SavedObject>().state = "band:4,0,0|2|0;";
return sb.ToString();
