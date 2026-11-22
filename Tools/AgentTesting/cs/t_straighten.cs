// Sets Sandbox.StraightenOff from PlayerPrefs "t_straighten" ("off" = leave free wheels to their joints)
var off = UnityEngine.PlayerPrefs.GetString("t_straighten", "on") == "off";
typeof(Protobot.Motion.Sandbox).GetField("StraightenOff", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, off);
return "straighten " + (off ? "off" : "on");
