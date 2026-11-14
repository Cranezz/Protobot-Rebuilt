var t = System.Type.GetType("Protobot.EditorTools.HiddenGeometry, Assembly-CSharp-Editor");
var sw = System.Diagnostics.Stopwatch.StartNew();
var line = (string)t.GetMethod("StripFamily").Invoke(null, new object[] { "Assets/ProtobotRebuilt/VEX Parts/Meshes/VXWHEELS.asset" });
return line + " in " + sw.ElapsedMilliseconds / 1000 + " s";
