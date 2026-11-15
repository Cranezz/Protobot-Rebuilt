var sb = new System.Text.StringBuilder();
sb.Append("problem: " + Protobot.Online.Voice.Problem + " | ");
try {
  var v = Unity.Services.Vivox.VivoxService.Instance;
  sb.Append("logged in " + v.IsLoggedIn + " | ");
  foreach (var kv in v.ActiveChannels) { sb.Append(kv.Key + ": "); foreach (var p in kv.Value) sb.Append(p.DisplayName + (p.IsSelf ? "(me)" : "") + " "); sb.Append(" | "); }
} catch (System.Exception e) { sb.Append("error " + e.Message); }
return sb.ToString();
