var panel = typeof(Protobot.RobotCode.CodePanel).GetField("open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
typeof(Protobot.RobotCode.CodePanel).GetMethod("SetTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(panel, new object[] { true });
return "tab";
