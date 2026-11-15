var sb = new System.Text.StringBuilder();
var asm = typeof(Velopack.UpdateManager).Assembly;
foreach (var name in new[] { "Velopack.UpdateManager", "Velopack.UpdateInfo", "Velopack.VelopackAsset", "Velopack.VelopackAppBuilder" }) {
  var t = System.Array.Find(asm.GetTypes(), x => x.Name == name.Substring(name.LastIndexOf('.') + 1)); if (t == null) { sb.AppendLine(name + " missing"); continue; }
  sb.Append(name + ": ");
  foreach (var m in t.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
    if (m.MemberType == System.Reflection.MemberTypes.Method || m.MemberType == System.Reflection.MemberTypes.Property) sb.Append(m.Name + (m is System.Reflection.MethodInfo mi ? "(" + string.Join(",", System.Array.ConvertAll(mi.GetParameters(), p => p.ParameterType.Name + " " + p.Name)) + ")" : "") + "; ");
  sb.AppendLine();
}
return sb.ToString();
