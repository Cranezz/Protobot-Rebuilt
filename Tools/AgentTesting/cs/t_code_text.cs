var p = typeof(Protobot.RobotCode.CodePanel).GetField("open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
var f = (TMPro.TMP_InputField)typeof(Protobot.RobotCode.CodePanel).GetField("field", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(p);
return f.text.Replace(" ", ".").Replace("\n", "|\n").Replace("\r", "<CR>") + "\ncaret=" + f.caretPosition;
