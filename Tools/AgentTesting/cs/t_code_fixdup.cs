foreach (var p in UnityEngine.Resources.FindObjectsOfTypeAll<Protobot.RobotCode.CodePanel>()) {
  var f = typeof(Protobot.RobotCode.CodePanel).GetField("field", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(p);
  if (f == null) UnityEngine.Object.Destroy(p.gameObject);
}
return "ok";
