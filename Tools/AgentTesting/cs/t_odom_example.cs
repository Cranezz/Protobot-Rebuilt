// (Play, the odometry robot) The Odometry and PROS + LemLib examples filled for this robot: their tracking-wheel lines.
var sb = new System.Text.StringBuilder();
foreach (var e in Protobot.RobotCode.Examples.All.Where(x => x.name.Contains("dometry") || x.name.Contains("LemLib"))) {
    var code = Protobot.RobotCode.Examples.Fill(e.code);
    sb.AppendLine("== " + e.name);
    foreach (var l in code.Replace("\r\n", "\n").Split('\n')) if (l.Contains("racking") || l.Contains("OdomSensors") || l.Contains("_rotation") || l.Contains("_wheel")) sb.AppendLine(l);
}
return sb.ToString();
