var lines = Protobot.RobotCode.CodeStore.Source.Replace("\r", "").Split('\n');
var s = "";
for (int i = 58; i < 70 && i < lines.Length; i++) s += (i + 1) + ": " + lines[i] + "\n";
return s;
