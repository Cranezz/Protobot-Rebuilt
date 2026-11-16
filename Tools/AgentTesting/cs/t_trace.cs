var sb = new System.Text.StringBuilder();
foreach (var s in Protobot.RobotCode.TuningData.All) {
  if (!s.name.Contains("position")) continue;
  sb.Append(s.name + ": ");
  for (var i = System.Math.Max(0, s.count - 40); i < s.count; i += 2) { var p = s.At(i); sb.Append(p.v.ToString("0.0") + " "); }
  sb.AppendLine();
  break;
}
foreach (var s in Protobot.RobotCode.TuningData.All) {
  if (!s.name.Contains("target position")) continue;
  var p = s.At(s.count - 1); sb.AppendLine(s.name + " last " + p.v.ToString("0.0")); break;
}
return sb.ToString();
