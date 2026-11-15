var sb = new System.Text.StringBuilder();
foreach (var m in UnityEditor.Menu.GetMenuItems("Services", true, false)) sb.AppendLine(m.path);
foreach (var m in UnityEditor.Menu.GetMenuItems("Window", true, false)) if (m.path.Contains("Deploy")) sb.AppendLine(m.path);
return sb.ToString();
