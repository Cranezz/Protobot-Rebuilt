var sb = new System.Text.StringBuilder();
foreach (var shaft in UnityEngine.Object.FindObjectsByType<Protobot.Shaft>(FindObjectsSortMode.None)) {
    if (!shaft.gameObject.activeInHierarchy) continue;
    var g = Protobot.ConnectedObjExtensions.GetConnectedObjects(shaft.gameObject, true, true);
    var holes = shaft.holeDetector != null ? shaft.holeDetector.holes.Where(h => h != null && h.holeData != null).Select(h => h.holeData.part.GetComponent<Protobot.SavedObject>()?.id ?? h.holeData.part.name) : new string[0];
    sb.AppendLine(shaft.GetComponent<Protobot.SavedObject>().id + " at " + shaft.transform.position + " group " + g.Count + ": " + string.Join(",", g.Select(o => o.GetComponent<Protobot.SavedObject>()?.id ?? o.name)) + " | holes in: " + string.Join(",", holes));
    break;
}
return sb.ToString();
