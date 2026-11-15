var b = Protobot.Interface.PartsBrowser.Instance;
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var t = typeof(Protobot.Interface.PartsBrowser);
t.GetMethod("SetCategory", flags).Invoke(b, new object[] { 1 });
Protobot.Interface.CatalogNode fam = null;
foreach (var n in Protobot.Interface.PartsCatalog.Nodes) if (n.section == "Hardware" && n.IsGroup && n.name.Contains("Spacer")) { fam = n; break; }
t.GetMethod("OpenFamily", flags).Invoke(b, new object[] { fam });
return "opened " + (fam != null ? fam.name : "none");
