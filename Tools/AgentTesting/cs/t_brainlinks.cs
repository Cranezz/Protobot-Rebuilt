var g = Protobot.Game.SkillsGame.Current; if (g != null) g.Exit();
var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(s => s.gameObject.activeInHierarchy).ToList();
var brain = parts.First(Protobot.Game.SkillsGame.IsBrain);
System.Collections.Generic.List<GameObject> grp;
var has = Protobot.GroupManager.TryGetGroup(brain.gameObject, out grp);
var nb = Protobot.GroupManager.Neighbours(brain.gameObject).Count();
var dets = parts.Sum(p => p.GetComponentsInChildren<Protobot.HoleDetector>(true).Sum(d => d.holes.Count));
return "brain " + brain.id + " group " + (has ? grp.Count : 0) + " neighbours " + nb + " detector holes " + dets + " parts " + parts.Count;
