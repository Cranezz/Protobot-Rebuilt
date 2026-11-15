var t = typeof(Velopack.VelopackApp); var sb = new System.Text.StringBuilder(); foreach (var m in t.GetMethods()) if (m.DeclaringType == t) sb.Append(m.Name + " "); return sb.ToString();
