// Play mode: makes sure the home menu restyle exists, then (Temp/menu.txt "home" already done) forces its fade in and
// takes picture "homebuild".
var r = UnityEngine.Object.FindAnyObjectByType<Protobot.Interface.LaunchRestyle>();
var step = UnityEngine.PlayerPrefs.GetInt("t_homeshot_step", 0);
if (step == 0) {
  if (r == null) new UnityEngine.GameObject("Launch Restyle").AddComponent<Protobot.Interface.LaunchRestyle>();
  UnityEngine.PlayerPrefs.SetInt("t_homeshot_step", 1);
  return "made restyle";
}
UnityEngine.PlayerPrefs.SetInt("t_homeshot_step", 0);
foreach (var cg in UnityEngine.GameObject.Find("Launch Canvas").GetComponentsInChildren<UnityEngine.CanvasGroup>(true)) cg.alpha = 1f;
foreach (var t in UnityEngine.GameObject.Find("Launch Canvas").GetComponentsInChildren<UnityEngine.Transform>(true)) if (t.name == "Splash Screen") t.localScale = UnityEngine.Vector3.one;
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("Screenshot").Invoke(null, new object[] { "homebuild" });
return "shot";
