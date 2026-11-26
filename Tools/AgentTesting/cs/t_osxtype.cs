var found = new System.Text.StringBuilder();
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) {
  if (!asm.GetName().Name.Contains("OSX")) continue;
  found.Append(asm.GetName().Name + ": ");
  try { foreach (var t in asm.GetTypes()) if (t.Name.Contains("UserBuildSettings")) { found.Append(t.FullName + " "); foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)) found.Append("[" + p.Name + ":" + p.PropertyType.Name + "] "); } } catch (System.Exception e) { found.Append(e.Message); }
  found.Append("\n");
}
return found.Length == 0 ? "no OSX assemblies loaded (restart Unity?)" : found.ToString();
