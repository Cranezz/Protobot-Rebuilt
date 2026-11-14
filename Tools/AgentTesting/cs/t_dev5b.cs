var panel = UnityEngine.Object.FindFirstObjectByType<Protobot.Devices.DevicesPanel>();
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var t = typeof(Protobot.Devices.DevicesPanel);
((System.Collections.Generic.HashSet<string>)t.GetField("expanded", flags).GetValue(panel)).Clear();
UnityEngine.GameObject intake = null;
foreach (var m in Protobot.Motion.MotorDrive.Live()) if (m.Label == "Intake") intake = m.gameObject;
Protobot.Devices.DevicesPanel.Focus(intake);
var page = 1;
if (page != 0) {
    var pageField = t.GetField("page", flags);
    pageField.SetValue(panel, System.Enum.ToObject(pageField.FieldType, page));
}
t.GetField("shown", flags).SetValue(panel, null);
return "ok";
