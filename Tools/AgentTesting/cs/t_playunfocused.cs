// Play mode starts without focusing the Game view (Unity's "Play Unfocused"), so the editor doesn't jump to the front.
var t = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.PlayModeWindow");
var set = t != null ? t.GetMethod("SetPlayModeFocused", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) : null;
var get = t != null ? t.GetMethod("GetPlayModeFocused", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) : null;
if (set == null) return "no PlayModeWindow.SetPlayModeFocused in this Unity";
set.Invoke(null, new object[] { false });
return "play focused now: " + (get != null ? get.Invoke(null, null) : "?");
