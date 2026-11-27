// Play mode: Temp/answer.txt = "type TEXT" (fills the notice card's answer box) or "send" (presses Send) or "ok" (presses OK).
var cmd = System.IO.File.ReadAllText("Temp/answer.txt");
var notice = UnityEngine.GameObject.Find("Notice");
if (notice == null) return "no card";
if (cmd.StartsWith("type ")) {
    var field = notice.GetComponentInChildren<TMPro.TMP_InputField>();
    field.text = cmd.Substring(5);
    return "typed " + field.text.Length;
}
foreach (var b in notice.GetComponentsInChildren<UnityEngine.UI.Button>())
    if ((cmd == "send" && b.name == "Send") || (cmd == "ok" && (b.name == "OK" || b.name == "Primary")) || (cmd == "later" && b.name == "Later") || (cmd == "close" && b.name == "Close")) { b.onClick.Invoke(); return "pressed " + b.name; }
return "no button for " + cmd;
