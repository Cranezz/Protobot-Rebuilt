// Active TMP texts on screen (with 1080p top-left positions), for demo scripts. PlayerPrefs "t_texts" = filter.
var f = UnityEngine.PlayerPrefs.GetString("t_texts", "");
var sb = new System.Text.StringBuilder(); float s = 1080f / Screen.height;
foreach (var t in UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None)) {
    if (!t.isActiveAndEnabled || string.IsNullOrWhiteSpace(t.text) || (f.Length > 0 && !t.text.Contains(f))) continue;
    var c = new Vector3[4]; t.rectTransform.GetWorldCorners(c); var m = (c[0] + c[2]) / 2f;
    var canvas = t.canvas; if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
    sb.AppendLine("\"" + t.text.Replace("\n", " ") + "\" @ " + (m.x * s).ToString("0") + "," + ((Screen.height - m.y) * s).ToString("0") + "  [" + new string(t.transform.parent.name.Where(ch => ch < 127).ToArray()) + "]");
}
return sb.ToString();
