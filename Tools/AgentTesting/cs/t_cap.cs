// Play mode: saves the Game view to C:/Dev/ProtobotRebuiltV2/Video/notices/<PlayerPrefs t_cap>.png (written at the end of the frame).
var name = UnityEngine.PlayerPrefs.GetString("t_cap", "shot");
System.IO.Directory.CreateDirectory("C:/Dev/ProtobotRebuiltV2/Video/notices");
UnityEngine.ScreenCapture.CaptureScreenshot("C:/Dev/ProtobotRebuiltV2/Video/notices/" + name + ".png");
return "capturing " + name;
