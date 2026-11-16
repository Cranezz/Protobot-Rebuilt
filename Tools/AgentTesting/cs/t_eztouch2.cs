var p = Protobot.RobotCode.RobotProgram.Current;
var brain = p.GetType().GetField("Brain", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(p);
var screen = (Protobot.RobotCode.BrainCanvas)brain.GetType().GetField("screen").GetValue(brain);
var before = UnityEngine.PlayerPrefs.GetInt("V2.Code.EzAuton");
screen.Touch(false, 420, 120);
return "page now " + before + " t=" + p.Time.ToString("0.0");
