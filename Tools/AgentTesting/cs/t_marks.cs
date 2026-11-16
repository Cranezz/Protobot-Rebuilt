var p = Protobot.RobotCode.RobotProgram.Current;
return (p == null ? "none" : p.Status + " t=" + p.Time.ToString("0.0")) + "\n" + UnityEngine.PlayerPrefs.GetString("t_code_marks");
