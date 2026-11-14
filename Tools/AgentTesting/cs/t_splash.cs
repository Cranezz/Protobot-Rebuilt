var launch = UnityEngine.GameObject.Find("Launch Canvas");
var sb = new System.Text.StringBuilder();
var canvas = launch.GetComponent<UnityEngine.Canvas>();
sb.Append("canvas order " + canvas.sortingOrder + " scaler " + (launch.GetComponent<UnityEngine.UI.CanvasScaler>() != null) + "\n");
System.Action<UnityEngine.Transform, int> walk = null;
walk = (tr, d) => {
  if (d > 3) return;
  var r = (UnityEngine.RectTransform)tr;
  var img = tr.GetComponent<UnityEngine.UI.Image>();
  var raw = tr.GetComponent<UnityEngine.UI.RawImage>();
  var mask = tr.GetComponent<UnityEngine.UI.Mask>() != null || tr.GetComponent<UnityEngine.UI.RectMask2D>() != null;
  sb.Append(new string(' ', d * 2) + tr.name + (tr.gameObject.activeSelf ? "" : " (off)") + " size " + r.rect.size + " pos " + r.anchoredPosition + (img != null ? " img " + (img.sprite != null ? img.sprite.name : "-") + " " + img.color : "") + (raw != null ? " raw" : "") + (mask ? " mask" : "") + "\n");
  if (tr.name.StartsWith("IconTextButton") || tr.name.EndsWith("Button")) return;
  foreach (UnityEngine.Transform c in tr) walk(c, d + 1);
};
walk(launch.transform, 0);
return sb.ToString();
