var s = "";
foreach (var p in UnityEngine.Resources.FindObjectsOfTypeAll<Protobot.RobotCode.CodePanel>()) {
  var f = typeof(Protobot.RobotCode.CodePanel).GetField("field", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(p);
  s += p.gameObject.name + " id=" + p.GetInstanceID() + " field=" + (f != null) + " scene=" + p.gameObject.scene.name + "\n";
}
var open = typeof(Protobot.RobotCode.CodePanel).GetField("open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null) as UnityEngine.Object;
return s + "open=" + (open != null ? open.GetInstanceID().ToString() : "null") + " dirty=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;
