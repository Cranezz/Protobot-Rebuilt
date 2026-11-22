// (game open) Drawn things outside the game and the field that are very wide (grid lines, axes).
var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder();
foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) {
    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
    if (g != null && (r.transform.IsChildOf(g.transform) || r.transform.IsChildOf(g.field.transform))) continue;
    if (r.bounds.size.magnitude < 150f) continue;
    sb.AppendLine(r.GetType().Name + " " + r.name + " <" + (r.transform.parent ? r.transform.parent.name : "") + "> layer " + r.gameObject.layer + " size " + r.bounds.size.ToString("F0") + " mat " + (r.sharedMaterial ? r.sharedMaterial.name : ""));
}
var cams = Camera.allCameras.Select(c => c.name + " mask " + c.cullingMask).ToArray();
return sb + " cams: " + string.Join(", ", cams);
