var p = Protobot.RobotCode.RobotProgram.Current;
var brain = typeof(Protobot.RobotCode.RobotProgram).GetField("Brain", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(p);
var canvas = (Protobot.RobotCode.BrainCanvas)brain.GetType().GetField("screen").GetValue(brain);
return string.Join(" | ", canvas.front.texts.Select(t => t.text));
