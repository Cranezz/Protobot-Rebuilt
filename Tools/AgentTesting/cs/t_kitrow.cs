// Play mode: every leaf of the catalog group named PlayerPrefs "t_kit" ("Advanced Gear Kit") placed in a row along +X
// at z = 0 (2.5" apart), with what each one's renderer draws with -> the result; then a picture "kit".
var sb = new System.Text.StringBuilder();
var want = UnityEngine.PlayerPrefs.GetString("t_kit", "Advanced Gear Kit");
Protobot.Interface.CatalogNode group = null;
System.Action<Protobot.Interface.CatalogNode> find = null;
find = n => { if (group == null && n.name == want && n.IsGroup) group = n; if (n.IsGroup) foreach (var c in n.children) find(c); };
foreach (var root in Protobot.Interface.PartsCatalog.Nodes) find(root);
if (group == null) return "no group " + want;
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) if (s.transform.position.z > 4f && s.transform.position.z < 6f) UnityEngine.Object.Destroy(s.gameObject);
var x = 0f;
foreach (var leaf in group.Leaves()) {
  if (leaf.typeId == null) continue;
  var type = Protobot.Interface.PartsCatalog.Type(leaf.typeId);
  var gen = type != null ? type.GetComponent<Protobot.PartGenerator>() : null;
  if (gen == null) { sb.AppendLine(leaf.name + ": no generator"); continue; }
  if (leaf.p1 != null) gen.param1.value = leaf.p1;
  if (leaf.p2 != null) gen.param2.value = leaf.p2;
  else if (gen.UsesTwoParams) { var o2 = gen.GetParam2Options(); if (o2.Count > 0 && !o2.Contains(gen.param2.value)) gen.param2.value = o2[0]; }
  sb.Append("(" + leaf.p1 + "/" + gen.param2.value + ") ");
  var part = gen.Generate(new UnityEngine.Vector3(x, 1.5f, 5f), UnityEngine.Quaternion.identity);
  x += 2.5f;
  var mats = new System.Text.StringBuilder();
  foreach (var r in part.GetComponentsInChildren<UnityEngine.MeshRenderer>()) foreach (var m in r.sharedMaterials) mats.Append((m != null ? m.name + "(" + (m.HasProperty("_BaseColor") ? ((UnityEngine.Color)m.GetColor("_BaseColor")).ToString("0.00") : "?") + " tex " + (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) + ")" : "null") + " ");
  var so = part.GetComponent<Protobot.SavedObject>();
  sb.AppendLine(leaf.name + " [" + (so != null ? so.id : "?") + "] holes " + part.GetComponentsInChildren<Protobot.HoleCollider>().Length + " : " + mats);
}
UnityEngine.PlayerPrefs.SetFloat("t_kit_x", x);
return sb.ToString();
