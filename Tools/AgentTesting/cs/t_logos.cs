var c = UnityEngine.GameObject.Find("Launch Canvas");
var sb = new System.Text.StringBuilder();
foreach (var img in c.GetComponentsInChildren<UnityEngine.UI.Image>(true)) if (img.sprite != null && (img.sprite.name.Contains("Logo") || img.name.Contains("Logo"))) sb.AppendLine(img.name + " sprite " + img.sprite.name + " size " + ((UnityEngine.RectTransform)img.transform).rect.size);
return sb.ToString();
