var p = Protobot.RobotCode.RobotProgram.Current;
var brain = typeof(Protobot.RobotCode.RobotProgram).GetField("Brain", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(p);
var canvas = (Protobot.RobotCode.BrainCanvas)brain.GetType().GetField("screen").GetValue(brain);
canvas.Touch(true, 120, 80);
canvas.Touch(false, 120, 80);
return "touched; pressing now " + canvas.Pressing;
