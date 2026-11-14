var sb = new System.Text.StringBuilder();
var cols = new System.Collections.Generic.List<Collider>();
foreach (var c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)) if (c.attachedRigidbody != null || c.name == "Ground") cols.Add(c);
for (int i = 0; i < cols.Count; i++) for (int j = i + 1; j < cols.Count; j++) {
  var a = cols[i]; var b = cols[j];
  var ign = Physics.GetIgnoreCollision(a, b) || Physics.GetIgnoreLayerCollision(a.gameObject.layer, b.gameObject.layer);
  var pen = Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out var dir, out var dist);
  sb.Append((a.attachedRigidbody ? a.attachedRigidbody.name : a.name) + "/" + (b.attachedRigidbody ? b.attachedRigidbody.name : b.name) + " ignored " + ign + " pen " + pen + (pen ? " " + dir.ToString("F2") + " " + dist.ToString("F3") : "") + "\n");
}
return sb.ToString();
