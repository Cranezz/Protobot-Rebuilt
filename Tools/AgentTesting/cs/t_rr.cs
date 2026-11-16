UnityEngine.PlayerPrefs.SetFloat("t_slide_rr", float.Parse(System.IO.File.ReadAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/Temp/rr.txt")));
return "rr " + UnityEngine.PlayerPrefs.GetFloat("t_slide_rr");
