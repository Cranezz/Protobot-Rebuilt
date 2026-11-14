var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
return t.GetMethod("Screenshot").Invoke(null, new object[] { "shot" });
