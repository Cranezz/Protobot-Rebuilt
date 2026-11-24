var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) {
    if (!s.id.StartsWith("SHFT")) continue;
    var mf = s.GetComponent<MeshFilter>();
    sb.AppendLine(s.id + " scale " + s.transform.localScale + " mesh " + mf.sharedMesh.name + " bounds " + mf.sharedMesh.bounds + " verts " + mf.sharedMesh.vertexCount + " state " + s.state);
    foreach (var h in s.GetComponentsInChildren<Protobot.HoleCollider>(true)) sb.AppendLine("  hole " + h.name + " lpos " + h.transform.localPosition + " lscale " + h.transform.localScale + " type " + h.holeType);
}
return sb.ToString();
