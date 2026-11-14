var t = System.Type.GetType("Protobot.EditorTools.HiddenGeometry, Assembly-CSharp-Editor");
var sb = new System.Text.StringBuilder();
foreach (var path in new[] { "Assets/ProtobotRebuilt/Models/Electronics/Motor.fbx", "Assets/ProtobotRebuilt/Models/Electronics/5.5WMotor.fbx" }) {
  foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path)) {
    var m = o as UnityEngine.Mesh; if (m == null) continue;
    var copy = UnityEngine.Object.Instantiate(m);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var r = t.GetMethod("Strip").Invoke(null, new object[] { copy });
    sb.Append(path + " / " + m.name + ": " + r + " in " + sw.ElapsedMilliseconds + " ms, readable " + m.isReadable + "\n");
    UnityEngine.Object.DestroyImmediate(copy);
  }
}
return sb.ToString();
