var panel = typeof(Protobot.RobotCode.CodePanel).GetField("open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
var sb = typeof(Protobot.RobotCode.CodePanel).GetField("output", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(panel);
return sb.ToString();
