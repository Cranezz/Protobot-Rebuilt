// (Play) The axle AxlePlaceDisplacement measures for the omni wheel templates, and their middle holes.
var sb = new System.Text.StringBuilder();
foreach (var t in Protobot.PartsManager.partTypes) {
    if (t == null || !t.id.StartsWith("OMNI")) continue;
    var id = t.id;
    foreach (var pd in t.GetComponentsInChildren<Protobot.PartData>(true)) {
        var go = pd.gameObject;
        var ok = Protobot.AxlePlaceDisplacement.AxleOf(go, true, out var ax);
        var mf = go.GetComponent<MeshFilter>();
        sb.AppendLine(id + " child " + go.name + " p1 " + pd.param1Value + " p2 " + pd.param2Value + " ok " + ok + " axis " + ax.axis + " center " + ax.center + " bounds " + (mf && mf.sharedMesh ? mf.sharedMesh.bounds.size.ToString() : "-") + " rot " + go.transform.localEulerAngles);
        foreach (var h in go.GetComponentsInChildren<Protobot.HoleCollider>(true))
            sb.AppendLine("   hole " + h.name + " pos " + h.transform.localPosition + " fwd " + (h.transform.localRotation * Vector3.forward) + " parent " + (h.transform.parent == go.transform));
    }
}
return sb.ToString();
