// Play mode: pictures of the row from t_kitrow (z = 5): kit_a (first half), kit_b (second half), from the front.
foreach (var i in UnityEngine.Object.FindObjectsByType<Protobot.Interface.Intro>(UnityEngine.FindObjectsSortMode.None)) UnityEngine.Object.Destroy(i.gameObject);
var cam = UnityEngine.Camera.main;
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
var x = UnityEngine.PlayerPrefs.GetFloat("t_kit_x", 50f);
var halves = new[] { (0f, x / 2f - 2.5f, "kit_a"), (x / 2f, x - 2.5f, "kit_b") };
foreach (var h in halves) {
  var mid = (h.Item1 + h.Item2) / 2f;
  var width = h.Item2 - h.Item1 + 3f;
  var at = new UnityEngine.Vector3(mid, 1.5f, 5f);
  cam.transform.position = at + new UnityEngine.Vector3(0f, width * 0.45f, -width * 0.75f);
  cam.transform.LookAt(at);
  pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
  pt.GetMethod("Screenshot").Invoke(null, new object[] { h.Item3 });
}
return "shot";
