var launch = UnityEngine.GameObject.Find("Launch Canvas");
var open = System.Linq.Enumerable.First(launch.GetComponentsInChildren<UnityEngine.UI.Button>(true), b => b.name == "Open Button");
var welcome = open.transform.parent.parent;
var sb = new System.Text.StringBuilder(welcome.name + " < " + welcome.parent.name + "\n");
foreach (UnityEngine.Transform c in welcome.parent) { var r = (UnityEngine.RectTransform)c; sb.Append(c.name + " pos " + r.anchoredPosition + " size " + r.sizeDelta + " amin " + r.anchorMin + " amax " + r.anchorMax + " pivot " + r.pivot + "\n"); }
foreach (UnityEngine.Transform c in welcome) { var r = (UnityEngine.RectTransform)c; sb.Append("  " + c.name + " pos " + r.anchoredPosition + " size " + r.sizeDelta + " amin " + r.anchorMin + " amax " + r.anchorMax + "\n"); }
return sb.ToString();
