// One-off (V2): puts the reports' webhook (from the old git-ignored Resources/Secrets/BugWebhook.txt, which was built
// into the app) on the server: Cloud Save private custom data "secrets"/"discordWebhook". Never prints it.
// Result in Temp/webhook_move.txt.
var outPath = System.IO.Path.GetFullPath("Temp/webhook_move.txt");
System.IO.File.WriteAllText(outPath, "working");
var file = "Assets/ProtobotRebuilt/Resources/Secrets/BugWebhook.txt";
if (!System.IO.File.Exists(file)) return "no file";
var url = System.IO.File.ReadAllText(file).Trim();
var type = System.Type.GetType("Protobot.EditorTools.LeaderboardModeration, Assembly-CSharp-Editor");
var task = (System.Threading.Tasks.Task<string>)type.GetMethod("SetWebhook").Invoke(null, new object[] { url });
task.ContinueWith(t => System.IO.File.WriteAllText(outPath, t.IsFaulted ? "error " + t.Exception.GetBaseException().Message : (t.Result ?? "saved")));
return "started";
