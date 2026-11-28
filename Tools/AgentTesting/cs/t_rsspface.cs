// (Play) RoboSource spacers: for every kind, the share of triangles facing the way their normals point (1 = right),
// then three spacers on the grid and a picture of them (Temp: shot "rssp_faces").
var sb = new System.Text.StringBuilder();
foreach (var key in new[] { "W64", "W32", "W16", "B8", "B4", "B38", "B2", "B78", "B1", "F" }) {
    var go = Protobot.PartsManager.GeneratePart("RSSP-" + key, new Vector3(0, -50, 0), Quaternion.identity);
    if (go == null) { sb.AppendLine(key + ": no part"); continue; }
    var mesh = go.GetComponent<MeshFilter>().sharedMesh;
    var v = mesh.vertices; var n = mesh.normals; var t = mesh.triangles; int good = 0, all = 0;
    for (var i = 0; i < t.Length; i += 3) {
        var face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
        all++; if (Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) > 0f) good++;
    }
    sb.AppendLine(key + ": " + good + " of " + all + " triangles face out");
    UnityEngine.Object.Destroy(go);
}
var keys = new[] { "B1", "F", "W16" };
for (var k = 0; k < keys.Length; k++) Protobot.PartsManager.GeneratePart("RSSP-" + keys[k], new Vector3(-2.5f + 2.5f * k, 1f, 0f), Quaternion.Euler(-60f, 20f, 0f));
Protobot.PivotCamera.Main.SetTransform(new Vector3(0f, 1f, 0f), new Vector3(25f, 0f, 0f), -9f);
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
pt.GetMethod("Screenshot").Invoke(null, new object[] { "rssp_faces" });
pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
return sb.ToString();
