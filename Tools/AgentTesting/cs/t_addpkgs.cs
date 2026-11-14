// Adds the multiplayer packages (latest versions this editor offers).
var req = UnityEditor.PackageManager.Client.AddAndRemove(new[] { "com.unity.netcode.gameobjects", "com.unity.services.multiplayer" }, null);
var t = System.DateTime.Now;
while (!req.IsCompleted && (System.DateTime.Now - t).TotalSeconds < 100) System.Threading.Thread.Sleep(200);
if (!req.IsCompleted) return "still running";
if (req.Status == UnityEditor.PackageManager.StatusCode.Failure) return "failed: " + req.Error.message;
var sb = new System.Text.StringBuilder();
foreach (var p in req.Result) if (p.name.Contains("netcode") || p.name.Contains("services") || p.name.Contains("transport")) sb.Append(p.name + " " + p.version + "\n");
return sb.ToString();
