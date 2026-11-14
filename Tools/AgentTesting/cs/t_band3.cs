var sb = new System.Text.StringBuilder();
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
var point = pt.GetMethod("PointAt", new[] { typeof(Vector3) });
GameObject chan = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s0.id != null && s0.id.StartsWith("CCHL")) chan = s0.gameObject;
Protobot.Motion.ElasticTool.Begin("V64");
var tool = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.ElasticTool>();
var click = typeof(Protobot.Motion.ElasticTool).GetMethod("Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
System.Func<Vector3, bool, string> Hit = (world, shift) => {
  point.Invoke(null, new object[] { world });
  pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
  Protobot.Motion.BandPost post;
  if (!Protobot.Motion.ElasticTool.PostUnderMouse(Camera.main, out post)) return "miss";
  click.Invoke(tool, new object[] { post, shift });
  return post.what + " r " + post.radius.ToString("F3") + " hole " + post.hole + " at " + post.Center.ToString("F2");
};
// quick band: two clicks, the second up the post
sb.Append("1 " + Hit(new Vector3(0f, 1.4f, -0.12f), false) + "\n");
sb.Append("2 " + Hit(new Vector3(5f, 1.4f, -0.12f), false) + "\n");
sb.Append("bands after quick: " + Protobot.Motion.ElasticBand.All.Count + "\n");
// shift: three posts then Enter (Make)
sb.Append("3 " + Hit(new Vector3(0f, 1.8f, -0.12f), true) + "\n");
sb.Append("4 " + Hit(new Vector3(5f, 1.8f, -0.12f), true) + "\n");
sb.Append("5 " + Hit(new Vector3(2.5f, 1.8f, 2.38f), true) + "\n");
typeof(Protobot.Motion.ElasticTool).GetMethod("Make", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(tool, null);
sb.Append("bands after shift: " + Protobot.Motion.ElasticBand.All.Count + "\n");
// a hole in the channel to a standoff
var holes = chan.GetComponentsInChildren<Protobot.HoleCollider>();
var hole = holes[holes.Length / 2];
sb.Append("7 " + Hit(hole.transform.position, false) + "\n");
sb.Append("8 " + Hit(new Vector3(2.5f, 1.2f, 2.38f), false) + "\n");
sb.Append("bands after hole: " + Protobot.Motion.ElasticBand.All.Count + "\n");
foreach (var band in Protobot.Motion.ElasticBand.All) sb.Append("  " + band.posts.Count + " posts: " + band.Describe() + "\n");
UnityEditor.EditorApplication.isPaused = false;
return sb.ToString();
