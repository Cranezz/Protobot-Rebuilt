// Every leaf of one section (section name in survey_section.txt): its picture into a contact sheet, and a check of the part.
var section = System.IO.File.ReadAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/survey_section.txt").Trim();
var leaves = new System.Collections.Generic.List<Protobot.Interface.CatalogNode>();
foreach (var n in Protobot.Interface.PartsCatalog.Nodes) if (n.section == section) foreach (var l in n.Leaves()) if (l.typeId != null && l.action == null) leaves.Add(l);
int cols = 12, cell = 128, rows = (leaves.Count + cols - 1) / cols;
var sheet = new Texture2D(cols * cell, Mathf.Max(1, rows) * cell, TextureFormat.RGBA32, false);
var fill = new Color32[sheet.width * sheet.height]; for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(40, 42, 48, 255); sheet.SetPixels32(fill);
var sb = new System.Text.StringBuilder();
for (int i = 0; i < leaves.Count; i++) {
  var l = leaves[i]; Texture2D tex = null; string note = "";
  try {
    tex = Protobot.Interface.PartsCatalog.IsVex(l.typeId) ? Protobot.Custom.Thumbnails.ForVexPart(l.typeId, l.p1) : Protobot.Custom.Thumbnails.ForPart(l.typeId, l.p1, l.p2);
    if (Protobot.Interface.PartsCatalog.IsVex(l.typeId)) {
      var part = Protobot.PartsManager.GeneratePart(l.typeId + "-" + l.p1, new Vector3(0f, -7000f, 0f), Quaternion.identity);
      var holes = part.GetComponentsInChildren<Protobot.HoleCollider>(true).Length;
      var mf = part.GetComponent<MeshFilter>(); var b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size : Vector3.zero;
      note = "holes " + holes + " size " + b.ToString("F2") + (mf != null && mf.sharedMesh != null && !mf.sharedMesh.isReadable ? " NOT READABLE" : "");
      UnityEngine.Object.DestroyImmediate(part);
    }
  } catch (System.Exception e) { note = "ERROR " + e.GetType().Name + ": " + e.Message; }
  if (tex == null && note == "") note = "no picture";
  if (tex != null) {
    var src = tex.GetPixels32(); int w = tex.width, h = tex.height; int x0 = (i % cols) * cell, y0 = (rows - 1 - i / cols) * cell;
    for (int y = 0; y < cell; y++) for (int x = 0; x < cell; x++) { var c = src[(y * h / cell) * w + (x * w / cell)]; if (c.a > 10) sheet.SetPixel(x0 + x, y0 + y, Color32.Lerp(new Color32(40, 42, 48, 255), new Color32(c.r, c.g, c.b, 255), c.a / 255f)); }
  }
  sb.Append(i + "\t" + l.name + "\t" + l.typeId + "-" + l.p1 + (l.p2 != null ? "-" + l.p2 : "") + "\t" + note + "\n");
}
sheet.Apply();
System.IO.File.WriteAllBytes(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/Screenshots/sheet_" + section + ".png", sheet.EncodeToPNG());
System.IO.File.WriteAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/survey_" + section + ".txt", sb.ToString());
return section + ": " + leaves.Count + " parts";
