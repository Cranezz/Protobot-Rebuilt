// Runs the moderation window's Finish deletions (main thread); then t_privacy4 reads the result.
var type = System.Type.GetType("Protobot.EditorTools.LeaderboardModeration, Assembly-CSharp-Editor");
var window = UnityEngine.ScriptableObject.CreateInstance(type);
UnityEngine.PlayerPrefs.SetString("t_priv_window", "made");
type.GetMethod("FinishDeletions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(window, null);
var holder = new UnityEngine.GameObject("t_priv_holder") { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
UnityEditor.EditorApplication.delayCall += () => { };
System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Temp/privacy3.txt"), "");
UnityEditor.EditorApplication.CallbackFunction poll = null;
var until = UnityEditor.EditorApplication.timeSinceStartup + 20;
poll = () => {
    var status = (string)type.GetField("status", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window);
    if (status != null && status.StartsWith("Finished") || UnityEditor.EditorApplication.timeSinceStartup > until) {
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Temp/privacy3.txt"), "status: " + status);
        UnityEditor.EditorApplication.update -= poll;
    }
};
UnityEditor.EditorApplication.update += poll;
return "started";
