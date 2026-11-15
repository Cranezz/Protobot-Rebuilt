// Exits the game, clears the build, makes an 11W motor and an omni wheel (catalog ids) and cuts each through its middle.
if (Protobot.Game.SkillsGame.Current != null) Protobot.Game.SkillsGame.Current.Exit();
Protobot.PartsManager.DestroyLoadedObjects();
var sb = new System.Text.StringBuilder();
var ids = new System.Collections.Generic.List<string>();
foreach (var n in Protobot.Interface.PartsCatalog.Nodes)
    foreach (var l in n.Leaves()) {
        if (l.typeId == null) continue;
        var id = l.typeId + (l.p1 != null ? "-" + l.p1 : "") + (l.p2 != null ? "-" + l.p2 : "");
        if (ids.Count == 0 && l.typeId.StartsWith("MOTR")) ids.Add(id);
        else if (ids.Count == 1 && l.name.Contains("Omni")) ids.Add(id);
    }
var x = 0f;
foreach (var id in ids) {
    var go = Protobot.PartsManager.GeneratePart(id, new Vector3(x, 3f, 0f), Quaternion.identity);
    x += 6f;
    var f = go.GetComponent<MeshFilter>();
    var before = f.sharedMesh.triangles.Length / 3;
    var ok = Protobot.Tools.PartCutting.Cut(go, f.sharedMesh.bounds.center, Vector3.right);
    sb.Append(id + ": cut " + ok + ", triangles " + before + " -> " + f.sharedMesh.triangles.Length / 3 + "; ");
}
Protobot.PivotCamera.Main.SetTransform(new Vector3(3f, 3f, 0f), new Vector3(20f, 270f, 0f), -16f);
return sb.ToString();
