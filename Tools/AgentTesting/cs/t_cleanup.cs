var t = System.Type.GetType("Protobot.EditorTools.ModelCleanup, Assembly-CSharp-Editor");
return (string)t.GetMethod("Motor").Invoke(null, null) + " | " + (string)t.GetMethod("LongScrews").Invoke(null, null);
