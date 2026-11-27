// Play mode: removes the intro if it's still up, aims the camera at the test parts near the origin, picture "ret".
foreach (var i in UnityEngine.Object.FindObjectsByType<Protobot.Interface.Intro>(UnityEngine.FindObjectsSortMode.None)) UnityEngine.Object.Destroy(i.gameObject);
var at = new UnityEngine.Vector3(UnityEngine.PlayerPrefs.GetFloat("t_shot_x", 1.5f), UnityEngine.PlayerPrefs.GetFloat("t_shot_y", 0.5f), 0f);
var cam = UnityEngine.Camera.main;
cam.transform.position = at + new UnityEngine.Vector3(0f, UnityEngine.PlayerPrefs.GetFloat("t_shot_up", 2.2f), -UnityEngine.PlayerPrefs.GetFloat("t_shot_back", 2.2f));
cam.transform.LookAt(at);
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
pt.GetMethod("Screenshot").Invoke(null, new object[] { UnityEngine.PlayerPrefs.GetString("t_shot_name", "ret") });
return "shot";
