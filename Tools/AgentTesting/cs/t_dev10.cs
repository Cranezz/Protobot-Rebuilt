var sb = new System.Text.StringBuilder();
foreach (var d in Protobot.Devices.V5Brain.Devices) {
    var s = d as Protobot.Devices.ShaftSensor; if (s == null) continue;
    var f = typeof(Protobot.Devices.ShaftSensor).GetField("shaft", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var shaft = (UnityEngine.GameObject)f.GetValue(s);
    sb.AppendLine("shaft " + (shaft != null ? shaft.name : "null") + " simulated shaft " + Protobot.Motion.Sandbox.Simulated(shaft) + " sensor " + Protobot.Motion.Sandbox.Simulated(s.part));
    var rbS = Protobot.Motion.Sandbox.BodyOf(shaft); var rbP = Protobot.Motion.Sandbox.BodyOf(s.part);
    sb.AppendLine("shaft rb " + (rbS != null ? rbS.name + " w " + rbS.angularVelocity : "null") + " sensor rb " + (rbP != null ? rbP.name + " w " + rbP.angularVelocity : "null") + " same " + (rbS == rbP));
}
return sb.ToString();
