var g = Protobot.Game.SkillsGame.Current;
var list = new System.Collections.Generic.List<(float, string)>();
foreach (var c in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Collider>()) {
    if (c.gameObject.layer != Protobot.Motion.Sandbox.Layer) continue;
    var b = c.bounds;
    var rb = c.attachedRigidbody;
    list.Add((g.field.HeightOf(b.min), c.transform.parent.name + "/" + c.name + " z " + g.field.ToField(b.center).ToString("F1") + " body " + (rb != null ? rb.mass.ToString("F2") : "-") + " size " + b.size.ToString("F1")));
}
list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
var sb = new System.Text.StringBuilder();
for (var i = 0; i < 12 && i < list.Count; i++) sb.AppendLine(list[i].Item1.ToString("F2") + " " + list[i].Item2);
return sb.ToString();
