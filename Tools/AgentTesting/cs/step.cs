var n = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor").GetMethod("Frames").Invoke(null, new object[] { 6 });
return "frame " + n;
