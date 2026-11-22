// Every full-screen background picture (RawImage with BackgroundDrift): its rect size, scale, and on-screen corners.
var sb = new System.Text.StringBuilder("t " + Time.unscaledTime.ToString("F2") + " screen " + Screen.width + "x" + Screen.height + "\n");
foreach (var d in Resources.FindObjectsOfTypeAll<Protobot.Online.BackgroundDrift>()) {
    if (!d.gameObject.scene.IsValid()) continue;
    var r = (RectTransform)d.transform; var c = new Vector3[4]; r.GetWorldCorners(c);
    sb.AppendLine(d.transform.parent.parent.name + "/" + d.transform.parent.name + " active " + d.gameObject.activeInHierarchy + " size " + r.rect.size.ToString("F1") + " scale " + r.localScale.x.ToString("F4") + " corners " + c[0].ToString("F0") + c[2].ToString("F0") + " canvasScale " + r.GetComponentInParent<Canvas>().rootCanvas.scaleFactor);
}
return sb.ToString();
