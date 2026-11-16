Protobot.RobotCode.CodePanel.Open();
var panel = typeof(Protobot.RobotCode.CodePanel).GetField("open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
typeof(Protobot.RobotCode.CodePanel).GetMethod("SetTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new System.Type[] { typeof(int) }, null).Invoke(panel, new object[] { 2 });
return "graph";
