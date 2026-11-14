var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
var want = System.IO.File.ReadAllText("Temp/browse.txt").Trim();
foreach (var f in UnityEngine.Object.FindObjectsByType<TMPro.TMP_InputField>(FindObjectsSortMode.None)) {
  if (f.transform.root.name.Contains("V2 Interface") || f.GetComponentInParent<Canvas>() != null) {
    var p = f.placeholder as TMPro.TMP_Text;
    if (p != null && p.text.StartsWith("Search every part")) { f.text = want; f.onValueChanged.Invoke(want); return "searched " + want; }
  }
}
return "no search box";
