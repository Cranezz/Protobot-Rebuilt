var mi = typeof(Protobot.Motion.MotionUi).GetMethod("DrawLabels");
mi.Invoke(null, new object[] { Protobot.Motion.MotorDrive.Live(), Protobot.Motion.Controls.Cylinders(), true });
var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
return t.GetMethod("Screenshot").Invoke(null, new object[] { "shot" });
