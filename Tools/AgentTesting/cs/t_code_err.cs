if (Protobot.Game.SkillsGame.Active) Protobot.Game.SkillsGame.Current.Exit();
var code = "from vex import *\n\nleft = Motor(Ports.PORT4)\n\ndef go(speed):\n    left.spin(FORWARD, speed, PERCENT)\n    wait(200, MSEC)\n    return speed / count\n\nprint('starting')\ngo(50)\n";
Protobot.RobotCode.CodeStore.Set(code);
Protobot.RobotCode.CodePanel.Close();
Protobot.RobotCode.CodePanel.Open();
var panel = UnityEngine.Object.FindFirstObjectByType<Protobot.RobotCode.CodePanel>();
typeof(Protobot.RobotCode.CodePanel).GetMethod("Run", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(panel, null);
return "ran";
