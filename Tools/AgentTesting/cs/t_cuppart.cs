// (Play) Makes the Override Cup part (OVCP) and lists its objects: meshes, readable, layers. Then deletes it.
var go = Protobot.PartsManager.GeneratePart("OVCP", new Vector3(0, 30, 0), Quaternion.identity);
if (go == null) return "no OVCP part";
var sb = new System.Text.StringBuilder("part " + go.name + "\n");
foreach (var t in go.GetComponentsInChildren<Transform>(true)) {
    var mf = t.GetComponent<MeshFilter>(); var r = t.GetComponent<MeshRenderer>(); var c = t.GetComponent<Collider>();
    sb.AppendLine("  " + t.name + (t == go.transform ? " (root)" : "") + " layer " + t.gameObject.layer + (mf != null && mf.sharedMesh != null ? " mesh " + mf.sharedMesh.name + " v " + mf.sharedMesh.vertexCount + " readable " + mf.sharedMesh.isReadable + " size " + mf.sharedMesh.bounds.size.ToString("F2") : "") + (r != null ? " renderer" + (r.enabled ? "" : "(off)") : "") + (c != null ? " collider " + c.GetType().Name : ""));
}
UnityEngine.Object.Destroy(go);
return sb.ToString();
