// (Play) The Settings window (build 77). Temp/settings.txt: "home off" (closes the home menu), "open N" (section
// 0 Preferences, 1 Graphics, 2 Keys, 3 Controller; "open" = the last one), "click NAME" (a button in Settings),
// "old" (the four old Open()s, each must land in the one window), "close", "state", "shot NAME".
var cmd = System.IO.File.ReadAllText("Temp/settings.txt").Trim();
var W = typeof(Protobot.Interface.SettingsWindow);
System.Func<string> State = () => {
    var dialogs = UnityEngine.Object.FindObjectsByType<Protobot.Interface.Dialog>(FindObjectsSortMode.None);
    var s = "open " + Protobot.Interface.SettingsWindow.IsOpen + " showing " + Protobot.Interface.SettingsWindow.Showing + " dialogs " + dialogs.Length;
    foreach (var d in dialogs) {
        s += " | " + d.name + " pages:";
        foreach (var mb in d.GetComponents<MonoBehaviour>()) if (mb != d) s += " " + mb.GetType().Name + (mb.enabled ? "" : "(off)");
        s += " body " + d.Body.childCount + " footer " + d.Footer.childCount;
    }
    return s;
};
if (cmd == "home off") {
    var launch = GameObject.Find("Launch Canvas");
    if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen" && b.gameObject.activeInHierarchy) { b.onClick.Invoke(); return "closed home"; }
    return "no home";
}
if (cmd == "open") { Protobot.Interface.SettingsWindow.Open(); return State(); }
if (cmd.StartsWith("open ")) { Protobot.Interface.SettingsWindow.Open((Protobot.Interface.SettingsWindow.Section)int.Parse(cmd.Substring(5))); return State(); }
if (cmd == "old") {
    var r = "";
    Protobot.Interface.GraphicsWindow.Open(); r += "graphics: " + State() + "\n";
    Protobot.Tools.KeybindsWindow.Open(); r += "keys: " + State() + "\n";
    Protobot.Devices.ControllerScreen.Open(); r += "controller: " + State() + "\n";
    Protobot.Interface.PreferencesWindow.Open(); r += "prefs: " + State() + "\n";
    return r;
}
if (cmd.StartsWith("click ")) {
    var name = cmd.Substring(6);
    var b = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == name && x.gameObject.activeInHierarchy && x.GetComponentInParent<Protobot.Interface.Dialog>() != null);
    if (b == null) return "no button " + name;
    b.onClick.Invoke();
    return "clicked " + name;
}
if (cmd == "close") { foreach (var d in UnityEngine.Object.FindObjectsByType<Protobot.Interface.Dialog>(FindObjectsSortMode.None)) d.Close(); return "closed"; }
if (cmd.StartsWith("shot ")) {
    var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
    return (string)t.GetMethod("Screenshot").Invoke(null, new object[] { cmd.Substring(5) });
}
return State();
