// (Play) The home menu from scripts. Temp/menu.txt: "home" (leaves a game, opens the home menu), "click NAME" (presses
// the first active button of that name anywhere), "list" (the home menu's buttons and texts), "shot NAME" (a
// screenshot to Assets/Screenshots/NAME.png at the end of the next frame: step a frame after).
var cmd = System.IO.File.ReadAllText("Temp/menu.txt").Trim();
if (cmd == "home") {
    if (Protobot.Game.SkillsGame.Current != null) Protobot.Game.SkillsGame.Current.Exit();
    var b = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>().FirstOrDefault(x => x.name == "Splash Screen Button" && x.gameObject.scene.IsValid());
    if (b == null) return "no splash button";
    b.onClick.Invoke();
    return "home";
}
if (cmd.StartsWith("click ")) {
    var name = cmd.Substring(6);
    var b = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == name && x.gameObject.activeInHierarchy);
    if (b == null) return "no button " + name;
    b.onClick.Invoke();
    return "clicked " + name;
}
if (cmd.StartsWith("shot ")) {
    var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
    return (string)t.GetMethod("Screenshot").Invoke(null, new object[] { cmd.Substring(5) });
}
var sb = new System.Text.StringBuilder();
var launch = GameObject.Find("Launch Canvas");
if (launch == null) return "no launch canvas";
foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>()) sb.AppendLine("button " + b.name + " (" + b.transform.parent.name + ")");
foreach (var t in launch.GetComponentsInChildren<TMPro.TMP_Text>()) if (t.text.Length > 0) sb.AppendLine("text " + t.name + ": " + t.text.Substring(0, Mathf.Min(80, t.text.Length)).Replace("\n", " / "));
return sb.ToString();
