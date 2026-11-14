// Runs of open space along a line on a part face: rays along `axis` at points stepping along `along`.
var sb = new System.Text.StringBuilder();
var specs = new object[][] {
  new object[] { "VXSLOTTEDANGLE-Slotted Angle", new Vector3(-0.5f, 0f, -0.369f), Vector3.right, Vector3.up, 15f },
  new object[] { "VXSLOTTEDANGLE-Slotted Angle", new Vector3(-0.5f, 0f, -0.25f), Vector3.right, Vector3.up, 15f },
  new object[] { "VXCHASSISRAILS-2x1 Steel Chassis Rail (16)", new Vector3(-6f, 2.345f, -0.5f), Vector3.forward, Vector3.right, 9f },
  new object[] { "VXCHASSISRAILS-2x1 Steel Chassis Rail (16)", new Vector3(-6f, 2.824f, 0.253f), Vector3.down, Vector3.right, 9f },
  new object[] { "VXCHASSISRAILS-2x1 Steel Chassis Rail (16)", new Vector3(-6f, 3.5f, 0.0f), Vector3.down, Vector3.right, 9f },
  new object[] { "VXCHASSISRAILS-2x1 Steel Chassis Rail (16)", new Vector3(-6f, 3.5f, 0.4f), Vector3.down, Vector3.right, 9f } };
var probe = new GameObject("p"); var mc = probe.AddComponent<MeshCollider>();
foreach (var spec in specs) {
  var part = Protobot.PartsManager.GeneratePart((string)spec[0], Vector3.zero, Quaternion.identity);
  mc.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
  probe.transform.SetPositionAndRotation(part.transform.position, part.transform.rotation);
  var start = (Vector3)spec[1]; var axis = (Vector3)spec[2]; var along = (Vector3)spec[3]; var len = (float)spec[4];
  bool open = false; float runStart = 0; var runs = new System.Collections.Generic.List<string>();
  for (float d = 0; d <= len; d += 0.01f) {
    var o = start + along * d; RaycastHit h;
    var pass = !mc.Raycast(new Ray(o, axis), out h, 3.0f);
    if (pass && !open) { open = true; runStart = d; }
    if (!pass && open) { open = false; runs.Add((runStart).ToString("F2") + "-" + d.ToString("F2")); }
  }
  sb.Append(spec[0] + " line " + start + ": " + runs.Count + " openings: " + string.Join(" ", runs.GetRange(0, System.Math.Min(14, runs.Count))) + "\n");
  UnityEngine.Object.DestroyImmediate(part);
}
UnityEngine.Object.DestroyImmediate(probe);
return sb.ToString();
