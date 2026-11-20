var sb = new System.Text.StringBuilder();
sb.Append("playing " + UnityEditor.EditorApplication.isPlaying + "\n");
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.Append("scene dirty " + scene.isDirty + "\n");
foreach (var p in UnityEngine.Resources.FindObjectsOfTypeAll<Protobot.RobotCode.CodePanel>()) sb.Append("panel " + p.gameObject.name + " scene=" + p.gameObject.scene.name + " path=" + (p.transform.parent ? p.transform.parent.name : "-") + " persistent=" + UnityEditor.EditorUtility.IsPersistent(p) + "\n");
return sb.ToString();
