var panel = UnityEngine.Object.FindFirstObjectByType<Protobot.Devices.DevicesPanel>();
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var t = typeof(Protobot.Devices.DevicesPanel);
((System.Collections.Generic.HashSet<string>)t.GetField("expanded", flags).GetValue(panel)).Clear();
t.GetMethod("Resize", flags).Invoke(panel, new object[] { new UnityEngine.Vector2(-0f, 0f), -1, 0 });
t.GetField("shown", flags).SetValue(panel, null);
return "ok";
