UnityEditor.AssetDatabase.Refresh();
System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor").GetMethod("Play").Invoke(null, null);
return "playing";
