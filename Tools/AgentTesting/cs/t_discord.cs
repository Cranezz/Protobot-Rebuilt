// Sends one test report (marked as a test) through the run-report path to the Discord webhook. Result in the log.
var webhook = Protobot.Builds.BugReport.Webhook;
if (string.IsNullOrEmpty(webhook)) return "no webhook file";
var post = typeof(Protobot.Online.PlayerReports).GetMethod("Post", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
post.Invoke(null, new object[] { "**Test report** (automated check of report delivery: please ignore)", "Test report", "# Test report\n\nAutomated check that run reports reach this channel. Nothing to do.", null, "test report sent" });
return "sending";
