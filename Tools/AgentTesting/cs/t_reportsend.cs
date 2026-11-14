// Makes a small report (no build, no picture) and sends it through BugReport.Upload, as Send does.
var hook = Protobot.Builds.BugReport.Webhook;
if (string.IsNullOrEmpty(hook)) return "no webhook";
var zip = Protobot.Builds.BugReport.Make("Test report from the agent: checking that reports reach this channel (ignore)", false, false);
if (zip == null) return "no zip";
var upload = typeof(Protobot.Builds.BugReport).GetMethod("Upload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var routine = (System.Collections.IEnumerator)upload.Invoke(null, new object[] { zip, "Test report from the agent: checking that reports reach this channel (ignore)" });
var guard = Protobot.Builds.CrashGuard.Instance;
if (guard == null) return "no CrashGuard (not playing?)";
guard.StartCoroutine(routine);
return "sending " + zip + " (" + new System.IO.FileInfo(zip).Length / 1024 + " KB) to webhook ending ..." + hook.Substring(hook.Length - 6);
