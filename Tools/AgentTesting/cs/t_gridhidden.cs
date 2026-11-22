// (game open) Renderers on the grid layer (9) that still draw, outside the game's own objects.
var g = Protobot.Game.SkillsGame.Current; if (g == null) return "no game";
var shown = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.gameObject.layer == 9 && r.enabled && r.gameObject.activeInHierarchy && !r.transform.IsChildOf(g.transform) && !r.transform.IsChildOf(g.field.transform)).Select(r => r.name + " <" + (r.transform.parent != null ? r.transform.parent.name : "") + ">").ToList();
var cam = Camera.main;
return "grid renderers still drawn: " + shown.Count + " " + string.Join(", ", shown.Take(10)) + " | camera draws layer 9: " + ((cam.cullingMask & (1 << 9)) != 0);
