UnityEngine.PlayerPrefs.SetInt("t_example", int.Parse(System.IO.File.ReadAllText("Temp/t_example.txt").Trim()));
return "set";
