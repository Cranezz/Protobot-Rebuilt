// (Play mode, a build loaded) the code window opened with a new blocks program.
Protobot.RobotCode.CodePanel.Open();
var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
typeof(Protobot.RobotCode.CodePanel).GetMethod("NewBlocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new System.Type[0], null).Invoke(panel, null);
return Protobot.RobotCode.CodeStore.Source.Substring(0, 80);
