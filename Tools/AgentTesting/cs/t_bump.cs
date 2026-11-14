System.Type.GetType("Protobot.EditorTools.BuildNumberStep, Assembly-CSharp-Editor").GetMethod("Next").Invoke(null, null);
return System.IO.File.ReadAllText("Assets/ProtobotRebuilt/Resources/Version/build.txt") + " / " + UnityEditor.PlayerSettings.bundleVersion;
