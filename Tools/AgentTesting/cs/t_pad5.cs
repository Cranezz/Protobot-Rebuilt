var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("Screenshot").Invoke(null, new object[] { "pad_screen" + UnityEngine.PlayerPrefs.GetString("t_pad_with") });
return "shot";
