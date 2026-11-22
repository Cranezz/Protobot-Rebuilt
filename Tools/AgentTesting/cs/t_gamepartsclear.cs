var g = Protobot.Game.SkillsGame.Current; if (g != null) g.Exit();
var n = 0;
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
    if (s.name.StartsWith("GameTest")) { UnityEngine.Object.Destroy(s.gameObject); n++; }
return "removed " + n;
