var code = Protobot.RobotCode.Examples.Fill(Protobot.RobotCode.Examples.All[11].code);
Protobot.RobotCode.CodeStore.Set(code);
Protobot.RobotCode.CodePanel.Close();
Protobot.RobotCode.CodePanel.Open();
var panel = typeof(Protobot.RobotCode.CodePanel).GetField("open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
typeof(Protobot.RobotCode.CodePanel).GetMethod("SetTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new System.Type[] { typeof(int) }, null).Invoke(panel, new object[] { 0 });
return "shown";
