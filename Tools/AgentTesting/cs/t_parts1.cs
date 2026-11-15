// Play mode: Hardware, then opens the spacers family and its first kind in place (nested rows), for a screenshot.
var b = Protobot.Interface.PartsBrowser.Instance;
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var t = typeof(Protobot.Interface.PartsBrowser);
t.GetMethod("SetCategory", flags).Invoke(b, new object[] { 1 });
Protobot.Interface.CatalogNode fam = null;
foreach (var n in Protobot.Interface.PartsCatalog.Nodes) if (n.section == "Hardware" && n.IsGroup && n.name.Contains("Spacer")) { fam = n; break; }
var folded = (System.Collections.Generic.HashSet<string>)t.GetField("folded", flags).GetValue(b);
var key = fam.section + "/" + fam.name;
folded.Clear();
folded.Add(key);
var kinds = new System.Text.StringBuilder();
foreach (var c in fam.children) if (c.IsGroup) { kinds.Append(c.name + " (" + c.Count + ", deeper " + c.children.FindAll(x => x.IsGroup).Count + "); "); }
foreach (var c in fam.children) if (c.IsGroup) { folded.Add(key + "/" + c.name); break; }
t.GetMethod("Refresh", flags).Invoke(b, null);
var heads = (System.Collections.Generic.Dictionary<Protobot.Interface.CatalogNode, UnityEngine.RectTransform>)t.GetField("headings", flags).GetValue(b);
var content = (UnityEngine.RectTransform)t.GetField("content", flags).GetValue(b);
UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
content.anchoredPosition = new UnityEngine.Vector2(0f, -heads[fam].anchoredPosition.y - 30f);
return "opened " + fam.name + ": " + kinds;
