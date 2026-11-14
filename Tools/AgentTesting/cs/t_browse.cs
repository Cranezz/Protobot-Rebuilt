var sb = new System.Text.StringBuilder();
var want = System.IO.File.Exists("Temp/browse.txt") ? System.IO.File.ReadAllText("Temp/browse.txt").Trim() : "Motion";
foreach (var b in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)) {
  var t = b.GetComponentInChildren<TMPro.TMP_Text>();
  if (t != null && t.text == want) { b.onClick.Invoke(); sb.Append("clicked " + want + "\n"); break; }
}
return sb.ToString();
