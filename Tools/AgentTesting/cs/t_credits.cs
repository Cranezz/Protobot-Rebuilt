var c = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x.name == "Credits List" && x.gameObject.scene.IsValid());
var t = c.GetComponent<TMPro.TMP_Text>(); var r = (RectTransform)c;
System.IO.File.WriteAllText("Temp/credits.txt", t.text + "\n--- fs " + t.fontSize + " auto " + t.enableAutoSizing + " pos " + r.anchoredPosition + " size " + r.sizeDelta + " anchors " + r.anchorMin + r.anchorMax + " overflow " + t.overflowMode);
return "ok";
