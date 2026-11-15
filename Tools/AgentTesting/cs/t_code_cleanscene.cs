var n = 0;
foreach (var p in UnityEngine.Resources.FindObjectsOfTypeAll<Protobot.RobotCode.CodePanel>()) {
  if (UnityEditor.EditorUtility.IsPersistent(p)) continue;
  UnityEngine.Object.DestroyImmediate(p.gameObject); n++;
}
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return "removed " + n + " playing=" + UnityEditor.EditorApplication.isPlaying + " dirty=" + scene.isDirty;
