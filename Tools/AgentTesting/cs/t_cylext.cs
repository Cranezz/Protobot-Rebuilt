var m = typeof(Protobot.Motion.PneumaticCylinder).GetMethod("SetInstant", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new[] { typeof(float) }, null);
foreach (var c in UnityEngine.Object.FindObjectsByType<Protobot.Motion.PneumaticCylinder>(FindObjectsSortMode.None)) m.Invoke(c, new object[] { 1f });
Protobot.PivotCamera.Main.SetTransform(new Vector3(-1.5f, 1f, 1.8f), new Vector3(20f, 160f, 0f), -6f);
return "ok";
