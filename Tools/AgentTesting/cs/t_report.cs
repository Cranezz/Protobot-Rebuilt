var zip = Protobot.Builds.BugReport.Make("Test report from the agent (ignore)", true, false);
if (zip == null) return "no zip";
var sb = new System.Text.StringBuilder(zip + " " + new System.IO.FileInfo(zip).Length / 1024 + " KB\n");
using (var z = new System.IO.Compression.ZipArchive(System.IO.File.OpenRead(zip))) foreach (var e in z.Entries) sb.Append("  " + e.FullName + " " + e.Length / 1024 + " KB\n");
var log = Protobot.Builds.ActionLog.CurrentFile;
sb.Append("log " + log + "\n");
var lines = System.IO.File.ReadAllLines(log);
for (int i = 0; i < lines.Length && i < 40; i++) sb.Append(lines[i].Length > 200 ? lines[i].Substring(0, 200) + "…\n" : lines[i] + "\n");

return sb.ToString();
