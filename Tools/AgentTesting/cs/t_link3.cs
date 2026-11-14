GameObject link = null;
foreach (var o in Protobot.PartsManager.FindLoadedObjects()) if (o.GetComponent<Protobot.Motion.RubberLinkPart>() != null) link = o;
var screw = link.GetComponent<Protobot.Screw>();
return "after frames: detector holes " + screw.holeDetector.holes.Count + " neighbours " + string.Join(",", System.Linq.Enumerable.Select(Protobot.GroupManager.Neighbours(link), n => n.name));
