var m = typeof(Protobot.Online.PlayerReports).GetMethod("Make", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var bytes = (byte[])m.Invoke(null, new object[] { "mOp470test", "Player 5441", Protobot.Online.PlayerReports.Reasons[0], "Test report (not sent): checking what goes in it." });
var path = "C:/Users/kille/AppData/Local/Temp/claude/5b0c4ec0a185a9e3/4fe6ed7d-e179-4b4f-b31d-ea600198646e/scratchpad/report_test.zip";
System.IO.File.WriteAllBytes(path, bytes);
return "zip " + bytes.Length + " bytes";
