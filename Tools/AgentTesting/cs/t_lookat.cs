// Play mode: aims the pivot camera at PlayerPrefs t_look "x,y,z,pitch,yaw,distance" and takes picture t_look_name.
foreach (var i in UnityEngine.Object.FindObjectsByType<Protobot.Interface.Intro>(UnityEngine.FindObjectsSortMode.None)) UnityEngine.Object.Destroy(i.gameObject);
var v = UnityEngine.PlayerPrefs.GetString("t_look", "0,0,0,30,0,10").Split(',');
System.Func<int, float> F = i => float.Parse(v[i], System.Globalization.CultureInfo.InvariantCulture);
Protobot.PivotCamera.Main.SetTransform(new UnityEngine.Vector3(F(0), F(1), F(2)), new UnityEngine.Vector3(F(3), F(4), 0f), -F(5));
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
pt.GetMethod("Screenshot").Invoke(null, new object[] { UnityEngine.PlayerPrefs.GetString("t_look_name", "look") });
pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
return "shot";
