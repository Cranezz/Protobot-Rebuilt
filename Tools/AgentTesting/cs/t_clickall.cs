// Clicks a button by its label text (Temp/label.txt) anywhere in the open interface.
var label = System.IO.File.ReadAllText("Temp/label.txt").Trim();
var b = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.gameObject.activeInHierarchy && x.GetComponentInChildren<TMPro.TMP_Text>() != null && x.GetComponentInChildren<TMPro.TMP_Text>().text.Trim() == label);
if (b == null) return "no " + label;
b.onClick.Invoke(); return "clicked " + label;
