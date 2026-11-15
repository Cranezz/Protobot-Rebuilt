// Robot colliders touching the ground or fixed field colliders (layers 17, 19), with how deep.
var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder();
var others = new System.Collections.Generic.List<Collider>();
foreach (var c in UnityEngine.Object.FindObjectsByType<Collider>(UnityEngine.FindObjectsSortMode.None))
    if (c.enabled && c.gameObject.layer != 16 && c.gameObject.layer != 11 && !c.isTrigger) others.Add(c);
foreach (var c in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Collider>()) {
    if (c.gameObject.layer != Protobot.Motion.Sandbox.Layer) continue;
    foreach (var o in others) {
        if (!c.bounds.Intersects(o.bounds)) continue;
        if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, o, o.transform.position, o.transform.rotation, out var dir, out var dist))
            sb.AppendLine(c.transform.parent.name + "/" + c.name + " (body " + (c.attachedRigidbody != null ? c.attachedRigidbody.mass.ToString("F2") : "-") + ", size " + c.bounds.size.ToString("F1") + ", at " + g.field.ToField(c.bounds.center).ToString("F1") + " h " + g.field.HeightOf(c.bounds.min).ToString("F2") + ") into " + o.name + " L" + o.gameObject.layer + " by " + dist.ToString("F2"));
    }
}
return sb.Length > 0 ? sb.ToString() : "nothing";
