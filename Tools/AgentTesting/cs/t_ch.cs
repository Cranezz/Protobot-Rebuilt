GameObject ch = null; string err = "";
try { ch = Protobot.PartsManager.GeneratePart("CCHL-1x2x1-15", new Vector3(0f, 0.5f, 0f), Quaternion.identity); } catch (System.Exception e) { err = e.Message; }
var t = Protobot.PartsManager.GetPartType("CCHL"); var g = t.GetComponent<Protobot.PartGenerator>();
return "ch " + (ch != null) + " " + err + " p1 " + string.Join("|", g.GetParam1Options()) + " p2 " + string.Join("|", g.GetParam2Options() ?? new System.Collections.Generic.List<string>());
