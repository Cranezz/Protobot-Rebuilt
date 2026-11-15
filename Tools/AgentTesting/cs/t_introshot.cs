var intro = UnityEngine.GameObject.Find("Intro");
var t = intro != null ? intro.GetComponent<Protobot.Interface.Intro>() : null;
if (t != null) {
  var f = typeof(Protobot.Interface.Intro).GetField("time", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
  f.SetValue(t, TIME);
  t.enabled = false;
  typeof(Protobot.Interface.Intro).GetMethod("Apply", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(t, new object[] { TIME });
}
return (t != null ? "intro at TIME" : "no intro") + " " + System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor").GetMethod("Screenshot").Invoke(null, new object[] { "intro" });
