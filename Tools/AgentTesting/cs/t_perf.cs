var panel = UnityEngine.GameObject.Find("Game Setup");
var comp = panel != null ? panel.GetComponents<UnityEngine.MonoBehaviour>() : null;
string builtInfo = "";
if (comp != null) foreach (var c in comp) { var f = c.GetType().GetField("built", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); if (f != null) builtInfo = "built " + f.GetValue(c) + " now " + UnityEngine.Time.unscaledTime; }
return builtInfo + " fps " + (1f / UnityEngine.Time.smoothDeltaTime).ToString("0") + " meshes " + UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>().Length + " objs " + UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(UnityEngine.FindObjectsSortMode.None).Length;
