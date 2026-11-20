var s = Protobot.RobotCode.CodeStore.Source.Replace("\n", " ");
var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
return "app: " + s + " | panel open " + (panel != null && panel.isActiveAndEnabled) + " | watching " + Protobot.RobotCode.CodeStore.Watching;
