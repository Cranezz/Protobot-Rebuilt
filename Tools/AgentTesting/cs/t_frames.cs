var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
t.GetMethod("Frames").Invoke(null, new object[] { 4 });
return t.GetMethod("Screenshot").Invoke(null, new object[] { "shot" });
