var launch = UnityEngine.GameObject.Find("Launch Canvas");
var open = System.Linq.Enumerable.First(launch.GetComponentsInChildren<UnityEngine.UI.Button>(true), b => b.name == "Open Button");
var parent = open.transform.parent;
var sb = new System.Text.StringBuilder(parent.name + " < " + parent.parent.name + "\n");
foreach (UnityEngine.Transform c in parent) { var r = (UnityEngine.RectTransform)c; sb.Append(c.name + " pos " + r.anchoredPosition + " size " + r.sizeDelta + " anchor " + r.anchorMin + "\n"); }
return sb.ToString();
