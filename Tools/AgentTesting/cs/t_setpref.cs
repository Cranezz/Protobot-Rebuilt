// Sets a PlayerPrefs string from Temp/setpref.txt ("key=value").
var t = System.IO.File.ReadAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Temp/setpref.txt").Trim();
var i = t.IndexOf('=');
UnityEngine.PlayerPrefs.SetString(t.Substring(0, i), t.Substring(i + 1));
return t;
