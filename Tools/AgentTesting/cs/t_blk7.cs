var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
var o = (System.Text.StringBuilder)typeof(Protobot.RobotCode.CodePanel).GetField("output", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(panel);
return o.ToString();
