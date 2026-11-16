var log = new System.Text.StringBuilder();
var f = typeof(Protobot.RobotCode.RobotProgram).GetField("Printed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
return "status: " + (Protobot.RobotCode.RobotProgram.Current == null ? "none" : Protobot.RobotCode.RobotProgram.Current.Status);
